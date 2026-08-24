using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The relate engine: computes the full dimensionally extended
/// nine-intersection matrix of two non-collection geometries and answers the
/// twenty-four named topological predicates and arbitrary intersection
/// patterns over it. The engine composes three evidence sources through one
/// raise-only accumulator — dimension seeding of the exterior cells, point
/// probes for everything that produces no intersection node (including one
/// representative vertex per node-free part, licensed by location being
/// constant along any node-free stretch), and per-node fan resolution for
/// everything that does. No planar graph and no noding pass exist; collection
/// operands are refused because union semantics over possibly overlapping
/// members need the overlay machinery of a later rung.
/// </summary>
public static class GeometryRelate
{
    private const string MutualWithinEqualsPattern = "T*F**FFF*";
    private const string EqualsPattern = "TFFFTFFFT";
    private const string DisjointPattern = "FF*FF****";
    private const string TouchesBoundaryBoundary = "FT*******";
    private const string TouchesBoundaryInterior = "F**T*****";
    private const string TouchesInteriorBoundary = "F***T****";
    private const string CrossesLowerPattern = "T*T******";
    private const string CrossesHigherPattern = "T*****T**";
    private const string CrossesLinesPattern = "0********";
    private const string WithinPattern = "T*F**F***";
    private const string ContainsPattern = "T*****FF*";
    private const string OverlapsPattern = "T*T***T**";
    private const string OverlapsLinesPattern = "1*T***T**";
    private const string EhCoversPattern = "T*TFT*FF*";
    private const string EhCoveredByPattern = "TFF*TFT**";
    private const string EhInsidePattern = "TFF*FFT**";
    private const string EhContainsPattern = "T*TFF*FF*";
    private const string Rcc8DcPattern = "FFTFFTTTT";
    private const string Rcc8EcPattern = "FFTFTTTTT";
    private const string Rcc8PoPattern = "TTTTTTTTT";
    private const string Rcc8TppPattern = "TFFTTFTTT";
    private const string Rcc8TppiPattern = "TTTFTTFFT";
    private const string Rcc8NtppPattern = "TFFTFFTTT";
    private const string Rcc8NtppiPattern = "TTTFFTFFT";

    /// <summary>
    /// Computes the intersection matrix of two geometries. False only for a
    /// geometry-collection operand on either side — including the empty
    /// collection and the default value, whose kind reads as the collection.
    /// </summary>
    public static bool TryRelate(in FlatGeometry first, in FlatGeometry second, out IntersectionMatrix matrix)
    {
        if(first.Kind == GeometryKind.GeometryCollection || second.Kind == GeometryKind.GeometryCollection)
        {
            matrix = default;

            return false;
        }

        matrix = Compute(first, second);

        return true;
    }

    /// <summary>
    /// Tests two geometries against a nine-character intersection pattern
    /// over the case-sensitive alphabet <c>T</c>, <c>F</c>, <c>*</c>,
    /// <c>0</c>, <c>1</c>, <c>2</c>. False for a malformed pattern or a
    /// collection operand; the match answer rides the out parameter.
    /// </summary>
    public static bool TryRelate(in FlatGeometry first, in FlatGeometry second, ReadOnlySpan<char> pattern, out bool matches)
    {
        matches = false;

        if(!IsValidPattern(pattern))
        {
            return false;
        }

        if(!TryRelate(first, second, out IntersectionMatrix matrix))
        {
            return false;
        }

        matches = matrix.Matches(pattern);

        return true;
    }

    /// <summary>
    /// The UTF-8 overload of the pattern test, over the same case-sensitive
    /// nine-symbol alphabet.
    /// </summary>
    public static bool TryRelate(in FlatGeometry first, in FlatGeometry second, ReadOnlySpan<byte> pattern, out bool matches)
    {
        matches = false;

        if(pattern.Length != 9)
        {
            return false;
        }

        Span<char> characters = stackalloc char[9];

        for(int index = 0; index < 9; index++)
        {
            characters[index] = (char)pattern[index];
        }

        return TryRelate(first, second, characters, out matches);
    }

