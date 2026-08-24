using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Transforms;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.Tests.Geo.GeoFunctionCalls;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The <c>geof:</c> function catalog's contracts on the catalog surface itself: the module composition,
/// the operand and error-value discipline (malformed WKT, foreign datatypes, wrong arities, undefined
/// operations), the canonical serialization with CRS carriage, the planar unit rules — a certified-roster
/// system answers exactly its declared unit, a system outside the roster answers the metre unit by the
/// explicit-CRS convention and never the degree unit, an unrecognized units IRI never answers — the
/// axis-order resolution of the coordinate extrema, the member conventions of <c>geof:numGeometries</c> and
/// <c>geof:geometryN</c>, and the serialization datatypes on both faces. On the reading face: the empty GML,
/// GeoJSON, and KML literal denotes the empty geometry in the default CRS, a non-empty body materializes
/// through its format's codec — a GML body under the system its root declares, in that system's declared
/// axis order and under that system's one canonical IRI whichever accepted spelling the document wrote,
/// GeoJSON and KML under the system their formats fix — and a body the codec refuses answers the error
/// value. On the writing face: <c>geof:asGML</c> answers the geometry in its own system and always declares
/// that system outright, while <c>geof:asGeoJSON</c> and <c>geof:asKML</c> answer in CRS84, converting first
/// where the operand's system differs and refusing wherever the conversion or the format itself has no
/// answer. The empty <c>geo:dggsLiteral</c> form reads the same way at exact zero length only — its
/// whitespace-only form carries no IRI prefix, so it stays unreadable, matching the registered datatype's
/// invalid verdict.
/// </summary>
[TestClass]
internal sealed class GeoFunctionsTests
{
    /// <summary>
    /// Installs the GeoJSON read binding the operand seam ingests through, so the direct-evaluation
    /// rows run under the same composition a registered host provides.
    /// </summary>
    static GeoFunctionsTests()
    {
        GeoFunctions.GeoJsonReader = GeoJsonGeometryReader.TryRead;
    }

    /// <summary>An explicit test-local CRS IRI whose linear unit the catalog's metre convention takes at face value.</summary>
    private const string MetricCrs = "http://example.org/def/crs/metric";

    /// <summary>The CRS84 IRI, spelled explicitly in a lexical form.</summary>
    private const string Crs84 = "http://www.opengis.net/def/crs/OGC/1.3/CRS84";

    /// <summary>The EPSG:4326 canonical IRI — the transform roster's latitude-first geographic member.</summary>
    private const string Epsg4326Crs = "http://www.opengis.net/def/crs/EPSG/0/4326";

    /// <summary>The EPSG:3857 canonical IRI — the transform roster's Web Mercator member.</summary>
    private const string WebMercatorCrs = "http://www.opengis.net/def/crs/EPSG/0/3857";

    /// <summary>The <c>geo:gmlLiteral</c> datatype IRI, spelled for the serialization-datatype rows.</summary>
    private const string GmlLiteral = "http://www.opengis.net/ont/geosparql#gmlLiteral";

    /// <summary>The <c>geo:geoJSONLiteral</c> datatype IRI, spelled for the serialization-datatype rows.</summary>
    private const string GeoJsonLiteral = "http://www.opengis.net/ont/geosparql#geoJSONLiteral";

    /// <summary>The <c>geo:kmlLiteral</c> datatype IRI, spelled for the serialization-datatype rows.</summary>
    private const string KmlLiteral = "http://www.opengis.net/ont/geosparql#kmlLiteral";

    /// <summary>The <c>geo:dggsLiteral</c> datatype IRI, spelled for the serialization-datatype rows.</summary>
    private const string DggsLiteral = "http://www.opengis.net/ont/geosparql#dggsLiteral";

    /// <summary>The house <c>a5Literal</c> datatype IRI, spelled for the flavour rows.</summary>
    private const string A5Literal = "https://lumoin.com/veritas/dggs/a5Literal";

    /// <summary>A house-flavour cell-set literal over one non-straddling cell — the resolution-21 cell at the origin.</summary>
    private const string HouseCellsLiteral = "<https://lumoin.com/veritas/dggs/a5> CELLS (4f05dccc726e0000)";

    /// <summary>The canonical GML root attribute run through the system declaration's opening quote — every emitted root carries it, the system declaration included.</summary>
    private const string GmlRootAttributePrefix = " xmlns:gml=\"http://www.opengis.net/gml/3.2\" gml:id=\"g0\" srsName=\"";

    /// <summary>The KML root's default namespace declaration, which every emitted root carries.</summary>
    private const string KmlRootDeclaration = " xmlns=\"http://www.opengis.net/kml/2.2\"";

    /// <summary>A GML point document declaring the default system.</summary>
    private const string GmlPointDocument = "<gml:Point " + GmlTestDocuments.NamespaceDeclaration + " srsName=\"" + Crs84 + "\">" + GmlTestDocuments.PointBody + "</gml:Point>";

    /// <summary>A GML line-string document declaring the default system.</summary>
    private const string GmlLineDocument = "<gml:LineString " + GmlTestDocuments.NamespaceDeclaration + " srsName=\"" + Crs84 + "\"><gml:posList>0 0 1 1 2 0</gml:posList></gml:LineString>";

    /// <summary>A GML polygon document declaring the default system.</summary>
    private const string GmlPolygonDocument = "<gml:Polygon " + GmlTestDocuments.NamespaceDeclaration + " srsName=\"" + Crs84 + "\">" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon>";

    /// <summary>The ring body of the latitude-first box: a rectangle four wide on the north axis and three on the east axis, written latitude first.</summary>
    private const string LatitudeFirstBoxBody = "<gml:exterior><gml:LinearRing><gml:posList>10 20 14 20 14 23 10 23 10 20</gml:posList></gml:LinearRing></gml:exterior>";

    /// <summary>The latitude-first box under the canonical EPSG:4326 IRI.</summary>
    private const string GmlLatitudeFirstBox = "<gml:Polygon " + GmlTestDocuments.NamespaceDeclaration + " srsName=\"" + Epsg4326Crs + "\">" + LatitudeFirstBoxBody + "</gml:Polygon>";

    /// <summary>The same box under the urn spelling of the same system.</summary>
    private const string GmlLatitudeFirstBoxUrn = "<gml:Polygon " + GmlTestDocuments.NamespaceDeclaration + " srsName=\"urn:ogc:def:crs:EPSG::4326\">" + LatitudeFirstBoxBody + "</gml:Polygon>";

    /// <summary>A GeoJSON point document.</summary>
    private const string GeoJsonPointDocument = GeoJsonTestDocuments.CanonicalPoint;

    /// <summary>A GeoJSON line-string document.</summary>
    private const string GeoJsonLineDocument = "{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1],[2,0]]}";

