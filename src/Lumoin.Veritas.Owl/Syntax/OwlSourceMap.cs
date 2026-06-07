using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Owl.Syntax;

/// <summary>
/// Maps character offsets of incrementally fed source text to
/// <see cref="SourceSpan"/> values: UTF-8 byte offsets via a
/// cumulative-length table, lines and code-point columns via the
/// line-start table.
/// </summary>
/// <remarks>
/// The tables grow per fed chunk, so spans for already-fed text are stable
/// across feeds. The OWL text syntaxes — functional and Manchester — share
/// this one map.
/// </remarks>
internal sealed class OwlSourceMap
{
    /// <summary>The cumulative UTF-8 byte length before each character index.</summary>
    private List<int> CumulativeBytes { get; } = [];

    /// <summary>The character index each line starts at.</summary>
    private List<int> LineStarts { get; } = [0];

    /// <summary>The UTF-8 byte length of all text appended so far.</summary>
    private int TotalBytes { get; set; }

    /// <summary>Extends the tables over an appended chunk.</summary>
    /// <param name="chunk">The appended characters.</param>
    public void Append(ReadOnlySpan<char> chunk)
    {
        foreach(char c in chunk)
        {
            CumulativeBytes.Add(TotalBytes);

            //The character's UTF-8 width; each half of a surrogate
            //pair contributes two of the pair's four bytes.
            TotalBytes += c switch
            {
                <= '\u007F' => 1,
                <= '\u07FF' => 2,
                _ when char.IsSurrogate(c) => 2,
                _ => 3
            };

            if(c == '\n')
            {
                LineStarts.Add(CumulativeBytes.Count);
            }
        }
    }

    /// <summary>Builds the span for a half-open character range.</summary>
    /// <param name="startChar">The inclusive start character index.</param>
    /// <param name="endChar">The exclusive end character index.</param>
    /// <returns>The span in byte and line-column form.</returns>
    public SourceSpan Span(int startChar, int endChar)
    {
        (int startLine, int startColumn) = Locate(startChar);
        (int endLine, int endColumn) = Locate(endChar);

        return new SourceSpan(ByteAt(startChar), ByteAt(endChar), startLine, startColumn, endLine, endColumn);
    }

    /// <summary>Returns the UTF-8 byte offset of a character offset, the end-of-text offset included.</summary>
    /// <param name="charOffset">The character offset.</param>
    /// <returns>The byte offset.</returns>
    private int ByteAt(int charOffset)
    {
        return charOffset == CumulativeBytes.Count ? TotalBytes : CumulativeBytes[charOffset];
    }

    /// <summary>
    /// Locates a character offset as a zero-based line and column: a
    /// binary search over <see cref="LineStarts"/> for the last line
    /// starting at or before the offset; the column is the distance
    /// from that line's start.
    /// </summary>
    /// <param name="charOffset">The character offset to locate.</param>
    /// <returns>The zero-based line index and column.</returns>
    private (int Line, int Column) Locate(int charOffset)
    {
        int low = 0;
        int high = LineStarts.Count - 1;
        while(low < high)
        {
            int middle = (low + high + 1) / 2;
            if(LineStarts[middle] <= charOffset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (low, charOffset - LineStarts[low]);
    }
}
