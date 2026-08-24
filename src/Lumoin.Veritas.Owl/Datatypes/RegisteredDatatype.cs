using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// One registered datatype the checker consults where the family classifier
/// answers <see cref="OwlDatatypeFamily.Unknown"/>. A registered datatype owns an
/// IRI and answers the four questions the concrete-domain checker asks of a value
/// space: the emptiness of a facet conjunction, the membership of one value, the
/// identity of two values, and the count of distinct values a conjunction admits.
/// Every answer is three-valued or bounded, so a datatype that cannot decide a
/// question abstains rather than guessing.
/// </summary>
public abstract class RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    public abstract Utf8String DatatypeIri { get; }

    /// <summary>Whether this definition is trusted without a registration self-test — the delegate-backed escape hatch, whose provenance surfaces in module diagnostics.</summary>
    public virtual bool SelfCertified => false;

    /// <summary>Decides the satisfiability of a facet conjunction with negated atoms — the emptiness question.</summary>
    /// <param name="question">The conjunction.</param>
    /// <returns>The satisfiability verdict; <see cref="DatatypeSatisfiability.Unknown"/> when the definition cannot decide it within budget.</returns>
    public abstract DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question);

    /// <summary>Decides the three-valued membership of one value in this datatype's value space — the degenerate and validation question.</summary>
    /// <param name="value">The candidate value.</param>
    /// <returns>The membership verdict.</returns>
    public abstract DatatypeMembership Contains(Literal value);

    /// <summary>Decides the three-valued value identity of two literals within this datatype — the enumeration question.</summary>
    /// <param name="first">The first literal.</param>
    /// <param name="second">The second literal.</param>
    /// <returns>The identity verdict.</returns>
    public abstract DatatypeValueIdentity SameValue(Literal first, Literal second);

    /// <summary>Bounds the number of distinct values a conjunction admits — the counting question.</summary>
    /// <param name="question">The conjunction.</param>
    /// <returns>The distinct-value bound; <see cref="DatatypeCountBound.Unknown"/> when the definition cannot size it within budget.</returns>
    public abstract DatatypeCountBound DistinctValues(in DatatypeConjunction question);

    /// <summary>
    /// Runs the structural admissibility check at registration — closure under negation and decidable
    /// finite conjunctions constructively exhibited within budget. The default is admissible; the
    /// automaton-backed definitions override it to determinize within the state ceiling.
    /// </summary>
    /// <param name="budgets">The automaton state ceilings the check runs under.</param>
    /// <returns>The admissibility result.</returns>
    internal virtual AdmissibilityResult CheckAdmissibility(AutomatonBudgets budgets)
    {
        return AdmissibilityResult.Accepted;
    }

    /// <summary>
    /// Runs the registration-time self-test — a bounded differential of the compiled operations against
    /// a naive oracle plus cross-operation consistency checks. The default passes (a non-automaton or
    /// delegate-backed definition has no compiled automaton to compare); the automaton-backed
    /// definitions override it.
    /// </summary>
    /// <param name="budgets">The automaton state ceilings the self-test runs under.</param>
    /// <returns><see langword="true"/> when the definition's operations agree with the oracle.</returns>
    internal virtual bool RunSelfTest(AutomatonBudgets budgets)
    {
        return true;
    }

    /// <summary>
    /// Exposes this definition's language as a nondeterministic automaton when it has one — the raw
    /// pattern language, before any conjunction facet or negation — so a combinator can compose it. The
    /// default has none.
    /// </summary>
    /// <param name="budgets">The automaton state ceilings any compilation runs under.</param>
    /// <param name="automaton">The language automaton, when the definition is automaton-backed.</param>
    /// <returns><see langword="true"/> when the definition exposes an automaton.</returns>
    internal virtual bool TryGetLanguageAutomaton(AutomatonBudgets budgets, out NondeterministicAutomaton? automaton)
    {
        automaton = null;

        return false;
    }
}

/// <summary>The value-based result of a registration-time admissibility check.</summary>
/// <param name="Admissible">Whether the definition is admissible.</param>
/// <param name="Breach">The budget breach that made it inadmissible, when a budget drove the rejection.</param>
internal readonly record struct AdmissibilityResult(bool Admissible, AutomatonBudgetBreach? Breach)
{
    /// <summary>An admissible result.</summary>
    public static AdmissibilityResult Accepted { get; } = new(true, null);

    /// <summary>An inadmissible result with no budget breach — a structural rejection such as the white-space gate.</summary>
    /// <returns>The result.</returns>
    public static AdmissibilityResult Rejected()
    {
        return new AdmissibilityResult(false, null);
    }

    /// <summary>An inadmissible result driven by a budget breach.</summary>
    /// <param name="breach">The budget breach.</param>
    /// <returns>The result.</returns>
    public static AdmissibilityResult RejectedByBudget(AutomatonBudgetBreach breach)
    {
        return new AdmissibilityResult(false, breach);
    }
}
