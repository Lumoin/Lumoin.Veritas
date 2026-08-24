using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Persistence;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// One scrub round shaped as a compute-lane turn: it brackets the round with began/completed lifecycle trace
/// events, verifies the committed generation, repairs what it can from the verified system-of-record, and
/// hands both reports to the round-result sink. It commits nothing — a generation-agnostic producer — so the
/// sink (a host today, the generation-commit coordinator later) owns staging and the atomic publish. Its
/// <see cref="RunAsync"/> matches the lane's <c>ComputeWorkDelegate</c>, so a host admits it on
/// <c>ComputeWorkClass.Scrub</c> (the lowest priority, yielding to all real work) as a method group, with no
/// lexical closure — every input is instance state.
/// </summary>
public sealed class ScrubTurn
{
    /// <summary>The store holding the committed generation's artifacts.</summary>
    private readonly PersistenceStore store;

    /// <summary>Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</summary>
    private readonly ResolveChecksumAlgorithmDelegate? resolveChecksum;

    /// <summary>The injected re-derive knobs and pools.</summary>
    private readonly RepairConfiguration configuration;

    /// <summary>The sink the round's verify and repair reports are handed to.</summary>
    private readonly ScrubRoundResultDelegate onRoundComplete;

    /// <summary>The diagnostics sink each event is emitted to; <see langword="null"/> emits nothing.</summary>
    private readonly TraceHandler<StorageTraceEvent>? trace;

    /// <summary>The correlation id shared by every event of this round — the bracket markers and both passes.</summary>
    private readonly Guid correlationId;

    /// <summary>The clock the round timestamps its bracket markers with.</summary>
    private readonly TimeProvider timeProvider;

    /// <summary>The seam that supplies the single-block peer-reconciliation restoring source per pass, or <see langword="null"/> to run every round local-only.</summary>
    private ProvidePeerReconciliationSourceDelegate? ProvidePeerSource { get; }

    /// <summary>The seam that supplies the sharded multi-block peer-reconciliation restoring source per pass, or <see langword="null"/> to leave the sharded path unsourced.</summary>
    private ProvideShardedPeerReconciliationSourceDelegate? ProvideShardedPeerSource { get; }

    /// <summary>Creates a scrub turn for one round.</summary>
    /// <param name="store">The store holding the committed generation's artifacts.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="configuration">The injected re-derive knobs and pools.</param>
    /// <param name="onRoundComplete">The sink the round's verify and repair reports are handed to.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this round.</param>
    /// <param name="timeProvider">The clock the round timestamps its bracket markers with.</param>
    /// <param name="providePeerSource">The seam that supplies the single-block peer-reconciliation restoring source per pass; <see langword="null"/> runs rounds local-only.</param>
    /// <param name="provideShardedPeerSource">The seam that supplies the sharded multi-block peer-reconciliation restoring source per pass; <see langword="null"/> leaves the sharded path unsourced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="configuration"/>, <paramref name="onRoundComplete"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public ScrubTurn(
        PersistenceStore store,
        ResolveChecksumAlgorithmDelegate? resolveChecksum,
        RepairConfiguration configuration,
        ScrubRoundResultDelegate onRoundComplete,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider,
        ProvidePeerReconciliationSourceDelegate? providePeerSource = null,
        ProvideShardedPeerReconciliationSourceDelegate? provideShardedPeerSource = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(onRoundComplete);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.resolveChecksum = resolveChecksum;
        this.configuration = configuration;
        this.onRoundComplete = onRoundComplete;
        this.trace = trace;
        this.correlationId = correlationId;
        this.timeProvider = timeProvider;
        ProvidePeerSource = providePeerSource;
        ProvideShardedPeerSource = provideShardedPeerSource;
    }

    /// <summary>Runs one scrub round: emits the began marker, verifies the committed generation, repairs what it can, emits the completed marker (with the scrubbed generation and corrupt-block count), and hands both reports to the round-result sink. A round that does not complete — the token cancelled it mid-repair, or an exception escaped a pass — emits the abandoned marker before the exception propagates, so every began bracket reaches a terminal marker on every path. Shaped as the lane's <c>ComputeWorkDelegate</c>.</summary>
    /// <param name="cancellationToken">Signals that the round should abandon cooperatively; observed by the repair pass's transport-bound work and forwarded to the result sink.</param>
    /// <returns>A task that completes when the round and its result handler are done.</returns>
    /// <exception cref="InvalidDataException">No committed manifest generation could be recovered; the abandoned marker was emitted first.</exception>
    /// <exception cref="NotSupportedException">A recovered manifest or a scrubbed artifact uses a checksum algorithm or format version this build does not support; the abandoned marker was emitted first.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>; the abandoned marker was emitted first.</exception>
    public async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        long timestampTicks = timeProvider.GetUtcNow().UtcTicks;
        EmitRoundMarker(StorageTraceEventKind.ScrubRoundBegan, sequence: 0, timestampTicks, generation: 0, itemCount: 0);

        ScrubRoundReport verifyReport;
        RepairPassReport repairReport;
        try
        {
            verifyReport = ScrubRound.RunVerifyPass(store, resolveChecksum, trace, correlationId, timeProvider);
            repairReport = await ScrubRound.RunRepairPassAsync(store, verifyReport, configuration, resolveChecksum, trace, correlationId, timeProvider, ProvidePeerSource, ProvideShardedPeerSource, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            //Every non-completing exit closes the bracket: cancellation AND any exception escaping a pass (a
            //transport fault a binding failed to convert, an unreadable store). The exception itself propagates
            //unchanged — the marker only guarantees the trace never shows a began bracket without a terminal.
            EmitRoundMarker(StorageTraceEventKind.ScrubRoundAbandoned, sequence: 1, timestampTicks, generation: 0, itemCount: 0);

            throw;
        }

        //The repair report owns any pooled image buffers it carries (a restored system-of-record image);
        //the sink reads them synchronously within the await, then this round returns them to the pool on dispose.
        //The completed marker precedes the sink, so a cancellation inside the sink never re-opens the bracket.
        using(repairReport)
        {
            EmitRoundMarker(StorageTraceEventKind.ScrubRoundCompleted, sequence: 1, timestampTicks, verifyReport.CommitGeneration, verifyReport.CorruptBlocks.Count);

            await onRoundComplete(verifyReport, repairReport, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Emits a round-level lifecycle marker when a sink is attached. Round markers carry role code 0 (no single artifact) and block index -1 (whole-round); the per-pass verdict and repair events number their own sequences, so a consumer groups a round by its correlation id and distinguishes the markers by kind.</summary>
    /// <param name="kind">The lifecycle marker kind.</param>
    /// <param name="sequence">The marker's sequence within the round bracket.</param>
    /// <param name="timestampTicks">The round timestamp.</param>
    /// <param name="generation">The scrubbed commit generation, or 0 before it is resolved.</param>
    /// <param name="itemCount">The round summary count the marker carries, or 0.</param>
    private void EmitRoundMarker(StorageTraceEventKind kind, long sequence, long timestampTicks, long generation, long itemCount)
    {
        if(trace is null)
        {
            return;
        }

        StorageTraceEvent evt = new(sequence, timestampTicks, correlationId, kind, generation, 0, -1, 0, 0, itemCount);
        trace(in evt);
    }
}
