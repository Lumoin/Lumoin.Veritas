using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Geometry;

/// <summary>
/// A <see cref="SphericalPolygonShape"/> constrained to exactly three vertices.
/// </summary>
internal sealed class SphericalTriangleShape : SphericalPolygonShape
{
    /// <summary>Builds a spherical triangle shape, throwing if <paramref name="vertices"/> is not exactly length 3.</summary>
    public SphericalTriangleShape(Cartesian[] vertices)
        : base(RequireExactlyThreeVertices(vertices))
    {
    }

    /// <summary>Validates the vertex count before the base constructor copies the array.</summary>
    private static Cartesian[] RequireExactlyThreeVertices(Cartesian[] vertices)
    {
        if(vertices.Length != 3)
        {
            throw new ArgumentException("A spherical triangle shape requires exactly three vertices.", nameof(vertices));
        }

        return vertices;
    }
}
