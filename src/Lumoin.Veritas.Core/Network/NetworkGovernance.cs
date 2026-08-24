using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// Built-in <see cref="NetworkGovernanceDelegate"/> defaults. The seam is opt-in: a host that wires no governance
/// uses <see cref="AlwaysPermit"/>, so a transport decorator over an unconfigured seam behaves exactly as the
/// undecorated transport — the same zero-cost-when-absent discipline the access-control seam follows.
/// </summary>
public static class NetworkGovernance
{
    /// <summary>The default that permits every call unconditionally — the seam's unconfigured behavior. A static method group, so it captures nothing.</summary>
    public static NetworkGovernanceDelegate AlwaysPermit { get; } = PermitAll;

    /// <summary>Permits any request immediately.</summary>
    /// <param name="request">The request; ignored.</param>
    /// <param name="cancellationToken">The token; ignored, the verdict is immediate.</param>
    /// <returns>A completed permit verdict.</returns>
    private static ValueTask<NetworkGovernanceDecision> PermitAll(NetworkGovernanceRequest request, CancellationToken cancellationToken)
    {
        return new ValueTask<NetworkGovernanceDecision>(NetworkGovernanceDecision.Permit);
    }
}
