using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Conversions between the branded coordinate structs in this namespace and the arithmetic vector
/// types in <see cref="Numerics"/>. Geometry and projection code that needs to add, scale, rotate, or
/// otherwise compute with a <see cref="Cartesian"/> or <see cref="Face"/> value converts through here
/// so no call site duplicates the field-by-field mapping.
/// </summary>
internal static class CoordinateConversions
{
    /// <summary>Converts a <see cref="Cartesian"/> point to a <see cref="Vector3d"/> for arithmetic.</summary>
    public static Vector3d ToVector3d(Cartesian cartesian)
    {
        return new Vector3d(cartesian.X, cartesian.Y, cartesian.Z);
    }

    /// <summary>Converts a <see cref="Vector3d"/> back to a <see cref="Cartesian"/> point.</summary>
    public static Cartesian ToCartesian(Vector3d vector)
    {
        return new Cartesian(vector.X, vector.Y, vector.Z);
    }

    /// <summary>Converts a <see cref="Face"/> point to a <see cref="Vector2d"/> for arithmetic.</summary>
    public static Vector2d ToVector2d(Face face)
    {
        return new Vector2d(face.X, face.Y);
    }

    /// <summary>Converts a <see cref="Vector2d"/> back to a <see cref="Face"/> point.</summary>
    public static Face ToFace(Vector2d vector)
    {
        return new Face(vector.X, vector.Y);
    }

    /// <summary>Converts an <see cref="IJ"/> lattice point to a <see cref="Vector2d"/> for arithmetic.</summary>
    public static Vector2d ToVector2d(IJ ij)
    {
        return new Vector2d(ij.I, ij.J);
    }

    /// <summary>Converts a <see cref="Vector2d"/> back to an <see cref="IJ"/> lattice point.</summary>
    public static IJ ToIJ(Vector2d vector)
    {
        return new IJ(vector.X, vector.Y);
    }
}
