using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.Tests.Geo.GeoFunctionCalls;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The constructive family at the function seam: the four overlay set operations, the buffer pair, the
/// convex hull, the centroid, the smallest enclosing circle, and the concave hull. These rows pin the
/// seam's own contracts — the split between a refused operand kind or
/// malformed argument (the expression error value) and a degenerate but defined geometric result (an
/// ordinary literal), the one-CRS gate with the result's CRS carriage, the buffer radius converting
/// from its requested unit into coordinate units before the substrate sees it, the seam's own
/// polygonization of the circle the substrate answers as a centre and a radius, and the seam's
/// documented default concaveness ratio. The point-set answers
/// themselves are certified by the substrate's own families.
/// </summary>
[TestClass]
internal sealed class GeoConstructiveFunctionsTests
{
    /// <summary>An explicit test-local CRS IRI whose linear unit the catalog's metre convention takes at face value.</summary>
    private const string MetricCrs = "http://example.org/def/crs/metric";

    /// <summary>A second explicit test-local CRS IRI, distinct from <see cref="MetricCrs"/>.</summary>
    private const string OtherCrs = "http://example.org/def/crs/other";

    /// <summary>The CRS84 default IRI, spelled explicitly in a lexical form.</summary>
    private const string Crs84 = "http://www.opengis.net/def/crs/OGC/1.3/CRS84";

    /// <summary>The reference square operand.</summary>
    private const string Square = "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))";

    /// <summary>A square overlapping <see cref="Square"/> in area.</summary>
    private const string OverlappingSquare = "POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))";

    /// <summary>A square disjoint from <see cref="Square"/>.</summary>
    private const string FarSquare = "POLYGON ((10 10, 12 10, 12 12, 10 12, 10 10))";

    /// <summary>The five-position operand whose concave hull differs from its convex hull.</summary>
    private const string ConcaveOperand = "MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))";

    /// <summary>The metre units argument as an IRI term.</summary>
    private static NamedNode MetreIri { get; } = new(OgcUnitsOfMeasure.Metre);

    /// <summary>The degree units argument as an IRI term.</summary>
    private static NamedNode DegreeIri { get; } = new(OgcUnitsOfMeasure.Degree);

