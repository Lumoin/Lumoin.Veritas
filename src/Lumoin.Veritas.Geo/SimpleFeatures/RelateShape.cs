using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Where a point sits relative to one operand's point set. The numeric values
/// index intersection-matrix rows and columns directly.
/// </summary>
internal enum PointPlacement
{
    /// <summary>The point is in the operand's interior.</summary>
    Interior = 0,

    /// <summary>The point is on the operand's boundary.</summary>
    Boundary = 1,

    /// <summary>The point is in the operand's exterior.</summary>
    Exterior = 2,
}

/// <summary>One polygon of an areal operand: its shell part followed by its hole parts.</summary>
/// <param name="FirstPart">Index of the exterior-ring part.</param>
/// <param name="PartCount">The shell plus its holes.</param>
internal readonly record struct PolygonGroup(int FirstPart, int PartCount);

/// <summary>
/// One relate operand decomposed for the engine: kind category, kind-intrinsic
/// and point-set dimensions, the Mod-2 endpoint-valence table for lineal
/// boundaries, polygon grouping with cached ring orientations, and the exact
/// point locator every probe rides. All sign decisions route through
/// <see cref="ExactOrientation"/>; all coincidence tests are value equality on
/// the ordinates, where negative and positive zero are the same point.
/// </summary>
internal sealed class RelateShape
{
    /// <summary>The wrapped operand; never a geometry collection.</summary>
    private FlatGeometry Geometry { get; }

    /// <summary>The operand's kind.</summary>
    public GeometryKind Kind { get; }

    /// <summary>Whether the operand's point set is empty.</summary>
    public bool IsEmpty { get; }

    /// <summary>The kind-intrinsic topological dimension — the predicate-gate currency.</summary>
    public int KindDimension { get; }

    /// <summary>The point-set dimension of the interior — the matrix-cell currency.</summary>
    public int InteriorDimension { get; }

    /// <summary>The point-set dimension of the boundary — the matrix-cell currency.</summary>
    public int BoundaryDimension { get; }

    /// <summary>Whether the kind is Point or MultiPoint.</summary>
    public bool IsPuntal { get; }

    /// <summary>Whether the kind is LineString or MultiLineString.</summary>
    public bool IsLineal { get; }

    /// <summary>Whether the kind is Polygon or MultiPolygon.</summary>
    public bool IsAreal { get; }

    /// <summary>Lineal endpoint valences keyed by ordinate value; null for other kinds.</summary>
    private Dictionary<(double X, double Y), int>? EndpointValences { get; }

    /// <summary>Per-part ring orientation: +1 counter-clockwise, −1 clockwise, 0 degenerate; null unless areal.</summary>
    private int[]? RingOrientations { get; }

    /// <summary>The polygon grouping of an areal operand; empty otherwise.</summary>
    public IReadOnlyList<PolygonGroup> ArealGroups { get; }

    /// <summary>The operand's vertex column.</summary>
    public ReadOnlySpan<Point2d> Vertices => Geometry.Vertices;

    /// <summary>The operand's flat part table.</summary>
    public ReadOnlySpan<FlatGeometryPart> Parts => Geometry.Parts;

    /// <summary>The larger of the interior and boundary point-set dimensions.</summary>
    public int ClosureDimension => Math.Max(InteriorDimension, BoundaryDimension);

    private RelateShape(
        FlatGeometry geometry,
        GeometryKind kind,
        bool isEmpty,
        int kindDimension,
        int interiorDimension,
        int boundaryDimension,
        Dictionary<(double X, double Y), int>? endpointValences,
        int[]? ringOrientations,
        IReadOnlyList<PolygonGroup> arealGroups)
    {
        Geometry = geometry;
        Kind = kind;
        IsEmpty = isEmpty;
        KindDimension = kindDimension;
        InteriorDimension = interiorDimension;
        BoundaryDimension = boundaryDimension;
        IsPuntal = kind is GeometryKind.Point or GeometryKind.MultiPoint;
        IsLineal = kind is GeometryKind.LineString or GeometryKind.MultiLineString;
        IsAreal = kind is GeometryKind.Polygon or GeometryKind.MultiPolygon;
        EndpointValences = endpointValences;
        RingOrientations = ringOrientations;
        ArealGroups = arealGroups;
    }

