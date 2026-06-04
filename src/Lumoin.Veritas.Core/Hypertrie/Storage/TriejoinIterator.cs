using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A stateful single-pattern iterator over a
/// <see cref="HypertrieSnapshot"/>. Descends the hypertrie
/// following the pattern's bound positions and presents
/// successive variable levels for the consumer to walk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> Construction acquires a reference on the
/// supplied snapshot and applies the pattern's bound positions to
/// the root. If any bound position has no matching descent in the
/// hypertrie the iterator is immediately at end and yields
/// nothing. <see cref="Dispose"/> releases the snapshot
/// reference; the iterator must be disposed.
/// </para>
/// <para>
/// <b>Variable descent.</b> The supplied variable order
/// determines the level structure the iterator presents. At
/// level i the iterator's <see cref="CurrentVariable"/> is
/// <c>variableOrder[i]</c>; the keys at this level are the
/// values at the position that variable occupies in the original
/// pattern. <see cref="Open"/> descends by binding the current
/// variable to a chosen key; <see cref="Up"/> rewinds one level.
/// At the deepest level — variable order fully consumed — the
/// last <see cref="Open"/> performs a leaf check rather than
/// descending further.
/// </para>
/// <para>
/// <b>Frame stack.</b> The iterator carries a stack of descent
/// frames, one per variable level reached. Each frame holds the
/// node at that level, the still-remaining RDF positions, and
/// the cursor at that level. Cursors are stored per-frame so
/// that <see cref="Up"/> restores the cursor exactly to its
/// pre-<see cref="Open"/> position by popping the deepest frame —
/// no rebuilding of cursor state. The post-bound construction
/// frame is always at <c>Frames[0]</c>.
/// </para>
/// <para>
/// A leaf <see cref="Open"/> (the final variable in the order,
/// against a depth-1 frame) does not push a frame — there is no
/// child node to descend into — but does increment
/// <see cref="DescendedLevels"/>. The relationship
/// <c>Frames.Count == DescendedLevels + 1</c> holds in the
/// normal state; <c>Frames.Count == DescendedLevels</c> indicates
/// that the most recent <see cref="Open"/> was a leaf open.
/// </para>
/// <para>
/// <b>Sorted iteration.</b> Keys at every level are visited in
/// ascending order, which is what worst-case-optimal join
/// algorithms (leapfrog triejoin) require.
/// <see cref="Seek"/> advances the cursor at the current level to
/// the first key greater than or equal to its target without
/// moving backwards.
/// </para>
/// <para>
/// <b>Self-joins.</b> A pattern in which the same variable
/// appears in two or more positions is rejected at
/// construction time.
/// </para>
/// <para>
/// <b>Tracing.</b> When a <see cref="TraceHandler{TEvent}"/> is
/// supplied the iterator emits a
/// <see cref="QueryTraceEventKind.IteratorOpened"/> event on
/// construction and per-step events on advancement. The handler
/// is null-checked at each emission point, so the no-handler
/// case is zero cost. Sequence numbers and timestamps are
/// monotonic over a single iterator's life; the correlation id
/// is supplied by the caller and must match the rest of the
/// query's trace stream.
/// </para>
/// <para>
/// <b>Cancellation.</b> Mutating operations
/// (<see cref="Open"/>, <see cref="Next"/>, <see cref="Seek"/>)
/// honour the supplied <see cref="CancellationToken"/>. Read-only
/// properties do not.
/// </para>
/// <para>
/// <b>Thread safety.</b> An iterator is single-threaded; sharing
/// across threads is undefined behaviour. Multiple iterators
/// over the same snapshot can run concurrently because the
/// snapshot's nodes are immutable.
/// </para>
/// </remarks>
[DebuggerDisplay("TriejoinIterator Depth={DescendedLevels} AtEnd={AtEnd}")]
public sealed class TriejoinIterator: IDisposable
{
    private const int PositionSubject = 0;

    private const int PositionPredicate = 1;

    private const int PositionObject = 2;

    //Sequence-number counter for trace events emitted by this
    //iterator. A field rather than a property because Interlocked
    //requires a ref parameter; reads happen only from the
    //emission helpers below.
    private long traceSequence;

    private int disposed;

    /// <summary>The snapshot this iterator descends.</summary>
    public HypertrieSnapshot Snapshot { get; }

    /// <summary>The pattern this iterator is matching.</summary>
    public TriplePattern Pattern { get; }

