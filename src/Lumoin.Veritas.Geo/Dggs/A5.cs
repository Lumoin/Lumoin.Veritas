using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;
using GridDiskTraversal = Lumoin.Veritas.Geo.Dggs.Traversal.GridDisk;
using PolygonToCellsRegion = Lumoin.Veritas.Geo.Dggs.Regions.PolygonToCells;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// The public surface of the A5 pentagonal equal-area discrete global grid system (DGGS): point/cell
/// conversion, hierarchy traversal, cell-count/area queries, compaction, neighbor and region traversal.
/// Every method here is a thin, input-sanitizing wrapper over the internal <c>Core</c>/<c>Traversal</c>/
/// <c>Regions</c> layer, which never changes behavior to accommodate this facade. Cell identity is
/// always the public <see cref="A5CellId"/> wrapper; the raw <see cref="ulong"/> encoding is an
/// implementation detail below this class.
/// </summary>
public static class A5
{
    /// <summary>The finest resolution the grid supports.</summary>
    public const int MaxResolution = Serialization.MaxResolution;

    /// <summary>
    /// The abstract cell that contains the whole world: resolution −1, with the twelve resolution-0
    /// cells as its children.
    /// </summary>
    public static A5CellId WorldCell { get; } = new(Serialization.WorldCell);

    /// <summary>Converts a geographic point to the id of the cell containing it at <paramref name="resolution"/>.</summary>
    /// <param name="lonLat">The point, in geographic longitude/latitude degrees.</param>
    /// <param name="resolution">The target resolution: −1 (the world cell) through <see cref="MaxResolution"/>.</param>
    /// <returns>The id of the cell containing <paramref name="lonLat"/> at <paramref name="resolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lonLat"/> has a non-finite longitude or latitude, or <paramref name="resolution"/>
    /// is less than −1 or greater than <see cref="MaxResolution"/>.
    /// </exception>
    public static A5CellId LonLatToCell(LonLat lonLat, int resolution)
    {
        ValidateLonLat(lonLat, nameof(lonLat));
        ValidateEncodingResolution(resolution, nameof(resolution));

        return new A5CellId(Cell.LonLatToCell(lonLat, resolution));
    }

    /// <summary>Converts a cell id to the geographic coordinates of its center.</summary>
    /// <param name="cellId">The cell to locate.</param>
    /// <returns>The cell's center, in geographic longitude/latitude degrees.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="cellId"/> does not decode to a valid origin/segment pair (a malformed id). The
    /// internal decoder already throws the correctly typed exception, so this facade does not duplicate
    /// the check.
    /// </exception>
    public static LonLat CellToLonLat(A5CellId cellId)
    {
        return Cell.CellToLonLat(cellId.Value);
    }

    /// <summary>Converts a cell id to its boundary ring, in geographic coordinates.</summary>
    /// <param name="cellId">The cell to compute the boundary of.</param>
    /// <param name="closedRing">Pass <see langword="true"/> (the default) to close the ring by repeating the first point as the last.</param>
    /// <param name="segments">
    /// Number of segments to split each pentagon edge into before projection. Pass 0 (the default) to use
    /// <c>max(1, 2^(6 - resolution))</c>, the cell's own resolution.
    /// </param>
    /// <returns>The cell's boundary ring, in geographic longitude/latitude degrees; empty for <see cref="WorldCell"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="cellId"/> does not decode to a valid origin/segment pair (a malformed id); see
    /// <see cref="CellToLonLat"/>.
    /// </exception>
    public static LonLat[] CellToBoundary(A5CellId cellId, bool closedRing = true, int segments = 0)
    {
        return Cell.CellToBoundary(cellId.Value, closedRing, segments);
    }

    /// <summary>
    /// Resolution encoded by <paramref name="cellId"/>: −1 for <see cref="WorldCell"/>, otherwise the
    /// cell's subdivision depth (0 through <see cref="MaxResolution"/>).
    /// </summary>
    /// <param name="cellId">The cell to inspect.</param>
    /// <returns>The cell's resolution.</returns>
    public static int GetResolution(A5CellId cellId)
    {
        return Serialization.GetResolution(cellId.Value);
    }

