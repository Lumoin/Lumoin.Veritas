using System;
using System.Threading;
using System.Threading.Channels;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// Fans the served engine's execution-trace events out to the <c>/trace</c> subscribers. The engine-side
/// <see cref="Handle"/> is the <see cref="TraceHandler{TEvent}"/> wired into the served database's options:
/// with no subscriber it returns after one volatile read, otherwise it projects the event to its wire shape
/// once and offers it to every subscriber's bounded channel — a subscriber that cannot keep up loses its
/// oldest events rather than ever blocking the query thread.
/// </summary>
internal sealed class TraceStreamHub
{
    /// <summary>The per-subscriber channel capacity; a subscriber farther behind than this loses its oldest events.</summary>
    private const int SubscriberCapacity = 1024;

    /// <summary>The current subscriber writers, replaced wholesale under <see cref="Gate"/> so <see cref="Handle"/> reads one immutable snapshot without locking.</summary>
    private ChannelWriter<TraceWireEvent>[] writers = [];

    /// <summary>The lock subscription changes mutate the writer snapshot under.</summary>
    private Lock Gate { get; } = new();

    /// <summary>Handles one engine trace event: projects it to the wire once and offers it to every subscriber.</summary>
    /// <param name="evt">The engine event.</param>
    public void Handle(in SparqlExecutionTraceEvent evt)
    {
        ChannelWriter<TraceWireEvent>[] current = Volatile.Read(ref writers);
        if(current.Length == 0)
        {
            return;
        }

        TraceWireEvent wire = SparqlExecutionTraceWire.ToWire(in evt);
        foreach(ChannelWriter<TraceWireEvent> writer in current)
        {
            //DropOldest bounding makes this non-blocking; a false return only races an unsubscribe's completion.
            writer.TryWrite(wire);
        }
    }

    /// <summary>Adds a subscriber and returns its subscription; disposing it removes the subscriber and completes its channel.</summary>
    /// <returns>The subscription carrying the reader the subscriber drains.</returns>
    public TraceStreamSubscription Subscribe()
    {
        Channel<TraceWireEvent> channel = Channel.CreateBounded<TraceWireEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        lock(Gate)
        {
            ChannelWriter<TraceWireEvent>[] next = new ChannelWriter<TraceWireEvent>[writers.Length + 1];
            Array.Copy(writers, next, writers.Length);
            next[^1] = channel.Writer;
            Volatile.Write(ref writers, next);
        }

        return new TraceStreamSubscription(this, channel);
    }

    /// <summary>Removes a subscriber's writer from the snapshot; the subscription's dispose calls this.</summary>
    /// <param name="writer">The writer to remove.</param>
    internal void Unsubscribe(ChannelWriter<TraceWireEvent> writer)
    {
        lock(Gate)
        {
            int index = Array.IndexOf(writers, writer);
            if(index < 0)
            {
                return;
            }

            ChannelWriter<TraceWireEvent>[] next = new ChannelWriter<TraceWireEvent>[writers.Length - 1];
            Array.Copy(writers, next, index);
            Array.Copy(writers, index + 1, next, index, writers.Length - index - 1);
            Volatile.Write(ref writers, next);
        }
    }
}

/// <summary>
/// One <c>/trace</c> subscriber's live subscription: the reader its Server-Sent-Events loop drains, and the
/// removal handshake — disposing removes the subscriber from the hub and completes the channel so a pending
/// read wakes and the loop ends.
/// </summary>
internal sealed class TraceStreamSubscription : IDisposable
{
    /// <summary>The hub the subscription removes itself from on dispose.</summary>
    private TraceStreamHub Hub { get; }

    /// <summary>The subscriber's channel; its writer side is registered with the hub.</summary>
    private Channel<TraceWireEvent> Subscription { get; }

    /// <summary>The reader the subscriber drains wire events from.</summary>
    public ChannelReader<TraceWireEvent> Reader => Subscription.Reader;

    /// <summary>Creates the subscription over an already-registered channel.</summary>
    /// <param name="hub">The owning hub.</param>
    /// <param name="subscription">The subscriber's registered channel.</param>
    public TraceStreamSubscription(TraceStreamHub hub, Channel<TraceWireEvent> subscription)
    {
        Hub = hub;
        Subscription = subscription;
    }

    /// <summary>Removes the subscriber from the hub and completes its channel.</summary>
    public void Dispose()
    {
        Hub.Unsubscribe(Subscription.Writer);
        Subscription.Writer.TryComplete();
    }
}