    /// <summary>The variable elimination order this iterator was constructed with.</summary>
    public IReadOnlyList<Variable> VariableOrder { get; }

    /// <summary>The trace handler for emitted events, or <c>null</c> for no tracing.</summary>
    public TraceHandler<QueryTraceEvent>? TraceHandler { get; }

    //Clock used to stamp Ticks on emitted trace events. Injected
    //so tests can pin time deterministically via FakeTimeProvider;
    //production callers pass TimeProvider.System at the
    //composition root.
    private TimeProvider TimeProvider { get; }

    /// <summary>The correlation id paired with every emitted trace event.</summary>
    public Guid CorrelationId { get; }

    /// <summary>The pattern index passed at construction, used in trace event payloads. Defaults to 0 when not supplied by a caller-side query driver.</summary>
    public int PatternIndex { get; }

    //The frame stack. Each frame carries the node at its level,
    //the still-remaining RDF positions at that node, and the
    //cursor at that level. Frames[0] is established by
    //InitialiseFromBoundPositions; subsequent non-leaf Opens
    //push, Ups pop. A leaf Open does not push.
    private List<DescentFrame> Frames { get; } = [];

    //The position at each level — needed to know which edge map of
    //the current node the variable at this level occupies.
    //LevelPositions[i] gives the original RDF position
    //(0=S/1=P/2=O) the variable at level i occupies.
    //LevelPositions has length VariableOrder.Count and
    //is precomputed at construction.
    private int[] LevelPositions { get; }

    //One per descended level; the value the iterator was opened
    //to at that level. OpenedValues[i] is meaningful only while
    //DescendedLevels > i.
    private uint[] OpenedValues { get; }

    /// <summary>
    /// The number of variable levels currently descended. 0 means
    /// no variables have been bound; <c>VariableOrder.Count</c>
    /// means every variable has been opened.
    /// </summary>
    public int DescendedLevels { get; private set; }

    /// <summary>
    /// <c>true</c> when no further keys are available at the
    /// current level — the cursor at the deepest frame is past
    /// its last key, or the iterator was constructed against a
    /// pattern with no matching descent path.
    /// </summary>
    public bool AtEnd
    {
        get
        {
            if(constructionAtEnd)
            {
                return true;
            }

            if(Frames.Count == 0)
            {
                return true;
            }

            return Frames[Frames.Count - 1].Cursor.AtEnd;
        }
    }

    //True only when the construction-phase initialisation
    //determined that no descent is possible (a bound position had
    //no match). The flag is sticky — once set it is never cleared.
    private bool constructionAtEnd;

    /// <summary>
    /// The variable at the current level — the next variable
    /// the consumer would bind via <see cref="Open"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">All variables have been bound; there is no current variable.</exception>
    public Variable CurrentVariable
    {
        get
        {
            if(DescendedLevels >= VariableOrder.Count)
            {
                throw new InvalidOperationException("All variables have been bound; there is no current variable.");
            }

            return VariableOrder[DescendedLevels];
        }
    }

    /// <summary>
    /// The current key at the current level — the
    /// <see cref="TermId"/> the iterator's cursor is positioned
    /// on. Undefined when <see cref="AtEnd"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The iterator is at end.</exception>
    public TermId Key
    {
        get
        {
            if(AtEnd)
            {
                throw new InvalidOperationException("Iterator is at end; Key is undefined.");
            }

            return TermId.FromEncoded(Frames[Frames.Count - 1].Cursor.CurrentKey);
        }
    }

