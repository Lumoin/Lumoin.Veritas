using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One cross-replica rateless anti-entropy reconciliation between a local <see cref="ColumnarTripleIndex"/> and a
/// peer reached through a <see cref="SketchFetchDelegate"/>. It projects the local triples to structural
/// reconciliation items, persists and loads its own verified sketch, fetches and loads the peer's, decodes their
/// exact symmetric difference under the active <see cref="ReplicationPolicy"/>'s budget, and — only on a complete
/// peel — applies the difference as repair-as-ingest so the local replica converges to the union of both. The
/// whole chain reuses the shipped, transport-free core seams; this library binds the rateless codec and the
/// session that drives them, so the core never takes a replication dependency.
/// </summary>
/// <remarks>
/// Both sides route through the verifying sketch load, so a value handed to the decoder is type-system evidence
/// that detection preceded the combine: an unverified or corrupt sketch can never be combined. The session is a
/// pure function of its inputs apart from the injected time provider — same replicas and peer image yield the
/// same outcome — so it composes under the repair-source ladder without owning any clock or identity itself.
/// </remarks>
public static class AntiEntropySession
{
    /// <summary>
    /// Reconciles the local replica against a peer: persists the local sketch at the policy's budget, fetches the
    /// peer's sketch at the same budget, decodes their symmetric difference, and applies it as repair-as-ingest on
    /// a complete peel. An unreachable peer (an empty fetch), a partial peel, or an overflow leaves the local
    /// index unchanged and reports an incomplete session, so a declined reconciliation never half-applies.
    /// </summary>
    /// <param name="local">The local replica to reconcile and, on a complete peel, converge.</param>
    /// <param name="fetch">The seam that returns the peer's persisted sketch image at a requested symbol budget.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under.</param>
    /// <param name="pool">The pool the session's transient item, image, and recovered-item buffers are rented from.</param>
    /// <param name="timeProvider">The clock the session's elapsed time is measured against.</param>
    /// <returns>The session outcome: the recovered count, the resulting index, completeness, absorbed symbols, and elapsed time.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The peer's sketch image is malformed, fails its checksum, or has the wrong geometry, or a replica holds more items than a single buffer can address.</exception>
    /// <exception cref="NotSupportedException">The peer's sketch image declares an unsupported version, feature, or checksum algorithm.</exception>
    public static AntiEntropySessionResult Reconcile(
        ColumnarTripleIndex local,
        SketchFetchDelegate fetch,
        ReplicationPolicy policy,
        MemoryPool<byte> pool,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);

        long startTimestamp = timeProvider.GetTimestamp();

        int localItemCount = local.TripleCount;
        int symbolBudget = policy.SymbolBudget(localItemCount);
        VerifiedSketch localSketch = PersistLocalSketch(local, localItemCount, symbolBudget, pool);

        ReadOnlyMemory<byte> peerImage = fetch(symbolBudget);
        if(peerImage.IsEmpty)
        {
            //An unreachable peer returns an empty image: there is no source to reconcile against, so the session
            //declines and the local index is returned unchanged.
            return new AntiEntropySessionResult(0, local, false, 0, timeProvider.GetElapsedTime(startTimestamp));
        }

        //The verifying load refuses corrupt peer bytes BEFORE any combine (DetectionPrecedesXor): an unverified
        //sketch can never reach the decoder.
        VerifiedSketch peerSketch = SketchPersistence.LoadVerifiedSketch(peerImage.Span, SketchContract.Structural);

        int sinkCapacity = localSketch.SymbolCount + peerSketch.SymbolCount;
        long sinkByteCount = (long)sinkCapacity * ContentKey128.ByteWidth;
        if(sinkByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The combined sketch budget addresses more recovered items than a single buffer can hold.");
        }

        using IMemoryOwner<byte> recoveredOwner = pool.Rent((int)Math.Max(1, sinkByteCount));
        Span<ContentKey128> recovered = MemoryMarshal.Cast<byte, ContentKey128>(recoveredOwner.Memory.Span)[..sinkCapacity];
        SketchDifference difference = RatelessSketchCodec.RecoverDifference(localSketch, peerSketch, symbolBudget, recovered);

        bool complete = difference.IsComplete && difference.RecoveredCount <= recovered.Length;
        if(!complete)
        {
            //A partial peel (the budget could not cover the difference) or an overflowed sink leaves the local
            //index untouched: applying an incomplete difference would not converge, so the session declines.
            return new AntiEntropySessionResult(difference.RecoveredCount, local, false, difference.AbsorbedSymbols, timeProvider.GetElapsedTime(startTimestamp));
        }

        if(difference.RecoveredCount == 0)
        {
            //The replicas already agree: a complete, empty peel is nothing to ingest, so the local index is
            //returned as-is without an Apply round.
            return new AntiEntropySessionResult(0, local, true, difference.AbsorbedSymbols, timeProvider.GetElapsedTime(startTimestamp));
        }

        EncodedTriple[] recoveredTriples = new EncodedTriple[difference.RecoveredCount];
        for(int i = 0; i < difference.RecoveredCount; i++)
        {
            recoveredTriples[i] = StructuralReconciliationProjection.Invert(recovered[i]);
        }

        //Repair-as-ingest: applying the whole symmetric difference as additions grows the local replica to the
        //union of both — Apply is idempotent, so the items the local side already holds are no-ops.
        ColumnarTripleIndex converged = local.Apply(recoveredTriples, []);

        return new AntiEntropySessionResult(difference.RecoveredCount, converged, true, difference.AbsorbedSymbols, timeProvider.GetElapsedTime(startTimestamp));
    }

    /// <summary>Projects the local triples into a pooled item buffer, persists them as a structural sketch through the rateless encode binder, and loads the image back as a verified sketch — the type-system evidence the local side carries into the combine.</summary>
    /// <param name="local">The local replica whose triples are projected.</param>
    /// <param name="localItemCount">The local replica's triple count, the projected-item buffer length.</param>
    /// <param name="symbolBudget">The number of coded symbols to persist.</param>
    /// <param name="pool">The pool the transient item and image buffers are rented from.</param>
    /// <returns>The local replica's verified sketch.</returns>
    /// <exception cref="InvalidDataException">The local replica holds more items than a single projected-item buffer can address.</exception>
    private static VerifiedSketch PersistLocalSketch(ColumnarTripleIndex local, int localItemCount, int symbolBudget, MemoryPool<byte> pool)
    {
        long itemByteCount = (long)localItemCount * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The local replica holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemOwner = pool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..localItemCount];
        int index = 0;
        foreach(EncodedTriple triple in local.EnumerateTriples())
        {
            items[index] = StructuralReconciliationProjection.Project(triple);
            index++;
        }

        using SlabBufferWriter writer = new(pool);
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, RatelessSketchCodec.Encode, writer);
        int imageLength = writer.BytesWritten;
        using IMemoryOwner<byte> imageOwner = writer.Detach();

        //LoadVerifiedSketch copies the symbols into an independent buffer, so the pooled image is released here
        //while the returned verified sketch outlives this scope.
        return SketchPersistence.LoadVerifiedSketch(imageOwner.Memory.Span[..imageLength], SketchContract.Structural);
    }
}
