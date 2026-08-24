using System;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Database;

/// <summary>
/// Runs a <see cref="GraphAnalyticsCatalog"/> descriptor, bracketing it with <see cref="GraphAlgorithmTraceEvent"/>
/// Started/Completed events on an optional trace handler so every surface (the in-process analytics <c>SERVICE</c>
/// and the CLI/MCP/HTTP operations) attributes analytics cost and row counts through one emission point rather than
/// re-implementing it. A run draws two monotonically increasing sequence numbers from this runner's own counter, so
/// concurrent runs through the same runner stay ordered, and its two events share a freshly minted correlation id —
/// the join key a consumer reassembles a single run from. A <see langword="null"/> handler runs the descriptor
/// directly with no emission, so an untraced surface pays nothing.
/// </summary>
/// <remarks>
/// Hold one runner per trace stream: an engine keeps a single instance for its lifetime so every analytics run it
/// serves shares one monotonic sequence, while a one-shot operation constructs a fresh runner whose sequence starts
/// over. The clock and identifier source are injected for deterministic-when-wanted timestamps and correlation ids.
/// </remarks>
public sealed class GraphAnalyticsRunner
{
    /// <summary>The clock stamping the emitted events and timing the run.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The identifier source minting each run's correlation id.</summary>
    private IdentifierDelegate Identifiers { get; }

    //A naked field rather than a property because Interlocked.Increment requires a ref to the backing storage,
    //which a property cannot expose; it is the monotonic sequence counter for this runner's trace stream.
    private long sequence;

    /// <summary>Constructs a runner over an optional clock and identifier source.</summary>
    /// <param name="timeProvider">The clock stamping events and timing runs; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="identifiers">The correlation-id source; <see langword="null"/> uses <see cref="VeritasIdentifiers.System"/>.</param>
    public GraphAnalyticsRunner(TimeProvider? timeProvider = null, IdentifierDelegate? identifiers = null)
    {
        TimeProvider = timeProvider ?? TimeProvider.System;
        Identifiers = identifiers ?? VeritasIdentifiers.System;
    }

    /// <summary>
    /// Runs <paramref name="descriptor"/> over <paramref name="context"/>, emitting a Started event before and a
    /// Completed event (carrying the result-row count and the run's wall-clock duration) after on
    /// <paramref name="trace"/> when it is non-<see langword="null"/>; a <see langword="null"/> handler runs the
    /// descriptor directly.
    /// </summary>
    /// <param name="descriptor">The catalog descriptor to run.</param>
    /// <param name="context">The analytics inputs the descriptor reads.</param>
    /// <param name="trace">The trace handler the run events go to, or <see langword="null"/> for no tracing.</param>
    /// <returns>The descriptor's result set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public SparqlResultSet Run(GraphAnalyticsDescriptor descriptor, AnalyticsContext context, TraceHandler<GraphAlgorithmTraceEvent>? trace)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(context);

        if(trace is null)
        {
            return descriptor.Run(context);
        }

        Guid correlationId = Identifiers(new IdentifierRequest(IdentifierPurpose.Correlation, default));

        GraphAlgorithmTraceEvent started = GraphAlgorithmTraceEvent.Started(Interlocked.Increment(ref sequence), TimeProvider.GetUtcNow().UtcTicks, correlationId, descriptor.Name);
        trace(in started);

        long startTimestamp = TimeProvider.GetTimestamp();
        SparqlResultSet result = descriptor.Run(context);
        long durationTicks = TimeProvider.GetElapsedTime(startTimestamp).Ticks;

        GraphAlgorithmTraceEvent completed = GraphAlgorithmTraceEvent.Completed(Interlocked.Increment(ref sequence), TimeProvider.GetUtcNow().UtcTicks, correlationId, descriptor.Name, result.Solutions.Count, durationTicks);
        trace(in completed);

        return result;
    }
}
