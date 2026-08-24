using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Geometric constants of the A5 dodecahedron. Every derivation is
/// transcribed exactly — same operations, same order — because downstream fixture parity asserts these to
/// fifteen decimal places.
/// </summary>
internal static class Constants
{
    /// <summary>The golden ratio (fixture key <c>φ</c>): <c>(1 + √5) / 2</c>.</summary>
    public static readonly double Phi = (1 + Math.Sqrt(5)) / 2;

    /// <summary>2π radians (fixture key <c>TWO_PI</c>).</summary>
    public const double TwoPi = 2 * Math.PI;

    /// <summary>2π/5 radians (fixture key <c>TWO_PI_OVER_5</c>).</summary>
    public const double TwoPiOver5 = 2 * Math.PI / 5;

    /// <summary>π/5 radians (fixture key <c>PI_OVER_5</c>).</summary>
    public const double PiOver5 = Math.PI / 5;

    /// <summary>π/10 radians (fixture key <c>PI_OVER_10</c>).</summary>
    public const double PiOver10 = Math.PI / 10;

    /// <summary>
    /// Angle between pentagon faces, radians: <c>2·atan(φ)</c> ≈ 116.565° (fixture key <c>dihedralAngle</c>).
    /// </summary>
    public static readonly double DihedralAngle = 2 * Math.Atan(Phi);

    /// <summary>
    /// Complement of the dihedral angle, radians: <c>π − dihedralAngle</c> ≈ 63.435° (fixture key
    /// <c>interhedralAngle</c>).
    /// </summary>
    public static readonly double InterhedralAngle = Math.PI - DihedralAngle;

    /// <summary>
    /// Face-edge angle, radians: <c>−π/2 + acos(−1/√(3 − φ))</c> (fixture key <c>faceEdgeAngle</c>); trust the
    /// formula and the fixtures over any prose label.
    /// </summary>
    public static readonly double FaceEdgeAngle = (-0.5 * Math.PI) + Math.Acos(-1 / Math.Sqrt(3 - Phi));

    /// <summary>Distance from pentagon-face center to edge: <c>(√5 − 1)/2</c> (fixture key <c>distanceToEdge</c>).</summary>
    public static readonly double DistanceToEdge = (Math.Sqrt(5) - 1) / 2;

    /// <summary>Distance from pentagon-face center to vertex: <c>3 − √5</c> (fixture key <c>distanceToVertex</c>).</summary>
    public static readonly double DistanceToVertex = 3 - Math.Sqrt(5);

    /// <summary>Radius of the dodecahedron's inscribed sphere; the normalization unit (fixture key <c>Rinscribed</c>).</summary>
    public const double RadiusInscribed = 1;

    /// <summary>Radius of the sphere touching the dodecahedron's edge midpoints: <c>√(3 − φ)</c> (fixture key <c>Rmidedge</c>).</summary>
    public static readonly double RadiusMidEdge = Math.Sqrt(3 - Phi);

    /// <summary>Radius of the circumscribed sphere: <c>√3 · Rmidedge / φ</c> (fixture key <c>Rcircumscribed</c>).</summary>
    public static readonly double RadiusCircumscribed = Math.Sqrt(3) * RadiusMidEdge / Phi;

    /// <summary>Authalic radius of Earth in meters: exactly 6371007.2, never rounded.</summary>
    public const double AuthalicRadiusEarth = 6371007.2;

    /// <summary>Authalic surface area of Earth in square meters: <c>4πR²</c>.</summary>
    public const double AuthalicAreaEarth = 4 * Math.PI * AuthalicRadiusEarth * AuthalicRadiusEarth;
}
