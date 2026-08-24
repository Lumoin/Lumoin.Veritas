using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// Well-known IRI constants from the GeoSPARQL 1.1 vocabularies: the GeoSPARQL ontology namespace
/// (<see cref="Geo"/>), the extension-function namespace (<see cref="Geof"/>), and the Simple Features
/// geometry ontology (<see cref="Sf"/>).
/// </summary>
/// <remarks>
/// <para>
/// These are allocated once as static byte arrays. Since <see cref="Utf8String"/> is a struct wrapping
/// <see cref="System.ReadOnlyMemory{T}"/>, these do not participate in pool allocation and remain valid
/// for the lifetime of the application.
/// </para>
/// <para>
/// The <see cref="Geo"/> and <see cref="Geof"/> rosters are authored from the house requirement census of
/// OGC 22-047r1 (GeoSPARQL 1.1), which enumerates every term the conformance classes demand — including
/// the functions the published function vocabulary omits. The <see cref="Sf"/> roster is measured from the
/// ratified Simple Features geometry ontology, which the census references without enumerating.
/// </para>
/// </remarks>
public static class GeoVocabulary
{
    /// <summary>
    /// The GeoSPARQL ontology namespace: the classes, properties, and literal datatypes of the Core,
    /// Topology Vocabulary, Geometry, and DGGS conformance classes.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "GeoVocabulary.Geo.WktLiteral is the intended usage pattern.")]
    public static class Geo
    {
        /// <summary>The GeoSPARQL ontology namespace IRI.</summary>
        public const string Namespace = "http://www.opengis.net/ont/geosparql#";

        //Classes.
        private static byte[] SpatialObjectBytes { get; } = "http://www.opengis.net/ont/geosparql#SpatialObject"u8.ToArray();
        private static byte[] FeatureBytes { get; } = "http://www.opengis.net/ont/geosparql#Feature"u8.ToArray();
        private static byte[] SpatialObjectCollectionBytes { get; } = "http://www.opengis.net/ont/geosparql#SpatialObjectCollection"u8.ToArray();
        private static byte[] FeatureCollectionBytes { get; } = "http://www.opengis.net/ont/geosparql#FeatureCollection"u8.ToArray();
        private static byte[] GeometryBytes { get; } = "http://www.opengis.net/ont/geosparql#Geometry"u8.ToArray();
        private static byte[] GeometryCollectionBytes { get; } = "http://www.opengis.net/ont/geosparql#GeometryCollection"u8.ToArray();

        //Spatial-object size properties.
        private static byte[] HasSizeBytes { get; } = "http://www.opengis.net/ont/geosparql#hasSize"u8.ToArray();
        private static byte[] HasMetricSizeBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricSize"u8.ToArray();
        private static byte[] HasLengthBytes { get; } = "http://www.opengis.net/ont/geosparql#hasLength"u8.ToArray();
        private static byte[] HasMetricLengthBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricLength"u8.ToArray();
        private static byte[] HasPerimeterLengthBytes { get; } = "http://www.opengis.net/ont/geosparql#hasPerimeterLength"u8.ToArray();
        private static byte[] HasMetricPerimeterLengthBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricPerimeterLength"u8.ToArray();
        private static byte[] HasAreaBytes { get; } = "http://www.opengis.net/ont/geosparql#hasArea"u8.ToArray();
        private static byte[] HasMetricAreaBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricArea"u8.ToArray();
        private static byte[] HasVolumeBytes { get; } = "http://www.opengis.net/ont/geosparql#hasVolume"u8.ToArray();
        private static byte[] HasMetricVolumeBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricVolume"u8.ToArray();

        //Feature properties.
        private static byte[] HasGeometryBytes { get; } = "http://www.opengis.net/ont/geosparql#hasGeometry"u8.ToArray();
        private static byte[] HasDefaultGeometryBytes { get; } = "http://www.opengis.net/ont/geosparql#hasDefaultGeometry"u8.ToArray();
        private static byte[] HasCentroidBytes { get; } = "http://www.opengis.net/ont/geosparql#hasCentroid"u8.ToArray();
        private static byte[] HasBoundingBoxBytes { get; } = "http://www.opengis.net/ont/geosparql#hasBoundingBox"u8.ToArray();

        //Simple Features topological relations.
        private static byte[] SfEqualsBytes { get; } = "http://www.opengis.net/ont/geosparql#sfEquals"u8.ToArray();
        private static byte[] SfDisjointBytes { get; } = "http://www.opengis.net/ont/geosparql#sfDisjoint"u8.ToArray();
        private static byte[] SfIntersectsBytes { get; } = "http://www.opengis.net/ont/geosparql#sfIntersects"u8.ToArray();
        private static byte[] SfTouchesBytes { get; } = "http://www.opengis.net/ont/geosparql#sfTouches"u8.ToArray();
        private static byte[] SfCrossesBytes { get; } = "http://www.opengis.net/ont/geosparql#sfCrosses"u8.ToArray();
        private static byte[] SfWithinBytes { get; } = "http://www.opengis.net/ont/geosparql#sfWithin"u8.ToArray();
        private static byte[] SfContainsBytes { get; } = "http://www.opengis.net/ont/geosparql#sfContains"u8.ToArray();
        private static byte[] SfOverlapsBytes { get; } = "http://www.opengis.net/ont/geosparql#sfOverlaps"u8.ToArray();

        //Egenhofer topological relations.
        private static byte[] EhEqualsBytes { get; } = "http://www.opengis.net/ont/geosparql#ehEquals"u8.ToArray();
        private static byte[] EhDisjointBytes { get; } = "http://www.opengis.net/ont/geosparql#ehDisjoint"u8.ToArray();
        private static byte[] EhMeetBytes { get; } = "http://www.opengis.net/ont/geosparql#ehMeet"u8.ToArray();
        private static byte[] EhOverlapBytes { get; } = "http://www.opengis.net/ont/geosparql#ehOverlap"u8.ToArray();
        private static byte[] EhCoversBytes { get; } = "http://www.opengis.net/ont/geosparql#ehCovers"u8.ToArray();
        private static byte[] EhCoveredByBytes { get; } = "http://www.opengis.net/ont/geosparql#ehCoveredBy"u8.ToArray();
        private static byte[] EhInsideBytes { get; } = "http://www.opengis.net/ont/geosparql#ehInside"u8.ToArray();
        private static byte[] EhContainsBytes { get; } = "http://www.opengis.net/ont/geosparql#ehContains"u8.ToArray();

        //RCC8 topological relations.
        private static byte[] Rcc8EqBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8eq"u8.ToArray();
        private static byte[] Rcc8DcBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8dc"u8.ToArray();
        private static byte[] Rcc8EcBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8ec"u8.ToArray();
        private static byte[] Rcc8PoBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8po"u8.ToArray();
        private static byte[] Rcc8TppiBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8tppi"u8.ToArray();
        private static byte[] Rcc8TppBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8tpp"u8.ToArray();
        private static byte[] Rcc8NtppBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8ntpp"u8.ToArray();
        private static byte[] Rcc8NtppiBytes { get; } = "http://www.opengis.net/ont/geosparql#rcc8ntppi"u8.ToArray();

        //Geometry properties.
        private static byte[] DimensionBytes { get; } = "http://www.opengis.net/ont/geosparql#dimension"u8.ToArray();
        private static byte[] CoordinateDimensionBytes { get; } = "http://www.opengis.net/ont/geosparql#coordinateDimension"u8.ToArray();
        private static byte[] SpatialDimensionBytes { get; } = "http://www.opengis.net/ont/geosparql#spatialDimension"u8.ToArray();
        private static byte[] HasSpatialResolutionBytes { get; } = "http://www.opengis.net/ont/geosparql#hasSpatialResolution"u8.ToArray();
        private static byte[] HasMetricSpatialResolutionBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricSpatialResolution"u8.ToArray();
        private static byte[] HasSpatialAccuracyBytes { get; } = "http://www.opengis.net/ont/geosparql#hasSpatialAccuracy"u8.ToArray();
        private static byte[] HasMetricSpatialAccuracyBytes { get; } = "http://www.opengis.net/ont/geosparql#hasMetricSpatialAccuracy"u8.ToArray();
        private static byte[] IsEmptyBytes { get; } = "http://www.opengis.net/ont/geosparql#isEmpty"u8.ToArray();
        private static byte[] IsSimpleBytes { get; } = "http://www.opengis.net/ont/geosparql#isSimple"u8.ToArray();
        private static byte[] HasSerializationBytes { get; } = "http://www.opengis.net/ont/geosparql#hasSerialization"u8.ToArray();

