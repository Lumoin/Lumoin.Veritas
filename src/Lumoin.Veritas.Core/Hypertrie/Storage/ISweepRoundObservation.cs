using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// The per-round observation seam a sweep trigger exposes to tests: a round is
/// one processed timer tick — the loop woke, evaluated its condition, and
/// swept or declined. Arming the seam and awaiting a round makes the loop's
/// progress a deterministic fact with no polling and no wall-clock dependence;
/// production never arms it, leaving each round a single volatile read.
/// </summary>
internal interface ISweepRoundObservation
{
    /// <summary>Arms the seam and returns the task completing when the trigger's loop finishes its next round, swept or not.</summary>
    /// <returns>The task completing on the next finished round.</returns>
    Task ObserveRound();
}
