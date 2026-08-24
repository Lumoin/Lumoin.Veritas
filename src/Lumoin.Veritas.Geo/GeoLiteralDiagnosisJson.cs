using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// Renders one geometry-literal diagnosis as the wire document every tier answers with:
/// <c>{"status":…,"kind":…,"byteOffset":…,"datatype":…}</c>. The refusal fields ride exactly the two statuses
/// that locate one — <c>warning</c> and <c>invalid</c> — so a reader never has to interpret a placeholder
/// kind, and the datatype is echoed as it arrived, escaped straight from its UTF-8 bytes. This is the
/// editor's wire shape, not a canonical format; the caller runs
/// <see cref="GeoLiteralDiagnostics.Describe"/> itself and hands the answer here.
/// </summary>
public static class GeoLiteralDiagnosisJson
{
    /// <summary>The Unicode replacement character, written in place of an ill-formed UTF-8 sequence when byte-native text is escaped into the document.</summary>
    private const char UnicodeReplacementCharacter = (char)0xFFFD;

    /// <summary>Renders one diagnosis as the literal-diagnosis JSON document.</summary>
    /// <param name="datatypeIri">The described literal's datatype IRI.</param>
    /// <param name="diagnosis">The diagnosis to render.</param>
    /// <returns>The JSON diagnosis document.</returns>
    public static string Write(Utf8String datatypeIri, in GeoLiteralDiagnosis diagnosis)
    {
        StringBuilder builder = new();
        builder.Append("{\"status\":\"");
        builder.Append(StatusToken(diagnosis.Status));
        builder.Append('"');
        if(diagnosis.Status is GeoLiteralDiagnosisStatus.Warning or GeoLiteralDiagnosisStatus.Invalid)
        {
            builder.Append(",\"kind\":\"");
            builder.Append(KindToken(diagnosis.Refusal.Kind));
            builder.Append("\",\"byteOffset\":");
            builder.Append(diagnosis.Refusal.ByteOffset.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(",\"datatype\":\"");
        AppendJsonEscaped(builder, datatypeIri.Span);
        builder.Append("\"}");

        return builder.ToString();
    }

    /// <summary>Names one diagnosis status on the wire: the abstention answers <c>unsupported</c>, and the three describing states answer their own lowercase token.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The wire token.</returns>
    private static string StatusToken(GeoLiteralDiagnosisStatus status)
    {
        return status switch
        {
            GeoLiteralDiagnosisStatus.Valid => "valid",
            GeoLiteralDiagnosisStatus.Warning => "warning",
            GeoLiteralDiagnosisStatus.Invalid => "invalid",
            _ => "unsupported"
        };
    }

    /// <summary>Names one codec refusal kind on the wire. The tokens are the roster's own member names, taken through <c>nameof</c> so the closed set and the wire vocabulary can never drift apart.</summary>
    /// <param name="kind">The refusal kind to name.</param>
    /// <returns>The wire token.</returns>
    private static string KindToken(GeometryCodecRefusalKind kind)
    {
        return kind switch
        {
            GeometryCodecRefusalKind.MalformedDocument => nameof(GeometryCodecRefusalKind.MalformedDocument),
            GeometryCodecRefusalKind.ProhibitedConstruct => nameof(GeometryCodecRefusalKind.ProhibitedConstruct),
            GeometryCodecRefusalKind.UnsupportedGeometry => nameof(GeometryCodecRefusalKind.UnsupportedGeometry),
            GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem => nameof(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem),
            GeometryCodecRefusalKind.DimensionMismatch => nameof(GeometryCodecRefusalKind.DimensionMismatch),
            GeometryCodecRefusalKind.NonFiniteCoordinate => nameof(GeometryCodecRefusalKind.NonFiniteCoordinate),
            GeometryCodecRefusalKind.StructuralViolation => nameof(GeometryCodecRefusalKind.StructuralViolation),
            GeometryCodecRefusalKind.NestingTooDeep => nameof(GeometryCodecRefusalKind.NestingTooDeep),
            GeometryCodecRefusalKind.TrailingContent => nameof(GeometryCodecRefusalKind.TrailingContent),
            GeometryCodecRefusalKind.MeasureUnrepresentable => nameof(GeometryCodecRefusalKind.MeasureUnrepresentable),
            GeometryCodecRefusalKind.EmptyUnrepresentable => nameof(GeometryCodecRefusalKind.EmptyUnrepresentable),
            _ => nameof(GeometryCodecRefusalKind.None)
        };
    }

    /// <summary>
    /// Appends <paramref name="utf8Text"/> JSON-string-escaped to <paramref name="builderToAppendTo"/>,
    /// decoding the UTF-8 bytes straight into the escaped output so byte-native text — a datatype IRI held as
    /// UTF-8 — reaches the document without a managed-string round-trip. An ill-formed sequence is replaced
    /// rather than copied, so a caller carrying invalid UTF-8 still gets a well-formed JSON document.
    /// </summary>
    /// <param name="builderToAppendTo">The builder the escaped text is appended to.</param>
    /// <param name="utf8Text">The UTF-8 text to escape.</param>
    private static void AppendJsonEscaped(StringBuilder builderToAppendTo, ReadOnlySpan<byte> utf8Text)
    {
        Span<char> utf16 = stackalloc char[2];
        ReadOnlySpan<byte> remaining = utf8Text;
        while(!remaining.IsEmpty)
        {
            if(Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                //The decoder always reports the length of the sequence it rejected, so the walk advances.
                builderToAppendTo.Append(UnicodeReplacementCharacter);
                remaining = remaining[consumed..];

                continue;
            }

            int units = rune.EncodeToUtf16(utf16);
            for(int i = 0; i < units; i++)
            {
                AppendJsonEscaped(builderToAppendTo, utf16[i]);
            }

            remaining = remaining[consumed..];
        }
    }

    /// <summary>Appends one character JSON-string-escaped to <paramref name="builderToAppendTo"/>: the named escapes for the forms JSON spells that way, the six-character form for the remaining control characters, and the character itself otherwise.</summary>
    /// <param name="builderToAppendTo">The builder the escaped character is appended to.</param>
    /// <param name="character">The character to escape.</param>
    private static void AppendJsonEscaped(StringBuilder builderToAppendTo, char character)
    {
        builderToAppendTo.Append(character switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
            _ => character.ToString()
        });
    }
}
