using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Owl.Datatypes.Automata;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The stage-A differential oracle for the pattern automaton module. A naive oracle
/// enumerates every string over a small representative alphabet up to a bounded length and decides
/// membership by direct NFA simulation, the simplest and most trustworthy code path. The module's
/// emptiness, intersection-emptiness, and complement decisions — the reachability, lazy-product, and
/// subset-plus-complement machinery — are then compared against that exhaustive ground truth for
/// every single pattern, every pairwise product, and every complement.
/// </summary>
/// <remarks>
/// <para>
/// The oracle parameters: the alphabet is the literal code points of the patterns under test plus one
/// symbol outside every class ('a', 'b', and a representative 'c' for the remaining universe); the
/// maximum length equals the product-state budget, so any within-budget non-empty product or
/// complement has a witness of length below the budget by reachability and therefore appears in the
/// enumeration. The patterns are drawn from the class partition {a}, {b}, other, for which the three
/// representatives are complete, so the emptiness comparisons are exact. A pair whose product or
/// complement exceeds the budget returns a budget-exceeded abstention and is skipped: it lies outside
/// the differential's certifying reach.
/// </para>
/// </remarks>
[TestClass]
internal sealed class XsdPatternDifferentialTests
{
    /// <summary>The enumeration length bound, equal to the product-state budget so the witness guarantee holds.</summary>
    private const int MaxLength = 8;

    /// <summary>The state budget every module operation runs under in this differential.</summary>
    private const int Budget = 8;

    /// <summary>The patterns under test, drawn from the {a}, {b}, other class partition.</summary>
    private static string[] PatternTexts =>
    [
        "a", "b", "ab", "ba", "a|b", "a+", "b+", "a*", "a*b", "(a|b)+",
        "[ab]", "[^a]", "a?b", "a{1,2}", "(ab)*", "ab*"
    ];

    /// <summary>The representative alphabet: the two literals plus one symbol for the rest of the universe.</summary>
    private static int[] Alphabet => ['a', 'b', 'c'];

    /// <summary>The budgets: a generous NFA-compile ceiling, with the product and subset ceilings pinned to the witness bound.</summary>
    private static AutomatonBudgets DiffBudgets => new(4096, Budget, Budget);

    /// <summary>Single-pattern emptiness agrees with exhaustive membership.</summary>
    [TestMethod]
    public void SingleEmptinessMatchesOracle()
    {
        (NondeterministicAutomaton[] automata, List<int[]> strings, bool[][] accepts) = BuildAcceptance();
        for(int i = 0; i < automata.Length; i++)
        {
            bool oracleEmpty = !AnyTrue(accepts[i]);
            Assert.AreEqual(oracleEmpty, automata[i].IsEmptyLanguage(), PatternTexts[i]);
        }
    }

    /// <summary>Pairwise intersection emptiness agrees with exhaustive membership (budget breaches skipped).</summary>
    [TestMethod]
    public void ProductEmptinessMatchesOracle()
    {
        (NondeterministicAutomaton[] automata, List<int[]> strings, bool[][] accepts) = BuildAcceptance();
        for(int i = 0; i < automata.Length; i++)
        {
            for(int j = 0; j < automata.Length; j++)
            {
                ProductEmptiness verdict = AutomatonProduct.IsIntersectionEmpty(automata[i], automata[j], Budget);
                if(verdict == ProductEmptiness.BudgetExceeded)
                {
                    continue;
                }

                bool oracleNonEmpty = AnyBoth(accepts[i], accepts[j]);
                Assert.AreEqual(oracleNonEmpty, verdict == ProductEmptiness.NonEmpty, $"{PatternTexts[i]} & {PatternTexts[j]}");
            }
        }
    }

    /// <summary>Each complement's membership and emptiness agree with the negation of the original (budget breaches skipped).</summary>
    [TestMethod]
    public void ComplementMatchesOracle()
    {
        (NondeterministicAutomaton[] automata, List<int[]> strings, bool[][] accepts) = BuildAcceptance();
        for(int i = 0; i < automata.Length; i++)
        {
            DeterminizeResult determinized = SubsetConstruction.Determinize(automata[i], Budget);
            if(determinized.Outcome == DeterminizeOutcome.BudgetExceeded)
            {
                continue;
            }

            DeterministicAutomaton complement = determinized.Automaton!.Complement();
            bool anyExcluded = false;
            for(int s = 0; s < strings.Count; s++)
            {
                bool complementAccepts = complement.Accepts(strings[s]);
                Assert.AreEqual(!accepts[i][s], complementAccepts, $"~{PatternTexts[i]} @ {s}");
                anyExcluded |= !accepts[i][s];
            }

            if(anyExcluded)
            {
                Assert.IsFalse(complement.IsEmptyLanguage(), $"~{PatternTexts[i]}");
            }
        }
    }

    /// <summary>Compiles the pattern set, enumerates the alphabet strings, and precomputes the acceptance matrix.</summary>
    /// <returns>The compiled automata, the enumerated strings, and the per-pattern per-string acceptance flags.</returns>
    private static (NondeterministicAutomaton[] Automata, List<int[]> Strings, bool[][] Accepts) BuildAcceptance()
    {
        string[] texts = PatternTexts;
        NondeterministicAutomaton[] automata = new NondeterministicAutomaton[texts.Length];
        for(int i = 0; i < texts.Length; i++)
        {
            PatternCompileResult compiled = XsdPatternCompiler.Compile(Encoding.UTF8.GetBytes(texts[i]), DiffBudgets);
            Assert.AreEqual(PatternCompileStatus.Compiled, compiled.Status, texts[i]);
            automata[i] = compiled.Automaton!;
        }

        List<int[]> strings = EnumerateStrings();
        bool[][] accepts = new bool[texts.Length][];
        for(int i = 0; i < texts.Length; i++)
        {
            bool[] row = new bool[strings.Count];
            for(int s = 0; s < strings.Count; s++)
            {
                row[s] = automata[i].Accepts(strings[s]);
            }

            accepts[i] = row;
        }

        return (automata, strings, accepts);
    }

    /// <summary>Enumerates every alphabet string of length zero through <see cref="MaxLength"/>.</summary>
    /// <returns>The enumerated strings as code-point arrays.</returns>
    private static List<int[]> EnumerateStrings()
    {
        int[] alphabet = Alphabet;
        List<int[]> all = [[]];
        List<int[]> previous = [[]];
        for(int length = 1; length <= MaxLength; length++)
        {
            List<int[]> current = [];
            foreach(int[] prefix in previous)
            {
                foreach(int symbol in alphabet)
                {
                    int[] extended = new int[prefix.Length + 1];
                    prefix.CopyTo(extended, 0);
                    extended[prefix.Length] = symbol;
                    current.Add(extended);
                }
            }

            all.AddRange(current);
            previous = current;
        }

        return all;
    }

    /// <summary>Whether any flag in a row is set.</summary>
    /// <param name="row">The acceptance row.</param>
    /// <returns><see langword="true"/> when any string is accepted.</returns>
    private static bool AnyTrue(bool[] row)
    {
        foreach(bool value in row)
        {
            if(value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any string is accepted by both rows.</summary>
    /// <param name="first">The first acceptance row.</param>
    /// <param name="second">The second acceptance row.</param>
    /// <returns><see langword="true"/> when some string is in both languages.</returns>
    private static bool AnyBoth(bool[] first, bool[] second)
    {
        for(int s = 0; s < first.Length; s++)
        {
            if(first[s] && second[s])
            {
                return true;
            }
        }

        return false;
    }
}
