using Lumoin.Veritas.Core.Hypertrie.AccessControl;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// One consultation of a <see cref="NetworkGovernanceDelegate"/>: the boundary the call is crossing, the opaque
/// <see cref="AccessContext"/> identifying who is asking, the self-describing network target, and the size and
/// partition hints a policy weighs. The <see cref="PeerKey"/> is a tagged, type-erased handle (see
/// <see cref="NetworkPeerKey"/>) so this Core type names no transport-specific identifier type from a downstream
/// assembly, while the policy still interprets the target by its tag rather than by inferring from the boundary.
/// </summary>
/// <param name="Boundary">The boundary at which the decision is taken.</param>
/// <param name="Context">The opaque access context (the authenticated "who is asking"); <see langword="null"/> when the call carries none.</param>
/// <param name="PeerKey">The self-describing network target (a replica id, an endpoint IRI, or a socket address), or <see cref="NetworkPeerKey.None"/> when the peer is unidentified. The request only borrows it; the caller that rented it owns and disposes it once the decision completes.</param>
/// <param name="OperationSizeHint">A non-negative size hint for THIS ONE call and never for the operation it belongs to — for <see cref="NetworkBoundary.OutboundReplicationFetch"/> the symbol budget of the single fetch being made, counted in symbols, and for a query an estimated byte or row weight — that a rate or quota policy may weigh; 0 when unknown.</param>
/// <param name="PartitionCoordinate">The partition or hypercube-cell coordinate the call belongs to, for a topology-aware policy; -1 when the call is not partitioned.</param>
public readonly record struct NetworkGovernanceRequest(
    NetworkBoundary Boundary,
    AccessContext? Context,
    NetworkPeerKey PeerKey,
    long OperationSizeHint,
    int PartitionCoordinate);
