using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The maintenance pipeline phase a measured region of an
/// <see cref="OwlRlClosure.ClosureContext.ApplyCore"/> is attributed to. The
/// phases partition the incremental add/retract pipeline so a cost breakdown
/// can ask which phase's per-marked-fact rate grows super-linearly across
/// retract bursts and which stay flat.
/// </summary>
/// <remarks>
/// Exactly three parent/child pairs overlap and every other pair of phases is
/// disjoint: <see cref="OverdeleteCharacteristicData"/> is measured within
/// <see cref="OverdeleteProperties"/>, <see cref="OverdeleteMaxPairs"/> within
/// <see cref="OverdeleteClasses"/>, and <see cref="RederiveEqRep"/> within
/// <see cref="Rederive"/>. A parent's accumulated time includes its child's; no
/// subtraction happens in the accumulator, so the attributed total sums only
/// the top-level phases.
/// </remarks>
internal enum OwlRlMaintenancePhase
{
    /// <summary>A per-round <see cref="OwlRlClosure.ClosureContext"/> frontier-grouping rebuild.</summary>
    OverdeleteGrouping = 0,

    /// <summary>The pre-loop owner-conclusion marking and the per-round frontier-owner collection.</summary>
    OwnerMarking = 1,

    /// <summary>A per-round eq-* overdelete pass.</summary>
    OverdeleteEquality = 2,

    /// <summary>A per-round prp-* overdelete pass; its accumulated time includes <see cref="OverdeleteCharacteristicData"/>.</summary>
    OverdeleteProperties = 3,

    /// <summary>The per-edge and per-typing characteristic overdelete body - the child region within <see cref="OverdeleteProperties"/>.</summary>
    OverdeleteCharacteristicData = 4,

    /// <summary>A per-round cls-* overdelete pass; its accumulated time includes <see cref="OverdeleteMaxPairs"/>.</summary>
    OverdeleteClasses = 5,

    /// <summary>The one-bounded max-cardinality overdelete body, per call - the child region within <see cref="OverdeleteClasses"/>.</summary>
    OverdeleteMaxPairs = 6,

    /// <summary>A per-round cax-* overdelete pass.</summary>
    OverdeleteClassAxioms = 7,

    /// <summary>A per-round scm-* overdelete pass.</summary>
    OverdeleteSchema = 8,

    /// <summary>The single physical removal loop that unindexes every marked fact.</summary>
    PhysicalRemoval = 9,

    /// <summary>The single base-addition admit/demote loop.</summary>
    BaseAdmission = 10,

    /// <summary>A single head-bound rederivability check of one candidate; its accumulated time includes <see cref="RederiveEqRep"/>.</summary>
    Rederive = 11,

    /// <summary>The three eq-rep entries at the head of the rederivability check - the child region within <see cref="Rederive"/>.</summary>
    RederiveEqRep = 12,

    /// <summary>The choice/list owner re-fire over the post-edit state.</summary>
    OwnerReFire = 13,

    /// <summary>A single semi-naive insert round - one delta firing and its pending merge.</summary>
    InsertRounds = 14,
}

/// <summary>
/// An opt-in, thread-local cost-attribution sink for the maintained OWL 2 RL
/// closure's incremental pipeline. It is disabled by default: every measured
/// site guards on <see cref="Enabled"/>, so a production Apply pays one
/// predicted-not-taken branch per site and its verdict and statistics are
/// byte-identical to an uninstrumented run. A diagnostic harness calls
/// <see cref="Enable"/> on its thread, applies one edit, then reads
/// <see cref="Snapshot"/> to attribute the elapsed time to the phases of
/// <see cref="OwlRlMaintenancePhase"/>.
/// </summary>
/// <remarks>
/// State is thread-local because the maintained closure runs synchronously on
/// the thread that invokes <see cref="OwlRlClosure.ClosureContext.ApplyCore"/>:
/// enabling measurement on the harness thread isolates it from any maintenance
/// concurrently underway on other threads and needs no synchronisation. Time is
/// accumulated as raw <see cref="Stopwatch"/> ticks and converted to
/// milliseconds only on read, so the per-region cost is two timestamp reads and
/// an array add.
/// </remarks>
internal static class OwlRlMaintenanceInstrumentation
{
    /// <summary>The number of phases - the width of the accumulator arrays.</summary>
    private const int PhaseCount = 15;

    //Thread-local accumulation requires fields: the [ThreadStatic] attribute
    //applies to static fields, not to auto-property backing fields by name, and
    //each measured region runs on the thread that enabled measurement.
    [ThreadStatic]
    private static bool enabled;

    [ThreadStatic]
    private static long[]? phaseTicks;

    [ThreadStatic]
    private static long[]? phaseCounts;

    /// <summary>Whether the calling thread is accumulating phase measurements.</summary>
    public static bool Enabled
    {
        get
        {
            return enabled;
        }
    }

    /// <summary>Enables phase measurement on the calling thread and clears its accumulators. The flag is set last, so a failed accumulator allocation never leaves the thread enabled over stale state.</summary>
    public static void Enable()
    {
        phaseTicks = new long[PhaseCount];
        phaseCounts = new long[PhaseCount];
        enabled = true;
    }

    /// <summary>Disables phase measurement on the calling thread; the accumulators are retained so a final <see cref="Snapshot"/> still reads them.</summary>
    public static void Disable()
    {
        enabled = false;
    }

