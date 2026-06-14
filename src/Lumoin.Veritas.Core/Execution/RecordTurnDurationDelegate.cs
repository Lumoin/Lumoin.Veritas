namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Records the wall-clock duration of one completed compute-lane turn,
/// tagged by its work class. A lane calls it at the turn-execution site once
/// per turn; the in-library implementation
/// (<see cref="ComputeLaneInstruments.CreateTurnDurationRecorder"/>) records
/// to the <c>veritas.compute_lane.turn_duration</c> histogram, but the lane
/// holds only this named seam, never a meter — so a lane runs meter-free
/// until a host supplies a recorder.
/// </summary>
/// <param name="workClass">The completed turn's work class.</param>
/// <param name="elapsedMilliseconds">The turn's wall-clock duration in milliseconds, fractional so a sub-millisecond turn is not lost to zero.</param>
public delegate void RecordTurnDurationDelegate(ComputeWorkClass workClass, double elapsedMilliseconds);
