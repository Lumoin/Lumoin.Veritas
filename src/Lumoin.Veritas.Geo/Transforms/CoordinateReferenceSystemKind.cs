namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// The closed roster of coordinate reference systems the transform surface
/// recognizes. <see cref="Unspecified"/> is the value of
/// <c>default(CoordinateReferenceSystemKind)</c> and therefore also of an
/// uninitialized <see cref="CoordinateReferenceSystem"/>; it names no system,
/// so a forgotten assignment can never silently stand in for a recognized
/// coordinate reference system — the transform surface always refuses it.
/// </summary>
public enum CoordinateReferenceSystemKind
{
    /// <summary>
    /// No coordinate reference system. The default-struct state of
    /// <see cref="CoordinateReferenceSystem"/>; every operation the transform
    /// surface offers refuses a value carrying this kind.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// CRS84, the OGC's longitude-first WGS 84 geographic coordinate
    /// reference system, expressed in degrees.
    /// </summary>
    Crs84 = 1,

    /// <summary>
    /// EPSG:4326, the EPSG registry's latitude-first WGS 84 geographic
    /// coordinate reference system, expressed in degrees.
    /// </summary>
    Epsg4326 = 2,

    /// <summary>
    /// EPSG:3857, Web Mercator: the spherical pseudo-Mercator projection of
    /// WGS 84, expressed in metres in easting-northing order.
    /// </summary>
    WebMercator = 3
}
