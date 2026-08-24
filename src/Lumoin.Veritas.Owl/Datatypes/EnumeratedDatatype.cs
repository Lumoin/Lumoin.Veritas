using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype whose value space is a finite list of literals. Membership and identity are
/// decided by value identity over the members; the distinct-value count is the number of pairwise-distinct
/// members admitted by a conjunction, when the conjunction imposes no facet or negation the enumeration
/// cannot price.
/// </summary>
public sealed class EnumeratedDatatype : RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The enumerated members.</summary>
    private IReadOnlyList<Literal> Members { get; }

    /// <summary>The enumerated members, exposed for a combinator that needs a finite candidate set.</summary>
    internal IReadOnlyList<Literal> EnumeratedMembers => Members;

    /// <summary>Creates an enumerated datatype over a finite list of literals.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="members">The enumerated members.</param>
    public EnumeratedDatatype(Utf8String datatypeIri, IReadOnlyList<Literal> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        Iri = datatypeIri;
        Members = members;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return DatatypeLexical.EnumerationMembership(value, Members);
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return DatatypeLexical.Identity(first, second);
    }

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        bool anyAdmitted = false;
        bool allExcluded = true;
        foreach(Literal member in Members)
        {
            CandidateStatus status = Classify(member, question);
            if(status == CandidateStatus.Admitted)
            {
                anyAdmitted = true;
            }

            if(status != CandidateStatus.Excluded)
            {
                allExcluded = false;
            }
        }

        if(anyAdmitted)
        {
            return DatatypeSatisfiability.Satisfiable;
        }

        return allExcluded ? DatatypeSatisfiability.Unsatisfiable : DatatypeSatisfiability.Unknown;
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        List<Literal> admitted = [];
        bool anyUndetermined = false;
        foreach(Literal member in Members)
        {
            CandidateStatus status = Classify(member, question);
            if(status == CandidateStatus.Excluded)
            {
                continue;
            }

            if(status == CandidateStatus.Undetermined)
            {
                anyUndetermined = true;

                continue;
            }

            if(IsDistinctFromAll(member, admitted))
            {
                admitted.Add(member);
            }
        }

        return anyUndetermined ? DatatypeCountBound.Unknown : DatatypeCountBound.Of(admitted.Count);
    }

    /// <summary>Classifies a member against a conjunction: admitted when it satisfies every positive facet and no negated atom, excluded when a conjunct rules it out, undetermined otherwise.</summary>
    /// <param name="member">The member.</param>
    /// <param name="question">The conjunction.</param>
    /// <returns>The candidate status.</returns>
    private static CandidateStatus Classify(Literal member, in DatatypeConjunction question)
    {
        bool admissible = true;
        foreach(OwlFacetRestriction facet in question.PositiveFacets)
        {
            DatatypeMembership membership = FacetEvaluator.Evaluate(facet, member, Automata.AutomatonBudgets.Default);
            if(membership == DatatypeMembership.Out)
            {
                return CandidateStatus.Excluded;
            }

            if(membership == DatatypeMembership.Indeterminate)
            {
                admissible = false;
            }
        }

        foreach(OwlDataRange negated in question.NegatedAtoms)
        {
            DatatypeMembership membership = NegatedMembership(member, negated);
            if(membership == DatatypeMembership.In)
            {
                return CandidateStatus.Excluded;
            }

            if(membership == DatatypeMembership.Indeterminate)
            {
                admissible = false;
            }
        }

        return admissible ? CandidateStatus.Admitted : CandidateStatus.Undetermined;
    }

    /// <summary>The membership of a member in a negated enumeration atom; other negated ranges are not priced here.</summary>
    /// <param name="member">The member.</param>
    /// <param name="negated">The negated data range.</param>
    /// <returns>The membership verdict.</returns>
    private static DatatypeMembership NegatedMembership(Literal member, OwlDataRange negated)
    {
        return negated is OwlDataOneOf oneOf ? DatatypeLexical.EnumerationMembership(member, oneOf.Literals) : DatatypeMembership.Indeterminate;
    }

    /// <summary>Whether a member is provably distinct from every member of a set.</summary>
    /// <param name="member">The member.</param>
    /// <param name="others">The set to test against.</param>
    /// <returns><see langword="true"/> when the member differs from all.</returns>
    private static bool IsDistinctFromAll(Literal member, List<Literal> others)
    {
        foreach(Literal other in others)
        {
            if(DatatypeLexical.Identity(member, other) != DatatypeValueIdentity.Distinct)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The status of an enumerated member against a conjunction.</summary>
    private enum CandidateStatus
    {
        /// <summary>The member provably satisfies the conjunction.</summary>
        Admitted,

        /// <summary>A conjunct provably rules the member out.</summary>
        Excluded,

        /// <summary>The member is neither provably admitted nor provably excluded.</summary>
        Undetermined,
    }
}
