using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Binds the Lumoin.Verisync rateless anti-entropy encoder and decoder to the core's host-bound sketch seams
/// (<see cref="SketchReconciliationDelegates"/>), so the core persists and reconciles integrity sketches in its
/// own vocabulary — content-key items and raw symbol bytes — while the rateless codec lives here, out of the
/// core. The structural contract pins a 16-byte item and an 8-byte well-known-keyed checksum. Both seams are
/// pure functions of the canonical item bytes — no clock, node identity, or iteration-order dependence — so two
/// replicas' streams combine by XOR and cancel their shared items cleanly, and both are static and capture
/// nothing.
/// </summary>
public static class RatelessSketchCodec
{
    /// <summary>The 8-byte checksum field width the structural symbol carries after its 16-byte sum field.</summary>
    private const int StructuralChecksumWidth = 8;

    /// <summary>The host-bound forward seam: folds a replica's projected items into a coded-symbol stream and writes the requested symbol bytes. Pass to <see cref="SketchPersistence.PersistSketch"/>.</summary>
    public static SketchReconciliationDelegates.EncodeSketchSymbols Encode { get; } = EncodeSymbols;

    /// <summary>The host-bound reverse seam: combines two verified sketches and decodes their exact symmetric difference. Pass to a reconciliation pass or peer-reconciliation rung.</summary>
    public static SketchReconciliationDelegates.DecodeSketchDifference Decode { get; } = DecodeDifference;

    /// <summary>The structural reconciliation contract both seams pin: the structural item domain, a 16-byte item, an 8-byte well-known-keyed checksum. It is input-independent and immutable, so it is built once and shared by every encode and decode.</summary>
    private static ReconciliationContract StructuralContract { get; } = new(
        ReconciliationItemDomain.Structural,
        ContentKey128.ByteWidth,
        StructuralChecksumWidth,
        ReconciliationContract.WellKnownChecksumKeyLow,
        ReconciliationContract.WellKnownChecksumKeyHigh);

    /// <summary>Folds the items into a rateless encoder and writes the first <paramref name="symbolCount"/> symbols' bytes (the sum field followed by the checksum field) into <paramref name="destination"/>, back to back.</summary>
    /// <param name="items">The replica's projected items.</param>
    /// <param name="symbolCount">The number of symbols to produce.</param>
    /// <param name="symbolWidth">The serialized width of one symbol in bytes.</param>
    /// <param name="destination">The buffer to fill; exactly <paramref name="symbolCount"/> times <paramref name="symbolWidth"/> bytes long.</param>
    private static void EncodeSymbols(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        using ReconciliationEncoder encoder = new(StructuralContract);
        Span<byte> itemBytes = stackalloc byte[ContentKey128.ByteWidth];
        foreach(ContentKey128 item in items)
        {
            item.WriteBytes(itemBytes);
            encoder.Add(itemBytes);
        }

        for(int i = 0; i < symbolCount; i++)
        {
            ReconciliationSymbol symbol = encoder.ProduceNext();
            symbol.Sum.Span.CopyTo(destination.Slice(i * symbolWidth, ContentKey128.ByteWidth));
            symbol.Checksum.Span.CopyTo(destination.Slice((i * symbolWidth) + ContentKey128.ByteWidth, checksumWidth));
        }
    }

    /// <summary>The count-only reverse seam: recovers the symmetric difference and returns just how many items it found, discarding the convergence signal. Binds <see cref="Decode"/>; <see cref="RecoverDifference"/> is the richer entry a session drives.</summary>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb before giving up.</param>
    /// <param name="recovered">The sink for the recovered difference items.</param>
    /// <returns>The number of recovered items; when it exceeds <paramref name="recovered"/>'s length nothing is written.</returns>
    private static int DecodeDifference(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered)
    {
        return RecoverDifference(left, right, symbolCap, recovered).RecoveredCount;
    }

    /// <summary>Reconstructs each verified sketch's symbols, combines the two streams index-wise, absorbs until the decoder converges or <paramref name="symbolCap"/> symbols are absorbed, writes the recovered difference items, and reports whether the peel was complete. The operands are <see cref="VerifiedSketch"/> values, so detection preceded this combine by construction.</summary>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb before giving up.</param>
    /// <param name="recovered">The sink for the recovered difference items; when the recovered count exceeds its length nothing is written.</param>
    /// <returns>The recovered count, whether the decoder converged, and how many symbols were absorbed.</returns>
    public static SketchDifference RecoverDifference(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered)
    {
        int symbolWidth = left.SymbolWidth;
        ReadOnlySpan<byte> leftSymbols = left.Symbols.Span;
        ReadOnlySpan<byte> rightSymbols = right.Symbols.Span;
        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        int pairs = Math.Min(leftSymbols.Length / symbolWidth, rightSymbols.Length / symbolWidth);
        using ReconciliationDecoder decoder = new(StructuralContract);

        int absorbed = 0;
        for(int i = 0; i < pairs && !decoder.IsComplete && absorbed < symbolCap; i++)
        {
            int offset = i * symbolWidth;
            ReconciliationSymbol leftSymbol = new(leftSymbols.Slice(offset, ContentKey128.ByteWidth), leftSymbols.Slice(offset + ContentKey128.ByteWidth, checksumWidth));
            ReconciliationSymbol rightSymbol = new(rightSymbols.Slice(offset, ContentKey128.ByteWidth), rightSymbols.Slice(offset + ContentKey128.ByteWidth, checksumWidth));
            decoder.Absorb(leftSymbol.Combine(rightSymbol));
            absorbed++;
        }

        IReadOnlyList<ReadOnlyMemory<byte>> decoded = decoder.DecodedItems;
        if(decoded.Count <= recovered.Length)
        {
            for(int i = 0; i < decoded.Count; i++)
            {
                recovered[i] = ContentKey128.FromBytes(decoded[i].Span);
            }
        }

        return new SketchDifference(decoded.Count, decoder.IsComplete, absorbed);
    }
}
