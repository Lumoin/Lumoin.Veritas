using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The engine's read-only graph-analytics seam (<see cref="VeritasEngine.TryGetDefaultGraphAnalytics"/>): an engine
/// with no access-control policy exposes an analytics view over the default graph's own columnar index (the index a
/// query reuses), and a metric computed through it matches the known graph.
/// </summary>
[TestClass]
internal sealed class VeritasEngineAnalyticsTests
{
    /// <summary>The example namespace the fixtures live under.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context, used for its cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A policy-free engine exposes the default-graph analytics view, and it counts the triangle once.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task DefaultGraphAnalyticsCountsTriangleOverTheEngineIndex()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(TriangleGraph(), VeritasEngineOptions.Default with { Reasoning = null }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //Run a query first so the columnar view the analytics seam reuses is materialised, and confirm the load.
        bool edge = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}a> <{Ex}knows> <{Ex}b> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(edge, "The triangle data loaded.");

        bool available = database.TryGetDefaultGraphAnalytics(out ColumnarGraphAnalytics? analytics, out TermDictionary? dictionary);

        Assert.IsTrue(available, "An engine with no access-control policy exposes the default-graph analytics view.");
        Assert.IsNotNull(analytics);
        Assert.IsNotNull(dictionary);
        Assert.AreEqual(1L, analytics.TriangleCount(GraphProjection.AllPredicates()), "The a-b-c triangle is counted once over the engine's own columnar index.");
    }

    /// <summary>An in-process analytics SERVICE enumerates the triangle as a three-clique inside a SPARQL query.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServiceCliquesComposeInsideSparql()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(TriangleGraph(), VeritasEngineOptions.Default with { Reasoning = null }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        string endpoint = GraphAnalyticsServices.CliquesEndpoint + "?size=3";
        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"SELECT ?v0 ?v1 ?v2 WHERE {{ SERVICE <{endpoint}> {{ ?v0 ?v1 ?v2 }} }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsFalse(result.IsAsk);
        Assert.HasCount(1, result.Bindings!.Solutions, "The triangle is the one three-clique.");

        string rows = SparqlResultsDelimitedWriter.WriteToString(result.Bindings!, SparqlDelimitedResultsFormat.Csv);
        Assert.Contains($"{Ex}a", rows);
        Assert.Contains($"{Ex}b", rows);
        Assert.Contains($"{Ex}c", rows);
    }

    /// <summary>An in-process analytics SERVICE returns a scalar metric (the triangle count) as a query binding.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServiceTriangleCountComposesInsideSparql()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(TriangleGraph(), VeritasEngineOptions.Default with { Reasoning = null }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        string endpoint = GraphAnalyticsServices.TriangleCountEndpoint;
        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"SELECT ?count WHERE {{ SERVICE <{endpoint}> {{ ?count ?p ?o }} }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsFalse(result.IsAsk);
        Assert.HasCount(1, result.Bindings!.Solutions);

        string rows = SparqlResultsDelimitedWriter.WriteToString(result.Bindings!, SparqlDelimitedResultsFormat.Csv);
        Assert.Contains("1", rows);
    }

    /// <summary>The async acquisition on a policy-free engine takes the near-free fast path (a <see langword="null"/> access context) and yields the same view the synchronous seam does.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AsyncAnalyticsAcquisitionTakesTheFastPathOnAPolicyFreeEngine()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(TriangleGraph(), VeritasEngineOptions.Default with { Reasoning = null }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        (ColumnarGraphAnalytics Analytics, TermDictionary Dictionary)? acquired = await database
            .TryGetDefaultGraphAnalyticsAsync(accessContext: null, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsNotNull(acquired, "A policy-free engine yields the analytics view through the async acquisition as well.");
        Assert.AreEqual(1L, acquired.Value.Analytics.TriangleCount(GraphProjection.AllPredicates()), "The triangle is counted once over the fast-path index.");
    }

    /// <summary>
    /// Under a configured access-control policy the analytics view is built FILTERED to the caller's authorized
    /// triples: a denied edge is absent from the analytics index, so the degree it would contribute and the node it
    /// would introduce never reach an algorithm. This is the security guarantee — analytics read the order columns
    /// directly, bypassing the per-triple authorization the query path enforces, so the access-scoped index is what
    /// keeps a hidden triple from leaking through them.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task PolicyScopedAnalyticsExcludesDeniedEdges()
    {
        DenyObjectPolicy policy = new();
        VeritasEngine database = await VeritasEngine
            .OpenAsync(SecretTriangleGraph(), VeritasEngineOptions.Default with { Reasoning = null, AccessControl = policy.DecideAsync }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The policy denies every triple whose object is :secret, resolving the candidate object through the
        //dictionary the engine now exposes (a real directory-consulting policy resolves ids the same way).
        policy.Dictionary = database.Dictionary;
        policy.DeniedObjectIri = Ex + "secret";
        TestAccessContext context = new("alice");

        (ColumnarGraphAnalytics Analytics, TermDictionary Dictionary)? acquired = await database
            .TryGetDefaultGraphAnalyticsAsync(context, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsNotNull(acquired, "A policy-bearing engine builds the access-scoped analytics view rather than refusing.");

        Dictionary<string, long> degreeByNode = [];
        foreach((TermId node, long degree) in acquired.Value.Analytics.Degrees(GraphProjection.AllPredicates(GraphEdgeDirection.Forward)))
        {
            degreeByNode[((NamedNode)acquired.Value.Dictionary.Resolve(node)).Iri.ToString()] = degree;
        }

        Assert.AreEqual(2L, degreeByNode[Ex + "a"], "Node a's out-degree counts only the two allowed :knows edges, not the denied a->secret edge.");
        Assert.IsFalse(degreeByNode.ContainsKey(Ex + "secret"), "The denied edge's object never enters the analytics vertex set.");
        Assert.IsTrue(degreeByNode.ContainsKey(Ex + "b") && degreeByNode.ContainsKey(Ex + "c"), "The allowed edges' endpoints remain.");
    }

    /// <summary>
    /// The degree analytics <c>SERVICE</c>, run under a policy, returns rows for only the authorized edges: the
    /// denied edge's node is absent from the SERVICE result inside the SPARQL query — the end-to-end access-scoped
    /// path that threads the caller's context from <see cref="VeritasEngine.QueryAsync"/> through the analytics
    /// transport to the filtered view.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task PolicyScopedDegreeServiceExcludesDeniedEdges()
    {
        DenyObjectPolicy policy = new();
        VeritasEngine database = await VeritasEngine
            .OpenAsync(SecretTriangleGraph(), VeritasEngineOptions.Default with { Reasoning = null, AccessControl = policy.DecideAsync }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        policy.Dictionary = database.Dictionary;
        policy.DeniedObjectIri = Ex + "secret";
        TestAccessContext context = new("alice");

        string endpoint = GraphAnalyticsServices.DegreeEndpoint;
        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"SELECT ?node ?degree WHERE {{ SERVICE <{endpoint}> {{ ?node ?degree ?o }} }}"), accessContext: context, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsFalse(result.IsAsk);
        string rows = SparqlResultsDelimitedWriter.WriteToString(result.Bindings!, SparqlDelimitedResultsFormat.Csv);
        Assert.IsFalse(rows.Contains(Ex + "secret", StringComparison.Ordinal), "The denied edge's object node is absent from the degree SERVICE result.");
        Assert.IsTrue(rows.Contains(Ex + "a", StringComparison.Ordinal), "The authorized node a is present in the degree SERVICE result.");
    }

    /// <summary>A single-triangle default graph: nodes a, b, c pairwise connected by <c>:knows</c>.</summary>
    /// <returns>The triangle's triples.</returns>
    private static IReadOnlyList<DataTriple> TriangleGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "a"), Iri(Ex + "knows"), Iri(Ex + "b")),
            new DataTriple(Iri(Ex + "b"), Iri(Ex + "knows"), Iri(Ex + "c")),
            new DataTriple(Iri(Ex + "a"), Iri(Ex + "knows"), Iri(Ex + "c")),
        ];
    }

    /// <summary>
    /// The engine emits a Started then a Completed <see cref="GraphAlgorithmTraceEvent"/> around an analytics
    /// <c>SERVICE</c> run on the configured trace handler: the two share a correlation id, the Completed carries the
    /// result-row count, and the sequence advances across the run — the analytics observability seam.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AnalyticsServiceEmitsStartedAndCompletedTraceEvents()
    {
        Channel<GraphAlgorithmTraceEvent> channel = Channel.CreateUnbounded<GraphAlgorithmTraceEvent>();
        VeritasEngineOptions options = VeritasEngineOptions.Default with { Reasoning = null, AnalyticsTrace = TraceHandlers.ToChannel(channel.Writer) };
        VeritasEngine database = await VeritasEngine
            .OpenAsync(TriangleGraph(), options, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        string endpoint = GraphAnalyticsServices.TriangleCountEndpoint;
        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"SELECT ?count WHERE {{ SERVICE <{endpoint}> {{ ?count ?p ?o }} }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(result.IsAsk, "The analytics SERVICE ran inside the SELECT.");

        channel.Writer.Complete();
        List<GraphAlgorithmTraceEvent> events = [];
        await foreach(GraphAlgorithmTraceEvent traceEvent in channel.Reader.ReadAllAsync(TestContext.CancellationToken).ConfigureAwait(false))
        {
            events.Add(traceEvent);
        }

        Assert.HasCount(2, events, "The run emits a Started then a Completed event.");
        Assert.AreEqual(GraphAlgorithmTraceEventKind.Started, events[0].Kind);
        Assert.AreEqual(GraphAlgorithmTraceEventKind.Completed, events[1].Kind);
        Assert.AreEqual(GraphAnalyticsServices.TriangleCount, events[0].Algorithm, "The events carry the algorithm's catalog name.");
        Assert.AreEqual(events[0].CorrelationId, events[1].CorrelationId, "The two events of one run share a correlation id.");
        Assert.AreEqual(1L, events[1].ResultCount, "Triangle-count returns one row, so the Completed event reports one.");
        Assert.IsGreaterThan(events[0].SequenceNumber, events[1].SequenceNumber, "The trace sequence advances across the run.");
    }

    /// <summary>The single-triangle graph plus a fourth edge <c>a :knows secret</c> whose object the security tests' policy denies, so the filtered analytics view drops it.</summary>
    /// <returns>The graph's triples.</returns>
    private static IReadOnlyList<DataTriple> SecretTriangleGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "a"), Iri(Ex + "knows"), Iri(Ex + "b")),
            new DataTriple(Iri(Ex + "b"), Iri(Ex + "knows"), Iri(Ex + "c")),
            new DataTriple(Iri(Ex + "a"), Iri(Ex + "knows"), Iri(Ex + "c")),
            new DataTriple(Iri(Ex + "a"), Iri(Ex + "knows"), Iri(Ex + "secret")),
        ];
    }

    /// <summary>An IRI term from its string form.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>A concrete access context carrying just a caller name; the engine treats it as opaque and threads it to the policy.</summary>
    /// <param name="Who">The caller identity the test threads through.</param>
    private sealed record TestAccessContext(string Who) : AccessContext;

    /// <summary>
    /// An access-control policy that denies exactly the triples whose object is a designated IRI, resolving each
    /// candidate's encoded object through a dictionary wired after the engine opens (the policy is configured before
    /// the engine exists, so the dictionary is a settable property — the same way a real directory-consulting policy
    /// resolves ids on demand). A denied triple is answered <see cref="AccessDecision.NotFound"/>.
    /// </summary>
    private sealed class DenyObjectPolicy
    {
        /// <summary>The dictionary the policy resolves a candidate triple's encoded object through.</summary>
        public TermDictionary? Dictionary { get; set; }

        /// <summary>The IRI whose incoming edges the policy denies.</summary>
        public string DeniedObjectIri { get; set; } = string.Empty;

        /// <summary>Decides a candidate: <see cref="AccessDecision.NotFound"/> when its object is the denied IRI, otherwise <see cref="AccessDecision.Allow"/>.</summary>
        /// <param name="request">The candidate triple and the caller's context.</param>
        /// <param name="cancellationToken">A token; the synchronous policy ignores it.</param>
        /// <returns>The decision.</returns>
        public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken)
        {
            bool denied = Dictionary!.Resolve(request.Triple.Object) is NamedNode named
                && string.Equals(named.Iri.ToString(), DeniedObjectIri, StringComparison.Ordinal);

            return new ValueTask<AccessDecision>(denied ? AccessDecision.NotFound : AccessDecision.Allow);
        }
    }
}
