using System;
using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// A range within the bytes of a parsed document, expressed in both
/// byte-offset and line-and-column form.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both representations.</b> Byte offsets are what storage and protocols
/// need: deterministic, position-stable, suitable for keys in indexes and
/// for unambiguous reference across machines. Line and column are what
/// humans need: editor caret positions, diagnostic underlines, navigation.
/// Carrying both removes the awkward conversions that come from picking
/// one and reconstructing the other.
/// </para>
/// <para>
/// <b>Half-open ranges.</b> All four endpoints are zero-based. The start
/// values are inclusive; the end values are exclusive. A single-byte token
/// at byte 10 has <c>StartByte = 10, EndByte = 11</c>. A single-character
/// token at line 3 column 5 has <c>StartLine = 3, StartColumn = 5,
/// EndLine = 3, EndColumn = 6</c>. An empty span at byte 10 has
/// <c>StartByte = EndByte = 10</c>; this represents a position rather
/// than a range and is used for "insertion-point" diagnostics that do
/// not cover existing bytes.
/// </para>
/// <para>
/// <b>UTF-8 byte semantics.</b> The byte offsets index into the document's
/// canonical UTF-8 byte stream. A multi-byte UTF-8 character occupies a
/// single column position but multiple byte positions. Editors that work
/// in UTF-16 code units convert at the boundary; the project's pipeline
/// works in UTF-8 throughout, so byte offsets compose without conversion
/// inside the library.
/// </para>
/// </remarks>
/// <param name="StartByte">Zero-based byte offset of the first byte (inclusive).</param>
/// <param name="EndByte">Zero-based byte offset of the first byte after the range (exclusive).</param>
/// <param name="StartLine">Zero-based line index of the first character.</param>
/// <param name="StartColumn">Zero-based column index of the first character.</param>
/// <param name="EndLine">Zero-based line index of the first character after the range.</param>
/// <param name="EndColumn">Zero-based column index of the first character after the range.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct SourceSpan(
    int StartByte,
    int EndByte,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    /// <summary>
    /// Creates a span covering a single line.
    /// </summary>
    /// <param name="startByte">Zero-based byte offset of the first byte (inclusive).</param>
    /// <param name="endByte">Zero-based byte offset of the first byte after the range (exclusive).</param>
    /// <param name="line">Zero-based line index.</param>
    /// <param name="startColumn">Zero-based column where the range begins (inclusive).</param>
    /// <param name="endColumn">Zero-based column where the range ends (exclusive).</param>
    /// <returns>A <see cref="SourceSpan"/> on a single line.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any byte or column index is negative, or end values precede their start values.
    /// </exception>
    public static SourceSpan SingleLine(
        int startByte,
        int endByte,
        int line,
        int startColumn,
        int endColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startByte);
        ArgumentOutOfRangeException.ThrowIfLessThan(endByte, startByte);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(startColumn);
        ArgumentOutOfRangeException.ThrowIfLessThan(endColumn, startColumn);

        return new SourceSpan(startByte, endByte, line, startColumn, line, endColumn);
    }

    /// <summary>
    /// Gets the sentinel span used when no source is available — for AST
    /// nodes built programmatically rather than parsed from a document.
    /// All endpoints are zero, which is a valid empty span at the start
    /// of any document; consumers that need to distinguish "no source"
    /// from "empty span at offset zero" should track that out of band.
    /// </summary>
    public static SourceSpan None => new(0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Gets the length in bytes of the span. Equals <c>EndByte - StartByte</c>
    /// because byte offsets use the half-open convention.
    /// </summary>
    public int ByteLength => EndByte - StartByte;

    /// <summary>
    /// Gets the debugger label rendering the span as bytes and lines.
    /// Used by the type's <see cref="DebuggerDisplayAttribute"/>.
    /// </summary>
    private string DebuggerLabel
        => string.Create(
            CultureInfo.InvariantCulture,
            $"SourceSpan bytes [{StartByte}..{EndByte}) lines [{StartLine}:{StartColumn}..{EndLine}:{EndColumn})");
}
