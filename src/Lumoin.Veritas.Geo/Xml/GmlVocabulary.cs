using System;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// Every well-known GML markup string the codec pair recognizes or emits, as UTF-8
/// span properties: element and attribute local names, the fixed attribute values,
/// the namespace names, and the refusal-recognition names for vocabulary the flat
/// model deliberately refuses. Every reader comparison and writer emission routes
/// through these members — no codec file spells a well-known string inline. Single
/// grammar bytes stay scanner grammar and lexicon territory, not vocabulary members.
/// </summary>
internal static class GmlVocabulary
{
    /// <summary>The GML 3.2 namespace name — the only namespace whose elements the codec recognizes.</summary>
    public static ReadOnlySpan<byte> GmlNamespace => "http://www.opengis.net/gml/3.2"u8;

    /// <summary>The GML 3.1 and earlier namespace name, recognized only to refuse it deliberately.</summary>
    public static ReadOnlySpan<byte> LegacyGmlNamespace => "http://www.opengis.net/gml"u8;

    /// <summary>The XLINK namespace name, recognized to refuse remote references.</summary>
    public static ReadOnlySpan<byte> XlinkNamespace => "http://www.w3.org/1999/xlink"u8;

    /// <summary>The XLINK reference attribute's local name.</summary>
    public static ReadOnlySpan<byte> HrefName => "href"u8;

    /// <summary>The point element's local name.</summary>
    public static ReadOnlySpan<byte> PointName => "Point"u8;

    /// <summary>The line-string element's local name.</summary>
    public static ReadOnlySpan<byte> LineStringName => "LineString"u8;

    /// <summary>The polygon element's local name.</summary>
    public static ReadOnlySpan<byte> PolygonName => "Polygon"u8;

    /// <summary>The linear-ring element's local name.</summary>
    public static ReadOnlySpan<byte> LinearRingName => "LinearRing"u8;

    /// <summary>The curve-bounded ring element's local name.</summary>
    public static ReadOnlySpan<byte> RingName => "Ring"u8;

    /// <summary>The segmented curve element's local name.</summary>
    public static ReadOnlySpan<byte> CurveName => "Curve"u8;

    /// <summary>The curve's segment container element's local name.</summary>
    public static ReadOnlySpan<byte> SegmentsName => "segments"u8;

    /// <summary>The linear curve segment's local name.</summary>
    public static ReadOnlySpan<byte> LineStringSegmentName => "LineStringSegment"u8;

    /// <summary>The three-point circular arc segment's local name.</summary>
    public static ReadOnlySpan<byte> ArcName => "Arc"u8;

    /// <summary>The three-point full-circle segment's local name.</summary>
    public static ReadOnlySpan<byte> CircleName => "Circle"u8;

    /// <summary>The center-and-radius full-circle segment's local name.</summary>
    public static ReadOnlySpan<byte> CircleByCenterPointName => "CircleByCenterPoint"u8;

    /// <summary>The radius element's local name inside a center-and-radius circle.</summary>
    public static ReadOnlySpan<byte> RadiusName => "radius"u8;

    /// <summary>The patch-composed surface element's local name.</summary>
    public static ReadOnlySpan<byte> SurfaceName => "Surface"u8;

    /// <summary>The surface's patch container element's local name.</summary>
    public static ReadOnlySpan<byte> PatchesName => "patches"u8;

    /// <summary>The planar polygon patch element's local name.</summary>
    public static ReadOnlySpan<byte> PolygonPatchName => "PolygonPatch"u8;

    /// <summary>The exterior boundary property's local name.</summary>
    public static ReadOnlySpan<byte> ExteriorName => "exterior"u8;

    /// <summary>The interior boundary property's local name.</summary>
    public static ReadOnlySpan<byte> InteriorName => "interior"u8;

    /// <summary>The single-position element's local name.</summary>
    public static ReadOnlySpan<byte> PosName => "pos"u8;

    /// <summary>The position-list element's local name.</summary>
    public static ReadOnlySpan<byte> PosListName => "posList"u8;

    /// <summary>The deprecated coordinates element's local name, recognized only to refuse it deliberately.</summary>
    public static ReadOnlySpan<byte> CoordinatesName => "coordinates"u8;