    /// <summary>Walks <paramref name="cellId"/> up the hierarchy to <paramref name="parentResolution"/> (default: one level up).</summary>
    /// <param name="cellId">The cell to find the ancestor of.</param>
    /// <param name="parentResolution">The target resolution; defaults to one level above <paramref name="cellId"/>'s own resolution.</param>
    /// <returns>The id of the ancestor cell at <paramref name="parentResolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="parentResolution"/> is less than −1 or greater than <see cref="MaxResolution"/>. The
    /// internal walker already throws the correctly typed exception, so this facade does not duplicate the
    /// check.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="cellId"/> is <see cref="WorldCell"/> and <paramref name="parentResolution"/>
    /// requests any resolution other than −1 (the world cell has no parent).
    /// </exception>
    public static A5CellId CellToParent(A5CellId cellId, int? parentResolution = null)
    {
        return new A5CellId(Serialization.CellToParent(cellId.Value, parentResolution));
    }

    /// <summary>Expands <paramref name="cellId"/> to every descendant at <paramref name="childResolution"/> (default: one level down).</summary>
    /// <param name="cellId">The cell to expand.</param>
    /// <param name="childResolution">The target resolution; defaults to one level below <paramref name="cellId"/>'s own resolution.</param>
    /// <returns>The ids of every descendant cell at <paramref name="childResolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="childResolution"/> is less than <paramref name="cellId"/>'s own resolution or
    /// greater than <see cref="MaxResolution"/>. The internal expander already throws the correctly typed
    /// exception, so this facade does not duplicate the check.
    /// </exception>
    public static A5CellId[] CellToChildren(A5CellId cellId, int? childResolution = null)
    {
        return WrapCellIds(Serialization.CellToChildren(cellId.Value, childResolution));
    }

    /// <summary>Returns the twelve resolution-0 cells, the starting point for all higher-resolution subdivisions.</summary>
    /// <returns>The twelve resolution-0 cell ids.</returns>
    public static A5CellId[] GetResolutionZeroCells()
    {
        return WrapCellIds(Serialization.GetResolutionZeroCells());
    }

    /// <summary>
    /// Number of cells at <paramref name="resolution"/>, as a <see cref="double"/>: exact for every valid
    /// resolution (0 through <see cref="MaxResolution"/>) — see <see cref="CellInfo.GetNumCells(int)"/>.
    /// Any negative resolution — including <see cref="WorldCell"/>'s −1 — returns zero, a deliberately
    /// preserved behavior this facade does not restrict further.
    /// </summary>
    /// <param name="resolution">The resolution to count cells at.</param>
    /// <returns>The number of cells at <paramref name="resolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> is greater than <see cref="MaxResolution"/>.</exception>
    public static double GetCellCount(int resolution)
    {
        ValidateResolutionUpperBound(resolution, nameof(resolution));

        return CellInfo.GetNumCells(resolution);
    }

    /// <summary>
    /// Number of cells at <paramref name="resolution"/>, as an exact <see cref="BigInteger"/>. Named
    /// distinctly from <see cref="GetCellCount(int)"/> because C# cannot overload on return type alone;
    /// the two are mathematically identical at every valid resolution.
    /// </summary>
    /// <param name="resolution">The resolution to count cells at.</param>
    /// <returns>The exact number of cells at <paramref name="resolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> is greater than <see cref="MaxResolution"/>.</exception>
    public static BigInteger GetCellCountExact(int resolution)
    {
        ValidateResolutionUpperBound(resolution, nameof(resolution));

        return CellInfo.GetNumCells((BigInteger)resolution);
    }

    /// <summary>Number of <paramref name="childResolution"/>-cells inside one <paramref name="parentResolution"/>-cell.</summary>
    /// <param name="parentResolution">The coarser resolution.</param>
    /// <param name="childResolution">The finer resolution.</param>
    /// <returns>The number of <paramref name="childResolution"/>-cells inside one <paramref name="parentResolution"/>-cell; zero if <paramref name="childResolution"/> is coarser than <paramref name="parentResolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="parentResolution"/> or <paramref name="childResolution"/> is greater than <see cref="MaxResolution"/>.
    /// </exception>
    public static double GetChildCount(int parentResolution, int childResolution)
    {
        ValidateResolutionUpperBound(parentResolution, nameof(parentResolution));
        ValidateResolutionUpperBound(childResolution, nameof(childResolution));

        return CellInfo.GetNumChildren(parentResolution, childResolution);
    }

