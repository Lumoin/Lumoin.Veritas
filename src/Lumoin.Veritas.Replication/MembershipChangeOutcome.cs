namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of admitting or retiring a member of the metadata chain. A membership change is an
/// ordinary write whose record carries the changed membership and the same value forward, and it is expressed
/// as a DELTA — add this replica, remove that one — so a change re-applied against a winning record composes
/// with a concurrent operator's change instead of silently undoing it.
/// </summary>
/// <remarks>
/// The default value is <see cref="Undecided"/>, so a zero-initialized field reads as definite ignorance and
/// never as an installed membership.
/// </remarks>
public enum MembershipChangeOutcome
{
    /// <summary>Returned when consensus reached no decision within the caller's attempt budget — a missed quorum or a spent budget, including an unreachable plane. The membership may still be changed later by a proposal already in flight, so the caller re-reads rather than assuming its change was refused.</summary>
    Undecided = 0,

    /// <summary>Returned when the reconfiguring write landed and the committed membership now reflects the requested delta.</summary>
    Changed = 1,

    /// <summary>Returned when the delta was already installed — admitting a member the record already lists, or retiring one it does not — so nothing was proposed and the committed membership stands. The idempotent arm that lets an operator repeat a change safely.</summary>
    Unchanged = 2,

    /// <summary>Returned when the chain carries no committed record yet, so there is no membership to change and the deployment's initial record must be bootstrapped first. The plane checks the committed record before writing so this is a value rather than a leaked host exception.</summary>
    RequiresBootstrap = 3,

    /// <summary>Returned when the calling replica is not a member of the chain's committed membership, so no attempt was made. A settled refusal about membership rather than an unlucky round, and distinct from <see cref="Undecided"/>'s definite ignorance.</summary>
    OutsideConfiguration = 4
}
