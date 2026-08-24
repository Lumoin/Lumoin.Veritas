using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Verisync.Core;
using DottedElement = Lumoin.Verisync.Core.DottedEntry<Lumoin.Veritas.Core.EncodedTriple>;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Runs one dotted (remove-aware) reconciliation exchange against a peer over a real wire: opens one fresh
/// connection, exchanges the header pair (the request declares this endpoint's epoch, its offer-shaped dotted
/// contract, and the symbol cap; the reply carries the PEER'S OWN declarations and, on a decline, the named
/// reason), then drives the remove-aware initiator session over the envelope stream. The session's apply seams
/// commit durable, causally self-consistent progress DURING the exchange, so an interrupted run leaves a
/// consistent prefix — never a torn state — and re-running converges idempotently. The connection is disposed
/// unconditionally on every exit — teardown, not session wind-down, is what releases a backpressure-blocked peer.
/// </summary>
/// <remarks>
/// <para>
/// FAULT POSTURE: every fault except cancellation converts to a VALUE outcome with the fault class on the
/// trace. A connect-phase fault reports peer-unavailable; the peer's EXPLICIT unknown-service refusal byte —
/// raised by the connection seam as <see cref="PeerServiceRefusedException"/> — reports
/// remove-aware-unsupported, and is never inferred from silence. Mid-exchange, a torn transport reports the
/// interrupted (durable-prefix) outcome, a protocol violation reports a protocol fault, an exhausted adopt
/// write-back reports the named conflict-exhausted outcome, and the identity-collision tripwire refuses by
/// name before the colliding knowledge reaches the session.
/// </para>
/// <para>
/// CAP DISCIPLINE: absorbed symbols are counted at batch granularity as they are submitted. The responder's
/// whole trigger stream is finite — sized from the requested cap plus its spare batch — so once more than the
/// cap plus one batch has been submitted, no further inbound frame can arrive until this side's decode speaks;
/// the wind-down decision then waits on the decode-completion signal (the session's own DONE send, observed at
/// the send binding) under the drain window, so a completing decode is never aborted while its queued batches
/// drain, and only a decode that stays silent through the whole window winds down as the bounded refusal. The
/// verdict is read only AFTER the session's run is joined.
/// </para>
/// </remarks>
public sealed class DottedDifferenceChannelClient
{
    /// <summary>The per-trigger symbol batch both endpoints default to: one wire frame well under any pipe threshold, and the cap-overshoot bound.</summary>
    public const int DefaultBatchSize = 64;

    /// <summary>The largest symbol cap either endpoint accepts: the ceiling keeps the responder's trigger-budget arithmetic and the client's wind-down threshold — each of which adds up to two batches to the cap — inside the integer range, so a hostile or mistaken cap can never wrap into a hung or falsely wound-down exchange.</summary>
    public const int MaximumSymbolCap = int.MaxValue - (2 * DefaultBatchSize);

    /// <summary>The default drain window an exhausted symbol stream grants the session's consumer before the exchange winds down as out of budget.</summary>
    private static TimeSpan DefaultDecodeDrainWindow { get; } = TimeSpan.FromSeconds(5);

    /// <summary>The seam that opens one fresh duplex connection per exchange.</summary>
    private OpenPeerDottedConnectionDelegate OpenConnection { get; }

    /// <summary>This endpoint's dictionary epoch, declared on every request header.</summary>
    private ulong DictionaryEpoch { get; }

    /// <summary>The local host identity axis the tripwire guards.</summary>
    private ReplicaAxis LocalAxis { get; }

    /// <summary>The live own-axis maximum seam the tripwire compares against.</summary>
    private ReadOwnAxisMaximumDelegate ReadOwnAxisMaximum { get; }

    /// <summary>The clock fault-event timestamps are read from and the drain window is measured on.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The drain window an exhausted symbol stream grants the session's consumer before the exchange winds down as out of budget.</summary>
    private TimeSpan DecodeDrainWindow { get; }

    /// <summary>The diagnostics sink fault events are emitted to, or <see langword="null"/> to emit nothing.</summary>
    private TraceHandler<DottedDifferenceFaultEvent>? Trace { get; }

