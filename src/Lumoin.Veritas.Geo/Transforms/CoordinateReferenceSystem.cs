using System;

namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// A coordinate reference system recognized by the transform surface: a
/// closed roster value, never a parser result and never a free-form
/// identifier. The roster holds exactly three members — CRS84, EPSG:4326,
/// and Web Mercator (EPSG:3857) — reachable only through the roster
/// properties (<see cref="Crs84"/>, <see cref="Epsg4326"/>,
/// <see cref="WebMercator"/>) or through <see cref="TryFromIri"/>. There is
/// no way to construct an instance carrying a system outside the roster.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default value refuses.</b> The only state this struct stores is
/// <see cref="Kind"/>; <c>default(CoordinateReferenceSystem)</c> — which
/// zero-initialization always produces, with no reference-type state to
/// leave dangling — carries <see cref="CoordinateReferenceSystemKind.Unspecified"/>
/// and names no system. It is never a fabricated coordinate reference
/// system: every operation the transform surface offers refuses it. Because
/// no string is stored, <see cref="Iri"/> on the default value reads
/// <see cref="string.Empty"/> rather than <see langword="null"/> — there is
/// no null field anywhere in this type for a default value to expose.
/// </para>
/// <para>
/// <b>Axis order and units are the contract, not a convention.</b>
/// <see cref="AxisOrder"/> and <see cref="Unit"/> are computed, machine-
/// readable properties, not doc-comment prose: a coordinate span in a given
/// system is interpreted strictly in that system's declared axis order.
/// CRS84 and EPSG:4326 share the same geographic domain and degree unit
/// while disagreeing on ordinate order — the pair exists in the roster
/// precisely to carry that duality explicitly rather than by convention.
/// </para>
/// </remarks>
public readonly struct CoordinateReferenceSystem: IEquatable<CoordinateReferenceSystem>
{
    /// <summary>The canonical IRI recognized for CRS84.</summary>
    private const string Crs84Iri = "http://www.opengis.net/def/crs/OGC/1.3/CRS84";

    /// <summary>The canonical IRI recognized for EPSG:4326.</summary>
    private const string Epsg4326Iri = "http://www.opengis.net/def/crs/EPSG/0/4326";

    /// <summary>The canonical IRI recognized for EPSG:3857 (Web Mercator).</summary>
    private const string WebMercatorIri = "http://www.opengis.net/def/crs/EPSG/0/3857";

    /// <summary>
    /// The roster member this value names. The sole stored state of this
    /// type; every other property is computed from it.
    /// </summary>
    public CoordinateReferenceSystemKind Kind { get; }

    /// <summary>
    /// The declared axis order for <see cref="Kind"/>: longitude-latitude
    /// for CRS84, latitude-longitude for EPSG:4326, easting-northing for
    /// Web Mercator, and <see cref="CoordinateAxisOrder.Unspecified"/> for
    /// the default value.
    /// </summary>
    public CoordinateAxisOrder AxisOrder => Kind switch
    {
        CoordinateReferenceSystemKind.Crs84 => CoordinateAxisOrder.LongitudeLatitude,
        CoordinateReferenceSystemKind.Epsg4326 => CoordinateAxisOrder.LatitudeLongitude,
        CoordinateReferenceSystemKind.WebMercator => CoordinateAxisOrder.EastingNorthing,
        _ => CoordinateAxisOrder.Unspecified
    };

    /// <summary>
    /// The declared unit for <see cref="Kind"/>: degrees for the two
    /// geographic systems, metres for Web Mercator, and
    /// <see cref="CoordinateUnit.Unspecified"/> for the default value.
    /// </summary>
    public CoordinateUnit Unit => Kind switch
    {
        CoordinateReferenceSystemKind.Crs84 => CoordinateUnit.Degree,
        CoordinateReferenceSystemKind.Epsg4326 => CoordinateUnit.Degree,
        CoordinateReferenceSystemKind.WebMercator => CoordinateUnit.Metre,
        _ => CoordinateUnit.Unspecified
    };

    /// <summary>
    /// The canonical IRI for <see cref="Kind"/>, or <see cref="string.Empty"/>
    /// for the default value. Never <see langword="null"/> — no string field
    /// exists to be uninitialized, so this property is safe to read on any
    /// instance, including <c>default(CoordinateReferenceSystem)</c>.
    /// </summary>
    public string Iri => Kind switch
    {
        CoordinateReferenceSystemKind.Crs84 => Crs84Iri,
        CoordinateReferenceSystemKind.Epsg4326 => Epsg4326Iri,
        CoordinateReferenceSystemKind.WebMercator => WebMercatorIri,
        _ => string.Empty
    };

    /// <summary>The CRS84 roster member: longitude-latitude order, degrees.</summary>
    public static CoordinateReferenceSystem Crs84 { get; } = new(CoordinateReferenceSystemKind.Crs84);

    /// <summary>The EPSG:4326 roster member: latitude-longitude order, degrees.</summary>
    public static CoordinateReferenceSystem Epsg4326 { get; } = new(CoordinateReferenceSystemKind.Epsg4326);

    /// <summary>The Web Mercator (EPSG:3857) roster member: easting-northing order, metres.</summary>
    public static CoordinateReferenceSystem WebMercator { get; } = new(CoordinateReferenceSystemKind.WebMercator);

    /// <summary>Carries the roster member; only the static roster properties construct instances.</summary>
    /// <param name="kind">The roster member.</param>
    private CoordinateReferenceSystem(CoordinateReferenceSystemKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Recognizes <paramref name="iri"/> against exactly the three canonical
    /// roster spellings, by ordinal exact sequence equality: no trimming, no
    /// case folding, no urn: forms, no alias of any kind. On a match,
    /// <paramref name="coordinateReferenceSystem"/> is the matching roster
    /// member and this method returns <see langword="true"/>; otherwise
    /// <paramref name="coordinateReferenceSystem"/> is
    /// <c>default(CoordinateReferenceSystem)</c> and this method returns
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="iri">The candidate IRI.</param>
    /// <param name="coordinateReferenceSystem">The recognized roster member, or the default value.</param>
    /// <returns><see langword="true"/> when the IRI is one of the three canonical spellings.</returns>
    public static bool TryFromIri(ReadOnlySpan<char> iri, out CoordinateReferenceSystem coordinateReferenceSystem)
    {
        if(iri.SequenceEqual(Crs84Iri))
        {
            coordinateReferenceSystem = Crs84;

            return true;
        }

        if(iri.SequenceEqual(Epsg4326Iri))
        {
            coordinateReferenceSystem = Epsg4326;

            return true;
        }

        if(iri.SequenceEqual(WebMercatorIri))
        {
            coordinateReferenceSystem = WebMercator;

            return true;
        }

        coordinateReferenceSystem = default;

        return false;
    }

    /// <summary>Kind equality: two values are equal exactly when <see cref="Kind"/> matches.</summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the kinds match.</returns>
    public bool Equals(CoordinateReferenceSystem other)
    {
        return Kind == other.Kind;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is CoordinateReferenceSystem other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Kind.GetHashCode();
    }

    /// <summary>Kind equality, see <see cref="Equals(CoordinateReferenceSystem)"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the kinds match.</returns>
    public static bool operator ==(CoordinateReferenceSystem left, CoordinateReferenceSystem right)
    {
        return left.Equals(right);
    }

    /// <summary>Kind inequality, see <see cref="Equals(CoordinateReferenceSystem)"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the kinds differ.</returns>
    public static bool operator !=(CoordinateReferenceSystem left, CoordinateReferenceSystem right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// The canonical IRI for a roster member, or <see cref="Kind"/>'s name
    /// for the default value. Never throws, including on the default value.
    /// </summary>
    /// <returns>The rendering.</returns>
    public override string ToString()
    {
        return Kind == CoordinateReferenceSystemKind.Unspecified ? Kind.ToString() : Iri;
    }
}
