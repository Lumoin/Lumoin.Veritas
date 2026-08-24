using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The agreed lineage baseline of a deployment: which replica minted the baseline the whole lineage descends
/// from, the causality digest that names it, and — once the minting replica has committed it durably — the
/// dataset state the baseline produced. It is recorded in TWO writes matched by the digest, so a baseline is an
/// agreed fact rather than a per-host claim, and two hosts cannot independently seed one lineage.
/// </summary>
/// <param name="ClaimantAxis">The replica identity axis the baseline dots were minted on.</param>
/// <param name="CausalityDigest">The content fingerprint of the minted baseline causality.</param>
/// <param name="Confirmation">The confirmation the minting replica's durable commit produced, or <see langword="null"/> while the intent is unconfirmed.</param>
/// <param name="RecordedAt">The register version the most recent of the two writes was decided at.</param>
/// <remarks>
/// <para>
/// THE TWO PHASES. The INTENT write happens before the local durable commit and carries
/// <see cref="ClaimantAxis"/> and <see cref="CausalityDigest"/> only: the digest over the minted
/// <see cref="CommitCausality"/> is the only lineage identity that exists at that point, because the dataset
/// StateId and the dictionary epoch are born at create and cannot ride the earlier write. The CONFIRM write
/// happens after the local durable commit and fills <see cref="StateId"/> and <see cref="DictionaryEpoch"/>,
/// matched to the intent by a byte-identical digest.
/// </para>
/// <para>
/// THE TRI-STATE, STRUCTURAL. An absent <see cref="Confirmation"/> means UNCONFIRMED and never zero: a
/// baseline whose state is genuinely the empty dataset still carries a confirmation value once confirmed, so a
/// reader can tell an unconfirmed intent from a confirmed empty baseline. <see cref="IsConfirmed"/> is the
/// single reading of that state, and a clone gates on it. The two confirmation facts travel as ONE
/// <see cref="LineageConfirmation"/> value, so a half-confirmed baseline is unconstructible by shape — the
/// invariant is structural, not a rule a caller could skip.
/// </para>
/// <para>
/// IDEMPOTENCE BY BYTE COMPARISON. Minting a baseline is deterministic given the identity and the present
/// triples, so a replica that crashed between the intent and its own commit reproduces the same digest on the
/// next open and its retry is recognized as an identical repeat rather than a second lineage. Only a genuinely
/// different tuple is a conflict, and a conflict is a loud refusal, because the alternative is two lineages
/// silently agreeing to disagree.
/// </para>
/// <para>
/// Equality is the synthesized record equality and is content-based in every member without help:
/// <see cref="ReplicaAxis"/> compares its identity bytes, <see cref="NodeIdentifier"/> and
/// <see cref="RegisterVersion"/> are numbers, and the nullable confirmation compares through
/// <see cref="LineageConfirmation"/>'s own content equality. A baseline decoded from bytes therefore equals
/// the baseline that was encoded — the property the containing record's comparison rests on
/// (<see cref="VeritasMetadataRecord"/>).
/// </para>
/// </remarks>
public sealed record LineageBaseline(
    ReplicaAxis ClaimantAxis,
    NodeIdentifier CausalityDigest,
    LineageConfirmation? Confirmation,
    RegisterVersion RecordedAt)
{
    /// <summary>
    /// Whether the baseline is CONFIRMED: the minting replica committed it durably and amended the record with
    /// the dataset StateId and the dictionary epoch. An unconfirmed baseline is an intent whose commit may not
    /// have survived, so a clone gates on this and a replica's next open re-issues its confirm idempotently.
    /// </summary>
    public bool IsConfirmed => Confirmation is not null;

    /// <summary>The confirmed dataset StateId, or <see langword="null"/> while the intent is unconfirmed.</summary>
    public NodeIdentifier? StateId => Confirmation?.StateId;

    /// <summary>The confirmed term-dictionary epoch, or <see langword="null"/> while the intent is unconfirmed.</summary>
    public long? DictionaryEpoch => Confirmation?.DictionaryEpoch;

    /// <summary>
    /// The INTENT form of a baseline: the claimant and the causality digest, with the confirmation fields
    /// absent. This is what the minting replica records BEFORE its local durable commit, when no dataset
    /// StateId and no dictionary epoch exist yet.
    /// </summary>
    /// <param name="claimantAxis">The replica identity axis the baseline dots are minted on.</param>
    /// <param name="causalityDigest">The content fingerprint of the minted baseline causality.</param>
    /// <param name="recordedAt">The register version the intent write is decided at.</param>
    /// <returns>The unconfirmed baseline.</returns>
    public static LineageBaseline Intent(ReplicaAxis claimantAxis, NodeIdentifier causalityDigest, RegisterVersion recordedAt)
    {
        return new LineageBaseline(
            ClaimantAxis: claimantAxis,
            CausalityDigest: causalityDigest,
            Confirmation: null,
            RecordedAt: recordedAt);
    }

    /// <summary>
    /// The CONFIRMED form of this baseline: the same claimant and the same digest, with the dataset StateId and
    /// the dictionary epoch filled together and the version advanced to the confirming write. Filling both in
    /// one transition is what keeps the tri-state readable — a baseline is unconfirmed or confirmed, never half
    /// of either.
    /// </summary>
    /// <param name="stateId">The dataset StateId the committed baseline produced.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
    /// <param name="recordedAt">The register version the confirm write is decided at.</param>
    /// <returns>The confirmed baseline.</returns>
    public LineageBaseline Confirm(NodeIdentifier stateId, long dictionaryEpoch, RegisterVersion recordedAt)
    {
        return this with { Confirmation = new LineageConfirmation(stateId, dictionaryEpoch), RecordedAt = recordedAt };
    }
}
