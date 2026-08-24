namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The distribution summary of one descent level's fan-out: the child-group
/// sizes a parent value expands into, read as the deltas of an exclusive-end
/// offset column.
/// </summary>
/// <param name="Min">The smallest child-group size.</param>
/// <param name="Max">The largest child-group size.</param>
/// <param name="Mean">The mean child-group size.</param>
public readonly record struct ColumnarFanOut(int Min, int Max, double Mean);
