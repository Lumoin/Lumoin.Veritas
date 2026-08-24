using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// A 64-bit A5 cell identifier — the public identity type for the whole grid. Every layer beneath the public facade
/// (<see cref="Serialization"/>, <see cref="CellInfo"/>, <see cref="Compaction"/>, and everything they
/// build on) works directly on the raw <see cref="ulong"/> value; this struct exists only at the
/// public surface, as a zero-cost, span-castable wrapper — a <c>Span&lt;A5CellId&gt;</c> reinterprets
/// as <c>Span&lt;ulong&gt;</c> via <see cref="System.Runtime.InteropServices.MemoryMarshal"/> for
/// columnar/batch use.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("{DebuggerDisplayText,nq}")]
public readonly record struct A5CellId(ulong Value) : IComparable<A5CellId>
{
    /// <summary>
    /// Compares two cell ids by unsigned numeric order of <see cref="Value"/> — the canonical cell-id
    /// ordering every sorted output in this library uses.
    /// <see cref="ulong"/> comparison is unsigned already, so no bespoke comparer is needed.
    /// </summary>
    public int CompareTo(A5CellId other)
    {
        return Value.CompareTo(other.Value);
    }

    /// <summary>
    /// The length in bytes of the canonical persistent form written by <see cref="TryWriteBigEndian"/>.
    /// </summary>
    public const int CanonicalByteLength = 8;

    /// <summary>
    /// Writes the canonical persistent form of this cell id: eight BIG-ENDIAN bytes, so lexicographic
    /// byte order equals unsigned numeric order — sorted stores, columnar indexes, and signed payloads
    /// (contracts, attestations) all agree on identity and ordering.
    /// </summary>
    /// <param name="destination">The buffer to write into; typically a window of a caller-pooled buffer.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is shorter than
    /// <see cref="CanonicalByteLength"/>; nothing is written in that case.</returns>
    public bool TryWriteBigEndian(Span<byte> destination)
    {
        return BinaryPrimitives.TryWriteUInt64BigEndian(destination, Value);
    }

    /// <summary>
    /// Reads a cell id from its canonical eight-byte big-endian form (the inverse of
    /// <see cref="TryWriteBigEndian"/>).
    /// </summary>
    /// <param name="source">The buffer to read from; only the first <see cref="CanonicalByteLength"/> bytes are read.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is shorter than
    /// <see cref="CanonicalByteLength"/>.</exception>
    public static A5CellId ReadBigEndian(ReadOnlySpan<byte> source)
    {
        if(source.Length < CanonicalByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(source), source.Length, $"The canonical cell id form is {CanonicalByteLength} bytes.");
        }

        return new A5CellId(BinaryPrimitives.ReadUInt64BigEndian(source));
    }

    /// <summary>
    /// Formats this cell id as minimal lowercase hexadecimal with no padding into a caller-provided
    /// buffer — the display/JSON convenience form, never the canonical persistent form (that is
    /// <see cref="TryWriteBigEndian"/>); zero renders as <c>"0"</c>.
    /// </summary>
    /// <param name="destination">The buffer to write into.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(Span<char> destination, out int charsWritten)
    {
        return Value.TryFormat(destination, out charsWritten, "x", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// UTF-8 variant of <see cref="TryFormat(Span{char}, out int)"/>.
    /// </summary>
    /// <param name="utf8Destination">The buffer to write into; typically a window of a caller-pooled buffer.</param>
    /// <param name="bytesWritten">The number of bytes written.</param>
    /// <returns><see langword="false"/> when <paramref name="utf8Destination"/> is too short.</returns>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten)
    {
        return Value.TryFormat(utf8Destination, out bytesWritten, "x", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a cell id from its hexadecimal character form: minimal digits, case-insensitive, no
    /// whitespace, no prefix.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <exception cref="FormatException"><paramref name="source"/> is empty or not hexadecimal.</exception>
    /// <exception cref="OverflowException"><paramref name="source"/> exceeds 64 bits (more than 16 digits).</exception>
    public static A5CellId Parse(ReadOnlySpan<char> source)
    {
        return new A5CellId(ulong.Parse(source, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Attempts to parse a cell id from its hexadecimal character form; the non-throwing counterpart of
    /// <see cref="Parse(ReadOnlySpan{char})"/>.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="result">The parsed cell id, or default on failure.</param>
    public static bool TryParse(ReadOnlySpan<char> source, out A5CellId result)
    {
        if(ulong.TryParse(source, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value))
        {
            result = new A5CellId(value);

            return true;
        }

        result = default;

        return false;
    }

    /// <summary>
    /// UTF-8 variant of <see cref="Parse(ReadOnlySpan{char})"/>.
    /// </summary>
    /// <param name="utf8Source">The UTF-8 bytes to parse.</param>
    /// <exception cref="FormatException"><paramref name="utf8Source"/> is empty or not hexadecimal.</exception>
    /// <exception cref="OverflowException"><paramref name="utf8Source"/> exceeds 64 bits (more than 16 digits).</exception>
    public static A5CellId Parse(ReadOnlySpan<byte> utf8Source)
    {
        return new A5CellId(ulong.Parse(utf8Source, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// UTF-8 variant of <see cref="TryParse(ReadOnlySpan{char}, out A5CellId)"/>.
    /// </summary>
    /// <param name="utf8Source">The UTF-8 bytes to parse.</param>
    /// <param name="result">The parsed cell id, or default on failure.</param>
    public static bool TryParse(ReadOnlySpan<byte> utf8Source, out A5CellId result)
    {
        if(ulong.TryParse(utf8Source, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value))
        {
            result = new A5CellId(value);

            return true;
        }

        result = default;

        return false;
    }


    /// <summary>Unsigned less-than of <see cref="Value"/>.</summary>
    public static bool operator <(A5CellId left, A5CellId right)
    {
        return left.Value < right.Value;
    }

    /// <summary>Unsigned less-than-or-equal of <see cref="Value"/>.</summary>
    public static bool operator <=(A5CellId left, A5CellId right)
    {
        return left.Value <= right.Value;
    }

    /// <summary>Unsigned greater-than of <see cref="Value"/>.</summary>
    public static bool operator >(A5CellId left, A5CellId right)
    {
        return left.Value > right.Value;
    }

    /// <summary>Unsigned greater-than-or-equal of <see cref="Value"/>.</summary>
    public static bool operator >=(A5CellId left, A5CellId right)
    {
        return left.Value >= right.Value;
    }

    /// <summary>Minimal lowercase hex, for the debugger display only; see <see cref="Hex.U64ToHex"/>.</summary>
    private string DebuggerDisplayText => Hex.U64ToHex(Value);
}
