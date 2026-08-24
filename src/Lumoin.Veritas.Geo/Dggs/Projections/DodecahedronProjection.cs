using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Projections;

/// <summary>
/// Projects between spherical coordinates on the unit sphere and <see cref="Face"/> coordinates on a
/// single dodecahedron face, by locating which of the ten triangular sectors of the face a point falls
/// in (or, near a face boundary, which NEIGHBORING face's sector it belongs to) and delegating the
/// actual equal-area mapping to <see cref="EqualAreaProjection"/> for that sector's matched pair of a
/// <see cref="FaceTriangle"/> and a <see cref="SphericalTriangle"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every one of the 30 possible face triangles (10 sectors × {plain, reflected-unsquashed,
/// reflected-squashed}) and 240 possible spherical triangles (10 sectors × 12 origins × {plain,
/// reflected}) is a pure, deterministic function of fixed indices, so rather than building each cache
/// entry lazily on first use, every entry is precomputed once, up front, into immutable arrays at
/// static-initialization time.
/// </para>
/// <para>
/// The "squashed" argument to the face-triangle lookup is not a stylistic choice: point projection
/// (<see cref="ForwardCartesian"/>, <see cref="Inverse"/>) always requests the UNSQUASHED triangle
/// (<c>squashed: false</c>, whether or not it is also reflected), while resolving a sector's matching
/// spherical triangle (<see cref="ComputeSphericalTriangle"/>) always requests the SQUASHED one
/// (<c>squashed: true</c>) — squashing is what makes an unprojected reflected triangle line up with the
/// correct neighboring face. Each call site below passes its own literal value; they must never be
/// unified.
/// </para>
/// </remarks>
internal static class DodecahedronProjection
{
    /// <summary>The number of triangular sectors per dodecahedron face.</summary>
    private const int FaceTriangleCount = 10;

    /// <summary>The number of dodecahedron faces (origins).</summary>
    private const int OriginCount = 12;

    /// <summary>
    /// Every plain, reflected-unsquashed and reflected-squashed face triangle, indexed
    /// <c>faceTriangleIndex + (reflected ? (squashed ? 20 : 10) : 0)</c> — see <see cref="GetFaceTriangle"/>.
    /// </summary>
    private static FaceTriangle[] FaceTriangles { get; } = BuildFaceTriangles();

    /// <summary>
    /// Every plain and reflected spherical triangle for every origin, indexed
    /// <c>(10 * originId) + faceTriangleIndex + (reflected ? 120 : 0)</c> — see <see cref="GetSphericalTriangle"/>.
    /// </summary>
    private static SphericalTriangle[] SphericalTriangles { get; } = BuildSphericalTriangles();

    /// <summary>
    /// The equal-area projection for this dodecahedron, constructed once from <see cref="Crs.GetCanonicalTriangle"/>:
    /// every spherical triangle this projection can ever supply is congruent with the canonical one, so
    /// a single set of shape constants serves every sector, origin and reflection.
    /// </summary>
    private static EqualAreaProjection EqualArea { get; } = new(Crs.GetCanonicalTriangle());

    /// <summary>Projects spherical coordinates to face coordinates for the dodecahedron face belonging to <paramref name="originId"/>.</summary>
    public static Face Forward(Spherical spherical, int originId)
    {
        return ForwardCartesian(CoordinateTransforms.ToCartesian(spherical), originId);
    }

    /// <summary>
    /// Same as <see cref="Forward"/> but takes a Cartesian unit vector directly, skipping the
    /// spherical-to-Cartesian round trip when the caller already holds the Cartesian form.
    /// </summary>
    public static Face ForwardCartesian(Cartesian unprojected, int originId)
    {
        Origin origin = Origins.All[originId];

        // Rotate back into origin space, then unproject gnomonically to polar coordinates there.
        Vector3d inOriginSpace = CoordinateConversions.ToVector3d(unprojected).Transform(origin.InverseQuaternion);
        Spherical sphericalInOriginSpace = CoordinateTransforms.ToSpherical(CoordinateConversions.ToCartesian(inOriginSpace));
        Polar polar = GnomonicProjection.Forward(sphericalInOriginSpace);

        // Rotate around the face axis to remove the origin's own rotation.
        polar = polar with { Gamma = polar.Gamma - origin.Angle };

        int faceTriangleIndex = GetFaceTriangleIndex(polar.Gamma);
        bool reflected = ShouldReflect(polar);
        FaceTriangle faceTriangle = GetFaceTriangle(faceTriangleIndex, reflected, squashed: false);
        SphericalTriangle sphericalTriangle = GetSphericalTriangle(faceTriangleIndex, originId, reflected);

        return EqualArea.Forward(unprojected, sphericalTriangle, faceTriangle);
    }

