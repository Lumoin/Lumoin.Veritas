using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Stage A pins for the algebra rewrite pipeline: the R1 transform-value pin (the applied replacement IS the
/// surviving operand — no corpus fixture exercises R1, so this pin carries its certification), single-step
/// idempotence, pipeline construction validation, the empty-pipeline short-circuit, exactly-once application
/// per evaluation entry (including the ASK and streaming fall-throughs that re-enter the materialising
/// core), fixpoint budget and pass semantics, the reference-equal-Applied defense, trace provenance with one
/// monotonic sequence, policy equality semantics, and answer identity through the engine.
/// </summary>
[TestClass]
internal sealed class AlgebraRewritePipelineTests
{
    /// <summary>The example namespace the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>R1's applied replacement IS the surviving operand, both orientations — the transform-value pin.</summary>
    [TestMethod]
    public void UnitJoinEliminationReplacesWithSurvivingOperand()
    {
        Bgp bgp = MakeBgp("s");
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        AlgebraRewriteOutcome leftUnit = AlgebraRewriteCatalog.UnitJoinElimination.Rule(new Join(new UnitTable(), bgp), in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, leftUnit.Application);
        Assert.AreSame(bgp, leftUnit.Algebra, "Join(UnitTable, A) must yield exactly A, not a rebuilt tree.");

        AlgebraRewriteOutcome rightUnit = AlgebraRewriteCatalog.UnitJoinElimination.Rule(new Join(bgp, new UnitTable()), in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, rightUnit.Application);
        Assert.AreSame(bgp, rightUnit.Algebra, "Join(A, UnitTable) must yield exactly A.");

        AlgebraRewriteOutcome untouched = AlgebraRewriteCatalog.UnitJoinElimination.Rule(new Join(bgp, MakeBgp("t")), in context);
        Assert.AreEqual(AlgebraRewriteApplication.NotApplicable, untouched.Application);
    }

    /// <summary>R1 is single-step idempotent: rewriting its own output applies nothing and returns the same instance.</summary>
    [TestMethod]
    public void UnitJoinEliminationIsSingleStepIdempotent()
    {
        Bgp bgp = MakeBgp("s");
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.UnitJoinElimination);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        AlgebraOperator once = pipeline.Rewrite(new Join(new UnitTable(), bgp), in context);
        Assert.AreSame(bgp, once);

        AlgebraOperator twice = pipeline.Rewrite(once, in context);
        Assert.AreSame(once, twice, "A second application must be NotApplicable end to end.");
    }

    /// <summary>Create validates its entries: duplicate names and null delegates throw; an empty list is the shared Empty instance.</summary>
    [TestMethod]
    public void CreateValidatesEntries()
    {
        AlgebraRewriteEntry rule = AlgebraRewriteCatalog.UnitJoinElimination;

        Assert.ThrowsExactly<ArgumentException>(() => AlgebraRewritePipeline.Create(rule, rule));
        Assert.ThrowsExactly<ArgumentException>(() => AlgebraRewritePipeline.Create(new AlgebraRewriteEntry("null-rule", null!, Fixpoint: false)));
        Assert.ThrowsExactly<ArgumentException>(() => AlgebraRewritePipeline.Create(new AlgebraRewriteEntry("", AbstainOnBgp, Fixpoint: false)));
        Assert.AreSame(AlgebraRewritePipeline.Empty, AlgebraRewritePipeline.Create());
        Assert.AreSame(AlgebraRewritePipeline.Empty, AlgebraRewritePipeline.Default, "Every catalog rule is default-off, so the default pipeline is empty.");
    }

    /// <summary>An empty pipeline short-circuits: the input returns by reference and no trace event is emitted.</summary>
    [TestMethod]
    public async Task EmptyPipelineShortCircuits()
    {
        Bgp bgp = MakeBgp("s");
        Join root = new(new UnitTable(), bgp);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);
        Assert.AreSame(root, AlgebraRewritePipeline.Empty.Rewrite(root, in context));

        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b"))],
            executionTrace: handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        _ = await engine.EvaluateAsync(Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o }", pool), AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, CountRewriteEvents(events), "The empty pipeline must emit no rewrite events.");
        Assert.IsNotEmpty(events, "The evaluation itself still traces its operators.");
    }

    /// <summary>
    /// The pass applies exactly once per public evaluation entry — including the ASK fall-through and the
    /// streaming not-streamable fallback, which re-enter the materialising core, never a public entry. An
    /// abstaining tracer rule makes double application visible as a doubled event count.
    /// </summary>
    [TestMethod]
    public async Task RewriteAppliesExactlyOncePerEvaluationEntry()
    {
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b"))],
            executionTrace: handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraRewritePipeline tracer = AlgebraRewritePipeline.Create(new AlgebraRewriteEntry("abstain-on-bgp", AbstainOnBgp, Fixpoint: false));

        //One BGP in the plan: exactly one Abstained event per entry when the pass runs once.
        AlgebraOperator select = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o }", pool);
        IReadOnlyList<SparqlSolution> rows = await engine.EvaluateAsync(select, tracer, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, rows);
        Assert.AreEqual(1, CountRewriteEvents(events), "EvaluateAsync must apply the pass exactly once.");

        //A non-bare-BGP ASK falls through to the materialising core — the pass must not re-apply there.
        events.Clear();
        AlgebraOperator ask = Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?o FILTER(BOUND(?s)) }", pool);
        Assert.IsTrue(await engine.EvaluateAskAsync(ask, tracer, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1, CountRewriteEvents(events), "The ASK fall-through must not re-apply the pass.");

        //A non-streamable shape drains the streaming entry's materialising fallback — likewise once.
        events.Clear();
        AlgebraOperator distinct = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?s WHERE { ?s :knows ?o FILTER(BOUND(?o)) }", pool);
        int streamed = 0;
        await foreach(SparqlSolution _ in engine.EvaluateStreamingAsync(distinct, tracer, TestContext.CancellationToken).ConfigureAwait(false))
        {
            streamed++;
        }

        Assert.AreEqual(1, streamed);
        Assert.AreEqual(1, CountRewriteEvents(events), "The streaming not-streamable fallback must not re-apply the pass.");
    }

    /// <summary>
    /// Fixpoint semantics: an always-applying rule stops at the pass budget (free soundness), and a
    /// non-fixpoint rule runs in pass zero only.
    /// </summary>
    [TestMethod]
    public async Task FixpointStopsAtBudgetAndNonFixpointRulesRunOnce()
    {
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b"))],
            executionTrace: handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(
            new AlgebraRewriteEntry("abstain-on-unit", AbstainOnUnit, Fixpoint: false),
            new AlgebraRewriteEntry("respin-unit", RespinUnit, Fixpoint: true));

        //A UnitTable root: the respin rule replaces it with a fresh instance every pass, so only the
        //MaxRewritePasses budget stops the loop; the evaluation then answers the single empty solution.
        IReadOnlyList<SparqlSolution> rows = await engine.EvaluateAsync(new UnitTable(), pipeline, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, rows);

        int applied = 0;
        int abstained = 0;
        int maxPass = -1;
        foreach(SparqlExecutionTraceEvent traceEvent in events)
        {
            if(traceEvent.Kind != SparqlExecutionEventKind.RewriteApplied)
            {
                continue;
            }

            maxPass = Math.Max(maxPass, traceEvent.RewritePass);
            if(traceEvent.RewriteApplication == AlgebraRewriteApplication.Applied)
            {
                applied++;
            }
            else
            {
                abstained++;
                Assert.AreEqual(0, traceEvent.RewritePass, "A non-fixpoint rule must run in pass zero only.");
            }
        }

        Assert.AreEqual(AlgebraRewritePipeline.MaxRewritePasses, applied, "One application per pass, capped by the budget.");
        Assert.AreEqual(1, abstained);
        Assert.AreEqual(AlgebraRewritePipeline.MaxRewritePasses - 1, maxPass);
    }

    /// <summary>A rule returning Applied with the same instance is defused: no dirty pass, no event, no spin.</summary>
    [TestMethod]
    public void ReferenceEqualAppliedIsDefused()
    {
        Bgp bgp = MakeBgp("s");
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(new AlgebraRewriteEntry("apply-same", ApplySame, Fixpoint: true));
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        Assert.AreSame(bgp, pipeline.Rewrite(bgp, in context), "A reference-equal Applied must count as NotApplicable.");

        //The defense is also trace-silent: a defused application emits nothing and runs one pass only.
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlExecutionTrace trace = new(handler, correlationId: default, TimeProvider.System);
        Assert.AreSame(bgp, pipeline.Rewrite(bgp, in context, trace));
        Assert.AreEqual(0, CountRewriteEvents(events), "A defused Applied must not reach the trace.");
    }

    /// <summary>
    /// Node-level chaining and one monotonic sequence (H8): a later rule sees an earlier rule's replacement at
    /// the same position, and rewrite events interleave with operator events under strictly increasing
    /// sequence numbers.
    /// </summary>
    [TestMethod]
    public async Task RulesChainInOrderAndSequenceIsMonotonic()
    {
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b"))],
            executionTrace: handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(
            AlgebraRewriteCatalog.UnitJoinElimination,
            new AlgebraRewriteEntry("abstain-on-bgp", AbstainOnBgp, Fixpoint: false));

        //R1 collapses Join(UnitTable, bgp) to the BGP at that position; the abstainer, later in the list,
        //then sees the BGP at the SAME position — the chaining contract. The leaf position abstains too.
        Bgp bgp = MakeBgp("s");
        IReadOnlyList<SparqlSolution> rows = await engine.EvaluateAsync(new Join(new UnitTable(), bgp), pipeline, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, rows);

        int applied = 0;
        int abstained = 0;
        long previousSequence = long.MinValue;
        foreach(SparqlExecutionTraceEvent traceEvent in events)
        {
            Assert.IsGreaterThan(previousSequence, traceEvent.SequenceNumber, "One evaluation carries one strictly monotonic sequence.");
            previousSequence = traceEvent.SequenceNumber;
            if(traceEvent.Kind != SparqlExecutionEventKind.RewriteApplied)
            {
                continue;
            }

            if(traceEvent.RewriteApplication == AlgebraRewriteApplication.Applied)
            {
                applied++;
                Assert.AreEqual("unit-join-elimination", traceEvent.Label);
                Assert.AreEqual(SparqlExecutionOperator.Join, traceEvent.Operator, "The rewrite event reports the REPLACED position's operator.");
            }
            else
            {
                abstained++;
                Assert.AreEqual("abstain-on-bgp", traceEvent.Label);
            }
        }

        Assert.AreEqual(1, applied);
        Assert.AreEqual(2, abstained, "The leaf BGP and the chained replacement position both abstain in pass zero.");
    }

    /// <summary>Policy equality is reference-based on the pipeline member (H6), and the default policy resolves to no rewriting.</summary>
    [TestMethod]
    public void PolicyEqualityIsReferenceBasedOnThePipeline()
    {
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.UnitJoinElimination);

        Assert.AreEqual(new SparqlEnginePolicy(Rewrites: pipeline), new SparqlEnginePolicy(Rewrites: pipeline));
        Assert.AreNotEqual(
            new SparqlEnginePolicy(Rewrites: AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.UnitJoinElimination)),
            new SparqlEnginePolicy(Rewrites: AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.UnitJoinElimination)),
            "Two equal-content pipelines are distinct references; policy equality does not look inside.");
        Assert.AreEqual(default, SparqlEnginePolicy.Default);
    }

    /// <summary>R1 through the whole engine is answer-identical to no rewriting, on rows and on ASK (H3 smoke: the output evaluates under GuardSupported).</summary>
    [TestMethod]
    public async Task UnitJoinAnswerIdentityThroughEngine()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b")), new DataTriple(Iri("b"), Iri("knows"), Iri("c"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.UnitJoinElimination);
        Bgp bgp = MakeBgp("s");
        Join root = new(new UnitTable(), bgp);

        IReadOnlyList<SparqlSolution> baseline = await engine.EvaluateAsync(root, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> rewritten = await engine.EvaluateAsync(root, pipeline, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(baseline.Count, rewritten);
        Assert.IsTrue(await engine.EvaluateAskAsync(root, pipeline, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// EXISTS plan builds run under the evaluation's pipeline (the registry-carried frame): a rewriting
    /// evaluation with an EXISTS site answers identically to the un-rewritten baseline.
    /// </summary>
    [TestMethod]
    public async Task ExistsPlanBuildRunsUnderTheEvaluationPipeline()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b")), new DataTriple(Iri("b"), Iri("knows"), Iri("c"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?o :knows ?b } }", pool);
        AlgebraRewritePipeline pipeline = AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.UnitJoinElimination);

        IReadOnlyList<SparqlSolution> baseline = await engine.EvaluateAsync(algebra, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> rewritten = await engine.EvaluateAsync(algebra, pipeline, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, baseline);
        Assert.HasCount(baseline.Count, rewritten);
    }

    /// <summary>Counts the rewrite-kind events in a collected stream.</summary>
    /// <param name="events">The collected events.</param>
    /// <returns>The number of rewrite events.</returns>
    private static int CountRewriteEvents(List<SparqlExecutionTraceEvent> events)
    {
        int count = 0;
        foreach(SparqlExecutionTraceEvent traceEvent in events)
        {
            if(traceEvent.Kind == SparqlExecutionEventKind.RewriteApplied)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>A tracer rule: abstains at every BGP position, touching nothing — double application doubles its event count.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused).</param>
    /// <returns>Abstained on a BGP, else not-applicable.</returns>
    private static AlgebraRewriteOutcome AbstainOnBgp(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node is Bgp ? AlgebraRewriteOutcome.Abstained(node) : AlgebraRewriteOutcome.NotApplicable(node);
    }

    /// <summary>A tracer rule: abstains at every unit-table position.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused).</param>
    /// <returns>Abstained on a unit table, else not-applicable.</returns>
    private static AlgebraRewriteOutcome AbstainOnUnit(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node is UnitTable ? AlgebraRewriteOutcome.Abstained(node) : AlgebraRewriteOutcome.NotApplicable(node);
    }

    /// <summary>A deliberately non-converging rule: replaces every unit table with a fresh (value-equal, reference-distinct) instance, so only the pass budget stops it.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused).</param>
    /// <returns>A fresh unit table on a unit table, else not-applicable.</returns>
    private static AlgebraRewriteOutcome RespinUnit(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node is UnitTable ? AlgebraRewriteOutcome.Applied(new UnitTable()) : AlgebraRewriteOutcome.NotApplicable(node);
    }

    /// <summary>A defective rule shape the pipeline must defuse: claims Applied while returning the same instance.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused).</param>
    /// <returns>Applied with the unchanged input.</returns>
    private static AlgebraRewriteOutcome ApplySame(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return AlgebraRewriteOutcome.Applied(node);
    }

    /// <summary>Parses, normalizes, and translates a query to algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The pool owning the parsed strings.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Builds a single-triple BGP over three fresh variables, the subject named as given.</summary>
    /// <param name="subjectName">The subject variable name.</param>
    /// <returns>The BGP.</returns>
    private static Bgp MakeBgp(string subjectName)
    {
        return new([new TriplePattern(default, Var(subjectName), Var("p"), Var("o"))]);
    }

    /// <summary>Builds a variable term with the given name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable term.</returns>
    private static VariableTerm Var(string name)
    {
        return new VariableTerm(default, new SparqlVariable(Utf8Strings.From(name)));
    }

    /// <summary>Builds an example-namespace IRI node from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The IRI node.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }
}
