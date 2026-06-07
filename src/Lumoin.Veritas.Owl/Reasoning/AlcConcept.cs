using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

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

/// <summary>An existential restriction ∃r.C.</summary>
/// <param name="Role">The role IRI.</param>
/// <param name="Filler">The filler concept.</param>
[DebuggerDisplay("∃{Role}")]
internal sealed record AlcExists(Utf8String Role, AlcConcept Filler): AlcConcept;

/// <summary>A universal restriction ∀r.C.</summary>
/// <param name="Role">The role IRI.</param>
/// <param name="Filler">The filler concept.</param>
[DebuggerDisplay("∀{Role}")]
internal sealed record AlcForAll(Utf8String Role, AlcConcept Filler): AlcConcept;

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
