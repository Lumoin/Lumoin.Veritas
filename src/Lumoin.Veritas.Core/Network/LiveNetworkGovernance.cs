using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A live, hot-swappable composition of network-governance policies: it folds an ordered chain of
/// <see cref="NetworkGovernanceDelegate"/>s into one (<see cref="Decide"/>) and lets a host replace the chain
/// atomically while calls are in flight — the control-in point through which an editor, MCP, or CLI command retunes
/// a running engine (swap in a re-rated limiter, add or remove the firewall) without restarting it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordered, short-circuit composition.</b> The chain is consulted in order and the first non-permit verdict
/// wins: a deny or delay returns immediately and the rest of the chain is not consulted. So the cheap, decisive
/// policy goes first (the firewall), and a denied peer never reaches — and never consumes a token from, or triggers
/// a remote-hardware call in — a later policy (the rate limiter). An empty chain permits.
/// </para>
/// <para>
/// <b>Lock-free reads, atomic swap.</b> The chain is an immutable array swapped by reference under Volatile
/// semantics (copy-on-write), so a governed call snapshots it once and evaluates that snapshot to completion even
/// if the chain is replaced mid-call; the next call sees the new chain.
/// </para>
/// </remarks>
public sealed class LiveNetworkGovernance
{
    //A naked field: the policy chain is swapped by reference under Volatile semantics so a governed call reads a
    //consistent immutable snapshot without locking; a swap replaces the whole array, never mutates it in place.
    private volatile NetworkGovernanceDelegate[] policies;

    /// <summary>Creates the holder over an initial ordered chain (empty permits everything).</summary>
    /// <param name="policies">The initial policies, consulted in order; none permits everything.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policies"/> or any element is <see langword="null"/>.</exception>
    public LiveNetworkGovernance(params NetworkGovernanceDelegate[] policies)
    {
        this.policies = Snapshot(policies);
        Decide = Evaluate;
    }

    /// <summary>The composed governance delegate the gate consults; folds the current chain. Bind this once at the transport decorator — it stays valid across swaps.</summary>
    public NetworkGovernanceDelegate Decide { get; }

    /// <summary>Atomically replaces the policy chain; the next call sees it. The control-in entry point an editor/MCP/CLI command drives to retune a running engine.</summary>
    /// <param name="policies">The new ordered chain; none permits everything.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policies"/> or any element is <see langword="null"/>.</exception>
    public void SetPolicies(params NetworkGovernanceDelegate[] policies)
    {
        this.policies = Snapshot(policies);
    }

    /// <summary>Consults the current chain in order, returning the first non-permit verdict (the rest are not consulted) or a permit when every policy permits.</summary>
    /// <param name="request">The call being governed.</param>
    /// <param name="cancellationToken">The token that cancels a policy consultation.</param>
    /// <returns>The folded verdict.</returns>
    private async ValueTask<NetworkGovernanceDecision> Evaluate(NetworkGovernanceRequest request, CancellationToken cancellationToken)
    {
        NetworkGovernanceDelegate[] chain = policies;
        foreach(NetworkGovernanceDelegate policy in chain)
        {
            NetworkGovernanceDecision decision = await policy(request, cancellationToken).ConfigureAwait(false);
            if(decision.Kind != NetworkGovernanceKind.Permit)
            {
                return decision;
            }
        }

        return NetworkGovernanceDecision.Permit;
    }

    /// <summary>Validates and copies the chain so the holder owns an immutable snapshot the caller cannot mutate after the fact.</summary>
    /// <param name="policies">The chain to snapshot.</param>
    /// <returns>The owned copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policies"/> or any element is <see langword="null"/>.</exception>
    private static NetworkGovernanceDelegate[] Snapshot(NetworkGovernanceDelegate[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        NetworkGovernanceDelegate[] copy = new NetworkGovernanceDelegate[policies.Length];
        for(int i = 0; i < policies.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(policies[i]);
            copy[i] = policies[i];
        }

        return copy;
    }
}
