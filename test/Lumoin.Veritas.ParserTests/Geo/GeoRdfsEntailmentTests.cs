using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The RDFS-entailment conformance rows for the GeoSPARQL arm: basic graph patterns answered on a reasoned
/// engine over the vendored GeoSPARQL ontology, the vendored simple-features class hierarchy, and the
/// vendored GML 3.2.1 geometry class hierarchy. The rows pin the entailments the RDFS regime demands —
/// subclass, subproperty, domain, and range — through the engine's query surface, alongside the negative
/// rows that keep the closure honest (no spurious type, no sibling leak) and the exact-member selections;
/// the GML rows additionally pin the hierarchy's DAG shape (dual parentage) and its side-hierarchy bridges
/// into <c>geo:Geometry</c>, and the vocabulary roster is pinned membership-exact against the vendored
/// document. Each row names the census requirement id it decides; the census manifest's evidence entries
/// point back here.
/// </summary>
[TestClass]
internal sealed class GeoRdfsEntailmentTests
{
    /// <summary>The example namespace the fact graphs and queries share.</summary>
    private const string Ex = "urn:x-veritas:geo-arm#";

    /// <summary>The prefix declarations every query in this battery carries.</summary>
    private const string Prefixes =
        "PREFIX geo: <" + GeoVocabulary.Geo.Namespace + "> PREFIX sf: <" + GeoVocabulary.Sf.Namespace + "> PREFIX gml: <" + GeoVocabulary.Gml.Namespace + "> PREFIX ex: <" + Ex + ">";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The reasoned open over the vendored ontology and hierarchy is consistent and surfaces its provenance — the schema itself never poisons the closure. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task ReasonedOpenOverTheVendoredOntologyIsConsistent()
    {
        VeritasEngine engine = await OpenReasonedAsync([], includeSimpleFeatures: true, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsNotNull(engine.ReasoningProvenance, "The reasoned open surfaces its outcome on the facade.");
        Assert.IsTrue(engine.ReasoningProvenance.IsConsistent, "The vendored ontology and hierarchy derive no contradiction.");
    }

    /// <summary>A member typed with the subclass answers the superclass graph pattern: <c>geo:Feature</c> is an RDFS subclass of <c>geo:SpatialObject</c>. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task SubClassEntailmentAnswersThroughAsk()
    {
        VeritasEngine engine = await OpenReasonedAsync(
            [new DataTriple(Iri(Ex + "f"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Geo.Feature))],
            includeSimpleFeatures: false, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(
            await AskAsync(engine, "ASK { ex:f a geo:SpatialObject }").ConfigureAwait(false),
            "The asserted geo:Feature member answers the geo:SpatialObject pattern under RDFS entailment.");
    }

    /// <summary>An assertion over the subproperty answers the superproperty graph pattern: <c>geo:hasDefaultGeometry</c> is an RDFS subproperty of <c>geo:hasGeometry</c>. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task SubPropertyEntailmentAnswersThroughAsk()
    {
        VeritasEngine engine = await OpenReasonedAsync(
            [new DataTriple(Iri(Ex + "f"), new NamedNode(GeoVocabulary.Geo.HasDefaultGeometry), Iri(Ex + "g"))],
            includeSimpleFeatures: false, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(
            await AskAsync(engine, "ASK { ex:f geo:hasGeometry ex:g }").ConfigureAwait(false),
            "The asserted geo:hasDefaultGeometry link answers the geo:hasGeometry pattern under RDFS entailment.");
    }

    /// <summary>The property's declared domain types the subject: <c>geo:hasGeometry</c> has domain <c>geo:Feature</c>. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task DomainEntailmentTypesTheSubject()
    {
        VeritasEngine engine = await OpenReasonedAsync(GeometryLinkFacts(), includeSimpleFeatures: false, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(
            await AskAsync(engine, "ASK { ex:f a geo:Feature }").ConfigureAwait(false),
            "The geo:hasGeometry domain types the subject as geo:Feature under RDFS entailment.");
    }

    /// <summary>The property's declared range types the object: <c>geo:hasGeometry</c> has range <c>geo:Geometry</c>. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task RangeEntailmentTypesTheObject()
    {
        VeritasEngine engine = await OpenReasonedAsync(GeometryLinkFacts(), includeSimpleFeatures: false, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(
            await AskAsync(engine, "ASK { ex:g a geo:Geometry }").ConfigureAwait(false),
            "The geo:hasGeometry range types the object as geo:Geometry under RDFS entailment.");
    }

    /// <summary>The closure adds no type the schema does not entail: the range-typed geometry never becomes a <c>geo:Feature</c>. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task EntailmentAddsNoSpuriousType()
    {
        VeritasEngine engine = await OpenReasonedAsync(GeometryLinkFacts(), includeSimpleFeatures: false, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsFalse(
            await AskAsync(engine, "ASK { ex:g a geo:Feature }").ConfigureAwait(false),
            "Nothing entails the geometry as a geo:Feature; the closure must not invent the type.");
    }

    /// <summary>A selection over the entailed superclass returns exactly the members the schema entails: the domain-typed feature and the range-typed geometry, both <c>geo:SpatialObject</c>s. (<c>/req/rdfs-entailment-extension/bgp-rdfs-ent</c>.)</summary>
    [TestMethod]
    public async Task SelectOverTheEntailedClassReturnsTheExactMembers()
    {
        VeritasEngine engine = await OpenReasonedAsync(GeometryLinkFacts(), includeSimpleFeatures: false, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        string[] members = await SelectIrisAsync(engine, "SELECT ?x WHERE { ?x a geo:SpatialObject }").ConfigureAwait(false);

        Assert.AreSequenceEqual(new[] { Ex + "f", Ex + "g" }, members);
    }

    /// <summary>A member typed with a simple-features leaf answers the hierarchy root's graph pattern: <c>sf:Point</c> is an RDFS subclass of <c>sf:Geometry</c>. (<c>/req/rdfs-entailment-extension/wkt-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task SfDirectSubClassAnswersThroughAsk()
    {
        VeritasEngine engine = await OpenReasonedAsync(PointFacts(), includeSimpleFeatures: true, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(
            await AskAsync(engine, "ASK { ex:p a sf:Geometry }").ConfigureAwait(false),
            "The asserted sf:Point member answers the sf:Geometry pattern under RDFS entailment.");
    }

    /// <summary>The deep hierarchy chain answers at every level: <c>sf:MultiPolygon</c> reaches <c>sf:MultiSurface</c>, <c>sf:GeometryCollection</c>, and <c>sf:Geometry</c>. (<c>/req/rdfs-entailment-extension/wkt-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task SfDeepChainAnswersThroughAsk()
    {
        VeritasEngine engine = await OpenReasonedAsync(
            [new DataTriple(Iri(Ex + "m"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Sf.MultiPolygon))],
            includeSimpleFeatures: true, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, "ASK { ex:m a sf:MultiSurface }").ConfigureAwait(false), "The first hierarchy step answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:m a sf:GeometryCollection }").ConfigureAwait(false), "The second hierarchy step answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:m a sf:Geometry }").ConfigureAwait(false), "The hierarchy root answers.");
    }

    /// <summary>The simple-features hierarchy reaches the GeoSPARQL classes: <c>sf:Geometry</c> is an RDFS subclass of <c>geo:Geometry</c>, itself a subclass of <c>geo:SpatialObject</c>. (<c>/req/rdfs-entailment-extension/wkt-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task SfHierarchyReachesTheGeoClasses()
    {
        VeritasEngine engine = await OpenReasonedAsync(PointFacts(), includeSimpleFeatures: true, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, "ASK { ex:p a geo:Geometry }").ConfigureAwait(false), "The bridge into geo:Geometry answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:p a geo:SpatialObject }").ConfigureAwait(false), "The GeoSPARQL root answers through the bridge.");
    }

    /// <summary>The hierarchy never leaks sideways: a <c>sf:Point</c> member is no <c>sf:Polygon</c>. (<c>/req/rdfs-entailment-extension/wkt-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task SfSiblingStaysUnentailed()
    {
        VeritasEngine engine = await OpenReasonedAsync(PointFacts(), includeSimpleFeatures: true, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsFalse(
            await AskAsync(engine, "ASK { ex:p a sf:Polygon }").ConfigureAwait(false),
            "Nothing entails the point as a polygon; the closure must not leak across siblings.");
    }

    /// <summary>A selection over the hierarchy root returns exactly the typed members, leaving the untyped decoy out. (<c>/req/rdfs-entailment-extension/wkt-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task SfSelectOverTheHierarchyReturnsTheExactMembers()
    {
        IReadOnlyList<DataTriple> facts =
        [
            new DataTriple(Iri(Ex + "m"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Sf.MultiPolygon)),
            new DataTriple(Iri(Ex + "p"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Sf.Point)),
            new DataTriple(Iri(Ex + "decoy"), new NamedNode(Vocabulary.Rdf.Type), Iri(Ex + "Unrelated")),
        ];
        VeritasEngine engine = await OpenReasonedAsync(facts, includeSimpleFeatures: true, includeGml: false, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        string[] members = await SelectIrisAsync(engine, "SELECT ?x WHERE { ?x a sf:Geometry }").ConfigureAwait(false);

        Assert.AreSequenceEqual(new[] { Ex + "m", Ex + "p" }, members);
    }

    /// <summary>The reasoned open over the vendored GeoSPARQL ontology, the simple-features hierarchy, and the GML class hierarchy is consistent — the three vendored documents co-load without contradiction. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlReasonedOpenOverTheVendoredHierarchyIsConsistent()
    {
        VeritasEngine engine = await OpenReasonedAsync([], includeSimpleFeatures: true, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsNotNull(engine.ReasoningProvenance, "The reasoned open surfaces its outcome on the facade.");
        Assert.IsTrue(engine.ReasoningProvenance.IsConsistent, "The vendored ontology and both class hierarchies derive no contradiction.");
    }

    /// <summary>A member typed with a GML leaf answers its direct parent's graph pattern: <c>gml:Point</c> is an RDFS subclass of <c>gml:AbstractGeometricPrimitive</c>. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlDirectSubClassAnswersThroughAsk()
    {
        VeritasEngine engine = await OpenReasonedAsync(GmlPointFacts(), includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(
            await AskAsync(engine, "ASK { ex:p a gml:AbstractGeometricPrimitive }").ConfigureAwait(false),
            "The asserted gml:Point member answers the gml:AbstractGeometricPrimitive pattern under RDFS entailment.");
    }

    /// <summary>The deep hierarchy chain answers at every entailed level: <c>gml:Tin</c> reaches <c>gml:TriangulatedSurface</c>, <c>gml:PolyhedralSurface</c>, <c>gml:Surface</c>, <c>gml:OrientableSurface</c> (the diamond's second branch), <c>gml:AbstractGeometricPrimitive</c>, and <c>gml:AbstractGeometry</c>. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlDeepChainAnswersThroughAsk()
    {
        VeritasEngine engine = await OpenReasonedAsync(
            [new DataTriple(Iri(Ex + "t"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.Tin))],
            includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, "ASK { ex:t a gml:TriangulatedSurface }").ConfigureAwait(false), "The first hierarchy step answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:t a gml:PolyhedralSurface }").ConfigureAwait(false), "The second hierarchy step answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:t a gml:Surface }").ConfigureAwait(false), "The third hierarchy step answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:t a gml:OrientableSurface }").ConfigureAwait(false), "The diamond's second branch answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:t a gml:AbstractGeometricPrimitive }").ConfigureAwait(false), "The branches converge on the geometric primitive.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:t a gml:AbstractGeometry }").ConfigureAwait(false), "The geometry root answers.");
    }

    /// <summary>Dual parentage answers both parents: <c>gml:Surface</c> is a subclass of <c>gml:AbstractGeometricPrimitive</c> AND <c>gml:OrientableSurface</c> — the GML hierarchy is a DAG, not a tree. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlDualParentageAnswersBothParents()
    {
        VeritasEngine engine = await OpenReasonedAsync(
            [new DataTriple(Iri(Ex + "s"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.Surface))],
            includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, "ASK { ex:s a gml:AbstractGeometricPrimitive }").ConfigureAwait(false), "The primitive parent answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:s a gml:OrientableSurface }").ConfigureAwait(false), "The orientable parent answers.");
    }

    /// <summary>The GML hierarchy reaches the GeoSPARQL classes: <c>gml:AbstractGeometry</c> is an RDFS subclass of <c>geo:Geometry</c>, itself a subclass of <c>geo:SpatialObject</c>. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlHierarchyReachesTheGeoClasses()
    {
        VeritasEngine engine = await OpenReasonedAsync(GmlPointFacts(), includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, "ASK { ex:p a geo:Geometry }").ConfigureAwait(false), "The bridge into geo:Geometry answers.");
        Assert.IsTrue(await AskAsync(engine, "ASK { ex:p a geo:SpatialObject }").ConfigureAwait(false), "The GeoSPARQL root answers through the bridge.");
    }

    /// <summary>The surface-patch side-hierarchy bridges into <c>geo:Geometry</c> beside the geometry root: a <c>gml:PolygonPatch</c> member reaches <c>geo:Geometry</c> through <c>gml:AbstractSurfacePatch</c> yet is no <c>gml:AbstractGeometry</c> — the closure follows the vendored document's three-bridge structure. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlPatchHierarchyBridgesBesideTheGeometryRoot()
    {
        VeritasEngine engine = await OpenReasonedAsync(
            [new DataTriple(Iri(Ex + "patch"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.PolygonPatch))],
            includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(engine, "ASK { ex:patch a geo:Geometry }").ConfigureAwait(false), "The side-hierarchy's own bridge answers.");
        Assert.IsFalse(
            await AskAsync(engine, "ASK { ex:patch a gml:AbstractGeometry }").ConfigureAwait(false),
            "Nothing entails the patch under the geometry root; the closure must not invent the type.");
    }

    /// <summary>The hierarchy never leaks sideways: a <c>gml:Point</c> member is no <c>gml:Solid</c>. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlSiblingStaysUnentailed()
    {
        VeritasEngine engine = await OpenReasonedAsync(GmlPointFacts(), includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        Assert.IsFalse(
            await AskAsync(engine, "ASK { ex:p a gml:Solid }").ConfigureAwait(false),
            "Nothing entails the point as a solid; the closure must not leak across siblings.");
    }

    /// <summary>Selections return exactly the entailed members: the <c>gml:AbstractGeometry</c> root gathers the Tin and Point members but never the side-hierarchy patch member, while <c>geo:Geometry</c> gathers all three through their respective bridges, leaving the untyped decoy out of both. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlSelectOverTheHierarchyReturnsTheExactMembers()
    {
        IReadOnlyList<DataTriple> facts =
        [
            new DataTriple(Iri(Ex + "t"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.Tin)),
            new DataTriple(Iri(Ex + "p"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.Point)),
            new DataTriple(Iri(Ex + "patch"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.PolygonPatch)),
            new DataTriple(Iri(Ex + "decoy"), new NamedNode(Vocabulary.Rdf.Type), Iri(Ex + "Unrelated"))
        ];
        VeritasEngine engine = await OpenReasonedAsync(facts, includeSimpleFeatures: false, includeGml: true, TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        string[] underTheRoot = await SelectIrisAsync(engine, "SELECT ?x WHERE { ?x a gml:AbstractGeometry }").ConfigureAwait(false);
        string[] underTheBridge = await SelectIrisAsync(engine, "SELECT ?x WHERE { ?x a geo:Geometry }").ConfigureAwait(false);

        Assert.AreSequenceEqual(new[] { Ex + "p", Ex + "t" }, underTheRoot);
        Assert.AreSequenceEqual(new[] { Ex + "p", Ex + "patch", Ex + "t" }, underTheBridge);
    }

    /// <summary>The production vocabulary's GML roster mirrors the vendored hierarchy membership-exactly: every <c>owl:Class</c> the vendored document declares under the GML namespace is a <c>GeoVocabulary.Gml</c> constant and nothing more, so a re-vendor or transcription slip fails loudly by name. (<c>/req/rdfs-entailment-extension/gml-geometry-types</c>.)</summary>
    [TestMethod]
    public async Task GmlVocabularyRosterMatchesTheVendoredHierarchy()
    {
        List<DataTriple> triples = [];
        await AppendRdfXmlOntologyAsync(triples, "gml_32_geometries.rdf", TestContext.CancellationToken).ConfigureAwait(false);

        HashSet<string> declaredSet = [];
        foreach(DataTriple triple in triples)
        {
            if(triple.Predicate is NamedNode predicate
                && predicate.Iri.Span.SequenceEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8)
                && triple.Object is NamedNode classTerm
                && classTerm.Iri.Span.SequenceEqual("http://www.w3.org/2002/07/owl#Class"u8)
                && triple.Subject is NamedNode subject
                && subject.Iri.Span.StartsWith("http://www.opengis.net/ont/gml#"u8))
            {
                declaredSet.Add(subject.Iri.ToString());
            }
        }

        string[] declared = [.. declaredSet];
        string[] roster = GmlRosterIris();
        Array.Sort(declared, StringComparer.Ordinal);
        Array.Sort(roster, StringComparer.Ordinal);

        Assert.AreSequenceEqual(roster, declared);
    }

    /// <summary>The feature-to-geometry fact graph the domain and range rows share.</summary>
    /// <returns>The fact triples.</returns>
    private static IReadOnlyList<DataTriple> GeometryLinkFacts()
    {
        return [new DataTriple(Iri(Ex + "f"), new NamedNode(GeoVocabulary.Geo.HasGeometry), Iri(Ex + "g"))];
    }

    /// <summary>The single-point fact graph the simple-features rows share.</summary>
    /// <returns>The fact triples.</returns>
    private static IReadOnlyList<DataTriple> PointFacts()
    {
        return [new DataTriple(Iri(Ex + "p"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Sf.Point))];
    }

    /// <summary>The single-point fact graph the GML hierarchy rows share.</summary>
    /// <returns>The fact triples.</returns>
    private static IReadOnlyList<DataTriple> GmlPointFacts()
    {
        return [new DataTriple(Iri(Ex + "p"), new NamedNode(Vocabulary.Rdf.Type), new NamedNode(GeoVocabulary.Gml.Point))];
    }

    /// <summary>The complete <see cref="GeoVocabulary.Gml"/> class roster as IRI texts — the production side of the membership-exact roster pin.</summary>
    /// <returns>The 53 class IRIs.</returns>
    private static string[] GmlRosterIris()
    {
        Utf8String[] roster =
        [
            GeoVocabulary.Gml.Point,
            GeoVocabulary.Gml.AbstractGeometricPrimitive,
            GeoVocabulary.Gml.AbstractGriddedSurface,
            GeoVocabulary.Gml.AbstractParametricCurveSurface,
            GeoVocabulary.Gml.PolyhedralSurface,
            GeoVocabulary.Gml.Surface,
            GeoVocabulary.Gml.Arc,
            GeoVocabulary.Gml.ArcString,
            GeoVocabulary.Gml.PolynomialSpline,
            GeoVocabulary.Gml.SplineCurve,
            GeoVocabulary.Gml.MultiCurve,
            GeoVocabulary.Gml.MultiGeometry,
            GeoVocabulary.Gml.CompositeSurface,
            GeoVocabulary.Gml.Composite,
            GeoVocabulary.Gml.OrientableSurface,
            GeoVocabulary.Gml.AbstractCurveSegment,
            GeoVocabulary.Gml.Cylinder,
            GeoVocabulary.Gml.Shell,
            GeoVocabulary.Gml.Polygon,
            GeoVocabulary.Gml.Tin,
            GeoVocabulary.Gml.TriangulatedSurface,
            GeoVocabulary.Gml.AbstractGeometry,
            GeoVocabulary.Gml.Bezier,
            GeoVocabulary.Gml.BSpline,
            GeoVocabulary.Gml.Curve,
            GeoVocabulary.Gml.OrientableCurve,
            GeoVocabulary.Gml.LineStringSegment,
            GeoVocabulary.Gml.Geodesic,
            GeoVocabulary.Gml.GeodesicString,
            GeoVocabulary.Gml.AbstractSurfacePatch,
            GeoVocabulary.Gml.GeometricComplex,
            GeoVocabulary.Gml.ArcByBulge,
            GeoVocabulary.Gml.ArcStringByBulge,
            GeoVocabulary.Gml.CircleByCenterPoint,
            GeoVocabulary.Gml.ArcByCenterPoint,
            GeoVocabulary.Gml.MultiPoint,
            GeoVocabulary.Gml.OffsetCurve,
            GeoVocabulary.Gml.LineString,
            GeoVocabulary.Gml.Circle,
            GeoVocabulary.Gml.Clothoid,
            GeoVocabulary.Gml.Triangle,
            GeoVocabulary.Gml.PolygonPatch,
            GeoVocabulary.Gml.CubicSpline,
            GeoVocabulary.Gml.Cone,
            GeoVocabulary.Gml.CompositeSolid,
            GeoVocabulary.Gml.Solid,
            GeoVocabulary.Gml.LinearRing,
            GeoVocabulary.Gml.Ring,
            GeoVocabulary.Gml.MultiSolid,
            GeoVocabulary.Gml.CompositeCurve,
            GeoVocabulary.Gml.Rectangle,
            GeoVocabulary.Gml.Sphere,
            GeoVocabulary.Gml.MultiSurface
        ];

        string[] iris = new string[roster.Length];
        for(int i = 0; i < roster.Length; i++)
        {
            iris[i] = roster[i].ToString();
        }

        return iris;
    }

    /// <summary>Opens a reasoned engine over the vendored GeoSPARQL ontology, optionally the vendored simple-features and GML class hierarchies, and the given fact triples, under the default reasoning configuration.</summary>
    /// <param name="facts">The fact triples asserted beside the schema.</param>
    /// <param name="includeSimpleFeatures">Whether the simple-features hierarchy loads beside the GeoSPARQL ontology.</param>
    /// <param name="includeGml">Whether the GML 3.2.1 geometry class hierarchy loads beside the GeoSPARQL ontology.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The opened engine.</returns>
    private static async Task<VeritasEngine> OpenReasonedAsync(IReadOnlyList<DataTriple> facts, bool includeSimpleFeatures, bool includeGml, CancellationToken cancellationToken)
    {
        List<DataTriple> triples = [];
        await AppendOntologyAsync(triples, "geo.ttl", cancellationToken).ConfigureAwait(false);
        if(includeSimpleFeatures)
        {
            await AppendOntologyAsync(triples, "sf_geometries.ttl", cancellationToken).ConfigureAwait(false);
        }

        if(includeGml)
        {
            await AppendRdfXmlOntologyAsync(triples, "gml_32_geometries.rdf", cancellationToken).ConfigureAwait(false);
        }

        triples.AddRange(facts);

        return await VeritasEngine.OpenAsync(triples, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses a vendored ontology file and appends its triples.</summary>
    /// <param name="triplesToAppendTo">The triple list the parsed ontology is appended to.</param>
    /// <param name="fileName">The ontology file name under the vendored <c>semantic-resources/ontologies</c> directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task AppendOntologyAsync(List<DataTriple> triplesToAppendTo, string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Geo"), "semantic-resources", "ontologies", fileName);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Uri baseUri = new(Path.GetFullPath(path));

        DiagnosticBag diagnostics = new();
        await foreach(Quad quad in TurtleReader.ReadAsync(
            bytes, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseUri.AbsoluteUri, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            triplesToAppendTo.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        if(diagnostics.HasErrors)
        {
            throw new TurtleParseException(TurtleConformanceReader.DescribeFirstError(diagnostics));
        }
    }

    /// <summary>Parses a vendored RDF/XML schema document and appends its triples. Relative references resolve against the document's own <c>xml:base</c>; the file URI serves only as the outer fallback base.</summary>
    /// <param name="triplesToAppendTo">The triple list the parsed document is appended to.</param>
    /// <param name="fileName">The document file name under the vendored <c>schemas/gml/3.2.1</c> directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task AppendRdfXmlOntologyAsync(List<DataTriple> triplesToAppendTo, string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Geo"), "schemas", "gml", "3.2.1", fileName);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Uri baseUri = new(Path.GetFullPath(path));

        DiagnosticBag diagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseUri.AbsoluteUri));

        foreach(Quad quad in quads)
        {
            triplesToAppendTo.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        if(diagnostics.HasErrors)
        {
            throw new FormatException(TurtleConformanceReader.DescribeFirstError(diagnostics));
        }
    }

    /// <summary>Runs an ASK query with the battery prefixes.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="ask">The ASK query without prefixes.</param>
    /// <returns>The boolean verdict.</returns>
    private async Task<bool> AskAsync(VeritasEngine engine, string ask)
    {
        return await engine.AskAsync(Utf8Strings.From(Prefixes + " " + ask), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a single-variable SELECT with the battery prefixes and returns the bound IRIs, ordinally sorted.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="select">The SELECT query without prefixes, projecting <c>?x</c>.</param>
    /// <returns>The sorted IRI texts.</returns>
    private async Task<string[]> SelectIrisAsync(VeritasEngine engine, string select)
    {
        VeritasQueryResult result = await engine.QueryAsync(Utf8Strings.From(Prefixes + " " + select), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(result.Bindings, "The SELECT answers with bindings.");
        SparqlVariable x = new(Utf8Strings.From("x"));
        List<string> iris = [];
        foreach(SparqlSolution solution in result.Bindings.Solutions)
        {
            Assert.IsTrue(solution.TryGetValue(x, out RdfTerm value), "Every solution binds ?x.");
            Assert.IsInstanceOfType<NamedNode>(value);
            iris.Add(((NamedNode)value).Iri.ToString());
        }

        iris.Sort(StringComparer.Ordinal);

        return [.. iris];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }
}
