using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// The core geometric layout of the A5 tiling pentagon: its five vertices in <see cref="Face"/>
/// coordinates, the equivalent 3-vertex triangle spanning a single quintant, and the basis matrix
/// mapping <see cref="IJ"/> lattice coordinates onto that triangle. Every literal below is transcribed
/// exactly. The five vertices are mutated in a specific order — scaled, then rotated — inside one
/// builder method, BEFORE <see cref="AngleV"/>'s underlying bisector angle is computed from the
/// already-mutated <c>c</c> vertex: reordering this sequence would silently corrupt the whole
/// downstream basis.
/// </summary>
internal static class PentagonConstants
{
    /// <summary>Pentagon interior angle at vertex a-b-c, in degrees.</summary>
    public const double AngleA = 72;

    /// <summary>Pentagon interior angle at vertex b-c-d, in degrees.</summary>
    public const double AngleB = 127.94543761193603;

    /// <summary>Pentagon interior angle at vertex c-d-e, in degrees.</summary>
    public const double AngleC = 108;

    /// <summary>Pentagon interior angle at vertex d-e-a, in degrees.</summary>
    public const double AngleD = 82.29202980963508;

    /// <summary>Pentagon interior angle at vertex e-a-b, in degrees.</summary>
    public const double AngleE = 149.7625318412527;

    /// <summary>Every value below, computed once by <see cref="Build"/> in a fixed operation order.</summary>
    private static Layout Computed { get; } = Build();

    /// <summary>Pentagon vertex a, after scaling and rotation: the tiling origin, always <c>(0, 0)</c>.</summary>
    public static Face VertexA => Computed.A;

    /// <summary>Pentagon vertex b, after scaling and rotation.</summary>
    public static Face VertexB => Computed.B;

    /// <summary>Pentagon vertex c, after scaling and rotation.</summary>
    public static Face VertexC => Computed.C;

    /// <summary>Pentagon vertex d, after scaling and rotation.</summary>
    public static Face VertexD => Computed.D;

    /// <summary>Pentagon vertex e, after scaling and rotation.</summary>
    public static Face VertexE => Computed.E;

    /// <summary>
    /// The pentagon used to tile the plane: not equilateral, but tiles with five-fold rotational
    /// symmetry into a regular pentagon. Callers that mutate a working copy call
    /// <see cref="PentagonShape.Clone"/> first.
    /// </summary>
    public static PentagonShape Pentagon => Computed.Pentagon;

    /// <summary>Triangle vertex u: the tiling origin, always <c>(0, 0)</c>.</summary>
    public static Face VertexU => Computed.U;

    /// <summary>Triangle vertex v.</summary>
    public static Face VertexV => Computed.V;

    /// <summary>Triangle vertex w.</summary>
    public static Face VertexW => Computed.W;

    /// <summary>The angle, in radians, locating vertex <see cref="VertexV"/>: <c>bisectorAngle + π/5</c>.</summary>
    public static double AngleV => Computed.AngleVRadians;

    /// <summary>The triangle <c>u, v, w</c>: one-fifth of <see cref="Pentagon"/>, used to describe a single quintant.</summary>
    public static PentagonShape Triangle => Computed.Triangle;

    /// <summary>
    /// Basis matrix mapping <see cref="IJ"/> lattice coordinates to <see cref="Face"/> coordinates:
    /// column-major, with <see cref="VertexV"/> as the first column and <see cref="VertexW"/> as the
    /// second.
    /// </summary>
    public static Matrix2x2d Basis => Computed.Basis;

    /// <summary>The inverse of <see cref="Basis"/>, mapping <see cref="Face"/> coordinates back to <see cref="IJ"/>.</summary>
    public static Matrix2x2d BasisInverse => Computed.BasisInverse;

    /// <summary>Scales a vertex by <paramref name="scale"/>, then rotates the result about the origin by <paramref name="radians"/>.</summary>
    private static Face ScaleThenRotate(Face vertex, double scale, double radians)
    {
        Vector2d scaled = CoordinateConversions.ToVector2d(vertex) * scale;
        Vector2d rotated = scaled.RotateAround(new Vector2d(0, 0), radians);

        return CoordinateConversions.ToFace(rotated);
    }

    /// <summary>
    /// Builds every computed value in one pass, in a fixed operation order: the
    /// scale-then-rotate vertex mutation, then the bisector-angle computation that depends on its
    /// result, then the triangle and basis matrix.
    /// </summary>
    private static Layout Build()
    {
        Face rawA = new(0, 0);
        Face rawB = new(0, 1);

        // c and d are calculated by circle intersections; transcribed digit-for-digit.
        Face rawC = new(0.7885966681787006, 1.6149108024237764);
        Face rawD = new(1.6171013659387945, 1.054928690397459);
        Face rawE = new(Math.Cos(Constants.PiOver10), Math.Sin(Constants.PiOver10));

        Vector2d rawCVector = CoordinateConversions.ToVector2d(rawC);

        // Distance to the edge midpoint, computed from the RAW (pre-mutation) c vertex.
        double edgeMidpointDistance = 2 * rawCVector.Length() * Math.Cos(Constants.PiOver5);

        // The lattice growth direction is AC; rotate it parallel to the x-axis (also from the raw c).
        double basisRotation = Constants.PiOver5 - Math.Atan2(rawCVector.Y, rawCVector.X);

        // Scale to match the unit sphere.
        double scale = (2 * Constants.DistanceToEdge) / edgeMidpointDistance;

        // Mutate every vertex: scale THEN rotate, in that order. bisectorAngle below depends on the
        // already-mutated c.
        Face a = ScaleThenRotate(rawA, scale, basisRotation);
        Face b = ScaleThenRotate(rawB, scale, basisRotation);
        Face c = ScaleThenRotate(rawC, scale, basisRotation);
        Face d = ScaleThenRotate(rawD, scale, basisRotation);
        Face e = ScaleThenRotate(rawE, scale, basisRotation);

        PentagonShape pentagon = new([a, b, c, d, e]);

        double bisectorAngle = Math.Atan2(c.Y, c.X) - Constants.PiOver5;

        Face u = new(0, 0);
        double edgeLength = Constants.DistanceToEdge / Math.Cos(Constants.PiOver5);

        double angleV = bisectorAngle + Constants.PiOver5;
        Face v = new(edgeLength * Math.Cos(angleV), edgeLength * Math.Sin(angleV));

        double angleW = bisectorAngle - Constants.PiOver5;
        Face w = new(edgeLength * Math.Cos(angleW), edgeLength * Math.Sin(angleW));

        PentagonShape triangle = new([u, v, w]);

        Matrix2x2d basis = new(v.X, v.Y, w.X, w.Y);
        Matrix2x2d basisInverse = basis.Invert();

        return new Layout(a, b, c, d, e, pentagon, u, v, w, angleV, triangle, basis, basisInverse);
    }

    /// <summary>The complete set of values <see cref="Build"/> produces, computed exactly once.</summary>
    private readonly record struct Layout(
        Face A,
        Face B,
        Face C,
        Face D,
        Face E,
        PentagonShape Pentagon,
        Face U,
        Face V,
        Face W,
        double AngleVRadians,
        PentagonShape Triangle,
        Matrix2x2d Basis,
        Matrix2x2d BasisInverse);
}
