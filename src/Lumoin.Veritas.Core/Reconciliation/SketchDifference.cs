namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The recovered symmetric difference of two verified sketches together with whether the rateless decoder fully
/// converged. The count-only <see cref="SketchReconciliationDelegates.DecodeSketchDifference"/> seam hides
/// convergence; a peer-reconciliation path needs it to tell a COMPLETE peel (the whole difference recovered, safe
/// to act on) from a PARTIAL one (the budget could not peel it all, so the recovered set is only a subset and
/// acting on it would not converge in one round). The host's rateless codec produces it through a
/// <see cref="SketchReconciliationDelegates.RecoverSketchDifference"/> binding.
/// </summary>
/// <param name="RecoveredCount">The number of recovered difference items; when it exceeds the sink length nothing was written.</param>
/// <param name="IsComplete">Whether the decoder peeled the whole symmetric difference within the cap; <see langword="false"/> is a partial peel.</param>
/// <param name="AbsorbedSymbols">The number of combined symbols absorbed before convergence or the cap.</param>
/// <param name="FalseDecodeProbabilityBound">The decoder's per-decode masquerade union bound — <c>PurityCheckCount * 2^(-8 * checksumWidth)</c>, clamped to 1 — that a peer-reconciliation session gates a completeness claim against; <c>0</c> for a fake or oracle that performed no purity checks.</param>
public readonly record struct SketchDifference(int RecoveredCount, bool IsComplete, int AbsorbedSymbols, double FalseDecodeProbabilityBound);
