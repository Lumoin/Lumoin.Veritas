using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Geometry;

/// <summary>
/// A polygon in <see cref="Face"/> coordinates — a pentagon for every A5 face, but also the
/// resolution-0 root triangle, so the vertex list is variable-length rather than fixed at five.
/// The instance owns its vertex list: the constructor copies the caller's array instead of aliasing
/// it, so external mutation of the array passed in cannot corrupt a constructed shape (a deliberate,
/// fixture-invisible hardening over the source, which stores and mutates the caller's array in place).
/// </summary>
internal sealed class PentagonShape
{
    /// <summary>The owned, mutable backing array of the pentagon's vertices.</summary>
    private Face[] VertexArray { get; }

    /// <summary>
    /// Builds a pentagon shape from its vertices, copying them into an owned array. If the shoelace
    /// signed area (see <see cref="GetArea"/>) is negative, the vertex order is reversed in place so
    /// every constructed shape has consistently correct (counter-clockwise) winding.
    /// </summary>
    public PentagonShape(Face[] vertices)
        : this(vertices, ownsArray: false)
    {
    }

    /// <summary>
    /// Builds a pentagon shape either by copying <paramref name="vertices"/> (<paramref name="ownsArray"/>
    /// <see langword="false"/>, the public <see cref="PentagonShape(Face[])"/> contract) or by adopting it
    /// directly (<paramref name="ownsArray"/> <see langword="true"/>) — for internal callers
    /// (<see cref="Tiling"/>'s <c>Get*Vertices</c> wrappers) that just allocated the array exclusively for
    /// this purpose and hold no other reference to it, so the defensive copy would be pure waste. Either
    /// way the same winding correction below applies.
    /// </summary>
    private PentagonShape(Face[] vertices, bool ownsArray)
    {
        if(ownsArray)
        {
            VertexArray = vertices;
        }
        else
        {
            VertexArray = new Face[vertices.Length];
            Array.Copy(vertices, VertexArray, vertices.Length);
        }

        if(!IsWindingCorrect())
        {
            Array.Reverse(VertexArray);
        }
    }

    /// <summary>
    /// Builds a pentagon shape by adopting <paramref name="vertices"/> directly, skipping the defensive
    /// copy <see cref="PentagonShape(Face[])"/> performs. Safe only when the caller just allocated the
    /// array exclusively for this purpose and will neither retain nor mutate it afterward.
    /// </summary>
    internal static PentagonShape AdoptVertices(Face[] vertices)
    {
        return new PentagonShape(vertices, ownsArray: true);
    }

    /// <summary>
    /// Returns the vertices in their current (owned, possibly winding-corrected) order.
    /// </summary>
    public ReadOnlySpan<Face> GetVertices()
    {
        return VertexArray;
    }

    /// <summary>
    /// Returns the DOUBLED signed shoelace sum of the vertices — not divided by two. Callers rely on
    /// this exact un-halved value; do not "fix" it by adding a <c>/ 2</c>.
    /// </summary>
    public double GetArea()
    {
        double signedArea = 0;
        int n = VertexArray.Length;
        for(int index = 0; index < n; index++)
        {
            int next = (index + 1) % n;
            signedArea += (VertexArray[next].X - VertexArray[index].X) * (VertexArray[next].Y + VertexArray[index].Y);
        }

        return signedArea;
    }

    /// <summary>Scales every vertex by <paramref name="scale"/> in place and returns this instance.</summary>
    public PentagonShape Scale(double scale)
    {
        for(int index = 0; index < VertexArray.Length; index++)
        {
            VertexArray[index] = new Face(VertexArray[index].X * scale, VertexArray[index].Y * scale);
        }

        return this;
    }

    /// <summary>Rotates the pentagon 180 degrees (equivalent to negating X and Y) and returns this instance.</summary>
    public PentagonShape Rotate180()
    {
        for(int index = 0; index < VertexArray.Length; index++)
        {
            VertexArray[index] = new Face(-VertexArray[index].X, -VertexArray[index].Y);
        }

        return this;
    }

