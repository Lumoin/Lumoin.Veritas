namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The per-evaluation CUMULATIVE cursor budget cell: one instance is created at each public evaluation
/// entry with <see cref="StreamingPipeline.MaxCursorDepth"/> and threaded BY REFERENCE through every
/// re-entry channel (nested <c>EXISTS</c> pipelines, materialise-boundary first pulls, driver
/// interceptions), so all cursor-spawning sites of one evaluation draw from the same pool and the
/// evaluation's live cursor pull frames stay bounded by the one constant. Compilation charges one unit
/// per constructed cursor; a declined compile refunds its charges (nothing lives); compiled pipelines
/// stay charged for the evaluation's lifetime. Off-mode evaluations carry the cell inertly — no pipeline
/// compiles, nothing draws from it.
/// </summary>
internal sealed class CursorBudget
{
    /// <summary>Constructs the cell with its initial budget.</summary>
    /// <param name="remaining">The initial budget; public entries pass <see cref="StreamingPipeline.MaxCursorDepth"/>.</param>
    public CursorBudget(int remaining)
    {
        Remaining = remaining;
    }

    /// <summary>The budget units remaining for this evaluation; decremented per constructed cursor.</summary>
    public int Remaining { get; set; }
}
