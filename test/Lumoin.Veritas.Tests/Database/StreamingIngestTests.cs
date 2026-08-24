using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The streaming-ingest boundary on the engine facade: the quad-stream <see cref="VeritasEngine.OpenAsync(System.Collections.Generic.IAsyncEnumerable{Quad}, VeritasEngineOptions?, CancellationToken)"/>
/// and <see cref="VeritasEngine.OpenMutableAsync(System.Collections.Generic.IAsyncEnumerable{Quad}, VeritasEngineOptions?, CancellationToken)"/>
/// overloads encode each quad into the dictionary as it streams — no intermediate quad or triple list — and must
/// be behaviourally identical to the list-based opens over the same data. These pins are the safety net on that
/// equivalence: the immutable stream open equals the dataset list open (contents, term count, named graphs); the
/// mutable stream open commits, persists, and reopens like the list open; the durable-journal option is honoured;
/// the stream is enumerated exactly once; and the real parser-to-pipe-to-engine pipeline the CLI wires answers
/// correctly.
/// </summary>
[TestClass]
internal sealed class StreamingIngestTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>A small TriG document with a default graph and one named graph, for the parser-to-pipe end-to-end pin.</summary>
    private const string TriGDocument = """
        @prefix ex: <http://example.org/> .
        ex:alice ex:knows ex:bob .
        ex:g1 {
            ex:carol ex:knows ex:dave .
        }
        """;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A named node in the example namespace for a local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From(Ex + local));
    }

    /// <summary>A data triple of three example-namespace terms.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="obj">The object local name.</param>
    /// <returns>The triple.</returns>
    private static DataTriple Triple(string subject, string predicate, string obj)
    {
        return new DataTriple(Iri(subject), Iri(predicate), Iri(obj));
    }

    /// <summary>A default-graph quad of three example-namespace terms.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="obj">The object local name.</param>
    /// <returns>The default-graph quad.</returns>
    private static Quad DefaultQuad(string subject, string predicate, string obj)
    {
        return new Quad(Iri(subject), Iri(predicate), Iri(obj));
    }

    /// <summary>A named-graph quad of three example-namespace terms in the named graph <paramref name="graph"/>.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="obj">The object local name.</param>
    /// <param name="graph">The graph local name.</param>
    /// <returns>The named-graph quad.</returns>
    private static Quad NamedQuad(string subject, string predicate, string obj, string graph)
    {
        return new Quad(Iri(subject), Iri(predicate), Iri(obj), Iri(graph));
    }

    /// <summary>A directory durability barrier that does nothing, so the store side does not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Wraps a materialised quad list as an async stream, so the engine ingests it through the streaming boundary exactly as a real streaming parser would.</summary>
    /// <param name="quads">The quads to yield.</param>
    /// <param name="cancellationToken">A token that aborts enumeration.</param>
    /// <returns>The quads, yielded asynchronously.</returns>
    private static async IAsyncEnumerable<Quad> ToAsync(IReadOnlyList<Quad> quads, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach(Quad quad in quads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return quad;
        }
    }

    /// <summary>Renders a solution sequence order-independently: each solution's bindings sorted within a line, then the lines sorted, so two sequences compare equal exactly when they hold the same solutions (regardless of row order).</summary>
    /// <param name="solutions">The solutions to render.</param>
    /// <returns>The canonical rendering.</returns>
    private static string Canonical(IReadOnlyList<SparqlSolution> solutions)
    {
        List<string> rows = new(solutions.Count);
        foreach(SparqlSolution solution in solutions)
        {
            List<string> cells = new(solution.Bindings.Count);
            foreach(SparqlBinding binding in solution.Bindings)
            {
                cells.Add($"{binding.Variable.Name}={binding.Value}");
            }

            cells.Sort(StringComparer.Ordinal);
            rows.Add(string.Join("|", cells));
        }

        rows.Sort(StringComparer.Ordinal);

        return string.Join("\n", rows);
    }

    /// <summary>Runs a SELECT and returns its canonical, order-independent rendering.</summary>
    /// <param name="database">The database to query.</param>
    /// <param name="select">The SELECT query text.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The canonical rendering of the result rows.</returns>
    private static async Task<string> SelectCanonicalAsync(VeritasEngine database, string select, CancellationToken cancellationToken)
    {
        VeritasQueryResult result = await database.QueryAsync(Utf8Strings.From(select), cancellationToken: cancellationToken).ConfigureAwait(false);

        return Canonical(result.Bindings!.Solutions);
    }

    [TestMethod]
    public async Task QuadStreamOpenMatchesTheListOpen()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;

        //Reasoning off, so the comparison is over the asserted data alone (dedup is still exercised by the
        //duplicate triples below); the reasoning wiring itself is shared by the list and stream cores.
        VeritasEngineOptions options = VeritasEngineOptions.Default with { Reasoning = null };

        //Default + two named graphs, terms shared across graphs, and duplicate triples to exercise dedup.
        List<DataTriple> defaultGraph =
        [
            Triple("alice", "knows", "bob"),
            Triple("bob", "knows", "carol"),
            Triple("alice", "type", "Person"),
            Triple("alice", "knows", "bob"),
        ];
        List<DataTriple> firstNamed =
        [
            Triple("alice", "type", "Person"),
            Triple("carol", "knows", "alice"),
            Triple("alice", "type", "Person"),
        ];
        List<DataTriple> secondNamed =
        [
            Triple("bob", "type", "Agent"),
            Triple("alice", "knows", "dave"),
            Triple("carol", "knows", "alice"),
        ];
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named =
        [
            (Iri("g1"), firstNamed),
            (Iri("g2"), secondNamed),
        ];

        //The quad stream is derived from the same list data, so the two opens see identical logical content.
        List<Quad> quads = [];
        foreach(DataTriple triple in defaultGraph)
        {
            quads.Add(new Quad(triple.Subject, (NamedNode)triple.Predicate, triple.Object));
        }

        foreach((RdfTerm name, IEnumerable<DataTriple> triples) in named)
        {
            foreach(DataTriple triple in triples)
            {
                quads.Add(new Quad(triple.Subject, (NamedNode)triple.Predicate, triple.Object, name));
            }
        }

        VeritasEngine listEngine = await VeritasEngine.OpenAsync(defaultGraph, named, options, cancellationToken).ConfigureAwait(false);
        await using var listScope = listEngine.ConfigureAwait(false);

        VeritasEngine streamEngine = await VeritasEngine.OpenAsync(ToAsync(quads, cancellationToken), options, cancellationToken).ConfigureAwait(false);
        await using var streamScope = streamEngine.ConfigureAwait(false);

        Assert.AreEqual(listEngine.Dictionary.Count, streamEngine.Dictionary.Count, "The list open and the stream open intern the same distinct terms.");

        const string DefaultQuery = "SELECT ?s ?p ?o WHERE { ?s ?p ?o }";
        string listDefault = await SelectCanonicalAsync(listEngine, DefaultQuery, cancellationToken).ConfigureAwait(false);
        Assert.IsGreaterThan(0, listDefault.Length, "The default graph is non-empty, so this is a real comparison.");
        Assert.AreEqual(listDefault, await SelectCanonicalAsync(streamEngine, DefaultQuery, cancellationToken).ConfigureAwait(false), "The default-graph contents match.");

        const string GraphQuery = "SELECT ?g ?s ?p ?o WHERE { GRAPH ?g { ?s ?p ?o } }";
        string listGraphs = await SelectCanonicalAsync(listEngine, GraphQuery, cancellationToken).ConfigureAwait(false);
        Assert.IsGreaterThan(0, listGraphs.Length, "The named graphs are non-empty, so this is a real comparison.");
        Assert.AreEqual(listGraphs, await SelectCanonicalAsync(streamEngine, GraphQuery, cancellationToken).ConfigureAwait(false), "The named-graph contents match.");

        Assert.IsTrue(await streamEngine.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}bob> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "A default-graph triple is served.");
        Assert.IsTrue(await streamEngine.AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g2> {{ <{Ex}bob> <{Ex}type> <{Ex}Agent> }} }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "A named-graph triple is served.");
    }

    [TestMethod]
    public async Task QuadStreamMutableOpenCommitsAndPersistsLikeTheListOpen()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        string storeDirectory = Directory.CreateTempSubdirectory("veritas-streamingest-mut-").FullName;
        try
        {
            List<Quad> seed =
            [
                DefaultQuad("alice", "knows", "bob"),
                NamedQuad("alice", "type", "Person", "g1"),
            ];
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync(ToAsync(seed, cancellationToken), cancellationToken: cancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);

                await mutable.UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}alice> <{Ex}knows> <{Ex}carol> }}"), cancellationToken: cancellationToken).ConfigureAwait(false);
                Assert.IsTrue(await mutable.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}carol> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The streamed mutable open reads its own committed write.");

                mutable.Persist(store);
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsTrue(await reopened.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}bob> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The streamed default triple survives persist and reopen.");
            Assert.IsTrue(await reopened.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}carol> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The committed insert survives persist and reopen.");
            Assert.IsTrue(await reopened.AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g1> {{ <{Ex}alice> <{Ex}type> <{Ex}Person> }} }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The streamed named graph survives persist and reopen.");
        }
        finally
        {
            Directory.Delete(storeDirectory, true);
        }
    }

    [TestMethod]
    public async Task QuadStreamHonorsTheDurableJournalOption()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        string root = Directory.CreateTempSubdirectory("veritas-streamingest-journal-").FullName;
        try
        {
            string storeDirectory = Path.Combine(root, "store");
            Directory.CreateDirectory(storeDirectory);
            string journalPath = Path.Combine(root, "journal", "dataset.journal");
            VeritasEngineOptions options = new() { DatasetJournalPath = journalPath };
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            List<Quad> seed =
            [
                DefaultQuad("alice", "knows", "bob"),
                NamedQuad("alice", "type", "Person", "g1"),
            ];

            //A streamed create over a durable journal lands its initial state durably; persist a generation.
            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync(ToAsync(seed, cancellationToken), options, cancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);

                mutable.Persist(store);
            }

            //Reopening the store folded through the durable journal round-trips the streamed initial state.
            {
                VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, options, cancellationToken).ConfigureAwait(false);
                await using var scope = reopened.ConfigureAwait(false);

                Assert.IsTrue(await reopened.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}bob> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The durable initial default triple round-trips.");
                Assert.IsTrue(await reopened.AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g1> {{ <{Ex}alice> <{Ex}type> <{Ex}Person> }} }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The durable initial named graph round-trips.");
            }

            //The streamed create overload refuses a journal path whose log already holds entries, exactly as the list overload does.
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(ToAsync(seed, cancellationToken), options, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task TheStreamIsEnumeratedExactlyOnce()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        VeritasEngineOptions options = VeritasEngineOptions.Default with { Reasoning = null };

        List<Quad> seed =
        [
            DefaultQuad("alice", "knows", "bob"),
            NamedQuad("alice", "type", "Person", "g1"),
        ];
        CountingAsyncEnumerable<Quad> counting = new(ToAsync(seed, cancellationToken));

        VeritasEngine engine = await VeritasEngine.OpenAsync(counting, options, cancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.AreEqual(1, counting.EnumerationCount, "The open enumerates the quad stream exactly once.");
        Assert.IsTrue(await engine.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}bob> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The default triple is served.");
        Assert.IsTrue(await engine.AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g1> {{ <{Ex}alice> <{Ex}type> <{Ex}Person> }} }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The named-graph triple is served.");
    }

    [TestMethod]
    public async Task ParserToPipeEndToEnd()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        string directory = Directory.CreateTempSubdirectory("veritas-streamingest-pipe-").FullName;
        try
        {
            string path = Path.Combine(directory, "data.trig");
            await File.WriteAllTextAsync(path, TriGDocument, cancellationToken).ConfigureAwait(false);

            //The real pipeline shape the CLI wires: FileStream -> PipeReader -> format reader -> engine.
            VeritasEngine engine = await VeritasEngine.OpenAsync(StreamTriGThroughPipeAsync(path, cancellationToken), cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var scope = engine.ConfigureAwait(false);

            Assert.IsTrue(await engine.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}alice> <{Ex}knows> <{Ex}bob> }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The default-graph triple parsed and streamed through the pipe.");
            Assert.IsTrue(await engine.AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g1> {{ <{Ex}carol> <{Ex}knows> <{Ex}dave> }} }}"), cancellationToken: cancellationToken).ConfigureAwait(false), "The named-graph triple parsed and streamed through the pipe.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Streams a TriG file through the real FileStream-to-PipeReader-to-reader pipeline, mirroring the CLI's streaming load.</summary>
    /// <param name="path">The TriG document path.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The document's quads.</returns>
    private static async IAsyncEnumerable<Quad> StreamTriGThroughPipeAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string baseIri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        DiagnosticBag diagnostics = new();
        using FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        await foreach(Quad quad in TurtleReader.ReadAsync(reader, TurtleSyntax.TriG, diagnostics, pool: null, baseIri: baseIri, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return quad;
        }

        Assert.IsFalse(diagnostics.HasErrors, "The TriG document parses without error.");
    }

    /// <summary>An async-enumerable decorator that counts how many times it is enumerated, for the enumerate-exactly-once pin.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    private sealed class CountingAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        /// <summary>The wrapped source.</summary>
        private IAsyncEnumerable<T> Inner { get; }

        /// <summary>The number of times an enumerator has been requested.</summary>
        public int EnumerationCount { get; private set; }

        /// <summary>Wraps a source enumerable.</summary>
        /// <param name="inner">The source to count enumerations of.</param>
        public CountingAsyncEnumerable(IAsyncEnumerable<T> inner)
        {
            Inner = inner;
        }

        /// <summary>Records the enumeration and returns the source's enumerator.</summary>
        /// <param name="cancellationToken">A token that aborts enumeration.</param>
        /// <returns>The source enumerator.</returns>
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            EnumerationCount++;

            return Inner.GetAsyncEnumerator(cancellationToken);
        }
    }
}
