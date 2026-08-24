using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype whose value space is the intersection of several member datatypes. A value is a
/// member when every member accepts it. Emptiness is decided by a candidate walk over an enumerated
/// member's finite set — every witness must lie in it — or, when all members expose an automaton, by the
/// product of the member languages with the conjunction. Admissibility and the self-test fold over the
/// members.
/// </summary>
public sealed class IntersectionDatatype : RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The member datatypes being intersected.</summary>
    private IReadOnlyList<RegisteredDatatype> Members { get; }

    /// <summary>The automaton budgets the composition runs under.</summary>
    private AutomatonBudgets Budgets { get; }

    /// <summary>Creates an intersection datatype.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="members">The member datatypes.</param>
    /// <param name="budgets">The automaton budgets, or <see langword="null"/> for the shared defaults.</param>
    public IntersectionDatatype(Utf8String datatypeIri, IReadOnlyList<RegisteredDatatype> members, AutomatonBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        Iri = datatypeIri;
        Members = members;
        Budgets = budgets ?? AutomatonBudgets.Default;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override bool SelfCertified
    {
        get
        {
            foreach(RegisteredDatatype member in Members)
            {
                if(member.SelfCertified)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool allIn = true;
        foreach(RegisteredDatatype member in Members)
        {
            DatatypeMembership membership = member.Contains(value);
            if(membership == DatatypeMembership.Out)
            {
                return DatatypeMembership.Out;
            }

            if(membership != DatatypeMembership.In)
            {
                allIn = false;
            }
        }

        return allIn ? DatatypeMembership.In : DatatypeMembership.Indeterminate;
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return Members.Count > 0 ? Members[0].SameValue(first, second) : DatatypeValueIdentity.Indeterminate;
    }

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        if(TryEnumeratedCandidates(out IReadOnlyList<Literal> candidates))
        {
            bool anyAdmitted = false;
            bool allExcluded = true;
            foreach(Literal candidate in candidates)
            {
                CandidateStatus status = ClassifyCandidate(candidate, question);
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

        return DecideByAutomata(question);
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        //The intersection count is a sound abstention unless a member bounds it; deferred to the counting-capable members.
        return DatatypeCountBound.Unknown;
    }

    /// <inheritdoc/>
    internal override AdmissibilityResult CheckAdmissibility(AutomatonBudgets budgets)
    {
        foreach(RegisteredDatatype member in Members)
        {
            AdmissibilityResult result = member.CheckAdmissibility(budgets);
            if(!result.Admissible)
            {
                return result;
            }
        }

        return AdmissibilityResult.Accepted;
    }

    /// <inheritdoc/>
    internal override bool RunSelfTest(AutomatonBudgets budgets)
    {
        foreach(RegisteredDatatype member in Members)
        {
            if(!member.RunSelfTest(budgets))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Finds the candidate set of the first enumerated member — a complete superset of the intersection value space.</summary>
    /// <param name="candidates">The candidate literals, when an enumerated member is present.</param>
    /// <returns><see langword="true"/> when a candidate set was found.</returns>
    private bool TryEnumeratedCandidates(out IReadOnlyList<Literal> candidates)
    {
        foreach(RegisteredDatatype member in Members)
        {
            if(member is EnumeratedDatatype enumerated)
            {
                candidates = enumerated.EnumeratedMembers;

                return true;
            }
        }

        candidates = [];

        return false;
    }

    /// <summary>Classifies a candidate against all members and the conjunction.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="question">The conjunction.</param>
    /// <returns>The candidate status.</returns>
    private CandidateStatus ClassifyCandidate(Literal candidate, in DatatypeConjunction question)
    {
        bool admissible = true;
        foreach(RegisteredDatatype member in Members)
        {
            DatatypeMembership membership = member.Contains(candidate);
            if(membership == DatatypeMembership.Out)
            {
                return CandidateStatus.Excluded;
            }

            if(membership != DatatypeMembership.In)
            {
                admissible = false;
            }
        }

        foreach(OwlFacetRestriction facet in question.PositiveFacets)
        {
            DatatypeMembership membership = FacetEvaluator.Evaluate(facet, candidate, Budgets);
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
            if(negated is OwlDataOneOf oneOf && DatatypeLexical.EnumerationMembership(candidate, oneOf.Literals) == DatatypeMembership.In)
            {
                return CandidateStatus.Excluded;
            }
        }

        return admissible ? CandidateStatus.Admitted : CandidateStatus.Undetermined;
    }

    /// <summary>Decides emptiness by the product of the member languages when every member exposes an automaton.</summary>
    /// <param name="question">The conjunction.</param>
    /// <returns>The satisfiability verdict.</returns>
    private DatatypeSatisfiability DecideByAutomata(in DatatypeConjunction question)
    {
        if(Members.Count == 0)
        {
            return DatatypeSatisfiability.Unknown;
        }

        List<NondeterministicAutomaton> languages = [];
        foreach(RegisteredDatatype member in Members)
        {
            if(!member.TryGetLanguageAutomaton(Budgets, out NondeterministicAutomaton? automaton))
            {
                return DatatypeSatisfiability.Unknown;
            }

            languages.Add(automaton!);
        }

        NondeterministicAutomaton product = languages[0];
        for(int i = 1; i < languages.Count; i++)
        {
            ProductResult result = AutomatonComposition.Product(product, languages[i], Budgets.MaxProductStates);
            if(result.Status != ProductStatus.Built)
            {
                return DatatypeSatisfiability.Unknown;
            }

            product = result.Automaton!;
        }

        return DatatypeAutomata.DecideEmptiness(product, question, Budgets);
    }

    /// <summary>The status of a candidate against the members and conjunction.</summary>
    private enum CandidateStatus
    {
        /// <summary>Every member and the conjunction admit the candidate.</summary>
        Admitted,

        /// <summary>A member or the conjunction excludes the candidate.</summary>
        Excluded,

        /// <summary>The candidate is neither provably admitted nor provably excluded.</summary>
        Undetermined,
    }
}
