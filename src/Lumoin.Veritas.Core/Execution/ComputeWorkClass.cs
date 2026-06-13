namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The priority class of a unit of work admitted to the compute lane.
/// Declared in priority order, highest first: the lane serves a lower
/// ordinal before a higher one, FIFO within a class. The two ends are
/// load-bearing — <see cref="ControlPlaneTick"/> is reserved above the
/// shed-able classes so it cannot be starved, and <see cref="Scrub"/>
/// is lowest so background integrity work yields to real work under
/// load (its starvation then shows as an explicit queue-depth metric
/// rather than stealing compute).
/// </summary>
/// <remarks>
/// <para>
/// The relative order of the middle compute classes
/// (<see cref="ViewBuild"/>, <see cref="BulkSort"/>,
/// <see cref="Reasoning"/>, <see cref="SketchUpdate"/>) is a tuning
/// choice; only the reserved top and the starve-by-design bottom are
/// invariants of the model.
/// </para>
/// </remarks>
public enum ComputeWorkClass
{
    /// <summary>The control-plane tick — the periodic CPU-quota re-read and lane resize. Reserved: admitted even when the shed-able classes are at capacity, and served first, so a quota change is observed without being starved by compute load.</summary>
    ControlPlaneTick,

    /// <summary>On-demand columnar view materialisation moved off the serve pool.</summary>
    ViewBuild,

    /// <summary>Bulk sorting work — the radix-sorted bulk-load path and kindred whole-dataset passes.</summary>
    BulkSort,

    /// <summary>A reasoning decision — one locality module per turn (never threads inside a decision).</summary>
    Reasoning,

    /// <summary>Integrity sketch maintenance — background, above only the scrub walk.</summary>
    SketchUpdate,

    /// <summary>The background integrity walk. Lowest priority by design: it yields to all real work, and its starvation under load is a visible metric, not stolen compute.</summary>
    Scrub,
}
