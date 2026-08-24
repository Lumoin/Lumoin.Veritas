using System;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Xml;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The projection face that answers one geometry literal's diagnosis: given a datatype IRI and a
/// literal body, whether the body stands, breaks its datatype's grammar, or is tolerated by that
/// grammar yet unreadable by the format's codec. The face reads only — it registers nothing, decides no
/// datatype semantics, and changes no operand seam verdict.
/// </summary>
/// <remarks>
/// <para>
/// The dispatch is staged, and severity falls out of the staging rather than out of a judgment table.
/// The datatype's LEXICAL layer runs first — exactly the layer its
/// <c>ValidateLexicalForm</c> consults. A lexically malformed body is INVALID, and its reason and
/// offending byte come from the format's codec reader run over the same body, or from the lexical
/// layer's own offset where the reader accepts what the lexical layer refused. A lexically well-formed
/// body and a lexical abstention alike run the codec reader: a body the reader reads is VALID, and a
/// body the reader refuses is a WARNING carrying the reader's own reason and byte, because the
/// validator tolerates it while no evaluation over it can succeed. A datatype whose stack ends at its
/// lexical layer answers VALID when that layer does not refuse — nothing is left to locate, and a
/// reason is never fabricated.
/// </para>
/// <para>
/// A blank body — the zero-length form included — denotes the empty geometry for the well-known-text,
/// GML, GeoJSON, and KML datatypes, decided ahead of the codec exactly as the operand seam decides it,
/// because no codec has a reading for one. The DGGS grammar gives a whitespace-only form no
/// interpretation, so only the exact zero-length form is empty there.
/// </para>
/// <para>
/// Every reported byte offset is relative to the WHOLE literal body. The well-known-text stack sees the
/// body after <see cref="WktCrsPrefix"/> strips an explicit CRS prefix, so the stripped prefix's width
/// is added back into each offset the stack reports.
/// </para>
/// </remarks>
public static class GeoLiteralDiagnostics
{
    /// <summary>The byte offset a refusal carries when no byte of the literal body is nameable.</summary>
    private const int UnlocatedOffset = -1;

    /// <summary>The diagnosis of a body that stands under its datatype; no reason accompanies it.</summary>
    private static GeoLiteralDiagnosis Standing { get; } = new(GeoLiteralDiagnosisStatus.Valid, GeometryCodecRefusal.None);

    /// <summary>The abstention a datatype outside this face's jurisdiction answers; no reason accompanies it.</summary>
    private static GeoLiteralDiagnosis Abstention { get; } = new(GeoLiteralDiagnosisStatus.UnsupportedDatatype, GeometryCodecRefusal.None);

    /// <summary>
    /// Diagnoses one geometry literal. The answered datatypes are <c>geo:wktLiteral</c>,
    /// <c>geo:gmlLiteral</c>, <c>geo:geoJSONLiteral</c>, <c>geo:kmlLiteral</c>, <c>geo:dggsLiteral</c>,
    /// and the house <c>a5Literal</c> subclass; every other datatype IRI answers
    /// <see cref="GeoLiteralDiagnosisStatus.UnsupportedDatatype"/> rather than a claim.
    /// </summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's lexical form as UTF-8 bytes.</param>
    /// <param name="geoJsonReader">
    /// The GeoJSON read binding. The Geo library holds no JSON tokenizer of its own, so the binding
    /// assembly's reader arrives as a parameter; a composing host already holds it to register the Geo
    /// function catalog.
    /// </param>
    /// <returns>The diagnosis.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geoJsonReader"/> is <see langword="null"/>.</exception>
    public static GeoLiteralDiagnosis Describe(Utf8String datatypeIri, ReadOnlySpan<byte> body, GeoJsonGeometryReadDelegate geoJsonReader)
    {
        ArgumentNullException.ThrowIfNull(geoJsonReader);

        ReadOnlySpan<byte> iri = datatypeIri.Span;
        if(iri.SequenceEqual(GeoVocabulary.Geo.WktLiteral.Span))
        {
            return DescribeWkt(body);
        }

        if(iri.SequenceEqual(GeoVocabulary.Geo.GmlLiteral.Span))
        {
            return DescribeGml(body);
        }

        if(iri.SequenceEqual(GeoVocabulary.Geo.GeoJsonLiteral.Span))
        {
            return DescribeGeoJson(body, geoJsonReader);
        }

        if(iri.SequenceEqual(GeoVocabulary.Geo.KmlLiteral.Span))
        {
            return DescribeKml(body);
        }

        if(iri.SequenceEqual(GeoVocabulary.Geo.DggsLiteral.Span))
        {
            return DescribeDggs(body);
        }

        if(iri.SequenceEqual(A5DggsVocabulary.DatatypeIri.Span))
        {
            return DescribeA5Dggs(body);
        }

        return Abstention;
    }