    /// <summary>
    /// Area of a cell at <paramref name="resolution"/> in square meters. Any negative resolution —
    /// including <see cref="WorldCell"/>'s −1 — returns the whole authalic sphere's area, a deliberately
    /// preserved behavior this facade does not restrict further.
    /// </summary>
    /// <param name="resolution">The resolution to compute the area at.</param>
    /// <returns>The area of one cell at <paramref name="resolution"/>, in square meters.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> is greater than <see cref="MaxResolution"/>.</exception>
    public static double CellArea(int resolution)
    {
        ValidateResolutionUpperBound(resolution, nameof(resolution));

        return CellInfo.CellArea(resolution);
    }

    /// <summary>Replaces every complete sibling group in <paramref name="cellIds"/> with its parent, repeatedly, until no more groups compact.</summary>
    /// <param name="cellIds">The cell ids to compact; duplicates are removed.</param>
    /// <returns>The compacted cell ids, unsigned-64 ascending.</returns>
    public static A5CellId[] Compact(ReadOnlySpan<A5CellId> cellIds)
    {
        return WrapCellIds(Compaction.Compact(UnwrapCellIds(cellIds)));
    }

    /// <summary>Expands every cell in <paramref name="cellIds"/> to all of its descendants at <paramref name="targetResolution"/>.</summary>
    /// <param name="cellIds">The cell ids to expand.</param>
    /// <param name="targetResolution">The resolution to expand to; must not be coarser than any cell in <paramref name="cellIds"/>.</param>
    /// <returns>The expanded cell ids.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="targetResolution"/> is greater than <see cref="MaxResolution"/>, or is coarser than
    /// one of the cells in <paramref name="cellIds"/> (the internal expander already throws the correctly
    /// typed exception for the latter case, so this facade does not duplicate that check).
    /// </exception>
    public static A5CellId[] Uncompact(ReadOnlySpan<A5CellId> cellIds, int targetResolution)
    {
        ValidateResolutionUpperBound(targetResolution, nameof(targetResolution));

        return WrapCellIds(Compaction.Uncompact(UnwrapCellIds(cellIds), targetResolution));
    }

    /// <summary>Computes the grid disk of edge-sharing neighbors within <paramref name="k"/> hops of <paramref name="cellId"/>, including the center cell.</summary>
    /// <param name="cellId">The center cell.</param>
    /// <param name="k">
    /// The hop count. A negative value degenerates gracefully to the same result as <c>k = 0</c> rather
    /// than throwing; this is deliberate and not validated against.
    /// </param>
    /// <returns>A sorted, compacted array of cell ids in the disk.</returns>
    public static A5CellId[] GridDisk(A5CellId cellId, int k)
    {
        return WrapCellIds(GridDiskTraversal.GetGridDisk(cellId.Value, k));
    }

    /// <summary>Computes the grid disk of all neighbors (edge- and vertex-sharing) within <paramref name="k"/> hops of <paramref name="cellId"/>, including the center cell.</summary>
    /// <param name="cellId">The center cell.</param>
    /// <param name="k">
    /// The hop count. A negative value degenerates gracefully to the same result as <c>k = 0</c> rather
    /// than throwing; this is deliberate and not validated against.
    /// </param>
    /// <returns>A sorted, compacted array of cell ids in the disk.</returns>
    public static A5CellId[] GridDiskVertex(A5CellId cellId, int k)
    {
        return WrapCellIds(GridDiskTraversal.GetGridDiskVertex(cellId.Value, k));
    }