        //Serialization properties.
        private static byte[] AsWktBytes { get; } = "http://www.opengis.net/ont/geosparql#asWKT"u8.ToArray();
        private static byte[] AsGmlBytes { get; } = "http://www.opengis.net/ont/geosparql#asGML"u8.ToArray();
        private static byte[] AsGeoJsonBytes { get; } = "http://www.opengis.net/ont/geosparql#asGeoJSON"u8.ToArray();
        private static byte[] AsKmlBytes { get; } = "http://www.opengis.net/ont/geosparql#asKML"u8.ToArray();
        private static byte[] AsDggsBytes { get; } = "http://www.opengis.net/ont/geosparql#asDGGS"u8.ToArray();

        //Literal datatypes.
        private static byte[] WktLiteralBytes { get; } = "http://www.opengis.net/ont/geosparql#wktLiteral"u8.ToArray();
        private static byte[] GmlLiteralBytes { get; } = "http://www.opengis.net/ont/geosparql#gmlLiteral"u8.ToArray();
        private static byte[] GeoJsonLiteralBytes { get; } = "http://www.opengis.net/ont/geosparql#geoJSONLiteral"u8.ToArray();
        private static byte[] KmlLiteralBytes { get; } = "http://www.opengis.net/ont/geosparql#kmlLiteral"u8.ToArray();
        private static byte[] DggsLiteralBytes { get; } = "http://www.opengis.net/ont/geosparql#dggsLiteral"u8.ToArray();

        /// <summary>The <c>geo:SpatialObject</c> class — anything with a spatial presence.</summary>
        public static Utf8String SpatialObject { get; } = new(SpatialObjectBytes);

        /// <summary>The <c>geo:Feature</c> class — a discrete spatial phenomenon.</summary>
        public static Utf8String Feature { get; } = new(FeatureBytes);

        /// <summary>The <c>geo:SpatialObjectCollection</c> class.</summary>
        public static Utf8String SpatialObjectCollection { get; } = new(SpatialObjectCollectionBytes);

        /// <summary>The <c>geo:FeatureCollection</c> class.</summary>
        public static Utf8String FeatureCollection { get; } = new(FeatureCollectionBytes);

        /// <summary>The <c>geo:Geometry</c> class.</summary>
        public static Utf8String Geometry { get; } = new(GeometryBytes);

        /// <summary>The <c>geo:GeometryCollection</c> class.</summary>
        public static Utf8String GeometryCollection { get; } = new(GeometryCollectionBytes);

        /// <summary>The <c>geo:hasSize</c> property — a spatial object's size as a quantity value.</summary>
        public static Utf8String HasSize { get; } = new(HasSizeBytes);

        /// <summary>The <c>geo:hasMetricSize</c> property — a spatial object's size in square meters.</summary>
        public static Utf8String HasMetricSize { get; } = new(HasMetricSizeBytes);

        /// <summary>The <c>geo:hasLength</c> property — a spatial object's length as a quantity value.</summary>
        public static Utf8String HasLength { get; } = new(HasLengthBytes);

        /// <summary>The <c>geo:hasMetricLength</c> property — a spatial object's length in meters.</summary>
        public static Utf8String HasMetricLength { get; } = new(HasMetricLengthBytes);

        /// <summary>The <c>geo:hasPerimeterLength</c> property — a spatial object's perimeter as a quantity value.</summary>
        public static Utf8String HasPerimeterLength { get; } = new(HasPerimeterLengthBytes);

        /// <summary>The <c>geo:hasMetricPerimeterLength</c> property — a spatial object's perimeter in meters.</summary>
        public static Utf8String HasMetricPerimeterLength { get; } = new(HasMetricPerimeterLengthBytes);

        /// <summary>The <c>geo:hasArea</c> property — a spatial object's area as a quantity value.</summary>
        public static Utf8String HasArea { get; } = new(HasAreaBytes);

        /// <summary>The <c>geo:hasMetricArea</c> property — a spatial object's area in square meters.</summary>
        public static Utf8String HasMetricArea { get; } = new(HasMetricAreaBytes);

        /// <summary>The <c>geo:hasVolume</c> property — a spatial object's volume as a quantity value.</summary>
        public static Utf8String HasVolume { get; } = new(HasVolumeBytes);

        /// <summary>The <c>geo:hasMetricVolume</c> property — a spatial object's volume in cubic meters.</summary>
        public static Utf8String HasMetricVolume { get; } = new(HasMetricVolumeBytes);

        /// <summary>The <c>geo:hasGeometry</c> property — links a feature to a geometry representing it.</summary>
        public static Utf8String HasGeometry { get; } = new(HasGeometryBytes);

        /// <summary>The <c>geo:hasDefaultGeometry</c> property — the feature's default geometry.</summary>
        public static Utf8String HasDefaultGeometry { get; } = new(HasDefaultGeometryBytes);

        /// <summary>The <c>geo:hasCentroid</c> property — the geometric center of a spatial object.</summary>
        public static Utf8String HasCentroid { get; } = new(HasCentroidBytes);

        /// <summary>The <c>geo:hasBoundingBox</c> property — the minimum bounding box of a spatial object.</summary>
        public static Utf8String HasBoundingBox { get; } = new(HasBoundingBoxBytes);

        /// <summary>The <c>geo:sfEquals</c> Simple Features relation.</summary>
        public static Utf8String SfEquals { get; } = new(SfEqualsBytes);

        /// <summary>The <c>geo:sfDisjoint</c> Simple Features relation.</summary>
        public static Utf8String SfDisjoint { get; } = new(SfDisjointBytes);

        /// <summary>The <c>geo:sfIntersects</c> Simple Features relation.</summary>
        public static Utf8String SfIntersects { get; } = new(SfIntersectsBytes);

        /// <summary>The <c>geo:sfTouches</c> Simple Features relation.</summary>
        public static Utf8String SfTouches { get; } = new(SfTouchesBytes);

        /// <summary>The <c>geo:sfCrosses</c> Simple Features relation.</summary>
        public static Utf8String SfCrosses { get; } = new(SfCrossesBytes);

        /// <summary>The <c>geo:sfWithin</c> Simple Features relation.</summary>
        public static Utf8String SfWithin { get; } = new(SfWithinBytes);

        /// <summary>The <c>geo:sfContains</c> Simple Features relation.</summary>
        public static Utf8String SfContains { get; } = new(SfContainsBytes);

        /// <summary>The <c>geo:sfOverlaps</c> Simple Features relation.</summary>
        public static Utf8String SfOverlaps { get; } = new(SfOverlapsBytes);

        /// <summary>The <c>geo:ehEquals</c> Egenhofer relation.</summary>
        public static Utf8String EhEquals { get; } = new(EhEqualsBytes);

        /// <summary>The <c>geo:ehDisjoint</c> Egenhofer relation.</summary>
        public static Utf8String EhDisjoint { get; } = new(EhDisjointBytes);

        /// <summary>The <c>geo:ehMeet</c> Egenhofer relation.</summary>
        public static Utf8String EhMeet { get; } = new(EhMeetBytes);

        /// <summary>The <c>geo:ehOverlap</c> Egenhofer relation.</summary>
        public static Utf8String EhOverlap { get; } = new(EhOverlapBytes);

        /// <summary>The <c>geo:ehCovers</c> Egenhofer relation.</summary>
        public static Utf8String EhCovers { get; } = new(EhCoversBytes);

        /// <summary>The <c>geo:ehCoveredBy</c> Egenhofer relation.</summary>
        public static Utf8String EhCoveredBy { get; } = new(EhCoveredByBytes);

        /// <summary>The <c>geo:ehInside</c> Egenhofer relation.</summary>
        public static Utf8String EhInside { get; } = new(EhInsideBytes);

        /// <summary>The <c>geo:ehContains</c> Egenhofer relation.</summary>
        public static Utf8String EhContains { get; } = new(EhContainsBytes);

        /// <summary>The <c>geo:rcc8eq</c> RCC8 relation (equals).</summary>
        public static Utf8String Rcc8Eq { get; } = new(Rcc8EqBytes);

        /// <summary>The <c>geo:rcc8dc</c> RCC8 relation (disconnected).</summary>
        public static Utf8String Rcc8Dc { get; } = new(Rcc8DcBytes);

        /// <summary>The <c>geo:rcc8ec</c> RCC8 relation (externally connected).</summary>
        public static Utf8String Rcc8Ec { get; } = new(Rcc8EcBytes);

        /// <summary>The <c>geo:rcc8po</c> RCC8 relation (partially overlapping).</summary>
        public static Utf8String Rcc8Po { get; } = new(Rcc8PoBytes);

        /// <summary>The <c>geo:rcc8tppi</c> RCC8 relation (tangential proper part inverse).</summary>
        public static Utf8String Rcc8Tppi { get; } = new(Rcc8TppiBytes);