    /// <summary>The four overlay operations answer their point sets as canonical geometry literals.</summary>
    [TestMethod]
    public void OverlayOperationsAnswerTheirPointSets()
    {
        AssertLexical(
            Invoke(GeoFunctions.Intersection, Wkt(Square), Wkt(OverlappingSquare)),
            "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Union, Wkt(Square), Wkt(OverlappingSquare)),
            "POLYGON ((0 0, 4 0, 4 2, 6 2, 6 6, 2 6, 2 4, 0 4, 0 0))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Difference, Wkt(Square), Wkt(OverlappingSquare)),
            "POLYGON ((0 0, 4 0, 4 2, 2 2, 2 4, 0 4, 0 0))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.SymDifference, Wkt(Square), Wkt(OverlappingSquare)),
            "MULTIPOLYGON (((0 0, 4 0, 4 2, 2 2, 2 4, 0 4, 0 0)), ((2 4, 4 4, 4 2, 6 2, 6 6, 2 6, 2 4)))",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>
    /// A degenerate but defined result is an ordinary literal, never the error value: the empty
    /// intersection of disjoint operands and the empty difference of a covered operand both answer the
    /// typed empty their operation's dimension forces.
    /// </summary>
    [TestMethod]
    public void DegenerateButDefinedResultsAreOrdinaryLiterals()
    {
        AssertLexical(Invoke(GeoFunctions.Intersection, Wkt(Square), Wkt(FarSquare)), "POLYGON EMPTY", GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Difference, Wkt("LINESTRING (2 2, 3 3)"), Wkt("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))")),
            "LINESTRING EMPTY",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.Difference, Wkt(Square), Wkt(Square)), "POLYGON EMPTY", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A collection operand is refused by intersection, difference, and symmetric difference — the empty collection and the empty lexical form included.</summary>
    [TestMethod]
    public void CollectionOperandsAreRefusedExceptForUnion()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Intersection, Wkt("GEOMETRYCOLLECTION (POINT (1 1))"), Wkt(Square)).IsError, "Intersection refuses a collection first operand.");
        Assert.IsTrue(Invoke(GeoFunctions.Intersection, Wkt(Square), Wkt("GEOMETRYCOLLECTION (POINT (1 1))")).IsError, "Intersection refuses a collection second operand.");
        Assert.IsTrue(Invoke(GeoFunctions.Difference, Wkt("GEOMETRYCOLLECTION (POINT (1 1))"), Wkt(Square)).IsError, "Difference refuses a collection operand.");
        Assert.IsTrue(Invoke(GeoFunctions.SymDifference, Wkt(Square), Wkt("GEOMETRYCOLLECTION (POINT (1 1))")).IsError, "SymDifference refuses a collection operand.");
        Assert.IsTrue(Invoke(GeoFunctions.Intersection, Wkt("GEOMETRYCOLLECTION EMPTY"), Wkt(Square)).IsError, "The empty collection is refused by root kind.");
        Assert.IsTrue(Invoke(GeoFunctions.Intersection, Wkt(""), Wkt(Square)).IsError, "The empty lexical form denotes the empty collection and is refused alike.");
    }

    /// <summary>Union accepts collections in either position and at any nesting depth, resolving them through the stratified member fold.</summary>
    [TestMethod]
    public void UnionAcceptsCollectionOperands()
    {
        AssertLexical(
            Invoke(
                GeoFunctions.Union,
                Wkt("GEOMETRYCOLLECTION (POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0)))"),
                Wkt("POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))")),
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(
                GeoFunctions.Union,
                Wkt("POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))"),
                Wkt("GEOMETRYCOLLECTION (POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0)))")),
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>
    /// The one unresolvable result dimension answers the empty collection as an ordinary literal, and that
    /// literal is then refused by the relate family exactly as its operands would be.
    /// </summary>
    [TestMethod]
    public void UnionOfEmptyCollectionsAnswersTheEmptyCollectionLiteral()
    {
        AssertLexical(
            Invoke(GeoFunctions.Union, Wkt("GEOMETRYCOLLECTION EMPTY"), Wkt("GEOMETRYCOLLECTION EMPTY")),
            "GEOMETRYCOLLECTION EMPTY",
            GeoVocabulary.Geo.WktLiteral);
        Assert.IsTrue(
            Invoke(GeoFunctions.SfDisjoint, Wkt("GEOMETRYCOLLECTION EMPTY"), Wkt(Square)).IsError,
            "That one unresolvable result is refused by relate, the falsifying direction of the typed-empty rule.");
    }

    /// <summary>The convex hull is total: every operand kind, every emptiness, and every collection depth has a defined hull.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected hull lexical form.</param>
    [TestMethod]
    [DataRow("MULTIPOINT ((0 0), (4 0), (4 4), (0 4), (2 2))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")]
    [DataRow("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (4 0, 4 4), POINT (0 4))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT EMPTY")]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    [DataRow("", "POINT EMPTY")]
    [DataRow("LINESTRING (0 0, 1 1, 3 3)", "LINESTRING (0 0, 3 3)")]
    [DataRow("POINT (1 2)", "POINT (1 2)")]
    public void ConvexHullIsTotal(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.ConvexHull, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The centroid is total: every operand kind, every emptiness, and every collection depth has a defined centroid.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected centroid lexical form.</param>
    [TestMethod]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "POINT (2 2)")]
    [DataRow("GEOMETRYCOLLECTION (POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0)), POINT (-50 0))", "POINT (1 1)")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT EMPTY")]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    [DataRow("", "POINT EMPTY")]
    [DataRow("MULTIPOINT ((0 0), (0 0), (3 0))", "POINT (1 0)")]
    [DataRow("POINT (1 2)", "POINT (1 2)")]
    public void CentroidIsTotal(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.Centroid, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>
    /// The smallest enclosing circle collapses at the seam exactly where the substrate refuses or answers a
    /// zero radius: no positions answer the empty point, coincident positions answer their centre point.
    /// </summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected collapsed lexical form.</param>
    [TestMethod]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT EMPTY")]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    [DataRow("", "POINT EMPTY")]
    [DataRow("POINT (1 2)", "POINT (1 2)")]
    [DataRow("MULTIPOINT ((1 1), (1 1))", "POINT (1 1)")]
    public void BoundingCircleCollapsesOnAZeroRadius(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.BoundingCircle, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>
    /// A positive radius renders as the seam's certified circumscribing circle polygon: eight segments
    /// per quadrant close into a thirty-three position ring whose coverage of the whole disc is verified
    /// per emission, every rendered position sits on-or-outside the certified circle and strictly inside
    /// the published-bound circle — the exact excess sign decides both, no tolerance anywhere — and the
    /// square's four corners, which sit on the answered circle, are all within the rendered polygon.
    /// </summary>
    [TestMethod]
    public void BoundingCircleRendersTheCircumscribingCirclePolygon()
    {
        SparqlFunctionResult result = Invoke(GeoFunctions.BoundingCircle, Wkt(Square));

        Assert.IsFalse(result.IsError, "The bounding circle of the square is an ordinary literal.");
        Assert.IsInstanceOfType<Literal>(result.Term);
        Literal literal = (Literal)result.Term;
        Assert.IsTrue(literal.Datatype.Iri.Span.SequenceEqual(GeoVocabulary.Geo.WktLiteral.Span), "The result is a geometry literal.");
        Assert.IsTrue(WktGeometryReader.TryRead(literal.Value.ToString(), out FlatGeometry circle, out _), "The rendered circle parses back.");
        Assert.AreEqual(GeometryKind.Polygon, circle.Kind, "A positive radius renders as a polygon.");
        Assert.HasCount(33, circle.Vertices, "Eight segments per quadrant close into a thirty-three position ring.");

        Assert.IsTrue(WktGeometryReader.TryRead(Square, out FlatGeometry operand, out _), "The square operand parses.");
        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle certified), "The square answers its certified circle.");

        double boundRadius = certified.Radius * 1.0049;

        foreach(Point2d vertex in circle.Vertices)
        {
            Assert.IsGreaterThan(-1, ExactCircleExcess.Sign(vertex, certified.Center, certified.Radius), "Every rendered position sits on-or-outside the certified circle, exactly.");
            Assert.IsLessThan(0, ExactCircleExcess.Sign(vertex, certified.Center, boundRadius), "Every rendered position sits strictly inside the published-bound circle, exactly.");
        }

        string rendered = literal.Value.ToString();

        AssertLexical(Invoke(GeoFunctions.SfWithin, Wkt("POINT (0 0)"), Wkt(rendered)), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfWithin, Wkt("POINT (4 0)"), Wkt(rendered)), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfWithin, Wkt("POINT (4 4)"), Wkt(rendered)), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfWithin, Wkt("POINT (0 4)"), Wkt(rendered)), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>
    /// The rendering seam's operand gate: ordinates beyond the certified predicates' documented
    /// magnitude wall answer the error value before the circle computes, and a two-point operand
    /// whose circle's radius sits below the exactness wall answers the error value from the
    /// verification — refusal, never an unverified polygon.
    /// </summary>
    [TestMethod]
    public void BoundingCircleRefusesOperandsOutsideTheCertifiedWalls()
    {
        string beyondWall = "2" + new string('0', 75);
        string tinySeparation = "0." + new string('0', 60) + "1";

        Assert.IsTrue(
            Invoke(GeoFunctions.BoundingCircle, Wkt($"MULTIPOINT ((-{beyondWall} 0), ({beyondWall} 0))")).IsError,
            "An ordinate beyond the operand wall refuses before the circle computes.");
        Assert.IsTrue(
            Invoke(GeoFunctions.BoundingCircle, Wkt($"MULTIPOINT ((0 0), ({tinySeparation} 0))")).IsError,
            "A radius below the exactness wall refuses at the verification.");
    }

    /// <summary>
    /// The containment contract holds where the operand points fall between tessellation vertices: the
    /// two-point operand's smallest enclosing circle is its diametral circle, centre <c>(0.5, 1)</c> and
    /// radius <c>sqrt(5) / 2</c>, and both operand points lie on that circle at directions strictly
    /// inside a tessellation step, where a polygon whose vertices sat on the circle would exclude them.
    /// Rendered at the circumscribed radius the polygon contains both.
    /// </summary>
    [TestMethod]
    public void BoundingCircleContainsOperandPointsBetweenTessellationVertices()
    {
        SparqlFunctionResult result = Invoke(GeoFunctions.BoundingCircle, Wkt("MULTIPOINT ((0 0), (1 2))"));

        Assert.IsFalse(result.IsError, "The bounding circle of the two-point operand is an ordinary literal.");
        Assert.IsInstanceOfType<Literal>(result.Term);
        string rendered = ((Literal)result.Term).Value.ToString();

        AssertLexical(Invoke(GeoFunctions.SfWithin, Wkt("POINT (0 0)"), Wkt(rendered)), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfWithin, Wkt("POINT (1 2)"), Wkt(rendered)), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>
    /// The concave hull seam is single-argument and its concaveness ratio is the catalog's documented
    /// default: the seam answers exactly what the kernel answers at that ratio, and the kernel's loose
    /// extreme answers the convex hull the seam gives for the same operand.
    /// </summary>
    [TestMethod]
    public void ConcaveHullUsesTheDocumentedDefaultRatio()
    {
        AssertLexical(
            Invoke(GeoFunctions.ConcaveHull, Wkt(ConcaveOperand)),
            "POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))",
            GeoVocabulary.Geo.WktLiteral);

        Assert.IsTrue(WktGeometryReader.TryRead(ConcaveOperand, out FlatGeometry operand, out _), "The concave operand parses.");
        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, 0.5, out FlatGeometry defaulted), "The kernel computes at the documented default ratio.");
        Assert.AreEqual(
            "POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in defaulted),
            "The seam's documented default is the kernel's one-half.");

        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, 1.0, out FlatGeometry loose), "The kernel computes at the loose extreme.");
        AssertLexical(
            Invoke(GeoFunctions.ConvexHull, Wkt(ConcaveOperand)),
            WktGeometryWriter.WriteString(in loose),
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The concave hull is point-set-total: refusal never depends on the operand, only on the ratio.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected hull lexical form.</param>
    [TestMethod]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT EMPTY")]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    [DataRow("", "POINT EMPTY")]
    [DataRow("POINT (1 2)", "POINT (1 2)")]
    [DataRow("LINESTRING (0 0, 1 1, 3 3)", "LINESTRING (0 0, 3 3)")]
    [DataRow("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (4 0, 4 4), POINT (0 4))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")]
    public void ConcaveHullIsPointSetTotal(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.ConcaveHull, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>Malformed geometry operands and wrong arities answer the error value across the three unary kernels.</summary>
    [TestMethod]
    public void UnaryKernelsRefuseMalformedOperandsAndArities()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Centroid, Wkt("POINT (1")).IsError, "Malformed well-known text is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.BoundingCircle, Wkt("CIRCULARSTRING (0 0, 1 1, 2 0)")).IsError, "An uncertified curve tag is outside the domain.");
        Assert.IsTrue(
            Invoke(GeoFunctions.ConcaveHull, new Literal(Utf8Strings.From(Square), new NamedNode(Vocabulary.Xsd.String))).IsError,
            "A foreign datatype is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.Centroid).IsError, "A missing operand is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.BoundingCircle, Wkt(Square), Wkt(Square)).IsError, "A surplus operand is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.ConcaveHull).IsError, "A missing operand is a wrong arity.");
        Assert.IsTrue(
            Invoke(GeoFunctions.ConcaveHull, Wkt(ConcaveOperand), Double("0.5")).IsError,
            "A ratio argument is a wrong arity: the concave hull seam is single-argument.");
    }

    /// <summary>
    /// The buffer radius is denominated in the requested unit and converts to coordinate units before the
    /// offset computes, so the unit rules gate it exactly as they gate the measure family: metre answers
    /// for an explicit CRS, degree answers for the CRS84 default, and the mismatched pairings never answer.
    /// </summary>
    [TestMethod]
    public void BufferReadsItsRadiusInTheRequestedUnit()
    {
        AssertLexical(
            Invoke(GeoFunctions.Buffer, Wkt($"<{MetricCrs}> {Square}"), Double("-1"), MetreIri),
            $"<{MetricCrs}> POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Buffer, Wkt(Square), Double("-1"), DegreeIri),
            "POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))",
            GeoVocabulary.Geo.WktLiteral);

        Assert.IsTrue(Invoke(GeoFunctions.Buffer, Wkt(Square), Double("-1"), MetreIri).IsError, "Metres over the known-degrees default would fabricate a magnitude.");
        Assert.IsTrue(Invoke(GeoFunctions.Buffer, Wkt($"<{MetricCrs}> {Square}"), Double("-1"), DegreeIri).IsError, "Degrees answer only for the CRS84 default.");
        Assert.IsTrue(
            Invoke(GeoFunctions.Buffer, Wkt($"<{MetricCrs}> {Square}"), Double("-1"), new NamedNode(Utf8Strings.From("http://example.org/uom/furlong"))).IsError,
            "An unrecognized units IRI never answers.");
    }

    /// <summary>The metric buffer is the same path with metres fixed, so the known-degrees default errs.</summary>
    [TestMethod]
    public void MetricBufferFixesMetres()
    {
        AssertLexical(
            Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Double("-1")),
            $"<{MetricCrs}> POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))",
            GeoVocabulary.Geo.WktLiteral);
        Assert.IsTrue(Invoke(GeoFunctions.MetricBuffer, Wkt(Square), Double("-1")).IsError, "The CRS84 default has no metre answer.");
    }

    /// <summary>A buffer that erodes its operand away answers the declared empty polygon as an ordinary literal.</summary>
    [TestMethod]
    public void ErodedAwayBufferIsAnOrdinaryLiteral()
    {
        AssertLexical(
            Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Double("-3")),
            $"<{MetricCrs}> POLYGON EMPTY",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> POINT (1 1)"), Double("-1")),
            $"<{MetricCrs}> POLYGON EMPTY",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A radius that is not a finite number, or not a numeric literal at all, is a malformed argument.</summary>
    [TestMethod]
    public void MalformedRadiusAnswersTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Double("NaN")).IsError, "A non-finite radius is a malformed argument.");
        Assert.IsTrue(Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Double("one")).IsError, "An unparseable lexical form is a malformed argument.");
        Assert.IsTrue(Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Text("-1")).IsError, "A foreign radius datatype is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}")).IsError, "A missing radius is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.Buffer, Wkt($"<{MetricCrs}> {Square}"), Double("-1")).IsError, "The unit-parameterized form needs its units argument.");
    }

    /// <summary>An integer radius is admissible: the numeric domain is not narrowed to <c>xsd:double</c>.</summary>
    [TestMethod]
    public void IntegerRadiusIsAdmissible()
    {
        AssertLexical(
            Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Integer("-1")),
            $"<{MetricCrs}> POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>An ill-typed integer radius — a fractional lexical form under <c>xsd:integer</c> — is a malformed argument, not a value.</summary>
    [TestMethod]
    public void IllTypedIntegerRadiusAnswersTheErrorValue()
    {
        Assert.IsTrue(
            Invoke(GeoFunctions.MetricBuffer, Wkt($"<{MetricCrs}> {Square}"), Integer("-1.5")).IsError,
            "A fractional lexical form under xsd:integer is ill-typed and refuses under the integer grammar.");
    }

    /// <summary>Binary operands whose resolved CRS IRIs differ answer the error value: the catalog transforms no coordinates.</summary>
    [TestMethod]
    public void DifferingCoordinateReferenceSystemsAnswerTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Intersection, Wkt($"<{MetricCrs}> {Square}"), Wkt($"<{OtherCrs}> {Square}")).IsError, "Intersection gates on one CRS.");
        Assert.IsTrue(Invoke(GeoFunctions.Union, Wkt($"<{MetricCrs}> {Square}"), Wkt(Square)).IsError, "An explicit CRS and the default are two systems.");
    }

    /// <summary>A binary result carries the explicit CRS prefix its operands resolved to; two defaulted operands answer an implicit form.</summary>
    [TestMethod]
    public void BinaryResultsCarryTheResolvedCrs()
    {
        AssertLexical(
            Invoke(GeoFunctions.Intersection, Wkt($"<{MetricCrs}> {Square}"), Wkt($"<{MetricCrs}> {OverlappingSquare}")),
            $"<{MetricCrs}> POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Intersection, Wkt(Square), Wkt(OverlappingSquare)),
            "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>
    /// A mixed explicit-CRS84/defaulted pair passes the one-CRS gate — the defaulted source always
    /// resolves to the CRS84 IRI, so mixed sources can only ever share it — and the result spells the
    /// prefix because one operand asserted it. The denoted system is identical either way; only the
    /// spelling is at stake, and this row pins the spelling as a decision.
    /// </summary>
    [TestMethod]
    public void MixedCrs84SourcesGateAndCarryTheExplicitSpelling()
    {
        AssertLexical(
            Invoke(GeoFunctions.Intersection, Wkt($"<{Crs84}> {Square}"), Wkt(OverlappingSquare)),
            $"<{Crs84}> POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Intersection, Wkt(Square), Wkt($"<{Crs84}> {OverlappingSquare}")),
            $"<{Crs84}> POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The unary constructive results carry their operand's explicit CRS prefix.</summary>
    [TestMethod]
    public void UnaryResultsCarryTheExplicitCrsPrefix()
    {
        AssertLexical(
            Invoke(GeoFunctions.ConvexHull, Wkt($"<{MetricCrs}> MULTIPOINT ((0 0), (4 0), (2 5))")),
            $"<{MetricCrs}> POLYGON ((0 0, 4 0, 2 5, 0 0))",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Centroid, Wkt($"<{MetricCrs}> {Square}")),
            $"<{MetricCrs}> POINT (2 2)",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.BoundingCircle, Wkt($"<{MetricCrs}> POINT (1 2)")),
            $"<{MetricCrs}> POINT (1 2)",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.ConcaveHull, Wkt($"<{MetricCrs}> {ConcaveOperand}")),
            $"<{MetricCrs}> POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>Malformed geometry operands and wrong arities answer the error value across the constructive family.</summary>
    [TestMethod]
    public void MalformedOperandsAnswerTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Intersection, Wkt("POINT (1"), Wkt(Square)).IsError, "Malformed well-known text is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.ConvexHull, Wkt("CIRCULARSTRING (0 0, 1 1, 2 0)")).IsError, "An uncertified curve tag is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.ConvexHull, new Literal(Utf8Strings.From(Square), new NamedNode(Vocabulary.Xsd.String))).IsError, "A foreign datatype is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.Union, Wkt(Square)).IsError, "A missing operand is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.ConvexHull, Wkt(Square), Wkt(Square)).IsError, "A surplus operand is a wrong arity.");
    }
}
