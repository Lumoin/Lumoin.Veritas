using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Core.Serialization;

/// <summary>
/// Little-endian span primitives shared by the persistence column and sequence codecs: a
/// length-prefixed primitive-array writer and reader, and a host-endianness guard. The on-disk
/// image is little-endian, so a big-endian host is rejected rather than silently producing a
/// wrong image.
/// </summary>
internal static class LittleEndianBuffer
{
    /// <summary>Throws on a big-endian host — the persistence byte image is little-endian.</summary>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    internal static void EnsureLittleEndian()
    {
        if(!BitConverter.IsLittleEndian)
        {
            throw new NotSupportedException("The persistence byte image is little-endian; big-endian hosts are not supported.");
        }
    }

    /// <summary>The serialized size of a length-prefixed primitive array: the count prefix plus the elements.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="count">The element count.</param>
    /// <returns>The byte count.</returns>
    internal static int ArrayBytes<T>(int count) where T : unmanaged
    {
        return sizeof(int) + (count * Unsafe.SizeOf<T>());
    }

    /// <summary>Writes a length-prefixed primitive array little-endian; returns the bytes written.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="destination">The buffer to write into.</param>
    /// <param name="values">The array to write.</param>
    /// <returns>The bytes written.</returns>
    internal static int WriteArray<T>(Span<byte> destination, ReadOnlySpan<T> values) where T : unmanaged
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, values.Length);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values);
        bytes.CopyTo(destination[sizeof(int)..]);

        return sizeof(int) + bytes.Length;
    }

    /// <summary>Reads a length-prefixed primitive array; sets <paramref name="consumed"/> to the bytes read.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="source">The byte image positioned at the array.</param>
    /// <param name="consumed">Receives the bytes consumed.</param>
    /// <returns>The read array.</returns>
    /// <exception cref="InvalidDataException">The source is truncated, or declares a length beyond its bounds (a malformed image).</exception>
    internal static T[] ReadArray<T>(ReadOnlySpan<byte> source, out int consumed) where T : unmanaged
    {
        if(source.Length < sizeof(int))
        {
            throw new InvalidDataException("The byte image is truncated before an array length.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(source);
        int elementSize = Unsafe.SizeOf<T>();
        if(count < 0 || ((long)count * elementSize) > source.Length - sizeof(int))
        {
            throw new InvalidDataException("The byte image declares an array length beyond its bounds.");
        }

        T[] result = new T[count];
        int byteCount = count * elementSize;
        source.Slice(sizeof(int), byteCount).CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
        consumed = sizeof(int) + byteCount;

        return result;
    }
}