        /// <summary>The <c>geo:rcc8tpp</c> RCC8 relation (tangential proper part).</summary>
        public static Utf8String Rcc8Tpp { get; } = new(Rcc8TppBytes);

        /// <summary>The <c>geo:rcc8ntpp</c> RCC8 relation (non-tangential proper part).</summary>
        public static Utf8String Rcc8Ntpp { get; } = new(Rcc8NtppBytes);

        /// <summary>The <c>geo:rcc8ntppi</c> RCC8 relation (non-tangential proper part inverse).</summary>
        public static Utf8String Rcc8Ntppi { get; } = new(Rcc8NtppiBytes);

        /// <summary>The <c>geo:dimension</c> property — the topological dimension of a geometry.</summary>
        public static Utf8String Dimension { get; } = new(DimensionBytes);

        /// <summary>The <c>geo:coordinateDimension</c> property — the coordinate tuple width of a geometry.</summary>
        public static Utf8String CoordinateDimension { get; } = new(CoordinateDimensionBytes);

        /// <summary>The <c>geo:spatialDimension</c> property — the spatial dimension of a geometry.</summary>
        public static Utf8String SpatialDimension { get; } = new(SpatialDimensionBytes);

        /// <summary>The <c>geo:hasSpatialResolution</c> property — a geometry's spatial resolution as a quantity value.</summary>
        public static Utf8String HasSpatialResolution { get; } = new(HasSpatialResolutionBytes);

        /// <summary>The <c>geo:hasMetricSpatialResolution</c> property — a geometry's spatial resolution in meters.</summary>
        public static Utf8String HasMetricSpatialResolution { get; } = new(HasMetricSpatialResolutionBytes);

        /// <summary>The <c>geo:hasSpatialAccuracy</c> property — a geometry's positional accuracy as a quantity value.</summary>
        public static Utf8String HasSpatialAccuracy { get; } = new(HasSpatialAccuracyBytes);

        /// <summary>The <c>geo:hasMetricSpatialAccuracy</c> property — a geometry's positional accuracy in meters.</summary>
        public static Utf8String HasMetricSpatialAccuracy { get; } = new(HasMetricSpatialAccuracyBytes);

        /// <summary>The <c>geo:isEmpty</c> property — whether a geometry has no points.</summary>
        public static Utf8String IsEmpty { get; } = new(IsEmptyBytes);

        /// <summary>The <c>geo:isSimple</c> property — whether a geometry has no self-intersections or self-tangencies.</summary>
        public static Utf8String IsSimple { get; } = new(IsSimpleBytes);

        /// <summary>The <c>geo:hasSerialization</c> property — links a geometry to its serialized representation.</summary>
        public static Utf8String HasSerialization { get; } = new(HasSerializationBytes);

        /// <summary>The <c>geo:asWKT</c> property — the well-known-text serialization of a geometry.</summary>
        public static Utf8String AsWkt { get; } = new(AsWktBytes);

        /// <summary>The <c>geo:asGML</c> property — the GML serialization of a geometry.</summary>
        public static Utf8String AsGml { get; } = new(AsGmlBytes);

        /// <summary>The <c>geo:asGeoJSON</c> property — the GeoJSON serialization of a geometry.</summary>
        public static Utf8String AsGeoJson { get; } = new(AsGeoJsonBytes);

        /// <summary>The <c>geo:asKML</c> property — the KML serialization of a geometry.</summary>
        public static Utf8String AsKml { get; } = new(AsKmlBytes);

        /// <summary>The <c>geo:asDGGS</c> property — the discrete-global-grid-system serialization of a geometry.</summary>
        public static Utf8String AsDggs { get; } = new(AsDggsBytes);

        /// <summary>The <c>geo:wktLiteral</c> datatype — a well-known-text geometry literal with an optional CRS IRI prefix.</summary>
        public static Utf8String WktLiteral { get; } = new(WktLiteralBytes);

        /// <summary>The <c>geo:gmlLiteral</c> datatype — a GML geometry literal.</summary>
        public static Utf8String GmlLiteral { get; } = new(GmlLiteralBytes);

        /// <summary>The <c>geo:geoJSONLiteral</c> datatype — a GeoJSON geometry literal.</summary>
        public static Utf8String GeoJsonLiteral { get; } = new(GeoJsonLiteralBytes);

        /// <summary>The <c>geo:kmlLiteral</c> datatype — a KML geometry literal.</summary>
        public static Utf8String KmlLiteral { get; } = new(KmlLiteralBytes);

        /// <summary>The <c>geo:dggsLiteral</c> datatype — a discrete-global-grid-system geometry literal with a required DGGS IRI prefix.</summary>
        public static Utf8String DggsLiteral { get; } = new(DggsLiteralBytes);

        /// <summary>Every IRI constant in this vocabulary, in declaration order — the GeoSPARQL ontology term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
        public static IReadOnlyList<Utf8String> All { get; } =
        [
            SpatialObject, Feature, SpatialObjectCollection, FeatureCollection, Geometry, GeometryCollection,
            HasSize, HasMetricSize, HasLength, HasMetricLength, HasPerimeterLength, HasMetricPerimeterLength,
            HasArea, HasMetricArea, HasVolume, HasMetricVolume, HasGeometry, HasDefaultGeometry, HasCentroid,
            HasBoundingBox, SfEquals, SfDisjoint, SfIntersects, SfTouches, SfCrosses, SfWithin, SfContains,
            SfOverlaps, EhEquals, EhDisjoint, EhMeet, EhOverlap, EhCovers, EhCoveredBy, EhInside, EhContains,
            Rcc8Eq, Rcc8Dc, Rcc8Ec, Rcc8Po, Rcc8Tppi, Rcc8Tpp, Rcc8Ntpp, Rcc8Ntppi, Dimension,
            CoordinateDimension, SpatialDimension, HasSpatialResolution, HasMetricSpatialResolution,
            HasSpatialAccuracy, HasMetricSpatialAccuracy, IsEmpty, IsSimple, HasSerialization, AsWkt, AsGml,
            AsGeoJson, AsKml, AsDggs, WktLiteral, GmlLiteral, GeoJsonLiteral, KmlLiteral, DggsLiteral
        ];
    }

    /// <summary>
    /// The GeoSPARQL extension-function namespace: all 74 functions the census derives from the
    /// conformance classes — the Simple Features non-topological set, the GeoSPARQL-native
    /// non-topological set, the SRID accessor, the spatial aggregates, the serializer functions,
    /// <c>geof:relate</c>, and the three topological predicate families.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "GeoVocabulary.Geof.Distance is the intended usage pattern.")]
    public static class Geof
    {
        /// <summary>The GeoSPARQL function namespace IRI.</summary>
        public const string Namespace = "http://www.opengis.net/def/function/geosparql/";

        //Simple Features non-topological query functions.
        private static byte[] BoundaryBytes { get; } = "http://www.opengis.net/def/function/geosparql/boundary"u8.ToArray();
        private static byte[] BoundingCircleBytes { get; } = "http://www.opengis.net/def/function/geosparql/boundingCircle"u8.ToArray();
        private static byte[] MetricBufferBytes { get; } = "http://www.opengis.net/def/function/geosparql/metricBuffer"u8.ToArray();
        private static byte[] BufferBytes { get; } = "http://www.opengis.net/def/function/geosparql/buffer"u8.ToArray();
        private static byte[] CentroidBytes { get; } = "http://www.opengis.net/def/function/geosparql/centroid"u8.ToArray();
        private static byte[] ConvexHullBytes { get; } = "http://www.opengis.net/def/function/geosparql/convexHull"u8.ToArray();
        private static byte[] ConcaveHullBytes { get; } = "http://www.opengis.net/def/function/geosparql/concaveHull"u8.ToArray();
        private static byte[] CoordinateDimensionBytes { get; } = "http://www.opengis.net/def/function/geosparql/coordinateDimension"u8.ToArray();
        private static byte[] DifferenceBytes { get; } = "http://www.opengis.net/def/function/geosparql/difference"u8.ToArray();
        private static byte[] DimensionBytes { get; } = "http://www.opengis.net/def/function/geosparql/dimension"u8.ToArray();
        private static byte[] MetricDistanceBytes { get; } = "http://www.opengis.net/def/function/geosparql/metricDistance"u8.ToArray();
        private static byte[] DistanceBytes { get; } = "http://www.opengis.net/def/function/geosparql/distance"u8.ToArray();
        private static byte[] EnvelopeBytes { get; } = "http://www.opengis.net/def/function/geosparql/envelope"u8.ToArray();
        private static byte[] GeometryTypeBytes { get; } = "http://www.opengis.net/def/function/geosparql/geometryType"u8.ToArray();
        private static byte[] IntersectionBytes { get; } = "http://www.opengis.net/def/function/geosparql/intersection"u8.ToArray();
        private static byte[] Is3DBytes { get; } = "http://www.opengis.net/def/function/geosparql/is3D"u8.ToArray();
        private static byte[] IsEmptyBytes { get; } = "http://www.opengis.net/def/function/geosparql/isEmpty"u8.ToArray();
        private static byte[] IsMeasuredBytes { get; } = "http://www.opengis.net/def/function/geosparql/isMeasured"u8.ToArray();
        private static byte[] IsSimpleBytes { get; } = "http://www.opengis.net/def/function/geosparql/isSimple"u8.ToArray();
        private static byte[] SpatialDimensionBytes { get; } = "http://www.opengis.net/def/function/geosparql/spatialDimension"u8.ToArray();
        private static byte[] SymDifferenceBytes { get; } = "http://www.opengis.net/def/function/geosparql/symDifference"u8.ToArray();
        private static byte[] TransformBytes { get; } = "http://www.opengis.net/def/function/geosparql/transform"u8.ToArray();
        private static byte[] UnionBytes { get; } = "http://www.opengis.net/def/function/geosparql/union"u8.ToArray();

