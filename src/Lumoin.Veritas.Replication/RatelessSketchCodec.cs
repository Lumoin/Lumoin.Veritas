using System;
using System.Buffers;
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
/// deterministic pure functions of the canonical item bytes — no clock, node identity, or iteration-order
/// dependence — so two replicas' streams combine by XOR and cancel their shared items cleanly.
/// </summary>
/// <remarks>
/// The codec is constructed with the governed <see cref="MemoryPool{T}"/> its transient encoder and decoder rent
/// their working buffers from, so reconciliation's memory is accounted through the engine's tracked pool — with the
/// pressure and allocation telemetry that carries — rather than an untracked shared allocator.
/// </remarks>
public sealed class RatelessSketchCodec
{
    /// <summary>The pool the transient encoder and decoder rent their working buffers from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a codec whose encoder and decoder rent from <paramref name="pool"/>.</summary>
    /// <param name="pool">The governed memory pool the rateless encoder and decoder rent their working buffers from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    public RatelessSketchCodec(MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Pool = pool;
    }

    /// <summary>The host-bound forward seam: folds a replica's projected items into a coded-symbol stream and writes the requested symbol bytes. Pass to <see cref="SketchPersistence.PersistSketch"/>.</summary>
    public SketchReconciliationDelegates.EncodeSketchSymbols Encode => EncodeSymbols;

    /// <summary>The host-bound reverse seam: combines two verified sketches and decodes their exact symmetric difference, returning only the recovered count. Pass to a reconciliation pass that needs no convergence signal.</summary>
    public SketchReconciliationDelegates.DecodeSketchDifference Decode => DecodeDifference;

    /// <summary>The completeness-aware reverse seam: combines two verified sketches and recovers their symmetric difference, reporting whether the peel was complete. Pass to the peer-reconciliation repair rung, which must distinguish a complete peel from a partial one before re-ingesting.</summary>
    public SketchReconciliationDelegates.RecoverSketchDifference Recover => RecoverDifference;

    /// <summary>The structural reconciliation contract both seams pin — the one shared library value, so every encode and decode combines against the maintainer's and the wire's streams.</summary>
    private static ReconciliationContract StructuralContract => StructuralReconciliationContract.Value;

    /// <summary>Folds the items into a rateless encoder and writes the first <paramref name="symbolCount"/> symbols' bytes (the sum field followed by the checksum field) into <paramref name="destination"/>, back to back.</summary>
    /// <param name="items">The replica's projected items.</param>
    /// <param name="symbolCount">The number of symbols to produce.</param>
    /// <param name="symbolWidth">The serialized width of one symbol in bytes.</param>
    /// <param name="destination">The buffer to fill; exactly <paramref name="symbolCount"/> times <paramref name="symbolWidth"/> bytes long.</param>
    private void EncodeSymbols(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        using ReconciliationEncoder encoder = new(StructuralContract, ReconciliationInjectivityEnforcement.None, Pool);
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
    private int DecodeDifference(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered)
    {
        return RecoverDifference(left, right, symbolCap, recovered).RecoveredCount;
    }

    /// <summary>Reconstructs each verified sketch's symbols, combines the two streams index-wise, absorbs until the decoder converges or <paramref name="symbolCap"/> symbols are absorbed, writes the recovered difference items, and reports whether the peel was complete. The operands are <see cref="VerifiedSketch"/> values, so detection preceded this combine by construction.</summary>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb before giving up.</param>
    /// <param name="recovered">The sink for the recovered difference items; when the recovered count exceeds its length nothing is written.</param>
    /// <returns>The recovered count, whether the decoder converged, and how many symbols were absorbed.</returns>
    public SketchDifference RecoverDifference(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered)
    {
        int symbolWidth = left.SymbolWidth;
        ReadOnlySpan<byte> leftSymbols = left.Symbols.Span;
        ReadOnlySpan<byte> rightSymbols = right.Symbols.Span;
        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        int pairs = Math.Min(leftSymbols.Length / symbolWidth, rightSymbols.Length / symbolWidth);
        using ReconciliationDecoder decoder = new(StructuralContract, Pool);

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

        //The decoder's false-decode probability bound (its purity checks against the checksum width) is carried out
        //so a peer-reconciliation session can gate a completeness claim on the evidence quality of the peel.
        return new SketchDifference(decoded.Count, decoder.IsComplete, absorbed, decoder.FalseDecodeProbabilityBound);
    }
}
