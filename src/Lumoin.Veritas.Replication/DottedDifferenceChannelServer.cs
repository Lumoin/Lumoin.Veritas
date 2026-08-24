using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
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
/// Serves one dotted-difference exchange to a peer over a duplex connection: reads the request header, answers
/// with this endpoint's OWN epoch and contract declaration (never an echo) plus a named decline reason when it
/// refuses, and — on accept — runs the remove-aware responder session over a freshly pinned ledger projection,
/// streaming symbol batches and applying the initiator's pushes and drops through the host's adopt seams as
/// durable, causally self-consistent commits. One connection carries exactly one exchange; the peer tearing
/// the connection down is the NORMAL end of serve, never an error. An active-active replica accepts these
/// beside its sketch and shard serves; the accept loop that dispatches connections isolates per-connection
/// faults.
/// </summary>
/// <remarks>
/// The serve declines — reply header with this endpoint's declarations and the named reason, nothing following
/// — on: an out-of-range symbol cap, a dictionary-epoch mismatch, a declared contract differing from this
/// endpoint's own, or the endpoint's standing refusal (not remove-aware, or no durable journal), which the
/// decline-mode constructor pins. The identity-collision tripwire inspects every inbound envelope before the
/// session sees it and winds the serve down with the named fault on the trace; an exhausted adopt write-back
/// ends the serve the same way, with the committed prefix standing.
/// </remarks>
public sealed class DottedDifferenceChannelServer
{
    /// <summary>This endpoint's dictionary epoch; a request declaring another is declined.</summary>
    private ulong DictionaryEpoch { get; }

    /// <summary>The standing refusal every request is answered with in decline mode; the absent reason on a serving endpoint.</summary>
    private DottedDifferenceDeclineReason StandingRefusal { get; }

    /// <summary>The seam supplying one pinned serve binding per accepted serve; <see langword="null"/> in decline mode, which accepts none.</summary>
    private ProvideDottedServeBindingDelegate? ProvideBinding { get; }

    /// <summary>The local host identity axis the tripwire guards; unread in decline mode.</summary>
    private ReplicaAxis LocalAxis { get; }

    /// <summary>The live own-axis maximum seam the tripwire compares against; <see langword="null"/> in decline mode, whose serves end before any envelope.</summary>
    private ReadOwnAxisMaximumDelegate? ReadOwnAxisMaximum { get; }

    /// <summary>The pool the responder sessions rent from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>The clock fault-event timestamps are read from.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The diagnostics sink fault events are emitted to, or <see langword="null"/> to emit nothing.</summary>
    private TraceHandler<DottedDifferenceFaultEvent>? Trace { get; }

    /// <summary>The correlation id emitted fault events carry.</summary>
    private Guid CorrelationId { get; }

    /// <summary>The largest frame accepted or produced, in bytes.</summary>
    private int MaxFrameLength { get; }

    /// <summary>The frame codec every serve of this endpoint runs under.</summary>
    private DottedDifferenceFraming<DottedElement> Framing { get; }

    /// <summary>Creates a SERVING endpoint over the host's per-serve binding seam.</summary>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch; a request declaring another is declined.</param>
    /// <param name="provideBinding">The seam supplying one pinned serve binding — the projection and its snapshot-bound apply seams — per accepted serve.</param>
    /// <param name="localAxis">The local host identity axis the tripwire guards.</param>
    /// <param name="readOwnAxisMaximum">The live own-axis maximum seam the tripwire compares against.</param>
    /// <param name="pool">The pool the responder sessions rent from; the engine's governed pool.</param>
    /// <param name="timeProvider">The clock fault-event timestamps are read from.</param>
    /// <param name="trace">The diagnostics sink fault events are emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id emitted fault events carry.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <exception cref="ArgumentNullException">A required seam is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public DottedDifferenceChannelServer(
        ulong dictionaryEpoch,
        ProvideDottedServeBindingDelegate provideBinding,
        ReplicaAxis localAxis,
        ReadOwnAxisMaximumDelegate readOwnAxisMaximum,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        TraceHandler<DottedDifferenceFaultEvent>? trace = null,
        Guid correlationId = default,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(provideBinding);
        ArgumentNullException.ThrowIfNull(readOwnAxisMaximum);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        DictionaryEpoch = dictionaryEpoch;
        StandingRefusal = DottedDifferenceDeclineReason.None;
        ProvideBinding = provideBinding;
        LocalAxis = localAxis;
        ReadOwnAxisMaximum = readOwnAxisMaximum;
        Pool = pool;
        TimeProvider = timeProvider;
        Trace = trace;
        CorrelationId = correlationId;
        MaxFrameLength = maxFrameLength;
        Framing = new DottedDifferenceFraming<DottedElement>(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
    }