        //GeoSPARQL-native non-topological query functions.
        private static byte[] MetricLengthBytes { get; } = "http://www.opengis.net/def/function/geosparql/metricLength"u8.ToArray();
        private static byte[] LengthBytes { get; } = "http://www.opengis.net/def/function/geosparql/length"u8.ToArray();
        private static byte[] MetricPerimeterBytes { get; } = "http://www.opengis.net/def/function/geosparql/metricPerimeter"u8.ToArray();
        private static byte[] PerimeterBytes { get; } = "http://www.opengis.net/def/function/geosparql/perimeter"u8.ToArray();
        private static byte[] MetricAreaBytes { get; } = "http://www.opengis.net/def/function/geosparql/metricArea"u8.ToArray();
        private static byte[] AreaBytes { get; } = "http://www.opengis.net/def/function/geosparql/area"u8.ToArray();
        private static byte[] GeometryNBytes { get; } = "http://www.opengis.net/def/function/geosparql/geometryN"u8.ToArray();
        private static byte[] MaxXBytes { get; } = "http://www.opengis.net/def/function/geosparql/maxX"u8.ToArray();
        private static byte[] MaxYBytes { get; } = "http://www.opengis.net/def/function/geosparql/maxY"u8.ToArray();
        private static byte[] MaxZBytes { get; } = "http://www.opengis.net/def/function/geosparql/maxZ"u8.ToArray();
        private static byte[] MinXBytes { get; } = "http://www.opengis.net/def/function/geosparql/minX"u8.ToArray();
        private static byte[] MinYBytes { get; } = "http://www.opengis.net/def/function/geosparql/minY"u8.ToArray();
        private static byte[] MinZBytes { get; } = "http://www.opengis.net/def/function/geosparql/minZ"u8.ToArray();
        private static byte[] NumGeometriesBytes { get; } = "http://www.opengis.net/def/function/geosparql/numGeometries"u8.ToArray();

        //The SRID accessor.
        private static byte[] GetSridBytes { get; } = "http://www.opengis.net/def/function/geosparql/getSRID"u8.ToArray();

        //Spatial aggregate functions.
        private static byte[] AggBoundingBoxBytes { get; } = "http://www.opengis.net/def/function/geosparql/aggBoundingBox"u8.ToArray();
        private static byte[] AggBoundingCircleBytes { get; } = "http://www.opengis.net/def/function/geosparql/aggBoundingCircle"u8.ToArray();
        private static byte[] AggCentroidBytes { get; } = "http://www.opengis.net/def/function/geosparql/aggCentroid"u8.ToArray();
        private static byte[] AggConcaveHullBytes { get; } = "http://www.opengis.net/def/function/geosparql/aggConcaveHull"u8.ToArray();
        private static byte[] AggConvexHullBytes { get; } = "http://www.opengis.net/def/function/geosparql/aggConvexHull"u8.ToArray();
        private static byte[] AggUnionBytes { get; } = "http://www.opengis.net/def/function/geosparql/aggUnion"u8.ToArray();

        //Serializer functions.
        private static byte[] AsWktBytes { get; } = "http://www.opengis.net/def/function/geosparql/asWKT"u8.ToArray();
        private static byte[] AsGmlBytes { get; } = "http://www.opengis.net/def/function/geosparql/asGML"u8.ToArray();
        private static byte[] AsGeoJsonBytes { get; } = "http://www.opengis.net/def/function/geosparql/asGeoJSON"u8.ToArray();
        private static byte[] AsKmlBytes { get; } = "http://www.opengis.net/def/function/geosparql/asKML"u8.ToArray();
        private static byte[] AsDggsBytes { get; } = "http://www.opengis.net/def/function/geosparql/asDGGS"u8.ToArray();

        //The DE-9IM pattern test.
        private static byte[] RelateBytes { get; } = "http://www.opengis.net/def/function/geosparql/relate"u8.ToArray();

        //Simple Features topological predicates.
        private static byte[] SfEqualsBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfEquals"u8.ToArray();
        private static byte[] SfDisjointBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfDisjoint"u8.ToArray();
        private static byte[] SfIntersectsBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfIntersects"u8.ToArray();
        private static byte[] SfTouchesBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfTouches"u8.ToArray();
        private static byte[] SfCrossesBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfCrosses"u8.ToArray();
        private static byte[] SfWithinBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfWithin"u8.ToArray();
        private static byte[] SfContainsBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfContains"u8.ToArray();
        private static byte[] SfOverlapsBytes { get; } = "http://www.opengis.net/def/function/geosparql/sfOverlaps"u8.ToArray();

        //Egenhofer topological predicates.
        private static byte[] EhEqualsBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehEquals"u8.ToArray();
        private static byte[] EhDisjointBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehDisjoint"u8.ToArray();
        private static byte[] EhMeetBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehMeet"u8.ToArray();
        private static byte[] EhOverlapBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehOverlap"u8.ToArray();
        private static byte[] EhCoversBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehCovers"u8.ToArray();
        private static byte[] EhCoveredByBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehCoveredBy"u8.ToArray();
        private static byte[] EhInsideBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehInside"u8.ToArray();
        private static byte[] EhContainsBytes { get; } = "http://www.opengis.net/def/function/geosparql/ehContains"u8.ToArray();

        //RCC8 topological predicates.
        private static byte[] Rcc8EqBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8eq"u8.ToArray();
        private static byte[] Rcc8DcBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8dc"u8.ToArray();
        private static byte[] Rcc8EcBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8ec"u8.ToArray();
        private static byte[] Rcc8PoBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8po"u8.ToArray();
        private static byte[] Rcc8TppiBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8tppi"u8.ToArray();
        private static byte[] Rcc8TppBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8tpp"u8.ToArray();
        private static byte[] Rcc8NtppBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8ntpp"u8.ToArray();
        private static byte[] Rcc8NtppiBytes { get; } = "http://www.opengis.net/def/function/geosparql/rcc8ntppi"u8.ToArray();

        /// <summary>The <c>geof:boundary</c> function.</summary>
        public static Utf8String Boundary { get; } = new(BoundaryBytes);

        /// <summary>The <c>geof:boundingCircle</c> function.</summary>
        public static Utf8String BoundingCircle { get; } = new(BoundingCircleBytes);

        /// <summary>The <c>geof:metricBuffer</c> function.</summary>
        public static Utf8String MetricBuffer { get; } = new(MetricBufferBytes);

        /// <summary>The <c>geof:buffer</c> function.</summary>
        public static Utf8String Buffer { get; } = new(BufferBytes);

        /// <summary>The <c>geof:centroid</c> function.</summary>
        public static Utf8String Centroid { get; } = new(CentroidBytes);

        /// <summary>The <c>geof:convexHull</c> function.</summary>
        public static Utf8String ConvexHull { get; } = new(ConvexHullBytes);

        /// <summary>The <c>geof:concaveHull</c> function.</summary>
        public static Utf8String ConcaveHull { get; } = new(ConcaveHullBytes);

        /// <summary>The <c>geof:coordinateDimension</c> function.</summary>
        public static Utf8String CoordinateDimension { get; } = new(CoordinateDimensionBytes);

        /// <summary>The <c>geof:difference</c> function.</summary>
        public static Utf8String Difference { get; } = new(DifferenceBytes);

        /// <summary>The <c>geof:dimension</c> function.</summary>
        public static Utf8String Dimension { get; } = new(DimensionBytes);

        /// <summary>The <c>geof:metricDistance</c> function.</summary>
        public static Utf8String MetricDistance { get; } = new(MetricDistanceBytes);

