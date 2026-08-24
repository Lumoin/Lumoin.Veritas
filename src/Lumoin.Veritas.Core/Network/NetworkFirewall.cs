using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO.Hashing;
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
/// Reads are lock-free and allocation-free: the denylist is an immutable <see cref="FrozenSet{T}"/> swapped
/// atomically on each mutation (copy-on-write), so a governed call reads a consistent snapshot with no contention,
/// while the infrequent admin mutations serialize on a private gate. A peer is keyed by its kind and byte content
/// (the kind discriminator prefixed to the identifier bytes), not a hash, so a denylist entry matches exactly and
/// cannot be evaded or falsely matched by a hash collision; the read path probes the set over a stack-built key
/// span through the set's <see cref="ReadOnlySpan{T}"/> alternate lookup, materializing no key object.
/// </remarks>
public sealed class NetworkFirewall
{
    private const int StackKeyThreshold = 256;

    private static FrozenSet<byte[]> EmptyDenied { get; } = Array.Empty<byte[]>().ToFrozenSet(ByteContentComparer.Instance);

    private readonly Lock mutationGate = new();

    //A naked field: the denylist is swapped by reference under Volatile semantics so governed calls read a
    //consistent immutable snapshot without locking; only the rare mutations take the gate.
    private volatile FrozenSet<byte[]> denied = EmptyDenied;

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
        byte[] key = BuildKey(kind, peer);
        lock(mutationGate)
        {
            HashSet<byte[]> next = new(denied, ByteContentComparer.Instance) { key };
            denied = next.ToFrozenSet(ByteContentComparer.Instance);
        }
    }

    /// <summary>Removes a peer from the denylist; calls naming it are permitted again from the next call on.</summary>
    /// <param name="kind">The peer's identifier kind.</param>
    /// <param name="peer">The peer's identifier bytes.</param>
    public void Allow(NetworkPeerKeyKind kind, ReadOnlySpan<byte> peer)
    {
        byte[] key = BuildKey(kind, peer);
        lock(mutationGate)
        {
            if(!denied.Contains(key))
            {
                return;
            }

            HashSet<byte[]> next = new(denied, ByteContentComparer.Instance);
            next.Remove(key);
            denied = next.ToFrozenSet(ByteContentComparer.Instance);
        }
    }

    /// <summary>Empties the denylist; every peer is permitted again.</summary>
    public void Clear()
    {
        lock(mutationGate)
        {
            denied = EmptyDenied;
        }
    }

    /// <summary>Permits the call unless its peer key is on the denylist; reads an atomic snapshot of the denylist with no lock and no allocation.</summary>
    /// <param name="request">The call being governed.</param>
    /// <param name="cancellationToken">The token; unused, the verdict is immediate and local.</param>
    /// <returns>A completed deny verdict for a denylisted peer, otherwise a permit.</returns>
    private ValueTask<NetworkGovernanceDecision> Evaluate(NetworkGovernanceRequest request, CancellationToken cancellationToken)
    {
        bool isDenied = IsDenied(denied, request.PeerKey);

        return new ValueTask<NetworkGovernanceDecision>(isDenied ? NetworkGovernanceDecision.Deny : NetworkGovernanceDecision.Permit);
    }

    /// <summary>Whether the peer is on the snapshot's denylist, probed over a stack-built kind-prefixed key span; an empty denylist or an unidentified peer is never denied.</summary>
    /// <param name="snapshot">The denylist snapshot to probe.</param>
    /// <param name="peer">The peer key the call names.</param>
    /// <returns><see langword="true"/> when the peer is denylisted.</returns>
    private static bool IsDenied(FrozenSet<byte[]> snapshot, NetworkPeerKey peer)
    {
        if(snapshot.Count == 0 || peer.IsUnidentified)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = peer.Bytes.Span;
        int keyLength = bytes.Length + 1;
        byte[]? rented = keyLength > StackKeyThreshold ? ArrayPool<byte>.Shared.Rent(keyLength) : null;
        Span<byte> key = rented is null ? stackalloc byte[keyLength] : rented.AsSpan(0, keyLength);
        key[0] = (byte)(int)peer.Tag.Get<NetworkPeerKeyKind>();
        bytes.CopyTo(key[1..]);

        bool isDenied = snapshot.GetAlternateLookup<ReadOnlySpan<byte>>().Contains(key);

        if(rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return isDenied;
    }

    /// <summary>Builds the stored denylist key: the kind discriminator followed by the identifier bytes, so entries match exactly by content.</summary>
    /// <param name="kind">The identifier kind.</param>
    /// <param name="bytes">The identifier bytes.</param>
    /// <returns>The owned key bytes.</returns>
    private static byte[] BuildKey(NetworkPeerKeyKind kind, ReadOnlySpan<byte> bytes)
    {
        byte[] key = new byte[bytes.Length + 1];
        key[0] = (byte)(int)kind;
        bytes.CopyTo(key.AsSpan(1));

        return key;
    }

    /// <summary>Content equality over key bytes for the denylist, with a <see cref="ReadOnlySpan{T}"/> alternate lookup so the read path probes without materializing a key array.</summary>
    private sealed class ByteContentComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        /// <summary>The shared comparer.</summary>
        public static ByteContentComparer Instance { get; } = new();

        /// <summary>Whether two key arrays have equal byte content.</summary>
        /// <param name="x">The first key.</param>
        /// <param name="y">The second key.</param>
        /// <returns><see langword="true"/> when both are <see langword="null"/> or have equal content.</returns>
        public bool Equals(byte[]? x, byte[]? y)
        {
            return x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);
        }

        /// <summary>A content hash of the key array.</summary>
        /// <param name="obj">The key.</param>
        /// <returns>The content hash.</returns>
        public int GetHashCode(byte[] obj)
        {
            return unchecked((int)XxHash3.HashToUInt64(obj));
        }

        /// <summary>Whether a probe span equals a stored key by byte content.</summary>
        /// <param name="alternate">The probe span.</param>
        /// <param name="other">The stored key.</param>
        /// <returns><see langword="true"/> when the content is equal.</returns>
        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other)
        {
            return alternate.SequenceEqual(other);
        }

        /// <summary>A content hash of the probe span, matching <see cref="GetHashCode(byte[])"/> for equal content.</summary>
        /// <param name="alternate">The probe span.</param>
        /// <returns>The content hash.</returns>
        public int GetHashCode(ReadOnlySpan<byte> alternate)
        {
            return unchecked((int)XxHash3.HashToUInt64(alternate));
        }

        /// <summary>Materializes a stored key from a probe span (used only when adding through the alternate lookup).</summary>
        /// <param name="alternate">The probe span.</param>
        /// <returns>The owned key bytes.</returns>
        public byte[] Create(ReadOnlySpan<byte> alternate)
        {
            return alternate.ToArray();
        }
    }
}
