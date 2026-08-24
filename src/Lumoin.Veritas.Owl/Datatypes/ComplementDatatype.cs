using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype whose value space is the complement of an inner datatype within the ambient data
/// domain. Membership is the flip of the inner membership. The emptiness question is decided by
/// complementing the inner language automaton and folding the conjunction, when the inner definition
/// exposes one; otherwise it abstains. Admissibility and the self-test delegate to the inner definition.
/// </summary>
public sealed class ComplementDatatype : RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The inner datatype being complemented.</summary>
    private RegisteredDatatype Inner { get; }

    /// <summary>The automaton budgets the complement runs under.</summary>
    private AutomatonBudgets Budgets { get; }

    /// <summary>Creates a complement datatype.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="inner">The inner datatype.</param>
    /// <param name="budgets">The automaton budgets, or <see langword="null"/> for the shared defaults.</param>
    public ComplementDatatype(Utf8String datatypeIri, RegisteredDatatype inner, AutomatonBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Iri = datatypeIri;
        Inner = inner;
        Budgets = budgets ?? AutomatonBudgets.Default;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override bool SelfCertified => Inner.SelfCertified;

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Inner.Contains(value) switch
        {
            DatatypeMembership.In => DatatypeMembership.Out,
            DatatypeMembership.Out => DatatypeMembership.In,
            _ => DatatypeMembership.Indeterminate
        };
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return Inner.SameValue(first, second);
    }

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        if(!Inner.TryGetLanguageAutomaton(Budgets, out NondeterministicAutomaton? language))
        {
            return DatatypeSatisfiability.Unknown;
        }

        if(DatatypeAutomata.TryComplement(language!, Budgets, out NondeterministicAutomaton? complement) != NegatedStatus.Modelled)
        {
            return DatatypeSatisfiability.Unknown;
        }

        return DatatypeAutomata.DecideEmptiness(complement!, question, Budgets);
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        //A complement of a finite inner set is typically infinite over the string domain; the exact count is a sound abstention.
        return DatatypeCountBound.Unknown;
    }

    /// <inheritdoc/>
    internal override AdmissibilityResult CheckAdmissibility(AutomatonBudgets budgets)
    {
        return Inner.CheckAdmissibility(budgets);
    }

    /// <inheritdoc/>
    internal override bool RunSelfTest(AutomatonBudgets budgets)
    {
        return Inner.RunSelfTest(budgets);
    }
}
