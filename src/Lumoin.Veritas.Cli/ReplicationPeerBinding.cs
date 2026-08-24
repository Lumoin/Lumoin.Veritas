using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The replicate host's late-bound peer seam: it holds the current peer address and the open engine, and binds
/// every outbound wire surface over them — the sketch fetch a reconcile pull drives, the per-shard connection
/// factory the sharded repair rung opens, and the two self-heal provider seams. The peer binds after the engine
/// opens (the <c>--peer</c> option or the <c>peer</c> verb), so every delegate reads the CURRENT state per call:
/// an unbound peer answers the unavailable or unsourced value its consumer declines on by name, and a socket
/// fault on the sketch fetch reads as an unreachable peer rather than propagating into the session.
/// </summary>
/// <remarks>
/// The composition root that binds these delegates is inside the repair trust base — the declared-capability
/// limit recorded on the sharded reconciliation source. This binding dials the address it was given and never
/// rewrites a peer's declaration.
/// </remarks>
internal sealed class ReplicationPeerBinding
{
    /// <summary>The current peer address, or <see langword="null"/> before one is bound; replaced atomically as one reference so a concurrent reader sees a whole address.</summary>
    private volatile PeerAddress? peer;

    /// <summary>Creates the binding over the host's transport resources.</summary>
    /// <param name="policy">The shard policy this endpoint drives and declares on the sharded repair wire.</param>
    /// <param name="pool">The pool every outbound channel and codec rents from.</param>
    /// <param name="shardFaultTrace">The sink declined shard fetches are named on, or <see langword="null"/> to name none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public ReplicationPeerBinding(PrefixShardPolicy policy, MemoryPool<byte> pool, TraceHandler<ShardDifferenceFaultEvent>? shardFaultTrace)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(pool);

