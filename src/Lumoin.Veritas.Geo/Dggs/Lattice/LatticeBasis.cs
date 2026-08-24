using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// Change of basis between <see cref="IJ"/> coordinates — the Hilbert curve's own eigenbasis — and
/// <see cref="KJ"/> coordinates, where defining <c>k = i + j</c> makes the two lattice generator
/// vectors unit length.
/// </summary>
internal static class LatticeBasis
{
    /// <summary>Converts <see cref="IJ"/> coordinates to <see cref="KJ"/> coordinates: <c>k = i + j</c>.</summary>
    public static KJ IJToKJ(IJ ij)
    {
        return new KJ(ij.I + ij.J, ij.J);
    }

    /// <summary>Converts <see cref="KJ"/> coordinates to <see cref="IJ"/> coordinates: <c>i = k − j</c>.</summary>
    public static IJ KJToIJ(KJ kj)
    {
        return new IJ(kj.K - kj.J, kj.J);
    }
}
