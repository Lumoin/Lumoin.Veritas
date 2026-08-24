using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.ParserTests.Geo.GeoSparqlQueries;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The query-rewrite extension's conformance rows: the census requirements
/// <c>/req/query-rewrite-extension/sf-query-rewrite</c>, <c>eh-query-rewrite</c>, and
/// <c>rcc8-query-rewrite</c> through parsed queries under the composed
/// <see cref="GeoExtensionModule.CreateRewritePipeline"/> pipeline. The rows pin the four case rules
/// (feature-feature, feature-geometry, geometry-feature, geometry-geometry), the surviving asserted route,
/// the set answers (a pair counts once however many witnesses derive it), the stated bounds (variable
/// predicates and property paths keep asserted-only matching), the dark and functionless degradations, the
/// structural properties of the rewritten algebra, and the full relation roster's pinned verdicts at the
/// feature level.
/// </summary>
[TestClass]
internal sealed class GeoQueryRewriteSparqlTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The reference square, the shared fixture family's anchor.</summary>
    private const string Square = "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))";

    /// <summary>A square strictly inside <see cref="Square"/>.</summary>
    private const string InnerSquare = "POLYGON ((2 2, 3 2, 3 3, 2 3, 2 2))";

    /// <summary>A square disjoint from <see cref="Square"/>.</summary>
    private const string FarSquare = "POLYGON ((10 10, 12 10, 12 12, 10 12, 10 10))";

    /// <summary>A square containing <see cref="Square"/> with a shared corner.</summary>
    private const string LargeSquare = "POLYGON ((0 0, 8 0, 8 8, 0 8, 0 0))";

    /// <summary>A square overlapping <see cref="Square"/> in area.</summary>
    private const string OverlappingSquare = "POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))";

    /// <summary>A square touching <see cref="Square"/> at one corner.</summary>
    private const string TouchingSquare = "POLYGON ((4 4, 6 4, 6 6, 4 6, 4 4))";

    /// <summary>The diagonal line fixture.</summary>
    private const string Diagonal = "LINESTRING (0 0, 2 2)";

    /// <summary>The anti-diagonal line crossing <see cref="Diagonal"/> at a point.</summary>
    private const string AntiDiagonal = "LINESTRING (0 2, 2 0)";

    /// <summary>A line crossing <see cref="Square"/>'s boundary.</summary>
    private const string CrossLine = "LINESTRING (2 2, 9 9)";

    /// <summary>The point fixture.</summary>
    private const string Point = "POINT (1 1)";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The module-composed extension-function registry.</summary>
    private static SparqlFunctionRegistry Functions { get; } = BuildModuleFunctions();

    /// <summary>The module-composed value-datatype registry.</summary>
    private static ValueDatatypeRegistry Datatypes { get; } = BuildModuleDatatypes();

    /// <summary>The module-composed rewrite pipeline under test.</summary>
    private static AlgebraRewritePipeline Pipeline { get; } = GeoExtensionModule.CreateRewritePipeline();

    /// <summary>The <c>geo:hasDefaultGeometry</c> predicate term of the test data.</summary>
    private static NamedNode HasDefaultGeometry { get; } = new(GeoVocabulary.Geo.HasDefaultGeometry);

    /// <summary>The <c>geo:asWKT</c> predicate term of the test data.</summary>
    private static NamedNode AsWkt { get; } = new(GeoVocabulary.Geo.AsWkt);

    /// <summary>The feature-feature case derives the relation through both features' default geometries.</summary>
    [TestMethod]
    public async Task FeatureFeatureCaseDerivesThroughTheRewrite()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fInner>").ConfigureAwait(false), "The square feature contains the inner feature through the derived route.");
        Assert.IsFalse(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fFar>").ConfigureAwait(false), "The far feature is not contained, so the derived route answers no solution.");
    }

    /// <summary>The feature-geometry and geometry-feature cases derive against a geometry node carrying its own serialization.</summary>
    [TestMethod]
    public async Task FeatureGeometryAndGeometryFeatureCasesDerive()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}rawInner>").ConfigureAwait(false), "A feature subject reaches a geometry object through the feature-geometry case.");
        Assert.IsTrue(await AskAsync(engine, $"<{Ex}rawSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fInner>").ConfigureAwait(false), "A geometry subject reaches a feature object through the geometry-feature case.");
    }

    /// <summary>The geometry-geometry case derives between two geometry nodes.</summary>
    [TestMethod]
    public async Task GeometryGeometryCaseDerives()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, $"<{Ex}rawSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}rawInner>").ConfigureAwait(false), "Two geometry nodes derive through the geometry-geometry case.");
    }

    /// <summary>The asserted route survives the rewrite: a pair with no geometries anywhere still matches its asserted triple.</summary>
    [TestMethod]
    public async Task TheAssertedRouteSurvivesTheRewrite()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}assertedThing>").ConfigureAwait(false), "The asserted branch matches the geometry-less pair.");
    }

    /// <summary>
    /// The entailment regime's set answers: a pair counts once however many witnesses derive it — the
    /// asserted-and-derived pair, the self-contained subject, and the two-default-geometry feature each
    /// bind exactly one solution.
    /// </summary>
    [TestMethod]
    public async Task EveryDerivablePairAnswersExactlyOnce()
    {
        List<DataTriple> data = [];
        AddFeature(data, "fA", Square);
        AddFeature(data, "fB", InnerSquare);
        AddFeature(data, "fTwin", InnerSquare);
        data.Add(new DataTriple(Iri(Ex + "fTwin"), HasDefaultGeometry, Iri(Ex + "fTwinGeom2")));
        data.Add(new DataTriple(Iri(Ex + "fTwinGeom2"), AsWkt, Wkt("POLYGON ((1 1, 2 1, 2 2, 1 2, 1 1))")));
        data.Add(new DataTriple(Iri(Ex + "fA"), Iri(Ex + "unused"), Iri(Ex + "decoy")));
        data.Add(new DataTriple(Iri(Ex + "fA"), new NamedNode(GeoVocabulary.Geo.SfContains), Iri(Ex + "fB")));

        SparqlQueryEngine engine = await BuildEngineAsync(data, registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?x WHERE {{ <{Ex}fA> <{GeoVocabulary.Geo.SfContains}> ?x }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, Pipeline, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(7, solutions, "The three features — fB (asserted and derived), fA (self-containment), fTwin (two geometry witnesses) — and the four contained geometry nodes each answer exactly once.");
        HashSet<string> bound = [];
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsTrue(solution.TryGetValue(Variable("x"), out RdfTerm term));
            Assert.IsInstanceOfType<NamedNode>(term);
            Assert.IsTrue(bound.Add(((NamedNode)term).Iri.ToString()), "No pair answers twice.");
        }

        Assert.IsTrue(bound.Contains(Ex + "fA") && bound.Contains(Ex + "fB") && bound.Contains(Ex + "fTwin"), "The three features are bound.");
        Assert.IsTrue(bound.Contains(Ex + "fAGeom") && bound.Contains(Ex + "fBGeom") && bound.Contains(Ex + "fTwinGeom") && bound.Contains(Ex + "fTwinGeom2"), "The geometry nodes are spatial objects of the geometry cases and are bound too.");
    }

    /// <summary>Without the pipeline the engine keeps asserted-only matching: the derivable pair answers false and the asserted pair answers true.</summary>
    [TestMethod]
    public async Task TheDarkEngineAnswersAssertedOnly()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsFalse(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fInner>", rewrite: false).ConfigureAwait(false), "Without the pipeline the derivable pair does not match.");
        Assert.IsTrue(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}assertedThing>", rewrite: false).ConfigureAwait(false), "The asserted pair matches without the pipeline.");
    }

    /// <summary>With the pipeline but no registered functions every derived-branch filter errs, so matching degrades to asserted-only — silent, never wrong.</summary>
    [TestMethod]
    public async Task TheRewriteWithoutFunctionsDegradesToAsserted()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: false).ConfigureAwait(false);

        Assert.IsFalse(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fInner>").ConfigureAwait(false), "Unregistered functions err in the derived branches, so only the asserted route matches.");
        Assert.IsTrue(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}assertedThing>").ConfigureAwait(false), "The asserted route still matches.");
    }

    /// <summary>A variable predicate keeps asserted-only matching — the stated implementation bound of the unbound-predicate case.</summary>
    [TestMethod]
    public async Task AVariablePredicatePatternKeepsAssertedOnlyMatching()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT ?p WHERE {{ <{Ex}fSquare> ?p <{Ex}fInner> }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, Pipeline, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions, "No triple between the features is asserted, and a variable predicate derives nothing.");
    }

    /// <summary>A property path over a relation IRI keeps asserted-only matching — paths are outside the rewrite's pattern.</summary>
    [TestMethod]
    public async Task APropertyPathKeepsAssertedOnlyMatching()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsFalse(await AskAsync(engine, $"<{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}>+ <{Ex}fInner>").ConfigureAwait(false), "The one-or-more path stays on asserted triples, where no chain connects the features.");
    }

    /// <summary>A rewritten triple joins its BGP's remaining patterns: the remainder constrains the solutions.</summary>
    [TestMethod]
    public async Task TheRemainderJoinsTheRewrittenTriple()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, $"<{Ex}fSquare> <{Ex}name> \"square\" . <{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fInner>").ConfigureAwait(false), "The matching remainder joins the derived pair.");
        Assert.IsFalse(await AskAsync(engine, $"<{Ex}fSquare> <{Ex}name> \"wrong\" . <{Ex}fSquare> <{GeoVocabulary.Geo.SfContains}> <{Ex}fInner>").ConfigureAwait(false), "A failing remainder removes the row however derivable the pair is.");
    }

    /// <summary>A graph-scoped pattern keeps its graph context: the replacement stays inside the <c>GRAPH</c> operator.</summary>
    [TestMethod]
    public void AGraphScopedPatternKeepsItsGraphContext()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator root = Translate($"ASK {{ GRAPH <{Ex}g> {{ <{Ex}x> <{GeoVocabulary.Geo.SfContains}> <{Ex}y> }} }}", pool);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, null, 0);

        AlgebraOperator rewritten = Pipeline.Rewrite(root, in context);

        Graph? graph = null;
        foreach(AlgebraOperator node in Nodes(rewritten))
        {
            if(node is Graph candidate)
            {
                graph = candidate;
            }
        }

        Assert.IsNotNull(graph, "The GRAPH operator survives the rewrite.");
        Assert.HasCount(4, FilterCallIris(graph), "The four derived case filters sit inside the graph scope.");
    }

    /// <summary>
    /// Every relation property routes to its own <c>geof:</c> predicate function: the rewritten algebra of
    /// a single-triple pattern carries exactly the four case filters, each calling the mapped function —
    /// so a transposed property-to-function mapping fails its row here.
    /// </summary>
    [TestMethod]
    public void EveryTopologicalPropertyRoutesToItsFunction()
    {
        (Utf8String Property, Utf8String Function)[] roster =
        [
            (GeoVocabulary.Geo.SfEquals, GeoVocabulary.Geof.SfEquals),
            (GeoVocabulary.Geo.SfDisjoint, GeoVocabulary.Geof.SfDisjoint),
            (GeoVocabulary.Geo.SfIntersects, GeoVocabulary.Geof.SfIntersects),
            (GeoVocabulary.Geo.SfTouches, GeoVocabulary.Geof.SfTouches),
            (GeoVocabulary.Geo.SfCrosses, GeoVocabulary.Geof.SfCrosses),
            (GeoVocabulary.Geo.SfWithin, GeoVocabulary.Geof.SfWithin),
            (GeoVocabulary.Geo.SfContains, GeoVocabulary.Geof.SfContains),
            (GeoVocabulary.Geo.SfOverlaps, GeoVocabulary.Geof.SfOverlaps),
            (GeoVocabulary.Geo.EhEquals, GeoVocabulary.Geof.EhEquals),
            (GeoVocabulary.Geo.EhDisjoint, GeoVocabulary.Geof.EhDisjoint),
            (GeoVocabulary.Geo.EhMeet, GeoVocabulary.Geof.EhMeet),
            (GeoVocabulary.Geo.EhOverlap, GeoVocabulary.Geof.EhOverlap),
            (GeoVocabulary.Geo.EhCovers, GeoVocabulary.Geof.EhCovers),
            (GeoVocabulary.Geo.EhCoveredBy, GeoVocabulary.Geof.EhCoveredBy),
            (GeoVocabulary.Geo.EhInside, GeoVocabulary.Geof.EhInside),
            (GeoVocabulary.Geo.EhContains, GeoVocabulary.Geof.EhContains),
            (GeoVocabulary.Geo.Rcc8Eq, GeoVocabulary.Geof.Rcc8Eq),
            (GeoVocabulary.Geo.Rcc8Dc, GeoVocabulary.Geof.Rcc8Dc),
            (GeoVocabulary.Geo.Rcc8Ec, GeoVocabulary.Geof.Rcc8Ec),
            (GeoVocabulary.Geo.Rcc8Po, GeoVocabulary.Geof.Rcc8Po),
            (GeoVocabulary.Geo.Rcc8Tppi, GeoVocabulary.Geof.Rcc8Tppi),
            (GeoVocabulary.Geo.Rcc8Tpp, GeoVocabulary.Geof.Rcc8Tpp),
            (GeoVocabulary.Geo.Rcc8Ntpp, GeoVocabulary.Geof.Rcc8Ntpp),
            (GeoVocabulary.Geo.Rcc8Ntppi, GeoVocabulary.Geof.Rcc8Ntppi),
        ];

        Assert.HasCount(24, roster, "The relation roster is the full twenty-four.");

        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, null, 0);
        foreach((Utf8String property, Utf8String function) in roster)
        {
            using Utf8StringPool pool = new();
            AlgebraOperator root = Translate($"ASK {{ <{Ex}x> <{property}> <{Ex}y> }}", pool);
            AlgebraOperator rewritten = Pipeline.Rewrite(root, in context);

            Assert.AreNotSame(root, rewritten, $"{property}: the pattern rewrites.");
            List<Utf8String> calls = FilterCallIris(rewritten);
            Assert.HasCount(4, calls, $"{property}: the four case filters are present.");
            foreach(Utf8String called in calls)
            {
                Assert.IsTrue(called.Span.SequenceEqual(function.Span), $"{property}: every case filter calls {function}.");
            }
        }
    }

    /// <summary>A BGP with no relation predicate passes through by reference: the rewrite touches nothing else.</summary>
    [TestMethod]
    public void AForeignPredicateBgpPassesThroughUntouched()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator root = Translate($"ASK {{ <{Ex}x> <{Ex}p> <{Ex}y> }}", pool);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, null, 0);

        Assert.AreSame(root, Pipeline.Rewrite(root, in context), "A foreign predicate leaves the tree untouched by reference.");
    }

    /// <summary>The rewritten pattern exposes only the triple's own variables: the internal join variables never leave their projection.</summary>
    [TestMethod]
    public void TheRewrittenPatternExposesOnlyTheTriplesOwnVariables()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator root = Translate($"SELECT * WHERE {{ ?a <{GeoVocabulary.Geo.SfWithin}> ?b }}", pool);
        AlgebraRewriteContext context = new(SparqlEnginePolicy.Default, null, 0);

        AlgebraOperator rewritten = Pipeline.Rewrite(root, in context);

        Assert.AreNotSame(root, rewritten, "The pattern rewrites.");
        Assert.HasCount(4, FilterCallIris(rewritten), "The four case filters are present.");
        Assert.HasCount(2, rewritten.OutputVariables, "Only the triple's own variables are visible at the root.");
        Assert.IsTrue(rewritten.OutputVariables.Contains(Variable("a")) && rewritten.OutputVariables.Contains(Variable("b")), "The visible variables are exactly the pattern's own.");
    }

    /// <summary>
    /// Every relation property answers its pinned verdict at the feature level through the rewrite: each
    /// row's fixtures discriminate the property from its plausible transposition partner, and every
    /// directional pair carries both directions.
    /// </summary>
    [TestMethod]
    public async Task EveryTopologicalPropertyAnswersItsPinnedVerdictThroughTheRewrite()
    {
        (Utf8String Property, string Subject, string Object, bool Expected)[] pinned =
        [
            (GeoVocabulary.Geo.SfEquals, "fPoint", "fPoint", true),
            (GeoVocabulary.Geo.SfDisjoint, "fSquare", "fFar", true),
            (GeoVocabulary.Geo.SfIntersects, "fSquare", "fOverlap", true),
            (GeoVocabulary.Geo.SfIntersects, "fSquare", "fFar", false),
            (GeoVocabulary.Geo.SfTouches, "fSquare", "fTouch", true),
            (GeoVocabulary.Geo.SfCrosses, "fCross", "fSquare", true),
            (GeoVocabulary.Geo.SfCrosses, "fSquare", "fTouch", false),
            (GeoVocabulary.Geo.SfWithin, "fInner", "fSquare", true),
            (GeoVocabulary.Geo.SfWithin, "fSquare", "fInner", false),
            (GeoVocabulary.Geo.SfContains, "fSquare", "fInner", true),
            (GeoVocabulary.Geo.SfContains, "fInner", "fSquare", false),
            (GeoVocabulary.Geo.SfOverlaps, "fSquare", "fOverlap", true),
            (GeoVocabulary.Geo.SfOverlaps, "fDiag", "fAnti", false),
            (GeoVocabulary.Geo.EhEquals, "fSquare", "fSquare", true),
            (GeoVocabulary.Geo.EhEquals, "fPoint", "fPoint", false),
            (GeoVocabulary.Geo.EhDisjoint, "fSquare", "fFar", true),
            (GeoVocabulary.Geo.EhMeet, "fSquare", "fTouch", true),
            (GeoVocabulary.Geo.EhOverlap, "fDiag", "fAnti", true),
            (GeoVocabulary.Geo.EhCovers, "fLarge", "fSquare", true),
            (GeoVocabulary.Geo.EhCovers, "fSquare", "fLarge", false),
            (GeoVocabulary.Geo.EhCoveredBy, "fSquare", "fLarge", true),
            (GeoVocabulary.Geo.EhCoveredBy, "fLarge", "fSquare", false),
            (GeoVocabulary.Geo.EhInside, "fInner", "fSquare", true),
            (GeoVocabulary.Geo.EhInside, "fSquare", "fInner", false),
            (GeoVocabulary.Geo.EhContains, "fSquare", "fInner", true),
            (GeoVocabulary.Geo.EhContains, "fInner", "fSquare", false),
            (GeoVocabulary.Geo.Rcc8Eq, "fSquare", "fSquare", true),
            (GeoVocabulary.Geo.Rcc8Eq, "fPoint", "fPoint", false),
            (GeoVocabulary.Geo.Rcc8Dc, "fSquare", "fFar", true),
            (GeoVocabulary.Geo.Rcc8Dc, "fSquare", "fTouch", false),
            (GeoVocabulary.Geo.Rcc8Ec, "fSquare", "fTouch", true),
            (GeoVocabulary.Geo.Rcc8Ec, "fSquare", "fFar", false),
            (GeoVocabulary.Geo.Rcc8Po, "fSquare", "fOverlap", true),
            (GeoVocabulary.Geo.Rcc8Tppi, "fLarge", "fSquare", true),
            (GeoVocabulary.Geo.Rcc8Tppi, "fSquare", "fLarge", false),
            (GeoVocabulary.Geo.Rcc8Tpp, "fSquare", "fLarge", true),
            (GeoVocabulary.Geo.Rcc8Tpp, "fLarge", "fSquare", false),
            (GeoVocabulary.Geo.Rcc8Ntpp, "fInner", "fSquare", true),
            (GeoVocabulary.Geo.Rcc8Ntpp, "fSquare", "fInner", false),
            (GeoVocabulary.Geo.Rcc8Ntppi, "fSquare", "fInner", true),
            (GeoVocabulary.Geo.Rcc8Ntppi, "fInner", "fSquare", false),
        ];

        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        foreach((Utf8String property, string subject, string @object, bool expected) in pinned)
        {
            bool answer = await AskAsync(engine, $"<{Ex}{subject}> <{property}> <{Ex}{@object}>").ConfigureAwait(false);

            Assert.AreEqual(expected, answer, $"{property} over {subject}, {@object}: the pinned verdict.");
        }
    }

    /// <summary>Evaluates an <c>ASK</c> pattern under the pipeline (or without it) and answers whether any solution matched.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="pattern">The group-graph-pattern body, without braces.</param>
    /// <param name="rewrite">Whether the evaluation runs under the module pipeline.</param>
    /// <returns>The ASK verdict.</returns>
    private async Task<bool> AskAsync(SparqlQueryEngine engine, string pattern, bool rewrite = true)
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"ASK {{ {pattern} }}", pool);
        IReadOnlyList<SparqlSolution> solutions = rewrite
            ? await engine.EvaluateAsync(algebra, Pipeline, TestContext.CancellationToken).ConfigureAwait(false)
            : await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        return solutions.Count > 0;
    }

    /// <summary>Builds the shared fixture engine: the fixture features, the two raw geometry nodes, the geometry-less asserted pair, and the remainder name triple.</summary>
    /// <param name="registered">Whether the engine composes the module's registries.</param>
    /// <returns>The engine.</returns>
    private Task<SparqlQueryEngine> BuildGeoEngineAsync(bool registered)
    {
        List<DataTriple> data = [];
        AddFeature(data, "fSquare", Square);
        AddFeature(data, "fInner", InnerSquare);
        AddFeature(data, "fFar", FarSquare);
        AddFeature(data, "fLarge", LargeSquare);
        AddFeature(data, "fOverlap", OverlappingSquare);
        AddFeature(data, "fTouch", TouchingSquare);
        AddFeature(data, "fDiag", Diagonal);
        AddFeature(data, "fAnti", AntiDiagonal);
        AddFeature(data, "fCross", CrossLine);
        AddFeature(data, "fPoint", Point);
        data.Add(new DataTriple(Iri(Ex + "rawSquare"), AsWkt, Wkt(Square)));
        data.Add(new DataTriple(Iri(Ex + "rawInner"), AsWkt, Wkt(InnerSquare)));
        data.Add(new DataTriple(Iri(Ex + "fSquare"), new NamedNode(GeoVocabulary.Geo.SfContains), Iri(Ex + "assertedThing")));
        data.Add(new DataTriple(Iri(Ex + "fSquare"), Iri(Ex + "name"), Text("square")));

        return BuildEngineAsync(data, registered);
    }

    /// <summary>Builds an engine over the given data, with or without the module registries in its expression context.</summary>
    /// <param name="data">The data triples.</param>
    /// <param name="registered">Whether the engine composes the module's registries.</param>
    /// <returns>The engine.</returns>
    private Task<SparqlQueryEngine> BuildEngineAsync(List<DataTriple> data, bool registered)
    {
        SparqlExpressionContext context = registered
            ? SparqlExpressionContext.CreateDefault(valueDatatypes: Datatypes, extensionFunctions: Functions)
            : SparqlExpressionContext.CreateDefault();

        return SparqlQueryEngine.BuildAsync(data, expressionContext: context, cancellationToken: TestContext.CancellationToken).AsTask();
    }

    /// <summary>Appends one feature's triples: the feature reaches its geometry node through <c>geo:hasDefaultGeometry</c>, and the node carries the WKT serialization.</summary>
    /// <param name="data">The data list appended to.</param>
    /// <param name="name">The feature's local name.</param>
    /// <param name="wkt">The geometry's WKT lexical form.</param>
    private static void AddFeature(List<DataTriple> data, string name, string wkt)
    {
        data.Add(new DataTriple(Iri(Ex + name), HasDefaultGeometry, Iri(Ex + name + "Geom")));
        data.Add(new DataTriple(Iri(Ex + name + "Geom"), AsWkt, Wkt(wkt)));
    }

    /// <summary>Enumerates a tree's operators through an explicit stack.</summary>
    /// <param name="root">The tree root.</param>
    /// <returns>The operators, root first.</returns>
    private static List<AlgebraOperator> Nodes(AlgebraOperator root)
    {
        List<AlgebraOperator> nodes = [];
        Stack<AlgebraOperator> pending = new();
        pending.Push(root);
        while(pending.Count > 0)
        {
            AlgebraOperator current = pending.Pop();
            nodes.Add(current);
            foreach(AlgebraOperator child in current.Children)
            {
                pending.Push(child);
            }
        }

        return nodes;
    }

    /// <summary>Collects the function IRIs of the tree's filter conditions that are direct function calls.</summary>
    /// <param name="root">The tree root.</param>
    /// <returns>The called IRIs, one per matching filter.</returns>
    private static List<Utf8String> FilterCallIris(AlgebraOperator root)
    {
        List<Utf8String> calls = [];
        foreach(AlgebraOperator node in Nodes(root))
        {
            if(node is Filter { Condition: FunctionCallExpression call })
            {
                calls.Add(call.Function.Value);
            }
        }

        return calls;
    }
}
