using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Lattice;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// The Hilbert-curve segment and traversal orientation a quintant index maps to on a given
/// <see cref="Origin"/> face (see <see cref="Origins.QuintantToSegment"/>).
/// </summary>
internal readonly record struct QuintantSegment(int Segment, Orientation Orientation);

/// <summary>
/// The quintant index and traversal orientation a Hilbert-curve segment maps to on a given
/// <see cref="Origin"/> face (see <see cref="Origins.SegmentToQuintant"/>): the inverse of
/// <see cref="QuintantSegment"/>.
/// </summary>
internal readonly record struct SegmentQuintant(int Quintant, Orientation Orientation);

/// <summary>
/// The twelve dodecahedron-face origins the A5 grid is built on, plus the lookup and distance
/// functions defined over them. The table is built exactly once, by a two-phase procedure: the twelve
/// origins are first generated in a fixed geometric order (indexing the per-face layout and
/// first-quintant tables by that pre-sort geometric position), then re-sorted into Hilbert-curve
/// traversal order — at which point ONLY the <see cref="Origin.Id"/> field is relabeled; every origin's
/// layout and first-quintant stay tied to its original geometric identity, not its new position.
/// </summary>
internal static class Origins
{
    /// <summary>Orientation sequence for <see cref="QuintantLayout.ClockwiseFan"/>.</summary>
    private static Orientation[] ClockwiseFanOrientations { get; } =
        [Orientation.VU, Orientation.UW, Orientation.VW, Orientation.VW, Orientation.VW];

    /// <summary>Orientation sequence for <see cref="QuintantLayout.ClockwiseStep"/>.</summary>
    private static Orientation[] ClockwiseStepOrientations { get; } =
        [Orientation.WU, Orientation.UW, Orientation.VW, Orientation.VU, Orientation.UW];

    /// <summary>Orientation sequence for <see cref="QuintantLayout.CounterStep"/>.</summary>
    private static Orientation[] CounterStepOrientations { get; } =
        [Orientation.WU, Orientation.UV, Orientation.WV, Orientation.WU, Orientation.UW];

    /// <summary>Orientation sequence for <see cref="QuintantLayout.CounterJump"/>.</summary>
    private static Orientation[] CounterJumpOrientations { get; } =
        [Orientation.VU, Orientation.UV, Orientation.WV, Orientation.WU, Orientation.UW];

    /// <summary>
    /// The layout each of the twelve faces uses, indexed by pre-sort geometric id (0 = north pole,
    /// 1-10 = the two equatorial rings in generation order, 11 = south pole). Transcribed verbatim,
    /// including the geographic-region comments below.
    /// </summary>
    private static QuintantLayout[] LayoutByGeometricId { get; } =
    [
        QuintantLayout.ClockwiseFan, // 0 Arctic
        QuintantLayout.CounterJump, // 1 North America
        QuintantLayout.CounterStep, // 2 South America
        QuintantLayout.ClockwiseStep, // 3 North Atlantic & Western Europe & Africa
        QuintantLayout.CounterStep, // 4 South Atlantic & Africa
        QuintantLayout.CounterJump, // 5 Europe, Middle East & Central Africa
        QuintantLayout.CounterStep, // 6 Indian Ocean
        QuintantLayout.ClockwiseStep, // 7 Asia
        QuintantLayout.ClockwiseStep, // 8 Australia
        QuintantLayout.ClockwiseStep, // 9 North Pacific
        QuintantLayout.CounterJump, // 10 South Pacific
        QuintantLayout.CounterJump, // 11 Antarctic
    ];

    /// <summary>The index of the first quintant on each face, indexed by pre-sort geometric id.</summary>
    private static int[] FirstQuintantByGeometricId { get; } = [4, 2, 3, 2, 0, 4, 3, 2, 2, 0, 3, 0];