    /// <summary>Computes all cells within a great-circle radius of <paramref name="cellId"/>.</summary>
    /// <param name="cellId">The center cell; its own resolution is the resolution of the result.</param>
    /// <param name="radiusMeters">The radius, in meters.</param>
    /// <returns>A sorted, compacted array of cell ids at mixed resolutions.</returns>
    public static A5CellId[] SphericalCap(A5CellId cellId, double radiusMeters)
    {
        return WrapCellIds(SphericalCapTraversal.SphericalCap(cellId.Value, radiusMeters));
    }

    /// <summary>Traces cells along a polyline of great-circle-connected waypoints.</summary>
    /// <param name="waypoints">The polyline's waypoints, in geographic coordinates.</param>
    /// <param name="resolution">The resolution to trace cells at.</param>
    /// <returns>The unique cell ids touched by the polyline, in traversal order (not sorted).</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resolution"/> is outside [0, <see cref="MaxResolution"/>], or one of
    /// <paramref name="waypoints"/> has a non-finite longitude or latitude.
    /// </exception>
    public static A5CellId[] LineStringToCells(ReadOnlySpan<LonLat> waypoints, int resolution)
    {
        ValidateLonLatSpan(waypoints, nameof(waypoints));
        ValidateAreaFillResolution(resolution, nameof(resolution));

        return WrapCellIds(LineTraversal.LineStringToCells(waypoints, resolution));
    }

    /// <summary>Finds all cells within a ring (with no holes) whose center lies inside it.</summary>
    /// <param name="ring">
    /// The ring's vertices, in geographic coordinates. May be open or closed (GeoJSON-style, first vertex
    /// repeated at the end) — closure is automatic either way.
    /// </param>
    /// <param name="resolution">The resolution to fill cells at.</param>
    /// <returns>A sorted, compacted array of cell ids whose centers lie inside the ring; use <see cref="Uncompact"/> to expand to <paramref name="resolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resolution"/> is outside [0, <see cref="MaxResolution"/>], or one of
    /// <paramref name="ring"/>'s vertices has a non-finite longitude or latitude.
    /// </exception>
    public static A5CellId[] PolygonToCells(ReadOnlySpan<LonLat> ring, int resolution)
    {
        ValidateLonLatSpan(ring, nameof(ring));
        ValidateAreaFillResolution(resolution, nameof(resolution));

        return WrapCellIds(PolygonToCellsRegion.GetCells(CopyToArray(ring), resolution));
    }

    /// <summary>Finds all cells within a polygon with holes whose center lies inside the outer ring and outside every hole ring.</summary>
    /// <param name="rings">
    /// GeoJSON-style rings: <c>[outer, ...holes]</c>, in geographic coordinates. Rings may be open or
    /// closed (first vertex repeated at the end) — closure is automatic either way. Holes with fewer than
    /// 3 distinct vertices are ignored.
    /// </param>
    /// <param name="resolution">The resolution to fill cells at.</param>
    /// <returns>A sorted, compacted array of cell ids whose centers lie inside the polygon; use <see cref="Uncompact"/> to expand to <paramref name="resolution"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resolution"/> is outside [0, <see cref="MaxResolution"/>], or one of
    /// <paramref name="rings"/>'s vertices has a non-finite longitude or latitude.
    /// </exception>
    public static A5CellId[] PolygonToCells(ReadOnlySpan<LonLat[]> rings, int resolution)
    {
        foreach(LonLat[] ring in rings)
        {
            ValidateLonLatSpan(ring, nameof(rings));
        }

        ValidateAreaFillResolution(resolution, nameof(resolution));

        return WrapCellIds(PolygonToCellsRegion.GetCells(CopyRingsToArray(rings), resolution));
    }

    /// <summary>Reinterprets a raw cell-id span as public <see cref="A5CellId"/> values, copying into a freshly allocated array.</summary>
    private static A5CellId[] WrapCellIds(ReadOnlySpan<ulong> rawCellIds)
    {
        A5CellId[] cellIds = new A5CellId[rawCellIds.Length];
        MemoryMarshal.Cast<ulong, A5CellId>(rawCellIds).CopyTo(cellIds);

        return cellIds;
    }

    /// <summary>Reinterprets a public <see cref="A5CellId"/> span as raw cell ids, at zero cost (same layout, same length).</summary>
    private static ReadOnlySpan<ulong> UnwrapCellIds(ReadOnlySpan<A5CellId> cellIds)
    {
        return MemoryMarshal.Cast<A5CellId, ulong>(cellIds);
    }

