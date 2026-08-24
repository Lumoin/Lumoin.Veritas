using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// A context term for the term order: either an F-term (<c>x</c>, <c>y</c>, or
/// <c>f(x)</c>) or a P-term atom (<c>B(t)</c> or <c>S(t1, t2)</c>). The order
/// (<see cref="ContextTermOrder"/>) compares these uniformly.
/// </summary>
internal readonly struct ContextOrderTerm
{
    /// <summary>Whether this element is a P-term atom (otherwise an F-term).</summary>
    public bool IsAtom { get; }

    /// <summary>The atom, when <see cref="IsAtom"/>.</summary>
    public DlLiteral Atom { get; }

    /// <summary>The F-term, when not <see cref="IsAtom"/>.</summary>
    public DlTerm Term { get; }

    /// <summary>Initialises a context term.</summary>
    /// <param name="isAtom">Whether the element is an atom.</param>
    /// <param name="atom">The atom.</param>
    /// <param name="term">The F-term.</param>
    private ContextOrderTerm(bool isAtom, DlLiteral atom, DlTerm term)
    {
        IsAtom = isAtom;
        Atom = atom;
        Term = term;
    }

    /// <summary>Builds a context term from an F-term.</summary>
    /// <param name="term">The F-term.</param>
    /// <returns>The context term.</returns>
    public static ContextOrderTerm OfTerm(DlTerm term)
    {
        return new ContextOrderTerm(false, default, term);
    }

    /// <summary>Builds a context term from a P-term atom.</summary>
    /// <param name="atom">The concept or role atom.</param>
    /// <returns>The context term.</returns>
    public static ContextOrderTerm OfAtom(DlLiteral atom)
    {
        return new ContextOrderTerm(true, atom, default);
    }
}

/// <summary>
/// The context term order of the consequence-based SRIQ calculus
/// (KR 2016, Definition 3;
/// <see href="https://arxiv.org/abs/1602.04498"/>): a relaxed LPO over context
/// terms that satisfies the five order conditions, most importantly the
/// completeness-critical fifth — a predecessor-trigger atom (<c>Pr(O)</c>) is
/// never <c>≻</c>-greater than any context term outside <c>{x, y}</c>. The
/// realization is a stratification, ascending: <c>y</c> (the least term), then
/// <c>x</c>, then the (finitely many) <c>Pr(O)</c> shapes, then everything
/// else. An LPO keyed on a total symbol precedence (constants below function
/// symbols below concept predicates below role predicates; function symbols by
/// their mint order, which is the KR 2016 Definition 3 precedence) orders the top
/// stratum. The context-term grammar is shallow (an atom over F-terms whose
/// only nesting is <c>f(x)</c>), so the LPO reduces to a symbol-precedence
/// comparison with a lexicographic fallback on F-term arguments — no recursion.
/// </summary>
/// <remarks>
/// <see cref="Su"/> and <see cref="Pr"/> (KR 2016 Definition 2) are computed here,
/// per module, from the clause set and handed to the order at construction —
/// this file is their home. Condition 1 holds because function terms sit in the
/// top stratum, above <c>x</c>, above <c>y</c>. The subterm property (condition
/// 4) holds because every atom — a Pr atom included — sits above its
/// <c>x</c>/<c>y</c> subterms, and within the top stratum an atom's symbol
/// outranks its F-term arguments. Condition 5 holds because a Pr atom sits below
/// the whole top stratum and is UNORDERED against its Pr peers — the condition
/// caps it against every term outside <c>{x, y}</c>, other Pr atoms included.
/// </remarks>
internal sealed class ContextTermOrder
{
    /// <summary>The stratum of the context variable <c>y</c> — the least context term (condition 1).</summary>
    private const int StratumY = 0;

    /// <summary>The stratum of the central variable <c>x</c>, above <c>y</c> (condition 1).</summary>
    private const int StratumX = 1;

    /// <summary>The stratum of a Pr(O) predecessor-trigger atom: above <c>x</c> and <c>y</c> (the subterm property, condition 4) but below every other context term, and unordered against its Pr peers (condition 5).</summary>
    private const int StratumPr = 2;

    /// <summary>The stratum of every other context term. Internally the comparison is term-major (the maximal F-term argument first), with the QUERY-CONCEPT BAND relaxation: central-variable concept atoms whose arguments tie are MUTUALLY UNORDERED — the deleted symbol comparisons keep every named atom query-minimal simultaneously without breaking substitution monotonicity (condition 3), which a below-everything band stratum would violate on <c>B(x)</c> versus a non-Pr <c>B(y)</c>.</summary>
    private const int StratumRest = 3;