        /// <summary>The <c>geof:distance</c> function.</summary>
        public static Utf8String Distance { get; } = new(DistanceBytes);

        /// <summary>The <c>geof:envelope</c> function.</summary>
        public static Utf8String Envelope { get; } = new(EnvelopeBytes);

        /// <summary>The <c>geof:geometryType</c> function.</summary>
        public static Utf8String GeometryType { get; } = new(GeometryTypeBytes);

        /// <summary>The <c>geof:intersection</c> function.</summary>
        public static Utf8String Intersection { get; } = new(IntersectionBytes);

        /// <summary>The <c>geof:is3D</c> function.</summary>
        public static Utf8String Is3D { get; } = new(Is3DBytes);

        /// <summary>The <c>geof:isEmpty</c> function.</summary>
        public static Utf8String IsEmpty { get; } = new(IsEmptyBytes);

        /// <summary>The <c>geof:isMeasured</c> function.</summary>
        public static Utf8String IsMeasured { get; } = new(IsMeasuredBytes);

        /// <summary>The <c>geof:isSimple</c> function.</summary>
        public static Utf8String IsSimple { get; } = new(IsSimpleBytes);

        /// <summary>The <c>geof:spatialDimension</c> function.</summary>
        public static Utf8String SpatialDimension { get; } = new(SpatialDimensionBytes);

        /// <summary>The <c>geof:symDifference</c> function.</summary>
        public static Utf8String SymDifference { get; } = new(SymDifferenceBytes);

        /// <summary>The <c>geof:transform</c> function.</summary>
        public static Utf8String Transform { get; } = new(TransformBytes);

        /// <summary>The <c>geof:union</c> function.</summary>
        public static Utf8String Union { get; } = new(UnionBytes);

        /// <summary>The <c>geof:metricLength</c> function.</summary>
        public static Utf8String MetricLength { get; } = new(MetricLengthBytes);

        /// <summary>The <c>geof:length</c> function.</summary>
        public static Utf8String Length { get; } = new(LengthBytes);

        /// <summary>The <c>geof:metricPerimeter</c> function.</summary>
        public static Utf8String MetricPerimeter { get; } = new(MetricPerimeterBytes);

        /// <summary>The <c>geof:perimeter</c> function.</summary>
        public static Utf8String Perimeter { get; } = new(PerimeterBytes);

        /// <summary>The <c>geof:metricArea</c> function.</summary>
        public static Utf8String MetricArea { get; } = new(MetricAreaBytes);

        /// <summary>The <c>geof:area</c> function.</summary>
        public static Utf8String Area { get; } = new(AreaBytes);

        /// <summary>The <c>geof:geometryN</c> function.</summary>
        public static Utf8String GeometryN { get; } = new(GeometryNBytes);

        /// <summary>The <c>geof:maxX</c> function.</summary>
        public static Utf8String MaxX { get; } = new(MaxXBytes);

        /// <summary>The <c>geof:maxY</c> function.</summary>
        public static Utf8String MaxY { get; } = new(MaxYBytes);

        /// <summary>The <c>geof:maxZ</c> function.</summary>
        public static Utf8String MaxZ { get; } = new(MaxZBytes);

        /// <summary>The <c>geof:minX</c> function.</summary>
        public static Utf8String MinX { get; } = new(MinXBytes);

        /// <summary>The <c>geof:minY</c> function.</summary>
        public static Utf8String MinY { get; } = new(MinYBytes);

        /// <summary>The <c>geof:minZ</c> function.</summary>
        public static Utf8String MinZ { get; } = new(MinZBytes);

        /// <summary>The <c>geof:numGeometries</c> function.</summary>
        public static Utf8String NumGeometries { get; } = new(NumGeometriesBytes);

        /// <summary>The <c>geof:getSRID</c> function.</summary>
        public static Utf8String GetSrid { get; } = new(GetSridBytes);

        /// <summary>The <c>geof:aggBoundingBox</c> aggregate function.</summary>
        public static Utf8String AggBoundingBox { get; } = new(AggBoundingBoxBytes);

        /// <summary>The <c>geof:aggBoundingCircle</c> aggregate function.</summary>
        public static Utf8String AggBoundingCircle { get; } = new(AggBoundingCircleBytes);

        /// <summary>The <c>geof:aggCentroid</c> aggregate function.</summary>
        public static Utf8String AggCentroid { get; } = new(AggCentroidBytes);

        /// <summary>The <c>geof:aggConcaveHull</c> aggregate function.</summary>
        public static Utf8String AggConcaveHull { get; } = new(AggConcaveHullBytes);

        /// <summary>The <c>geof:aggConvexHull</c> aggregate function.</summary>
        public static Utf8String AggConvexHull { get; } = new(AggConvexHullBytes);

        /// <summary>The <c>geof:aggUnion</c> aggregate function.</summary>
        public static Utf8String AggUnion { get; } = new(AggUnionBytes);

        /// <summary>The <c>geof:asWKT</c> serializer function.</summary>
        public static Utf8String AsWkt { get; } = new(AsWktBytes);

        /// <summary>The <c>geof:asGML</c> serializer function.</summary>
        public static Utf8String AsGml { get; } = new(AsGmlBytes);

        /// <summary>The <c>geof:asGeoJSON</c> serializer function.</summary>
        public static Utf8String AsGeoJson { get; } = new(AsGeoJsonBytes);

        /// <summary>The <c>geof:asKML</c> serializer function.</summary>
        public static Utf8String AsKml { get; } = new(AsKmlBytes);

        /// <summary>The <c>geof:asDGGS</c> serializer function.</summary>
        public static Utf8String AsDggs { get; } = new(AsDggsBytes);

        /// <summary>The <c>geof:relate</c> DE-9IM pattern-test function.</summary>
        public static Utf8String Relate { get; } = new(RelateBytes);

        /// <summary>The <c>geof:sfEquals</c> predicate function.</summary>
        public static Utf8String SfEquals { get; } = new(SfEqualsBytes);

        /// <summary>The <c>geof:sfDisjoint</c> predicate function.</summary>
        public static Utf8String SfDisjoint { get; } = new(SfDisjointBytes);

        /// <summary>The <c>geof:sfIntersects</c> predicate function.</summary>
        public static Utf8String SfIntersects { get; } = new(SfIntersectsBytes);

        /// <summary>The <c>geof:sfTouches</c> predicate function.</summary>
        public static Utf8String SfTouches { get; } = new(SfTouchesBytes);

        /// <summary>The <c>geof:sfCrosses</c> predicate function.</summary>
        public static Utf8String SfCrosses { get; } = new(SfCrossesBytes);

        /// <summary>The <c>geof:sfWithin</c> predicate function.</summary>
        public static Utf8String SfWithin { get; } = new(SfWithinBytes);

        /// <summary>The <c>geof:sfContains</c> predicate function.</summary>
        public static Utf8String SfContains { get; } = new(SfContainsBytes);

        /// <summary>The <c>geof:sfOverlaps</c> predicate function.</summary>
        public static Utf8String SfOverlaps { get; } = new(SfOverlapsBytes);

        /// <summary>The <c>geof:ehEquals</c> predicate function.</summary>
        public static Utf8String EhEquals { get; } = new(EhEqualsBytes);

        /// <summary>The <c>geof:ehDisjoint</c> predicate function.</summary>
        public static Utf8String EhDisjoint { get; } = new(EhDisjointBytes);

        /// <summary>The <c>geof:ehMeet</c> predicate function.</summary>
        public static Utf8String EhMeet { get; } = new(EhMeetBytes);

        /// <summary>The <c>geof:ehOverlap</c> predicate function.</summary>
        public static Utf8String EhOverlap { get; } = new(EhOverlapBytes);

        /// <summary>The <c>geof:ehCovers</c> predicate function.</summary>
        public static Utf8String EhCovers { get; } = new(EhCoversBytes);

        /// <summary>The <c>geof:ehCoveredBy</c> predicate function.</summary>
        public static Utf8String EhCoveredBy { get; } = new(EhCoveredByBytes);

        /// <summary>The <c>geof:ehInside</c> predicate function.</summary>
        public static Utf8String EhInside { get; } = new(EhInsideBytes);

        /// <summary>The <c>geof:ehContains</c> predicate function.</summary>
        public static Utf8String EhContains { get; } = new(EhContainsBytes);

        /// <summary>The <c>geof:rcc8eq</c> predicate function.</summary>
        public static Utf8String Rcc8Eq { get; } = new(Rcc8EqBytes);

        /// <summary>The <c>geof:rcc8dc</c> predicate function.</summary>
        public static Utf8String Rcc8Dc { get; } = new(Rcc8DcBytes);

        /// <summary>The <c>geof:rcc8ec</c> predicate function.</summary>
        public static Utf8String Rcc8Ec { get; } = new(Rcc8EcBytes);

