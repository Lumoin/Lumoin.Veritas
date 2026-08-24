using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A stateful single-pattern iterator over a <see cref="TripleSelfIndex"/>,
/// presenting the same level contract as the columnar triejoin iterator:
/// bound positions are applied at construction, successive variable levels are
/// walked in ascending key order, and <see cref="Open"/> / <see cref="Up"/> /
/// <see cref="Next"/> / <see cref="Seek"/> drive the descent. Because the
/// self-index answers every rotation from one structure, ANY variable order is
/// servable — there is no permutation to select and no rotation-compatibility
/// constraint on the order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Level steps.</b> Each level binds one more triple position. The step
/// kind is fixed by the pattern's bound-set evolution: an empty bound set
/// binds a rotation's leader block; with one position bound, the cyclically
/// preceding position binds backward and the following position binds forward
/// through the following rotation's column; with two bound, the remaining
/// position always binds backward. The matching seeks enumerate each level's
/// candidates in ascending key order.
/// </para>
/// <para>
/// <b>Self-joins.</b> A pattern with the same variable in multiple positions
/// is rejected at construction, matching the columnar iterator's contract.
/// </para>
/// <para>
/// <b>Thread safety.</b> An iterator is single-threaded; concurrent iterators
/// over the same immutable index are safe.
/// </para>
/// </remarks>
[DebuggerDisplay("SelfIndexTriejoinIterator Depth={DescendedLevels} AtEnd={AtEnd}")]
public sealed class SelfIndexTriejoinIterator
{
    /// <summary>How a variable level binds its position given the positions bound above it.</summary>
    private enum LevelStep : byte
    {
        /// <summary>Nothing bound yet: bind a rotation's leader block.</summary>
        First,

        /// <summary>The position cyclically precedes the bound prefix: a backward search step.</summary>
        Preceding,

        /// <summary>The position follows the single bound leader: a forward step within its block.</summary>
        Following,
    }

    private readonly TripleSelfIndex index;

    //The triple position (0=S, 1=P, 2=O) each variable level binds.
    private readonly byte[] variablePositions;

    //The step kind per variable level, fixed by the bound-set evolution.
    private readonly LevelStep[] levelSteps;

    //Per level: the range with the constants and the shallower variables
    //bound — entry 0 is the post-constant frontier, entry ℓ+1 the result of
    //opening level ℓ.
    private readonly SelfIndexRange[] contextRanges;

    //Per level: the cursor's current candidate key and its exhaustion flag.
    private readonly uint[] candidates;

    private readonly bool[] exhausted;

    //One per descended level: the value the iterator was opened to.
    private readonly uint[] openedValues;

    //The forward step's leader symbol when the single bound position is a
    //constant; when the leader was bound by level 0 instead, the opened value
    //serves.
    private readonly uint constantLeader;

    private readonly bool leaderIsConstant;

    //True only when the construction-phase constant binding found no match.
    private readonly bool constructionAtEnd;

    /// <summary>The pattern this iterator is matching.</summary>
    public TriplePattern Pattern { get; }

    /// <summary>The variable elimination order this iterator was constructed with.</summary>
    public IReadOnlyList<Variable> VariableOrder { get; }

    /// <summary>The number of variable levels currently descended: 0 before any variable binds, <c>VariableOrder.Count</c> when every variable has been opened.</summary>
    public int DescendedLevels { get; private set; }

    /// <summary>
    /// <c>true</c> when no further keys are available at the current level, or
    /// the construction-time constant binding found no match.
    /// </summary>
    public bool AtEnd
    {
        get
        {
            if(constructionAtEnd || VariableOrder.Count == 0)
            {
                return constructionAtEnd;
            }

            return exhausted[Math.Min(DescendedLevels, VariableOrder.Count - 1)];
        }
    }

    /// <summary>The variable at the current level — the next variable the consumer would bind via <see cref="Open"/>.</summary>
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

    /// <summary>The current key at the current level. Undefined when <see cref="AtEnd"/>.</summary>
    /// <exception cref="InvalidOperationException">The iterator is at end.</exception>
    public TermId Key
    {
        get
        {
            if(AtEnd)
            {
                throw new InvalidOperationException("Iterator is at end; Key is undefined.");
            }

            return TermId.FromEncoded(candidates[Math.Min(DescendedLevels, VariableOrder.Count - 1)]);
        }
    }

