using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
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
    /// partial peel, an overflow, or a peel whose false-decode probability bound exceeds the policy ceiling leaves
    /// the local index unchanged and reports the matching declining <see cref="AntiEntropyOutcome"/>, so a declined
    /// reconciliation never half-applies.
    /// </summary>
    /// <remarks>
    /// This is the in-process seam (no wire), so the peer image carries no domain-or-epoch stamp: its epoch guard is
    /// the caller's — the repair ladder consults it through
    /// <see cref="Lumoin.Veritas.Core.Integrity.PeerReconciliationSource.DictionaryEpoch"/> before descending —
    /// whereas the asynchronous, transport-facing <see cref="ReconcileAsync"/> guards the domain and epoch at the
    /// wire stamp itself.
    /// </remarks>
    /// <param name="local">The local replica to reconcile and, on a complete peel, converge.</param>
    /// <param name="fetch">The seam that returns the peer's persisted sketch image at a requested symbol budget.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under, and the false-decode ceiling a complete peel is gated on.</param>
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
        VerifiedSketch localSketch = PersistLocalSketch(local, symbolBudget, pool);

        ReadOnlyMemory<byte> peerImage = fetch(symbolBudget);

        return CombineAndApply(local, localSketch, peerImage, localItemCount, symbolBudget, pool, policy.MaxFalseDecodeProbability, trace, correlationId, timeProvider, startTimestamp);
    }

    /// <summary>
    /// The asynchronous reconcile: identical to <see cref="Reconcile"/> except it awaits an
    /// <see cref="AsyncSketchFetchDelegate"/> that crosses a transport for the peer's sketch and guards the peer's
    /// wire stamp before combining. The local sketch is persisted synchronously before the await and the
    /// recover-and-apply runs synchronously after it, so the only suspension point is the fetch — every await stays
    /// in the session, never in the core's synchronous repair path.
    /// </summary>
    /// <remarks>
    /// Order of checks after the fetch, each leaving the local index unchanged: no framed response is an unavailable
    /// peer (<see cref="AntiEntropyOutcome.PeerUnavailable"/>); a peer stamped for another domain is a contract
    /// mismatch (<see cref="AntiEntropyOutcome.PeerContractMismatch"/>); a structural peer whose dictionary epoch
    /// differs from <paramref name="dictionaryEpoch"/> is refused before any XOR
    /// (<see cref="AntiEntropyOutcome.PeerEpochMismatch"/>), since its identifiers number a different dictionary; a
    /// stamped decline that carries no image is also an unavailable peer; only then does the combine run.
    /// </remarks>
    /// <param name="local">The local replica to reconcile and, on a complete peel, converge.</param>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch; a structural peer stamped with a different epoch is refused as <see cref="AntiEntropyOutcome.PeerEpochMismatch"/> before any combine.</param>
    /// <param name="fetch">The asynchronous seam that returns the peer's persisted sketch image at a requested symbol budget.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under, and the false-decode ceiling a complete peel is gated on.</param>
    /// <param name="pool">The pool the session's transient item, image, and recovered-item buffers are rented from.</param>
    /// <param name="timeProvider">The clock the session's elapsed time and event timestamp are measured against.</param>
    /// <param name="trace">The diagnostics sink each reconcile emits its outcome to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted event carries, linking the reconcile to the operation that drove it.</param>
    /// <param name="cancellationToken">The token that cancels the awaited fetch.</param>
    /// <returns>The session outcome: the value-based outcome, recovered count, resulting index, absorbed symbols, and elapsed time.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The local replica holds more items than a single buffer can address, or the combined budget overflows a single recovered-item buffer.</exception>
    public static async ValueTask<AntiEntropySessionResult> ReconcileAsync(
        ColumnarTripleIndex local,
        ulong dictionaryEpoch,
        AsyncSketchFetchDelegate fetch,
        ReplicationPolicy policy,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        TraceHandler<ReplicationTraceEvent>? trace = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);

        long startTimestamp = timeProvider.GetTimestamp();
        int localItemCount = local.TripleCount;
        int symbolBudget = policy.SymbolBudget(localItemCount);
        VerifiedSketch localSketch = PersistLocalSketch(local, symbolBudget, pool);

        //The fetched result OWNS its pooled image; the using releases it after the synchronous combine, which the
        //verifying load has already copied its symbols out of, so the owner safely outlives every combine read.
        using SketchFetchResult peer = await fetch(symbolBudget, cancellationToken).ConfigureAwait(false);
        if(peer.IsUnavailable)
        {
            //No framed response at all: an unreachable peer, so there is no source to reconcile against.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerUnavailable, localItemCount, symbolBudget, 0, 0, local);
        }

        if(peer.Domain != SketchChannelDomain.Structural)
        {
            //A peer stamped for another reconciliation domain: its image is shaped for a different item space, so the
            //structural session refuses at the stamp rather than inverting garbage into triples.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerContractMismatch, localItemCount, symbolBudget, 0, 0, local);
        }

        if(peer.DictionaryEpoch != dictionaryEpoch)
        {
            //A structural peer keyed to a different dictionary epoch: its identifiers number a different dictionary,
            //so combining would mis-relate encoded identifiers. The session refuses before any XOR.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerEpochMismatch, localItemCount, symbolBudget, 0, 0, local);
        }

        if(!peer.HasImage)
        {
            //A stamped decline (a serveable image was not built) is an unavailable peer for this round.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerUnavailable, localItemCount, symbolBudget, 0, 0, local);
        }

        return CombineAndApply(local, localSketch, peer.Image, localItemCount, symbolBudget, pool, policy.MaxFalseDecodeProbability, trace, correlationId, timeProvider, startTimestamp);
    }

    /// <summary>Combines the loaded local sketch with the fetched peer image and, on a complete peel, applies the recovered difference — the synchronous recover-and-apply shared by the synchronous and asynchronous reconciles, so they differ only in how the peer image is fetched. A complete peel whose false-decode probability bound exceeds <paramref name="maxFalseDecodeProbability"/> is declined before it is acted on, so an under-checked "already consistent" or "converged" claim is refused rather than laundered.</summary>
    /// <param name="local">The local replica being reconciled.</param>
    /// <param name="localSketch">The local replica's verified sketch.</param>
    /// <param name="peerImage">The peer's fetched sketch image, or empty when the peer was unavailable.</param>
    /// <param name="localItemCount">The local replica's item count, for the emitted event.</param>
    /// <param name="symbolBudget">The symbol budget the sketches were built and the recovery is capped at.</param>
    /// <param name="pool">The pool the recovered-item buffer is rented from.</param>
    /// <param name="maxFalseDecodeProbability">The ceiling on the decoder's false-decode probability bound a complete peel may carry and still be acted on.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The reconcile's correlation id.</param>
    /// <param name="timeProvider">The clock the event timestamp and elapsed time are read from.</param>
    /// <param name="startTimestamp">The timestamp the session began at.</param>
    /// <returns>The session result.</returns>
    /// <exception cref="InvalidDataException">The combined budget overflows a single recovered-item buffer.</exception>
    private static AntiEntropySessionResult CombineAndApply(
        ColumnarTripleIndex local,
        VerifiedSketch localSketch,
        ReadOnlyMemory<byte> peerImage,
        int localItemCount,
        int symbolBudget,
        MemoryPool<byte> pool,
        double maxFalseDecodeProbability,
        TraceHandler<ReplicationTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider,
        long startTimestamp)
    {
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
        SketchDifference difference = new RatelessSketchCodec(pool).RecoverDifference(localSketch, peerSketch, symbolBudget, recovered);

        bool complete = difference.IsComplete && difference.RecoveredCount <= recovered.Length;
        if(!complete)
        {
            //A partial peel (the budget could not cover the difference) or an overflowed sink leaves the local
            //index untouched: applying an incomplete difference would not converge, so the session declines.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.IncompletePeel, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        if(difference.FalseDecodeProbabilityBound > maxFalseDecodeProbability)
        {
            //A complete peel whose evidence quality is below the policy ceiling: the union bound that no masquerading
            //symbol forged the peel is too weak to act on. This gate precedes the empty-peel branch, so even an
            //implausibly-bounded "already consistent" claim is refused rather than laundered into convergence.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.FalseDecodeBoundExceeded, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
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

        return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.Converged, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, converged, recoveredTriples);
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
    /// <param name="recoveredAdditions">The triples applied to converge; empty (the default) on every decline and on an already-consistent peel.</param>
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
        ColumnarTripleIndex convergedIndex,
        ReadOnlyMemory<EncodedTriple> recoveredAdditions = default)
    {
        if(trace is not null)
        {
            ReplicationTraceEvent evt = new(0, timeProvider.GetUtcNow().UtcTicks, correlationId, outcome, localItemCount, symbolBudget, recoveredCount, absorbedSymbols);
            trace(in evt);
        }

        return new AntiEntropySessionResult(recoveredCount, convergedIndex, outcome, absorbedSymbols, timeProvider.GetElapsedTime(startTimestamp), recoveredAdditions);
    }

    /// <summary>Persists the local replica's structural sketch and loads the image back as a verified sketch — the type-system evidence the local side carries into the combine.</summary>
    /// <param name="local">The local replica whose triples are projected.</param>
    /// <param name="symbolBudget">The number of coded symbols to persist.</param>
    /// <param name="pool">The pool the transient item and image buffers are rented from.</param>
    /// <returns>The local replica's verified sketch.</returns>
    /// <exception cref="InvalidDataException">The local replica holds more items than a single projected-item buffer can address.</exception>
    private static VerifiedSketch PersistLocalSketch(ColumnarTripleIndex local, int symbolBudget, MemoryPool<byte> pool)
    {
        using SlabBufferWriter writer = new(pool);
        LocalSketchImage.Write(local, symbolBudget, pool, writer);
        int imageLength = writer.BytesWritten;
        using IMemoryOwner<byte> imageOwner = writer.Detach();

        //LoadVerifiedSketch copies the symbols into an independent buffer, so the pooled image is released here
        //while the returned verified sketch outlives this scope.
        return SketchPersistence.LoadVerifiedSketch(imageOwner.Memory.Span[..imageLength], SketchContract.Structural);
    }
}
