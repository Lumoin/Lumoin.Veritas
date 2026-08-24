using System;
using Lumoin.Verisync.Core;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The local seams one metadata serve dispatches to, bundled so a serve reaches one host and not three: the
/// recorder that answers consensus record requests, the reader that answers a catch-up with the committed
/// record this host has learned, and the apply that learns a disseminated record durably — beside the identity
/// the serving host answers a version probe under. Every seam is a consensus-library delegate the host's runner
/// satisfies by method-group conversion, so a host wires its runner in without an adapter of its own.
/// </summary>
/// <param name="Self">The host identity of the host these seams reach — its replica beside the store it holds — which is what its version-probe answers name themselves with.</param>
/// <param name="Record">The recorder seam — the host runner's record path, which serves the one instance it can derive a leader for and faults for any other.</param>
/// <param name="ReadCommitted">The catch-up seam — the host runner's committed-record read, which makes the host durable before the record it reports leaves the process.</param>
/// <param name="OfferRecord">The dissemination seam — the host runner's durable learn of a decided record.</param>
/// <remarks>
/// The identity is the serving side's own and is taken from the host these seams reach, never from the frame
/// that asked: a probe answer naming the member the caller aimed at would pass the register's mis-wiring
/// refusal whatever host actually answered, which is the one thing that refusal exists to catch. It names the
/// store as well as the replica, so an answer from a store the membership never admitted is caught by the same
/// comparison rather than counted as the member it was provisioned under.
/// </remarks>
public sealed record MetadataServeBinding(
    HostId Self,
    VersionedRecorderEndpointDelegate<CommittedMetadataRecord> Record,
    ReadCommittedRecordDelegate<VeritasMetadataRecord> ReadCommitted,
    OfferMetadataRecordDelegate OfferRecord)
{
    /// <summary>
    /// The identity the serving host answers a version probe under. It is validated on construction and on a
    /// <c>with</c> expression alike, because the initializer writes the backing field directly and no accessor
    /// runs for it.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the identity names no replica.</exception>
    public HostId Self { get; init { field = ValidateSelf(value); } } = ValidateSelf(Self);

    /// <summary>
    /// The recorder seam. It is validated on construction and on a <c>with</c> expression alike, for the same
    /// reason the identity is.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the seam is <see langword="null"/>.</exception>
    public VersionedRecorderEndpointDelegate<CommittedMetadataRecord> Record { get; init { field = Validate(value); } } = Validate(Record);

    /// <summary>
    /// The catch-up seam. It is validated on construction and on a <c>with</c> expression alike, for the same
    /// reason the identity is.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the seam is <see langword="null"/>.</exception>
    public ReadCommittedRecordDelegate<VeritasMetadataRecord> ReadCommitted { get; init { field = Validate(value); } } = Validate(ReadCommitted);

    /// <summary>
    /// The dissemination seam. It is validated on construction and on a <c>with</c> expression alike, for the
    /// same reason the identity is.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the seam is <see langword="null"/>.</exception>
    public OfferMetadataRecordDelegate OfferRecord { get; init { field = Validate(value); } } = Validate(OfferRecord);

    /// <summary>
    /// Validates the probe identity: the default value names no replica, and a serve answering under it would
    /// be diagnosed at the asking register as a mis-wired endpoint map — pointing an operator at the one thing
    /// that is not wrong — so it is refused here, where the mis-plumbed binding actually is.
    /// </summary>
    /// <param name="value">The identity to validate.</param>
    /// <returns>The validated identity.</returns>
    /// <exception cref="ArgumentException">Thrown if the identity is the default value.</exception>
    private static HostId ValidateSelf(HostId value)
    {
        if(value.Replica == default)
        {
            throw new ArgumentException("A metadata serve binding names the host identity its host answers a version probe under; the default identity names no replica.", nameof(value));
        }

        return value;
    }

    /// <summary>Validates one seam delegate: a null seam is no serve, which an absent binding already says.</summary>
    /// <typeparam name="TDelegate">The seam delegate's type.</typeparam>
    /// <param name="value">The delegate to validate.</param>
    /// <returns>The validated delegate.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the delegate is <see langword="null"/>.</exception>
    private static TDelegate Validate<TDelegate>(TDelegate value)
        where TDelegate: class
    {
        ArgumentNullException.ThrowIfNull(value);

        return value;
    }
}