    /// <summary>
    /// Evaluates one named topological predicate. False for a collection
    /// operand; the predicate answer rides the out parameter — a predicate
    /// whose dimension condition cannot hold answers a defined false result,
    /// not a refusal. An out-of-range predicate value is a caller contract
    /// violation and throws.
    /// </summary>
    public static bool TryEvaluate(in FlatGeometry first, in FlatGeometry second, TopologicalPredicate predicate, out bool result)
    {
        if(predicate < TopologicalPredicate.SfEquals || predicate > TopologicalPredicate.Rcc8Ntppi)
        {
            throw new ArgumentOutOfRangeException(nameof(predicate), predicate, "Unknown topological predicate.");
        }

        result = false;

        if(first.Kind == GeometryKind.GeometryCollection || second.Kind == GeometryKind.GeometryCollection)
        {
            return false;
        }

        IntersectionMatrix matrix = Compute(first, second);
        result = Evaluate(matrix, first.TopologicalDimension, second.TopologicalDimension, predicate);

        return true;
    }

    /// <summary>
    /// The predicate reader over a computed matrix: pure pattern tests for
    /// every family except the two Simple Features members whose defining
    /// conditions the standard states outside the pattern cells — the
    /// dimension-branched crosses and the equal-dimension-gated overlaps with
    /// its linear refinement. The gates read the kind-intrinsic dimensions.
    /// </summary>
    private static bool Evaluate(in IntersectionMatrix matrix, int firstDimension, int secondDimension, TopologicalPredicate predicate)
    {
        switch(predicate)
        {
            case TopologicalPredicate.SfEquals:
                //Simple Features defines equals as mutual within; the census's
                //TFFFTFFFT is its area/area illustration and cannot hold for
                //puntal operands, whose boundaries are empty. The Egenhofer
                //and RCC8 names have nothing but their pinned matrices and
                //stay literal, same split as crosses and overlaps.
                return matrix.Matches(MutualWithinEqualsPattern);
            case TopologicalPredicate.EhEquals:
            case TopologicalPredicate.Rcc8Eq:
                return matrix.Matches(EqualsPattern);
            case TopologicalPredicate.SfDisjoint:
            case TopologicalPredicate.EhDisjoint:
                return matrix.Matches(DisjointPattern);
            case TopologicalPredicate.SfIntersects:
                return !matrix.Matches(DisjointPattern);
            case TopologicalPredicate.SfTouches:
            case TopologicalPredicate.EhMeet:
                return matrix.Matches(TouchesBoundaryBoundary)
                    || matrix.Matches(TouchesBoundaryInterior)
                    || matrix.Matches(TouchesInteriorBoundary);
            case TopologicalPredicate.SfCrosses:
                if(firstDimension < secondDimension)
                {
                    return matrix.Matches(CrossesLowerPattern);
                }

                if(firstDimension > secondDimension)
                {
                    return matrix.Matches(CrossesHigherPattern);
                }

                return firstDimension == 1 && matrix.Matches(CrossesLinesPattern);
            case TopologicalPredicate.SfWithin:
                return matrix.Matches(WithinPattern);
            case TopologicalPredicate.SfContains:
                return matrix.Matches(ContainsPattern);
            case TopologicalPredicate.SfOverlaps:
                if(firstDimension != secondDimension)
                {
                    return false;
                }

                return matrix.Matches(firstDimension == 1 ? OverlapsLinesPattern : OverlapsPattern);
            case TopologicalPredicate.EhOverlap:
                return matrix.Matches(OverlapsPattern);
            case TopologicalPredicate.EhCovers:
                return matrix.Matches(EhCoversPattern);
            case TopologicalPredicate.EhCoveredBy:
                return matrix.Matches(EhCoveredByPattern);
            case TopologicalPredicate.EhInside:
                return matrix.Matches(EhInsidePattern);
            case TopologicalPredicate.EhContains:
                return matrix.Matches(EhContainsPattern);
            case TopologicalPredicate.Rcc8Dc:
                return matrix.Matches(Rcc8DcPattern);
            case TopologicalPredicate.Rcc8Ec:
                return matrix.Matches(Rcc8EcPattern);
            case TopologicalPredicate.Rcc8Po:
                return matrix.Matches(Rcc8PoPattern);
            case TopologicalPredicate.Rcc8Tpp:
                return matrix.Matches(Rcc8TppPattern);
            case TopologicalPredicate.Rcc8Tppi:
                return matrix.Matches(Rcc8TppiPattern);
            case TopologicalPredicate.Rcc8Ntpp:
                return matrix.Matches(Rcc8NtppPattern);
            default:
                return matrix.Matches(Rcc8NtppiPattern);
        }
    }

