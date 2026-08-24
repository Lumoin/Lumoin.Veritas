namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// Which kind of network target a <see cref="NetworkPeerKey"/>'s bytes name — carried in the key's
/// <see cref="Lumoin.Base.Tag"/> as the out-of-band description of the otherwise-opaque identifier bytes. There is
/// one kind per byte payload, not one per <see cref="NetworkBoundary"/> — an outbound SERVICE query and a graph
/// resolve both carry an endpoint IRI, so both use <see cref="EndpointIri"/> and the boundary distinguishes them.
/// Identities and credentials are never a peer-key kind; they live in the opaque access context.
/// </summary>
public enum NetworkPeerKeyKind
{
    /// <summary>No peer identified — empty bytes. The default when no peer is known (an inbound serve before the gate resolves the caller); a policy denies or rate-limits it by default.</summary>
    Unidentified = 0,

    /// <summary>The bytes are a replica identifier (a replica id serialized to bytes), for a replication fetch or an inbound replication serve.</summary>
    ReplicaId = 1,

    /// <summary>The bytes are an absolute endpoint IRI as UTF-8: a federated SPARQL SERVICE endpoint, or a FROM / FROM NAMED / LOAD graph-resolve source.</summary>
    EndpointIri = 2,

    /// <summary>The bytes are a raw network socket address (IPv4/IPv6 octets or a hostname:port in UTF-8), for an inbound peer at the network gate or direct mesh routing; the address layout is the policy's agreed convention, so Core stays protocol-agnostic.</summary>
    SocketAddress = 3,
}
