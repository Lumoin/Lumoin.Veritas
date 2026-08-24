using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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
/// Tests for the streaming operator pipeline's first slice: the <c>ASK</c> route through
/// <see cref="StreamingPipeline"/> agrees with the materialising executor on every shape (streamable leaves,
/// per-solution rewrites, the type-expansion ladder, and materialise-boundary fallbacks), the compiler charges
/// and honours the cumulative cursor budget, disposal is exactly-once and safe on partial advance, the leaf
/// cursor re-arms through <see cref="SolutionCursor.ResetAsync"/>, and the nested-<c>EXISTS</c> driver
/// re-entry — the depth the cursor-budget argument leans on — completes synchronously under a constrained
/// stack.
/// </summary>
[TestClass]
internal sealed class StreamingPipelineTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The nested-<c>EXISTS</c> depth the constrained-stack pin drives — the initial value of the uniform nesting cap the spec commits (the corpus's measured real maximum is 1).</summary>
    private const int NestedExistsDepth = 16;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The on-mode policy under test.</summary>
    private static SparqlEnginePolicy StreamingOn { get; } = new(PreferStreamingOperators: true);

    /// <summary>The <c>rdf:type</c> IRI for the expansion-ladder data.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>
    /// <c>ASK</c> answers identically with the streaming pipeline on and off across the Stage A shape set: a
    /// matching and a non-matching bare BGP, an unencodable constant, a self-join-equality pattern (matching
    /// only a genuine self-loop), a multi-pattern BGP, an inline <c>VALUES</c> block, and the
    /// materialise-boundary shapes (<c>FILTER</c>, <c>UNION</c>).
    /// </summary>
    [TestMethod]
    public async Task AskAgreesAcrossModesOnStreamableAndBoundaryShapes()
    {
        (string Query, bool Expected)[] cases =
        [
            ("ASK { ?s :knows ?o }", true),
            ("ASK { ?s :absent ?o }", false),
            ("ASK { ?x :p ?x }", true),
            ("ASK { ?x :knows ?x }", false),
            ("ASK { ?s :knows ?o . ?o :knows ?z }", true),
            ("ASK { VALUES ?x { 1 } }", true),
            ("ASK { ?s :knows ?o FILTER(?s = :a) }", true),
            ("ASK { ?s :knows ?o FILTER(?s = :nobody) }", false),
            ("ASK { { ?s :absent ?o } UNION { ?s :knows ?o } }", true),
        ];

        SparqlQueryEngine off = await EngineAsync(SparqlEnginePolicy.Default).ConfigureAwait(false);
        SparqlQueryEngine on = await EngineAsync(StreamingOn).ConfigureAwait(false);
        Assert.IsTrue(on.EnginePolicy.PreferStreamingOperators);
        Assert.IsFalse(off.EnginePolicy.PreferStreamingOperators);

        using Utf8StringPool pool = new();
        foreach((string query, bool expected) in cases)
        {
            AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> " + query, pool);
            bool offAnswer = await off.EvaluateAskAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
            bool onAnswer = await on.EvaluateAskAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(expected, offAnswer, $"Off-mode answer for '{query}'.");
            Assert.AreEqual(offAnswer, onAnswer, $"On/off divergence for '{query}'.");
        }
    }

    /// <summary>
    /// The streaming BGP cursor walks the <c>rdf:type</c> expansion ladder exactly as the materialising leaf: a
    /// subclass-only-typed instance answers an <c>ASK</c> on the superclass in both modes, and a class the
    /// expansion does not widen stays unmatched in both.
    /// </summary>
    [TestMethod]
    public async Task AskOnModeWalksTypeExpansionLadder()
    {
        //The subclass axiom interns :Animal into the dictionary — a class absent from the data graph is
        //unencodable and matches nothing before the expansion seam is ever consulted.
        DataTriple[] data =
        [
            new DataTriple(Iri("rex"), new NamedNode(Utf8Strings.From(RdfType)), Iri("Dog")),
            new DataTriple(Iri("Dog"), Iri("subClassOf"), Iri("Animal")),
        ];
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, typeExpansion: ExpandAnimalToDog, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, typeExpansion: ExpandAnimalToDog, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator superclass = Translate("PREFIX : <http://example.org/> ASK { ?x a :Animal }", pool);
        AlgebraOperator unrelated = Translate("PREFIX : <http://example.org/> ASK { ?x a :Plant }", pool);

        Assert.IsTrue(await off.EvaluateAskAsync(superclass, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await on.EvaluateAskAsync(superclass, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await off.EvaluateAskAsync(unrelated, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await on.EvaluateAskAsync(unrelated, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The pipeline compiler charges the evaluation's shared budget cell one unit per constructed cursor,
    /// declines (value-based, <see langword="null"/>) when the remaining budget cannot afford the
    /// compilation, and refunds a declined compile's charges (nothing lives).
    /// </summary>
    [TestMethod]
    public async Task TryCompileChargesAndHonoursTheCursorBudget()
    {
        SparqlQueryEngine engine = await EngineAsync(StreamingOn).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator bgp = FindBgp(Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o }", pool));

        CursorBudget budget = new(StreamingPipeline.MaxCursorDepth);
        StreamingPipeline? compiled = StreamingPipeline.TryCompile(engine, engine.Machinery, bgp, TermId.None, budget, existsDepth: 0, AlgebraRewritePipeline.Empty);
        Assert.IsNotNull(compiled);
        Assert.AreEqual(1, compiled.CursorCount);
        Assert.AreEqual(StreamingPipeline.MaxCursorDepth - 1, budget.Remaining);
        await compiled.DisposeAsync().ConfigureAwait(false);

        CursorBudget exhausted = new(0);
        StreamingPipeline? declined = StreamingPipeline.TryCompile(engine, engine.Machinery, bgp, TermId.None, exhausted, existsDepth: 0, AlgebraRewritePipeline.Empty);
        Assert.IsNull(declined);
        Assert.AreEqual(0, exhausted.Remaining);
    }

    /// <summary>
    /// Pipeline teardown is exactly-once and safe on a partially-advanced chain: one pull then two disposals do
    /// not throw, and a never-pulled pipeline (whose cursor opened no sources) disposes trivially.
    /// </summary>
    [TestMethod]
    public async Task DisposeIsExactlyOnceAndSafeOnPartialAdvance()
    {
        SparqlQueryEngine engine = await EngineAsync(StreamingOn).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o }", pool);

        StreamingPipeline advanced = StreamingPipeline.TryCompile(engine, engine.Machinery, FindBgp(algebra), TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        Assert.IsTrue(await advanced.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1, advanced.Root.RowsProduced);
        await advanced.DisposeAsync().ConfigureAwait(false);
        await advanced.DisposeAsync().ConfigureAwait(false);

        StreamingPipeline unpulled = StreamingPipeline.TryCompile(engine, engine.Machinery, FindBgp(algebra), TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        await unpulled.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="SolutionCursor.ResetAsync"/> re-arms the BGP cursor from scratch: after a full drain, a reset
    /// with the empty pre-binding drains the same row count again and the per-binding counter starts afresh.
    /// </summary>
    [TestMethod]
    public async Task ResetRearmsTheBgpCursorAndCountsAfresh()
    {
        SparqlQueryEngine engine = await EngineAsync(StreamingOn).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o }", pool);

        StreamingPipeline pipeline = StreamingPipeline.TryCompile(engine, engine.Machinery, FindBgp(algebra), TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        try
        {
            int first = await DrainAsync(pipeline.Root, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(2, first);
            Assert.AreEqual(2, pipeline.Root.RowsProduced);

            await pipeline.Root.ResetAsync(new SparqlSolution([])).ConfigureAwait(false);
            Assert.AreEqual(0, pipeline.Root.RowsProduced);

            int second = await DrainAsync(pipeline.Root, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(first, second);
            Assert.AreEqual(2, pipeline.Root.RowsProduced);
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The nested-<c>EXISTS</c> driver re-entry — the multiplier in the cursor-budget stack argument — runs to
    /// completion SYNCHRONOUSLY on a 1 MiB thread at the committed cap depth, in both modes. The synchronous
    /// completion is asserted, not assumed: were the in-memory evaluation to suspend, the constrained thread
    /// would no longer bound the stack and this pin would say so.
    /// </summary>
    [TestMethod]
    public async Task NestedExistsCompletesUnderConstrainedStack()
    {
        StringBuilder query = new("PREFIX : <http://example.org/> ASK { ?s :knows ?o ");
        for(int depth = 0; depth < NestedExistsDepth; depth++)
        {
            query.Append("FILTER(EXISTS { ?s :knows ?o ");
        }

        for(int depth = 0; depth < NestedExistsDepth; depth++)
        {
            query.Append("}) ");
        }

        query.Append('}');
        string text = query.ToString();

        await RunOnConstrainedStackAsync(text, SparqlEnginePolicy.Default, TestContext.CancellationToken).ConfigureAwait(false);
        await RunOnConstrainedStackAsync(text, StreamingOn, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates the nested-<c>EXISTS</c> <c>ASK</c> on a dedicated 1 MiB-stack thread and asserts it completes synchronously and answers <see langword="true"/>. The thread runs in the background and its completion signal is awaited bound only by <paramref name="cancellationToken"/>, so a genuinely hung evaluation surfaces at the runner-level hang guard without pinning the process.</summary>
    /// <param name="queryText">The nested-<c>EXISTS</c> query text.</param>
    /// <param name="enginePolicy">The engine policy the run builds under.</param>
    /// <param name="cancellationToken">The test cancellation token bounding the wait.</param>
    /// <returns>A task completing when the run has been asserted.</returns>
    private static async Task RunOnConstrainedStackAsync(string queryText, SparqlEnginePolicy enginePolicy, CancellationToken cancellationToken)
    {
        ConstrainedStackRun run = new(queryText, enginePolicy);
        Thread thread = new(run.Execute, 1 << 20) { IsBackground = true };
        thread.Start();
        await run.Finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        thread.Join();
        Assert.IsNull(run.Failure, run.Failure?.ToString());
        Assert.IsTrue(run.CompletedSynchronously, "The in-memory evaluation suspended; the constrained thread no longer bounds the stack and the headroom premise needs re-deriving.");
        Assert.IsTrue(run.Answer, "The nested-EXISTS ASK answered false.");
    }

    /// <summary>The constrained-stack run's explicit state (the thread body is a bound method group, not a closure).</summary>
    private sealed class ConstrainedStackRun
    {
        private readonly string queryText;

        private readonly SparqlEnginePolicy enginePolicy;

        /// <summary>Constructs the run state.</summary>
        /// <param name="queryText">The query to evaluate.</param>
        /// <param name="enginePolicy">The engine policy the run builds under.</param>
        public ConstrainedStackRun(string queryText, SparqlEnginePolicy enginePolicy)
        {
            this.queryText = queryText;
            this.enginePolicy = enginePolicy;
        }

        /// <summary>Whether the whole evaluation completed synchronously on the constrained thread.</summary>
        public bool CompletedSynchronously { get; private set; }

        /// <summary>The <c>ASK</c> answer.</summary>
        public bool Answer { get; private set; }

        /// <summary>The failure that aborted the run, or <see langword="null"/>.</summary>
        public Exception? Failure { get; private set; }

        /// <summary>Completed as the thread body's last act, after every result field is written — the signal the test awaits bound only by its cancellation token.</summary>
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// The thread body: starts the async evaluation and records whether its task was ALREADY COMPLETE when
        /// the call returned — the sync-completion observation. Under the in-memory sync-completion premise the
        /// whole evaluation ran on this constrained thread before the call returned; results were stored by the
        /// async body itself, so nothing here blocks on a task (a suspension fails the pin via
        /// <see cref="CompletedSynchronously"/> and the stray continuation only writes fields this run no
        /// longer reads).
        /// </summary>
        public void Execute()
        {
            Task run = ExecuteAsync();
            CompletedSynchronously = run.IsCompleted;
            Finished.TrySetResult();
        }

        /// <summary>Builds the engine, evaluates the <c>ASK</c>, and stores the answer; every failure lands in <see cref="Failure"/>.</summary>
        /// <returns>The asynchronous run.</returns>
        private async Task ExecuteAsync()
        {
            try
            {
                SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
                    [new DataTriple(new NamedNode(Utf8Strings.From(Ex + "a")), new NamedNode(Utf8Strings.From(Ex + "knows")), new NamedNode(Utf8Strings.From(Ex + "b")))],
                    enginePolicy: enginePolicy).ConfigureAwait(false);
                using Utf8StringPool pool = new();
                AlgebraOperator algebra = Translate(queryText, pool);

                Answer = await engine.EvaluateAskAsync(algebra, CancellationToken.None).ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                Failure = ex;
            }
        }
    }

    /// <summary>Pulls a cursor to exhaustion and counts the rows.</summary>
    /// <param name="cursor">The cursor to drain.</param>
    /// <param name="cancellationToken">The pull token.</param>
    /// <returns>The number of rows produced.</returns>
    private static async Task<int> DrainAsync(SolutionCursor cursor, CancellationToken cancellationToken)
    {
        int rows = 0;
        while(await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            rows++;
        }

        return rows;
    }

    /// <summary>Finds the BGP leaf of a translated SELECT plan (the pipeline compiles the leaf directly in these tests).</summary>
    /// <param name="algebra">The translated plan.</param>
    /// <returns>The BGP leaf.</returns>
    private static Bgp FindBgp(AlgebraOperator algebra)
    {
        foreach(AlgebraOperator op in AlgebraWalker.Traverse(algebra))
        {
            if(op is Bgp bgp)
            {
                return bgp;
            }
        }

        throw new InvalidOperationException("The plan carries no BGP leaf.");
    }

    /// <summary>The type-expansion seam mapping the Animal class onto itself plus Dog (the subclass ladder), and every other class onto itself.</summary>
    /// <param name="classIri">The bound class IRI.</param>
    /// <returns>The expansion classes.</returns>
    private static IReadOnlyCollection<Utf8String> ExpandAnimalToDog(Utf8String classIri)
    {
        return classIri.Equals(Utf8Strings.From(Ex + "Animal"))
            ? [Utf8Strings.From(Ex + "Animal"), Utf8Strings.From(Ex + "Dog")]
            : [classIri];
    }

    /// <summary>Builds an engine over the shared example graph: two knows-edges and one genuine self-loop.</summary>
    /// <param name="enginePolicy">The engine policy to build under.</param>
    /// <returns>The engine.</returns>
    private static async Task<SparqlQueryEngine> EngineAsync(SparqlEnginePolicy enginePolicy)
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
            new DataTriple(Iri("loop"), Iri("p"), Iri("loop")),
        ];

        return await SparqlQueryEngine.BuildAsync(data, enginePolicy: enginePolicy).ConfigureAwait(false);
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Parses, normalizes, and translates a query to its algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The pool the parse interns into.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }
}