    /// <summary>
    /// Reflects the pentagon over the x-axis (negating Y) and then reverses the vertex order to keep
    /// the winding consistent — both effects, in that order. Returns this instance.
    /// </summary>
    public PentagonShape ReflectY()
    {
        for(int index = 0; index < VertexArray.Length; index++)
        {
            VertexArray[index] = new Face(VertexArray[index].X, -VertexArray[index].Y);
        }

        Array.Reverse(VertexArray);

        return this;
    }

    /// <summary>Translates every vertex by <paramref name="translation"/> in place and returns this instance.</summary>
    public PentagonShape Translate(Vector2d translation)
    {
        for(int index = 0; index < VertexArray.Length; index++)
        {
            Vector2d translated = CoordinateConversions.ToVector2d(VertexArray[index]) + translation;
            VertexArray[index] = CoordinateConversions.ToFace(translated);
        }

        return this;
    }

    /// <summary>
    /// Applies a linear transform (column-major: <c>x·column0 + y·column1</c>, see
    /// <see cref="Matrix2x2d"/>) to every vertex in place and returns this instance.
    /// </summary>
    public PentagonShape Transform(Matrix2x2d transform)
    {
        for(int index = 0; index < VertexArray.Length; index++)
        {
            Vector2d transformed = transform.Transform(CoordinateConversions.ToVector2d(VertexArray[index]));
            VertexArray[index] = CoordinateConversions.ToFace(transformed);
        }

        return this;
    }

    /// <summary>Returns a new, independently owned copy of this shape.</summary>
    public PentagonShape Clone()
    {
        return new PentagonShape(VertexArray);
    }

    /// <summary>
    /// The center of the pentagon, computed by dividing each vertex coordinate by the vertex count
    /// before accumulating — per-term division, not sum-then-divide.
    /// </summary>
    public Face GetCenter()
    {
        double n = VertexArray.Length;
        double centerX = 0;
        double centerY = 0;
        foreach(Face vertex in VertexArray)
        {
            centerX += vertex.X / n;
            centerY += vertex.Y / n;
        }

        return new Face(centerX, centerY);
    }

    /// <summary>
    /// Tests whether <paramref name="point"/> is inside the pentagon by checking which side of every
    /// edge it falls on. Assumes consistent (counter-clockwise) winding order, which the constructor
    /// guarantees at construction time — but later in-place transforms with a reflecting matrix could
    /// invalidate it, so this re-checks the winding rather than trusting the invariant silently.
    /// Delegates the actual per-edge arithmetic to <see cref="ContainsPoint(ReadOnlySpan{Face}, Face)"/>
    /// so there is exactly one implementation of the containment test.
    /// </summary>
    /// <returns>1 if the point is inside; otherwise a negative value proportional to the distance to the nearest edge.</returns>
    public double ContainsPoint(Face point)
    {
        if(!IsWindingCorrect())
        {
            throw new InvalidOperationException("The pentagon's vertices are not wound counter-clockwise.");
        }

        return ContainsPointCore(VertexArray, reversed: false, point);
    }

    /// <summary>
    /// Tests whether <paramref name="point"/> is inside the polygon described by <paramref name="vertices"/>
    /// directly, without requiring a constructed <see cref="PentagonShape"/> — the stack-only path the
    /// point-location hot loop in <see cref="Cell"/> uses so no per-candidate heap allocation
    /// survives a containment test. Applies the same conditional winding correction the constructor
    /// applies: if the shoelace signed area over <paramref name="vertices"/> is negative, edges are
    /// walked in the same reversed order the constructor's own <c>Array.Reverse</c> would produce, so a
    /// raw span filled straight from <see cref="Tiling"/>'s span-filling core matches a heap-wrapped
    /// shape bit-for-bit.
    /// </summary>
    /// <returns>1 if the point is inside; otherwise a negative value proportional to the distance to the nearest edge.</returns>
    public static double ContainsPoint(ReadOnlySpan<Face> vertices, Face point)
    {
        int n = vertices.Length;
        double signedArea = 0;
        for(int index = 0; index < n; index++)
        {
            int next = (index + 1) % n;
            signedArea += (vertices[next].X - vertices[index].X) * (vertices[next].Y + vertices[index].Y);
        }

        return ContainsPointCore(vertices, signedArea < 0, point);
    }

