namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// The measurement unit a coordinate reference system's ordinates are
/// expressed in — part of the machine-readable per-CRS contract alongside
/// <see cref="CoordinateAxisOrder"/>, never left to doc-comment prose.
/// </summary>
public enum CoordinateUnit
{
    /// <summary>
    /// No declared unit. The default-struct state, carried by
    /// <see cref="CoordinateReferenceSystemKind.Unspecified"/>.
    /// </summary>
    Unspecified = 0,

    /// <summary>Degrees — the unit of the geographic systems, CRS84 and EPSG:4326.</summary>
    Degree = 1,

    /// <summary>Metres — the unit of the projected system, Web Mercator.</summary>
    Metre = 2
}
