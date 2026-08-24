using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype whose value space is the language of one or more XSD-dialect patterns over a
/// white-space-preserving string base, compiled to table automata at construction. Multiple patterns are
/// unioned, as XSD pattern facets on one derivation step are. <c>Contains</c> walks the value's runes
/// through the automaton; <c>DecideConjunction</c> intersects the base with the conjunction's facet
/// automata and negated-atom complements and reads off emptiness; <c>DistinctValues</c> determinizes that
/// product and counts distinct accepting paths; <c>SameValue</c> is lexical codepoint identity — sound only
/// over the preserve base the admissibility gate enforces.
/// </summary>
public class PatternDatatype : RegisteredDatatype
{
    /// <summary>The most probe strings the registration self-test enumerates per length.</summary>
    private const int SelfTestMaxLength = 3;

    /// <summary>The most alphabet symbols the registration self-test draws from the pattern literals.</summary>
    private const int SelfTestMaxAlphabet = 6;

    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The white-space base the patterns restrict; a lexical-identity <c>SameValue</c> is sound only over a preserve base.</summary>
    private Utf8String BaseIri { get; }

    /// <summary>The automaton budgets the query-time operations run under.</summary>
    private AutomatonBudgets Budgets { get; }

    /// <summary>The compiled base language automaton (the union of the pattern automata), or <see langword="null"/> when a pattern failed to compile.</summary>
    private NondeterministicAutomaton? BaseAutomaton { get; }

    /// <summary>Whether a pattern failed to compile because it crossed the NFA state ceiling rather than a parse error.</summary>
    private bool CompileBudgetExceeded { get; }

    /// <summary>The complement automaton cached at registration, exposed for a negation consumer; <see langword="null"/> until admissibility runs.</summary>
    internal NondeterministicAutomaton? CachedComplement { get; private set; }

    /// <summary>Creates a pattern datatype over a single pattern and a white-space-preserving base, defaulting the base to <c>xsd:string</c>.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="pattern">The XSD-dialect pattern.</param>
    public PatternDatatype(Utf8String datatypeIri, Utf8String pattern)
        : this(datatypeIri, [pattern], Vocabulary.Xsd.String, null)
    {
    }

