using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Tests.Diagnostics;

/// <summary>
/// The storage scrub's structured trace event: it carries scalar-only verify/repair payload, exposes the
/// <see cref="ITraceEvent"/> stream fields, and flows through the diagnostics <see cref="TraceHandler{TEvent}"/>
/// channel an emitter binds — the seam a later scrub round emits per verified block and per queued repair.
/// </summary>
[TestClass]
internal sealed class StorageTraceEventTests
{
    /// <summary>Captures emitted events through a method-group handler so the test body holds no closure.</summary>
    private sealed class TraceCapture
    {
        /// <summary>The events captured, in emission order.</summary>
        public List<StorageTraceEvent> Events { get; } = [];

        /// <summary>The handler entry point; a method group converts to <see cref="TraceHandler{TEvent}"/>.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in StorageTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>The event exposes its scalar payload and the trace-stream fields, and is an <see cref="ITraceEvent"/>.</summary>
    [TestMethod]
    public void EventExposesItsScalarFieldsAsAnITraceEvent()
    {
        Guid correlation = new("11112222-3333-4444-5555-666677778888");
        StorageTraceEvent evt = new(
            SequenceNumber: 7,
            TimestampTicks: 123456789,
            CorrelationId: correlation,
            Kind: StorageTraceEventKind.BlockCorrupt,
            CommitGeneration: 42,
            RoleCode: 2,
            BlockIndex: 5,
            ByteOffset: 4096,
            ByteLength: 128,
            ItemCount: 0);

        Assert.AreEqual(StorageTraceEventKind.BlockCorrupt, evt.Kind);
        Assert.AreEqual(42, evt.CommitGeneration);
        Assert.AreEqual(2, evt.RoleCode);
        Assert.AreEqual(5, evt.BlockIndex);
        Assert.AreEqual(4096, evt.ByteOffset);
        Assert.AreEqual(128, evt.ByteLength);
        Assert.AreEqual(0, evt.ItemCount);

        //SequenceNumber / TimestampTicks / CorrelationId are the ITraceEvent stream fields — the type
        //declares ": ITraceEvent" (compile-time-checked, and TraceHandler<StorageTraceEvent> requires it),
        //and exposes them directly on the value.
        Assert.AreEqual(7, evt.SequenceNumber);
        Assert.AreEqual(123456789, evt.TimestampTicks);
        Assert.AreEqual(correlation, evt.CorrelationId);
    }

    /// <summary>Events flow through the trace-handler channel in emission order, carrying their payload intact.</summary>
    [TestMethod]
    public void EmitsThroughTheTraceHandlerChannel()
    {
        TraceCapture capture = new();
        TraceHandler<StorageTraceEvent> handler = capture.Capture;

        StorageTraceEvent verified = new(1, 1000, Guid.Empty, StorageTraceEventKind.BlockVerified, 9, 1, 0, 0, 1536, 16);
        StorageTraceEvent lost = new(2, 2000, Guid.Empty, StorageTraceEventKind.NamedLoss, 9, 1, 3, 12288, 192, 16);
        handler(in verified);
        handler(in lost);

        Assert.HasCount(2, capture.Events);
        Assert.AreEqual(StorageTraceEventKind.BlockVerified, capture.Events[0].Kind);
        Assert.AreEqual(1, capture.Events[0].SequenceNumber);
        Assert.AreEqual(StorageTraceEventKind.NamedLoss, capture.Events[1].Kind);
        Assert.AreEqual(3, capture.Events[1].BlockIndex);
        Assert.AreEqual(16, capture.Events[1].ItemCount);
    }
}
