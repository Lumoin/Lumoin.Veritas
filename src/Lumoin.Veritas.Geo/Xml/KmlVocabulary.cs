using System;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// Every well-known KML markup string the codec pair recognizes or emits, as UTF-8
/// span properties: element and attribute local names, the closed altitude-mode
/// value set, the namespace names, and the writer's canonical emission fragments.
/// Every reader comparison and writer emission routes through these members — no
/// codec file spells a well-known string inline. Single grammar bytes stay scanner
/// grammar and lexicon territory, not vocabulary members. The writer carries no
/// self-closing fragments by design: the canonical form pairs every tag and every
/// empty value refuses before emission.
/// </summary>
internal static class KmlVocabulary
{
    /// <summary>The KML 2.2 namespace name — the only namespace whose elements the codec recognizes.</summary>
    public static ReadOnlySpan<byte> KmlNamespace => "http://www.opengis.net/kml/2.2"u8;

    /// <summary>The vendor extension namespace name, recognized only to refuse its elements deliberately — before identity, at every inspected position.</summary>
    public static ReadOnlySpan<byte> GxNamespace => "http://www.google.com/kml/ext/2.2"u8;

    /// <summary>The point element's local name.</summary>
    public static ReadOnlySpan<byte> PointName => "Point"u8;

    /// <summary>The line-string element's local name.</summary>
    public static ReadOnlySpan<byte> LineStringName => "LineString"u8;

    /// <summary>The linear-ring element's local name — a full geometry element in this format, carried as the closed line string.</summary>
    public static ReadOnlySpan<byte> LinearRingName => "LinearRing"u8;

    /// <summary>The polygon element's local name.</summary>
    public static ReadOnlySpan<byte> PolygonName => "Polygon"u8;

    /// <summary>The heterogeneous aggregate element's local name — the format's one aggregate.</summary>
    public static ReadOnlySpan<byte> MultiGeometryName => "MultiGeometry"u8;

    /// <summary>The textured-model element's local name, recognized only to refuse it deliberately — the value model carries no external 3D resource.</summary>
    public static ReadOnlySpan<byte> ModelName => "Model"u8;

    /// <summary>The coordinate run element's local name.</summary>
    public static ReadOnlySpan<byte> CoordinatesName => "coordinates"u8;

    /// <summary>The extrusion flag element's local name, tolerated and skipped wholesale.</summary>
    public static ReadOnlySpan<byte> ExtrudeName => "extrude"u8;

    /// <summary>The terrain-draping flag element's local name, tolerated and skipped wholesale.</summary>
    public static ReadOnlySpan<byte> TessellateName => "tessellate"u8;

    /// <summary>The altitude interpretation element's local name.</summary>
    public static ReadOnlySpan<byte> AltitudeModeName => "altitudeMode"u8;

    /// <summary>The exterior boundary property's local name.</summary>
    public static ReadOnlySpan<byte> OuterBoundaryName => "outerBoundaryIs"u8;

    /// <summary>The interior boundary property's local name.</summary>
    public static ReadOnlySpan<byte> InnerBoundaryName => "innerBoundaryIs"u8;

    /// <summary>The object identifier attribute's local name, ignored wholesale — the codec carries no identity and emits none.</summary>
    public static ReadOnlySpan<byte> IdName => "id"u8;

    /// <summary>The update-target attribute's local name, ignored wholesale beside the identifier.</summary>
    public static ReadOnlySpan<byte> TargetIdName => "targetId"u8;

    /// <summary>The altitude-mode token that clamps to the terrain — the schema default an exactly-empty element also means.</summary>
    public static ReadOnlySpan<byte> ClampToGroundValue => "clampToGround"u8;

    /// <summary>The altitude-mode token measured from the terrain surface.</summary>
    public static ReadOnlySpan<byte> RelativeToGroundValue => "relativeToGround"u8;

    /// <summary>The altitude-mode token measured from the vertical datum.</summary>
    public static ReadOnlySpan<byte> AbsoluteValue => "absolute"u8;

    /// <summary>The root element's default namespace declaration, emitted once.</summary>
    public static ReadOnlySpan<byte> RootNamespaceDeclaration => " xmlns=\"http://www.opengis.net/kml/2.2\""u8;

    /// <summary>The whole altitude-mode emission for a third-dimension node — the only altitude-mode form the writer ever emits.</summary>
    public static ReadOnlySpan<byte> AbsoluteAltitudeModeElement => "<altitudeMode>absolute</altitudeMode>"u8;
}