    /// <summary>Copies a span of geographic points into a freshly allocated array, for internal APIs that require an array rather than a span.</summary>
    private static LonLat[] CopyToArray(ReadOnlySpan<LonLat> points)
    {
        LonLat[] array = new LonLat[points.Length];
        points.CopyTo(array);

        return array;
    }

    /// <summary>Copies a span of ring arrays into a freshly allocated jagged array, for internal APIs that require <c>LonLat[][]</c> rather than a span.</summary>
    private static LonLat[][] CopyRingsToArray(ReadOnlySpan<LonLat[]> rings)
    {
        LonLat[][] array = new LonLat[rings.Length][];
        for(int index = 0; index < rings.Length; index++)
        {
            array[index] = rings[index];
        }

        return array;
    }

    /// <summary>Validates a geographic point's coordinates are both finite — the facade's own posture, never delegated to the internal layer.</summary>
    private static void ValidateLonLat(LonLat point, string paramName)
    {
        if(!double.IsFinite(point.Longitude))
        {
            throw new ArgumentOutOfRangeException(paramName, point.Longitude, "Longitude must be a finite number.");
        }

        if(!double.IsFinite(point.Latitude))
        {
            throw new ArgumentOutOfRangeException(paramName, point.Latitude, "Latitude must be a finite number.");
        }
    }

    /// <summary>Validates every point in a span of geographic points; see <see cref="ValidateLonLat"/>.</summary>
    private static void ValidateLonLatSpan(ReadOnlySpan<LonLat> points, string paramName)
    {
        foreach(LonLat point in points)
        {
            ValidateLonLat(point, paramName);
        }
    }

    /// <summary>
    /// Validates a resolution accepted by the point-to-cell encoding pipeline (<see cref="LonLatToCell"/>):
    /// −1 (<see cref="WorldCell"/>) through <see cref="MaxResolution"/>. Below −1, the internal encoder
    /// does not itself guard the value and would silently produce a corrupted id, so this facade
    /// validates it explicitly.
    /// </summary>
    private static void ValidateEncodingResolution(int resolution, string paramName)
    {
        if(resolution < -1 || resolution > MaxResolution)
        {
            throw new ArgumentOutOfRangeException(paramName, resolution, $"Resolution must be between -1 (the world cell) and {MaxResolution} inclusive.");
        }
    }

    /// <summary>
    /// Validates a resolution accepted by the area-fill traversals (<see cref="LineStringToCells"/>,
    /// <see cref="PolygonToCells(ReadOnlySpan{LonLat}, int)"/>): 0 through <see cref="MaxResolution"/>,
    /// the same domain <see cref="SphericalCapTraversal.EstimateCellRadius"/> enforces internally, but
    /// enforced here too since it is not reached by every input shape (e.g. a single-waypoint line skips
    /// straight to <see cref="Cell.LonLatToCell"/> without an intervening cell-radius lookup).
    /// </summary>
    private static void ValidateAreaFillResolution(int resolution, string paramName)
    {
        if(resolution < 0 || resolution > MaxResolution)
        {
            throw new ArgumentOutOfRangeException(paramName, resolution, $"Resolution must be between 0 and {MaxResolution} inclusive.");
        }
    }

    /// <summary>
    /// Validates only the upper bound of a resolution accepted by the pure cell-info functions
    /// (<see cref="GetCellCount(int)"/>, <see cref="GetCellCountExact"/>, <see cref="CellArea"/>,
    /// <see cref="GetChildCount"/>): any negative value is a deliberately preserved, already-tested
    /// domain (every negative resolution behaves as <see cref="WorldCell"/> does), so only resolutions
    /// beyond the grid's actual depth are rejected here.
    /// </summary>
    private static void ValidateResolutionUpperBound(int resolution, string paramName)
    {
        if(resolution > MaxResolution)
        {
            throw new ArgumentOutOfRangeException(paramName, resolution, $"Resolution must not exceed {MaxResolution}.");
        }
    }
}
