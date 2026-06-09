namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// How a <see cref="BlockPackedColumn"/> encodes its blocks — the
/// space/access trade per column KIND, chosen by the column's
/// access pattern.
/// </summary>
public enum BlockPackedColumnMode
{
    /// <summary>
    /// Zigzag successive deltas, bit-packed with patched exceptions,
    /// prefix-summed from the block's first value on decode. The
    /// densest encoding, but a single value is only reachable by
    /// decoding its whole block — right for columns read pointwise
    /// with strong locality (offset columns during descent).
    /// </summary>
    PrefixedDeltas,

    /// <summary>
    /// Frame of reference: lanes are <c>value − blockMinimum</c>,
    /// bit-packed at the block's span width, no exceptions. Wider
    /// than deltas, but every lane is independently readable — a
    /// seek probes single lanes instead of decoding blocks. Right
    /// for the value columns worst-case-optimal joins seek over.
    /// </summary>
    FrameOfReference,

    /// <summary>
    /// Elias-Fano quasi-succinct: the whole column is ONE monotone
    /// <see cref="Collections.EliasFanoSequence"/> near the
    /// information floor (≈ <c>2 + log2(universe / count)</c> bits
    /// per value), NOT block-structured. Valid only for a globally
    /// non-decreasing column. It drops the per-block frame overhead
    /// frame of reference carries; access and seek are select-driven
    /// (<c>Access</c>, <c>NextGEQ</c>) rather than a lane probe and a
    /// binary search. The encoder opts into it where the column
    /// qualifies.
    /// </summary>
    EliasFano,

    /// <summary>
    /// Partitioned Elias-Fano: a column that is non-decreasing WITHIN each
    /// parent group but resets across group boundaries — the level-1 and
    /// level-2 value columns — stored as one
    /// <see cref="Collections.PartitionedEliasFanoSequence"/>, each group
    /// Elias-Fano relative to its own minimum, the group boundaries borrowed
    /// from the offset column. Access and the segment-local seek are
    /// select-driven. Valid only for a within-group-monotone column built with
    /// the matching boundaries.
    /// </summary>
    PartitionedEliasFano,
}
