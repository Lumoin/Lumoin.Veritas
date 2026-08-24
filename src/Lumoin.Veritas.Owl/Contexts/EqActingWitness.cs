using System;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The maximal-head shape of a rewrite target for the Eq acting-literal dispatch: a
/// singleton maximal set, where the selected literal is the sole acting literal, or a multi-maximal
/// set, where the acting literal is any maximal-set member that sources the rewrite (band or
/// probability atoms, or literals split by the variable-versus-individual incomparability). The
/// dispatch enumerates every maximal target literal for the rewrite-slot gate in both shapes; the
/// discriminator names the per-target classification.</summary>
internal enum HeadShape
{
    /// <summary>A one-member maximal set: the selected literal is the sole acting literal.</summary>
    SingletonMaximal = 0,

    /// <summary>A multi-member maximal set: the acting literal is chosen per source-mention, not by first-maximal.</summary>
    MultiMaximal = 1,
}

/// <summary>A head-span index proven to be a member of a clause head's maximal set under the
/// selection order — the provenance the Eq acting-literal dispatch enumerates via
/// <see cref="ContextTermOrder.CollectMaximalHead"/>. Distinct from a raw head index so an
/// acting-literal selection can only be indexed by a maximal-set member, never by a first-maximal
/// shortcut value.</summary>
/// <param name="Value">The head-span index.</param>
internal readonly record struct MaximalHeadIndex(int Value);