    /// <summary>Creates a DECLINE-MODE endpoint that answers every request with the standing named refusal — the shape a host serves when its store is not remove-aware or keeps no durable journal, so the operator on the other end sees a name, never a silent close.</summary>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch, still declared honestly on the decline.</param>
    /// <param name="standingRefusal">The named refusal every request is answered with; never the absent reason.</param>
    /// <param name="pool">The pool the framing rents from.</param>
    /// <param name="timeProvider">The clock fault-event timestamps are read from.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <exception cref="ArgumentException"><paramref name="standingRefusal"/> is the absent reason.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public DottedDifferenceChannelServer(
        ulong dictionaryEpoch,
        DottedDifferenceDeclineReason standingRefusal,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        if(standingRefusal == DottedDifferenceDeclineReason.None)
        {
            throw new ArgumentException("A decline-mode dotted-difference server needs a real standing refusal; the absent reason names nothing.", nameof(standingRefusal));
        }

        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        DictionaryEpoch = dictionaryEpoch;
        StandingRefusal = standingRefusal;
        Pool = pool;
        TimeProvider = timeProvider;
        MaxFrameLength = maxFrameLength;
        Framing = new DottedDifferenceFraming<DottedElement>(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
    }

    /// <summary>Serves one connection: the header exchange, then — on accept — the responder session until the peer's side ends. The response side is completed on every exit so the peer's reader always observes the channel ending; a serve fault still propagates after the completion, for the accept loop to isolate.</summary>
    /// <param name="requestReader">The pipe request frames are read from.</param>
    /// <param name="responseWriter">The pipe response frames are written to.</param>
    /// <param name="cancellationToken">Cancels the serve.</param>
    /// <returns>A task that completes when the connection's exchange ends and the response side is completed.</returns>
    public async Task ServeAsync(PipeReader requestReader, PipeWriter responseWriter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);

        MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader = new(requestReader, Framing.ReadFrame, MaxFrameLength);
        MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer = new(responseWriter, Framing.WriteFrame, MaxFrameLength);
        try
        {
            await ServeExchangeAsync(reader, writer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Runs the one exchange behind the completed-on-every-exit response discipline.</summary>
    /// <param name="reader">The channel reader request frames arrive on.</param>
    /// <param name="writer">The channel writer the reply header and session envelopes leave on.</param>
    /// <param name="cancellationToken">Cancels the serve.</param>
    /// <returns>A task that completes when the exchange ends.</returns>
    /// <exception cref="InvalidDataException">The first frame is not a request header, or a later frame is not an envelope.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session is disposed unconditionally in the finally's own nested finally, after the run join, on every path including a join that throws; the analyzer does not model the await-bearing nested finally.")]
    private async Task ServeExchangeAsync(
        MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader,
        MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer,
        CancellationToken cancellationToken)
    {
        AntiEntropySession<DottedElement>? session = null;
        Task? run = null;
        try
        {
            //The trigger budget covers the whole cap in batches plus one spare, so the stream can carry the cap
            //and the client's own counter is what stops an exchange the decode never completes.
            int triggerBudget = 0;
            await foreach(DottedDifferenceFrame<DottedElement> frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if(session is null)
                {
                    if(frame.RequestHeader is not { } request)
                    {
                        throw new InvalidDataException("A dotted-difference connection must open with a request header.");
                    }

                    DottedDifferenceDeclineReason reason = EvaluateRequest(request);
                    bool accepted = reason == DottedDifferenceDeclineReason.None;
                    await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForReplyHeader(new DottedDifferenceReplyHeader(accepted, DictionaryEpoch, ReconciliationOffer.FromContract(DottedReconciliationContract.Value), reason)), cancellationToken).ConfigureAwait(false);
                    if(!accepted)
                    {
                        return;
                    }

                    DottedDifferenceServeBinding binding = ProvideBinding!();
                    session = new AntiEntropySession<DottedElement>(AntiEntropyRole.Responder, DottedReconciliationContract.Value, binding.Projection.Projection.Items, DottedDifferenceChannelClient.DefaultBatchSize, Pool, binding.Projection.Projection.Context);
                    triggerBudget = ((request.SymbolCap + DottedDifferenceChannelClient.DefaultBatchSize - 1) / DottedDifferenceChannelClient.DefaultBatchSize) + 1;
                    FetchServeBinding serveBinding = new(binding.Projection);
                    EnvelopeSendBinding send = new(writer);
                    run = session.RunAsync(send.SendAsync, resolveDifference: null, serveFetch: serveBinding.Serve, binding.ApplyElements, binding.ApplyDrops, binding.MergeContext, cancellationToken);

                    continue;
                }

                if(frame.Envelope is not { } envelope)
                {
                    throw new InvalidDataException("A dotted-difference connection carries one request header, then envelopes only.");
                }

                //The tripwire runs BEFORE the session sees the frame, so colliding causal knowledge is refused
                //by name and never applied; winding the serve down is the refusal, and the peer observes its
                //connection ending.
                if(DottedIdentityTripwire.Violates(envelope, LocalAxis, ReadOwnAxisMaximum!))
                {
                    EmitFault(DottedDifferenceFaultKind.IdentityCollision);

                    break;
                }

                bool isOffer = envelope.Offer is not null;
                try
                {
                    await session.SubmitAsync(envelope, cancellationToken).ConfigureAwait(false);

                    //The batch triggers enqueue BEHIND the peer's offer on the session's ordered work channel, so
                    //the offer pins the contract first and every trigger then streams while reconciling; triggers
                    //dispatched after the peer's done signal no-op by the session's own phase guard.
                    if(isOffer)
                    {
                        for(int i = 0; i < triggerBudget; i++)
                        {
                            await session.TriggerBatchAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch(ChannelClosedException)
                {
                    break;
                }
            }
        }
        finally
        {
            if(session is not null)
            {
                //The session disposes even when the run join throws (a peer violating the session protocol
                //propagates out of the responder's run), so no pooled cell store leaks on the fault path. An
                //exhausted adopt write-back ends the serve as a NAMED fault with the committed prefix standing,
                //not a propagating exception — the initiator re-runs and converges.
                try
                {
                    session.Complete();
                    if(run is not null)
                    {
                        try
                        {
                            await run.ConfigureAwait(false);
                        }
                        catch(DottedAdoptConflictExhaustedException)
                        {
                            EmitFault(DottedDifferenceFaultKind.ConflictExhausted);
                        }
                    }
                }
                finally
                {
                    session.Dispose();
                }
            }
        }
    }

    /// <summary>Evaluates one request against this endpoint's standing refusal and declarations, answering the named decline reason — the absent reason exactly when the exchange may proceed.</summary>
    /// <param name="request">The peer's request header.</param>
    /// <returns>The decline reason, or the absent reason to accept.</returns>
    private DottedDifferenceDeclineReason EvaluateRequest(DottedDifferenceRequestHeader request)
    {
        if(StandingRefusal != DottedDifferenceDeclineReason.None)
        {
            return StandingRefusal;
        }

        if(request.SymbolCap <= 0 || request.SymbolCap > DottedDifferenceChannelClient.MaximumSymbolCap)
        {
            return DottedDifferenceDeclineReason.SymbolCapInvalid;
        }

        if(request.DictionaryEpoch != DictionaryEpoch)
        {
            return DottedDifferenceDeclineReason.EpochMismatch;
        }

        if(!request.Declaration.Matches(DottedReconciliationContract.Value))
        {
            return DottedDifferenceDeclineReason.ContractMismatch;
        }

        return DottedDifferenceDeclineReason.None;
    }

    /// <summary>Emits one fault event naming the serve's fault class, when a sink is attached.</summary>
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

    /// <summary>Serves the initiator's fetch from the pinned projection as a bound method group: every requested item resolves to the dotted entry that produced it, and a miss fails the serve closed — the snapshot produced the coded stream, so an unresolvable item is a protocol violation, never a silent gap.</summary>
    /// <param name="projection">The pinned projection the serve resolves against.</param>
    private sealed class FetchServeBinding(DottedLedgerProjection projection)
    {
        /// <summary>The pinned projection the serve resolves against.</summary>
        private DottedLedgerProjection Projection { get; } = projection;

        /// <summary>Resolves the requested items to their dotted entries. The concrete list return binds covariantly to the fetch-serve delegate's read-only view.</summary>
        /// <param name="items">The requested items.</param>
        /// <returns>One entry per requested item.</returns>
        /// <exception cref="InvalidOperationException">An item does not resolve against the pinned snapshot.</exception>
        public List<ReconciliationElementEntry<DottedElement>> Serve(IReadOnlyList<ReadOnlyMemory<byte>> items)
        {
            List<ReconciliationElementEntry<DottedElement>> served = new(items.Count);
            foreach(ReadOnlyMemory<byte> item in items)
            {
                if(!Projection.Projection.TryResolve(item, out DottedElement? entry))
                {
                    throw new InvalidOperationException("A dotted-difference fetch named an item the pinned snapshot did not produce; the exchange fails closed rather than serving a gap.");
                }

                served.Add(new ReconciliationElementEntry<DottedElement>(item, entry));
            }

            return served;
        }
    }

    /// <summary>Binds the session's outbound edge to the frame writer as a method group, so the send seam carries no closure.</summary>
    /// <param name="writer">The channel writer envelope frames are written through.</param>
    private sealed class EnvelopeSendBinding(MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer)
    {
        /// <summary>The channel writer envelope frames are written through.</summary>
        private MessageChannelWriter<DottedDifferenceFrame<DottedElement>> Writer { get; } = writer;

        /// <summary>Wraps one envelope as a frame and writes it.</summary>
        /// <param name="envelope">The envelope to send.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the frame is flushed.</returns>
        public ValueTask SendAsync(ReconciliationEnvelope<DottedElement> envelope, CancellationToken cancellationToken)
        {
            return Writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForEnvelope(envelope), cancellationToken);
        }
    }
}
