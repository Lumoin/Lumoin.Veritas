using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Opens one duplex connection to a cluster member's metadata endpoint — the transport seam the host binds (a
/// loopback socket, an in-process pipe pair). Unlike the reconciliation seams, ONE connection carries MANY
/// correlated calls: <see cref="MetadataChannelClient"/> opens it on the first call, serializes every later
/// call over it, and dials again only after a fault or a cancellation left the frame stream out of step. A
/// connect fault is an ordinary I/O fault, and the consensus seams the client serves read it as an unreachable
/// member, which the protocol already handles.
/// </summary>
/// <param name="cancellationToken">Cancels the connection attempt.</param>
/// <returns>The opened connection; ownership transfers to the caller.</returns>
public delegate ValueTask<PeerChannelConnection> OpenPeerMetadataConnectionDelegate(CancellationToken cancellationToken);
