using System;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A network-governance verdict: the <see cref="NetworkGovernanceKind"/> and, for a <see cref="NetworkGovernanceKind.Delay"/>,
/// how long the caller backs off before the call may proceed. Value-based: the decorator that consulted the policy
/// permits, delays, or declines by reading this, never by catching an exception.
/// </summary>
/// <param name="Kind">The verdict.</param>
/// <param name="RetryAfter">The back-off before retry when <paramref name="Kind"/> is <see cref="NetworkGovernanceKind.Delay"/>; <see cref="TimeSpan.Zero"/> otherwise.</param>
public readonly record struct NetworkGovernanceDecision(NetworkGovernanceKind Kind, TimeSpan RetryAfter)
{
    /// <summary>The permit verdict — the call proceeds unchanged, no back-off.</summary>
    public static NetworkGovernanceDecision Permit { get; } = new(NetworkGovernanceKind.Permit, TimeSpan.Zero);

    /// <summary>The deny verdict — the call is refused, no back-off.</summary>
    public static NetworkGovernanceDecision Deny { get; } = new(NetworkGovernanceKind.Deny, TimeSpan.Zero);

    /// <summary>
    /// A delay verdict — the call is granted, but the caller backs off for <paramref name="retryAfter"/> first. The
    /// gate honors this by waiting then proceeding <em>without re-consulting the policy</em>, so a policy returns a
    /// delay only when it has already accounted for the admission (a paced grant), never to mean "ask again": a
    /// policy that must withhold capacity until a resource frees should wait internally and return a permit, or
    /// return a deny — see <see cref="NetworkRateLimiter"/>, which queues then permits or denies and never delays.
    /// </summary>
    /// <param name="retryAfter">The back-off duration; not negative.</param>
    /// <returns>The delay verdict.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryAfter"/> is negative.</exception>
    public static NetworkGovernanceDecision Delay(TimeSpan retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryAfter, TimeSpan.Zero);

        return new NetworkGovernanceDecision(NetworkGovernanceKind.Delay, retryAfter);
    }
}