/// <summary>A construction-proven acting equality for one Eq firing: a maximal head literal of the
/// equality premise that genuinely sources the rewrite. The sole constructor <see cref="TryFrom"/>
/// establishes the four invariants the Eq rule's residual subtraction and orientation rely on, so a
/// value of this type cannot carry a literal that did not source the rewrite. Consumed by the Eq
/// application, whose residual subtraction removes exactly <see cref="Literal"/> from the premise
/// head.</summary>
internal readonly record struct ActingEquality
{
    /// <summary>The acting equality literal — a maximal head literal of the equality premise.</summary>
    public DlLiteral Literal { get; }

    /// <summary>The rewrite source term <c>s1</c> the firing rewrites from.</summary>
    public DlTerm FromTerm { get; }

    /// <summary>The rewrite replacement term <c>t1</c> — the equality's other side.</summary>
    public DlTerm Replacement { get; }

    /// <summary>The validated maximal-head-span index of <see cref="Literal"/> within the premise head.</summary>
    public MaximalHeadIndex Index { get; }

    /// <summary>Initialises a witness from its established fields; callers reach this only through <see cref="TryFrom"/>.</summary>
    /// <param name="literal">The acting equality literal.</param>
    /// <param name="fromTerm">The rewrite source.</param>
    /// <param name="replacement">The rewrite replacement.</param>
    /// <param name="index">The validated maximal-head index.</param>
    private ActingEquality(DlLiteral literal, DlTerm fromTerm, DlTerm replacement, MaximalHeadIndex index)
    {
        Literal = literal;
        FromTerm = fromTerm;
        Replacement = replacement;
        Index = index;
    }

    /// <summary>Attempts to build the witness for a caller-attested maximal index of the premise head,
    /// establishing the acting-equality invariants: the attested index is a member of the caller's
    /// maximal set (INV-4), the indexed literal is an equality (INV-1), its two sides are exactly the
    /// rewrite terms in either orientation (INV-2, unordered), and the source side is an admissible
    /// rewrite source under the partial order (INV-3, delegating verbatim to
    /// <see cref="ContextTermOrder.IsRewriteSourceSide"/>). Failure returns <see langword="false"/>
    /// with <paramref name="witness"/> left at its default: at a production mint site the dispatch loop
    /// enumerates only qualifying, gated literals, so a failure there is an invariant violation, never
    /// an expected condition.</summary>
    /// <param name="head">The equality premise's head span.</param>
    /// <param name="maximalSet">The maximal-head indexes the dispatch loop already collected; read by value and never stored.</param>
    /// <param name="maximalIndex">The attested maximal index into <paramref name="head"/>.</param>
    /// <param name="fromTerm">The rewrite source <c>s1</c>.</param>
    /// <param name="replacement">The rewrite replacement <c>t1</c>.</param>
    /// <param name="witness">The established witness when the attempt succeeds; the default otherwise.</param>
    /// <returns><see langword="true"/> when the four invariants hold; otherwise <see langword="false"/>.</returns>
    public static bool TryFrom(ReadOnlySpan<DlLiteral> head, ReadOnlySpan<int> maximalSet, int maximalIndex, DlTerm fromTerm, DlTerm replacement, out ActingEquality witness)
    {
        witness = default;
        if(maximalIndex < 0 || maximalIndex >= head.Length || !IsMaximalSetMember(maximalSet, maximalIndex))
        {
            return false;
        }

        DlLiteral literal = head[maximalIndex];
        bool connectsRewriteTerms = (literal.First.Equals(fromTerm) && literal.Second.Equals(replacement))
            || (literal.First.Equals(replacement) && literal.Second.Equals(fromTerm));
        if(literal.Kind != DlLiteralKind.Equality
            || !connectsRewriteTerms
            || !ContextTermOrder.IsRewriteSourceSide(fromTerm, replacement))
        {
            return false;
        }

        witness = new ActingEquality(literal, fromTerm, replacement, new MaximalHeadIndex(maximalIndex));

        return true;
    }

    /// <summary>Whether an index is a member of a maximal set — a linear scan of the small already-collected buffer, so maximality is proven as set membership at the attested index rather than a first-maximal read.</summary>
    /// <param name="maximalSet">The maximal-head indexes to scan.</param>
    /// <param name="index">The attested index.</param>
    /// <returns><see langword="true"/> when the index occurs in the set.</returns>
    private static bool IsMaximalSetMember(ReadOnlySpan<int> maximalSet, int index)
    {
        for(int i = 0; i < maximalSet.Length; i++)
        {
            if(maximalSet[i] == index)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A construction-proven acting target for one Eq firing: a target head literal that
/// mentions the rewrite source in a rewrite-eligible slot. The sole constructor <see cref="TryFrom"/>
/// establishes the target-mention invariant in its slot-eligible form, so a value of this type is
/// always a genuine rewrite occurrence the residual subtraction removes exactly.</summary>
internal readonly record struct ActingTarget
{
    /// <summary>The acting target literal — a rewrite occurrence of the source term.</summary>
    public DlLiteral Literal { get; }

    /// <summary>Initialises a witness from its established literal; callers reach this only through <see cref="TryFrom"/>.</summary>
    /// <param name="literal">The acting target literal.</param>
    private ActingTarget(DlLiteral literal)
    {
        Literal = literal;
    }

    /// <summary>Attempts to build the witness, establishing that <paramref name="literal"/> mentions
    /// <paramref name="fromTerm"/> in a rewrite-eligible slot — every non-variable slot of a concept or
    /// role atom, and each (in)equality side not strictly dominated by its other side, with the concept
    /// second slot never eligible — by delegating verbatim to
    /// <see cref="ContextSaturationEngine.MentionsInRewritableSlot"/>. Failure returns
    /// <see langword="false"/> with <paramref name="witness"/> left at its default: at a production mint
    /// site the dispatch gates on the same predicate before construction, so a failure there is an
    /// invariant violation, never an expected condition.</summary>
    /// <param name="literal">The candidate target literal.</param>
    /// <param name="fromTerm">The rewrite source <c>s1</c>.</param>
    /// <param name="witness">The established witness when the attempt succeeds; the default otherwise.</param>
    /// <returns><see langword="true"/> when the literal mentions the source in a rewrite-eligible slot.</returns>
    public static bool TryFrom(DlLiteral literal, DlTerm fromTerm, out ActingTarget witness)
    {
        if(ContextSaturationEngine.MentionsInRewritableSlot(literal, fromTerm))
        {
            witness = new ActingTarget(literal);

            return true;
        }

        witness = default;

        return false;
    }
}
