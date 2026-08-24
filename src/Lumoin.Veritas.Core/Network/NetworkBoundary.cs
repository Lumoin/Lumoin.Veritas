namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// The boundary at which a network-governance decision is taken — the place an outbound peer/endpoint call leaves
/// the node, or an inbound request enters it. A governance policy keys on the boundary (replication and federation
/// are governed independently) so the same decision delegate serves every transport while a host can apply
/// distinct limits and firewall rules per boundary.
/// </summary>
public enum NetworkBoundary
{
    /// <summary>An outbound replication fetch of a peer's integrity sketch (the anti-entropy client side).</summary>
    OutboundReplicationFetch,

    /// <summary>An outbound SPARQL SERVICE query to a federated endpoint.</summary>
    OutboundServiceQuery,

    /// <summary>An outbound graph resolve for a SPARQL FROM / FROM NAMED dataset clause or an UPDATE LOAD.</summary>
    OutboundGraphResolve,

    /// <summary>An inbound request a node serves to a peer (the replication server side, or a query/SPARQL-protocol endpoint).</summary>
    InboundServe,

    /// <summary>A consensus metadata exchange between cluster members — outbound to a member (a record request, a committed read, a decided-record push) or inbound from one. Both directions share the boundary because they are two ends of one coordinated exchange among named members of a known cluster, which a policy governs as a whole rather than as an unrelated call out and call in.</summary>
    ConsensusExchange
}
