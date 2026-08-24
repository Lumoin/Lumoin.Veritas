using System;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// A term identifier in canonical space: the representative of its
/// <c>owl:sameAs</c> equivalence clique, produced only by
/// <see cref="OwlSameAsEquivalence.Find"/>. Raw and canonical identifiers
/// never compare or mix directly — unwrapping through <see cref="Id"/> is
/// the visible, deliberate act this type exists to force, so a
/// fixed-identifier read against canonicalized data cannot compile by
/// accident.
/// </summary>
/// <remarks>
/// The constructor is <see langword="internal"/> — the narrowest scope the
/// language admits for a cross-file producer. External assemblies cannot
/// mint a canonical identifier at all; inside the assembly the union-find
/// is the sole sanctioned producer, an accepted residue recorded with the
/// canonicalization design.
/// </remarks>
public readonly record struct CanonicalTermId : IComparable<CanonicalTermId>
{
    /// <summary>The underlying term identifier — the clique representative.</summary>
    public TermId Id { get; }

    /// <summary>Wraps a representative identifier; the union-find is the sole sanctioned caller.</summary>
    /// <param name="id">The representative's term identifier.</param>
    internal CanonicalTermId(TermId id)
    {
        Id = id;
    }

    /// <summary>Orders canonical identifiers by their underlying representative order — the protected tie-break's comparison.</summary>
    /// <param name="other">The compared identifier.</param>
    /// <returns>The comparison of the underlying identifiers.</returns>
    public int CompareTo(CanonicalTermId other)
    {
        return Id.CompareTo(other.Id);
    }

    /// <summary>Whether the left identifier orders strictly before the right.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true"/> when the left orders strictly before the right.</returns>
    public static bool operator <(CanonicalTermId left, CanonicalTermId right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>Whether the left identifier orders before or equal to the right.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true"/> when the left orders before or equal to the right.</returns>
    public static bool operator <=(CanonicalTermId left, CanonicalTermId right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>Whether the left identifier orders strictly after the right.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true"/> when the left orders strictly after the right.</returns>
    public static bool operator >(CanonicalTermId left, CanonicalTermId right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>Whether the left identifier orders after or equal to the right.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true"/> when the left orders after or equal to the right.</returns>
    public static bool operator >=(CanonicalTermId left, CanonicalTermId right)
    {
        return left.CompareTo(right) >= 0;
    }
}
