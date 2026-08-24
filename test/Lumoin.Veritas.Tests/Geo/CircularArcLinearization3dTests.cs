using System;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial3D;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The three-dimensional arc linearization kernel pinned directly, below any codec:
/// the certified circle solve through the plane basis, the exact per-emission checks
/// re-verified through the same exact predicates — both bands, the chord stop, and the
/// once-per-arc center gate — the published constants, the seed verbatim guarantees in
/// all three ordinates, the split machinery with its membership invariant, the wall
/// and drift refusals with their outcomes and offending indexes on both kernel
/// entries, the planarity refusals on both their arms, and bit-for-bit determinism.
/// Refusal rows assert the outcome AND the offending seed index, and nothing random
/// participates — every row is deterministic.
/// </summary>
[TestClass]
internal sealed class CircularArcLinearization3dTests
{
    /// <summary>The canonical tilted circle's expected vertex count: gaps of one hundred and twenty-eight, one hundred and twenty-eight, and five hundred and twelve chords, half-open.</summary>
    private const int TiltedCircleVertexCount = 768;

    /// <summary>The tilted arc's expected vertex count: two gaps of two hundred and fifty-six chords, half-open.</summary>
    private const int TiltedArcVertexCount = 512;

    /// <summary>The tilted major arc's expected vertex count: a roughly one-hundred-and-ninety-nine-degree seed gap at five hundred and twelve chords plus a roughly forty-eight-degree gap at one hundred and twenty-eight, half-open.</summary>
    private const int MajorArcVertexCount = 640;

    /// <summary>The tilted near-full-turn arc's expected vertex count: one sliver gap at a single chord plus a nearly whole-turn gap at one thousand and twenty-four chords, half-open.</summary>
    private const int NearFullTurnVertexCount = 1025;

    /// <summary>The axis-plane sweep circle's expected vertex count: two quarter gaps at two hundred and fifty-six chords and one half-turn gap at five hundred and twelve, half-open.</summary>
    private const int AxisPlaneCircleVertexCount = 1024;

    /// <summary>The single scratch carrier of the class — single-owner state, created once and reused, exactly as a consuming parser would hold it.</summary>
    private static Orientation3dScratch Scratch { get; } = Orientation3dScratch.Create();

    /// <summary>The canonical tilted circle's first control point.</summary>
    private static Vector3d TiltedFirst { get; } = new(2.0, 0.25, 1.0);

    /// <summary>The canonical tilted circle's second control point.</summary>
    private static Vector3d TiltedSecond { get; } = new(0.5, 2.0, 0.25);

    /// <summary>The canonical tilted circle's third control point.</summary>
    private static Vector3d TiltedThird { get; } = new(-1.5, 0.75, -0.5);

    /// <summary>Materializes a builder's appended vertex run as a Z-carrying LineString for span inspection.</summary>
    private static FlatGeometry Materialize(FlatGeometryBuilder builder)
    {
        builder.AddPart(new FlatGeometryPart(0, builder.VertexCount, FlatGeometryPartRole.Line));
        builder.RootIndex = builder.AddNode(GeometryKind.LineString, hasZ: true, hasM: false, firstPart: 0, partCount: 1);

        return builder.ToGeometry();
    }

    /// <summary>Reads one emitted vertex back as a coordinate triple.</summary>
    private static Vector3d VertexAt(FlatGeometry geometry, int index)
    {
        return new Vector3d(geometry.Vertices[index].X, geometry.Vertices[index].Y, geometry.ZOrdinates[index]);
    }

