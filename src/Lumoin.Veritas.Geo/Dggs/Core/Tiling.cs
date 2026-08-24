using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Lattice;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Builds the pentagon and triangle vertex sets that tile a dodecahedron face, and converts polar
/// angles to quintant indices. No result is cached across calls — every call recomputes from the
/// immutable <see cref="PentagonConstants"/> tables, which keeps every function here safe under
/// concurrent callers.
/// </summary>
internal static class Tiling
{
    /// <summary>
    /// Whether the triangle (rather than the full pentagon) is used as the base shape for
    /// <see cref="GetPentagonVertices"/>. Always <see langword="false"/> — kept as a named constant
    /// rather than inlined.
    /// </summary>
    private const bool TriangleModeEnabled = false;

    /// <summary>The translation applied when an anchor's second axis is flipped: triangle vertex w.</summary>
    private static Vector2d ShiftRight { get; } = CoordinateConversions.ToVector2d(PentagonConstants.VertexW);

    /// <summary>The translation applied when an anchor's first axis is flipped: the negation of <see cref="ShiftRight"/>.</summary>
    private static Vector2d ShiftLeft { get; } = -ShiftRight;

    /// <summary>Rotation matrices for each of the five quintants, by <c>quintant · 2π/5</c>.</summary>
    private static Matrix2x2d[] QuintantRotations { get; } = BuildQuintantRotations();

    /// <summary>Vertex count of the tiling pentagon (and the dodecahedron-face vertex set): 5.</summary>
    public const int PentagonVertexCount = 5;

    /// <summary>Vertex count of the single-quintant triangle: 3.</summary>
    public const int TriangleVertexCount = 3;

    /// <summary>
    /// The largest vertex count any <c>Fill*Vertices</c> core below can produce — the size a stackalloc
    /// caller (<see cref="Cell"/>'s stack-only point-location hot path) needs to reserve.
    /// </summary>
    public const int MaxVertexCount = PentagonVertexCount;

    /// <summary>
    /// Builds a pentagon (or, were <see cref="TriangleModeEnabled"/> ever turned on, the equivalent
    /// triangle) for a single Hilbert-curve anchor at the given resolution and quintant, applying the
    /// anchor's flip-driven reflect/rotate/translate sequence in a fixed order.
    /// </summary>
    public static PentagonShape GetPentagonVertices(int resolution, int quintant, Anchor anchor)
    {
        Face[] vertices = new Face[PentagonVertexCount];
        FillPentagonVertices(vertices, resolution, quintant, anchor);

        return PentagonShape.AdoptVertices(vertices);
    }

    /// <summary>
    /// Writes a pentagon (or, were <see cref="TriangleModeEnabled"/> ever turned on, the equivalent
    /// triangle) for a single Hilbert-curve anchor into <paramref name="destination"/>, applying the
    /// anchor's flip-driven reflect/rotate/translate sequence in a fixed order. The one
    /// shared core both <see cref="GetPentagonVertices"/> (which wraps the result in a heap
    /// <see cref="PentagonShape"/>) and the stack-only point-location hot path in <see cref="Cell"/>
    /// call, so the numerics cannot fork between the two call shapes.
    /// </summary>
    /// <returns>The number of vertices written to <paramref name="destination"/>.</returns>
    internal static int FillPentagonVertices(Span<Face> destination, int resolution, int quintant, Anchor anchor)
    {
        ReadOnlySpan<Face> baseVertices = (TriangleModeEnabled ? PentagonConstants.Triangle : PentagonConstants.Pentagon).GetVertices();
        Span<Face> vertices = destination[..baseVertices.Length];
        baseVertices.CopyTo(vertices);

        Vector2d translation = PentagonConstants.Basis.Transform(CoordinateConversions.ToVector2d(anchor.Offset));

        if(anchor.Flips.FlipX == Flip.No && anchor.Flips.FlipY == Flip.Yes)
        {
            RotateSpan180(vertices);
        }

        int q = anchor.Q;
        int flipSum = (int)anchor.Flips.FlipX + (int)anchor.Flips.FlipY;
        if(IsLastTwoOrEndpointFlavor(flipSum, q))
        {
            ReflectSpanY(vertices);
        }

        if(anchor.Flips.FlipX == Flip.Yes && anchor.Flips.FlipY == Flip.Yes)
        {
            RotateSpan180(vertices);
        }
        else if(anchor.Flips.FlipX == Flip.Yes)
        {
            TranslateSpan(vertices, ShiftLeft);
        }
        else if(anchor.Flips.FlipY == Flip.Yes)
        {
            TranslateSpan(vertices, ShiftRight);
        }

        // Position within the quintant.
        TranslateSpan(vertices, translation);
        ScaleSpan(vertices, 1 / Math.Pow(2, resolution));
        TransformSpan(vertices, QuintantRotations[quintant]);

        return vertices.Length;
    }

