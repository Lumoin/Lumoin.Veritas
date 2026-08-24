namespace Lumoin.Veritas.Geo;

/// <summary>
/// Well-known IRI constants from the OGC units-of-measure register that the unit-parameterized
/// <c>geof:</c> functions recognize. The GeoSPARQL 1.1 specification types the units argument as
/// <c>xsd:anyURI</c> without normatively enumerating the permitted IRIs; the house recognizes exactly this
/// register's linear units, and an unrecognized units IRI makes the invocation answer the expression error
/// value rather than a magnitude in an unverified unit.
/// </summary>
public static class OgcUnitsOfMeasure
{
    /// <summary>The OGC units-of-measure register namespace IRI.</summary>
    public const string Namespace = "http://www.opengis.net/def/uom/OGC/1.0/";

    /// <summary>The metre IRI bytes.</summary>
    private static byte[] MetreBytes { get; } = "http://www.opengis.net/def/uom/OGC/1.0/metre"u8.ToArray();

    /// <summary>The degree IRI bytes.</summary>
    private static byte[] DegreeBytes { get; } = "http://www.opengis.net/def/uom/OGC/1.0/degree"u8.ToArray();

    /// <summary>The metre unit IRI — the unit the <c>metric*</c> function family answers in.</summary>
    public static Utf8String Metre { get; } = new(MetreBytes);

    /// <summary>The degree unit IRI — the coordinate unit of the CRS84 default coordinate reference system.</summary>
    public static Utf8String Degree { get; } = new(DegreeBytes);
}
