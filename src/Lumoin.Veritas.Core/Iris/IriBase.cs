using System;

namespace Lumoin.Veritas.Core.Iris;

/// <summary>
/// One RFC 3986 §3 component of an IRI as a byte range into the IRI's UTF-8 bytes,
/// distinguishing an absent component (its delimiter never appeared) from a present but
/// empty one — the distinction the §5.2.2 transform branches on (an explicit empty query
/// does not inherit the base's query; a present empty authority still recomposes its
/// <c>//</c> prefix).
/// </summary>
/// <param name="Start">The component's inclusive start byte offset, or <c>-1</c> for an absent component.</param>
/// <param name="Length">The component's byte length; <c>0</c> for a present-but-empty component.</param>
public readonly record struct IriComponent(int Start, int Length)
{
    /// <summary>The absent component: its delimiter never appeared in the IRI.</summary>
    public static IriComponent Absent { get; } = new(-1, 0);

    /// <summary>Whether the component is present (possibly empty), as opposed to absent.</summary>
    public bool IsPresent => Start >= 0;
}

/// <summary>
/// A base IRI parsed once into its RFC 3986 §3 component ranges, so every reference
/// resolved under the same base amortizes the base parse. Built by
/// <see cref="IriResolver.ParseBase"/>; <see cref="None"/> stands for no base in scope.
/// A parsed base whose scheme is absent (a relative base) is representable and resolves
/// nothing, exactly as §5.1 requires — <see cref="IriResolver.ResolveIri"/> returns the
/// reference unchanged for it.
/// </summary>
public readonly struct IriBase: IEquatable<IriBase>
{
    /// <summary>The base IRI's UTF-8 bytes the component ranges index into.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The scheme component (without its terminating <c>':'</c>), or absent.</summary>
    public IriComponent Scheme { get; }

    /// <summary>The authority component (without its <c>//</c> prefix), or absent.</summary>
    public IriComponent Authority { get; }

    /// <summary>The path component; always present on a parsed base, possibly empty.</summary>
    public IriComponent Path { get; }

    /// <summary>The query component (without its <c>'?'</c> prefix), or absent.</summary>
    public IriComponent Query { get; }

    /// <summary>Whether this value carries a parsed base at all; <see langword="false"/> only on <see cref="None"/>.</summary>
    public bool HasValue { get; }

    /// <summary>No base in scope.</summary>
    public static IriBase None => default;

    /// <summary>Binds a parsed base's bytes and component ranges.</summary>
    /// <param name="bytes">The base IRI's UTF-8 bytes.</param>
    /// <param name="scheme">The scheme component, or absent.</param>
    /// <param name="authority">The authority component, or absent.</param>
    /// <param name="path">The path component (present, possibly empty).</param>
    /// <param name="query">The query component, or absent.</param>
    internal IriBase(ReadOnlyMemory<byte> bytes, IriComponent scheme, IriComponent authority, IriComponent path, IriComponent query)
    {
        Bytes = bytes;
        Scheme = scheme;
        Authority = authority;
        Path = path;
        Query = query;
        HasValue = true;
    }

    /// <summary>Whether two parsed bases carry the same bytes and component ranges.</summary>
    /// <param name="other">The base to compare with.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public bool Equals(IriBase other)
    {
        return HasValue == other.HasValue
            && Scheme == other.Scheme
            && Authority == other.Authority
            && Path == other.Path
            && Query == other.Query
            && Bytes.Span.SequenceEqual(other.Bytes.Span);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is IriBase other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HasValue ? Utf8SpanComparer.HashBytes(Bytes.Span) : 0;
    }

    /// <summary>Whether two parsed bases are equal.</summary>
    /// <param name="left">The first base.</param>
    /// <param name="right">The second base.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(IriBase left, IriBase right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two parsed bases differ.</summary>
    /// <param name="left">The first base.</param>
    /// <param name="right">The second base.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(IriBase left, IriBase right)
    {
        return !left.Equals(right);
    }
}
