using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// A cell of the A5 pentagonal grid: the dodecahedron-face <see cref="Origin"/> it belongs to, the
/// 0-4 <see cref="Segment"/> (triangular subdivision of that face) it falls in, its position
/// <see cref="S"/> along that segment's Hilbert curve, and the <see cref="Resolution"/> (subdivision
/// depth) the position was computed at.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(origin {Origin.Id}, segment {Segment}, s {S}, res {Resolution})")]
internal readonly record struct A5Cell(Origin Origin, int Segment, ulong S, int Resolution);
