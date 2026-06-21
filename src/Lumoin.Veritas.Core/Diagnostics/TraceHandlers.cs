using System;
using System.Threading.Channels;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// Static helpers that adapt common sinks to the
/// <see cref="TraceHandler{TEvent}"/> delegate shape.
/// </summary>
/// <remarks>
/// <para>
/// Consumers compose these to build the trace pipeline their scenario
/// requires. Each helper returns a <see cref="TraceHandler{TEvent}"/>
/// which the emitting subsystem assigns to its <c>Trace</c> property.
/// </para>
/// </remarks>
public static class TraceHandlers
{
    /// <summary>
    /// Returns a handler that writes events to a bounded or unbounded
    /// <see cref="Channel{T}"/>. Use <c>TryWrite</c> so that backpressure
    /// policies configured on the channel (<c>DropOldest</c>,
    /// <c>DropNewest</c>, <c>Wait</c>, etc.) take effect naturally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <see cref="BoundedChannelFullMode.Wait"/> channels a full
    /// channel causes <c>TryWrite</c> to return <c>false</c>; events are
    /// dropped silently. Consumers who cannot tolerate drops should use
    /// a bounded channel with <see cref="BoundedChannelFullMode.DropOldest"/>
    /// or write a custom handler that blocks on <c>WriteAsync</c>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="writer">The channel writer to push events to.</param>
    /// <returns>A trace handler routing events to the channel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static TraceHandler<TEvent> ToChannel<TEvent>(ChannelWriter<TEvent> writer)
        where TEvent : struct, ITraceEvent
    {
        ArgumentNullException.ThrowIfNull(writer);
        return new ChannelTraceHandler<TEvent>(writer).Handle;
    }

    /// <summary>
    /// Returns a handler that forwards every event to all supplied
    /// handlers in array order. Used when multiple consumers observe
    /// the same trace stream — for example, a UI canvas and a JSONL
    /// file sink running simultaneously.
    /// </summary>
    /// <remarks>
    /// Exceptions from one handler do not prevent subsequent handlers
    /// from being called; the composite catches and discards them. For
    /// strict failure semantics, write a custom handler.
    /// </remarks>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="handlers">The handlers to fan out to.</param>
    /// <returns>A composite handler.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handlers"/> is <c>null</c>.</exception>
    public static TraceHandler<TEvent> Composite<TEvent>(params TraceHandler<TEvent>[] handlers)
        where TEvent : struct, ITraceEvent
    {
        ArgumentNullException.ThrowIfNull(handlers);
        TraceHandler<TEvent>[] copy = new TraceHandler<TEvent>[handlers.Length];
        Array.Copy(handlers, copy, handlers.Length);
        return new CompositeTraceHandler<TEvent>(copy).Handle;
    }

    private static void InvokeAll<TEvent>(TraceHandler<TEvent>[] handlers, in TEvent evt)
        where TEvent : struct, ITraceEvent
    {
        foreach(TraceHandler<TEvent> handler in handlers)
        {
            try
            {
                handler(in evt);
            }
            catch
            {
                //Swallow per docs: one misbehaving handler must not block others.
            }
        }
    }

    /// <summary>
    /// Carries the channel writer behind a <see cref="ToChannel{TEvent}"/> handler as
    /// explicit state, so the produced <see cref="TraceHandler{TEvent}"/> closes over no
    /// enclosing local.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="writer">The channel writer events are pushed to.</param>
    private sealed class ChannelTraceHandler<TEvent>(ChannelWriter<TEvent> writer)
        where TEvent : struct, ITraceEvent
    {
        /// <summary>The channel writer events are pushed to.</summary>
        private ChannelWriter<TEvent> Writer { get; } = writer;

        /// <summary>Writes <paramref name="evt"/> to the channel, honouring its backpressure policy.</summary>
        /// <param name="evt">The event to write.</param>
        public void Handle(in TEvent evt)
        {
            Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Carries the snapshot of fan-out handlers behind a <see cref="Composite{TEvent}"/>
    /// result as explicit state, so the produced <see cref="TraceHandler{TEvent}"/> closes
    /// over no enclosing local.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="handlers">The handlers to fan out to, in order.</param>
    private sealed class CompositeTraceHandler<TEvent>(TraceHandler<TEvent>[] handlers)
        where TEvent : struct, ITraceEvent
    {
        /// <summary>The handlers to fan out to, in order.</summary>
        private TraceHandler<TEvent>[] Handlers { get; } = handlers;

        /// <summary>Forwards <paramref name="evt"/> to every handler in order.</summary>
        /// <param name="evt">The event to fan out.</param>
        public void Handle(in TEvent evt)
        {
            InvokeAll(Handlers, in evt);
        }
    }
}
