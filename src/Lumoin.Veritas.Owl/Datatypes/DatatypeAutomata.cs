using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>Whether a conjunction language was built or crossed an automaton budget.</summary>
internal enum ConjunctionStatus
{
    /// <summary>The conjunction language automaton was built within budget.</summary>
    Built,

    /// <summary>An automaton budget was crossed; the caller abstains.</summary>
    BudgetExceeded,
}

/// <summary>
/// The language automaton of a facet-and-negation conjunction over a base automaton, together with
/// whether some conjunct could not be modelled and so blocks a positive satisfiability witness (the
/// modelled product is still a sound superset of the true language, so an empty product proves emptiness).
/// </summary>
/// <param name="Status">Whether the language was built or a budget was crossed.</param>
/// <param name="Language">The built language automaton, on success.</param>
/// <param name="Unmodelled">Whether an unmodelled conjunct blocks a positive witness.</param>
internal readonly record struct ConjunctionLanguage(ConjunctionStatus Status, NondeterministicAutomaton? Language, bool Unmodelled)
{
    /// <summary>A built conjunction language.</summary>
    /// <param name="language">The language automaton.</param>
    /// <param name="unmodelled">Whether an unmodelled conjunct blocks a positive witness.</param>
    /// <returns>The result.</returns>
    public static ConjunctionLanguage Built(NondeterministicAutomaton language, bool unmodelled)
    {
        return new ConjunctionLanguage(ConjunctionStatus.Built, language, unmodelled);
    }

    /// <summary>A budget-exceeded result.</summary>
    /// <returns>The result.</returns>
    public static ConjunctionLanguage Breach()
    {
        return new ConjunctionLanguage(ConjunctionStatus.BudgetExceeded, null, false);
    }
}

/// <summary>Whether a negated atom's complement was modelled, could not be modelled, or crossed a budget.</summary>
internal enum NegatedStatus
{
    /// <summary>The complement automaton was built.</summary>
    Modelled,

    /// <summary>The negated atom could not be modelled as an automaton.</summary>
    Unmodelled,

    /// <summary>A budget was crossed complementing the negated atom.</summary>
    BudgetExceeded,
}

/// <summary>
/// The automaton machinery the automaton-backed datatype definitions share: folding a base automaton with
/// the positive facet automata and the negated-atom complements of a conjunction into one language
/// automaton, deciding its emptiness or counting its strings, and complementing an automaton within
/// budget. Every path is value-based and non-recursive; a budget breach is an abstention, never a wrong
/// answer.
/// </summary>
internal static class DatatypeAutomata
{
    /// <summary>
    /// Builds the language automaton of a conjunction over a base automaton: the product of the base with
    /// each modelled positive facet automaton and each modelled negated-atom complement. An unmodelled
    /// facet or negated atom sets the block-witness flag but the product is still built from the modelled
    /// conjuncts (a sound superset).
    /// </summary>
    /// <param name="baseAutomaton">The base language automaton.</param>
    /// <param name="conjunction">The conjunction.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <returns>The conjunction language result.</returns>
    public static ConjunctionLanguage BuildConjunctionLanguage(NondeterministicAutomaton baseAutomaton, in DatatypeConjunction conjunction, AutomatonBudgets budgets)
    {
        List<NondeterministicAutomaton> factors = [baseAutomaton];
        bool unmodelled = false;

        foreach(OwlFacetRestriction facet in conjunction.PositiveFacets)
        {
            if(TryFacetFactor(facet, budgets, out NondeterministicAutomaton? factor))
            {
                factors.Add(factor!);
            }
            else
            {
                unmodelled = true;
            }
        }

        foreach(OwlDataRange atom in conjunction.NegatedAtoms)
        {
            NegatedStatus status = TryNegatedComplement(atom, budgets, out NondeterministicAutomaton? complement);
            switch(status)
            {
                case NegatedStatus.BudgetExceeded:
                {
                    return ConjunctionLanguage.Breach();
                }

                case NegatedStatus.Unmodelled:
                {
                    unmodelled = true;

                    break;
                }

                default:
                {
                    factors.Add(complement!);

                    break;
                }
            }
        }

        NondeterministicAutomaton language = factors[0];
        for(int i = 1; i < factors.Count; i++)
        {
            ProductResult product = AutomatonComposition.Product(language, factors[i], budgets.MaxProductStates);
            if(product.Status != ProductStatus.Built)
            {
                return ConjunctionLanguage.Breach();
            }

            language = product.Automaton!;
        }

        return ConjunctionLanguage.Built(language, unmodelled);
    }

    /// <summary>Interprets a conjunction language as an emptiness verdict.</summary>
    /// <param name="baseAutomaton">The base language automaton.</param>
    /// <param name="conjunction">The conjunction.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <returns>The satisfiability verdict.</returns>
    public static DatatypeSatisfiability DecideEmptiness(NondeterministicAutomaton baseAutomaton, in DatatypeConjunction conjunction, AutomatonBudgets budgets)
    {
        ConjunctionLanguage built = BuildConjunctionLanguage(baseAutomaton, conjunction, budgets);
        if(built.Status != ConjunctionStatus.Built)
        {
            return DatatypeSatisfiability.Unknown;
        }

        if(built.Language!.IsEmptyLanguage())
        {
            return DatatypeSatisfiability.Unsatisfiable;
        }

        return built.Unmodelled ? DatatypeSatisfiability.Unknown : DatatypeSatisfiability.Satisfiable;
    }

