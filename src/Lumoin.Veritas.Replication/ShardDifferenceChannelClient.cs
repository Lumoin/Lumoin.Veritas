using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Runs one shard's add-only reconciliation against a peer over a real wire — the transport binding behind
/// <see cref="FetchPeerShardDifferenceDelegate"/>. Each fetch opens one fresh connection, exchanges the
/// header pair (the request carries the driving policy's fingerprint and this endpoint's epoch; the reply
/// carries the PEER'S OWN declaration), then drives the initiator session over the envelope stream until the
/// decode completes, the symbol cap trips, or the peer declines. The connection is disposed unconditionally
/// on every exit — teardown, not session wind-down, is what releases a backpressure-blocked peer.
/// </summary>
/// <remarks>
/// <para>
/// FAULT POSTURE: every fault except cancellation — a torn transport, a malformed frame, a peer violating the
/// session protocol — converts to a VALUE DECLINE: the result carries whatever fingerprint the peer declared
/// (<see langword="null"/> when the header never arrived, which the rung refuses as
/// <see cref="ShardedRepairOutcome.PeerUndeclared"/> ahead of the fingerprint comparison), Completed is
/// <see langword="false"/>, and one <see cref="ShardDifferenceFaultEvent"/> names the fault class on the
/// trace. A transport hiccup therefore never aborts a viable repair round and is never diagnosed as a policy
/// mismatch. Cancellation propagates as itself.
/// </para>
/// <para>
/// CAP DISCIPLINE: absorbed symbols are counted at batch granularity as they are submitted. The responder's
/// whole trigger stream is finite — sized from the requested cap plus its spare batch — so once more than the
/// cap plus one batch has been submitted, no further inbound frame can arrive until this side's decode speaks;
/// the wind-down decision then waits on the decode-completion signal (the session's own DONE send, observed at
/// the send binding) under the drain window, so a completing decode is never aborted while its queued batches
/// drain, and only a decode that stays silent through the whole window winds down as the bounded refusal. The
/// verdict is read only AFTER the session's run is joined. A declared fingerprint differing from the local one
/// skips the exchange fast-path (the rung's comparison stays the authority); decoded items are copied to owned
/// heap arrays before the session disposes, because the decoder's arena returns to the pool on dispose.
/// </para>
/// </remarks>
public sealed class ShardDifferenceChannelClient
{
    /// <summary>The per-trigger symbol batch both endpoints default to: one wire frame well under any pipe threshold, and the cap-overshoot bound.</summary>
    public const int DefaultBatchSize = 64;

    /// <summary>The largest symbol cap either endpoint accepts: the ceiling keeps the responder's trigger-budget arithmetic and the client's wind-down threshold — each of which adds up to two batches to the cap — inside the integer range, so a hostile or mistaken cap can never wrap into a hung or falsely wound-down exchange.</summary>
    public const int MaximumSymbolCap = int.MaxValue - (2 * DefaultBatchSize);

    /// <summary>The default drain window an exhausted symbol stream grants the session's consumer before the exchange winds down as out of budget.</summary>
    private static TimeSpan DefaultDecodeDrainWindow { get; } = TimeSpan.FromSeconds(5);

    private readonly OpenPeerShardConnectionDelegate openConnection;
    private readonly ulong dictionaryEpoch;
    private readonly int maxFrameLength;
    private readonly TraceHandler<ShardDifferenceFaultEvent>? trace;
    private readonly Guid correlationId;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan decodeDrainWindow;
    private readonly ShardDifferenceFraming<ReadOnlyMemory<byte>> framing;

