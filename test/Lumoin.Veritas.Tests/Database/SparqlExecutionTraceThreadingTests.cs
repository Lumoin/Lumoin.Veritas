using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The engine-level SPARQL execution-trace seam: a database opened with
/// <see cref="VeritasEngineOptions.SparqlExecutionTrace"/> emits per-operator events for every query it
/// answers, each run under its own non-empty correlation id with a sequence monotonic from one — the
/// contract the first-party hosts project to their consuming surfaces, and the guarantee that keeps
/// concurrent runs distinguishable on one shared stream.
/// </summary>
[TestClass]
internal sealed class SparqlExecutionTraceThreadingTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Captures emitted events through a method-group handler so the test body holds no closure.</summary>
    private sealed class TraceCapture
    {
        /// <summary>The events captured, in emission order.</summary>
        public List<SparqlExecutionTraceEvent> Events { get; } = [];

        /// <summary>The handler entry point; a method group converts to <see cref="TraceHandler{TEvent}"/>.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in SparqlExecutionTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>The immutable lane threads the handler: each query run emits operator events under its own non-empty correlation id, sequenced from one, and two runs mint distinct ids.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ImmutableLaneEmitsPerRunCorrelatedExecutionTrace()
    {
        TraceCapture capture = new();
        VeritasEngineOptions options = new() { Reasoning = null, SparqlExecutionTrace = capture.Capture };

        VeritasEngine engine = await VeritasEngine.OpenAsync(NameData(), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        VeritasQueryResult first = await engine.QueryAsync(Utf8Strings.From($"SELECT ?o WHERE {{ ?s <{Ex}name> ?o }}"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(first.IsAsk);
        AssertOneCorrelatedRun(capture.Events, 0, capture.Events.Count);

        int firstRunEventCount = capture.Events.Count;
        VeritasQueryResult second = await engine.QueryAsync(Utf8Strings.From($"SELECT ?o WHERE {{ ?s <{Ex}name> ?o }}"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(second.IsAsk);
        AssertOneCorrelatedRun(capture.Events, firstRunEventCount, capture.Events.Count);

        Assert.AreNotEqual(capture.Events[0].CorrelationId, capture.Events[firstRunEventCount].CorrelationId, "Each run mints its own correlation id, so runs stay distinguishable on one shared stream.");
    }

    /// <summary>The mutable lane threads the handler through its per-read engines: each query run over the mutable database emits correlated events exactly as the immutable lane does.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task MutableLaneEmitsPerRunCorrelatedExecutionTrace()
    {
        TraceCapture capture = new();
        VeritasEngineOptions options = new() { Reasoning = null, SparqlExecutionTrace = capture.Capture };

        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(NameData(), options, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        VeritasQueryResult first = await engine.QueryAsync(Utf8Strings.From($"SELECT ?o WHERE {{ ?s <{Ex}name> ?o }}"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(first.IsAsk);
        AssertOneCorrelatedRun(capture.Events, 0, capture.Events.Count);

        int firstRunEventCount = capture.Events.Count;
        VeritasQueryResult second = await engine.QueryAsync(Utf8Strings.From($"SELECT ?o WHERE {{ ?s <{Ex}name> ?o }}"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(second.IsAsk);
        AssertOneCorrelatedRun(capture.Events, firstRunEventCount, capture.Events.Count);

        Assert.AreNotEqual(capture.Events[0].CorrelationId, capture.Events[firstRunEventCount].CorrelationId, "The mutable database's per-read engines mint per-run correlation ids too.");
    }

    /// <summary>
    /// The streaming-operator mode's materialise-boundary re-entry emits into the SPAWNING evaluation's
    /// sink: a streamed plan whose non-streamable subtree becomes a lazy boundary (an <c>ORDER BY</c> under
    /// the projection) still reports ONE correlation and one contiguous sequence for the whole run.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task StreamingBoundaryReentryKeepsTheRunsCorrelationAndSequence()
    {
        TraceCapture capture = new();
        VeritasEngineOptions options = new()
        {
            Reasoning = null,
            SparqlExecution = SparqlEnginePolicy.Default with { PreferStreamingOperators = true },
            SparqlExecutionTrace = capture.Capture
        };

        VeritasEngine engine = await VeritasEngine.OpenAsync(NameData(), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        using VeritasSelectStream stream = await engine.StreamSelectAsync(Utf8Strings.From($"SELECT ?o WHERE {{ ?s <{Ex}name> ?o }} ORDER BY ?o"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        int rows = 0;
        await foreach(SparqlSolution solution in stream.Solutions.WithCancellation(TestContext.CancellationToken).ConfigureAwait(false))
        {
            rows++;
        }

        Assert.AreEqual(2, rows);
        AssertOneCorrelatedRun(capture.Events, 0, capture.Events.Count);
    }

    /// <summary>
    /// Asserts the events in <c>[from, to)</c> form one traced run: non-empty, at least one operator
    /// evaluation, one shared non-empty correlation id, and a sequence monotonic from one.
    /// </summary>
    /// <param name="events">The captured events.</param>
    /// <param name="from">The run's first event index.</param>
    /// <param name="to">The exclusive end index.</param>
    private static void AssertOneCorrelatedRun(List<SparqlExecutionTraceEvent> events, int from, int to)
    {
        Assert.IsGreaterThan(from, to, "The traced run emitted no events.");

        Guid correlation = events[from].CorrelationId;
        Assert.AreNotEqual(Guid.Empty, correlation, "An engine-level-traced run mints a non-empty correlation id.");

        bool sawOperator = false;
        for(int i = from; i < to; i++)
        {
            Assert.AreEqual(correlation, events[i].CorrelationId, "Every event of one run shares the run's correlation id.");
            Assert.AreEqual(i - from + 1, events[i].SequenceNumber, "The run's sequence is monotonic from one.");
            if(events[i].Kind == SparqlExecutionEventKind.OperatorEvaluated)
            {
                sawOperator = true;
            }
        }

        Assert.IsTrue(sawOperator, "The run reported at least one operator evaluation.");
    }

    /// <summary>The two-sensor name graph the queries run over.</summary>
    /// <returns>The data triples.</returns>
    private static IReadOnlyList<DataTriple> NameData()
    {
        return
        [
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "name"), new Literal(Utf8Strings.From("alice"), Iri("http://www.w3.org/2001/XMLSchema#string"))),
            new DataTriple(Iri(Ex + "s2"), Iri(Ex + "name"), new Literal(Utf8Strings.From("bob"), Iri("http://www.w3.org/2001/XMLSchema#string"))),
        ];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }
}
