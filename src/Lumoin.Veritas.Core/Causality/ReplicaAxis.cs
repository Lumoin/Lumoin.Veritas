using System;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// The replica identity axis a causal dot is minted on: 32 fixed pseudonymous bytes, public protocol state. It
/// names WHO minted an event, never what the event may do — authorization is a separate axis entirely. The host
/// supplies its identity at open; identity never travels with store bytes, so copying a store directory cannot
/// copy who a replica is. Two hosts minting concurrently under one identity produce colliding dots (distinct
/// events under one name); replica-identity distinctness is a declared deployment obligation.
/// </summary>
public readonly struct ReplicaAxis : IEquatable<ReplicaAxis>
{
    /// <summary>The fixed byte width of a replica identity axis.</summary>
    public const int ByteWidth = 32;

    /// <summary>The identity bytes; exactly <see cref="ByteWidth"/> long.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The content hash over <see cref="Bytes"/>, computed once at construction so dictionary probes never rehash the identity.</summary>
    private readonly int hashCode;

    /// <summary>Creates an axis over identity bytes.</summary>
    /// <param name="bytes">The identity bytes; exactly <see cref="ByteWidth"/> long. The axis holds the memory as given — the caller does not mutate it afterwards.</param>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not exactly <see cref="ByteWidth"/> bytes.</exception>
    public ReplicaAxis(ReadOnlyMemory<byte> bytes)
    {
        if(bytes.Length != ByteWidth)
        {
            throw new ArgumentException($"A replica identity axis is exactly {ByteWidth} bytes; {bytes.Length} were supplied.", nameof(bytes));
        }

        Bytes = bytes;

        HashCode hash = new();
        hash.AddBytes(bytes.Span);
        hashCode = hash.ToHashCode();
    }

    /// <summary>Determines whether this axis names the same identity bytes as another.</summary>
    /// <param name="other">The other axis.</param>
    /// <returns><see langword="true"/> when the identity bytes are equal.</returns>
    public bool Equals(ReplicaAxis other)
    {
        return hashCode == other.hashCode && Bytes.Span.SequenceEqual(other.Bytes.Span);
    }

    /// <summary>Determines whether this axis equals another object.</summary>
    /// <param name="obj">The other object.</param>
    /// <returns><see langword="true"/> when it is an axis with the same identity bytes.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ReplicaAxis other && Equals(other);
    }

    /// <summary>Gets the hash code computed over the identity bytes at construction.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return hashCode;
    }

    /// <summary>Determines whether two axes name the same identity bytes.</summary>
    /// <param name="left">The left axis.</param>
    /// <param name="right">The right axis.</param>
    /// <returns><see langword="true"/> when the identity bytes are equal.</returns>
    public static bool operator ==(ReplicaAxis left, ReplicaAxis right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two axes name different identity bytes.</summary>
    /// <param name="left">The left axis.</param>
    /// <param name="right">The right axis.</param>
    /// <returns><see langword="true"/> when the identity bytes differ.</returns>
    public static bool operator !=(ReplicaAxis left, ReplicaAxis right)
    {
        return !left.Equals(right);
    }
}
