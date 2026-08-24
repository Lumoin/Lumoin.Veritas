using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Execution.Streaming;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The operator battery: whole-plan streaming answers are multiset-identical to the materialising
/// executor across the composite operator set (bag semantics through Union/Join duplicates, OPTIONAL's
/// per-left-row decision, MINUS's disjoint-domain exception, an UNORDERED cross-operator composition so
/// interior-cursor coverage does not depend on the conformance corpus's operator mix); the dedup-trap shape
/// (DISTINCT under LIMIT) streams and yields exactly the off-mode window with the leaf stopping early; the
/// filter-aware cap terminates upstream production where the leaf row cap declines today; and the order
/// gate rolls a reordering window's subtree onto the materialise boundary (with its charges refunded)
/// instead of ever weakening the multiset contract.
/// </summary>
[TestClass]
internal sealed class StreamingOperatorTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The on-mode policy under test.</summary>
    private static SparqlEnginePolicy StreamingOn { get; } = new(PreferStreamingOperators: true);

    /// <summary>
    /// Whole-plan streaming multiset identity over an UNORDERED composition battery: Join, LeftJoin, Minus,
    /// Union, Distinct, Filter, Extend, and sub-SELECT in shapes chosen independently of the conformance
    /// corpus's operator mix, incl. duplicate-preserving unions and joins (bag semantics pinned by count
    /// agreement) and the §18.6 corners (a compatible-but-condition-failing OPTIONAL right, a
    /// disjoint-domain MINUS).
    /// </summary>
    [TestMethod]
    public async Task StreamedPlansMatchTheMaterialisedMultiset()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("a"), Iri("knows"), Iri("c")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
            new DataTriple(Iri("a"), Iri("likes"), Iri("b")),
            new DataTriple(Iri("c"), Iri("likes"), Iri("d")),
            new DataTriple(Iri("a"), Iri("name"), Iri("na")),
            new DataTriple(Iri("c"), Iri("name"), Iri("nc")),
            new DataTriple(Iri("x"), Iri("solo"), Iri("y")),
        ];

        string[] queries =
        [
            //Join with duplicates on both sides of the shared variable (bag counts must agree).
            "SELECT * WHERE { ?s :knows ?o . ?s :likes ?w }",
            //Join through separate groups (columnar off-mode route).
            "SELECT * WHERE { { ?s :knows ?o } { ?o :likes ?w } }",
            //OPTIONAL with a lifted condition that fails on a compatible right (the bare-left rule).
            "SELECT * WHERE { ?s :knows ?o OPTIONAL { ?o :likes ?w FILTER(?w = :d) } }",
            //MINUS with a disjoint-domain left row (kept) and overlapping rows (subtracted).
            "SELECT * WHERE { { ?s :knows ?o } UNION { ?x :solo ?y } MINUS { ?s :name ?n } }",
            //Union preserving duplicates across branches.
            "SELECT * WHERE { { ?s :knows ?o } UNION { ?s :knows ?o } }",
            //Distinct over a duplicate-producing union.
            "SELECT DISTINCT ?s WHERE { { ?s :knows ?o } UNION { ?s :likes ?w } }",
            //Filter + Extend composition over a join.
            "SELECT * WHERE { ?s :knows ?o . ?s :name ?n BIND(EXISTS { ?o :likes ?w } AS ?e) FILTER(?n != :missing) }",
            //Sub-SELECT (ToMultiSet) joined into the enclosing group.
            "SELECT * WHERE { ?s :name ?n { SELECT ?s WHERE { ?s :knows ?o } } }",
            //ORDER BY plan: the whole plan is a boundary; ordered identity.
            "SELECT * WHERE { ?s :knows ?o } ORDER BY ?s ?o",
        ];

        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        foreach(string query in queries)
        {
            AlgebraOperator algebra = Translate(query, pool);
            IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

            List<SparqlSolution> streamed = [];
            await foreach(SparqlSolution row in on.EvaluateStreamingAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false))
            {
                streamed.Add(row);
            }

            AssertSameMultiset(offRows, streamed, query);

            IReadOnlyList<SparqlSolution> onRows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
            AssertSameMultiset(offRows, onRows, query + " (materialising on-mode)");
        }
    }

    /// <summary>
    /// The dedup-trap streams correctly: <c>DISTINCT</c> under <c>LIMIT</c> yields exactly the off-mode
    /// window (the cap counts post-DISTINCT survivors by construction), and the leaf stops producing once
    /// the window fills — pinned on the leaf cursor's <see cref="SolutionCursor.RowsProduced"/> against a
    /// graph whose distinct prefix satisfies the window early.
    /// </summary>
    [TestMethod]
    public async Task DedupTrapWindowStreamsAndStopsTheLeafEarly()
    {
        //200 subjects x 3 duplicate-producing objects each; the first two subjects' rows are adjacent in
        //the leaf's order, so a window of 2 distinct subjects needs only a small leaf prefix.
        List<DataTriple> data = new(600);
        for(int subject = 0; subject < 200; subject++)
        {
            for(int fan = 0; fan < 3; fan++)
            {
                data.Add(new DataTriple(Iri($"s{subject:D3}"), Iri("p"), Iri($"o{subject:D3}_{fan}")));
            }
        }

        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT DISTINCT ?s WHERE { ?s :p ?o } LIMIT 2", pool);

        IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onRows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        AssertSameMultiset(offRows, onRows, "dedup-trap window");
        Assert.HasCount(2, onRows);

        //The cursor-level bounded-work pin: drain the same window shape as a compiled pipeline and assert
        //the LEAF handed out far fewer rows than the 600-row scan (batched-leaf bounds stated in units of
        //batches: the leaf may overshoot by up to one 1024-row batch, so the pin allows one batch).
        StreamingPipeline pipeline = StreamingPipeline.TryCompile(on, on.Machinery, algebra, TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        try
        {
            int emitted = 0;
            while(await pipeline.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                emitted++;
            }

            Assert.AreEqual(2, emitted);
            Assert.IsInstanceOfType<SliceCursor>(pipeline.Root, "the window must have compiled as a streaming slice over the order-preserving chain");
            Assert.IsLessThanOrEqualTo(1024, FindLeaf(pipeline).RowsProduced, $"the leaf produced {FindLeaf(pipeline).RowsProduced} rows; the window must terminate upstream production");
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The filter-aware cap: <c>LIMIT</c> over <c>FILTER</c> — where the leaf row cap declines today — stops
    /// the leaf early in the compiled pipeline, and the driver's interception answers identically to
    /// off-mode through the public surface.
    /// </summary>
    [TestMethod]
    public async Task FilterAwareCapStopsTheLeafWhereTheRowCapDeclines()
    {
        List<DataTriple> data = new(2000);
        for(int i = 0; i < 2000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D4}"), Iri("p"), Iri(i % 2 == 0 ? "even" : "odd")));
        }

        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :p ?v FILTER(?v = :even) } LIMIT 3", pool);

        IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onRows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        AssertSameMultiset(offRows, onRows, "filter-aware cap window");
        Assert.HasCount(3, onRows);

        StreamingPipeline pipeline = StreamingPipeline.TryCompile(on, on.Machinery, algebra, TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        try
        {
            int emitted = 0;
            while(await pipeline.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                emitted++;
            }

            Assert.AreEqual(3, emitted);
            Assert.IsInstanceOfType<SliceCursor>(pipeline.Root);
            Assert.IsLessThanOrEqualTo(1024, FindLeaf(pipeline).RowsProduced, $"the leaf produced {FindLeaf(pipeline).RowsProduced} rows; the cap must terminate upstream production");
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The order gate's own regression: a windowed REORDERING plan (LIMIT over a join) never compiles a
    /// streaming window — the Slice subtree rolls back onto the materialise boundary with its charges
    /// refunded — and the public-surface answers stay identical.
    /// </summary>
    [TestMethod]
    public async Task WindowedJoinDeclinesTheStreamingWindow()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
            new DataTriple(Iri("c"), Iri("knows"), Iri("d")),
        ];
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { { ?a :knows ?b } { ?b :knows ?c } } LIMIT 1", pool);

        CursorBudget budget = new(StreamingPipeline.MaxCursorDepth);
        StreamingPipeline pipeline = StreamingPipeline.TryCompile(on, on.Machinery, algebra, TermId.None, budget, existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        try
        {
            Assert.IsFalse(HasCursor<SliceCursor>(pipeline), "a window over a reordering chain must roll onto the materialise boundary");
            Assert.AreEqual(StreamingPipeline.MaxCursorDepth - pipeline.CursorCount, budget.Remaining, "the rolled-back subtree's charges must be refunded");
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }

        IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onRows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(offRows.Count, onRows);
        Assert.HasCount(1, onRows);
    }

    /// <summary>
    /// The bare-left rule's own killer: a right row COMPATIBLE with the streamed left row whose lifted
    /// condition FAILS must not suppress the bare left row — the decision is per left row over
    /// condition-SATISFYING merges only. The blind-certified row for the
    /// satisfied-on-compatibility mutation family.
    /// </summary>
    [TestMethod]
    public async Task OptionalBareLeftSurvivesConditionFailingRight()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("b"), Iri("likes"), Iri("e")),
        ];
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :knows ?o OPTIONAL { ?o :likes ?w FILTER(?w = :d) } }", pool);

        IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, offRows);
        Assert.IsFalse(offRows[0].TryGetValue(new SparqlVariable(Utf8Strings.From("w")), out _), "the bare left row leaves ?w unbound");

        List<SparqlSolution> streamed = [];
        await foreach(SparqlSolution row in on.EvaluateStreamingAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false))
        {
            streamed.Add(row);
        }

        Assert.HasCount(1, streamed, "the compatible-but-condition-failing right must not suppress the bare left row");
        Assert.IsFalse(streamed[0].TryGetValue(new SparqlVariable(Utf8Strings.From("w")), out _));
    }

    /// <summary>
    /// The streaming trace's completion walk: streamed operators emit <c>Streaming</c>-strategy events at
    /// pipeline completion carrying the rows they ACTUALLY produced — an abandoned window reports the
    /// early-terminated counts (the early-termination evidence), with per-operator RowsLeft/Right read off
    /// the child cursors.
    /// </summary>
    [TestMethod]
    public async Task StreamingTraceReportsActuallyProducedRows()
    {
        List<DataTriple> data = new(100);
        for(int i = 0; i < 100; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D3}"), Iri("p"), Iri(i % 2 == 0 ? "even" : "odd")));
        }

        TraceCollector collector = new();
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, executionTrace: collector.Handle, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :p ?v FILTER(?v = :even) } LIMIT 2", pool);

        List<SparqlSolution> streamed = [];
        await foreach(SparqlSolution row in on.EvaluateStreamingAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false))
        {
            streamed.Add(row);
        }

        Assert.HasCount(2, streamed);
        Assert.IsTrue(HasStreamingEvent(collector, SparqlExecutionOperator.Slice, rowsOut: 2), "the window must report the two rows it produced");
        Assert.IsTrue(HasStreamingEvent(collector, SparqlExecutionOperator.Filter, rowsOut: 2), "the filter must report the two survivors it handed to the window");
        Assert.IsTrue(HasStreamingOperator(collector, SparqlExecutionOperator.Bgp), "the leaf must report its early-terminated production");
    }

    /// <summary>Per-binding EXISTS probe pipelines emit NO per-cursor streaming events — an EXISTS-bearing FILTER over M rows contributes only the driver's own operator events.</summary>
    [TestMethod]
    public async Task ExistsProbePipelinesEmitNoPerCursorEvents()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
        ];
        TraceCollector collector = new();
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, executionTrace: collector.Handle, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?o :knows ?z } }", pool);
        IReadOnlyList<SparqlSolution> rows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, rows);
        foreach(SparqlExecutionTraceEvent evt in collector.Events)
        {
            Assert.AreNotEqual(SparqlExecutionStrategy.Streaming, evt.Strategy, $"the EXISTS probe must not emit per-cursor events (saw a streaming {evt.Operator}).");
        }
    }

    /// <summary>The interception's completion events land in the SPAWNING driver's correlation and sequence stream (spec 3.7's scope rule).</summary>
    [TestMethod]
    public async Task InterceptionEmitsIntoTheDriversStream()
    {
        List<DataTriple> data = new(50);
        for(int i = 0; i < 50; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D2}"), Iri("p"), Iri(i % 2 == 0 ? "even" : "odd")));
        }

        TraceCollector collector = new();
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :p ?v FILTER(?v = :even) } LIMIT 2", pool);
        Guid correlation = new("11111111-2222-3333-4444-555555555555");
        IReadOnlyList<SparqlSolution> rows = await on.EvaluateAsync(algebra, collector.Handle, correlation, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, rows);
        bool sawStreaming = false;
        long lastSequence = 0;
        foreach(SparqlExecutionTraceEvent evt in collector.Events)
        {
            Assert.AreEqual(correlation, evt.CorrelationId, "every event of the evaluation shares the driver's correlation id");
            Assert.IsGreaterThan(lastSequence, evt.SequenceNumber, "the sequence stays one monotonic stream across driver and pipeline events");
            lastSequence = evt.SequenceNumber;
            sawStreaming |= evt.Strategy == SparqlExecutionStrategy.Streaming;
        }

        Assert.IsTrue(sawStreaming, "the interception must emit its completion events into the driver's stream");
    }

    /// <summary>Whether a streaming event for the operator with the exact produced-row count was collected.</summary>
    /// <param name="collector">The collected events.</param>
    /// <param name="operator">The operator to find.</param>
    /// <param name="rowsOut">The required produced-row count.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasStreamingEvent(TraceCollector collector, SparqlExecutionOperator @operator, int rowsOut)
    {
        foreach(SparqlExecutionTraceEvent evt in collector.Events)
        {
            if(evt.Strategy == SparqlExecutionStrategy.Streaming && evt.Operator == @operator && evt.RowsOut == rowsOut)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any streaming event for the operator was collected.</summary>
    /// <param name="collector">The collected events.</param>
    /// <param name="operator">The operator to find.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasStreamingOperator(TraceCollector collector, SparqlExecutionOperator @operator)
    {
        foreach(SparqlExecutionTraceEvent evt in collector.Events)
        {
            if(evt.Strategy == SparqlExecutionStrategy.Streaming && evt.Operator == @operator)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collects execution-trace events through a bound method group (no closures).</summary>
    private sealed class TraceCollector
    {
        /// <summary>The collected events, in emission order.</summary>
        public List<SparqlExecutionTraceEvent> Events { get; } = [];

        /// <summary>Appends one emitted event.</summary>
        /// <param name="evt">The event.</param>
        public void Handle(in SparqlExecutionTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>Asserts two row sets are the same multiset (canonical order-insensitive row keys, sorted).</summary>
    /// <param name="expected">The expected rows.</param>
    /// <param name="actual">The actual rows.</param>
    /// <param name="label">The failing query's label.</param>
    private static void AssertSameMultiset(IReadOnlyList<SparqlSolution> expected, IReadOnlyList<SparqlSolution> actual, string label)
    {
        Assert.HasCount(expected.Count, actual, $"Row count for {label}.");
        List<string> expectedKeys = CanonicalKeys(expected);
        List<string> actualKeys = CanonicalKeys(actual);
        for(int i = 0; i < expectedKeys.Count; i++)
        {
            Assert.AreEqual(expectedKeys[i], actualKeys[i], $"Multiset divergence for {label}.");
        }
    }

    /// <summary>Builds the sorted canonical row keys of a row set (binding-order-insensitive).</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The sorted keys.</returns>
    private static List<string> CanonicalKeys(IReadOnlyList<SparqlSolution> rows)
    {
        List<string> keys = new(rows.Count);
        foreach(SparqlSolution row in rows)
        {
            List<string> parts = new(row.Bindings.Count);
            foreach(SparqlBinding binding in row.Bindings)
            {
                parts.Add($"{binding.Variable}={binding.Value}");
            }

            parts.Sort(StringComparer.Ordinal);
            keys.Add(string.Join("|", parts));
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>Finds the pipeline's leaf cursor (the single <see cref="BgpCursor"/> these shapes compile).</summary>
    /// <param name="pipeline">The compiled pipeline.</param>
    /// <returns>The leaf cursor.</returns>
    private static SolutionCursor FindLeaf(StreamingPipeline pipeline)
    {
        foreach(SolutionCursor cursor in pipeline.Cursors)
        {
            if(cursor is BgpCursor)
            {
                return cursor;
            }
        }

        throw new InvalidOperationException("The pipeline carries no BGP leaf cursor.");
    }

    /// <summary>Whether the pipeline holds a cursor of the given type.</summary>
    /// <typeparam name="T">The cursor type.</typeparam>
    /// <param name="pipeline">The compiled pipeline.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasCursor<T>(StreamingPipeline pipeline)
    {
        foreach(SolutionCursor cursor in pipeline.Cursors)
        {
            if(cursor is T)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Parses, normalizes, and translates a query (the shared example prefix prepended) to its algebra.</summary>
    /// <param name="text">The query text without the prefix.</param>
    /// <param name="pool">The pool the parse interns into.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> " + text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }
}
