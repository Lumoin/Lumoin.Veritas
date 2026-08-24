using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Owl.Contexts;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The five order-condition tests for <see cref="ContextTermOrder"/> from the
/// consequence-based SRIQ calculus
/// (KR 2016, Definition 3; <see href="https://arxiv.org/abs/1602.04498"/>). Each
/// condition is a named test that quantifies over a seeded, reproducible term
/// population (built through <see cref="RandomSources.FromSeed"/>, the sanctioned
/// randomness seam -- a raw <c>System.Random</c> is banned) together with
/// hand-built adversarial pairs, and reports every offending pair rather than
/// failing on the first. Condition 4 pins that a predecessor-trigger atom sits
/// above its own <c>x</c>/<c>y</c> subterms; condition 5 pins that a
/// predecessor-trigger atom is never <c>></c>-greater than any term outside
/// <c>{x, y}</c> -- its peers in <c>Pr(O)</c> included, which are mutually
/// incomparable (the completeness-critical relaxation the saturation calculus
/// leans on).
/// </summary>
[TestClass]
internal sealed class ContextTermOrderTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The number of Skolem function symbols the population spans.</summary>
    private const int FunctionSymbolCount = 6;

    /// <summary>The number of neighbour variables the population spans.</summary>
    private const int NeighbourCount = 4;

    /// <summary>The concept-atom symbol ids the population draws from.</summary>
    private static int[] ConceptSymbols { get; } = [2, 3, 4, 5];

    /// <summary>The directioned role symbol ids the population draws from (forward, even ids).</summary>
    private static int[] RoleSymbols { get; } = [0, 2, 4];

    /// <summary>The central variable <c>x</c> as an order term.</summary>
    private static ContextOrderTerm X { get; } = ContextOrderTerm.OfTerm(DlTerm.Central);

    /// <summary>The context variable <c>y</c> as an order term.</summary>
    private static ContextOrderTerm Y { get; } = ContextOrderTerm.OfTerm(DlTerm.Context);

    /// <summary>
    /// Condition 1: <c>f(x) > x > y</c> for every function symbol. Every function
    /// term dominates the central variable, which dominates the context variable.
    /// </summary>
    [TestMethod]
    public void Condition1FunctionOverCentralOverContext()
    {
        ContextTermOrder order = ContextTermOrder.ForModule([]);
        List<string> offenders = [];

        if(!order.Greater(X, Y, ContextGrammarKind.Ordinary))
        {
            offenders.Add("x is not greater than y");
        }

        for(int symbol = 0; symbol < FunctionSymbolCount; symbol++)
        {
            ContextOrderTerm functionTerm = Function(symbol);
            if(!order.Greater(functionTerm, X, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"f{symbol}(x) is not greater than x");
            }

            if(!order.Greater(functionTerm, Y, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"f{symbol}(x) is not greater than y");
            }
        }

        Assert.IsEmpty(offenders, Report("condition 1 (f(x) > x > y)", offenders));
    }

    /// <summary>
    /// Condition 2: <c>f >> g => f(x) > g(x)</c>. The function-symbol precedence is
    /// the dense mint order, so a higher-id function term dominates a lower-id one
    /// and the comparison is strict and antisymmetric.
    /// </summary>
    [TestMethod]
    public void Condition2FunctionSymbolPrecedence()
    {
        ContextTermOrder order = ContextTermOrder.ForModule([]);
        List<string> offenders = [];

        for(int higher = 0; higher < FunctionSymbolCount; higher++)
        {
            for(int lower = 0; lower < higher; lower++)
            {
                ContextOrderTerm high = Function(higher);
                ContextOrderTerm low = Function(lower);
                if(!order.Greater(high, low, ContextGrammarKind.Ordinary))
                {
                    offenders.Add($"f{higher}(x) is not greater than f{lower}(x)");
                }

                if(order.Greater(low, high, ContextGrammarKind.Ordinary))
                {
                    offenders.Add($"f{lower}(x) is wrongly greater than f{higher}(x)");
                }
            }
        }

        Assert.IsEmpty(offenders, Report("condition 2 (f > g => f(x) > g(x))", offenders));
    }

    /// <summary>
    /// Condition 3: <c>s1 > s2 => t[s1]p > t[s2]p</c>. Replacing an argument of an
    /// atom by a strictly greater term makes the whole atom strictly greater -- the
    /// order is monotone under the shallow context the grammar admits (a concept
    /// or role atom over F-terms). The seeded population supplies the ordered
    /// argument pairs and the enclosing atom contexts.
    /// </summary>
    [TestMethod]
    public void Condition3MonotoneUnderContext()
    {
        ContextTermOrder order = ContextTermOrder.ForModule([]);
        RandomSourceDelegate random = RandomSources.FromSeed(52341);
        List<(DlTerm Greater, DlTerm Lesser)> orderedPairs = OrderedFTermPairs();
        List<string> offenders = [];

        foreach((DlTerm greater, DlTerm lesser) in orderedPairs)
        {
            //Sanity: the argument pair is itself strictly ordered before it is embedded.
            if(!order.Greater(ContextOrderTerm.OfTerm(greater), ContextOrderTerm.OfTerm(lesser), ContextGrammarKind.Ordinary))
            {
                offenders.Add($"argument pair not ordered: {Render(greater)} vs {Render(lesser)}");

                continue;
            }

            int conceptSymbol = ConceptSymbols[(int)(random() % (ulong)ConceptSymbols.Length)];
            CheckMonotone(order, DlLiteral.Concept(conceptSymbol, greater), DlLiteral.Concept(conceptSymbol, lesser), offenders);

            int roleSymbol = RoleSymbols[(int)(random() % (ulong)RoleSymbols.Length)];
            DlTerm other = DlTerm.Neighbour(1);
            CheckMonotone(order, DlLiteral.Role(roleSymbol, greater, other), DlLiteral.Role(roleSymbol, lesser, other), offenders);
            CheckMonotone(order, DlLiteral.Role(roleSymbol, other, greater), DlLiteral.Role(roleSymbol, other, lesser), offenders);
        }

        Assert.IsEmpty(offenders, Report("condition 3 (monotone under context)", offenders));
    }

    /// <summary>
    /// Condition 4: <c>s > s|p</c> -- the subterm property. Every atom dominates
    /// each of its F-term arguments, and every function term dominates <c>x</c>.
    /// The predecessor-trigger atoms are included on purpose: a <c>Pr(O)</c> atom
    /// sits ABOVE its <c>x</c> and <c>y</c> subterms (it is the completeness
    /// relaxation, not a demotion below the variables), so <c>S(x, y) > x > y</c>.
    /// </summary>
    [TestMethod]
    public void Condition4SubtermProperty()
    {
        ContextTermOrder plain = ContextTermOrder.ForModule([]);
        List<string> offenders = [];

        foreach(DlLiteral atom in BuildPopulation(RandomSources.FromSeed(88817)).Atoms)
        {
            ContextOrderTerm atomTerm = ContextOrderTerm.OfAtom(atom);
            foreach(DlTerm subterm in Subterms(atom))
            {
                if(!plain.Greater(atomTerm, ContextOrderTerm.OfTerm(subterm), ContextGrammarKind.Ordinary))
                {
                    offenders.Add($"atom {Render(atom)} not greater than its subterm {Render(subterm)}");
                }
            }
        }

        for(int symbol = 0; symbol < FunctionSymbolCount; symbol++)
        {
            if(!plain.Greater(Function(symbol), X, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"f{symbol}(x) not greater than its subterm x");
            }
        }

        //The Pr-atoms sit above their x/y subterms -- the load-bearing part of condition 4.
        ContextTermOrder pr = PredecessorOrder();
        foreach(DlLiteral prAtom in PredecessorAtoms())
        {
            ContextOrderTerm prTerm = ContextOrderTerm.OfAtom(prAtom);
            if(!pr.Greater(prTerm, X, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"Pr atom {Render(prAtom)} not greater than x");
            }

            if(!pr.Greater(prTerm, Y, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"Pr atom {Render(prAtom)} not greater than y");
            }
        }

        Assert.IsEmpty(offenders, Report("condition 4 (subterm property, incl. Pr over x/y)", offenders));
    }

    /// <summary>
    /// Condition 5: for every atom <c>A  in  Pr(O)</c> and every context term
    /// <c>s  not in  {x, y}</c>, <c>A not> s</c>. A predecessor-trigger atom is
    /// <c>></c>-minimal among the non-variable terms; the quantifier ranges over
    /// its <c>Pr(O)</c> peers too, so two distinct predecessor atoms are mutually
    /// incomparable -- <c>Compare</c> returns zero both ways, never an LPO tie
    /// break (KR 2016 Definition 3 requires no totality). This is the relaxation a plain
    /// LPO would break (mutation M7).
    /// </summary>
    [TestMethod]
    public void Condition5PredecessorTriggerMinimal()
    {
        ContextTermOrder pr = PredecessorOrder();
        List<DlLiteral> predecessorAtoms = PredecessorAtoms();
        List<ContextOrderTerm> population = [];
        Population population2 = BuildPopulation(RandomSources.FromSeed(20260705));
        foreach(DlTerm term in population2.Terms)
        {
            population.Add(ContextOrderTerm.OfTerm(term));
        }

        foreach(DlLiteral atom in population2.Atoms)
        {
            population.Add(ContextOrderTerm.OfAtom(atom));
        }

        foreach(DlLiteral prAtom in predecessorAtoms)
        {
            population.Add(ContextOrderTerm.OfAtom(prAtom));
        }

        List<string> offenders = [];
        foreach(DlLiteral prAtom in predecessorAtoms)
        {
            ContextOrderTerm prTerm = ContextOrderTerm.OfAtom(prAtom);
            foreach(ContextOrderTerm s in population)
            {
                if(IsVariable(s))
                {
                    continue;
                }

                if(pr.Greater(prTerm, s, ContextGrammarKind.Ordinary))
                {
                    offenders.Add($"Pr atom {Render(prAtom)} is wrongly greater than {Render(s)}");
                }
            }
        }

        //Mutual incomparability of distinct Pr atoms: Compare returns zero both ways.
        for(int i = 0; i < predecessorAtoms.Count; i++)
        {
            for(int j = i + 1; j < predecessorAtoms.Count; j++)
            {
                ContextOrderTerm a = ContextOrderTerm.OfAtom(predecessorAtoms[i]);
                ContextOrderTerm b = ContextOrderTerm.OfAtom(predecessorAtoms[j]);
                if(pr.Compare(a, b, ContextGrammarKind.Ordinary) != 0 || pr.Compare(b, a, ContextGrammarKind.Ordinary) != 0)
                {
                    offenders.Add($"Pr atoms {Render(predecessorAtoms[i])} and {Render(predecessorAtoms[j])} are not mutually incomparable");
                }
            }
        }

        Assert.IsEmpty(offenders, Report("condition 5 (Pr-trigger minimality + incomparability)", offenders));
    }

    /// <summary>
    /// Condition 2 over the nominal vocabulary: named individuals order by their
    /// GLOBAL interned precedence, so <c>CompareFTerm(oᵢ, oⱼ)</c> carries the sign of
    /// <c>i</c> versus <c>j</c> (the label-monotone <c>⋗</c> mint order the
    /// appendix-A partial order lays over the individual tier). Sweeps every ordered
    /// pair of the first six individual ids and reports every disagreement.
    /// </summary>
    [TestMethod]
    public void Condition2IndividualsOrderByGlobalPrecedence()
    {
        List<string> offenders = [];
        for(int left = 0; left <= 5; left++)
        {
            for(int right = 0; right <= 5; right++)
            {
                int actual = SignOf(ContextTermOrder.CompareFTerm(DlTerm.Individual(left), DlTerm.Individual(right)));
                int expected = SignOf(left.CompareTo(right));
                if(actual != expected)
                {
                    offenders.Add($"CompareFTerm(o{left}, o{right}) sign {actual} does not match id order sign {expected}");
                }
            }
        }

        Assert.IsEmpty(offenders, Report("condition 2 (individuals by global precedence)", offenders));
    }

    /// <summary>
    /// Condition 7 label monotonicity through the mint order: the one in-saturation
    /// mint channel (<see cref="ContextSymbolTable.MintGeneratedNominal"/>) always
    /// interns a generated nominal AFTER its prefix, so a longer label outranks its
    /// prefix under <see cref="ContextTermOrder.CompareFTerm"/>; the channel is
    /// memoized per (prefix, role), so a re-fire returns the SAME sibling block and
    /// never grows the label set, and a deeper mint raises the observed label depth.
    /// </summary>
    [TestMethod]
    public void Condition7LabelMonotonicityByMintOrder()
    {
        ContextSymbolTable symbols = new();
        int prefix = symbols.InternIndividual(Utf8Strings.From("http://example.org/tier3order#o"), IndividualOrigin.IriDenoted);
        Assert.AreEqual(0, prefix, "The first interned individual takes id zero.");

        bool minted = symbols.MintGeneratedNominal(prefix, roleId: 0, count: 3, out int first);
        Assert.IsTrue(minted, "The first mint for a (prefix, role) pair mints a fresh sibling block.");
        Assert.AreEqual(1, first, "The first generated-nominal sibling interns immediately after the sole input individual.");
        Assert.AreEqual(3, symbols.GeneratedNominalCount, "The block minted the requested three siblings.");
        Assert.AreEqual(1, symbols.MaxNominalLabelDepth, "A sibling of an input individual carries label depth one.");
        Assert.IsGreaterThan(0, ContextTermOrder.CompareFTerm(DlTerm.Individual(first), DlTerm.Individual(prefix)), "A generated nominal outranks its prefix under the global order.");

        bool remint = symbols.MintGeneratedNominal(prefix, roleId: 0, count: 3, out int firstAgain);
        Assert.IsFalse(remint, "A re-fire for the same (prefix, role) pair returns the memoized block rather than minting anew.");
        Assert.AreEqual(first, firstAgain, "The memoized re-fire returns the SAME first sibling id, so the label set stays a fixed function of the module.");

        bool deeper = symbols.MintGeneratedNominal(first, roleId: 0, count: 2, out int deeperFirst);
        Assert.IsTrue(deeper, "Minting from the generated nominal as prefix mints a fresh deeper block.");
        Assert.AreEqual(2, symbols.MaxNominalLabelDepth, "A sibling of a depth-one nominal carries label depth two.");
        Assert.IsGreaterThan(0, ContextTermOrder.CompareFTerm(DlTerm.Individual(deeperFirst), DlTerm.Individual(first)), "The deeper nominal outranks the prefix it extends.");
    }

    /// <summary>
    /// Condition 6 exemption widened to the constant vocabulary: a predecessor-trigger
    /// atom is never <c>≻</c>-greater than a named-individual term nor a
    /// function-of-individual term (an ordinary context, so the band is the materialized
    /// <c>Pr(O)</c> set). The widened <c>{x, y, true} ∪ Σo</c> exemption leaves the band
    /// realization minimal against the constants too — the relaxation condition 6 permits
    /// so the nominal read-off stays complete.
    /// </summary>
    [TestMethod]
    public void Condition6ExemptionWidensToConstants()
    {
        ContextTermOrder pr = PredecessorOrder();
        ContextOrderTerm individual = ContextOrderTerm.OfTerm(DlTerm.Individual(0));
        ContextOrderTerm functionOfIndividual = ContextOrderTerm.OfTerm(DlTerm.FunctionOf(0, 0));
        List<string> offenders = [];

        foreach(DlLiteral prAtom in PredecessorAtoms())
        {
            ContextOrderTerm prTerm = ContextOrderTerm.OfAtom(prAtom);
            if(pr.Greater(prTerm, individual, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"Pr atom {Render(prAtom)} is wrongly greater than the individual o0");
            }

            if(pr.Greater(prTerm, functionOfIndividual, ContextGrammarKind.Ordinary))
            {
                offenders.Add($"Pr atom {Render(prAtom)} is wrongly greater than the function-of-individual f0(o0)");
            }
        }

        Assert.IsEmpty(offenders, Report("condition 6 (Pr exemption over constants)", offenders));
    }

    /// <summary>
    /// The variable-versus-individual incomparability of the appendix-A partial order:
    /// <c>x</c> and <c>y</c> are UNORDERED against a named individual in both directions
    /// (<see cref="ContextTermOrder.TryCompareFTerm"/> returns <see langword="false"/>,
    /// <see cref="ContextTermOrder.CompareFTerm"/> zero), while an individual is strictly
    /// below both <c>f(o)</c> and <c>f(x)</c> — the incomparable pairs are exactly the
    /// dropped ones, everything else stays totally ordered.
    /// </summary>
    [TestMethod]
    public void Incomparability1VariableVersusIndividual()
    {
        Assert.IsFalse(ContextTermOrder.TryCompareFTerm(DlTerm.Central, DlTerm.Individual(0), out _), "The central variable is incomparable to a named individual.");
        Assert.IsFalse(ContextTermOrder.TryCompareFTerm(DlTerm.Context, DlTerm.Individual(0), out _), "The context variable is incomparable to a named individual.");
        Assert.IsFalse(ContextTermOrder.TryCompareFTerm(DlTerm.Individual(0), DlTerm.Context, out _), "A named individual is incomparable to the context variable in the reverse direction too.");
        Assert.AreEqual(0, ContextTermOrder.CompareFTerm(DlTerm.Central, DlTerm.Individual(0)), "The incomparable central-versus-individual pair compares as zero.");
        Assert.AreEqual(0, ContextTermOrder.CompareFTerm(DlTerm.Context, DlTerm.Individual(0)), "The incomparable context-versus-individual pair compares as zero.");

        Assert.IsTrue(ContextTermOrder.TryCompareFTerm(DlTerm.Individual(0), DlTerm.FunctionOf(0, 0), out int belowFunctionOfIndividual), "An individual is comparable to a function-of-individual term.");
        Assert.IsLessThan(0, belowFunctionOfIndividual, "An individual sits strictly below a function-of-individual term.");
        Assert.IsTrue(ContextTermOrder.TryCompareFTerm(DlTerm.Individual(0), DlTerm.Function(0), out int belowFunction), "An individual is comparable to a function term.");
        Assert.IsLessThan(0, belowFunction, "An individual sits strictly below a function term f(x).");
    }

    /// <summary>
    /// The order-level face of the enumeration read-off: <see cref="ContextTermOrder.OrientEqualityLiteral"/>
    /// stores an incomparable variable-versus-individual equality in the canonical
    /// VARIABLE-first form regardless of the argument order it was built in (so the Eq
    /// rule reads the constant side as the sole rewrite source), and a comparable
    /// individual-versus-individual equality maximal-side-first (the greater interned id
    /// first). This is the storage canonicalisation the read-off depends on.
    /// </summary>
    [TestMethod]
    public void Incomparability2ReadOffDiesIfComparable()
    {
        DlLiteral keptVariableFirst = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(4)));
        Assert.AreEqual(DlTerm.Central, keptVariableFirst.First, "A variable-first incomparable equality keeps the variable first.");

        DlLiteral swappedToVariableFirst = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Individual(4), DlTerm.Central));
        Assert.AreEqual(DlTerm.Central, swappedToVariableFirst.First, "A constant-first incomparable equality swaps to variable-first.");

        DlLiteral greaterFirst = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Individual(2), DlTerm.Individual(7)));
        Assert.AreEqual(DlTerm.Individual(7), greaterFirst.First, "A comparable individual equality stores the greater interned id first.");
    }

    /// <summary>
    /// Condition 4 substitution monotonicity over the constant vocabulary: replacing a
    /// role atom's or concept atom's constant argument by a greater-id one makes the
    /// whole head literal strictly greater under <see cref="ContextTermOrder.CompareHeadLiterals"/>
    /// (an ordinary context) — the new individual and function-of-individual terms enter
    /// the shallow-context monotonicity the selection order leans on, just as the F-term
    /// arguments do.
    /// </summary>
    [TestMethod]
    public void Condition4MonotoneOverConstantArguments()
    {
        ContextTermOrder order = ContextTermOrder.ForModule([]);
        DlLiteral roleHigh = DlLiteral.Role(RoleSymbols[0], DlTerm.Central, DlTerm.Individual(5));
        DlLiteral roleLow = DlLiteral.Role(RoleSymbols[0], DlTerm.Central, DlTerm.Individual(2));
        Assert.IsGreaterThan(0, order.CompareHeadLiterals(roleHigh, roleLow, ContextGrammarKind.Ordinary), "The role atom with the greater-id constant argument orders strictly above.");
        Assert.IsLessThan(0, order.CompareHeadLiterals(roleLow, roleHigh, ContextGrammarKind.Ordinary), "The role comparison is antisymmetric over the constant argument.");

        DlLiteral conceptHigh = DlLiteral.Concept(ConceptSymbols[0], DlTerm.Individual(5));
        DlLiteral conceptLow = DlLiteral.Concept(ConceptSymbols[0], DlTerm.Individual(2));
        Assert.IsGreaterThan(0, order.CompareHeadLiterals(conceptHigh, conceptLow, ContextGrammarKind.Ordinary), "The concept atom with the greater-id constant argument orders strictly above.");
        Assert.IsLessThan(0, order.CompareHeadLiterals(conceptLow, conceptHigh, ContextGrammarKind.Ordinary), "The concept comparison is antisymmetric over the constant argument.");
    }

    /// <summary>The normalised sign of a comparison — minus one, zero, or one — so a comparison whose magnitude is unspecified matches an expected direction.</summary>
    /// <param name="value">The comparison result.</param>
    /// <returns>The sign.</returns>
    private static int SignOf(int value)
    {
        return value < 0 ? -1 : value > 0 ? 1 : 0;
    }

    /// <summary>Checks that one atom is strictly greater than another and not the reverse, recording an offender otherwise.</summary>
    /// <param name="order">The term order.</param>
    /// <param name="greater">The atom expected to be the greater.</param>
    /// <param name="lesser">The atom expected to be the lesser.</param>
    /// <param name="offenders">The offender list this check appends to.</param>
    private static void CheckMonotone(ContextTermOrder order, DlLiteral greater, DlLiteral lesser, List<string> offenders)
    {
        ContextOrderTerm greaterTerm = ContextOrderTerm.OfAtom(greater);
        ContextOrderTerm lesserTerm = ContextOrderTerm.OfAtom(lesser);
        if(!order.Greater(greaterTerm, lesserTerm, ContextGrammarKind.Ordinary))
        {
            offenders.Add($"context not monotone: {Render(greater)} not greater than {Render(lesser)}");
        }
    }

    /// <summary>The strictly ordered F-term pairs (greater, lesser) the monotonicity check embeds: function terms by mint order, function terms over the central variable, and the central over the context variable.</summary>
    /// <returns>The ordered pairs.</returns>
    private static List<(DlTerm Greater, DlTerm Lesser)> OrderedFTermPairs()
    {
        List<(DlTerm, DlTerm)> pairs = [];
        for(int higher = 1; higher < FunctionSymbolCount; higher++)
        {
            pairs.Add((DlTerm.Function(higher), DlTerm.Function(higher - 1)));
            pairs.Add((DlTerm.Function(higher), DlTerm.Central));
        }

        pairs.Add((DlTerm.Function(0), DlTerm.Central));
        pairs.Add((DlTerm.Central, DlTerm.Context));

        return pairs;
    }

    /// <summary>The three predecessor-trigger atom shapes <c>B(y)</c>, <c>S(x, y)</c>, <c>S(y, x)</c> the condition-4 and condition-5 tests exercise, over the first concept and role symbol.</summary>
    /// <returns>The predecessor atoms.</returns>
    private static List<DlLiteral> PredecessorAtoms()
    {
        return
        [
            DlLiteral.Concept(ConceptSymbols[0], DlTerm.Context),
            DlLiteral.Role(RoleSymbols[0], DlTerm.Central, DlTerm.Context),
            DlLiteral.Role(RoleSymbols[0], DlTerm.Context, DlTerm.Central),
        ];
    }

    /// <summary>
    /// Builds a term order whose <c>Pr(O)</c> contains each of the three
    /// predecessor shapes, by handing <see cref="ContextTermOrder.ForModule"/> a
    /// clause set whose bodies induce them: a concept atom seeds <c>B(y)</c>, a
    /// predecessor role body atom seeds <c>S(x, y)</c>, and a successor role body
    /// atom seeds <c>S(y, x)</c> (KR 2016 Definition 2's swap).
    /// </summary>
    /// <returns>The order with a populated predecessor-trigger set.</returns>
    private static ContextTermOrder PredecessorOrder()
    {
        DlClause conceptClause = DlClause.Create([DlLiteral.Concept(ConceptSymbols[0], DlTerm.Central)], [DlLiteral.Concept(ConceptSymbols[1], DlTerm.Central)], 0);
        DlClause successorRole = DlClause.Create([DlLiteral.Role(RoleSymbols[0], DlTerm.Central, DlTerm.Neighbour(1))], [DlLiteral.Concept(ConceptSymbols[1], DlTerm.Neighbour(1))], 0);
        DlClause predecessorRole = DlClause.Create([DlLiteral.Role(RoleSymbols[0], DlTerm.Neighbour(1), DlTerm.Central)], [DlLiteral.Concept(ConceptSymbols[1], DlTerm.Neighbour(1))], 0);
        ContextTermOrder order = ContextTermOrder.ForModule([conceptClause, successorRole, predecessorRole]);

        return order;
    }

    /// <summary>Whether an order term is one of the two variable constants <c>x</c> or <c>y</c>, excluded by condition 5's quantifier.</summary>
    /// <param name="term">The order term.</param>
    /// <returns><see langword="true"/> for the central or context variable.</returns>
    private static bool IsVariable(ContextOrderTerm term)
    {
        return !term.IsAtom && (term.Term.Kind == DlTermKind.Central || term.Term.Kind == DlTermKind.Context);
    }

    /// <summary>The F-term subterms of an atom -- the concept argument, or both role arguments.</summary>
    /// <param name="atom">The atom.</param>
    /// <returns>The argument terms.</returns>
    private static List<DlTerm> Subterms(DlLiteral atom)
    {
        return atom.Kind == DlLiteralKind.Role ? [atom.First, atom.Second] : [atom.First];
    }

    /// <summary>A function term as an order term.</summary>
    /// <param name="symbol">The function symbol id.</param>
    /// <returns>The order term.</returns>
    private static ContextOrderTerm Function(int symbol)
    {
        return ContextOrderTerm.OfTerm(DlTerm.Function(symbol));
    }

    /// <summary>A seeded, reproducible population of F-terms and atoms drawn from the fixed symbol pools.</summary>
    /// <param name="random">The seeded randomness source.</param>
    /// <returns>The population.</returns>
    private static Population BuildPopulation(RandomSourceDelegate random)
    {
        List<DlTerm> terms = [DlTerm.Central, DlTerm.Context];
        for(int neighbour = 1; neighbour <= NeighbourCount; neighbour++)
        {
            terms.Add(DlTerm.Neighbour(neighbour));
        }

        for(int symbol = 0; symbol < FunctionSymbolCount; symbol++)
        {
            terms.Add(DlTerm.Function(symbol));
        }

        List<DlLiteral> atoms = [];
        for(int i = 0; i < 40; i++)
        {
            DlTerm first = terms[(int)(random() % (ulong)terms.Count)];
            if(random() % 2 == 0)
            {
                int conceptSymbol = ConceptSymbols[(int)(random() % (ulong)ConceptSymbols.Length)];
                atoms.Add(DlLiteral.Concept(conceptSymbol, first));
            }
            else
            {
                DlTerm second = terms[(int)(random() % (ulong)terms.Count)];
                int roleSymbol = RoleSymbols[(int)(random() % (ulong)RoleSymbols.Length)];
                atoms.Add(DlLiteral.Role(roleSymbol, first, second));
            }
        }

        return new Population(terms, atoms);
    }

    /// <summary>Renders an F-term for an offender message.</summary>
    /// <param name="term">The term.</param>
    /// <returns>The rendering.</returns>
    private static string Render(DlTerm term)
    {
        return term.Kind switch
        {
            DlTermKind.Central => "x",
            DlTermKind.Context => "y",
            DlTermKind.Neighbour => $"z{term.Index}",
            _ => $"f{term.Index}(x)",
        };
    }

    /// <summary>Renders an atom for an offender message.</summary>
    /// <param name="atom">The atom.</param>
    /// <returns>The rendering.</returns>
    private static string Render(DlLiteral atom)
    {
        return atom.Kind == DlLiteralKind.Role
            ? $"S{atom.Symbol}({Render(atom.First)},{Render(atom.Second)})"
            : $"B{atom.Symbol}({Render(atom.First)})";
    }

    /// <summary>Renders an order term for an offender message.</summary>
    /// <param name="term">The order term.</param>
    /// <returns>The rendering.</returns>
    private static string Render(ContextOrderTerm term)
    {
        return term.IsAtom ? Render(term.Atom) : Render(term.Term);
    }

    /// <summary>Builds the failure report from the offender list.</summary>
    /// <param name="condition">The condition's label.</param>
    /// <param name="offenders">The offenders.</param>
    /// <returns>The report text.</returns>
    private static string Report(string condition, List<string> offenders)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Definition-3 {condition}: {offenders.Count} offender(s).");
        foreach(string offender in offenders)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {offender}");
        }

        return report.ToString();
    }

    /// <summary>A generated term population: the F-terms and the atoms over them.</summary>
    /// <param name="Terms">The F-terms.</param>
    /// <param name="Atoms">The atoms.</param>
    private sealed record Population(List<DlTerm> Terms, List<DlLiteral> Atoms);
}
