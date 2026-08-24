using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// The twelve unit quaternions that rotate the north pole <c>(0, 0, 1)</c> onto each face center of
/// the A5 dodecahedron: index 0 is the identity (north pole), indices 1-5 rotate onto the first
/// pentagon ring, indices 6-10 onto the second ring, and index 11 is the south pole. Every derivation
/// below is transcribed exactly, in a fixed operation order — the ten ring quaternions rely
/// on exact algebraic cancellation of the sqrt/trig identities and are never renormalized, so
/// reassociating any subexpression risks drifting a quaternion's norm away from 1.
/// </summary>
internal static class DodecahedronQuaternions
{
    /// <summary><c>√5</c>.</summary>
    private static double Sqrt5 { get; } = Math.Sqrt(5);

    /// <summary>
    /// <c>√0.2</c> — this exact expression, never algebraically rewritten to <c>1/√5</c>.
    /// </summary>
    private static double InvSqrt5 { get; } = Math.Sqrt(0.2);

    /// <summary>
    /// Sine of the half-angle of rotation from the pole to the first ring's face centers:
    /// <c>√((1 − InvSqrt5) / 2)</c>. For the second ring sine and cosine swap by the
    /// <c>(π/2 − x)</c> identities.
    /// </summary>
    private static double SinAlpha { get; } = Math.Sqrt((1 - InvSqrt5) / 2);

    /// <summary>Cosine of the same half-angle: <c>√((1 + InvSqrt5) / 2)</c>.</summary>
    private static double CosAlpha { get; } = Math.Sqrt((1 + InvSqrt5) / 2);

    /// <summary>Equals <c>sin72°·sinAlpha</c> (and <c>sin36°·cosAlpha</c>): exactly <c>0.5</c>.</summary>
    private const double A = 0.5;

    /// <summary><c>cos72°·sinAlpha</c>: <c>√((2.5 − √5) / 10)</c>.</summary>
    private static double B { get; } = Math.Sqrt((2.5 - Sqrt5) / 10);

    /// <summary><c>cos36°·cosAlpha</c>: <c>√((2.5 + √5) / 10)</c>.</summary>
    private static double C { get; } = Math.Sqrt((2.5 + Sqrt5) / 10);

    /// <summary><c>cos36°·sinAlpha</c>: <c>√((1 + InvSqrt5) / 8)</c>.</summary>
    private static double D { get; } = Math.Sqrt((1 + InvSqrt5) / 8);

    /// <summary><c>cos72°·cosAlpha</c>: <c>√((1 − InvSqrt5) / 8)</c>.</summary>
    private static double E { get; } = Math.Sqrt((1 - InvSqrt5) / 8);

    /// <summary><c>sin36°·sinAlpha</c>: <c>√((3 − √5) / 8)</c>.</summary>
    private static double F { get; } = Math.Sqrt((3 - Sqrt5) / 8);

    /// <summary><c>sin72°·cosAlpha</c>: <c>√((3 + √5) / 8)</c>.</summary>
    private static double G { get; } = Math.Sqrt((3 + Sqrt5) / 8);

    /// <summary>
    /// The first pentagon ring's five face centers, projected onto the <c>z = 0</c> plane and scaled
    /// by <see cref="SinAlpha"/>, in counter-clockwise order starting from the positive x-axis.
    /// </summary>
    private static Vector2d[] FirstRingCenters { get; } =
    [
        new(SinAlpha, 0),
        new(B, A),
        new(-D, F),
        new(-D, -F),
        new(B, -A)
    ];

    /// <summary>
    /// The second pentagon ring's five face centers: the first ring rotated 180 degrees and scaled by
    /// <see cref="CosAlpha"/> instead of <see cref="SinAlpha"/>.
    /// </summary>
    private static Vector2d[] SecondRingCenters { get; } =
    [
        new(-CosAlpha, 0),
        new(-E, -G),
        new(C, -A),
        new(C, A),
        new(-E, G)
    ];

    /// <summary>
    /// The twelve dodecahedron face quaternions, in component order <c>(X, Y, Z, W)</c>. Index 0 and
    /// index 11 are hardcoded rather than derived from the ring formula below, because the general
    /// formula is undefined at the poles.
    /// </summary>
    public static readonly QuaternionD[] Quaternions = BuildQuaternions();

    /// <summary>
    /// Builds the twelve quaternions once at static-initialization time: the north pole identity, the
    /// two rings of five (rotation axis obtained by crossing each face center with the z-axis, i.e.
    /// <c>(x, y) → (−y, x)</c>), and the hardcoded south pole rotation.
    /// </summary>
    private static QuaternionD[] BuildQuaternions()
    {
        QuaternionD[] quaternions = new QuaternionD[12];
        quaternions[0] = new QuaternionD(0, 0, 0, 1);

        for(int index = 0; index < FirstRingCenters.Length; index++)
        {
            Vector2d center = FirstRingCenters[index];
            Vector2d axis = new(-center.Y, center.X);
            quaternions[index + 1] = new QuaternionD(axis.X, axis.Y, 0, CosAlpha);
        }

        for(int index = 0; index < SecondRingCenters.Length; index++)
        {
            Vector2d center = SecondRingCenters[index];
            Vector2d axis = new(-center.Y, center.X);
            quaternions[index + 6] = new QuaternionD(axis.X, axis.Y, 0, SinAlpha);
        }

        quaternions[11] = new QuaternionD(0, -1, 0, 0);

        return quaternions;
    }
}
