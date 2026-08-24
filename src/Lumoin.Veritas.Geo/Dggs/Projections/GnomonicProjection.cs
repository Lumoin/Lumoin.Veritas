using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Projections;

/// <summary>
/// Gnomonic (great-circle-preserving, perspective) projection between the unit sphere and a
/// dodecahedron face's tangent plane, expressed in <see cref="Spherical"/> and <see cref="Polar"/>
/// coordinates.
/// </summary>
internal static class GnomonicProjection
{
    /// <summary>
    /// Projects spherical coordinates to polar coordinates. <c>tan(phi)</c> grows without bound as
    /// <paramref name="spherical"/>'s latitude approaches the horizon — an inherent numerical property
    /// of the projection itself, not a defect to guard against.
    /// </summary>
    public static Polar Forward(Spherical spherical)
    {
        return new Polar(Math.Tan(spherical.Phi), spherical.Theta);
    }

    /// <summary>Unprojects polar coordinates back to spherical coordinates.</summary>
    public static Spherical Inverse(Polar polar)
    {
        return new Spherical(polar.Gamma, Math.Atan(polar.Rho));
    }
}
