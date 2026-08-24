using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// Decodes SPARQL 1.2 numeric codepoint escapes (<c>\uXXXX</c> and <c>\UXXXXXXXX</c>) over the lexer
/// input, ahead of tokenisation.
/// </summary>
/// <remarks>
/// <para>
/// SPARQL 1.2 §19.2 treats codepoint escapes as a rewrite of the whole character stream: they are
/// decoded everywhere — inside tokens, in the whitespace between tokens, and even where the decoded
/// codepoint is itself a structural character (a codepoint-escaped quote opens a string literal). Only
/// the decoded stream is tokenised. This decoder is therefore the single site that interprets
/// <c>\u</c>/<c>\U</c>; the tokeniser sees only decoded bytes and never a numeric escape.
/// </para>
/// <para>
/// Decoding precedes string-escape (<c>ECHAR</c>) interpretation. On a backslash the next byte decides:
/// <c>u</c>/<c>U</c> introduces a numeric escape that is consumed whole and replaced by its codepoint's
/// UTF-8 bytes; any other byte (including a second backslash) is passed through literally and the scan
/// advances a single byte, so the source <c>\\u0074</c> yields <c>\</c> followed by the decode of
/// <c>t</c> (<c>t</c>), i.e. the string escape <c>\t</c>. A malformed numeric escape (bad hex
/// digit, surrogate, out-of-range, or truncated at end of input) is recorded as a diagnostic and dropped.
/// </para>
/// <para>
/// Alongside the decoded bytes the decoder builds an offset map from each decoded byte boundary back to
/// the source position (byte offset, line, column) it originated from. Every byte produced by one escape
/// maps to that escape's source start, so a token or diagnostic span built over decoded positions reports
/// accurate <i>source</i> coordinates — preserving the lexer contract that a token's payload is the
/// decoded value while its <see cref="SourceSpan"/> covers the original source bytes.
/// </para>
/// <para>
/// The scan is iterative (no recursion) and incremental: <see cref="Feed"/> may be called repeatedly with
/// successive source spans. When a span ends mid-escape and more input may follow, the trailing partial
/// escape is left unconsumed (reported through the returned consumed count) for the caller to re-present.
/// </para>
/// </remarks>
internal sealed class SparqlCodepointDecoder
{
    private byte[] decoded = new byte[64];
    private int[] sourceOffsets = new int[64];
    private int[] sourceLines = new int[64];
    private int[] sourceColumns = new int[64];
    private int decodedLength;
    private int sourceOffset;
    private int sourceLine;
    private int sourceColumn;
    private bool pendingCarriageReturn;
    private readonly List<SparqlLexDiagnostic> diagnostics = [];

    /// <summary>Gets the decoded UTF-8 bytes produced so far.</summary>
    public ReadOnlyMemory<byte> Decoded => decoded.AsMemory(0, decodedLength);

    /// <summary>Gets the number of decoded bytes produced so far.</summary>
    public int DecodedLength => decodedLength;

    /// <summary>Gets the diagnostics recorded for malformed numeric escapes, in source order.</summary>
    public IReadOnlyList<SparqlLexDiagnostic> Diagnostics => diagnostics;

    /// <summary>
    /// Decodes the supplied source span, appending decoded bytes and extending the offset map.
    /// </summary>
    /// <param name="source">The source UTF-8 bytes to decode.</param>
    /// <param name="isFinal">
    /// <see langword="true"/> when no further input will follow this span; a trailing partial escape is
    /// then resolved (a lone backslash passes through; an incomplete numeric escape is a diagnostic).
    /// When <see langword="false"/> a trailing partial escape is left unconsumed.
    /// </param>
    /// <returns>The number of source bytes consumed; less than the span length only when a partial escape was held.</returns>
    public int Feed(ReadOnlySpan<byte> source, bool isFinal)
    {
        int i = 0;

        while(i < source.Length)
        {
            byte b = source[i];

            if(b != (byte)'\\')
            {
                Emit(b, sourceOffset, sourceLine, sourceColumn);
                AdvanceSource(b);
                i++;

                continue;
            }

            if(i + 1 >= source.Length)
            {
                if(!isFinal)
                {
                    return i;
                }

                //A lone trailing backslash at end of input is not a numeric escape; pass it through and
                //let the tokeniser judge it in context.
                Emit(b, sourceOffset, sourceLine, sourceColumn);
                AdvanceSource(b);
                i++;

                continue;
            }

            int hexCount = source[i + 1] switch
            {
                (byte)'u' => 4,
                (byte)'U' => 8,
                _ => -1
            };

            if(hexCount < 0)
            {
                //Backslash not followed by u/U: emit it literally and advance one byte so the following
                //byte (which may itself begin a numeric escape) is reconsidered.
                Emit(b, sourceOffset, sourceLine, sourceColumn);
                AdvanceSource(b);
                i++;

                continue;
            }

            int escapeLength = 2 + hexCount;

            if(i + escapeLength > source.Length)
            {
                if(!isFinal)
                {
                    return i;
                }

                int remaining = source.Length - i;
                RecordDiagnostic(SparqlLexErrorCode.TruncatedEscape, sourceOffset, remaining, detail: null);
                ConsumeSource(source[i..]);
                i = source.Length;

                continue;
            }

            int escapeStartOffset = sourceOffset;
            int escapeStartLine = sourceLine;
            int escapeStartColumn = sourceColumn;
            uint codepoint = 0;
            byte badHex = 0;
            bool validHex = true;

            for(int h = 0; h < hexCount; h++)
            {
                byte hex = source[i + 2 + h];
                if(!TryHexValue(hex, out uint digit))
                {
                    badHex = hex;
                    validHex = false;

                    break;
                }

                codepoint = (codepoint << 4) | digit;
            }

            if(!validHex)
            {
                RecordDiagnostic(SparqlLexErrorCode.InvalidHexDigit, escapeStartOffset, escapeLength, ((char)badHex).ToString());
            }
            else if(codepoint >= (uint)UnicodeConstants.SurrogateRangeFirst && codepoint <= (uint)UnicodeConstants.SurrogateRangeLast)
            {
                RecordDiagnostic(SparqlLexErrorCode.SurrogateCodePoint, escapeStartOffset, escapeLength, FormatCodePoint(codepoint));
            }
            else if(codepoint > (uint)UnicodeConstants.MaximumCodePoint)
            {
                RecordDiagnostic(SparqlLexErrorCode.CodePointOutOfRange, escapeStartOffset, escapeLength, FormatCodePoint(codepoint));
            }
            else
            {
                EmitCodepoint(codepoint, escapeStartOffset, escapeStartLine, escapeStartColumn);
            }

            ConsumeSource(source.Slice(i, escapeLength));
            i += escapeLength;
        }

        return source.Length;
    }