    /// <summary>
    /// The shared per-edge containment loop both <see cref="ContainsPoint(Face)"/> and
    /// <see cref="ContainsPoint(ReadOnlySpan{Face}, Face)"/> funnel through: identical arithmetic either
    /// way, same initial <c>distanceMax = 1</c>; only the edge-endpoint index mapping differs when
    /// <paramref name="reversed"/> is set, exactly mirroring what <see cref="Array.Reverse"/> on the
    /// backing array would have produced.
    /// </summary>
    private static double ContainsPointCore(ReadOnlySpan<Face> vertices, bool reversed, Face point)
    {
        int n = vertices.Length;
        double distanceMax = 1;
        for(int index = 0; index < n; index++)
        {
            int index1 = reversed ? n - 1 - index : index;
            int index2 = reversed ? n - 1 - ((index + 1) % n) : (index + 1) % n;

            Face v1 = vertices[index1];
            Face v2 = vertices[index2];

            double dx = v1.X - v2.X;
            double dy = v1.Y - v2.Y;
            double px = point.X - v1.X;
            double py = point.Y - v1.Y;

            double crossProduct = (dx * py) - (dy * px);
            if(crossProduct < 0)
            {
                double pointLength = Math.Sqrt((px * px) + (py * py));
                distanceMax = Math.Min(distanceMax, crossProduct / pointLength);
            }
        }

        return distanceMax;
    }

    /// <summary>
    /// Tests whether the 2D segment from <paramref name="a"/> to <paramref name="b"/> intersects this
    /// pentagon: true if either endpoint is inside, or any pentagon edge crosses the segment.
    /// </summary>
    public bool IntersectsSegment(Face a, Face b)
    {
        if(ContainsPoint(a) > 0 || ContainsPoint(b) > 0)
        {
            return true;
        }

        int n = VertexArray.Length;
        for(int index = 0; index < n; index++)
        {
            Face v1 = VertexArray[index];
            Face v2 = VertexArray[(index + 1) % n];
            if(Segments2dIntersect(a, b, v1, v2))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits every edge of the pentagon into <paramref name="segments"/> pieces, returning a new
    /// shape with more vertices — or this same instance, unchanged, if <paramref name="segments"/> is
    /// 1 or less.
    /// </summary>
    public PentagonShape SplitEdges(int segments)
    {
        if(segments <= 1)
        {
            return this;
        }

        int n = VertexArray.Length;
        Face[] newVertices = new Face[n * segments];
        int writeIndex = 0;
        for(int index = 0; index < n; index++)
        {
            Vector2d v1 = CoordinateConversions.ToVector2d(VertexArray[index]);
            Vector2d v2 = CoordinateConversions.ToVector2d(VertexArray[(index + 1) % n]);

            newVertices[writeIndex] = CoordinateConversions.ToFace(v1);
            writeIndex++;

            for(int segment = 1; segment < segments; segment++)
            {
                double t = (double)segment / segments;
                newVertices[writeIndex] = CoordinateConversions.ToFace(Vector2d.Lerp(v1, v2, t));
                writeIndex++;
            }
        }

        return new PentagonShape(newVertices);
    }

    /// <summary>Whether the current vertex order yields a non-negative (counter-clockwise) shoelace area.</summary>
    private bool IsWindingCorrect()
    {
        return GetArea() >= 0;
    }

    /// <summary>
    /// 2D segment-versus-segment intersection test: true iff the closed segments p1→p2 and p3→p4
    /// share at least one point. The <c>1e-12</c> epsilon on the near-parallel denominator has no
    /// colinear-overlap fallback.
    /// </summary>
    private static bool Segments2dIntersect(Face p1, Face p2, Face p3, Face p4)
    {
        double d1x = p2.X - p1.X;
        double d1y = p2.Y - p1.Y;
        double d2x = p4.X - p3.X;
        double d2y = p4.Y - p3.Y;
        double denominator = (d1x * d2y) - (d1y * d2x);
        if(Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        double dx = p3.X - p1.X;
        double dy = p3.Y - p1.Y;
        double t = ((dx * d2y) - (dy * d2x)) / denominator;
        double u = ((dx * d1y) - (dy * d1x)) / denominator;

        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
    }
}
