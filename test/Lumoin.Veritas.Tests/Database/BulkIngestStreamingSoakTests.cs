using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The in-suite pin for the bulk-load streaming-ingest boundary at a modest scale: a functional soak that opens the
/// quad-stream <see cref="VeritasEngine.OpenAsync(System.Collections.Generic.IAsyncEnumerable{Quad}, VeritasEngineOptions?, CancellationToken)"/>
/// over an in-memory quad stream (no file, so nothing here touches the filesystem), asserts the served contents and
/// graph routing, and reports wall-clock and allocated bytes through <see cref="TestContext"/> — with no timing- or
/// allocation-dependent assertion, so it cannot flake under a loaded parallel suite. A second method loads the same
/// data through the list overload and asserts the two engines answer identically, the cannot-diverge property the
/// shared encoded-input cores promise. The scale-out numbers live in the Benchmarks harness's
/// <c>--profile-bulk-load</c>, where the process-wide metrics are meaningful; this pin only guards correctness.
/// </summary>
[TestClass]
internal sealed class BulkIngestStreamingSoakTests
{
    /// <summary>The example-namespace prefix the generated data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The modest triple count the soak generates in memory.</summary>
    private const int TripleCount = 50_000;

    /// <summary>The size of the shared predicate vocabulary the chain edges cycle through.</summary>
    private const int PredicateCount = 4;

    /// <summary>The number of named graphs the corpus spreads its named triples across.</summary>
    private const int NamedGraphCount = 2;

