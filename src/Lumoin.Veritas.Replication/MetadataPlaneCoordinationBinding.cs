using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Binds ONE metadata plane to the three consultations an engine's mutable open makes: it holds the plane and
/// the attempt budget every obligation is spent under, and exposes the seams as method groups over itself.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ADAPTER EXISTS AT ALL. The plane's obligations answer a
/// <see cref="MetadataPlaneResult{TOutcome}"/> and each takes an attempt budget; the engine's seams answer the
/// outcome alone and carry no budget. The budget is a host's deployment choice rather than a per-consultation
/// one — it bounds how long an identity-bearing open may spend against an unreachable quorum before it fails
/// open — so it is held here once and read by all three seams.
/// </para>
/// <para>
/// THE RECORD AND THE VERSION AN OBLIGATION DECIDED ARE DELIBERATELY DROPPED. The engine reads the outcome and
/// nothing else, and a caller that wants the record reads it with
/// <see cref="VeritasMetadataPlane.ReadRecordAsync"/>, which is a catch-up rather than a claim of currency.
/// </para>
/// <para>
/// ONE BINDING NAMES ONE PLANE. Every seam reaches the same instance, so an obligation the engine's open
/// enqueued and one a host's own operator surface enqueued are serialized by that plane's single-consumer write
/// queue — which is what makes the record every consultation reads the record every other consultation wrote.
/// </para>
/// <para>
/// THE PLANE OUTLIVES THE ENGINE. The confirm arm runs at the open, but a host may drive further obligations
/// against the same plane afterwards, and disposing a plane under an in-flight consultation would abandon that
/// obligation — so a host disposes its engine first and its plane after it.
/// </para>
/// </remarks>
public sealed class MetadataPlaneCoordinationBinding
{
    /// <summary>Binds one plane and the budget its obligations are spent under.</summary>
    /// <param name="plane">The plane every consultation reaches.</param>
    /// <param name="attemptBudget">How many consensus attempts one consultation may spend before it answers that it is undecided; at least one.</param>
    /// <exception cref="ArgumentNullException">Thrown if the plane is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the budget is below one, which is a budget that permits no attempt at all.</exception>
    public MetadataPlaneCoordinationBinding(VeritasMetadataPlane plane, int attemptBudget)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        Plane = plane;
        AttemptBudget = attemptBudget;
    }

    /// <summary>The plane every consultation reaches.</summary>
    private VeritasMetadataPlane Plane { get; }

    /// <summary>How many consensus attempts one consultation may spend before it answers that it is undecided.</summary>
    private int AttemptBudget { get; }

    /// <summary>
    /// The three seams an engine is configured with, each a method group over this instance so nothing captures
    /// an enclosing scope.
    /// </summary>
    public MetadataCoordinationSeams Seams => new(ClaimIdentityAsync, RecordBaselineIntentAsync, ConfirmBaselineAsync);

    /// <summary>Claims a replica identity axis on the coordinated record — a <see cref="ClaimReplicaIdentityDelegate"/>.</summary>
    /// <param name="axis">The identity axis the opening host will mint under.</param>
    /// <param name="cancellationToken">The token that cancels the consultation.</param>
    /// <returns>The claim's value-based outcome.</returns>
    public async ValueTask<IdentityClaimOutcome> ClaimIdentityAsync(ReplicaAxis axis, CancellationToken cancellationToken)
    {
        MetadataPlaneResult<IdentityClaimOutcome> result = await Plane.ClaimIdentityAsync(axis, AttemptBudget, cancellationToken).ConfigureAwait(false);

        return result.Outcome;
    }

    /// <summary>Records the lineage baseline's intent on the coordinated record — a <see cref="RecordLineageBaselineIntentDelegate"/>.</summary>
    /// <param name="claimantAxis">The identity axis the baseline dots are minted on.</param>
    /// <param name="causalityDigest">The digest of the minted baseline causality.</param>
    /// <param name="cancellationToken">The token that cancels the consultation.</param>
    /// <returns>The intent's value-based outcome.</returns>
    public async ValueTask<BaselineRecordOutcome> RecordBaselineIntentAsync(ReplicaAxis claimantAxis, NodeIdentifier causalityDigest, CancellationToken cancellationToken)
    {
        MetadataPlaneResult<BaselineRecordOutcome> result = await Plane.RecordBaselineIntentAsync(claimantAxis, causalityDigest, AttemptBudget, cancellationToken).ConfigureAwait(false);

        return result.Outcome;
    }

    /// <summary>Confirms the lineage baseline on the coordinated record — a <see cref="ConfirmLineageBaselineDelegate"/>.</summary>
    /// <param name="causalityDigest">The digest that matches this confirm to its intent.</param>
    /// <param name="stateId">The dataset StateId the committed baseline produced.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
    /// <param name="cancellationToken">The token that cancels the consultation.</param>
    /// <returns>The confirm's value-based outcome.</returns>
    public async ValueTask<BaselineRecordOutcome> ConfirmBaselineAsync(NodeIdentifier causalityDigest, NodeIdentifier stateId, long dictionaryEpoch, CancellationToken cancellationToken)
    {
        MetadataPlaneResult<BaselineRecordOutcome> result = await Plane.ConfirmBaselineAsync(causalityDigest, stateId, dictionaryEpoch, AttemptBudget, cancellationToken).ConfigureAwait(false);

        return result.Outcome;
    }
}
