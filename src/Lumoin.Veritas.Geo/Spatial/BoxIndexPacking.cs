namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// The bulk-load packing family a <see cref="PackedBoxIndex"/> orders its
/// entries by. Both packings share one engine, one layout, and one query
/// path — the ordering pass is the only degree of freedom — so candidate
/// sets are identical across packings by construction; what differs is
/// clustering quality, and with it traversal cost, on a given workload.
/// The zero member names the shipped default, picked by measurement on
/// consumer-shaped workloads; the published evidence says neither family
/// dominates, which is why both stay selectable.
/// </summary>
public enum BoxIndexPacking
{
    /// <summary>
    /// Hilbert-curve packing: per level, entries sort by the Hilbert distance
    /// of their centers on a grid normalized to the level's own center
    /// extent, preserving spatial locality along the curve. The measured
    /// dataset-scale query winner, hence the zero member.
    /// </summary>
    HilbertCurve = 0,

    /// <summary>
    /// Sort-Tile-Recursive: per level, entries sort by center X, partition
    /// into vertical slices, and sort by center Y within each slice, packing
    /// consecutive runs into nodes — at most one partial node per level. The
    /// measured build-cost winner, which also carries the small-count
    /// rebuild-per-query cadence.
    /// </summary>
    SortTileRecursive = 1
}
