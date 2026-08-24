using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.Tests.Geo.GeoFunctionCalls;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The relate family at the function seam: the DE-9IM pattern test, the twenty-four named topological
/// predicates dispatched by function IRI, and the simplicity test. The seam's own contracts are what
/// these rows pin — the one-CRS gate, the malformed-pattern refusal, the collection-union composition and
/// the empty-collection refusal that survives it, and the split between a defined false result and the
/// expression error value. The underlying matrix and predicate answers are certified by the substrate's
/// own families.
/// </summary>
[TestClass]
internal sealed class GeoRelateFunctionsTests
{
    /// <summary>An explicit test-local CRS IRI.</summary>
    private const string MetricCrs = "http://example.org/def/crs/metric";

    /// <summary>A second explicit test-local CRS IRI, distinct from <see cref="MetricCrs"/>.</summary>
    private const string OtherCrs = "http://example.org/def/crs/other";

    /// <summary>The reference square operand.</summary>
    private const string Square = "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))";

    /// <summary>A square strictly inside <see cref="Square"/>.</summary>
    private const string InnerSquare = "POLYGON ((2 2, 3 2, 3 3, 2 3, 2 2))";

    /// <summary>A square disjoint from <see cref="Square"/>.</summary>
    private const string FarSquare = "POLYGON ((10 10, 12 10, 12 12, 10 12, 10 10))";

    /// <summary>A square containing <see cref="Square"/> with a shared corner — the tangential proper-part fixture.</summary>
    private const string LargeSquare = "POLYGON ((0 0, 8 0, 8 8, 0 8, 0 0))";

    /// <summary>A square overlapping <see cref="Square"/> in area.</summary>
    private const string OverlappingSquare = "POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))";

    /// <summary>A square touching <see cref="Square"/> at one corner.</summary>
    private const string TouchingSquare = "POLYGON ((4 4, 6 4, 6 6, 4 6, 4 4))";

    /// <summary>The diagonal line fixture.</summary>
    private const string Diagonal = "LINESTRING (0 0, 2 2)";

    /// <summary>The anti-diagonal line crossing <see cref="Diagonal"/> at a point.</summary>
    private const string AntiDiagonal = "LINESTRING (0 2, 2 0)";

