using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The endpoint map this host reaches its fellow metadata-plane members through: one MUTABLE route per founder,
/// resolved by identity rather than by position, and rebound in place while the plane runs. It answers the
/// <see cref="ResolvePeerMetadataConnectionDelegate"/> the transport binding composes every member's channel
/// over.
/// </summary>
/// <remarks>
/// <para>
/// A ROUTE IS PLACEABLE BEFORE IT IS BOUND. The transport binding looks every fellow founder up at composition
/// and refuses a null answer, while a deployment learns its peers' ephemeral ports only after they have started
/// — so a route object exists for every founder from the moment the table is built, and an address it does not
/// have yet is a DIAL-TIME fault rather than a composition failure. The register reads that fault as an
/// unreachable recorder whose quorum slot it keeps, which is the case the protocol already handles.
/// </para>
/// <para>
/// REBINDING IS AN OPERATOR ACT. A restarted member binds a fresh ephemeral port, and no locator can guess it,
/// so the address is replaced under a gate and the next dial goes to the new home; connections a channel already
/// holds keep the host they dialed, and the channel redials after the fault its next call meets.
/// </para>
/// <para>
/// A MEMBER NO FOUNDER NAMES IS STILL PLACED, with a seam that reports the gap by faulting. This command admits
/// no member, so the case arises only if a record named an identity the founder list does not — and the register
/// keeps that member's slot and counts it unreachable, rather than deciding on fewer replicas than its
/// arithmetic claims.
/// </para>
/// <para>
/// Every seam is a method group over an object that holds its own state, so nothing here captures an enclosing
/// scope.
/// </para>
/// </remarks>
internal sealed class MetadataRouteTable
{
    /// <summary>Builds one unbound route per founder of the deployment.</summary>
    /// <param name="deployment">The chain's genesis, whose founders are the members this table places.</param>
    /// <param name="pool">The pool every metadata connection's stream pipes rent their buffers from; the host's governed pool.</param>
    /// <exception cref="ArgumentNullException"><paramref name="deployment"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public MetadataRouteTable(MetadataPlaneDeployment deployment, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(pool);

        StreamPipeReaderOptions readerOptions = new(pool, leaveOpen: true);
        StreamPipeWriterOptions writerOptions = new(pool, leaveOpen: true);

        //A route reaches a REPLICA, because that is what an operator addresses; which store answers there is
        //what the answer itself states and what the register compares.
        ImmutableArray<MetadataFounder> founders = deployment.Founders;
        List<FounderRoute> routes = new(founders.Length);
        for(int i = 0; i < founders.Length; i++)
        {
            routes.Add(new FounderRoute(founders[i].Axis, readerOptions, writerOptions));
        }

