using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The boolean overlay surface of the constructive set: intersection, union,
/// difference, and symmetric difference as whole point-set answers — lower-
/// dimensional pieces are result members, never dropped. Collection operands are
/// refused for intersection, difference, and symmetric difference (the relate
/// engine's refusal extended); union accepts them through a dimension-stratified
/// member fold, so the graph engine itself never sees a collection. Empty results
/// are the typed atomic empty of the operation's forced dimension — intersection
/// the minimum of the operand dimensions, union and symmetric difference the
/// maximum, difference the first operand's — keeping every result admissible to
/// the relate engine; the empty collection appears only when that dimension is
/// unresolvable. Results are always fresh, heap-backed, planar XY, canonicalized
/// deterministically, and never alias operand columns. A false answer means a
/// refused operand kind or a detected inconsistency in the represented
/// arrangement — the honest refusal, never a broken result.
/// </summary>
public static class GeometryOverlay
{
    /// <summary>Computes the intersection point set. False refuses a collection operand or an inconsistent arrangement.</summary>
    public static bool TryIntersection(in FlatGeometry first, in FlatGeometry second, out FlatGeometry result)
    {
        if(first.Kind == GeometryKind.GeometryCollection || second.Kind == GeometryKind.GeometryCollection)
        {
            result = default;

            return false;
        }

        return TryComputeBinary(in first, in second, OverlayOperation.Intersection, out result);
    }

    /// <summary>
    /// Computes the union point set. Collections are accepted in either or both
    /// positions at any nesting depth through the stratified member fold.
    /// </summary>
    public static bool TryUnion(in FlatGeometry first, in FlatGeometry second, out FlatGeometry result)
    {
        if(first.Kind == GeometryKind.GeometryCollection || second.Kind == GeometryKind.GeometryCollection)
        {
            return TryUnionFold(in first, in second, out result);
        }

        return TryComputeBinary(in first, in second, OverlayOperation.Union, out result);
    }

    /// <summary>Computes the difference point set. False refuses a collection operand or an inconsistent arrangement.</summary>
    public static bool TryDifference(in FlatGeometry first, in FlatGeometry second, out FlatGeometry result)
    {
        if(first.Kind == GeometryKind.GeometryCollection || second.Kind == GeometryKind.GeometryCollection)
        {
            result = default;

            return false;
        }

        return TryComputeBinary(in first, in second, OverlayOperation.Difference, out result);
    }

    /// <summary>Computes the symmetric-difference point set. False refuses a collection operand or an inconsistent arrangement.</summary>
    public static bool TrySymDifference(in FlatGeometry first, in FlatGeometry second, out FlatGeometry result)
    {
        if(first.Kind == GeometryKind.GeometryCollection || second.Kind == GeometryKind.GeometryCollection)
        {
            result = default;

            return false;
        }

        return TryComputeBinary(in first, in second, OverlayOperation.SymDifference, out result);
    }

    /// <summary>The binary engine over two non-collection operands.</summary>
    internal static bool TryComputeBinary(in FlatGeometry first, in FlatGeometry second, OverlayOperation operation, out FlatGeometry result)
    {
        int firstDimension = first.TopologicalDimension;
        int secondDimension = second.TopologicalDimension;
        int emptyDimension = EmptyResultDimension(operation, firstDimension, secondDimension);

        if(first.IsEmpty || second.IsEmpty)
        {
            result = ComputeWithEmptyOperand(in first, in second, operation, emptyDimension);

            return true;
        }

        var firstSegments = new List<OverlaySegment>();
        var secondSegments = new List<OverlaySegment>();
        OverlayNoding.CollectSegments(in first, 0, firstSegments);
        OverlayNoding.CollectSegments(in second, 1, secondSegments);

        //An operand without one positive-length segment is a point set — the
        //puntal locator path serves it whatever its kind gate says: an operand
        //gates by kind but contributes its point set.
        if(firstSegments.Count == 0 || secondSegments.Count == 0)
        {
            result = ComputeWithPuntalOperand(in first, in second, firstSegments.Count == 0, operation, emptyDimension);

            return true;
        }

        var segments = new List<OverlaySegment>(firstSegments.Count + secondSegments.Count);
        segments.AddRange(firstSegments);
        segments.AddRange(secondSegments);

        var graph = new OverlayGraph();

        if(!OverlayNoding.TryBuildGraph(segments, graph))
        {
            result = default;

            return false;
        }

        graph.SortStars();

        RelateShape firstShape = RelateShape.Create(in first);
        RelateShape secondShape = RelateShape.Create(in second);

        if(!OverlayLabeling.TryResolve(graph, firstShape, secondShape))
        {
            result = default;

            return false;
        }

        if(!OverlayAssembly.TryExtract(graph, operation, out OverlayResultPieces pieces))
        {
            result = default;

            return false;
        }

        result = Compose(pieces, emptyDimension);

        return true;
    }

