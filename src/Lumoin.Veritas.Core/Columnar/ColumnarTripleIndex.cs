using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A columnar triple index: the triple set materialised in all six
/// position permutations, each as a three-level compressed-sparse
/// layout of contiguous sorted columns. One permutation answers
/// one descent shape — the bound positions form the permutation's
/// prefix and the variable positions follow — so a worst-case
/// optimal join always finds an order whose levels match its
/// variable elimination sequence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout per permutation.</b> Level 0 holds the distinct
/// first-position values in ascending order; a parallel offset
/// column marks where each value's group begins in level 1.
/// Level 1 holds, per level-0 group, the distinct second-position
/// values in ascending order, again with offsets into level 2.
/// Level 2 holds the third-position values, ascending within each
/// level-1 group. Every column is a contiguous <see cref="uint"/>
/// array, so a level's candidate set is one span and a seek is a
/// search over adjacent elements — no per-node objects and no
/// pointer chasing between levels, only offset arithmetic.
/// </para>
/// <para>
/// <b>Mutability model.</b> The base columns are immutable after
/// construction. Updates and deletes arrive as journal-shaped
/// deltas — sorted addition runs and a removal set — merged by the
/// cursors at read time; compaction folds an accumulated delta
/// into a fresh base. The base is the "main" half of that
/// delta-main pair.
/// </para>
/// <para>
/// <b>Build cost.</b> Construction sorts the triple set once per
/// permutation and emits each level in a single pass — no
/// per-triple intermediate structures.
/// </para>
/// <para>
/// <b>Why this type is partial.</b> The implementation is split
/// across concern-focused files — the build and query core here,
/// the on-disk codec in <c>.Serialization.cs</c>, the integrity
/// verify round in <c>.Verify.cs</c> — that share this type's
/// private state, so each concern reaches the internals directly
/// without widening the public surface. The split is
/// organisational, not a variation point: a partial holds the one
/// fixed implementation of a concern, whereas a concern that
/// genuinely varies is composed instead through an injected named
/// delegate (the hash a verify reaches for is a
/// <see cref="Lumoin.Veritas.Core.Integrity.ChecksumComputeDelegate"/>,
/// while the framing around it stays a partial method). A concern
/// graduates from a partial to an injected delegate the day it has
/// more than one implementation.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarTripleIndex Triples={TripleCount}")]
public sealed partial class ColumnarTripleIndex
{
    /// <summary>The six position permutations, indexed by <see cref="PermutationIndex"/>: each entry lists the RDF positions (0 = subject, 1 = predicate, 2 = object) in descent order.</summary>
    private static readonly byte[][] Permutations =
    [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0],
    ];

    //One accumulated-delta fraction of the base size triggers
    //compaction: when added + removed reach a quarter of the base
    //triple count, Apply folds the delta into a fresh base instead
    //of growing the merge cost.
    private const int CompactionDenominator = 4;

    private static readonly EncodedTriple[] EmptyTriples = [];

    //Materialised orders by permutation index; null = not built
    //under this index's order-set mode.
    private readonly ColumnarOrder?[] orders;

    //Per-order level-0 (start, end) bounds when this index is a
    //GRAPH VIEW over a shared graph-set's concatenated columns;
    //null = the whole columns (a standalone index). Offsets in the
    //shared columns are absolute, so the level-0 slice plus the
    //ordinary descent covers exactly the view's graph.
    private readonly (int Start, int End)[]? level0Bounds;

    //The accumulated delta over the immutable base: additions and
    //removals as canonical sets (for membership and Apply
    //normalisation) plus per-permutation sorted runs (for the merge
    //cursors). Invariants: added ∩ base = ∅, removed ⊆ base,
    //added ∩ removed = ∅.
    private readonly HashSet<EncodedTriple> addedSet;

    private readonly HashSet<EncodedTriple> removedSet;

    private readonly EncodedTriple[][] addedByOrder;

    private readonly EncodedTriple[][] removedByOrder;

    private readonly int baseTripleCount;

    /// <summary>The number of distinct triples the merged view contains — the base count net of the accumulated delta.</summary>
    public int TripleCount => baseTripleCount - removedSet.Count + addedSet.Count;

    /// <summary>Whether an accumulated delta sits over the base — the distinction between the batch scan's direct CSR walk and its merged fallback.</summary>
    public bool HasDelta => addedSet.Count != 0 || removedSet.Count != 0;

    /// <summary>Which permutation set this index materialises.</summary>
    public ColumnarOrderSetMode OrderSetMode { get; }

    /// <summary>Where this index's block-packed column payloads live; preserved across <see cref="Apply"/> so a native index stays native through compaction.</summary>
    internal ColumnPayloadBacking Backing { get; }

    private ColumnarTripleIndex(
        ColumnarOrder?[] orders,
        ColumnarOrderSetMode orderSetMode,
        int baseTripleCount,
        HashSet<EncodedTriple> addedSet,
        HashSet<EncodedTriple> removedSet,
        EncodedTriple[][] addedByOrder,
        EncodedTriple[][] removedByOrder,
        (int Start, int End)[]? level0Bounds = null,
        ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        this.orders = orders;
        OrderSetMode = orderSetMode;
        this.baseTripleCount = baseTripleCount;
        this.addedSet = addedSet;
        this.removedSet = removedSet;
        this.addedByOrder = addedByOrder;
        this.removedByOrder = removedByOrder;
        this.level0Bounds = level0Bounds;
        Backing = backing;
    }

    /// <summary>
    /// Constructs a graph view over a graph-set's shared orders:
    /// the given level-0 ranges slice each order to one graph, the
    /// delta starts empty, and the view is immutable —
    /// <see cref="Apply"/> on a view compacts into a standalone
    /// per-graph index rather than evolving the shared columns.
    /// Called by <see cref="ColumnarGraphSetIndex.GetView"/>;
    /// consumers do not call this directly.
    /// </summary>
    /// <param name="orders">The shared materialised orders.</param>
    /// <param name="orderSetMode">The shared order-set mode.</param>
    /// <param name="tripleCount">The graph's triple count.</param>
    /// <param name="level0Bounds">Per-order level-0 (start, end) ranges of the graph's run.</param>
    /// <param name="backing">Where the shared columns' block-packed payloads live; the view inherits the graph-set's backing so a compacting <see cref="Apply"/> preserves it.</param>
    /// <returns>The view.</returns>
    internal static ColumnarTripleIndex CreateView(
        ColumnarOrder?[] orders,
        ColumnarOrderSetMode orderSetMode,
        int tripleCount,
        (int Start, int End)[] level0Bounds,
        ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        EncodedTriple[][] emptyRuns = [EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples];

        return new ColumnarTripleIndex(orders, orderSetMode, tripleCount, [], [], emptyRuns, emptyRuns, level0Bounds, backing);
    }

    /// <summary>
    /// The level-0 slice this index descends from in the given
    /// order: the whole column for a standalone index, the graph's
    /// run for a view.
    /// </summary>
    /// <param name="permutationIndex">A materialised permutation index.</param>
    /// <returns>The level-0 (start, end) range.</returns>
    internal (int Start, int End) Level0BoundsAt(int permutationIndex)
    {
        return level0Bounds?[permutationIndex] ?? (0, OrderAt(permutationIndex).ValuesLengthAt(0));
    }

    /// <summary>
    /// Builds the index from the given triples with an empty delta.
    /// Duplicates are absorbed; the realised
    /// <see cref="TripleCount"/> is the distinct count.
    /// </summary>
    /// <param name="triples">The triples to index.</param>
    /// <param name="orderSetMode">Which permutation set to materialise; see <see cref="ColumnarOrderSetMode"/> for the trade.</param>
    /// <param name="valueEncoding">How value columns are encoded; default Elias-Fano where a column qualifies (each column keeps the smaller of the candidate and its frame-of-reference packing, so the default never enlarges a column), or frame of reference throughout. See <see cref="ColumnarValueColumnEncoding"/>.</param>
    /// <param name="backing">Where block-packed column payloads live; default managed. See <see cref="ColumnPayloadBacking"/>.</param>
    /// <returns>The constructed index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    public static ColumnarTripleIndex Build(IEnumerable<EncodedTriple> triples, ColumnarOrderSetMode orderSetMode = ColumnarOrderSetMode.AllSixOrders, ColumnarValueColumnEncoding valueEncoding = ColumnarValueColumnEncoding.EliasFanoWhenMonotone, ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        ArgumentNullException.ThrowIfNull(triples);

        HashSet<EncodedTriple> distinct = [.. triples];
        EncodedTriple[] working = [.. distinct];
        ColumnarOrder?[] orders = new ColumnarOrder?[Permutations.Length];

        for(int i = 0; i < Permutations.Length; i++)
        {
            if(IsPermutationInMode(i, orderSetMode))
            {
                orders[i] = ColumnarOrder.Build(working, Permutations[i], valueEncoding, backing);
            }
        }

        EncodedTriple[][] emptyRuns = [EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples];

        return new ColumnarTripleIndex(orders, orderSetMode, working.Length, [], [], emptyRuns, emptyRuns, backing: backing);
    }

    /// <summary>Whether the permutation at <paramref name="permutationIndex"/> belongs to <paramref name="mode"/>'s set: the rotations are SPO, POS, and OSP.</summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <param name="mode">The order-set mode.</param>
    /// <returns><c>true</c> when the permutation is materialised under the mode.</returns>
    internal static bool IsPermutationInMode(int permutationIndex, ColumnarOrderSetMode mode)
    {
        if(mode == ColumnarOrderSetMode.AllSixOrders)
        {
            return true;
        }

        //The cyclic rotations of (S, P, O): SPO = [0,1,2], POS =
        //[1,2,0], OSP = [2,0,1] — permutation indices 0, 3, and 4.
        return permutationIndex is 0 or 3 or 4;
    }

    /// <summary>Whether the permutation at <paramref name="permutationIndex"/> is materialised in this index.</summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns><c>true</c> when <see cref="OrderAt"/> can serve it.</returns>
    public bool IsPermutationAvailable(int permutationIndex)
    {
        return orders[permutationIndex] is not null;
    }

    /// <summary>
    /// Selects a materialised permutation whose prefix is exactly
    /// the bound positions (in the permutation's own order) and
    /// whose tail is exactly the variable positions in the given
    /// sequence. Under <see cref="ColumnarOrderSetMode.AllSixOrders"/>
    /// this always succeeds; under three rotations it succeeds
    /// exactly when the requested variable sequence is
    /// rotation-compatible (<see cref="ColumnarRotationPlanner"/>
    /// chooses global orders that make it so).
    /// </summary>
    /// <param name="boundPositions">The pattern's bound RDF positions, as a set in any order.</param>
    /// <param name="variablePositionsInOrder">The pattern's variable RDF positions, in the required descent sequence.</param>
    /// <param name="permutationIndex">Receives the selected permutation index.</param>
    /// <returns><c>true</c> when a materialised permutation serves the shape.</returns>
    public bool TrySelectPermutation(ReadOnlySpan<byte> boundPositions, ReadOnlySpan<byte> variablePositionsInOrder, out int permutationIndex)
    {
        int boundCount = boundPositions.Length;

        for(int i = 0; i < Permutations.Length; i++)
        {
            if(orders[i] is null)
            {
                continue;
            }

            byte[] permutation = Permutations[i];
            bool matches = true;

            //The prefix must cover the bound positions as a SET —
            //the descent applies them one constant at a time, so
            //their order within the prefix is the permutation's own.
            for(int j = 0; j < boundCount && matches; j++)
            {
                matches = boundPositions.IndexOf(permutation[j]) >= 0;
            }

            //The tail must equal the variable sequence exactly: the
            //iterator presents variables in the permutation's level
            //order.
            for(int j = 0; j < variablePositionsInOrder.Length && matches; j++)
            {
                matches = permutation[boundCount + j] == variablePositionsInOrder[j];
            }

            if(matches)
            {
                permutationIndex = i;

                return true;
            }
        }

        permutationIndex = -1;

        return false;
    }

    /// <summary>
    /// Produces a new index whose merged view reflects
    /// <paramref name="additions"/> and <paramref name="removals"/>
    /// applied to this one. The base columns are shared; the delta
    /// accumulates — until it reaches a quarter of the base size,
    /// at which point the merged triple set is folded into a fresh
    /// base with an empty delta.
    /// </summary>
    /// <param name="additions">Triples to add. Triples already present in the merged view are ignored.</param>
    /// <param name="removals">Triples to remove. Triples absent from the merged view are ignored.</param>
    /// <returns>The new index; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <remarks>
    /// One application costs O(batch · log batch + accumulated
    /// delta): only the incoming batch is sorted, and each
    /// accumulated per-permutation run is evolved by a single
    /// linear merge pass. A sustained stream of small commits
    /// therefore stays linear in the accumulated delta between
    /// compactions — the write-rate contract insert-heavy graph
    /// workloads rely on.
    /// </remarks>
    public ColumnarTripleIndex Apply(IEnumerable<EncodedTriple> additions, IEnumerable<EncodedTriple> removals)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        HashSet<EncodedTriple> newAdded = [.. addedSet];
        HashSet<EncodedTriple> newRemoved = [.. removedSet];

        foreach(EncodedTriple triple in additions)
        {
            if(newAdded.Contains(triple))
            {
                continue;
            }

            //A base triple removed earlier is re-added by clearing
            //its tombstone; a genuinely new triple joins the added
            //set; a base triple still visible is a no-op.
            if(!newRemoved.Remove(triple) && !ContainsBase(triple))
            {
                newAdded.Add(triple);
            }
        }

        foreach(EncodedTriple triple in removals)
        {
            //An added triple is removed by dropping it from the
            //added set; a visible base triple gains a tombstone; a
            //triple absent from the merged view is a no-op.
            if(!newAdded.Remove(triple) && ContainsBase(triple))
            {
                newRemoved.Add(triple);
            }
        }

        if((newAdded.Count + newRemoved.Count) * CompactionDenominator >= baseTripleCount || baseTripleCount == 0)
        {
            HashSet<EncodedTriple> merged = [.. EnumerateBaseTriples()];
            merged.ExceptWith(newRemoved);
            merged.UnionWith(newAdded);

            return Build(merged, OrderSetMode, backing: Backing);
        }

        //Evolve the per-order runs by linear merge: only this
        //batch's net effect is sorted (batch-sized), and each
        //accumulated run is walked once. This bounds one Apply at
        //O(batch log batch + accumulated delta), keeping sustained
        //small-commit streams linear — never quadratic — in the
        //accumulated delta between compactions.
        EncodedTriple[] addedInsertions = CollectMissingFrom(newAdded, addedSet);
        EncodedTriple[] addedDeletions = CollectMissingFrom(addedSet, newAdded);
        EncodedTriple[] removedInsertions = CollectMissingFrom(newRemoved, removedSet);
        EncodedTriple[] removedDeletions = CollectMissingFrom(removedSet, newRemoved);

        return new ColumnarTripleIndex(
            orders,
            OrderSetMode,
            baseTripleCount,
            newAdded,
            newRemoved,
            MergePerOrder(addedByOrder, addedInsertions, addedDeletions, orders),
            MergePerOrder(removedByOrder, removedInsertions, removedDeletions, orders),
            backing: Backing);
    }

    /// <summary>
    /// Enumerates the merged view's triples — the base net of the
    /// accumulated delta. Ordering is unspecified.
    /// </summary>
    /// <returns>The merged triple sequence.</returns>
    public IEnumerable<EncodedTriple> EnumerateTriples()
    {
        foreach(EncodedTriple triple in EnumerateBaseTriples())
        {
            if(!removedSet.Contains(triple))
            {
                yield return triple;
            }
        }

        foreach(EncodedTriple triple in addedSet)
        {
            yield return triple;
        }
    }

    //Walks the subject-predicate-object base order reconstructing
    //its triples; the delta is not applied. The walk is sequential,
    //so the per-column readers decode each block exactly once.
    private IEnumerable<EncodedTriple> EnumerateBaseTriples()
    {
        //SPO is materialised under every order-set mode; a view
        //walks its level-0 slice and the absolute offsets keep the
        //deeper levels inside the view's graph.
        ColumnarOrder spo = orders[0]!;
        (int level0Start, int level0End) = Level0BoundsAt(0);
        BlockPackedColumnReader values0 = new(spo.ValuesColumnAt(0));
        BlockPackedColumnReader offsets0 = new(spo.OffsetsColumnAt(0));
        BlockPackedColumnReader values1 = new(spo.ValuesColumnAt(1));
        BlockPackedColumnReader offsets1 = new(spo.OffsetsColumnAt(1));
        BlockPackedColumnReader values2 = new(spo.ValuesColumnAt(2));

        for(int i = level0Start; i < level0End; i++)
        {
            uint subject = values0.ValueAt(i);
            int level1Start = (int)offsets0.ValueAt(i);
            int level1End = (int)offsets0.ValueAt(i + 1);

            for(int j = level1Start; j < level1End; j++)
            {
                uint predicate = values1.ValueAt(j);
                int level2Start = (int)offsets1.ValueAt(j);
                int level2End = (int)offsets1.ValueAt(j + 1);

                for(int k = level2Start; k < level2End; k++)
                {
                    yield return EncodedTriple.FromEncoded(subject, predicate, values2.ValueAt(k));
                }
            }
        }
    }

    //Collects the members of `source` absent from `reference` —
    //the net insertions (or deletions) one Apply contributed to a
    //delta set.
    private static EncodedTriple[] CollectMissingFrom(HashSet<EncodedTriple> source, HashSet<EncodedTriple> reference)
    {
        List<EncodedTriple> result = [];

        foreach(EncodedTriple triple in source)
        {
            if(!reference.Contains(triple))
            {
                result.Add(triple);
            }
        }

        return [.. result];
    }

    //Evolves the per-permutation sorted runs by one batch: the
    //insertions and deletions are sorted per permutation (they are
    //batch-sized), then each existing run is walked once in a
    //three-way merge.
    private static EncodedTriple[][] MergePerOrder(
        EncodedTriple[][] oldRuns,
        EncodedTriple[] insertions,
        EncodedTriple[] deletions,
        ColumnarOrder?[] orders)
    {
        EncodedTriple[][] runs = new EncodedTriple[Permutations.Length][];

        for(int i = 0; i < Permutations.Length; i++)
        {
            //Unmaterialised permutations never serve cursors, so
            //their runs stay empty rather than paying the merge.
            if(orders[i] is null)
            {
                runs[i] = EmptyTriples;

                continue;
            }

            byte position0 = Permutations[i][0];
            byte position1 = Permutations[i][1];
            byte position2 = Permutations[i][2];

            //The shared batch arrays are re-sorted per permutation;
            //copies keep one permutation's sort from disturbing the
            //next.
            EncodedTriple[] sortedInsertions = [.. insertions];
            EncodedTriple[] sortedDeletions = [.. deletions];

            ColumnarSearch.SortByPermutation(sortedInsertions, position0, position1, position2);
            ColumnarSearch.SortByPermutation(sortedDeletions, position0, position1, position2);

            runs[i] = MergeRun(oldRuns[i], sortedInsertions, sortedDeletions, position0, position1, position2);
        }

        return runs;
    }

    //Produces old − deletions + insertions in one pass over three
    //runs sorted under the same permutation. A deletion always
    //names a triple present in the old run, and an insertion never
    //does, so the result length is exact. Packed keys cover all
    //three columns, so key equality is triple equality.
    private static EncodedTriple[] MergeRun(
        EncodedTriple[] oldRun,
        EncodedTriple[] insertions,
        EncodedTriple[] deletions,
        byte position0,
        byte position1,
        byte position2)
    {
        int resultLength = oldRun.Length - deletions.Length + insertions.Length;

        if(resultLength == 0)
        {
            return EmptyTriples;
        }

        EncodedTriple[] result = new EncodedTriple[resultLength];
        int oldIndex = 0;
        int insertIndex = 0;
        int deleteIndex = 0;
        int write = 0;

        while(oldIndex < oldRun.Length)
        {
            UInt128 oldKey = ColumnarSearch.PackKey(in oldRun[oldIndex], position0, position1, position2);

            if(deleteIndex < deletions.Length
                && ColumnarSearch.PackKey(in deletions[deleteIndex], position0, position1, position2) == oldKey)
            {
                oldIndex++;
                deleteIndex++;

                continue;
            }

            if(insertIndex < insertions.Length
                && ColumnarSearch.PackKey(in insertions[insertIndex], position0, position1, position2) < oldKey)
            {
                result[write++] = insertions[insertIndex++];

                continue;
            }

            result[write++] = oldRun[oldIndex++];
        }

        while(insertIndex < insertions.Length)
        {
            result[write++] = insertions[insertIndex++];
        }

        Debug.Assert(write == resultLength, "Merge must consume every insertion and deletion exactly once.");
        Debug.Assert(deleteIndex == deletions.Length, "Every deletion must match a triple in the old run.");

        return result;
    }

    /// <summary>
    /// Returns the added-triples run for the permutation at the
    /// given index, sorted under that permutation.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns>The sorted added run; empty when no delta has accumulated.</returns>
    internal ReadOnlySpan<EncodedTriple> AddedAt(int permutationIndex)
    {
        return addedByOrder[permutationIndex];
    }

    /// <summary>
    /// Returns the removed-triples run for the permutation at the
    /// given index, sorted under that permutation.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns>The sorted removed run; empty when no delta has accumulated.</returns>
    internal ReadOnlySpan<EncodedTriple> RemovedAt(int permutationIndex)
    {
        return removedByOrder[permutationIndex];
    }

    /// <summary>
    /// Returns the permutation index whose descent order starts
    /// with the given RDF positions, in the given sequence.
    /// </summary>
    /// <param name="positionSequence">The RDF positions (0 = subject, 1 = predicate, 2 = object) in descent order; one to three entries covering distinct positions.</param>
    /// <returns>The matching permutation index in [0, 6).</returns>
    /// <exception cref="ArgumentException">The sequence is empty, longer than three, or contains duplicates.</exception>
    public static int PermutationIndexFor(ReadOnlySpan<byte> positionSequence)
    {
        if(positionSequence.Length is < 1 or > 3)
        {
            throw new ArgumentException("A position sequence covers one to three positions.", nameof(positionSequence));
        }

        for(int i = 0; i < Permutations.Length; i++)
        {
            byte[] permutation = Permutations[i];
            bool matches = true;

            for(int j = 0; j < positionSequence.Length; j++)
            {
                if(permutation[j] != positionSequence[j])
                {
                    matches = false;

                    break;
                }
            }

            if(matches)
            {
                return i;
            }
        }

        throw new ArgumentException("The position sequence contains duplicate positions.", nameof(positionSequence));
    }

    /// <summary>
    /// Returns the columnar order at the given permutation index.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6), from <see cref="PermutationIndexFor"/> or <see cref="TrySelectPermutation"/>.</param>
    /// <returns>The order's column set.</returns>
    /// <exception cref="InvalidOperationException">The permutation is not materialised under this index's <see cref="OrderSetMode"/>.</exception>
    public ColumnarOrder OrderAt(int permutationIndex)
    {
        return orders[permutationIndex]
            ?? throw new InvalidOperationException($"Permutation {permutationIndex} is not materialised under {OrderSetMode}; select through TrySelectPermutation.");
    }

    /// <summary>
    /// Emits one <see cref="ColumnarStatisticsTraceEvent"/> per materialised
    /// order to <paramref name="handler"/>, in permutation order, sharing
    /// <paramref name="correlationId"/> and a single timestamp; sequence numbers
    /// are assigned from zero.
    /// </summary>
    /// <param name="handler">The trace handler receiving the events.</param>
    /// <param name="correlationId">The correlation id shared by the emitted events.</param>
    /// <param name="timeProvider">The clock supplying the event timestamp.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public void EmitStatistics(TraceHandler<ColumnarStatisticsTraceEvent> handler, Guid correlationId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        long timestampTicks = timeProvider.GetUtcNow().UtcTicks;
        long sequence = 0;
        for(int permutation = 0; permutation < orders.Length; permutation++)
        {
            if(orders[permutation] is null)
            {
                continue;
            }

            ColumnarStatistics statistics = ColumnarStatistics.From(orders[permutation]!, permutation);
            ColumnarStatisticsTraceEvent traceEvent = ColumnarStatisticsTraceEvent.ForOrder(sequence, timestampTicks, correlationId, statistics);
            sequence++;
            handler(in traceEvent);
        }
    }

    /// <summary>
    /// The RDF positions (0 = subject, 1 = predicate, 2 = object)
    /// of the permutation at the given index, in descent order.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns>The three-position descent sequence.</returns>
    public static ReadOnlySpan<byte> PermutationAt(int permutationIndex)
    {
        return Permutations[permutationIndex];
    }

    /// <summary>
    /// Tests whether the merged view contains the given fully-bound
    /// triple: present in the base without a tombstone, or present
    /// in the accumulated additions.
    /// </summary>
    /// <param name="subject">The bound subject.</param>
    /// <param name="predicate">The bound predicate.</param>
    /// <param name="object">The bound object.</param>
    /// <returns><c>true</c> when the triple is present.</returns>
    public bool Contains(TermId subject, TermId predicate, TermId @object)
    {
        EncodedTriple triple = EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);

        if(addedSet.Count != 0 && addedSet.Contains(triple))
        {
            return true;
        }

        if(removedSet.Count != 0 && removedSet.Contains(triple))
        {
            return false;
        }

        return ContainsBase(triple);
    }

    //Tests the immutable base alone: three lower-bound searches
    //down the subject-predicate-object permutation, over one
    //stack-held block scratch — a cold one-shot descent that
    //allocates nothing.
    private bool ContainsBase(EncodedTriple triple)
    {
        //SPO is materialised under every order-set mode; a view's
        //descent starts from its level-0 slice.
        ColumnarOrder spo = orders[0]!;
        Span<uint> keys = [triple.Subject.Encoded, triple.Predicate.Encoded, triple.Object.Encoded];
        Span<uint> scratch = stackalloc uint[BlockPackedColumn.BlockLength];

        (int lo, int hi) = Level0BoundsAt(0);

        for(int level = 0; level < 3; level++)
        {
            //The scratch is shared across columns, so the cache slot
            //resets whenever the column changes hands.
            BlockPackedColumn values = spo.ValuesColumnAt(level);
            int cachedBlock = -1;
            int found = values.LowerBound(lo, hi, keys[level], scratch, ref cachedBlock);

            if(found >= hi || values.ValueAt(found, scratch, ref cachedBlock) != keys[level])
            {
                return false;
            }

            if(level < 2)
            {
                BlockPackedColumn offsets = spo.OffsetsColumnAt(level);
                cachedBlock = -1;
                lo = (int)offsets.ValueAt(found, scratch, ref cachedBlock);
                hi = (int)offsets.ValueAt(found + 1, scratch, ref cachedBlock);
            }
        }

        return true;
    }
}