    /// <summary>The predecessor-trigger atoms of the module (KR 2016 Definition 2, <c>Pr(O)</c>): the shapes <c>B(y)</c>, <c>S(x, y)</c>, <c>S(y, x)</c>, never <c>≻</c>-greater than any term outside <c>{x, y}</c>.</summary>
    public IReadOnlySet<DlLiteral> Pr { get; }

    /// <summary>The successor-trigger atoms of the module (KR 2016 Definition 2, <c>Su(O)</c>): the shapes <c>B(x)</c>, <c>S(x, y)</c>, <c>S(y, x)</c> read off the clause bodies.</summary>
    public IReadOnlySet<DlLiteral> Su { get; }

    /// <summary>Initialises the order with its module's successor and predecessor trigger sets.</summary>
    /// <param name="successorTriggers">The <c>Su(O)</c> set.</param>
    /// <param name="predecessorTriggers">The <c>Pr(O)</c> set.</param>
    private ContextTermOrder(IReadOnlySet<DlLiteral> successorTriggers, IReadOnlySet<DlLiteral> predecessorTriggers)
    {
        Su = successorTriggers;
        Pr = predecessorTriggers;
    }

    /// <summary>
    /// Builds the order for a module by computing <c>Su(O)</c> and <c>Pr(O)</c>
    /// from its clause set (KR 2016 Definition 2): each body atom contributes its
    /// successor shape to <c>Su</c>; <c>Pr</c> is <c>Su</c> with <c>x</c> and
    /// <c>y</c> swapped, plus <c>B(y)</c> for every concept atom in the module.
    /// </summary>
    /// <param name="clauses">The module's DL-clauses.</param>
    /// <returns>The context term order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is <see langword="null"/>.</exception>
    public static ContextTermOrder ForModule(IReadOnlyList<DlClause> clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        HashSet<DlLiteral> successorTriggers = [];
        HashSet<DlLiteral> predecessorTriggers = [];
        HashSet<int> concepts = [];

        foreach(DlClause clause in clauses)
        {
            foreach(DlLiteral literal in clause.Body)
            {
                CollectTriggers(literal, successorTriggers, predecessorTriggers, concepts);
            }

            foreach(DlLiteral literal in clause.Head)
            {
                CollectConcept(literal, concepts);
            }
        }

        foreach(int concept in concepts)
        {
            predecessorTriggers.Add(DlLiteral.Concept(concept, DlTerm.Context));
        }

        return new ContextTermOrder(successorTriggers, predecessorTriggers);
    }

