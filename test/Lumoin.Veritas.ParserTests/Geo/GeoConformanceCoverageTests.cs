using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The arm-coverage ledger for the GeoSPARQL 1.1 conformance arm: every census requirement id is a named
/// row with exactly one disposition, so an unclaimed requirement is a named failure rather than a silent
/// gap. The census manifest is house-authored from OGC 22-047r1 (no upstream conformance-test manifest
/// exists); this ledger pins its totals — 57 requirement ids across the seven conformance classes — the
/// closed bucket and disposition vocabularies, per-entry coherence, and the exact decided set, so a
/// disposition flip is a deliberate reviewed edit, never drift.
/// </summary>
[TestClass]
internal sealed class GeoConformanceCoverageTests
{
    /// <summary>The census's per-conformance-class requirement-id totals (OGC 22-047r1 Annex A).</summary>
    private static readonly ImmutableDictionary<string, int> ClassTotals = new Dictionary<string, int>
    {
        ["core"] = 7,
        ["topology-vocab-extension"] = 3,
        ["geometry-extension"] = 29,
        ["geometry-extension-dggs"] = 8,
        ["geometry-topology-extension"] = 4,
        ["rdfs-entailment-extension"] = 3,
        ["query-rewrite-extension"] = 3,
    }.ToImmutableDictionary();

    /// <summary>The closed census bucket vocabulary.</summary>
    private static readonly ImmutableHashSet<string> Buckets =
        ["vocabulary", "datatype", "serialization", "function", "entailment", "query-rewrite", "other"];

    /// <summary>The closed disposition vocabulary.</summary>
    private static readonly ImmutableHashSet<string> Dispositions =
        [GeoRequirementCase.Decided, GeoRequirementCase.PinnedBacklog, GeoRequirementCase.SilencedWithReason];

    /// <summary>The census loads with 57 unique requirement ids and the per-class totals of the specification's conformance-class roster.</summary>
    [TestMethod]
    public void RosterMatchesTheCensusTotals()
    {
        ImmutableArray<GeoRequirementCase> cases = GeoRequirementManifest.Load();

        Assert.HasCount(57, cases);

        HashSet<string> seen = [];
        Dictionary<string, int> perClass = [];
        foreach(GeoRequirementCase entry in cases)
        {
            Assert.IsTrue(seen.Add(entry.RequirementId), $"{entry.RequirementId}: duplicate requirement id.");
            perClass[entry.ConformanceClass] = perClass.TryGetValue(entry.ConformanceClass, out int count) ? count + 1 : 1;
        }

        Assert.HasCount(ClassTotals.Count, perClass);
        foreach(KeyValuePair<string, int> expected in ClassTotals)
        {
            Assert.IsTrue(perClass.TryGetValue(expected.Key, out int actual), $"Conformance class '{expected.Key}' is missing from the census.");
            Assert.AreEqual(expected.Value, actual, $"Conformance class '{expected.Key}' has the wrong requirement count.");
        }
    }

