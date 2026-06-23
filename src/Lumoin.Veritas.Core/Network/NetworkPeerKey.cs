using System;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A self-describing network peer or endpoint key: a <see cref="NetworkPeerKeyKind"/> tag discriminating how the
/// byte payload is interpreted, plus the bytes. Carried in <see cref="NetworkGovernanceRequest"/> so a governance
/// policy and audit interpret the network target by its tag rather than inferring it from the
/// <see cref="NetworkBoundary"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Network WHERE, not authenticated WHO.</b> This type names the network target a call reaches or arrives from —
/// a replica to fetch from, an endpoint to query, a socket to route to — and is keyed by rate, quota, firewall, and
/// routing policy. Identities and credentials (TPM, OAuth, EUDI, agentic attestations, tokens, certificates) are
/// never carried here; they belong in the opaque <see cref="Lumoin.Veritas.Core.Hypertrie.AccessControl.AccessContext"/>,
/// which the library never inspects. A policy that wants an identity-aware network rule reads both the peer key
/// (the topology) and the access context (the identity); neither field does the other's job.
/// </para>
/// <para>
/// <b>A tagged, type-erased handle.</b> Core carries only a tag and bytes, so this type does not name the replica-id
/// type or a federation IRI type that live in downstream assemblies: the concrete identifier is erased to bytes at
/// this Core seam, and the tag lets a downstream consumer recover its meaning. Each boundary's transport decorator
/// fills the bytes from its own identifier type.
/// </para>
/// </remarks>
/// <param name="Kind">The discriminator: how <paramref name="Bytes"/> is to be interpreted.</param>
/// <param name="Bytes">The payload — replica-id bytes, an endpoint IRI as UTF-8, socket-address bytes, or empty.</param>
public readonly record struct NetworkPeerKey(NetworkPeerKeyKind Kind, ReadOnlyMemory<byte> Bytes)
{
    /// <summary>The unidentified peer — empty bytes, <see cref="NetworkPeerKeyKind.Unidentified"/>; the default a host passes when no peer is known.</summary>
    public static NetworkPeerKey None { get; } = new(NetworkPeerKeyKind.Unidentified, ReadOnlyMemory<byte>.Empty);

    /// <summary>Whether no peer is identified.</summary>
    public bool IsUnidentified => Kind == NetworkPeerKeyKind.Unidentified;

    /// <summary>Creates a replica-targeted key from a replica id's bytes (the replication boundary).</summary>
    /// <param name="replicaIdBytes">The replica id as bytes; not empty.</param>
    /// <returns>A <see cref="NetworkPeerKeyKind.ReplicaId"/> key over the bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="replicaIdBytes"/> is empty.</exception>
    public static NetworkPeerKey ReplicaId(ReadOnlyMemory<byte> replicaIdBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(replicaIdBytes.Length, nameof(replicaIdBytes));

        return new NetworkPeerKey(NetworkPeerKeyKind.ReplicaId, replicaIdBytes);
    }

    /// <summary>Creates an endpoint-targeted key from an absolute IRI's UTF-8 bytes (a SERVICE query or a graph resolve / LOAD).</summary>
    /// <param name="endpointIriBytes">The endpoint IRI as UTF-8 bytes; not empty.</param>
    /// <returns>An <see cref="NetworkPeerKeyKind.EndpointIri"/> key over the bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="endpointIriBytes"/> is empty.</exception>
    public static NetworkPeerKey EndpointIri(ReadOnlyMemory<byte> endpointIriBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(endpointIriBytes.Length, nameof(endpointIriBytes));

        return new NetworkPeerKey(NetworkPeerKeyKind.EndpointIri, endpointIriBytes);
    }

    /// <summary>Creates a socket-address-targeted key (an inbound peer at the network gate or direct mesh routing).</summary>
    /// <param name="addressBytes">The socket address as bytes — IPv4/IPv6 octets or a hostname:port in UTF-8, per the policy's convention; not empty.</param>
    /// <returns>A <see cref="NetworkPeerKeyKind.SocketAddress"/> key over the bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="addressBytes"/> is empty.</exception>
    public static NetworkPeerKey SocketAddress(ReadOnlyMemory<byte> addressBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(addressBytes.Length, nameof(addressBytes));

        return new NetworkPeerKey(NetworkPeerKeyKind.SocketAddress, addressBytes);
    }
}
