using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A peer's fetched sketch response that OWNS its pooled backing: the value the transport-facing
/// <see cref="AsyncSketchFetchDelegate"/> yields, whose receiver disposes it exactly once to return the rental to its
/// pool. Alongside the image bytes it carries the peer's wire stamp — a <see cref="SketchChannelDomain"/> and a
/// dictionary epoch — so a session refuses a contract or epoch mismatch by name before any combine. An absent peer
/// is the <see cref="Unavailable"/> value, which carries no frame (<see cref="IsUnavailable"/>), no rental, and
/// disposes as a no-op; a stamped frame that carries no image (<see cref="HasImage"/> is <see langword="false"/>) is
/// the peer's stamped decline, a distinct state the session also reads as an unavailable peer. The framing is the
/// only producer — the copy out of the channel buffer into pooled memory happens once, in
/// <see cref="SketchChannelFraming.ReadOwnedImage"/>, and the in-process serve path — so a consumer never sees the
/// wire buffer.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "SketchFetchResult is a single-owner disposal handle over a pooled buffer, not a value-compared type. The default all-field Equals is sufficient (two results are equal only when their owner references and stamps match); an explicit equality contract would suggest a by-content comparison the ownership handle does not have.")]
public readonly struct SketchFetchResult: IDisposable
{
    /// <summary>The pooled owner of the image bytes, or <see langword="null"/> for an unavailable peer or a stamped decline that carries no image.</summary>
    private IMemoryOwner<byte>? Owner { get; }

    /// <summary>The number of image bytes at the start of the owner's memory.</summary>
    private int Length { get; }

    /// <summary>Creates a stamped result over an optional pooled image; the framing (and the in-process serve path) is the only producer.</summary>
    /// <param name="owner">The pooled owner of the image bytes, or <see langword="null"/> for a stamped decline with no image; the result disposes it.</param>
    /// <param name="length">The number of image bytes at the start of <paramref name="owner"/>'s memory; zero when there is no image.</param>
    /// <param name="domain">The reconciliation domain the peer stamped the response with.</param>
    /// <param name="dictionaryEpoch">The dictionary epoch the peer stamped the response with.</param>
    internal SketchFetchResult(IMemoryOwner<byte>? owner, int length, SketchChannelDomain domain, ulong dictionaryEpoch)
    {
        Owner = owner;
        Length = length;
        Domain = domain;
        DictionaryEpoch = dictionaryEpoch;
    }

    /// <summary>The absent-peer result: it carries no frame, reports <see cref="IsUnavailable"/>, exposes an empty <see cref="Image"/>, and disposes as a no-op.</summary>
    public static SketchFetchResult Unavailable { get; }

    /// <summary>The reconciliation domain the peer stamped the response with; the default (no valid domain) for an <see cref="Unavailable"/> result.</summary>
    public SketchChannelDomain Domain { get; }

    /// <summary>The dictionary epoch the peer stamped the response with; unspecified for an <see cref="Unavailable"/> result.</summary>
    public ulong DictionaryEpoch { get; }

    /// <summary>Whether no framed response is carried — an unreachable peer that sent nothing at all; a stamped decline is a frame, so it is NOT unavailable, only lacking an image.</summary>
    public bool IsUnavailable => Domain is not (SketchChannelDomain.Structural or SketchChannelDomain.ContentHash);

    /// <summary>Whether a framed response carries a non-empty sketch image; <see langword="false"/> for an unavailable peer or a stamped decline that built no image.</summary>
    public bool HasImage => Owner is not null && Length > 0;

    /// <summary>The peer's sketch image, or an empty span when no image is carried; valid until this result is disposed.</summary>
    public ReadOnlyMemory<byte> Image => HasImage ? Owner!.Memory[..Length] : ReadOnlyMemory<byte>.Empty;

    /// <summary>Returns the pooled image to its pool; the receiver disposes exactly once, and disposing an <see cref="Unavailable"/> result or a stamped decline (neither holds a rental) is a no-op.</summary>
    public void Dispose()
    {
        Owner?.Dispose();
    }
}
