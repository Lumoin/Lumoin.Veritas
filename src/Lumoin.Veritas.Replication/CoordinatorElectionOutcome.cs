namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of taking, refreshing, or releasing the coordinator lease. Succession is settled by
/// the write discipline itself: a vacant lease is taken by any member, a lease held by the caller is refreshed,
/// a lease held by another CURRENT member is not usurped, and a lease held by a replica outside the current
/// membership is taken over — which ties usurpation to retiring the dead holder through the membership
/// obligation the plane already coordinates.
/// </summary>
/// <remarks>
/// The default value is <see cref="Undecided"/>, so a zero-initialized field reads as definite ignorance and
/// never as a held lease. No value here is time-based: the consensus procedure is timeout-free, and deciding
/// that a holder is dead is an application-level health signal the plane deliberately does not embed.
/// </remarks>
public enum CoordinatorElectionOutcome
{
    /// <summary>Returned when consensus reached no decision within the caller's attempt budget — a missed quorum or a spent budget, including an unreachable plane. It is not evidence the lease was refused, and the caller must not act as the coordinator on it.</summary>
    Undecided = 0,

    /// <summary>Returned when the lease was vacant, or held by a replica outside the current membership, and this write took it for the caller at a new term.</summary>
    Elected = 1,

    /// <summary>Returned when the caller already held the lease and this write renewed it at a new term.</summary>
    Refreshed = 2,

    /// <summary>Returned when the lease is held by another replica that is still a CURRENT member: the living are not usurped. Retiring the holder through the membership obligation is what unlocks the lease.</summary>
    HeldByOther = 3,

    /// <summary>Returned when the caller held the lease and this write vacated it, so any member may elect next.</summary>
    Released = 4,

    /// <summary>Returned when the calling replica is not a member of the chain's committed membership, so no attempt was made. A settled refusal about membership rather than an unlucky round, and distinct from <see cref="Undecided"/>'s definite ignorance.</summary>
    OutsideConfiguration = 5
}
