using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// An ALC concept in negation normal form: negation sits only on atoms,
/// so a tableau clash is an atom meeting its negation and nothing else.
/// </summary>
internal abstract record AlcConcept;

/// <summary>The universal concept ⊤.</summary>
[DebuggerDisplay("⊤")]
internal sealed record AlcTop: AlcConcept
{
    /// <summary>The shared instance.</summary>
    public static AlcTop Instance { get; } = new();
}

/// <summary>The empty concept ⊥ — its presence in a node label is a clash.</summary>
[DebuggerDisplay("⊥")]
internal sealed record AlcBottom: AlcConcept
{
    /// <summary>The shared instance.</summary>
    public static AlcBottom Instance { get; } = new();
}

/// <summary>A named class.</summary>
/// <param name="Class">The class IRI.</param>
[DebuggerDisplay("Atom {Class}")]
internal sealed record AlcAtom(Utf8String Class): AlcConcept;

/// <summary>A negated named class — the only negation NNF leaves standing.</summary>
/// <param name="Operand">The negated atom.</param>
[DebuggerDisplay("¬{Operand}")]
internal sealed record AlcNot(AlcAtom Operand): AlcConcept;

/// <summary>A conjunction.</summary>
/// <param name="Operands">The conjuncts, in deterministic order.</param>
[DebuggerDisplay("And ({Operands.Count})")]
internal sealed record AlcAnd(IReadOnlyList<AlcConcept> Operands): AlcConcept
{
    /// <summary>Structural equality over the ordered operand list.</summary>
    /// <param name="other">The other conjunction.</param>
    /// <returns><see langword="true"/> when the operand sequences match.</returns>
    public bool Equals(AlcAnd? other)
    {
        return other is not null && SequenceEquality.Equals(Operands, other.Operands);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return SequenceEquality.Hash(typeof(AlcAnd), Operands);
    }
}

/// <summary>A disjunction — the tableau's branch point.</summary>
/// <param name="Operands">The disjuncts, in deterministic order.</param>
[DebuggerDisplay("Or ({Operands.Count})")]
internal sealed record AlcOr(IReadOnlyList<AlcConcept> Operands): AlcConcept
{
    /// <summary>Structural equality over the ordered operand list.</summary>
    /// <param name="other">The other disjunction.</param>
    /// <returns><see langword="true"/> when the operand sequences match.</returns>
    public bool Equals(AlcOr? other)
    {
        return other is not null && SequenceEquality.Equals(Operands, other.Operands);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return SequenceEquality.Hash(typeof(AlcOr), Operands);
    }
}

/// <summary>
/// A role in a restriction: a named object property used forwards, or its
/// inverse (<c>ObjectInverseOf</c>). The named property is carried by IRI and
/// <see cref="IsInverse"/> distinguishes <c>r</c> from <c>r⁻</c>. Inversion is
/// an involution — the inverse of an inverse is the role itself — so a role and
/// its inverse share one IRI and differ only in the flag.
/// </summary>
/// <param name="Iri">The named object property IRI.</param>
/// <param name="IsInverse">Whether the role is the inverse of the named property.</param>
[DebuggerDisplay("{IsInverse ? \"⁻\" : \"\"}{Iri}")]
internal readonly record struct AlcRole(Utf8String Iri, bool IsInverse)
{
    /// <summary>The forward (non-inverse) role over a named property IRI.</summary>
    /// <param name="iri">The named property IRI.</param>
    /// <returns>The forward role.</returns>
    public static AlcRole Forward(Utf8String iri)
    {
        return new AlcRole(iri, IsInverse: false);
    }

    /// <summary>This role's inverse — the same named property with the direction flipped.</summary>
    /// <returns>The inverse role.</returns>
    public AlcRole Inverse()
    {
        return new AlcRole(Iri, !IsInverse);
    }
}

/// <summary>An existential restriction ∃r.C.</summary>
/// <param name="Role">The role, forward or inverse.</param>
/// <param name="Filler">The filler concept.</param>
[DebuggerDisplay("∃{Role}")]
internal sealed record AlcExists(AlcRole Role, AlcConcept Filler): AlcConcept;