        /// <summary>The <c>geof:rcc8po</c> predicate function.</summary>
        public static Utf8String Rcc8Po { get; } = new(Rcc8PoBytes);

        /// <summary>The <c>geof:rcc8tppi</c> predicate function.</summary>
        public static Utf8String Rcc8Tppi { get; } = new(Rcc8TppiBytes);

        /// <summary>The <c>geof:rcc8tpp</c> predicate function.</summary>
        public static Utf8String Rcc8Tpp { get; } = new(Rcc8TppBytes);

        /// <summary>The <c>geof:rcc8ntpp</c> predicate function.</summary>
        public static Utf8String Rcc8Ntpp { get; } = new(Rcc8NtppBytes);

        /// <summary>The <c>geof:rcc8ntppi</c> predicate function.</summary>
        public static Utf8String Rcc8Ntppi { get; } = new(Rcc8NtppiBytes);

        /// <summary>Every IRI constant in this vocabulary, in declaration order — the GeoSPARQL function term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
        public static IReadOnlyList<Utf8String> All { get; } =
        [
            Boundary, BoundingCircle, MetricBuffer, Buffer, Centroid, ConvexHull, ConcaveHull,
            CoordinateDimension, Difference, Dimension, MetricDistance, Distance, Envelope, GeometryType,
            Intersection, Is3D, IsEmpty, IsMeasured, IsSimple, SpatialDimension, SymDifference, Transform,
            Union, MetricLength, Length, MetricPerimeter, Perimeter, MetricArea, Area, GeometryN, MaxX, MaxY,
            MaxZ, MinX, MinY, MinZ, NumGeometries, GetSrid, AggBoundingBox, AggBoundingCircle, AggCentroid,
            AggConcaveHull, AggConvexHull, AggUnion, AsWkt, AsGml, AsGeoJson, AsKml, AsDggs, Relate, SfEquals,
            SfDisjoint, SfIntersects, SfTouches, SfCrosses, SfWithin, SfContains, SfOverlaps, EhEquals,
            EhDisjoint, EhMeet, EhOverlap, EhCovers, EhCoveredBy, EhInside, EhContains, Rcc8Eq, Rcc8Dc, Rcc8Ec,
            Rcc8Po, Rcc8Tppi, Rcc8Tpp, Rcc8Ntpp, Rcc8Ntppi
        ];
    }

    /// <summary>
    /// The Simple Features geometry ontology: the geometry-type class hierarchy the RDFS Entailment
    /// conformance class matches over, plus the envelope corner properties. The roster is measured from the
    /// ratified ontology document.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "GeoVocabulary.Sf.Point is the intended usage pattern.")]
    public static class Sf
    {
        /// <summary>The Simple Features ontology namespace IRI.</summary>
        public const string Namespace = "http://www.opengis.net/ont/sf#";

        //Classes.
        private static byte[] GeometryBytes { get; } = "http://www.opengis.net/ont/sf#Geometry"u8.ToArray();
        private static byte[] PointBytes { get; } = "http://www.opengis.net/ont/sf#Point"u8.ToArray();
        private static byte[] CurveBytes { get; } = "http://www.opengis.net/ont/sf#Curve"u8.ToArray();
        private static byte[] LineStringBytes { get; } = "http://www.opengis.net/ont/sf#LineString"u8.ToArray();
        private static byte[] LineBytes { get; } = "http://www.opengis.net/ont/sf#Line"u8.ToArray();
        private static byte[] LinearRingBytes { get; } = "http://www.opengis.net/ont/sf#LinearRing"u8.ToArray();
        private static byte[] SurfaceBytes { get; } = "http://www.opengis.net/ont/sf#Surface"u8.ToArray();
        private static byte[] PolygonBytes { get; } = "http://www.opengis.net/ont/sf#Polygon"u8.ToArray();
        private static byte[] TriangleBytes { get; } = "http://www.opengis.net/ont/sf#Triangle"u8.ToArray();
        private static byte[] PolyhedralSurfaceBytes { get; } = "http://www.opengis.net/ont/sf#PolyhedralSurface"u8.ToArray();
        private static byte[] TinBytes { get; } = "http://www.opengis.net/ont/sf#TIN"u8.ToArray();
        private static byte[] GeometryCollectionBytes { get; } = "http://www.opengis.net/ont/sf#GeometryCollection"u8.ToArray();
        private static byte[] MultiPointBytes { get; } = "http://www.opengis.net/ont/sf#MultiPoint"u8.ToArray();
        private static byte[] MultiCurveBytes { get; } = "http://www.opengis.net/ont/sf#MultiCurve"u8.ToArray();
        private static byte[] MultiLineStringBytes { get; } = "http://www.opengis.net/ont/sf#MultiLineString"u8.ToArray();
        private static byte[] MultiSurfaceBytes { get; } = "http://www.opengis.net/ont/sf#MultiSurface"u8.ToArray();
        private static byte[] MultiPolygonBytes { get; } = "http://www.opengis.net/ont/sf#MultiPolygon"u8.ToArray();
        private static byte[] EnvelopeBytes { get; } = "http://www.opengis.net/ont/sf#Envelope"u8.ToArray();

        //Envelope corner properties.
        private static byte[] MaximumBytes { get; } = "http://www.opengis.net/ont/sf#maximum"u8.ToArray();
        private static byte[] MinimumBytes { get; } = "http://www.opengis.net/ont/sf#minimum"u8.ToArray();

        /// <summary>The <c>sf:Geometry</c> class — the root of the Simple Features geometry hierarchy.</summary>
        public static Utf8String Geometry { get; } = new(GeometryBytes);

        /// <summary>The <c>sf:Point</c> class.</summary>
        public static Utf8String Point { get; } = new(PointBytes);

        /// <summary>The <c>sf:Curve</c> abstract class.</summary>
        public static Utf8String Curve { get; } = new(CurveBytes);

        /// <summary>The <c>sf:LineString</c> class.</summary>
        public static Utf8String LineString { get; } = new(LineStringBytes);

        /// <summary>The <c>sf:Line</c> class — a two-point line string.</summary>
        public static Utf8String Line { get; } = new(LineBytes);

        /// <summary>The <c>sf:LinearRing</c> class — a closed, simple line string.</summary>
        public static Utf8String LinearRing { get; } = new(LinearRingBytes);

        /// <summary>The <c>sf:Surface</c> abstract class.</summary>
        public static Utf8String Surface { get; } = new(SurfaceBytes);

        /// <summary>The <c>sf:Polygon</c> class.</summary>
        public static Utf8String Polygon { get; } = new(PolygonBytes);

        /// <summary>The <c>sf:Triangle</c> class.</summary>
        public static Utf8String Triangle { get; } = new(TriangleBytes);

        /// <summary>The <c>sf:PolyhedralSurface</c> class.</summary>
        public static Utf8String PolyhedralSurface { get; } = new(PolyhedralSurfaceBytes);

        /// <summary>The <c>sf:TIN</c> class — a triangulated irregular network.</summary>
        public static Utf8String Tin { get; } = new(TinBytes);

        /// <summary>The <c>sf:GeometryCollection</c> class.</summary>
        public static Utf8String GeometryCollection { get; } = new(GeometryCollectionBytes);

        /// <summary>The <c>sf:MultiPoint</c> class.</summary>
        public static Utf8String MultiPoint { get; } = new(MultiPointBytes);

        /// <summary>The <c>sf:MultiCurve</c> abstract class.</summary>
        public static Utf8String MultiCurve { get; } = new(MultiCurveBytes);

        /// <summary>The <c>sf:MultiLineString</c> class.</summary>
        public static Utf8String MultiLineString { get; } = new(MultiLineStringBytes);

        /// <summary>The <c>sf:MultiSurface</c> abstract class.</summary>
        public static Utf8String MultiSurface { get; } = new(MultiSurfaceBytes);

        /// <summary>The <c>sf:MultiPolygon</c> class.</summary>
        public static Utf8String MultiPolygon { get; } = new(MultiPolygonBytes);

        /// <summary>The <c>sf:Envelope</c> class — an axis-aligned bounding box.</summary>
        public static Utf8String Envelope { get; } = new(EnvelopeBytes);

        /// <summary>The <c>sf:maximum</c> property — the point carrying an envelope's maximum coordinate values.</summary>
        public static Utf8String Maximum { get; } = new(MaximumBytes);

        /// <summary>The <c>sf:minimum</c> property — the point carrying an envelope's minimum coordinate values.</summary>
        public static Utf8String Minimum { get; } = new(MinimumBytes);

