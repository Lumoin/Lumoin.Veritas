using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The planar-XY centroid of a <see cref="FlatGeometry"/>, stratified by *effective*
/// dimension — the highest dimension at which the operand carries nonzero measure,
/// where the measures are <see cref="GeometryMeasures.Area"/>'s and
/// <see cref="GeometryMeasures.Length"/>'s own readings. Nonzero
/// area answers the area-weighted moment centroid over all areal parts; else nonzero
/// length the length-weighted centroid over lineal parts and polygonal rings; else
/// the arithmetic mean over the stored positions, where position multiplicity is
/// mass but ring closure is structure (a ring's closing duplicate is excluded).
/// Degeneracy is the rule, not a special case: a zero-area polygon falls to the
/// length rule over its own rings, a zero-length line to the vertex mean. Refusal is
/// by emptiness, never by kind: collections are full operands at any depth,
/// and every typed empty — <c>GEOMETRYCOLLECTION EMPTY</c> and <c>default</c>
/// included — answers false on the <c>Try</c> tier and the empty point on the
/// geometry tier. Plain double per the house numeric split; each stratum anchors
/// its accumulation to keep magnitudes near the shape's own scale far from the
/// origin, with the areal stratum anchored per ring and de-anchored through the
/// explicit identity.
/// </summary>
public static class GeometryCentroid
{
    /// <summary>
    /// Computes the centroid; false when the operand carries no positions (every
    /// typed empty, the empty collection, and <c>default</c>).
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="centroid">The computed centroid.</param>
    /// <returns><see langword="true"/> when the operand carries positions.</returns>
    public static bool TryCompute(in FlatGeometry geometry, out Point2d centroid)
    {
        if(geometry.IsEmpty)
        {
            centroid = default;

            return false;
        }

        if(TryAreaCentroid(in geometry, out centroid))
        {
            return true;
        }

        if(TryLengthCentroid(in geometry, out centroid))
        {
            return true;
        }

        centroid = VertexMean(in geometry);

        return true;
    }

    /// <summary>
    /// The centroid as a geometry: a <c>POINT</c>, or the empty point for an empty
    /// operand — the envelope family's two-tier shape.
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <returns>The centroid point, or the empty point.</returns>
    public static FlatGeometry ComputeCentroidGeometry(in FlatGeometry geometry)
    {
        if(!TryCompute(in geometry, out Point2d centroid))
        {
            return FlatGeometry.Empty(GeometryKind.Point);
        }

        return FlatGeometryFactory.CreatePoint(centroid);
    }

    /// <summary>
    /// The areal stratum: per-ring anchored shoelace triples, role-signed as one
    /// (negating area and both moments together when the stored winding disagrees
    /// with the part's role, mirroring <see cref="GeometryMeasures.Area"/>'s refusal
    /// to trust winding), de-anchored per ring into the common frame through
    /// <c>M += M' + A·(t − t₀)</c>, divided once at the end. False when the total
    /// role-signed area is zero — the cascade to the length rule.
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="centroid">The area-weighted centroid.</param>
    /// <returns><see langword="true"/> when the total role-signed area is nonzero.</returns>
    private static bool TryAreaCentroid(in FlatGeometry geometry, out Point2d centroid)
    {
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        bool haveFrame = false;
        Point2d frame = default;
        double totalArea = 0;
        double totalMomentX = 0;
        double totalMomentY = 0;

        foreach(FlatGeometryNode node in geometry.Nodes)
        {
            if(node.Kind is not (GeometryKind.Polygon or GeometryKind.MultiPolygon))
            {
                continue;
            }

            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = geometry.Parts[node.FirstPart + index];

                if(part.Length < 3)
                {
                    continue;
                }

                if(!haveFrame)
                {
                    frame = vertices[part.Start];
                    haveFrame = true;
                }

                Point2d anchor = vertices[part.Start];
                double ringArea = 0;
                double ringMomentX = 0;
                double ringMomentY = 0;

                for(int vertex = 0; vertex < part.Length - 1; vertex++)
                {
                    double currentX = vertices[part.Start + vertex].X - anchor.X;
                    double currentY = vertices[part.Start + vertex].Y - anchor.Y;
                    double nextX = vertices[part.Start + vertex + 1].X - anchor.X;
                    double nextY = vertices[part.Start + vertex + 1].Y - anchor.Y;
                    double cross = (currentX * nextY) - (nextX * currentY);

                    ringArea += cross;
                    ringMomentX += (currentX + nextX) * cross;
                    ringMomentY += (currentY + nextY) * cross;
                }

                ringArea /= 2.0;
                ringMomentX /= 6.0;
                ringMomentY /= 6.0;

                double roleSign = part.Role == FlatGeometryPartRole.InteriorRing ? -1.0 : 1.0;

                if((roleSign > 0 && ringArea < 0) || (roleSign < 0 && ringArea > 0))
                {
                    ringArea = -ringArea;
                    ringMomentX = -ringMomentX;
                    ringMomentY = -ringMomentY;
                }

                totalArea += ringArea;
                totalMomentX += ringMomentX + (ringArea * (anchor.X - frame.X));
                totalMomentY += ringMomentY + (ringArea * (anchor.Y - frame.Y));
            }
        }