    /// <summary>Clears the calling thread's accumulators without changing whether measurement is enabled.</summary>
    public static void Reset()
    {
        phaseTicks = new long[PhaseCount];
        phaseCounts = new long[PhaseCount];
    }

    /// <summary>Marks the start of a measured region.</summary>
    /// <returns>The start timestamp to pass to <see cref="End"/>, or zero when measurement is disabled.</returns>
    public static long Begin()
    {
        return enabled ? Stopwatch.GetTimestamp() : 0L;
    }

    /// <summary>Attributes the time elapsed since <paramref name="startTimestamp"/> to <paramref name="phase"/> and counts the region.</summary>
    /// <param name="phase">The phase the measured region belongs to.</param>
    /// <param name="startTimestamp">The timestamp <see cref="Begin"/> returned at the start of the region.</param>
    public static void End(OwlRlMaintenancePhase phase, long startTimestamp)
    {
        if(!enabled)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        long[] ticks = phaseTicks ??= new long[PhaseCount];
        long[] counts = phaseCounts ??= new long[PhaseCount];
        ticks[(int)phase] += elapsed;
        counts[(int)phase]++;
    }

    /// <summary>The calling thread's accumulated measurements as elapsed milliseconds and call count per phase.</summary>
    /// <returns>An immutable report over the current accumulator values.</returns>
    public static OwlRlMaintenanceInstrumentationReport Snapshot()
    {
        return new OwlRlMaintenanceInstrumentationReport(phaseTicks ?? new long[PhaseCount], phaseCounts ?? new long[PhaseCount]);
    }
}

/// <summary>
/// An immutable read of <see cref="OwlRlMaintenanceInstrumentation"/>'s per-phase
/// accumulators: elapsed milliseconds and call count for each
/// <see cref="OwlRlMaintenancePhase"/>. Ticks are converted to milliseconds
/// against <see cref="Stopwatch.Frequency"/> on read.
/// </summary>
internal sealed class OwlRlMaintenanceInstrumentationReport
{
    private readonly long[] ticks;
    private readonly long[] counts;

    /// <summary>Captures a copy of the accumulator arrays so the report is stable against later measurement.</summary>
    /// <param name="ticks">The per-phase tick accumulators.</param>
    /// <param name="counts">The per-phase call counters.</param>
    internal OwlRlMaintenanceInstrumentationReport(long[] ticks, long[] counts)
    {
        this.ticks = (long[])ticks.Clone();
        this.counts = (long[])counts.Clone();
    }

    /// <summary>The elapsed milliseconds attributed to <paramref name="phase"/>.</summary>
    /// <param name="phase">The phase whose accumulated time is wanted.</param>
    /// <returns>The elapsed milliseconds; a parent phase's figure includes its nested child.</returns>
    public double Milliseconds(OwlRlMaintenancePhase phase)
    {
        return ToMilliseconds(ticks[(int)phase]);
    }

    /// <summary>The number of measured regions attributed to <paramref name="phase"/>.</summary>
    /// <param name="phase">The phase whose region count is wanted.</param>
    /// <returns>The region count.</returns>
    public long Count(OwlRlMaintenancePhase phase)
    {
        return counts[(int)phase];
    }

    /// <summary>
    /// The sum of the milliseconds attributed to the top-level phases only. The
    /// three nested child phases - <see cref="OwlRlMaintenancePhase.OverdeleteCharacteristicData"/>
    /// within <see cref="OwlRlMaintenancePhase.OverdeleteProperties"/>,
    /// <see cref="OwlRlMaintenancePhase.OverdeleteMaxPairs"/> within
    /// <see cref="OwlRlMaintenancePhase.OverdeleteClasses"/>, and
    /// <see cref="OwlRlMaintenancePhase.RederiveEqRep"/> within
    /// <see cref="OwlRlMaintenancePhase.Rederive"/> - are excluded, because a
    /// parent's measured time already includes its child, so the remainder
    /// against the Apply wall-clock is well-defined.
    /// </summary>
    public double TotalAttributedMilliseconds
    {
        get
        {
            double total = 0.0;
            for(int phase = 0; phase < ticks.Length; phase++)
            {
                if(IsChildPhase((OwlRlMaintenancePhase)phase))
                {
                    continue;
                }

                total += ToMilliseconds(ticks[phase]);
            }

            return total;
        }
    }

    /// <summary>Whether <paramref name="phase"/> is one of the three nested child phases excluded from the attributed total.</summary>
    /// <param name="phase">The phase to classify.</param>
    /// <returns><c>true</c> when the phase is a nested child of another phase.</returns>
    private static bool IsChildPhase(OwlRlMaintenancePhase phase)
    {
        return phase switch
        {
            OwlRlMaintenancePhase.OverdeleteCharacteristicData => true,
            OwlRlMaintenancePhase.OverdeleteMaxPairs => true,
            OwlRlMaintenancePhase.RederiveEqRep => true,
            _ => false,
        };
    }

    /// <summary>Converts a <see cref="Stopwatch"/> tick count to milliseconds.</summary>
    /// <param name="tickCount">The accumulated ticks.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double ToMilliseconds(long tickCount)
    {
        return tickCount * 1000.0 / Stopwatch.Frequency;
    }
}