    /// <summary>
    /// The full matrix computation: seed, then the cheap definitional paths
    /// (empty operands, point sets against point sets, strictly disjoint
    /// envelopes), then probes, the segment scan with fan resolution, and the
    /// representative probes of every part the scan left node-free.
    /// </summary>
    private static IntersectionMatrix Compute(in FlatGeometry first, in FlatGeometry second)
    {
        RelateShape firstShape = RelateShape.Create(first);
        RelateShape secondShape = RelateShape.Create(second);
        RelateTopology topology = new();
        topology.Raise(PointPlacement.Exterior, PointPlacement.Exterior, 2);
        SeedBeyondClosure(firstShape, secondShape, topology);

        if(firstShape.IsEmpty || secondShape.IsEmpty)
        {
            return topology.ToMatrix();
        }

        if(firstShape.IsPuntal && secondShape.IsPuntal)
        {
            RelatePointSets(firstShape, secondShape, topology);

            return topology.ToMatrix();
        }

        if(HasStrictlyDisjointEnvelopes(first, second))
        {
            RaiseDisjointForm(firstShape, secondShape, topology);

            return topology.ToMatrix();
        }

        ProbeVertices(firstShape, secondShape, topology, firstIsRows: true);
        ProbeVertices(secondShape, firstShape, topology, firstIsRows: false);

        bool[] firstNoded = new bool[firstShape.Parts.Length];
        bool[] secondNoded = new bool[secondShape.Parts.Length];

        if(firstShape.HasSegments && secondShape.HasSegments)
        {
            ScanSegments(firstShape, secondShape, topology, firstNoded, secondNoded);
        }

        ProbeQuietParts(firstShape, secondShape, firstNoded, topology, firstIsRows: true);
        ProbeQuietParts(secondShape, firstShape, secondNoded, topology, firstIsRows: false);

        return topology.ToMatrix();
    }

    /// <summary>
    /// Seeds every exterior cell a dimension argument already decides: a
    /// point set of higher dimension than the other operand's closure cannot
    /// fit inside it, so part of it must lie in the other's exterior. An
    /// empty operand's closure dimension is −1, which makes the empty-operand
    /// matrix forms fall out of the same rule.
    /// </summary>
    private static void SeedBeyondClosure(RelateShape firstShape, RelateShape secondShape, RelateTopology topology)
    {
        if(firstShape.InteriorDimension > secondShape.ClosureDimension)
        {
            topology.Raise(PointPlacement.Interior, PointPlacement.Exterior, firstShape.InteriorDimension);
        }

        if(firstShape.BoundaryDimension > secondShape.ClosureDimension)
        {
            topology.Raise(PointPlacement.Boundary, PointPlacement.Exterior, firstShape.BoundaryDimension);
        }

        if(secondShape.InteriorDimension > firstShape.ClosureDimension)
        {
            topology.Raise(PointPlacement.Exterior, PointPlacement.Interior, secondShape.InteriorDimension);
        }

        if(secondShape.BoundaryDimension > firstShape.ClosureDimension)
        {
            topology.Raise(PointPlacement.Exterior, PointPlacement.Boundary, secondShape.BoundaryDimension);
        }
    }

    /// <summary>
    /// The point-set fast path for two puntal operands: vertex membership
    /// decides every interior cell, and puntal boundaries are empty.
    /// </summary>
    private static void RelatePointSets(RelateShape firstShape, RelateShape secondShape, RelateTopology topology)
    {
        foreach(Point2d vertex in firstShape.Vertices)
        {
            PointPlacement placement = secondShape.Locate(vertex);
            topology.Raise(PointPlacement.Interior, placement, 0);
        }

        foreach(Point2d vertex in secondShape.Vertices)
        {
            if(firstShape.Locate(vertex) == PointPlacement.Exterior)
            {
                topology.Raise(PointPlacement.Exterior, PointPlacement.Interior, 0);
            }
        }
    }