    /// <summary>
    /// Constructs a new iterator over <paramref name="snapshot"/>
    /// for <paramref name="pattern"/>, descending in
    /// <paramref name="variableOrder"/>. Acquires a reference on
    /// the snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot to iterate.</param>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="variableOrder">The order in which the iterator's variable levels should be presented; must contain exactly the distinct variables of <paramref name="pattern"/>.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Pass <see cref="TimeProvider.System"/> in production; tests pinning trace timing pass a <c>FakeTimeProvider</c>.</param>
    /// <param name="patternIndex">The pattern index in the parent <see cref="BasicGraphPattern"/>, for trace events. Pass 0 for standalone iterators.</param>
    /// <param name="correlationId">The correlation id paired with emitted trace events; pass <see cref="Guid.Empty"/> when not part of a wider query.</param>
    /// <param name="traceHandler">A trace handler, or <c>null</c> for no tracing.</param>
    /// <param name="cancellationToken">Cancellation token for the construction-time descent through bound positions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/>, <paramref name="variableOrder"/>, or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The pattern contains a self-join, or <paramref name="variableOrder"/> is not a permutation of the pattern's variables.</exception>
    /// <exception cref="OperationCanceledException">The construction-time descent was cancelled.</exception>
    public TriejoinIterator(
        HypertrieSnapshot snapshot,
        TriplePattern pattern,
        IReadOnlyList<Variable> variableOrder,
        TimeProvider timeProvider,
        int patternIndex = 0,
        Guid correlationId = default,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(variableOrder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if(pattern.HasSelfJoin())
        {
            throw new ArgumentException("Patterns containing self-joins (the same variable in multiple positions) are not yet supported.", nameof(pattern));
        }

        ValidateVariableOrder(pattern, variableOrder);

        Snapshot = snapshot.Acquire();
        Pattern = pattern;
        VariableOrder = variableOrder;
        TimeProvider = timeProvider;
        PatternIndex = patternIndex;
        CorrelationId = correlationId;
        TraceHandler = traceHandler;

        OpenedValues = new uint[variableOrder.Count];
        LevelPositions = ComputeLevelPositions(pattern, variableOrder);

        cancellationToken.ThrowIfCancellationRequested();

        EmitIteratorOpened();

        InitialiseFromBoundPositions(cancellationToken);
    }

    /// <summary>
    /// Returns the <see cref="TermId"/> previously bound to
    /// <paramref name="variable"/> by a successful
    /// <see cref="Open"/>. The variable must precede the current
    /// level in <see cref="VariableOrder"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="variable"/> has not yet been bound, or is not in this iterator's variable order.</exception>
    public TermId ValueOf(Variable variable)
    {
        for(int i = 0; i < DescendedLevels; i++)
        {
            if(VariableOrder[i] == variable)
            {
                return TermId.FromEncoded(OpenedValues[i]);
            }
        }

        throw new ArgumentException($"Variable id {variable.Id} has not yet been bound by this iterator.", nameof(variable));
    }

    /// <summary>
    /// Descends to the next variable level by binding the current
    /// variable to <paramref name="value"/>. Returns <c>true</c>
    /// when the descent path exists in the hypertrie; <c>false</c>
    /// when no descent path exists, in which case the iterator's
    /// state is unchanged.
    /// </summary>
    /// <param name="value">The <see cref="TermId"/> to bind the current variable to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the descent succeeded.</returns>
    /// <exception cref="InvalidOperationException">All variables have already been bound.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public bool Open(TermId value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if(DescendedLevels >= VariableOrder.Count)
        {
            throw new InvalidOperationException("All variables have been bound; cannot Open further.");
        }

        if(constructionAtEnd || Frames.Count == 0)
        {
            return false;
        }

        DescentFrame currentFrame = Frames[Frames.Count - 1];
        uint rawValue = value.Encoded;

        //Synthetic frame: the cursor carries a single key and the
        //frame's Node is default. Descent succeeds iff the
        //requested value matches that key. A pending synthetic key
        //means a deeper single-key level follows (the SEN2 chain);
        //pushing its frame keeps the Frames/DescendedLevels
        //relationship identical to a non-leaf open.
        if(currentFrame.Node.EdgeMaps is null)
        {
            uint senKey = currentFrame.Cursor.CurrentKey;
            if(rawValue != senKey)
            {
                return false;
            }

            OpenedValues[DescendedLevels] = rawValue;

            if(currentFrame.PendingSyntheticKey is uint pendingKey)
            {
                int consumedIndex = IndexOf(currentFrame.RemainingPositions, LevelPositions[DescendedLevels]);
                int[] deeperRemaining = WithoutAt(currentFrame.RemainingPositions, consumedIndex);

                Frames.Add(new DescentFrame
                {
                    Node = default,
                    RemainingPositions = deeperRemaining,
                    Cursor = new EdgeMapKeyCursor(pendingKey),
                });
            }

            DescendedLevels++;

            EmitIteratorAdvanced(VariableOrder[DescendedLevels - 1], rawValue);

            return true;
        }

        int positionInRemaining = IndexOf(currentFrame.RemainingPositions, LevelPositions[DescendedLevels]);

        if(positionInRemaining < 0)
        {
            //Should be unreachable given the precomputed LevelPositions, but defensive.
            return false;
        }

        EdgeMap edgeMap = currentFrame.Node.EdgeMaps[positionInRemaining];

        if(!EdgeMap.TryGetChild(in edgeMap, rawValue, InlineKeyLookups.Scalar, out NodeHandle childHandle))
        {
            return false;
        }

        OpenedValues[DescendedLevels] = rawValue;

        if(currentFrame.Node.Depth == 1)
        {
            //Leaf-open: the descent succeeded (TryGetChild
            //confirmed the key is present), but there is no
            //child node to push a frame for. We mark the level
            //consumed and leave the frame stack untouched. The
            //deepest frame's cursor stays where it is, so a
            //subsequent Up restores trivially.
            DescendedLevels++;

            EmitIteratorAdvanced(VariableOrder[DescendedLevels - 1], rawValue);

            return true;
        }

        Debug.Assert(!childHandle.IsNone, "depth-2 or depth-3 descent yielded NodeHandle.None; invariant violation.");

        int[] childRemainingPositions = WithoutAt(currentFrame.RemainingPositions, positionInRemaining);

        if(childHandle.IsSingleEntry)
        {
            //SEN child: push a synthetic frame whose cursor yields
            //the SEN's single key. The next level enumerates one
            //value (or seeks to it), and an Open against the same
            //value completes the descent.
            EdgeMapKeyCursor senCursor = new(childHandle.SingleEntryKey);

            Frames.Add(new DescentFrame
            {
                Node = default,
                RemainingPositions = childRemainingPositions,
                Cursor = senCursor,
            });

            DescendedLevels++;

            EmitIteratorAdvanced(VariableOrder[DescendedLevels - 1], rawValue);

            return true;
        }

        if(childHandle.IsSingleEntryPair)
        {
            //SEN2 child: the whole remaining subtree is one pair,
            //mapped to the two remaining positions in ascending
            //order. The next variable level decides which key the
            //synthetic frame presents; the other key follows as
            //that frame's pending synthetic key.
            Debug.Assert(childRemainingPositions.Length == 2,
                "SEN2 descent requires exactly two remaining positions.");

            (uint pairFirst, uint pairSecond) = Snapshot.Store.GetPair(childHandle);
            bool nextLevelTakesFirst = LevelPositions[DescendedLevels + 1] == childRemainingPositions[0];
            uint cursorKey = nextLevelTakesFirst ? pairFirst : pairSecond;
            uint pendingKey = nextLevelTakesFirst ? pairSecond : pairFirst;

            Frames.Add(new DescentFrame
            {
                Node = default,
                RemainingPositions = childRemainingPositions,
                Cursor = new EdgeMapKeyCursor(cursorKey),
                PendingSyntheticKey = pendingKey,
            });

            DescendedLevels++;

            EmitIteratorAdvanced(VariableOrder[DescendedLevels - 1], rawValue);

            return true;
        }

        HypertrieNode child = Snapshot.Store.GetByHandle(childHandle);

        //Non-leaf descent: push a frame for the child node with a
        //fresh cursor over the appropriate edge map for the next
        //variable level.
        EdgeMapKeyCursor childCursor = BuildCursorForLevel(child, childRemainingPositions, DescendedLevels + 1);

        Frames.Add(new DescentFrame
        {
            Node = child,
            RemainingPositions = childRemainingPositions,
            Cursor = childCursor,
        });

        DescendedLevels++;

        EmitIteratorAdvanced(VariableOrder[DescendedLevels - 1], rawValue);

        return true;
    }