        Routes = routes;
    }

    /// <summary>The one-byte selector opening an outbound metadata connection.</summary>
    private static ReadOnlyMemory<byte> MetadataServiceSelector { get; } = new[] { ReplicateWire.MetadataService };

    /// <summary>The routes, one per founder, in genesis order.</summary>
    private List<FounderRoute> Routes { get; }

    /// <summary>
    /// Answers which connection seam reaches one named member — a
    /// <see cref="ResolvePeerMetadataConnectionDelegate"/>. It never answers <see langword="null"/> and never
    /// faults here: a founder answers its own mutable route, and a member no founder names answers a seam that
    /// reports the gap when it is dialed.
    /// </summary>
    /// <param name="member">The member to reach, named by its replica identity axis.</param>
    /// <returns>The seam that opens one duplex connection to that member's metadata endpoint.</returns>
    public OpenPeerMetadataConnectionDelegate Resolve(ReplicaAxis member)
    {
        for(int i = 0; i < Routes.Count; i++)
        {
            if(Routes[i].Member.Equals(member))
            {
                return Routes[i].OpenAsync;
            }
        }

        return new UnplaceableMember(member).OpenAsync;
    }

    /// <summary>Points one founder's route at an address, replacing whatever it held.</summary>
    /// <param name="member">The founder whose route is bound.</param>
    /// <param name="host">The member's host.</param>
    /// <param name="port">The member's port.</param>
    /// <returns><see langword="true"/> when a founder carries that identity axis; <see langword="false"/> when the founder list names no such member.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public bool TryRebind(ReplicaAxis member, string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);

        for(int i = 0; i < Routes.Count; i++)
        {
            if(Routes[i].Member.Equals(member))
            {
                Routes[i].Rebind(host, port);

                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the peer's one service-verdict byte after the selector. Every answer other than acceptance is an ordinary I/O fault, because a consensus register reads an unreachable member and a member that declines to serve the plane identically — it keeps the slot and counts the member unreachable either way.</summary>
    /// <param name="stream">The connection's stream.</param>
    /// <param name="pool">The pool the one verdict byte is read into.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the verdict was accepted.</returns>
    /// <exception cref="IOException">The peer closed without answering, or answered anything other than acceptance.</exception>
    private static async ValueTask ReadServiceVerdictAsync(NetworkStream stream, MemoryPool<byte> pool, CancellationToken cancellationToken)
    {
        using IMemoryOwner<byte> verdict = pool.Rent(1);
        int read = await stream.ReadAsync(verdict.Memory[..1], cancellationToken).ConfigureAwait(false);
        if(read != 1)
        {
            throw new IOException("The member closed without answering the service verdict byte, so nothing reaches its metadata endpoint.");
        }

        byte value = verdict.Memory.Span[0];
        if(value != ReplicateWire.ServiceAccepted)
        {
            throw new IOException(FormattableString.Invariant($"The member answered service verdict byte {value} rather than accepting the metadata connection."));
        }
    }

    /// <summary>
    /// One founder's route: the address its metadata endpoint is currently believed to sit at, held under a gate
    /// so the operator's rebinding and a dialing channel never read a half-written address.
    /// </summary>
    /// <param name="member">The founder this route reaches.</param>
    /// <param name="readerOptions">The stream-pipe reader options a connection's read side is created under.</param>
    /// <param name="writerOptions">The stream-pipe writer options a connection's write side is created under.</param>
    private sealed class FounderRoute(ReplicaAxis member, StreamPipeReaderOptions readerOptions, StreamPipeWriterOptions writerOptions)
    {
        /// <summary>The founder this route reaches.</summary>
        public ReplicaAxis Member { get; } = member;

        /// <summary>The stream-pipe reader options a connection's read side is created under; its buffers come from the host's governed pool.</summary>
        private StreamPipeReaderOptions ReaderOptions { get; } = readerOptions;

        /// <summary>The stream-pipe writer options a connection's write side is created under; its buffers come from the host's governed pool.</summary>
        private StreamPipeWriterOptions WriterOptions { get; } = writerOptions;

        /// <summary>The gate the address is read and written under.</summary>
        private Lock Gate { get; } = new();

        /// <summary>The member's host, or <see langword="null"/> while nothing has bound this route. Read and written only under <see cref="Gate"/>.</summary>
        private string? Host { get; set; }

        /// <summary>The member's port. Read and written only under <see cref="Gate"/>.</summary>
        private int Port { get; set; }

        /// <summary>Points this route at an address, replacing whatever it held.</summary>
        /// <param name="host">The member's host.</param>
        /// <param name="port">The member's port.</param>
        public void Rebind(string host, int port)
        {
            lock(Gate)
            {
                Host = host;
                Port = port;
            }
        }

        /// <summary>Opens one duplex connection to the member's metadata endpoint — an <see cref="OpenPeerMetadataConnectionDelegate"/>.</summary>
        /// <param name="cancellationToken">Cancels the connection attempt.</param>
        /// <returns>The opened connection; ownership transfers to the caller.</returns>
        /// <exception cref="IOException">Nothing has bound this route yet, or the member did not accept the metadata connection.</exception>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection and the socket transport it owns transfer to the caller per the OpenPeerMetadataConnectionDelegate contract; the metadata channel client disposes the connection on every fault, cancellation and teardown path, and the finally here disposes the socket when a throw prevented the transfer.")]
        public async ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            string host;
            int port;
            lock(Gate)
            {
                if(Host is not string bound)
                {
                    throw new IOException("No endpoint is bound for this metadata-plane member, so nothing reaches it; bind one with the metadata-route verb.");
                }

                host = bound;
                port = Port;
            }

            TcpClient? client = new();
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(MetadataServiceSelector, cancellationToken).ConfigureAwait(false);
                await ReadServiceVerdictAsync(stream, ReaderOptions.Pool, cancellationToken).ConfigureAwait(false);
                PeerChannelConnection connection = new(
                    PipeWriter.Create(stream, WriterOptions),
                    PipeReader.Create(stream, ReaderOptions),
                    new OwnedTcpClient(client));
                client = null;

                return connection;
            }
            finally
            {
                client?.Dispose();
            }
        }
    }

    /// <summary>
    /// The seam answered for a member the founder list does not name: dialing it reports the gap by faulting, so
    /// the asking register keeps that member's quorum slot and counts it unreachable rather than deciding over a
    /// membership smaller than the one the record names.
    /// </summary>
    /// <param name="member">The member this host cannot place.</param>
    private sealed class UnplaceableMember(ReplicaAxis member)
    {
        /// <summary>The member this host cannot place.</summary>
        private ReplicaAxis Member { get; } = member;

        /// <summary>Reports that the member cannot be placed — an <see cref="OpenPeerMetadataConnectionDelegate"/>.</summary>
        /// <param name="cancellationToken">Unread: nothing is attempted.</param>
        /// <returns>Never returns a connection.</returns>
        /// <exception cref="IOException">Always, naming the member no founder of this deployment carries.</exception>
        public ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            throw new IOException(FormattableString.Invariant($"No founder of this deployment carries the identity axis {Convert.ToHexString(Member.Bytes.Span[..8])}, so this host cannot place that member."));
        }
    }
}
