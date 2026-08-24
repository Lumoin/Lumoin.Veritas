namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The tagged geometry kinds of the flat model: the seven concrete Simple Features
/// kinds plus the heterogeneous collection. Non-surface tags normalize on read
/// (<c>TRIANGLE</c> to <see cref="Polygon"/>; <c>TIN</c> and <c>POLYHEDRALSURFACE</c>
/// to <see cref="MultiPolygon"/>), so no value beyond these eight ever appears in a
/// <see cref="FlatGeometry"/>.
/// </summary>
public enum GeometryKind
{
    /// <summary>A single position, possibly empty.</summary>
    Point,

    /// <summary>A polyline of two or more positions, possibly empty.</summary>
    LineString,

    /// <summary>One exterior ring and zero or more interior rings, possibly empty.</summary>
    Polygon,

    /// <summary>Zero or more points as one node with a flat part run.</summary>
    MultiPoint,

    /// <summary>Zero or more linestrings as one node with a flat part run.</summary>
    MultiLineString,

    /// <summary>Zero or more polygons as one node with a flat part run.</summary>
    MultiPolygon,

    /// <summary>A heterogeneous collection; the only kind that nests child nodes.</summary>
    GeometryCollection,
}
