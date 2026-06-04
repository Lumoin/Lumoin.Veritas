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
/// <b>Level state.</b> A level's candidate set is one contiguous
/// slice <c>[lo, hi)</c> of the permutation's value column at that
/// depth, derived from the parent level's offsets. The per-level
/// frame is three integers; descent is offset arithmetic into
/// contiguous arrays, with no node objects between levels.
/// </para>
/// <para>
/// <b>Self-joins.</b> A pattern with the same variable in multiple
/// positions is rejected at construction, matching the hypertrie
/// iterator's contract.
/// </para>
/// <para>
/// <b>Thread safety.</b> An iterator is single-threaded. Multiple
/// iterators over the same index can run concurrently because the
/// index is immutable.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarTriejoinIterator Depth={DescendedLevels} AtEnd={AtEnd}")]
public sealed class ColumnarTriejoinIterator
{
    //One frame per presented level: the level's slice bounds in
    //the value column and the cursor position within them.
    private struct LevelFrame
    {
        /// <summary>The slice's inclusive start index in the level's value column.</summary>
        public int Lo;

        /// <summary>The slice's exclusive end index in the level's value column.</summary>
        public int Hi;

        /// <summary>The cursor's current absolute index; in <c>[Lo, Hi]</c>, where <c>Hi</c> means exhausted.</summary>
        public int Position;
    }

    private readonly ColumnarOrder order;

    //The number of bound positions applied at construction; the
    //CSR level for variable level v is boundCount + v.
    private readonly int boundCount;

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
    //match. Sticky.
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
    /// current level — the cursor at the deepest frame is past its
    /// last key, or the construction-time bound descent found no
    /// match.
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

            return frame.Position >= frame.Hi;
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
    /// The current key at the current level. Undefined when
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

            return TermId.FromEncoded(order.ValuesAt(boundCount + frameCount - 1)[frame.Position]);
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

        Pattern = pattern;
        VariableOrder = variableOrder;
        openedValues = new uint[variableOrder.Count];
        frames = new LevelFrame[3];

        //Compose the descent sequence: bound positions in S/P/O
        //order, then the variable positions in level order.
        Span<byte> descent = stackalloc byte[3];
        int descentLength = 0;

        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            if(pattern.At(rdfPosition).IsBound)
            {
                descent[descentLength++] = (byte)rdfPosition;
            }
        }

        boundCount = descentLength;

        for(int level = 0; level < variableOrder.Count; level++)
        {
            descent[descentLength++] = (byte)PositionOfVariable(pattern, variableOrder[level]);
        }

        order = index.OrderAt(ColumnarTripleIndex.PermutationIndexFor(descent[..descentLength]));

        //Apply the bound prefix: narrow the slice level by level via
        //exact lookups. A miss leaves the iterator at end.
        int lo = 0;
        int hi = order.ValuesAt(0).Length;

        for(int level = 0; level < boundCount; level++)
        {
            uint boundValue = pattern.At(descent[level]).BoundTerm.Encoded;
            int found = SearchAtLeast(order.ValuesAt(level), lo, hi, boundValue);

            if(found >= hi || order.ValuesAt(level)[found] != boundValue)
            {
                constructionAtEnd = true;

                return;
            }

            if(level < 2)
            {
                ReadOnlySpan<uint> offsets = order.OffsetsAt(level);
                lo = (int)offsets[found];
                hi = (int)offsets[found + 1];
            }
            else
            {
                //Fully bound pattern verified; no variable levels.
                Debug.Assert(variableOrder.Count == 0, "A pattern whose bound prefix consumes all three levels has no variables.");

                return;
            }
        }

        if(variableOrder.Count == 0)
        {
            return;
        }

        frames[0] = new LevelFrame { Lo = lo, Hi = hi, Position = lo };
        frameCount = 1;
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
    /// when the value exists at this level; <c>false</c> when it
    /// does not, in which case the iterator's state is unchanged.
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
        ReadOnlySpan<uint> values = order.ValuesAt(level);
        uint rawValue = value.Encoded;

        int found = SearchAtLeast(values, frame.Lo, frame.Hi, rawValue);

        if(found >= frame.Hi || values[found] != rawValue)
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

        ReadOnlySpan<uint> offsets = order.OffsetsAt(level);
        int childLo = (int)offsets[found];
        int childHi = (int)offsets[found + 1];

        frames[frameCount] = new LevelFrame { Lo = childLo, Hi = childHi, Position = childLo };
        frameCount++;
        DescendedLevels++;

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

        frames[frameCount - 1].Position = frames[frameCount - 1].Lo;
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

        frames[frameCount - 1].Position++;
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
        ReadOnlySpan<uint> values = order.ValuesAt(boundCount + frameCount - 1);
        int found = SearchAtLeast(values, frame.Position, frame.Hi, target.Encoded);
        frame.Position = found;
    }

    //Returns the smallest index in [lo, hi) whose value is greater
    //than or equal to `target`, or `hi` when no such index exists.
    private static int SearchAtLeast(ReadOnlySpan<uint> values, int lo, int hi, uint target)
    {
        return ColumnarSearch.LowerBound(values, lo, hi, target);
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
