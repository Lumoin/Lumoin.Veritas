using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Owl.Syntax;

/// <summary>
/// Maps UTF-8 byte offsets of incrementally fed source text to
/// <see cref="SourceSpan"/> values. Because the byte-native OWL reader lexes
/// over bytes, a byte offset needs no translation; this map only records where
/// each line begins so a byte offset resolves to a line and column.
/// </summary>
/// <remarks>
/// The line-start table grows per fed chunk, so spans for already-fed text are
/// stable across feeds. Columns are counted in bytes from the line start —
/// consistent with the pipeline working in UTF-8 throughout; an editor working
/// in UTF-16 code units converts at its own boundary. For an all-ASCII line a
/// byte column equals the code-point column.
/// </remarks>
internal sealed class OwlByteSourceMap
{
    /// <summary>The byte offset each line begins at; the first line begins at zero.</summary>
    private List<int> LineStarts { get; } = [0];

    /// <summary>The number of bytes appended so far; the end-of-text byte offset.</summary>
    private int TotalBytes { get; set; }

    /// <summary>Extends the line-start table over an appended chunk.</summary>
    /// <param name="chunk">The appended UTF-8 bytes.</param>
    public void Append(ReadOnlySpan<byte> chunk)
    {
        for(int i = 0; i < chunk.Length; i++)
        {
            if(chunk[i] == (byte)'\n')
            {
                LineStarts.Add(TotalBytes + i + 1);
            }
        }

        TotalBytes += chunk.Length;
    }

    /// <summary>Builds the span for a half-open byte range.</summary>
    /// <param name="startByte">The inclusive start byte offset.</param>
    /// <param name="endByte">The exclusive end byte offset.</param>
    /// <returns>The span in byte and line-column form.</returns>
    public SourceSpan Span(int startByte, int endByte)
    {
        (int startLine, int startColumn) = Locate(startByte);
        (int endLine, int endColumn) = Locate(endByte);

        return new SourceSpan(startByte, endByte, startLine, startColumn, endLine, endColumn);
    }

    /// <summary>
    /// Locates a byte offset as a zero-based line and column: a binary search
    /// over <see cref="LineStarts"/> for the last line beginning at or before
    /// the offset; the column is the byte distance from that line's start.
    /// </summary>
    /// <param name="byteOffset">The byte offset to locate.</param>
    /// <returns>The zero-based line index and byte column.</returns>
    private (int Line, int Column) Locate(int byteOffset)
    {
        int low = 0;
        int high = LineStarts.Count - 1;
        while(low < high)
        {
            int middle = (low + high + 1) / 2;
            if(LineStarts[middle] <= byteOffset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (low, byteOffset - LineStarts[low]);
    }
}
