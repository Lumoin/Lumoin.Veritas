using System;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// One consultation of a <see cref="NetworkGovernanceDelegate"/>: the boundary the call is crossing, the opaque
/// <see cref="AccessContext"/> identifying who is asking, a boundary-interpreted peer/endpoint key, and the size
/// and partition hints a policy weighs. The key is carried as raw bytes so this Core type stays free of the
/// transport-specific identifier types (a federation endpoint IRI, a replica id) that live in downstream
/// assemblies: the outbound decorator fills it with the endpoint IRI's UTF-8 bytes or the peer-id bytes for its
/// boundary, and the policy interprets it per boundary.
/// </summary>
/// <param name="Boundary">The boundary at which the decision is taken.</param>
/// <param name="Context">The opaque access context (the "who is asking"); <see langword="null"/> when the call carries none.</param>
/// <param name="PeerKey">The peer or endpoint identifier as bytes, interpreted per <paramref name="Boundary"/>; empty when the peer is unidentified.</param>
/// <param name="OperationSizeHint">A non-negative size hint for the call — the symbol budget for a replication fetch, an estimated byte or row weight for a query — that a rate or quota policy may weigh; 0 when unknown.</param>
/// <param name="PartitionCoordinate">The partition or hypercube-cell coordinate the call belongs to, for a topology-aware policy; -1 when the call is not partitioned.</param>
public readonly record struct NetworkGovernanceRequest(
    NetworkBoundary Boundary,
    AccessContext? Context,
    ReadOnlyMemory<byte> PeerKey,
    long OperationSizeHint,
    int PartitionCoordinate);
