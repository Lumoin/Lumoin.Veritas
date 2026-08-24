using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The three consultations an engine's mutable open makes against the deployment's coordinated metadata plane:
/// the identity claim before minting, the baseline intent before the local durable commit, and the baseline
/// confirm after it. A host that runs planeless passes no seams at all; a host that coordinates binds all three
/// to one <see cref="VeritasMetadataPlane"/>, so the record every consultation reads is the record every other
/// consultation wrote.
/// </summary>
/// <param name="ClaimIdentity">The identity-claim consultation.</param>
/// <param name="RecordBaselineIntent">The baseline-intent consultation.</param>
/// <param name="ConfirmBaseline">The baseline-confirm consultation.</param>
/// <remarks>
/// The seams are all-or-nothing by construction: a plane that can claim but not record intents would leave the
/// storm half-closed, so the record validates all three on construction and on a <c>with</c> expression alike.
/// The plane is never a liveness dependency — each delegate's contract names the fail-open reading of its
/// undecided arm, and the engine consults these off no hot path: an identity-bearing mutable open only.
/// </remarks>
public sealed record MetadataCoordinationSeams(
    ClaimReplicaIdentityDelegate ClaimIdentity,
    RecordLineageBaselineIntentDelegate RecordBaselineIntent,
    ConfirmLineageBaselineDelegate ConfirmBaseline)
{
    /// <summary>
    /// The identity-claim consultation. It is validated on construction and on a <c>with</c> expression alike,
    /// because the initializer writes the backing field directly and no accessor runs for it.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the delegate is <see langword="null"/>.</exception>
    public ClaimReplicaIdentityDelegate ClaimIdentity { get; init { field = Validate(value); } } = Validate(ClaimIdentity);

    /// <summary>
    /// The baseline-intent consultation. It is validated on construction and on a <c>with</c> expression alike,
    /// for the same reason the claim seam is.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the delegate is <see langword="null"/>.</exception>
    public RecordLineageBaselineIntentDelegate RecordBaselineIntent { get; init { field = Validate(value); } } = Validate(RecordBaselineIntent);

    /// <summary>
    /// The baseline-confirm consultation. It is validated on construction and on a <c>with</c> expression alike,
    /// for the same reason the claim seam is.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the delegate is <see langword="null"/>.</exception>
    public ConfirmLineageBaselineDelegate ConfirmBaseline { get; init { field = Validate(value); } } = Validate(ConfirmBaseline);

    /// <summary>Validates one seam delegate: a null seam is no coordination, which a null seams record already says.</summary>
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
