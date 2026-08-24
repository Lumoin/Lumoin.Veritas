using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// The pair of axis flips describing which of the four congruent sub-triangles a Hilbert curve
/// quaternary digit selects, one <see cref="Flip"/> per lattice axis.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({FlipX}, {FlipY})")]
internal readonly record struct FlipPair(Flip FlipX, Flip FlipY)
{
    /// <summary>
    /// Composes two flip pairs component-wise by integer multiplication of their ±1 values. The
    /// running flip state accumulates this way as Hilbert curve digits are consumed one at a time.
    /// </summary>
    public static FlipPair Multiply(FlipPair a, FlipPair b)
    {
        return new FlipPair((Flip)((int)a.FlipX * (int)b.FlipX), (Flip)((int)a.FlipY * (int)b.FlipY));
    }
}