    /// <summary>The decided set is exactly the pinned roster — the whole 57-id census: the protocol id (the serve command's HTTP endpoint is the engine's SPARQL 1.1 Protocol endpoint under the server deployment posture, its query operation — submission forms, results formats, dataset parameters, fault codes, and service description — served and pinned whole), the datatype-seam ids, the three serialization-datatype families (the GML, GeoJSON, and KML literal recognition ids with their SRS rules, empty-literal denotations, and the documented GML profile), the three DGGS literal ids (the prefix-certified literal grammar, the empty-literal denotation, and the serialization graph pattern), the vocabulary and serialization graph-pattern ids, the three RDFS-entailment ids (the entailment regime, the simple-features hierarchy, and the GML class hierarchy), the function ids (the WKT serializer, the three serialization-format serializers over the codec layer — the coordinate-reference-preserving GML writer and the CRS84-fixed GeoJSON and KML writers — the SRID accessor, the complete twenty-three-member non-topological query-function roster with transform over the closed certified coordinate-system roster, its DGGS mirror, the non-SF accessor set, the spatial-aggregate set, and the empty-literal denotation), the axis-order interpretation id (tuples read per the certified roster's declared axis orders), the whole Geometry Topology Extension class (the relate pattern test and the three eight-member predicate families), and the whole Query Rewrite Extension class (the three relation families' transformation rules) — and nothing is silenced; a disposition flip must edit this pin deliberately. The comparison ordinal-sorts both sides, so the pin is membership-exact and no listing or load order carries meaning.</summary>
    [TestMethod]
    public void DecidedSetIsExactlyThePinnedRoster()
    {
        List<string> decided = [];
        List<string> silenced = [];
        foreach(GeoRequirementCase entry in GeoRequirementManifest.Load())
        {
            if(entry.Disposition == GeoRequirementCase.Decided)
            {
                decided.Add(entry.RequirementId);
            }

            if(entry.Disposition == GeoRequirementCase.SilencedWithReason)
            {
                silenced.Add(entry.RequirementId);
            }
        }

        string[] expectedDecided =
        [
            "/req/core/feature-class",
            "/req/core/feature-collection-class",
            "/req/core/feature-properties",
            "/req/core/sparql-protocol",
            "/req/core/spatial-object-class",
            "/req/core/spatial-object-collection-class",
            "/req/core/spatial-object-properties",
            "/req/geometry-extension-dggs/asDGGS-function",
            "/req/geometry-extension-dggs/dggs-literal",
            "/req/geometry-extension-dggs/dggs-literal-empty",
            "/req/geometry-extension-dggs/geometry-as-dggs-literal",
            "/req/geometry-extension-dggs/query-functions",
            "/req/geometry-extension-dggs/query-functions-non-sf",
            "/req/geometry-extension-dggs/sa-functions",
            "/req/geometry-extension-dggs/srid-function",
            "/req/geometry-extension/asGML-function",
            "/req/geometry-extension/asGeoJSON-function",
            "/req/geometry-extension/asKML-function",
            "/req/geometry-extension/asWKT-function",
            "/req/geometry-extension/feature-properties",
            "/req/geometry-extension/geojson-literal",
            "/req/geometry-extension/geojson-literal-empty",
            "/req/geometry-extension/geojson-literal-srs",
            "/req/geometry-extension/geometry-as-geojson-literal",
            "/req/geometry-extension/geometry-as-gml-literal",
            "/req/geometry-extension/geometry-as-kml-literal",
            "/req/geometry-extension/geometry-as-wkt-literal",
            "/req/geometry-extension/geometry-class",
            "/req/geometry-extension/geometry-collection-class",
            "/req/geometry-extension/geometry-properties",
            "/req/geometry-extension/gml-literal",
            "/req/geometry-extension/gml-literal-empty",
            "/req/geometry-extension/gml-profile",
            "/req/geometry-extension/kml-literal",
            "/req/geometry-extension/kml-literal-empty",
            "/req/geometry-extension/kml-literal-srs",
            "/req/geometry-extension/query-functions",
            "/req/geometry-extension/query-functions-non-sf",
            "/req/geometry-extension/sa-functions",
            "/req/geometry-extension/srid-function",
            "/req/geometry-extension/wkt-axis-order",
            "/req/geometry-extension/wkt-literal",
            "/req/geometry-extension/wkt-literal-default-srs",
            "/req/geometry-extension/wkt-literal-empty",
            "/req/geometry-topology-extension/eh-query-functions",
            "/req/geometry-topology-extension/rcc8-query-functions",
            "/req/geometry-topology-extension/relate-query-function",
            "/req/geometry-topology-extension/sf-query-functions",
            "/req/query-rewrite-extension/eh-query-rewrite",
            "/req/query-rewrite-extension/rcc8-query-rewrite",
            "/req/query-rewrite-extension/sf-query-rewrite",
            "/req/rdfs-entailment-extension/bgp-rdfs-ent",
            "/req/rdfs-entailment-extension/gml-geometry-types",
            "/req/rdfs-entailment-extension/wkt-geometry-types",
            "/req/topology-vocab-extension/eh-spatial-relations",
            "/req/topology-vocab-extension/rcc8-spatial-relations",
            "/req/topology-vocab-extension/sf-spatial-relations"
        ];

        //The pin is membership-exact: both sides sort into one ordinal order before comparing,
        //so neither the listing order above nor the manifest loader's internal order carries meaning.
        Array.Sort(expectedDecided, StringComparer.Ordinal);
        decided.Sort(StringComparer.Ordinal);
        Assert.AreSequenceEqual(expectedDecided, decided);
        Assert.IsEmpty(silenced, "The claim set is ruled total coverage - every conformance class is claimed, so no requirement is ever silenced.");
    }

    /// <summary>Every census requirement id is a named, coherent row: the id sits under its conformance class, the bucket and disposition come from the closed vocabularies, a decided entry names its evidence, and a non-decided entry names its reason.</summary>
    /// <param name="entry">The census entry under test.</param>
    [TestMethod]
    [GeoRequirementData]
    public void EveryCensusIdIsANamedCoherentRow(GeoRequirementCase entry)
    {
        Assert.StartsWith($"/req/{entry.ConformanceClass}/", entry.RequirementId, $"{entry.RequirementId}: the id must sit under its conformance class '{entry.ConformanceClass}'.");
        Assert.Contains(entry.Bucket, Buckets, $"{entry.RequirementId}: unknown bucket '{entry.Bucket}'.");
        Assert.Contains(entry.Disposition, Dispositions, $"{entry.RequirementId}: unknown disposition '{entry.Disposition}'.");

        if(entry.Disposition == GeoRequirementCase.Decided)
        {
            Assert.IsNotEmpty(entry.Evidence, $"{entry.RequirementId}: a decided entry must name the rows deciding it.");
            Assert.IsEmpty(entry.Reason, $"{entry.RequirementId}: a decided entry carries evidence, not a reason.");
        }
        else
        {
            Assert.IsNotEmpty(entry.Reason, $"{entry.RequirementId}: a non-decided entry must name what it awaits.");
            Assert.IsEmpty(entry.Evidence, $"{entry.RequirementId}: evidence belongs to decided entries only.");
        }
    }
}
