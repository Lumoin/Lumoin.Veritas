using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// A registration's projection-coverage declaration — the explicit statement of which projections
/// carry a reason code, or the explicit statement that no projection plumbing exists for it yet.
/// </summary>
/// <remarks>
/// <para>
/// The three states are distinct and load-bearing at the registry's shape-sanity rung:
/// </para>
/// <list type="bullet">
/// <item><description><em>Undeclared</em> — the <see langword="default"/> struct. This is the
/// forbidden ABSENCE of a declaration; the ladder rejects it.</description></item>
/// <item><description><em>Deferred</em> — an explicit positive statement that no projection
/// plumbing exists yet (registration-identity-only). This is a VALID declaration.</description></item>
/// <item><description><em>Declared</em> — the registration names the projections that carry
/// it; each named projection must resolve at the ladder's self-test rung.</description></item>
/// </list>
/// </remarks>
public readonly struct EpistemicProjectionCoverage: IEquatable<EpistemicProjectionCoverage>
{
    /// <summary>The undeclared kind — the forbidden absence of a declaration.</summary>
    private const int UndeclaredKind = 0;

    /// <summary>The deferred kind — a valid, explicit no-plumbing-yet declaration.</summary>
    private const int DeferredKind = 1;

    /// <summary>The declared kind — the registration names the projections that carry it.</summary>
    private const int DeclaredKind = 2;

    /// <summary>The shared empty projection-name list a non-declared coverage exposes.</summary>
    private static readonly IReadOnlyList<ReadOnlyMemory<byte>> NoProjectionNames = [];

    /// <summary>Which of the three states this coverage is in.</summary>
    private readonly int kind;

    /// <summary>The declared projection names when <see cref="kind"/> is <see cref="DeclaredKind"/>; otherwise <see langword="null"/>.</summary>
    private readonly IReadOnlyList<ReadOnlyMemory<byte>>? projectionNames;

    /// <summary>Constructs a coverage in the given state.</summary>
    /// <param name="kind">The state discriminator.</param>
    /// <param name="projectionNames">The declared projection names, or <see langword="null"/>.</param>
    private EpistemicProjectionCoverage(int kind, IReadOnlyList<ReadOnlyMemory<byte>>? projectionNames)
    {
        this.kind = kind;
        this.projectionNames = projectionNames;
    }

    /// <summary>The explicit deferred declaration — a positive statement that no projection plumbing exists yet.</summary>
    public static EpistemicProjectionCoverage Deferred => new(DeferredKind, null);

    /// <summary>Declares that the named projections carry the registration.</summary>
    /// <param name="names">The projection names, as <c>u8</c> bytes, that carry the code.</param>
    /// <returns>The declared coverage.</returns>
    /// <exception cref="ArgumentNullException">The names list is <see langword="null"/>.</exception>
    public static EpistemicProjectionCoverage Declare(IReadOnlyList<ReadOnlyMemory<byte>> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return new EpistemicProjectionCoverage(DeclaredKind, names);
    }

    /// <summary>Whether this is the forbidden undeclared absence (the default struct).</summary>
    public bool IsUndeclared => kind == UndeclaredKind;

    /// <summary>Whether this is the explicit deferred declaration.</summary>
    public bool IsDeferred => kind == DeferredKind;

    /// <summary>Whether this names the projections that carry the registration.</summary>
    public bool IsDeclared => kind == DeclaredKind;

    /// <summary>The declared projection names, or an empty list when the coverage is deferred or undeclared.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> ProjectionNames => projectionNames ?? NoProjectionNames;

    /// <summary>Tests two coverages for equality by state, and by projection-name-list identity when declared.</summary>
    /// <param name="other">The coverage to compare against.</param>
    /// <returns><see langword="true"/> when both are in the same state and reference the same declared name list.</returns>
    public bool Equals(EpistemicProjectionCoverage other) => kind == other.kind && ReferenceEquals(projectionNames, other.projectionNames);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EpistemicProjectionCoverage other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(kind, projectionNames);

    /// <summary>Tests two coverages for equality.</summary>
    /// <param name="left">The first coverage.</param>
    /// <param name="right">The second coverage.</param>
    /// <returns><see langword="true"/> when the two coverages are equal.</returns>
    public static bool operator ==(EpistemicProjectionCoverage left, EpistemicProjectionCoverage right) => left.Equals(right);

    /// <summary>Tests two coverages for inequality.</summary>
    /// <param name="left">The first coverage.</param>
    /// <param name="right">The second coverage.</param>
    /// <returns><see langword="true"/> when the two coverages are not equal.</returns>
    public static bool operator !=(EpistemicProjectionCoverage left, EpistemicProjectionCoverage right) => !left.Equals(right);
}