    /// <summary>A GeoJSON polygon document.</summary>
    private const string GeoJsonPolygonDocument = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[4,0],[4,4],[0,0]]]}";

    /// <summary>A KML point document.</summary>
    private const string KmlPointDocument = "<Point " + KmlTestDocuments.NamespaceDeclaration + ">" + KmlTestDocuments.PointCoordinates + "</Point>";

    /// <summary>A KML line-string document.</summary>
    private const string KmlLineDocument = "<LineString " + KmlTestDocuments.NamespaceDeclaration + "><coordinates>0,0 1,1 2,0</coordinates></LineString>";

    /// <summary>A KML polygon document.</summary>
    private const string KmlPolygonDocument = "<Polygon " + KmlTestDocuments.NamespaceDeclaration + ">" + KmlTestDocuments.SquarePolygonBody + "</Polygon>";

    /// <summary>The metre units argument as an IRI term.</summary>
    private static NamedNode MetreIri { get; } = new(OgcUnitsOfMeasure.Metre);

    /// <summary>The degree units argument as an IRI term.</summary>
    private static NamedNode DegreeIri { get; } = new(OgcUnitsOfMeasure.Degree);

    /// <summary>Builds a serialization-datatype argument: a literal under the <c>geo:gmlLiteral</c>, <c>geo:geoJSONLiteral</c>, <c>geo:kmlLiteral</c>, or <c>geo:dggsLiteral</c> datatype.</summary>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <returns>The literal term.</returns>
    private static Literal Serialized(string datatypeIri, string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(Utf8Strings.From(datatypeIri)));
    }

    /// <summary>The module registers every catalog entry, every registration is accepted, and each entry resolves from the built registry.</summary>
    [TestMethod]
    public void ModuleRegistersEveryCatalogEntry()
    {
        SparqlFunctionRegistryBuilder builder = new();
        GeoExtensionModule.RegisterFunctions(builder, GeoJsonGeometryReader.TryRead);

        Assert.HasCount(GeoFunctions.All.Count, builder.Outcomes);
        foreach(SparqlFunctionRegistration outcome in builder.Outcomes)
        {
            Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, outcome.Kind, $"{outcome.FunctionIri}: the catalog must register cleanly.");
        }

        SparqlFunctionRegistry registry = builder.Build();
        foreach(SparqlFunctionEntry entry in GeoFunctions.All)
        {
            if(entry.Scalar is not null)
            {
                Assert.IsTrue(registry.TryGet(entry.FunctionIri, out _), $"{entry.FunctionIri}: the built registry must resolve the entry's scalar face.");
            }

            if(entry.Aggregate is not null)
            {
                Assert.IsTrue(registry.TryGetAggregate(entry.FunctionIri, out _), $"{entry.FunctionIri}: the built registry must resolve the entry's aggregate face.");
            }
        }
    }

    /// <summary>The module registers the <c>geo:wktLiteral</c> value datatype, proven by the duplicate registration declining.</summary>
    [TestMethod]
    public void ModuleRegistersTheWktLiteralDatatype()
    {
        ValueDatatypeRegistryBuilder builder = new();
        GeoExtensionModule.RegisterValueDatatypes(builder);

        Assert.AreNotEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(WktLiteralValueDatatype.Instance).Kind, "The module's registration must already occupy the datatype IRI.");
    }

    /// <summary>The serializer answers the writer's canonical form regardless of the input's spelling, and an empty lexical form denotes the empty geometry.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The canonical output lexical form.</param>
    [TestMethod]
    [DataRow("point(1 2)", "POINT (1 2)")]
    [DataRow("MULTIPOINT(1 2,3 4)", "MULTIPOINT ((1 2), (3 4))")]
    [DataRow("TRIANGLE ((0 0, 1 0, 0 1, 0 0))", "POLYGON ((0 0, 1 0, 0 1, 0 0))")]
    [DataRow("", "GEOMETRYCOLLECTION EMPTY")]
    public void AsWktAnswersTheCanonicalForm(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.AsWkt, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The serializer re-emits an explicit CRS prefix with one separating space ahead of the canonical body.</summary>
    [TestMethod]
    public void AsWktCarriesTheExplicitCrsPrefix()
    {
        AssertLexical(Invoke(GeoFunctions.AsWkt, Wkt($"<{MetricCrs}>   point(1 2)")), $"<{MetricCrs}> POINT (1 2)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>Lexical and structural violations answer the error value: wrong arity in a position list, unbalanced parentheses, a glued ordinate marker, a one-position linestring, an uncertified curve tag, and an EWKT SRID prefix.</summary>
    /// <param name="lexicalForm">The malformed lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1)")]
    [DataRow("POINT (1 2")]
    [DataRow("POINTZ(1 2 3)")]
    [DataRow("LINESTRING (1 2)")]
    [DataRow("CIRCULARSTRING (0 0, 1 1, 2 0)")]
    [DataRow("SRID=4326;POINT (1 2)")]
    public void MalformedBodiesAnswerTheErrorValue(string lexicalForm)
    {
        Assert.IsTrue(Invoke(GeoFunctions.AsWkt, Wkt(lexicalForm)).IsError, $"'{lexicalForm}' must answer the expression error value.");
    }

    /// <summary>A geometry argument that is not a <c>geo:wktLiteral</c> literal errs, as does a wrong argument count.</summary>
    [TestMethod]
    public void ForeignArgumentShapesAnswerTheErrorValue()
    {
        Assert.IsTrue(Invoke(GeoFunctions.AsWkt, new Literal(Utf8Strings.From("POINT (1 2)"), new NamedNode(Vocabulary.Xsd.String))).IsError, "A foreign datatype is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.AsWkt, new NamedNode(Utf8Strings.From("http://example.org/geometry"))).IsError, "An IRI term is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.AsWkt).IsError, "A missing argument is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.AsWkt, Wkt("POINT (1 2)"), Wkt("POINT (1 2)")).IsError, "A surplus argument is a wrong arity.");
    }

    /// <summary>The SRID accessor answers the resolved CRS IRI as <c>xsd:anyURI</c>: the CRS84 default for a bare form, the named IRI for a prefixed form.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expectedIri">The expected CRS IRI.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", Crs84)]
    [DataRow("<" + MetricCrs + "> POINT (1 2)", MetricCrs)]
    public void GetSridAnswersTheResolvedCrs(string lexicalForm, string expectedIri)
    {
        AssertLexical(Invoke(GeoFunctions.GetSrid, Wkt(lexicalForm)), expectedIri, Vocabulary.Xsd.AnyUri);
    }

    /// <summary>The emptiness test distinguishes the empty point set from populated geometries; the empty lexical form denotes an empty geometry.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected boolean lexical form.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", "true")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "true")]
    [DataRow("", "true")]
    [DataRow("POINT (1 2)", "false")]
    public void IsEmptyAnswersTheEmptinessOfThePointSet(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.IsEmpty, Wkt(lexicalForm)), expected, Vocabulary.Xsd.Boolean);
    }

    /// <summary>The ordinate-carriage tests read the Z and M markers independently.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expectedIs3D">The expected <c>geof:is3D</c> answer.</param>
    /// <param name="expectedIsMeasured">The expected <c>geof:isMeasured</c> answer.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "false", "false")]
    [DataRow("POINT Z (1 2 3)", "true", "false")]
    [DataRow("POINT M (1 2 4)", "false", "true")]
    [DataRow("POINT ZM (1 2 3 4)", "true", "true")]
    public void OrdinateCarriageAnswersTheMarkers(string lexicalForm, string expectedIs3D, string expectedIsMeasured)
    {
        AssertLexical(Invoke(GeoFunctions.Is3D, Wkt(lexicalForm)), expectedIs3D, Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.IsMeasured, Wkt(lexicalForm)), expectedIsMeasured, Vocabulary.Xsd.Boolean);
    }

    /// <summary>The topological dimension is kind-intrinsic, typed empties keep their kind's answer, the memberless collection answers −1, and a mixed collection answers the member maximum.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected integer lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "0")]
    [DataRow("LINESTRING (0 0, 1 1)", "1")]
    [DataRow("POLYGON ((0 0, 1 0, 0 1, 0 0))", "2")]
    [DataRow("POINT EMPTY", "0")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "-1")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", "1")]
    public void DimensionAnswersTheTopologicalDimension(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.Dimension, Wkt(lexicalForm)), expected, Vocabulary.Xsd.Integer);
    }

    /// <summary>The coordinate dimension counts the carried ordinates: two, plus Z, plus M.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected integer lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "2")]
    [DataRow("POINT Z (1 2 3)", "3")]
    [DataRow("POINT M (1 2 4)", "3")]
    [DataRow("POINT ZM (1 2 3 4)", "4")]
    public void CoordinateDimensionCountsCarriedOrdinates(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.CoordinateDimension, Wkt(lexicalForm)), expected, Vocabulary.Xsd.Integer);
    }

    /// <summary>The spatial dimension counts spatial axes only: M carries none.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected integer lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "2")]
    [DataRow("POINT Z (1 2 3)", "3")]
    [DataRow("POINT M (1 2 4)", "2")]
    public void SpatialDimensionCountsSpatialAxes(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.SpatialDimension, Wkt(lexicalForm)), expected, Vocabulary.Xsd.Integer);
    }

    /// <summary>The type accessor answers the Simple Features class IRI of the root kind as <c>xsd:anyURI</c>; the non-SF surface tags normalize on read.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expectedIri">The expected Simple Features class IRI.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "http://www.opengis.net/ont/sf#Point")]
    [DataRow("LINESTRING (0 0, 1 1)", "http://www.opengis.net/ont/sf#LineString")]
    [DataRow("POLYGON ((0 0, 1 0, 0 1, 0 0))", "http://www.opengis.net/ont/sf#Polygon")]
    [DataRow("MULTIPOINT ((1 2))", "http://www.opengis.net/ont/sf#MultiPoint")]
    [DataRow("MULTILINESTRING ((0 0, 1 1))", "http://www.opengis.net/ont/sf#MultiLineString")]
    [DataRow("MULTIPOLYGON (((0 0, 1 0, 0 1, 0 0)))", "http://www.opengis.net/ont/sf#MultiPolygon")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2))", "http://www.opengis.net/ont/sf#GeometryCollection")]
    [DataRow("TRIANGLE ((0 0, 1 0, 0 1, 0 0))", "http://www.opengis.net/ont/sf#Polygon")]
    public void GeometryTypeAnswersTheSfClassIri(string lexicalForm, string expectedIri)
    {
        AssertLexical(Invoke(GeoFunctions.GeometryType, Wkt(lexicalForm)), expectedIri, Vocabulary.Xsd.AnyUri);
    }

    /// <summary>The envelope applies the conventional degenerate collapse: a real box answers the counter-clockwise rectangle, one degenerate axis a two-point linestring, two a point, and the empty point set the empty point.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected envelope lexical form.</param>
    [TestMethod]
    [DataRow("LINESTRING (0 0, 2 3)", "POLYGON ((0 0, 2 0, 2 3, 0 3, 0 0))")]
    [DataRow("LINESTRING (1 0, 1 5)", "LINESTRING (1 0, 1 5)")]
    [DataRow("MULTIPOINT ((2 3), (2 3))", "POINT (2 3)")]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    public void EnvelopeAnswersTheDegenerateCollapse(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.Envelope, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The envelope result carries the input's explicit CRS prefix.</summary>
    [TestMethod]
    public void EnvelopeCarriesTheExplicitCrsPrefix()
    {
        AssertLexical(Invoke(GeoFunctions.Envelope, Wkt($"<{MetricCrs}> LINESTRING (0 0, 2 3)")), $"<{MetricCrs}> POLYGON ((0 0, 2 0, 2 3, 0 3, 0 0))", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The boundary answers the canonical per-dimension result kinds: puntal input the empty collection, open curves their odd-parity endpoints, closed curves the empty multipoint, and polygonal input every ring.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected boundary lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "GEOMETRYCOLLECTION EMPTY")]
    [DataRow("LINESTRING (0 0, 1 1)", "MULTIPOINT ((0 0), (1 1))")]
    [DataRow("LINESTRING (0 0, 1 0, 1 1, 0 0)", "MULTIPOINT EMPTY")]
    [DataRow("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 2 4, 2 2))", "MULTILINESTRING ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 2 4, 2 2))")]
    public void BoundaryAnswersTheCanonicalPerDimensionKinds(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.Boundary, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The boundary of a heterogeneous collection is undefined and answers the error value.</summary>
    [TestMethod]
    public void BoundaryOfAHeterogeneousCollectionErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Boundary, Wkt("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))")).IsError);
    }

    /// <summary>Under the CRS84 default the degree unit answers the planar magnitude.</summary>
    [TestMethod]
    public void DistanceAnswersInDegreesUnderCrs84()
    {
        AssertLexical(Invoke(GeoFunctions.Distance, Wkt("POINT (0 0)"), Wkt("POINT (3 4)"), DegreeIri), "5", Vocabulary.Xsd.Double);
    }

    /// <summary>Under the CRS84 default — whose coordinate unit is fixed as degree — a metre-denominated answer would be a fabrication, so the metre unit errs.</summary>
    [TestMethod]
    public void DistanceWithMetreUnitsUnderCrs84Errs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Distance, Wkt("POINT (0 0)"), Wkt("POINT (3 4)"), MetreIri).IsError);
    }

    /// <summary>Under an explicit CRS the metre convention answers the planar magnitude.</summary>
    [TestMethod]
    public void MetricDistanceAnswersUnderAnExplicitCrs()
    {
        AssertLexical(Invoke(GeoFunctions.MetricDistance, Wkt($"<{MetricCrs}> POINT (0 0)"), Wkt($"<{MetricCrs}> POINT (3 4)")), "5", Vocabulary.Xsd.Double);
    }

    /// <summary>The metric family errs for CRS84 whether the CRS is defaulted or spelled explicitly — both resolve to the known-degrees IRI.</summary>
    /// <param name="firstForm">The first operand's lexical form.</param>
    /// <param name="secondForm">The second operand's lexical form.</param>
    [TestMethod]
    [DataRow("POINT (0 0)", "POINT (3 4)")]
    [DataRow("<" + Crs84 + "> POINT (0 0)", "<" + Crs84 + "> POINT (3 4)")]
    public void MetricDistanceUnderCrs84Errs(string firstForm, string secondForm)
    {
        Assert.IsTrue(Invoke(GeoFunctions.MetricDistance, Wkt(firstForm), Wkt(secondForm)).IsError);
    }

    /// <summary>The catalog performs no coordinate transformation, so operands under differing CRS IRIs err.</summary>
    [TestMethod]
    public void DistanceAcrossDifferingCrsIrisErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.MetricDistance, Wkt($"<{MetricCrs}> POINT (0 0)"), Wkt("POINT (3 4)")).IsError);
    }

    /// <summary>The point-set distance to an empty operand is undefined and errs.</summary>
    [TestMethod]
    public void DistanceToAnEmptyOperandErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.MetricDistance, Wkt($"<{MetricCrs}> POINT EMPTY"), Wkt($"<{MetricCrs}> POINT (3 4)")).IsError);
    }

    /// <summary>An unrecognized units IRI never answers, and the degree unit answers only for declared-degree roster systems — a non-roster CRS's degree request errs.</summary>
    [TestMethod]
    public void UnitsOutsideTheRecognizedRulesErr()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Distance, Wkt("POINT (0 0)"), Wkt("POINT (3 4)"), new NamedNode(Utf8Strings.From("http://example.org/uom/furlong"))).IsError, "An unrecognized units IRI errs.");
        Assert.IsTrue(Invoke(GeoFunctions.Distance, Wkt($"<{MetricCrs}> POINT (0 0)"), Wkt($"<{MetricCrs}> POINT (3 4)"), DegreeIri).IsError, "The degree unit never answers for a system outside the certified roster.");
    }

    /// <summary>The units argument is also accepted as an <c>xsd:anyURI</c> literal naming the unit IRI.</summary>
    [TestMethod]
    public void UnitsArgumentAcceptsTheAnyUriLiteralForm()
    {
        Literal metreLiteral = new(OgcUnitsOfMeasure.Metre, new NamedNode(Vocabulary.Xsd.AnyUri));

        AssertLexical(Invoke(GeoFunctions.Distance, Wkt($"<{MetricCrs}> POINT (0 0)"), Wkt($"<{MetricCrs}> POINT (3 4)"), metreLiteral), "5", Vocabulary.Xsd.Double);
    }

    /// <summary>The planar area counts a polygon's shell minus its holes.</summary>
    [TestMethod]
    public void MetricAreaAnswersShellMinusHolesUnderAnExplicitCrs()
    {
        AssertLexical(Invoke(GeoFunctions.MetricArea, Wkt($"<{MetricCrs}> POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))")), "96", Vocabulary.Xsd.Double);
    }

    /// <summary>The metric area errs under the CRS84 default.</summary>
    [TestMethod]
    public void MetricAreaUnderTheCrs84DefaultErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.MetricArea, Wkt("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")).IsError);
    }

    /// <summary>Under CRS84 the degree unit reads as the squared linear unit for the area magnitude.</summary>
    [TestMethod]
    public void AreaWithDegreeUnitsUnderCrs84Answers()
    {
        AssertLexical(Invoke(GeoFunctions.Area, Wkt("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))"), DegreeIri), "1", Vocabulary.Xsd.Double);
    }

    /// <summary>The length sums lineal parts only — a collection's polygon contributes nothing to it.</summary>
    [TestMethod]
    public void MetricLengthSumsLinealPartsOnly()
    {
        AssertLexical(Invoke(GeoFunctions.MetricLength, Wkt($"<{MetricCrs}> GEOMETRYCOLLECTION (LINESTRING (0 0, 3 4), POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0)))")), "5", Vocabulary.Xsd.Double);
    }

    /// <summary>The perimeter sums polygonal rings only, shells and holes alike — a collection's linestring contributes nothing to it.</summary>
    [TestMethod]
    public void MetricPerimeterSumsPolygonalRingsOnly()
    {
        AssertLexical(Invoke(GeoFunctions.MetricPerimeter, Wkt($"<{MetricCrs}> GEOMETRYCOLLECTION (LINESTRING (0 0, 3 4), POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2)))")), "48", Vocabulary.Xsd.Double);
    }

    /// <summary>Length and perimeter partition the segment families: a polygon has zero length and a linestring zero perimeter.</summary>
    [TestMethod]
    public void LengthAndPerimeterPartitionTheSegmentFamilies()
    {
        AssertLexical(Invoke(GeoFunctions.Length, Wkt("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))"), DegreeIri), "0", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Perimeter, Wkt("LINESTRING (0 0, 3 4)"), DegreeIri), "0", Vocabulary.Xsd.Double);
    }

    /// <summary>The coordinate extrema range over every position of every part.</summary>
    [TestMethod]
    public void CoordinateExtremaAnswerOverEveryPosition()
    {
        Literal geometry = Wkt("GEOMETRYCOLLECTION (POINT (5 -1), LINESTRING (0 0, 2 3))");

        AssertLexical(Invoke(GeoFunctions.MinX, geometry), "0", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxX, geometry), "5", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MinY, geometry), "-1", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxY, geometry), "3", Vocabulary.Xsd.Double);
    }

    /// <summary>The coordinate extrema of the empty point set are undefined and err.</summary>
    [TestMethod]
    public void CoordinateExtremaOfTheEmptyPointSetErr()
    {
        Literal geometry = Wkt("POINT EMPTY");

        Assert.IsTrue(Invoke(GeoFunctions.MinX, geometry).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MaxX, geometry).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MinY, geometry).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MaxY, geometry).IsError);
    }

    /// <summary>The Z extrema range over the carried ordinates only — members carrying no Z leave NaN slots, which are skipped.</summary>
    [TestMethod]
    public void ZExtremaSkipUncarriedSlots()
    {
        Literal geometry = Wkt("GEOMETRYCOLLECTION (POINT Z (1 2 7), POINT (9 9), POINT Z (0 0 -2))");

        AssertLexical(Invoke(GeoFunctions.MaxZ, geometry), "7", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MinZ, geometry), "-2", Vocabulary.Xsd.Double);
    }

    /// <summary>The Z extrema of a geometry carrying no Z ordinate err.</summary>
    [TestMethod]
    public void ZExtremaOfAGeometryCarryingNoZErr()
    {
        Assert.IsTrue(Invoke(GeoFunctions.MaxZ, Wkt("POINT (1 2)")).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MinZ, Wkt("POINT (1 2)")).IsError);
    }

    /// <summary>The member count follows the SQL/MM convention: children for a collection, elements for a multi kind, one for an atomic geometry — typed empties included — and zero for empty multi kinds and collections.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="expected">The expected member count.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "1")]
    [DataRow("POINT EMPTY", "1")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "2")]
    [DataRow("MULTIPOLYGON (((0 0, 1 0, 0 1, 0 0)), ((5 5, 8 5, 8 8, 5 8, 5 5), (6 6, 7 6, 6 7, 6 6)))", "2")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", "2")]
    [DataRow("MULTIPOINT EMPTY", "0")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "0")]
    public void NumGeometriesCountsMembers(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.NumGeometries, Wkt(lexicalForm)), expected, Vocabulary.Xsd.Integer);
    }

    /// <summary>The one-based member extraction answers the member as its own canonical literal: an atomic geometry at one, a multi kind's element with its rings and ordinates, and a collection child extracted as a whole subtree.</summary>
    /// <param name="lexicalForm">The input lexical form.</param>
    /// <param name="memberNumber">The one-based member number.</param>
    /// <param name="expected">The expected member lexical form.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "1", "POINT (1 2)")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "2", "POINT (3 4)")]
    [DataRow("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", "1", "LINESTRING (0 0, 1 1)")]
    [DataRow("MULTIPOLYGON (((0 0, 1 0, 0 1, 0 0)), ((5 5, 8 5, 8 8, 5 8, 5 5), (6 6, 7 6, 6 7, 6 6)))", "2", "POLYGON ((5 5, 8 5, 8 8, 5 8, 5 5), (6 6, 7 6, 6 7, 6 6))")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))", "2", "GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1))")]
    [DataRow("MULTIPOINT Z ((1 2 3), (4 5 6))", "1", "POINT Z (1 2 3)")]
    public void GeometryNExtractsMembers(string lexicalForm, string memberNumber, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.GeometryN, Wkt(lexicalForm), Integer(memberNumber)), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>Member numbers outside [1, count] err, as does a member-number argument that is not an <c>xsd:integer</c> literal.</summary>
    /// <param name="memberNumber">The out-of-range member number.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("3")]
    [DataRow("-1")]
    public void GeometryNOutOfRangeErrs(string memberNumber)
    {
        Assert.IsTrue(Invoke(GeoFunctions.GeometryN, Wkt("MULTIPOINT ((1 2), (3 4))"), Integer(memberNumber)).IsError);
    }

    /// <summary>A member-number argument outside the <c>xsd:integer</c> domain errs, and the empty multi kind has no members.</summary>
    [TestMethod]
    public void GeometryNForeignNumberOrEmptyInputErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.GeometryN, Wkt("MULTIPOINT ((1 2))"), new Literal(Utf8Strings.From("1"), new NamedNode(Vocabulary.Xsd.String))).IsError, "A foreign number datatype errs.");
        Assert.IsTrue(Invoke(GeoFunctions.GeometryN, Wkt("MULTIPOINT EMPTY"), Integer("1")).IsError, "An empty multi kind has no members.");
    }

    /// <summary>The extracted member's literal carries the input's explicit CRS prefix.</summary>
    [TestMethod]
    public void GeometryNCarriesTheExplicitCrsPrefix()
    {
        AssertLexical(Invoke(GeoFunctions.GeometryN, Wkt($"<{MetricCrs}> MULTIPOINT ((1 2), (3 4))"), Integer("1")), $"<{MetricCrs}> POINT (1 2)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The empty lexical form of a serialization datatype denotes the empty geometry, so the emptiness test answers true under each of the three.</summary>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    [TestMethod]
    [DataRow(GmlLiteral)]
    [DataRow(GeoJsonLiteral)]
    [DataRow(KmlLiteral)]
    public void EmptySerializationLiteralIsEmpty(string datatypeIri)
    {
        AssertLexical(Invoke(GeoFunctions.IsEmpty, Serialized(datatypeIri, "")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>An all-whitespace lexical form is the empty form of a serialization datatype too.</summary>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    [TestMethod]
    [DataRow(GmlLiteral)]
    [DataRow(GeoJsonLiteral)]
    [DataRow(KmlLiteral)]
    public void WhitespaceOnlySerializationLiteralIsEmpty(string datatypeIri)
    {
        AssertLexical(Invoke(GeoFunctions.IsEmpty, Serialized(datatypeIri, "   ")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The empty form of a serialization datatype serializes as the canonical empty-geometry form; the CRS is the defaulted one, so no prefix is carried.</summary>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    [TestMethod]
    [DataRow(GmlLiteral)]
    [DataRow(GeoJsonLiteral)]
    [DataRow(KmlLiteral)]
    public void EmptySerializationLiteralAsWktAnswersTheEmptyForm(string datatypeIri)
    {
        AssertLexical(Invoke(GeoFunctions.AsWkt, Serialized(datatypeIri, "")), "GEOMETRYCOLLECTION EMPTY", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A non-empty body under a serialization datatype materializes through its format's codec: the geometry is not empty, it serializes as the canonical well-known text under the system its provenance carries, and it answers its own topological dimension.</summary>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    /// <param name="lexicalForm">The non-empty lexical form.</param>
    /// <param name="expectedWkt">The expected canonical well-known text, prefixed where the operand's system is explicit.</param>
    /// <param name="expectedDimension">The expected topological dimension.</param>
    [TestMethod]
    [DataRow(GmlLiteral, GmlPointDocument, "<" + Crs84 + "> POINT (1 2)", "0")]
    [DataRow(GmlLiteral, GmlLineDocument, "<" + Crs84 + "> LINESTRING (0 0, 1 1, 2 0)", "1")]
    [DataRow(GmlLiteral, GmlPolygonDocument, "<" + Crs84 + "> POLYGON ((0 0, 4 0, 4 4, 0 0))", "2")]
    [DataRow(GeoJsonLiteral, GeoJsonPointDocument, "POINT (1 2)", "0")]
    [DataRow(GeoJsonLiteral, GeoJsonLineDocument, "LINESTRING (0 0, 1 1, 2 0)", "1")]
    [DataRow(GeoJsonLiteral, GeoJsonPolygonDocument, "POLYGON ((0 0, 4 0, 4 4, 0 0))", "2")]
    [DataRow(KmlLiteral, KmlPointDocument, "POINT (1 2)", "0")]
    [DataRow(KmlLiteral, KmlLineDocument, "LINESTRING (0 0, 1 1, 2 0)", "1")]
    [DataRow(KmlLiteral, KmlPolygonDocument, "POLYGON ((0 0, 4 0, 4 4, 0 0))", "2")]
    public void NonEmptySerializationLiteralReadsAtTheOperandSeam(string datatypeIri, string lexicalForm, string expectedWkt, string expectedDimension)
    {
        Literal operand = Serialized(datatypeIri, lexicalForm);

        AssertLexical(Invoke(GeoFunctions.IsEmpty, operand), "false", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.AsWkt, operand), expectedWkt, GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.Dimension, operand), expectedDimension, Vocabulary.Xsd.Integer);
    }

    /// <summary>A body its format's codec refuses is an unreadable operand and answers the error value: a GML root declaring no system or a system outside the certified roster, a GeoJSON position of one element or an unknown type tag, and a KML fragment in no namespace or under a feature envelope.</summary>
    /// <param name="datatypeIri">The serialization datatype's IRI.</param>
    /// <param name="lexicalForm">The refused lexical form.</param>
    [TestMethod]
    [DataRow(GmlLiteral, "<gml:Point " + GmlTestDocuments.NamespaceDeclaration + ">" + GmlTestDocuments.PointBody + "</gml:Point>")]
    [DataRow(GmlLiteral, "<gml:Point " + GmlTestDocuments.NamespaceDeclaration + " srsName=\"http://www.opengis.net/def/crs/EPSG/0/25833\">" + GmlTestDocuments.PointBody + "</gml:Point>")]
    [DataRow(GeoJsonLiteral, "{\"type\":\"Point\",\"coordinates\":[1]}")]
    [DataRow(GeoJsonLiteral, "{\"type\":\"Circle\",\"coordinates\":[1,2]}")]
    [DataRow(KmlLiteral, "<Point>" + KmlTestDocuments.PointCoordinates + "</Point>")]
    [DataRow(KmlLiteral, "<Placemark " + KmlTestDocuments.NamespaceDeclaration + "><Point>" + KmlTestDocuments.PointCoordinates + "</Point></Placemark>")]
    public void RefusedSerializationBodyAnswersTheErrorValue(string datatypeIri, string lexicalForm)
    {
        Assert.IsTrue(Invoke(GeoFunctions.IsEmpty, Serialized(datatypeIri, lexicalForm)).IsError, $"'{lexicalForm}' must answer the expression error value.");
    }

    /// <summary>An ingested latitude-first GML literal carries its coordinates in the declared axis order, so the extrema resolve the east axis through that order — the longitude ordinate answers <c>geof:minX</c> — and the SRID accessor answers the declared system.</summary>
    [TestMethod]
    public void ExtremaOverAnIngestedLatitudeFirstGmlLiteralResolveTheDeclaredAxes()
    {
        Literal box = Serialized(GmlLiteral, GmlLatitudeFirstBox);

        AssertLexical(Invoke(GeoFunctions.MinX, box), "20", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxX, box), "23", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MinY, box), "10", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxY, box), "14", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.GetSrid, box), Epsg4326Crs, Vocabulary.Xsd.AnyUri);
    }

    /// <summary>The urn spelling of a declared system names the same system under the one canonical IRI: the urn-form literal answers the identical SRID, extrema, and unit-bearing magnitudes as its HTTP-spelled twin, and the two forms compare under the one-CRS gate rather than refusing as differing systems.</summary>
    [TestMethod]
    public void TheUrnSpelledGmlLiteralAnswersAsItsHttpSpelledTwin()
    {
        Literal httpSpelled = Serialized(GmlLiteral, GmlLatitudeFirstBox);
        Literal urnSpelled = Serialized(GmlLiteral, GmlLatitudeFirstBoxUrn);

        AssertLexical(Invoke(GeoFunctions.GetSrid, urnSpelled), Epsg4326Crs, Vocabulary.Xsd.AnyUri);
        AssertLexical(Invoke(GeoFunctions.MinX, urnSpelled), "20", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxY, urnSpelled), "14", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Area, urnSpelled, DegreeIri), "12", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Perimeter, urnSpelled, DegreeIri), "14", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Area, httpSpelled, DegreeIri), "12", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Perimeter, httpSpelled, DegreeIri), "14", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.SfEquals, httpSpelled, urnSpelled), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The empty <c>geo:gmlLiteral</c> form carries no CRS of its own, so the SRID accessor answers the CRS84 default.</summary>
    [TestMethod]
    public void GetSridOverEmptySerializationLiteralAnswersTheDefault()
    {
        AssertLexical(Invoke(GeoFunctions.GetSrid, Serialized(GmlLiteral, "")), Crs84, Vocabulary.Xsd.AnyUri);
    }

    /// <summary>The empty <c>geo:dggsLiteral</c> form denotes the empty geometry, so the emptiness test answers true.</summary>
    [TestMethod]
    public void EmptyDggsLiteralIsEmpty()
    {
        AssertLexical(Invoke(GeoFunctions.IsEmpty, Serialized(DggsLiteral, "")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The empty <c>geo:dggsLiteral</c> form serializes as the canonical empty-geometry form; the CRS is the defaulted one, so no prefix is carried.</summary>
    [TestMethod]
    public void EmptyDggsLiteralAsWktAnswersTheEmptyForm()
    {
        AssertLexical(Invoke(GeoFunctions.AsWkt, Serialized(DggsLiteral, "")), "GEOMETRYCOLLECTION EMPTY", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A whitespace-only <c>geo:dggsLiteral</c> form is not the empty form — it carries no IRI prefix and its grammar gives it no interpretation — so it answers the error value where the three markup datatypes read the empty geometry.</summary>
    [TestMethod]
    public void WhitespaceOnlyDggsLiteralErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.IsEmpty, Serialized(DggsLiteral, "   ")).IsError);
    }

    /// <summary>A non-empty <c>geo:dggsLiteral</c> body naming a FOREIGN grid is outside the operand domain — its geometry data is formulated per the identified DGGS, which the house cannot decode — so it answers the error value.</summary>
    [TestMethod]
    public void NonEmptyDggsLiteralErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.IsEmpty, Serialized(DggsLiteral, "<https://w3id.org/dggs/auspix> CELL (R3234)")).IsError);
    }

    /// <summary>The empty <c>geo:dggsLiteral</c> form carries no CRS of its own, so the SRID accessor answers the CRS84 default — the widening's uniform carriage into every reading function.</summary>
    [TestMethod]
    public void GetSridOverEmptyDggsLiteralAnswersTheDefault()
    {
        AssertLexical(Invoke(GeoFunctions.GetSrid, Serialized(DggsLiteral, "")), Crs84, Vocabulary.Xsd.AnyUri);
    }

    /// <summary>A house-flavour cell-set literal reads at the operand seam: the cells materialize as planar geometry, so the emptiness test answers false.</summary>
    [TestMethod]
    public void HouseFlavourDggsLiteralReadsAtTheSeam()
    {
        AssertLexical(Invoke(GeoFunctions.IsEmpty, Serialized(DggsLiteral, HouseCellsLiteral)), "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>A house-flavour literal reads identically under the generic and the subclass datatype — the value is what the literal denotes, whichever typed it — and one cell materializes as a polygon.</summary>
    [TestMethod]
    public void HouseFlavourReadsIdenticallyUnderBothDggsDatatypes()
    {
        SparqlFunctionResult generic = Invoke(GeoFunctions.AsWkt, Serialized(DggsLiteral, HouseCellsLiteral));
        SparqlFunctionResult subclass = Invoke(GeoFunctions.AsWkt, Serialized(A5Literal, HouseCellsLiteral));

        Assert.IsFalse(generic.IsError);
        Assert.IsFalse(subclass.IsError);
        Literal genericLiteral = (Literal)generic.Term!;
        Literal subclassLiteral = (Literal)subclass.Term!;
        Assert.IsTrue(genericLiteral.Value.Span.SequenceEqual(subclassLiteral.Value.Span), "Both DGGS datatypes must materialize the identical geometry.");
        Assert.IsTrue(genericLiteral.Value.ToString().StartsWith("POLYGON", StringComparison.Ordinal), "One cell must materialize as a polygon.");
    }

    /// <summary>The SRID accessor answers the CRS84 default over a parsed house-flavour form under either DGGS datatype.</summary>
    [TestMethod]
    public void GetSridOverHouseFlavourLiteralAnswersTheDefault()
    {
        AssertLexical(Invoke(GeoFunctions.GetSrid, Serialized(A5Literal, HouseCellsLiteral)), Crs84, Vocabulary.Xsd.AnyUri);
    }

    /// <summary>A two-cell house literal materializes as a multipolygon, one member polygon per cell.</summary>
    [TestMethod]
    public void HouseFlavourMultiCellReadsAsMultiPolygon()
    {
        AssertLexical(
            Invoke(GeoFunctions.GeometryType, Serialized(DggsLiteral, "<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000 a00000000000000)")),
            "http://www.opengis.net/ont/sf#MultiPolygon",
            Vocabulary.Xsd.AnyUri);
    }

    /// <summary>The planar magnitude functions compute over the bridged geometry: the cell polygon's degree-denominated area is positive.</summary>
    [TestMethod]
    public void HouseFlavourAreaIsPositive()
    {
        SparqlFunctionResult result = Invoke(GeoFunctions.Area, Serialized(A5Literal, HouseCellsLiteral), DegreeIri);

        Assert.IsFalse(result.IsError);
        double area = double.Parse(((Literal)result.Term!).Value.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsGreaterThan(0.0, area);
    }

    /// <summary>The binary functions mix a house DGGS operand with a WKT operand under the shared CRS84 default: the resolution-21 cell at the origin contains its own seed point.</summary>
    [TestMethod]
    public void HouseFlavourCellContainsItsSeedPoint()
    {
        AssertLexical(Invoke(GeoFunctions.SfContains, Serialized(DggsLiteral, HouseCellsLiteral), Wkt("POINT(0 0)")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>An antimeridian-straddling cell's boundary is not planar-faithful in CRS84 — its unwrapped vertices leave the canonical longitude range — so the bridge refuses and the function answers the error value; the world cell has no planar boundary at all and refuses the same way.</summary>
    /// <param name="cellToken">The refused cell's token.</param>
    [TestMethod]
    [DataRow("2e00000000000000")]
    [DataRow("0")]
    public void HouseFlavourPlanarUnfaithfulCellErrs(string cellToken)
    {
        Assert.IsTrue(Invoke(GeoFunctions.IsEmpty, Serialized(DggsLiteral, $"<https://lumoin.com/veritas/dggs/a5> CELLS ({cellToken})")).IsError);
    }

    /// <summary>A cell set containing an ancestor and its own descendant materializes structurally overlapping polygons, which is outside the floor's computation contract, so the bridge refuses.</summary>
    [TestMethod]
    public void HouseFlavourNestedPairErrs()
    {
        A5CellId parent = A5CellId.Parse("4f05dccc726e0000");
        A5CellId child = A5.CellToChildren(parent)[0];
        string literal = $"<https://lumoin.com/veritas/dggs/a5> CELLS (4f05dccc726e0000 {child.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture)})";

        Assert.IsTrue(Invoke(GeoFunctions.IsEmpty, Serialized(DggsLiteral, literal)).IsError);
    }

    /// <summary>The point conversion answers exactly the containing cell's canonical literal, typed under the house subclass datatype.</summary>
    [TestMethod]
    public void AsDggsOfPointAnswersTheContainingCell()
    {
        A5CellId expected = A5.LonLatToCell(new LonLat(0, 0), 21);
        string expectedLiteral = $"<https://lumoin.com/veritas/dggs/a5> CELLS ({expected.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture)})";

        AssertLexical(
            Invoke(GeoFunctions.AsDggs, Wkt("POINT(0 0)"), new NamedNode(Utf8Strings.From(A5Literal + "?resolution=21"))),
            expectedLiteral,
            Utf8Strings.From(A5Literal));
    }

    /// <summary>The conversion round-trips through the seam: the cell set produced for a point contains that point.</summary>
    [TestMethod]
    public void AsDggsRoundTripContainsTheSeedPoint()
    {
        SparqlFunctionResult conversion = Invoke(GeoFunctions.AsDggs, Wkt("POINT(2.3522 48.8566)"), new NamedNode(Utf8Strings.From(A5Literal + "?resolution=10")));

        Assert.IsFalse(conversion.IsError);
        AssertLexical(Invoke(GeoFunctions.SfContains, (Literal)conversion.Term!, Wkt("POINT(2.3522 48.8566)")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The empty geometry converts to the empty house literal, and an explicit CRS84 operand is accepted like the default.</summary>
    [TestMethod]
    public void AsDggsOfEmptyGeometryAnswersTheEmptyLiteral()
    {
        AssertLexical(Invoke(GeoFunctions.AsDggs, Wkt("POINT EMPTY"), new NamedNode(Utf8Strings.From(A5Literal + "?resolution=10"))), "", Utf8Strings.From(A5Literal));
        Assert.IsFalse(Invoke(GeoFunctions.AsDggs, Wkt($"<{Crs84}> POINT(0 0)"), new NamedNode(Utf8Strings.From(A5Literal + "?resolution=10"))).IsError);
    }

    /// <summary>The datatype argument accepts an <c>xsd:anyURI</c> literal carrying the same resolution query as an IRI term.</summary>
    [TestMethod]
    public void AsDggsAcceptsTheAnyUriLiteralArgument()
    {
        Assert.IsFalse(Invoke(GeoFunctions.AsDggs, Wkt("POINT(0 0)"), new Literal(Utf8Strings.From(A5Literal + "?resolution=5"), new NamedNode(Vocabulary.Xsd.AnyUri))).IsError);
    }

    /// <summary>Every out-of-grammar datatype argument answers the error value: a bare IRI without the resolution (no default is fabricated), an empty or out-of-range or zero-padded value, foreign query content, a foreign datatype IRI, and a non-IRI term.</summary>
    /// <param name="argumentIri">The datatype argument under test.</param>
    [TestMethod]
    [DataRow("https://lumoin.com/veritas/dggs/a5Literal")]
    [DataRow("https://lumoin.com/veritas/dggs/a5Literal?resolution=")]
    [DataRow("https://lumoin.com/veritas/dggs/a5Literal?resolution=31")]
    [DataRow("https://lumoin.com/veritas/dggs/a5Literal?resolution=05")]
    [DataRow("https://lumoin.com/veritas/dggs/a5Literal?resolution=-1")]
    [DataRow("https://lumoin.com/veritas/dggs/a5Literal?resolution=10&x=1")]
    [DataRow("http://www.opengis.net/ont/geosparql#dggsLiteral?resolution=10")]
    [DataRow("https://w3id.org/dggs/auspix")]
    public void AsDggsOutOfGrammarArgumentErrs(string argumentIri)
    {
        Assert.IsTrue(Invoke(GeoFunctions.AsDggs, Wkt("POINT(0 0)"), new NamedNode(Utf8Strings.From(argumentIri))).IsError);
    }

    /// <summary>An explicit non-CRS84 operand, a three-dimensional operand, and a wrong arity all answer the error value.</summary>
    [TestMethod]
    public void AsDggsRefusedOperandFamiliesErr()
    {
        NamedNode target = new(Utf8Strings.From(A5Literal + "?resolution=10"));

        Assert.IsTrue(Invoke(GeoFunctions.AsDggs, Wkt($"<{MetricCrs}> POINT(1 2)"), target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.AsDggs, Wkt("POINT Z (1 2 3)"), target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.AsDggs, Wkt("POINT(0 0)")).IsError);
    }

    /// <summary>The spatial aggregates fold house-flavour DGGS members through the same operand seam: the union of a two-cell DGGS group is a non-empty geometry literal.</summary>
    [TestMethod]
    public void AggUnionFoldsAHouseFlavourDggsGroup()
    {
        SparqlFunctionResult result = InvokeAggregate(
            GeoFunctions.AggUnion,
            Serialized(DggsLiteral, "<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000)"),
            Serialized(DggsLiteral, "<https://lumoin.com/veritas/dggs/a5> CELLS (a00000000000000)"));

        Assert.IsFalse(result.IsError);
        AssertLexical(Invoke(GeoFunctions.IsEmpty, (Literal)result.Term!), "false", Vocabulary.Xsd.Boolean);
    }

    /// <summary>A mixed DGGS and WKT member group aggregates under the shared CRS84 default: the group's bounding box spans both members, so an interior point between them is contained.</summary>
    [TestMethod]
    public void AggBoundingBoxFoldsAMixedDggsAndWktGroup()
    {
        SparqlFunctionResult result = InvokeAggregate(
            GeoFunctions.AggBoundingBox,
            Serialized(A5Literal, HouseCellsLiteral),
            Wkt("POINT(10 10)"));

        Assert.IsFalse(result.IsError);
        AssertLexical(Invoke(GeoFunctions.SfContains, (Literal)result.Term!, Wkt("POINT(5 5)")), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The projection pair answers exactly what the transform surface computes: the result literal's parsed vertex agrees bitwise with the surface's own output for the same input.</summary>
    [TestMethod]
    public void TransformToWebMercatorMatchesTheSurfaceBitwise()
    {
        SparqlFunctionResult result = Invoke(GeoFunctions.Transform, Wkt("POINT (24.9384 60.1699)"), new NamedNode(Utf8Strings.From(WebMercatorCrs)));

        Assert.IsFalse(result.IsError, "The certified pair transforms.");
        Literal literal = (Literal)result.Term!;
        Assert.IsTrue(WktCrsPrefix.TryParse(literal.Value, out WktCrsPrefix decomposition));
        Assert.AreEqual(WktCrsSource.Explicit, decomposition.Source);
        Assert.AreEqual(WebMercatorCrs, decomposition.CrsIri.ToString());
        Assert.IsTrue(WktGeometryReader.TryRead(decomposition.Body.Span, out FlatGeometry parsed, out _));

        Span<double> expected = stackalloc double[] { 24.9384, 60.1699 };
        Assert.IsTrue(CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, expected, expected, out _));
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected[0]), BitConverter.DoubleToInt64Bits(parsed.Vertices[0].X));
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected[1]), BitConverter.DoubleToInt64Bits(parsed.Vertices[0].Y));
    }

    /// <summary>The geographic pair is the axis-order duality: a CRS84 literal re-expressed in EPSG:4326 swaps to latitude-first coordinates, and the reverse direction swaps back, each under its explicit target prefix.</summary>
    [TestMethod]
    public void TransformBetweenTheGeographicPairSwapsAxisOrder()
    {
        AssertLexical(
            Invoke(GeoFunctions.Transform, Wkt("POINT (24.9384 60.1699)"), new NamedNode(Utf8Strings.From(Epsg4326Crs))),
            $"<{Epsg4326Crs}> POINT (60.1699 24.9384)",
            GeoVocabulary.Geo.WktLiteral);
        AssertLexical(
            Invoke(GeoFunctions.Transform, Wkt($"<{Epsg4326Crs}> POINT (60.1699 24.9384)"), new NamedNode(Utf8Strings.From(Crs84))),
            $"<{Crs84}> POINT (24.9384 60.1699)",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>An identity transform answers the canonical serialization under the explicit target prefix — the function names the requested system even when it is the CRS84 default.</summary>
    [TestMethod]
    public void TransformIdentityEmitsTheExplicitTargetPrefix()
    {
        AssertLexical(
            Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)"), new NamedNode(Utf8Strings.From(Crs84))),
            $"<{Crs84}> POINT (1 2)",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The defaulted and the explicitly spelled CRS84 source resolve to the same roster member, so both answer the identical result literal.</summary>
    [TestMethod]
    public void TransformOfExplicitAndDefaultedCrs84SourcesAgree()
    {
        SparqlFunctionResult defaulted = Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)"), new NamedNode(Utf8Strings.From(WebMercatorCrs)));
        SparqlFunctionResult explicitSource = Invoke(GeoFunctions.Transform, Wkt($"<{Crs84}> POINT (1 2)"), new NamedNode(Utf8Strings.From(WebMercatorCrs)));

        Assert.IsFalse(defaulted.IsError);
        Assert.IsFalse(explicitSource.IsError);
        Assert.AreEqual(((Literal)defaulted.Term!).Value.ToString(), ((Literal)explicitSource.Term!).Value.ToString());
    }

    /// <summary>The target argument is also accepted as an <c>xsd:anyURI</c> literal naming the same canonical spelling, answering the identical result.</summary>
    [TestMethod]
    public void TransformAcceptsTheAnyUriLiteralTarget()
    {
        SparqlFunctionResult viaIri = Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)"), new NamedNode(Utf8Strings.From(WebMercatorCrs)));
        SparqlFunctionResult viaLiteral = Invoke(
            GeoFunctions.Transform,
            Wkt("POINT (1 2)"),
            new Literal(Utf8Strings.From(WebMercatorCrs), new NamedNode(Vocabulary.Xsd.AnyUri)));

        Assert.IsFalse(viaIri.IsError);
        Assert.IsFalse(viaLiteral.IsError);
        Assert.AreEqual(((Literal)viaIri.Term!).Value.ToString(), ((Literal)viaLiteral.Term!).Value.ToString());
    }

    /// <summary>A typed empty transforms to the typed empty under the explicit target prefix — no coordinates exist to refuse, and the kind survives.</summary>
    [TestMethod]
    public void TransformOfTheEmptyGeometryAnswersTheTypedEmptyUnderTheTargetPrefix()
    {
        AssertLexical(
            Invoke(GeoFunctions.Transform, Wkt("POINT EMPTY"), new NamedNode(Utf8Strings.From(WebMercatorCrs))),
            $"<{WebMercatorCrs}> POINT EMPTY",
            GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>A house-flavour cell-set literal materializes through the operand seam at the CRS84 default and re-expresses in Web Mercator: the answer is a non-empty polygonal <c>geo:wktLiteral</c> under the explicit target prefix.</summary>
    [TestMethod]
    public void TransformOverTheHouseFlavourCellSetAnswersAWebMercatorGeometry()
    {
        SparqlFunctionResult result = Invoke(GeoFunctions.Transform, Serialized(A5Literal, HouseCellsLiteral), new NamedNode(Utf8Strings.From(WebMercatorCrs)));

        Assert.IsFalse(result.IsError, "The cell set materializes and the certified pair transforms.");
        Literal literal = (Literal)result.Term!;
        Assert.IsTrue(literal.Datatype.Iri.Span.SequenceEqual(GeoVocabulary.Geo.WktLiteral.Span));
        Assert.IsTrue(WktCrsPrefix.TryParse(literal.Value, out WktCrsPrefix decomposition));
        Assert.AreEqual(WktCrsSource.Explicit, decomposition.Source);
        Assert.AreEqual(WebMercatorCrs, decomposition.CrsIri.ToString());
        Assert.IsTrue(WktGeometryReader.TryRead(decomposition.Body.Span, out FlatGeometry parsed, out _));
        Assert.AreEqual(GeometryKind.Polygon, parsed.Kind);
        Assert.IsFalse(parsed.IsEmpty);
    }

    /// <summary>A target system outside the closed roster refuses loudly: nothing outside the three canonical spellings — no urn: serialization, no case variant, no foreign registry code — is recognized.</summary>
    /// <param name="targetIri">The unrecognized target IRI.</param>
    [TestMethod]
    [DataRow("http://example.org/def/crs/metric")]
    [DataRow("urn:ogc:def:crs:EPSG::3857")]
    [DataRow("urn:ogc:def:crs:OGC:1.3:CRS84")]
    [DataRow("HTTP://WWW.OPENGIS.NET/DEF/CRS/OGC/1.3/CRS84")]
    [DataRow("http://www.opengis.net/def/crs/EPSG/0/25832")]
    public void TransformToAnUnrecognizedTargetErrs(string targetIri)
    {
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)"), new NamedNode(Utf8Strings.From(targetIri))).IsError);
    }

    /// <summary>An operand whose explicit CRS is outside the closed roster refuses — the source side is certified-roster-only, exactly like the target side.</summary>
    [TestMethod]
    public void TransformFromAnUnrecognizedExplicitSourceErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt($"<{MetricCrs}> POINT (1 2)"), new NamedNode(Utf8Strings.From(WebMercatorCrs))).IsError);
    }

    /// <summary>A three-dimensional operand, a measured operand, a wrong arity, a non-literal geometry argument, and a foreign-typed target literal all answer the error value.</summary>
    [TestMethod]
    public void TransformRefusedOperandFamiliesErr()
    {
        NamedNode target = new(Utf8Strings.From(WebMercatorCrs));

        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT Z (1 2 3)"), target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT M (1 2 3)"), target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)")).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)"), target, target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.Transform, new NamedNode(Utf8Strings.From("http://example.org/geometry")), target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT (1 2)"), Text(WebMercatorCrs)).IsError);
    }

    /// <summary>Coordinates the certified pair refuses answer the error value: a poleward latitude never clamps and an out-of-range longitude never wraps.</summary>
    [TestMethod]
    public void TransformOfARefusedCoordinateErrs()
    {
        NamedNode target = new(Utf8Strings.From(WebMercatorCrs));

        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT (0 86)"), target).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.Transform, Wkt("POINT (200 10)"), target).IsError);
    }

    /// <summary>The extrema resolve X and Y through the declared axis order, so both geographic spellings of one geometry — longitude-first CRS84 and latitude-first EPSG:4326 — answer alike.</summary>
    /// <param name="lexicalForm">The geometry in one of the two geographic spellings.</param>
    [TestMethod]
    [DataRow("LINESTRING (10 20, 30 40)")]
    [DataRow("<" + Epsg4326Crs + "> LINESTRING (20 10, 40 30)")]
    public void ExtremaAgreeAcrossTheGeographicPairSpellings(string lexicalForm)
    {
        Literal geometry = Wkt(lexicalForm);

        AssertLexical(Invoke(GeoFunctions.MinX, geometry), "10", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxX, geometry), "30", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MinY, geometry), "20", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxY, geometry), "40", Vocabulary.Xsd.Double);
    }

    /// <summary>The extrema are invariant under a pure re-expression of the geometry through the geographic transform pair: the transformed literal answers the untransformed literal's values.</summary>
    [TestMethod]
    public void ExtremaAreInvariantUnderTransformReExpression()
    {
        SparqlFunctionResult transformed = Invoke(GeoFunctions.Transform, Wkt("LINESTRING (10 20, 30 40)"), new NamedNode(Utf8Strings.From(Epsg4326Crs)));

        Assert.IsFalse(transformed.IsError, "The certified geographic pair transforms.");
        Literal reExpressed = (Literal)transformed.Term!;
        AssertLexical(Invoke(GeoFunctions.MinX, reExpressed), "10", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxY, reExpressed), "40", Vocabulary.Xsd.Double);
    }

    /// <summary>Outside the geographic swap the written order is the read order: Web Mercator's declared easting-first coincides with it, and a system the roster does not recognize resolves no declared order at all.</summary>
    /// <param name="lexicalForm">The geometry whose first written ordinate is the X answer.</param>
    [TestMethod]
    [DataRow("<" + WebMercatorCrs + "> LINESTRING (10 20, 30 40)")]
    [DataRow("<" + MetricCrs + "> LINESTRING (10 20, 30 40)")]
    public void ExtremaKeepTheWrittenOrderOutsideTheGeographicSwap(string lexicalForm)
    {
        Literal geometry = Wkt(lexicalForm);

        AssertLexical(Invoke(GeoFunctions.MinX, geometry), "10", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.MaxY, geometry), "40", Vocabulary.Xsd.Double);
    }

    /// <summary>The envelope commutes with the geographic transform: transforming the envelope and taking the transformed geometry's envelope answer the same region, witnessed by <c>sfEquals</c> under the shared explicit target prefix.</summary>
    [TestMethod]
    public void EnvelopeCommutesWithTheGeographicTransform()
    {
        NamedNode target = new(Utf8Strings.From(Epsg4326Crs));
        Literal source = Wkt("POLYGON ((0 0, 4 0, 4 2, 0 2, 0 0))");

        SparqlFunctionResult envelopeOfTransformed = Invoke(GeoFunctions.Envelope, (Literal)Invoke(GeoFunctions.Transform, source, target).Term!);
        SparqlFunctionResult transformedEnvelope = Invoke(GeoFunctions.Transform, (Literal)Invoke(GeoFunctions.Envelope, source).Term!, target);

        Assert.IsFalse(envelopeOfTransformed.IsError);
        Assert.IsFalse(transformedEnvelope.IsError);
        AssertLexical(Invoke(GeoFunctions.SfEquals, (Literal)envelopeOfTransformed.Term!, (Literal)transformedEnvelope.Term!), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>Operands under the two geographic spellings never mix: the catalog inserts no transformation, so a CRS84 and an EPSG:4326 operand of the same point refuse rather than compare tuples of differing declared order.</summary>
    [TestMethod]
    public void MixedAxisOrderOperandsRefuse()
    {
        Literal longitudeFirst = Wkt("POINT (24 60)");
        Literal latitudeFirst = Wkt($"<{Epsg4326Crs}> POINT (60 24)");

        Assert.IsTrue(Invoke(GeoFunctions.Distance, longitudeFirst, latitudeFirst, DegreeIri).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.SfEquals, longitudeFirst, latitudeFirst).IsError);
    }

    /// <summary>An aggregate group mixing the two geographic spellings refuses through the group seam's own gate — the no-mixing rule holds for aggregates as it does for binary functions.</summary>
    [TestMethod]
    public void AggregatesRefuseAMixedAxisOrderGroup()
    {
        Assert.IsTrue(InvokeAggregate(GeoFunctions.AggBoundingBox, Wkt("POINT (1 2)"), Wkt($"<{Epsg4326Crs}> POINT (2 1)")).IsError);
    }

    /// <summary>The metric family errs over EPSG:4326 operands: the roster declares the system's unit as degree, so a metre-denominated answer would be a fabrication.</summary>
    [TestMethod]
    public void MetricFamilyErrsOverTheLatitudeFirstDegrees()
    {
        Assert.IsTrue(Invoke(GeoFunctions.MetricDistance, Wkt($"<{Epsg4326Crs}> POINT (0 0)"), Wkt($"<{Epsg4326Crs}> POINT (3 4)")).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MetricArea, Wkt($"<{Epsg4326Crs}> POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MetricLength, Wkt($"<{Epsg4326Crs}> LINESTRING (0 0, 3 4)")).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MetricPerimeter, Wkt($"<{Epsg4326Crs}> POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")).IsError);
        Assert.IsTrue(Invoke(GeoFunctions.MetricBuffer, Wkt($"<{Epsg4326Crs}> POINT (1 1)"), Double("0.5")).IsError);
    }

    /// <summary>The degree unit answers over EPSG:4326 — the roster declares its unit as degree — and each magnitude equals its longitude-first twin's answer, because the planar magnitudes are invariant under tuple transposition.</summary>
    [TestMethod]
    public void DegreeUnitAnswersOverTheLatitudeFirstTwins()
    {
        AssertLexical(Invoke(GeoFunctions.Distance, Wkt($"<{Epsg4326Crs}> POINT (0 0)"), Wkt($"<{Epsg4326Crs}> POINT (4 3)"), DegreeIri), "5", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Distance, Wkt("POINT (0 0)"), Wkt("POINT (3 4)"), DegreeIri), "5", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Area, Wkt($"<{Epsg4326Crs}> POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))"), DegreeIri), "1", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Length, Wkt($"<{Epsg4326Crs}> LINESTRING (0 0, 4 3)"), DegreeIri), "5", Vocabulary.Xsd.Double);
        AssertLexical(Invoke(GeoFunctions.Perimeter, Wkt($"<{Epsg4326Crs}> POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))"), DegreeIri), "4", Vocabulary.Xsd.Double);
    }

    /// <summary>The unit rules follow the roster's declared units on every side: the metre unit errs over declared-degree EPSG:4326, answers over declared-metre Web Mercator, and the degree unit errs over Web Mercator.</summary>
    [TestMethod]
    public void MetreAndDegreeFollowTheDeclaredUnits()
    {
        Assert.IsTrue(Invoke(GeoFunctions.Distance, Wkt($"<{Epsg4326Crs}> POINT (0 0)"), Wkt($"<{Epsg4326Crs}> POINT (4 3)"), MetreIri).IsError);
        AssertLexical(Invoke(GeoFunctions.MetricDistance, Wkt($"<{WebMercatorCrs}> POINT (0 0)"), Wkt($"<{WebMercatorCrs}> POINT (3 4)")), "5", Vocabulary.Xsd.Double);
        Assert.IsTrue(Invoke(GeoFunctions.Distance, Wkt($"<{WebMercatorCrs}> POINT (0 0)"), Wkt($"<{WebMercatorCrs}> POINT (3 4)"), DegreeIri).IsError);
    }

    /// <summary>The buffer's radius unit follows the declared units over EPSG:4326: the degree radius answers a buffered geometry under the operand's explicit prefix, and the metre radius errs.</summary>
    [TestMethod]
    public void BufferUnitsFollowTheDeclaredUnitsOverTheLatitudeFirstOperand()
    {
        SparqlFunctionResult buffered = Invoke(GeoFunctions.Buffer, Wkt($"<{Epsg4326Crs}> POINT (1 1)"), Double("0.5"), DegreeIri);

        Assert.IsFalse(buffered.IsError, "The degree radius answers over the declared-degree system.");
        Assert.StartsWith($"<{Epsg4326Crs}>", ((Literal)buffered.Term!).Value.ToString());
        Assert.IsTrue(Invoke(GeoFunctions.Buffer, Wkt($"<{Epsg4326Crs}> POINT (1 1)"), Double("0.5"), MetreIri).IsError);
    }

    /// <summary>An explicit EPSG:4326 operand refuses at <c>geof:asDGGS</c>: the cell bridge names its ordinates longitude-first, so only CRS84-declared geometry may reach it and no implicit transformation is inserted.</summary>
    [TestMethod]
    public void AsDggsRefusesTheLatitudeFirstRosterMember()
    {
        Assert.IsTrue(Invoke(GeoFunctions.AsDggs, Wkt($"<{Epsg4326Crs}> POINT (60 25)"), new NamedNode(Utf8Strings.From(A5Literal + "?resolution=10"))).IsError);
    }

    /// <summary>The GML serialization answers the geometry in the system it already carries and declares that system on the root in every case — a defaulted operand answers a document naming CRS84 outright — with the coordinates written exactly as the operand carries them and the third dimension declared on the carrier that carries it.</summary>
    /// <param name="lexicalForm">The operand's lexical form.</param>
    /// <param name="expected">The expected GML document.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "<gml:Point" + GmlRootAttributePrefix + Crs84 + "\"><gml:pos>1 2</gml:pos></gml:Point>")]
    [DataRow("<" + Crs84 + "> POINT (1 2)", "<gml:Point" + GmlRootAttributePrefix + Crs84 + "\"><gml:pos>1 2</gml:pos></gml:Point>")]
    [DataRow("<" + Epsg4326Crs + "> POINT (60 24)", "<gml:Point" + GmlRootAttributePrefix + Epsg4326Crs + "\"><gml:pos>60 24</gml:pos></gml:Point>")]
    [DataRow("<" + WebMercatorCrs + "> POINT (100 200)", "<gml:Point" + GmlRootAttributePrefix + WebMercatorCrs + "\"><gml:pos>100 200</gml:pos></gml:Point>")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "<gml:LineString" + GmlRootAttributePrefix + Crs84 + "\"><gml:posList>0 0 1 1 2 0</gml:posList></gml:LineString>")]
    [DataRow("POINT Z (1 2 3)", "<gml:Point" + GmlRootAttributePrefix + Crs84 + "\"><gml:pos srsDimension=\"3\">1 2 3</gml:pos></gml:Point>")]
    public void AsGmlAnswersTheCanonicalDocumentInTheOperandSystem(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.AsGml, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.GmlLiteral);
    }

    /// <summary>The GML serialization of an empty operand follows the format's own empty forms: the memberless collection and the memberless typed aggregate answer their self-closing documents, system declaration included, while an empty primitive has no encoding in the format and answers the error value.</summary>
    [TestMethod]
    public void AsGmlOverEmptyOperandsFollowsTheFormatsOwnEmptyForms()
    {
        AssertLexical(Invoke(GeoFunctions.AsGml, Wkt(string.Empty)), "<gml:MultiGeometry" + GmlRootAttributePrefix + Crs84 + "\"/>", GeoVocabulary.Geo.GmlLiteral);
        AssertLexical(Invoke(GeoFunctions.AsGml, Serialized(GeoJsonLiteral, string.Empty)), "<gml:MultiGeometry" + GmlRootAttributePrefix + Crs84 + "\"/>", GeoVocabulary.Geo.GmlLiteral);
        AssertLexical(Invoke(GeoFunctions.AsGml, Wkt("MULTIPOINT EMPTY")), "<gml:MultiPoint" + GmlRootAttributePrefix + Crs84 + "\"/>", GeoVocabulary.Geo.GmlLiteral);
        Assert.IsTrue(Invoke(GeoFunctions.AsGml, Wkt("POINT EMPTY")).IsError, "The format encodes no empty primitive, so the writer refuses and the function answers the error value.");
    }

    /// <summary>The GeoJSON serialization answers the canonical document in CRS84: an operand already in that system is written as it stands, one in another certified system is converted first — the latitude-first pair swapping to longitude-first — and a CRS84 operand's third ordinate rides into the position's third element.</summary>
    /// <param name="lexicalForm">The operand's lexical form.</param>
    /// <param name="expected">The expected GeoJSON document.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", GeoJsonTestDocuments.CanonicalPoint)]
    [DataRow("<" + Crs84 + "> POINT (1 2)", GeoJsonTestDocuments.CanonicalPoint)]
    [DataRow("<" + Epsg4326Crs + "> POINT (60.1699 24.9384)", "{\"type\":\"Point\",\"coordinates\":[24.9384,60.1699]}")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", GeoJsonLineDocument)]
    [DataRow("POINT Z (1 2 3)", GeoJsonTestDocuments.CanonicalPointWithAltitude)]
    [DataRow("", GeoJsonTestDocuments.CanonicalEmptyCollection)]
    [DataRow("POINT EMPTY", "{\"type\":\"Point\",\"coordinates\":[]}")]
    public void AsGeoJsonAnswersTheCanonicalDocumentInCrs84(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.AsGeoJson, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.GeoJsonLiteral);
    }

    /// <summary>The KML serialization answers the canonical document in CRS84 under the same conversion rule, with a carried third ordinate written as the format's absolute altitude.</summary>
    /// <param name="lexicalForm">The operand's lexical form.</param>
    /// <param name="expected">The expected KML document.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "<Point" + KmlRootDeclaration + ">" + KmlTestDocuments.PointCoordinates + "</Point>")]
    [DataRow("<" + Crs84 + "> POINT (1 2)", "<Point" + KmlRootDeclaration + ">" + KmlTestDocuments.PointCoordinates + "</Point>")]
    [DataRow("<" + Epsg4326Crs + "> POINT (60.1699 24.9384)", "<Point" + KmlRootDeclaration + "><coordinates>24.9384,60.1699</coordinates></Point>")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "<LineString" + KmlRootDeclaration + "><coordinates>0,0 1,1 2,0</coordinates></LineString>")]
    [DataRow("POINT Z (1 2 3)", "<Point" + KmlRootDeclaration + "><altitudeMode>absolute</altitudeMode><coordinates>1,2,3</coordinates></Point>")]
    public void AsKmlAnswersTheCanonicalDocumentInCrs84(string lexicalForm, string expected)
    {
        AssertLexical(Invoke(GeoFunctions.AsKml, Wkt(lexicalForm)), expected, GeoVocabulary.Geo.KmlLiteral);
    }

    /// <summary>The KML format expresses no empty geometry at all, so every empty operand answers the error value rather than a fabricated document — the memberless collection, the empty primitive, and the empty form of another serialization datatype alike.</summary>
    [TestMethod]
    public void AsKmlOverAnEmptyOperandErrs()
    {
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, Wkt(string.Empty)).IsError, "The empty lexical form denotes the empty collection, which the format cannot express.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, Wkt("GEOMETRYCOLLECTION EMPTY")).IsError, "The memberless collection has no KML form.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, Wkt("POINT EMPTY")).IsError, "The empty primitive has no KML form.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, Serialized(GmlLiteral, string.Empty)).IsError, "The empty GML literal denotes the same empty geometry, with the same answer.");
    }

    /// <summary>The GML pair round-trips through the operand seam whole: the re-read serialization answers the operand's own geometry under the operand's own system, which the document declared explicitly.</summary>
    /// <param name="lexicalForm">The operand's lexical form.</param>
    /// <param name="expected">The expected well-known text of the re-read serialization.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "<" + Crs84 + "> POINT (1 2)")]
    [DataRow("<" + Crs84 + "> POINT (1 2)", "<" + Crs84 + "> POINT (1 2)")]
    [DataRow("<" + Epsg4326Crs + "> POINT (60 24)", "<" + Epsg4326Crs + "> POINT (60 24)")]
    [DataRow("<" + WebMercatorCrs + "> POINT (100 200)", "<" + WebMercatorCrs + "> POINT (100 200)")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "<" + Crs84 + "> POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))")]
    public void GmlRoundTripPreservesTheGeometryAndItsSystem(string lexicalForm, string expected)
    {
        SparqlFunctionResult serialized = Invoke(GeoFunctions.AsGml, Wkt(lexicalForm));

        Assert.IsFalse(serialized.IsError, $"'{lexicalForm}' must serialize as GML.");
        AssertLexical(Invoke(GeoFunctions.AsWkt, (Literal)serialized.Term!), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The GeoJSON and KML pairs round-trip into CRS84: the re-read serialization answers the operand's geometry expressed in the system those formats fix, carrying no prefix because that system is the default.</summary>
    /// <param name="lexicalForm">The operand's lexical form.</param>
    /// <param name="expected">The expected well-known text of the re-read serialization.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "POINT (1 2)")]
    [DataRow("<" + Crs84 + "> LINESTRING (0 0, 1 1, 2 0)", "LINESTRING (0 0, 1 1, 2 0)")]
    [DataRow("<" + Epsg4326Crs + "> POINT (60.1699 24.9384)", "POINT (24.9384 60.1699)")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))")]
    public void GeoJsonAndKmlRoundTripsAnswerTheCrs84Geometry(string lexicalForm, string expected)
    {
        SparqlFunctionResult geoJson = Invoke(GeoFunctions.AsGeoJson, Wkt(lexicalForm));
        SparqlFunctionResult kml = Invoke(GeoFunctions.AsKml, Wkt(lexicalForm));

        Assert.IsFalse(geoJson.IsError, $"'{lexicalForm}' must serialize as GeoJSON.");
        Assert.IsFalse(kml.IsError, $"'{lexicalForm}' must serialize as KML.");
        AssertLexical(Invoke(GeoFunctions.AsWkt, (Literal)geoJson.Term!), expected, GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.AsWkt, (Literal)kml.Term!), expected, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The conversion the CRS84-fixing formats perform is the transform surface's own: serializing a Web Mercator operand answers exactly what serializing its explicitly transformed CRS84 twin answers, byte for byte, in both formats.</summary>
    [TestMethod]
    public void TheCrs84ConversionAgreesWithTheExplicitTransform()
    {
        Literal projected = Wkt($"<{WebMercatorCrs}> POINT (2775000 8375000)");
        SparqlFunctionResult transformed = Invoke(GeoFunctions.Transform, projected, new NamedNode(Utf8Strings.From(Crs84)));

        Assert.IsFalse(transformed.IsError, "The certified pair transforms.");
        Literal reExpressed = (Literal)transformed.Term!;

        SparqlFunctionResult directGeoJson = Invoke(GeoFunctions.AsGeoJson, projected);
        SparqlFunctionResult viaTransformGeoJson = Invoke(GeoFunctions.AsGeoJson, reExpressed);
        SparqlFunctionResult directKml = Invoke(GeoFunctions.AsKml, projected);
        SparqlFunctionResult viaTransformKml = Invoke(GeoFunctions.AsKml, reExpressed);

        Assert.IsFalse(directGeoJson.IsError, "The projected operand converts and serializes as GeoJSON.");
        Assert.IsFalse(viaTransformGeoJson.IsError, "The re-expressed operand serializes as GeoJSON.");
        Assert.IsFalse(directKml.IsError, "The projected operand converts and serializes as KML.");
        Assert.IsFalse(viaTransformKml.IsError, "The re-expressed operand serializes as KML.");
        Assert.AreEqual(((Literal)viaTransformGeoJson.Term!).Value.ToString(), ((Literal)directGeoJson.Term!).Value.ToString(), "The GeoJSON conversion answers the transform surface's own coordinates.");
        Assert.AreEqual(((Literal)viaTransformKml.Term!).Value.ToString(), ((Literal)directKml.Term!).Value.ToString(), "The KML conversion answers the transform surface's own coordinates.");
    }

    /// <summary>A system outside the certified roster answers the error value from every serialization function: the GML system declaration is closed to that roster, and the CRS84-fixing formats have no conversion to offer.</summary>
    [TestMethod]
    public void SerializationOverANonRosterSystemErrs()
    {
        Literal operand = Wkt($"<{MetricCrs}> POINT (1 2)");

        Assert.IsTrue(Invoke(GeoFunctions.AsGml, operand).IsError, "The GML system declaration is closed to the certified roster.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, operand).IsError, "No conversion into CRS84 exists for a system outside the roster.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, operand).IsError, "No conversion into CRS84 exists for a system outside the roster.");
    }

    /// <summary>A coordinate the certified conversion refuses answers the error value from the CRS84-fixing formats — never a clamped or wrapped ordinate — while the system-preserving format needs no conversion and writes the operand as it stands.</summary>
    /// <param name="lexicalForm">The operand whose coordinate the conversion refuses.</param>
    [TestMethod]
    [DataRow("<" + Epsg4326Crs + "> POINT (95 24)")]
    [DataRow("<" + Epsg4326Crs + "> POINT (24 200)")]
    [DataRow("<" + WebMercatorCrs + "> POINT (30000000 0)")]
    public void SerializationRefusesWhereTheCrs84ConversionRefuses(string lexicalForm)
    {
        Literal operand = Wkt(lexicalForm);

        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, operand).IsError, $"'{lexicalForm}' must answer the expression error value.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, operand).IsError, $"'{lexicalForm}' must answer the expression error value.");
        Assert.IsFalse(Invoke(GeoFunctions.AsGml, operand).IsError, "The system-preserving serialization converts nothing, so the coordinate rides.");
    }

    /// <summary>A required conversion into CRS84 is planar, and dropping an ordinate the operand carries is never an answer, so a three-dimensional operand outside CRS84 errs at both CRS84-fixing formats while the system-preserving format writes its third dimension.</summary>
    [TestMethod]
    public void SerializationRefusesTheThirdOrdinateUnderARequiredConversion()
    {
        Literal latitudeFirst = Wkt($"<{Epsg4326Crs}> POINT Z (60 24 5)");
        Literal projected = Wkt($"<{WebMercatorCrs}> POINT Z (100 200 5)");

        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, latitudeFirst).IsError, "The planar conversion would have to drop the third ordinate.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, latitudeFirst).IsError, "The planar conversion would have to drop the third ordinate.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, projected).IsError, "The planar conversion would have to drop the third ordinate.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, projected).IsError, "The planar conversion would have to drop the third ordinate.");
        AssertLexical(
            Invoke(GeoFunctions.AsGml, latitudeFirst),
            "<gml:Point" + GmlRootAttributePrefix + Epsg4326Crs + "\"><gml:pos srsDimension=\"3\">60 24 5</gml:pos></gml:Point>",
            GeoVocabulary.Geo.GmlLiteral);
    }

    /// <summary>No format in the serialization family carries a measure, so a measured operand answers the error value everywhere rather than a silently stripped ordinate.</summary>
    [TestMethod]
    public void SerializationRefusesAMeasuredOperand()
    {
        Literal measured = Wkt("POINT M (1 2 4)");
        Literal measuredAndThreeDimensional = Wkt("POINT ZM (1 2 3 4)");

        Assert.IsTrue(Invoke(GeoFunctions.AsGml, measured).IsError, "The GML writer carries no measure.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, measured).IsError, "The GeoJSON writer carries no measure.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, measured).IsError, "The KML writer carries no measure.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGml, measuredAndThreeDimensional).IsError, "A measure beside a third dimension is refused just the same.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, measuredAndThreeDimensional).IsError, "A measure beside a third dimension is refused just the same.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, measuredAndThreeDimensional).IsError, "A measure beside a third dimension is refused just the same.");
    }

    /// <summary>The three serialization functions take exactly one geometry operand: a missing argument, a surplus one, a foreign datatype, and an IRI term all answer the error value.</summary>
    [TestMethod]
    public void SerializationFunctionsRefuseForeignArgumentShapes()
    {
        Assert.IsTrue(Invoke(GeoFunctions.AsGml).IsError, "A missing argument is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, Wkt("POINT (1 2)"), Wkt("POINT (1 2)")).IsError, "A surplus argument is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml).IsError, "A missing argument is a wrong arity.");
        Assert.IsTrue(Invoke(GeoFunctions.AsGeoJson, Text("POINT (1 2)")).IsError, "A foreign datatype is outside the domain.");
        Assert.IsTrue(Invoke(GeoFunctions.AsKml, new NamedNode(Utf8Strings.From("http://example.org/geometry"))).IsError, "An IRI term is outside the domain.");
    }

    /// <summary>A house-flavour DGGS operand reaches every serialization through the same seam: the materialized cell polygon serializes in each format and the re-read serialization is the same region.</summary>
    [TestMethod]
    public void TheSerializationsAnswerOverAHouseFlavourDggsOperand()
    {
        Literal cells = Serialized(A5Literal, HouseCellsLiteral);

        SparqlFunctionResult gml = Invoke(GeoFunctions.AsGml, cells);
        SparqlFunctionResult geoJson = Invoke(GeoFunctions.AsGeoJson, cells);
        SparqlFunctionResult kml = Invoke(GeoFunctions.AsKml, cells);

        Assert.IsFalse(gml.IsError, "The cell set materializes and serializes as GML.");
        Assert.IsFalse(geoJson.IsError, "The cell set materializes and serializes as GeoJSON.");
        Assert.IsFalse(kml.IsError, "The cell set materializes and serializes as KML.");
        Assert.IsTrue(((Literal)gml.Term!).Value.ToString().StartsWith("<gml:Polygon" + GmlRootAttributePrefix + Crs84 + "\">", StringComparison.Ordinal), "One cell serializes as a polygon declaring the default system.");
        Assert.IsTrue(((Literal)geoJson.Term!).Value.ToString().StartsWith("{\"type\":\"Polygon\",\"coordinates\":", StringComparison.Ordinal), "One cell serializes as a polygon.");
        Assert.IsTrue(((Literal)kml.Term!).Value.ToString().StartsWith("<Polygon" + KmlRootDeclaration + ">", StringComparison.Ordinal), "One cell serializes as a polygon.");
        AssertLexical(Invoke(GeoFunctions.SfEquals, cells, (Literal)gml.Term!), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfEquals, cells, (Literal)geoJson.Term!), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfEquals, cells, (Literal)kml.Term!), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>Four literal forms of one logical geometry denote one point set: every pair of the well-known-text, GML, GeoJSON, and KML forms answers the Simple Features equality.</summary>
    [TestMethod]
    public void TheFourLiteralFormsOfOneGeometryDenoteOnePointSet()
    {
        Literal wellKnownText = Wkt("LINESTRING (0 0, 1 1, 2 0)");
        Literal gml = Serialized(GmlLiteral, GmlLineDocument);
        Literal geoJson = Serialized(GeoJsonLiteral, GeoJsonLineDocument);
        Literal kml = Serialized(KmlLiteral, KmlLineDocument);

        AssertLexical(Invoke(GeoFunctions.SfEquals, wellKnownText, gml), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfEquals, wellKnownText, geoJson), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfEquals, wellKnownText, kml), "true", Vocabulary.Xsd.Boolean);
        AssertLexical(Invoke(GeoFunctions.SfEquals, geoJson, kml), "true", Vocabulary.Xsd.Boolean);
    }

    /// <summary>The forms whose system stays defaulted serialize identically — the bare well-known text, the GeoJSON form, and the KML form answer one byte-identical canonical literal — while the GML form's document declared its system, so its answer carries that system's explicit prefix.</summary>
    [TestMethod]
    public void TheDefaultedFormsSerializeIdenticallyWhileTheGmlFormCarriesItsDeclaredSystem()
    {
        const string CanonicalForm = "LINESTRING (0 0, 1 1, 2 0)";

        AssertLexical(Invoke(GeoFunctions.AsWkt, Wkt(CanonicalForm)), CanonicalForm, GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.AsWkt, Serialized(GeoJsonLiteral, GeoJsonLineDocument)), CanonicalForm, GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.AsWkt, Serialized(KmlLiteral, KmlLineDocument)), CanonicalForm, GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.AsWkt, Serialized(GmlLiteral, GmlLineDocument)), $"<{Crs84}> " + CanonicalForm, GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The KML format has one aggregate, so a typed multi collapses into it one way and re-reads as the collection twin, while the GML serialization of the same operand preserves the typed kind.</summary>
    [TestMethod]
    public void AsKmlCollapsesATypedMultiWhileAsGmlPreservesIt()
    {
        Literal multiPoint = Wkt("MULTIPOINT ((1 2), (3 4))");
        SparqlFunctionResult kml = Invoke(GeoFunctions.AsKml, multiPoint);
        SparqlFunctionResult gml = Invoke(GeoFunctions.AsGml, multiPoint);

        Assert.IsFalse(kml.IsError, "The typed multi serializes as KML.");
        Assert.IsFalse(gml.IsError, "The typed multi serializes as GML.");
        AssertLexical(Invoke(GeoFunctions.AsWkt, (Literal)kml.Term!), "GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))", GeoVocabulary.Geo.WktLiteral);
        AssertLexical(Invoke(GeoFunctions.AsWkt, (Literal)gml.Term!), $"<{Crs84}> MULTIPOINT ((1 2), (3 4))", GeoVocabulary.Geo.WktLiteral);
    }
}
