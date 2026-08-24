namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// The build configuration of a <see cref="PackedBoxIndex"/>: the packing
/// family, the node capacity (children per node, sanctioned range
/// [2, 65536]), and the dominance materialization carriage. Validated by
/// <see cref="PackedBoxIndex.Create(PackedBoxIndexOptions)"/> — an
/// undefined packing, an out-of-range capacity, or an undefined
/// materialization mode throws there, and so does
/// <c>default(PackedBoxIndexOptions)</c>, whose zero capacity is the
/// record-struct default trap; <see cref="Default"/> is the sanctioned
/// default.
/// </summary>
/// <remarks>
/// <para>
/// <b>The measured default.</b> <see cref="Default"/> carries the values a
/// measurement matrix picked on consumer-shaped workloads:
/// <see cref="BoxIndexPacking.HilbertCurve"/> at capacity 32 — the best
/// query aggregate over the primary dataset-scale cells, with every
/// configuration answering identical candidate sets. Small-count
/// rebuild-per-query consumers measured faster on
/// <see cref="BoxIndexPacking.SortTileRecursive"/>, whose build cost is
/// lower — both stay selectable.
/// </para>
/// <para>
/// <b>What the choice can and cannot change.</b> Candidate sets are
/// invariant across packings and capacities — the ordering pass is the
/// engine's only degree of freedom — so switching options never changes
/// which items a query yields, only clustering quality and traversal cost.
/// Enumeration order is contractual per (packing, capacity) over the same
/// item sequence and differs between configurations.
/// </para>
/// <para>
/// <b>The dominance carriage.</b> <paramref name="DominanceMaterialization"/>
/// selects WHEN the containing mode's dominance structure materializes,
/// never WHAT it answers: both carriages run the identical pass and produce
/// the identical structure, so results and enumeration order are
/// carriage-invariant. The default,
/// <see cref="DominanceMaterializationMode.DeferredToFirstUse"/>, keeps
/// builds in the plain packed-tree cost class and defers the one-time
/// dominance cost to the first containing use of each built epoch;
/// <see cref="DominanceMaterializationMode.EagerAtBuild"/> moves the same
/// one-time cost to the build tail for consumers whose first containing
/// query's latency outranks build cost.
/// </para>
/// </remarks>
/// <param name="Packing">The packing family ordering every level of the build.</param>
/// <param name="NodeCapacity">Children per node, in [2, 65536]; the leaf fan-out and the internal fan-out alike.</param>
/// <param name="DominanceMaterialization">The dominance carriage: when the containing mode's structure materializes, deferred to first use by default.</param>
public readonly record struct PackedBoxIndexOptions(BoxIndexPacking Packing, int NodeCapacity, DominanceMaterializationMode DominanceMaterialization = DominanceMaterializationMode.DeferredToFirstUse)
{
    /// <summary>
    /// The sanctioned default configuration — the measured-default carrier
    /// (see the type remarks for what the measurement decided).
    /// </summary>
    public static PackedBoxIndexOptions Default { get; } = new(BoxIndexPacking.HilbertCurve, 32);
}
