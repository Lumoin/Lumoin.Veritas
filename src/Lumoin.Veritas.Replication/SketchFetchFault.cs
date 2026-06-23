using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One injected fault: the <see cref="SketchFetchFaultKind"/> to apply and an optional <see cref="Delay"/> applied
/// before it, so a plan can model a slow peer (delay then pass), a timeout (delay then drop), or a hang that ends
/// in an error (delay then fail). The delay runs against the injector's injected clock, so a plan is deterministic
/// and a test never waits on the wall clock.
/// </summary>
/// <param name="Kind">The fault to apply.</param>
/// <param name="Delay">The latency to inject before applying <paramref name="Kind"/>; <see cref="TimeSpan.Zero"/> for none.</param>
public readonly record struct SketchFetchFault(SketchFetchFaultKind Kind, TimeSpan Delay)
{
    /// <summary>No fault and no delay — the fetch runs normally.</summary>
    public static SketchFetchFault Pass { get; } = new(SketchFetchFaultKind.Pass, TimeSpan.Zero);

    /// <summary>Drop the fetch (empty image) with no delay.</summary>
    public static SketchFetchFault Drop { get; } = new(SketchFetchFaultKind.Drop, TimeSpan.Zero);

    /// <summary>Corrupt the fetched image with no delay.</summary>
    public static SketchFetchFault Corrupt { get; } = new(SketchFetchFaultKind.Corrupt, TimeSpan.Zero);

    /// <summary>Fail the fetch (throw) with no delay.</summary>
    public static SketchFetchFault Fail { get; } = new(SketchFetchFaultKind.Fail, TimeSpan.Zero);

    /// <summary>A fault applied after a latency.</summary>
    /// <param name="delay">The latency injected before the fault; not negative.</param>
    /// <param name="kind">The fault to apply after the latency.</param>
    /// <returns>The delayed fault.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
    public static SketchFetchFault After(TimeSpan delay, SketchFetchFaultKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        return new SketchFetchFault(kind, delay);
    }
}