    /// <summary>Every this-many-th triple is routed into a named graph; the rest form the default-graph chain.</summary>
    private const int NamedGraphEvery = 8;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Reasoning is off so the served triple counts stay exactly the ingested counts and the list and stream cores are compared over the asserted data alone.</summary>
    private static VeritasEngineOptions IngestOptions { get; } = VeritasEngineOptions.Default with { Reasoning = null };

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From(Ex + local));
    }

    /// <summary>Yields a materialised quad list as an async stream, so the engine ingests it through the streaming boundary exactly as a real streaming parser would.</summary>
    /// <param name="quads">The quads to yield.</param>
    /// <param name="cancellationToken">A token that aborts enumeration.</param>
    /// <returns>The quads, yielded asynchronously.</returns>
    private static async IAsyncEnumerable<Quad> ToAsync(IReadOnlyList<Quad> quads, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach(Quad quad in quads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return quad;
        }
    }

    /// <summary>Generates the deterministic corpus: a default-graph chain over a shared predicate vocabulary with every eighth triple routed round-robin into one of a few named graphs.</summary>
    /// <returns>The generated corpus.</returns>
    private static Corpus Generate()
    {
        List<Quad> quads = new(TripleCount);
        long defaultCount = 0;
        long namedCount = 0;
        for(int index = 0; index < TripleCount; index++)
        {
            NamedNode subject = Iri("n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            NamedNode predicate = Iri("p" + (index % PredicateCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
            NamedNode @object = Iri("n" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

            if(index % NamedGraphEvery == 0)
            {
                NamedNode graph = Iri("g" + ((index / NamedGraphEvery) % NamedGraphCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
                quads.Add(new Quad(subject, predicate, @object, graph));
                namedCount++;
            }
            else
            {
                quads.Add(new Quad(subject, predicate, @object));
                defaultCount++;
            }
        }

        return new Corpus(quads, defaultCount, namedCount);
    }

    /// <summary>Partitions a corpus's quads into the default-graph triple list and per-named-graph triple lists the list overload consumes — the same data the quad stream carries.</summary>
    /// <param name="corpus">The corpus to partition.</param>
    /// <returns>The default-graph triples and the named graphs, each its graph-name term paired with its triples.</returns>
    private static (List<DataTriple> DefaultGraph, List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> NamedGraphs) Partition(Corpus corpus)
    {
        List<DataTriple> defaultGraph = [];
        Dictionary<string, List<DataTriple>> buckets = [];
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs = [];
        foreach(Quad quad in corpus.Quads)
        {
            DataTriple triple = new(quad.Subject, quad.Predicate, quad.Object);
            if(quad.Graph is null)
            {
                defaultGraph.Add(triple);
            }
            else
            {
                string key = quad.Graph.ToString();
                if(!buckets.TryGetValue(key, out List<DataTriple>? bucket))
                {
                    bucket = [];
                    buckets[key] = bucket;
                    namedGraphs.Add((quad.Graph, bucket));
                }

                bucket.Add(triple);
            }
        }

        return (defaultGraph, namedGraphs);
    }

    /// <summary>Counts the solutions a query yields against a database.</summary>
    /// <param name="database">The database to query.</param>
    /// <param name="query">The SELECT query text.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The number of solutions.</returns>
    private static async Task<int> SolutionCountAsync(VeritasEngine database, string query, CancellationToken cancellationToken)
    {
        VeritasQueryResult result = await database.QueryAsync(Utf8Strings.From(query), cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Bindings!.Solutions.Count;
    }

    [TestMethod]
    public async Task StreamingIngestServesTheCorpusAndReportsThroughput()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        Corpus corpus = Generate();

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long startTimestamp = Stopwatch.GetTimestamp();
        VeritasEngine engine = await VeritasEngine.OpenAsync(ToAsync(corpus.Quads, cancellationToken), IngestOptions, cancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        //Functional correctness only — no timing or allocation assertion, so a loaded parallel suite cannot flake this.
        int defaultServed = await SolutionCountAsync(engine, "SELECT ?s ?p ?o WHERE { ?s ?p ?o }", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(corpus.DefaultCount, defaultServed, "The served default-graph count equals the ingested default-graph count.");

        int namedServed = await SolutionCountAsync(engine, "SELECT ?g ?s ?p ?o WHERE { GRAPH ?g { ?s ?p ?o } }", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(corpus.NamedCount, namedServed, "The served named-graph count equals the ingested named-graph count.");

        //A default-graph triple (index 1 is default: 1 % 8 != 0) is served, and its counterpart routed into a named
        //graph (index 0) is served only under its graph, not in the default graph — the graph routing the stream carries.
        Assert.IsTrue(await engine.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}n1> <{Ex}p1> <{Ex}n2> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "A default-graph triple is served.");
        Assert.IsTrue(await engine.AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g0> {{ <{Ex}n0> <{Ex}p0> <{Ex}n1> }} }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "A named-graph triple is served under its graph.");
        Assert.IsFalse(await engine.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}n0> <{Ex}p0> <{Ex}n1> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "A named-graph triple is not served in the default graph.");

        double bytesPerTriple = allocated / (double)corpus.Total;
        TestContext.WriteLine($"bulk ingest streaming soak: triples={corpus.Total:N0}, default={corpus.DefaultCount:N0}, named={corpus.NamedCount:N0}, time={elapsed.TotalMilliseconds:F0}ms, allocated={allocated / (1024.0 * 1024.0):F1}MB, bytes/triple={bytesPerTriple:F0}");
    }

    [TestMethod]
    public async Task StreamAndListOpensAnswerIdentically()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        Corpus corpus = Generate();
        (List<DataTriple> defaultGraph, List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs) = Partition(corpus);

        VeritasEngine streamEngine = await VeritasEngine.OpenAsync(ToAsync(corpus.Quads, cancellationToken), IngestOptions, cancellationToken).ConfigureAwait(false);
        await using var streamScope = streamEngine.ConfigureAwait(false);

        VeritasEngine listEngine = await VeritasEngine.OpenAsync(defaultGraph, namedGraphs, IngestOptions, cancellationToken).ConfigureAwait(false);
        await using var listScope = listEngine.ConfigureAwait(false);

        //The shared encoded-input core promises the two opens intern the same terms and serve the same triples: same
        //dictionary size, same default and named counts, and same membership answers — a load-robust differential.
        Assert.AreEqual(listEngine.Dictionary.Count, streamEngine.Dictionary.Count, "The two opens intern the same distinct terms.");

        const string DefaultQuery = "SELECT ?s ?p ?o WHERE { ?s ?p ?o }";
        const string NamedQuery = "SELECT ?g ?s ?p ?o WHERE { GRAPH ?g { ?s ?p ?o } }";
        Assert.AreEqual(
            await SolutionCountAsync(listEngine, DefaultQuery, cancellationToken).ConfigureAwait(false),
            await SolutionCountAsync(streamEngine, DefaultQuery, cancellationToken).ConfigureAwait(false),
            "The default-graph contents match.");
        Assert.AreEqual(
            await SolutionCountAsync(listEngine, NamedQuery, cancellationToken).ConfigureAwait(false),
            await SolutionCountAsync(streamEngine, NamedQuery, cancellationToken).ConfigureAwait(false),
            "The named-graph contents match.");

        //Probe membership on both engines: a default triple, a named triple under its graph, and an absent triple.
        await AssertSameAskAsync(streamEngine, listEngine, $"ASK {{ <{Ex}n1> <{Ex}p1> <{Ex}n2> }}", cancellationToken).ConfigureAwait(false);
        await AssertSameAskAsync(streamEngine, listEngine, $"ASK {{ GRAPH <{Ex}g0> {{ <{Ex}n0> <{Ex}p0> <{Ex}n1> }} }}", cancellationToken).ConfigureAwait(false);
        await AssertSameAskAsync(streamEngine, listEngine, $"ASK {{ <{Ex}missing> <{Ex}p0> <{Ex}n1> }}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asserts two engines answer an ASK identically.</summary>
    /// <param name="first">The first engine.</param>
    /// <param name="second">The second engine.</param>
    /// <param name="ask">The ASK query text.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    private static async Task AssertSameAskAsync(VeritasEngine first, VeritasEngine second, string ask, CancellationToken cancellationToken)
    {
        bool firstAnswer = await first.AskAsync(Utf8Strings.From(ask), cancellationToken: cancellationToken).ConfigureAwait(false);
        bool secondAnswer = await second.AskAsync(Utf8Strings.From(ask), cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(firstAnswer, secondAnswer, $"The stream and list opens agree on {ask}.");
    }

    /// <summary>The generated corpus: the quads and the realised default and named counts.</summary>
    /// <param name="Quads">The generated quads.</param>
    /// <param name="DefaultCount">The number of default-graph triples.</param>
    /// <param name="NamedCount">The number of named-graph triples.</param>
    private sealed record Corpus(IReadOnlyList<Quad> Quads, long DefaultCount, long NamedCount)
    {
        /// <summary>The total triple count across the default and named graphs.</summary>
        public long Total => DefaultCount + NamedCount;
    }
}