    /// <summary>
    /// The forced matrix of strictly separated envelopes: every interaction
    /// cell is empty and each operand's point sets land whole in the other's
    /// exterior at their own dimensions.
    /// </summary>
    private static void RaiseDisjointForm(RelateShape firstShape, RelateShape secondShape, RelateTopology topology)
    {
        if(firstShape.InteriorDimension >= 0)
        {
            topology.Raise(PointPlacement.Interior, PointPlacement.Exterior, firstShape.InteriorDimension);
        }

        if(firstShape.BoundaryDimension >= 0)
        {
            topology.Raise(PointPlacement.Boundary, PointPlacement.Exterior, firstShape.BoundaryDimension);
        }

        if(secondShape.InteriorDimension >= 0)
        {
            topology.Raise(PointPlacement.Exterior, PointPlacement.Interior, secondShape.InteriorDimension);
        }

        if(secondShape.BoundaryDimension >= 0)
        {
            topology.Raise(PointPlacement.Exterior, PointPlacement.Boundary, secondShape.BoundaryDimension);
        }
    }

    /// <summary>
    /// Whether the operands' envelopes are strictly separated — separated by
    /// a positive gap on some axis, so not even a touch is possible. Empty
    /// operands never reach this test.
    /// </summary>
    private static bool HasStrictlyDisjointEnvelopes(in FlatGeometry first, in FlatGeometry second)
    {
        if(!GeometryEnvelope.TryComputeBounds(first, out BoundingBox firstBounds)
            || !GeometryEnvelope.TryComputeBounds(second, out BoundingBox secondBounds))
        {
            return false;
        }

        return firstBounds.MaxX < secondBounds.MinX
            || secondBounds.MaxX < firstBounds.MinX
            || firstBounds.MaxY < secondBounds.MinY
            || secondBounds.MaxY < firstBounds.MinY;
    }

    /// <summary>
    /// The vertex probes: every puntal vertex, located with dimension zero
    /// against the other operand; every lineal run endpoint likewise, carrying
    /// its Mod-2 boundary status. Areal operands have no endpoint probes —
    /// their rings resolve through fans or the quiet-part probes.
    /// </summary>
    private static void ProbeVertices(RelateShape shape, RelateShape other, RelateTopology topology, bool firstIsRows)
    {
        ReadOnlySpan<FlatGeometryPart> parts = shape.Parts;
        ReadOnlySpan<Point2d> vertices = shape.Vertices;

        if(shape.IsPuntal)
        {
            for(int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                Point2d vertex = vertices[parts[partIndex].Start];
                RaiseOriented(topology, PointPlacement.Interior, other.Locate(vertex), 0, firstIsRows);
            }

            return;
        }

        if(!shape.IsLineal)
        {
            return;
        }

        for(int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            FlatGeometryPart part = parts[partIndex];

            if(part.Length < 2)
            {
                continue;
            }

            Point2d firstVertex = vertices[part.Start];
            Point2d lastVertex = vertices[part.Start + part.Length - 1];
            PointPlacement firstStatus = shape.IsOddValenceEndpoint(firstVertex) ? PointPlacement.Boundary : PointPlacement.Interior;
            PointPlacement lastStatus = shape.IsOddValenceEndpoint(lastVertex) ? PointPlacement.Boundary : PointPlacement.Interior;
            RaiseOriented(topology, firstStatus, other.Locate(firstVertex), 0, firstIsRows);
            RaiseOriented(topology, lastStatus, other.Locate(lastVertex), 0, firstIsRows);
        }
    }

