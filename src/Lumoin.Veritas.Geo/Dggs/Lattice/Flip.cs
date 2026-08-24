using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// The sign multiplier applied to one lattice axis when a Hilbert curve sub-triangle is mirrored.
/// Composed across recursion levels by ordinary integer multiplication of the underlying ±1 values
/// — never boolean logic or XOR.
/// </summary>
internal enum Flip
{
    /// <summary>The axis is mirrored; the underlying multiplier is −1.</summary>
    Yes = -1,

    /// <summary>The axis is not mirrored; the underlying multiplier is 1.</summary>
    No = 1,
}