    /// <summary>The typed-empty dimension the operation forces.</summary>
    private static int EmptyResultDimension(OverlayOperation operation, int firstDimension, int secondDimension)
    {
        return operation switch
        {
            OverlayOperation.Intersection => Math.Min(firstDimension, secondDimension),
            OverlayOperation.Difference => firstDimension,
            _ => Math.Max(firstDimension, secondDimension),
        };
    }

    /// <summary>The typed atomic empty of a forced dimension; the collection only when unresolvable.</summary>
    internal static FlatGeometry EmptyOfDimension(int dimension)
    {
        return dimension switch
        {
            2 => FlatGeometry.Empty(GeometryKind.Polygon),
            1 => FlatGeometry.Empty(GeometryKind.LineString),
            0 => FlatGeometry.Empty(GeometryKind.Point),
            _ => FlatGeometry.Empty(GeometryKind.GeometryCollection),
        };
    }

    /// <summary>The pinned empty-operand identities, every result a fresh rebuild.</summary>
    private static FlatGeometry ComputeWithEmptyOperand(in FlatGeometry first, in FlatGeometry second, OverlayOperation operation, int emptyDimension)
    {
        bool firstEmpty = first.IsEmpty;
        bool secondEmpty = second.IsEmpty;

        if(operation == OverlayOperation.Intersection || (firstEmpty && secondEmpty))
        {
            return EmptyOfDimension(emptyDimension);
        }

        if(operation == OverlayOperation.Difference)
        {
            return firstEmpty ? EmptyOfDimension(emptyDimension) : CanonicalRebuild(in first);
        }

        //Union and symmetric difference with one empty operand answer the other.
        return firstEmpty ? CanonicalRebuild(in second) : CanonicalRebuild(in first);
    }

    /// <summary>
    /// The locator path for a puntal-content operand: its distinct positions
    /// classify against the other operand; the other operand's point set survives
    /// whole wherever the operation keeps it, per the reference convention that
    /// removing a measure-zero set leaves a closed operand unchanged.
    /// </summary>
    private static FlatGeometry ComputeWithPuntalOperand(in FlatGeometry first, in FlatGeometry second, bool firstIsPuntal, OverlayOperation operation, int emptyDimension)
    {
        FlatGeometry puntal = firstIsPuntal ? first : second;
        FlatGeometry other = firstIsPuntal ? second : first;
        List<Point2d> points = DistinctSortedPoints(in puntal);
        bool otherIsPuntalToo = !HasPositiveLengthSegment(in other);

        if(otherIsPuntalToo)
        {
            return ComposePointSets(DistinctSortedPoints(in first), DistinctSortedPoints(in second), operation, emptyDimension);
        }

        RelateShape otherShape = RelateShape.Create(in other);
        var insidePoints = new List<Point2d>();
        var outsidePoints = new List<Point2d>();

        foreach(Point2d point in points)
        {
            if(otherShape.Locate(point) == PointPlacement.Exterior)
            {
                outsidePoints.Add(point);
            }
            else
            {
                insidePoints.Add(point);
            }
        }

        if(operation == OverlayOperation.Intersection)
        {
            return insidePoints.Count == 0 ? EmptyOfDimension(emptyDimension) : ComposePuntal(insidePoints);
        }

        if(operation == OverlayOperation.Difference)
        {
            if(firstIsPuntal)
            {
                return outsidePoints.Count == 0 ? EmptyOfDimension(emptyDimension) : ComposePuntal(outsidePoints);
            }

            //Removing finitely many points from a lineal or areal operand leaves
            //its closed point set — the adopted reference convention.
            return CanonicalRebuild(in other);
        }

        //Union and symmetric difference: the other operand whole, plus the puntal
        //leftovers outside it; symmetric difference coincides because the
        //overlapping points are measure-zero in the other operand.
        if(outsidePoints.Count == 0)
        {
            return CanonicalRebuild(in other);
        }

        var members = new List<FlatGeometry>();

        foreach(Point2d point in outsidePoints)
        {
            members.Add(FlatGeometryFactory.CreatePoint(point));
        }

        AppendSingularMembers(in other, members);

        return FlatGeometryFactory.CreateCollection(members);
    }