    /// <summary>
    /// Computes which of the eight symmetry-equivalent pentagon shapes an anchor's flip state and
    /// quaternary digit select. The same boolean condition is also needed in
    /// <see cref="GetPentagonVertices"/>; both call sites share <see cref="IsLastTwoOrEndpointFlavor"/>
    /// rather than duplicating the condition.
    /// </summary>
    public static int GetPentagonFlavor(Anchor anchor)
    {
        int flavor = 0;
        if(anchor.Flips.FlipY == Flip.Yes)
        {
            flavor += 2;
        }

        int q = anchor.Q;
        int flipSum = (int)anchor.Flips.FlipX + (int)anchor.Flips.FlipY;
        if(IsLastTwoOrEndpointFlavor(flipSum, q))
        {
            flavor += 1;
        }

        if(flipSum is -2 or 2)
        {
            flavor += 4;
        }

        return flavor;
    }

    /// <summary>Builds the triangle covering a single quintant, rotated into place.</summary>
    public static PentagonShape GetQuintantVertices(int quintant)
    {
        Face[] vertices = new Face[TriangleVertexCount];
        FillQuintantVertices(vertices, quintant);

        return PentagonShape.AdoptVertices(vertices);
    }

    /// <summary>
    /// Writes the triangle covering a single quintant, rotated into place, into
    /// <paramref name="destination"/> — the shared core for <see cref="GetQuintantVertices"/> and the
    /// stack-only point-location hot path in <see cref="Cell"/>.
    /// </summary>
    /// <returns>The number of vertices written to <paramref name="destination"/>.</returns>
    internal static int FillQuintantVertices(Span<Face> destination, int quintant)
    {
        ReadOnlySpan<Face> baseVertices = PentagonConstants.Triangle.GetVertices();
        Span<Face> vertices = destination[..baseVertices.Length];
        baseVertices.CopyTo(vertices);
        TransformSpan(vertices, QuintantRotations[quintant]);

        return vertices.Length;
    }

    /// <summary>
    /// Builds the full dodecahedron face from its five quintants' outer vertex, one per quintant. The
    /// vertex list is explicitly reversed here to obtain the correct winding order BEFORE
    /// <see cref="PentagonShape"/>'s own constructor applies its independent, conditional winding
    /// correction — both reversals happen, in that order.
    /// </summary>
    public static PentagonShape GetFaceVertices()
    {
        Face[] vertices = new Face[PentagonVertexCount];
        FillFaceVertices(vertices);

        return PentagonShape.AdoptVertices(vertices);
    }

    /// <summary>
    /// Writes the full dodecahedron face from its five quintants' outer vertex, one per quintant, into
    /// <paramref name="destination"/>. The vertex list is explicitly reversed here to obtain the correct
    /// winding order BEFORE <see cref="PentagonShape"/>'s own constructor (or, on the stack-only hot
    /// path, the static containment test) applies its own independent, conditional winding correction —
    /// both reversals happen, in that order. The shared core for <see cref="GetFaceVertices"/> and the
    /// stack-only point-location hot path in <see cref="Cell"/>.
    /// </summary>
    /// <returns>The number of vertices written to <paramref name="destination"/>.</returns>
    internal static int FillFaceVertices(Span<Face> destination)
    {
        Span<Face> vertices = destination[..QuintantRotations.Length];
        for(int quintant = 0; quintant < QuintantRotations.Length; quintant++)
        {
            Vector2d rotated = QuintantRotations[quintant].Transform(CoordinateConversions.ToVector2d(PentagonConstants.VertexV));
            vertices[quintant] = CoordinateConversions.ToFace(rotated);
        }

        vertices.Reverse();

        return vertices.Length;
    }