    /// <summary>
    /// Builds the decomposition of one non-collection operand: tallies lineal
    /// endpoint valences, groups areal parts into polygons, orients every
    /// ring, and derives both dimension currencies.
    /// </summary>
    public static RelateShape Create(in FlatGeometry geometry)
    {
        GeometryKind kind = geometry.Kind;

        if(kind == GeometryKind.GeometryCollection)
        {
            //The three-branch decomposition below would silently answer
            //Exterior for every probe against a collection; callers decompose
            //collections into members first (relate refuses them, the overlay
            //fold flattens them), so reaching here is a bookkeeping bug.
            throw new InvalidOperationException(
                "A geometry collection cannot be decomposed as one relate operand (simple-features shape bookkeeping).");
        }

        bool isEmpty = geometry.IsEmpty;
        int kindDimension = geometry.TopologicalDimension;
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        Dictionary<(double X, double Y), int>? valences = null;
        int[]? orientations = null;
        List<PolygonGroup> groups = [];
        int interiorDimension = -1;
        int boundaryDimension = -1;

        if(kind is GeometryKind.Point or GeometryKind.MultiPoint)
        {
            interiorDimension = isEmpty ? -1 : 0;
        }
        else if(kind is GeometryKind.LineString or GeometryKind.MultiLineString)
        {
            valences = [];

            foreach(FlatGeometryPart part in parts)
            {
                if(part.Length < 2)
                {
                    continue;
                }

                Point2d first = vertices[part.Start];
                Point2d last = vertices[part.Start + part.Length - 1];
                Tally(valences, first);
                Tally(valences, last);

                if(interiorDimension < 1 && HasDistinctVertices(vertices, part))
                {
                    interiorDimension = 1;
                }
            }

            if(!isEmpty && interiorDimension < 0)
            {
                interiorDimension = 0;
            }

            foreach(KeyValuePair<(double X, double Y), int> entry in valences)
            {
                if(entry.Value % 2 == 1)
                {
                    boundaryDimension = 0;

                    break;
                }
            }
        }
        else if(kind is GeometryKind.Polygon or GeometryKind.MultiPolygon)
        {
            orientations = new int[parts.Length];
            int groupStart = -1;

            for(int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                FlatGeometryPart part = parts[partIndex];
                orientations[partIndex] = OrientRing(vertices, part);

                if(part.Role == FlatGeometryPartRole.ExteriorRing)
                {
                    if(groupStart >= 0)
                    {
                        groups.Add(new PolygonGroup(groupStart, partIndex - groupStart));
                    }

                    groupStart = partIndex;
                }

                if(orientations[partIndex] != 0)
                {
                    interiorDimension = 2;
                }

                if(boundaryDimension < 1 && HasDistinctVertices(vertices, part))
                {
                    boundaryDimension = 1;
                }
            }

            if(groupStart >= 0)
            {
                groups.Add(new PolygonGroup(groupStart, parts.Length - groupStart));
            }

            if(!isEmpty && boundaryDimension < 0)
            {
                boundaryDimension = 0;
            }
        }

        return new RelateShape(geometry, kind, isEmpty, kindDimension, interiorDimension, boundaryDimension, valences, orientations, groups);
    }

    /// <summary>
    /// Whether the operand has at least one segment worth scanning — a lineal
    /// or areal operand with a positive-length stretch somewhere.
    /// </summary>
    public bool HasSegments => (IsLineal && InteriorDimension == 1) || (IsAreal && BoundaryDimension == 1);