    /// <summary>Records a body atom's successor shape into <c>Su</c> and its swapped predecessor shape into <c>Pr</c>, and its concept symbols.</summary>
    /// <param name="literal">The body atom.</param>
    /// <param name="successorTriggers">The <c>Su</c> accumulator.</param>
    /// <param name="predecessorTriggers">The <c>Pr</c> accumulator.</param>
    /// <param name="concepts">The concept-symbol accumulator.</param>
    private static void CollectTriggers(DlLiteral literal, HashSet<DlLiteral> successorTriggers, HashSet<DlLiteral> predecessorTriggers, HashSet<int> concepts)
    {
        switch(literal.Kind)
        {
            case(DlLiteralKind.Concept):
            {
                concepts.Add(literal.Symbol);
                successorTriggers.Add(DlLiteral.Concept(literal.Symbol, DlTerm.Central));
                break;
            }
            case(DlLiteralKind.Role):
            {
                bool firstCentral = literal.First.IsCentral;
                DlTerm successorFirst = firstCentral ? DlTerm.Central : DlTerm.Context;
                DlTerm successorSecond = firstCentral ? DlTerm.Context : DlTerm.Central;
                successorTriggers.Add(DlLiteral.Role(literal.Symbol, successorFirst, successorSecond));
                predecessorTriggers.Add(DlLiteral.Role(literal.Symbol, successorSecond, successorFirst));
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Records a head literal's concept symbol, if it is a concept atom, into the concept-symbol accumulator.</summary>
    /// <param name="literal">The head literal.</param>
    /// <param name="concepts">The concept-symbol accumulator.</param>
    private static void CollectConcept(DlLiteral literal, HashSet<int> concepts)
    {
        if(literal.Kind == DlLiteralKind.Concept)
        {
            concepts.Add(literal.Symbol);
        }
    }

    /// <summary>Whether <paramref name="a"/> is strictly <c>≻</c>-greater than <paramref name="b"/> under the order of the given context kind.</summary>
    /// <param name="a">The left context term.</param>
    /// <param name="b">The right context term.</param>
    /// <param name="kind">The context kind whose condition-6 minimal band the comparison runs under (Definition 3 condition 6 names the ordinary <c>Pr</c> and root <c>Prr</c> bands; the nominal-root band unions them).</param>
    /// <returns><see langword="true"/> when <c>a ≻ b</c>.</returns>
    public bool Greater(ContextOrderTerm a, ContextOrderTerm b, ContextGrammarKind kind)
    {
        return Compare(a, b, kind) > 0;
    }

    /// <summary>
    /// Compares two context terms under the relaxed order: the stratum ranks
    /// first (ascending <c>y</c>, <c>x</c>, band, rest), and only the top stratum
    /// is internally ordered — by symbol precedence with a lexicographic
    /// fallback on a-term arguments. Band atoms compare as unordered against one
    /// another (condition 6), and the <c>x</c>/<c>y</c> strata are singletons.
    /// The band is context-kind-dependent: <c>Pr(O)</c> atoms (plus the broadened
    /// ground role atoms) in an ordinary context, the <c>Prr</c> shapes in the
    /// single root context, and their union in a nominal-root context.
    /// </summary>
    /// <param name="a">The left context term.</param>
    /// <param name="b">The right context term.</param>
    /// <param name="kind">The context kind whose condition-6 minimal band applies.</param>
    /// <returns>A negative value when <c>a ≺ b</c>, a positive value when <c>a ≻ b</c>, zero when equal or unordered.</returns>
    public int Compare(ContextOrderTerm a, ContextOrderTerm b, ContextGrammarKind kind)
    {
        int stratumA = Stratum(a, kind);
        int stratumB = Stratum(b, kind);
        if(stratumA != stratumB)
        {
            return stratumA.CompareTo(stratumB);
        }

        return stratumA == StratumRest ? CompareWithinRank(a, b) : 0;
    }

    /// <summary>The stratum of a context term, ascending: <c>y</c>, then <c>x</c>, then a band atom (the condition-6 minimal shapes of the context kind), then everything else. Named individuals and function-of-individual terms sit in the rest stratum.</summary>
    /// <param name="element">The context term.</param>
    /// <param name="kind">The context kind whose band applies.</param>
    /// <returns>The stratum.</returns>
    private int Stratum(ContextOrderTerm element, ContextGrammarKind kind)
    {
        if(element.IsAtom)
        {
            return IsBandAtom(element.Atom, kind) ? StratumPr : StratumRest;
        }

        return element.Term.Kind switch
        {
            DlTermKind.Context => StratumY,
            DlTermKind.Central => StratumX,
            _ => StratumRest,
        };
    }

    /// <summary>Whether a head literal sits in the condition-6 minimal band of the given context kind: in an ordinary context, the module's materialized <c>Pr(O)</c> atoms plus the broadened ground role atoms <c>S(o, o′)</c>; in the single root context, the <c>Prr</c> shapes — <c>B(y)</c>, <c>B(o)</c>, <c>S(y, o)</c>, <c>S(o, y)</c>, and the broadened <c>S(o, o′)</c>; in a nominal-root context, the union of both — the entry translation respells own constants as the central variable, so <c>v_o</c>'s central- and predecessor-anchored atoms must band exactly as the identical literal bands in an ordinary context, while the foreign-constant <c>Prr</c> shapes keep the root banding. Equality and inequality literals are never band members (condition 6 ranges over atoms).</summary>
    /// <param name="atom">The head literal.</param>
    /// <param name="kind">The context kind whose band applies.</param>
    /// <returns><see langword="true"/> for a band atom.</returns>
    private bool IsBandAtom(DlLiteral atom, ContextGrammarKind kind)
    {
        return kind switch
        {
            ContextGrammarKind.Root => IsRootBandAtom(atom),
            ContextGrammarKind.NominalRoot => IsRootBandAtom(atom) || Pr.Contains(atom),
            _ => Pr.Contains(atom) || IsGroundRoleAtom(atom),
        };
    }

    /// <summary>Whether a literal is a <c>Prr</c>-shaped band atom of the root context order: a concept atom over <c>y</c> or a named individual, or a role atom whose arguments are drawn from <c>y</c> and named individuals with at least one individual side.</summary>
    /// <param name="atom">The head literal.</param>
    /// <returns><see langword="true"/> for a root band atom.</returns>
    private static bool IsRootBandAtom(DlLiteral atom)
    {
        return atom.Kind switch
        {
            DlLiteralKind.Concept => atom.First.Kind == DlTermKind.Context || atom.First.IsIndividual,
            DlLiteralKind.Role => (atom.First.Kind == DlTermKind.Context || atom.First.IsIndividual)
                && (atom.Second.Kind == DlTermKind.Context || atom.Second.IsIndividual)
                && (atom.First.IsIndividual || atom.Second.IsIndividual),
            _ => false,
        };
    }

    /// <summary>Whether a literal is a ground role atom <c>S(o, o′)</c> — the broadened predecessor-trigger shape banded in ordinary contexts alongside the materialized <c>Pr(O)</c> set.</summary>
    /// <param name="atom">The head literal.</param>
    /// <returns><see langword="true"/> for a ground role atom.</returns>
    private static bool IsGroundRoleAtom(DlLiteral atom)
    {
        return atom.Kind == DlLiteralKind.Role && atom.First.IsIndividual && atom.Second.IsIndividual;
    }

    /// <summary>
    /// Compares two head literals under the selection order the
    /// disjunctive lift resolves on: the stratified atom-level order first —
    /// query-concept-band peers and Pr peers are MUTUALLY UNORDERED (zero), the
    /// published relaxation that keeps every named atom query-minimal and every
    /// Pr atom condition-5-minimal — and within the rest stratum, where
    /// every equality and inequality literal carries <see cref="DlLiteral.NoSymbol"/>
    /// and <see cref="CompareArguments"/> never reaches its second term, the
    /// total tie-break is <see cref="CompareFTerm"/> on the term-order-maximal
    /// side, then on the minimal side, then <see cref="DlLiteral.CompareTo"/>.
    /// Equality and inequality literals are compared through their oriented
    /// (maximal-side-first) view, so the comparison never depends on storage
    /// order. The relation is total on rest-stratum literals and partial at
    /// the band/Pr bottom — a head's maximal set is therefore a singleton
    /// unless the head consists of band and/or Pr literals only.
    /// </summary>
    /// <param name="a">The left head literal.</param>
    /// <param name="b">The right head literal.</param>
    /// <param name="kind">The context kind whose condition-6 minimal band the comparison runs under.</param>
    /// <returns>A negative value when <c>a</c> orders below <c>b</c>, a positive value when above, zero for equal or mutually unordered literals.</returns>
    public int CompareHeadLiterals(DlLiteral a, DlLiteral b, ContextGrammarKind kind)
    {
        DlLiteral orientedA = OrientView(a);
        DlLiteral orientedB = OrientView(b);
        int primary = Compare(ContextOrderTerm.OfAtom(orientedA), ContextOrderTerm.OfAtom(orientedB), kind);
        if(primary != 0)
        {
            return primary;
        }

        if(Stratum(ContextOrderTerm.OfAtom(orientedA), kind) != StratumRest
            || (IsCentralConceptAtom(ContextOrderTerm.OfAtom(orientedA)) && IsCentralConceptAtom(ContextOrderTerm.OfAtom(orientedB))))
        {
            return 0;
        }

        if(!TryCompareFTerm(orientedA.First, orientedB.First, out int byFirst))
        {
            return 0;
        }

        if(byFirst != 0)
        {
            return byFirst;
        }

        if(!TryCompareFTerm(orientedA.Second, orientedB.Second, out int bySecond))
        {
            return 0;
        }

        if(bySecond != 0)
        {
            return bySecond;
        }

        return orientedA.CompareTo(orientedB);
    }

    /// <summary>Appends the indexes of the MAXIMAL literals of a non-empty canonical head span — every literal no other head literal strictly exceeds under the selection order of the context kind. A singleton everywhere except pure-band heads and heads whose members are mutually unordered (band atoms, or literals split by the variable-versus-individual incomparability); ordered resolution fires once per maximal literal.</summary>
    /// <param name="head">The head literals (canonical, non-empty).</param>
    /// <param name="maximalToAppendTo">The buffer the maximal indexes are appended to, in head order.</param>
    /// <param name="kind">The context kind whose selection order applies.</param>
    public void CollectMaximalHead(ReadOnlySpan<DlLiteral> head, List<int> maximalToAppendTo, ContextGrammarKind kind)
    {
        for(int i = 0; i < head.Length; i++)
        {
            bool dominated = false;
            for(int j = 0; j < head.Length; j++)
            {
                if(j != i && CompareHeadLiterals(head[j], head[i], kind) > 0)
                {
                    dominated = true;

                    break;
                }
            }

            if(!dominated)
            {
                maximalToAppendTo.Add(i);
            }
        }
    }

    /// <summary>The selection view of a head literal: an equality or inequality literal oriented maximal-side-first, an atom unchanged — the storage-independent form the selection order compares.</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns>The oriented view.</returns>
    private static DlLiteral OrientView(DlLiteral literal)
    {
        return literal.Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality ? OrientEqualityLiteral(literal) : literal;
    }

    /// <summary>
    /// Orients an equality or inequality literal into its canonical stored form:
    /// a COMPARABLE pair stores its maximal a-term first (the orientation
    /// invariant the Eq rule's maximal-side reads rely on), and an INCOMPARABLE
    /// pair — a variable against a named individual, which the partial order
    /// cannot orient — stores the VARIABLE side first, so head selection and
    /// <see cref="MaxArgument"/> stay storage-independent despite the unoriented
    /// comparison, and the Eq rule reads the constant side as the sole rewrite
    /// source (a variable slot is never a rewrite position).
    /// </summary>
    /// <param name="literal">The equality or inequality literal.</param>
    /// <returns>The canonically stored literal.</returns>
    public static DlLiteral OrientEqualityLiteral(DlLiteral literal)
    {
        bool comparable = TryCompareFTerm(literal.First, literal.Second, out int comparison);
        bool keep = comparable ? comparison >= 0 : literal.First.IsVariable;
        if(keep)
        {
            return literal;
        }

        return literal.Kind == DlLiteralKind.Equality
            ? DlLiteral.Equality(literal.Second, literal.First)
            : DlLiteral.Inequality(literal.Second, literal.First);
    }

    /// <summary>
    /// Compares two context terms in the rest stratum: TERM-MAJOR first — the
    /// maximal a-term argument under <see cref="CompareFTerm"/>, so any
    /// function-bearing literal outranks every plain central-concept form and
    /// substitution monotonicity (condition 4) holds. INCOMPARABLE maximal
    /// arguments (a variable against an individual) leave the whole literals
    /// mutually unordered — falling through to symbol precedence there would
    /// impose comparisons the partial order does not sanction and shrink maximal
    /// sets, a completeness risk; unordered only adds inferences. Equal maximal
    /// arguments continue: the query-concept band relaxation (two
    /// central-variable concept atoms whose arguments tie are mutually
    /// unordered), then symbol precedence with a lexicographic fallback
    /// on the a-term arguments.
    /// </summary>
    /// <param name="a">The left context term.</param>
    /// <param name="b">The right context term.</param>
    /// <returns>A signed comparison; zero for equal or mutually unordered terms.</returns>
    private static int CompareWithinRank(ContextOrderTerm a, ContextOrderTerm b)
    {
        if(!TryCompareFTerm(MaxArgument(a), MaxArgument(b), out int byMaxArgument))
        {
            return 0;
        }

        if(byMaxArgument != 0)
        {
            return byMaxArgument;
        }

        if(IsCentralConceptAtom(a) && IsCentralConceptAtom(b))
        {
            return 0;
        }

        (int tierA, long idA) = Precedence(a);
        (int tierB, long idB) = Precedence(b);
        int byTier = tierA.CompareTo(tierB);
        if(byTier != 0)
        {
            return byTier;
        }

        int byId = idA.CompareTo(idB);
        if(byId != 0)
        {
            return byId;
        }

        return CompareArguments(a, b);
    }

    /// <summary>The maximal F-term argument of a context term under <see cref="CompareFTerm"/>: the term itself for an F-term, the sole argument of a concept atom, the greater argument of a role atom or (in)equality — storage-order independent.</summary>
    /// <param name="element">The context term.</param>
    /// <returns>The maximal argument.</returns>
    private static DlTerm MaxArgument(ContextOrderTerm element)
    {
        if(!element.IsAtom)
        {
            return element.Term;
        }

        if(element.Atom.Kind == DlLiteralKind.Concept)
        {
            return element.Atom.First;
        }

        return CompareFTerm(element.Atom.First, element.Atom.Second) >= 0 ? element.Atom.First : element.Atom.Second;
    }

    /// <summary>Whether a context term is a central-variable concept atom <c>B(x)</c> — the query-concept shape the band relaxation leaves mutually unordered.</summary>
    /// <param name="element">The context term.</param>
    /// <returns><see langword="true"/> for a central-variable concept atom.</returns>
    private static bool IsCentralConceptAtom(ContextOrderTerm element)
    {
        return element.IsAtom && element.Atom.Kind == DlLiteralKind.Concept && element.Atom.First.IsCentral;
    }

    /// <summary>The symbol precedence tuple: a tier and the symbol id, ordered so the variables sit below individuals below function symbols below concept predicates below role predicates, with function symbols in mint order and individuals in interned mint order. The term arms are EXHAUSTIVE (a silent fall-through would drop individuals into the function tier, where an individual id colliding with a function id makes distinct terms compare equal).</summary>
    /// <param name="element">The context term.</param>
    /// <returns>The (tier, id) precedence.</returns>
    private static (int Tier, long Id) Precedence(ContextOrderTerm element)
    {
        if(element.IsAtom)
        {
            return element.Atom.Kind == DlLiteralKind.Role ? (7, element.Atom.Symbol) : (6, element.Atom.Symbol);
        }

        return element.Term.Kind switch
        {
            DlTermKind.Context => (0, 0L),
            DlTermKind.Central => (1, 0L),
            DlTermKind.Neighbour => (2, element.Term.Index),
            DlTermKind.Individual => (3, element.Term.Index),
            DlTermKind.Function => (4, element.Term.Index),
            DlTermKind.FunctionOfIndividual => (5, element.Term.Index),
            _ => throw new ArgumentOutOfRangeException(nameof(element), element.Term.Kind, "Every a-term carries one of the six packed term kinds."),
        };
    }

    /// <summary>Compares the F-term arguments of two context terms sharing a symbol, lexicographically.</summary>
    /// <param name="a">The left context term.</param>
    /// <param name="b">The right context term.</param>
    /// <returns>A signed comparison.</returns>
    private static int CompareArguments(ContextOrderTerm a, ContextOrderTerm b)
    {
        if(!a.IsAtom)
        {
            return 0;
        }

        int byFirst = CompareFTerm(a.Atom.First, b.Atom.First);
        if(byFirst != 0 || a.Atom.Kind != DlLiteralKind.Role)
        {
            return byFirst;
        }

        return CompareFTerm(a.Atom.Second, b.Atom.Second);
    }

    /// <summary>
    /// The a-term order, a PARTIAL order (arXiv:1805.01396 appendix A): the
    /// constructed order drops <c>x</c>-versus-individual and
    /// <c>y</c>-versus-individual in BOTH directions — if <c>x ≻ o</c> held, the
    /// <c>A ⊑ {o}</c>, <c>{o} ⊑ B</c> subsumption read-off would be incomplete,
    /// because <c>B(o)</c> could never rewrite toward <c>B(x)</c> — and is total
    /// everywhere else: <c>y ≺ x ≺ f(x) ≺ f(o)</c>, individuals below every
    /// function term and among themselves by interned mint order (Definition 3
    /// condition 2 and the label-monotone global order), function symbols in mint
    /// order, <c>f(o)</c> terms lexicographically by (function, individual). Zero
    /// means equal or incomparable; callers that must tell those apart use
    /// <see cref="TryCompareFTerm"/>. The saturation engine's equality-literal
    /// orientation reads this comparison, storing comparable literals
    /// maximal-side-first and incomparable (variable-versus-individual) literals
    /// in the canonical variable-first form.
    /// </summary>
    /// <param name="a">The left a-term.</param>
    /// <param name="b">The right a-term.</param>
    /// <returns>A signed comparison; zero when equal or incomparable.</returns>
    internal static int CompareFTerm(DlTerm a, DlTerm b)
    {
        return TryCompareFTerm(a, b, out int comparison) ? comparison : 0;
    }

    /// <summary>The a-term comparison with incomparability made explicit: <see langword="false"/> exactly on the variable-versus-individual shapes the appendix-A order construction drops (<c>x</c> or <c>y</c> against a named individual, either direction); a signed comparison through <paramref name="comparison"/> otherwise.</summary>
    /// <param name="a">The left a-term.</param>
    /// <param name="b">The right a-term.</param>
    /// <param name="comparison">The signed comparison when the terms are comparable.</param>
    /// <returns><see langword="true"/> when the terms are comparable.</returns>
    internal static bool TryCompareFTerm(DlTerm a, DlTerm b, out int comparison)
    {
        if(AreIncomparable(a, b))
        {
            comparison = 0;

            return false;
        }

        (int tierA, long idA) = FTermRank(a);
        (int tierB, long idB) = FTermRank(b);
        int byTier = tierA.CompareTo(tierB);
        comparison = byTier != 0 ? byTier : idA.CompareTo(idB);

        return true;
    }

    /// <summary>Whether an equality side can act as the Eq rewrite SOURCE <c>s1</c> against its other side <c>t1</c> (Table 2 Eq premise: <c>t1 ⋡ s1</c>): not a variable, and not dominated-or-equalled by the other side — satisfied both by a strictly smaller and by an incomparable other side, so the constant side of an unoriented <c>x ≈ o</c> is a source while the variable side never is.</summary>
    /// <param name="side">The candidate source side.</param>
    /// <param name="other">The other side.</param>
    /// <returns><see langword="true"/> when the side is a legal rewrite source.</returns>
    internal static bool IsRewriteSourceSide(DlTerm side, DlTerm other)
    {
        return !side.IsVariable && !(TryCompareFTerm(other, side, out int comparison) && comparison >= 0);
    }

    /// <summary>Whether an (in)equality side is a rewrite POSITION <c>s2</c> against its other side <c>t2</c> (Table 2 Eq target: <c>t2 ⊁ s2</c>): not a variable, and not strictly dominated by the other side — an incomparable other side leaves the position rewritable, so the constant side of <c>x ≈ o</c> is a rewrite position while the minimal side of an oriented equality is not.</summary>
    /// <param name="side">The candidate rewrite position.</param>
    /// <param name="other">The other side.</param>
    /// <returns><see langword="true"/> when the side is a legal rewrite position.</returns>
    internal static bool IsRewritableSide(DlTerm side, DlTerm other)
    {
        return !side.IsVariable && !(TryCompareFTerm(other, side, out int comparison) && comparison > 0);
    }

    /// <summary>Whether two a-terms are order-incomparable: one is the central or context variable and the other a named individual — the dropped pairs of the appendix-A construction.</summary>
    /// <param name="a">The left a-term.</param>
    /// <param name="b">The right a-term.</param>
    /// <returns><see langword="true"/> for a dropped (incomparable) pair.</returns>
    private static bool AreIncomparable(DlTerm a, DlTerm b)
    {
        return (IsCentralOrContext(a) && b.IsIndividual) || (IsCentralOrContext(b) && a.IsIndividual);
    }

    /// <summary>Whether a term is the central or the context variable — the two variables whose comparisons against individuals the order drops.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> for <c>x</c> or <c>y</c>.</returns>
    private static bool IsCentralOrContext(DlTerm term)
    {
        return term.Kind is DlTermKind.Central or DlTermKind.Context;
    }

    /// <summary>The rank tuple of an a-term, EXHAUSTIVE over the term kinds (a silent fall-through would drop individuals into the function tier, where an individual id colliding with a function id makes distinct terms compare equal and corrupts equality orientation): <c>y</c>, then <c>x</c>, then neighbours by index, then individuals by interned mint order (the global <c>⋗</c>), then function terms by symbol mint order, then <c>f(o)</c> terms lexicographically by (function, individual) — the packed payload IS that lexicographic key.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns>The (tier, id) rank.</returns>
    private static (int Tier, long Id) FTermRank(DlTerm term)
    {
        return term.Kind switch
        {
            DlTermKind.Context => (0, 0L),
            DlTermKind.Central => (1, 0L),
            DlTermKind.Neighbour => (2, term.Index),
            DlTermKind.Individual => (3, term.Index),
            DlTermKind.Function => (4, term.Index),
            DlTermKind.FunctionOfIndividual => (5, term.Index),
            _ => throw new ArgumentOutOfRangeException(nameof(term), term.Kind, "Every a-term carries one of the six packed term kinds."),
        };
    }
}
