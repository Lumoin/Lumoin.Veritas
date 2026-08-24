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

    /// <summary>
    /// Conflict-driven clause learning as <see cref="ConflictLearning"/>, but
    /// with two-watched-literal unit propagation and a conflict-driven branching
    /// order. A clause is inspected only when one of its two watched literals is
    /// falsified, and a backtrack needs no watch-list adjustment — this replaces
    /// the per-round full-clause scan of the other modes, the dominant cost of
    /// propagation. Branching is variable-move-to-front with phase saving rather
    /// than the lowest-index, true-first order of the other modes: each decision
    /// takes the most recently bumped unassigned variable and assigns it the
    /// polarity it last held, so the search concentrates on the variables that
    /// recent conflicts resolved on. It decides the same satisfiability and
    /// returns a model honouring the formula and every assumption; the learned
    /// clauses and search path differ from <see cref="ConflictLearning"/> because
    /// propagation reaches its fixpoint in a different order and the branching
    /// order differs.
    /// </summary>
    WatchedLearning,
}
