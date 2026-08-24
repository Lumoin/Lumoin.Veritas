using System;

using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Json;

/// <summary>
/// The single source of the GeoJSON format's well-known strings: the
/// recognized member names, the seven case-sensitive geometry type tags, the
/// reserved member names of the other GeoJSON types a Geometry object must
/// not carry, the legacy coordinate-reference-system name, and the canonical
/// emission fragments the writer composes its byte-exact form from. Every
/// reader comparison and writer emission routes through these members —
/// no codec file spells a well-known string inline.
/// </summary>
internal static class GeoJsonVocabulary
{
    /// <summary>The type member's name.</summary>
    public static ReadOnlySpan<byte> TypeMemberName => "type"u8;

    /// <summary>The coordinates member's name.</summary>
    public static ReadOnlySpan<byte> CoordinatesMemberName => "coordinates"u8;

    /// <summary>The geometries member's name.</summary>
    public static ReadOnlySpan<byte> GeometriesMemberName => "geometries"u8;

    /// <summary>The bounding-box member's name.</summary>
    public static ReadOnlySpan<byte> BoundingBoxMemberName => "bbox"u8;

    /// <summary>The removed legacy coordinate-reference-system member's name.</summary>
    public static ReadOnlySpan<byte> CrsMemberName => "crs"u8;

    /// <summary>
    /// The geometry member's name — a defining member of the Feature type a
    /// Geometry object must not carry.
    /// </summary>
    public static ReadOnlySpan<byte> GeometryMemberName => "geometry"u8;

    /// <summary>
    /// The properties member's name — a defining member of the Feature type
    /// a Geometry object must not carry.
    /// </summary>
    public static ReadOnlySpan<byte> PropertiesMemberName => "properties"u8;

    /// <summary>
    /// The features member's name — the defining member of the
    /// FeatureCollection type a Geometry object must not carry.
    /// </summary>
    public static ReadOnlySpan<byte> FeaturesMemberName => "features"u8;

    /// <summary>The name member inside a legacy crs object.</summary>
    public static ReadOnlySpan<byte> CrsNameMemberName => "name"u8;

    /// <summary>The name-form discriminator value of a legacy crs object.</summary>
    public static ReadOnlySpan<byte> CrsNameFormValue => "name"u8;

    /// <summary>
    /// The one legacy coordinate-reference-system name the codec tolerates:
    /// the 2008 urn spelling of CRS84, the system the format fixes anyway.
    /// </summary>
    public static ReadOnlySpan<byte> LegacyCrs84Name => "urn:ogc:def:crs:OGC:1.3:CRS84"u8;

    /// <summary>
    /// The id member's name — a Feature identifier, a JSON string or number;
    /// on every other object kind the name is an ordinary foreign member.
    /// </summary>
    public static ReadOnlySpan<byte> IdMemberName => "id"u8;

    /// <summary>The point type tag.</summary>
    public static ReadOnlySpan<byte> PointTag => "Point"u8;

    /// <summary>The linestring type tag.</summary>
    public static ReadOnlySpan<byte> LineStringTag => "LineString"u8;

    /// <summary>The polygon type tag.</summary>
    public static ReadOnlySpan<byte> PolygonTag => "Polygon"u8;

    /// <summary>The multipoint type tag.</summary>
    public static ReadOnlySpan<byte> MultiPointTag => "MultiPoint"u8;

    /// <summary>The multilinestring type tag.</summary>
    public static ReadOnlySpan<byte> MultiLineStringTag => "MultiLineString"u8;

    /// <summary>The multipolygon type tag.</summary>
    public static ReadOnlySpan<byte> MultiPolygonTag => "MultiPolygon"u8;

    /// <summary>The geometry-collection type tag.</summary>
    public static ReadOnlySpan<byte> GeometryCollectionTag => "GeometryCollection"u8;

    /// <summary>The feature type tag.</summary>
    public static ReadOnlySpan<byte> FeatureTag => "Feature"u8;

