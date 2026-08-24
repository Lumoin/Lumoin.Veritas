using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// Coordinates on the triangular grid underlying the pentagonal A5 grid. Neighboring cells differ by
/// ±1 in exactly one coordinate while the other two stay constant, and the same geometric cell has
/// the same triple regardless of which Hilbert curve orientation is in use.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y}, {Z})")]
internal readonly record struct Triple(int X, int Y, int Z);
