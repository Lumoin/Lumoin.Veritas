using System;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One founding member of a metadata chain: the replica identity axis it serves under, beside the incarnation
/// of the store admitted to answer for it.
/// </summary>
/// <param name="Axis">The replica identity axis, which is the role a host fills and what causality counts.</param>
/// <param name="Store">The incarnation the founder's store minted when that store was created.</param>
/// <remarks>
/// <para>
/// A replica identity names a ROLE and does not name the STORE answering for it. Two hosts provisioned under
/// one axis — a store wiped and restarted, or one axis deployed twice — both answer as that member honestly,
/// and a quorum counted over distinct replicas counts them once. The store's incarnation is what separates
/// them, and a membership listing the pair is what lets a host that holds the wrong store be refused rather
/// than counted.
/// </para>
/// <para>
/// PROVISIONING IS THEREFORE TWO-PHASE. A store mints its incarnation when it is created, so a founder list
/// cannot be written before the stores it names exist: create each store, read the incarnation out of it,
/// form the list from the pairs, and only then start the hosts under it.
/// <see cref="MetadataNodeStore.EnsureIncarnationAsync"/> is where a store's own incarnation comes from.
/// </para>
/// </remarks>
public readonly record struct MetadataFounder(ReplicaAxis Axis, StoreIncarnation Store)
{
    /// <summary>The consensus host identity of this founder: its axis as a consensus identity, beside its store.</summary>
    /// <returns>The host identity a membership lists this founder as.</returns>
    /// <exception cref="ArgumentException">The axis is the default axis, which carries no bytes.</exception>
    public HostId ToHostId()
    {
        return new HostId(MetadataPlaneDeployment.ReplicaIdFor(Axis), Store);
    }
}