        if(totalArea == 0)
        {
            centroid = default;

            return false;
        }

        centroid = new Point2d((totalMomentX / totalArea) + frame.X, (totalMomentY / totalArea) + frame.Y);

        return true;
    }

    /// <summary>
    /// The lineal stratum: length-weighted anchored segment midpoints over every
    /// part <see cref="GeometryMeasures.Length"/> reads — lineal runs and polygonal
    /// rings alike — with one common anchor (no sign cancellation rides this sum),
    /// divided once. False when the total length is zero — the cascade to the
    /// vertex mean.
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="centroid">The length-weighted centroid.</param>
    /// <returns><see langword="true"/> when the total length is nonzero.</returns>
    private static bool TryLengthCentroid(in FlatGeometry geometry, out Point2d centroid)
    {
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        bool haveFrame = false;
        Point2d frame = default;
        double totalLength = 0;
        double totalMomentX = 0;
        double totalMomentY = 0;

        foreach(FlatGeometryNode node in geometry.Nodes)
        {
            if(node.Kind is GeometryKind.Point or GeometryKind.MultiPoint or GeometryKind.GeometryCollection)
            {
                continue;
            }

            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = geometry.Parts[node.FirstPart + index];

                if(part.Length < 2)
                {
                    continue;
                }

                if(!haveFrame)
                {
                    frame = vertices[part.Start];
                    haveFrame = true;
                }

                for(int vertex = 1; vertex < part.Length; vertex++)
                {
                    Point2d previous = vertices[part.Start + vertex - 1];
                    Point2d current = vertices[part.Start + vertex];
                    double length = double.Hypot(current.X - previous.X, current.Y - previous.Y);
                    double midpointX = ((previous.X - frame.X) + (current.X - frame.X)) / 2.0;
                    double midpointY = ((previous.Y - frame.Y) + (current.Y - frame.Y)) / 2.0;

                    totalLength += length;
                    totalMomentX += length * midpointX;
                    totalMomentY += length * midpointY;
                }
            }
        }

        if(totalLength == 0)
        {
            centroid = default;

            return false;
        }

        centroid = new Point2d((totalMomentX / totalLength) + frame.X, (totalMomentY / totalLength) + frame.Y);

        return true;
    }

    /// <summary>
    /// The puntal stratum: the anchored multiset mean over the stored positions,
    /// ring-closure duplicates excluded — multiplicity is mass, closure is storage.
    /// Reached only when every part is degenerate, so the count is
    /// positive whenever the operand is non-empty; a malformed sub-two-vertex ring
    /// still contributes its stored positions rather than vanishing, the
    /// substrate's deterministic best-effort posture.
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <returns>The mean of the stored positions.</returns>
    private static Point2d VertexMean(in FlatGeometry geometry)
    {
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        bool haveFrame = false;
        Point2d frame = default;
        double sumX = 0;
        double sumY = 0;
        int count = 0;

        foreach(FlatGeometryNode node in geometry.Nodes)
        {
            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = geometry.Parts[node.FirstPart + index];
                bool ring = part.Role is FlatGeometryPartRole.ExteriorRing or FlatGeometryPartRole.InteriorRing;
                int counted = ring && part.Length > 1 ? part.Length - 1 : part.Length;

                for(int vertex = 0; vertex < counted; vertex++)
                {
                    Point2d position = vertices[part.Start + vertex];

                    if(!haveFrame)
                    {
                        frame = position;
                        haveFrame = true;
                    }

                    sumX += position.X - frame.X;
                    sumY += position.Y - frame.Y;
                    count++;
                }
            }
        }

        return new Point2d((sumX / count) + frame.X, (sumY / count) + frame.Y);
    }
}
