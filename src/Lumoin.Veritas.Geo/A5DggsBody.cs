using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The one implementation of the house A5 DGGS body grammar — the geometry-data region of a
/// house-flavour DGGS literal:
/// <c>CELLS</c> (case-insensitive), optional whitespace, <c>(</c>, one or more whitespace-separated
/// cell tokens, <c>)</c>, optional trailing whitespace. A cell token is one through sixteen
/// hexadecimal digits, case-insensitive with leading zeros permitted, and must name a decodable A5
/// cell. The recognizer, the value layer, and the operand seam all consult this class, so the
/// certified lexical space, the value space, and the readable space stay aligned structurally.
/// </summary>
/// <remarks>
/// The denoted value is the SET of named cells: duplicate tokens and token order carry no meaning,
/// and the canonical form is the deduplicated, ascending-sorted cell sequence. The set is taken
/// as-written — no hierarchy operation (compaction, ancestor collapse) is ever applied, because A5
/// child cells only approximately tile their parent, so collapsing a complete sibling group into its
/// parent would change the denoted region's boundary. Equal canonical sequences therefore correspond
/// to bit-identical materialized geometry.
/// </remarks>
public static class A5DggsBody
{
    /// <summary>The longest cell token: sixteen hexadecimal digits.</summary>
    private const int MaximumTokenLength = 16;

    /// <summary>The offset a conforming scan reports when no byte of the region offends.</summary>
    private const int NoOffendingByte = -1;

    /// <summary>Certifies a geometry-data region against the house A5 body grammar.</summary>
    /// <param name="data">The geometry-data region, as decomposed by <see cref="DggsLexical.TryDecompose"/>.</param>
    /// <param name="offendingOffset">
    /// The offset into <paramref name="data"/> of the first offending byte, or minus one when the region
    /// conforms; a refused scan always names a byte.
    /// </param>
    /// <returns><see langword="true"/> when the region conforms.</returns>
    public static bool Certify(ReadOnlySpan<byte> data, out int offendingOffset)
    {
        return ScanCells(data, cellsToAppendTo: null, out offendingOffset);
    }

    /// <summary>
    /// Reads a WHOLE house-flavour literal into its canonical cell sequence: the empty form yields the
    /// empty sequence; a non-empty form must carry exactly the house grid IRI in its prefix and a
    /// conformant body, whose token set is deduplicated and sorted ascending.
    /// </summary>
    /// <param name="literal">The whole lexical form.</param>
    /// <param name="cellsToAppendTo">The canonical cell sequence, appended in ascending order.</param>
    /// <returns><see langword="true"/> when the literal is a readable house-flavour form.</returns>
    public static bool TryReadCanonicalCells(ReadOnlySpan<byte> literal, List<A5CellId> cellsToAppendTo)
    {
        ArgumentNullException.ThrowIfNull(cellsToAppendTo);

        if(literal.Length == 0)
        {
            return true;
        }

        if(!DggsLexical.TryDecompose(literal, out Range iriRegion, out Range dataRegion, out _)
            || !literal[iriRegion].SequenceEqual(A5DggsVocabulary.GridIri.Span))
        {
            return false;
        }

        int appendStart = cellsToAppendTo.Count;
        if(!ScanCells(literal[dataRegion], cellsToAppendTo, out _))
        {
            cellsToAppendTo.RemoveRange(appendStart, cellsToAppendTo.Count - appendStart);

            return false;
        }

        CanonicalizeSet(cellsToAppendTo, appendStart);

        return true;
    }

