namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The structural role a vertex-run part plays inside its owning node. An exterior
/// ring begins a new polygon within a multipolygon run; interior rings that follow
/// belong to the polygon the preceding exterior ring opened.
/// </summary>
public enum FlatGeometryPartRole
{
    /// <summary>A single-vertex run: one point.</summary>
    Point,

    /// <summary>A polyline run: one linestring.</summary>
    Line,

    /// <summary>A closed shell ring; opens a polygon.</summary>
    ExteriorRing,

    /// <summary>A closed hole ring belonging to the polygon the preceding exterior ring opened.</summary>
    InteriorRing,
}

/// <summary>
/// One vertex-run slice of a <see cref="FlatGeometry"/>: <see cref="Length"/> vertices
/// beginning at <see cref="Start"/> in the vertex columns, tagged with the structural
/// <see cref="Role"/> it plays. Local to the flat model: the start is an
/// <see cref="int"/> because the vertex columns are arrays, whose index space an
/// <see cref="int"/> spans exactly.
/// </summary>
/// <param name="Start">The first vertex index of the run.</param>
/// <param name="Length">The vertex count of the run; zero only for an empty primitive.</param>
/// <param name="Role">The structural role of the run.</param>
public readonly record struct FlatGeometryPart(int Start, int Length, FlatGeometryPartRole Role);