    /// <summary>The cached orientation of the ring part at <paramref name="partIndex"/>.</summary>
    public int RingOrientation(int partIndex)
    {
        return RingOrientations is null ? 0 : RingOrientations[partIndex];
    }

    /// <summary>
    /// Whether <paramref name="point"/> is a lineal endpoint of odd valence —
    /// the Mod-2 boundary answer for a coordinate.
    /// </summary>
    public bool IsOddValenceEndpoint(Point2d point)
    {
        if(EndpointValences is null)
        {
            return false;
        }

        return EndpointValences.TryGetValue((point.X, point.Y), out int valence) && valence % 2 == 1;
    }

    /// <summary>
    /// Locates <paramref name="point"/> against the operand's point set:
    /// vertex membership for puntal operands, odd-valence-endpoint boundary
    /// then on-segment interior for lineal ones, ring-then-hole crossing
    /// tests per polygon group for areal ones.
    /// </summary>
    public PointPlacement Locate(Point2d point)
    {
        if(IsEmpty)
        {
            return PointPlacement.Exterior;
        }

        if(IsPuntal)
        {
            foreach(Point2d vertex in Vertices)
            {
                if(vertex.X == point.X && vertex.Y == point.Y)
                {
                    return PointPlacement.Interior;
                }
            }

            return PointPlacement.Exterior;
        }

        if(IsLineal)
        {
            if(IsOddValenceEndpoint(point))
            {
                return PointPlacement.Boundary;
            }

            ReadOnlySpan<FlatGeometryPart> parts = Parts;

            for(int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                if(OnPart(point, parts[partIndex]))
                {
                    return PointPlacement.Interior;
                }
            }

            return PointPlacement.Exterior;
        }

        bool onBoundary = false;

        foreach(PolygonGroup group in ArealGroups)
        {
            PointPlacement placement = LocateInGroup(point, group);

            if(placement == PointPlacement.Interior)
            {
                return PointPlacement.Interior;
            }

            if(placement == PointPlacement.Boundary)
            {
                onBoundary = true;
            }
        }

        return onBoundary ? PointPlacement.Boundary : PointPlacement.Exterior;
    }

    /// <summary>
    /// Locates a point against one polygon group: on any ring is boundary;
    /// inside the shell and inside no hole is interior; inside a hole is
    /// exterior.
    /// </summary>
    public PointPlacement LocateInGroup(Point2d point, PolygonGroup group)
    {
        ReadOnlySpan<FlatGeometryPart> parts = Parts;
        PointPlacement shellPlacement = LocateInRing(point, parts[group.FirstPart]);

        if(shellPlacement != PointPlacement.Interior)
        {
            return shellPlacement;
        }

        for(int holeIndex = 1; holeIndex < group.PartCount; holeIndex++)
        {
            PointPlacement holePlacement = LocateInRing(point, parts[group.FirstPart + holeIndex]);

            if(holePlacement == PointPlacement.Boundary)
            {
                return PointPlacement.Boundary;
            }

            if(holePlacement == PointPlacement.Interior)
            {
                return PointPlacement.Exterior;
            }
        }

        return PointPlacement.Interior;
    }

    /// <summary>
    /// Builds the four-double-per-part flat envelope table the pair scan
    /// prunes with: minimum X, minimum Y, maximum X, maximum Y per part.
    /// </summary>
    public double[] BuildPartBounds()
    {
        ReadOnlySpan<FlatGeometryPart> parts = Parts;
        ReadOnlySpan<Point2d> vertices = Vertices;
        double[] bounds = new double[parts.Length * 4];

        for(int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            FlatGeometryPart part = parts[partIndex];
            double minimumX = double.PositiveInfinity;
            double minimumY = double.PositiveInfinity;
            double maximumX = double.NegativeInfinity;
            double maximumY = double.NegativeInfinity;

            for(int vertexIndex = 0; vertexIndex < part.Length; vertexIndex++)
            {
                Point2d vertex = vertices[part.Start + vertexIndex];
                minimumX = Math.Min(minimumX, vertex.X);
                minimumY = Math.Min(minimumY, vertex.Y);
                maximumX = Math.Max(maximumX, vertex.X);
                maximumY = Math.Max(maximumY, vertex.Y);
            }

            bounds[partIndex * 4] = minimumX;
            bounds[(partIndex * 4) + 1] = minimumY;
            bounds[(partIndex * 4) + 2] = maximumX;
            bounds[(partIndex * 4) + 3] = maximumY;
        }

        return bounds;
    }

