using Lumoin.Veritas.Core.Causality;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The coordinator lease on the metadata record: which replica currently holds the coordinating role and the
/// term it took it at. The lease is taken, refreshed, and released by consensus writes, so at most one member
/// holds it at a time and succession is settled by agreement rather than by a race.
/// </summary>
/// <param name="Holder">The replica identity axis of the member holding the lease.</param>
/// <param name="Term">The register version the lease was taken or last refreshed at.</param>
/// <remarks>
/// <para>
/// A lease is not a timeout. Its term is a register version and never a clock reading: the consensus procedure
/// is timeout-free, and the plane provides SAFE succession — a vacant lease is taken by any member, a lease
/// held by the caller is refreshed, a lease held by another CURRENT member is not usurped, and a lease held by
/// a replica outside the current membership is taken over freely, which ties usurpation to retiring the dead
/// holder through the membership obligation the plane already coordinates. Deciding that a holder is dead is an
/// application-level health signal outside this plane; the plane never embeds one.
/// </para>
/// <para>
/// Equality is the synthesized record equality and is content-based in both members without help:
/// <see cref="ReplicaAxis"/> compares its identity bytes and <see cref="RegisterVersion"/> is a number, so a
/// lease decoded from bytes equals the lease that was encoded — the property the containing record's comparison
/// rests on (<see cref="VeritasMetadataRecord"/>).
/// </para>
/// </remarks>
public sealed record CoordinatorLease(ReplicaAxis Holder, RegisterVersion Term);
