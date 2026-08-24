using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core.Diagnostics;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Minimal trace event for the dark-cost seam comparison. Carries only
/// the <see cref="ITraceEvent"/> correlation substrate; the armed path
/// never executes in these benchmarks, so no payload fields are needed.
/// </summary>
/// <param name="SequenceNumber">Monotonic sequence number within the stream.</param>
/// <param name="TimestampTicks">UTC timestamp in ticks.</param>
/// <param name="CorrelationId">Correlation id of the logical operation.</param>
public readonly record struct SeamProbeTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId): ITraceEvent;

/// <summary>
/// Head-to-head dark-cost comparison of the two candidate hot-path
/// observability gates: the nullable <see cref="TraceHandler{TEvent}"/>
/// null-check idiom versus <see cref="ActivitySource.HasListeners"/>.
/// Both gates are measured DARK (no handler attached, no listener
/// registered) — the question is what an instrumented site costs when
/// nobody is listening.
/// </summary>
/// <remarks>
/// <para>
/// Each gate is measured in two shapes bracketing what the JIT can do
/// with it. The <b>Inline</b> shape consults the gate directly inside
/// the measurement loop, where invariant code motion may hoist the
/// check — the best case for a hot loop over an invariant field. The
/// <b>PerCall</b> shape consults the gate inside a non-inlinable
/// method called once per iteration, forcing a fresh check per call —
/// the worst case, modelling an emission site behind a real call
/// boundary.
/// </para>
/// <para>
/// Every variant performs the same loop-carried accumulator work as
/// <see cref="EmptyLoopBaseline"/>, so the reported difference against
/// the baseline is the gate cost alone. Results are per gate
/// consultation via <c>OperationsPerInvoke</c>.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "BenchmarkDotNet instantiates this class via reflection.")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public types and members for its reflection-based runner.")]
public class TraceSeamDarkCostBenchmarks
{
    /// <summary>Gate consultations per benchmark invocation.</summary>
    private const int LoopLength = 4096;

    /// <summary>
    /// The dark activity source: constructed, never listened to.
    /// </summary>
    private static readonly ActivitySource SeamProbeSource = new("Lumoin.Veritas.Benchmarks.SeamProbe");

    /// <summary>
    /// The dark trace handler: the null default every emitter carries
    /// when no consumer is attached. A settable property mirroring the
    /// production probe seams; these benchmarks never set it.
    /// </summary>
    public TraceHandler<SeamProbeTraceEvent>? SeamTrace { get; set; }

    /// <summary>
    /// The loop skeleton alone: the shared accumulator work with no
    /// gate, subtracted from every gated variant to isolate gate cost.
    /// </summary>
    /// <returns>The loop-carried accumulator, returned to keep the loop live.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = LoopLength)]
    public long EmptyLoopBaseline()
    {
        long accumulator = 0;
        for(int i = 0; i < LoopLength; i++)
        {
            accumulator += (uint)i;
        }

        return accumulator;
    }

    /// <summary>
    /// The null-delegate gate consulted directly in the loop: the house
    /// emitter idiom <c>if(handler is not null) handler(in evt);</c>
    /// with the handler null.
    /// </summary>
    /// <returns>The loop-carried accumulator, returned to keep the loop live.</returns>
    [Benchmark(OperationsPerInvoke = LoopLength)]
    public long NullDelegateGateInline()
    {
        long accumulator = 0;
        for(int i = 0; i < LoopLength; i++)
        {
            TraceHandler<SeamProbeTraceEvent>? handler = SeamTrace;
            if(handler is not null)
            {
                SeamProbeTraceEvent evt = new(i, i, default);
                handler(in evt);
            }

            accumulator += (uint)i;
        }

        return accumulator;
    }

    /// <summary>
    /// The listener gate consulted directly in the loop:
    /// <see cref="ActivitySource.HasListeners"/> with no listener
    /// registered.
    /// </summary>
    /// <returns>The loop-carried accumulator, returned to keep the loop live.</returns>
    [Benchmark(OperationsPerInvoke = LoopLength)]
    public long HasListenersGateInline()
    {
        long accumulator = 0;
        for(int i = 0; i < LoopLength; i++)
        {
            if(SeamProbeSource.HasListeners())
            {
                SeamProbeSource.StartActivity("SeamProbe")?.Dispose();
            }

            accumulator += (uint)i;
        }

        return accumulator;
    }

    /// <summary>
    /// The null-delegate gate behind a non-inlinable call boundary,
    /// forcing a fresh field load and check on every consultation.
    /// </summary>
    /// <returns>The loop-carried accumulator, returned to keep the loop live.</returns>
    [Benchmark(OperationsPerInvoke = LoopLength)]
    public long NullDelegateGatePerCall()
    {
        long accumulator = 0;
        for(int i = 0; i < LoopLength; i++)
        {
            EmitThroughNullDelegateGate(i);
            accumulator += (uint)i;
        }

        return accumulator;
    }

    /// <summary>
    /// The listener gate behind a non-inlinable call boundary, forcing
    /// a fresh <see cref="ActivitySource.HasListeners"/> consultation
    /// on every call.
    /// </summary>
    /// <returns>The loop-carried accumulator, returned to keep the loop live.</returns>
    [Benchmark(OperationsPerInvoke = LoopLength)]
    public long HasListenersGatePerCall()
    {
        long accumulator = 0;
        for(int i = 0; i < LoopLength; i++)
        {
            EmitThroughHasListenersGate(i);
            accumulator += (uint)i;
        }

        return accumulator;
    }

    /// <summary>
    /// One emission site behind the null-delegate gate, kept out of the
    /// caller so the gate cannot be hoisted.
    /// </summary>
    /// <param name="sequence">The event sequence number for the armed path.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EmitThroughNullDelegateGate(int sequence)
    {
        TraceHandler<SeamProbeTraceEvent>? handler = SeamTrace;
        if(handler is not null)
        {
            SeamProbeTraceEvent evt = new(sequence, sequence, default);
            handler(in evt);
        }
    }

    /// <summary>
    /// One emission site behind the listener gate, kept out of the
    /// caller so the gate cannot be hoisted.
    /// </summary>
    /// <param name="sequence">The activity discriminator for the armed path.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EmitThroughHasListenersGate(int sequence)
    {
        if(SeamProbeSource.HasListeners())
        {
            SeamProbeSource.StartActivity("SeamProbe")?.AddTag("sequence", sequence).Dispose();
        }
    }
}
