using System;
using System.Threading;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The host policy for a long-lived <see cref="IncrementalSketchMaintainer"/>: the re-seed cadence that bounds the
/// maintained encoder's append-only arena and pending-cursor heap, the caller-injected <see cref="System.TimeProvider"/>
/// the time-based cadence reads, the encoder's initial cell-buffer capacity hint, and an optional diagnostic
/// enforcement override. The maintained encoder's arena and cursor heap grow with the TOTAL number of committed
/// operations since the last re-seed — not with the live-set size — because every add and every remove appends item
/// bytes plus a pending cursor that are freed only on dispose. A periodic re-seed (dispose plus rebuild over the
/// current committed set) reclaims that dead weight; these knobs choose when.
/// </summary>
/// <param name="TimeProvider">The time source the time-based re-seed cadence reads; never wall-clock <c>DateTime.Now</c> directly, so the cadence is testable under a fake clock.</param>
/// <param name="ReseedOperationBudget">The number of committed add/remove operations since the last re-seed that raises the re-seed hint for the host cadence. The per-operation footprint is amortized ~112 bytes (the 16-byte structural item plus ~56 bytes of pending-cursor and arena bookkeeping), so one million operations amortizes to roughly 112 MB of dead weight; because the arena and cursor heap grow by doubling, the transient peak just before a reclaim is closer to ~181 bytes per operation, roughly 181 MB, which is the ceiling the budget must be sized against.</param>
/// <param name="ReseedInterval">The wall-clock interval since the last re-seed that also raises the re-seed hint, measured through <paramref name="TimeProvider"/>. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables the time-based trigger, leaving <paramref name="ReseedOperationBudget"/> the sole cadence.</param>
/// <param name="CellCapacityHint">The initial cell-buffer and arena-block capacity a rebuilt encoder pre-sizes to. Zero lets both grow by doubling from their minimum; the arena grows by adding blocks and never relocates a stored item, so doubling during the re-seed's linear re-add is cheap, and the cell buffer only ever grows to the served symbol prefix (tens to low hundreds of symbols), so a large hint over-allocates cells for no benefit. Zero is the sound default; raise it only to a measured serve-prefix size.</param>
/// <param name="EnforcementOverride">A diagnostic override for the maintained encoder's injectivity enforcement. <see langword="null"/> (the default) selects production posture: <see cref="ReconciliationInjectivityEnforcement.None"/> in release builds, <see cref="ReconciliationInjectivityEnforcement.DebugAssert"/> in debug builds so a non-net delta is caught during development. Set it (typically to <see cref="ReconciliationInjectivityEnforcement.Strict"/>) in a diagnostic or test host to police the injectivity obligation — note that a checking enforcement makes a fold fault SURFACE on the commit path instead of being isolated as a dirty-rebuild, which is the point in tests but must not be enabled on a live commit path.</param>
public sealed record IncrementalSketchMaintainerOptions(
    TimeProvider TimeProvider,
    long ReseedOperationBudget,
    TimeSpan ReseedInterval,
    int CellCapacityHint,
    ReconciliationInjectivityEnforcement? EnforcementOverride = null)
{
    /// <summary>The default policy: the system clock, a one-million-operation re-seed budget, no time-based trigger, a zero cell-capacity hint, and production enforcement posture.</summary>
    public static IncrementalSketchMaintainerOptions Default { get; } = new(
        System.TimeProvider.System,
        1_000_000,
        Timeout.InfiniteTimeSpan,
        0);
}