        /// <summary>Every IRI constant in this vocabulary, in declaration order — the Simple Features ontology term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
        public static IReadOnlyList<Utf8String> All { get; } =
        [
            Geometry, Point, Curve, LineString, Line, LinearRing, Surface, Polygon, Triangle,
            PolyhedralSurface, Tin, GeometryCollection, MultiPoint, MultiCurve, MultiLineString, MultiSurface,
            MultiPolygon, Envelope, Maximum, Minimum
        ];
    }

    /// <summary>
    /// The GML geometry ontology: the GML 3.2.1 geometry-type class hierarchy the RDFS Entailment
    /// conformance class matches over. The roster is measured from the vendored ontology document and
    /// is pinned membership-exact against it by the conformance rows.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "GeoVocabulary.Gml.Point is the intended usage pattern.")]
    public static class Gml
    {
        /// <summary>The GML geometry ontology namespace IRI.</summary>
        public const string Namespace = "http://www.opengis.net/ont/gml#";

        //Classes.
        private static byte[] PointBytes { get; } = "http://www.opengis.net/ont/gml#Point"u8.ToArray();
        private static byte[] AbstractGeometricPrimitiveBytes { get; } = "http://www.opengis.net/ont/gml#AbstractGeometricPrimitive"u8.ToArray();
        private static byte[] AbstractGriddedSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#AbstractGriddedSurface"u8.ToArray();
        private static byte[] AbstractParametricCurveSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#AbstractParametricCurveSurface"u8.ToArray();
        private static byte[] PolyhedralSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#PolyhedralSurface"u8.ToArray();
        private static byte[] SurfaceBytes { get; } = "http://www.opengis.net/ont/gml#Surface"u8.ToArray();
        private static byte[] ArcBytes { get; } = "http://www.opengis.net/ont/gml#Arc"u8.ToArray();
        private static byte[] ArcStringBytes { get; } = "http://www.opengis.net/ont/gml#ArcString"u8.ToArray();
        private static byte[] PolynomialSplineBytes { get; } = "http://www.opengis.net/ont/gml#PolynomialSpline"u8.ToArray();
        private static byte[] SplineCurveBytes { get; } = "http://www.opengis.net/ont/gml#SplineCurve"u8.ToArray();
        private static byte[] MultiCurveBytes { get; } = "http://www.opengis.net/ont/gml#MultiCurve"u8.ToArray();
        private static byte[] MultiGeometryBytes { get; } = "http://www.opengis.net/ont/gml#MultiGeometry"u8.ToArray();
        private static byte[] CompositeSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#CompositeSurface"u8.ToArray();
        private static byte[] CompositeBytes { get; } = "http://www.opengis.net/ont/gml#Composite"u8.ToArray();
        private static byte[] OrientableSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#OrientableSurface"u8.ToArray();
        private static byte[] AbstractCurveSegmentBytes { get; } = "http://www.opengis.net/ont/gml#AbstractCurveSegment"u8.ToArray();
        private static byte[] CylinderBytes { get; } = "http://www.opengis.net/ont/gml#Cylinder"u8.ToArray();
        private static byte[] ShellBytes { get; } = "http://www.opengis.net/ont/gml#Shell"u8.ToArray();
        private static byte[] PolygonBytes { get; } = "http://www.opengis.net/ont/gml#Polygon"u8.ToArray();
        private static byte[] TinBytes { get; } = "http://www.opengis.net/ont/gml#Tin"u8.ToArray();
        private static byte[] TriangulatedSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#TriangulatedSurface"u8.ToArray();
        private static byte[] AbstractGeometryBytes { get; } = "http://www.opengis.net/ont/gml#AbstractGeometry"u8.ToArray();
        private static byte[] BezierBytes { get; } = "http://www.opengis.net/ont/gml#Bezier"u8.ToArray();
        private static byte[] BSplineBytes { get; } = "http://www.opengis.net/ont/gml#BSpline"u8.ToArray();
        private static byte[] CurveBytes { get; } = "http://www.opengis.net/ont/gml#Curve"u8.ToArray();
        private static byte[] OrientableCurveBytes { get; } = "http://www.opengis.net/ont/gml#OrientableCurve"u8.ToArray();
        private static byte[] LineStringSegmentBytes { get; } = "http://www.opengis.net/ont/gml#LineStringSegment"u8.ToArray();
        private static byte[] GeodesicBytes { get; } = "http://www.opengis.net/ont/gml#Geodesic"u8.ToArray();
        private static byte[] GeodesicStringBytes { get; } = "http://www.opengis.net/ont/gml#GeodesicString"u8.ToArray();
        private static byte[] AbstractSurfacePatchBytes { get; } = "http://www.opengis.net/ont/gml#AbstractSurfacePatch"u8.ToArray();
        private static byte[] GeometricComplexBytes { get; } = "http://www.opengis.net/ont/gml#GeometricComplex"u8.ToArray();
        private static byte[] ArcByBulgeBytes { get; } = "http://www.opengis.net/ont/gml#ArcByBulge"u8.ToArray();
        private static byte[] ArcStringByBulgeBytes { get; } = "http://www.opengis.net/ont/gml#ArcStringByBulge"u8.ToArray();
        private static byte[] CircleByCenterPointBytes { get; } = "http://www.opengis.net/ont/gml#CircleByCenterPoint"u8.ToArray();
        private static byte[] ArcByCenterPointBytes { get; } = "http://www.opengis.net/ont/gml#ArcByCenterPoint"u8.ToArray();
        private static byte[] MultiPointBytes { get; } = "http://www.opengis.net/ont/gml#MultiPoint"u8.ToArray();
        private static byte[] OffsetCurveBytes { get; } = "http://www.opengis.net/ont/gml#OffsetCurve"u8.ToArray();
        private static byte[] LineStringBytes { get; } = "http://www.opengis.net/ont/gml#LineString"u8.ToArray();
        private static byte[] CircleBytes { get; } = "http://www.opengis.net/ont/gml#Circle"u8.ToArray();
        private static byte[] ClothoidBytes { get; } = "http://www.opengis.net/ont/gml#Clothoid"u8.ToArray();
        private static byte[] TriangleBytes { get; } = "http://www.opengis.net/ont/gml#Triangle"u8.ToArray();
        private static byte[] PolygonPatchBytes { get; } = "http://www.opengis.net/ont/gml#PolygonPatch"u8.ToArray();
        private static byte[] CubicSplineBytes { get; } = "http://www.opengis.net/ont/gml#CubicSpline"u8.ToArray();
        private static byte[] ConeBytes { get; } = "http://www.opengis.net/ont/gml#Cone"u8.ToArray();
        private static byte[] CompositeSolidBytes { get; } = "http://www.opengis.net/ont/gml#CompositeSolid"u8.ToArray();
        private static byte[] SolidBytes { get; } = "http://www.opengis.net/ont/gml#Solid"u8.ToArray();
        private static byte[] LinearRingBytes { get; } = "http://www.opengis.net/ont/gml#LinearRing"u8.ToArray();
        private static byte[] RingBytes { get; } = "http://www.opengis.net/ont/gml#Ring"u8.ToArray();
        private static byte[] MultiSolidBytes { get; } = "http://www.opengis.net/ont/gml#MultiSolid"u8.ToArray();
        private static byte[] CompositeCurveBytes { get; } = "http://www.opengis.net/ont/gml#CompositeCurve"u8.ToArray();
        private static byte[] RectangleBytes { get; } = "http://www.opengis.net/ont/gml#Rectangle"u8.ToArray();
        private static byte[] SphereBytes { get; } = "http://www.opengis.net/ont/gml#Sphere"u8.ToArray();
        private static byte[] MultiSurfaceBytes { get; } = "http://www.opengis.net/ont/gml#MultiSurface"u8.ToArray();

        /// <summary>The <c>gml:Point</c> class.</summary>
        public static Utf8String Point { get; } = new(PointBytes);

        /// <summary>The <c>gml:AbstractGeometricPrimitive</c> abstract class.</summary>
        public static Utf8String AbstractGeometricPrimitive { get; } = new(AbstractGeometricPrimitiveBytes);

        /// <summary>The <c>gml:AbstractGriddedSurface</c> abstract class.</summary>
        public static Utf8String AbstractGriddedSurface { get; } = new(AbstractGriddedSurfaceBytes);

        /// <summary>The <c>gml:AbstractParametricCurveSurface</c> abstract class.</summary>
        public static Utf8String AbstractParametricCurveSurface { get; } = new(AbstractParametricCurveSurfaceBytes);

        /// <summary>The <c>gml:PolyhedralSurface</c> class.</summary>
        public static Utf8String PolyhedralSurface { get; } = new(PolyhedralSurfaceBytes);

        /// <summary>The <c>gml:Surface</c> class — a subclass of both the geometric-primitive and orientable-surface parents.</summary>
        public static Utf8String Surface { get; } = new(SurfaceBytes);