    /// <summary>
    /// The placement of the twelve geometric-id origins along the Hilbert curve: position <c>i</c> in
    /// this table holds the geometric id of the origin that becomes id <c>i</c> after the re-sort.
    /// </summary>
    private static int[] OriginOrder { get; } = [0, 1, 2, 4, 3, 5, 7, 8, 6, 11, 10, 9];

    /// <summary>
    /// The twelve origins, in Hilbert-curve traversal order (final <see cref="Origin.Id"/> 0 through
    /// 11). Built once; the array and every element are immutable thereafter.
    /// </summary>
    public static readonly Origin[] All = Build();

    /// <summary>
    /// Converts a quintant index (0-4) on <paramref name="origin"/>'s face to its Hilbert-curve
    /// segment index and traversal orientation.
    /// </summary>
    public static QuintantSegment QuintantToSegment(int quintant, Origin origin)
    {
        Orientation[] layout = OrientationsForLayout(origin.Layout);
        int step = StepFor(origin.Layout);

        // Find the (counter-clockwise) delta from the face's first quintant.
        int delta = (quintant - origin.FirstQuintant + 5) % 5;

        // Looking up the orientation needs clockwise/counter-clockwise counting.
        int faceRelativeQuintant = ((step * delta) + 5) % 5;
        Orientation orientation = layout[faceRelativeQuintant];
        int segment = (origin.FirstQuintant + faceRelativeQuintant) % 5;

        return new QuintantSegment(segment, orientation);
    }

    /// <summary>
    /// Converts a Hilbert-curve segment index on <paramref name="origin"/>'s face to its quintant
    /// index (0-4) and traversal orientation — the inverse of <see cref="QuintantToSegment"/>.
    /// </summary>
    public static SegmentQuintant SegmentToQuintant(int segment, Origin origin)
    {
        Orientation[] layout = OrientationsForLayout(origin.Layout);
        int step = StepFor(origin.Layout);

        int faceRelativeQuintant = (segment - origin.FirstQuintant + 5) % 5;
        Orientation orientation = layout[faceRelativeQuintant];
        int quintant = (origin.FirstQuintant + (step * faceRelativeQuintant) + 5) % 5;

        return new SegmentQuintant(quintant, orientation);
    }

