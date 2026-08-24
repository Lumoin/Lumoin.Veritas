using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Lattice;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// The four winding layouts a dodecahedron face's five Hilbert-curve <see cref="Orientation"/> values
/// are arranged in around its quintants. The layout also determines the direction (clockwise or
/// counter-clockwise) that quintant indices are counted in on that face. This is an explicit,
/// value-typed enum rather than comparing four literal orientation arrays by object identity.
/// </summary>
internal enum QuintantLayout
{
    /// <summary>Orientation sequence <c>vu, uw, vw, vw, vw</c> — used only by the north-pole face.</summary>
    ClockwiseFan,

    /// <summary>Orientation sequence <c>wu, uw, vw, vu, uw</c>.</summary>
    ClockwiseStep,

    /// <summary>Orientation sequence <c>wu, uv, wv, wu, uw</c>.</summary>
    CounterStep,

    /// <summary>Orientation sequence <c>vu, uv, wv, wu, uw</c>.</summary>
    CounterJump,
}

/// <summary>
/// One of the twelve dodecahedron faces the A5 grid tiles pentagons onto, in Hilbert-curve traversal
/// order: <see cref="Id"/> 0 is the north pole, 1 through 10 are the two rings of five equatorial
/// faces, and 11 is the south pole. The complete table is built once by <see cref="Origins"/> and
/// never mutated afterward.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(id {Id})")]
internal readonly record struct Origin(
    int Id,
    Spherical Axis,
    Cartesian AxisCartesian,
    QuaternionD Quaternion,
    QuaternionD InverseQuaternion,
    double Angle,
    QuintantLayout Layout,
    int FirstQuintant);
