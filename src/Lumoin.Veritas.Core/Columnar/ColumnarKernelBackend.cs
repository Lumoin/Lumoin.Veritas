using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A bundle of block-granular codec kernels implementing a single
/// per-ISA backend for <see cref="BlockPackedColumn"/>: packing one
/// block's bit-lanes at build time, and decoding one block —
/// unpack, exception patch, un-zigzag, prefix-sum — at read time.
/// Engines swap as whole bundles; delegate indirection happens once
/// per BLOCK, never per element.
/// </summary>
/// <remarks>
/// <para>
/// Construct via the factories on the backend classes
/// (<see cref="ColumnarPortableBackend"/>,
/// <see cref="ColumnarVector128Backend"/>,
/// <see cref="ColumnarWasmPackedSimdBackend"/>,
/// <see cref="ColumnarVector256Backend"/>,
/// <see cref="ColumnarVector512Backend"/>) or use
/// <see cref="Default"/> for cached runtime auto-selection.
/// </para>
/// <para>
/// The bundle covers only how the bytes MOVE. What they mean —
/// the zigzag-delta layout, per-block width selection, which lanes
/// become exceptions — is fixed by <see cref="BlockPackedColumn"/>
/// and identical across backends, so two backends always agree on
/// every byte they produce and consume.
/// </para>
/// </remarks>
public readonly struct ColumnarKernelBackend: IEquatable<ColumnarKernelBackend>
{
    /// <summary>
    /// Packs <paramref name="values"/> into <paramref name="payload"/>
    /// as consecutive <paramref name="bitWidth"/>-bit lanes: value
    /// <c>i</c> occupies payload bits
    /// <c>[i·bitWidth, (i+1)·bitWidth)</c>, little-endian within
    /// each 64-bit word. Only the low <paramref name="bitWidth"/>
    /// bits of each value are written; the payload must arrive
    /// zeroed.
    /// </summary>
    /// <param name="values">The zigzag lane values to pack.</param>
    /// <param name="bitWidth">The lane width in bits; 0 packs nothing, 32 packs whole values.</param>
    /// <param name="payload">The zeroed destination words; at least <c>ceil(values.Length·bitWidth / 64)</c> long.</param>
    public delegate void PackLanesDelegate(ReadOnlySpan<uint> values, int bitWidth, Span<ulong> payload);

    /// <summary>
    /// Decodes one whole block: unpacks
    /// <paramref name="destination"/><c>.Length</c> lanes from
    /// <paramref name="payload"/>, overwrites the exception lanes
    /// with their full zigzag values, then un-zigzags and
    /// prefix-sums from <paramref name="anchor"/> in wrapping
    /// 32-bit arithmetic, leaving the block's decoded values in
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="anchor">The block's first decoded value; lane 0's zigzag delta is zero by construction.</param>
    /// <param name="exceptionPositions">The in-block positions of the exception lanes, ascending.</param>
    /// <param name="exceptionValues">The exception lanes' full zigzag values, parallel to <paramref name="exceptionPositions"/>.</param>
    /// <param name="destination">Receives the decoded block values.</param>
    public delegate void DecodeBlockDelegate(
        ReadOnlySpan<ulong> payload,
        int bitWidth,
        uint anchor,
        ReadOnlySpan<ushort> exceptionPositions,
        ReadOnlySpan<uint> exceptionValues,
        Span<uint> destination);

    /// <summary>
    /// Decodes one whole frame-of-reference block: unpacks
    /// <paramref name="destination"/><c>.Length</c> lanes from
    /// <paramref name="payload"/> and adds
    /// <paramref name="frameBase"/> to each in wrapping 32-bit
    /// arithmetic.
    /// </summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="frameBase">The block's frame minimum.</param>
    /// <param name="destination">Receives the decoded block values.</param>
    public delegate void DecodeFrameDelegate(
        ReadOnlySpan<ulong> payload,
        int bitWidth,
        uint frameBase,
        Span<uint> destination);

    /// <summary>The build-time lane-packing kernel.</summary>
    public PackLanesDelegate Pack { get; }

    /// <summary>The read-time whole-block decode kernel for prefixed-delta blocks.</summary>
    public DecodeBlockDelegate Decode { get; }

    /// <summary>The read-time whole-block decode kernel for frame-of-reference blocks.</summary>
    public DecodeFrameDelegate DecodeFrame { get; }

    /// <summary>The cached best-available backend for this process.</summary>
    public static ColumnarKernelBackend Default => ColumnarKernelBackendSelection.SelectBest();

    /// <summary>
    /// The best-available backend whose vector width does not exceed
    /// <paramref name="cap"/> — the <see cref="ExecutionPolicy.KernelWidthCap"/>
    /// ceiling applied to the static capability ladder, for the
    /// down-clocking SKU case and force-narrow measurement passes.
    /// <see cref="KernelWidthCap.Auto"/> returns <see cref="Default"/>.
    /// </summary>
    /// <param name="cap">The width ceiling.</param>
    /// <returns>The selected backend.</returns>
    public static ColumnarKernelBackend ForCap(KernelWidthCap cap) => ColumnarKernelBackendSelection.SelectForCap(cap);

    /// <summary>Constructs a backend bundle from its kernels.</summary>
    /// <param name="pack">The pack kernel.</param>
    /// <param name="decode">The prefixed-delta decode kernel.</param>
    /// <param name="decodeFrame">The frame-of-reference decode kernel.</param>
    /// <exception cref="ArgumentNullException">A kernel is <c>null</c>.</exception>
    public ColumnarKernelBackend(PackLanesDelegate pack, DecodeBlockDelegate decode, DecodeFrameDelegate decodeFrame)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(decodeFrame);

        Pack = pack;
        Decode = decode;
        DecodeFrame = decodeFrame;
    }

    /// <inheritdoc/>
    public bool Equals(ColumnarKernelBackend other) => Pack == other.Pack && Decode == other.Decode && DecodeFrame == other.DecodeFrame;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ColumnarKernelBackend other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Pack, Decode, DecodeFrame);

    /// <inheritdoc/>
    public static bool operator ==(ColumnarKernelBackend left, ColumnarKernelBackend right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(ColumnarKernelBackend left, ColumnarKernelBackend right) => !left.Equals(right);
}
