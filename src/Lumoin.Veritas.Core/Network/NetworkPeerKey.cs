using System;
using System.Buffers;
using Lumoin.Base;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A network peer or endpoint key: an owned, pool-allocated block of identifier bytes described by a
/// <see cref="Tag"/> carrying its out-of-band <see cref="NetworkPeerKeyKind"/>. The peer key is the opaque data
/// block the <see cref="Tag"/> idiom is for — the bytes are an identifier whose meaning lives beside them in the
/// tag, not inferred from the <see cref="NetworkBoundary"/>. The concrete transport identifier (a replica id, a
/// federation endpoint IRI) is erased to bytes at this Core seam (Core names no downstream identifier type), and
/// the tag lets a policy or a downstream consumer recover the kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owned and disposable.</b> The bytes are copied into a buffer rented from the caller's pool, so the key
/// remains valid across the asynchronous governance decision that may round-trip remote hardware — a borrowed
/// view of a caller's stack buffer could not. The caller that rents the key owns it and disposes it once the
/// decision completes; <see cref="NetworkGovernanceRequest"/> only borrows it.
/// </para>
/// <para>
/// <b>Network WHERE, not authenticated WHO.</b> This names the network target a call reaches or arrives from. The
/// authenticated identity and its credentials (TPM, OAuth, EUDI, agentic attestations) live in the opaque
/// <see cref="Lumoin.Veritas.Core.Hypertrie.AccessControl.AccessContext"/>, never in a peer key.
/// </para>
/// </remarks>
public sealed class NetworkPeerKey : IDisposable
{
    /// <summary>The shared tag describing replica-id bytes; built once.</summary>
    private static Tag ReplicaIdTag { get; } = Tag.Empty.With(NetworkPeerKeyKind.ReplicaId);

    /// <summary>The shared tag describing endpoint-IRI bytes; built once.</summary>
    private static Tag EndpointIriTag { get; } = Tag.Empty.With(NetworkPeerKeyKind.EndpointIri);

    /// <summary>The shared tag describing socket-address bytes; built once.</summary>
    private static Tag SocketAddressTag { get; } = Tag.Empty.With(NetworkPeerKeyKind.SocketAddress);

    private readonly IMemoryOwner<byte> owner;
    private readonly int length;

    /// <summary>Creates a key over an owned buffer and its describing tag.</summary>
    /// <param name="owner">The buffer owner backing the bytes; disposed with this key.</param>
    /// <param name="length">The number of identifier bytes at the front of <paramref name="owner"/>.</param>
    /// <param name="tag">The tag describing the bytes.</param>
    private NetworkPeerKey(IMemoryOwner<byte> owner, int length, Tag tag)
    {
        this.owner = owner;
        this.length = length;
        Tag = tag;
    }

    /// <summary>The unidentified peer — no bytes, an empty tag; the default a host passes when no peer is known. Shared and holds no pooled buffer, so disposing it is a no-op.</summary>
    public static NetworkPeerKey None { get; } = new(EmptyOwner.Instance, 0, Tag.Empty);

    /// <summary>The out-of-band metadata describing the bytes — at minimum the <see cref="NetworkPeerKeyKind"/>, retrieved through the tag.</summary>
    public Tag Tag { get; }

    /// <summary>The identifier bytes this key owns.</summary>
    public ReadOnlyMemory<byte> Bytes => owner.Memory[..length];

    /// <summary>Whether no peer is identified (no bytes).</summary>
    public bool IsUnidentified => length == 0;

    /// <summary>Rents a replica-targeted key, copying the replica id's bytes into a pooled buffer (the replication boundary).</summary>
    /// <param name="pool">The pool the buffer is rented from.</param>
    /// <param name="replicaId">The replica id as bytes; not empty.</param>
    /// <returns>An owned <see cref="NetworkPeerKeyKind.ReplicaId"/> key; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="replicaId"/> is empty.</exception>
    public static NetworkPeerKey RentReplicaId(MemoryPool<byte> pool, ReadOnlySpan<byte> replicaId)
    {
        return Rent(pool, replicaId, ReplicaIdTag);
    }

    /// <summary>Rents an endpoint-targeted key, copying the absolute IRI's UTF-8 bytes into a pooled buffer (a SERVICE query or a graph resolve / LOAD).</summary>
    /// <param name="pool">The pool the buffer is rented from.</param>
    /// <param name="endpointIri">The endpoint IRI as UTF-8 bytes; not empty.</param>
    /// <returns>An owned <see cref="NetworkPeerKeyKind.EndpointIri"/> key; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="endpointIri"/> is empty.</exception>
    public static NetworkPeerKey RentEndpointIri(MemoryPool<byte> pool, ReadOnlySpan<byte> endpointIri)
    {
        return Rent(pool, endpointIri, EndpointIriTag);
    }

    /// <summary>Rents a socket-address-targeted key, copying the address bytes into a pooled buffer (an inbound peer at the network gate or direct mesh routing).</summary>
    /// <param name="pool">The pool the buffer is rented from.</param>
    /// <param name="address">The socket address as bytes — IPv4/IPv6 octets or a hostname:port in UTF-8, per the policy's convention; not empty.</param>
    /// <returns>An owned <see cref="NetworkPeerKeyKind.SocketAddress"/> key; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="address"/> is empty.</exception>
    public static NetworkPeerKey RentSocketAddress(MemoryPool<byte> pool, ReadOnlySpan<byte> address)
    {
        return Rent(pool, address, SocketAddressTag);
    }

    /// <summary>Returns the owned buffer to its pool. A no-op for <see cref="None"/>, which holds none.</summary>
    public void Dispose()
    {
        owner.Dispose();
    }

    /// <summary>Rents a buffer, copies the identifier bytes in, and tags the result.</summary>
    /// <param name="pool">The pool the buffer is rented from.</param>
    /// <param name="source">The identifier bytes to copy; not empty.</param>
    /// <param name="tag">The tag describing the bytes.</param>
    /// <returns>The owned, tagged key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is empty.</exception>
    private static NetworkPeerKey Rent(MemoryPool<byte> pool, ReadOnlySpan<byte> source, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfZero(source.Length, nameof(source));

        IMemoryOwner<byte> owner = pool.Rent(source.Length);
        source.CopyTo(owner.Memory.Span);

        return new NetworkPeerKey(owner, source.Length, tag);
    }

    /// <summary>An <see cref="IMemoryOwner{T}"/> over no bytes, backing <see cref="None"/> so it holds no rented buffer and is safe to dispose repeatedly.</summary>
    private sealed class EmptyOwner : IMemoryOwner<byte>
    {
        /// <summary>The shared empty owner.</summary>
        public static EmptyOwner Instance { get; } = new();

        /// <summary>The empty backing memory.</summary>
        public Memory<byte> Memory => Memory<byte>.Empty;

        /// <summary>Does nothing; there is no rented memory to return.</summary>
        public void Dispose()
        {
        }
    }
}