    /// <summary>Finds the origin whose axis is nearest <paramref name="point"/> by <see cref="Haversine"/> distance.</summary>
    public static Origin FindNearestOrigin(Spherical point)
    {
        double minimumDistance = double.PositiveInfinity;
        Origin nearest = All[0];
        foreach(Origin origin in All)
        {
            double distance = Haversine(point, origin.Axis);
            if(distance < minimumDistance)
            {
                minimumDistance = distance;
                nearest = origin;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Tests whether <paramref name="origin"/> is the FARTHEST origin from <paramref name="point"/>
    /// rather than the nearest, despite the name: the <c>0.49999999</c> threshold is a near-antipodal
    /// test.
    /// </summary>
    public static bool IsNearestOrigin(Spherical point, Origin origin)
    {
        return Haversine(point, origin.Axis) > 0.49999999;
    }

    /// <summary>
    /// Finds the origin whose axis is nearest <paramref name="point"/>, taking a Cartesian unit
    /// vector directly. The arg-min of <c>1 − a·b</c> matches the arg-min of <see cref="Haversine"/>,
    /// so this returns the same origin as <see cref="FindNearestOrigin"/> without any spherical-
    /// trigonometry conversions.
    /// </summary>
    public static Origin FindNearestOriginCartesian(Cartesian point)
    {
        double minimumDistance = double.PositiveInfinity;
        Origin nearest = All[0];
        foreach(Origin origin in All)
        {
            Cartesian axis = origin.AxisCartesian;
            double distance = 1 - ((point.X * axis.X) + (point.Y * axis.Y) + (point.Z * axis.Z));
            if(distance < minimumDistance)
            {
                minimumDistance = distance;
                nearest = origin;
            }
        }

        return nearest;
    }

    /// <summary>
    /// A non-standard surrogate great-circle distance formula (not the textbook haversine), needed
    /// only as a monotonic proxy for angular separation between <paramref name="point"/> and
    /// <paramref name="axis"/> — kept exactly as specified rather than replaced with a textbook formula.
    /// </summary>
    public static double Haversine(Spherical point, Spherical axis)
    {
        double deltaTheta = axis.Theta - point.Theta;
        double deltaPhi = axis.Phi - point.Phi;
        double a1 = Math.Sin(deltaPhi / 2);
        double a2 = Math.Sin(deltaTheta / 2);

        return (a1 * a1) + (a2 * a2 * Math.Sin(point.Phi) * Math.Sin(axis.Phi));
    }

    /// <summary>Returns the orientation sequence for a quintant layout.</summary>
    public static Orientation[] OrientationsForLayout(QuintantLayout layout)
    {
        return layout switch
        {
            QuintantLayout.ClockwiseFan => ClockwiseFanOrientations,
            QuintantLayout.ClockwiseStep => ClockwiseStepOrientations,
            QuintantLayout.CounterStep => CounterStepOrientations,
            QuintantLayout.CounterJump => CounterJumpOrientations,
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown quintant layout."),
        };
    }

    /// <summary>
    /// Returns the quintant-counting direction for a layout: clockwise layouts count backward (-1),
    /// counter-clockwise layouts count forward (1).
    /// </summary>
    private static int StepFor(QuintantLayout layout)
    {
        return layout is QuintantLayout.ClockwiseFan or QuintantLayout.ClockwiseStep ? -1 : 1;
    }

    /// <summary>
    /// Runs the two-phase construction procedure once: generate the twelve origins in fixed geometric
    /// order, then re-sort into Hilbert-curve traversal order and relabel only the id field.
    /// </summary>
    private static Origin[] Build()
    {
        Origin[] geometricOrder = new Origin[12];

        // North pole (geometric id 0).
        geometricOrder[0] = CreateOrigin(0, new Spherical(0, 0), 0, DodecahedronQuaternions.Quaternions[0]);

        // The two equatorial rings of five (geometric ids 1 through 10).
        for(int ringPosition = 0; ringPosition < 5; ringPosition++)
        {
            double alpha = ringPosition * Constants.TwoPiOver5;
            double alpha2 = alpha + Constants.PiOver5;

            int ring1GeometricId = 1 + (2 * ringPosition);
            int ring2GeometricId = 2 + (2 * ringPosition);

            geometricOrder[ring1GeometricId] = CreateOrigin(
                ring1GeometricId,
                new Spherical(alpha, Constants.InterhedralAngle),
                Constants.PiOver5,
                DodecahedronQuaternions.Quaternions[ringPosition + 1]);

            geometricOrder[ring2GeometricId] = CreateOrigin(
                ring2GeometricId,
                new Spherical(alpha2, Math.PI - Constants.InterhedralAngle),
                Constants.PiOver5,
                DodecahedronQuaternions.Quaternions[((ringPosition + 3) % 5) + 6]);
        }

        // South pole (geometric id 11).
        geometricOrder[11] = CreateOrigin(11, new Spherical(0, Math.PI), 0, DodecahedronQuaternions.Quaternions[11]);

        // Re-sort into Hilbert-curve traversal order and relabel only the id field.
        Origin[] hilbertOrder = new Origin[12];
        for(int index = 0; index < OriginOrder.Length; index++)
        {
            hilbertOrder[index] = geometricOrder[OriginOrder[index]] with { Id = index };
        }

        return hilbertOrder;
    }

    /// <summary>Builds a single origin at its pre-sort geometric position.</summary>
    private static Origin CreateOrigin(int geometricId, Spherical axis, double angle, QuaternionD quaternion)
    {
        QuaternionD inverseQuaternion = QuaternionD.Conjugate(quaternion);

        return new Origin(
            geometricId,
            axis,
            CoordinateTransforms.ToCartesian(axis),
            quaternion,
            inverseQuaternion,
            angle,
            LayoutByGeometricId[geometricId],
            FirstQuintantByGeometricId[geometricId]);
    }
}
