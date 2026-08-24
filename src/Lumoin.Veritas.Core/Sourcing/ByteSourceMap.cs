using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// Maps UTF-8 byte offsets of source text to <see cref="SourceSpan"/> values.
/// Because the byte-native readers lex over bytes, a byte offset needs no
/// translation; this map only records where each line begins so a byte offset
/// resolves to a line and column.
/// </summary>
/// <remarks>
/// The line-start table grows per appended chunk, so spans for already-seen text
/// are stable across appends — a reader fed incrementally appends each chunk as it
/// arrives, and a whole-buffer reader appends the buffer once. Columns are counted
/// in bytes from the line start — consistent with the pipeline working in UTF-8
/// throughout; an editor working in UTF-16 code units converts at its own boundary.
/// For an all-ASCII line a byte column equals the code-point column.
/// A byte-bounded streaming reader, having reclaimed a consumed source prefix, calls
/// <see cref="PruneBefore"/> to drop the line starts that prefix held; the recorded
/// offsets and line/column numbers stay absolute (a soft-dead boundary defers the
/// physical removal so the prune is amortized), so a span query for any still-live
/// offset resolves exactly as it would against the whole document.
/// </remarks>
public sealed class ByteSourceMap
{
    /// <summary>The byte offset each retained line begins at, in ascending order; entries before <see cref="Origin"/> are pruned but not yet physically removed. The first line begins at zero.</summary>
    private List<int> LineStarts { get; } = [0];

    /// <summary>The number of bytes appended so far; the end-of-text byte offset.</summary>
    private int TotalBytes { get; set; }

    /// <summary>The index of the first live entry in <see cref="LineStarts"/>; entries below it are pruned (their lines fully precede the reclaimed source prefix) and skipped by a lookup until physically removed.</summary>
    private int Origin { get; set; }

    /// <summary>The number of line starts physically removed by a compaction; added to a live entry's physical index to recover its absolute zero-based line number.</summary>
    private int PrunedLines { get; set; }

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
    /// over the live <see cref="LineStarts"/> for the last line beginning at or before
    /// the offset; the column is the byte distance from that line's start, and the line
    /// number adds the count of physically removed lines so it stays absolute after a prune.
    /// </summary>
    /// <param name="byteOffset">The byte offset to locate; must be at or after the start of the oldest live line (a pruned offset is never queried).</param>
    /// <returns>The zero-based line index and byte column.</returns>
    private (int Line, int Column) Locate(int byteOffset)
    {
        if(byteOffset < LineStarts[Origin])
        {
            //An offset below the oldest live line start fell in a reclaimed (pruned) prefix: only the long-lived
            //streaming container and document-root elements, which open at the document start and close after the
            //prefix has been pruned, query here. Their true line cannot be recovered once the prefix is gone, so anchor
            //to the oldest live line at column zero — a defined, non-negative result rather than an underflowed column.
            return (Origin + PrunedLines, 0);
        }

        int low = Origin;
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

        return (low + PrunedLines, byteOffset - LineStarts[low]);
    }

    /// <summary>
    /// Drops the line starts whose lines fully precede <paramref name="byteOffset"/> — the boundary up to which a
    /// streaming reader has reclaimed the source — keeping the line that contains the offset so any still-live offset
    /// still resolves. The drop is soft (a moved <see cref="Origin"/>) and the entries are physically removed only once
    /// the dead prefix is the majority, so repeated pruning over a long document stays linear overall.
    /// </summary>
    /// <param name="byteOffset">The absolute byte offset below which the source has been reclaimed.</param>
    public void PruneBefore(int byteOffset)
    {
        int low = Origin;
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

        if(low <= Origin)
        {
            return;
        }

        Origin = low;
        if(Origin * 2 > LineStarts.Count)
        {
            LineStarts.RemoveRange(0, Origin);
            PrunedLines += Origin;
            Origin = 0;
        }
    }
}
