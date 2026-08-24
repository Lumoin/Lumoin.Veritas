using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.ParserTests.Geo.GeoSparqlQueries;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The <c>geof:</c> function catalog's conformance rows at the SPARQL level: the registering host composes
/// BOTH registries from <see cref="GeoExtensionModule"/> — the function catalog and the
/// geometry-serialization value datatypes — and the rows exercise the census requirements
/// <c>/req/geometry-extension/asWKT-function</c>, <c>/req/geometry-extension/srid-function</c>,
/// <c>/req/geometry-extension/query-functions-non-sf</c>,
/// <c>/req/geometry-extension/query-functions</c>, and
/// <c>/req/geometry-extension/wkt-axis-order</c> through parsed queries over a graph of
/// <c>geo:wktLiteral</c> data. The serialization families ride the same shape over a graph of
/// geometry-serialization literals: the three format serializers bind their typed answers, and a stored
/// non-empty body binds through an accessor, witnessing the operand seam's codec reads at the engine
/// level. The error-value discipline surfaces as SPARQL semantics — a FILTER error drops the row and a
/// BIND error leaves the variable unbound — and the unregistered engine keeps the dark-by-default posture.
/// </summary>
[TestClass]
internal sealed class GeoFunctionSparqlTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>An explicit test-local CRS IRI whose linear unit the catalog's metre convention takes at face value.</summary>
    private const string MetricCrs = "http://example.org/def/crs/metric";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The module-composed extension-function registry; every registration must be accepted.</summary>
    private static SparqlFunctionRegistry Functions { get; } = BuildModuleFunctions();

    /// <summary>The module-composed value-datatype registry.</summary>
    private static ValueDatatypeRegistry Datatypes { get; } = BuildModuleDatatypes();

    /// <summary><c>geof:asWKT</c> answers the canonical serialization through a SELECT expression over stored data.</summary>
    [TestMethod]
    public async Task AsWktBindsTheCanonicalFormThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.AsWkt}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "POINT (1 2)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:getSRID</c> answers the CRS84 default as <c>xsd:anyURI</c> for an unprefixed literal.</summary>
    [TestMethod]
    public async Task GetSridBindsTheDefaultCrsIri()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.GetSrid}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "http://www.opengis.net/def/crs/OGC/1.3/CRS84", Vocabulary.Xsd.AnyUri);
    }

    /// <summary><c>geof:transform</c> re-expresses the stored CRS84 literal in EPSG:4326 through a SELECT expression: the answer swaps to latitude-first coordinates under the explicit target prefix — the query-functions roster's last member decided through the engine.</summary>
    [TestMethod]
    public async Task TransformBindsTheAxisSwappedLiteralThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.Transform}>(?g, <http://www.opengis.net/def/crs/EPSG/0/4326>) AS ?r) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "<http://www.opengis.net/def/crs/EPSG/0/4326> POINT (2 1)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:minX</c> resolves the east axis through the declared order at the engine level: over the latitude-first re-expression of the stored CRS84 point its answer equals the untransformed literal's own — the axis-order requirement decided through the engine.</summary>
    [TestMethod]
    public async Task MinXBindsTheEastAxisOverTheLatitudeFirstLiteral()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.MinX}>(<{GeoVocabulary.Geof.Transform}>(?g, <http://www.opengis.net/def/crs/EPSG/0/4326>)) AS ?swapped) (<{GeoVocabulary.Geof.MinX}>(?g) AS ?direct) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "swapped", "1", Vocabulary.Xsd.Double);
        AssertTypedLiteral(solutions[0], "direct", "1", Vocabulary.Xsd.Double);
    }

    /// <summary><c>geof:isEmpty</c> decides a FILTER over every stored geometry: exactly the empty geometry's row survives.</summary>
    [TestMethod]
    public async Task IsEmptyDecidesTheFilter()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s ?p ?g FILTER(<{GeoVocabulary.Geof.IsEmpty}>(?g)) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "Exactly the empty geometry's row satisfies geof:isEmpty.");
        Assert.IsTrue(solutions[0].TryGetValue(Variable("p"), out RdfTerm predicate));
        Assert.IsInstanceOfType<NamedNode>(predicate);
        Assert.IsTrue(((NamedNode)predicate).Iri.Span.SequenceEqual(Encoding.UTF8.GetBytes(Ex + "emptyGeom")));
    }

    /// <summary>The dimension family binds over a Z-carrying point: topological 0, coordinate 3, spatial 3.</summary>
    [TestMethod]
    public async Task DimensionFamilyBindsOverThePointWithZ()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Dimension}>(?g) AS ?d) (<{GeoVocabulary.Geof.CoordinateDimension}>(?g) AS ?c) (<{GeoVocabulary.Geof.SpatialDimension}>(?g) AS ?p) WHERE {{ <{Ex}s> <{Ex}pointZ> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "d", "0", Vocabulary.Xsd.Integer);
        AssertTypedLiteral(solutions[0], "c", "3", Vocabulary.Xsd.Integer);
        AssertTypedLiteral(solutions[0], "p", "3", Vocabulary.Xsd.Integer);
    }

    /// <summary><c>geof:distance</c> answers in degrees under the CRS84 default through a three-argument call joining two stored geometries.</summary>
    [TestMethod]
    public async Task DistanceBindsInDegreesUnderCrs84()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Distance}>(?a, ?b, <{OgcUnitsOfMeasure.Degree}>) AS ?r) WHERE {{ <{Ex}s> <{Ex}pointA> ?a . <{Ex}s> <{Ex}pointB> ?b }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "5", Vocabulary.Xsd.Double);
    }

    /// <summary>Under the CRS84 default a metre-denominated answer is the expression error, so the BIND leaves the variable unbound while the row survives.</summary>
    [TestMethod]
    public async Task MetricDistanceUnderCrs84LeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}pointA> ?a . <{Ex}s> <{Ex}pointB> ?b BIND(<{GeoVocabulary.Geof.MetricDistance}>(?a, ?b) AS ?r) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "The BIND error affects only ?r; the row survives.");
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _), "The errored metric answer leaves ?r unbound.");
    }

    /// <summary><c>geof:metricArea</c> answers shell minus holes over a stored literal with an explicit CRS.</summary>
    [TestMethod]
    public async Task MetricAreaBindsUnderAnExplicitCrs()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.MetricArea}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}metricPolygon> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "96", Vocabulary.Xsd.Double);
    }

    /// <summary><c>geof:envelope</c> and <c>geof:boundary</c> bind their geometry results as canonical <c>geo:wktLiteral</c> values.</summary>
    [TestMethod]
    public async Task EnvelopeAndBoundaryBindAsWktLiterals()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Envelope}>(?g) AS ?e) (<{GeoVocabulary.Geof.Boundary}>(?g) AS ?b) WHERE {{ <{Ex}s> <{Ex}line> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "e", "POLYGON ((0 0, 2 0, 2 3, 0 3, 0 0))", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "b", "MULTIPOINT ((0 0), (2 3))", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:numGeometries</c> and <c>geof:geometryN</c> bind the member count and the one-based member over a stored multipoint.</summary>
    [TestMethod]
    public async Task MemberAccessorsBindOverTheMultipoint()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.NumGeometries}>(?g) AS ?n) (<{GeoVocabulary.Geof.GeometryN}>(?g, 2) AS ?m) WHERE {{ <{Ex}s> <{Ex}multi> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "n", "2", Vocabulary.Xsd.Integer);
        AssertTypedLiteral(solutions[0], "m", "POINT (3 4)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The coordinate extrema bind over a stored linestring.</summary>
    [TestMethod]
    public async Task CoordinateExtremaBindOverTheLine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.MinX}>(?g) AS ?a) (<{GeoVocabulary.Geof.MaxX}>(?g) AS ?b) (<{GeoVocabulary.Geof.MinY}>(?g) AS ?c) (<{GeoVocabulary.Geof.MaxY}>(?g) AS ?d) WHERE {{ <{Ex}s> <{Ex}line> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "a", "0", Vocabulary.Xsd.Double);
        AssertTypedLiteral(solutions[0], "b", "2", Vocabulary.Xsd.Double);
        AssertTypedLiteral(solutions[0], "c", "0", Vocabulary.Xsd.Double);
        AssertTypedLiteral(solutions[0], "d", "3", Vocabulary.Xsd.Double);
    }

    /// <summary><c>geof:relate</c> answers the DE-9IM pattern test through the engine, and a malformed pattern leaves the bound variable absent.</summary>
    [TestMethod]
    public async Task RelateBindsThePatternTestThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Relate}>(?a, ?a, \"2FFF1FFF2\") AS ?equal) (<{GeoVocabulary.Geof.Relate}>(?a, ?b, \"FF*FF****\") AS ?disjoint) WHERE {{ <{Ex}s> <{Ex}squareA> ?a . <{Ex}s> <{Ex}squareB> ?b }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "equal", "true", Vocabulary.Xsd.Boolean);
        AssertTypedLiteral(solutions[0], "disjoint", "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>A malformed relate pattern is an expression error, so the BIND leaves its variable unbound.</summary>
    [TestMethod]
    public async Task MalformedRelatePatternLeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}squareA> ?a BIND(<{GeoVocabulary.Geof.Relate}>(?a, ?a, \"tFFFTFFFT\") AS ?r) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _), "A malformed pattern errs, so the BIND leaves the variable unbound.");
    }

    /// <summary>The three predicate families dispatch by function IRI through the engine and answer over the stored squares.</summary>
    [TestMethod]
    public async Task TopologicalPredicateFamiliesBindThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.SfContains}>(?a, ?i) AS ?sf) (<{GeoVocabulary.Geof.EhInside}>(?i, ?a) AS ?eh) (<{GeoVocabulary.Geof.Rcc8Ntpp}>(?i, ?a) AS ?rcc) (<{GeoVocabulary.Geof.SfOverlaps}>(?a, ?b) AS ?overlaps) WHERE {{ <{Ex}s> <{Ex}squareA> ?a . <{Ex}s> <{Ex}squareB> ?b . <{Ex}s> <{Ex}innerSquare> ?i }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "sf", "true", Vocabulary.Xsd.Boolean);
        AssertTypedLiteral(solutions[0], "eh", "true", Vocabulary.Xsd.Boolean);
        AssertTypedLiteral(solutions[0], "rcc", "true", Vocabulary.Xsd.Boolean);
        AssertTypedLiteral(solutions[0], "overlaps", "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>A predicate decides a FILTER: exactly the rows whose stored geometry the square contains survive.</summary>
    [TestMethod]
    public async Task PredicateDecidesTheFilter()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}squareA> ?a . <{Ex}s> ?p ?g FILTER(<{GeoVocabulary.Geof.SfContains}>(?a, ?g)) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotEmpty(solutions, "The square contains at least the inner square and itself.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsTrue(solution.TryGetValue(Variable("p"), out RdfTerm predicate));
            Assert.IsInstanceOfType<NamedNode>(predicate);
        }
    }

    /// <summary>A collection operand composes through union, so the relate family answers over the merged point set rather than refusing.</summary>
    [TestMethod]
    public async Task CollectionOperandComposesThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.SfContains}>(?c, ?i) AS ?r) WHERE {{ <{Ex}s> <{Ex}collection> ?c . <{Ex}s> <{Ex}innerSquare> ?i }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary><c>geof:isSimple</c> binds over the stored geometries as a total per-kind answer.</summary>
    [TestMethod]
    public async Task IsSimpleBindsThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.IsSimple}>(?a) AS ?square) (<{GeoVocabulary.Geof.IsSimple}>(?g) AS ?bowtie) WHERE {{ <{Ex}s> <{Ex}squareA> ?a . <{Ex}s> <{Ex}bowtie> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "square", "true", Vocabulary.Xsd.Boolean);
        AssertTypedLiteral(solutions[0], "bowtie", "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The four overlay set operations bind geometry literals through the engine.</summary>
    [TestMethod]
    public async Task OverlayOperationsBindGeometryLiterals()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Intersection}>(?a, ?b) AS ?i) (<{GeoVocabulary.Geof.Union}>(?a, ?b) AS ?u) (<{GeoVocabulary.Geof.Difference}>(?a, ?b) AS ?d) (<{GeoVocabulary.Geof.SymDifference}>(?a, ?b) AS ?x) WHERE {{ <{Ex}s> <{Ex}squareA> ?a . <{Ex}s> <{Ex}squareB> ?b }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "i", "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "u", "POLYGON ((0 0, 4 0, 4 2, 6 2, 6 6, 2 6, 2 4, 0 4, 0 0))", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "d", "POLYGON ((0 0, 4 0, 4 2, 2 2, 2 4, 0 4, 0 0))", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "x", "MULTIPOLYGON (((0 0, 4 0, 4 2, 2 2, 2 4, 0 4, 0 0)), ((2 4, 4 4, 4 2, 6 2, 6 6, 2 6, 2 4)))", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A refused operand kind is an expression error, so the BIND leaves its variable unbound while union answers over the same collection.</summary>
    [TestMethod]
    public async Task RefusedOverlayOperandLeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}collection> ?c . <{Ex}s> <{Ex}squareB> ?b BIND(<{GeoVocabulary.Geof.Intersection}>(?c, ?b) AS ?r) BIND(<{GeoVocabulary.Geof.Union}>(?c, ?b) AS ?u) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _), "Intersection refuses a collection operand, so its BIND leaves the variable unbound.");
        Assert.IsTrue(solutions[0].TryGetValue(Variable("u"), out _), "Union accepts the same collection operand.");
    }

    /// <summary>The buffer pair reads its radius in the requested unit, and the convex hull binds its total answer.</summary>
    [TestMethod]
    public async Task BufferAndConvexHullBindThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Buffer}>(?m, -1, <{OgcUnitsOfMeasure.Metre}>) AS ?b) (<{GeoVocabulary.Geof.MetricBuffer}>(?m, -1) AS ?mb) (<{GeoVocabulary.Geof.ConvexHull}>(?g) AS ?h) WHERE {{ <{Ex}s> <{Ex}metricSquare> ?m . <{Ex}s> <{Ex}multi> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "b", $"<{MetricCrs}> POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "mb", $"<{MetricCrs}> POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "h", "LINESTRING (1 2, 3 4)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:centroid</c> and <c>geof:boundingCircle</c> bind their geometry results through the engine, the circle collapsing to its centre point on a single-position operand.</summary>
    [TestMethod]
    public async Task CentroidAndBoundingCircleBindThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.Centroid}>(?a) AS ?c) (<{GeoVocabulary.Geof.Centroid}>(?e) AS ?ec) (<{GeoVocabulary.Geof.BoundingCircle}>(?p) AS ?b) WHERE {{ <{Ex}s> <{Ex}squareA> ?a . <{Ex}s> <{Ex}emptyGeom> ?e . <{Ex}s> <{Ex}pointB> ?p }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "c", "POINT (2 2)", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "ec", "POINT EMPTY", GeoVocabulary.Geo.WktLiteral);
        AssertTypedLiteral(solutions[0], "b", "POINT (3 4)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:concaveHull</c> binds its single-argument form, whose concaveness ratio is the catalog's documented default.</summary>
    [TestMethod]
    public async Task ConcaveHullBindsTheDefaultRatioFormThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.ConcaveHull}>(?g) AS ?d) WHERE {{ <{Ex}s> <{Ex}concavePoints> ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "d", "POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The seam takes one argument, so a second one is a wrong arity — an expression error whose BIND leaves the variable unbound while the row survives.</summary>
    [TestMethod]
    public async Task TwoArgumentConcaveHullLeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}concavePoints> ?g BIND(<{GeoVocabulary.Geof.ConcaveHull}>(?g, 0.5) AS ?r) BIND(<{GeoVocabulary.Geof.ConcaveHull}>(?g) AS ?d) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "The BIND error affects only ?r; the row survives.");
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _), "A second argument is a wrong arity, so the BIND leaves the variable unbound.");
        Assert.IsTrue(solutions[0].TryGetValue(Variable("d"), out _), "The single-argument form answers over the same operand.");
    }

    /// <summary><c>geof:aggUnion</c> folds the implicit group through the engine — the spatial-aggregate requirement's flagship shape: one solution, the union of every matched geometry.</summary>
    [TestMethod]
    public async Task AggUnionFoldsTheImplicitGroupThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.AggUnion}>(?g) AS ?u) WHERE {{ VALUES ?p {{ <{Ex}squareA> <{Ex}squareB> }} <{Ex}s> ?p ?g }}",
            pool,
            Functions);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "The aggregate call alone groups the whole match into one implicit group.");
        AssertTypedLiteral(solutions[0], "u", "POLYGON ((0 0, 4 0, 4 2, 6 2, 6 6, 2 6, 2 4, 0 4, 0 0))", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:aggCentroid</c> folds each explicit group through the engine: one centroid per grouping key.</summary>
    [TestMethod]
    public async Task AggCentroidFoldsPerGroupThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT ?p (<{GeoVocabulary.Geof.AggCentroid}>(?g) AS ?c) WHERE {{ VALUES ?p {{ <{Ex}squareA> <{Ex}squareB> }} <{Ex}s> ?p ?g }} GROUP BY ?p",
            pool,
            Functions);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "One solution per grouping key.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsTrue(solution.TryGetValue(Variable("p"), out RdfTerm key));
            string expected = ((NamedNode)key).Iri.Span.SequenceEqual(Encoding.UTF8.GetBytes(Ex + "squareA")) ? "POINT (2 2)" : "POINT (4 4)";
            AssertTypedLiteral(solution, "c", expected, GeoVocabulary.Geo.WktLiteral);
        }
    }

    /// <summary><c>geof:aggBoundingBox</c> binds the combined envelope of the implicit group through the engine.</summary>
    [TestMethod]
    public async Task AggBoundingBoxBindsTheCombinedEnvelopeThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.AggBoundingBox}>(?g) AS ?b) WHERE {{ VALUES ?p {{ <{Ex}squareA> <{Ex}squareB> }} <{Ex}s> ?p ?g }}",
            pool,
            Functions);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "b", "POLYGON ((0 0, 6 0, 6 6, 0 6, 0 0))", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A group mixing resolved CRS IRIs refuses whole: the aggregate answers the error value, so its variable stays unbound while the solution survives.</summary>
    [TestMethod]
    public async Task MixedCrsGroupLeavesTheAggregateUnbound()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.AggUnion}>(?g) AS ?u) WHERE {{ VALUES ?p {{ <{Ex}squareA> <{Ex}metricSquare> }} <{Ex}s> ?p ?g }}",
            pool,
            Functions);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("u"), out _), "The group-wide one-CRS gate refuses the mixed group.");
    }

    /// <summary>An engine composed without the module keeps the dark posture for the aggregates: the same call stays a scalar function call, errs per row, and never groups.</summary>
    [TestMethod]
    public async Task TheUnregisteredEngineKeepsTheDarkPostureForTheAggregates()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: false).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT (<{GeoVocabulary.Geof.AggUnion}>(?g) AS ?u) WHERE {{ VALUES ?p {{ <{Ex}squareA> <{Ex}squareB> }} <{Ex}s> ?p ?g }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "Without the declared profile no implicit grouping arises; the call is a per-row scalar.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsFalse(solution.TryGetValue(Variable("u"), out _), "The unregistered scalar call errs, leaving the variable unbound.");
        }
    }

    /// <summary>An engine composed without the module keeps the dark-by-default posture: the same <c>geof:</c> FILTER errs on every row and answers empty, the three kernel IRIs included.</summary>
    [TestMethod]
    public async Task TheUnregisteredEngineKeepsTheDarkPostureForTheKernelFunctions()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: false).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}concavePoints> ?g BIND(<{GeoVocabulary.Geof.Centroid}>(?g) AS ?c) BIND(<{GeoVocabulary.Geof.BoundingCircle}>(?g) AS ?b) BIND(<{GeoVocabulary.Geof.ConcaveHull}>(?g) AS ?h) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("c"), out _), "Without the module geof:centroid is unregistered and its BIND errs.");
        Assert.IsFalse(solutions[0].TryGetValue(Variable("b"), out _), "Without the module geof:boundingCircle is unregistered and its BIND errs.");
        Assert.IsFalse(solutions[0].TryGetValue(Variable("h"), out _), "Without the module geof:concaveHull is unregistered and its BIND errs.");
    }

    /// <summary>An engine composed without the module keeps the dark-by-default posture: the same <c>geof:</c> FILTER errs on every row and answers empty.</summary>
    [TestMethod]
    public async Task TheUnregisteredEngineKeepsTheDarkPosture()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: false).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s ?p ?g FILTER(<{GeoVocabulary.Geof.IsEmpty}>(?g)) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions, "Without the module the geof: IRIs are unregistered and every FILTER condition errs.");
    }

    /// <summary>An empty <c>geo:gmlLiteral</c> denotes the empty geometry, so <c>geof:isEmpty</c> binds true through a parsed SELECT over stored data.</summary>
    [TestMethod]
    public async Task IsEmptyBindsTrueOverTheEmptyGmlLiteral()
    {
        SparqlQueryEngine engine = await BuildSerializationEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.IsEmpty}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}emptyGml> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>An empty <c>geo:geoJSONLiteral</c> denotes the empty geometry, so <c>geof:asWKT</c> binds its canonical serialization.</summary>
    [TestMethod]
    public async Task AsWktBindsTheCanonicalEmptyFormOverTheEmptyGeoJsonLiteral()
    {
        SparqlQueryEngine engine = await BuildSerializationEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.AsWkt}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}emptyGeoJson> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "GEOMETRYCOLLECTION EMPTY", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>An empty <c>geo:dggsLiteral</c> denotes the empty geometry, so <c>geof:isEmpty</c> binds true through a parsed SELECT over stored data.</summary>
    [TestMethod]
    public async Task IsEmptyBindsTrueOverTheEmptyDggsLiteral()
    {
        SparqlQueryEngine engine = await BuildSerializationEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.IsEmpty}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}emptyDggs> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>A GML body declaring no coordinate reference system is not a readable operand — the reader takes the system from the root's own declaration and never assumes one — so the BIND leaves its variable unbound while the row survives.</summary>
    [TestMethod]
    public async Task GmlBodyWithoutASystemDeclarationLeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildSerializationEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate(
            $"SELECT * WHERE {{ <{Ex}s> <{Ex}gmlPoint> ?g BIND(<{GeoVocabulary.Geof.AsWkt}>(?g) AS ?r) }}",
            pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "The BIND error affects only ?r; the row survives.");
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _), "A root declaring no system is unreadable, so the BIND leaves the variable unbound.");
    }

    /// <summary>A stored non-empty GML body binds through an accessor: the operand seam reads it with the system its root declares, so the canonical well-known text carries that system's explicit prefix.</summary>
    [TestMethod]
    public async Task AsWktBindsOverAStoredNonEmptyGmlBody()
    {
        SparqlQueryEngine engine = await BuildSerializationEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.AsWkt}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}gmlPointWithSystem> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "<http://www.opengis.net/def/crs/OGC/1.3/CRS84> POINT (1 2)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary><c>geof:asGML</c> binds the typed GML serialization of the stored geometry through a SELECT expression, the root declaring the system the operand resolved to.</summary>
    [TestMethod]
    public async Task AsGmlBindsTheTypedSerializationThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.AsGml}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(
            solutions[0],
            "r",
            "<gml:Point xmlns:gml=\"http://www.opengis.net/gml/3.2\" gml:id=\"g0\" srsName=\"http://www.opengis.net/def/crs/OGC/1.3/CRS84\"><gml:pos>1 2</gml:pos></gml:Point>",
            GeoVocabulary.Geo.GmlLiteral);
    }

    /// <summary><c>geof:asGeoJSON</c> binds the typed GeoJSON serialization of the stored geometry, which the format expresses in CRS84.</summary>
    [TestMethod]
    public async Task AsGeoJsonBindsTheTypedSerializationThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.AsGeoJson}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "{\"type\":\"Point\",\"coordinates\":[1,2]}", GeoVocabulary.Geo.GeoJsonLiteral);
    }

    /// <summary><c>geof:asKML</c> binds the typed KML serialization of the stored geometry, which the format expresses in CRS84.</summary>
    [TestMethod]
    public async Task AsKmlBindsTheTypedSerializationThroughTheEngine()
    {
        SparqlQueryEngine engine = await BuildGeoEngineAsync(registered: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{GeoVocabulary.Geof.AsKml}>(?g) AS ?r) WHERE {{ <{Ex}s> <{Ex}lower> ?g }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        AssertTypedLiteral(solutions[0], "r", "<Point xmlns=\"http://www.opengis.net/kml/2.2\"><coordinates>1,2</coordinates></Point>", GeoVocabulary.Geo.KmlLiteral);
    }

    /// <summary>
    /// Builds the geometry graph — a lowercase-spelled point, an empty point, the 3-4-5 point pair, a
    /// Z-carrying point, a holed polygon under the explicit metric CRS, a two-member multipoint, and a
    /// linestring — over an engine whose expression context carries the module-composed registries, or the
    /// empty defaults for the dark-posture row.
    /// </summary>
    /// <param name="registered">Whether the engine composes the module's registries.</param>
    /// <returns>The engine.</returns>
    private async Task<SparqlQueryEngine> BuildGeoEngineAsync(bool registered)
    {
        List<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "lower"), Wkt("point(1 2)")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "emptyGeom"), Wkt("POINT EMPTY")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "pointA"), Wkt("POINT (0 0)")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "pointB"), Wkt("POINT (3 4)")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "pointZ"), Wkt("POINT Z (1 2 3)")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "metricPolygon"), Wkt($"<{MetricCrs}> POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "multi"), Wkt("MULTIPOINT ((1 2), (3 4))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "line"), Wkt("LINESTRING (0 0, 2 3)")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "squareA"), Wkt("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "squareB"), Wkt("POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "innerSquare"), Wkt("POLYGON ((2 2, 3 2, 3 3, 2 3, 2 2))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "collection"), Wkt("GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "bowtie"), Wkt("POLYGON ((0 0, 4 0, 0 4, 4 4, 0 0))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "metricSquare"), Wkt($"<{MetricCrs}> POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "concavePoints"), Wkt("MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))")),
        ];

        SparqlExpressionContext context = registered
            ? SparqlExpressionContext.CreateDefault(valueDatatypes: Datatypes, extensionFunctions: Functions)
            : SparqlExpressionContext.CreateDefault();

        return await SparqlQueryEngine.BuildAsync(data, expressionContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the serialization-literal graph — an empty <c>geo:gmlLiteral</c>, an empty
    /// <c>geo:geoJSONLiteral</c>, an empty <c>geo:dggsLiteral</c>, a GML point body declaring no system,
    /// and a GML point body declaring the default system — over an engine whose expression context carries
    /// the module-composed registries.
    /// </summary>
    /// <returns>The engine.</returns>
    private async Task<SparqlQueryEngine> BuildSerializationEngineAsync()
    {
        List<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "emptyGml"), Serialization(string.Empty, GeoVocabulary.Geo.GmlLiteral)),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "emptyGeoJson"), Serialization(string.Empty, GeoVocabulary.Geo.GeoJsonLiteral)),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "emptyDggs"), Serialization(string.Empty, GeoVocabulary.Geo.DggsLiteral)),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "gmlPoint"), Serialization("<Point xmlns=\"http://www.opengis.net/gml/3.2\"><pos>1 2</pos></Point>", GeoVocabulary.Geo.GmlLiteral)),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "gmlPointWithSystem"), Serialization("<gml:Point xmlns:gml=\"http://www.opengis.net/gml/3.2\" srsName=\"http://www.opengis.net/def/crs/OGC/1.3/CRS84\"><gml:pos>1 2</gml:pos></gml:Point>", GeoVocabulary.Geo.GmlLiteral))
        ];

        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault(valueDatatypes: Datatypes, extensionFunctions: Functions);

        return await SparqlQueryEngine.BuildAsync(data, expressionContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds a geometry-serialization data literal.</summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    /// <returns>The literal term.</returns>
    private static Literal Serialization(string lexicalForm, Utf8String datatypeIri)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(datatypeIri));
    }
}
