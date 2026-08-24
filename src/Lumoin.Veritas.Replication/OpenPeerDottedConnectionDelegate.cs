using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Opens one fresh duplex connection to the peer's dotted-difference endpoint — the transport seam the host
/// binds (a loopback socket, an in-process pipe pair). One connection carries exactly one dotted exchange; the
/// client disposes it unconditionally on every exit. A peer that answers the EXPLICIT unknown-service refusal
/// byte to the dialed selector is raised as <see cref="PeerServiceRefusedException"/> — the one evidence for a
/// remove-aware-unsupported outcome; any other connect fault is an ordinary I/O fault the exchange reports as
/// peer-unavailable.
/// </summary>
/// <param name="cancellationToken">Cancels the connection attempt.</param>
/// <returns>The opened connection; ownership transfers to the caller.</returns>
public delegate ValueTask<PeerChannelConnection> OpenPeerDottedConnectionDelegate(CancellationToken cancellationToken);