    /// <summary>Unprojects face coordinates back to spherical coordinates for the dodecahedron face belonging to <paramref name="originId"/>.</summary>
    public static Spherical Inverse(Face face, int originId)
    {
        Polar polar = CoordinateTransforms.ToPolar(face);
        int faceTriangleIndex = GetFaceTriangleIndex(polar.Gamma);
        bool reflected = ShouldReflect(polar);
        FaceTriangle faceTriangle = GetFaceTriangle(faceTriangleIndex, reflected, squashed: false);
        SphericalTriangle sphericalTriangle = GetSphericalTriangle(faceTriangleIndex, originId, reflected);

        Cartesian unprojected = EqualArea.Inverse(face, faceTriangle, sphericalTriangle);

        return CoordinateTransforms.ToSpherical(unprojected);
    }

    /// <summary>
    /// The shape constants of the single equal-area projection instance, exposed for the batch
    /// point-to-cell kernel core, which mirrors <see cref="EqualAreaProjection.Forward"/> lane-wise and
    /// needs the same <see cref="EqualAreaTriangleConstants.TriangleArea"/> divisor the scalar path uses.
    /// </summary>
    internal static EqualAreaTriangleConstants EqualAreaConstants => EqualArea.Constants;

    /// <summary>
    /// Detects whether a point lies beyond the edge of the dodecahedron face — meaning its face
    /// triangle must be reflected across that edge to unproject onto the correct neighboring face.
    /// Internal (not private) so the batch point-to-cell kernel core can call the exact same
    /// reflection decision per lane instead of re-transcribing it.
    /// </summary>
    internal static bool ShouldReflect(Polar polar)
    {
        double normalizedGamma = NormalizeGamma(polar.Gamma);
        double distanceFromCenter = CoordinateTransforms.ToFace(new Polar(polar.Rho, normalizedGamma)).X;

        return distanceFromCenter > Constants.DistanceToEdge;
    }

    /// <summary>
    /// Maps an azimuthal angle to its triangular sector index (0-9). The <c>+10</c>-before-<c>%10</c>
    /// idiom keeps the result non-negative regardless of which of the ten sign-alternating sectors
    /// <paramref name="gamma"/> falls in. Internal (not private) so the batch
    /// point-to-cell kernel core can call the exact same sector decision per lane.
    /// </summary>
    internal static int GetFaceTriangleIndex(double gamma)
    {
        return (int)((Math.Floor(gamma / Constants.PiOver5) + 10) % 10);
    }

    /// <summary>
    /// Normalizes an azimuthal angle to the range [-π/5, π/5]: the azimuthal offset from the nearest
    /// sector bisector. Uses <see cref="JsMath.Round"/> — round-half-toward-positive-infinity, not
    /// banker's rounding.
    /// </summary>
    private static double NormalizeGamma(double gamma)
    {
        double segment = gamma / Constants.TwoPiOver5;
        double segmentCenter = JsMath.Round(segment);
        double segmentOffset = segment - segmentCenter;

        return segmentOffset * Constants.TwoPiOver5;
    }

    /// <summary>
    /// Looks up a precomputed face triangle by sector index, reflection and squash state. Internal
    /// (not private) so the batch point-to-cell kernel core can gather the same unsquashed triangle
    /// per lane that <see cref="ForwardCartesian"/> resolves.
    /// </summary>
    internal static FaceTriangle GetFaceTriangle(int faceTriangleIndex, bool reflected, bool squashed)
    {
        int index = faceTriangleIndex;
        if(reflected)
        {
            index += squashed ? 20 : 10;
        }

        return FaceTriangles[index];
    }

    /// <summary>Looks up a precomputed spherical triangle by sector index, origin and reflection.</summary>
    public static SphericalTriangle GetSphericalTriangle(int faceTriangleIndex, int originId, bool reflected)
    {
        int index = (FaceTriangleCount * originId) + faceTriangleIndex;
        if(reflected)
        {
            index += FaceTriangleCount * OriginCount;
        }

        return SphericalTriangles[index];
    }

    /// <summary>
    /// Builds all 30 face triangles once: the 10 plain triangles, then the 10 reflected-unsquashed and
    /// 10 reflected-squashed triangles derived from them.
    /// </summary>
    private static FaceTriangle[] BuildFaceTriangles()
    {
        FaceTriangle[] triangles = new FaceTriangle[3 * FaceTriangleCount];

        for(int faceTriangleIndex = 0; faceTriangleIndex < FaceTriangleCount; faceTriangleIndex++)
        {
            triangles[faceTriangleIndex] = ComputeFaceTriangle(faceTriangleIndex);
        }

        for(int faceTriangleIndex = 0; faceTriangleIndex < FaceTriangleCount; faceTriangleIndex++)
        {
            triangles[FaceTriangleCount + faceTriangleIndex] = ComputeReflectedFaceTriangle(triangles[faceTriangleIndex], faceTriangleIndex, squashed: false);
        }

        for(int faceTriangleIndex = 0; faceTriangleIndex < FaceTriangleCount; faceTriangleIndex++)
        {
            triangles[(2 * FaceTriangleCount) + faceTriangleIndex] = ComputeReflectedFaceTriangle(triangles[faceTriangleIndex], faceTriangleIndex, squashed: true);
        }

        return triangles;
    }