    /// <summary>Constructs a new iterator over <paramref name="index"/> for <paramref name="pattern"/>, descending in <paramref name="variableOrder"/> — any order; the self-index serves them all.</summary>
    /// <param name="index">The index to iterate.</param>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="variableOrder">The order in which the iterator's variable levels should be presented; must contain exactly the distinct variables of <paramref name="pattern"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="variableOrder"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The pattern contains a self-join, or <paramref name="variableOrder"/> is not a permutation of the pattern's variables.</exception>
    public SelfIndexTriejoinIterator(
        TripleSelfIndex index,
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

        int variableCount = variableOrder.Count;
        variablePositions = new byte[variableCount];
        levelSteps = new LevelStep[variableCount];
        contextRanges = new SelfIndexRange[variableCount + 1];
        candidates = new uint[variableCount];
        exhausted = new bool[variableCount];
        openedValues = new uint[variableCount];

        int boundMask = 0;
        int boundCount = 0;
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsBound)
            {
                boundMask |= 1 << position;
                boundCount++;
            }
        }

        for(int level = 0; level < variableCount; level++)
        {
            variablePositions[level] = (byte)PositionOfVariable(pattern, variableOrder[level]);
        }

        //Fix each level's step kind from the bound-set evolution: constants
        //first, then the variables in presentation order.
        int mask = boundMask;
        for(int level = 0; level < variableCount; level++)
        {
            int position = variablePositions[level];
            int boundSoFar = BitOperations.PopCount((uint)mask);
            levelSteps[level] = boundSoFar switch
            {
                0 => LevelStep.First,
                1 => position == PrecedingPosition(BitOperations.TrailingZeroCount((uint)mask)) ? LevelStep.Preceding : LevelStep.Following,
                _ => LevelStep.Preceding,
            };

            mask |= 1 << position;
        }

        //Bind the constants: a single constant is its rotation's leader
        //block; two bind the one whose cyclic predecessor is the other, then
        //prepend that predecessor; three chain a further backward step.
        (SelfIndexRange frontier, constantLeader, leaderIsConstant) = boundCount switch
        {
            0 => (index.FullRange(variableCount > 0 ? LeaderRotation(variablePositions[0]) : SelfIndexRotation.SubjectPredicateObject), 0u, false),
            1 => BindSingleConstant(index, pattern, boundMask),
            2 => (BindConstantPair(index, pattern, boundMask), 0u, false),
            _ => (BindAllConstants(index, pattern), 0u, false),
        };

        contextRanges[0] = frontier;
        constructionAtEnd = frontier.IsEmpty;

        if(!constructionAtEnd && variableCount > 0)
        {
            PositionLevel(0, 0);
        }
    }

    /// <summary>Binds a single bound position as its rotation's leader block, recording it as the forward step's constant leader.</summary>
    /// <param name="index">The self-index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="boundMask">The bound-position mask with exactly one bit set.</param>
    /// <returns>The leader block, the leader symbol, and the constant-leader flag.</returns>
    private static (SelfIndexRange Frontier, uint ConstantLeader, bool LeaderIsConstant) BindSingleConstant(TripleSelfIndex index, TriplePattern pattern, int boundMask)
    {
        int position = BitOperations.TrailingZeroCount((uint)boundMask);
        TermId term = pattern.At(position).BoundTerm;

        return (index.BindFirst(LeaderRotation(position), term), term.Encoded, true);
    }

    /// <summary>Binds two bound positions: the one whose cyclic predecessor is the other anchors a leader block, then the predecessor prepends backward.</summary>
    /// <param name="index">The self-index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="boundMask">The bound-position mask with exactly two bits set.</param>
    /// <returns>The two-bound range.</returns>
    private static SelfIndexRange BindConstantPair(TripleSelfIndex index, TriplePattern pattern, int boundMask)
    {
        int anchor = 0;
        for(int position = 0; position < 3; position++)
        {
            if((boundMask & (1 << position)) != 0 && (boundMask & (1 << PrecedingPosition(position))) != 0)
            {
                anchor = position;

                break;
            }
        }

        SelfIndexRange frontier = index.BindFirst(LeaderRotation(anchor), pattern.At(anchor).BoundTerm);

        return index.BindPreceding(frontier, pattern.At(PrecedingPosition(anchor)).BoundTerm);
    }

    /// <summary>Binds all three positions: the predicate's leader block, then the subject and the object prepend backward — a membership check.</summary>
    /// <param name="index">The self-index.</param>
    /// <param name="pattern">The fully bound pattern.</param>
    /// <returns>The membership range.</returns>
    private static SelfIndexRange BindAllConstants(TripleSelfIndex index, TriplePattern pattern)
    {
        SelfIndexRange frontier = index.BindFirst(LeaderRotation(1), pattern.At(1).BoundTerm);
        frontier = index.BindPreceding(frontier, pattern.At(0).BoundTerm);

        return index.BindPreceding(frontier, pattern.At(2).BoundTerm);
    }

    /// <summary>Checks the order is a permutation of the pattern's distinct variables.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="variableOrder">The proposed order.</param>
    /// <exception cref="ArgumentException">The order does not match the pattern's variables.</exception>
    private static void ValidateVariableOrder(TriplePattern pattern, IReadOnlyList<Variable> variableOrder)
    {
        HashSet<Variable> patternVariables = [.. pattern.Variables()];
        if(patternVariables.Count != variableOrder.Count)
        {
            throw new ArgumentException("The variable order must contain exactly the pattern's distinct variables.", nameof(variableOrder));
        }

        foreach(Variable variable in variableOrder)
        {
            if(!patternVariables.Remove(variable))
            {
                throw new ArgumentException($"Variable id {variable.Id} is not a distinct variable of the pattern, or repeats in the order.", nameof(variableOrder));
            }
        }
    }

    /// <summary>The triple position a variable occupies in the pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="variable">The variable.</param>
    /// <returns>The position index.</returns>
    private static int PositionOfVariable(TriplePattern pattern, Variable variable)
    {
        for(int position = 0; position < 3; position++)
        {
            PatternPosition candidate = pattern.At(position);
            if(candidate.IsVariable && candidate.Variable == variable)
            {
                return position;
            }
        }

        return 0;
    }

    /// <summary>The position cyclically preceding another in the subject→predicate→object cycle.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The preceding position.</returns>
    private static int PrecedingPosition(int position) => position switch
    {
        0 => 2,
        1 => 0,
        _ => 1,
    };

    /// <summary>The rotation a position leads.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The rotation.</returns>
    private static SelfIndexRotation LeaderRotation(int position) => position switch
    {
        0 => SelfIndexRotation.SubjectPredicateObject,
        1 => SelfIndexRotation.PredicateObjectSubject,
        _ => SelfIndexRotation.ObjectSubjectPredicate,
    };

    /// <summary>The forward step's leader symbol at a level: the single bound constant, or the value level 0 opened.</summary>
    /// <returns>The leader symbol.</returns>
    private TermId LeaderSymbol()
    {
        return leaderIsConstant ? TermId.FromEncoded(constantLeader) : TermId.FromEncoded(openedValues[0]);
    }

    /// <summary>Positions a level's cursor at the smallest candidate key at or above the target, or marks it exhausted.</summary>
    /// <param name="level">The variable level.</param>
    /// <param name="target">The sought lower bound.</param>
    private void PositionLevel(int level, uint target)
    {
        bool found = levelSteps[level] switch
        {
            LevelStep.First => index.TrySeekFirst(LeaderRotation(variablePositions[level]), TermId.FromEncoded(target), out TermId first) && Capture(level, first),
            LevelStep.Preceding => index.TrySeekPreceding(contextRanges[level], TermId.FromEncoded(target), out TermId preceding) && Capture(level, preceding),
            _ => index.TrySeekFollowing(contextRanges[level], LeaderSymbol(), TermId.FromEncoded(target), out TermId following) && Capture(level, following),
        };

        exhausted[level] = !found;
    }

    /// <summary>Records a level's found candidate; always <see langword="true"/> so the seek expression can chain it.</summary>
    /// <param name="level">The level.</param>
    /// <param name="symbol">The candidate.</param>
    /// <returns><see langword="true"/>.</returns>
    private bool Capture(int level, TermId symbol)
    {
        candidates[level] = symbol.Encoded;

        return true;
    }

    /// <summary>The binding step for a level: the range with the level's value bound, empty when the value does not occur.</summary>
    /// <param name="level">The variable level.</param>
    /// <param name="value">The value to bind.</param>
    /// <returns>The narrowed range.</returns>
    private SelfIndexRange BindLevel(int level, TermId value) => levelSteps[level] switch
    {
        LevelStep.First => index.BindFirst(LeaderRotation(variablePositions[level]), value),
        LevelStep.Preceding => index.BindPreceding(contextRanges[level], value),
        _ => index.BindFollowing(contextRanges[level], LeaderSymbol(), value),
    };

    /// <summary>Returns the <see cref="TermId"/> previously bound to <paramref name="variable"/> by a successful <see cref="Open"/>.</summary>
    /// <param name="variable">A variable this iterator has already bound.</param>
    /// <returns>The bound key.</returns>
    /// <exception cref="ArgumentException"><paramref name="variable"/> has not yet been bound.</exception>
    public TermId ValueOf(Variable variable)
    {
        for(int level = 0; level < DescendedLevels; level++)
        {
            if(VariableOrder[level] == variable)
            {
                return TermId.FromEncoded(openedValues[level]);
            }
        }

        throw new ArgumentException($"Variable id {variable.Id} has not yet been bound by this iterator.", nameof(variable));
    }

    /// <summary>
    /// Descends to the next variable level by binding the current variable to
    /// <paramref name="value"/>. Returns <c>true</c> when the value occurs at
    /// this level; <c>false</c> when it does not, in which case the iterator's
    /// state is unchanged. The current level's cursor does not move either way.
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

        if(constructionAtEnd)
        {
            return false;
        }

        SelfIndexRange child = BindLevel(DescendedLevels, value);
        if(child.IsEmpty)
        {
            return false;
        }

        openedValues[DescendedLevels] = value.Encoded;
        contextRanges[DescendedLevels + 1] = child;
        DescendedLevels++;

        if(DescendedLevels < VariableOrder.Count)
        {
            PositionLevel(DescendedLevels, 0);
        }

        return true;
    }

    /// <summary>Rewinds one variable level; the restored level's cursor is exactly where it was when <see cref="Open"/> was called.</summary>
    /// <exception cref="InvalidOperationException">No variable has been bound; nothing to rewind.</exception>
    public void Up()
    {
        if(DescendedLevels == 0)
        {
            throw new InvalidOperationException("No variables have been bound; cannot Up.");
        }

        DescendedLevels--;
    }

    /// <summary>Re-positions the current variable level at its first key without changing <see cref="DescendedLevels"/> or any deeper bound value.</summary>
    /// <exception cref="InvalidOperationException">All variables are bound, so there is no current level.</exception>
    public void RestartCurrentLevel()
    {
        if(DescendedLevels >= VariableOrder.Count)
        {
            throw new InvalidOperationException("Cannot restart the current level: all variables are bound, so there is no current level.");
        }

        if(constructionAtEnd)
        {
            return;
        }

        PositionLevel(DescendedLevels, 0);
    }

    /// <summary>Advances the cursor at the current level to the next key.</summary>
    public void Next()
    {
        if(AtEnd)
        {
            return;
        }

        int level = Math.Min(DescendedLevels, VariableOrder.Count - 1);
        uint current = candidates[level];
        if(current == uint.MaxValue)
        {
            exhausted[level] = true;

            return;
        }

        PositionLevel(level, current + 1);
    }

    /// <summary>Advances the cursor at the current level to the first key greater than or equal to <paramref name="target"/>. Never moves the cursor backwards.</summary>
    /// <param name="target">The seek target.</param>
    public void Seek(TermId target)
    {
        if(AtEnd)
        {
            return;
        }

        int level = Math.Min(DescendedLevels, VariableOrder.Count - 1);
        if(target.Encoded <= candidates[level])
        {
            return;
        }

        PositionLevel(level, target.Encoded);
    }
}