/// <summary>A universal restriction ∀r.C.</summary>
/// <param name="Role">The role, forward or inverse.</param>
/// <param name="Filler">The filler concept.</param>
[DebuggerDisplay("∀{Role}")]
internal sealed record AlcForAll(AlcRole Role, AlcConcept Filler): AlcConcept;

/// <summary>
/// An existential data restriction ∃dp.R — a demand that the data property
/// have at least one value in the data range. A concrete-domain leaf: it
/// names no object successor and is discharged by the datatype
/// satisfiability checker, not by tableau expansion.
/// </summary>
/// <param name="Property">The data-property IRI.</param>
/// <param name="Range">The required data range.</param>
[DebuggerDisplay("∃data {Property}")]
internal sealed record AlcDataSome(Utf8String Property, OwlDataRange Range): AlcConcept;

/// <summary>
/// A universal data restriction ∀dp.R — every value of the data property
/// lies in the data range. Like its object counterpart it imposes nothing
/// on its own; it constrains the value each <see cref="AlcDataSome"/> or
/// grounded assertion on the same property must take.
/// </summary>
/// <param name="Property">The data-property IRI.</param>
/// <param name="Range">The constraining data range.</param>
[DebuggerDisplay("∀data {Property}")]
internal sealed record AlcDataAll(Utf8String Property, OwlDataRange Range): AlcConcept;

/// <summary>
/// A qualified minimum data cardinality ≥n dp.R — the data property has at
/// least <paramref name="Count"/> pairwise-distinct values, each in the
/// data range. It pairs with <see cref="AlcDataMaxCard"/>: a positive exact
/// cardinality enters the calculus as the conjunction of the two halves, while
/// a negated data cardinality forces value merging (the choose-rule cliff) and
/// stays out of the fragment.
/// </summary>
/// <param name="Count">The minimum number of distinct values.</param>
/// <param name="Property">The data-property IRI.</param>
/// <param name="Range">The data range each counted value lies in.</param>
[DebuggerDisplay("≥{Count} data {Property}")]
internal sealed record AlcDataMinCard(int Count, Utf8String Property, OwlDataRange Range): AlcConcept;

/// <summary>
/// A qualified maximum data cardinality ≤n dp.R — the data property has at
/// most <paramref name="Count"/> pairwise-distinct values in the data range.
/// A concrete-domain leaf like its minimum sibling: it names no successor of
/// its own and is discharged by the datatype sidecar's per-property max-slot
/// pool, which merges the slot's forced values into one satisfiability check
/// only where the range is the literal top — a qualified bound counts only its
/// range-typed fillers — and otherwise certifies a witness model or abstains.
/// </summary>
/// <param name="Count">The maximum number of distinct values.</param>
/// <param name="Property">The data-property IRI.</param>
/// <param name="Range">The data range each counted value lies in.</param>
[DebuggerDisplay("≤{Count} data {Property}")]
internal sealed record AlcDataMaxCard(int Count, Utf8String Property, OwlDataRange Range): AlcConcept;

/// <summary>Ordered-sequence equality for the n-ary concept records.</summary>
internal static class SequenceEquality
{
    /// <summary>Whether two operand sequences are element-wise equal.</summary>
    /// <param name="left">The first sequence.</param>
    /// <param name="right">The second sequence.</param>
    /// <returns><see langword="true"/> on element-wise equality.</returns>
    public static bool Equals(IReadOnlyList<AlcConcept> left, IReadOnlyList<AlcConcept> right)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        for(int i = 0; i < left.Count; i++)
        {
            if(!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A hash over the operand sequence, seeded by the concrete type.</summary>
    /// <param name="type">The concrete record type.</param>
    /// <param name="operands">The operand sequence.</param>
    /// <returns>The combined hash.</returns>
    public static int Hash(System.Type type, IReadOnlyList<AlcConcept> operands)
    {
        System.HashCode hash = new();
        hash.Add(type);
        foreach(AlcConcept operand in operands)
        {
            hash.Add(operand);
        }

        return hash.ToHashCode();
    }
}
