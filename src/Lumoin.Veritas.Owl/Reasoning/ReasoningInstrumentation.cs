using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The reasoning phase a measured region is attributed to. The phases
/// partition the work the deciding engines spend so a cost breakdown can ask
/// where reasoning time goes — in particular whether the blocking check is a
/// meaningful fraction of a decision or a negligible one.
/// </summary>
public enum ReasoningPhase
{
    /// <summary>A propositional world solve — the SAT-backed engine's per-world satisfiability call.</summary>
    SatSolve = 0,

    /// <summary>A blocking check — the snapshot engine's per-iteration blocking-status computation, or the SAT-backed engine's per-successor subset-blocking ancestor scan.</summary>
    Blocking = 1,

    /// <summary>A tableau rule application — the snapshot engine's per-iteration rule-expansion step.</summary>
    TableauRule = 2,

    /// <summary>A concrete-domain consistency decision over a completion's data obligations.</summary>
    DataConsistency = 3,

    /// <summary>The EL fast-path completion-rule saturation to fixpoint.</summary>
    ElSaturation = 4,

    /// <summary>The consequence-based context-saturation run to fixpoint, one logical bracket per admitted context decision, spanning the saturation and its post-completion ground ghost pass.</summary>
    ContextSaturation = 5,
}

/// <summary>
/// An opt-in, thread-local cost-attribution sink for the deciding engines. It
/// is disabled by default: every measured site guards on <see cref="Enabled"/>,
/// so a production decision pays one predicted-not-taken branch per site and
/// its verdict is byte-identical to an uninstrumented run. A diagnostic harness
/// calls <see cref="Enable"/> on its thread, decides one or more modules, then
/// reads <see cref="Snapshot"/> to attribute the elapsed time to the phases of
/// <see cref="ReasoningPhase"/>.
/// </summary>
/// <remarks>
/// State is thread-local because the deciding engines run synchronously on the
/// thread that invokes them: enabling measurement on the harness thread
/// isolates the measurement from any reasoning concurrently underway on other
/// threads, and needs no synchronisation. Time is accumulated as raw
/// <see cref="Stopwatch"/> ticks and converted to milliseconds only on read, so
/// the per-region cost is two timestamp reads and an array add.
/// </remarks>
public static class ReasoningInstrumentation
{
    /// <summary>The number of phases — the width of the accumulator arrays and the member count of <see cref="ReasoningPhase"/>.</summary>
    public const int PhaseCount = 6;

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

    /// <summary>Enables phase measurement on the calling thread and clears its accumulators.</summary>
    public static void Enable()
    {
        enabled = true;
        phaseTicks = new long[PhaseCount];
        phaseCounts = new long[PhaseCount];
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
    public static void End(ReasoningPhase phase, long startTimestamp)
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
    public static ReasoningInstrumentationReport Snapshot()
    {
        return new ReasoningInstrumentationReport(phaseTicks ?? new long[PhaseCount], phaseCounts ?? new long[PhaseCount]);
    }
}

/// <summary>
/// An immutable read of <see cref="ReasoningInstrumentation"/>'s per-phase
/// accumulators: elapsed milliseconds and call count for each
/// <see cref="ReasoningPhase"/>. Ticks are converted to milliseconds against
/// <see cref="Stopwatch.Frequency"/>.
/// </summary>
public sealed class ReasoningInstrumentationReport
{
    private readonly long[] ticks;
    private readonly long[] counts;

    /// <summary>Captures a copy of the accumulator arrays so the report is stable against later measurement.</summary>
    /// <param name="ticks">The per-phase tick accumulators.</param>
    /// <param name="counts">The per-phase call counters.</param>
    internal ReasoningInstrumentationReport(long[] ticks, long[] counts)
    {
        this.ticks = (long[])ticks.Clone();
        this.counts = (long[])counts.Clone();
    }

    /// <summary>The elapsed milliseconds attributed to the SAT-solve phase.</summary>
    public double SatSolveMilliseconds
    {
        get
        {
            return ToMilliseconds(ticks[(int)ReasoningPhase.SatSolve]);
        }
    }

    /// <summary>The number of SAT world solves measured.</summary>
    public long SatSolveCount
    {
        get
        {
            return counts[(int)ReasoningPhase.SatSolve];
        }
    }

    /// <summary>The elapsed milliseconds attributed to the blocking phase.</summary>
    public double BlockingMilliseconds
    {
        get
        {
            return ToMilliseconds(ticks[(int)ReasoningPhase.Blocking]);
        }
    }

    /// <summary>The number of blocking checks measured.</summary>
    public long BlockingCount
    {
        get
        {
            return counts[(int)ReasoningPhase.Blocking];
        }
    }

    /// <summary>The elapsed milliseconds attributed to the tableau-rule phase.</summary>
    public double TableauRuleMilliseconds
    {
        get
        {
            return ToMilliseconds(ticks[(int)ReasoningPhase.TableauRule]);
        }
    }

    /// <summary>The number of tableau rule-application steps measured.</summary>
    public long TableauRuleCount
    {
        get
        {
            return counts[(int)ReasoningPhase.TableauRule];
        }
    }

    /// <summary>The elapsed milliseconds attributed to the concrete-domain consistency phase.</summary>
    public double DataConsistencyMilliseconds
    {
        get
        {
            return ToMilliseconds(ticks[(int)ReasoningPhase.DataConsistency]);
        }
    }

    /// <summary>The number of concrete-domain consistency decisions measured.</summary>
    public long DataConsistencyCount
    {
        get
        {
            return counts[(int)ReasoningPhase.DataConsistency];
        }
    }

    /// <summary>The elapsed milliseconds attributed to the EL saturation phase.</summary>
    public double ElSaturationMilliseconds
    {
        get
        {
            return ToMilliseconds(ticks[(int)ReasoningPhase.ElSaturation]);
        }
    }

    /// <summary>The number of EL saturations measured.</summary>
    public long ElSaturationCount
    {
        get
        {
            return counts[(int)ReasoningPhase.ElSaturation];
        }
    }

    /// <summary>The elapsed milliseconds attributed to the context-saturation phase.</summary>
    public double ContextSaturationMilliseconds
    {
        get
        {
            return ToMilliseconds(ticks[(int)ReasoningPhase.ContextSaturation]);
        }
    }

    /// <summary>The number of context saturations measured — one per admitted context decision.</summary>
    public long ContextSaturationCount
    {
        get
        {
            return counts[(int)ReasoningPhase.ContextSaturation];
        }
    }

    /// <summary>The sum of the milliseconds attributed across every phase — the measured fraction of a decision, against which unattributed time is the remainder.</summary>
    public double TotalAttributedMilliseconds
    {
        get
        {
            double total = 0.0;
            for(int phase = 0; phase < ticks.Length; phase++)
            {
                total += ToMilliseconds(ticks[phase]);
            }

            return total;
        }
    }

    /// <summary>Converts a <see cref="Stopwatch"/> tick count to milliseconds.</summary>
    /// <param name="tickCount">The accumulated ticks.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double ToMilliseconds(long tickCount)
    {
        return tickCount * 1000.0 / Stopwatch.Frequency;
    }
}
