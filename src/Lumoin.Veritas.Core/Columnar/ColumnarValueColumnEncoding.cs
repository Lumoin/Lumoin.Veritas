namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// How a <see cref="ColumnarOrder"/> encodes its VALUE columns — an
/// opt-in build policy, default <see cref="FrameOfReference"/>, which
/// reproduces the historical layout byte-for-byte. Offset columns are
/// unaffected: they stay prefixed-delta packed, the measured winner
/// for their regular group-size deltas (Elias-Fano loses there).
/// </summary>
public enum ColumnarValueColumnEncoding
{
    /// <summary>
    /// Every value column is frame-of-reference block-packed
    /// (<see cref="BlockPackedColumnMode.FrameOfReference"/>): each
    /// lane independently seekable, the seek-latency default.
    /// </summary>
    FrameOfReference,

    /// <summary>
    /// A value column that is GLOBALLY non-decreasing — the level-0
    /// column of a single-run build — is stored as one
    /// <see cref="BlockPackedColumnMode.EliasFano"/> sequence, the
    /// measured ~5-6 bits/value saving; within-group value columns
    /// (levels 1 and 2 reset per parent) fall back to frame of
    /// reference. Trades seek latency for footprint, so the index
    /// build opts into it where memory outweighs latency.
    /// </summary>
    EliasFanoWhenMonotone,
}
