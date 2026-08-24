using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Opens one fresh duplex connection to the peer's shard-difference endpoint — the transport seam the host
/// binds (a loopback socket, an in-process pipe pair). One connection carries exactly one shard's exchange;
/// the client disposes it unconditionally on every exit, and a raised concurrent-shard window drives one
/// distinct connection per in-flight shard.
/// </summary>
/// <param name="shardIndex">The shard the connection will carry, so a host can route or label per shard.</param>
/// <param name="cancellationToken">Cancels the connection attempt.</param>
/// <returns>The opened connection; ownership transfers to the caller.</returns>
public delegate ValueTask<PeerChannelConnection> OpenPeerShardConnectionDelegate(int shardIndex, CancellationToken cancellationToken);