    /// <summary>
    /// Writes the complete canonical lexical form of a house-flavour literal over
    /// <paramref name="canonicalCells"/>: the grid-IRI prefix, one space, and the body with every
    /// token in minimal lowercase hexadecimal. The empty sequence writes the empty form.
    /// </summary>
    /// <param name="canonicalCells">The deduplicated, ascending-sorted cell sequence.</param>
    /// <returns>The literal bytes.</returns>
    public static byte[] WriteLiteral(ReadOnlySpan<A5CellId> canonicalCells)
    {
        if(canonicalCells.Length == 0)
        {
            return [];
        }

        var buffer = new ArrayBufferWriter<byte>();
        buffer.Write("<"u8);
        buffer.Write(A5DggsVocabulary.GridIri.Span);
        buffer.Write("> CELLS ("u8);
        Span<byte> token = stackalloc byte[MaximumTokenLength];
        for(int index = 0; index < canonicalCells.Length; index++)
        {
            if(index > 0)
            {
                buffer.Write(" "u8);
            }

            bool formatted = canonicalCells[index].TryFormat(token, out int written);
            if(formatted)
            {
                buffer.Write(token[..written]);
            }
        }

        buffer.Write(")"u8);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Deduplicates and ascending-sorts the cells appended at and after <paramref name="start"/> —
    /// the canonical sequence of the denoted cell set.
    /// </summary>
    /// <param name="cells">The cell list to canonicalize in place.</param>
    /// <param name="start">The first index of the run to canonicalize.</param>
    public static void CanonicalizeSet(List<A5CellId> cells, int start)
    {
        ArgumentNullException.ThrowIfNull(cells);

        int count = cells.Count - start;
        if(count <= 1)
        {
            return;
        }

        cells.Sort(start, count, comparer: null);
        int write = start + 1;
        for(int read = start + 1; read < cells.Count; read++)
        {
            if(cells[read].Value != cells[write - 1].Value)
            {
                cells[write] = cells[read];
                write++;
            }
        }

        cells.RemoveRange(write, cells.Count - write);
    }

    /// <summary>
    /// The one body scan: certifies the grammar and optionally accumulates the parsed cells. Every
    /// token must parse as hexadecimal and name a decodable cell.
    /// </summary>
    /// <param name="data">The geometry-data region.</param>
    /// <param name="cellsToAppendTo">The accumulator, or <see langword="null"/> to certify only.</param>
    /// <param name="offendingOffset">
    /// The offset into <paramref name="data"/> of the first offending byte, or minus one when the region
    /// conforms. An absent keyword or opener names the byte at which it had to appear, a token violation
    /// names the token's first byte — except an over-long token, which names the byte past its longest
    /// admissible extent — an unclosed or memberless list names the byte at which the closer had to
    /// appear, and trailing content names its first byte.
    /// </param>
    /// <returns><see langword="true"/> when the region conforms.</returns>
    private static bool ScanCells(ReadOnlySpan<byte> data, List<A5CellId>? cellsToAppendTo, out int offendingOffset)
    {
        int index = 0;
        if(!MatchesCellsKeyword(data, ref index))
        {
            offendingOffset = index;

            return false;
        }

        SkipWhitespace(data, ref index);
        if(index == data.Length || data[index] != (byte)'(')
        {
            offendingOffset = index;

            return false;
        }

        index++;
        SkipWhitespace(data, ref index);
        int tokenCount = 0;
        while(index < data.Length && data[index] != (byte)')')
        {
            int tokenStart = index;
            while(index < data.Length && IsHexDigit(data[index]))
            {
                index++;
            }

            int tokenLength = index - tokenStart;
            if(tokenLength == 0 || tokenLength > MaximumTokenLength)
            {
                offendingOffset = tokenLength == 0 ? tokenStart : tokenStart + MaximumTokenLength;

                return false;
            }

            if(!A5CellId.TryParse(data.Slice(tokenStart, tokenLength), out A5CellId cell)
                || !A5CellValidity.IsDecodable(cell.Value))
            {
                offendingOffset = tokenStart;

                return false;
            }

            cellsToAppendTo?.Add(cell);
            tokenCount++;
            int afterToken = index;
            SkipWhitespace(data, ref index);
            if(index < data.Length && data[index] != (byte)')' && index == afterToken)
            {
                offendingOffset = index;

                return false;
            }
        }

        if(index == data.Length || tokenCount == 0)
        {
            offendingOffset = index;

            return false;
        }

        index++;
        SkipWhitespace(data, ref index);
        if(index != data.Length)
        {
            offendingOffset = index;

            return false;
        }

        offendingOffset = NoOffendingByte;

        return true;
    }

    /// <summary>Matches the case-insensitive <c>CELLS</c> keyword at <paramref name="index"/>.</summary>
    /// <param name="data">The geometry-data region.</param>
    /// <param name="index">The scan position, advanced past the keyword on a match.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool MatchesCellsKeyword(ReadOnlySpan<byte> data, ref int index)
    {
        ReadOnlySpan<byte> keyword = "cells"u8;
        if(data.Length - index < keyword.Length)
        {
            return false;
        }

        for(int offset = 0; offset < keyword.Length; offset++)
        {
            if((byte)(data[index + offset] | 0x20) != keyword[offset])
            {
                return false;
            }
        }

        index += keyword.Length;

        return true;
    }

    /// <summary>Advances <paramref name="index"/> past separator whitespace.</summary>
    /// <param name="data">The geometry-data region.</param>
    /// <param name="index">The scan position.</param>
    private static void SkipWhitespace(ReadOnlySpan<byte> data, ref int index)
    {
        while(index < data.Length && DggsLexical.IsSeparatorWhitespace(data[index]))
        {
            index++;
        }
    }

    /// <summary>Classifies a hexadecimal digit byte, either case.</summary>
    /// <param name="value">The byte to classify.</param>
    /// <returns><see langword="true"/> when the byte is a hexadecimal digit.</returns>
    private static bool IsHexDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9'
            or >= (byte)'a' and <= (byte)'f'
            or >= (byte)'A' and <= (byte)'F';
    }
}
