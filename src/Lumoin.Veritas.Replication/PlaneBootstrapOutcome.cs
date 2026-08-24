namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of bootstrapping a deployment's metadata chain: the first write, which commits the
/// deterministic initial record under the genesis membership. Every founder may call it — the proposals are
/// identical, so the race between founders resolves without anyone's state being lost.
/// </summary>
/// <remarks>
/// The default value is <see cref="Undecided"/>, so a zero-initialized field reads as definite ignorance and
/// never as a bootstrapped chain. Bootstrapping is the precondition of every other obligation: the membership
/// obligations report that it is required rather than leaking the host's own refusal to reconfigure a chain
/// that has decided nothing.
/// </remarks>
public enum PlaneBootstrapOutcome
{
    /// <summary>Returned when consensus reached no decision within the caller's attempt budget — a missed quorum or a spent budget, including an unreachable plane. Another founder's identical proposal may still be carried to decision, so the caller retries rather than concluding the chain is unbootstrapped.</summary>
    Undecided = 0,

    /// <summary>Returned when this write committed the deterministic initial record: no claims, no baseline, the default policy, and a vacant lease.</summary>
    Bootstrapped = 1,

    /// <summary>Returned when the chain already carries a committed record — another founder's identical proposal won the bootstrap race, or the deployment was bootstrapped earlier — so nothing changed.</summary>
    AlreadyBootstrapped = 2,

    /// <summary>Returned when the calling replica is not a member of the genesis membership, so no attempt was made. A settled refusal about membership rather than an unlucky round, and distinct from <see cref="Undecided"/>'s definite ignorance.</summary>
    OutsideConfiguration = 3
}
