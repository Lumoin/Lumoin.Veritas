namespace Lumoin.Veritas.Replication;

/// <summary>
/// Which coordination obligation a <see cref="MetadataPlaneTraceEvent"/> reports on. It names the docket entry
/// and, with it, the outcome ladder the event's outcome code is read against.
/// </summary>
public enum MetadataPlaneObligation
{
    /// <summary>The chain's first write, which commits the deterministic initial record under the genesis membership. Its outcome code is a <see cref="PlaneBootstrapOutcome"/>.</summary>
    Bootstrap = 0,

    /// <summary>A replica identity axis claimed on the record before its owner mints under it. Its outcome code is an <see cref="IdentityClaimOutcome"/>.</summary>
    IdentityClaim = 1,

    /// <summary>The lineage baseline's INTENT write, recorded before the minting replica's local durable commit. Its outcome code is a <see cref="BaselineRecordOutcome"/>.</summary>
    BaselineIntent = 2,

    /// <summary>The lineage baseline's CONFIRM write, recorded after the minting replica's local durable commit. Its outcome code is a <see cref="BaselineRecordOutcome"/>.</summary>
    BaselineConfirm = 3,

    /// <summary>An amendment of the coordination policy every host of the deployment reads identically. Its outcome code is a <see cref="PolicyAmendmentOutcome"/>.</summary>
    PolicyAmendment = 4,

    /// <summary>The coordinator lease taken or refreshed. Its outcome code is a <see cref="CoordinatorElectionOutcome"/>.</summary>
    CoordinatorElection = 5,

    /// <summary>The coordinator lease vacated by its holder. Its outcome code is a <see cref="CoordinatorElectionOutcome"/>.</summary>
    CoordinatorRelease = 6,

    /// <summary>A replica admitted to the chain's membership. Its outcome code is a <see cref="MembershipChangeOutcome"/>.</summary>
    MemberAdmission = 7,

    /// <summary>A replica retired from the chain's membership. Its outcome code is a <see cref="MembershipChangeOutcome"/>.</summary>
    MemberRetirement = 8
}
