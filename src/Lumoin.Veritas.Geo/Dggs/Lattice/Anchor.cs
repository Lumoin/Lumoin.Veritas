using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// A position on the Hilbert curve: the quaternary digit <see cref="Q"/> selecting a sub-cell (valid
/// range 0 to 3), the integer offset in the <see cref="IJ"/> lattice basis, and the axis flips
/// accumulated while descending the curve to this position.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(q {Q}, offset {Offset}, flips {Flips})")]
internal readonly record struct Anchor(int Q, IJ Offset, FlipPair Flips);
