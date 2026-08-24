namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of a coordinated policy amendment
/// (<see cref="VeritasMetadataPlane.AmendPolicyAsync"/>). An adverse or already-satisfied answer is an expected
/// operational condition reported here as a value; the plane never throws for one.
/// </summary>
public enum PolicyAmendmentOutcome
{
    /// <summary>
    /// No decision was reached within the attempt budget: the consensus round missed its quorum or spent its
    /// step budget. Definite ignorance, not refusal — the amendment may still be carried to decision by a
    /// later proposer, and the caller retries with a fresh budget.
    /// </summary>
    Undecided = 0,

    /// <summary>The amendment committed: the agreed policy now carries the proposed facts.</summary>
    Amended,

    /// <summary>The committed policy already carried the proposed facts byte-for-byte; nothing changed.</summary>
    Unchanged,

    /// <summary>This replica stands outside the current membership, so it may not amend the policy. A definite refusal by report, distinct from <see cref="Undecided"/>'s definite ignorance.</summary>
    OutsideConfiguration
}
