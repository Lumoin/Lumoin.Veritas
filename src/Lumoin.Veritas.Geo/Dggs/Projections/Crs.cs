using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Projections;

/// <summary>
/// A triangle on the unit sphere, described by its three <see cref="Cartesian"/> vertices in a fixed
/// order. <see cref="Crs.GetCanonicalTriangle"/> uses the order
/// [center, midpoint, corner], which bakes in the chirality <see cref="EqualAreaProjection"/> depends
/// on — never reorder it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("[{A}, {B}, {C}]")]
internal readonly record struct SphericalTriangle(Cartesian A, Cartesian B, Cartesian C);

/// <summary>
/// A triangle in <see cref="Face"/> coordinates on a single dodecahedron face, in a fixed vertex order.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("[{A}, {B}, {C}]")]
internal readonly record struct FaceTriangle(Face A, Face B, Face C);

/// <summary>
/// The Coordinate Reference System of the dodecahedron: a fixed frame of 62 <see cref="Cartesian"/>
/// vertices — 12 face centers, 20 dodecahedron vertices, and 30 edge midpoints — each stored exactly
/// once even though every dodecahedron vertex and edge midpoint is shared by multiple faces. The table
/// is built exactly once, at static-initialization time, by adding candidate vertices in a fixed nested
/// order (face centers, then vertices origin-major/i-minor, then edge midpoints) and discarding any
/// candidate within <see cref="DeduplicationTolerance"/> of a vertex already in the table: the order in
/// which candidates are considered determines which one becomes each geometric position's permanent
/// representative, so the nesting below must match exactly.
/// A C# value-type table has no reference identity to preserve the way a shared-object return would —
/// what a <see cref="Cartesian"/> caller needs instead is that repeated lookups for the
/// same geometric vertex return the same VALUE, which the fixed build order below guarantees, even
/// though that guarantee is invisible to any fixture (a value comparison could never tell two
/// value-equal candidates apart).
/// </summary>
internal static class Crs
{
    /// <summary>Deduplication tolerance, in Cartesian distance on the unit sphere.</summary>
    private const double DeduplicationTolerance = 1e-5;

    /// <summary>The required vertex count: 12 face centers + 20 vertices + 30 edge midpoints.</summary>
    private const int ExpectedVertexCount = 12 + 20 + 30;

    /// <summary>
    /// The complete, deduplicated vertex table, built once at static-initialization time and never
    /// mutated afterward.
    /// </summary>
    public static readonly Cartesian[] Vertices = Build();

    /// <summary>
    /// A canonical spherical face triangle (face center, edge midpoint, corner vertex) of the
    /// dodecahedron, taken from origin 0's vertices: the face center is <see cref="Vertices"/>[0], its
    /// first corner is [12] (the first of the 20 vertices, added right after the 12 centers), and its
    /// first edge midpoint is [32] (the first of the 30 midpoints, added right after the 20 vertices);
    /// the corner and midpoint are π/5 apart, forming a genuine face triangle. Every face triangle
    /// <see cref="DodecahedronProjection"/> can produce is congruent and consistently wound with this
    /// one, so it serves as the fixed source of <see cref="EqualAreaProjection"/>'s shape constants,
    /// independent of projection call order. The vertex order below — center, midpoint, corner — is
    /// exact and must not be reordered.
    /// </summary>
    public static SphericalTriangle GetCanonicalTriangle()
    {
        return new SphericalTriangle(Vertices[0], Vertices[32], Vertices[12]);
    }

    /// <summary>
    /// Returns the table vertex within <see cref="DeduplicationTolerance"/> of <paramref name="point"/>;
    /// throws if none is found. A reference-typed table could return the same stored object on every
    /// call so that reference-equal callers observe reference-identical results; a struct has no such
    /// identity to offer, so callers here compare by VALUE instead — the only difference downstream
    /// code can ever observe.
    /// </summary>
    public static Cartesian GetVertex(Cartesian point)
    {
        foreach(Cartesian vertex in Vertices)
        {
            if(Distance(point, vertex) < DeduplicationTolerance)
            {
                return vertex;
            }
        }

        throw new InvalidOperationException("Failed to find vertex in CRS.");
    }

