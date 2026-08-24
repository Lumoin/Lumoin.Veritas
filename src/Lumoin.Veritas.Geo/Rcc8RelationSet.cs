using System.Numerics;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// A set of <see cref="Rcc8Relation"/> members as one bit per relation — the value a composition-table
/// cell holds. Because the base relations are jointly exhaustive and pairwise disjoint, a multi-member set
/// is a disjunction of mutually exclusive possibilities: knowledge, but not an assertable relation.
/// </summary>
/// <param name="Bits">The membership bits, indexed by each relation's numeric value.</param>
public readonly record struct Rcc8RelationSet(byte Bits)
{
    /// <summary>The empty set.</summary>
    public static Rcc8RelationSet Empty { get; }

    /// <summary>The full set of all eight base relations — the no-information composition outcome.</summary>
    public static Rcc8RelationSet All { get; } = new(0xFF);

    /// <summary>The number of member relations.</summary>
    public int Count => BitOperations.PopCount(Bits);

    /// <summary>Whether the set contains a relation.</summary>
    /// <param name="relation">The relation to test.</param>
    /// <returns><see langword="true"/> when the relation is a member.</returns>
    public bool Contains(Rcc8Relation relation)
    {
        return (Bits & (byte)(1 << (int)relation)) != 0;
    }

    /// <summary>Returns the set extended with one relation.</summary>
    /// <param name="relation">The relation to add.</param>
    /// <returns>The extended set.</returns>
    public Rcc8RelationSet With(Rcc8Relation relation)
    {
        return new Rcc8RelationSet((byte)(Bits | (byte)(1 << (int)relation)));
    }

    /// <summary>
    /// Reads the set's sole member, when it has exactly one. The singleton case is what the composition
    /// closure materializes: only a one-member outcome names a base relation outright.
    /// </summary>
    /// <param name="relation">The sole member on success.</param>
    /// <returns><see langword="true"/> when the set holds exactly one relation.</returns>
    public bool TryGetSingleton(out Rcc8Relation relation)
    {
        if(BitOperations.PopCount(Bits) == 1)
        {
            relation = (Rcc8Relation)BitOperations.TrailingZeroCount(Bits);

            return true;
        }

        relation = default;

        return false;
    }
}