    /// <summary>The feature-collection type tag.</summary>
    public static ReadOnlySpan<byte> FeatureCollectionTag => "FeatureCollection"u8;

    /// <summary>The JSON null literal — the unlocated geometry and the properties degradation form.</summary>
    public static ReadOnlySpan<byte> NullLiteral => "null"u8;

    /// <summary>
    /// The canonical byte form of the empty geometry collection — the
    /// degradation target of an uninitialized carrier and the emission of
    /// the typed empty collection.
    /// </summary>
    public static ReadOnlySpan<byte> EmptyCollectionDocument => "{\"type\":\"GeometryCollection\",\"geometries\":[]}"u8;

    /// <summary>
    /// The canonical opening of a geometry collection, up to and including
    /// the geometries array's opening bracket.
    /// </summary>
    public static ReadOnlySpan<byte> CollectionOpening => "{\"type\":\"GeometryCollection\",\"geometries\":["u8;

    /// <summary>
    /// The canonical opening of a leaf geometry object, up to the type
    /// tag's opening quote.
    /// </summary>
    public static ReadOnlySpan<byte> LeafOpening => "{\"type\":\""u8;

    /// <summary>
    /// The canonical bridge between a leaf's type tag and its coordinates
    /// value: the tag's closing quote, the separator, and the coordinates
    /// member's name.
    /// </summary>
    public static ReadOnlySpan<byte> CoordinatesOpening => "\",\"coordinates\":"u8;

    /// <summary>The canonical opening of a Feature object, through its type member.</summary>
    public static ReadOnlySpan<byte> FeatureOpening => "{\"type\":\"Feature\""u8;

    /// <summary>The canonical opening of a FeatureCollection object, through its type member.</summary>
    public static ReadOnlySpan<byte> FeatureCollectionOpening => "{\"type\":\"FeatureCollection\""u8;

    /// <summary>The canonical bridge into the id member.</summary>
    public static ReadOnlySpan<byte> IdOpening => ",\"id\":"u8;

    /// <summary>The canonical bridge into the bbox member, its array opened.</summary>
    public static ReadOnlySpan<byte> BoundingBoxOpening => ",\"bbox\":["u8;

    /// <summary>The canonical bridge into the geometry member.</summary>
    public static ReadOnlySpan<byte> GeometryOpening => ",\"geometry\":"u8;

    /// <summary>The canonical bridge into the properties member.</summary>
    public static ReadOnlySpan<byte> PropertiesOpening => ",\"properties\":"u8;

    /// <summary>The canonical bridge into the features member, its array opened.</summary>
    public static ReadOnlySpan<byte> FeaturesOpening => ",\"features\":["u8;

    /// <summary>The canonical byte form of the empty feature collection.</summary>
    public static ReadOnlySpan<byte> EmptyFeatureCollectionDocument => "{\"type\":\"FeatureCollection\",\"features\":[]}"u8;

    /// <summary>
    /// The canonical byte form an uninitialized feature carrier degrades to:
    /// the empty-collection geometry and the null properties value.
    /// </summary>
    public static ReadOnlySpan<byte> DefaultFeatureDocument => "{\"type\":\"Feature\",\"geometry\":{\"type\":\"GeometryCollection\",\"geometries\":[]},\"properties\":null}"u8;

    /// <summary>The type tag bytes of a non-collection kind.</summary>
    public static ReadOnlySpan<byte> TagOf(GeometryKind kind)
    {
        return kind switch
        {
            GeometryKind.Point => PointTag,
            GeometryKind.LineString => LineStringTag,
            GeometryKind.Polygon => PolygonTag,
            GeometryKind.MultiPoint => MultiPointTag,
            GeometryKind.MultiLineString => MultiLineStringTag,
            GeometryKind.MultiPolygon => MultiPolygonTag,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown geometry kind.")
        };
    }
}