    /// <summary>
    /// The representative probes of every part the scan left node-free: one
    /// original vertex speaks for the whole part because location is constant
    /// along a node-free stretch. Skipped entirely against a puntal operand,
    /// whose isolated points never produce nodes — there the puntal side's
    /// own vertex probes and the dimension seeds carry every cell. Ring
    /// probes add the two-dimensional neighborhood raises: the patches on
    /// both sides of a ring share the open region its representative sits in.
    /// </summary>
    private static void ProbeQuietParts(RelateShape shape, RelateShape other, bool[] noded, RelateTopology topology, bool firstIsRows)
    {
        if(other.IsPuntal || shape.IsPuntal)
        {
            return;
        }

        ReadOnlySpan<FlatGeometryPart> parts = shape.Parts;
        ReadOnlySpan<Point2d> vertices = shape.Vertices;

        for(int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            if(noded[partIndex])
            {
                continue;
            }

            FlatGeometryPart part = parts[partIndex];

            if(part.Length < 2)
            {
                continue;
            }

            Point2d representative = vertices[part.Start];
            bool isRing = part.Role is FlatGeometryPartRole.ExteriorRing or FlatGeometryPartRole.InteriorRing;
            bool degenerate = !HasPositiveLengthSegment(vertices, part);
            PointPlacement placement = other.Locate(representative);

            if(!isRing)
            {
                if(degenerate)
                {
                    continue;
                }

                RaiseOriented(topology, PointPlacement.Interior, placement, 1, firstIsRows);

                if(placement == PointPlacement.Interior && other.IsAreal)
                {
                    RaiseOriented(topology, PointPlacement.Exterior, placement, 2, firstIsRows);
                }

                continue;
            }

            if(degenerate)
            {
                RaiseOriented(topology, PointPlacement.Boundary, placement, 0, firstIsRows);

                continue;
            }

            RaiseOriented(topology, PointPlacement.Boundary, placement, 1, firstIsRows);

            bool boundsArea = shape.RingOrientation(partIndex) != 0;

            if(placement == PointPlacement.Interior && other.IsAreal)
            {
                if(boundsArea)
                {
                    RaiseOriented(topology, PointPlacement.Interior, placement, 2, firstIsRows);
                }

                RaiseOriented(topology, PointPlacement.Exterior, placement, 2, firstIsRows);
            }
            else if(placement == PointPlacement.Exterior && boundsArea)
            {
                RaiseOriented(topology, PointPlacement.Interior, placement, 2, firstIsRows);
            }
        }
    }

    /// <summary>
    /// The brute-force segment scan with envelope pruning at both the part
    /// and segment levels: every classified contact registers node sections
    /// for both operands, and every registered node resolves through its fan.
    /// </summary>
    private static void ScanSegments(RelateShape firstShape, RelateShape secondShape, RelateTopology topology, bool[] firstNoded, bool[] secondNoded)
    {
        ReadOnlySpan<FlatGeometryPart> firstParts = firstShape.Parts;
        ReadOnlySpan<FlatGeometryPart> secondParts = secondShape.Parts;
        ReadOnlySpan<Point2d> firstVertices = firstShape.Vertices;
        ReadOnlySpan<Point2d> secondVertices = secondShape.Vertices;
        double[] firstBounds = firstShape.BuildPartBounds();
        double[] secondBounds = secondShape.BuildPartBounds();
        Dictionary<(double X, double Y), List<NodeSection>> nodes = [];

        for(int firstIndex = 0; firstIndex < firstParts.Length; firstIndex++)
        {
            FlatGeometryPart firstPart = firstParts[firstIndex];

            if(firstPart.Length < 2)
            {
                continue;
            }

            for(int secondIndex = 0; secondIndex < secondParts.Length; secondIndex++)
            {
                FlatGeometryPart secondPart = secondParts[secondIndex];

                if(secondPart.Length < 2 || !BoundsOverlap(firstBounds, firstIndex, secondBounds, secondIndex))
                {
                    continue;
                }

                for(int firstSegment = 0; firstSegment < firstPart.Length - 1; firstSegment++)
                {
                    Point2d firstStart = firstVertices[firstPart.Start + firstSegment];
                    Point2d firstEnd = firstVertices[firstPart.Start + firstSegment + 1];

                    if(firstStart.X == firstEnd.X && firstStart.Y == firstEnd.Y)
                    {
                        continue;
                    }

                    for(int secondSegment = 0; secondSegment < secondPart.Length - 1; secondSegment++)
                    {
                        Point2d secondStart = secondVertices[secondPart.Start + secondSegment];
                        Point2d secondEnd = secondVertices[secondPart.Start + secondSegment + 1];

                        if(secondStart.X == secondEnd.X && secondStart.Y == secondEnd.Y)
                        {
                            continue;
                        }

                        if(!SegmentBoxesOverlap(firstStart, firstEnd, secondStart, secondEnd))
                        {
                            continue;
                        }

                        SegmentIntersection intersection = SegmentTopology.Classify(firstStart, firstEnd, secondStart, secondEnd);

                        if(intersection.Relation == SegmentRelation.Disjoint)
                        {
                            continue;
                        }

                        firstNoded[firstIndex] = true;
                        secondNoded[secondIndex] = true;
                        AddNode(nodes, intersection.FirstPoint, firstIndex, firstSegment, secondIndex, secondSegment);

                        if(intersection.Relation == SegmentRelation.CollinearOverlap)
                        {
                            AddNode(nodes, intersection.SecondPoint, firstIndex, firstSegment, secondIndex, secondSegment);
                        }
                    }
                }
            }
        }

        foreach(KeyValuePair<(double X, double Y), List<NodeSection>> node in nodes)
        {
            RelateNodeFan.Resolve(new Point2d(node.Key.X, node.Key.Y), node.Value, firstShape, secondShape, topology);
        }
    }

