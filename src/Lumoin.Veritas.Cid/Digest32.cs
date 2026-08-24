using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// A 32-byte SHA-256 digest stored inline. Mirrors the fixed shape of a
/// DASL CID digest without a heap allocation per <see cref="Cid"/>.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <see cref="InlineArrayAttribute"/>(32) so the storage lives
/// inline in whatever frame, field, or array the value is held in. At
/// firehose rates (an AT Protocol repository snapshot contains thousands of CIDs)
/// this turns the per-CID 32-byte heap allocation into a stack-resident
/// copy and a span read.
/// </para>
/// <para>
/// The value is conceptually immutable: there is no mutating API beyond
/// construction. The inline-array element field is private, and the
/// public surface exposes only span-based access. Equality is bytewise
/// over the 32 bytes.
/// </para>
/// </remarks>
[InlineArray(Size)]
public struct Digest32: IEquatable<Digest32>
{
    /// <summary>The number of bytes in a SHA-256 digest.</summary>
    public const int Size = 32;

    private byte element0;

    /// <summary>
    /// Constructs a <see cref="Digest32"/> by copying exactly
    /// <see cref="Size"/> bytes from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes to copy.</param>
    /// <returns>A new <see cref="Digest32"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not exactly <see cref="Size"/> bytes long.</exception>
    public static Digest32 FromSpan(ReadOnlySpan<byte> source)
    {
        if(source.Length != Size)
        {
            throw new ArgumentException(
                $"Digest32 requires exactly {Size} bytes; got {source.Length}.",
                nameof(source));
        }

        Digest32 result = default;
        source.CopyTo(result);

        return result;
    }

    /// <summary>
    /// Returns a read-only span over the 32 bytes of this digest.
    /// </summary>
    public readonly ReadOnlySpan<byte> AsSpan()
    {
        return MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AsRef(in element0),
            Size);
    }

    /// <summary>
    /// Copies the 32 bytes of this digest into <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">Destination span; must be at least <see cref="Size"/> bytes.</param>
    public readonly void CopyTo(Span<byte> destination)
    {
        AsSpan().CopyTo(destination);
    }

    /// <summary>
    /// Allocates a fresh <see cref="T:byte[]"/> containing the 32 bytes of
    /// this digest. Use only when an array shape is required (e.g. for
    /// crypto APIs that allocate); prefer <see cref="AsSpan"/> for reads.
    /// </summary>
    public readonly byte[] ToArray()
    {
        return AsSpan().ToArray();
    }

    /// <inheritdoc/>
    public readonly bool Equals(Digest32 other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
    {
        return obj is Digest32 other && Equals(other);
    }

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        //First eight bytes of a SHA-256 digest are already well-distributed.
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(AsSpan());
    }

    public static bool operator ==(Digest32 left, Digest32 right) => left.Equals(right);
    public static bool operator !=(Digest32 left, Digest32 right) => !left.Equals(right);
}
