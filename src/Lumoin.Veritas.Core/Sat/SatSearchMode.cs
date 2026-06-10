namespace Lumoin.Veritas.Core.Sat;

/// <summary>
/// How <see cref="SatSolver"/> reacts to a conflict. Both modes decide the
/// same satisfiability and, when satisfiable, return a model honouring the
/// formula and every assumption; they differ only in how the search prunes,
/// so the choice is a measured per-workload knob.
/// </summary>
public enum SatSearchMode
{
    /// <summary>
    /// Chronological backtracking: a conflict unwinds to the deepest decision
    /// whose second branch is untried, with no clause recorded. The default.
    /// </summary>
    PropagationOnly,

    /// <summary>
    /// Conflict-driven clause learning: a conflict is analysed to its
    /// first-UIP clause, that clause is added, and the search backjumps to the
    /// clause's assertion level. The learned clause then forces the asserting
    /// literal, pruning the subtree that produced the conflict.
    /// </summary>
    ConflictLearning,
}
