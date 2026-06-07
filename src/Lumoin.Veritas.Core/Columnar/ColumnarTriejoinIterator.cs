using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A stateful single-pattern iterator over a
/// <see cref="ColumnarTripleIndex"/>, presenting the same level
/// contract as the hypertrie's triejoin iterator: bound positions
/// are applied at construction, successive variable levels are
/// walked in ascending key order, and
/// <see cref="Open"/> / <see cref="Up"/> / <see cref="Next"/> /
/// <see cref="Seek"/> drive the descent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order selection.</b> Construction concatenates the pattern's
/// bound positions (in subject-predicate-object order) with the
/// variable positions (in the supplied variable order) into a
/// three-position descent sequence and picks the index permutation
/// whose levels match it. Every (bound-set, variable-order)
/// combination has a matching permutation because the index
/// carries all six.
/// </para>
/// <para>
/// <b>Merged view.</b> Each level's candidate set is the union of
/// a contiguous slice <c>[lo, hi)</c> of the base permutation's
/// value column and the distinct level keys of the accumulated
/// added-triples run under the current prefix, minus base keys
/// whose subtrees are fully tombstoned by the removed run. A key
/// is visible when its net triple count — base minus removed plus
/// added — is positive; positioning operations skip invisible
/// keys, so the cursor always rests on a visible key or at end.
/// With an empty delta the merge degenerates to the pure base
/// slice at the cost of two slice-emptiness checks per step.
/// </para>
/// <para>
/// <b>Self-joins.</b> A pattern with the same variable in multiple
/// positions is rejected at construction, matching the hypertrie
/// iterator's contract.
/// </para>
/// <para>
/// <b>Thread safety.</b> An iterator is single-threaded. Multiple
/// iterators over the same index can run concurrently because the
/// index — base and delta both — is immutable.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarTriejoinIterator Depth={DescendedLevels} AtEnd={AtEnd}")]
public sealed class ColumnarTriejoinIterator
{
    //One frame per presented level: the base slice bounds and
    //cursor, the added-run slice bounds and cursor (the cursor
    //rests on the first triple of its current key run), and the
    //removed-run slice bounds for visibility counting.
    private struct LevelFrame
    {
        /// <summary>The base slice's inclusive start index in the level's value column.</summary>
        public int BaseLo;

        /// <summary>The base slice's exclusive end index in the level's value column.</summary>
        public int BaseHi;

        /// <summary>The base cursor's current absolute index; in <c>[BaseLo, BaseHi]</c>, where <c>BaseHi</c> means exhausted.</summary>
        public int BasePos;

        /// <summary>The added run's inclusive start for the current prefix.</summary>
        public int AddLo;

        /// <summary>The added run's exclusive end for the current prefix.</summary>
        public int AddHi;

        /// <summary>The added cursor's current index — the first triple of its current key run; <c>AddHi</c> means exhausted.</summary>
        public int AddPos;

        /// <summary>The removed run's inclusive start for the current prefix.</summary>
        public int RemLo;

        /// <summary>The removed run's exclusive end for the current prefix.</summary>
        public int RemHi;
    }

    private readonly ColumnarTripleIndex index;

    private readonly ColumnarOrder order;

    private readonly int permutationIndex;

    //The chosen permutation's descent positions: the RDF position
    //(0=S, 1=P, 2=O) each CSR level orders by.
    private readonly byte descentPosition0;

    private readonly byte descentPosition1;

    private readonly byte descentPosition2;

    //The number of bound positions applied at construction; the
    //CSR level for variable level v is boundCount + v.
    private readonly int boundCount;

    //Per-level column readers, created on first touch; each owns a
    //one-block decode scratch, and the iterator's descent locality
    //keeps most touches on the cached block.
    private readonly BlockPackedColumnReader?[] valueReaders = new BlockPackedColumnReader?[3];

    private readonly BlockPackedColumnReader?[] offsetReaders = new BlockPackedColumnReader?[2];

