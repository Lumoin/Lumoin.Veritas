using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Segment;
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
        //Rent at least one byte so a zero-symbol budget produces a valid empty sketch on any pool (some pools
        //reject a zero-length rent); the symbols view is still sliced to the exact, possibly-empty, byte count.
        using IMemoryOwner<byte> symbolOwner = pool.Rent(Math.Max(1, symbolBytes));
        ReadOnlyMemory<byte> symbols = symbolOwner.Memory[..symbolBytes];
        encode(items, symbolCount, contract.SymbolWidth, symbolOwner.Memory.Span[..symbolBytes]);

        SketchSegment segment = new(symbols, contract.SymbolWidth, contract.SymbolsPerBlock);
        int imageSize = checked((int)segment.ComputeSerializedSize(checksum));
        Span<byte> image = destination.GetSpan(imageSize)[..imageSize];
        segment.WriteTo(image, checksum);
        destination.Advance(imageSize);
    }

    /// <summary>
    /// Runs one sketch-update round for a generation: walks the just-staged system-of-record image block by
    /// block, excludes any block that fails its checksum (the feed face of
    /// <see cref="PersistenceInvariant.DetectionPrecedesXor"/> — a corrupt block's items never enter the
    /// encoder), projects the verified triples into reconciliation items, and produces a budgeted sketch image
    /// into <paramref name="destination"/>. The round only produces bytes; staging the image and listing it in
    /// the generation's manifest before the CURRENT publish are the caller's, so the sketch co-versions with the
    /// system-of-record and the manifest as one generation.
    /// </summary>
    /// <param name="systemOfRecordImage">The system-of-record item-segment image this generation's sketch describes.</param>
    /// <param name="contract">The sketch geometry (symbol width and symbols per block).</param>
    /// <param name="symbolBudget">The number of symbols to produce — the round's budgeted cap, never an unbounded loop.</param>
    /// <param name="checksum">The per-block checksum algorithm — used both to verify the system-of-record blocks and to stamp the sketch's blocks.</param>
    /// <param name="pool">The pool the transient item and symbol buffers are rented from.</param>
    /// <param name="triplePool">The pool the verified-triple feed buffer is rented from.</param>
    /// <param name="project">The projection from a triple to its reconciliation item — the structural built-in by default, an alternative domain when injected.</param>
    /// <param name="encode">The host-bound encoder that folds items into symbol bytes.</param>
    /// <param name="destination">The sink for the sketch image.</param>
    /// <param name="resolveChecksum">Resolves the system-of-record image's checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <returns>The round's verdict: symbols produced, items fed, and the item ranges excluded by a failed block.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolBudget"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The system-of-record image is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The system-of-record image's version, a required feature, or the checksum algorithm is unsupported.</exception>
    public static SketchUpdateRoundReport RunSketchUpdateRound(
        ReadOnlySpan<byte> systemOfRecordImage,
        SketchContract contract,
        int symbolBudget,
        ChecksumAlgorithm checksum,
        MemoryPool<byte> pool,
        MemoryPool<EncodedTriple> triplePool,
        ProjectReconciliationItemDelegate project,
        SketchReconciliationDelegates.EncodeSketchSymbols encode,
        IBufferWriter<byte> destination,
        ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(triplePool);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(symbolBudget);

        //Feed face of I2: a block that fails its checksum is excluded here, so its items never reach the
        //encoder. Framing or front-matter damage is untrusted geometry and refuses the image instead.
        using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(systemOfRecordImage, triplePool, resolveChecksum);
        int itemCount = feed.VerifiedCount;

        long itemByteCount = (long)itemCount * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The system-of-record holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemOwner = pool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..itemCount];
        ReadOnlySpan<EncodedTriple> verified = feed.VerifiedItems.Span;
        for(int i = 0; i < itemCount; i++)
        {
            items[i] = project(verified[i]);
        }

        PersistSketch(items, contract, symbolBudget, checksum, pool, encode, destination);

        return new SketchUpdateRoundReport(symbolBudget, itemCount, feed.SkippedRanges, feed.WasChecksumGated);
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