    /// <summary>Gets the source byte offset that the given decoded boundary originated from.</summary>
    /// <param name="decodedPosition">A decoded byte boundary in <c>[0, <see cref="DecodedLength"/>]</c>.</param>
    /// <returns>The corresponding zero-based source byte offset.</returns>
    public int SourceOffsetAt(int decodedPosition)
    {
        return decodedPosition >= decodedLength ? sourceOffset : sourceOffsets[decodedPosition];
    }

    /// <summary>Gets the source line index that the given decoded boundary originated from.</summary>
    /// <param name="decodedPosition">A decoded byte boundary in <c>[0, <see cref="DecodedLength"/>]</c>.</param>
    /// <returns>The corresponding zero-based source line index.</returns>
    public int SourceLineAt(int decodedPosition)
    {
        return decodedPosition >= decodedLength ? sourceLine : sourceLines[decodedPosition];
    }

    /// <summary>Gets the source column index that the given decoded boundary originated from.</summary>
    /// <param name="decodedPosition">A decoded byte boundary in <c>[0, <see cref="DecodedLength"/>]</c>.</param>
    /// <returns>The corresponding zero-based source column index.</returns>
    public int SourceColumnAt(int decodedPosition)
    {
        return decodedPosition >= decodedLength ? sourceColumn : sourceColumns[decodedPosition];
    }

    private void EmitCodepoint(uint codepoint, int offset, int line, int column)
    {
        Span<byte> bytes = stackalloc byte[4];
        int byteCount = new Rune((int)codepoint).EncodeToUtf8(bytes);

        for(int j = 0; j < byteCount; j++)
        {
            Emit(bytes[j], offset, line, column);
        }
    }

    private void Emit(byte value, int offset, int line, int column)
    {
        if(decodedLength == decoded.Length)
        {
            int grown = decoded.Length * 2;
            Array.Resize(ref decoded, grown);
            Array.Resize(ref sourceOffsets, grown);
            Array.Resize(ref sourceLines, grown);
            Array.Resize(ref sourceColumns, grown);
        }

        decoded[decodedLength] = value;
        sourceOffsets[decodedLength] = offset;
        sourceLines[decodedLength] = line;
        sourceColumns[decodedLength] = column;
        decodedLength++;
    }

    private void ConsumeSource(ReadOnlySpan<byte> run)
    {
        foreach(byte b in run)
        {
            AdvanceSource(b);
        }
    }

    private void AdvanceSource(byte b)
    {
        if(b == (byte)'\n')
        {
            if(pendingCarriageReturn)
            {
                pendingCarriageReturn = false;
            }
            else
            {
                sourceLine++;
                sourceColumn = 0;
            }
        }
        else if(b == (byte)'\r')
        {
            sourceLine++;
            sourceColumn = 0;
            pendingCarriageReturn = true;
        }
        else
        {
            pendingCarriageReturn = false;
            sourceColumn++;
        }

        sourceOffset++;
    }

    private void RecordDiagnostic(SparqlLexErrorCode code, int startOffset, int length, string? detail)
    {
        SourceSpan span = SourceSpan.SingleLine(startOffset, startOffset + length, sourceLine, sourceColumn, sourceColumn + length);
        diagnostics.Add(new SparqlLexDiagnostic(code, span, detail));
    }

    private static string FormatCodePoint(uint codepoint)
    {
        return string.Concat("U+", codepoint.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryHexValue(byte b, out uint value)
    {
        //The hexadecimal letters 'a'/'A' denote value ten — the ten decimal digits occupy values 0-9.
        const uint HexLetterValue = 10;

        (bool ok, uint parsed) = b switch
        {
            >= (byte)'0' and <= (byte)'9' => (true, (uint)(b - (byte)'0')),
            >= (byte)'a' and <= (byte)'f' => (true, (uint)(b - (byte)'a' + HexLetterValue)),
            >= (byte)'A' and <= (byte)'F' => (true, (uint)(b - (byte)'A' + HexLetterValue)),
            _ => (false, 0u)
        };

        value = parsed;

        return ok;
    }
}