    /// <summary>
    /// Converts a polar angle to its quintant index (0-4): rounds <c>gamma / (2π/5)</c> using
    /// round-half-up semantics, not banker's rounding.
    /// </summary>
    public static int GetQuintantPolar(Polar polar)
    {
        return (int)((JsMath.Round(polar.Gamma / Constants.TwoPiOver5) + 5) % 5);
    }

    /// <summary>
    /// Builds the five quintant rotation matrices once, at static-initialization time.
    /// </summary>
    private static Matrix2x2d[] BuildQuintantRotations()
    {
        Matrix2x2d[] rotations = new Matrix2x2d[5];
        for(int quintant = 0; quintant < rotations.Length; quintant++)
        {
            rotations[quintant] = Matrix2x2d.FromRotation(Constants.TwoPiOver5 * quintant);
        }

        return rotations;
    }

    /// <summary>
    /// The reflectY-trigger condition shared by <see cref="GetPentagonVertices"/> and
    /// <see cref="GetPentagonFlavor"/>: true when orienting the last two pentagons (both or neither
    /// flip is <see cref="Flip.Yes"/> and the quaternary digit is past the first two), or when
    /// orienting the first and last pentagons (exactly one flip is <see cref="Flip.Yes"/> and the
    /// quaternary digit is the first or last).
    /// </summary>
    private static bool IsLastTwoOrEndpointFlavor(int flipSum, int quaternaryDigit)
    {
        bool lastTwoPentagons = (flipSum is -2 or 2) && quaternaryDigit > 1;
        bool firstAndLastPentagons = flipSum == 0 && (quaternaryDigit == 0 || quaternaryDigit == 3);

        return lastTwoPentagons || firstAndLastPentagons;
    }

    /// <summary>
    /// Rotates every vertex in <paramref name="vertices"/> 180 degrees (negating X and Y) in place — the
    /// span-only equivalent of <see cref="PentagonShape.Rotate180"/>, transcribed identically.
    /// </summary>
    private static void RotateSpan180(Span<Face> vertices)
    {
        for(int index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new Face(-vertices[index].X, -vertices[index].Y);
        }
    }

    /// <summary>
    /// Reflects every vertex in <paramref name="vertices"/> over the x-axis (negating Y), then reverses
    /// the vertex order in place to keep the winding consistent — both effects, in that order, the
    /// span-only equivalent of <see cref="PentagonShape.ReflectY"/>, transcribed identically.
    /// </summary>
    private static void ReflectSpanY(Span<Face> vertices)
    {
        for(int index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new Face(vertices[index].X, -vertices[index].Y);
        }

        vertices.Reverse();
    }

    /// <summary>
    /// Translates every vertex in <paramref name="vertices"/> by <paramref name="translation"/> in place
    /// — the span-only equivalent of <see cref="PentagonShape.Translate"/>, transcribed identically.
    /// </summary>
    private static void TranslateSpan(Span<Face> vertices, Vector2d translation)
    {
        for(int index = 0; index < vertices.Length; index++)
        {
            Vector2d translated = CoordinateConversions.ToVector2d(vertices[index]) + translation;
            vertices[index] = CoordinateConversions.ToFace(translated);
        }
    }

    /// <summary>
    /// Scales every vertex in <paramref name="vertices"/> by <paramref name="scale"/> in place — the
    /// span-only equivalent of <see cref="PentagonShape.Scale"/>, transcribed identically.
    /// </summary>
    private static void ScaleSpan(Span<Face> vertices, double scale)
    {
        for(int index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new Face(vertices[index].X * scale, vertices[index].Y * scale);
        }
    }

    /// <summary>
    /// Applies a linear transform (column-major: <c>x·column0 + y·column1</c>, see
    /// <see cref="Matrix2x2d"/>) to every vertex in <paramref name="vertices"/> in place — the span-only
    /// equivalent of <see cref="PentagonShape.Transform"/>, transcribed identically.
    /// </summary>
    private static void TransformSpan(Span<Face> vertices, Matrix2x2d transform)
    {
        for(int index = 0; index < vertices.Length; index++)
        {
            Vector2d transformed = transform.Transform(CoordinateConversions.ToVector2d(vertices[index]));
            vertices[index] = CoordinateConversions.ToFace(transformed);
        }
    }
}
