using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Utils;

/// <summary>
/// Lazy spiral sampler around a center point on the unit sphere — used to discover nearby cells when a
/// projection-based estimate lands in the wrong one. Every field is set once by the constructor and
/// never mutated afterward, so a single instance is safe to share across concurrent callers.
/// A readonly struct rather than a class: every field
/// is value state (two structs and a double), so constructing one per point-location lookup — the
/// fallback search in <see cref="Cell.SphericalToCell"/> builds exactly one per call — costs no
/// heap allocation.
/// </summary>
internal readonly struct Spiral
{
    /// <summary>
    /// Number of perturbed sample points the spiral can produce: tuned so that, across a wide corpus of
    /// spherical points at many resolutions, the spiral hits a strictly-containing cell within this many
    /// iterations for all but a handful of points right at the polar singularity at very high resolutions.
    /// </summary>
    public const int SampleCount = 24;

    /// <summary>
    /// Azimuthal step between consecutive samples in the rotated tangent plane: 1.4 radians (~80°), a
    /// literal radian value never derived from a degree literal.
    /// </summary>
    private const double AngleStepRadians = 1.4;

    /// <summary>The canonical pole the precomputed spiral directions in <see cref="SpiralDirections"/> are defined around.</summary>
    private static Vector3d Pole { get; } = new(0, 0, 1);

    /// <summary>
    /// Precomputed unit-direction spiral at the canonical pole's tangent plane (z = 0). Each entry is the
    /// tangent direction of one sample; the pattern is independent of resolution, and per spiral each
    /// direction is rotated into the input point's tangent plane by a single quaternion
    /// (<see cref="Rotation"/>).
    /// </summary>
    private static Vector3d[] SpiralDirections { get; } = BuildSpiralDirections();

    /// <summary>The spiral's center, as a Cartesian unit vector.</summary>
    private Vector3d Center { get; }

    /// <summary>
    /// The pole-to-center Rotation, computed once by the constructor. <see cref="QuaternionD.RotationTo"/>
    /// handles the antipodal case internally — the exact code path this spiral needs near the poles.
    /// </summary>
    private QuaternionD Rotation { get; }

    /// <summary>The tangent-plane radius of the outermost sample.</summary>
    private double ScaleRadians { get; }

    /// <summary>
    /// Initializes a spiral around <paramref name="center"/> on the unit sphere. The tangent-plane radius
    /// of the outermost sample is <paramref name="scaleRadians"/>; intermediate samples scale linearly
    /// between 0 and that.
    /// </summary>
    public Spiral(Spherical center, double scaleRadians)
    {
        Center = CoordinateConversions.ToVector3d(CoordinateTransforms.ToCartesian(center));
        this.Rotation = QuaternionD.RotationTo(Pole, Center);
        ScaleRadians = scaleRadians;
    }

    /// <summary>
    /// Returns the <paramref name="index"/>-th spiral sample (0 ≤ <paramref name="index"/> &lt;
    /// <see cref="SampleCount"/>). Sample <paramref name="index"/> sits at a tangent-plane offset of
    /// magnitude <c>(index + 1) / (SampleCount + 1) · scaleRadians</c> from the center, rotated by
    /// azimuth <c>(index + 1) · 1.4</c> radians in the center's tangent frame. A pure function returning
    /// a value, not a shared output buffer written in place. The returned point sits slightly off the
    /// unit sphere by O(scaleRadians²); callers either tolerate this or normalize.
    /// </summary>
    public Cartesian Sample(int index)
    {
        Debug.Assert(index >= 0 && index < SampleCount, "Sample index must fall within [0, SampleCount).");

        Vector3d direction = SpiralDirections[index].Transform(Rotation);
        double radius = ((index + 1) / (double)(SampleCount + 1)) * ScaleRadians;
        Vector3d point = Center + (direction * radius);

        return CoordinateConversions.ToCartesian(point);
    }

    /// <summary>Builds the precomputed pole-tangent-plane spiral directions once, at static-initialization time.</summary>
    private static Vector3d[] BuildSpiralDirections()
    {
        Vector3d[] directions = new Vector3d[SampleCount];
        for(int index = 0; index < SampleCount; index++)
        {
            double angle = (index + 1) * AngleStepRadians;
            directions[index] = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
        }

        return directions;
    }
}
