using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Claims a replica identity axis on the deployment's coordinated metadata record before its owner mints under
/// it. The engine consults this at an identity-bearing mutable open when metadata coordination is configured;
/// the host binds it to <see cref="VeritasMetadataPlane.ClaimIdentityAsync"/> with the attempt budget of its
/// choosing.
/// </summary>
/// <param name="axis">The identity axis the opening host supplies, which is the axis it will mint under.</param>
/// <param name="cancellationToken">The token that cancels the consultation.</param>
/// <returns>
/// The claim's value-based outcome. <see cref="IdentityClaimOutcome.Undecided"/> is what an unreachable or
/// quorumless plane produces, and the open FAILS OPEN on it — the plane is never a liveness dependency of the
/// data lane; only the definite <see cref="IdentityClaimOutcome.RefusedHeldByOther"/> refuses the open.
/// </returns>
public delegate ValueTask<IdentityClaimOutcome> ClaimReplicaIdentityDelegate(ReplicaAxis axis, CancellationToken cancellationToken);
