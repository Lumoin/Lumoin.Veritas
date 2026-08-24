using Lumoin.Veritas.Core.Causality;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Answers which connection seam reaches ONE named cluster member's metadata endpoint, so that a deployment
/// whose membership changes under it is addressed by identity rather than by a list of routes fixed when the
/// host was composed.
/// </summary>
/// <param name="member">The member to reach, named by the replica identity axis that is also its consensus identity.</param>
/// <returns>The seam that opens one duplex connection to that member's metadata endpoint.</returns>
/// <remarks>
/// <para>
/// IT LOOKS A MEMBER UP RATHER THAN DIALING ONE. It is called synchronously and it hands back a seam, and the
/// <see cref="MetadataChannelClient"/> built over that seam is what dials — on its first call, reusing the one
/// connection afterwards. A resolver that connected here would put a round trip inside a lookup that a
/// consensus attempt makes on its own path.
/// </para>
/// <para>
/// A MEMBER IT CANNOT PLACE REPORTS SO BY THROWING, and the throw is an ordinary answer rather than a defect: a
/// register asked to reach an unplaceable member keeps that member's slot and treats it as an unreachable
/// recorder, which the protocol already handles, so the quorum stays counted over the membership the record
/// names. Returning a seam that always faults says the same thing.
/// </para>
/// <para>
/// It is asked about identities no founder list named. A membership change admits replicas, and a host learns
/// of a joiner from the record that installed it, so the deployment's own locator — not the genesis list — is
/// what makes a member reachable.
/// </para>
/// </remarks>
public delegate OpenPeerMetadataConnectionDelegate ResolvePeerMetadataConnectionDelegate(ReplicaAxis member);
