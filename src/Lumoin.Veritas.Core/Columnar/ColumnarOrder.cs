using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One permutation's three-level compressed-sparse column set
/// inside a <see cref="ColumnarTripleIndex"/>: distinct sorted
/// values per level, with offset columns demarcating each parent
/// value's group in the next level.
/// </summary>
/// <remarks>
/// <para>
/// Offsets are exclusive-end: the children of the level-0 value at
/// index <c>i</c> occupy level-1 indices
/// <c>[Level0Offsets[i], Level0Offsets[i + 1])</c>, and likewise
/// from level 1 to level 2. The offset columns carry one more
/// entry than their value columns; the final entry equals the next
/// level's length.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarOrder L0={level0Values.Length} L1={level1Values.Length} L2={level2Values.Length}")]
public sealed class ColumnarOrder
{
    private readonly uint[] level0Values;

    private readonly uint[] level0Offsets;

    private readonly uint[] level1Values;

    private readonly uint[] level1Offsets;

    private readonly uint[] level2Values;

    private ColumnarOrder(
        uint[] level0Values,
        uint[] level0Offsets,
        uint[] level1Values,
        uint[] level1Offsets,
        uint[] level2Values)
    {
        this.level0Values = level0Values;
        this.level0Offsets = level0Offsets;
        this.level1Values = level1Values;
        this.level1Offsets = level1Offsets;
        this.level2Values = level2Values;
    }

    /// <summary>The distinct first-position values, ascending.</summary>
    public ReadOnlySpan<uint> Level0Values => level0Values;

    /// <summary>Exclusive-end offsets from level 0 into <see cref="Level1Values"/>; one entry more than <see cref="Level0Values"/>.</summary>
    public ReadOnlySpan<uint> Level0Offsets => level0Offsets;

    /// <summary>The second-position values, ascending within each level-0 group.</summary>
    public ReadOnlySpan<uint> Level1Values => level1Values;

    /// <summary>Exclusive-end offsets from level 1 into <see cref="Level2Values"/>; one entry more than <see cref="Level1Values"/>.</summary>
    public ReadOnlySpan<uint> Level1Offsets => level1Offsets;

    /// <summary>The third-position values, ascending within each level-1 group.</summary>
    public ReadOnlySpan<uint> Level2Values => level2Values;

    /// <summary>
    /// Returns the value column at the given descent level.
    /// </summary>
    /// <param name="level">The descent level; 0, 1, or 2.</param>
    /// <returns>The level's value column.</returns>
    public ReadOnlySpan<uint> ValuesAt(int level)
    {
        return level switch
        {
            0 => level0Values,
            1 => level1Values,
            2 => level2Values,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be 0, 1, or 2."),
        };
    }

    /// <summary>
    /// Returns the offset column from the given descent level into
    /// the next level. Levels 0 and 1 carry offsets; level 2 is the
    /// deepest.
    /// </summary>
    /// <param name="level">The descent level; 0 or 1.</param>
    /// <returns>The level's exclusive-end offset column.</returns>
    public ReadOnlySpan<uint> OffsetsAt(int level)
    {
        return level switch
        {
            0 => level0Offsets,
            1 => level1Offsets,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Only levels 0 and 1 carry offsets."),
        };
    }

    //Builds one permutation's column set: sorts the working array
    //by its packed 96-bit permutation keys, then emits all five
    //columns in a single pass over the sorted run.
    internal static ColumnarOrder Build(EncodedTriple[] working, byte[] permutation)
    {
        byte position0 = permutation[0];
        byte position1 = permutation[1];
        byte position2 = permutation[2];

        ColumnarSearch.SortByPermutation(working, position0, position1, position2);

        List<uint> values0 = [];
        List<uint> offsets0 = [];
        List<uint> values1 = [];
        List<uint> offsets1 = [];
        List<uint> values2 = new(working.Length);

        for(int i = 0; i < working.Length; i++)
        {
            uint key0 = KeyAt(working[i], position0);
            uint key1 = KeyAt(working[i], position1);
            uint key2 = KeyAt(working[i], position2);

            bool newGroup0 = values0.Count == 0 || values0[^1] != key0;

            if(newGroup0)
            {
                values0.Add(key0);
                offsets0.Add((uint)values1.Count);
            }

            if(newGroup0 || values1[^1] != key1)
            {
                values1.Add(key1);
                offsets1.Add((uint)values2.Count);
            }

            values2.Add(key2);
        }

        offsets0.Add((uint)values1.Count);
        offsets1.Add((uint)values2.Count);

        return new ColumnarOrder([.. values0], [.. offsets0], [.. values1], [.. offsets1], [.. values2]);
    }

    private static uint KeyAt(in EncodedTriple triple, byte position)
    {
        return position switch
        {
            0 => triple.Subject.Encoded,
            1 => triple.Predicate.Encoded,
            2 => triple.Object.Encoded,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be 0 (subject), 1 (predicate), or 2 (object)."),
        };
    }
}
