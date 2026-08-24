using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core;

/// <summary>
/// An equality comparer over <see cref="Utf8String"/> keys whose alternate face probes by
/// raw UTF-8 span, so a dictionary keyed by owned values answers span lookups without
/// materializing a key (via <c>Dictionary.GetAlternateLookup</c>). Both faces hash the
/// same FNV-1a fold over the bytes, so they agree by construction; the fold is the
/// comparer's own and independent of <see cref="Utf8String"/>'s hash.
/// </summary>
public sealed class Utf8SpanComparer: IEqualityComparer<Utf8String>, IAlternateEqualityComparer<ReadOnlySpan<byte>, Utf8String>
{
    /// <summary>The shared instance.</summary>
    public static Utf8SpanComparer Instance { get; } = new();

    /// <summary>The FNV-1a 64-bit offset basis the fold starts from.</summary>
    private const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>The FNV-1a 64-bit prime the fold multiplies by per byte.</summary>
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>The comparer is stateless; consumers share <see cref="Instance"/>.</summary>
    private Utf8SpanComparer()
    {
    }

    /// <summary>Whether two keys carry the same bytes.</summary>
    /// <param name="x">The first key.</param>
    /// <param name="y">The second key.</param>
    /// <returns><see langword="true"/> when the bytes are equal.</returns>
    public bool Equals(Utf8String x, Utf8String y)
    {
        return x.Span.SequenceEqual(y.Span);
    }

    /// <summary>The FNV-1a hash of a key's bytes.</summary>
    /// <param name="obj">The key.</param>
    /// <returns>The hash.</returns>
    public int GetHashCode(Utf8String obj)
    {
        return HashBytes(obj.Span);
    }

    /// <summary>Whether a span probe carries the same bytes as a key.</summary>
    /// <param name="alternate">The probe span.</param>
    /// <param name="other">The key.</param>
    /// <returns><see langword="true"/> when the bytes are equal.</returns>
    public bool Equals(ReadOnlySpan<byte> alternate, Utf8String other)
    {
        return alternate.SequenceEqual(other.Span);
    }

    /// <summary>The FNV-1a hash of a probe span, equal to <see cref="GetHashCode(Utf8String)"/> of a key with the same bytes.</summary>
    /// <param name="alternate">The probe span.</param>
    /// <returns>The hash.</returns>
    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        return HashBytes(alternate);
    }

    /// <summary>Materializes an owned key from a probe span, for insertion through the alternate face.</summary>
    /// <param name="alternate">The probe span.</param>
    /// <returns>The owned key.</returns>
    public Utf8String Create(ReadOnlySpan<byte> alternate)
    {
        return new Utf8String(alternate.ToArray());
    }

    /// <summary>Folds a byte span with FNV-1a 64 and condenses to a 32-bit hash.</summary>
    /// <param name="bytes">The bytes to fold.</param>
    /// <returns>The hash.</returns>
    public static int HashBytes(ReadOnlySpan<byte> bytes)
    {
        return Condense(Fold(FoldSeed, bytes));
    }

    /// <summary>The seed a fresh FNV-1a fold starts from.</summary>
    public static ulong FoldSeed => FnvOffsetBasis;

    /// <summary>
    /// Folds a byte span into a running FNV-1a 64 state. Public so a comparer over a
    /// composite key can fold its parts sequentially and stay consistent with the
    /// one-span fold over their concatenation.
    /// </summary>
    /// <param name="hash">The running fold state.</param>
    /// <param name="bytes">The bytes to fold.</param>
    /// <returns>The updated state.</returns>
    public static ulong Fold(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach(byte value in bytes)
        {
            hash = (hash ^ value) * FnvPrime;
        }

        return hash;
    }

    /// <summary>Condenses a finished fold state to the 32-bit hash a dictionary consumes.</summary>
    /// <param name="hash">The finished fold state.</param>
    /// <returns>The hash.</returns>
    public static int Condense(ulong hash)
    {
        return (int)(hash ^ (hash >> 32));
    }
}
