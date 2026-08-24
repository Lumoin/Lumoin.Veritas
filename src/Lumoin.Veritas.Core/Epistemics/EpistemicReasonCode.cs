using System;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// A reason code on the engine's epistemic surface — the house dynamic-enum identity: a
/// <see langword="readonly"/> <see langword="struct"/> over an <see cref="int"/> with named
/// built-in instances minted by consumers and a <see cref="Create"/> value-construction entry
/// point, mirroring the project's <c>ComputeWorkClass</c> shape.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="int"/> is the whole identity, the equality key, and the wire value. It is laid
/// out in digit bands: the high digits (<see cref="Code"/> divided by ten thousand,
/// <see cref="ClassBand"/>) carry the class family, and the four-digit slot
/// (<see cref="Code"/> modulo ten thousand) carries the specific code within that family. A
/// consumer recovers the class band by integer division without any string parse and without a
/// registry lookup, so an unrecognised specific code still degrades gracefully to its family.
/// </para>
/// <para>
/// Band 0 (a <see cref="Code"/> below ten thousand) is RESERVED-INVALID: it names no class
/// family, so <see langword="default"/>(<see cref="EpistemicReasonCode"/>) is not a registrable
/// identity. The registry's acceptance ladder rejects it at its shape-sanity rung; identity
/// validity is enforced there, not in <see cref="Create"/>, which is pure value construction.
/// </para>
/// <para>
/// Unlike <c>ComputeWorkClass.Create</c>, this type carries NO process-global mutable registry:
/// collision checking is the responsibility of the composition-root <c>Build()</c> ladder, whose
/// registry instance is the single minting authority. <see cref="Create"/> exists only to decode
/// a wire value back into the value type.
/// </para>
/// </remarks>
public readonly struct EpistemicReasonCode: IEquatable<EpistemicReasonCode>, IComparable<EpistemicReasonCode>
{
    /// <summary>Constructs a code over the given integer identity.</summary>
    /// <param name="code">The integer identity, equality key, and wire value.</param>
    private EpistemicReasonCode(int code)
    {
        Code = code;
    }

    /// <summary>The integer identity — the equality key and the wire value.</summary>
    public int Code { get; }

    /// <summary>The class-family band: the high digits of <see cref="Code"/>. Band 0 is reserved-invalid.</summary>
    public int ClassBand => Code / 10000;

    /// <summary>
    /// Constructs a code from an integer identity. This is value construction only — the
    /// wire-decode entry point — and performs no registration or collision check; validity is
    /// established when the code is accepted into a registry through the composition-root ladder.
    /// </summary>
    /// <param name="code">The integer identity.</param>
    /// <returns>The code value.</returns>
    public static EpistemicReasonCode Create(int code)
    {
        return new EpistemicReasonCode(code);
    }

    /// <inheritdoc/>
    public bool Equals(EpistemicReasonCode other) => Code == other.Code;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EpistemicReasonCode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Code;

    /// <inheritdoc/>
    public int CompareTo(EpistemicReasonCode other) => Code.CompareTo(other.Code);

    /// <summary>Tests two codes for equality by integer identity.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when the identities are equal.</returns>
    public static bool operator ==(EpistemicReasonCode left, EpistemicReasonCode right) => left.Equals(right);

    /// <summary>Tests two codes for inequality by integer identity.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when the identities differ.</returns>
    public static bool operator !=(EpistemicReasonCode left, EpistemicReasonCode right) => !left.Equals(right);

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> orders before <paramref name="right"/>.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/>'s identity is the smaller.</returns>
    public static bool operator <(EpistemicReasonCode left, EpistemicReasonCode right) => left.Code < right.Code;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> orders before or with <paramref name="right"/>.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/>'s identity is the smaller or equal.</returns>
    public static bool operator <=(EpistemicReasonCode left, EpistemicReasonCode right) => left.Code <= right.Code;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> orders after <paramref name="right"/>.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/>'s identity is the larger.</returns>
    public static bool operator >(EpistemicReasonCode left, EpistemicReasonCode right) => left.Code > right.Code;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> orders after or with <paramref name="right"/>.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/>'s identity is the larger or equal.</returns>
    public static bool operator >=(EpistemicReasonCode left, EpistemicReasonCode right) => left.Code >= right.Code;
}