    /// <summary>Creates a client over a connection factory. The structural reconciliation contract is bound here, host-side, so the core coordinator never names it.</summary>
    /// <param name="openConnection">The seam that opens one fresh duplex connection per shard fetch.</param>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch, stamped on every request header; Core's <see langword="long"/> epoch converts by raw bit reinterpretation.</param>
    /// <param name="timeProvider">The clock fault-event timestamps are read from and the drain window is measured on.</param>
    /// <param name="trace">The diagnostics sink declined-fetch fault events are emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id emitted fault events carry, linking them to the repair round.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <param name="decodeDrainWindow">The drain window an exhausted symbol stream grants the session's consumer before the exchange winds down as out of budget, or <see langword="null"/> for the default; a zero window winds down immediately and is sound only when the decode provably cannot complete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="openConnection"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one, or <paramref name="decodeDrainWindow"/> is negative.</exception>
    public ShardDifferenceChannelClient(
        OpenPeerShardConnectionDelegate openConnection,
        ulong dictionaryEpoch,
        TimeProvider timeProvider,
        TraceHandler<ShardDifferenceFaultEvent>? trace = null,
        Guid correlationId = default,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength,
        TimeSpan? decodeDrainWindow = null)
    {
        ArgumentNullException.ThrowIfNull(openConnection);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);
        if(decodeDrainWindow is { } window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero, nameof(decodeDrainWindow));
        }

        this.openConnection = openConnection;
        this.dictionaryEpoch = dictionaryEpoch;
        this.timeProvider = timeProvider;
        this.trace = trace;
        this.correlationId = correlationId;
        this.maxFrameLength = maxFrameLength;
        this.decodeDrainWindow = decodeDrainWindow ?? DefaultDecodeDrainWindow;
        framing = new ShardDifferenceFraming<ReadOnlyMemory<byte>>(StructuralReconciliationContract.Value);
    }

    /// <summary>Runs one shard's exchange — the <see cref="FetchPeerShardDifferenceDelegate"/> the sharded source binds.</summary>
    /// <param name="shardIndex">The shard being reconciled.</param>
    /// <param name="localFingerprint">The driving policy's fingerprint, transmitted on the request header.</param>
    /// <param name="localShardItems">The shard's local operand, pinned as the session's snapshot.</param>
    /// <param name="symbolCap">The symbol ceiling that bounds a non-terminating decode into an abort; positive, at most <see cref="MaximumSymbolCap"/>.</param>
    /// <param name="pool">The pool the session rents from; the engine's governed pool.</param>
    /// <param name="cancellationToken">Cancels the shard exchange; propagates as itself.</param>
    /// <returns>The shard's decoded difference, completion status, and the peer's declared fingerprint (or <see langword="null"/> when the peer never declared).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolCap"/> is not positive or exceeds <see cref="MaximumSymbolCap"/>.</exception>
    public async ValueTask<ShardReconcileResult> FetchShardDifferenceAsync(
        int shardIndex,
        ShardPolicyFingerprint localFingerprint,
        IReadOnlyList<ReadOnlyMemory<byte>> localShardItems,
        int symbolCap,
        MemoryPool<byte> pool,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolCap);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(symbolCap, MaximumSymbolCap);

        ExchangeState exchange = new(symbolCap, localFingerprint, timeProvider, decodeDrainWindow);
        try
        {
            return await RunExchangeAsync(shardIndex, localShardItems, pool, exchange, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception exception)
        {
            //A malformed frame, a channel deserialization failure, or a session dispatch-rule violation is the
            //peer breaking protocol; everything else is the transport itself. Both convert to the same value
            //decline — only the named class differs.
            EmitFault(shardIndex, exception is InvalidDataException or InvalidOperationException or MessageDeserializationException ? ShardDifferenceFaultKind.Protocol : ShardDifferenceFaultKind.Transport);

            return new ShardReconcileResult(shardIndex, exchange.DeclaredFingerprint, [], Completed: false, exchange.AbsorbedSymbols);
        }
    }

    /// <summary>Runs the connection-scoped exchange: header out, session concurrent with the inbound pump, post-join verdict, unconditional teardown.</summary>
    /// <param name="shardIndex">The shard being reconciled.</param>
    /// <param name="localShardItems">The shard's local operand.</param>
    /// <param name="pool">The pool the session rents from.</param>
    /// <param name="exchange">The exchange state the pump and the verdict share.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The shard's result.</returns>
    [SuppressMessage("Reliability", "CA2025:Do not pass 'IDisposable' instances into unawaited tasks", Justification = "Both tasks that use the session are joined in the try's finally — teardown is flagged, the connection is disposed (which unblocks a pump mid-read), and the pump is awaited — before the using scope disposes the session on every path, including a faulted or cancelled run; the analyzer does not model the await-bearing finally join.")]
    private async ValueTask<ShardReconcileResult> RunExchangeAsync(
        int shardIndex,
        IReadOnlyList<ReadOnlyMemory<byte>> localShardItems,
        MemoryPool<byte> pool,
        ExchangeState exchange,
        CancellationToken cancellationToken)
    {
        PeerChannelConnection connection = await openConnection(shardIndex, cancellationToken).ConfigureAwait(false);
        await using(connection.ConfigureAwait(false))
        {
            MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> writer = new(connection.RequestWriter, framing.WriteFrame, maxFrameLength);
            MessageChannelReader<ShardDifferenceFrame<ReadOnlyMemory<byte>>> reader = new(connection.ResponseReader, framing.ReadFrame, maxFrameLength);

            await writer.WriteAsync(ShardDifferenceFrame<ReadOnlyMemory<byte>>.ForRequestHeader(new ShardDifferenceRequestHeader(shardIndex, exchange.LocalFingerprint, dictionaryEpoch, exchange.SymbolCap)), cancellationToken).ConfigureAwait(false);

            using AntiEntropySession<ReadOnlyMemory<byte>> session = new(AntiEntropyRole.Initiator, StructuralReconciliationContract.Value, localShardItems, DefaultBatchSize, pool);
            EnvelopeSendBinding send = new(writer, exchange);
            Task run = session.RunAsync(send.SendAsync, ResolveEmptyDifference, cancellationToken: cancellationToken);
            Task pump = exchange.PumpAsync(session, reader, cancellationToken);

            //The run joins FIRST: the pump always winds the session down on its own exit (cap, decline, channel
            //end, fault — its finally completes the session), so the join cannot hang past the pump's action.
            //The finally joins the PUMP on every path — including a faulted or cancelled run — after flagging
            //teardown and tearing the connection down, which is what ends a read blocked on a stream the peer
            //keeps open; the disposal-induced read fault is the pump's normal end once teardown is flagged.
            //Both tasks are therefore complete before the session's using scope disposes it on any path.
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

            //The post-join verdict (never a pre-join snapshot): an in-flight batch that completed the decode
            //reports completion here even when the pump's counter had already reached the cap.
            bool completed = session.State == AntiEntropySessionState.Completed && session.IsConverged;
            IReadOnlyList<ReadOnlyMemory<byte>> decoded = session.DecodedItems;
            ReadOnlyMemory<byte>[] items = new ReadOnlyMemory<byte>[completed ? decoded.Count : 0];
            for(int i = 0; i < items.Length; i++)
            {
                //Copied to owned heap arrays BEFORE the session disposes: the decoder's arena returns to the
                //pool on dispose, so a later read would view repurposed rental memory with no exception.
                items[i] = decoded[i].ToArray();
            }

            return new ShardReconcileResult(shardIndex, exchange.DeclaredFingerprint, items, completed, exchange.AbsorbedSymbols);
        }
    }

    /// <summary>The add-only difference resolution: nothing is fetched and nothing is pushed — the rung resolves direction and re-ingests itself. This resolver MUST stay side-effect-free: the session hands it decoded items before the rung's fingerprint check runs.</summary>
    /// <param name="decodedItems">The decoded symmetric difference; unread.</param>
    /// <param name="peerContext">The peer's causal context; the empty clock on this add-only channel.</param>
    /// <returns>The empty resolution.</returns>
    private static ReconciliationDifferenceResolution<ReadOnlyMemory<byte>> ResolveEmptyDifference(IReadOnlyList<ReadOnlyMemory<byte>> decodedItems, VectorClockState peerContext)
    {
        return ReconciliationDifferenceResolution<ReadOnlyMemory<byte>>.Empty;
    }

    /// <summary>Emits one fault event naming a declined fetch's fault class, when a sink is attached.</summary>
    /// <param name="shardIndex">The shard whose fetch declined.</param>
    /// <param name="kind">The fault's class.</param>
    private void EmitFault(int shardIndex, ShardDifferenceFaultKind kind)
    {
        if(trace is null)
        {
            return;
        }

        ShardDifferenceFaultEvent evt = new(0, timeProvider.GetUtcNow().UtcTicks, correlationId, shardIndex, kind);
        trace(in evt);
    }

    /// <summary>Binds the session's outbound edge to the frame writer as a method group, so the send seam carries no closure — and observes the session's own DONE send, the exact decode-completion signal the pump's cap decision reads.</summary>
    /// <param name="writer">The channel writer envelope frames are written through.</param>
    /// <param name="exchange">The exchange state the decode-completion observation lands on.</param>
    private sealed class EnvelopeSendBinding(MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> writer, ExchangeState exchange)
    {
        /// <summary>The channel writer envelope frames are written through.</summary>
        private MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> Writer { get; } = writer;

        /// <summary>The exchange state the decode-completion observation lands on.</summary>
        private ExchangeState Exchange { get; } = exchange;

        /// <summary>Wraps one envelope as a frame and writes it, marking the exchange decode-complete when the envelope is the session's done signal.</summary>
        /// <param name="envelope">The envelope to send.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the frame is flushed.</returns>
        public ValueTask SendAsync(ReconciliationEnvelope<ReadOnlyMemory<byte>> envelope, CancellationToken cancellationToken)
        {
            if(envelope.Done is not null)
            {
                Exchange.MarkDecodeCompleted();
            }

            return Writer.WriteAsync(ShardDifferenceFrame<ReadOnlyMemory<byte>>.ForEnvelope(envelope), cancellationToken);
        }
    }

    /// <summary>
    /// The inbound pump and the state it shares with the verdict: the peer's declared fingerprint, the symbols
    /// submitted, and the wind-down triggers. The pump's finally ALWAYS winds the session down, so the run task
    /// the caller joins can never hang past the pump's exit; the caller reads the shared state only after
    /// joining both tasks.
    /// </summary>
    /// <param name="symbolCap">The symbol ceiling the exchange is bounded by.</param>
    /// <param name="localFingerprint">The driving policy's fingerprint, for the fast-path comparison.</param>
    /// <param name="timeProvider">The clock the out-of-budget drain window is measured on.</param>
    /// <param name="decodeDrainWindow">The drain window an exhausted symbol stream grants the session's consumer.</param>
    private sealed class ExchangeState(int symbolCap, ShardPolicyFingerprint localFingerprint, TimeProvider timeProvider, TimeSpan decodeDrainWindow)
    {
        /// <summary>The symbol ceiling the exchange is bounded by.</summary>
        public int SymbolCap { get; } = symbolCap;

        /// <summary>The driving policy's fingerprint.</summary>
        public ShardPolicyFingerprint LocalFingerprint { get; } = localFingerprint;

        /// <summary>The clock the out-of-budget drain window is measured on.</summary>
        private TimeProvider TimeProvider { get; } = timeProvider;

        /// <summary>The drain window an exhausted symbol stream grants the session's consumer before the exchange is wound down as out of budget: a completing decode announces itself (the DONE send) in microseconds once the queued batches drain, so the window is generous scheduling slack that only a genuinely out-of-budget decode ever runs out.</summary>
        private TimeSpan DecodeDrainWindow { get; } = decodeDrainWindow;

        /// <summary>The peer's own declared fingerprint from the reply header, or <see langword="null"/> until (and unless) it arrives.</summary>
        public ShardPolicyFingerprint? DeclaredFingerprint { get; private set; }

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

        /// <summary>Whether the session's DONE signal has been sent.</summary>
        private bool DecodeCompleted
        {
            get
            {
                return DecodeCompletedSignal.Task.IsCompleted;
            }
        }

        /// <summary>Grants the session's consumer the drain window over the exhausted symbol stream: a completing decode announces itself (the DONE send) as soon as the queued batches drain, without a spin; only a decode that stays silent through the whole window is out of budget.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns><see langword="true"/> when the decode completed within the window; <see langword="false"/> to wind down as the bounded refusal.</returns>
        private async ValueTask<bool> WaitForDecodeDrainAsync(CancellationToken cancellationToken)
        {
            Task completed = await Task.WhenAny(DecodeCompletedSignal.Task, Task.Delay(DecodeDrainWindow, TimeProvider, cancellationToken)).ConfigureAwait(false);

            return completed == DecodeCompletedSignal.Task;
        }

        /// <summary>Pumps inbound frames into the session until the reply declines, the cap trips, the channel ends, or the session stops accepting; the finally winds the session down on every exit.</summary>
        /// <param name="session">The initiator session inbound envelopes are submitted to.</param>
        /// <param name="reader">The channel reader inbound frames are read from.</param>
        /// <param name="cancellationToken">Cancels the pump.</param>
        /// <returns>A task that completes when the pump exits.</returns>
        /// <exception cref="InvalidDataException">The peer violated the channel protocol: an envelope before the reply header, or an inbound request header.</exception>
        public async Task PumpAsync(AntiEntropySession<ReadOnlyMemory<byte>> session, MessageChannelReader<ShardDifferenceFrame<ReadOnlyMemory<byte>>> reader, CancellationToken cancellationToken)
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
        private async Task PumpFramesAsync(AntiEntropySession<ReadOnlyMemory<byte>> session, MessageChannelReader<ShardDifferenceFrame<ReadOnlyMemory<byte>>> reader, CancellationToken cancellationToken)
        {
            await foreach(ShardDifferenceFrame<ReadOnlyMemory<byte>> frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if(frame.ReplyHeader is { } reply)
                {
                    if(DeclaredFingerprint is not null)
                    {
                        throw new InvalidDataException("A shard-difference peer sent a second reply header.");
                    }

                    DeclaredFingerprint = reply.Fingerprint;

                    //The fast path: a decline, or a declared policy differing from the local one, ends the
                    //exchange without a session — the rung's comparison over the declared value stays the
                    //authoritative refusal; skipping the session only avoids a wasted full exchange.
                    if(!reply.Accepted || reply.Fingerprint != LocalFingerprint)
                    {
                        return;
                    }

                    continue;
                }

                if(frame.Envelope is not { } envelope)
                {
                    throw new InvalidDataException("A shard-difference peer sent a request header on the response stream.");
                }

                if(DeclaredFingerprint is null)
                {
                    throw new InvalidDataException("A shard-difference peer sent an envelope before its reply header.");
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
                //window: a completing decode flips the exchange back to normal pumping, and only a decode that
                //stays silent through the window winds down here, the bounded refusal the cap promises. The
                //post-join verdict still decides the final word.
                if(AbsorbedSymbols >= SymbolCap + ShardDifferenceChannelClient.DefaultBatchSize && !DecodeCompleted && !await WaitForDecodeDrainAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
    }
}
