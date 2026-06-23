using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
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
/// that detection preceded the combine: an unverified or corrupt sketch can never be combined. Every reconcile
/// ends at one of the <see cref="AntiEntropyOutcome"/> values, reported both as the result's outcome and — when a
/// handler is attached — as a <see cref="ReplicationTraceEvent"/> on the Core diagnostics bus. The session is a
/// pure function of its inputs apart from the injected time provider and that observe-only trace, so it composes
/// under the repair-source ladder without owning any clock or identity itself.
/// </remarks>
public static class AntiEntropySession
{
    /// <summary>
    /// Reconciles the local replica against a peer: persists the local sketch at the policy's budget, fetches the
    /// peer's sketch at the same budget, decodes their symmetric difference, and applies it as repair-as-ingest on
    /// a complete peel. An unreachable peer (an empty fetch), a refused peer sketch (corrupt or wrong geometry), a
    /// partial peel, or an overflow leaves the local index unchanged and reports the matching declining
    /// <see cref="AntiEntropyOutcome"/>, so a declined reconciliation never half-applies.
    /// </summary>
    /// <param name="local">The local replica to reconcile and, on a complete peel, converge.</param>
    /// <param name="fetch">The seam that returns the peer's persisted sketch image at a requested symbol budget.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under.</param>
    /// <param name="pool">The pool the session's transient item, image, and recovered-item buffers are rented from.</param>
    /// <param name="timeProvider">The clock the session's elapsed time and event timestamp are measured against.</param>
    /// <param name="trace">The diagnostics sink each reconcile emits its outcome to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted event carries, linking the reconcile to the operation that drove it.</param>
    /// <returns>The session outcome: the value-based outcome, recovered count, resulting index, absorbed symbols, and elapsed time.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The local replica holds more items than a single buffer can address, or the combined budget overflows a single recovered-item buffer.</exception>
    public static AntiEntropySessionResult Reconcile(
        ColumnarTripleIndex local,
        SketchFetchDelegate fetch,
        ReplicationPolicy policy,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        TraceHandler<ReplicationTraceEvent>? trace = null,
        Guid correlationId = default)
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
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerUnavailable, localItemCount, symbolBudget, 0, 0, local);
        }

        //The verifying load refuses corrupt or wrong-geometry peer bytes BEFORE any combine (DetectionPrecedesXor).
        //A peer reached over a transport can send bytes that fail that load, which is an expected operational
        //condition, so it is a value-based decline here rather than a propagated throw; the LOCAL sketch's own load
        //still throws, since that is our own data and a genuine invariant breach.
        VerifiedSketch peerSketch;
        try
        {
            peerSketch = SketchPersistence.LoadVerifiedSketch(peerImage.Span, SketchContract.Structural);
        }
        catch(Exception exception) when(exception is InvalidDataException or NotSupportedException)
        {
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerSketchRejected, localItemCount, symbolBudget, 0, 0, local);
        }

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
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.IncompletePeel, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        if(difference.RecoveredCount == 0)
        {
            //The replicas already agree: a complete, empty peel is nothing to ingest, so the local index is
            //returned as-is without an Apply round.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.AlreadyConsistent, localItemCount, symbolBudget, 0, difference.AbsorbedSymbols, local);
        }

        EncodedTriple[] recoveredTriples = new EncodedTriple[difference.RecoveredCount];
        for(int i = 0; i < difference.RecoveredCount; i++)
        {
            recoveredTriples[i] = StructuralReconciliationProjection.Invert(recovered[i]);
        }

        //Repair-as-ingest: applying the whole symmetric difference as additions grows the local replica to the
        //union of both — Apply is idempotent, so the items the local side already holds are no-ops.
        ColumnarTripleIndex converged = local.Apply(recoveredTriples, []);

        return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.Converged, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, converged);
    }

    /// <summary>Builds the session result for an outcome and, when a handler is attached, emits the matching <see cref="ReplicationTraceEvent"/> — the one place every reconcile path reports through, so the result's outcome and the emitted event never disagree.</summary>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The reconcile's correlation id.</param>
    /// <param name="timeProvider">The clock the event timestamp and elapsed time are read from.</param>
    /// <param name="startTimestamp">The timestamp the session began at, for the elapsed measurement.</param>
    /// <param name="outcome">How the reconcile ended.</param>
    /// <param name="localItemCount">The local replica's item count.</param>
    /// <param name="symbolBudget">The symbol budget the sketches were built and the recovery capped at.</param>
    /// <param name="recoveredCount">The recovered difference-item count.</param>
    /// <param name="absorbedSymbols">The symbols the decoder absorbed.</param>
    /// <param name="convergedIndex">The resulting index — the converged union, or the unchanged local index on a decline.</param>
    /// <returns>The session result.</returns>
    private static AntiEntropySessionResult Complete(
        TraceHandler<ReplicationTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider,
        long startTimestamp,
        AntiEntropyOutcome outcome,
        int localItemCount,
        int symbolBudget,
        int recoveredCount,
        int absorbedSymbols,
        ColumnarTripleIndex convergedIndex)
    {
        if(trace is not null)
        {
            ReplicationTraceEvent evt = new(0, timeProvider.GetUtcNow().UtcTicks, correlationId, outcome, localItemCount, symbolBudget, recoveredCount, absorbedSymbols);
            trace(in evt);
        }

        return new AntiEntropySessionResult(recoveredCount, convergedIndex, outcome, absorbedSymbols, timeProvider.GetElapsedTime(startTimestamp));
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
