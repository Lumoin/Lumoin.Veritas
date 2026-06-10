namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// How a <see cref="ColumnarOrder"/> encodes its VALUE columns — an
/// opt-in build policy, default <see cref="FrameOfReference"/>, which
/// reproduces the historical layout byte-for-byte. Offset columns are
/// unaffected: they stay prefixed-delta packed.
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
    /// column of a single-run build — is offered as one
    /// <see cref="BlockPackedColumnMode.EliasFano"/> sequence, and a
    /// within-group value column (levels 1 and 2 reset per parent) as
    /// <see cref="BlockPackedColumnMode.PartitionedEliasFano"/> over
    /// the parent offsets' boundaries; each column keeps the smaller
    /// of the candidate and its frame-of-reference packing, so the
    /// policy never enlarges a column. Opt-in.
    /// </summary>
    EliasFanoWhenMonotone,
}
