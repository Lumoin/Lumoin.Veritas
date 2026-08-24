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
/// Pins for the catalog rules R2-R5, implementing the independently derived ground-truth
/// rows: the slice-fusion arithmetic incl. the
/// finite-limit trap and saturation, distinct idempotence over both redex forms, the parent-keyed no-op
/// projection collapse with its terminator and narrowing declines, the restricted empty-table annihilation
/// with its error-free allowlist, and per-rule answer identity through the engine.
/// </summary>
[TestClass]
internal sealed class AlgebraRewriteCatalogTests
{
    /// <summary>The example namespace the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The full catalog in order — the fourth arm's pipeline, shared by the engine-identity pins.</summary>
    private static AlgebraRewritePipeline FullCatalog { get; } = AlgebraRewritePipeline.Create(
        AlgebraRewriteCatalog.UnitJoinElimination,
        AlgebraRewriteCatalog.SliceFusion,
        AlgebraRewriteCatalog.DistinctIdempotence,
        AlgebraRewriteCatalog.NoopProjectCollapse,
        AlgebraRewriteCatalog.EmptyTableAnnihilation);

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>GT-R2-1..7: the certified fusion arithmetic — both-present, exhausted-inner, null handling, the finite-limit trap, and offset saturation.</summary>
    [TestMethod]
    public void SliceFusionArithmeticMatchesTheCertifiedRows()
    {
        AssertFusion(inner: (2, 10), outer: (3, 4), expected: (5, 4));
        AssertFusion(inner: (1, 3), outer: (5, 2), expected: (6, 0));
        AssertFusion(inner: (4, null), outer: (2, 6), expected: (6, 6));
        AssertFusion(inner: (3, 9), outer: (4, null), expected: (7, 5));
        AssertFusion(inner: (2, null), outer: (3, null), expected: (5, null));
        AssertFusion(inner: (2147483646, null), outer: (5, null), expected: (2147483647, null));
        AssertFusion(inner: (0, 4), outer: (0, 4), expected: (0, 4));
    }

