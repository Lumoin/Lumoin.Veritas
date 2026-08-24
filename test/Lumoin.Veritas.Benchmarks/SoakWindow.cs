using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// One closed soak measurement: wall time, the per-thread and process-wide allocation deltas, and
/// the measurement-validity verdict. The per-thread number is the primary column (comparable with
/// the historical soak ledgers); <see cref="ThreadHopped"/> marks the windows where it is SUSPECT —
/// the window closed on a different thread than it opened on (a genuinely-asynchronous completion
/// hopped the awaiter), so the per-thread counter compared two unrelated threads and only the
/// process-wide delta is trustworthy.
/// </summary>
/// <param name="Milliseconds">The wall time.</param>
/// <param name="ThreadAllocatedBytes">The per-thread allocation delta; suspect when <paramref name="ThreadHopped"/> is set.</param>
/// <param name="TotalAllocatedBytes">The precise process-wide allocation delta — valid regardless of hops, but inclusive of every concurrent thread's allocations.</param>
/// <param name="ThreadHopped">Whether the window closed on a different managed thread than it opened on.</param>
public readonly record struct SoakSample(double Milliseconds, long ThreadAllocatedBytes, long TotalAllocatedBytes, bool ThreadHopped)
{
    /// <summary>The allocation cell for line-oriented soak output: the per-thread KB, with the hop marker and the process-wide KB appended on the rare suspect window so a reader never mistakes a cross-thread delta for a route difference.</summary>
    public string AllocCell => ThreadHopped
        ? $"{ThreadAllocatedBytes / 1024:N0} KB HOP(total {TotalAllocatedBytes / 1024:N0} KB)"
        : $"{ThreadAllocatedBytes / 1024:N0} KB";

    /// <summary>The byte-precision allocation cell for rows whose signal is sub-kilobyte (a bare pass overhead); same hop semantics as <see cref="AllocCell"/>.</summary>
    public string AllocCellBytes => ThreadHopped
        ? $"{ThreadAllocatedBytes:N0} B HOP(total {TotalAllocatedBytes:N0} B)"
        : $"{ThreadAllocatedBytes:N0} B";
}

/// <summary>
/// The shared soak measurement window: opens on the measuring thread capturing the wall clock, the
/// per-thread allocation counter, the precise process-wide allocation counter, and the thread
/// identity; closing returns the <see cref="SoakSample"/> with the deltas and the hop verdict. The
/// scope shape (open, run the measured operation inline, close) keeps call sites free of capture —
/// no callback, no closure — and makes every soak's measurement self-validating: a window that
/// crossed threads says so instead of reporting a silently-wrong per-thread delta.
/// </summary>
public readonly record struct SoakWindow
{
    /// <summary>The running wall clock.</summary>
    private Stopwatch Clock { get; }

    /// <summary>The per-thread allocation counter at open.</summary>
    private long ThreadAllocatedAtOpen { get; }

    /// <summary>The precise process-wide allocation counter at open.</summary>
    private long TotalAllocatedAtOpen { get; }

    /// <summary>The managed thread the window opened on.</summary>
    private int OpenedThreadId { get; }

    /// <summary>Captures the counters and starts the clock. Called through <see cref="Open"/>.</summary>
    /// <param name="clock">The started wall clock.</param>
    /// <param name="threadAllocatedAtOpen">The per-thread allocation counter at open.</param>
    /// <param name="totalAllocatedAtOpen">The process-wide allocation counter at open.</param>
    /// <param name="openedThreadId">The opening managed thread id.</param>
    private SoakWindow(Stopwatch clock, long threadAllocatedAtOpen, long totalAllocatedAtOpen, int openedThreadId)
    {
        Clock = clock;
        ThreadAllocatedAtOpen = threadAllocatedAtOpen;
        TotalAllocatedAtOpen = totalAllocatedAtOpen;
        OpenedThreadId = openedThreadId;
    }

    /// <summary>Opens a measurement window on the current thread; run the measured operation inline, then <see cref="Close"/>.</summary>
    /// <remarks>The Stopwatch is allocated BEFORE the counters are read, so the harness itself contributes zero bytes to the measured window.</remarks>
    /// <returns>The open window.</returns>
    public static SoakWindow Open()
    {
        Stopwatch clock = new();
        long total = GC.GetTotalAllocatedBytes(precise: true);
        long thread = GC.GetAllocatedBytesForCurrentThread();
        int openedThreadId = Environment.CurrentManagedThreadId;
        clock.Start();

        return new SoakWindow(clock, thread, total, openedThreadId);
    }

    /// <summary>Stops the clock, reads the counters, and returns the sample with the hop verdict.</summary>
    /// <returns>The closed sample.</returns>
    public SoakSample Close()
    {
        Clock.Stop();
        long thread = GC.GetAllocatedBytesForCurrentThread() - ThreadAllocatedAtOpen;
        long total = GC.GetTotalAllocatedBytes(precise: true) - TotalAllocatedAtOpen;

        return new SoakSample(Clock.Elapsed.TotalMilliseconds, thread, total, Environment.CurrentManagedThreadId != OpenedThreadId);
    }
}
