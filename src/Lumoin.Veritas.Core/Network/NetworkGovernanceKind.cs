namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// The verdict a <see cref="NetworkGovernanceDelegate"/> returns for one network-boundary call — the network
/// sibling of <see cref="Lumoin.Veritas.Core.Hypertrie.AccessControl.AccessDecision"/>, value-based so a refusal
/// is a return value rather than a thrown exception. Topology-aware rerouting (a Reroute verdict carrying a target
/// peer) is a planned addition once the routing target type is settled; until then a policy that cannot serve a
/// call <see cref="Deny"/>s it.
/// </summary>
public enum NetworkGovernanceKind
{
    /// <summary>Permit: the call proceeds unchanged.</summary>
    Permit = 0,

    /// <summary>Delay: the caller backs off for the decision's retry-after before the call may proceed (a rate or concurrency limit is momentarily exhausted).</summary>
    Delay = 1,

    /// <summary>Deny: the call is refused (a firewall rule, an exhausted quota, or an unauthorized peer); the caller treats it as an unavailable peer/endpoint rather than an error.</summary>
    Deny = 2,
}