    /// <summary>Set algebra over two puntal coordinate sets.</summary>
    private static FlatGeometry ComposePointSets(List<Point2d> first, List<Point2d> second, OverlayOperation operation, int emptyDimension)
    {
        var secondSet = new HashSet<(double X, double Y)>();

        foreach(Point2d point in second)
        {
            secondSet.Add((point.X, point.Y));
        }

        var firstSet = new HashSet<(double X, double Y)>();

        foreach(Point2d point in first)
        {
            firstSet.Add((point.X, point.Y));
        }

        var kept = new List<Point2d>();

        foreach(Point2d point in first)
        {
            bool inSecond = secondSet.Contains((point.X, point.Y));

            if(OverlayAssembly.Member(operation, true, inSecond))
            {
                kept.Add(point);
            }
        }

        foreach(Point2d point in second)
        {
            bool inFirst = firstSet.Contains((point.X, point.Y));

            if(!inFirst && OverlayAssembly.Member(operation, false, true))
            {
                kept.Add(point);
            }
        }

        if(kept.Count == 0)
        {
            return EmptyOfDimension(emptyDimension);
        }

        kept.Sort((left, right) => OverlayGraph.ComparePoints(left, right));

        return ComposePuntal(kept);
    }

    /// <summary>A point or multipoint over already-distinct sorted positions.</summary>
    private static FlatGeometry ComposePuntal(List<Point2d> points)
    {
        if(points.Count == 1)
        {
            return FlatGeometryFactory.CreatePoint(points[0]);
        }

        var span = new Point2d[points.Count];
        points.CopyTo(span);

        return FlatGeometryFactory.CreateMultiPoint(span);
    }

    /// <summary>The distinct, sorted, zero-normalized positions of an operand.</summary>
    private static List<Point2d> DistinctSortedPoints(in FlatGeometry geometry)
    {
        var seen = new HashSet<(double X, double Y)>();
        var points = new List<Point2d>();

        foreach(Point2d vertex in geometry.Vertices)
        {
            Point2d normalized = OverlayNoding.NormalizeNode(vertex);

            if(seen.Add((normalized.X, normalized.Y)))
            {
                points.Add(normalized);
            }
        }

        points.Sort((left, right) => OverlayGraph.ComparePoints(left, right));

        return points;
    }