    /// <summary>Counts the distinct strings a conjunction language admits.</summary>
    /// <param name="baseAutomaton">The base language automaton.</param>
    /// <param name="conjunction">The conjunction.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <returns>The distinct-value bound.</returns>
    public static DatatypeCountBound CountDistinct(NondeterministicAutomaton baseAutomaton, in DatatypeConjunction conjunction, AutomatonBudgets budgets)
    {
        ConjunctionLanguage built = BuildConjunctionLanguage(baseAutomaton, conjunction, budgets);
        if(built.Status != ConjunctionStatus.Built || built.Unmodelled)
        {
            return DatatypeCountBound.Unknown;
        }

        DeterminizeResult determinized = SubsetConstruction.Determinize(built.Language!, budgets.MaxDfaStates);
        if(determinized.Outcome != DeterminizeOutcome.Done)
        {
            return DatatypeCountBound.Unknown;
        }

        AutomatonCount count = AutomatonCounting.CountDistinct(determinized.Automaton!);

        return count.Kind == AutomatonCountKind.Infinite ? DatatypeCountBound.Infinite : DatatypeCountBound.Of(count.Value);
    }

    /// <summary>Complements an automaton within budget by determinizing then flipping over the XML Char universe.</summary>
    /// <param name="automaton">The automaton to complement.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <param name="complement">The complement automaton, on success.</param>
    /// <returns>Whether the complement was built, unmodellable, or budget-exceeded.</returns>
    public static NegatedStatus TryComplement(NondeterministicAutomaton automaton, AutomatonBudgets budgets, out NondeterministicAutomaton? complement)
    {
        complement = null;
        DeterminizeResult determinized = SubsetConstruction.Determinize(automaton, budgets.MaxDfaStates);
        if(determinized.Outcome != DeterminizeOutcome.Done)
        {
            return NegatedStatus.BudgetExceeded;
        }

        complement = AutomatonComposition.FromDeterministic(determinized.Automaton!.Complement());

        return NegatedStatus.Modelled;
    }

    /// <summary>Builds the automaton factor of a positive facet — a pattern automaton or a length counter automaton.</summary>
    /// <param name="facet">The facet restriction.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <param name="factor">The factor automaton, when the facet is an automaton facet.</param>
    /// <returns><see langword="true"/> when the facet was modelled as an automaton.</returns>
    private static bool TryFacetFactor(OwlFacetRestriction facet, AutomatonBudgets budgets, out NondeterministicAutomaton? factor)
    {
        factor = null;
        Utf8String facetIri = facet.Facet.Iri;
        if(facetIri.Equals(Vocabulary.XsdFacets.Pattern))
        {
            PatternCompileResult compiled = XsdPatternCompiler.Compile(facet.Value.Value.Span, budgets);
            if(compiled.Status != PatternCompileStatus.Compiled)
            {
                return false;
            }

            factor = compiled.Automaton;

            return true;
        }

        bool isLength = facetIri.Equals(Vocabulary.XsdFacets.Length);
        bool isMinLength = facetIri.Equals(Vocabulary.XsdFacets.MinLength);
        bool isMaxLength = facetIri.Equals(Vocabulary.XsdFacets.MaxLength);
        if((isLength || isMinLength || isMaxLength) && int.TryParse(facet.Value.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bound) && bound >= 0)
        {
            factor = isLength ? LengthAutomaton.Exactly(bound) : isMinLength ? LengthAutomaton.AtLeast(bound) : LengthAutomaton.AtMost(bound);

            return true;
        }

        return false;
    }

    /// <summary>Builds the complement automaton of a negated atom — an enumeration's exact strings or a string restriction's facet language, flipped.</summary>
    /// <param name="atom">The negated data range.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <param name="complement">The complement automaton, when modelled.</param>
    /// <returns>Whether the negation was modelled, unmodellable, or budget-exceeded.</returns>
    private static NegatedStatus TryNegatedComplement(OwlDataRange atom, AutomatonBudgets budgets, out NondeterministicAutomaton? complement)
    {
        complement = null;
        switch(atom)
        {
            case OwlDataOneOf oneOf:
            {
                if(oneOf.Literals.Count == 0)
                {
                    //A negated empty enumeration removes nothing; report unmodelled so it is skipped without a factor.
                    return NegatedStatus.Unmodelled;
                }

                List<NondeterministicAutomaton> strings = [];
                foreach(Literal literal in oneOf.Literals)
                {
                    strings.Add(AutomatonComposition.ExactString(DatatypeLexical.CodePoints(literal.Value)));
                }

                return TryComplement(AutomatonComposition.Union(strings), budgets, out complement);
            }

            case OwlDatatypeRestriction restriction when IsStringBase(restriction.Datatype.Iri):
            {
                NondeterministicAutomaton language = LengthAutomaton.AtLeast(0);
                foreach(OwlFacetRestriction facet in restriction.Restrictions)
                {
                    if(!TryFacetFactor(facet, budgets, out NondeterministicAutomaton? factor))
                    {
                        return NegatedStatus.Unmodelled;
                    }

                    ProductResult product = AutomatonComposition.Product(language, factor!, budgets.MaxProductStates);
                    if(product.Status != ProductStatus.Built)
                    {
                        return NegatedStatus.BudgetExceeded;
                    }

                    language = product.Automaton!;
                }

                return TryComplement(language, budgets, out complement);
            }

            default:
            {
                return NegatedStatus.Unmodelled;
            }
        }
    }

    /// <summary>Whether a base IRI is a string-family datatype the automaton route models.</summary>
    /// <param name="baseIri">The base datatype IRI.</param>
    /// <returns><see langword="true"/> when the base is a text-family datatype.</returns>
    private static bool IsStringBase(Utf8String baseIri)
    {
        return OwlDatatypeFamilies.Classify(baseIri) == OwlDatatypeFamily.Text;
    }
}