    //Frames for the presented variable levels. Frames[0] is the
    //post-bound frontier; a leaf open does not push, so
    //frameCount == DescendedLevels + 1 in the normal state and
    //frameCount == DescendedLevels after a leaf open.
    private readonly LevelFrame[] frames;

    private int frameCount;

    //One per descended level; the value the iterator was opened to
    //at that level.
    private readonly uint[] openedValues;

    //True only when the construction-phase bound descent found no
    //visible match. Sticky.
    private readonly bool constructionAtEnd;

    /// <summary>The pattern this iterator is matching.</summary>
    public TriplePattern Pattern { get; }

    /// <summary>The variable elimination order this iterator was constructed with.</summary>
    public IReadOnlyList<Variable> VariableOrder { get; }

    /// <summary>
    /// The number of variable levels currently descended. 0 means
    /// no variables have been bound; <c>VariableOrder.Count</c>
    /// means every variable has been opened.
    /// </summary>
    public int DescendedLevels { get; private set; }

    /// <summary>
    /// <c>true</c> when no further keys are available at the
    /// current level — both the base and added cursors at the
    /// deepest frame are past their last keys, or the
    /// construction-time bound descent found no visible match.
    /// </summary>
    public bool AtEnd
    {
        get
        {
            if(constructionAtEnd)
            {
                return true;
            }

            if(frameCount == 0)
            {
                return true;
            }

            ref readonly LevelFrame frame = ref frames[frameCount - 1];

            return frame.BasePos >= frame.BaseHi && frame.AddPos >= frame.AddHi;
        }
    }

    /// <summary>
    /// The variable at the current level — the next variable the
    /// consumer would bind via <see cref="Open"/>.
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
    /// The current key at the current level — the smaller of the
    /// base and added candidates. Undefined when
    /// <see cref="AtEnd"/>.
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

            ref readonly LevelFrame frame = ref frames[frameCount - 1];
            int level = boundCount + frameCount - 1;