    /// <summary>
    /// Diagnoses a <c>geo:wktLiteral</c> body: the CRS prefix decomposes first, then the well-known-text
    /// stack runs over the geometry body alone, and every offset the stack reports is re-based onto the
    /// whole literal. A body whose prefix structure is broken is invalid with no nameable byte, because
    /// the decomposition reports no cursor.
    /// </summary>
    /// <param name="body">The whole literal body.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis DescribeWkt(ReadOnlySpan<byte> body)
    {
        if(!WktCrsPrefix.TryParse(new Utf8String(body.ToArray()), out WktCrsPrefix decomposition))
        {
            return new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, UnlocatedOffset));
        }

        //The decomposition's body always runs to the end of the literal, so its width names where it starts.
        ReadOnlySpan<byte> geometryBody = decomposition.Body.Span;
        int prefixWidth = body.Length - geometryBody.Length;
        GeometryLexicalRecognition recognition = WktLexical.Recognize(geometryBody, out int lexicalOffset);
        if(recognition != GeometryLexicalRecognition.Malformed && geometryBody.IsEmpty)
        {
            return Standing;
        }

        bool read = WktGeometryReader.TryRead(geometryBody, FlatGeometryAllocators.Default, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        if(read)
        {
            geometry.Dispose();
        }

        GeoLiteralDiagnosis diagnosis = Decide(recognition, read, refusal, lexicalOffset);

        return new GeoLiteralDiagnosis(diagnosis.Status, ReBase(diagnosis.Refusal, prefixWidth));
    }

    /// <summary>Diagnoses a <c>geo:gmlLiteral</c> body through the GML lexical layer and the GML reader.</summary>
    /// <param name="body">The whole literal body.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis DescribeGml(ReadOnlySpan<byte> body)
    {
        GeometryLexicalRecognition recognition = GmlLexical.Recognize(body);
        if(recognition != GeometryLexicalRecognition.Malformed && IsBlankBody(body))
        {
            return Standing;
        }

        bool read = GmlGeometryReader.TryRead(body, FlatGeometryAllocators.Default, out FlatGeometry geometry, out _, out GeometryCodecRefusal refusal);
        if(read)
        {
            geometry.Dispose();
        }

        return Decide(recognition, read, refusal, UnlocatedOffset);
    }

    /// <summary>Diagnoses a <c>geo:geoJSONLiteral</c> body through the GeoJSON lexical layer and the bound reader.</summary>
    /// <param name="body">The whole literal body.</param>
    /// <param name="geoJsonReader">The GeoJSON read binding.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis DescribeGeoJson(ReadOnlySpan<byte> body, GeoJsonGeometryReadDelegate geoJsonReader)
    {
        GeometryLexicalRecognition recognition = GeoJsonLexical.Recognize(body);
        if(recognition != GeometryLexicalRecognition.Malformed && IsBlankBody(body))
        {
            return Standing;
        }

        bool read = geoJsonReader(body, FlatGeometryAllocators.Default, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        if(read)
        {
            geometry.Dispose();
        }

        return Decide(recognition, read, refusal, UnlocatedOffset);
    }

    /// <summary>Diagnoses a <c>geo:kmlLiteral</c> body through the KML lexical layer and the KML reader.</summary>
    /// <param name="body">The whole literal body.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis DescribeKml(ReadOnlySpan<byte> body)
    {
        GeometryLexicalRecognition recognition = KmlLexical.Recognize(body);
        if(recognition != GeometryLexicalRecognition.Malformed && IsBlankBody(body))
        {
            return Standing;
        }

        bool read = KmlGeometryReader.TryRead(body, FlatGeometryAllocators.Default, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        if(read)
        {
            geometry.Dispose();
        }

        return Decide(recognition, read, refusal, UnlocatedOffset);
    }

    /// <summary>
    /// Diagnoses a <c>geo:dggsLiteral</c> body. The datatype's stack ends at its lexical layer: a
    /// malformed form is invalid at the located byte, and the foreign-grid abstention stands, because
    /// that data is formulated according to the grid its IRI names and this face holds no reader for it.
    /// </summary>
    /// <param name="body">The whole literal body.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis DescribeDggs(ReadOnlySpan<byte> body)
    {
        return DggsLexical.Recognize(body, out int offendingOffset) == GeometryLexicalRecognition.Malformed
            ? new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, offendingOffset))
            : Standing;
    }

    /// <summary>
    /// Diagnoses a house <c>a5Literal</c> body. The subclass names the specific grid, so its whole
    /// grammar is certified: the empty form stands, and a broken prefix, a foreign grid IRI, and a
    /// non-conformant cell body are each invalid at the located byte.
    /// </summary>
    /// <param name="body">The whole literal body.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis DescribeA5Dggs(ReadOnlySpan<byte> body)
    {
        if(body.IsEmpty)
        {
            return Standing;
        }

        if(!DggsLexical.TryDecompose(body, out Range iriRegion, out Range dataRegion, out int prefixOffset))
        {
            return new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, prefixOffset));
        }

        if(!body[iriRegion].SequenceEqual(A5DggsVocabulary.GridIri.Span))
        {
            return new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, iriRegion.Start.Value));
        }

        if(!A5DggsBody.Certify(body[dataRegion], out int dataOffset))
        {
            return new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, dataRegion.Start.Value + dataOffset));
        }

        return Standing;
    }

    /// <summary>
    /// Decides the staged verdict once a datatype's lexical layer and codec reader have both answered
    /// over the same body: a lexically malformed body is invalid, carrying the reader's located reason
    /// where the reader also refused and the lexical layer's own offset where it did not; a read body
    /// stands; and a refused body the lexical layer tolerated is a warning carrying the reader's reason.
    /// </summary>
    /// <param name="recognition">The lexical layer's answer.</param>
    /// <param name="read">Whether the codec reader read the body.</param>
    /// <param name="refusal">The reader's refusal, meaningful only when <paramref name="read"/> is <see langword="false"/>.</param>
    /// <param name="lexicalOffset">The lexical layer's offending offset, or minus one where that layer reports none.</param>
    /// <returns>The diagnosis, with offsets still relative to the body both layers saw.</returns>
    private static GeoLiteralDiagnosis Decide(GeometryLexicalRecognition recognition, bool read, GeometryCodecRefusal refusal, int lexicalOffset)
    {
        if(recognition == GeometryLexicalRecognition.Malformed)
        {
            return new GeoLiteralDiagnosis(
                GeoLiteralDiagnosisStatus.Invalid,
                read ? new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, lexicalOffset) : refusal);
        }

        return read ? Standing : new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Warning, refusal);
    }

    /// <summary>
    /// Moves a refusal's byte offset from a stripped body onto the whole literal. An unlocated refusal
    /// stays unlocated — minus one names no byte in either frame.
    /// </summary>
    /// <param name="refusal">The refusal to re-base.</param>
    /// <param name="prefixWidth">The width of the text stripped ahead of the body the refusal saw.</param>
    /// <returns>The re-based refusal.</returns>
    private static GeometryCodecRefusal ReBase(GeometryCodecRefusal refusal, int prefixWidth)
    {
        return refusal.ByteOffset < 0
            ? refusal
            : new GeometryCodecRefusal(refusal.Kind, refusal.ByteOffset + prefixWidth);
    }

    /// <summary>
    /// Whether a serialization body carries no content: the zero-length form and the all-whitespace
    /// form alike denote the empty geometry, the reading the operand seam takes ahead of every codec.
    /// </summary>
    /// <param name="body">The literal body.</param>
    /// <returns><see langword="true"/> when the body is empty or all whitespace.</returns>
    private static bool IsBlankBody(ReadOnlySpan<byte> body)
    {
        for(int index = 0; index < body.Length; index++)
        {
            if(!WktLexical.IsWhitespace(body[index]))
            {
                return false;
            }
        }

        return true;
    }
}