    /// <summary>
    /// Asserts one emitted vertex matches the expected triple bit for bit in all
    /// three ordinates — the verbatim form that cannot be satisfied by a negative
    /// zero where a positive zero was promised, which value equality would allow.
    /// </summary>
    private static void AssertVertexBits(Vector3d expected, FlatGeometry geometry, int index, string what)
    {
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected.X), BitConverter.DoubleToInt64Bits(geometry.Vertices[index].X), $"{what} must match bit for bit on X.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected.Y), BitConverter.DoubleToInt64Bits(geometry.Vertices[index].Y), $"{what} must match bit for bit on Y.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected.Z), BitConverter.DoubleToInt64Bits(geometry.ZOrdinates[index]), $"{what} must match bit for bit on Z.");
    }

    /// <summary>
    /// The documented plane-basis solve, repeated here so the family can reason about
    /// the certified circle from outside the kernel: the construction-order normal
    /// from the edge differences, the basis seeded by crossing the normal with the
    /// ordinate axis of its smallest-magnitude component, every solve input a
    /// difference from the second control point projected onto the basis, the center
    /// placed back through the basis, and the radius as the single square root of the
    /// three-dimensional distance from the placed center to the anchor.
    /// </summary>
    private readonly record struct SolvedCircle(Vector3d Center, double Radius, Vector3d Normal, Vector3d BasisU, Vector3d BasisV)
    {
        /// <summary>Runs the documented construction for the given control points.</summary>
        public static SolvedCircle Of(Vector3d first, Vector3d second, Vector3d third)
        {
            Vector3d edgeSecond = Vector3d.Subtract(second, first);
            Vector3d edgeThird = Vector3d.Subtract(third, first);
            Vector3d normal = Vector3d.Cross(edgeSecond, edgeThird);
            double magnitudeX = Math.Abs(normal.X);
            double magnitudeY = Math.Abs(normal.Y);
            double magnitudeZ = Math.Abs(normal.Z);
            Vector3d axis = magnitudeX <= magnitudeY && magnitudeX <= magnitudeZ
                ? Vector3d.UnitX
                : (magnitudeY <= magnitudeZ ? Vector3d.UnitY : Vector3d.UnitZ);
            Vector3d seedDirection = Vector3d.Cross(normal, axis);
            double seedLength = seedDirection.Length();
            Vector3d basisU = new(seedDirection.X / seedLength, seedDirection.Y / seedLength, seedDirection.Z / seedLength);
            Vector3d crossDirection = Vector3d.Cross(normal, basisU);
            double crossLength = crossDirection.Length();
            Vector3d basisV = new(crossDirection.X / crossLength, crossDirection.Y / crossLength, crossDirection.Z / crossLength);
            Vector3d towardFirst = Vector3d.Subtract(first, second);
            Vector3d towardThird = Vector3d.Subtract(third, second);
            double towardFirstU = Vector3d.Dot(towardFirst, basisU);
            double towardFirstV = Vector3d.Dot(towardFirst, basisV);
            double towardThirdU = Vector3d.Dot(towardThird, basisU);
            double towardThirdV = Vector3d.Dot(towardThird, basisV);
            double towardFirstSquared = (towardFirstU * towardFirstU) + (towardFirstV * towardFirstV);
            double towardThirdSquared = (towardThirdU * towardThirdU) + (towardThirdV * towardThirdV);
            double cross = (towardFirstU * towardThirdV) - (towardFirstV * towardThirdU);
            double offsetU = ((towardThirdV * towardFirstSquared) - (towardFirstV * towardThirdSquared)) / (2.0 * cross);
            double offsetV = ((towardFirstU * towardThirdSquared) - (towardThirdU * towardFirstSquared)) / (2.0 * cross);
            Vector3d center = new(
                second.X + ((offsetU * basisU.X) + (offsetV * basisV.X)),
                second.Y + ((offsetU * basisU.Y) + (offsetV * basisV.Y)),
                second.Z + ((offsetU * basisU.Z) + (offsetV * basisV.Z)));
            double towardAnchorX = second.X - center.X;
            double towardAnchorY = second.Y - center.Y;
            double towardAnchorZ = second.Z - center.Z;
            double radius = Math.Sqrt(((towardAnchorX * towardAnchorX) + (towardAnchorY * towardAnchorY)) + (towardAnchorZ * towardAnchorZ));

            return new SolvedCircle(center, radius, normal, basisU, basisV);
        }
    }

    /// <summary>
    /// The canonical tilted circle linearized from its three control points matches
    /// the pinned polyline bit for bit, element-wise over every vertex in all three
    /// ordinates, with the subdivision count pinned.
    /// </summary>
    [TestMethod]
    public void TheTiltedCirclePolylineMatchesThePinnedBits()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeCircle(Scratch, TiltedFirst, TiltedSecond, TiltedThird, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsTrue(certified, "The canonical tilted circle must certify.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.Certified, outcome, "The outcome must be the success value.");
        Assert.AreEqual(-1, offendingSeedIndex, "A certified run names no offending seed.");

        using FlatGeometry circle = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = circle.Vertices;
        ReadOnlySpan<double> heights = circle.ZOrdinates;
        long[] expected = CircularArcLinearization3dFixtures.TiltedCircleVertexBits;
        int vertexCount = vertices.Length;
        Assert.AreEqual(TiltedCircleVertexCount, vertexCount, "The subdivision count is pinned.");
        Assert.HasCount(vertexCount * 3, expected, "The fixture carries one X, one Y, and one Z pattern per vertex.");

        for(int index = 0; index < vertices.Length; index++)
        {
            Assert.AreEqual(expected[3 * index], BitConverter.DoubleToInt64Bits(vertices[index].X), $"Vertex {index} X must match the pinned bits.");
            Assert.AreEqual(expected[(3 * index) + 1], BitConverter.DoubleToInt64Bits(vertices[index].Y), $"Vertex {index} Y must match the pinned bits.");
            Assert.AreEqual(expected[(3 * index) + 2], BitConverter.DoubleToInt64Bits(heights[index]), $"Vertex {index} Z must match the pinned bits.");
        }
    }

    /// <summary>
    /// The circle path's control points enter the output verbatim at their pinned run
    /// indexes, and the first control point closes the ring verbatim in all three
    /// ordinates — the closing vertex is the opening control point bit for bit.
    /// </summary>
    [TestMethod]
    public void TiltedCircleSeedsEmitVerbatimAndCloseTheRing()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeCircle(Scratch, TiltedFirst, TiltedSecond, TiltedThird, builder, out _, out _);
        Assert.IsTrue(certified, "The canonical tilted circle must certify.");

        using FlatGeometry circle = Materialize(builder);
        Assert.AreEqual(TiltedSecond, VertexAt(circle, 127), "The second control point closes the first gap verbatim.");
        Assert.AreEqual(TiltedThird, VertexAt(circle, 255), "The third control point closes the second gap verbatim.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(TiltedFirst.X), BitConverter.DoubleToInt64Bits(circle.Vertices[767].X), "The ring closes verbatim on X.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(TiltedFirst.Y), BitConverter.DoubleToInt64Bits(circle.Vertices[767].Y), "The ring closes verbatim on Y.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(TiltedFirst.Z), BitConverter.DoubleToInt64Bits(circle.ZOrdinates[767]), "The ring closes verbatim on Z.");
    }

    /// <summary>
    /// Every emitted vertex clears both exact bands, every emitted chord the exact
    /// sagitta check, the computed center its once-per-arc planarity gate, and the
    /// document seeds their exact-zero planarity — re-verified here through the same
    /// exact predicates against the documented comparison constructions, from outside
    /// the kernel.
    /// </summary>
    [TestMethod]
    public void EveryVertexPassesBothBandsAndEveryChordTheSagittaCheck()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeCircle(Scratch, TiltedFirst, TiltedSecond, TiltedThird, builder, out _, out _);
        Assert.IsTrue(certified, "The canonical tilted circle must certify.");

        using FlatGeometry circle = Materialize(builder);
        SolvedCircle solved = SolvedCircle.Of(TiltedFirst, TiltedSecond, TiltedThird);
        double comparisonRadius = Math.BitIncrement(solved.Radius * (1.0 - CircularArcLinearization3d.MaximumRelativeSagitta));
        double annulusInner = Math.BitDecrement(solved.Radius * (1.0 - CircularArcLinearization3d.MaximumRelativeVertexDrift));
        double annulusOuter = Math.BitIncrement(solved.Radius * (1.0 + CircularArcLinearization3d.MaximumRelativeVertexDrift));
        double planarBand = Math.BitIncrement(solved.Radius * CircularArcLinearization3d.MaximumRelativePlanarDrift);

        Assert.AreEqual(0, ExactOrientation3d.Sign(TiltedFirst, TiltedSecond, TiltedThird, TiltedFirst), "The first seed defines the plane and is exactly planar.");
        Assert.AreEqual(0, ExactOrientation3d.Sign(TiltedFirst, TiltedSecond, TiltedThird, TiltedSecond), "The second seed defines the plane and is exactly planar.");
        Assert.AreEqual(0, ExactOrientation3d.Sign(TiltedFirst, TiltedSecond, TiltedThird, TiltedThird), "The third seed defines the plane and is exactly planar.");
        Assert.IsLessThanOrEqualTo(0, ExactOrientation3d.PlaneBandComparisonSign(Scratch, TiltedFirst, TiltedSecond, TiltedThird, solved.Center, planarBand), "The computed center must clear the once-per-arc planarity gate.");

        Vector3d previous = TiltedFirst;

        for(int index = 0; index < circle.Vertices.Length; index++)
        {
            Vector3d vertex = VertexAt(circle, index);
            Assert.IsLessThanOrEqualTo(0, ExactSphereExcess.Sign(vertex, solved.Center, annulusOuter), $"Vertex {index} must sit at or inside the outer annulus radius.");
            Assert.IsGreaterThanOrEqualTo(0, ExactSphereExcess.Sign(vertex, solved.Center, annulusInner), $"Vertex {index} must sit at or outside the inner annulus radius.");
            Assert.IsLessThanOrEqualTo(0, ExactOrientation3d.PlaneBandComparisonSign(Scratch, TiltedFirst, TiltedSecond, TiltedThird, vertex, planarBand), $"Vertex {index} must sit at or inside the planarity band.");

            Vector3d midpoint = new((previous.X + vertex.X) / 2.0, (previous.Y + vertex.Y) / 2.0, (previous.Z + vertex.Z) / 2.0);
            Assert.IsGreaterThanOrEqualTo(0, ExactSphereExcess.Sign(midpoint, solved.Center, comparisonRadius), $"Chord {index} must clear the exact sagitta check.");
            previous = vertex;
        }
    }

    /// <summary>
    /// The published constants carry their pinned bit patterns, and their
    /// conservative one-bit adjustments land on the pinned unit-radius comparison
    /// values — the planar band adjusting upward like the outer annulus radius —
    /// asserted through runtime conversions the compiler cannot fold away.
    /// </summary>
    [TestMethod]
    public void PublishedConstantsCarryTheirPinnedValues()
    {
        Assert.AreEqual(4535124824762089472L, BitConverter.DoubleToInt64Bits(CircularArcLinearization3d.MaximumRelativeSagitta), "The sagitta bound is two to the negative sixteenth.");
        Assert.AreEqual(4517110426252607488L, BitConverter.DoubleToInt64Bits(CircularArcLinearization3d.MaximumRelativeVertexDrift), "The radial drift band is two to the negative twentieth.");
        Assert.AreEqual(4517110426252607488L, BitConverter.DoubleToInt64Bits(CircularArcLinearization3d.MaximumRelativePlanarDrift), "The planar drift band is two to the negative twentieth.");

        int bisectionDepth = CircularArcLinearization3d.MaximumBisectionDepth;
        Assert.AreEqual(16, bisectionDepth, "The bisection depth cap is sixteen.");
        Assert.AreEqual(4607182281361063937L, BitConverter.DoubleToInt64Bits(Math.BitIncrement(1.0 - CircularArcLinearization3d.MaximumRelativeSagitta)), "The unit-radius comparison radius rounds one bit upward.");
        Assert.AreEqual(4607182410210082815L, BitConverter.DoubleToInt64Bits(Math.BitDecrement(1.0 - CircularArcLinearization3d.MaximumRelativeVertexDrift)), "The unit-radius inner annulus radius rounds one bit downward.");
        Assert.AreEqual(4607182423094984705L, BitConverter.DoubleToInt64Bits(Math.BitIncrement(1.0 + CircularArcLinearization3d.MaximumRelativeVertexDrift)), "The unit-radius outer annulus radius rounds one bit upward.");
        Assert.AreEqual(4517110426252607489L, BitConverter.DoubleToInt64Bits(Math.BitIncrement(1.0 * CircularArcLinearization3d.MaximumRelativePlanarDrift)), "The unit-radius planar band rounds one bit upward — its underlying power-of-two product is exact, so the widening is uniformity and disclosure.");

        var frame = CircularArcLinearization3d.CircleFrame.Create(new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(-1.0, 0.0, 0.0), new Vector3d(0.0, 0.0, 0.0), 1.0, new Vector3d(0.0, 0.0, 2.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(-1.0, 0.0, 0.0));
        Assert.AreEqual(4607182281361063937L, BitConverter.DoubleToInt64Bits(frame.ComparisonRadius), "The frame's comparison radius carries the kernel's own upward adjustment at unit radius.");
        Assert.AreEqual(4607182410210082815L, BitConverter.DoubleToInt64Bits(frame.AnnulusInner), "The frame's inner annulus radius carries the kernel's own downward adjustment at unit radius.");
        Assert.AreEqual(4607182423094984705L, BitConverter.DoubleToInt64Bits(frame.AnnulusOuter), "The frame's outer annulus radius carries the kernel's own upward adjustment at unit radius.");
        Assert.AreEqual(4517110426252607489L, BitConverter.DoubleToInt64Bits(frame.PlanarBand), "The frame's planar band carries the kernel's own upward adjustment at unit radius.");
    }

    /// <summary>
    /// A three-point tilted arc's control points enter the output verbatim in all
    /// three ordinates: the middle and end seeds sit at their exact positions in the
    /// half-open run, bit-preserved, with the count pinned and an interior vertex
    /// bit-pinned at the anchor commit.
    /// </summary>
    [TestMethod]
    public void ArcControlPointsEnterTheOutputVerbatim()
    {
        Vector3d start = new(0.0, -1.0, 0.5);
        Vector3d middle = new(1.0, 0.0, 0.125);
        Vector3d end = new(-0.25, 1.0, 0.0);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out _, out _);
        Assert.IsTrue(certified, "The tilted arc must certify.");

        using FlatGeometry arc = Materialize(builder);
        int vertexCount = arc.Vertices.Length;
        Assert.AreEqual(TiltedArcVertexCount, vertexCount, "The half-open count is pinned.");
        AssertVertexBits(middle, arc, 255, "The middle control point ending the first gap verbatim");
        AssertVertexBits(end, arc, 511, "The end control point closing the run verbatim");
        Assert.AreEqual(4607180907704487264L, BitConverter.DoubleToInt64Bits(arc.Vertices[256].X), "The first interior vertex after the middle seed is bit-pinned on X.");
        Assert.AreEqual(4574634427409164932L, BitConverter.DoubleToInt64Bits(arc.Vertices[256].Y), "The first interior vertex after the middle seed is bit-pinned on Y.");
        Assert.AreEqual(4593541275874475983L, BitConverter.DoubleToInt64Bits(arc.ZOrdinates[256]), "The first interior vertex after the middle seed is bit-pinned on Z.");
    }

    /// <summary>
    /// An arc spanning roughly two hundred and forty-seven degrees in a tilted plane
    /// — its first seed gap of about one hundred and ninety-nine degrees beyond a
    /// half turn — certifies through the major-arc split without wrapping: the count
    /// is pinned, the end seed closes the run verbatim, and the certified center
    /// stays on the travel side of every emitted chord, seen along the plane normal.
    /// </summary>
    [TestMethod]
    public void TheMajorArcCertifiesWithoutWrapping()
    {
        Vector3d start = new(1.0, 0.0, 0.25);
        Vector3d middle = new(-0.9396926207859084, -0.3420201433256687, -0.32042819102789427);
        Vector3d end = new(-0.3420201433256688, -0.9396926207859083, -0.32042819102789427);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out _);
        Assert.IsTrue(certified, "The tilted major arc must certify.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.Certified, outcome, "The outcome must be the success value.");

        using FlatGeometry arc = Materialize(builder);
        int vertexCount = arc.Vertices.Length;
        Assert.AreEqual(MajorArcVertexCount, vertexCount, "The pinned count forbids a wrapped double cover.");
        Assert.AreEqual(end, VertexAt(arc, vertexCount - 1), "The end control point closes the run verbatim in all three ordinates.");

        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        Vector3d previous = start;

        for(int index = 0; index < arc.Vertices.Length; index++)
        {
            Vector3d vertex = VertexAt(arc, index);
            Assert.AreEqual(1, ExactOrientation3d.InPlaneSign(Scratch, start, middle, end, previous, vertex, solved.Center), $"Chord {index} must keep the certified center on the travel side — no backtracking, no wrap.");
            previous = vertex;
        }
    }

    /// <summary>
    /// An arc whose middle control point sits a sliver behind its start, in a tilted
    /// plane, leaving one seed gap spanning nearly the whole turn: the gap's own
    /// chord midpoint clears the sagitta comparison but keeps the center on the
    /// wrong side of the in-plane test, so only the exact minor-side gate forces the
    /// subdivision — the run certifies with the count pinned far beyond the half-arc
    /// count, and every emitted chord keeps the certified center on the travel side.
    /// </summary>
    [TestMethod]
    public void TheNearFullTurnGapSubdividesOnTheTravelSide()
    {
        Vector3d start = new(1.0, 0.0, 0.25);
        Vector3d middle = new(0.9999995000000417, -0.0009999998333333417, 0.2497498750416771);
        Vector3d end = new(0.9999980000006666, 0.0019999986666669333, 0.2504994996668334);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out _);
        Assert.IsTrue(certified, "The tilted near-full-turn arc must certify.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.Certified, outcome, "The outcome must be the success value.");

        using FlatGeometry arc = Materialize(builder);
        int vertexCount = arc.Vertices.Length;
        Assert.AreEqual(NearFullTurnVertexCount, vertexCount, "The pinned count covers the nearly whole turn.");
        Assert.IsGreaterThan(TiltedArcVertexCount, vertexCount, "The wide gap subdivides far beyond the half-arc count instead of collapsing to its chord.");
        Assert.AreEqual(middle, VertexAt(arc, 0), "The middle seed opens the run verbatim.");
        Assert.AreEqual(end, VertexAt(arc, vertexCount - 1), "The end control point closes the run verbatim in all three ordinates.");

        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        double comparisonRadius = Math.BitIncrement(solved.Radius * (1.0 - CircularArcLinearization3d.MaximumRelativeSagitta));
        Vector3d wideMidpoint = new((middle.X + end.X) / 2.0, (middle.Y + end.Y) / 2.0, (middle.Z + end.Z) / 2.0);
        Assert.AreNotEqual(1, ExactOrientation3d.InPlaneSign(Scratch, start, middle, end, middle, end, solved.Center), "The wide gap's chord keeps the center on the wrong side of the in-plane test.");
        Assert.IsGreaterThanOrEqualTo(0, ExactSphereExcess.Sign(wideMidpoint, solved.Center, comparisonRadius), "The wide gap's chord midpoint clears the sagitta comparison, so the minor-side gate alone forces the subdivision.");

        Vector3d previous = start;

        for(int index = 0; index < arc.Vertices.Length; index++)
        {
            Vector3d vertex = VertexAt(arc, index);
            Assert.AreEqual(1, ExactOrientation3d.InPlaneSign(Scratch, start, middle, end, previous, vertex, solved.Center), $"Chord {index} must keep the certified center on the travel side — the wide gap never ships as one chord.");
            previous = vertex;
        }
    }

    /// <summary>
    /// Degenerate control points refuse on the arc path with their outcome and
    /// offending index, and nothing is emitted before the offense — coincidence over
    /// all three ordinates, and exact collinearity both along an ordinate diagonal
    /// and in a plane no axis spans.
    /// </summary>
    [TestMethod]
    [DataRow(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 2.0, 0.0, 1.0, (int)CircularArcLinearization3dOutcome.CoincidentControlPoints, 1, DisplayName = "A start coinciding with the middle refuses at the middle.")]
    [DataRow(0.0, 0.0, 0.0, 2.0, 0.0, 1.0, 2.0, 0.0, 1.0, (int)CircularArcLinearization3dOutcome.CoincidentControlPoints, 2, DisplayName = "A middle coinciding with the end refuses at the end.")]
    [DataRow(0.0, 0.0, 0.0, 2.0, 0.0, 1.0, 0.0, 0.0, 0.0, (int)CircularArcLinearization3dOutcome.CoincidentControlPoints, 2, DisplayName = "An end coinciding with the start refuses at the end.")]
    [DataRow(0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 2.0, 2.0, 2.0, (int)CircularArcLinearization3dOutcome.CollinearControlPoints, 2, DisplayName = "Exactly collinear diagonal control points refuse at the third.")]
    [DataRow(0.0, 0.0, 0.0, 1.0, 1.0, 0.5, 2.0, 2.0, 1.0, (int)CircularArcLinearization3dOutcome.CollinearControlPoints, 2, DisplayName = "Exactly collinear tilted control points refuse at the third.")]
    public void DegenerateControlPointsRefuseWithTheirIndexes(double startX, double startY, double startZ, double middleX, double middleY, double middleZ, double endX, double endY, double endZ, int expectedOutcome, int expectedSeedIndex)
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(startX, startY, startZ), new Vector3d(middleX, middleY, middleZ), new Vector3d(endX, endY, endZ), builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "A degenerate triple must refuse.");
        Assert.AreEqual((CircularArcLinearization3dOutcome)expectedOutcome, outcome, "The outcome names the degeneracy.");
        Assert.AreEqual(expectedSeedIndex, offendingSeedIndex, "The offending control point is named.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The circle path shares the degeneracy refusals: coincident and exactly
    /// collinear control points refuse with their outcome and offending index, and
    /// nothing is emitted before the offense.
    /// </summary>
    [TestMethod]
    [DataRow(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 2.0, 0.0, 1.0, (int)CircularArcLinearization3dOutcome.CoincidentControlPoints, 1, DisplayName = "A first point coinciding with the second refuses at the second.")]
    [DataRow(0.0, 0.0, 0.0, 2.0, 0.0, 1.0, 2.0, 0.0, 1.0, (int)CircularArcLinearization3dOutcome.CoincidentControlPoints, 2, DisplayName = "A second point coinciding with the third refuses at the third.")]
    [DataRow(0.0, 0.0, 0.0, 2.0, 0.0, 1.0, 0.0, 0.0, 0.0, (int)CircularArcLinearization3dOutcome.CoincidentControlPoints, 2, DisplayName = "A third point coinciding with the first refuses at the third.")]
    [DataRow(0.0, 0.0, 0.0, 1.0, 1.0, 0.5, 2.0, 2.0, 1.0, (int)CircularArcLinearization3dOutcome.CollinearControlPoints, 2, DisplayName = "Exactly collinear circle control points refuse at the third.")]
    public void DegenerateCircleControlPointsRefuseWithTheirIndexes(double firstX, double firstY, double firstZ, double secondX, double secondY, double secondZ, double thirdX, double thirdY, double thirdZ, int expectedOutcome, int expectedSeedIndex)
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeCircle(Scratch, new Vector3d(firstX, firstY, firstZ), new Vector3d(secondX, secondY, secondZ), new Vector3d(thirdX, thirdY, thirdZ), builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "A degenerate triple must refuse on the circle path too.");
        Assert.AreEqual((CircularArcLinearization3dOutcome)expectedOutcome, outcome, "The outcome names the degeneracy.");
        Assert.AreEqual(expectedSeedIndex, offendingSeedIndex, "The offending control point is named.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The one degeneracy class the third dimension adds: exactly non-degenerate
    /// control points whose plain-double cross products all round to zero — every
    /// minor collapses to a tie the rounding cannot hold — poison the basis into
    /// values that are not numbers, and the acceptance-form walls refuse the computed
    /// garbage with no seed named. The exact collinearity test itself passes: the
    /// exact cross product is nonzero, which this row asserts through the exact
    /// orientation predicate before running the kernel.
    /// </summary>
    [TestMethod]
    public void RoundedProjectionCollapseRefusesAtTheComputedWall()
    {
        double stepTwentySeven = 1.0 + (1.0 / 134217728.0);
        double stepTwentySix = 1.0 + (1.0 / 67108864.0);
        Vector3d start = new(0.0, 0.0, 0.0);
        Vector3d middle = new(stepTwentySeven, stepTwentySeven, stepTwentySix);
        Vector3d end = new(1.0, 1.0, stepTwentySeven);
        Assert.AreNotEqual(0, ExactOrientation3d.InPlaneSign(Scratch, start, middle, end, start, middle, end), "The triple is exactly non-collinear — the collapse lives only in the rounding.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The collapsed construction must refuse.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, outcome, "The acceptance-form walls catch the non-finite computed values.");
        Assert.AreEqual(-1, offendingSeedIndex, "A computed-value wall names no seed.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The magnitude walls refuse input values and computed values alike, in
    /// acceptance form — a value that is not a number fails them too, and the third
    /// ordinate offends like the first two.
    /// </summary>
    [TestMethod]
    public void MagnitudeWallsRefuseInputAndComputedValues()
    {
        FlatGeometryBuilder inputBuilder = new();
        bool inputCertified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(0.0, -1.0, 1e46), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0), inputBuilder, out CircularArcLinearization3dOutcome inputOutcome, out int inputSeed);
        Assert.IsFalse(inputCertified, "An over-wall input Z ordinate must refuse.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, inputOutcome, "The wall outcome is named.");
        Assert.AreEqual(0, inputSeed, "The offending control point is the first.");
        Assert.AreEqual(0, inputBuilder.VertexCount, "Nothing is emitted before the offense.");

        FlatGeometryBuilder nanBuilder = new();
        bool nanCertified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(0.0, -1.0, 0.5), new Vector3d(1.0, 0.0, double.NaN), new Vector3d(0.0, 1.0, 0.0), nanBuilder, out CircularArcLinearization3dOutcome nanOutcome, out int nanSeed);
        Assert.IsFalse(nanCertified, "A Z ordinate that is not a number must refuse at the wall.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, nanOutcome, "The acceptance-form wall catches the value.");
        Assert.AreEqual(1, nanSeed, "The offending control point is the middle.");
        Assert.AreEqual(0, nanBuilder.VertexCount, "Nothing is emitted before the offense.");

        double bulk = Math.ScaleB(1.0, 100);
        double doubled = Math.ScaleB(1.0, 101);
        double ulpStep = Math.ScaleB(1.0, 49);
        FlatGeometryBuilder computedBuilder = new();
        bool computedCertified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(0.0, 0.0, 0.0), new Vector3d(bulk, bulk, bulk), new Vector3d(doubled, doubled, doubled + ulpStep), computedBuilder, out CircularArcLinearization3dOutcome computedOutcome, out int computedSeed);
        Assert.IsFalse(computedCertified, "A one-ulp sliver at in-wall magnitudes solves to a circle beyond the wall and must refuse at the computed values.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, computedOutcome, "The wall outcome covers computed values.");
        Assert.AreEqual(-1, computedSeed, "A computed-value wall names no seed.");
        Assert.AreEqual(0, computedBuilder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The computed wall deciding on the third ordinate alone: a shallow sagitta bump
    /// in a vertical plane solves to a center flying along Z — re-derived here
    /// through the documented solve as the in-test proof that the first two center
    /// ordinates are exactly zero and the radius stays inside the walls, so only the
    /// center's Z ordinate can refuse — and the kernel refuses it at the computed
    /// wall with no seed named and nothing emitted.
    /// </summary>
    [TestMethod]
    public void TheComputedCenterRefusesTheWallOnItsThirdOrdinate()
    {
        Vector3d start = new(-Math.ScaleB(1.0, 123), 0.0, Math.ScaleB(1.0, 149));
        Vector3d middle = new(0.0, 0.0, Math.ScaleB(1.0, 149) - Math.ScaleB(1.0, 97));
        Vector3d end = new(Math.ScaleB(1.0, 123), 0.0, Math.ScaleB(1.0, 149));
        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        Assert.AreEqual(0.0, solved.Center.X, "The solved center sits exactly on the bump's symmetry line.");
        Assert.AreEqual(0.0, solved.Center.Y, "The solved center stays exactly in the vertical plane.");
        Assert.IsGreaterThan(ExactOrientation3d.MaximumMagnitude, solved.Center.Z, "The solved center's third ordinate alone crosses the upper wall.");
        Assert.IsLessThanOrEqualTo(ExactOrientation3d.MaximumMagnitude, solved.Radius, "The solved radius stays inside the walls, so the refusal cannot ride the radius check.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The over-wall center ordinate must refuse.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, outcome, "The wall outcome covers the computed center's third ordinate.");
        Assert.AreEqual(-1, offendingSeedIndex, "A computed-value wall names no seed.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// A constructed split ordinate that cancels beneath the lower wall refuses at
    /// the wall mid-run: a tiny circle grazing the origin in a vertical plane keeps
    /// every input ordinate inside the walls — asserted here — and its solve exact,
    /// but the splits descending toward the origin-side seed construct third
    /// ordinates that shrink quadratically below the wall, where the planarity
    /// comparison's exactness window ends; the run refuses with the certified prefix
    /// intact and no seed named.
    /// </summary>
    [TestMethod]
    public void TheSubWallConstructedSplitRefusesAtTheWall()
    {
        Vector3d start = new(0.0, 0.0, Math.ScaleB(1.0, -93));
        Vector3d middle = new(0.0, Math.ScaleB(1.0, -94), Math.ScaleB(1.0, -94));
        Vector3d end = new(0.0, 0.0, 0.0);
        Assert.IsGreaterThanOrEqualTo(ExactOrientation3d.MinimumMagnitude, Math.ScaleB(1.0, -94), "Every nonzero input ordinate sits inside the walls, so the refusal cannot be an input wall.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The sub-wall constructed ordinate must refuse.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, outcome, "The wall outcome covers constructed split ordinates.");
        Assert.AreEqual(-1, offendingSeedIndex, "A constructed value names no seed.");
        Assert.AreEqual(510, builder.VertexCount, "The refusal is mid-run — the first gap and most of the second certify before a split cancels beneath the wall.");
    }

    /// <summary>
    /// An ordinate that is nonzero yet below the lower magnitude wall refuses at the
    /// wall with its offending control point named, and nothing is emitted — the
    /// sub-wall arm of the acceptance-form test, offending on Z seed by seed.
    /// </summary>
    [TestMethod]
    [DataRow(0, DisplayName = "A sub-wall start ordinate refuses at the first seed.")]
    [DataRow(1, DisplayName = "A sub-wall middle ordinate refuses at the middle seed.")]
    [DataRow(2, DisplayName = "A sub-wall end ordinate refuses at the end seed.")]
    public void SubWallOrdinatesRefuseAtTheirSeeds(int offendingSeed)
    {
        Span<Vector3d> seeds = [new Vector3d(0.0, -1.0, 0.5), new Vector3d(1.0, 0.0, 0.25), new Vector3d(0.0, 1.0, 0.0)];
        seeds[offendingSeed] = new Vector3d(seeds[offendingSeed].X, seeds[offendingSeed].Y, 1e-40);

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, seeds[0], seeds[1], seeds[2], builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "A sub-wall ordinate must refuse.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.MagnitudeWall, outcome, "The wall outcome covers the tiny magnitudes.");
        Assert.AreEqual(offendingSeed, offendingSeedIndex, "The offending control point is named.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// A tiny arc riding a coarse coordinate grid exposes the mis-solved circle
    /// through the document's own points, in the axis plane where the basis is exact
    /// and the gate cannot fire: the placed center rounds to the grid, displacing it
    /// radially far beyond the drift band, and the first seed fails its exact annulus
    /// check with nothing emitted.
    /// </summary>
    [TestMethod]
    public void TheCoarseGridArcRefusesSeedDrift()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(100000000.00000095, 100000000.0000003, 0.0), new Vector3d(100000000.0000009, 100000000.00000043, 0.0), new Vector3d(100000000.00000082, 100000000.00000057, 0.0), builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The mis-solved circle must refuse through its own seeds.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.VertexDrift, outcome, "The annulus check names the refusal.");
        Assert.AreEqual(0, offendingSeedIndex, "The document's first seed is named.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The same coarse-grid arc lifted into a plane tilted by a few last-place bits
    /// of Z: the center's coarse rounding lies almost entirely in the plane, so the
    /// once-per-arc gate passes — re-verified here through the documented solve —
    /// and the refusal still arrives radially at the first seed. The row witnesses
    /// the gate's blindness contrast: the gate pins the plane direction the seeds
    /// cannot see, while the seeds pin the radial error the gate cannot see.
    /// </summary>
    [TestMethod]
    public void TheFineTiltSeedDriftPassesTheGateFirst()
    {
        Vector3d start = new(100000000.00000095, 100000000.0000003, 0.25);
        Vector3d middle = new(100000000.0000009, 100000000.00000043, 0.25 + Math.ScaleB(1.0, -52));
        Vector3d end = new(100000000.00000082, 100000000.00000057, 0.25 + Math.ScaleB(1.0, -51));
        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        double planarBand = Math.BitIncrement(solved.Radius * CircularArcLinearization3d.MaximumRelativePlanarDrift);
        Assert.IsLessThanOrEqualTo(0, ExactOrientation3d.PlaneBandComparisonSign(Scratch, start, middle, end, solved.Center, planarBand), "The computed center clears the gate — the coarse rounding lies in the plane.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The mis-solved circle must refuse through its own seeds.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.VertexDrift, outcome, "The refusal is radial, past the gate.");
        Assert.AreEqual(0, offendingSeedIndex, "The document's first seed is named.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The constructed-vertex twin of the coarse-grid refusal, in three-point form
    /// because the exact-cardinal center-and-radius path does not exist here: seeds
    /// one unit off a center at two to the fortieth are exact doubles and the
    /// symmetric anchored solve reproduces the center and radius exactly, so all
    /// three seeds pass their annulus checks, and the first constructed split vertex
    /// is the one the grid cannot host inside the drift band: the run refuses with
    /// nothing emitted and no seed named.
    /// </summary>
    [TestMethod]
    public void TheCoarseGridSplitRefusesConstructedVertexDrift()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(1099511627777.0, 0.0, 0.0), new Vector3d(1099511627776.0, 1.0, 0.0), new Vector3d(1099511627775.0, 0.0, 0.0), builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "A unit radius on the two-to-the-fortieth grid must refuse at the first split.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.VertexDrift, outcome, "The annulus check names the refusal.");
        Assert.AreEqual(-1, offendingSeedIndex, "A constructed vertex names no seed.");
        Assert.AreEqual(0, builder.VertexCount, "The seeds pass and the half-open first gap refuses before any emission.");
    }

    /// <summary>
    /// The once-per-arc center gate refuses a center the plane cannot hold: a small
    /// circle in a plane tilted by one last-place bit of a large Z offset places its
    /// center off-plane by a slice of that coarse Z grid, beyond the planar band —
    /// re-verified here through the documented solve and the same exact predicate —
    /// and the arc refuses before any seed check, with nothing emitted and no seed
    /// named. The seeds themselves are exactly planar and exactly radial: only the
    /// gate sees this failure, which is the axis-blindness theorem the gate exists
    /// to close.
    /// </summary>
    [TestMethod]
    public void TheCenterGateRefusesPlanarDrift()
    {
        double zBase = 134217728.0;
        Vector3d start = new(0.0078125, 0.0, zBase);
        Vector3d middle = new(0.0, 0.0078125, zBase);
        Vector3d end = new(-0.0078125, 0.0, zBase + Math.ScaleB(1.0, -25));
        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        double planarBand = Math.BitIncrement(solved.Radius * CircularArcLinearization3d.MaximumRelativePlanarDrift);
        Assert.AreEqual(1, ExactOrientation3d.PlaneBandComparisonSign(Scratch, start, middle, end, solved.Center, planarBand), "The computed center sits strictly outside the planarity band — the gate's own comparison, re-verified.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The off-plane center must refuse at the gate.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.PlanarDrift, outcome, "The gate names the planarity refusal.");
        Assert.AreEqual(-1, offendingSeedIndex, "The computed center names no seed.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The constructed arm of the planarity refusal: doubling the same tilted
    /// circle's radius lands the computed center inside the band — the gate passes,
    /// re-verified here through the documented solve — and the first seed gap
    /// certifies two hundred and fifty-six vertices before a constructed split's Z
    /// snaps to the coarse grid beyond the band; the run refuses mid-flight with the
    /// emitted prefix intact and no seed named.
    /// </summary>
    [TestMethod]
    public void TheConstructedSplitRefusesPlanarDrift()
    {
        double zBase = 134217728.0;
        Vector3d start = new(0.015625, 0.0, zBase);
        Vector3d middle = new(0.0, 0.015625, zBase);
        Vector3d end = new(-0.015625, 0.0, zBase + Math.ScaleB(1.0, -25));
        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        double planarBand = Math.BitIncrement(solved.Radius * CircularArcLinearization3d.MaximumRelativePlanarDrift);
        Assert.IsLessThanOrEqualTo(0, ExactOrientation3d.PlaneBandComparisonSign(Scratch, start, middle, end, solved.Center, planarBand), "The computed center clears the gate at this radius.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The coarse Z grid must refuse a constructed vertex off the plane.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.PlanarDrift, outcome, "The planarity band names the refusal.");
        Assert.AreEqual(-1, offendingSeedIndex, "A constructed vertex names no seed.");
        Assert.AreEqual(256, builder.VertexCount, "The first gap certifies in full before the offending split — the refusal is mid-run, not at the gate.");
    }

    /// <summary>
    /// The membership refusal live: a micro-sliver arc whose control points sit a few
    /// last-place bits apart at six-figure magnitudes passes the exact degeneracy
    /// checks — the points are genuinely distinct and non-collinear — while the
    /// plain-double frame it induces is finite noise, so every split candidate fails
    /// the exact membership check on its own gap through all three constructions and
    /// the run refuses with no seed named. The triple was found by the instrumented
    /// differential fuzz and is pinned verbatim.
    /// </summary>
    [TestMethod]
    public void TheMicroSliverArcRefusesSplitMembership()
    {
        Vector3d start = new(207074.3324329851, 226281.77049105262, -72160.09744566967);
        Vector3d middle = new(207074.3324329847, 226281.7704910525, -72160.09744566854);
        Vector3d end = new(207074.33243298426, 226281.7704910524, -72160.09744566739);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "The micro-sliver arc must refuse.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.SplitMembership, outcome, "The exact membership check names the refusal.");
        Assert.AreEqual(-1, offendingSeedIndex, "A membership refusal names no seed.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// The gate's existence witnessed black-box: a sliver arc at a coarse Z offset
    /// whose two seed gaps would each certify as a single chord — proven in-test
    /// through the documented solve, the exact side test, and the exact sagitta
    /// comparison — so no split vertex would ever be constructed and no per-emission
    /// planarity check would ever run; only the once-per-arc gate stands between the
    /// off-plane center and a certified two-vertex output, and the kernel refuses at
    /// it with nothing emitted.
    /// </summary>
    [TestMethod]
    public void TheCenterGateAloneSeparatesTheSliverFromCertification()
    {
        double zBase = 134217728.0;
        double zStep = Math.ScaleB(1.0, -25);
        Vector3d start = new(0.0, 0.0, zBase + zStep);
        Vector3d middle = new(Math.ScaleB(1.0, -14), Math.ScaleB(1.0, -22), zBase + zStep);
        Vector3d end = new(Math.ScaleB(1.0, -13), 0.0, zBase + (2.0 * zStep));
        SolvedCircle solved = SolvedCircle.Of(start, middle, end);
        double comparisonRadius = Math.BitIncrement(solved.Radius * (1.0 - CircularArcLinearization3d.MaximumRelativeSagitta));
        double planarBand = Math.BitIncrement(solved.Radius * CircularArcLinearization3d.MaximumRelativePlanarDrift);
        Vector3d firstMidpoint = new((start.X + middle.X) / 2.0, (start.Y + middle.Y) / 2.0, (start.Z + middle.Z) / 2.0);
        Vector3d secondMidpoint = new((middle.X + end.X) / 2.0, (middle.Y + end.Y) / 2.0, (middle.Z + end.Z) / 2.0);
        Assert.AreEqual(1, ExactOrientation3d.InPlaneSign(Scratch, start, middle, end, start, middle, solved.Center), "The first seed gap is minor: the center sits on the travel side of its chord.");
        Assert.AreEqual(1, ExactOrientation3d.InPlaneSign(Scratch, start, middle, end, middle, end, solved.Center), "The second seed gap is minor: the center sits on the travel side of its chord.");
        Assert.IsGreaterThanOrEqualTo(0, ExactSphereExcess.Sign(firstMidpoint, solved.Center, comparisonRadius), "The first gap's chord midpoint clears the sagitta comparison, so the gap would emit as one chord.");
        Assert.IsGreaterThanOrEqualTo(0, ExactSphereExcess.Sign(secondMidpoint, solved.Center, comparisonRadius), "The second gap's chord midpoint clears the sagitta comparison, so the gap would emit as one chord.");
        Assert.AreEqual(1, ExactOrientation3d.PlaneBandComparisonSign(Scratch, start, middle, end, solved.Center, planarBand), "The computed center sits strictly outside the planarity band.");

        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, start, middle, end, builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "Without the gate this sliver would certify as its two far seeds; the gate must refuse it.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.PlanarDrift, outcome, "The gate names the planarity refusal.");
        Assert.AreEqual(-1, offendingSeedIndex, "The computed center names no seed.");
        Assert.AreEqual(0, builder.VertexCount, "Nothing is emitted before the offense.");
    }

    /// <summary>
    /// A near-collinear triple that passes the exact collinearity check certifies as
    /// a giant circle whose chords clear immediately: the output is exactly the two
    /// remaining seeds, verbatim — the published bound is relative to the certified
    /// radius, and this is the honest, recorded consequence, adjudicated afresh for
    /// the third dimension: the tilted basis conditions well enough here that the
    /// planar behavior carries over rather than being assumed.
    /// </summary>
    [TestMethod]
    public void TheGiantNearCollinearArcCertifiesAsItsSeeds()
    {
        double sliver = Math.ScaleB(1.0, -30);
        Vector3d middle = new(1.0, sliver, sliver);
        Vector3d end = new(2.0, 0.0, 0.0);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeArc(Scratch, new Vector3d(0.0, 0.0, 0.0), middle, end, builder, out CircularArcLinearization3dOutcome outcome, out _);
        Assert.IsTrue(certified, "The giant circle must certify.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.Certified, outcome, "The outcome must be the success value.");

        using FlatGeometry arc = Materialize(builder);
        int vertexCount = arc.Vertices.Length;
        Assert.AreEqual(2, vertexCount, "Both gaps clear at once, emitting only the far seeds.");
        AssertVertexBits(middle, arc, 0, "The middle seed emitted verbatim");
        AssertVertexBits(end, arc, 1, "The end seed emitted verbatim");
    }

    /// <summary>
    /// The split construction keys the diametral case on the exact side test and
    /// pins the in-plane perpendicular — the cross of the plane normal with the
    /// chord: with the chord through the center the midpoint direction is unusable,
    /// the first perpendicular sign fails the exact membership check, and the second
    /// lands the split on the gap's own sub-arc — both control-point orders
    /// distinguished, because the travel content lives in the construction-order
    /// normal, and the off-center chord-through-center case resolved identically.
    /// </summary>
    [TestMethod]
    public void TheDiametralSplitKeysOnTheSideTestAndPinsThePerpendicular()
    {
        Vector3d first = new(1.0, 0.0, 0.0);
        Vector3d second = new(0.0, 1.0, 0.0);
        Vector3d third = new(-1.0, 0.0, 0.0);
        var frame = CircularArcLinearization3d.CircleFrame.Create(first, second, third, new Vector3d(0.0, 0.0, 0.0), 1.0, new Vector3d(0.0, 0.0, 2.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(-1.0, 0.0, 0.0));
        bool forward = CircularArcLinearization3d.TryConstructSplit(Scratch, first, third, 0, in frame, out Vector3d forwardSplit);
        Assert.IsTrue(forward, "The diametral gap must split.");
        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(forwardSplit.X), "Forward control-point order splits through the upper half — the X ordinate carries the positive zero pattern.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(1.0), BitConverter.DoubleToInt64Bits(forwardSplit.Y), "Forward control-point order splits through the upper half bit for bit on Y.");
        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(forwardSplit.Z), "The forward split stays exactly in the axis plane.");

        Vector3d reversedFirst = new(-1.0, 0.0, 0.0);
        Vector3d reversedThird = new(1.0, 0.0, 0.0);
        var reversedFrame = CircularArcLinearization3d.CircleFrame.Create(reversedFirst, second, reversedThird, new Vector3d(0.0, 0.0, 0.0), 1.0, new Vector3d(0.0, 0.0, -2.0), new Vector3d(0.0, -1.0, 0.0), new Vector3d(-1.0, 0.0, 0.0));
        bool reversed = CircularArcLinearization3d.TryConstructSplit(Scratch, reversedThird, reversedFirst, 0, in reversedFrame, out Vector3d reversedSplit);
        Assert.IsTrue(reversed, "The diametral gap must split under the reversed order too.");
        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(reversedSplit.X), "Reversed control-point order splits through the lower half — the X ordinate carries the positive zero pattern.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-1.0), BitConverter.DoubleToInt64Bits(reversedSplit.Y), "Reversed control-point order splits through the lower half bit for bit on Y.");
        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(reversedSplit.Z), "The reversed split stays exactly in the axis plane.");

        var offCenterFrame = CircularArcLinearization3d.CircleFrame.Create(first, second, third, new Vector3d(0.25, 0.0, 0.0), 1.0, new Vector3d(0.0, 0.0, 2.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(-1.0, 0.0, 0.0));
        bool offCenter = CircularArcLinearization3d.TryConstructSplit(Scratch, first, third, 0, in offCenterFrame, out Vector3d offCenterSplit);
        Assert.IsTrue(offCenter, "A chord through the center line with an off-chord-midpoint center must split.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(0.25), BitConverter.DoubleToInt64Bits(offCenterSplit.X), "The split sits on the certified circle above the chord bit for bit on X.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(1.0), BitConverter.DoubleToInt64Bits(offCenterSplit.Y), "The split sits on the certified circle above the chord bit for bit on Y.");
        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(offCenterSplit.Z), "The off-center split stays exactly in the axis plane.");
    }

    /// <summary>
    /// A minor gap splits at the midpoint direction, placed through the plane basis,
    /// and the split passes the exact membership check on the gap's own sub-arc; the
    /// quarter split is symmetric, so its first two ordinates carry identical bits
    /// and its third stays exactly zero.
    /// </summary>
    [TestMethod]
    public void TheMinorSplitTakesTheMidpointDirection()
    {
        Vector3d first = new(1.0, 0.0, 0.0);
        Vector3d second = new(0.0, 1.0, 0.0);
        Vector3d third = new(-1.0, 0.0, 0.0);
        var frame = CircularArcLinearization3d.CircleFrame.Create(first, second, third, new Vector3d(0.0, 0.0, 0.0), 1.0, new Vector3d(0.0, 0.0, 2.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(-1.0, 0.0, 0.0));
        int side = ExactOrientation3d.InPlaneSign(Scratch, first, second, third, first, second, new Vector3d(0.0, 0.0, 0.0));
        Assert.AreEqual(1, side, "The quarter gap is minor: the center sits on the travel side.");

        bool split = CircularArcLinearization3d.TryConstructSplit(Scratch, first, second, side, in frame, out Vector3d vertex);
        Assert.IsTrue(split, "The quarter gap must split.");
        Assert.AreEqual(-1, ExactOrientation3d.InPlaneSign(Scratch, first, second, third, first, second, vertex), "The split vertex lies on the gap's own sub-arc.");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(vertex.X), BitConverter.DoubleToInt64Bits(vertex.Y), "The quarter split is symmetric, so its planar ordinates carry identical bits.");
        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(vertex.Z), "The split stays exactly in the axis plane.");
    }

    /// <summary>Two runs over the same input emit bit-identical polylines in all three ordinates — the arithmetic is correctly rounded only, so determinism is a theorem, and this row is its witness.</summary>
    [TestMethod]
    public void TwinRunsEmitIdenticalBits()
    {
        FlatGeometryBuilder firstBuilder = new();
        FlatGeometryBuilder secondBuilder = new();
        Assert.IsTrue(CircularArcLinearization3d.TryLinearizeCircle(Scratch, TiltedFirst, TiltedSecond, TiltedThird, firstBuilder, out _, out _), "The first run must certify.");
        Assert.IsTrue(CircularArcLinearization3d.TryLinearizeCircle(Scratch, TiltedFirst, TiltedSecond, TiltedThird, secondBuilder, out _, out _), "The second run must certify.");

        using FlatGeometry first = Materialize(firstBuilder);
        using FlatGeometry second = Materialize(secondBuilder);
        int firstCount = first.Vertices.Length;
        int secondCount = second.Vertices.Length;
        Assert.AreEqual(firstCount, secondCount, "The runs agree on the vertex count.");

        for(int index = 0; index < first.Vertices.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first.Vertices[index].X), BitConverter.DoubleToInt64Bits(second.Vertices[index].X), $"Vertex {index} X must agree bit for bit.");
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first.Vertices[index].Y), BitConverter.DoubleToInt64Bits(second.Vertices[index].Y), $"Vertex {index} Y must agree bit for bit.");
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first.ZOrdinates[index]), BitConverter.DoubleToInt64Bits(second.ZOrdinates[index]), $"Vertex {index} Z must agree bit for bit.");
        }
    }

    /// <summary>
    /// The plane-degeneracy sweep: a circle lying exactly in the XY plane runs
    /// through the three-dimensional kernel and certifies against this tranche's own
    /// pins — the axis-plane basis is exact, every emitted Z carries the positive
    /// zero bit pattern, and the ring closes verbatim. No identity with the planar
    /// kernel's output is claimed or owed: the two kernels are independent, each
    /// certified against its own published bands.
    /// </summary>
    [TestMethod]
    public void TheAxisPlaneCircleSweepsThroughTheThreeDimensionalKernel()
    {
        Vector3d first = new(1.0, 0.0, 0.0);
        Vector3d second = new(0.0, 1.0, 0.0);
        Vector3d third = new(-1.0, 0.0, 0.0);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization3d.TryLinearizeCircle(Scratch, first, second, third, builder, out CircularArcLinearization3dOutcome outcome, out _);
        Assert.IsTrue(certified, "The axis-plane circle must certify through the three-dimensional kernel.");
        Assert.AreEqual(CircularArcLinearization3dOutcome.Certified, outcome, "The outcome must be the success value.");

        using FlatGeometry circle = Materialize(builder);
        int vertexCount = circle.Vertices.Length;
        Assert.AreEqual(AxisPlaneCircleVertexCount, vertexCount, "The subdivision count is pinned.");
        AssertVertexBits(second, circle, 255, "The second control point closing the first quarter gap verbatim");
        AssertVertexBits(third, circle, 511, "The third control point closing the second quarter gap verbatim");
        AssertVertexBits(first, circle, 1023, "The first control point closing the ring verbatim");
        Assert.AreEqual(4607182249242036883L, BitConverter.DoubleToInt64Bits(circle.Vertices[0].X), "The first emitted vertex is bit-pinned on X.");
        Assert.AreEqual(4573724215515480178L, BitConverter.DoubleToInt64Bits(circle.Vertices[0].Y), "The first emitted vertex is bit-pinned on Y.");
        Assert.AreEqual(-4616189787612738925L, BitConverter.DoubleToInt64Bits(circle.Vertices[512].X), "The half-turn vertex is bit-pinned on X.");
        Assert.AreEqual(-4649647821339295630L, BitConverter.DoubleToInt64Bits(circle.Vertices[512].Y), "The half-turn vertex is bit-pinned on Y.");

        for(int index = 0; index < circle.ZOrdinates.Length; index++)
        {
            Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(circle.ZOrdinates[index]), $"Vertex {index} Z must carry the positive zero bit pattern.");
        }
    }
}
