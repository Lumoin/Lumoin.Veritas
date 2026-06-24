namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based result of attempting to load a node's durable structural sketch on restart
/// (<see cref="DurableSketchStore.TryLoad"/>). Expected conditions — an empty store, a foreign-epoch
/// sketch, at-rest rot — are outcomes, not exceptions, so a node decides whether to serve the loaded
/// sketch or re-derive one from its feed without a try/catch.
/// </summary>
public enum DurableSketchLoadOutcome
{
    /// <summary>A committed sketch generation was recovered and its image verified; the image is loaded.</summary>
    Loaded,

    /// <summary>The store holds no recoverable committed sketch generation — a fresh node or none persisted yet, or (not distinguished from these) a control plane so damaged that no CURRENT pointer and no manifest verifies.</summary>
    NotFound,

    /// <summary>A generation was recovered but it was written against a different term-dictionary epoch, so its structural identifiers denote different terms here; it is refused rather than served.</summary>
    EpochMismatch,

    /// <summary>A generation was recovered but its manifest names no sketch artifact.</summary>
    NoSketchEntry,

    /// <summary>The sketch artifact is missing from the store or fails its at-rest verification (a block checksum or the geometry); it is refused rather than served.</summary>
    Rejected,
}
