namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// The declared ordinate order a coordinate reference system's spans are
/// read in. This is the machine-readable half of the CRS84 ↔ EPSG:4326
/// duality: the two systems share a geographic domain and a unit while
/// disagreeing on which ordinate a span carries first, and a coordinate
/// span is always interpreted per its declared <see cref="CoordinateAxisOrder"/>
/// rather than by any informal convention.
/// </summary>
public enum CoordinateAxisOrder
{
    /// <summary>
    /// No declared axis order. The default-struct state, carried by
    /// <see cref="CoordinateReferenceSystemKind.Unspecified"/>.
    /// </summary>
    Unspecified = 0,

    /// <summary>Longitude first, then latitude — CRS84's declared order.</summary>
    LongitudeLatitude = 1,

    /// <summary>Latitude first, then longitude — EPSG:4326's declared order.</summary>
    LatitudeLongitude = 2,

    /// <summary>Easting first, then northing — Web Mercator's declared order.</summary>
    EastingNorthing = 3
}
