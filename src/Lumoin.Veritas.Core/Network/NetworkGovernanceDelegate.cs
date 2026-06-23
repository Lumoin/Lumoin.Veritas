using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// Decides how one network-boundary call is governed — permit, delay, or deny — the network sibling of
/// <see cref="Lumoin.Veritas.Core.Hypertrie.AccessControl.AccessControlDelegate"/>. The host supplies the
/// implementation (an in-process rate/concurrency limiter and firewall, or an out-of-process enforcer such as an
/// eBPF backend); a transport decorator consults it before each outbound call and an inbound gate before serving.
/// </summary>
/// <remarks>
/// It is asynchronous by contract: a single decision may round-trip to remote hardware (an HSM or TPM), a remote
/// verification service (OAuth, EUDI), or an agentic policy backend, so the seam never assumes the verdict is
/// available synchronously. A purely local policy returns a completed value task; the synchronous, in-process
/// replication fast path therefore does not host this seam at all — only the asynchronous transport boundary does.
/// </remarks>
/// <param name="request">The call being governed.</param>
/// <param name="cancellationToken">The token that cancels the decision.</param>
/// <returns>The governance verdict.</returns>
public delegate ValueTask<NetworkGovernanceDecision> NetworkGovernanceDelegate(NetworkGovernanceRequest request, CancellationToken cancellationToken);
