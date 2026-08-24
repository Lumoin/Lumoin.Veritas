using System;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// A class family on the epistemic surface — the owner of one contiguous four-digit code block.
/// A <see langword="readonly"/> <see langword="struct"/> pairing a band index with the family's
/// canonical <c>u8</c> name.
/// </summary>
/// <remarks>
/// The family owns the integer block [<see cref="BlockStart"/>, <see cref="BlockInclusiveEnd"/>] =
/// [<see cref="BandIndex"/> times ten thousand, <see cref="BandIndex"/> times ten thousand plus
/// nine thousand nine hundred ninety-nine]. A valid band index is at least 1; band 0 is
/// reserved-invalid (it is the <see langword="default"/> struct's band and names no family).
/// Identity is the <see cref="BandIndex"/> alone — the <see cref="Name"/> is descriptive, so two
/// families that disagree on the name but share a band index are a band-reservation collision the
/// registry ladder rejects.
/// </remarks>
public readonly struct EpistemicReasonClassFamily: IEquatable<EpistemicReasonClassFamily>
{
    /// <summary>Constructs a family owning the block for the given band index.</summary>
    /// <param name="bandIndex">The band index and identity; a valid value is at least 1.</param>
    /// <param name="name">The family's canonical <c>u8</c> name.</param>
    public EpistemicReasonClassFamily(int bandIndex, ReadOnlyMemory<byte> name)
    {
        BandIndex = bandIndex;
        Name = name;
    }

    /// <summary>The band index — the family's identity and the high-digit prefix of every code it owns.</summary>
    public int BandIndex { get; }

    /// <summary>The family's canonical name as <c>u8</c> bytes. Descriptive, not part of identity.</summary>
    public ReadOnlyMemory<byte> Name { get; }

    /// <summary>The inclusive lower bound of the family's owned code block.</summary>
    public int BlockStart => BandIndex * 10000;

    /// <summary>The inclusive upper bound of the family's owned code block.</summary>
    public int BlockInclusiveEnd => (BandIndex * 10000) + 9999;

    /// <summary>Whether the given code falls inside this family's owned block.</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code's class band equals this family's band index.</returns>
    public bool Contains(EpistemicReasonCode code) => code.ClassBand == BandIndex;

    /// <inheritdoc/>
    public bool Equals(EpistemicReasonClassFamily other) => BandIndex == other.BandIndex;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EpistemicReasonClassFamily other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => BandIndex;

    /// <summary>Tests two families for equality by band index.</summary>
    /// <param name="left">The first family.</param>
    /// <param name="right">The second family.</param>
    /// <returns><see langword="true"/> when the band indices are equal.</returns>
    public static bool operator ==(EpistemicReasonClassFamily left, EpistemicReasonClassFamily right) => left.Equals(right);

    /// <summary>Tests two families for inequality by band index.</summary>
    /// <param name="left">The first family.</param>
    /// <param name="right">The second family.</param>
    /// <returns><see langword="true"/> when the band indices differ.</returns>
    public static bool operator !=(EpistemicReasonClassFamily left, EpistemicReasonClassFamily right) => !left.Equals(right);
}
