using System;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// A source channel through which silent data corruption can enter, modelled as a first-class,
/// consumer-extensible value so the scrub-cadence estimator accounts for each channel separately
/// rather than conflating them into one rate. Built-ins are exposed as named static instances; a
/// deployment adds its own with <see cref="Create"/>. The defining fact a channel carries is whether
/// hardware memory error correction reduces its corruption rate — true for the memory channel, false
/// for channels error correction does not reach, such as data at rest.
/// </summary>
/// <remarks>
/// <para>
/// The channel names only the source and its error-correction applicability; the assumed corruption
/// rate per channel is a calibration owned by the estimator, not by the channel identity. A future
/// per-channel detection routine — the storage analogue of the memory-protection probe — would supply
/// measured rates keyed by channel, feeding the estimator real intelligence in place of the default
/// assumptions.
/// </para>
/// </remarks>
public readonly struct CorruptionChannel : IEquatable<CorruptionChannel>
{
    /// <summary>The stable channel code; 0 is reserved for "no channel" and is not a valid code.</summary>
    public int Code { get; }

    /// <summary>A short human-readable name.</summary>
    public string Name { get; }

    /// <summary>Whether hardware memory error correction reduces this channel's corruption rate.</summary>
    public bool IsReducedByMemoryErrorCorrection { get; }

    /// <summary>Creates a channel with a non-zero code, a name, and its error-correction applicability.</summary>
    /// <param name="code">The stable channel code (non-zero).</param>
    /// <param name="name">A short human-readable name.</param>
    /// <param name="isReducedByMemoryErrorCorrection">Whether hardware memory error correction reduces this channel's corruption rate.</param>
    private CorruptionChannel(int code, string name, bool isReducedByMemoryErrorCorrection)
    {
        if(code == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "Corruption-channel code 0 is reserved and is not a valid channel.");
        }

        ArgumentException.ThrowIfNullOrEmpty(name);

        Code = code;
        Name = name;
        IsReducedByMemoryErrorCorrection = isReducedByMemoryErrorCorrection;
    }

    /// <summary>The memory channel: bit errors in main memory, reduced by hardware error correction.</summary>
    public static CorruptionChannel Memory { get; } = new(1, "Memory", true);

    /// <summary>The storage channel: corruption of data at rest, which memory error correction does not reach.</summary>
    public static CorruptionChannel Storage { get; } = new(2, "Storage", false);

    /// <summary>Creates a custom corruption channel; the deployment is responsible for a globally-unique code.</summary>
    /// <param name="code">The stable channel code (non-zero).</param>
    /// <param name="name">A short human-readable name.</param>
    /// <param name="isReducedByMemoryErrorCorrection">Whether hardware memory error correction reduces this channel's corruption rate.</param>
    /// <returns>The channel.</returns>
    public static CorruptionChannel Create(int code, string name, bool isReducedByMemoryErrorCorrection)
    {
        return new CorruptionChannel(code, name, isReducedByMemoryErrorCorrection);
    }

    /// <summary>Determines whether this channel has the same <see cref="Code"/> as another.</summary>
    /// <param name="other">The other channel.</param>
    /// <returns><see langword="true"/> when the codes match.</returns>
    public bool Equals(CorruptionChannel other)
    {
        return Code == other.Code;
    }

    /// <summary>Determines whether this channel equals another object.</summary>
    /// <param name="obj">The other object.</param>
    /// <returns><see langword="true"/> when it is a channel with the same code.</returns>
    public override bool Equals(object? obj)
    {
        return obj is CorruptionChannel other && Equals(other);
    }

    /// <summary>Gets a hash code derived from the channel code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return Code;
    }

    /// <summary>Determines whether two channels have the same code.</summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <returns><see langword="true"/> when the codes match.</returns>
    public static bool operator ==(CorruptionChannel left, CorruptionChannel right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two channels have different codes.</summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <returns><see langword="true"/> when the codes differ.</returns>
    public static bool operator !=(CorruptionChannel left, CorruptionChannel right)
    {
        return !left.Equals(right);
    }
}
