namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Which permutation set a <see cref="ColumnarTripleIndex"/>
/// materialises — the memory/coverage trade of the order count.
/// </summary>
/// <remarks>
/// <para>
/// Three rotations cover every BOUND-PREFIX shape (any subset of
/// positions bound), but they fix each pattern's variable tail to
/// the rotation's order. A multi-pattern join needs one global
/// variable order whose per-pattern restriction matches an
/// available rotation everywhere
/// (<see cref="ColumnarRotationPlanner"/>); CYCLIC shapes — the
/// triangle among them — have contradictory per-pattern
/// constraints under the three rotations and are not answerable,
/// falling back to the system of record. Structures that serve all
/// six orders at three orders' cost do so with rank/select
/// machinery, not flat CSR columns; that is the recorded
/// alternative if rotation coverage ever has to widen without the
/// memory doubling.
/// </para>
/// </remarks>
public enum ColumnarOrderSetMode
{
    /// <summary>All six permutations: every (bound set, variable order) combination is answerable. The default.</summary>
    AllSixOrders,

    /// <summary>The three cyclic rotations (SPO, POS, OSP): roughly half the memory, full bound-prefix coverage, rotation-compatible joins only — the opt-in profile for memory-extreme deployments, with rendezvous fallback for the rest.</summary>
    ThreeRotations,
}
