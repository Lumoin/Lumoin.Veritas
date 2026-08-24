using System;
using System.Threading;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// A forward cursor over one vertex's sorted neighbor run in a <see cref="SymmetricAdjacency"/>, presenting the
/// minimal surface the clique leapfrog needs: the current neighbor, a forward-only seek to the first neighbor at
/// least a target, and a single step. Keys are dense vertex indices, ascending.
/// </summary>
internal struct NeighborCursor
{
    /// <summary>The CSR neighbor column the run lives in.</summary>
    private readonly int[] column;

    /// <summary>The run's exclusive end index in <see cref="column"/>.</summary>
    private readonly int end;

    /// <summary>The cursor's current absolute index in <see cref="column"/>; at or past <see cref="end"/> means exhausted.</summary>
    private int position;

    /// <summary>Creates a cursor over <paramref name="column"/>'s run <c>[start, end)</c>.</summary>
    /// <param name="column">The CSR neighbor column.</param>
    /// <param name="start">The run's inclusive start index.</param>
    /// <param name="end">The run's exclusive end index.</param>
    public NeighborCursor(int[] column, int start, int end)
    {
        this.column = column;
        position = start;
        this.end = end;
    }

    /// <summary>Whether the cursor has passed the last neighbor of its run.</summary>
    public readonly bool AtEnd => position >= end;

    /// <summary>The current neighbor's dense index; valid only when not <see cref="AtEnd"/>.</summary>
    public readonly int Key => column[position];

    /// <summary>Steps past the current neighbor to the next.</summary>
    public void Next()
    {
        position++;
    }

    /// <summary>Advances to the first neighbor whose dense index is at least <paramref name="target"/>, never moving backwards.</summary>
    /// <param name="target">The dense index to seek to.</param>
    public void Seek(int target)
    {
        position = LowerBound(column, position, end, target);
    }

