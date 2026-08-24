namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The well-known GeoJSON documents the codec test families share: the
/// canonical baseline values that appear across the reader, writer, and
/// serializer-context families, and the writer's fixed-point corpus. A
/// document lives here when more than one test or family compares against
/// it; one-off adversarial row literals stay inline in their rows, where the
/// offense they carry is the row's whole meaning.
/// </summary>
internal static class GeoJsonTestDocuments
{
    /// <summary>The canonical point every baseline-equality test compares against.</summary>
    public const string CanonicalPoint = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

    /// <summary>The canonical three-dimensional point.</summary>
    public const string CanonicalPointWithAltitude = "{\"type\":\"Point\",\"coordinates\":[1,2,3]}";

    /// <summary>The canonical typed empty collection — also the degradation target of an uninitialized carrier.</summary>
    public const string CanonicalEmptyCollection = "{\"type\":\"GeometryCollection\",\"geometries\":[]}";

    /// <summary>The canonical one-member collection every member-shape test builds from.</summary>
    public const string CanonicalCollectionOfOnePoint = "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}]}";

    /// <summary>The canonical two-ring polygon: shell then hole, roles positional.</summary>
    public const string CanonicalPolygonWithHole = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[3,0],[3,3],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}";

    /// <summary>
    /// The canonical three-member collection with one duplicated member in
    /// non-ascending order — the fixture that turns red under any dedup,
    /// sort, promotion, or unwrap.
    /// </summary>
    public const string CanonicalCollectionWithDuplicateMember = "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[3,4]},{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"Point\",\"coordinates\":[3,4]}]}";

    /// <summary>
    /// The canonical texts the writer is a fixed point over: every entry
    /// parses and rewrites to itself byte for byte, covering all seven
    /// kinds, both windings, mixed-Z closure, the antimeridian-crossing
    /// line, and the duplicate-member collection.
    /// </summary>
    public static string[] CanonicalCorpus { get; } =
    [
        CanonicalPoint,
        "{\"type\":\"Point\",\"coordinates\":[]}",
        "{\"type\":\"LineString\",\"coordinates\":[[1,2],[3,4]]}",
        "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}",
        CanonicalPolygonWithHole,
        "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[0,1],[1,1],[0,0]]]}",
        "{\"type\":\"Polygon\",\"coordinates\":[[[0,0,1],[1,0,1],[1,1,1],[0,0,9]]]}",
        "{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],[3,4]]}",
        "{\"type\":\"MultiLineString\",\"coordinates\":[[[0,0],[1,1]]]}",
        "{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]]]}",
        "{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]],[[[2,2],[3,2],[3,3],[2,2]]]]}",
        "{\"type\":\"LineString\",\"coordinates\":[[170,45],[-170,45]]}",
        CanonicalEmptyCollection,
        CanonicalCollectionOfOnePoint,
        CanonicalCollectionWithDuplicateMember,
    ];

    /// <summary>The canonical feature: every member present, canonical order, shortest-form numbers.</summary>
    public const string CanonicalFeature = "{\"type\":\"Feature\",\"id\":\"f1\",\"bbox\":[1,2,3,4],\"geometry\":{\"type\":\"Point\",\"coordinates\":[1,2]},\"properties\":{\"name\":\"x\"}}";

    /// <summary>The canonical minimal feature: only the three required members.</summary>
    public const string CanonicalMinimalFeature = "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[1,2]},\"properties\":{}}";

    /// <summary>The canonical unlocated feature: a null geometry and null properties.</summary>
    public const string CanonicalUnlocatedFeature = "{\"type\":\"Feature\",\"geometry\":null,\"properties\":null}";

    /// <summary>The canonical bytes an uninitialized feature carrier degrades to.</summary>
    public const string DefaultFeatureDocument = "{\"type\":\"Feature\",\"geometry\":{\"type\":\"GeometryCollection\",\"geometries\":[]},\"properties\":null}";

    /// <summary>The canonical empty feature collection — also the streaming writer's no-feature form.</summary>
    public const string CanonicalEmptyFeatureCollection = "{\"type\":\"FeatureCollection\",\"features\":[]}";

    /// <summary>The canonical one-feature collection.</summary>
    public const string CanonicalFeatureCollection = "{\"type\":\"FeatureCollection\",\"bbox\":[1,2,3,4],\"features\":[{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[1,2]},\"properties\":{}}]}";

    /// <summary>
    /// The canonical antimeridian-spanning bounding-box feature: west
    /// exceeds east, the RFC's own Fiji-shaped convention.
    /// </summary>
    public const string CanonicalAntimeridianBoundingBoxFeature = "{\"type\":\"Feature\",\"bbox\":[177,-20,-178,-16],\"geometry\":{\"type\":\"Point\",\"coordinates\":[179,-18]},\"properties\":null}";

    /// <summary>
    /// The canonical feature texts the feature writer is a fixed point
    /// over — admissible per the corpus rule: canonical skeleton and member
    /// order, shortest-form numbers, no crs, no foreign members, no
    /// geometry-level bbox; the properties and id extents ride verbatim.
    /// Every entry is also skeleton-clean (ASCII, unrepeated names), so the
    /// cleanliness row enumerates this corpus directly.
    /// </summary>
    public static string[] FeatureCorpus { get; } =
    [
        CanonicalFeature,
        CanonicalMinimalFeature,
        CanonicalUnlocatedFeature,
        DefaultFeatureDocument,
        CanonicalAntimeridianBoundingBoxFeature,
        "{\"type\":\"Feature\",\"id\":12345678901234567890123,\"geometry\":null,\"properties\":null}",
        "{\"type\":\"Feature\",\"bbox\":[1,2,0,3,4,10],\"geometry\":{\"type\":\"Point\",\"coordinates\":[1,2,5]},\"properties\":{\"a\":[1,{\"b\":null}]}}",
        "{\"type\":\"Feature\",\"geometry\":{\"type\":\"GeometryCollection\",\"geometries\":[]},\"properties\":{}}",
    ];
}
