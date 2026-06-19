using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Attempts to restore a detected corruption from one repair rung, returning whether the rung restored it.
/// The ladder invokes this for each restoring rung in order; the implementation (a scrub round) knows how to
/// re-derive, peel parity, or reconcile for the artifact at hand and re-applies any recovered items through
/// the ordinary ingest path. A rung that does not apply — its source is absent, or the corruption is not of a
/// kind it restores — returns <see langword="false"/> so the ladder descends to the next rung.
/// </summary>
/// <param name="rung">The rung being attempted.</param>
/// <returns><see langword="true"/> when the rung restored the corruption; <see langword="false"/> to descend.</returns>
public delegate bool RepairAttemptDelegate(RepairRung rung);

/// <summary>
/// The repair-source ladder: the fixed, exhaustive escalation a storage repair follows when a block is
/// detected corrupt — re-derive locally, else local parity, else peer reconciliation, else a named loss. It
/// owns the descent ORDER and the first-success-wins plus <see cref="RepairRung.NamedLoss"/>-terminal
/// semantics, so a scrub round never re-implements the escalation; only the per-rung restore action is
/// injected. There is no privileged repair writer — a restoring rung re-enters recovered items through the
/// ordinary ingest path, and the terminal rung names the loss rather than dropping it.
/// </summary>
public static class RepairSourceLadder
{
    /// <summary>The restoring rungs in descent order, attempted until one succeeds. <see cref="RepairRung.NamedLoss"/> is deliberately NOT here — it is the terminal OUTCOME when every restoring rung declines, not an attempt.</summary>
    public static IReadOnlyList<RepairRung> RestoringRungs { get; } = [RepairRung.RederiveLocally, RepairRung.LocalParity, RepairRung.PeerReconciliation];

    /// <summary>Descends the restoring rungs in order, attempting each through <paramref name="attempt"/>, and returns the first rung that restored the corruption — or <see cref="RepairRung.NamedLoss"/> when every restoring rung declined.</summary>
    /// <param name="attempt">The per-rung restore action; returns whether the rung restored the corruption.</param>
    /// <returns>The rung that restored the corruption, or <see cref="RepairRung.NamedLoss"/> when none did.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attempt"/> is <see langword="null"/>.</exception>
    public static RepairRung Descend(RepairAttemptDelegate attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        for(int i = 0; i < RestoringRungs.Count; i++)
        {
            if(attempt(RestoringRungs[i]))
            {
                return RestoringRungs[i];
            }
        }

        return RepairRung.NamedLoss;
    }
}