    /// <summary>The first index in <c>[lo, hi)</c> whose value is at least <paramref name="target"/>, or <paramref name="hi"/> when none is.</summary>
    /// <param name="column">The sorted column.</param>
    /// <param name="lo">The inclusive search start.</param>
    /// <param name="hi">The exclusive search end.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    private static int LowerBound(int[] column, int lo, int hi, int target)
    {
        while(lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);

            if(column[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}

/// <summary>
/// Enumerates the fixed-size cliques of a <see cref="SymmetricAdjacency"/> by a worst-case-optimal leapfrog
/// generic join, one clique per <see cref="MoveNext"/>. A clique of size k is built vertex by vertex in strictly
/// ascending dense-index order, so each clique is produced exactly once, in ascending lexicographic order: the
/// next vertex must be a neighbor of every already-chosen vertex and greater than the last, which is the multi-way
/// intersection the leapfrog computes over the chosen vertices' neighbor runs. The descent is an explicit stack —
/// no call recursion — and the intersection is the track-max variant of <see cref="ColumnarLeapfrogIntersection"/>.
/// </summary>
internal sealed class LeapfrogCliqueWalker
{
    /// <summary>One descent level: the neighbor cursors intersected to choose the level's vertex.</summary>
    private struct Level
    {
        /// <summary>The neighbor runs of the vertices chosen above this level, each positioned past the last chosen vertex; their common keys are this level's candidates. Mutable struct frames stepped by reference, hence an array element rather than a copy.</summary>
        public NeighborCursor[] Cursors;

        /// <summary>The clique position this level fills — the number of vertices chosen above it.</summary>
        public int Depth;

        /// <summary>Whether a candidate has already been chosen here, so the next visit advances the cursors before intersecting again.</summary>
        public bool Started;
    }

    /// <summary>The adjacency whose cliques are enumerated.</summary>
    private readonly SymmetricAdjacency adjacency;

    /// <summary>The clique size to enumerate; at least two.</summary>
    private readonly int cliqueSize;

    /// <summary>Cancellation token, honoured once per base vertex and once per leapfrog pass.</summary>
    private readonly CancellationToken cancellationToken;

    /// <summary>The current partial clique as dense indices; positions <c>[0, cliqueSize)</c> are valid when <see cref="MoveNext"/> last returned true. Its elements are assigned as levels are chosen, hence a field.</summary>
    private readonly int[] chosen;

    /// <summary>The descent stack of mutable struct frames, stepped by reference; indices <c>[0, stackDepth)</c> are live. A field for the same by-reference reason as <see cref="Level.Cursors"/>.</summary>
    private readonly Level[] stack;

    /// <summary>The number of live descent levels.</summary>
    private int stackDepth;

    /// <summary>The next base vertex (dense index) to start a clique from.</summary>
    private int baseIndex;

    /// <summary>Creates a walker over <paramref name="adjacency"/> for cliques of size <paramref name="cliqueSize"/>.</summary>
    /// <param name="adjacency">The dense symmetric adjacency to enumerate.</param>
    /// <param name="cliqueSize">The clique size; at least two.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the walk.</param>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <see langword="null"/>.</exception>
    public LeapfrogCliqueWalker(SymmetricAdjacency adjacency, int cliqueSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        this.adjacency = adjacency;
        this.cliqueSize = cliqueSize;
        this.cancellationToken = cancellationToken;
        chosen = new int[cliqueSize];

        //At most cliqueSize - 1 levels are ever live (the base vertex is not a level); cliqueSize is an ample cap.
        stack = new Level[cliqueSize];
        stackDepth = 0;
        baseIndex = 0;
    }

    /// <summary>The current clique as dense vertex indices, ascending; valid only after <see cref="MoveNext"/> returns true.</summary>
    public ReadOnlySpan<int> CurrentDense => chosen;

    /// <summary>
    /// Advances to the next clique, returning <see langword="true"/> when one was found and exposing it through
    /// <see cref="CurrentDense"/>, or <see langword="false"/> when the enumeration is exhausted.
    /// </summary>
    /// <returns><see langword="true"/> when a clique was found.</returns>
    /// <exception cref="OperationCanceledException">Cancelled via the walker's cancellation token.</exception>
    public bool MoveNext()
    {
        while(true)
        {
            if(stackDepth == 0)
            {
                if(!TryStartNextBase())
                {
                    return false;
                }

                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            ref Level top = ref stack[stackDepth - 1];

            if(top.Started)
            {
                AdvanceCursors(top.Cursors);
            }

            if(TryFindCommon(top.Cursors, cancellationToken, out int candidate))
            {
                top.Started = true;
                chosen[top.Depth] = candidate;

                if(top.Depth == cliqueSize - 1)
                {
                    return true;
                }

                PushLevel(top.Cursors, top.Depth + 1, candidate);

                continue;
            }

            stackDepth--;
        }
    }

    /// <summary>Advances to the next base vertex that could begin a clique and opens the first descent level on it.</summary>
    /// <returns><see langword="true"/> when a usable base vertex was found and a level pushed.</returns>
    /// <exception cref="OperationCanceledException">Cancelled via the walker's cancellation token.</exception>
    private bool TryStartNextBase()
    {
        while(baseIndex < adjacency.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int baseVertex = baseIndex;
            baseIndex++;

            //A vertex in a k-clique needs at least k - 1 neighbors; skipping the rest avoids a doomed descent.
            if(adjacency.NeighborCountOf(baseVertex) < cliqueSize - 1)
            {
                continue;
            }

            chosen[0] = baseVertex;
            PushLevel(parentCursors: null, depth: 1, pivot: baseVertex);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Pushes the descent level that chooses the clique's vertex at <paramref name="depth"/>: the inherited
    /// cursors of the vertices chosen above, each advanced past <paramref name="pivot"/>, plus the pivot's own
    /// neighbor run advanced past itself. Their common keys are the vertices adjacent to every chosen vertex and
    /// greater than the pivot — the next clique members. Inherited cursors are struct copies, so the parent level's
    /// own cursors are left untouched for its later candidates.
    /// </summary>
    /// <param name="parentCursors">The parent level's cursors, all resting on <paramref name="pivot"/>, or <see langword="null"/> for the first level over a base vertex.</param>
    /// <param name="depth">The clique position the pushed level fills.</param>
    /// <param name="pivot">The vertex just chosen; candidates must exceed it and be adjacent to it.</param>
    private void PushLevel(NeighborCursor[]? parentCursors, int depth, int pivot)
    {
        int inherited = parentCursors?.Length ?? 0;
        NeighborCursor[] cursors = new NeighborCursor[inherited + 1];

        for(int i = 0; i < inherited; i++)
        {
            cursors[i] = parentCursors![i];
            cursors[i].Seek(pivot + 1);
        }

        NeighborCursor pivotCursor = adjacency.CursorFor(pivot);
        pivotCursor.Seek(pivot + 1);
        cursors[inherited] = pivotCursor;

        stack[stackDepth] = new Level
        {
            Cursors = cursors,
            Depth = depth,
            Started = false,
        };
        stackDepth++;
    }

    /// <summary>Steps every cursor past its current key, so the next intersection resumes after the last common key.</summary>
    /// <param name="cursors">The level's cursors, all resting on the same key.</param>
    private static void AdvanceCursors(NeighborCursor[] cursors)
    {
        for(int i = 0; i < cursors.Length; i++)
        {
            cursors[i].Next();
        }
    }

    /// <summary>
    /// The track-max leapfrog intersection: raise a running target to the maximum current key, seek every lagging
    /// cursor up to it, and restart whenever a seek overshoots; a pass with no overshoot means all cursors agree.
    /// Mirrors <see cref="ColumnarLeapfrogIntersection.TryFindNextCommonKey"/> over dense-index cursors, honouring
    /// cancellation once per pass.
    /// </summary>
    /// <param name="cursors">The cursors to intersect, each positioned where the search should begin.</param>
    /// <param name="cancellationToken">Cancellation token, checked once per intersection pass.</param>
    /// <param name="common">The agreed dense index on success; <c>-1</c> on failure.</param>
    /// <returns><see langword="true"/> when every cursor agrees on a common key.</returns>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    private static bool TryFindCommon(NeighborCursor[] cursors, CancellationToken cancellationToken, out int common)
    {
        int target = int.MinValue;
        for(int i = 0; i < cursors.Length; i++)
        {
            if(cursors[i].AtEnd)
            {
                common = -1;

                return false;
            }

            if(cursors[i].Key > target)
            {
                target = cursors[i].Key;
            }
        }

        bool advanced;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            advanced = false;

            for(int i = 0; i < cursors.Length; i++)
            {
                if(cursors[i].Key == target)
                {
                    continue;
                }

                cursors[i].Seek(target);

                if(cursors[i].AtEnd)
                {
                    common = -1;

                    return false;
                }

                if(cursors[i].Key > target)
                {
                    target = cursors[i].Key;
                    advanced = true;

                    break;
                }
            }
        }
        while(advanced);

        common = target;

        return true;
    }
}
