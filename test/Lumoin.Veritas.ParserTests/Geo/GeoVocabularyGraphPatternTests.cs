using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The graph-pattern conformance rows for the GeoSPARQL vocabulary requirements: every class and property
/// the census's vocabulary and serialization buckets demand is exercised inside a SPARQL basic graph
/// pattern over a dataset that asserts the term beside a decoy, and the query answers exactly the asserted
/// bindings — the term is usable in graph patterns and matches selectively. Each row names the census
/// requirement id it decides; the census manifest's evidence entries point back here.
/// </summary>
[TestClass]
internal sealed class GeoVocabularyGraphPatternTests
{
    /// <summary>The example namespace the datasets and queries share.</summary>
    private const string Ex = "urn:x-veritas:geo-arm#";

    /// <summary>The prefix declarations every query in this battery carries.</summary>
    private const string Prefixes = "PREFIX geo: <" + GeoVocabulary.Geo.Namespace + "> PREFIX ex: <" + Ex + ">";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every vocabulary class is usable in a graph pattern: a member typed with the class answers a <c>rdf:type</c> BGP over it, and the decoy typed with an unrelated class stays unmatched.</summary>
    /// <param name="requirementId">The census requirement id the row decides.</param>
    /// <param name="classLocalName">The class's local name under the GeoSPARQL namespace.</param>
    [TestMethod]
    [DataRow("/req/core/spatial-object-class", "SpatialObject")]
    [DataRow("/req/core/feature-class", "Feature")]
    [DataRow("/req/core/spatial-object-collection-class", "SpatialObjectCollection")]
    [DataRow("/req/core/feature-collection-class", "FeatureCollection")]
    [DataRow("/req/geometry-extension/geometry-class", "Geometry")]
    [DataRow("/req/geometry-extension/geometry-collection-class", "GeometryCollection")]
    public async Task ClassIsUsableInGraphPatterns(string requirementId, string classLocalName)
    {
        IReadOnlyList<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "member"), new NamedNode(Vocabulary.Rdf.Type), Iri(GeoVocabulary.Geo.Namespace + classLocalName)),
            new DataTriple(Iri(Ex + "decoy"), new NamedNode(Vocabulary.Rdf.Type), Iri(Ex + "Unrelated")),
        ];
        string query = Prefixes + $" SELECT ?x WHERE {{ ?x a geo:{classLocalName} }}";

        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, data).ConfigureAwait(false);

        Assert.HasCount(1, solutions, $"{requirementId}: the class BGP must match exactly the typed member.");
        AssertBoundIri(solutions[0], "x", Ex + "member", requirementId);
    }

    /// <summary>Every vocabulary property is usable in a graph pattern: an asserted triple over the property answers a BGP with the property as predicate, binding exactly the asserted subject and object while the decoy triple stays unmatched.</summary>
    /// <param name="requirementId">The census requirement id the row decides.</param>
    /// <param name="propertyLocalName">The property's local name under the GeoSPARQL namespace.</param>
    [TestMethod]
    [DataRow("/req/core/spatial-object-properties", "hasSize")]
    [DataRow("/req/core/spatial-object-properties", "hasMetricSize")]
    [DataRow("/req/core/spatial-object-properties", "hasLength")]
    [DataRow("/req/core/spatial-object-properties", "hasMetricLength")]
    [DataRow("/req/core/spatial-object-properties", "hasPerimeterLength")]
    [DataRow("/req/core/spatial-object-properties", "hasMetricPerimeterLength")]
    [DataRow("/req/core/spatial-object-properties", "hasArea")]
    [DataRow("/req/core/spatial-object-properties", "hasMetricArea")]
    [DataRow("/req/core/spatial-object-properties", "hasVolume")]
    [DataRow("/req/core/spatial-object-properties", "hasMetricVolume")]
    [DataRow("/req/core/feature-properties", "hasGeometry")]
    [DataRow("/req/core/feature-properties", "hasDefaultGeometry")]
    [DataRow("/req/core/feature-properties", "hasCentroid")]
    [DataRow("/req/core/feature-properties", "hasBoundingBox")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfEquals")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfDisjoint")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfIntersects")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfTouches")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfCrosses")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfWithin")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfContains")]
    [DataRow("/req/topology-vocab-extension/sf-spatial-relations", "sfOverlaps")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehEquals")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehDisjoint")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehMeet")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehOverlap")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehCovers")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehCoveredBy")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehInside")]
    [DataRow("/req/topology-vocab-extension/eh-spatial-relations", "ehContains")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8eq")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8dc")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8ec")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8po")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8tppi")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8tpp")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8ntpp")]
    [DataRow("/req/topology-vocab-extension/rcc8-spatial-relations", "rcc8ntppi")]
    [DataRow("/req/geometry-extension/feature-properties", "hasGeometry")]
    [DataRow("/req/geometry-extension/feature-properties", "hasDefaultGeometry")]
    [DataRow("/req/geometry-extension/feature-properties", "hasLength")]
    [DataRow("/req/geometry-extension/feature-properties", "hasArea")]
    [DataRow("/req/geometry-extension/feature-properties", "hasVolume")]
    [DataRow("/req/geometry-extension/feature-properties", "hasCentroid")]
    [DataRow("/req/geometry-extension/feature-properties", "hasBoundingBox")]
    [DataRow("/req/geometry-extension/feature-properties", "hasSpatialResolution")]
    [DataRow("/req/geometry-extension/geometry-properties", "dimension")]
    [DataRow("/req/geometry-extension/geometry-properties", "coordinateDimension")]
    [DataRow("/req/geometry-extension/geometry-properties", "spatialDimension")]
    [DataRow("/req/geometry-extension/geometry-properties", "hasSpatialResolution")]
    [DataRow("/req/geometry-extension/geometry-properties", "hasMetricSpatialResolution")]
    [DataRow("/req/geometry-extension/geometry-properties", "hasSpatialAccuracy")]
    [DataRow("/req/geometry-extension/geometry-properties", "hasMetricSpatialAccuracy")]
    [DataRow("/req/geometry-extension/geometry-properties", "isEmpty")]
    [DataRow("/req/geometry-extension/geometry-properties", "isSimple")]
    [DataRow("/req/geometry-extension/geometry-properties", "hasSerialization")]
    public async Task PropertyIsUsableInGraphPatterns(string requirementId, string propertyLocalName)
    {
        IReadOnlyList<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "s"), Iri(GeoVocabulary.Geo.Namespace + propertyLocalName), Iri(Ex + "o")),
            new DataTriple(Iri(Ex + "s"), Iri(Ex + "unrelated"), Iri(Ex + "o2")),
        ];
        string query = Prefixes + $" SELECT ?s ?o WHERE {{ ?s geo:{propertyLocalName} ?o }}";

        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, data).ConfigureAwait(false);

        Assert.HasCount(1, solutions, $"{requirementId}: the property BGP must match exactly the asserted triple.");
        AssertBoundIri(solutions[0], "s", Ex + "s", requirementId);
        AssertBoundIri(solutions[0], "o", Ex + "o", requirementId);
    }

    /// <summary>Every serialization property binds a geometry to its typed literal through a graph pattern: the asserted literal comes back with its lexical form and serialization datatype intact.</summary>
    /// <param name="requirementId">The census requirement id the row decides.</param>
    /// <param name="propertyLocalName">The serialization property's local name under the GeoSPARQL namespace.</param>
    /// <param name="datatypeLocalName">The serialization datatype's local name under the GeoSPARQL namespace.</param>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    [TestMethod]
    [DataRow("/req/geometry-extension/geometry-as-wkt-literal", "asWKT", "wktLiteral", "POINT(1 2)")]
    [DataRow("/req/geometry-extension/geometry-as-gml-literal", "asGML", "gmlLiteral", "<gml:Point xmlns:gml=\"http://www.opengis.net/ont/gml\"><gml:pos>1 2</gml:pos></gml:Point>")]
    [DataRow("/req/geometry-extension/geometry-as-geojson-literal", "asGeoJSON", "geoJSONLiteral", "{\"type\":\"Point\",\"coordinates\":[1.0,2.0]}")]
    [DataRow("/req/geometry-extension/geometry-as-kml-literal", "asKML", "kmlLiteral", "<Point><coordinates>1,2</coordinates></Point>")]
    [DataRow("/req/geometry-extension-dggs/geometry-as-dggs-literal", "asDGGS", "dggsLiteral", "<https://w3id.org/dggs/auspix> CELL (R3234)")]
    public async Task SerializationPropertyBindsTheTypedLiteral(string requirementId, string propertyLocalName, string datatypeLocalName, string lexicalForm)
    {
        string datatypeIri = GeoVocabulary.Geo.Namespace + datatypeLocalName;
        IReadOnlyList<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "g"), Iri(GeoVocabulary.Geo.Namespace + propertyLocalName), new Literal(Utf8Strings.From(lexicalForm), Iri(datatypeIri))),
            new DataTriple(Iri(Ex + "g"), Iri(Ex + "unrelated"), Iri(Ex + "o2")),
        ];
        string query = Prefixes + $" SELECT ?g ?serialization WHERE {{ ?g geo:{propertyLocalName} ?serialization }}";

        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, data).ConfigureAwait(false);

        Assert.HasCount(1, solutions, $"{requirementId}: the serialization-property BGP must match exactly the asserted literal.");
        AssertBoundIri(solutions[0], "g", Ex + "g", requirementId);
        Literal serialization = BoundLiteral(solutions[0], "serialization", requirementId);
        Assert.AreEqual(lexicalForm, serialization.Value.ToString(), $"{requirementId}: the bound literal keeps its lexical form.");
        Assert.IsTrue(serialization.Datatype.Iri.Span.SequenceEqual(Utf8Strings.From(datatypeIri).Span), $"{requirementId}: the bound literal keeps its serialization datatype.");
    }

    /// <summary>The specification's canonical feature-to-serialization shape answers as one query: a three-pattern join over <c>rdf:type</c>, <c>geo:hasDefaultGeometry</c>, and <c>geo:asWKT</c> binds exactly the feature that has a serialized geometry, leaving the geometry-less feature out.</summary>
    [TestMethod]
    public async Task FeatureGeometrySerializationJoinAnswers()
    {
        const string Wkt = "POINT(30 10)";
        IReadOnlyList<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "f"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Geo.Feature)),
            new DataTriple(Iri(Ex + "f"), new NamedNode(GeoVocabulary.Geo.HasDefaultGeometry), Iri(Ex + "g")),
            new DataTriple(Iri(Ex + "g"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Geo.Geometry)),
            new DataTriple(Iri(Ex + "g"), new NamedNode(GeoVocabulary.Geo.AsWkt), new Literal(Utf8Strings.From(Wkt), new NamedNode(GeoVocabulary.Geo.WktLiteral))),
            new DataTriple(Iri(Ex + "bare"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Geo.Feature)),
        ];
        string query = Prefixes + " SELECT ?f ?wkt WHERE { ?f a geo:Feature . ?f geo:hasDefaultGeometry ?g . ?g geo:asWKT ?wkt }";

        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, data).ConfigureAwait(false);

        Assert.HasCount(1, solutions, "The join must answer exactly the feature with a serialized default geometry.");
        AssertBoundIri(solutions[0], "f", Ex + "f", "/req/core/feature-properties");
        Assert.AreEqual(Wkt, BoundLiteral(solutions[0], "wkt", "/req/geometry-extension/geometry-as-wkt-literal").Value.ToString(), "The join binds the serialized form.");
    }

    /// <summary>Parses, translates, and evaluates a query over the given data graph under the engine-default expression context.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="data">The data triples the engine is built over.</param>
    /// <returns>The solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> RunAsync(string query, IReadOnlyList<DataTriple> data)
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(query, pool);

        return await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses and translates a query to algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The string pool the parse allocates from.</param>
    /// <returns>The algebra root.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Asserts a variable is bound to the expected IRI.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name without the marker.</param>
    /// <param name="expectedIri">The expected IRI text.</param>
    /// <param name="requirementId">The requirement id for the failure message.</param>
    private static void AssertBoundIri(SparqlSolution solution, string variableName, string expectedIri, string requirementId)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"{requirementId}: expected ?{variableName} to be bound.");
        Assert.IsInstanceOfType<NamedNode>(value);
        Assert.IsTrue(((NamedNode)value).Iri.Span.SequenceEqual(Utf8Strings.From(expectedIri).Span), $"{requirementId}: ?{variableName} must bind {expectedIri}.");
    }

    /// <summary>Asserts a variable is bound to a literal and returns it.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name without the marker.</param>
    /// <param name="requirementId">The requirement id for the failure message.</param>
    /// <returns>The bound literal.</returns>
    private static Literal BoundLiteral(SparqlSolution solution, string variableName, string requirementId)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"{requirementId}: expected ?{variableName} to be bound.");
        Assert.IsInstanceOfType<Literal>(value);

        return (Literal)value;
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Builds a SPARQL variable.</summary>
    /// <param name="name">The variable name without the marker.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }
}