    /// <summary>The correlation id emitted fault events carry.</summary>
    private Guid CorrelationId { get; }

    /// <summary>The largest frame accepted or produced, in bytes.</summary>
    private int MaxFrameLength { get; }

    /// <summary>The frame codec every exchange of this client runs under.</summary>
    private DottedDifferenceFraming<DottedElement> Framing { get; }

    /// <summary>Creates a client over a connection factory. The dotted reconciliation contract is bound here, host-side, so the session seams never name it.</summary>
    /// <param name="openConnection">The seam that opens one fresh duplex connection per exchange.</param>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch, declared on every request header.</param>
    /// <param name="localAxis">The local host identity axis the tripwire guards.</param>
    /// <param name="readOwnAxisMaximum">The live own-axis maximum seam the tripwire compares against.</param>
    /// <param name="timeProvider">The clock fault-event timestamps are read from.</param>
    /// <param name="trace">The diagnostics sink fault events are emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id emitted fault events carry, linking them to the reconcile that drove the exchange.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <param name="decodeDrainWindow">The drain window an exhausted symbol stream grants the session's consumer before the exchange winds down as out of budget, or <see langword="null"/> for the default; a zero window winds down immediately and is sound only when the decode provably cannot complete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="openConnection"/>, <paramref name="readOwnAxisMaximum"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one, or <paramref name="decodeDrainWindow"/> is negative.</exception>
    public DottedDifferenceChannelClient(
        OpenPeerDottedConnectionDelegate openConnection,
        ulong dictionaryEpoch,
        ReplicaAxis localAxis,
        ReadOwnAxisMaximumDelegate readOwnAxisMaximum,
        TimeProvider timeProvider,
        TraceHandler<DottedDifferenceFaultEvent>? trace = null,
        Guid correlationId = default,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength,
        TimeSpan? decodeDrainWindow = null)
    {
        ArgumentNullException.ThrowIfNull(openConnection);
        ArgumentNullException.ThrowIfNull(readOwnAxisMaximum);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);
        if(decodeDrainWindow is { } window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero, nameof(decodeDrainWindow));
        }

        OpenConnection = openConnection;
        DictionaryEpoch = dictionaryEpoch;
        LocalAxis = localAxis;
        ReadOwnAxisMaximum = readOwnAxisMaximum;
        TimeProvider = timeProvider;
        DecodeDrainWindow = decodeDrainWindow ?? DefaultDecodeDrainWindow;
        Trace = trace;
        CorrelationId = correlationId;
        MaxFrameLength = maxFrameLength;
        Framing = new DottedDifferenceFraming<DottedElement>(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
    }

    /// <summary>Runs one dotted exchange over a pinned ledger projection. The counts on the returned outcome are zero; the host's adopt binding, which observed the committed effect, composes them on.</summary>
    /// <param name="projection">The pinned snapshot's projection the session reconciles.</param>
    /// <param name="symbolCap">The symbol ceiling that bounds a non-terminating decode into an abort; positive, at most <see cref="MaximumSymbolCap"/>.</param>
    /// <param name="resolveDifference">The initiator's classification seam: decoded items to fetch, push, and local drops, against the peer's EXCHANGED context.</param>
    /// <param name="applyElements">The seam that admits the fetch answer's entries to the local store and answers covered dots as push-drops.</param>
    /// <param name="applyDrops">The seam that drops the initiator's own observed-removed entries.</param>
    /// <param name="mergeContext">The terminal context-fold seam for quiescent paths.</param>
    /// <param name="pool">The pool the session rents from; the engine's governed pool.</param>
    /// <param name="cancellationToken">Cancels the exchange; propagates as itself.</param>
    /// <returns>The exchange's value outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolCap"/> is not positive or exceeds <see cref="MaximumSymbolCap"/>.</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The channel's documented fault posture: every fault except cancellation converts to a value outcome with the fault class on the trace, so a transport hiccup is never diagnosed as a peer defect and the caller always receives a named verdict.")]
    public async ValueTask<DottedReconcileOutcome> ExchangeAsync(
        DottedLedgerProjection projection,
        int symbolCap,
        ResolveReconciliationDifferenceDelegate<DottedElement> resolveDifference,
        ApplyReconciliationElementsDelegate<DottedElement> applyElements,
        ApplyReconciliationDropsDelegate<DottedElement> applyDrops,
        MergeReconciliationContextDelegate mergeContext,
        MemoryPool<byte> pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(resolveDifference);
        ArgumentNullException.ThrowIfNull(applyElements);
        ArgumentNullException.ThrowIfNull(applyDrops);
        ArgumentNullException.ThrowIfNull(mergeContext);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolCap);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(symbolCap, MaximumSymbolCap);

        PeerChannelConnection connection;
        try
        {
            connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(PeerServiceRefusedException)
        {
            //The peer's EXPLICIT refusal byte: its executable does not serve the dotted selector. The one
            //evidence for the unsupported outcome; an absent reply below reports peer-unavailable instead.
            return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.PeerRemoveAwareUnsupported);
        }
        catch(Exception)
        {
            EmitFault(DottedDifferenceFaultKind.Transport);

            return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.PeerUnavailable);
        }

        ExchangeState exchange = new(symbolCap, LocalAxis, ReadOwnAxisMaximum, TimeProvider, DecodeDrainWindow);
        try
        {
            return await RunExchangeAsync(connection, projection, exchange, resolveDifference, applyElements, applyDrops, mergeContext, pool, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(DottedAdoptConflictExhaustedException)
        {
            //The adopt seam's documented fail-closed signal: committed prefix commits stand; the named value
            //outcome tells the operator a re-run converges.
            EmitFault(DottedDifferenceFaultKind.ConflictExhausted);

            return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.ConflictExhausted);
        }
        catch(Exception exception)
        {
            //A malformed frame, a channel deserialization failure, or a session dispatch-rule violation is the
            //peer breaking protocol; everything else is the transport itself — the interrupted (durable-prefix)
            //outcome, since any progress already committed atomically.
            bool protocol = exception is InvalidDataException or InvalidOperationException or MessageDeserializationException;
            EmitFault(protocol ? DottedDifferenceFaultKind.Protocol : DottedDifferenceFaultKind.Transport);

            return DottedReconcileOutcome.ForKind(protocol ? DottedReconcileOutcomeKind.ProtocolFault : DottedReconcileOutcomeKind.Interrupted);
        }
    }

    /// <summary>Runs the connection-scoped exchange: header out, session concurrent with the inbound pump, post-join verdict, unconditional teardown.</summary>
    /// <param name="connection">The opened connection; disposed on every exit.</param>
    /// <param name="projection">The pinned snapshot's projection.</param>
    /// <param name="exchange">The exchange state the pump and the verdict share.</param>
    /// <param name="resolveDifference">The initiator's classification seam.</param>
    /// <param name="applyElements">The fetch-answer apply seam.</param>
    /// <param name="applyDrops">The local-drop apply seam.</param>
    /// <param name="mergeContext">The terminal context-fold seam.</param>
    /// <param name="pool">The pool the session rents from.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The exchange's value outcome.</returns>
    [SuppressMessage("Reliability", "CA2025:Do not pass 'IDisposable' instances into unawaited tasks", Justification = "Both tasks that use the session are joined in the try's finally — teardown is flagged, the connection is disposed (which unblocks a pump mid-read), and the pump is awaited — before the using scope disposes the session on every path, including a faulted or cancelled run; the analyzer does not model the await-bearing finally join.")]
    private async ValueTask<DottedReconcileOutcome> RunExchangeAsync(
        PeerChannelConnection connection,
        DottedLedgerProjection projection,
        ExchangeState exchange,
        ResolveReconciliationDifferenceDelegate<DottedElement> resolveDifference,
        ApplyReconciliationElementsDelegate<DottedElement> applyElements,
        ApplyReconciliationDropsDelegate<DottedElement> applyDrops,
        MergeReconciliationContextDelegate mergeContext,
        MemoryPool<byte> pool,
        CancellationToken cancellationToken)
    {
        await using(connection.ConfigureAwait(false))
        {
            MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer = new(connection.RequestWriter, Framing.WriteFrame, MaxFrameLength);
            MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader = new(connection.ResponseReader, Framing.ReadFrame, MaxFrameLength);

            await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForRequestHeader(new DottedDifferenceRequestHeader(DictionaryEpoch, ReconciliationOffer.FromContract(DottedReconciliationContract.Value), exchange.SymbolCap)), cancellationToken).ConfigureAwait(false);

            using AntiEntropySession<DottedElement> session = new(AntiEntropyRole.Initiator, DottedReconciliationContract.Value, projection.Projection.Items, DefaultBatchSize, pool, projection.Projection.Context);
            EnvelopeSendBinding send = new(writer, exchange);
            Task run = session.RunAsync(send.SendAsync, resolveDifference, serveFetch: null, applyElements, applyDrops, mergeContext, cancellationToken);
            Task pump = exchange.PumpAsync(session, reader, cancellationToken);

            //The run joins FIRST: the pump always winds the session down on its own exit (decline, tripwire,
            //cap, channel end, fault — its finally completes the session), so the join cannot hang past the
            //pump's action. The finally joins the PUMP on every path — including a faulted or cancelled run —
            //after flagging teardown and tearing the connection down, which is what ends a read blocked on a
            //stream the peer keeps open; the disposal-induced read fault is the pump's normal end once teardown
            //is flagged. Both tasks are therefore complete before the session's using scope disposes it.
            try
            {
                await run.ConfigureAwait(false);
            }
            finally
            {
                exchange.RequestTeardown();
                await connection.DisposeAsync().ConfigureAwait(false);
                await pump.ConfigureAwait(false);
            }

            if(exchange.IdentityCollisionDetected)
            {
                EmitFault(DottedDifferenceFaultKind.IdentityCollision);

                return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.IdentityCollision);
            }

            if(exchange.Declined is { } reason)
            {
                return new DottedReconcileOutcome(DottedReconcileOutcomeKind.PeerDeclined, reason, 0, 0, 0, 0);
            }

            //The post-join verdict (never a pre-join snapshot): an in-flight batch that completed the decode
            //reports completion here even when the pump's counter had already reached the cap.
            bool completed = session.State == AntiEntropySessionState.Completed && session.IsConverged;
            if(!completed)
            {
                return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.Interrupted);
            }

            return DottedReconcileOutcome.ForKind(session.DecodedItems.Count == 0 ? DottedReconcileOutcomeKind.AlreadyConsistent : DottedReconcileOutcomeKind.Converged);
        }
    }

    /// <summary>Emits one fault event naming the exchange's fault class, when a sink is attached.</summary>
    /// <param name="kind">The fault's class.</param>
    private void EmitFault(DottedDifferenceFaultKind kind)
    {
        if(Trace is null)
        {
            return;
        }

        DottedDifferenceFaultEvent evt = new(0, TimeProvider.GetUtcNow().UtcTicks, CorrelationId, kind);
        Trace(in evt);
    }

    /// <summary>Binds the session's outbound edge to the frame writer as a method group, so the send seam carries no closure — and observes the session's own DONE send, the exact decode-completion signal the pump's cap decision reads.</summary>
    /// <param name="writer">The channel writer envelope frames are written through.</param>
    /// <param name="exchange">The exchange state the decode-completion observation lands on.</param>
    private sealed class EnvelopeSendBinding(MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer, ExchangeState exchange)
    {
        /// <summary>The channel writer envelope frames are written through.</summary>
        private MessageChannelWriter<DottedDifferenceFrame<DottedElement>> Writer { get; } = writer;

        /// <summary>The exchange state the decode-completion observation lands on.</summary>
        private ExchangeState Exchange { get; } = exchange;

        /// <summary>Wraps one envelope as a frame and writes it, marking the exchange decode-complete when the envelope is the session's done signal.</summary>
        /// <param name="envelope">The envelope to send.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the frame is flushed.</returns>
        public ValueTask SendAsync(ReconciliationEnvelope<DottedElement> envelope, CancellationToken cancellationToken)
        {
            if(envelope.Done is not null)
            {
                Exchange.MarkDecodeCompleted();
            }

            return Writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForEnvelope(envelope), cancellationToken);
        }
    }

    /// <summary>
    /// The inbound pump and the state it shares with the verdict: the peer's decline, the tripwire's finding,
    /// the symbols submitted, and the wind-down triggers. The pump's finally ALWAYS winds the session down, so
    /// the run task the caller joins can never hang past the pump's exit; the caller reads the shared state
    /// only after joining both tasks.
    /// </summary>
    /// <param name="symbolCap">The symbol ceiling the exchange is bounded by.</param>
    /// <param name="localAxis">The local host identity axis the tripwire guards.</param>
    /// <param name="readOwnAxisMaximum">The live own-axis maximum seam.</param>
    /// <param name="timeProvider">The clock the out-of-budget drain window is measured on.</param>
    /// <param name="decodeDrainWindow">The drain window an exhausted symbol stream grants the session's consumer.</param>
    private sealed class ExchangeState(int symbolCap, ReplicaAxis localAxis, ReadOwnAxisMaximumDelegate readOwnAxisMaximum, TimeProvider timeProvider, TimeSpan decodeDrainWindow)
    {
        /// <summary>The drain window an exhausted symbol stream grants the session's consumer before the exchange is wound down as out of budget: a completing decode announces itself (the DONE send) in microseconds once the queued batches drain, so the window is generous scheduling slack that only a genuinely out-of-budget decode ever runs out.</summary>
        private TimeSpan DecodeDrainWindow { get; } = decodeDrainWindow;

        /// <summary>The symbol ceiling the exchange is bounded by.</summary>
        public int SymbolCap { get; } = symbolCap;

        /// <summary>The local host identity axis the tripwire guards.</summary>
        private ReplicaAxis LocalAxis { get; } = localAxis;

        /// <summary>The live own-axis maximum seam the tripwire compares against.</summary>
        private ReadOwnAxisMaximumDelegate ReadOwnAxisMaximum { get; } = readOwnAxisMaximum;

        /// <summary>The clock the out-of-budget drain window is measured on.</summary>
        private TimeProvider TimeProvider { get; } = timeProvider;

        /// <summary>The peer's named decline reason from a declined reply header, or <see langword="null"/> when the peer accepted (or never replied).</summary>
        public DottedDifferenceDeclineReason? Declined { get; private set; }

        /// <summary>Whether the tripwire found peer coverage or a dot beyond the local axis's own maximum; the offending frame was not submitted.</summary>
        public bool IdentityCollisionDetected { get; private set; }

        /// <summary>The symbols submitted to the session so far, counted at batch granularity.</summary>
        public int AbsorbedSymbols { get; private set; }

        /// <summary>Whether the owner has requested teardown; written with volatile semantics on the main flow before the connection is disposed, read by the pump after a read fault, so a teardown-induced fault is distinguishable from a peer violating the protocol.</summary>
        private int teardownRequested;

        /// <summary>The decode-completion signal: completed when the session sends its DONE — the exact decode-completion fact, observed at the send binding by the session's consumer and awaited by the pump's out-of-budget decision.</summary>
        private TaskCompletionSource DecodeCompletedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Flags that the owner is tearing the connection down, so the pump's next read fault is its normal end.</summary>
        public void RequestTeardown()
        {
            Volatile.Write(ref teardownRequested, 1);
        }

        /// <summary>Records that the session sent its DONE signal: the decode recovered the whole difference, so the out-of-budget bound no longer applies and the pump keeps serving the exchange's remaining legs.</summary>
        public void MarkDecodeCompleted()
        {
            DecodeCompletedSignal.TrySetResult();
        }

        /// <summary>Grants the session's consumer the drain window over the exhausted symbol stream: a completing decode announces itself (the DONE send) as soon as the queued batches drain, without a spin; only a decode that stays silent through the whole window is out of budget.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns><see langword="true"/> when the decode completed within the window; <see langword="false"/> to wind down as the bounded refusal.</returns>
        private async ValueTask<bool> WaitForDecodeDrainAsync(CancellationToken cancellationToken)
        {
            Task completed = await Task.WhenAny(DecodeCompletedSignal.Task, Task.Delay(DecodeDrainWindow, TimeProvider, cancellationToken)).ConfigureAwait(false);

            return completed == DecodeCompletedSignal.Task;
        }

        /// <summary>Whether the session's DONE signal has been sent.</summary>
        private bool DecodeCompleted
        {
            get
            {
                return DecodeCompletedSignal.Task.IsCompleted;
            }
        }

        /// <summary>Pumps inbound frames into the session until the reply declines, the tripwire fires, the cap trips, the channel ends, or the session stops accepting; the finally winds the session down on every exit.</summary>
        /// <param name="session">The initiator session inbound envelopes are submitted to.</param>
        /// <param name="reader">The channel reader inbound frames are read from.</param>
        /// <param name="cancellationToken">Cancels the pump.</param>
        /// <returns>A task that completes when the pump exits.</returns>
        public async Task PumpAsync(AntiEntropySession<DottedElement> session, MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader, CancellationToken cancellationToken)
        {
            try
            {
                await PumpFramesAsync(session, reader, cancellationToken).ConfigureAwait(false);
            }
            catch when(Volatile.Read(ref teardownRequested) != 0)
            {
                //The owner tore the connection down under the pump's in-flight read: the read fault IS the end
                //of the stream, not a peer misbehaving. A fault that precedes the teardown still propagates.
            }
            finally
            {
                session.Complete();
            }
        }

        /// <summary>The pump loop proper; see <see cref="PumpAsync"/> for the exit contract.</summary>
        /// <param name="session">The initiator session inbound envelopes are submitted to.</param>
        /// <param name="reader">The channel reader inbound frames are read from.</param>
        /// <param name="cancellationToken">Cancels the pump.</param>
        /// <returns>A task that completes when the loop exits.</returns>
        /// <exception cref="InvalidDataException">The peer violated the channel protocol: an envelope before the reply header, a second reply header, or an inbound request header.</exception>
        private async Task PumpFramesAsync(AntiEntropySession<DottedElement> session, MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader, CancellationToken cancellationToken)
        {
            bool replySeen = false;
            await foreach(DottedDifferenceFrame<DottedElement> frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if(frame.ReplyHeader is { } reply)
                {
                    if(replySeen)
                    {
                        throw new InvalidDataException("A dotted-difference peer sent a second reply header.");
                    }

                    replySeen = true;
                    if(!reply.Accepted)
                    {
                        Declined = reply.DeclineReason;

                        return;
                    }

                    continue;
                }

                if(frame.Envelope is not { } envelope)
                {
                    throw new InvalidDataException("A dotted-difference peer sent a request header on the response stream.");
                }

                if(!replySeen)
                {
                    throw new InvalidDataException("A dotted-difference peer sent an envelope before its reply header.");
                }

                //The tripwire runs BEFORE the session sees the frame, so colliding causal knowledge is refused
                //by name and never applied; the pump's exit winds the session down.
                if(DottedIdentityTripwire.Violates(envelope, LocalAxis, ReadOwnAxisMaximum))
                {
                    IdentityCollisionDetected = true;

                    return;
                }

                if(envelope.Symbols is { } batch)
                {
                    AbsorbedSymbols += batch.Symbols.Length;
                }

                try
                {
                    await session.SubmitAsync(envelope, cancellationToken).ConfigureAwait(false);
                }
                catch(ChannelClosedException)
                {
                    //The session stopped accepting (already wound down): the pump's work is over.
                    return;
                }

                //The non-terminating-decode bound: the responder's whole trigger stream is FINITE — sized from
                //the requested cap plus its spare batch — so once more than the cap plus one batch has been
                //submitted (a threshold within the responder's guaranteed stream total for every cap), no
                //further inbound frame can arrive until this side's decode speaks. The session's consumer may
                //still be draining the queued batches, so the wind-down decision waits on the decode-completion
                //signal — the session's own DONE send, the exact decode-completion fact — under the drain
                //window: a completing decode flips the exchange back to normal pumping, its fetch answer still
                //coming behind the stragglers the session ignores, and only a decode that stays silent through
                //the window winds down here, the bounded refusal the cap promises. The post-join verdict still
                //decides the final word.
                if(AbsorbedSymbols >= SymbolCap + DottedDifferenceChannelClient.DefaultBatchSize && !DecodeCompleted && !await WaitForDecodeDrainAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
    }
}
