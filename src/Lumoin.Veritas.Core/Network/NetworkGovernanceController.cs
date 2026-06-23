using System;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// The live network-governance control surface: it composes a <see cref="NetworkFirewall"/> and an optional
/// <see cref="NetworkRateLimiter"/> behind a <see cref="LiveNetworkGovernance"/> chain (firewall first, so a denied
/// peer never reaches the limiter) and exposes the reconfiguration an editor, MCP, or CLI command drives on a
/// running engine — block or unblock a peer, attach or retune the throttle — while a single bound
/// <see cref="Decide"/> the transport decorators consult stays valid across every change.
/// </summary>
/// <remarks>
/// The controller owns the firewall and reconfigures it in place. It does <b>not</b> own the lifetime of any
/// <see cref="NetworkRateLimiter"/> handed to <see cref="UseThrottle"/>: the caller (the composition root that
/// knows when in-flight calls have drained) disposes a replaced limiter, so swapping the throttle never disposes a
/// limiter out from under a call in flight.
/// </remarks>
public sealed class NetworkGovernanceController
{
    private readonly NetworkFirewall firewall;
    private readonly LiveNetworkGovernance live;

    /// <summary>Creates a controller whose firewall starts empty (permitting every peer), optionally with a throttle composed after it.</summary>
    /// <param name="throttle">The initial rate limiter to compose after the firewall, or <see langword="null"/> for firewall-only governance. The caller owns its lifetime.</param>
    public NetworkGovernanceController(NetworkRateLimiter? throttle = null)
    {
        firewall = new NetworkFirewall();
        live = throttle is null
            ? new LiveNetworkGovernance(firewall.Decide)
            : new LiveNetworkGovernance(firewall.Decide, throttle.Decide);
        Decide = live.Decide;
    }

    /// <summary>The composed governance delegate the transport decorators consult; stays valid across every reconfiguration. Bind it once.</summary>
    public NetworkGovernanceDelegate Decide { get; }

    /// <summary>Blocks a peer: calls naming it are denied from the next call on, without reaching the throttle. The control-in an editor/MCP/CLI command drives.</summary>
    /// <param name="kind">The peer's identifier kind.</param>
    /// <param name="peer">The peer's identifier bytes.</param>
    public void Deny(NetworkPeerKeyKind kind, ReadOnlySpan<byte> peer)
    {
        firewall.Deny(kind, peer);
    }

    /// <summary>Unblocks a peer: calls naming it are permitted by the firewall again (still subject to the throttle) from the next call on.</summary>
    /// <param name="kind">The peer's identifier kind.</param>
    /// <param name="peer">The peer's identifier bytes.</param>
    public void Allow(NetworkPeerKeyKind kind, ReadOnlySpan<byte> peer)
    {
        firewall.Allow(kind, peer);
    }

    /// <summary>Unblocks every peer at once.</summary>
    public void ClearFirewall()
    {
        firewall.Clear();
    }

    /// <summary>Attaches or replaces the throttle composed after the firewall; the next call uses it. To retune, the caller builds a new limiter, calls this, and disposes the previous one once its in-flight calls have drained — the controller never disposes a limiter.</summary>
    /// <param name="throttle">The rate limiter to compose after the firewall. The caller owns its lifetime.</param>
    /// <exception cref="ArgumentNullException"><paramref name="throttle"/> is <see langword="null"/>.</exception>
    public void UseThrottle(NetworkRateLimiter throttle)
    {
        ArgumentNullException.ThrowIfNull(throttle);

        live.SetPolicies(firewall.Decide, throttle.Decide);
    }

    /// <summary>Removes the throttle, leaving firewall-only governance; the next call is no longer rate-limited.</summary>
    public void RemoveThrottle()
    {
        live.SetPolicies(firewall.Decide);
    }
}