    /// <summary>Creates a pattern datatype over one or more patterns and an explicit base.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="patterns">The XSD-dialect patterns, unioned.</param>
    /// <param name="baseIri">The white-space base datatype IRI.</param>
    /// <param name="budgets">The automaton budgets, or <see langword="null"/> for the shared defaults.</param>
    public PatternDatatype(Utf8String datatypeIri, IReadOnlyList<Utf8String> patterns, Utf8String baseIri, AutomatonBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        Iri = datatypeIri;
        BaseIri = baseIri;
        Budgets = budgets ?? AutomatonBudgets.Default;

        List<NondeterministicAutomaton> compiled = [];
        bool budgetExceeded = false;
        bool anyFailure = patterns.Count == 0;
        foreach(Utf8String pattern in patterns)
        {
            PatternCompileResult result = XsdPatternCompiler.Compile(pattern.Span, Budgets);
            switch(result.Status)
            {
                case PatternCompileStatus.Compiled:
                {
                    compiled.Add(result.Automaton!);

                    break;
                }

                case PatternCompileStatus.BudgetExceeded:
                {
                    budgetExceeded = true;
                    anyFailure = true;

                    break;
                }

                default:
                {
                    anyFailure = true;

                    break;
                }
            }
        }

        CompileBudgetExceeded = budgetExceeded;
        BaseAutomaton = anyFailure ? null : compiled.Count == 1 ? compiled[0] : AutomatonComposition.Union(compiled);
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        return BaseAutomaton is null ? DatatypeSatisfiability.Unknown : DatatypeAutomata.DecideEmptiness(BaseAutomaton, question, Budgets);
    }

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return BaseAutomaton is null
            ? DatatypeMembership.Indeterminate
            : BaseAutomaton.Accepts(DatatypeLexical.CodePoints(value.Value)) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return first.Value.Equals(second.Value) ? DatatypeValueIdentity.Same : DatatypeValueIdentity.Distinct;
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        return BaseAutomaton is null ? DatatypeCountBound.Unknown : DatatypeAutomata.CountDistinct(BaseAutomaton, question, Budgets);
    }

    /// <inheritdoc/>
    internal override bool TryGetLanguageAutomaton(AutomatonBudgets budgets, out NondeterministicAutomaton? automaton)
    {
        automaton = BaseAutomaton;

        return BaseAutomaton is not null;
    }

    /// <inheritdoc/>
    internal override AdmissibilityResult CheckAdmissibility(AutomatonBudgets budgets)
    {
        if(BaseAutomaton is null)
        {
            return CompileBudgetExceeded
                ? AdmissibilityResult.RejectedByBudget(new AutomatonBudgetBreach(AutomatonBudgetKind.MaxNfaStates, budgets.MaxNfaStates, budgets.MaxNfaStates + 1))
                : AdmissibilityResult.Rejected();
        }

        if(!DatatypeLexical.IsPreserveWhiteSpace(BaseIri))
        {
            //A lexical-identity SameValue is unsound over a collapse or replace base.
            return AdmissibilityResult.Rejected();
        }

        DeterminizeResult determinized = SubsetConstruction.Determinize(BaseAutomaton, budgets.MaxDfaStates);
        if(determinized.Outcome != DeterminizeOutcome.Done)
        {
            return AdmissibilityResult.RejectedByBudget(new AutomatonBudgetBreach(AutomatonBudgetKind.MaxDfaStates, budgets.MaxDfaStates, budgets.MaxDfaStates + 1));
        }

        CachedComplement = AutomatonComposition.FromDeterministic(determinized.Automaton!.Complement());

        return AdmissibilityResult.Accepted;
    }

    /// <inheritdoc/>
    internal override bool RunSelfTest(AutomatonBudgets budgets)
    {
        if(BaseAutomaton is null)
        {
            return true;
        }

        int[] alphabet = SelfTestAlphabet();
        long witnessedDistinct = 0;
        bool anyIn = false;
        List<int> digits = [];
        for(int length = 0; length <= SelfTestMaxLength; length++)
        {
            long combinations = Power(alphabet.Length, length);
            for(long index = 0; index < combinations; index++)
            {
                int[] codePoints = Decode(index, alphabet, length, digits);
                bool accepted = BaseAutomaton.Accepts(codePoints);
                Literal probe = new(DatatypeLexical.Utf8FromCodePoints(codePoints), new NamedNode(BaseIri));
                DatatypeMembership expected = accepted ? DatatypeMembership.In : DatatypeMembership.Out;
                if(Contains(probe) != expected)
                {
                    //The compiled operation disagrees with the naive oracle.
                    return false;
                }

                if(accepted)
                {
                    anyIn = true;
                    witnessedDistinct++;
                    if(SameValue(probe, probe) != DatatypeValueIdentity.Same)
                    {
                        return false;
                    }
                }
            }
        }

        if(anyIn && DecideConjunction(DatatypeConjunction.Empty) == DatatypeSatisfiability.Unsatisfiable)
        {
            //A non-empty language must not be reported empty.
            return false;
        }

        DatatypeCountBound distinct = DistinctValues(DatatypeConjunction.Empty);

        return distinct.Kind != DatatypeCountKind.Finite || distinct.Value >= witnessedDistinct;
    }

    /// <summary>The probe alphabet of the self-test: the distinct literal code points of the base automaton plus one symbol outside them.</summary>
    /// <returns>The alphabet code points.</returns>
    private int[] SelfTestAlphabet()
    {
        List<int> symbols = [];
        for(int state = 0; state < BaseAutomaton!.StateCount && symbols.Count < SelfTestMaxAlphabet; state++)
        {
            (System.ReadOnlySpan<CodePointRange> labels, _) = BaseAutomaton.SymbolTransitions(state);
            foreach(CodePointRange label in labels)
            {
                if(!symbols.Contains(label.Low))
                {
                    symbols.Add(label.Low);
                    if(symbols.Count >= SelfTestMaxAlphabet)
                    {
                        break;
                    }
                }
            }
        }

        foreach(int candidate in SentinelCandidates)
        {
            if(!symbols.Contains(candidate))
            {
                symbols.Add(candidate);

                break;
            }
        }

        return [.. symbols];
    }

    /// <summary>The candidate sentinel code points the self-test draws its outside symbol from.</summary>
    private static int[] SentinelCandidates { get; } = ['z', 'q', 'x', '0', '9', '!', '~'];

    /// <summary>Decodes an odometer index into a code-point array over an alphabet.</summary>
    /// <param name="index">The odometer index.</param>
    /// <param name="alphabet">The alphabet code points.</param>
    /// <param name="length">The string length.</param>
    /// <param name="digits">A reusable digit scratch.</param>
    /// <returns>The code points.</returns>
    private static int[] Decode(long index, int[] alphabet, int length, List<int> digits)
    {
        digits.Clear();
        long remaining = index;
        for(int position = 0; position < length; position++)
        {
            digits.Add(alphabet[(int)(remaining % alphabet.Length)]);
            remaining /= alphabet.Length;
        }

        return [.. digits];
    }

    /// <summary>An integer power for the bounded odometer, saturating well within the self-test bound.</summary>
    /// <param name="baseValue">The base.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>The power.</returns>
    private static long Power(int baseValue, int exponent)
    {
        long result = 1;
        for(int i = 0; i < exponent; i++)
        {
            result *= baseValue;
        }

        return result;
    }
}