        Policy = policy;
        Pool = pool;
        ShardFaultTrace = shardFaultTrace;
    }

    /// <summary>The shard policy this endpoint drives and declares on the sharded repair wire.</summary>
    private PrefixShardPolicy Policy { get; }

    /// <summary>The pool every outbound channel and codec rents from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>The sink declined shard fetches are named on, or <see langword="null"/>.</summary>
    private TraceHandler<ShardDifferenceFaultEvent>? ShardFaultTrace { get; }

    /// <summary>The open engine whose dictionary epoch every outbound request is stamped with; the host sets it once after the store-backed open, before any verb or background loop runs.</summary>
    public VeritasEngine? Engine { get; set; }

    /// <summary>The one-byte selector opening an outbound sketch-fetch connection.</summary>
    private static ReadOnlyMemory<byte> SketchServiceSelector { get; } = new[] { ReplicateWire.SketchService };

    /// <summary>The one-byte selector opening an outbound shard-difference connection.</summary>
    private static ReadOnlyMemory<byte> ShardDifferenceServiceSelector { get; } = new[] { ReplicateWire.ShardDifferenceService };

    /// <summary>The one-byte selector opening an outbound dotted-difference connection.</summary>
    private static ReadOnlyMemory<byte> DottedDifferenceServiceSelector { get; } = new[] { ReplicateWire.DottedDifferenceService };

    /// <summary>Binds the peer address every outbound surface dials from the next call on.</summary>
    /// <param name="host">The peer host.</param>
    /// <param name="port">The peer port.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public void SetPeer(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);

        peer = new PeerAddress(host, port);
    }

    /// <summary>
    /// Fetches the bound peer's stamped sketch image over one fresh TCP connection — the
    /// <see cref="AsyncSketchFetchDelegate"/> the reconcile pull and the single-block repair provider drive. The
    /// request is stamped with the LOCAL dictionary epoch, so a cross-lineage peer's stamped decline is named by
    /// the session's own epoch check rather than laundered. An unbound peer, an unopened engine, or a
    /// socket-level fault answers <see cref="SketchFetchResult.Unavailable"/> — the wire's ordinary absent-peer
    /// value; a protocol violation propagates as the malformed-input type the session surfaces by name.
    /// </summary>
    /// <param name="symbolBudget">The number of coded symbols the peer's sketch must carry.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The peer's stamped sketch image, or <see cref="SketchFetchResult.Unavailable"/>.</returns>
    public async ValueTask<SketchFetchResult> FetchPeerSketchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        if(peer is not PeerAddress address || Engine is not VeritasEngine engine)
        {
            return SketchFetchResult.Unavailable;
        }

        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(address.Host, address.Port, cancellationToken).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(SketchServiceSelector, cancellationToken).ConfigureAwait(false);
            await ReadServiceVerdictAsync(stream, cancellationToken).ConfigureAwait(false);
            SketchChannelClient sketch = new(PipeWriter.Create(stream, ReplicateWire.LeaveOpenWriterOptions), PipeReader.Create(stream, ReplicateWire.LeaveOpenReaderOptions), Pool, SketchChannelDomain.Structural, engine.Dictionary.Epoch);

            return await sketch.FetchAsync(symbolBudget, cancellationToken).ConfigureAwait(false);
        }
        catch(SocketException)
        {
            return SketchFetchResult.Unavailable;
        }
        catch(IOException)
        {
            return SketchFetchResult.Unavailable;
        }
    }

    /// <summary>
    /// Opens one fresh duplex connection to the bound peer's shard-difference endpoint — the
    /// <see cref="OpenPeerShardConnectionDelegate"/> the sharded repair rung drives, one connection per shard
    /// fetch. The connection owns its socket, so the client's unconditional teardown (the channel's liveness
    /// mechanism) closes it on every exit. An unbound peer throws the transport fault the client converts to a
    /// value decline with a null declaration, refused as the missing-declaration outcome it is.
    /// </summary>
    /// <param name="shardIndex">The shard the connection will carry; the serve reads it from the request header, not from here.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connection over the dialed socket.</returns>
    /// <exception cref="IOException">No peer is bound, so the fetch has no endpoint to dial.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the socket transport it owns) transfers to the caller per the OpenPeerShardConnectionDelegate contract; the shard-difference client disposes it unconditionally on every exit, and the finally here disposes the socket when a throw prevented the transfer.")]
    public async ValueTask<PeerChannelConnection> OpenPeerShardConnectionAsync(int shardIndex, CancellationToken cancellationToken)
    {
        if(peer is not PeerAddress address)
        {
            throw new IOException("No replication peer is bound; the shard fetch has no endpoint to dial.");
        }

        TcpClient? client = new();
        try
        {
            await client.ConnectAsync(address.Host, address.Port, cancellationToken).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(ShardDifferenceServiceSelector, cancellationToken).ConfigureAwait(false);
            await ReadServiceVerdictAsync(stream, cancellationToken).ConfigureAwait(false);
            PeerChannelConnection connection = new(PipeWriter.Create(stream, ReplicateWire.LeaveOpenWriterOptions), PipeReader.Create(stream, ReplicateWire.LeaveOpenReaderOptions), new OwnedTcpClient(client));
            client = null;

            return connection;
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>
    /// Opens one fresh duplex connection to the bound peer's dotted-difference endpoint — the
    /// <see cref="OpenPeerDottedConnectionDelegate"/> the remove-aware reconcile drives, one connection per
    /// exchange. The peer's EXPLICIT unknown-service refusal byte surfaces as
    /// <see cref="PeerServiceRefusedException"/> — the one evidence for the remove-aware-unsupported outcome —
    /// while an unbound peer or an absent verdict is an ordinary I/O fault the exchange reports as
    /// peer-unavailable. The connection owns its socket, so the client's unconditional teardown closes it on
    /// every exit.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connection over the dialed socket.</returns>
    /// <exception cref="IOException">No peer is bound, or the peer closed without answering the service verdict.</exception>
    /// <exception cref="PeerServiceRefusedException">The peer answered the unknown-service refusal byte.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the socket transport it owns) transfers to the caller per the OpenPeerDottedConnectionDelegate contract; the dotted client disposes it unconditionally on every exit, and the finally here disposes the socket when a throw prevented the transfer.")]
    public async ValueTask<PeerChannelConnection> OpenPeerDottedConnectionAsync(CancellationToken cancellationToken)
    {
        if(peer is not PeerAddress address)
        {
            throw new IOException("No replication peer is bound; the dotted exchange has no endpoint to dial.");
        }

        TcpClient? client = new();
        try
        {
            await client.ConnectAsync(address.Host, address.Port, cancellationToken).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(DottedDifferenceServiceSelector, cancellationToken).ConfigureAwait(false);
            await ReadServiceVerdictAsync(stream, cancellationToken).ConfigureAwait(false);
            PeerChannelConnection connection = new(PipeWriter.Create(stream, ReplicateWire.LeaveOpenWriterOptions), PipeReader.Create(stream, ReplicateWire.LeaveOpenReaderOptions), new OwnedTcpClient(client));
            client = null;

            return connection;
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>
    /// Reads the peer's one service-verdict byte after the selector: accepted proceeds, the EXPLICIT
    /// unknown-service refusal raises its typed signal, the not-ready verdict is an ordinary I/O fault the
    /// exchange reports as peer-unavailable, and an absent or unrecognized verdict is an ordinary I/O fault too
    /// — death is never inferred as unsupported, and neither is a peer whose engine has not finished opening.
    /// </summary>
    /// <param name="stream">The connection's stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the verdict was accepted.</returns>
    /// <exception cref="IOException">The peer closed without answering, answered that the service is not ready yet, or answered an unrecognized verdict byte.</exception>
    /// <exception cref="PeerServiceRefusedException">The peer answered the unknown-service refusal byte.</exception>
    private async ValueTask ReadServiceVerdictAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using IMemoryOwner<byte> verdict = Pool.Rent(1);
        int read = await stream.ReadAsync(verdict.Memory[..1], cancellationToken).ConfigureAwait(false);
        if(read != 1)
        {
            throw new IOException("The peer closed without answering the service verdict byte.");
        }

        byte value = verdict.Memory.Span[0];
        if(value == ReplicateWire.ServiceRefusedUnknown)
        {
            throw new PeerServiceRefusedException();
        }

        if(value == ReplicateWire.ServiceUnavailableNotReady)
        {
            //The peer knows this service and its engine is not open yet, which is availability and never
            //capability: reporting it as a refusal would let one exchange during a peer's start-up window be
            //read as a peer that cannot speak the remove-aware lane at all.
            throw new IOException("The peer answered that the service is not ready yet; its engine has not finished opening.");
        }

        if(value != ReplicateWire.ServiceAccepted)
        {
            throw new IOException(FormattableString.Invariant($"The peer answered an unknown service verdict byte {value}."));
        }
    }

    /// <summary>
    /// Supplies the single-block peer-reconciliation restoring source for one repair pass — the
    /// <see cref="ProvidePeerReconciliationSourceDelegate"/> seam. It fetches the peer's sketch at the
    /// single-block budget over the sketch service and carries the PEER'S OWN stamped epoch onto the source, so
    /// the rung's epoch gate compares real declarations. An unbound peer, an unreachable peer, or a stamped
    /// decline leaves the rung unsourced; a malformed sketch image propagates, which the pass names as an
    /// unavailable peer source and continues local-only.
    /// </summary>
    /// <param name="commitGeneration">The damaged generation under repair; the fetch serves the peer's CURRENT set, and the pass's own gates decide faithfulness.</param>
    /// <param name="dictionaryEpoch">The damaged generation's dictionary epoch; the rung compares it against the source's carried declaration.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The restoring source, or <see langword="null"/> to leave the rung unsourced.</returns>
    public async ValueTask<PeerReconciliationSource?> ProvideSingleBlockPeerSourceAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
    {
        using SketchFetchResult fetched = await FetchPeerSketchAsync(ReplicateWire.SingleBlockSymbolCap, cancellationToken).ConfigureAwait(false);
        if(!fetched.HasImage)
        {
            return null;
        }

        VerifiedSketch peerSketch = SketchPersistence.LoadVerifiedSketch(fetched.Image.Span, SketchContract.Structural);

        return new PeerReconciliationSource(
            peerSketch,
            new RatelessSketchCodec(Pool).Recover,
            StructuralReconciliationProjection.Inversion,
            ReplicateWire.SingleBlockSymbolCap,
            unchecked((long)fetched.DictionaryEpoch));
    }

    /// <summary>
    /// Supplies the sharded multi-block peer-reconciliation restoring source for one repair pass — the
    /// <see cref="ProvideShardedPeerReconciliationSourceDelegate"/> seam — built over the per-shard connection
    /// factory and declaring the LOCAL live dictionary epoch (the same-lineage assertion the per-fetch wire epoch
    /// check enforces). An unbound peer or an unopened engine leaves the rung unsourced.
    /// </summary>
    /// <param name="commitGeneration">The damaged generation under repair.</param>
    /// <param name="dictionaryEpoch">The damaged generation's dictionary epoch; the rung compares it against the source's declared epoch.</param>
    /// <param name="cancellationToken">Unused by this binding — the per-shard fetches carry the pass's own token.</param>
    /// <returns>The sharded source, or <see langword="null"/> to leave the rung unsourced.</returns>
    public ValueTask<ShardedPeerReconciliationSource?> ProvideShardedPeerSourceAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
    {
        if(peer is null || Engine is not VeritasEngine engine)
        {
            return new ValueTask<ShardedPeerReconciliationSource?>((ShardedPeerReconciliationSource?)null);
        }

        ShardedPeerReconciliationSource source = ShardedPeerTransportBinding.CreateSource(
            Policy,
            OpenPeerShardConnectionAsync,
            Pool,
            engine.Dictionary.Epoch,
            ReplicateWire.ShardSymbolCap,
            interShardPacing: TimeSpan.Zero,
            TimeProvider.System,
            ShardFaultTrace);

        return new ValueTask<ShardedPeerReconciliationSource?>(source);
    }

    /// <summary>A bound peer address, held as one immutable reference so rebinding swaps it atomically.</summary>
    /// <param name="Host">The peer host.</param>
    /// <param name="Port">The peer port.</param>
    private sealed record PeerAddress(string Host, int Port);
}