    /// <summary>
    /// Computes the plain (unreflected) face triangle for a sector: the quintant triangle's center and
    /// two corners, with the edge midpoint between the corners standing in for whichever corner the
    /// sign of the sector's azimuth excludes. The sign of <paramref name="faceTriangleIndex"/>'s parity
    /// determines which corner/midpoint pairing keeps the triangle counter-clockwise wound.
    /// </summary>
    private static FaceTriangle ComputeFaceTriangle(int faceTriangleIndex)
    {
        int quintant = ((faceTriangleIndex + 1) / 2) % 5;
        ReadOnlySpan<Face> quintantVertices = Tiling.GetQuintantVertices(quintant).GetVertices();
        Face center = quintantVertices[0];
        Face corner1 = quintantVertices[1];
        Face corner2 = quintantVertices[2];

        Vector2d edgeMidpointVector = Vector2d.Lerp(CoordinateConversions.ToVector2d(corner1), CoordinateConversions.ToVector2d(corner2), 0.5);
        Face edgeMidpoint = CoordinateConversions.ToFace(edgeMidpointVector);

        bool even = faceTriangleIndex % 2 == 0;

        return even ? new FaceTriangle(center, edgeMidpoint, corner1) : new FaceTriangle(center, corner2, edgeMidpoint);
    }

    /// <summary>
    /// Reflects a plain face triangle's center vertex across its opposite edge, scaled by
    /// <paramref name="squashed"/>'s factor — <c>1 + 1/cos(interhedralAngle)</c> squashed, exactly
    /// <c>2</c> unsquashed — then swaps the other two vertices to keep the winding order correct. A
    /// squashed triangle unprojects onto the correct neighboring spherical triangle; the plain (2×)
    /// scale is used only for point projection, never for resolving a spherical triangle.
    /// </summary>
    private static FaceTriangle ComputeReflectedFaceTriangle(FaceTriangle unreflected, int faceTriangleIndex, bool squashed)
    {
        bool even = faceTriangleIndex % 2 == 0;
        Vector2d oppositeEdgeVertex = CoordinateConversions.ToVector2d(even ? unreflected.B : unreflected.C);
        double scale = squashed ? 1 + (1 / Math.Cos(Constants.InterhedralAngle)) : 2;

        Vector2d negatedCenter = -CoordinateConversions.ToVector2d(unreflected.A);
        Vector2d reflectedCenter = negatedCenter + (oppositeEdgeVertex * scale);

        return new FaceTriangle(CoordinateConversions.ToFace(reflectedCenter), unreflected.C, unreflected.B);
    }

    /// <summary>Builds all 240 spherical triangles once: every sector, every origin, plain and reflected.</summary>
    private static SphericalTriangle[] BuildSphericalTriangles()
    {
        SphericalTriangle[] triangles = new SphericalTriangle[2 * FaceTriangleCount * OriginCount];

        for(int originId = 0; originId < OriginCount; originId++)
        {
            for(int faceTriangleIndex = 0; faceTriangleIndex < FaceTriangleCount; faceTriangleIndex++)
            {
                int baseIndex = (FaceTriangleCount * originId) + faceTriangleIndex;
                triangles[baseIndex] = ComputeSphericalTriangle(faceTriangleIndex, originId, reflected: false);
                triangles[baseIndex + (FaceTriangleCount * OriginCount)] = ComputeSphericalTriangle(faceTriangleIndex, originId, reflected: true);
            }
        }

        return triangles;
    }

    /// <summary>
    /// Computes the spherical triangle matching a sector's SQUASHED face triangle for a given origin:
    /// each face vertex is rotated back to polar coordinates, offset by the origin's own rotation,
    /// unprojected gnomonically, rotated onto the origin's face by its quaternion, and snapped to the
    /// nearest <see cref="Crs"/> table vertex.
    /// </summary>
    private static SphericalTriangle ComputeSphericalTriangle(int faceTriangleIndex, int originId, bool reflected)
    {
        Origin origin = Origins.All[originId];
        FaceTriangle faceTriangle = GetFaceTriangle(faceTriangleIndex, reflected, squashed: true);

        return new SphericalTriangle(
            ProjectFaceVertexToCrsVertex(faceTriangle.A, origin),
            ProjectFaceVertexToCrsVertex(faceTriangle.B, origin),
            ProjectFaceVertexToCrsVertex(faceTriangle.C, origin));
    }

    /// <summary>Projects a single face-triangle vertex onto its canonical CRS vertex for <paramref name="origin"/>.</summary>
    private static Cartesian ProjectFaceVertexToCrsVertex(Face face, Origin origin)
    {
        Polar polar = CoordinateTransforms.ToPolar(face);
        Polar rotatedPolar = new(polar.Rho, polar.Gamma + origin.Angle);
        Spherical rotatedSpherical = GnomonicProjection.Inverse(rotatedPolar);
        Cartesian rotatedCartesian = CoordinateTransforms.ToCartesian(rotatedSpherical);
        Vector3d rotated = CoordinateConversions.ToVector3d(rotatedCartesian).Transform(origin.Quaternion);

        return Crs.GetVertex(CoordinateConversions.ToCartesian(rotated));
    }
}
