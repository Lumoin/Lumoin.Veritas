using System;
using System.Buffers.Binary;

namespace Lumoin.Veritas.Core.ContentAddressing;

/// <summary>
/// A 128-bit content-addressed key — a content hash, or any opaque
/// fixed-width identity such as a GUID — held as two 64-bit words. The
/// load-bearing identity unit of the integrity and reconciliation layers:
/// equality drives content-addressed dedup and lookup, and XOR drives
/// rateless reconciliation (combining hashes so matched items cancel).
/// </summary>
/// <remarks>
/// <para>
/// The two-word representation is deliberate: it is exactly what a
/// <see cref="System.Runtime.Intrinsics.Vector128{T}"/> punning treats as
/// one register, and exactly what the portable two-step word-parallel
/// fallback operates on (see <see cref="ContentKey128Kernel"/>). Bytes are
/// read and written little-endian, so a key serialized on one machine and
/// compared on another agrees — endianness is pinned at the byte boundary,
/// not left to register layout.
/// </para>
/// </remarks>
/// <param name="Low">The low 64 bits.</param>
/// <param name="High">The high 64 bits.</param>
public readonly record struct ContentKey128(ulong Low, ulong High)
{
    /// <summary>The fixed width of a key in bytes.</summary>
    public const int ByteWidth = 16;

    /// <summary>The all-zero key — the XOR identity, and the empty content digest.</summary>
    public static ContentKey128 Zero => default;

    /// <summary>Reads a key from its 16 little-endian bytes.</summary>
    /// <param name="bytes">The source bytes; at least <see cref="ByteWidth"/> long.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is shorter than <see cref="ByteWidth"/>.</exception>
    public static ContentKey128 FromBytes(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length < ByteWidth)
        {
            throw new ArgumentException($"A content key needs {ByteWidth} bytes.", nameof(bytes));
        }

        ulong low = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        ulong high = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);

        return new ContentKey128(low, high);
    }

    /// <summary>Reads a key from a <see cref="Guid"/>, the canonical 128-bit opaque identity.</summary>
    /// <param name="value">The GUID.</param>
    /// <returns>The key.</returns>
    public static ContentKey128 FromGuid(Guid value)
    {
        Span<byte> bytes = stackalloc byte[ByteWidth];
        value.TryWriteBytes(bytes);

        return FromBytes(bytes);
    }

    /// <summary>Writes the key as 16 little-endian bytes.</summary>
    /// <param name="destination">The destination; at least <see cref="ByteWidth"/> long.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="ByteWidth"/>.</exception>
    public void WriteBytes(Span<byte> destination)
    {
        if(destination.Length < ByteWidth)
        {
            throw new ArgumentException($"A content key needs {ByteWidth} bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination, Low);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], High);
    }

    /// <summary>Reconstructs the <see cref="Guid"/> a key was read from.</summary>
    /// <returns>The GUID.</returns>
    public Guid ToGuid()
    {
        Span<byte> bytes = stackalloc byte[ByteWidth];
        WriteBytes(bytes);

        return new Guid(bytes);
    }
}
