using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Sketch;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// Persists and loads an integrity sketch — a rateless-reconciliation coded-symbol stream — through the shipped
/// <see cref="SketchSegment"/> format, upholding <see cref="PersistenceInvariant.DetectionPrecedesXor"/>: the
/// only path that yields combine-ready symbol bytes is <see cref="LoadVerifiedSketch"/>, which verifies every
/// block's checksum before returning, so no decode ever folds an unverified symbol.
/// </summary>
public static class SketchPersistence
{
    /// <summary>Encodes a replica's items into a coded-symbol stream and writes a self-describing, block-checksummed sketch image to <paramref name="destination"/>.</summary>
    /// <param name="items">This replica's projected reconciliation items.</param>
    /// <param name="contract">The sketch geometry (symbol width and symbols per block).</param>
    /// <param name="symbolCount">The number of symbols to produce — the caller's budget.</param>
    /// <param name="checksum">The per-block checksum algorithm.</param>
    /// <param name="pool">The pool the transient symbol buffer is rented from.</param>
    /// <param name="encode">The host-bound encoder that folds items into symbol bytes.</param>
    /// <param name="destination">The sink for the sketch image.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolCount"/> is negative.</exception>
    public static void PersistSketch(
        ReadOnlySpan<ContentKey128> items,
        SketchContract contract,
        int symbolCount,
        ChecksumAlgorithm checksum,
        MemoryPool<byte> pool,
        SketchReconciliationDelegates.EncodeSketchSymbols encode,
        IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(symbolCount);

        long symbolByteCount = (long)symbolCount * contract.SymbolWidth;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(symbolByteCount, Array.MaxLength);
        int symbolBytes = (int)symbolByteCount;
        using IMemoryOwner<byte> symbolOwner = pool.Rent(symbolBytes);
        ReadOnlyMemory<byte> symbols = symbolOwner.Memory[..symbolBytes];
        encode(items, symbolCount, contract.SymbolWidth, symbolOwner.Memory.Span[..symbolBytes]);

        SketchSegment segment = new(symbols, contract.SymbolWidth, contract.SymbolsPerBlock);
        int imageSize = checked((int)segment.ComputeSerializedSize(checksum));
        Span<byte> image = destination.GetSpan(imageSize)[..imageSize];
        segment.WriteTo(image, checksum);
        destination.Advance(imageSize);
    }

    /// <summary>Loads and verifies a sketch image, returning combine-ready symbol bytes only after the front-matter trailer and every block checksum pass and the stored geometry matches <paramref name="contract"/>.</summary>
    /// <param name="image">The sketch image.</param>
    /// <param name="contract">The geometry the reader requires.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <returns>The verified sketch.</returns>
    /// <exception cref="InvalidDataException">The image is malformed, a block fails its checksum, or the stored geometry does not match <paramref name="contract"/>.</exception>
    /// <exception cref="NotSupportedException">The image's version, a required feature, or the checksum algorithm is unsupported.</exception>
    public static VerifiedSketch LoadVerifiedSketch(ReadOnlySpan<byte> image, SketchContract contract, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        //Refuse a wrong-geometry sketch with the cheap decode-free geometry gate before paying for the full
        //verify-and-copy, then verify every block and copy the symbols out.
        (int loadedSymbolWidth, int loadedSymbolsPerBlock) = SketchSegment.ReadGeometry(image);
        SketchContract.RequireMatch(contract, loadedSymbolWidth, loadedSymbolsPerBlock);
        byte[] symbols = SketchSegment.ReadFrom(image, resolveChecksum);

        return new VerifiedSketch(symbols, loadedSymbolWidth, symbols.Length / loadedSymbolWidth);
    }
}