    /// <summary>
    /// The even-odd crossing-number location of a point against one ring:
    /// vertex coincidence and on-segment answer boundary; otherwise the
    /// rightward ray parity decides, with every side-of-edge sign routed
    /// through the exact orientation.
    /// </summary>
    private PointPlacement LocateInRing(Point2d point, FlatGeometryPart ring)
    {
        return RingGeometry.LocateInRing(point, Vertices.Slice(ring.Start, ring.Length));
    }

    /// <summary>Whether the point lies on any positive-length segment of the part.</summary>
    private bool OnPart(Point2d point, FlatGeometryPart part)
    {
        ReadOnlySpan<Point2d> vertices = Vertices;

        for(int vertexIndex = 1; vertexIndex < part.Length; vertexIndex++)
        {
            Point2d start = vertices[part.Start + vertexIndex - 1];
            Point2d end = vertices[part.Start + vertexIndex];

            if(start.X == end.X && start.Y == end.Y)
            {
                if(point.X == start.X && point.Y == start.Y)
                {
                    return true;
                }

                continue;
            }

            if(OnSegment(point, start, end))
            {
                return true;
            }
        }

        if(part.Length == 1)
        {
            Point2d only = vertices[part.Start];

            return point.X == only.X && point.Y == only.Y;
        }

        return false;
    }

    /// <summary>
    /// Whether the point lies on the closed segment: exact collinearity plus
    /// containment in the segment's box on both ordinates.
    /// </summary>
    private static bool OnSegment(Point2d point, Point2d start, Point2d end)
    {
        if(ExactOrientation.Orient2D(start, end, point) != 0)
        {
            return false;
        }

        double minimumX = Math.Min(start.X, end.X);
        double maximumX = Math.Max(start.X, end.X);
        double minimumY = Math.Min(start.Y, end.Y);
        double maximumY = Math.Max(start.Y, end.Y);

        return point.X >= minimumX && point.X <= maximumX && point.Y >= minimumY && point.Y <= maximumY;
    }

    /// <summary>
    /// Orients one ring by the extreme-vertex method: the exact orientation
    /// of the corner at the lexicographically smallest vertex — no summed
    /// area, so no unbounded exact arithmetic. Zero means degenerate.
    /// </summary>
    private static int OrientRing(ReadOnlySpan<Point2d> vertices, FlatGeometryPart ring)
    {
        return RingGeometry.Orientation(vertices.Slice(ring.Start, ring.Length));
    }

    /// <summary>Whether the part carries at least one positive-length segment.</summary>
    private static bool HasDistinctVertices(ReadOnlySpan<Point2d> vertices, FlatGeometryPart part)
    {
        for(int vertexIndex = 1; vertexIndex < part.Length; vertexIndex++)
        {
            Point2d start = vertices[part.Start + vertexIndex - 1];
            Point2d end = vertices[part.Start + vertexIndex];

            if(start.X != end.X || start.Y != end.Y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Increments one endpoint's valence in the Mod-2 table.</summary>
    private static void Tally(Dictionary<(double X, double Y), int> valences, Point2d position)
    {
        valences.TryGetValue((position.X, position.Y), out int valence);
        valences[(position.X, position.Y)] = valence + 1;
    }
}
