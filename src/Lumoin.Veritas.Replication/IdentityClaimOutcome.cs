namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of claiming a replica identity axis on the metadata plane. Claiming is what makes
/// axis distinctness proactive: a replica claims its axis before it mints under it, so a second host is refused
/// at claim time instead of being detected once colliding dots have already crossed the wire.
/// </summary>
/// <remarks>
/// The default value is <see cref="Undecided"/>, so a zero-initialized field reads as definite ignorance and
/// never as a granted claim. The distinction the whole ladder turns on is ignorance versus a definite adverse
/// answer: <see cref="Undecided"/> fails OPEN — the engine open proceeds, the unresolved claim surfaces as a
/// pending status, and the host's coordination loop retries off the open path — while
/// <see cref="RefusedHeldByOther"/> refuses the open, because that is correctness rather than liveness.
/// </remarks>
public enum IdentityClaimOutcome
{
    /// <summary>Returned when consensus reached no decision within the caller's attempt budget — a missed quorum or a spent budget, including an unreachable plane. It is not evidence the claim was rejected: another proposer may still carry it to decision. The plane never becomes a liveness dependency of the data lane, so this fails open.</summary>
    Undecided = 0,

    /// <summary>Returned when the axis was absent from the committed record and this write appended it: the claimant may mint under the axis.</summary>
    Claimed = 1,

    /// <summary>Returned when the axis was already claimed by the CALLING replica — a re-issued claim after a crash, or a routine reopen — so nothing was appended and the claim stands. The idempotent arm of the claim rule.</summary>
    AlreadyClaimedBySelf = 2,

    /// <summary>Returned when the axis is claimed by ANOTHER replica: a definite adverse answer, so the open is refused by value and the second minter never starts.</summary>
    RefusedHeldByOther = 3,

    /// <summary>Returned when the calling replica is not a member of the chain's committed membership, so no attempt was made. A settled refusal about membership rather than an unlucky round, and distinct from <see cref="Undecided"/>'s definite ignorance.</summary>
    OutsideConfiguration = 4
}
