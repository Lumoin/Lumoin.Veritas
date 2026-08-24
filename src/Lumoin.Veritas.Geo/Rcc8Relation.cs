namespace Lumoin.Veritas.Geo;

/// <summary>
/// The eight base relations of the region connection calculus over regular closed regions. The relations
/// are jointly exhaustive and pairwise disjoint: exactly one holds between any two regions, which is why a
/// disjunction of them is not assertable as a single triple. Each member's numeric value is its bit index
/// in <see cref="Rcc8RelationSet"/>.
/// </summary>
public enum Rcc8Relation
{
    /// <summary>Disconnected: the regions' closures share no point.</summary>
    Dc = 0,

    /// <summary>Externally connected: the closures share boundary points but no interior point.</summary>
    Ec = 1,

    /// <summary>Partially overlapping: the interiors intersect and neither region is a part of the other.</summary>
    Po = 2,

    /// <summary>Tangential proper part: the first is a proper part of the second and touches its boundary.</summary>
    Tpp = 3,

    /// <summary>Non-tangential proper part: the first's closure lies within the second's interior.</summary>
    Ntpp = 4,

    /// <summary>Tangential proper part inverse: the second is a tangential proper part of the first.</summary>
    Tppi = 5,

    /// <summary>Non-tangential proper part inverse: the second is a non-tangential proper part of the first.</summary>
    Ntppi = 6,

    /// <summary>Equal: the regions are the same point set.</summary>
    Eq = 7
}
