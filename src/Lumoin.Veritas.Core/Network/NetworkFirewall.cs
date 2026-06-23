using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A live-configurable denylist firewall: a <see cref="NetworkGovernanceDelegate"/> that denies a call whose peer
/// key is on the denylist and permits every other. It is one facet of network governance — the allow/deny facet —
/// and the first concrete provider behind the seam; the rate/concurrency facet plugs in beside it. The denylist is
/// the real-time control-in surface: <see cref="Deny"/> / <see cref="Allow"/> / <see cref="Clear"/> are the
/// entry points an editor, MCP, or CLI command drives to reconfigure a running engine, and the change takes effect
/// on the next call.
/// </summary>
/// <remarks>
/// Reads are lock-free: the denylist is an immutable <see cref="FrozenSet{T}"/> swapped atomically on each
/// mutation (copy-on-write), so a governed call reads a consistent snapshot with no contention, while the
/// infrequent admin mutations serialize on a private gate. A peer is keyed by its kind and byte content (not a
/// hash), so a denylist entry matches exactly and cannot be evaded or falsely matched by a hash collision.
/// </remarks>
public sealed class NetworkFirewall
{
    private readonly Lock mutationGate = new();

    //A naked field: the denylist is swapped by reference under Volatile semantics so governed calls read a
    //consistent immutable snapshot without locking; only the rare mutations take the gate.
    private volatile FrozenSet<string> denied = FrozenSet<string>.Empty;

    /// <summary>Creates a firewall that permits every peer until one is denied.</summary>
    public NetworkFirewall()
    {
        Decide = Evaluate;
    }

    /// <summary>The governance delegate the gate consults; permits any peer not on the denylist. Bind this at the transport decorator.</summary>
    public NetworkGovernanceDelegate Decide { get; }

    /// <summary>Adds a peer to the denylist; calls naming it are denied from the next call on. The control-in entry point an editor/MCP/CLI command drives.</summary>
    /// <param name="kind">The peer's identifier kind.</param>
    /// <param name="peer">The peer's identifier bytes.</param>
    public void Deny(NetworkPeerKeyKind kind, ReadOnlySpan<byte> peer)
    {
        string key = Key(kind, peer);
        lock(mutationGate)
        {
            HashSet<string> next = new(denied) { key };
            denied = next.ToFrozenSet();
        }
    }

    /// <summary>Removes a peer from the denylist; calls naming it are permitted again from the next call on.</summary>
    /// <param name="kind">The peer's identifier kind.</param>
    /// <param name="peer">The peer's identifier bytes.</param>
    public void Allow(NetworkPeerKeyKind kind, ReadOnlySpan<byte> peer)
    {
        string key = Key(kind, peer);
        lock(mutationGate)
        {
            if(!denied.Contains(key))
            {
                return;
            }

            HashSet<string> next = new(denied);
            next.Remove(key);
            denied = next.ToFrozenSet();
        }
    }

    /// <summary>Empties the denylist; every peer is permitted again.</summary>
    public void Clear()
    {
        lock(mutationGate)
        {
            denied = FrozenSet<string>.Empty;
        }
    }

    /// <summary>Permits the call unless its peer key is on the denylist; reads an atomic snapshot of the denylist with no lock.</summary>
    /// <param name="request">The call being governed.</param>
    /// <param name="cancellationToken">The token; unused, the verdict is immediate and local.</param>
    /// <returns>A completed deny verdict for a denylisted peer, otherwise a permit.</returns>
    private ValueTask<NetworkGovernanceDecision> Evaluate(NetworkGovernanceRequest request, CancellationToken cancellationToken)
    {
        bool isDenied = denied.Contains(Key(request.PeerKey));

        return new ValueTask<NetworkGovernanceDecision>(isDenied ? NetworkGovernanceDecision.Deny : NetworkGovernanceDecision.Permit);
    }

    /// <summary>The denylist key for an identifier kind and its bytes: the kind's discriminator and the bytes in hex, so a key matches exactly by content.</summary>
    /// <param name="kind">The identifier kind.</param>
    /// <param name="bytes">The identifier bytes.</param>
    /// <returns>The content key.</returns>
    private static string Key(NetworkPeerKeyKind kind, ReadOnlySpan<byte> bytes)
    {
        return $"{(int)kind}:{Convert.ToHexString(bytes)}";
    }

    /// <summary>The denylist key for a peer key: an unidentified peer keys to the empty string (never produced by <see cref="Deny"/>, so it is never on the denylist).</summary>
    /// <param name="peer">The peer key.</param>
    /// <returns>The content key.</returns>
    private static string Key(NetworkPeerKey peer)
    {
        return peer.IsUnidentified
            ? string.Empty
            : Key(peer.Tag.Get<NetworkPeerKeyKind>(), peer.Bytes.Span);
    }
}