    /// <summary>Whether any part carries a positive-length segment.</summary>
    private static bool HasPositiveLengthSegment(in FlatGeometry geometry)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        foreach(FlatGeometryPart part in parts)
        {
            for(int index = 1; index < part.Length; index++)
            {
                if(vertices[part.Start + index - 1].X != vertices[part.Start + index].X
                    || vertices[part.Start + index - 1].Y != vertices[part.Start + index].Y)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The dimension-stratified member fold behind union's collection admission:
    /// members flatten iteratively at any depth, partition by effective content —
    /// areal with any non-degenerate ring, else lineal with any positive-length
    /// segment, else puntal — and each stratum folds through the binary engine in
    /// member order; the strata then merge under the coverage rule. The graph
    /// engine never sees a collection operand.
    /// </summary>
    private static bool TryUnionFold(in FlatGeometry first, in FlatGeometry second, out FlatGeometry result)
    {
        int emptyDimension = Math.Max(first.TopologicalDimension, second.TopologicalDimension);
        var members = new List<FlatGeometry>();
        FlattenIntoMembers(in first, members);
        FlattenIntoMembers(in second, members);

        var arealMembers = new List<FlatGeometry>();
        var linealMembers = new List<FlatGeometry>();
        var puntalPoints = new List<Point2d>();

        foreach(FlatGeometry member in members)
        {
            if(member.IsEmpty)
            {
                continue;
            }

            if(HasNonDegenerateRing(in member))
            {
                arealMembers.Add(member);
            }
            else if(HasPositiveLengthSegment(in member))
            {
                linealMembers.Add(member);
            }
            else
            {
                foreach(Point2d point in DistinctSortedPoints(in member))
                {
                    puntalPoints.Add(point);
                }
            }
        }

        if(!TryFoldStratum(arealMembers, keepDimension: 2, out FlatGeometry areaUnion))
        {
            result = default;

            return false;
        }

        if(!TryFoldStratum(linealMembers, keepDimension: 1, out FlatGeometry lineUnion))
        {
            result = default;

            return false;
        }

        //The coverage merge: line pieces covered by the areal result vanish into
        //it, puntal pieces covered by either higher stratum vanish likewise.
        FlatGeometry lineLeftover = lineUnion;

        if(!areaUnion.IsEmpty && !lineUnion.IsEmpty)
        {
            if(!TryComputeBinary(in lineUnion, in areaUnion, OverlayOperation.Difference, out lineLeftover))
            {
                result = default;

                return false;
            }
        }

        var leftoverPoints = new List<Point2d>();

        if(puntalPoints.Count > 0)
        {
            RelateShape? areaShape = areaUnion.IsEmpty ? null : RelateShape.Create(in areaUnion);
            RelateShape? lineShape = lineUnion.IsEmpty ? null : RelateShape.Create(in lineUnion);
            var seen = new HashSet<(double X, double Y)>();
            puntalPoints.Sort((left, right) => OverlayGraph.ComparePoints(left, right));

            foreach(Point2d point in puntalPoints)
            {
                if(!seen.Add((point.X, point.Y)))
                {
                    continue;
                }

                bool covered = (areaShape is not null && areaShape.Locate(point) != PointPlacement.Exterior)
                    || (lineShape is not null && lineShape.Locate(point) != PointPlacement.Exterior);

                if(!covered)
                {
                    leftoverPoints.Add(point);
                }
            }
        }

        var resultMembers = new List<FlatGeometry>();

        foreach(Point2d point in leftoverPoints)
        {
            resultMembers.Add(FlatGeometryFactory.CreatePoint(point));
        }

        if(!lineLeftover.IsEmpty)
        {
            AppendSingularMembers(in lineLeftover, resultMembers);
        }

        if(!areaUnion.IsEmpty)
        {
            AppendSingularMembers(in areaUnion, resultMembers);
        }

        if(resultMembers.Count == 0)
        {
            result = EmptyOfDimension(emptyDimension);

            return true;
        }

        if(resultMembers.Count == 1)
        {
            result = resultMembers[0];

            return true;
        }

        result = AllSameDimension(resultMembers, out int sharedDimension)
            ? ComposeHomogeneous(resultMembers, sharedDimension)
            : FlatGeometryFactory.CreateCollection(resultMembers);

        return true;
    }

    /// <summary>Folds one stratum's members left-to-right through the binary union.</summary>
    private static bool TryFoldStratum(List<FlatGeometry> stratumMembers, int keepDimension, out FlatGeometry union)
    {
        if(stratumMembers.Count == 0)
        {
            union = default;

            return true;
        }

        union = CanonicalRebuild(stratumMembers[0]);

        for(int index = 1; index < stratumMembers.Count; index++)
        {
            FlatGeometry member = stratumMembers[index];

            if(union.IsEmpty)
            {
                union = CanonicalRebuild(in member);

                continue;
            }

            if(!TryComputeBinary(in union, in member, OverlayOperation.Union, out FlatGeometry merged))
            {
                return false;
            }

            //A degenerate member can shed measure-zero residue below the
            //stratum's dimension; the fold keeps its own stratum and lets the
            //residue re-enter through the coverage merge as recorded best-effort
            //semantics.
            FlatGeometry kept = KeepDimension(in merged, keepDimension);
            union.Dispose();
            merged.Dispose();
            union = kept;
        }

        return true;
    }

    /// <summary>Keeps the pieces of one dimension from a possibly mixed result.</summary>
    private static FlatGeometry KeepDimension(in FlatGeometry geometry, int dimension)
    {
        if(geometry.Kind != GeometryKind.GeometryCollection)
        {
            return CanonicalRebuild(in geometry);
        }

        var members = new List<FlatGeometry>();
        FlattenIntoMembers(in geometry, members);
        var kept = new List<FlatGeometry>();

        foreach(FlatGeometry member in members)
        {
            if(!member.IsEmpty && member.TopologicalDimension == dimension)
            {
                kept.Add(member);
            }
        }

        if(kept.Count == 0)
        {
            return default;
        }

        if(kept.Count == 1)
        {
            return kept[0];
        }

        return ComposeHomogeneous(kept, dimension);
    }

    /// <summary>Flattens an operand into non-collection members, iteratively at any depth.</summary>
    internal static void FlattenIntoMembers(in FlatGeometry geometry, List<FlatGeometry> members)
    {
        if(geometry.Kind != GeometryKind.GeometryCollection)
        {
            members.Add(CanonicalRebuild(in geometry));

            return;
        }

        ReadOnlySpan<FlatGeometryNode> nodes = geometry.Nodes;
        var pending = new Stack<int>();

        if(nodes.Length > 0 && nodes[0].ChildCount > 0)
        {
            for(int childIndex = nodes[0].FirstChild + nodes[0].ChildCount - 1; childIndex >= nodes[0].FirstChild; childIndex--)
            {
                pending.Push(childIndex);
            }
        }

        while(pending.Count > 0)
        {
            int nodeIndex = pending.Pop();
            FlatGeometryNode node = nodes[nodeIndex];

            if(node.Kind == GeometryKind.GeometryCollection)
            {
                for(int childIndex = node.FirstChild + node.ChildCount - 1; childIndex >= node.FirstChild; childIndex--)
                {
                    pending.Push(childIndex);
                }

                continue;
            }

            members.Add(ExtractMember(in geometry, node));
        }
    }

    /// <summary>Copies one non-collection member node out of a collection.</summary>
    private static FlatGeometry ExtractMember(in FlatGeometry geometry, FlatGeometryNode node)
    {
        var builder = new FlatGeometryBuilder();
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        for(int partIndex = node.FirstPart; partIndex < node.FirstPart + node.PartCount; partIndex++)
        {
            FlatGeometryPart part = parts[partIndex];
            int start = builder.VertexCount;

            for(int vertexIndex = 0; vertexIndex < part.Length; vertexIndex++)
            {
                builder.AddVertex(vertices[part.Start + vertexIndex]);
            }

            builder.AddPart(new FlatGeometryPart(start, part.Length, part.Role));
        }

        builder.RootIndex = builder.AddNode(node.Kind, hasZ: false, hasM: false, firstPart: 0, partCount: node.PartCount);

        return builder.ToGeometry();
    }

    /// <summary>Whether the member carries at least one non-degenerate ring.</summary>
    private static bool HasNonDegenerateRing(in FlatGeometry geometry)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        foreach(FlatGeometryPart part in parts)
        {
            if(part.Role is FlatGeometryPartRole.ExteriorRing or FlatGeometryPartRole.InteriorRing
                && part.Length >= 4
                && RingGeometry.Orientation(vertices.Slice(part.Start, part.Length)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The canonical rebuild of a non-collection operand: the same point set as a
    /// fresh, planar-XY, deterministically canonicalized instance — never an alias.
    /// </summary>
    internal static FlatGeometry CanonicalRebuild(in FlatGeometry geometry)
    {
        if(geometry.IsEmpty)
        {
            return FlatGeometry.Empty(geometry.Kind);
        }

        GeometryKind kind = geometry.Kind;

        if(kind is GeometryKind.Point or GeometryKind.MultiPoint)
        {
            return ComposePuntal(DistinctSortedPoints(in geometry));
        }

        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        if(kind is GeometryKind.LineString or GeometryKind.MultiLineString)
        {
            var runs = new List<Point2d[]>();

            foreach(FlatGeometryPart part in parts)
            {
                if(part.Length >= 2)
                {
                    var run = new List<Point2d>(part.Length);

                    for(int index = 0; index < part.Length; index++)
                    {
                        run.Add(vertices[part.Start + index]);
                    }

                    runs.Add(OverlayAssembly.CanonicalizeRun(run));
                }
            }

            runs.Sort(CompareRunsByStart);

            if(runs.Count == 0)
            {
                return FlatGeometry.Empty(kind);
            }

            return runs.Count == 1
                ? FlatGeometryFactory.CreateLineString(runs[0])
                : FlatGeometryFactory.CreateMultiLineString(runs);
        }

        var polygons = new List<List<Point2d[]>>();
        List<Point2d[]>? current = null;

        foreach(FlatGeometryPart part in parts)
        {
            if(part.Length < 4)
            {
                continue;
            }

            var open = new List<Point2d>(part.Length - 1);

            for(int index = 0; index < part.Length - 1; index++)
            {
                open.Add(vertices[part.Start + index]);
            }

            if(part.Role == FlatGeometryPartRole.ExteriorRing)
            {
                current = [OverlayAssembly.CanonicalizeRing(open, wantCounterClockwise: true)];
                polygons.Add(current);
            }
            else if(current is not null)
            {
                current.Add(OverlayAssembly.CanonicalizeRing(open, wantCounterClockwise: false));
            }
        }

        if(polygons.Count == 0)
        {
            return FlatGeometry.Empty(kind);
        }

        foreach(List<Point2d[]> polygon in polygons)
        {
            polygon.Sort(1, polygon.Count - 1, Comparer<Point2d[]>.Create((left, right) => OverlayGraph.ComparePoints(left[0], right[0])));
        }

        polygons.Sort((left, right) => OverlayGraph.ComparePoints(left[0][0], right[0][0]));

        return polygons.Count == 1
            ? FlatGeometryFactory.CreatePolygon(polygons[0])
            : FlatGeometryFactory.CreateMultiPolygon(polygons);
    }

    /// <summary>Splits a non-collection geometry into singular canonical members.</summary>
    private static void AppendSingularMembers(in FlatGeometry geometry, List<FlatGeometry> members)
    {
        GeometryKind kind = geometry.Kind;

        if(kind is GeometryKind.Point or GeometryKind.MultiPoint)
        {
            foreach(Point2d point in DistinctSortedPoints(in geometry))
            {
                members.Add(FlatGeometryFactory.CreatePoint(point));
            }

            return;
        }

        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        if(kind is GeometryKind.LineString or GeometryKind.MultiLineString)
        {
            foreach(FlatGeometryPart part in parts)
            {
                if(part.Length >= 2)
                {
                    var run = new Point2d[part.Length];
                    vertices.Slice(part.Start, part.Length).CopyTo(run);
                    members.Add(FlatGeometryFactory.CreateLineString(run));
                }
            }

            return;
        }

        List<Point2d[]>? current = null;

        foreach(FlatGeometryPart part in parts)
        {
            if(part.Role == FlatGeometryPartRole.ExteriorRing)
            {
                if(current is not null)
                {
                    members.Add(FlatGeometryFactory.CreatePolygon(current));
                }

                current = [];
            }

            if(current is not null && part.Length > 0)
            {
                var ring = new Point2d[part.Length];
                vertices.Slice(part.Start, part.Length).CopyTo(ring);
                current.Add(ring);
            }
        }

        if(current is not null)
        {
            members.Add(FlatGeometryFactory.CreatePolygon(current));
        }
    }

    /// <summary>Composes the graph extraction's pieces into the result.</summary>
    private static FlatGeometry Compose(OverlayResultPieces pieces, int emptyDimension)
    {
        bool hasPolygons = pieces.Polygons.Count > 0;
        bool hasLines = pieces.Lines.Count > 0;
        bool hasPoints = pieces.Points.Count > 0;
        int strata = (hasPolygons ? 1 : 0) + (hasLines ? 1 : 0) + (hasPoints ? 1 : 0);

        if(strata == 0)
        {
            return EmptyOfDimension(emptyDimension);
        }

        if(strata == 1)
        {
            if(hasPolygons)
            {
                return pieces.Polygons.Count == 1
                    ? FlatGeometryFactory.CreatePolygon(pieces.Polygons[0])
                    : FlatGeometryFactory.CreateMultiPolygon(pieces.Polygons);
            }

            if(hasLines)
            {
                return pieces.Lines.Count == 1
                    ? FlatGeometryFactory.CreateLineString(pieces.Lines[0])
                    : FlatGeometryFactory.CreateMultiLineString(pieces.Lines);
            }

            return ComposePuntal(pieces.Points);
        }

        var members = new List<FlatGeometry>();

        foreach(Point2d point in pieces.Points)
        {
            members.Add(FlatGeometryFactory.CreatePoint(point));
        }

        foreach(Point2d[] run in pieces.Lines)
        {
            members.Add(FlatGeometryFactory.CreateLineString(run));
        }

        foreach(List<Point2d[]> polygon in pieces.Polygons)
        {
            members.Add(FlatGeometryFactory.CreatePolygon(polygon));
        }

        return FlatGeometryFactory.CreateCollection(members);
    }

    /// <summary>Whether every member shares one topological dimension.</summary>
    private static bool AllSameDimension(List<FlatGeometry> members, out int dimension)
    {
        dimension = members[0].TopologicalDimension;

        foreach(FlatGeometry member in members)
        {
            if(member.TopologicalDimension != dimension)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Composes singular same-dimension members into the tightest kind.</summary>
    private static FlatGeometry ComposeHomogeneous(List<FlatGeometry> members, int dimension)
    {
        if(dimension == 0)
        {
            var points = new List<Point2d>();

            foreach(FlatGeometry member in members)
            {
                foreach(Point2d point in DistinctSortedPoints(in member))
                {
                    points.Add(point);
                }
            }

            points.Sort((left, right) => OverlayGraph.ComparePoints(left, right));

            return ComposePuntal(points);
        }

        if(dimension == 1)
        {
            var runs = new List<Point2d[]>();

            foreach(FlatGeometry member in members)
            {
                ReadOnlySpan<FlatGeometryPart> parts = member.Parts;
                ReadOnlySpan<Point2d> vertices = member.Vertices;

                foreach(FlatGeometryPart part in parts)
                {
                    if(part.Length >= 2)
                    {
                        var run = new Point2d[part.Length];
                        vertices.Slice(part.Start, part.Length).CopyTo(run);
                        runs.Add(run);
                    }
                }
            }

            runs.Sort(CompareRunsByStart);

            return runs.Count == 1
                ? FlatGeometryFactory.CreateLineString(runs[0])
                : FlatGeometryFactory.CreateMultiLineString(runs);
        }

        var polygons = new List<List<Point2d[]>>();

        foreach(FlatGeometry member in members)
        {
            ReadOnlySpan<FlatGeometryPart> parts = member.Parts;
            ReadOnlySpan<Point2d> vertices = member.Vertices;
            List<Point2d[]>? current = null;

            foreach(FlatGeometryPart part in parts)
            {
                var ring = new Point2d[part.Length];
                vertices.Slice(part.Start, part.Length).CopyTo(ring);

                if(part.Role == FlatGeometryPartRole.ExteriorRing)
                {
                    current = [ring];
                    polygons.Add(current);
                }
                else
                {
                    current?.Add(ring);
                }
            }
        }

        polygons.Sort((left, right) => OverlayGraph.ComparePoints(left[0][0], right[0][0]));

        return polygons.Count == 1
            ? FlatGeometryFactory.CreatePolygon(polygons[0])
            : FlatGeometryFactory.CreateMultiPolygon(polygons);
    }

    /// <summary>Deterministic run order by first, then second vertex.</summary>
    private static int CompareRunsByStart(Point2d[] left, Point2d[] right)
    {
        int order = OverlayGraph.ComparePoints(left[0], right[0]);

        if(order != 0)
        {
            return order;
        }

        return OverlayGraph.ComparePoints(left[1], right[1]);
    }
}