    /// <summary>
    /// Rewinds one variable level. The variable at the level the
    /// iterator just exited becomes
    /// <see cref="CurrentVariable"/> again. The cursor at the
    /// restored level is exactly where it was when
    /// <see cref="Open"/> was called.
    /// </summary>
    /// <exception cref="InvalidOperationException">No variable has been bound; nothing to rewind.</exception>
    public void Up()
    {
        if(DescendedLevels == 0)
        {
            throw new InvalidOperationException("No variables have been bound; cannot Up.");
        }

        //If Frames.Count == DescendedLevels we are in leaf-open
        //state — no frame was pushed by the last Open, so nothing
        //to pop. Just decrement DescendedLevels; the deepest
        //frame's cursor is already where it should be.
        //Otherwise Frames.Count == DescendedLevels + 1 (non-leaf
        //state) and we must pop the deepest frame to expose the
        //parent frame's cursor at its saved position.
        if(Frames.Count > DescendedLevels)
        {
            Frames.RemoveAt(Frames.Count - 1);
        }

        DescendedLevels--;
    }

    /// <summary>
    /// Rebuilds the current variable level's cursor from scratch, re-positioning it at its first key, without
    /// changing <see cref="DescendedLevels"/> or any deeper bound value.
    /// </summary>
    /// <remarks>
    /// The worst-case-optimal join driver uses this to re-enumerate an <em>independent</em> variable when another
    /// variable this iterator does not share has just been re-bound. Without it the iterator, left exhausted
    /// (<see cref="AtEnd"/>) from the previous binding of that other variable, would wrongly contribute nothing for
    /// the new binding — collapsing a cross product (e.g. <c>{?s :p ?o1 ; :q ?o2}</c> yielding the diagonal instead
    /// of every <c>?o1</c>×<c>?o2</c> pair). The deepest frame's node and remaining positions are unchanged by
    /// exhausting its cursor, so rebuilding the cursor over the same edge map restores enumeration from the first
    /// key; the snapshot is immutable so the keys are exactly as before.
    /// </remarks>
    /// <exception cref="InvalidOperationException">There is no frame to rebuild (the iterator never descended, or is in a fully-bound state with no current level).</exception>
    public void RestartCurrentLevel()
    {
        if(Frames.Count == 0)
        {
            throw new InvalidOperationException("Cannot restart the current level: the iterator has no descent frame to rebuild.");
        }

        if(DescendedLevels >= VariableOrder.Count)
        {
            throw new InvalidOperationException("Cannot restart the current level: all variables are bound, so there is no current level.");
        }

        //The deepest frame walks the current level. Resetting its cursor re-presents the same (immutable) key
        //sequence from the first key — works uniformly for a synthetic single-key cursor and a real edge-map cursor.
        DescentFrame frame = Frames[Frames.Count - 1];
        EdgeMapKeyCursor cursor = frame.Cursor;
        cursor.Reset();
        frame.Cursor = cursor;
    }