    /// <summary>
    /// Builds the 62-vertex table once: face centers, then vertices (origin-major, i-minor), then edge
    /// midpoints, deduplicating every candidate against the vertices already accepted. The count is
    /// hard-checked against <see cref="ExpectedVertexCount"/>.
    /// </summary>
    private static Cartesian[] Build()
    {
        List<Cartesian> vertices = new(ExpectedVertexCount);

        AddFaceCenters(vertices);
        AddVertices(vertices);
        AddMidpoints(vertices);

        if(vertices.Count != ExpectedVertexCount)
        {
            throw new InvalidOperationException("Failed to construct CRS: vertex count is not 62.");
        }

        return [.. vertices];
    }

    /// <summary>Adds the 12 face centers, one per origin, in <see cref="Origins.All"/> order.</summary>
    private static void AddFaceCenters(List<Cartesian> vertices)
    {
        foreach(Origin origin in Origins.All)
        {
            Add(vertices, CoordinateTransforms.ToCartesian(origin.Axis));
        }
    }

    /// <summary>
    /// Adds the 20 dodecahedron vertices: origins in the outer loop, the five candidate vertices per
    /// face (index <c>i</c>) in the inner loop — origin-major, i-minor, exactly the nesting the
    /// deduplication order depends on.
    /// </summary>
    private static void AddVertices(List<Cartesian> vertices)
    {
        double phiVertex = Math.Atan(Constants.DistanceToVertex);

        foreach(Origin origin in Origins.All)
        {
            for(int i = 0; i < 5; i++)
            {
                double thetaVertex = (((2 * i) + 1) * Math.PI) / 5;
                Cartesian vertex = CoordinateTransforms.ToCartesian(new Spherical(thetaVertex + origin.Angle, phiVertex));
                Vector3d rotated = CoordinateConversions.ToVector3d(vertex).Transform(origin.Quaternion);
                Add(vertices, CoordinateConversions.ToCartesian(rotated));
            }
        }
    }

    /// <summary>Adds the 30 edge midpoints, with the same origin-major, i-minor nesting as <see cref="AddVertices"/>.</summary>
    private static void AddMidpoints(List<Cartesian> vertices)
    {
        double phiMidpoint = Math.Atan(Constants.DistanceToEdge);

        foreach(Origin origin in Origins.All)
        {
            for(int i = 0; i < 5; i++)
            {
                double thetaMidpoint = (2 * i * Math.PI) / 5;
                Cartesian midpoint = CoordinateTransforms.ToCartesian(new Spherical(thetaMidpoint + origin.Angle, phiMidpoint));
                Vector3d rotated = CoordinateConversions.ToVector3d(midpoint).Transform(origin.Quaternion);
                Add(vertices, CoordinateConversions.ToCartesian(rotated));
            }
        }
    }

    /// <summary>
    /// Normalizes <paramref name="newVertex"/> and appends it to <paramref name="vertices"/> only if it
    /// is not within <see cref="DeduplicationTolerance"/> of a vertex already present — first candidate
    /// wins, so whichever candidate reaches a given geometric position first becomes that cluster's
    /// permanent representative.
    /// </summary>
    private static void Add(List<Cartesian> vertices, Cartesian newVertex)
    {
        Cartesian normalized = CoordinateConversions.ToCartesian(CoordinateConversions.ToVector3d(newVertex).Normalize());

        foreach(Cartesian existingVertex in vertices)
        {
            if(Distance(normalized, existingVertex) < DeduplicationTolerance)
            {
                return;
            }
        }

        vertices.Add(normalized);
    }

    /// <summary>Euclidean distance between two Cartesian points, via <see cref="Vector3d.Distance"/>.</summary>
    private static double Distance(Cartesian a, Cartesian b)
    {
        return Vector3d.Distance(CoordinateConversions.ToVector3d(a), CoordinateConversions.ToVector3d(b));
    }
}
