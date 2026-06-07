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
}