    /// <summary>The by-reference point property's local name, recognized only to refuse it deliberately.</summary>
    public static ReadOnlySpan<byte> PointPropertyName => "pointProperty"u8;

    /// <summary>The point repetition element's local name, recognized only to refuse it deliberately.</summary>
    public static ReadOnlySpan<byte> PointRepName => "pointRep"u8;

    /// <summary>The multi-point aggregate element's local name.</summary>
    public static ReadOnlySpan<byte> MultiPointName => "MultiPoint"u8;

    /// <summary>The multi-curve aggregate element's local name.</summary>
    public static ReadOnlySpan<byte> MultiCurveName => "MultiCurve"u8;

    /// <summary>The multi-surface aggregate element's local name.</summary>
    public static ReadOnlySpan<byte> MultiSurfaceName => "MultiSurface"u8;

    /// <summary>The heterogeneous aggregate element's local name.</summary>
    public static ReadOnlySpan<byte> MultiGeometryName => "MultiGeometry"u8;

    /// <summary>The singular point member property's local name.</summary>
    public static ReadOnlySpan<byte> PointMemberName => "pointMember"u8;

    /// <summary>The plural point member property's local name.</summary>
    public static ReadOnlySpan<byte> PointMembersName => "pointMembers"u8;

    /// <summary>The singular curve member property's local name.</summary>
    public static ReadOnlySpan<byte> CurveMemberName => "curveMember"u8;

    /// <summary>The plural curve member property's local name.</summary>
    public static ReadOnlySpan<byte> CurveMembersName => "curveMembers"u8;

    /// <summary>The singular surface member property's local name.</summary>
    public static ReadOnlySpan<byte> SurfaceMemberName => "surfaceMember"u8;

    /// <summary>The plural surface member property's local name.</summary>
    public static ReadOnlySpan<byte> SurfaceMembersName => "surfaceMembers"u8;

    /// <summary>The singular geometry member property's local name.</summary>
    public static ReadOnlySpan<byte> GeometryMemberName => "geometryMember"u8;

    /// <summary>The plural geometry member property's local name.</summary>
    public static ReadOnlySpan<byte> GeometryMembersName => "geometryMembers"u8;

    /// <summary>The coordinate-reference-system attribute's local name.</summary>
    public static ReadOnlySpan<byte> SrsNameName => "srsName"u8;

    /// <summary>The coordinate-dimension attribute's local name.</summary>
    public static ReadOnlySpan<byte> SrsDimensionName => "srsDimension"u8;

    /// <summary>The position-count attribute's local name on a position list.</summary>
    public static ReadOnlySpan<byte> CountName => "count"u8;

    /// <summary>The axis-label attribute's local name, tolerated and ignored.</summary>
    public static ReadOnlySpan<byte> AxisLabelsName => "axisLabels"u8;

    /// <summary>The unit-label attribute's local name, tolerated and ignored.</summary>
    public static ReadOnlySpan<byte> UomLabelsName => "uomLabels"u8;

    /// <summary>The curve-segment interpolation attribute's local name.</summary>
    public static ReadOnlySpan<byte> InterpolationName => "interpolation"u8;

    /// <summary>The arc-count attribute's local name.</summary>
    public static ReadOnlySpan<byte> NumArcName => "numArc"u8;

    /// <summary>The unit-of-measure attribute's local name on a radius.</summary>
    public static ReadOnlySpan<byte> UomName => "uom"u8;

    /// <summary>The ring aggregation attribute's local name.</summary>
    public static ReadOnlySpan<byte> AggregationTypeName => "aggregationType"u8;

    /// <summary>The object identifier attribute's local name, tolerated on read and generated on write.</summary>
    public static ReadOnlySpan<byte> IdName => "id"u8;

    /// <summary>The bearing angle element local names, removed by the circle restriction and refused deliberately.</summary>
    public static ReadOnlySpan<byte> StartAngleName => "startAngle"u8;

    /// <summary>The end bearing angle element's local name, refused beside the start angle.</summary>
    public static ReadOnlySpan<byte> EndAngleName => "endAngle"u8;

    /// <summary>The fixed interpolation value of a linear segment.</summary>
    public static ReadOnlySpan<byte> LinearValue => "linear"u8;

