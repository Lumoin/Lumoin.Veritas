namespace Lumoin.Veritas.Replication;

/// <summary>
/// The recovered symmetric difference of two verified sketches together with whether the rateless decoder fully
/// converged. The count-only decode seam (<see cref="RatelessSketchCodec.Decode"/>) hides convergence; a
/// peer-reconciliation session needs it to tell a COMPLETE peel (the whole difference recovered, safe to apply)
/// from a PARTIAL one (the budget could not peel it all, so applying the partial set would not converge in one
/// round). <see cref="RatelessSketchCodec.RecoverDifference"/> produces it.
/// </summary>
/// <param name="RecoveredCount">The number of recovered difference items; when it exceeds the sink length nothing was written.</param>
/// <param name="IsComplete">Whether the decoder peeled the whole symmetric difference within the cap; <see langword="false"/> is a partial peel.</param>
/// <param name="AbsorbedSymbols">The number of combined symbols absorbed before convergence or the cap.</param>
public readonly record struct SketchDifference(int RecoveredCount, bool IsComplete, int AbsorbedSymbols);
