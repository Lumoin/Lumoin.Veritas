using System;
using Lumoin.Veritas.Geo.Transforms;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The codec-local coordinate-reference-system recognition roster: the three
/// canonical HTTP IRIs the transform surface recognizes plus their urn twins — six
/// spellings, matched ordinally over decoded attribute bytes, no case folding, no
/// trimming, no aliases. Recognition reports the matched spelling's index as well as
/// the system, because a nested declaration must repeat the effective root SPELLING
/// byte for byte, and the root's own attribute span is long dead by the time a
/// nested element arrives — the closed roster makes the spelling one of six
/// constants a stored index recovers. The transform surface's own recognition stays
/// exactly three HTTP spellings; the urn twins live here and never leak out of the
/// codec. The unit roster rules the radius of a center-and-radius circle: the unit
/// must spell the document system's own horizontal unit, from a closed set, and
/// everything else — a recognized unit under the wrong system included — is
/// unrecognized, because honoring a metre radius in a degree plane would demand
/// geodesy this value codec refuses to guess at.
/// </summary>
internal static class GmlSrsName
{
    /// <summary>The number of recognized system spellings.</summary>
    public const int SpellingCount = 6;

    /// <summary>The canonical HTTP IRI of CRS84, spelling index zero.</summary>
    public static ReadOnlySpan<byte> Crs84Iri => "http://www.opengis.net/def/crs/OGC/1.3/CRS84"u8;

    /// <summary>The canonical HTTP IRI of EPSG:4326, spelling index one.</summary>
    public static ReadOnlySpan<byte> Epsg4326Iri => "http://www.opengis.net/def/crs/EPSG/0/4326"u8;

    /// <summary>The canonical HTTP IRI of Web Mercator, spelling index two.</summary>
    public static ReadOnlySpan<byte> WebMercatorIri => "http://www.opengis.net/def/crs/EPSG/0/3857"u8;

    /// <summary>The urn spelling of CRS84, spelling index three.</summary>
    public static ReadOnlySpan<byte> Crs84Urn => "urn:ogc:def:crs:OGC:1.3:CRS84"u8;

    /// <summary>The urn spelling of EPSG:4326, spelling index four.</summary>
    public static ReadOnlySpan<byte> Epsg4326Urn => "urn:ogc:def:crs:EPSG::4326"u8;

    /// <summary>The urn spelling of Web Mercator, spelling index five.</summary>
    public static ReadOnlySpan<byte> WebMercatorUrn => "urn:ogc:def:crs:EPSG::3857"u8;

    /// <summary>The compact metre unit symbol.</summary>
    public static ReadOnlySpan<byte> MetreSymbol => "m"u8;

    /// <summary>The urn spelling of the metre unit.</summary>
    public static ReadOnlySpan<byte> MetreUrn => "urn:ogc:def:uom:EPSG::9001"u8;

    /// <summary>The HTTP spelling of the metre unit.</summary>
    public static ReadOnlySpan<byte> MetreIri => "http://www.opengis.net/def/uom/EPSG/0/9001"u8;

    /// <summary>The compact degree unit symbol.</summary>
    public static ReadOnlySpan<byte> DegreeSymbol => "deg"u8;

    /// <summary>The urn spelling of the degree unit.</summary>
    public static ReadOnlySpan<byte> DegreeUrn => "urn:ogc:def:uom:EPSG::9102"u8;

    /// <summary>The HTTP spelling of the degree unit.</summary>
    public static ReadOnlySpan<byte> DegreeIri => "http://www.opengis.net/def/uom/EPSG/0/9102"u8;

    /// <summary>
    /// Recognizes a decoded attribute value against the six-spelling roster,
    /// reporting the system and the matched spelling's index. False leaves the
    /// system at its default — which names no system — and the index at minus one.
    /// </summary>
    public static bool TryRecognize(ReadOnlySpan<byte> decodedValue, out CoordinateReferenceSystem coordinateReferenceSystem, out int spellingIndex)
    {
        for(int index = 0; index < SpellingCount; index++)
        {
            if(decodedValue.SequenceEqual(SpellingAt(index)))
            {
                coordinateReferenceSystem = SystemAt(index);
                spellingIndex = index;

                return true;
            }
        }

        coordinateReferenceSystem = default;
        spellingIndex = -1;

        return false;
    }

    /// <summary>
    /// The roster spelling at an index — the constant a stored index recovers so a
    /// nested declaration can compare byte for byte against the effective root
    /// spelling after the root's own span has died.
    /// </summary>
    public static ReadOnlySpan<byte> SpellingAt(int index)
    {
        return index switch
        {
            0 => Crs84Iri,
            1 => Epsg4326Iri,
            2 => WebMercatorIri,
            3 => Crs84Urn,
            4 => Epsg4326Urn,
            5 => WebMercatorUrn,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "The spelling index must name one of the six roster spellings."),
        };
    }

    /// <summary>
    /// The canonical HTTP IRI the writer emits for a roster system. The writer
    /// refuses the default value before emission, so only the three named systems
    /// arrive here; anything else is an internal defect.
    /// </summary>
    public static ReadOnlySpan<byte> CanonicalIriOf(CoordinateReferenceSystem coordinateReferenceSystem)
    {
        if(coordinateReferenceSystem == CoordinateReferenceSystem.Crs84)
        {
            return Crs84Iri;
        }

        if(coordinateReferenceSystem == CoordinateReferenceSystem.Epsg4326)
        {
            return Epsg4326Iri;
        }

        if(coordinateReferenceSystem == CoordinateReferenceSystem.WebMercator)
        {
            return WebMercatorIri;
        }

        throw new InvalidOperationException("The coordinate reference system names no roster member; the writer validates before emission.");
    }

    /// <summary>
    /// Whether a decoded unit-of-measure value spells the document system's own
    /// horizontal unit, from the closed unit roster. A recognized unit under the
    /// wrong system fails exactly like an unknown token — one rule, one refusal.
    /// </summary>
    public static bool UnitMatches(ReadOnlySpan<byte> decodedValue, CoordinateReferenceSystem coordinateReferenceSystem)
    {
        if(coordinateReferenceSystem.Unit == CoordinateUnit.Metre)
        {
            return decodedValue.SequenceEqual(MetreSymbol)
                || decodedValue.SequenceEqual(MetreUrn)
                || decodedValue.SequenceEqual(MetreIri);
        }

        if(coordinateReferenceSystem.Unit == CoordinateUnit.Degree)
        {
            return decodedValue.SequenceEqual(DegreeSymbol)
                || decodedValue.SequenceEqual(DegreeUrn)
                || decodedValue.SequenceEqual(DegreeIri);
        }

        return false;
    }

    /// <summary>The roster system a spelling index names.</summary>
    private static CoordinateReferenceSystem SystemAt(int index)
    {
        return index switch
        {
            0 or 3 => CoordinateReferenceSystem.Crs84,
            1 or 4 => CoordinateReferenceSystem.Epsg4326,
            _ => CoordinateReferenceSystem.WebMercator,
        };
    }
}