    /// <summary>The fixed interpolation value of a three-point arc.</summary>
    public static ReadOnlySpan<byte> CircularArcValue => "circularArc3Points"u8;

    /// <summary>The fixed interpolation value of a center-and-radius circle.</summary>
    public static ReadOnlySpan<byte> CircularArcCenterValue => "circularArcCenterPointWithRadius"u8;

    /// <summary>The fixed interpolation value of a planar patch.</summary>
    public static ReadOnlySpan<byte> PlanarValue => "planar"u8;

    /// <summary>The only admitted ring aggregation value.</summary>
    public static ReadOnlySpan<byte> SequenceValue => "sequence"u8;

    /// <summary>The only admitted arc-count token, in its canonical lexical form.</summary>
    public static ReadOnlySpan<byte> OneValue => "1"u8;

    /// <summary>The writer's element-open fragment: the angle bracket and the canonical prefix.</summary>
    public static ReadOnlySpan<byte> PrefixOpening => "<gml:"u8;

    /// <summary>The writer's end-tag fragment: the closing bracket pair and the canonical prefix.</summary>
    public static ReadOnlySpan<byte> PrefixClosing => "</gml:"u8;

    /// <summary>The root element's namespace declaration, emitted once.</summary>
    public static ReadOnlySpan<byte> RootNamespaceDeclaration => " xmlns:gml=\"http://www.opengis.net/gml/3.2\""u8;

    /// <summary>The identifier attribute's opening through its generated prefix.</summary>
    public static ReadOnlySpan<byte> IdAttributeOpening => " gml:id=\"g"u8;

    /// <summary>The system attribute's opening.</summary>
    public static ReadOnlySpan<byte> SrsNameAttributeOpening => " srsName=\""u8;

    /// <summary>The per-carrier third-dimension declaration, whole.</summary>
    public static ReadOnlySpan<byte> SrsDimensionThreeAttribute => " srsDimension=\"3\""u8;

    /// <summary>
    /// Whether a local name in the GML namespace is recognized non-simple-features
    /// vocabulary — curve segments, surfaces, solids, composites, and topology the
    /// flat model deliberately refuses as unsupported rather than unknown. The
    /// roster covers the names a simple-features document's neighborhood plausibly
    /// carries; an unlisted name still refuses, as unrecognized vocabulary.
    /// </summary>
    public static bool IsRefusedVocabulary(ReadOnlySpan<byte> localName)
    {
        return localName.SequenceEqual("ArcString"u8)
            || localName.SequenceEqual("ArcByCenterPoint"u8)
            || localName.SequenceEqual("ArcByBulge"u8)
            || localName.SequenceEqual("ArcStringByBulge"u8)
            || localName.SequenceEqual("Bezier"u8)
            || localName.SequenceEqual("BSpline"u8)
            || localName.SequenceEqual("CubicSpline"u8)
            || localName.SequenceEqual("Geodesic"u8)
            || localName.SequenceEqual("GeodesicString"u8)
            || localName.SequenceEqual("OrientableCurve"u8)
            || localName.SequenceEqual("CompositeCurve"u8)
            || localName.SequenceEqual("OrientableSurface"u8)
            || localName.SequenceEqual("CompositeSurface"u8)
            || localName.SequenceEqual("PolyhedralSurface"u8)
            || localName.SequenceEqual("TriangulatedSurface"u8)
            || localName.SequenceEqual("Tin"u8)
            || localName.SequenceEqual("Triangle"u8)
            || localName.SequenceEqual("Rectangle"u8)
            || localName.SequenceEqual("Solid"u8)
            || localName.SequenceEqual("CompositeSolid"u8)
            || localName.SequenceEqual("MultiSolid"u8)
            || localName.SequenceEqual("Grid"u8)
            || localName.SequenceEqual("RectifiedGrid"u8)
            || localName.SequenceEqual("GeometricComplex"u8)
            || localName.SequenceEqual("MultiLineString"u8)
            || localName.SequenceEqual("MultiPolygon"u8)
            || localName.SequenceEqual("outerBoundaryIs"u8)
            || localName.SequenceEqual("innerBoundaryIs"u8);
    }
}
