namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// Delegate type receiving structured trace events from a Veritas
/// subsystem. The producer side of the subsystem's trace channel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a delegate and not an interface.</b> A single-method contract
/// composes more naturally as a C# lambda than as an interface. Consumer
/// code writes <c>validator.Trace = evt =&gt; channel.Writer.TryWrite(evt);</c>
/// directly, with no wrapper type in between. Common patterns
/// (channels, composites, forwarding to OpenTelemetry) are offered as
/// static adapters in <see cref="TraceHandlers"/>.
/// </para>
/// <para>
/// <b>Why <c>in</c>.</b> Concrete event types are expected to be
/// <c>readonly record struct</c>. The <c>in</c> parameter avoids the
/// copy that a by-value parameter would impose. At the call site
/// <c>handler(in evt)</c> passes a reference; the JIT elides the
/// indirection for straightforward forwarders.
/// </para>
/// <para>
/// <b>Allocation behaviour.</b> A null handler is the zero-cost default:
/// emitters pattern <c>if (handler is not null) handler(in evt);</c> and
/// the branch is predicted-taken-not-traced under normal operation.
/// When a handler is attached, the cost is one indirect call plus
/// whatever the handler does.
/// </para>
/// <para>
/// <b>Concurrency.</b> Handlers must be safe to call from the thread of
/// the emitting subsystem. Most emitters are single-threaded per
/// operation; handlers that fan out to multiple threads should
/// synchronize internally.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">
/// The concrete event type, constrained to <c>struct</c> and
/// <see cref="ITraceEvent"/>.
/// </typeparam>
/// <param name="evt">The event being emitted. Passed by reference.</param>
public delegate void TraceHandler<TEvent>(in TEvent evt)
    where TEvent : struct, ITraceEvent;