        /// <summary>The <c>gml:Arc</c> class.</summary>
        public static Utf8String Arc { get; } = new(ArcBytes);

        /// <summary>The <c>gml:ArcString</c> class.</summary>
        public static Utf8String ArcString { get; } = new(ArcStringBytes);

        /// <summary>The <c>gml:PolynomialSpline</c> class.</summary>
        public static Utf8String PolynomialSpline { get; } = new(PolynomialSplineBytes);

        /// <summary>The <c>gml:SplineCurve</c> class.</summary>
        public static Utf8String SplineCurve { get; } = new(SplineCurveBytes);

        /// <summary>The <c>gml:MultiCurve</c> class.</summary>
        public static Utf8String MultiCurve { get; } = new(MultiCurveBytes);

        /// <summary>The <c>gml:MultiGeometry</c> class.</summary>
        public static Utf8String MultiGeometry { get; } = new(MultiGeometryBytes);

        /// <summary>The <c>gml:CompositeSurface</c> class.</summary>
        public static Utf8String CompositeSurface { get; } = new(CompositeSurfaceBytes);

        /// <summary>The <c>gml:Composite</c> abstract class.</summary>
        public static Utf8String Composite { get; } = new(CompositeBytes);

        /// <summary>The <c>gml:OrientableSurface</c> class.</summary>
        public static Utf8String OrientableSurface { get; } = new(OrientableSurfaceBytes);

        /// <summary>The <c>gml:AbstractCurveSegment</c> abstract class — the curve-segment side-hierarchy root, bridging directly into <c>geo:Geometry</c>.</summary>
        public static Utf8String AbstractCurveSegment { get; } = new(AbstractCurveSegmentBytes);

        /// <summary>The <c>gml:Cylinder</c> class.</summary>
        public static Utf8String Cylinder { get; } = new(CylinderBytes);

        /// <summary>The <c>gml:Shell</c> class.</summary>
        public static Utf8String Shell { get; } = new(ShellBytes);

        /// <summary>The <c>gml:Polygon</c> class.</summary>
        public static Utf8String Polygon { get; } = new(PolygonBytes);

        /// <summary>The <c>gml:Tin</c> class — a triangulated irregular network.</summary>
        public static Utf8String Tin { get; } = new(TinBytes);

        /// <summary>The <c>gml:TriangulatedSurface</c> class.</summary>
        public static Utf8String TriangulatedSurface { get; } = new(TriangulatedSurfaceBytes);

        /// <summary>The <c>gml:AbstractGeometry</c> abstract class — the geometry root of the GML hierarchy, bridging into <c>geo:Geometry</c>.</summary>
        public static Utf8String AbstractGeometry { get; } = new(AbstractGeometryBytes);

        /// <summary>The <c>gml:Bezier</c> class.</summary>
        public static Utf8String Bezier { get; } = new(BezierBytes);

        /// <summary>The <c>gml:BSpline</c> class.</summary>
        public static Utf8String BSpline { get; } = new(BSplineBytes);

        /// <summary>The <c>gml:Curve</c> class — a subclass of both the geometric-primitive and orientable-curve parents.</summary>
        public static Utf8String Curve { get; } = new(CurveBytes);

        /// <summary>The <c>gml:OrientableCurve</c> class.</summary>
        public static Utf8String OrientableCurve { get; } = new(OrientableCurveBytes);

        /// <summary>The <c>gml:LineStringSegment</c> class.</summary>
        public static Utf8String LineStringSegment { get; } = new(LineStringSegmentBytes);

        /// <summary>The <c>gml:Geodesic</c> class.</summary>
        public static Utf8String Geodesic { get; } = new(GeodesicBytes);

        /// <summary>The <c>gml:GeodesicString</c> class.</summary>
        public static Utf8String GeodesicString { get; } = new(GeodesicStringBytes);

        /// <summary>The <c>gml:AbstractSurfacePatch</c> abstract class — the surface-patch side-hierarchy root, bridging directly into <c>geo:Geometry</c>.</summary>
        public static Utf8String AbstractSurfacePatch { get; } = new(AbstractSurfacePatchBytes);

        /// <summary>The <c>gml:GeometricComplex</c> class.</summary>
        public static Utf8String GeometricComplex { get; } = new(GeometricComplexBytes);

        /// <summary>The <c>gml:ArcByBulge</c> class.</summary>
        public static Utf8String ArcByBulge { get; } = new(ArcByBulgeBytes);

        /// <summary>The <c>gml:ArcStringByBulge</c> class.</summary>
        public static Utf8String ArcStringByBulge { get; } = new(ArcStringByBulgeBytes);

        /// <summary>The <c>gml:CircleByCenterPoint</c> class.</summary>
        public static Utf8String CircleByCenterPoint { get; } = new(CircleByCenterPointBytes);

        /// <summary>The <c>gml:ArcByCenterPoint</c> class.</summary>
        public static Utf8String ArcByCenterPoint { get; } = new(ArcByCenterPointBytes);

        /// <summary>The <c>gml:MultiPoint</c> class.</summary>
        public static Utf8String MultiPoint { get; } = new(MultiPointBytes);

        /// <summary>The <c>gml:OffsetCurve</c> class.</summary>
        public static Utf8String OffsetCurve { get; } = new(OffsetCurveBytes);

        /// <summary>The <c>gml:LineString</c> class.</summary>
        public static Utf8String LineString { get; } = new(LineStringBytes);

        /// <summary>The <c>gml:Circle</c> class.</summary>
        public static Utf8String Circle { get; } = new(CircleBytes);

        /// <summary>The <c>gml:Clothoid</c> class.</summary>
        public static Utf8String Clothoid { get; } = new(ClothoidBytes);

        /// <summary>The <c>gml:Triangle</c> class.</summary>
        public static Utf8String Triangle { get; } = new(TriangleBytes);

        /// <summary>The <c>gml:PolygonPatch</c> class.</summary>
        public static Utf8String PolygonPatch { get; } = new(PolygonPatchBytes);

        /// <summary>The <c>gml:CubicSpline</c> class.</summary>
        public static Utf8String CubicSpline { get; } = new(CubicSplineBytes);

        /// <summary>The <c>gml:Cone</c> class.</summary>
        public static Utf8String Cone { get; } = new(ConeBytes);

        /// <summary>The <c>gml:CompositeSolid</c> class.</summary>
        public static Utf8String CompositeSolid { get; } = new(CompositeSolidBytes);

        /// <summary>The <c>gml:Solid</c> class.</summary>
        public static Utf8String Solid { get; } = new(SolidBytes);

        /// <summary>The <c>gml:LinearRing</c> class.</summary>
        public static Utf8String LinearRing { get; } = new(LinearRingBytes);

        /// <summary>The <c>gml:Ring</c> class.</summary>
        public static Utf8String Ring { get; } = new(RingBytes);

        /// <summary>The <c>gml:MultiSolid</c> class.</summary>
        public static Utf8String MultiSolid { get; } = new(MultiSolidBytes);

        /// <summary>The <c>gml:CompositeCurve</c> class.</summary>
        public static Utf8String CompositeCurve { get; } = new(CompositeCurveBytes);

        /// <summary>The <c>gml:Rectangle</c> class.</summary>
        public static Utf8String Rectangle { get; } = new(RectangleBytes);

        /// <summary>The <c>gml:Sphere</c> class.</summary>
        public static Utf8String Sphere { get; } = new(SphereBytes);

        /// <summary>The <c>gml:MultiSurface</c> class.</summary>
        public static Utf8String MultiSurface { get; } = new(MultiSurfaceBytes);

        /// <summary>Every IRI constant in this vocabulary, in declaration order — the GML geometry ontology term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
        public static IReadOnlyList<Utf8String> All { get; } =
        [
            Point, AbstractGeometricPrimitive, AbstractGriddedSurface, AbstractParametricCurveSurface,
            PolyhedralSurface, Surface, Arc, ArcString, PolynomialSpline, SplineCurve, MultiCurve,
            MultiGeometry, CompositeSurface, Composite, OrientableSurface, AbstractCurveSegment, Cylinder,
            Shell, Polygon, Tin, TriangulatedSurface, AbstractGeometry, Bezier, BSpline, Curve,
            OrientableCurve, LineStringSegment, Geodesic, GeodesicString, AbstractSurfacePatch,
            GeometricComplex, ArcByBulge, ArcStringByBulge, CircleByCenterPoint, ArcByCenterPoint, MultiPoint,
            OffsetCurve, LineString, Circle, Clothoid, Triangle, PolygonPatch, CubicSpline, Cone,
            CompositeSolid, Solid, LinearRing, Ring, MultiSolid, CompositeCurve, Rectangle, Sphere,
            MultiSurface
        ];
    }
}
