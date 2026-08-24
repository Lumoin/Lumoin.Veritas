using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The engine-level attachment for the sampled in-saturation progress trace:
/// the handler each power-of-two mark is emitted to, the clock stamping the
/// marks, and the correlation id linking them to the decision they belong to.
/// A saturation engine with no sampler attached emits nothing at zero cost;
/// attaching at the engine is the certified surface, with the facade option
/// plumbing the banked consumer.
/// </summary>
public sealed class SaturationProgressSampler
{
    /// <summary>Binds the sampler's three parts.</summary>
    /// <param name="handler">The handler receiving each emitted mark.</param>
    /// <param name="clock">The clock stamping each mark's emission time.</param>
    /// <param name="correlationId">The correlation id carried on every mark.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> or <paramref name="clock"/> is <see langword="null"/>.</exception>
    public SaturationProgressSampler(TraceHandler<SaturationProgressTraceEvent> handler, TimeProvider clock, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);
        Handler = handler;
        Clock = clock;
        CorrelationId = correlationId;
    }

    /// <summary>The handler receiving each emitted mark.</summary>
    public TraceHandler<SaturationProgressTraceEvent> Handler { get; }

    /// <summary>The clock stamping each mark's emission time.</summary>
    public TimeProvider Clock { get; }

    /// <summary>The correlation id carried on every mark.</summary>
    public Guid CorrelationId { get; }
}