    /// <summary>The pattern test answers whether the computed matrix matches the requested pattern.</summary>
    /// <param name="firstText">The first operand's lexical form.</param>
    /// <param name="secondText">The second operand's lexical form.</param>
    /// <param name="pattern">The nine-symbol pattern.</param>
    /// <param name="expected">The expected boolean lexical form.</param>
    [TestMethod]
    [DataRow(Square, Square, "2FFF1FFF2", "true", DisplayName = "the equal-polygon matrix matches itself")]
    [DataRow(Square, Square, "FFFFFFFF2", "false", DisplayName = "a foreign matrix does not match")]
    [DataRow(Square, Square, "*********", "true", DisplayName = "nine wildcards match every matrix")]
    [DataRow(Square, FarSquare, "FF*FF****", "true", DisplayName = "the disjoint pattern matches disjoint operands")]
    public void RelateAnswersThePatternTest(string firstText, string secondText, string pattern, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.Relate, Wkt(firstText), Wkt(secondText), Text(pattern)), expected, Vocabulary.Xsd.Boolean);
    }

    /// <summary>A malformed pattern argument is a malformed argument and answers the error value.</summary>
    /// <param name="pattern">The malformed pattern.</param>
    [TestMethod]
    [DataRow("TFFFTFFF", DisplayName = "too short")]
    [DataRow("TFFFTFFFTT", DisplayName = "too long")]
    [DataRow("tFFFTFFFT", DisplayName = "lowercase symbol")]
    [DataRow("TFFFTFFF3", DisplayName = "digit outside the alphabet")]
    public void RelateMalformedPatternAnswersTheErrorValue(string pattern)
    {
        Assert.IsTrue(Invoke(GeoFunctions.Relate, Wkt(Square), Wkt(Square), Text(pattern)).IsError, $"'{pattern}' is a malformed argument.");
    }

    /// <summary>A pattern argument that is not an <c>xsd:string</c> literal errs, as does a wrong argument count.</summary>
    [TestMethod]
    public void RelateForeignArgumentShapesAnswerTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Relate, Wkt(Square), Wkt(Square), Integer("9")).IsError, "A foreign pattern datatype is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.Relate, Wkt(Square), Wkt(Square)).IsError, "A missing pattern is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.SfEquals, Wkt(Square)).IsError, "A missing operand is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.SfEquals, Wkt(Square), Wkt(Square), Wkt(Square)).IsError, "A surplus operand is a wrong arity.");
    }

    /// <summary>
    /// Every named predicate entry answers its pinned verdict over discriminating fixtures: each
    /// entry has a true row on operands where its plausible transposition partner answers false,
    /// and every directional pair carries both directions — so a single transposed mapping in the
    /// dispatch table fails at least one row here.
    /// </summary>
    [TestMethod]
    public void EveryNamedPredicateAnswersItsPinnedVerdict()
    {
        (SparqlFunctionEntry Entry, string FirstText, string SecondText, string Expected)[] pinned =
        [
            (GeoFunctions.SfEquals, "POINT (1 1)", "POINT (1 1)", "true"),
            (GeoFunctions.SfDisjoint, Square, FarSquare, "true"),
            (GeoFunctions.SfIntersects, Square, OverlappingSquare, "true"),
            (GeoFunctions.SfIntersects, Square, FarSquare, "false"),
            (GeoFunctions.SfTouches, Square, TouchingSquare, "true"),
            (GeoFunctions.SfCrosses, "LINESTRING (2 2, 9 9)", Square, "true"),
            (GeoFunctions.SfCrosses, Square, TouchingSquare, "false"),
            (GeoFunctions.SfWithin, InnerSquare, Square, "true"),
            (GeoFunctions.SfWithin, Square, InnerSquare, "false"),
            (GeoFunctions.SfContains, Square, InnerSquare, "true"),
            (GeoFunctions.SfContains, InnerSquare, Square, "false"),
            (GeoFunctions.SfOverlaps, Square, OverlappingSquare, "true"),
            (GeoFunctions.SfOverlaps, Diagonal, AntiDiagonal, "false"),
            (GeoFunctions.EhEquals, Square, Square, "true"),
            (GeoFunctions.EhEquals, "POINT (1 1)", "POINT (1 1)", "false"),
            (GeoFunctions.EhDisjoint, Square, FarSquare, "true"),
            (GeoFunctions.EhMeet, Square, TouchingSquare, "true"),
            (GeoFunctions.EhOverlap, Diagonal, AntiDiagonal, "true"),
            (GeoFunctions.EhCovers, LargeSquare, Square, "true"),
            (GeoFunctions.EhCovers, Square, LargeSquare, "false"),
            (GeoFunctions.EhCoveredBy, Square, LargeSquare, "true"),
            (GeoFunctions.EhCoveredBy, LargeSquare, Square, "false"),
            (GeoFunctions.EhInside, InnerSquare, Square, "true"),
            (GeoFunctions.EhInside, Square, InnerSquare, "false"),
            (GeoFunctions.EhContains, Square, InnerSquare, "true"),
            (GeoFunctions.EhContains, InnerSquare, Square, "false"),
            (GeoFunctions.Rcc8Eq, Square, Square, "true"),
            (GeoFunctions.Rcc8Eq, "POINT (1 1)", "POINT (1 1)", "false"),
            (GeoFunctions.Rcc8Dc, Square, FarSquare, "true"),
            (GeoFunctions.Rcc8Dc, Square, TouchingSquare, "false"),
            (GeoFunctions.Rcc8Ec, Square, TouchingSquare, "true"),
            (GeoFunctions.Rcc8Ec, Square, FarSquare, "false"),
            (GeoFunctions.Rcc8Po, Square, OverlappingSquare, "true"),
            (GeoFunctions.Rcc8Tppi, LargeSquare, Square, "true"),
            (GeoFunctions.Rcc8Tppi, Square, LargeSquare, "false"),
            (GeoFunctions.Rcc8Tpp, Square, LargeSquare, "true"),
            (GeoFunctions.Rcc8Tpp, LargeSquare, Square, "false"),
            (GeoFunctions.Rcc8Ntpp, InnerSquare, Square, "true"),
            (GeoFunctions.Rcc8Ntpp, Square, InnerSquare, "false"),
            (GeoFunctions.Rcc8Ntppi, Square, InnerSquare, "true"),
            (GeoFunctions.Rcc8Ntppi, InnerSquare, Square, "false"),
        ];

        foreach((SparqlFunctionEntry entry, string firstText, string secondText, string expected) in pinned)
        {
            AssertLexical(
                Invoke(entry, Wkt(firstText), Wkt(secondText)),
                expected,
                Vocabulary.Xsd.Boolean);
        }
    }

    /// <summary>Every named predicate entry answers a bound boolean, so no catalog entry is left undispatched.</summary>
    [TestMethod]
    public void EveryNamedPredicateEntryAnswersABoundBoolean()
    {
        SparqlFunctionEntry[] predicates =
        [
            GeoFunctions.SfEquals, GeoFunctions.SfDisjoint, GeoFunctions.SfIntersects, GeoFunctions.SfTouches,
            GeoFunctions.SfCrosses, GeoFunctions.SfWithin, GeoFunctions.SfContains, GeoFunctions.SfOverlaps,
            GeoFunctions.EhEquals, GeoFunctions.EhDisjoint, GeoFunctions.EhMeet, GeoFunctions.EhOverlap,
            GeoFunctions.EhCovers, GeoFunctions.EhCoveredBy, GeoFunctions.EhInside, GeoFunctions.EhContains,
            GeoFunctions.Rcc8Eq, GeoFunctions.Rcc8Dc, GeoFunctions.Rcc8Ec, GeoFunctions.Rcc8Po,
            GeoFunctions.Rcc8Tppi, GeoFunctions.Rcc8Tpp, GeoFunctions.Rcc8Ntpp, GeoFunctions.Rcc8Ntppi,
        ];

        Assert.HasCount(24, predicates, "The named predicate roster is the full twenty-four.");

        foreach(SparqlFunctionEntry predicate in predicates)
        {
            SparqlFunctionResult result = Invoke(predicate, Wkt(Square), Wkt(InnerSquare));

            Assert.IsFalse(result.IsError, $"{predicate.FunctionIri}: a non-collection pair always evaluates.");
            Assert.IsInstanceOfType<Literal>(result.Term);
            Assert.IsTrue(((Literal)result.Term).Datatype.Iri.Span.SequenceEqual(Vocabulary.Xsd.Boolean.Span), $"{predicate.FunctionIri}: the answer is a boolean.");
        }
    }

    /// <summary>An out-of-domain predicate answers a defined false result, never the error value.</summary>
    [TestMethod]
    public void OutOfDomainPredicateAnswersDefinedFalse()
    {
        AssertLexical(Invoke(GeoFunctions.SfCrosses, Wkt(Square), Wkt(InnerSquare)), "false", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfOverlaps, Wkt("LINESTRING (0 0, 2 2)"), Wkt(Square)), "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The naming families diverge exactly where their patterns do, and the seam carries the divergence through unchanged.</summary>
    [TestMethod]
    public void NamingFamiliesDivergeThroughTheSeam()
    {
        AssertLexical(Invoke(GeoFunctions.SfEquals, Wkt("POINT (1 1)"), Wkt("POINT (1 1)")), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.EhEquals, Wkt("POINT (1 1)"), Wkt("POINT (1 1)")), "false", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.Rcc8Eq, Wkt("POINT (1 1)"), Wkt("POINT (1 1)")), "false", Vocabulary.Xsd.Boolean);

        AssertLexical(Invoke(GeoFunctions.EhOverlap, Wkt("LINESTRING (0 0, 2 2)"), Wkt("LINESTRING (0 2, 2 0)")), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfOverlaps, Wkt("LINESTRING (0 0, 2 2)"), Wkt("LINESTRING (0 2, 2 0)")), "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>Empty non-collection operands compute: only the disjoint names hold, and nothing errs.</summary>
    [TestMethod]
    public void EmptyOperandsComputeThroughTheSeam()
    {
        AssertLexical(Invoke(GeoFunctions.SfDisjoint, Wkt("POINT EMPTY"), Wkt(Square)), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.EhDisjoint, Wkt("POINT EMPTY"), Wkt(Square)), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfIntersects, Wkt("POINT EMPTY"), Wkt(Square)), "false", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfEquals, Wkt("POINT EMPTY"), Wkt("LINESTRING EMPTY")), "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>
    /// A collection operand composes: the seam unions its members first, so the relate family answers
    /// exactly as it does for the merged geometry, in either operand position and at any nesting depth.
    /// </summary>
    [TestMethod]
    public void CollectionOperandsComposeThroughUnion()
    {
        AssertLexical(
            Invoke(GeoFunctions.SfContains, Wkt("GEOMETRYCOLLECTION (" + Square + ")"), Wkt(InnerSquare)),
            "true",
            Vocabulary.Xsd.Boolean);
        AssertLexical(
            Invoke(GeoFunctions.SfWithin, Wkt(InnerSquare), Wkt("GEOMETRYCOLLECTION (" + Square + ")")),
            "true",
            Vocabulary.Xsd.Boolean);
        AssertLexical(
            Invoke(GeoFunctions.SfDisjoint, Wkt("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (" + Square + "))"), Wkt(FarSquare)),
            "true",
            Vocabulary.Xsd.Boolean);
        AssertLexical(
            Invoke(GeoFunctions.Relate, Wkt("GEOMETRYCOLLECTION (" + Square + ")"), Wkt(Square), Text("2FFF1FFF2")),
            "true",
            Vocabulary.Xsd.Boolean);
    }

    /// <summary>
    /// A collection whose members overlap composes to the merged arrangement, so the seam answers over one
    /// resolved point set rather than any member's own.
    /// </summary>
    [TestMethod]
    public void OverlappingCollectionMembersComposeToTheMergedPointSet()
    {
        const string overlapping = "GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)), POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2)))";

        AssertLexical(Invoke(GeoFunctions.SfContains, Wkt(overlapping), Wkt("POINT (5 5)")), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfContains, Wkt(overlapping), Wkt("POINT (1 1)")), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfDisjoint, Wkt(overlapping), Wkt("POINT (9 9)")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The empty collection stays refused: the union composition resolves it to itself, which the relate engine refuses.</summary>
    [TestMethod]
    public void EmptyCollectionOperandAnswersTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.SfDisjoint, Wkt("GEOMETRYCOLLECTION EMPTY"), Wkt(Square)).IsError, "The empty collection is refused in the first position.");
        Assert.IsTrue(Invoke(GeoFunctions.SfDisjoint, Wkt(Square), Wkt("GEOMETRYCOLLECTION EMPTY")).IsError, "The empty collection is refused in the second position.");
        Assert.IsTrue(Invoke(GeoFunctions.Relate, Wkt("GEOMETRYCOLLECTION EMPTY"), Wkt(Square), Text("*********")).IsError, "The pattern form refuses it the same way.");
        Assert.IsTrue(Invoke(GeoFunctions.SfDisjoint, Wkt(""), Wkt(Square)).IsError, "The empty lexical form denotes the empty collection and is refused alike.");
    }

    /// <summary>Operands whose resolved CRS IRIs differ answer the error value: the catalog transforms no coordinates.</summary>
    [TestMethod]
    public void DifferingCoordinateReferenceSystemsAnswerTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.SfEquals, Wkt($"<{MetricCrs}> {Square}"), Wkt($"<{OtherCrs}> {Square}")).IsError, "The predicate family gates on one CRS.");
        Assert.IsTrue(Invoke(GeoFunctions.Relate, Wkt($"<{MetricCrs}> {Square}"), Wkt(Square), Text("*********")).IsError, "The pattern form gates on one CRS.");
        AssertLexical(Invoke(GeoFunctions.SfEquals, Wkt($"<{MetricCrs}> {Square}"), Wkt($"<{MetricCrs}> {Square}")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The simplicity test is total per kind, collections included, and the empty lexical form answers vacuously simple.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected boolean lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1 1)", "true")]
    [DataRow("MULTIPOINT ((1 1), (1 1))", "false")]
    [DataRow("LINESTRING (0 0, 2 2, 2 0, 0 2)", "false")]
    [DataRow("LINESTRING (0 0, 2 0, 2 2, 0 2, 0 0)", "true")]
    [DataRow("POLYGON ((0 0, 4 0, 0 4, 4 4, 0 0))", "false")]
    [DataRow("GEOMETRYCOLLECTION (LINESTRING (0 0, 2 0, 1 0))", "false")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "true")]
    [DataRow("", "true")]
    public void IsSimpleAnswersPerKind(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.IsSimple, Wkt(lexicalForm)), expected, Vocabulary.Xsd.Boolean);
    }
}
