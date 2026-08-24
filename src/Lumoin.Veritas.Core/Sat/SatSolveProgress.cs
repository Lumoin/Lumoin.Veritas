namespace Lumoin.Veritas.Core.Sat;

/// <summary>
/// A snapshot of one solve call's search counters, handed to a
/// <see cref="SatSolveProgressDelegate"/> once per propagation round. The counters are
/// per-call: they reset at the start of every <see cref="SatSolverSession.Solve"/> even on a
/// session whose learned state carries over.
/// </summary>
/// <param name="Decisions">The branching decisions taken so far in this call.</param>
/// <param name="Conflicts">The conflicts detected so far in this call.</param>
/// <param name="Propagations">The literal propagations performed so far in this call.</param>
/// <param name="LearnedClauses">The clauses learned so far in this call.</param>
/// <param name="Restarts">The restarts taken so far in this call.</param>
/// <param name="CurrentLevel">The current decision level.</param>
public readonly record struct SatSolveProgress(
    int Decisions,
    int Conflicts,
    long Propagations,
    int LearnedClauses,
    int Restarts,
    int CurrentLevel);

/// <summary>
/// Observes a solve call's search progress: invoked synchronously on the solving thread at the
/// top of every propagation round, immediately BEFORE the round's cancellation check — so an
/// observer that cancels the solve's token on a chosen counter value lands the cancellation
/// deterministically in that same round, mid-search. The observer must not mutate the session;
/// it shares the session's single-thread-per-solve contract.
/// </summary>
/// <param name="progress">The current search counters.</param>
public delegate void SatSolveProgressDelegate(in SatSolveProgress progress);
