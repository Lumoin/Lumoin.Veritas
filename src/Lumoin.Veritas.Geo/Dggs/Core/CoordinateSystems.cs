using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Core;

// Each coordinate system is a distinct readonly record struct so that values in different systems cannot be
// swapped accidentally — a compile-time brand. Angle-typed scalars stay plain doubles with the unit documented
// on the carrying field, since a wrapper struct on every trigonometric call adds friction without adding
// safety at this layer.

/// <summary>
/// Geographic longitude and latitude, in degrees.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(lon {Longitude}, lat {Latitude})")]
public readonly record struct LonLat(double Longitude, double Latitude);

/// <summary>
/// Spherical coordinates (theta, phi) on the unit sphere, both angles in radians.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(θ {Theta}, φ {Phi})")]
public readonly record struct Spherical(double Theta, double Phi);

/// <summary>
/// 3D cartesian coordinates centered on the unit sphere / dodecahedron.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y}, {Z})")]
public readonly record struct Cartesian(double X, double Y, double Z);

/// <summary>
/// 2D cartesian coordinates with origin at the center of a dodecahedron face.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y})")]
public readonly record struct Face(double X, double Y);

/// <summary>
/// 2D polar coordinates (rho, gamma) with origin at the center of a dodecahedron face, gamma in radians.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(ρ {Rho}, γ {Gamma})")]
public readonly record struct Polar(double Rho, double Gamma);

/// <summary>
/// 2D planar coordinates defined by the eigenvectors of the lattice tiling.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(i {I}, j {J})")]
public readonly record struct IJ(double I, double J);

/// <summary>
/// 2D planar coordinates formed by the transformation K → I + J.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(k {K}, j {J})")]
public readonly record struct KJ(double K, double J);

/// <summary>
/// Barycentric coordinates for a triangle, summing to 1.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({B0}, {B1}, {B2})")]
public readonly record struct Barycentric(double B0, double B1, double B2);
