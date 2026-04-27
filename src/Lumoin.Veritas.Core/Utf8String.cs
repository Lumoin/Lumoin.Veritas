using System;
using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Core;

/// <summary>
/// An immutable view over UTF-8 encoded bytes with an optionally precomputed hash code.
/// </summary>
/// <remarks>
/// <para>
/// This is the fundamental string type for all RDF term representation in Veritas.
/// Rather than using <see cref="string"/> (which is UTF-16 and heap-allocated),
/// all IRIs, blank node labels, and literal values are stored as UTF-8 byte sequences.
/// </para>
/// <para>
/// The type supports two construction modes that trade hash cost against lookup cost.
/// The default constructor computes the hash eagerly at construction time, which is
/// the right trade-off for term values used as dictionary keys (IRIs, blank node labels,
/// literal lexical forms): paying once at construction avoids rehashing on every lookup.
/// The <see cref="WithoutPrecomputedHash(ReadOnlyMemory{byte})"/> factory skips the
/// eager hash entirely, which is the right trade-off for blob-like values (document
/// bodies, large opaque payloads) that flow through the system once and are not used
/// as dictionary keys. Calling <see cref="GetHashCode"/> on a deferred-hash instance
/// recomputes the hash on every call, which is acceptable for the intended usage but
/// would be a performance hazard if such instances were placed in hash-keyed collections.
/// </para>
/// <para>
/// Equality and ordering operate directly on the underlying byte span and do not depend
/// on the hash code, so an eager-hash instance and a deferred-hash instance covering
/// the same bytes compare equal.
/// </para>
/// <para>
/// Instances do not own the underlying memory. The <see cref="Utf8StringPool"/>
/// manages allocation and lifetime. All <see cref="Utf8String"/> instances created
/// from a given pool become invalid when that pool is disposed.
/// </para>
/// </remarks>
[DebuggerDisplay("\"{ToString(),nq}\" {DebuggerHashStatus,nq}")]
public readonly struct Utf8String: IEquatable<Utf8String>, IComparable<Utf8String>
{
    /// <summary>
    /// Sentinel stored in <see cref="precomputedHashCode"/> when the hash has not been
    /// precomputed. The eager constructor remaps a real hash of zero to one to keep
    /// this sentinel unambiguous; a one-bit collision is statistically negligible.
    /// </summary>
    private const int DeferredHashSentinel = 0;

    private readonly ReadOnlyMemory<byte> bytes;
    private readonly int precomputedHashCode;

    /// <summary>
    /// Initializes a new <see cref="Utf8String"/> over the given UTF-8 bytes
    /// and computes the hash code eagerly. Use this constructor for term values
    /// (IRIs, blank node labels, literal lexical forms) that participate in
    /// dictionary lookups.
    /// </summary>
    /// <param name="utf8Bytes">The UTF-8 encoded byte sequence.</param>
    public Utf8String(ReadOnlyMemory<byte> utf8Bytes)
    {
        bytes = utf8Bytes;
        precomputedHashCode = ComputeHashCode(utf8Bytes.Span);
    }

    /// <summary>
    /// Gets the UTF-8 encoded bytes.
    /// </summary>
    public ReadOnlySpan<byte> Span => bytes.Span;

    /// <summary>
    /// Gets the UTF-8 encoded bytes as a <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Memory => bytes;

    /// <summary>
    /// Gets the length in bytes.
    /// </summary>
    public int Length => bytes.Length;

    /// <summary>
    /// Gets a value indicating whether this instance contains no bytes.
    /// </summary>
    public bool IsEmpty => bytes.IsEmpty;

    /// <summary>
    /// Gets a value indicating whether this instance has a precomputed hash code.
    /// Deferred-hash instances recompute the hash on every <see cref="GetHashCode"/>
    /// call and should not be used as dictionary keys.
    /// </summary>
    public bool HasPrecomputedHash => precomputedHashCode != DeferredHashSentinel;

    /// <summary>
    /// Gets the debugger label describing the hash state of this instance.
    /// Used by the type's <see cref="DebuggerDisplayAttribute"/>.
    /// </summary>
    private string DebuggerHashStatus
        => precomputedHashCode != DeferredHashSentinel
            ? string.Create(CultureInfo.InvariantCulture, $"#{precomputedHashCode:X8}")
            : "(hash deferred)";

    /// <summary>
    /// Decodes the UTF-8 bytes to a .NET <see cref="string"/>.
    /// </summary>
    /// <remarks>
    /// This allocates a new string on the heap. Use sparingly — prefer operating
    /// on the raw UTF-8 bytes for performance-critical paths.
    /// </remarks>
    /// <returns>The decoded string.</returns>
    public override string ToString()
    {
        return System.Text.Encoding.UTF8.GetString(bytes.Span);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Eager-hash instances return the value computed at construction. Deferred-hash
    /// instances recompute the hash on every call.
    /// </remarks>
    public override int GetHashCode()
    {
        return precomputedHashCode != DeferredHashSentinel
            ? precomputedHashCode
            : ComputeHashCode(bytes.Span);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is Utf8String other && Equals(other);
    }

    /// <inheritdoc/>
    public bool Equals(Utf8String other)
    {
        return bytes.Span.SequenceEqual(other.bytes.Span);
    }

    /// <summary>
    /// Compares two <see cref="Utf8String"/> instances lexicographically by their UTF-8 bytes.
    /// </summary>
    /// <param name="other">The other instance to compare against.</param>
    /// <returns>
    /// A negative value if this instance precedes <paramref name="other"/>,
    /// zero if they are equal, or a positive value if this instance follows <paramref name="other"/>.
    /// </returns>
    public int CompareTo(Utf8String other)
    {
        return bytes.Span.SequenceCompareTo(other.bytes.Span);
    }

    /// <summary>
    /// Determines whether two <see cref="Utf8String"/> instances are equal.
    /// </summary>
    public static bool operator ==(Utf8String left, Utf8String right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="Utf8String"/> instances are not equal.
    /// </summary>
    public static bool operator !=(Utf8String left, Utf8String right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether one <see cref="Utf8String"/> precedes another lexicographically.
    /// </summary>
    public static bool operator <(Utf8String left, Utf8String right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Determines whether one <see cref="Utf8String"/> precedes or equals another lexicographically.
    /// </summary>
    public static bool operator <=(Utf8String left, Utf8String right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Determines whether one <see cref="Utf8String"/> follows another lexicographically.
    /// </summary>
    public static bool operator >(Utf8String left, Utf8String right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Determines whether one <see cref="Utf8String"/> follows or equals another lexicographically.
    /// </summary>
    public static bool operator >=(Utf8String left, Utf8String right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <summary>
    /// Creates a <see cref="Utf8String"/> from a .NET string by encoding it as UTF-8.
    /// The hash is computed eagerly.
    /// </summary>
    /// <remarks>
    /// This allocates a new byte array. For bulk operations, prefer using
    /// <see cref="Utf8StringPool.Intern(ReadOnlySpan{byte})"/> to avoid per-term allocations.
    /// </remarks>
    /// <param name="value">The string to encode.</param>
    /// <returns>A new <see cref="Utf8String"/> containing the UTF-8 encoding of <paramref name="value"/>.</returns>
    public static Utf8String FromString(string value)
    {
        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return new Utf8String(utf8Bytes);
    }

    /// <summary>
    /// Creates a <see cref="Utf8String"/> over the given UTF-8 bytes without precomputing
    /// the hash code. Suitable for blob-like values (document bodies, opaque payloads)
    /// that flow through the system once and are not used as dictionary keys.
    /// </summary>
    /// <remarks>
    /// Calling <see cref="GetHashCode"/> on the returned instance recomputes the hash
    /// on every invocation. Placing such an instance in a hash-keyed collection will
    /// cause that collection to rehash the underlying bytes on every lookup.
    /// </remarks>
    /// <param name="utf8Bytes">The UTF-8 encoded byte sequence.</param>
    /// <returns>
    /// A new <see cref="Utf8String"/> over <paramref name="utf8Bytes"/>
    /// with the hash code deferred.
    /// </returns>
    public static Utf8String WithoutPrecomputedHash(ReadOnlyMemory<byte> utf8Bytes)
    {
        return new Utf8String(utf8Bytes, deferHash: true);
    }

    /// <summary>
    /// Private constructor used by <see cref="WithoutPrecomputedHash(ReadOnlyMemory{byte})"/>
    /// to create instances with a deferred hash. The <paramref name="deferHash"/> parameter
    /// is a tag distinguishing this overload from the public eager constructor; it is
    /// always <see langword="true"/> at the call site.
    /// </summary>
    /// <param name="utf8Bytes">The UTF-8 encoded byte sequence.</param>
    /// <param name="deferHash">Always <see langword="true"/>; tag parameter only.</param>
    private Utf8String(ReadOnlyMemory<byte> utf8Bytes, bool deferHash)
    {
        _ = deferHash;
        bytes = utf8Bytes;
        precomputedHashCode = DeferredHashSentinel;
    }

    /// <summary>
    /// Computes the hash code for a UTF-8 byte sequence, remapping a result of zero
    /// to one so the deferred-hash sentinel remains unambiguous.
    /// </summary>
    /// <param name="span">The UTF-8 byte span to hash.</param>
    /// <returns>A non-zero hash code derived from <paramref name="span"/>.</returns>
    private static int ComputeHashCode(ReadOnlySpan<byte> span)
    {
        HashCode hash = new();
        hash.AddBytes(span);
        int result = hash.ToHashCode();
        return result != DeferredHashSentinel ? result : 1;
    }
}