    /// <summary>
    /// Advances the cursor at the current level to the next key.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public void Next(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if(AtEnd)
        {
            return;
        }

        //Mutate the deepest frame's cursor in place via index access
        //(struct in a List requires copy-modify-write).
        DescentFrame frame = Frames[Frames.Count - 1];
        EdgeMapKeyCursor cursor = frame.Cursor;
        cursor.MoveNext();
        frame.Cursor = cursor;

        if(cursor.AtEnd)
        {
            EmitIteratorReachedEnd();
        }
    }

    /// <summary>
    /// Advances the cursor at the current level to the first key
    /// greater than or equal to <paramref name="target"/>. Never
    /// moves the cursor backwards.
    /// </summary>
    /// <param name="target">The seek target as a <see cref="TermId"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public void Seek(TermId target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if(AtEnd)
        {
            return;
        }

        DescentFrame frame = Frames[Frames.Count - 1];
        EdgeMapKeyCursor cursor = frame.Cursor;
        cursor.SeekTo(target.Encoded);
        frame.Cursor = cursor;

        if(cursor.AtEnd)
        {
            EmitIteratorReachedEnd();
        }
    }

    /// <summary>
    /// Releases the snapshot reference acquired by this
    /// iterator. Calling more than once is a no-op.
    /// </summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Snapshot.Release();
    }

    //Validates that variableOrder is exactly the set of variables
    //appearing in pattern, with no duplicates and no extras.
    private static void ValidateVariableOrder(TriplePattern pattern, IReadOnlyList<Variable> variableOrder)
    {
        HashSet<Variable> patternVariables = [.. pattern.Variables()];
        HashSet<Variable> orderVariables = [];

        foreach(Variable variable in variableOrder)
        {
            if(!orderVariables.Add(variable))
            {
                throw new ArgumentException($"Variable order contains duplicate variable id {variable.Id}.", nameof(variableOrder));
            }
        }

        if(!patternVariables.SetEquals(orderVariables))
        {
            throw new ArgumentException("Variable order is not a permutation of the pattern's variables.", nameof(variableOrder));
        }
    }

    //Maps each level i in the variable order to the original RDF
    //position (0/1/2) the variable at that level occupies in the
    //pattern.
    private static int[] ComputeLevelPositions(TriplePattern pattern, IReadOnlyList<Variable> variableOrder)
    {
        int[] result = new int[variableOrder.Count];

        for(int level = 0; level < variableOrder.Count; level++)
        {
            Variable variable = variableOrder[level];
            int position = -1;

            for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
            {
                PatternPosition slot = pattern.At(rdfPosition);

                if(slot.IsVariable && slot.Variable == variable)
                {
                    position = rdfPosition;

                    break;
                }
            }

            //ValidateVariableOrder above guarantees the variable is in the pattern.
            Debug.Assert(position >= 0, "Every variable in the order must be in the pattern.");

            result[level] = position;
        }

        return result;
    }

    //Walks the snapshot root applying the pattern's bound
    //positions in S/P/O order. After this method returns,
    //Frames[0] is the post-bound frontier from which the variable
    //descent will proceed (or constructionAtEnd is set).
    private void InitialiseFromBoundPositions(CancellationToken cancellationToken)
    {
        HypertrieNode current = Snapshot.Store.GetByHandle(Snapshot.Root);
        int[] remainingPositions = [PositionSubject, PositionPredicate, PositionObject];

        //Pending synthetic keys produced by SEN or SEN2 descent,
        //mapped one-to-one onto remainingPositions (ascending).
        //While any are pending, there is no node left to query —
        //bounds verify against the keys directly and variables
        //enumerate them through synthetic cursors.
        uint pendingKey0 = 0;
        uint pendingKey1 = 0;
        int pendingCount = 0;

        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PatternPosition slot = Pattern.At(rdfPosition);

            if(!slot.IsBound)
            {
                continue;
            }

            int positionInRemaining = IndexOf(remainingPositions, rdfPosition);

            if(positionInRemaining < 0)
            {
                throw new InvalidOperationException("Internal error: bound position not found in remaining-position list.");
            }

            uint boundValue = slot.BoundTerm.Encoded;

            //A prior iteration descended into inline keys (SEN or
            //SEN2); this bound verifies against the pending key at
            //its position — there is no further edge map to query.
            if(pendingCount > 0)
            {
                Debug.Assert(pendingCount == remainingPositions.Length,
                    "Pending synthetic keys must map one-to-one onto the remaining positions.");

                uint expected = positionInRemaining == 0 ? pendingKey0 : pendingKey1;

                if(boundValue != expected)
                {
                    constructionAtEnd = true;

                    return;
                }

                if(positionInRemaining == 0)
                {
                    pendingKey0 = pendingKey1;
                }

                pendingCount--;
                remainingPositions = WithoutAt(remainingPositions, positionInRemaining);

                continue;
            }

            EdgeMap edgeMap = current.EdgeMaps[positionInRemaining];

            if(!EdgeMap.TryGetChild(in edgeMap, boundValue, InlineKeyLookups.Scalar, out NodeHandle childHandle))
            {
                constructionAtEnd = true;

                return;
            }

            if(current.Depth == 1)
            {
                //Bound position landed at a leaf — no further
                //descent. The pattern is fully bound (no
                //variables); the iterator presents zero variable
                //levels and is in a "verified" state.
                Debug.Assert(VariableOrder.Count == 0, "A pattern that lands a bound position at the leaf has no remaining variables.");

                return;
            }

            if(childHandle.IsSingleEntry)
            {
                //Descended into an SEN leaf: one pending key for
                //the one remaining position.
                pendingKey0 = childHandle.SingleEntryKey;
                pendingCount = 1;
                remainingPositions = WithoutAt(remainingPositions, positionInRemaining);

                continue;
            }

            if(childHandle.IsSingleEntryPair)
            {
                //Descended into an SEN2 pair: two pending keys for
                //the two remaining positions, in ascending order.
                (pendingKey0, pendingKey1) = Snapshot.Store.GetPair(childHandle);
                pendingCount = 2;
                remainingPositions = WithoutAt(remainingPositions, positionInRemaining);

                continue;
            }

            Debug.Assert(!childHandle.IsNone, "depth-2 or depth-3 descent yielded NodeHandle.None; invariant violation.");

            current = Snapshot.Store.GetByHandle(childHandle);
            remainingPositions = WithoutAt(remainingPositions, positionInRemaining);
        }

        if(VariableOrder.Count == 0)
        {
            //Fully bound pattern. Any pending keys were verified by
            //the bound loop above (or constructionAtEnd was set).
            //Nothing else to do.
            return;
        }

        if(pendingCount == 2)
        {
            //Post-bound frontier is an SEN2 pair with both keys
            //unconsumed. The first variable level decides which key
            //its synthetic cursor presents; the other follows as
            //the pending synthetic key.
            bool firstLevelTakesFirst = LevelPositions[0] == remainingPositions[0];

            Frames.Add(new DescentFrame
            {
                Node = default,
                RemainingPositions = remainingPositions,
                Cursor = new EdgeMapKeyCursor(firstLevelTakesFirst ? pendingKey0 : pendingKey1),
                PendingSyntheticKey = firstLevelTakesFirst ? pendingKey1 : pendingKey0,
            });

            return;
        }

        if(pendingCount == 1)
        {
            //Post-bound frontier is a single inline key. The next
            //(and only remaining) variable enumerates it via a
            //synthetic cursor.
            Frames.Add(new DescentFrame
            {
                Node = default,
                RemainingPositions = remainingPositions,
                Cursor = new EdgeMapKeyCursor(pendingKey0),
            });

            return;
        }

        EdgeMapKeyCursor cursor = BuildCursorForLevel(current, remainingPositions, level: 0);

        Frames.Add(new DescentFrame
        {
            Node = current,
            RemainingPositions = remainingPositions,
            Cursor = cursor,
        });
    }

    //Builds a fresh cursor over the edge map of `node`
    //corresponding to the variable at the given descent level.
    //`remainingPositions` is the positions still active at this
    //node (S/P/O); the variable at `level` occupies one of them.
    private EdgeMapKeyCursor BuildCursorForLevel(HypertrieNode node, int[] remainingPositions, int level)
    {
        int rdfPosition = LevelPositions[level];
        int positionInRemaining = IndexOf(remainingPositions, rdfPosition);

        if(positionInRemaining < 0)
        {
            throw new InvalidOperationException("Internal error: variable's RDF position not found in node's remaining-position list.");
        }

        return new EdgeMapKeyCursor(node, positionInRemaining);
    }

    //Returns the index of `value` in `array`, or -1 if absent.
    private static int IndexOf(int[] array, int value)
    {
        for(int i = 0; i < array.Length; i++)
        {
            if(array[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    //Returns a copy of `array` with the entry at `index` removed.
    private static int[] WithoutAt(int[] array, int index)
    {
        int[] result = new int[array.Length - 1];
        int destination = 0;

        for(int source = 0; source < array.Length; source++)
        {
            if(source == index)
            {
                continue;
            }

            result[destination++] = array[source];
        }

        return result;
    }

    private void EmitIteratorOpened()
    {
        if(TraceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.IteratorOpened(sequence, TimeProvider.GetUtcNow().UtcTicks, CorrelationId, PatternIndex);

        TraceHandler(in evt);
    }

    private void EmitIteratorAdvanced(Variable variable, uint value)
    {
        if(TraceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.IteratorAdvanced(sequence, TimeProvider.GetUtcNow().UtcTicks, CorrelationId, PatternIndex, variable, value);

        TraceHandler(in evt);
    }

    private void EmitIteratorReachedEnd()
    {
        if(TraceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        Variable variable = DescendedLevels < VariableOrder.Count ? VariableOrder[DescendedLevels] : default;
        QueryTraceEvent evt = QueryTraceEvent.IteratorReachedEnd(sequence, TimeProvider.GetUtcNow().UtcTicks, CorrelationId, PatternIndex, variable);

        TraceHandler(in evt);
    }

    //Per-level descent state. The Cursor field is mutable —
    //Next/Seek copy-modify-write through Frames[Frames.Count-1].
    private sealed class DescentFrame
    {
        /// <summary>The node whose edge map the cursor walks; <c>default</c> for synthetic single-key frames.</summary>
        public required HypertrieNode Node { get; init; }

        /// <summary>The original RDF positions still unresolved at this level, ascending.</summary>
        public required int[] RemainingPositions { get; init; }

        /// <summary>The cursor over this level's keys.</summary>
        public required EdgeMapKeyCursor Cursor { get; set; }

        /// <summary>For a synthetic frame produced by SEN2 descent: the key the level below this one presents, or <c>null</c> when this synthetic frame is the final level.</summary>
        public uint? PendingSyntheticKey { get; init; }
    }
}