    /// <summary>GT-R2-D: nested and fused windows select the same rows over the same base subtree, end to end.</summary>
    [TestMethod]
    public async Task SliceFusionIsAnswerIdenticalThroughTheEngine()
    {
        List<DataTriple> data = [];
        for(int i = 0; i < 9; i++)
        {
            data.Add(new DataTriple(Iri($"m{i}"), Iri("p"), Iri("v")));
        }

        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Bgp bgp = MakeBgp();
        Slice nested = new(new Slice(bgp, Offset: 1, Limit: 6), Offset: 3, Limit: 4);

        IReadOnlyList<SparqlSolution> baseline = await engine.EvaluateAsync(nested, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> fused = await engine.EvaluateAsync(nested, FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(3, baseline);
        Assert.HasCount(baseline.Count, fused);
        for(int i = 0; i < baseline.Count; i++)
        {
            Assert.HasCount(baseline[i].Bindings.Count, fused[i].Bindings);
        }
    }

    /// <summary>GT-R3-1/2/5: both distinct redexes absorb; the reversed shape is untouched.</summary>
    [TestMethod]
    public void DistinctIdempotenceAbsorbsBothRedexForms()
    {
        Bgp bgp = MakeBgp();
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        Distinct tower = new(new Distinct(bgp));
        AlgebraRewriteOutcome towerOutcome = AlgebraRewriteCatalog.DistinctIdempotence.Rule(tower, in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, towerOutcome.Application);
        Assert.AreSame(tower.Input, towerOutcome.Algebra, "The inner Distinct instance survives.");

        Distinct reducedForm = new(new Reduced(bgp));
        AlgebraRewriteOutcome reducedOutcome = AlgebraRewriteCatalog.DistinctIdempotence.Rule(reducedForm, in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, reducedOutcome.Application);
        Distinct absorbed = Assert.IsInstanceOfType<Distinct>(reducedOutcome.Algebra);
        Assert.AreSame(bgp, absorbed.Input, "Distinct(Reduced(X)) becomes Distinct(X) over the SAME X.");

        Reduced reversed = new(new Distinct(bgp));
        Assert.AreEqual(AlgebraRewriteApplication.NotApplicable, AlgebraRewriteCatalog.DistinctIdempotence.Rule(reversed, in context).Application);
    }

    /// <summary>GT-R3-1/2 through the engine: a duplicate-bearing input dedups identically with and without the rule.</summary>
    [TestMethod]
    public async Task DistinctIdempotenceIsAnswerIdenticalThroughTheEngine()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("m"), Iri("p"), Iri("v"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Bgp bgp = MakeBgp();
        Union duplicating = new(bgp, MakeBgp());
        Distinct tower = new(new Distinct(duplicating));

        IReadOnlyList<SparqlSolution> baseline = await engine.EvaluateAsync(tower, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> rewritten = await engine.EvaluateAsync(tower, FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, baseline);
        Assert.HasCount(baseline.Count, rewritten);
    }

    /// <summary>GT-R4-2/3/5: the parent-keyed collapse fires on a listed parent with a set-equal (order-shuffled) projection, and declines at a terminator parent and on a narrowing projection.</summary>
    [TestMethod]
    public void NoopProjectCollapseGuardsHoldAtTheCertifiedShapes()
    {
        Bgp bgpA = MakeBgp();
        Bgp bgpB = MakeBgp();
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        //Order-shuffled but set-equal projection under a Join parent: collapse, child unwrapped by reference.
        Join joinParent = new(new Project(bgpA, [Variable("o"), Variable("u"), Variable("p")]), bgpB);
        AlgebraRewriteOutcome collapsed = AlgebraRewriteCatalog.NoopProjectCollapse.Rule(joinParent, in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, collapsed.Application);
        Join rebuilt = Assert.IsInstanceOfType<Join>(collapsed.Algebra);
        Assert.AreSame(bgpA, rebuilt.Left);
        Assert.AreSame(bgpB, rebuilt.Right);

        //A query-form terminator is NOT a collapse-permitting parent — the H9 positional-dedup hazard.
        Distinct terminator = new(new Project(bgpA, [Variable("u"), Variable("p"), Variable("o")]));
        Assert.AreEqual(AlgebraRewriteApplication.NotApplicable, AlgebraRewriteCatalog.NoopProjectCollapse.Rule(terminator, in context).Application);

        //A narrowing projection drops bindings and stays.
        Join narrowing = new(new Project(bgpA, [Variable("u")]), bgpB);
        Assert.AreEqual(AlgebraRewriteApplication.NotApplicable, AlgebraRewriteCatalog.NoopProjectCollapse.Rule(narrowing, in context).Application);
    }

    /// <summary>GT-R4-6/8 through the engine: a union-parent collapse keeps bag counts, and a projection tower under a listed parent reduces across fixpoint passes.</summary>
    [TestMethod]
    public async Task NoopProjectCollapseIsAnswerIdenticalAndTowersReduce()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("m"), Iri("p"), Iri("v")), new DataTriple(Iri("n"), Iri("p"), Iri("v"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Bgp bgpA = MakeBgp();
        Bgp bgpB = MakeBgp();

        //Union parent, both sides same shape: 2 + 2 = 4 rows as a bag, with and without the collapse.
        Union union = new(new Project(bgpA, [Variable("u"), Variable("p"), Variable("o")]), bgpB);
        IReadOnlyList<SparqlSolution> baseline = await engine.EvaluateAsync(union, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> rewritten = await engine.EvaluateAsync(union, FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(4, baseline);
        Assert.HasCount(baseline.Count, rewritten);

        //A Project tower under a Join parent reduces one layer per pass; the fixpoint budget covers it.
        IReadOnlyList<SparqlVariable> all = [Variable("u"), Variable("p"), Variable("o")];
        Join towered = new(new Project(new Project(bgpA, all), all), bgpB);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);
        AlgebraOperator reduced = AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.NoopProjectCollapse).Rewrite(towered, in context);
        Join flattened = Assert.IsInstanceOfType<Join>(reduced);
        Assert.AreSame(bgpA, flattened.Left, "Two passes strip both projection layers.");
    }

    /// <summary>GT-R5-1/2/7 + the allowlist declines: annihilation fires exactly per the certified table.</summary>
    [TestMethod]
    public void EmptyTableAnnihilationFiresPerTheCertifiedRows()
    {
        Bgp bgp = MakeBgp();
        Table empty = EmptyTable();
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        AlgebraRewriteOutcome left = AlgebraRewriteCatalog.EmptyTableAnnihilation.Rule(new Join(empty, bgp), in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, left.Application);
        Assert.AreSame(empty, left.Algebra);

        AlgebraRewriteOutcome right = AlgebraRewriteCatalog.EmptyTableAnnihilation.Rule(new Join(bgp, empty), in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, right.Application);
        Assert.AreSame(empty, right.Algebra);

        AlgebraRewriteOutcome union = AlgebraRewriteCatalog.EmptyTableAnnihilation.Rule(new Union(empty, bgp), in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, union.Application);
        Assert.AreSame(bgp, union.Algebra, "The union identity KEEPS the non-empty side.");

        //A non-allowlisted subtree (Distinct here) blocks the join annihilation — the error-semantics guard.
        AlgebraRewriteOutcome guarded = AlgebraRewriteCatalog.EmptyTableAnnihilation.Rule(new Join(empty, new Distinct(bgp)), in context);
        Assert.AreEqual(AlgebraRewriteApplication.NotApplicable, guarded.Application);

        //A deep allowlisted subtree passes the walk.
        AlgebraRewriteOutcome deep = AlgebraRewriteCatalog.EmptyTableAnnihilation.Rule(new Join(empty, new Join(bgp, new Union(MakeBgp(), MakeBgp()))), in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, deep.Application);
    }

    /// <summary>GT-R5-1/2/3 through the engine: annihilated plans answer identically, including the never-evaluated filter condition.</summary>
    [TestMethod]
    public async Task EmptyTableAnnihilationIsAnswerIdenticalThroughTheEngine()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("m"), Iri("p"), Iri("v")), new DataTriple(Iri("n"), Iri("p"), Iri("v"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        Bgp bgp = MakeBgp();
        Table empty = EmptyTable();

        IReadOnlyList<SparqlSolution> joinBaseline = await engine.EvaluateAsync(new Join(empty, bgp), AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> joinRewritten = await engine.EvaluateAsync(new Join(empty, bgp), FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(0, joinBaseline);
        Assert.HasCount(joinBaseline.Count, joinRewritten);

        IReadOnlyList<SparqlSolution> unionBaseline = await engine.EvaluateAsync(new Union(empty, bgp), AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> unionRewritten = await engine.EvaluateAsync(new Union(empty, bgp), FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(2, unionBaseline);
        Assert.HasCount(unionBaseline.Count, unionRewritten);

        //A real filter condition plucked from a translated query, over the empty table: zero evaluations.
        AlgebraOperator translated = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?u :p ?o FILTER(isIRI(?u)) }", pool);
        Filter? filterTemplate = null;
        foreach(AlgebraOperator node in AlgebraWalker.Traverse(translated))
        {
            if(node is Filter found)
            {
                filterTemplate = found;

                break;
            }
        }

        Filter overEmpty = new(filterTemplate!.Condition, EmptyTable());
        IReadOnlyList<SparqlSolution> filterBaseline = await engine.EvaluateAsync(overEmpty, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> filterRewritten = await engine.EvaluateAsync(overEmpty, FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(0, filterBaseline);
        Assert.HasCount(filterBaseline.Count, filterRewritten);
    }

    /// <summary>H1 smoke under the full catalog: an on-mode EXISTS evaluation answers identically with every rule enabled (the bare-BGP core stays bare, so seeding still engages; the corpus arms re-certify at scale).</summary>
    [TestMethod]
    public async Task ExistsUnderTheFullCatalogAnswersIdentically()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("m"), Iri("p"), Iri("n")),
            new DataTriple(Iri("n"), Iri("p"), Iri("q")),
        ];
        SparqlQueryEngine baseline = await SparqlQueryEngine.BuildAsync(data, enginePolicy: new SparqlEnginePolicy(PreferStreamingOperators: true), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine rewriting = await SparqlQueryEngine.BuildAsync(data, enginePolicy: new SparqlEnginePolicy(PreferStreamingOperators: true, Rewrites: FullCatalog), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        const string query = "PREFIX : <http://example.org/> SELECT * WHERE { ?u :p ?o FILTER EXISTS { ?o :p ?b } }";

        IReadOnlyList<SparqlSolution> off = await baseline.EvaluateAsync(Translate(query, pool), TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> on = await rewriting.EvaluateAsync(Translate(query, pool), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, off);
        Assert.HasCount(off.Count, on);

        //Plan-build provenance: an abstaining tracer fires on the OUTER tree's BGP and, through the
        //registry-carried frame, on the EXISTS site's inner tree at plan build — at least two events.
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlQueryEngine traced = await SparqlQueryEngine.BuildAsync(data, executionTrace: handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraRewritePipeline tracer = AlgebraRewritePipeline.Create(new AlgebraRewriteEntry("abstain-on-bgp", AbstainOnBgp, Fixpoint: false));
        _ = await traced.EvaluateAsync(Translate(query, pool), tracer, TestContext.CancellationToken).ConfigureAwait(false);

        int rewriteEvents = 0;
        foreach(SparqlExecutionTraceEvent traceEvent in events)
        {
            if(traceEvent.Kind == SparqlExecutionEventKind.RewriteApplied)
            {
                rewriteEvents++;
            }
        }

        Assert.IsGreaterThanOrEqualTo(2, rewriteEvents, "The EXISTS plan build must apply the evaluation's pipeline (the registry-carried frame).");
    }

    /// <summary>A tracer rule: abstains at every BGP position, touching nothing.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused).</param>
    /// <returns>Abstained on a BGP, else not-applicable.</returns>
    private static AlgebraRewriteOutcome AbstainOnBgp(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node is Bgp ? AlgebraRewriteOutcome.Abstained(node) : AlgebraRewriteOutcome.NotApplicable(node);
    }

    /// <summary>R2 and R3 are single-step idempotent: applying each rule to its own output declines (spec 3.2.7(iii) for the non-nesting rules; R1's twin lives in the pipeline tests).</summary>
    [TestMethod]
    public void SliceFusionAndDistinctIdempotenceAreSingleStepIdempotent()
    {
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        Slice nested = new(new Slice(MakeBgp(), Offset: 2, Limit: 10), Offset: 3, Limit: 4);
        AlgebraRewriteOutcome fused = AlgebraRewriteCatalog.SliceFusion.Rule(nested, in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, fused.Application);
        Assert.AreEqual(
            AlgebraRewriteApplication.NotApplicable,
            AlgebraRewriteCatalog.SliceFusion.Rule(fused.Algebra, in context).Application,
            "The fused window sits directly over the BGP, so a second application must decline.");

        Distinct tower = new(new Distinct(MakeBgp()));
        AlgebraRewriteOutcome absorbed = AlgebraRewriteCatalog.DistinctIdempotence.Rule(tower, in context);
        Assert.AreEqual(AlgebraRewriteApplication.Applied, absorbed.Application);
        Assert.AreEqual(
            AlgebraRewriteApplication.NotApplicable,
            AlgebraRewriteCatalog.DistinctIdempotence.Rule(absorbed.Algebra, in context).Application,
            "Distinct over a plain input is not a redex.");
    }

    /// <summary>
    /// R5 is fixpoint-stable through the pipeline: a nested dead join converges to the empty table — the
    /// inner annihilation happens bottom-up within the pass and the outer redex it exposes fires in the
    /// same walk, after which no further pass applies anything.
    /// </summary>
    [TestMethod]
    public void EmptyTableAnnihilationConvergesThroughThePipeline()
    {
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);
        Table empty = EmptyTable();
        Join nested = new(new Join(empty, MakeBgp()), MakeBgp());

        AlgebraOperator reduced = AlgebraRewritePipeline.Create(AlgebraRewriteCatalog.EmptyTableAnnihilation).Rewrite(nested, in context);

        Assert.AreSame(empty, reduced, "Both dead joins annihilate and the surviving operand IS the empty table instance.");
    }

    /// <summary>
    /// H3, catalog-wide: a deterministic compositional shape sweep runs the FULL catalog and evaluates
    /// both forms of every shape — an out-of-set rule output would refuse at the engine's operator guard
    /// and an answer change would diverge, so a green sweep certifies closure and identity together.
    /// </summary>
    [TestMethod]
    public async Task CatalogOutputsStayInsideTheExecutableOperatorSetAcrossGeneratedShapes()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("m"), Iri("p"), Iri("v")), new DataTriple(Iri("n"), Iri("p"), Iri("v"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        int applied = 0;
        int swept = 0;
        foreach(AlgebraOperator shape in GeneratedShapes())
        {
            swept++;
            AlgebraOperator rewritten = FullCatalog.Rewrite(shape, in context);
            if(!ReferenceEquals(rewritten, shape))
            {
                applied++;
            }

            IReadOnlyList<SparqlSolution> baseline = await engine.EvaluateAsync(shape, AlgebraRewritePipeline.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            IReadOnlyList<SparqlSolution> viaCatalog = await engine.EvaluateAsync(shape, FullCatalog, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(baseline.Count, viaCatalog, $"Answer identity must hold for sweep shape {swept} ({shape.GetType().Name}-rooted).");
        }

        Assert.IsGreaterThan(8, applied, "The sweep must actually exercise rule applications, not decline everywhere.");
    }

    /// <summary>Enumerates the deterministic compositional sweep: every redex-bearing core under every wrapper, plus the bare cores.</summary>
    /// <returns>The shapes.</returns>
    private static IEnumerable<AlgebraOperator> GeneratedShapes()
    {
        List<AlgebraOperator> cores =
        [
            MakeBgp(),
            new Join(new UnitTable(), MakeBgp()),
            new Join(MakeBgp(), new UnitTable()),
            new Join(EmptyTable(), MakeBgp()),
            new Union(EmptyTable(), MakeBgp()),
            new Union(MakeBgp(), EmptyTable()),
            new Slice(new Slice(MakeBgp(), Offset: 1, Limit: 6), Offset: 0, Limit: 2),
            new Distinct(new Distinct(MakeBgp())),
            new Distinct(new Reduced(MakeBgp())),
            new Join(new Project(MakeBgp(), [Variable("u"), Variable("p"), Variable("o")]), MakeBgp()),
            new Join(EmptyTable(), new Join(new UnitTable(), MakeBgp())),
        ];

        foreach(AlgebraOperator core in cores)
        {
            yield return core;
            yield return new Distinct(core);
            yield return new Slice(core, Offset: 0, Limit: 3);
            yield return new Join(core, MakeBgp());
            yield return new Union(core, MakeBgp());
        }
    }

    /// <summary>Asserts one certified fusion row: the rule fuses the nested slices into the expected window over the inner input.</summary>
    /// <param name="inner">The inner (offset, limit).</param>
    /// <param name="outer">The outer (offset, limit).</param>
    /// <param name="expected">The certified fused (offset, limit).</param>
    private static void AssertFusion((int Offset, int? Limit) inner, (int Offset, int? Limit) outer, (int Offset, int? Limit) expected)
    {
        Bgp bgp = MakeBgp();
        Slice nested = new(new Slice(bgp, inner.Offset, inner.Limit), outer.Offset, outer.Limit);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, Statistics: null, Pass: 0);

        AlgebraRewriteOutcome outcome = AlgebraRewriteCatalog.SliceFusion.Rule(nested, in context);

        Assert.AreEqual(AlgebraRewriteApplication.Applied, outcome.Application);
        Slice fused = Assert.IsInstanceOfType<Slice>(outcome.Algebra);
        Assert.AreSame(bgp, fused.Input);
        Assert.AreEqual(expected.Offset, fused.Offset, $"offset of {inner}/{outer}");
        Assert.AreEqual(expected.Limit, fused.Limit, $"limit of {inner}/{outer}");
    }

    /// <summary>Builds the zero-row inline-data table over one fresh variable.</summary>
    /// <returns>The empty table.</returns>
    private static Table EmptyTable()
    {
        return new Table(new ValuesClause(default, [Variable("x")], []));
    }

    /// <summary>Builds a single-triple BGP over the fresh variables <c>?u ?p ?o</c> (a distinct instance per call).</summary>
    /// <returns>The BGP.</returns>
    private static Bgp MakeBgp()
    {
        return new([new TriplePattern(default, Var("u"), Var("p"), Var("o"))]);
    }

    /// <summary>Builds a variable term with the given name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable term.</returns>
    private static VariableTerm Var(string name)
    {
        return new VariableTerm(default, Variable(name));
    }

    /// <summary>Builds a variable with the given name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
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

    /// <summary>Builds an example-namespace IRI node from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The IRI node.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }
}