            return TermId.FromEncoded(CurrentKeyOf(in frame, level));
        }
    }

    /// <summary>
    /// Constructs a new iterator over <paramref name="index"/> for
    /// <paramref name="pattern"/>, descending in
    /// <paramref name="variableOrder"/>.
    /// </summary>
    /// <param name="index">The index to iterate.</param>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="variableOrder">The order in which the iterator's variable levels should be presented; must contain exactly the distinct variables of <paramref name="pattern"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="variableOrder"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The pattern contains a self-join, or <paramref name="variableOrder"/> is not a permutation of the pattern's variables.</exception>
    public ColumnarTriejoinIterator(
        ColumnarTripleIndex index,
        TriplePattern pattern,
        IReadOnlyList<Variable> variableOrder)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(variableOrder);

        if(pattern.HasSelfJoin())
        {
            throw new ArgumentException("Patterns containing self-joins (the same variable in multiple positions) are not yet supported.", nameof(pattern));
        }

        ValidateVariableOrder(pattern, variableOrder);

        this.index = index;
        Pattern = pattern;
        VariableOrder = variableOrder;
        openedValues = new uint[variableOrder.Count];
        frames = new LevelFrame[3];

        //Select a materialised permutation whose prefix covers the
        //bound positions and whose tail presents the variables in
        //the requested order. The descent then follows the
        //permutation itself — bound constants apply in ITS prefix
        //order, which is equivalent (each bound level narrows
        //independently) and is what lets the three-rotation mode
        //serve bound sets the subject-predicate-object prefix order
        //could not.
        Span<byte> boundPositions = stackalloc byte[3];
        int boundLength = 0;

        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            if(pattern.At(rdfPosition).IsBound)
            {
                boundPositions[boundLength++] = (byte)rdfPosition;
            }
        }

        boundCount = boundLength;

        Span<byte> variablePositions = stackalloc byte[3];
        for(int level = 0; level < variableOrder.Count; level++)
        {
            variablePositions[level] = (byte)PositionOfVariable(pattern, variableOrder[level]);
        }

        if(!index.TrySelectPermutation(boundPositions[..boundLength], variablePositions[..variableOrder.Count], out permutationIndex))
        {
            throw new ArgumentException(
                $"No materialised permutation under {index.OrderSetMode} serves this pattern's bound set with the requested variable order; plan orders through ColumnarRotationPlanner.",
                nameof(variableOrder));
        }

        order = index.OrderAt(permutationIndex);

        //The selected permutation IS the descent sequence; the
        //bound-descent loop below indexes it directly.
        ReadOnlySpan<byte> descent = ColumnarTripleIndex.PermutationAt(permutationIndex);
        descentPosition0 = descent[0];
        descentPosition1 = descent[1];
        descentPosition2 = descent[2];

        //Apply the bound prefix: narrow the base slice and both
        //delta runs level by level. A level whose net triple count
        //is zero leaves the iterator at end.
        ReadOnlySpan<EncodedTriple> added = index.AddedAt(permutationIndex);
        ReadOnlySpan<EncodedTriple> removed = index.RemovedAt(permutationIndex);

        (int baseLo, int baseHi) = index.Level0BoundsAt(permutationIndex);
        int addLo = 0;
        int addHi = added.Length;
        int remLo = 0;
        int remHi = removed.Length;

        for(int level = 0; level < boundCount; level++)
        {
            uint boundValue = pattern.At(descent[level]).BoundTerm.Encoded;
            byte column = DescentPositionAt(level);

            BlockPackedColumnReader values = ValueReaderAt(level);
            int found = values.LowerBound(baseLo, baseHi, boundValue);
            bool baseFound = found < baseHi && values.ValueAt(found) == boundValue;

            int addChildLo = ColumnarSearch.LowerBoundByColumn(added, addLo, addHi, column, boundValue);
            int addChildHi = ColumnarSearch.UpperBoundByColumn(added, addChildLo, addHi, column, boundValue);
            int remChildLo = ColumnarSearch.LowerBoundByColumn(removed, remLo, remHi, column, boundValue);
            int remChildHi = ColumnarSearch.UpperBoundByColumn(removed, remChildLo, remHi, column, boundValue);

            int baseTriples = baseFound ? TriplesUnder(level, found) : 0;
            int net = baseTriples - (remChildHi - remChildLo) + (addChildHi - addChildLo);

            if(net <= 0)
            {
                constructionAtEnd = true;

                return;
            }

            addLo = addChildLo;
            addHi = addChildHi;
            remLo = remChildLo;
            remHi = remChildHi;

            if(level == 2)
            {
                //Fully bound pattern verified; no variable levels.
                Debug.Assert(variableOrder.Count == 0, "A pattern whose bound prefix consumes all three levels has no variables.");

                return;
            }

            if(baseFound)
            {
                BlockPackedColumnReader offsets = OffsetReaderAt(level);
                baseLo = (int)offsets.ValueAt(found);
                baseHi = (int)offsets.ValueAt(found + 1);
            }
            else
            {
                baseLo = 0;
                baseHi = 0;
            }
        }

        if(variableOrder.Count == 0)
        {
            return;
        }

        frames[0] = new LevelFrame
        {
            BaseLo = baseLo,
            BaseHi = baseHi,
            BasePos = baseLo,
            AddLo = addLo,
            AddHi = addHi,
            AddPos = addLo,
            RemLo = remLo,
            RemHi = remHi,
        };
        frameCount = 1;

        Normalize(ref frames[0], boundCount);
    }

    /// <summary>
    /// Returns the <see cref="TermId"/> previously bound to
    /// <paramref name="variable"/> by a successful
    /// <see cref="Open"/>.
    /// </summary>
    /// <param name="variable">A variable this iterator has already bound.</param>
    /// <returns>The bound key.</returns>
    /// <exception cref="ArgumentException"><paramref name="variable"/> has not yet been bound, or is not in this iterator's variable order.</exception>
    public TermId ValueOf(Variable variable)
    {
        for(int i = 0; i < DescendedLevels; i++)
        {
            if(VariableOrder[i] == variable)
            {
                return TermId.FromEncoded(openedValues[i]);
            }
        }

        throw new ArgumentException($"Variable id {variable.Id} has not yet been bound by this iterator.", nameof(variable));
    }

    /// <summary>
    /// Descends to the next variable level by binding the current
    /// variable to <paramref name="value"/>. Returns <c>true</c>
    /// when the value is visible at this level; <c>false</c> when
    /// it is not, in which case the iterator's state is unchanged.
    /// </summary>
    /// <param name="value">The <see cref="TermId"/> to bind the current variable to.</param>
    /// <returns><c>true</c> when the descent succeeded.</returns>
    /// <exception cref="InvalidOperationException">All variables have already been bound.</exception>
    public bool Open(TermId value)
    {
        if(DescendedLevels >= VariableOrder.Count)
        {
            throw new InvalidOperationException("All variables have been bound; cannot Open further.");
        }

        if(constructionAtEnd || frameCount == 0)
        {
            return false;
        }

        ref readonly LevelFrame frame = ref frames[frameCount - 1];
        int level = boundCount + frameCount - 1;
        byte column = DescentPositionAt(level);
        uint rawValue = value.Encoded;

        BlockPackedColumnReader values = ValueReaderAt(level);
        ReadOnlySpan<EncodedTriple> added = index.AddedAt(permutationIndex);
        ReadOnlySpan<EncodedTriple> removed = index.RemovedAt(permutationIndex);

        int found = values.LowerBound(frame.BaseLo, frame.BaseHi, rawValue);
        bool baseFound = found < frame.BaseHi && values.ValueAt(found) == rawValue;

        int addChildLo = ColumnarSearch.LowerBoundByColumn(added, frame.AddLo, frame.AddHi, column, rawValue);
        int addChildHi = ColumnarSearch.UpperBoundByColumn(added, addChildLo, frame.AddHi, column, rawValue);
        int remChildLo = ColumnarSearch.LowerBoundByColumn(removed, frame.RemLo, frame.RemHi, column, rawValue);
        int remChildHi = ColumnarSearch.UpperBoundByColumn(removed, remChildLo, frame.RemHi, column, rawValue);

        int baseTriples = baseFound ? TriplesUnder(level, found) : 0;
        int net = baseTriples - (remChildHi - remChildLo) + (addChildHi - addChildLo);

        if(net <= 0)
        {
            return false;
        }

        openedValues[DescendedLevels] = rawValue;

        if(level == 2)
        {
            //Leaf open: no deeper level to push a frame for.
            DescendedLevels++;

            return true;
        }

        int childBaseLo = 0;
        int childBaseHi = 0;

        if(baseFound)
        {
            BlockPackedColumnReader offsets = OffsetReaderAt(level);
            childBaseLo = (int)offsets.ValueAt(found);
            childBaseHi = (int)offsets.ValueAt(found + 1);
        }

        frames[frameCount] = new LevelFrame
        {
            BaseLo = childBaseLo,
            BaseHi = childBaseHi,
            BasePos = childBaseLo,
            AddLo = addChildLo,
            AddHi = addChildHi,
            AddPos = addChildLo,
            RemLo = remChildLo,
            RemHi = remChildHi,
        };
        frameCount++;
        DescendedLevels++;

        Normalize(ref frames[frameCount - 1], level + 1);

        return true;
    }

    /// <summary>
    /// Rewinds one variable level. The variable at the level the
    /// iterator just exited becomes <see cref="CurrentVariable"/>
    /// again; the cursor at the restored level is exactly where it
    /// was when <see cref="Open"/> was called.
    /// </summary>
    /// <exception cref="InvalidOperationException">No variable has been bound; nothing to rewind.</exception>
    public void Up()
    {
        if(DescendedLevels == 0)
        {
            throw new InvalidOperationException("No variables have been bound; cannot Up.");
        }

        if(frameCount > DescendedLevels)
        {
            frameCount--;
        }

        DescendedLevels--;
    }

    /// <summary>
    /// Re-positions the current variable level at its first key
    /// without changing <see cref="DescendedLevels"/> or any deeper
    /// bound value. The worst-case-optimal join driver uses this to
    /// re-enumerate an independent variable when another variable
    /// this iterator does not share has just been re-bound.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no frame to rebuild, or all variables are bound.</exception>
    public void RestartCurrentLevel()
    {
        if(frameCount == 0)
        {
            throw new InvalidOperationException("Cannot restart the current level: the iterator has no descent frame to rebuild.");
        }

        if(DescendedLevels >= VariableOrder.Count)
        {
            throw new InvalidOperationException("Cannot restart the current level: all variables are bound, so there is no current level.");
        }

        ref LevelFrame frame = ref frames[frameCount - 1];
        frame.BasePos = frame.BaseLo;
        frame.AddPos = frame.AddLo;

        Normalize(ref frame, boundCount + frameCount - 1);
    }

    /// <summary>
    /// Advances the cursor at the current level to the next key.
    /// </summary>
    public void Next()
    {
        if(AtEnd)
        {
            return;
        }

        ref LevelFrame frame = ref frames[frameCount - 1];
        int level = boundCount + frameCount - 1;
        uint key = CurrentKeyOf(in frame, level);

        if(frame.BasePos < frame.BaseHi && ValueReaderAt(level).ValueAt(frame.BasePos) == key)
        {
            frame.BasePos++;
        }

        if(frame.AddPos < frame.AddHi)
        {
            ReadOnlySpan<EncodedTriple> added = index.AddedAt(permutationIndex);
            byte column = DescentPositionAt(level);

            if(ColumnarSearch.ColumnAt(in added[frame.AddPos], column) == key)
            {
                frame.AddPos = ColumnarSearch.UpperBoundByColumn(added, frame.AddPos, frame.AddHi, column, key);
            }
        }

        Normalize(ref frame, level);
    }

    /// <summary>
    /// Advances the cursor at the current level to the first key
    /// greater than or equal to <paramref name="target"/>. Never
    /// moves the cursor backwards.
    /// </summary>
    /// <param name="target">The seek target.</param>
    public void Seek(TermId target)
    {
        if(AtEnd)
        {
            return;
        }

        ref LevelFrame frame = ref frames[frameCount - 1];
        int level = boundCount + frameCount - 1;

        frame.BasePos = ValueReaderAt(level).LowerBound(frame.BasePos, frame.BaseHi, target.Encoded);

        if(frame.AddPos < frame.AddHi)
        {
            frame.AddPos = ColumnarSearch.LowerBoundByColumn(
                index.AddedAt(permutationIndex), frame.AddPos, frame.AddHi, DescentPositionAt(level), target.Encoded);
        }

        Normalize(ref frame, level);
    }

    //The smaller of the base and added candidates at the frame's
    //current positions. At least one side must be live.
    private uint CurrentKeyOf(in LevelFrame frame, int level)
    {
        uint baseKey = frame.BasePos < frame.BaseHi
            ? ValueReaderAt(level).ValueAt(frame.BasePos)
            : uint.MaxValue;
        uint addKey = frame.AddPos < frame.AddHi
            ? ColumnarSearch.ColumnAt(in index.AddedAt(permutationIndex)[frame.AddPos], DescentPositionAt(level))
            : uint.MaxValue;

        return Math.Min(baseKey, addKey);
    }

    //Skips base-only keys whose subtrees are fully tombstoned, so
    //the cursor rests on a visible key or at end. An added-side
    //candidate is always visible (additions are disjoint from the
    //base, so nothing tombstones them), and a key present on both
    //sides is visible through its addition.
    private void Normalize(ref LevelFrame frame, int level)
    {
        if(frame.RemLo == frame.RemHi)
        {
            return;
        }

        BlockPackedColumnReader values = ValueReaderAt(level);
        ReadOnlySpan<EncodedTriple> added = index.AddedAt(permutationIndex);
        ReadOnlySpan<EncodedTriple> removed = index.RemovedAt(permutationIndex);
        byte column = DescentPositionAt(level);

        while(frame.BasePos < frame.BaseHi)
        {
            uint baseKey = values.ValueAt(frame.BasePos);

            if(frame.AddPos < frame.AddHi
                && ColumnarSearch.ColumnAt(in added[frame.AddPos], column) <= baseKey)
            {
                //The current candidate is an addition (or a key
                //present on both sides) — visible either way.
                return;
            }

            int removedLo = ColumnarSearch.LowerBoundByColumn(removed, frame.RemLo, frame.RemHi, column, baseKey);
            int removedHi = ColumnarSearch.UpperBoundByColumn(removed, removedLo, frame.RemHi, column, baseKey);
            int removedCount = removedHi - removedLo;

            if(removedCount == 0 || TriplesUnder(level, frame.BasePos) > removedCount)
            {
                return;
            }

            //Fully tombstoned base key — skip it.
            frame.BasePos++;
        }
    }

    //The number of base triples under the key at `absoluteIndex`
    //of the given level's value column.
    private int TriplesUnder(int level, int absoluteIndex)
    {
        if(level == 2)
        {
            return 1;
        }

        if(level == 1)
        {
            BlockPackedColumnReader offsets = OffsetReaderAt(1);

            return (int)(offsets.ValueAt(absoluteIndex + 1) - offsets.ValueAt(absoluteIndex));
        }

        BlockPackedColumnReader level0Offsets = OffsetReaderAt(0);
        BlockPackedColumnReader level1Offsets = OffsetReaderAt(1);
        int childLo = (int)level0Offsets.ValueAt(absoluteIndex);
        int childHi = (int)level0Offsets.ValueAt(absoluteIndex + 1);

        return (int)(level1Offsets.ValueAt(childHi) - level1Offsets.ValueAt(childLo));
    }

    /// <summary>Looks up or creates the reader over the given level's value column.</summary>
    /// <param name="level">The descent level; 0, 1, or 2.</param>
    /// <returns>The reader.</returns>
    private BlockPackedColumnReader ValueReaderAt(int level)
    {
        return valueReaders[level] ??= new BlockPackedColumnReader(order.ValuesColumnAt(level));
    }

    /// <summary>Looks up or creates the reader over the given level's offset column.</summary>
    /// <param name="level">The descent level; 0 or 1.</param>
    /// <returns>The reader.</returns>
    private BlockPackedColumnReader OffsetReaderAt(int level)
    {
        return offsetReaders[level] ??= new BlockPackedColumnReader(order.OffsetsColumnAt(level));
    }

    //The RDF position the given CSR level orders by.
    private byte DescentPositionAt(int level)
    {
        return level switch
        {
            0 => descentPosition0,
            1 => descentPosition1,
            2 => descentPosition2,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be 0, 1, or 2."),
        };
    }

    //Returns the RDF position (0=S, 1=P, 2=O) `variable` occupies
    //in `pattern`.
    private static int PositionOfVariable(TriplePattern pattern, Variable variable)
    {
        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            PatternPosition slot = pattern.At(rdfPosition);

            if(slot.IsVariable && slot.Variable == variable)
            {
                return rdfPosition;
            }
        }

        throw new ArgumentException($"Variable id {variable.Id} does not appear in the pattern.", nameof(variable));
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
}
