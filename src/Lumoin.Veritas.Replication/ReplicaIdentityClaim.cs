using Lumoin.Veritas.Core.Causality;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One replica identity axis claimed on the coordinated metadata record, with the register version the claim
/// was decided at. A replica claims its axis before it mints a dot under it, so a second host attempting to
/// mint under a claimed axis is refused at claim time by consensus rather than detected afterwards, once
/// colliding dots have already crossed the wire.
/// </summary>
/// <param name="Axis">The claimed replica identity axis.</param>
/// <param name="ClaimedAt">The register version the claim was decided at.</param>
/// <remarks>
/// <para>
/// The claim is append-only: it is added when the axis is absent from the record and never rewritten in place,
/// so <see cref="ClaimedAt"/> names the version that first settled the axis rather than the version of the last
/// record that carried it. Releasing a claim is not a plane obligation yet, so an axis that has been claimed
/// stays claimed for the deployment's life.
/// </para>
/// <para>
/// Equality is the synthesized record-struct equality, and it is content-based in both members without help:
/// <see cref="ReplicaAxis"/> compares its identity BYTES (it holds them as memory but its
/// <see cref="ReplicaAxis.Equals(ReplicaAxis)"/> reads the span, never the memory's backing object), and
/// <see cref="RegisterVersion"/> is a number. A claim decoded from bytes therefore equals the claim that was
/// encoded, which is what lets <see cref="VeritasMetadataRecord"/> compare its claim list element-wise and have
/// the comparison survive a codec round trip.
/// </para>
/// </remarks>
public readonly record struct ReplicaIdentityClaim(ReplicaAxis Axis, RegisterVersion ClaimedAt);
