using System;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A structured trace event <see cref="VeritasMetadataPlane"/> emits when one coordination obligation completes:
/// which obligation it was, the value its own outcome ladder answered with, the register version the obligation
/// addressed, and how many consensus attempts it spent. It rides the same Core diagnostics bus as the storage and
/// replication events and shares their <see cref="ITraceEvent.CorrelationId"/>, so a consumer stitches a refused
/// claim or an undecided baseline write into the same per-operation timeline as the open that drove it.
/// Scalar-only, so emitting it is allocation-free under the <see langword="in"/> parameter.
/// </summary>
/// <param name="SequenceNumber">The monotonic stream sequence number the plane assigns. The plane's write loop is the sole emitter, so the numbers run in obligation-completion order over one plane.</param>
/// <param name="TimestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units, from the caller-injected time provider.</param>
/// <param name="CorrelationId">The correlation id the plane carries, shared with the operation that drove the obligation.</param>
/// <param name="Obligation">Which obligation completed. It also names the outcome ladder <paramref name="OutcomeCode"/> is read against.</param>
/// <param name="OutcomeCode">
/// The numeric value of the obligation's own outcome ladder — <see cref="IdentityClaimOutcome"/>,
/// <see cref="BaselineRecordOutcome"/>, <see cref="PolicyAmendmentOutcome"/>,
/// <see cref="CoordinatorElectionOutcome"/>, <see cref="MembershipChangeOutcome"/> or
/// <see cref="PlaneBootstrapOutcome"/>, whichever <paramref name="Obligation"/> names. The verdict is the PAIR:
/// one event type answers for six ladders, and a code carried as a number keeps the event scalar-only, where a
/// name would allocate on every emit.
/// </param>
/// <param name="Version">The register version the obligation addressed, which is the version it was decided at when the outcome is a decided one, and <see cref="RegisterVersion.Unwritten"/> when the obligation answered without writing.</param>
/// <param name="Attempts">The number of consensus attempts the obligation spent; zero when it answered without writing.</param>
public readonly record struct MetadataPlaneTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    MetadataPlaneObligation Obligation,
    int OutcomeCode,
    RegisterVersion Version,
    int Attempts): ITraceEvent;