    /// <summary>Registers both operands' sections at one node coordinate.</summary>
    private static void AddNode(
        Dictionary<(double X, double Y), List<NodeSection>> nodes,
        Point2d point,
        int firstPartIndex,
        int firstSegmentIndex,
        int secondPartIndex,
        int secondSegmentIndex)
    {
        if(!nodes.TryGetValue((point.X, point.Y), out List<NodeSection>? sections))
        {
            sections = [];
            nodes[(point.X, point.Y)] = sections;
        }

        sections.Add(new NodeSection(IsFirst: true, firstPartIndex, firstSegmentIndex));
        sections.Add(new NodeSection(IsFirst: false, secondPartIndex, secondSegmentIndex));
    }

    /// <summary>Whether two parts' envelopes overlap or touch in the flat bounds tables.</summary>
    private static bool BoundsOverlap(double[] firstBounds, int firstIndex, double[] secondBounds, int secondIndex)
    {
        int firstOffset = firstIndex * 4;
        int secondOffset = secondIndex * 4;

        return firstBounds[firstOffset + 2] >= secondBounds[secondOffset]
            && secondBounds[secondOffset + 2] >= firstBounds[firstOffset]
            && firstBounds[firstOffset + 3] >= secondBounds[secondOffset + 1]
            && secondBounds[secondOffset + 3] >= firstBounds[firstOffset + 1];
    }

    /// <summary>Whether two segments' boxes overlap or touch.</summary>
    private static bool SegmentBoxesOverlap(Point2d firstStart, Point2d firstEnd, Point2d secondStart, Point2d secondEnd)
    {
        return Math.Max(firstStart.X, firstEnd.X) >= Math.Min(secondStart.X, secondEnd.X)
            && Math.Max(secondStart.X, secondEnd.X) >= Math.Min(firstStart.X, firstEnd.X)
            && Math.Max(firstStart.Y, firstEnd.Y) >= Math.Min(secondStart.Y, secondEnd.Y)
            && Math.Max(secondStart.Y, secondEnd.Y) >= Math.Min(firstStart.Y, firstEnd.Y);
    }

    /// <summary>Raises a cell in row-major or transposed orientation.</summary>
    private static void RaiseOriented(RelateTopology topology, PointPlacement shapePlacement, PointPlacement otherPlacement, int dimension, bool firstIsRows)
    {
        if(firstIsRows)
        {
            topology.Raise(shapePlacement, otherPlacement, dimension);
        }
        else
        {
            topology.Raise(otherPlacement, shapePlacement, dimension);
        }
    }

    /// <summary>Whether the part carries at least one positive-length segment.</summary>
    private static bool HasPositiveLengthSegment(ReadOnlySpan<Point2d> vertices, FlatGeometryPart part)
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

    /// <summary>
    /// Whether the text is a nine-character intersection pattern over the
    /// case-sensitive closed alphabet.
    /// </summary>
    private static bool IsValidPattern(ReadOnlySpan<char> pattern)
    {
        if(pattern.Length != 9)
        {
            return false;
        }

        foreach(char symbol in pattern)
        {
            if(symbol is not ('*' or 'T' or 'F' or '0' or '1' or '2'))
            {
                return false;
            }
        }

        return true;
    }
}
