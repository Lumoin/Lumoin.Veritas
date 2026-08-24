using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves one shard-difference exchange to a peer over a duplex connection: reads the request header, answers
/// with this endpoint's OWN policy declaration and epoch (never an echo), and — when the request matches — runs
/// the responder session over its own shard operand, streaming symbol batches until the peer's decoder signals
/// completion or the connection ends. One connection carries exactly one shard's exchange; the peer tearing
/// the connection down is the NORMAL end of serve, never an error. An active-active replica accepts these
/// beside its sketch serves; the accept loop that dispatches connections isolates per-connection faults.
/// </summary>
/// <remarks>
/// The serve declines — reply header with the declaration, nothing following — on: a negative shard index, a
/// non-positive symbol cap, a dictionary-epoch mismatch, or a declared policy fingerprint differing from this
/// endpoint's own (the wire-level half of the typed handshake; the requesting rung's comparison over this
/// reply's declaration is the authoritative backstop). The shard operand is materialized by walking the served
/// snapshot and keeping the keys hashing into the requested shard — LOAD-BEARING SHAPE: a foreign shard index
/// simply matches nothing and serves an empty shard; this walk must never be replaced by an indexed partition
/// lookup whose bounds check would fault the connection instead.
/// </remarks>
public sealed class ShardDifferenceChannelServer
{
    private readonly PrefixShardPolicy policy;
    private readonly ProvideShardServeSnapshotDelegate provideSnapshot;
    private readonly ulong dictionaryEpoch;
    private readonly MemoryPool<byte> pool;
    private readonly int maxFrameLength;
    private readonly ShardDifferenceFraming<ReadOnlyMemory<byte>> framing;

    /// <summary>Creates a server over this endpoint's shard policy and served snapshot.</summary>
    /// <param name="policy">This endpoint's own shard policy; its fingerprint is the declaration every reply carries.</param>
    /// <param name="provideSnapshot">The seam supplying the current committed set's projected keys per serve.</param>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch; a request stamped differently is declined.</param>
    /// <param name="pool">The pool the responder sessions rent from; the engine's governed pool.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/>, <paramref name="provideSnapshot"/>, or <paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public ShardDifferenceChannelServer(
        PrefixShardPolicy policy,
        ProvideShardServeSnapshotDelegate provideSnapshot,
        ulong dictionaryEpoch,
        MemoryPool<byte> pool,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(provideSnapshot);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.policy = policy;
        this.provideSnapshot = provideSnapshot;
        this.dictionaryEpoch = dictionaryEpoch;
        this.pool = pool;
        this.maxFrameLength = maxFrameLength;
        framing = new ShardDifferenceFraming<ReadOnlyMemory<byte>>(StructuralReconciliationContract.Value);
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

        MessageChannelReader<ShardDifferenceFrame<ReadOnlyMemory<byte>>> reader = new(requestReader, framing.ReadFrame, maxFrameLength);
        MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> writer = new(responseWriter, framing.WriteFrame, maxFrameLength);
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
        MessageChannelReader<ShardDifferenceFrame<ReadOnlyMemory<byte>>> reader,
        MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> writer,
        CancellationToken cancellationToken)
    {
        AntiEntropySession<ReadOnlyMemory<byte>>? session = null;
        Task? run = null;
        try
        {
            //The trigger budget covers the whole cap in batches plus one spare, so the stream can carry the cap
            //and the client's own counter is what stops an exchange the decode never completes.
            int triggerBudget = 0;
            await foreach(ShardDifferenceFrame<ReadOnlyMemory<byte>> frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if(session is null)
                {
                    if(frame.RequestHeader is not { } request)
                    {
                        throw new InvalidDataException("A shard-difference connection must open with a request header.");
                    }

                    bool accepted = request.ShardIndex >= 0
                        && request.SymbolCap > 0
                        && request.SymbolCap <= ShardDifferenceChannelClient.MaximumSymbolCap
                        && request.DictionaryEpoch == dictionaryEpoch
                        && request.Fingerprint == policy.Fingerprint;
                    await writer.WriteAsync(ShardDifferenceFrame<ReadOnlyMemory<byte>>.ForReplyHeader(new ShardDifferenceReplyHeader(accepted, policy.Fingerprint, dictionaryEpoch)), cancellationToken).ConfigureAwait(false);
                    if(!accepted)
                    {
                        return;
                    }

                    session = new AntiEntropySession<ReadOnlyMemory<byte>>(AntiEntropyRole.Responder, StructuralReconciliationContract.Value, ShardOperand(request.ShardIndex), ShardDifferenceChannelClient.DefaultBatchSize, pool);
                    triggerBudget = ((request.SymbolCap + ShardDifferenceChannelClient.DefaultBatchSize - 1) / ShardDifferenceChannelClient.DefaultBatchSize) + 1;
                    EnvelopeSendBinding send = new(writer);
                    run = session.RunAsync(send.SendAsync, serveFetch: RefuseFetch, cancellationToken: cancellationToken);

                    continue;
                }

                if(frame.Envelope is not { } envelope)
                {
                    throw new InvalidDataException("A shard-difference connection carries one request header, then envelopes only.");
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
                //propagates out of the responder's run), so no pooled cell store leaks on the fault path.
                try
                {
                    session.Complete();
                    if(run is not null)
                    {
                        await run.ConfigureAwait(false);
                    }
                }
                finally
                {
                    session.Dispose();
                }
            }
        }
    }

    /// <summary>Materializes this endpoint's operand for one shard: walk the served snapshot, keep the keys hashing into the shard. A foreign shard index matches nothing and yields an empty operand.</summary>
    /// <param name="shardIndex">The requested shard.</param>
    /// <returns>The shard's keys.</returns>
    private List<ReadOnlyMemory<byte>> ShardOperand(int shardIndex)
    {
        IReadOnlyList<ReadOnlyMemory<byte>> snapshot = provideSnapshot();
        List<ReadOnlyMemory<byte>> operand = [];
        for(int i = 0; i < snapshot.Count; i++)
        {
            if(policy.ShardOf(snapshot[i].Span) == shardIndex)
            {
                operand.Add(snapshot[i]);
            }
        }

        return operand;
    }

    /// <summary>The add-only fetch refusal: this channel transfers no elements, so a fetch from the peer is a protocol violation and fails the serve closed.</summary>
    /// <param name="items">The requested items; unread.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    private static IReadOnlyList<ReconciliationElementEntry<ReadOnlyMemory<byte>>> RefuseFetch(IReadOnlyList<ReadOnlyMemory<byte>> items)
    {
        throw new InvalidOperationException("The add-only shard-difference channel transfers no elements; a fetch is a protocol violation.");
    }

    /// <summary>Binds the session's outbound edge to the frame writer as a method group, so the send seam carries no closure.</summary>
    /// <param name="writer">The channel writer envelope frames are written through.</param>
    private sealed class EnvelopeSendBinding(MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> writer)
    {
        /// <summary>The channel writer envelope frames are written through.</summary>
        private MessageChannelWriter<ShardDifferenceFrame<ReadOnlyMemory<byte>>> Writer { get; } = writer;

        /// <summary>Wraps one envelope as a frame and writes it.</summary>
        /// <param name="envelope">The envelope to send.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the frame is flushed.</returns>
        public ValueTask SendAsync(ReconciliationEnvelope<ReadOnlyMemory<byte>> envelope, CancellationToken cancellationToken)
        {
            return Writer.WriteAsync(ShardDifferenceFrame<ReadOnlyMemory<byte>>.ForEnvelope(envelope), cancellationToken);
        }
    }
}
