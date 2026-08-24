using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The SROIQ clausifier batteries. Normal-form fidelity is checked against the
/// consequence-based SRIQ calculus Table 1
/// (KR 2016; <see href="https://arxiv.org/abs/1602.04498"/>); the
/// role-automaton encoding against the role-inclusion automaton construction
/// (Decidability of SHIQ with Complex Role Inclusion
/// Axioms, Artificial Intelligence 160, 2004, Definition 10;
/// <see href="https://doi.org/10.1016/j.artint.2004.06.002"/>) and chain
/// elimination (RIQ and SROIQ are Harder than SHOIQ, KR 2008, Lemma 10;
/// <see href="https://cdn.aaai.org/KR/2008/KR08-027.pdf"/>). The families cover
/// normal-form fidelity, polarity-correct fresh names (the H3 falsifier), the RBox
/// and simple-role guard rejections, the full-construct census (mutation M8), and
/// the inert engine shell. Every family is a report-every-offender loop over
/// hand-built modules whose ground truth lives in the row comments; a clause set
/// is asserted by its rendered, order-insensitive form (both sides sorted), with
/// fresh structural / automaton-state names canonicalised so a pin survives a
/// change in id allocation. The automaton rows reconstruct the emitted role
/// automaton from the chain-elimination clauses and assert language acceptance
/// directly, which pins the HS2004 construction independently of any fresh-atom
/// naming.
/// </summary>
[TestClass]
internal sealed partial class ContextClausifierTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The shared origin quad for the hand-built axioms; the clausifier keys provenance by module index, so one quad suffices.</summary>
    private static Quad AxiomOrigin { get; } = new(new NamedNode(Utf8Strings.From("s")), new NamedNode(Utf8Strings.From("p")), new NamedNode(Utf8Strings.From("o")), Graph: null);

    /// <summary>
    /// Every KR 2016 Table 1 form, including each inverse-position variant of
    /// DL2/DL3/DL4, domain/range lowering, equivalence split, pairwise
    /// disjointness, DisjointUnion, and functional / inverse-functional as DL4
    /// n=1. Each module is atomic, so the emitted clause set is exact and free of
    /// fresh structural atoms (only the deterministic Skolem functions of DL2 and
    /// the counting role of DL4 appear).
    /// </summary>
    [TestMethod]
    public void NormalFormFidelityBattery()
    {
        (string Name, ReasoningModule Module, string[] Expected)[] rows =
        [
            //DL1 atomic: A [= B ~> A(x) -> B(x).
            ("DL1_Atomic", Module(SubClassOf(Class("A"), Class("B"))),
                ["A(x) -> B(x)"]),

            //DL1 conjunctive subclass: A  AND  B [= C ~> A(x)  AND  B(x) -> C(x).
            ("DL1_ConjunctiveSubclass", Module(SubClassOf(Intersection(Class("A"), Class("B")), Class("C"))),
                ["A(x), B(x) -> C(x)"]),

            //DL1 disjunctive superclass (non-Horn, emitted faithfully): A [= B  OR  C ~> A(x) -> B(x)  OR  C(x).
            ("DL1_DisjunctiveSuperclass", Module(SubClassOf(Class("A"), Union(Class("B"), Class("C")))),
                ["A(x) -> B(x), C(x)"]),

            //Pairwise disjointness (2): A  AND  B [= Bottom ~> A(x)  AND  B(x) -> (empty head).
            ("Disjoint2", Module(DisjointClasses(Class("A"), Class("B"))),
                ["A(x), B(x) ->"]),

            //Pairwise disjointness (3): the three pairs, each an empty head.
            ("Disjoint3", Module(DisjointClasses(Class("A"), Class("B"), Class("C"))),
                ["A(x), B(x) ->", "A(x), C(x) ->", "B(x), C(x) ->"]),

            //DL2 n=1: A [= exists r.B ~> A(x) -> r(x, f0(x)); A(x) -> B(f0(x)).
            ("DL2_n1", Module(SubClassOf(Class("A"), Some("r", Class("B")))),
                ["A(x) -> r(x,f0(x))", "A(x) -> B(f0(x))"]),

            //DL2 n=2: two witnesses plus the pairwise inequality (mutation M3 target).
            ("DL2_n2", Module(SubClassOf(Class("A"), Min("r", 2, Class("B")))),
                ["A(x) -> r(x,f0(x))", "A(x) -> B(f0(x))", "A(x) -> r(x,f1(x))", "A(x) -> B(f1(x))", "A(x) -> f0(x) != f1(x)"]),

            //DL2 inverse: A [= exists r^-.B ~> the role atom becomes r(f0(x), x) (mutation M2 target).
            ("DL2_Inverse", Module(SubClassOf(Class("A"), SomeInverse("r", Class("B")))),
                ["A(x) -> r(f0(x),x)", "A(x) -> B(f0(x))"]),

            //DL3: exists r.B [= C ~> r(z1, x)  AND  B(x) -> C(z1).
            ("DL3", Module(SubClassOf(Some("r", Class("B")), Class("C"))),
                ["B(x), r(z1,x) -> C(z1)"]),

            //DL3 inverse: exists r^-.B [= C ~> r(x, z1)  AND  B(x) -> C(z1) (mutation M2 target).
            ("DL3_Inverse", Module(SubClassOf(SomeInverse("r", Class("B")), Class("C"))),
                ["B(x), r(x,z1) -> C(z1)"]),

            //DL4: A [= <=1 r.B ~> aux r(z1, x)  AND  B(x) -> S_B(z1, x); A(x)  AND  S_B(x, z1)  AND  S_B(x, z2) -> z1 = z2.
            ("DL4", Module(SubClassOf(Class("A"), Max("r", 1, Class("B")))),
                ["B(x), r(z1,x) -> cr0(z1,x)", "A(x), cr0(x,z1), cr0(x,z2) -> z1 = z2"]),

            //DL4 inverse: the aux role atom becomes r(x, z1) while the fresh counting role stays predecessor-oriented.
            ("DL4_Inverse", Module(SubClassOf(Class("A"), MaxInverse("r", 1, Class("B")))),
                ["B(x), r(x,z1) -> cr0(z1,x)", "A(x), cr0(x,z1), cr0(x,z2) -> z1 = z2"]),

            //Domain: exists r.Top [= C ~> r(z1, x)  AND  Top(x) -> C(z1).
            ("Domain", Module(Domain("r", Class("C"))),
                ["Top(x), r(z1,x) -> C(z1)"]),

            //Range over a simple role: Top [= forall r.C ~> Top(x)  AND  r(x, z1) -> C(z1) (DL3-shape).
            ("Range", Module(Range("r", Class("C"))),
                ["Top(x), r(x,z1) -> C(z1)"]),

            //Equivalence split: A == B ~> A [= B and B [= A.
            ("Equivalence", Module(EquivalentClasses(Class("A"), Class("B"))),
                ["A(x) -> B(x)", "B(x) -> A(x)"]),

            //DisjointUnion A = B  OR  C: covering, both member directions, and pairwise disjointness.
            ("DisjointUnion", Module(DisjointUnion("A", Class("B"), Class("C"))),
                ["A(x) -> B(x), C(x)", "B(x) -> A(x)", "C(x) -> A(x)", "B(x), C(x) ->"]),

            //DL5: r [= s ~> r(z1, x) -> s(z1, x).
            ("DL5", Module(SubProperty("r", "s")),
                ["r(z1,x) -> s(z1,x)"]),

            //DL6: r [= s^- ~> r(z1, x) -> s(x, z1).
            ("DL6", Module(SubPropertyInverse("r", "s")),
                ["r(z1,x) -> s(x,z1)"]),

            //Inverse sub-role r^- [= s normalizes to r(x, z1) -> s(z1, x) via Inv before emission.
            ("DL5_InverseSubRole", Module(InverseSubProperty("r", "s")),
                ["r(x,z1) -> s(z1,x)"]),

            //Functional as DL4 n=1: Top [= <=1 r.Top.
            ("Functional", Module(Functional("r")),
                ["Top(x), r(z1,x) -> cr0(z1,x)", "Top(x), cr0(x,z1), cr0(x,z2) -> z1 = z2"]),

            //InverseFunctional as DL4 n=1 over r^-: the aux role atom becomes r(x, z1).
            ("InverseFunctional", Module(InverseFunctional("r")),
                ["Top(x), r(x,z1) -> cr0(z1,x)", "Top(x), cr0(x,z1), cr0(x,z2) -> z1 = z2"]),
        ];

        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("normal-form fidelity: row | verdict");
        foreach((string name, ReasoningModule module, string[] expected) in rows)
        {
            ClausificationResult result = ContextClausifier.Clausify(module);
            string? detail = CompareClauseSets(expected, result);
            report.AppendLine(CultureInfo.InvariantCulture, $"{name} | {(detail is null ? "OK" : "MISMATCH")}");
            if(detail is not null)
            {
                failures.Add($"{name}: {detail}");
            }
        }

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The ELH degeneracy witness (KR 2016 Section 2): an ELH module
    /// emits ONLY DL1 with single-atom heads, DL2 with n=1, DL3, and DL5 -- no
    /// disjunction, no equality/inequality, no fresh counting role. Asserted
    /// structurally over the emitted clause kinds.
    /// </summary>
    [TestMethod]
    public void ElhDegeneracyShapeFamily()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Class("B")),
            SubClassOf(Intersection(Class("A"), Class("B")), Class("C")),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Some("r", Class("B")), Class("C")),
            SubProperty("r", "s"),
            Domain("r", Class("D")));

        ClausificationResult result = ContextClausifier.Clausify(module);
        List<string> failures = [];

        Assert.IsEmpty(result.Remainder, "An ELH module has no remainder.");
        Assert.AreEqual(0, result.FreshRoles, "An ELH module mints no fresh counting role.");
        Assert.AreEqual(0, result.FreshAtoms, "An ELH module mints no fresh structural atom.");

        foreach(DlClause clause in result.Clauses)
        {
            foreach(DlLiteral literal in clause.Head)
            {
                if(literal.Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality)
                {
                    failures.Add($"ELH clause carries an (in)equality head: {RenderClause(clause, result.Symbols)}");
                }
            }

            bool conceptBody = true;
            foreach(DlLiteral literal in clause.Body)
            {
                if(literal.Kind != DlLiteralKind.Concept)
                {
                    conceptBody = false;
                }
            }

            //A DL1 clause (all-concept body) must have a single-atom head -- no disjunctive head in ELH.
            if(conceptBody && clause.Body.Length >= 1 && !HasRoleHead(clause) && clause.Head.Length > 1)
            {
                failures.Add($"ELH DL1 clause has a disjunctive head: {RenderClause(clause, result.Symbols)}");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// The H3 falsifier: rows where the WRONG fresh-name abstraction
    /// direction changes the emitted clause set in a hand-derivable way -- nested
    /// complement double flip, the negative abstraction of a <c><=n</c> filler, the
    /// <c>forall  [=</c> contrapositive, and union under negation. Each row's ground
    /// truth is derived in its comment; the fresh structural atoms are
    /// canonicalised (n0, n1) so the pin fixes the shape, not the id.
    /// </summary>
    [TestMethod]
    public void PolarityBattery()
    {
        (string Name, ReasoningModule Module, string[] Expected)[] rows =
        [
            //Nested complement double flip: A [= not not B ~> A  AND  not B [= Bottom, then not B [= n0 becomes Top [= B  OR  n0.
            //A negative-position complement lowers to a bottom clause; the inner not B abstracts negatively (n0),
            //and n0's definition re-enters as a positive disjunction.
            ("NestedComplementDoubleFlip", Module(SubClassOf(Class("A"), Complement(Complement(Class("B"))))),
                ["A(x), n0(x) ->", " -> B(x), n0(x)"]),

            //<=n filler negative abstraction (the classic false direction): A [= <=1 r.(B  AND  C). The <= filler is
            //NEGATIVE, so B  AND  C abstracts as B  AND  C [= n0 (n0 in a DL4 body), NOT n0 [= B  AND  C (mutation M1).
            ("MaxFillerNegativeAbstraction", Module(SubClassOf(Class("A"), Max("r", 1, Intersection(Class("B"), Class("C"))))),
                ["B(x), C(x) -> n0(x)", "n0(x), r(z1,x) -> cr0(z1,x)", "A(x), cr0(x,z1), cr0(x,z2) -> z1 = z2"]),

            //forall  [= contrapositive: (forall r.C) [= D == Top [= exists r.not C  OR  D. Fresh n0 (the exists  carrier) and n1 (its filler,
            //constrained by n1  AND  C [= Bottom); n0 abstracts the existential positively, n1 the negated filler.
            ("UniversalSubContrapositive", Module(SubClassOf(All("r", Class("C")), Class("D"))),
                [" -> D(x), n0(x)", "n0(x) -> r(x,f0(x))", "n0(x) -> n1(f0(x))", "C(x), n1(x) ->"]),

            //Union under negation: C [= not (A  OR  B) ~> C  AND  (A  OR  B) [= Bottom. The union abstracts negatively (A  OR  B [= n0),
            //so n0's definition splits as A [= n0 and B [= n0 -- a positive abstraction here would be unsound (M1).
            ("UnionUnderNegation", Module(SubClassOf(Class("C"), Complement(Union(Class("A"), Class("B"))))),
                ["C(x), n0(x) ->", "A(x) -> n0(x)", "B(x) -> n0(x)"]),

            //Existential-subclass filler over a SIMPLE role: exists r.(B  AND  C) [= D. The filler lands in the
            //DL3 clause BODY, so it is NEGATIVE and must abstract as B  AND  C [= n0. The positive direction
            //(n0 [= B  AND  C) leaves n0 unimplied and the whole axiom's content vacuously satisfiable --
            //hand model: r(a,b), B(b), C(b), not D(a) with n0 empty. Pins the negative filler-abstraction direction (M1 target).
            ("ExistentialSubComplexFiller", Module(SubClassOf(Some("r", Intersection(Class("B"), Class("C"))), Class("D"))),
                ["B(x), C(x) -> n0(x)", "n0(x), r(z1,x) -> D(z1)"]),
        ];

        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("polarity: row | verdict");
        foreach((string name, ReasoningModule module, string[] expected) in rows)
        {
            ClausificationResult result = ContextClausifier.Clausify(module);
            string? detail = CompareClauseSets(expected, result);
            report.AppendLine(CultureInfo.InvariantCulture, $"{name} | {(detail is null ? "OK" : "MISMATCH")}");
            if(detail is not null)
            {
                failures.Add($"{name}: {detail}");
            }
        }

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The role-automaton construction (HS2004 Definition 10 + chain elimination,
    /// KAZ2008 Lemma 10). Each row reconstructs the emitted automaton
    /// from the chain-elimination clauses (carrier atom seeds the initial states,
    /// filler atom marks the finals, transition clauses give the edges) and
    /// asserts the accepted / rejected role words that pin the shape -- including
    /// the two non-acceptance pins the corrected STEP 2 forces: no empty word and
    /// no composition without transitivity. The pure-symmetric (simple) case
    /// clausifies directly as DL3 and is checked as an exact clause set.
    /// </summary>
    [TestMethod]
    public void AutomatonBattery()
    {
        List<string> failures = [];

        //Transitivity loop: Tra(r), A [= forall r.B. The automaton is the epsilon-eliminated transitive closure;
        //it accepts r, r r, r r r (depth composition), rejects the empty word.
        {
            ReasoningModule module = Module(Transitive("r"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "Transitivity", failures,
                accept: [Word(("r", false)), Word(("r", false), ("r", false)), Word(("r", false), ("r", false), ("r", false))],
                reject: [Word(), Word(("r", true))]);
            CheckNoRoleHead(result, "Transitivity", failures);
        }

        //Pure symmetric role is SIMPLE: Sym(r), A [= forall r.B clausifies directly as DL3 (one r-hop) PLUS the
        //explicit symmetry clause r(z1,x) -> r(x,z1) -- the single-predicate encoding loses the inverse direction
        //without it (completeness witness: {Sym(r), A [= forall r.B, C [= exists r.A} entails C [= B and needs the
        //derived r(f,x) edge). It never propagates depth 2 (no composition without transitivity) and never emits
        //A(x) -> B(x) (no empty word).
        {
            ReasoningModule module = Module(Symmetric("r"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            string? detail = CompareClauseSets(["A(x), r(x,z1) -> B(z1)", "r(z1,x) -> r(x,z1)"], result);
            if(detail is not null)
            {
                failures.Add($"PureSymmetricIsDl3: {detail}");
            }
        }

        //Symmetric fold with a proper 2-chain (mutation M4 target): Sym(r), p o q [= r, A [= forall r.B. STEP 2 folds a
        //mirrored copy, so the language is closed under inversion: it accepts p q AND the reverse-inverse q^- p^-.
        //Skipping the mirror loses q^- p^-.
        {
            ReasoningModule module = Module(Symmetric("r"), Chain("r", "p", "q"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "SymmetricFoldMirror", failures,
                accept: [Word(("r", false)), Word(("p", false), ("q", false)), Word(("q", true), ("p", true))],
                reject: [Word(), Word(("p", false)), Word(("p", true), ("q", true))]);
        }

        //A 2-chain r o s [= t, A [= forall t.B: the automaton accepts the told letter t and the chain word r s.
        {
            ReasoningModule module = Module(Chain("t", "r", "s"), SubClassOf(Class("A"), All("t", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "TwoChain", failures,
                accept: [Word(("t", false)), Word(("r", false), ("s", false))],
                reject: [Word(), Word(("r", false)), Word(("s", false))]);
            CheckNoRoleHead(result, "TwoChain", failures);
        }

        //Shape 4 (R-prefix) r o s [= r, A [= forall r.B: the language is r s* (r then any number of s).
        {
            ReasoningModule module = Module(Chain("r", "r", "s"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "RPrefix", failures,
                accept: [Word(("r", false)), Word(("r", false), ("s", false)), Word(("r", false), ("s", false), ("s", false))],
                reject: [Word(), Word(("s", false))]);
        }

        //Shape 5 (R-suffix) s o r [= r, A [= forall r.B: the language is s* r (any number of s then r).
        {
            ReasoningModule module = Module(Chain("r", "s", "r"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "RSuffix", failures,
                accept: [Word(("r", false)), Word(("s", false), ("r", false)), Word(("s", false), ("s", false), ("r", false))],
                reject: [Word(), Word(("r", false), ("s", false))]);
        }

        //<-recursion inlining: Tra(s), s [= r, A [= forall r.B. r is non-simple through s, and B_s (s+) is inlined at
        //r's s-arc, so r's language is r | s+ -- the inlined s composes to depth 2 (s s).
        {
            ReasoningModule module = Module(Transitive("s"), SubProperty("s", "r"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "PrecRecursionInlining", failures,
                accept: [Word(("r", false)), Word(("s", false)), Word(("s", false), ("s", false))],
                reject: [Word(), Word(("r", false), ("r", false))]);
        }

        //B_{R^-} mirror: Tra(v), A [= forall v^-.B. The automaton for v^- is the mirror of v's, over the inverse letter,
        //so it accepts v^- and v^- v^- (transitive) but not the forward v.
        {
            ReasoningModule module = Module(Transitive("v"), SubClassOf(Class("A"), AllInverse("v", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "InverseMirror", failures,
                accept: [Word(("v", true)), Word(("v", true), ("v", true))],
                reject: [Word(), Word(("v", false))]);
        }

        //Symmetric + transitive + a simple sub-role: the language is the transitive closure of r plus the
        //single told sub-letter s.
        {
            ReasoningModule module = Module(Symmetric("r"), Transitive("r"), SubProperty("s", "r"), SubClassOf(Class("A"), All("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            CheckAutomatonAccepts(result, "A", "B", "SymmetricTransitiveSubRole", failures,
                accept: [Word(("r", false)), Word(("r", false), ("r", false)), Word(("s", false))],
                reject: [Word()]);
        }

        //Mutual inclusion with one transitive (roles quotiented by mutual-inclusion equivalence): r [= s, s [= r, Tra(s), A [= forall s.B. r and s collapse to
        //one class (canonical letter r); the automaton is finite, NOT a budget rejection.
        {
            ReasoningModule module = Module(SubProperty("r", "s"), SubProperty("s", "r"), Transitive("s"), SubClassOf(Class("A"), All("s", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            if(result.Remainder.Count > 0)
            {
                failures.Add("MutualInclusionTransitive: mutual inclusion with a transitive role is regular and finite, not rejected; remainder = " + string.Join("; ", result.Remainder));
            }
            else
            {
                CheckAutomatonAccepts(result, "A", "B", "MutualInclusionTransitive", failures,
                    accept: [Word(("r", false)), Word(("r", false), ("r", false))],
                    reject: [Word()]);
            }
        }

        //Chain-RIA deletion: a folded chain leaves NO DL5 role clause (mutation M6 target) -- covered by the
        //no-role-head checks on Transitivity and TwoChain above.

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// The state-budget rejection: a doubling tower of chain RIAs whose automata
    /// blow past the 4096-state budget refuses the whole module with the named
    /// remainder, never wedging on the exponential automaton blow-up HS2004 Lemma
    /// 11 proves unavoidable.
    /// </summary>
    [TestMethod]
    public void AutomatonStateBudgetRejection()
    {
        List<OwlAxiom> axioms = [];
        //s0 transitive; s_i is the super of s_{i-1}  o  s_{i-1}, so each B_{s_i} inlines two copies of B_{s_{i-1}} --
        //the state count doubles per level and crosses the budget well before level 13.
        axioms.Add(Transitive("s0"));
        for(int level = 1; level <= 13; level++)
        {
            axioms.Add(Chain($"s{level}", $"s{level - 1}", $"s{level - 1}"));
        }

        axioms.Add(SubClassOf(Class("A"), All("s13", Class("B"))));

        ClausificationResult result = ContextClausifier.Clausify(Module([.. axioms]));

        Assert.IsTrue(result.AutomatonBudgetExceeded, "The doubling tower must exceed the automaton state budget.");
        Assert.IsEmpty(result.Clauses, "A budget rejection is whole-module: no clause survives.");
        Assert.HasCount(1, result.Remainder, "A budget rejection reports the single named remainder.");
        Assert.AreEqual("RboxAutomaton(state-budget-exceeded)", result.Remainder[0], "The budget rejection is named verbatim.");
    }

    /// <summary>
    /// The RBox and simple-role guard rejections, each pinned by its verbatim
    /// remainder name. Covers the irregular counterexample of
    /// The Even More Irresistible SROIQ (KR 2006) and the
    /// strengthened-order cycle, the middle-super chain, number restrictions over a
    /// transitive role and its inverse, the symmetric-only-is-simple acceptance,
    /// and the expression-level rejections.
    /// </summary>
    [TestMethod]
    public void GuardRejectionBattery()
    {
        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("guards: row | verdict");

        //Irregular RBox -- the SROIQ2006 counterexample {R o S [= S, R o T [= R, V o T [= T, V o S [= V}: the induced order
        //has a cycle, so the whole module is refused.
        CheckRemainder(report, failures, "IrregularSroiq2006", Module(
            Chain("S", "R", "S"),
            Chain("R", "R", "T"),
            Chain("T", "V", "T"),
            Chain("V", "V", "S")), ["RboxIrregular(role-cycle)"]);

        //Strengthened-order cycle {a o a [= b, b [= a}: the chain forces a < b and the hierarchy forces b < a.
        CheckRemainder(report, failures, "StrengthenedOrderCycle", Module(
            Chain("b", "a", "a"),
            SubProperty("b", "a")), ["RboxIrregular(role-cycle)"]);

        //Middle-super chain s1  o  R  o  s2 [= R: the super occurs in the interior, which is inadmissible.
        CheckRemainder(report, failures, "MiddleSuperChain", Module(
            Chain("R", "s1", "R", "s2")), ["RboxIrregular(role-cycle)"]);

        //Number restriction over a transitive (non-simple) role: A [= <=1 r.B with Tra(r) is refused, named by r.
        CheckRemainder(report, failures, "MaxOverTransitive", Module(
            Transitive("r"),
            SubClassOf(Class("A"), Max("r", 1, Class("B")))), ["NonSimpleRoleInNumberRestriction(r)"]);

        //Simplicity propagates through inverses: r non-simple => r^- non-simple, so <=1 r^- is refused too.
        CheckRemainder(report, failures, "MaxOverTransitiveInverse", Module(
            Transitive("r"),
            SubClassOf(Class("A"), MaxInverse("r", 1, Class("B")))), ["NonSimpleRoleInNumberRestriction(r)"]);

        //GL5b min-side companion {Trans(r), A [= >=2 r.B}: a genuine (>= 2) number restriction over a
        //transitive (non-simple) role is refused by the StepMinSuper simplicity guard, named by r -- the
        //min-side twin of MaxOverTransitive and the two-guard split's min face (simplicity fires before any
        //loop-capability question).
        CheckRemainder(report, failures, "MinTwoOverTransitive", Module(
            Transitive("r"),
            SubClassOf(Class("A"), Min("r", 2, Class("B")))), ["NonSimpleRoleInNumberRestriction(r)"]);

        //Symmetric-only role IS simple: r^- [= r alone plus <=1 r is ACCEPTED -- no rejection.
        {
            ReasoningModule module = Module(Symmetric("r"), SubClassOf(Class("A"), Max("r", 1, Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            bool ok = result.Remainder.Count == 0 && result.FreshRoles == 1;
            report.AppendLine(CultureInfo.InvariantCulture, $"SymmetricOnlyIsSimple | {(ok ? "OK" : "MISMATCH")}");
            if(!ok)
            {
                failures.Add($"SymmetricOnlyIsSimple: remainder=[{string.Join(", ", result.Remainder)}] freshRoles={result.FreshRoles}");
            }
        }

        //Non-simple Self restriction {Trans(r), A [= exists r.Self}: exists S.Self requires S simple
        //(KR2006 Definition 5); Trans(r) makes r non-simple, so the Self lowering emits the named remainder
        //and no producer clause. The HasSelf rejection row the guard family keeps, since superclass HasSelf
        //over a SIMPLE role lowers to a Self producer.
        CheckRemainder(report, failures, "NonSimpleSelfRestriction", Module(
            Transitive("r"),
            SubClassOf(Class("A"), new OwlObjectHasSelf(Property("r")))),
            ["NonSimpleRoleInSelfRestriction(r)"]);

        //The four battery delegated rows whose whole-module or per-role remainder the clausifier
        //names, pinned here at the clausifier level beside the NonSimpleSelfRestriction pin above (the X2
        //sibling): X3's non-simple irreflexivity, the RR-cyclic4 and RRinv irregular RBoxes, and the
        //automaton-state-budget tower. The soundness battery asserts only that each delegates; these pins fix
        //the remainder string that drives that delegation.

        //X3 {Trans(r), Irr(r)}: irreflexivity requires r simple (KR2006); Trans(r) makes r non-simple, so the
        //irreflexive lowering names the non-simple role and emits no bottom clause.
        CheckRemainder(report, failures, "NonSimpleIrreflexivity", Module(
            Transitive("r"),
            Irreflexive("r")),
            ["NonSimpleRoleInIrreflexivity(r)"]);

        //RR-cyclic4 {p o q [= q, q o r [= r, r o s [= s, s o p [= p}: each RIA forces its leading letter below
        //its head, so p < q < r < s < p is a cycle in the regularity order and the whole module is refused.
        CheckRemainder(report, failures, "RRcyclic4Irregular", Module(
            Chain("q", "p", "q"),
            Chain("r", "q", "r"),
            Chain("s", "r", "s"),
            Chain("p", "s", "p")),
            ["RboxIrregular(role-cycle)"]);

        //RRinv {R o R^- [= R}: the KR2006 p.2 irregular chain -- the super R occurs as its own inverse tail,
        //matching no admissible Def-2 shape, so the regularity guard refuses the whole module under the shared
        //RboxIrregular remainder (the umbrella name every regularity rejection carries, cycle or not, as
        //MiddleSuperChain above shows).
        OwlObjectPropertyExpression[] rrinvLinks = [Property("R"), InverseProperty("R")];
        OwlPropertyChainAxiom rrinv = new(rrinvLinks, Property("R")) { Origin = AxiomOrigin };
        CheckRemainder(report, failures, "RRinvIrregular", Module(rrinv),
            ["RboxIrregular(role-cycle)"]);

        //BC1 {R o Q [= P, Inv(P,Q)}: Inv(P,Q) makes Q == P-, so the interior chain letter Q shares P's base
        //after the told-cycle quotient and the regularity interior check (base(letter) == base(super)) refuses
        //the whole module. The quotient-MEDIATED companion to RRinvIrregular's syntactic R o R- [= R.
        OwlInverseObjectPropertiesAxiom bc1Inverse = new(Property("P"), Property("Q")) { Origin = AxiomOrigin };
        CheckRemainder(report, failures, "BC1ChainInverseQuotientIrregular", Module(
            Chain("P", "R", "Q"),
            bc1Inverse),
            ["RboxIrregular(role-cycle)"]);

        //Automaton-state-budget tower {s0 transitive; s_i the super of s_{i-1} o s_{i-1} (doubling levels);
        //A [= forall s13.B}: the product automaton for the top role exceeds the 4096-state budget, so the whole
        //module is refused with the automaton-budget remainder.
        List<OwlAxiom> budgetTower = [Transitive("s0")];
        for(int level = 1; level <= 13; level++)
        {
            budgetTower.Add(Chain($"s{level}", $"s{level - 1}", $"s{level - 1}"));
        }

        budgetTower.Add(SubClassOf(Class("A"), All("s13", Class("B"))));
        CheckRemainder(report, failures, "AutomatonStateBudgetTower", Module([.. budgetTower]),
            ["RboxAutomaton(state-budget-exceeded)"]);

        //A subclass-position enumeration clausifies CLEAN since the nominal lowering
        //(the nominal DL7 face: {a} [= A emits the ground fact T -> A(a)); the survey
        //stays the admission gate, so the production path is unchanged until it opens.
        CheckRemainder(report, failures, "OneOfSubclass", Module(
            SubClassOf(OneOf("a"), Class("A"))),
            []);

        //An anonymous individual in a NOMINAL position delegates the module whole:
        //a blank node is existential, and constant-treating it is a skolemization the
        //nominal fragment does not argue.
        CheckRemainder(report, failures, "AnonymousInNominal", Module(
            SubClassOf(Class("A"), new OwlObjectOneOf([new BlankNode(Utf8Strings.From("b1"))]))),
            ["AnonymousIndividualInNominal"]);

        //A HasKey axiom co-occurring with a nominal construct trips the key-on-nominal
        //guard under the DARK face (key join off): the pre-lift baseline the lit
        //production default lifts. The row drives the dark face explicitly to keep the
        //guard's remainder under test; the lit routing past this guard is pinned by
        //ContextCooccurrenceLiftTests JUR-1.
        CheckRemainder(report, failures, "KeyOnNominal", Module(
            new OwlHasKeyAxiom(Class("A"), [Property("r")], []) { Origin = AxiomOrigin },
            SubClassOf(Class("B"), OneOf("a"))),
            ["KeyOnNominalModule"], rootKeyJoinEnabled: false);

        //A negative object-property assertion is a ground closure obligation, not a remainder, and carries
        //no simple-role guard: over a transitive role with no matching asserted edge the closure entails
        //nothing, so the module clausifies clean.
        CheckRemainder(report, failures, "NegativeObjectPropertyAssertionIsClosureObligation", Module(
            Transitive("r"),
            NegativeEdge("a", "r", "b")),
            []);

        //A well-formed HasKey clausifies CLEAN: the ground key join owns the axiom
        //(one descriptor per axiom, no clause, no remainder) and the module survey
        //is the admission gate. The degenerate shapes keep their named defensive
        //remainders (empty key list; non-atomic keyed class), pinned in
        //ContextHasKeyGroundEnginePinTests.
        CheckRemainder(report, failures, "HasKeyOwned", Module(
            new OwlHasKeyAxiom(Class("A"), [Property("r")], []) { Origin = AxiomOrigin }),
            []);

        //The loops x counting guard: a counted role that can carry a loop cannot express the
        //owner-successor diagonal a functional merge forces, so the module delegates with the loop-capable name.
        //GL1 {A [= exists r.Self, A [= exists r.B, Func(r)}: r is a loop base via the Self restriction and a
        //counting target via Func; the post-closure check names r.
        CheckRemainder(report, failures, "GL1SelfCounting", Module(
            SubClassOf(Class("A"), new OwlObjectHasSelf(Property("r"))),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            Functional("r")), ["LoopCapableRoleInNumberRestriction(r)"]);

        //GL2 {Ref(r), Func(r), A [= exists r.B}: Ref seeds r into the loop set; Func makes it a counting target.
        CheckRemainder(report, failures, "GL2ReflexiveCounting", Module(
            Reflexive("r"),
            Functional("r"),
            SubClassOf(Class("A"), Some("r", Class("B")))), ["LoopCapableRoleInNumberRestriction(r)"]);

        //GL3 {s [= r, A [= exists s.Self, Func(r), A [= exists r.B}: the s-loop promotes to r through the upward
        //closure (r reaches the loop set only via CloseLoopSet, never the seed), and Func makes r a counting
        //target -- the upward-closure face of the guard.
        CheckRemainder(report, failures, "GL3UpwardClosureCounting", Module(
            SubProperty("s", "r"),
            SubClassOf(Class("A"), new OwlObjectHasSelf(Property("s"))),
            Functional("r"),
            SubClassOf(Class("A"), Some("r", Class("B")))), ["LoopCapableRoleInNumberRestriction(r)"]);

        //GL4 {Irr(r), Func(r), A [= exists r.B}: the guard over-approximates -- Irr seeds r into the loop set
        //even though it FORBIDS a loop, so the module delegates conservatively (the deliberate over-delegation
        //face).
        CheckRemainder(report, failures, "GL4IrreflexiveCounting", Module(
            Irreflexive("r"),
            Functional("r"),
            SubClassOf(Class("A"), Some("r", Class("B")))), ["LoopCapableRoleInNumberRestriction(r)"]);

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The F3.1 router's lifted arm under nominal jurisdiction — the Wine/Food
    /// premise mechanism (WebOnt-miscellaneous-001/-002): a module carrying a
    /// nominal enumeration and a data property asserted with a domain
    /// co-occurrence (a LIFTED position) is admitted whole, takes nominal
    /// jurisdiction, and lowers the told assertion on the root tier — a fresh
    /// atom GCI plus a root fact carrying the demand to the root individual.
    /// </summary>
    [TestMethod]
    public void LiftedDataBeltAdmitsNominalJurisdictionModule()
    {
        Literal five = new(Utf8Strings.From("5"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
        ReasoningModule module = Module(
            SubClassOf(Class("B"), OneOf("a")),
            new OwlDataPropertyDomainAxiom(Individual("d"), Class("Host")) { Origin = AxiomOrigin },
            new OwlDataPropertyAssertionAxiom(Individual("x"), Individual("d"), five) { Origin = AxiomOrigin });

        ClausificationResult result = ContextClausifier.Clausify(module);

        Assert.IsEmpty(result.Remainder, "The lifted domain co-occurrence admits the module whole.");
        Assert.IsTrue(result.NominalJurisdiction, "The named oneOf takes jurisdiction once the router admits the module.");
        Assert.IsNotEmpty(result.RootFacts, "The told assertion lowers on the root tier.");
        Assert.IsNotEmpty(result.DataDemandDescriptors, "The lowering mints the value-forcing demand.");
    }

    /// <summary>
    /// The key-data belt's KEPT arm precedes the nominal-jurisdiction scan: a
    /// module carrying both a nominal enumeration and a data property asserted
    /// with a disjointness co-occurrence (a KEPT position) clausifies to the
    /// belt's AssertedDataPropertyBeyondKeys remainder -- never the
    /// KeyOnNominalModule guard, never a clean admission -- with
    /// NominalJurisdiction false, because the belt runs at Run() before
    /// ScanNominalJurisdiction.
    /// </summary>
    [TestMethod]
    public void KeptDataBeltPrecedesNominalJurisdictionScan()
    {
        Literal five = new(Utf8Strings.From("5"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
        ReasoningModule module = Module(
            SubClassOf(Class("B"), OneOf("a")),
            new OwlDisjointDataPropertiesAxiom([Individual("d"), Individual("e")]) { Origin = AxiomOrigin },
            new OwlDataPropertyAssertionAxiom(Individual("x"), Individual("d"), five) { Origin = AxiomOrigin });

        ClausificationResult result = ContextClausifier.Clausify(module);

        Assert.HasCount(1, result.Remainder, "The belt rejects the whole module with a single named remainder.");
        Assert.AreEqual("AssertedDataPropertyBeyondKeys(d)", result.Remainder[0], "The belt names the asserted data property, not the nominal guard.");
        Assert.IsEmpty(result.Clauses, "A belt rejection is whole-module: no clause survives.");
        Assert.IsFalse(result.NominalJurisdiction, "The belt runs before the nominal scan, so the module never takes nominal jurisdiction.");
    }

    /// <summary>
    /// The Asymmetric + DisjointObjectProperties structural pins
    /// cover the pairwise disjoint-role clash-clause shape, the
    /// same-representative dedupe to the single-literal emptiness clause, the
    /// derived-irreflexivity self-variant, and the two provenance-split reserved and
    /// non-simple remainder names. Each pin observes the clausifier boundary where
    /// the lowering is real, so a saturation or second-gate regression is caught
    /// here rather than through a verdict-blind fallback.
    /// </summary>
    [TestMethod]
    public void RoleDisjointnessClauseFamily()
    {
        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("role-disjointness pins: row | verdict");

        //Pin-1 clash-shape {Dis(r,s)}: distinct reps => EXACTLY the canonical two-role-literal empty-head clash
        //clause r(z1,x) AND s(z1,x) -> bot and nothing else new (the section 1.2 boundary).
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(DisjointProperties("r", "s")));
            string? detail = CompareClauseSets(["r(z1,x), s(z1,x) ->"], result);
            report.AppendLine(CultureInfo.InvariantCulture, $"Pin1ClashShape | {(detail is null ? "OK" : "MISMATCH")}");
            if(detail is not null)
            {
                failures.Add($"Pin1ClashShape: {detail}");
            }
        }

        //Pin-2 dedupe {Sym(r), Asy(r)}: r symmetric => self-inverse => Rep(r-)=Rep(r) => the two Asy literals
        //dedupe to the single-literal emptiness clause r(z1,x) -> bot. CONTAINS, not exact set: Sym(r)
        //co-emits its symmetry clause r(z1,x) -> r(x,z1).
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(Symmetric("r"), Asymmetric("r")));
            List<string> clauses = CanonicalClauses(result);
            bool ok = clauses.Contains("r(z1,x) ->");
            report.AppendLine(CultureInfo.InvariantCulture, $"Pin2Dedupe | {(ok ? "OK" : "MISMATCH")}");
            if(!ok)
            {
                failures.Add($"Pin2Dedupe: expected clause set to contain 'r(z1,x) ->', got {{{string.Join(" | ", clauses)}}}");
            }
        }

        //Pin-3 variant {Asy(r), A [= exists r.Self}: A [= exists r.Self mints Self_{base(r)} (canonical n0) and
        //seeds base(r) in L, so the collapsed Asy diagonal is emitted as the single-literal loop clash n0(x) ->
        //bot alongside the loop producer A(x) -> n0(x) (n0 the SAME fresh atom).
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(Asymmetric("r"), SubClassOf(Class("A"), new OwlObjectHasSelf(Property("r")))));
            List<string> clauses = CanonicalClauses(result);
            bool ok = clauses.Contains("n0(x) ->") && clauses.Contains("A(x) -> n0(x)");
            report.AppendLine(CultureInfo.InvariantCulture, $"Pin3DerivedIrreflexivity | {(ok ? "OK" : "MISMATCH")}");
            if(!ok)
            {
                failures.Add($"Pin3DerivedIrreflexivity: expected 'n0(x) ->' and 'A(x) -> n0(x)', got {{{string.Join(" | ", clauses)}}}");
            }
        }

        //Pin-4 remainder {Trans(r), Asy(r)}: Trans(r) makes r non-simple, so the asymmetric guard names the
        //non-simple role in the remainder -- the EXACT string the soundness-battery tuple cannot observe (MU6).
        CheckRemainder(report, failures, "Pin4NonSimpleAsymmetry", Module(
            Transitive("r"),
            Asymmetric("r")),
            ["NonSimpleRoleInAsymmetry(r)"]);

        //Pin-5 reserved-Asy {Asy(owl:topObjectProperty)} (the G5 reshaping, SI-1): the top operand trips the
        //soundness-forced reserved guard on the asymmetric construct, provenance-split to the asymmetric role's
        //IRI, without routing through the verdict-blind fallback.
        CheckRemainder(report, failures, "Pin5ReservedAsymmetry", Module(
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty))) { Origin = AxiomOrigin }),
            ["ReservedRoleInAsymmetry(http://www.w3.org/2002/07/owl#topObjectProperty)"]);

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The module-level reserved-role scan pins: every provenance
    /// name the pre-intake scan mints for an <c>owl:topObjectProperty</c> /
    /// <c>owl:bottomObjectProperty</c> mention, each single-edit observable. P1 the
    /// sub-property-of role hierarchy, P3 the multi-hit characteristic-plus-hierarchy
    /// dedupe, P5 the domain property, P6 the range property, P7 a property-chain
    /// link, and P8 a class-expression role position each reject with their named
    /// remainder and emit NO clause (the pre-intake early return, mutation MU8's
    /// clauses-EMPTY kill); P4 and P1a are the two bottom carve-outs at the clausifier
    /// boundary -- a bottom DisjointObjectProperties operand and a bottom Asymmetric
    /// property -- which stay admitted (empty remainder) and emit the ordinary
    /// emptiness clash. P2, the <c>{Asy(top)}</c> pin, is
    /// RoleDisjointnessClauseFamily's Pin5ReservedAsymmetry.
    /// </summary>
    [TestMethod]
    public void ReservedRoleScanFamily()
    {
        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("reserved-role scan pins: row | verdict");

        //P1 {top [= r}: the reserved top in a sub-property SUB position mints the role-hierarchy remainder, and
        //the scan early-returns before intake, so no clause survives (the MU8 clauses-EMPTY kill).
        CheckReservedRejection(report, failures, "P1RoleHierarchyTop", Module(
            new OwlSubObjectPropertyOfAxiom(TopProperty(), Property("r")) { Origin = AxiomOrigin }),
            ["ReservedRoleInRoleHierarchy(http://www.w3.org/2002/07/owl#topObjectProperty)"]);

        //P3 {Irr(top), s [= bottom}: two hits in axiom order -- the top irreflexive characteristic and the
        //bottom super-property -- deduplicated per name, no clause emitted (the MU8 clauses-EMPTY kill).
        CheckReservedRejection(report, failures, "P3MultiHit", Module(
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, TopProperty()) { Origin = AxiomOrigin },
            new OwlSubObjectPropertyOfAxiom(Property("s"), BottomProperty()) { Origin = AxiomOrigin }),
            ["ReservedRoleInCharacteristic(http://www.w3.org/2002/07/owl#topObjectProperty)", "ReservedRoleInRoleHierarchy(http://www.w3.org/2002/07/owl#bottomObjectProperty)"]);

        //P4 {Dis(r,bottom), A [= exists r.B}: the bottom DisjointObjectProperties operand is a scan carve-out --
        //the scan skips it, so the module stays admitted with an EMPTY remainder and EmitRoleDisjointness emits
        //the ordinary two-literal clash, the bottom role interned as an ordinary symbol and rendered as its full
        //IRI. A mutation that scanned the bottom operand flips the remainder non-empty and drops the clash.
        {
            ReasoningModule module = Module(
                new OwlDisjointObjectPropertiesAxiom([Property("r"), BottomProperty()]) { Origin = AxiomOrigin },
                SubClassOf(Class("A"), Some("r", Class("B"))));
            ClausificationResult result = ContextClausifier.Clausify(module);
            string bottomRole = OwlVocabulary.BottomObjectProperty.ToString();
            string[] clashLiterals = [$"{bottomRole}(z1,x)", "r(z1,x)"];
            Array.Sort(clashLiterals, StringComparer.Ordinal);
            string expectedClash = string.Join(", ", clashLiterals) + " ->";
            List<string> clauses = CanonicalClauses(result);
            bool ok = result.Remainder.Count == 0 && clauses.Contains(expectedClash);
            report.AppendLine(CultureInfo.InvariantCulture, $"P4BottomDisjointCarveOut | {(ok ? "OK" : "MISMATCH")}");
            if(!ok)
            {
                failures.Add($"P4BottomDisjointCarveOut: expected empty remainder and clash '{expectedClash}', got remainder [{string.Join(", ", result.Remainder)}] clauses {{{string.Join(" | ", clauses)}}}");
            }
        }

        //P5 {Domain(top,A)}: the reserved top in a domain axiom's PROPERTY position mints the dedicated domain
        //remainder (the domain filler A carries no role position).
        CheckRemainder(report, failures, "P5DomainTop", Module(
            new OwlObjectPropertyDomainAxiom(TopProperty(), Class("A")) { Origin = AxiomOrigin }),
            ["ReservedRoleInDomain(http://www.w3.org/2002/07/owl#topObjectProperty)"]);

        //P6 {Range(top,A)}: the reserved top in a range axiom's PROPERTY position mints the dedicated range
        //remainder.
        CheckRemainder(report, failures, "P6RangeTop", Module(
            new OwlObjectPropertyRangeAxiom(TopProperty(), Class("A")) { Origin = AxiomOrigin }),
            ["ReservedRoleInRange(http://www.w3.org/2002/07/owl#topObjectProperty)"]);

        //P7 {r o bottom [= s}: the reserved bottom in a property-chain LINK mints the chain remainder.
        OwlObjectPropertyExpression[] chainLinks = [Property("r"), BottomProperty()];
        CheckRemainder(report, failures, "P7ChainBottom", Module(
            new OwlPropertyChainAxiom(chainLinks, Property("s")) { Origin = AxiomOrigin }),
            ["ReservedRoleInPropertyChain(http://www.w3.org/2002/07/owl#bottomObjectProperty)"]);

        //P8 {A [= exists bottom.B}: the reserved bottom in a class-expression role position (an existential
        //restriction) mints the class-expression remainder, walked from the SubClassOf superclass.
        CheckRemainder(report, failures, "P8ClassExpressionBottom", Module(
            SubClassOf(Class("A"), new OwlObjectSomeValuesFrom(BottomProperty(), Class("B")))),
            ["ReservedRoleInClassExpression(http://www.w3.org/2002/07/owl#bottomObjectProperty)"]);

        //P1a {Asy(bottom), A [= exists r.B}: the bottom Asymmetric property is the same scan carve-out's asymmetric
        //side (complements P4) -- the scan skips it, so the module stays admitted with an EMPTY remainder.
        CheckRemainder(report, failures, "P1aBottomAsymmetryCarveOut", Module(
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, BottomProperty()) { Origin = AxiomOrigin },
            SubClassOf(Class("A"), Some("r", Class("B")))),
            []);

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The non-simple-body conversion: exists v.C [= D over a
    /// non-simple v is not a DL3 over the forward v -- it converts to C [= forall v^-.D, so
    /// v occurs only in the automaton's propagation. The emitted set is the
    /// v^--automaton (carrier C, filler D), never the direct clause C(x)  AND  v(x, z1)
    /// -> D(z1).
    /// </summary>
    [TestMethod]
    public void ExistentialSubOverNonSimpleConverts()
    {
        ReasoningModule module = Module(Transitive("v"), SubClassOf(Some("v", Class("C")), Class("D")));
        ClausificationResult result = ContextClausifier.Clausify(module);
        List<string> failures = [];

        Assert.IsEmpty(result.Remainder, "The conversion is not a rejection.");
        CheckAutomatonAccepts(result, "C", "D", "ExistentialSubConversion", failures,
            accept: [Word(("v", true)), Word(("v", true), ("v", true))],
            reject: [Word(), Word(("v", false))]);

        foreach(string clause in CanonicalClauses(result))
        {
            if(clause == "C(x), v(x,z1) -> D(z1)")
            {
                failures.Add("The non-simple existential wrongly emitted a forward DL3 clause instead of converting.");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// Deliverable D and mutation M8: a hand-built module with a fixed expected
    /// full-construct census, discriminating <c>ObjectComplementOf(sub)</c> from
    /// <c>(super)</c>, <c>ObjectCardinality(Max,1)</c> from <c>(Max,n&gt;=2)</c>,
    /// and <c>ObjectUnionOf</c> by polarity. Every polarity-split pair carries
    /// ASYMMETRIC counts (2 vs 1) so both one-directional confusion AND a
    /// symmetric two-way key swap change the table — a one-of-each table is
    /// invariant under the swap and cannot see it. The
    /// <c>ObjectPropertyCharacteristic(Functional)</c> and
    /// <c>(InverseFunctional)</c> rows pin the two equality-tier characteristic keys
    /// distinctly, so a Functional/InverseFunctional key
    /// confusion is caught.
    /// </summary>
    [TestMethod]
    public void CensusFixedTablePin()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Complement(Class("B"))),
            SubClassOf(Complement(Class("C")), Class("D")),
            SubClassOf(Complement(Class("O")), Class("P")),
            SubClassOf(Class("E"), Max("r", 1, Class("F"))),
            SubClassOf(Class("G"), Max("r", 2, Class("H"))),
            SubClassOf(Class("T"), Max("r", 3, Class("U"))),
            SubClassOf(Class("I"), Union(Class("J"), Class("K"))),
            SubClassOf(Class("Q"), Union(Class("R"), Class("S"))),
            SubClassOf(Union(Class("L"), Class("M")), Class("N")),
            Functional("r"),
            InverseFunctional("r"));

        (string Key, int Count)[] expected =
        [
            ("SubClassOf", 9),
            ("ObjectCardinality(Max,n>=2)", 2),
            ("ObjectComplementOf(sub)", 2),
            ("ObjectUnionOf(super)", 2),
            ("ObjectCardinality(Max,1)", 1),
            ("ObjectComplementOf(super)", 1),
            ("ObjectPropertyCharacteristic(Functional)", 1),
            ("ObjectPropertyCharacteristic(InverseFunctional)", 1),
            ("ObjectUnionOf(sub)", 1),
        ];

        IReadOnlyList<(string Key, int Count)> actual = OwlConstructCensus.Count(module);
        List<string> failures = [];

        if(actual.Count != expected.Length)
        {
            failures.Add($"census size: expected {expected.Length}, got {actual.Count} ({string.Join(", ", RenderCensus(actual))})");
        }
        else
        {
            for(int i = 0; i < expected.Length; i++)
            {
                if(actual[i].Key != expected[i].Key || actual[i].Count != expected[i].Count)
                {
                    failures.Add($"census row {i}: expected {expected[i].Count} x {expected[i].Key}, got {actual[i].Count} x {actual[i].Key}");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// The delegation-path reference identity. The engine is no longer inert: the
    /// survey admits the Horn-ALCHI slice with the object-side ground ABox, so an
    /// empty module admits, a class assertion over an admitted superclass shape
    /// admits, and — since the nominal tier — a plain one-of enumeration admits
    /// too. For a survey-rejected module the coupled delegate is a pure
    /// passthrough: it returns the fallback oracle's own <see cref="ModuleDecision"/>
    /// reference-identical, without re-wrapping. At the production default the
    /// key-on-nominal shape now ADMITS too — the lit key join routes a
    /// <c>HasKey</c> axiom beside a one-of enumeration past the co-occurrence
    /// guard — so the pin drives an ANONYMOUS-individual-in-nominal module, whose
    /// anonymous-in-nominal guard stays governing, as the construct the survey
    /// still rejects, keeping the delegation passthrough under test.
    /// </summary>
    [TestMethod]
    public async Task InertShellPassthrough()
    {
        ReasoningModule aboxModule = Module(
            SubClassOf(Class("Car"), Class("Vehicle")),
            ClassAssertion(Class("Car"), Individual("x")));
        ReasoningModule emptyModule = Module();
        ReasoningModule nominalModule = Module(SubClassOf(Class("A"), OneOf("a")));
        ReasoningModule keyOnNominalModule = Module(
            new OwlHasKeyAxiom(Class("K"), [Property("r")], []) { Origin = AxiomOrigin },
            SubClassOf(Class("A"), OneOf("a")));
        ReasoningModule anonymousNominalModule = Module(
            SubClassOf(Class("A"), new OwlObjectOneOf([new BlankNode(Utf8Strings.From("b1"))])));

        Assert.IsTrue(ContextModuleSurvey.Survey(aboxModule).Admitted, "The Horn-ALCHI slice admits the object-side ground ABox, so a class assertion over an admitted named superclass admits.");
        Assert.IsTrue(ContextModuleSurvey.Survey(emptyModule).Admitted, "The Horn-ALCHI slice admits a module with no beyond-slice axiom, so an empty module admits vacuously.");
        Assert.IsTrue(ContextModuleSurvey.Survey(nominalModule).Admitted, "A one-of enumeration is in-slice under the nominal tier, so the plain nominal module admits.");
        Assert.IsTrue(ContextModuleSurvey.Survey(keyOnNominalModule).Admitted, "A HasKey axiom beside a nominal is admitted at the production default — the lit key join routes it past the co-occurrence guard into intake.");
        Assert.IsFalse(ContextModuleSurvey.Survey(anonymousNominalModule).Admitted, "An anonymous individual in a nominal position trips the anonymous-in-nominal guard, which stays governing, so the module delegates whole.");

        ModuleDecision canned = AlcModuleReasoner.DecideModule(aboxModule, TestContext.CancellationToken);
        int fallbackCalls = 0;
        DescriptionLogicDelegate spy = (module, cancellationToken) =>
        {
            fallbackCalls++;

            return new ValueTask<ModuleDecision>(canned);
        };

        DescriptionLogicDelegate contextDelegate = ReasoningEngines.ContextSaturation(spy);
        ModuleDecision result = await contextDelegate(anonymousNominalModule, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, fallbackCalls, "The delegation path hands a survey-rejected module to the fallback exactly once.");
        Assert.AreSame(canned, result, "The delegation path returns the fallback's decision object unchanged and reference-identical.");
    }

    /// <summary>
    /// The equality-tier survey admissions: the Functional and InverseFunctional
    /// characteristics, max-, exact-, and min-cardinality of EVERY bound
    /// (qualified and unqualified) at EITHER polarity ADMIT — the disjunctive
    /// widening lowers a bound above one to the DL4 pairwise merge head and a
    /// negative-position restriction through the contrapositive duals into a
    /// positive union. Each row asserts <see cref="ContextModuleSurvey.Survey"/>
    /// directly -- the survey boundary the second gate then names -- so a survey
    /// widening or narrowing regression is caught here independently of the
    /// clausifier.
    /// </summary>
    [TestMethod]
    public void SurveyEqualityTierAdmissionPins()
    {
        (string Name, ReasoningModule Module, bool Admits)[] rows =
        [
            ("Functional", Module(Functional("r")), true),
            ("InverseFunctional", Module(InverseFunctional("r")), true),
            ("MaxOneQualified", Module(SubClassOf(Class("A"), Max("r", 1, Class("B")))), true),
            ("MaxOneUnqualified", Module(SubClassOf(Class("A"), new OwlObjectCardinality(OwlCardinalityKind.Max, 1, Property("r"), null))), true),
            ("ExactOneQualified", Module(SubClassOf(Class("A"), Exact("r", 1, Class("B")))), true),
            ("MinTwoQualified", Module(SubClassOf(Class("A"), Min("r", 2, Class("B")))), true),
            ("MaxTwoAdmits", Module(SubClassOf(Class("A"), Max("r", 2, Class("B")))), true),
            ("ExactTwoAdmits", Module(SubClassOf(Class("A"), Exact("r", 2, Class("B")))), true),
            ("NegativeMaxOneAdmits", Module(SubClassOf(Max("r", 1, Class("B")), Class("A"))), true),
        ];

        List<string> failures = [];
        foreach((string name, ReasoningModule module, bool admits) in rows)
        {
            bool actual = ContextModuleSurvey.Survey(module).Admitted;
            if(actual != admits)
            {
                failures.Add($"{name}: expected Admitted={admits}, got {actual}");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// The ground-key-tier survey admissions: a
    /// <c>HasKey</c> axiom admits when its keyed class is a named class
    /// (<c>owl:Thing</c> included), its key list is non-empty, its object key
    /// properties are named or inverse, and its data key properties are named;
    /// the degenerate shapes — the empty key list and the non-atomic keyed
    /// class — reject so the whole module delegates. A positive data-property
    /// assertion admits over a named data property and a non-literal subject; a
    /// reserved data property, a literal subject, and every negative
    /// data-property assertion reject. Each row asserts
    /// <see cref="ContextModuleSurvey.Survey"/> directly, so an admission-grammar
    /// regression is caught here independently of the clausifier's defensive
    /// remainders.
    /// </summary>
    [TestMethod]
    public void SurveyKeyTierAdmissionPins()
    {
        OwlClassReference thing = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));
        Literal five = new(Utf8Strings.From("5"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
        (string Name, ReasoningModule Module, bool Admits)[] rows =
        [
            ("HasKeyNamedClassObjectKeyAdmits", Module(new OwlHasKeyAxiom(Class("A"), [Property("r")], []) { Origin = AxiomOrigin }), true),
            ("HasKeyThingDataKeyAdmits", Module(new OwlHasKeyAxiom(thing, [], [Individual("d")]) { Origin = AxiomOrigin }), true),
            ("HasKeyInverseObjectKeyAdmits", Module(new OwlHasKeyAxiom(Class("A"), [InverseProperty("r")], []) { Origin = AxiomOrigin }), true),
            ("HasKeyCompositeKeyAdmits", Module(new OwlHasKeyAxiom(Class("A"), [Property("r")], [Individual("d")]) { Origin = AxiomOrigin }), true),
            ("HasKeyEmptyKeyListRejects", Module(new OwlHasKeyAxiom(Class("A"), [], []) { Origin = AxiomOrigin }), false),
            ("HasKeyUnionKeyedClassRejects", Module(new OwlHasKeyAxiom(Union(Class("A"), Class("B")), [Property("r")], []) { Origin = AxiomOrigin }), false),
            ("HasKeyOneOfKeyedClassRejects", Module(new OwlHasKeyAxiom(OneOf("a"), [Property("r")], []) { Origin = AxiomOrigin }), false),
            ("HasKeyReservedDataKeyRejects", Module(new OwlHasKeyAxiom(Class("A"), [], [new NamedNode(OwlVocabulary.TopDataProperty)]) { Origin = AxiomOrigin }), false),
            ("DataAssertionNamedSubjectAdmits", Module(new OwlDataPropertyAssertionAxiom(Individual("x"), Individual("d"), five) { Origin = AxiomOrigin }), true),
            ("DataAssertionBnodeSubjectAdmits", Module(new OwlDataPropertyAssertionAxiom(new BlankNode(Utf8Strings.From("b1")), Individual("d"), five) { Origin = AxiomOrigin }), true),
            ("DataAssertionReservedPropertyRejects", Module(new OwlDataPropertyAssertionAxiom(Individual("x"), new NamedNode(OwlVocabulary.TopDataProperty), five) { Origin = AxiomOrigin }), false),
            ("DataAssertionLiteralSubjectRejects", Module(new OwlDataPropertyAssertionAxiom(five, Individual("d"), five) { Origin = AxiomOrigin }), false),
            ("NegativeDataAssertionRejects", Module(new OwlNegativeDataPropertyAssertionAxiom(Individual("x"), Individual("d"), five) { Origin = AxiomOrigin }), false),
        ];

        List<string> failures = [];
        foreach((string name, ReasoningModule module, bool admits) in rows)
        {
            bool actual = ContextModuleSurvey.Survey(module).Admitted;
            if(actual != admits)
            {
                failures.Add($"{name}: expected Admitted={admits}, got {actual}");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>Compares an expected canonical clause set against a clausification result, returning a diff description or <see langword="null"/> on a match.</summary>
    /// <param name="expected">The expected canonical clause strings.</param>
    /// <param name="result">The clausification result.</param>
    /// <returns>A mismatch description, or <see langword="null"/> when the sets match.</returns>
    private static string? CompareClauseSets(string[] expected, ClausificationResult result)
    {
        List<string> actual = CanonicalClauses(result);
        List<string> want = [.. expected];
        want.Sort(StringComparer.Ordinal);

        if(actual.Count == want.Count)
        {
            bool same = true;
            for(int i = 0; i < actual.Count; i++)
            {
                if(!string.Equals(actual[i], want[i], StringComparison.Ordinal))
                {
                    same = false;

                    break;
                }
            }

            if(same)
            {
                return null;
            }
        }

        return $"expected {{{string.Join(" | ", want)}}}, got {{{string.Join(" | ", actual)}}}";
    }

    /// <summary>Renders and canonicalises a result's clauses: literals within a clause are string-sorted, fresh structural / automaton-state atoms map to n0, n1, ..., and fresh counting roles map to cr0, cr1, ... by first appearance, so a pin fixes the shape rather than the id allocation.</summary>
    /// <param name="result">The clausification result.</param>
    /// <returns>The canonical clause strings, ordinally sorted.</returns>
    private static List<string> CanonicalClauses(ClausificationResult result)
    {
        List<string> rendered = [];
        foreach(DlClause clause in result.Clauses)
        {
            rendered.Add(RenderClause(clause, result.Symbols));
        }

        rendered.Sort(StringComparer.Ordinal);

        Dictionary<string, string> map = new(StringComparer.Ordinal);
        int atomCounter = 0;
        int roleCounter = 0;
        foreach(string clause in rendered)
        {
            foreach(Match match in FreshToken().Matches(clause))
            {
                if(!map.ContainsKey(match.Value))
                {
                    map[match.Value] = match.Value.StartsWith("_r", StringComparison.Ordinal) ? $"cr{roleCounter++}" : $"n{atomCounter++}";
                }
            }
        }

        List<string> canonical = [];
        foreach(string clause in rendered)
        {
            canonical.Add(ResortLiterals(FreshToken().Replace(clause, match => map[match.Value])));
        }

        canonical.Sort(StringComparer.Ordinal);

        return canonical;
    }

    /// <summary>Re-sorts the literals of a rendered clause after fresh-name substitution -- the substitution changes literal spellings, so the pre-substitution sort goes stale. The separator <c>", "</c> never occurs inside a rendered literal (role arguments join on a bare comma).</summary>
    /// <param name="clause">The rendered clause.</param>
    /// <returns>The clause with each side's literals re-sorted.</returns>
    private static string ResortLiterals(string clause)
    {
        if(clause.EndsWith(" ->", StringComparison.Ordinal))
        {
            return $"{SortSide(clause[..^3])} ->";
        }

        int arrow = clause.IndexOf(" -> ", StringComparison.Ordinal);
        string left = clause[..arrow];
        string right = clause[(arrow + 4)..];

        return $"{SortSide(left)} -> {SortSide(right)}";
    }

    /// <summary>Sorts one side's comma-separated literals ordinally.</summary>
    /// <param name="side">The rendered side.</param>
    /// <returns>The sorted side.</returns>
    private static string SortSide(string side)
    {
        if(side.Length == 0)
        {
            return side;
        }

        string[] literals = side.Split(", ", StringSplitOptions.None);
        Array.Sort(literals, StringComparer.Ordinal);

        return string.Join(", ", literals);
    }

    /// <summary>The regex matching a fresh structural / automaton-state atom (<c>urn:veritas:ctx:aN</c> or <c>_aN</c>) or a fresh counting role (<c>_rN</c>).</summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex(@"urn:veritas:ctx:a\d+|_a\d+|_r\d+")]
    private static partial Regex FreshToken();

    /// <summary>Renders a clause as <c>sortedBody -&gt; sortedHead</c>, each literal side ordinally sorted so the clause is order-insensitive; an empty head renders as a bare arrow.</summary>
    /// <param name="clause">The clause.</param>
    /// <param name="symbols">The symbol table naming atoms and roles.</param>
    /// <returns>The canonical clause rendering.</returns>
    private static string RenderClause(DlClause clause, ContextSymbolTable symbols)
    {
        List<string> body = [];
        foreach(DlLiteral literal in clause.Body)
        {
            body.Add(RenderLiteral(literal, symbols));
        }

        List<string> head = [];
        foreach(DlLiteral literal in clause.Head)
        {
            head.Add(RenderLiteral(literal, symbols));
        }

        body.Sort(StringComparer.Ordinal);
        head.Sort(StringComparer.Ordinal);
        string left = string.Join(", ", body);

        return head.Count == 0 ? $"{left} ->" : $"{left} -> {string.Join(", ", head)}";
    }

    /// <summary>Renders one literal against a symbol table.</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="symbols">The symbol table naming atoms and roles.</param>
    /// <returns>The rendered literal.</returns>
    private static string RenderLiteral(DlLiteral literal, ContextSymbolTable symbols)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => $"{symbols.RenderAtom(literal.Symbol)}({RenderTerm(literal.First)})",
            DlLiteralKind.Role => $"{symbols.RenderRole(literal.Symbol)}({RenderTerm(literal.First)},{RenderTerm(literal.Second)})",
            DlLiteralKind.Equality => $"{RenderTerm(literal.First)} = {RenderTerm(literal.Second)}",
            _ => $"{RenderTerm(literal.First)} != {RenderTerm(literal.Second)}",
        };
    }

    /// <summary>Renders one F-term (<c>x</c>, <c>y</c>, <c>z{i}</c>, <c>f{k}(x)</c>).</summary>
    /// <param name="term">The term.</param>
    /// <returns>The rendered term.</returns>
    private static string RenderTerm(DlTerm term)
    {
        return term.Kind switch
        {
            DlTermKind.Central => "x",
            DlTermKind.Context => "y",
            DlTermKind.Neighbour => $"z{term.Index}",
            _ => $"f{term.Index}(x)",
        };
    }

    /// <summary>Renders a census as <c>{count} x {key}</c> entries for a diagnostic.</summary>
    /// <param name="census">The census entries.</param>
    /// <returns>The rendered entries.</returns>
    private static List<string> RenderCensus(IReadOnlyList<(string Key, int Count)> census)
    {
        List<string> rendered = [];
        foreach((string key, int count) in census)
        {
            rendered.Add($"{count} x {key}");
        }

        return rendered;
    }

    /// <summary>Whether a clause has a role atom in its head (a DL5/DL6 role inclusion).</summary>
    /// <param name="clause">The clause.</param>
    /// <returns><see langword="true"/> when the head carries a role atom.</returns>
    private static bool HasRoleHead(DlClause clause)
    {
        foreach(DlLiteral literal in clause.Head)
        {
            if(literal.Kind == DlLiteralKind.Role)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Asserts that no clause emits a role-atom head -- a folded chain leaves no DL5 role clause (mutation M6).</summary>
    /// <param name="result">The clausification result.</param>
    /// <param name="row">The row label.</param>
    /// <param name="failures">The failure list this check appends to.</param>
    private static void CheckNoRoleHead(ClausificationResult result, string row, List<string> failures)
    {
        foreach(DlClause clause in result.Clauses)
        {
            if(HasRoleHead(clause))
            {
                failures.Add($"{row}: a folded chain wrongly emitted a role-head (DL5) clause: {RenderClause(clause, result.Symbols)}");
            }
        }
    }

    /// <summary>Reconstructs the emitted role automaton and asserts the accepted and rejected role words, recording every offender.</summary>
    /// <param name="result">The clausification result.</param>
    /// <param name="carrier">The carrier atom name that seeds the initial states.</param>
    /// <param name="filler">The filler atom name that marks the final states.</param>
    /// <param name="row">The row label.</param>
    /// <param name="failures">The failure list this check appends to.</param>
    /// <param name="accept">The words the automaton must accept.</param>
    /// <param name="reject">The words the automaton must reject.</param>
    private static void CheckAutomatonAccepts(ClausificationResult result, string carrier, string filler, string row, List<string> failures, List<(string Name, bool Inverse)[]> accept, List<(string Name, bool Inverse)[]> reject)
    {
        ReconstructedAutomaton automaton = Reconstruct(result, carrier, filler);
        foreach((string Name, bool Inverse)[] word in accept)
        {
            if(!automaton.Accepts(word))
            {
                failures.Add($"{row}: the automaton should accept {RenderWord(word)} but does not.");
            }
        }

        foreach((string Name, bool Inverse)[] word in reject)
        {
            if(automaton.Accepts(word))
            {
                failures.Add($"{row}: the automaton should reject {RenderWord(word)} but accepts it.");
            }
        }
    }

    /// <summary>Reconstructs the role automaton from a chain-elimination clause set: named-carrier seeds are the initial states, named-filler discharges are the finals, and the transition clauses are the letter edges.</summary>
    /// <param name="result">The clausification result.</param>
    /// <param name="carrier">The carrier atom name.</param>
    /// <param name="filler">The filler atom name.</param>
    /// <returns>The reconstructed automaton.</returns>
    private static ReconstructedAutomaton Reconstruct(ClausificationResult result, string carrier, string filler)
    {
        HashSet<int> initial = [];
        HashSet<int> finals = [];
        List<(int From, string Name, bool Inverse, int To)> edges = [];

        foreach(DlClause clause in result.Clauses)
        {
            if(clause.Body.Length == 1 && clause.Head.Length == 1 && clause.Body[0].Kind == DlLiteralKind.Concept && clause.Head[0].Kind == DlLiteralKind.Concept
                && clause.Body[0].First.IsCentral && clause.Head[0].First.IsCentral)
            {
                string bodyName = result.Symbols.RenderAtom(clause.Body[0].Symbol);
                string headName = result.Symbols.RenderAtom(clause.Head[0].Symbol);
                if(bodyName == carrier && IsFreshState(headName))
                {
                    initial.Add(clause.Head[0].Symbol);
                }

                if(IsFreshState(bodyName) && headName == filler)
                {
                    finals.Add(clause.Body[0].Symbol);
                }

                continue;
            }

            if(clause.Body.Length == 2 && clause.Head.Length == 1 && clause.Head[0].Kind == DlLiteralKind.Concept && !clause.Head[0].First.IsCentral)
            {
                DlLiteral conceptBody = clause.Body[0].Kind == DlLiteralKind.Concept ? clause.Body[0] : clause.Body[1];
                DlLiteral roleBody = clause.Body[0].Kind == DlLiteralKind.Role ? clause.Body[0] : clause.Body[1];
                if(conceptBody.Kind == DlLiteralKind.Concept && roleBody.Kind == DlLiteralKind.Role && conceptBody.First.IsCentral)
                {
                    bool inverse = !roleBody.First.IsCentral;
                    string name = result.Symbols.RenderRole(roleBody.Symbol);
                    edges.Add((conceptBody.Symbol, name, inverse, clause.Head[0].Symbol));
                }
            }
        }

        return new ReconstructedAutomaton(initial, finals, edges);
    }

    /// <summary>Whether a rendered atom name is a fresh (null-named) automaton-state atom, which renders with the <c>_a</c> prefix.</summary>
    /// <param name="name">The rendered atom name.</param>
    /// <returns><see langword="true"/> for a fresh automaton-state atom.</returns>
    private static bool IsFreshState(string name)
    {
        return name.StartsWith("_a", StringComparison.Ordinal);
    }

    /// <summary>Builds a role word from its directioned letters.</summary>
    /// <param name="letters">The (role name, inverse) letters, in order.</param>
    /// <returns>The word.</returns>
    private static (string Name, bool Inverse)[] Word(params (string Name, bool Inverse)[] letters)
    {
        return letters;
    }

    /// <summary>Renders a role word for an offender message.</summary>
    /// <param name="word">The word.</param>
    /// <returns>The rendering.</returns>
    private static string RenderWord((string Name, bool Inverse)[] word)
    {
        if(word.Length == 0)
        {
            return "(empty)";
        }

        List<string> letters = [];
        foreach((string name, bool inverse) in word)
        {
            letters.Add(inverse ? $"{name}^-" : name);
        }

        return string.Join(" ", letters);
    }

    /// <summary>Clausifies a module, asserts its remainder equals the expected named set, and records the row's verdict.</summary>
    /// <param name="report">The report the verdict appends to.</param>
    /// <param name="failures">The failure list a mismatch appends to.</param>
    /// <param name="row">The row label.</param>
    /// <param name="module">The module to clausify.</param>
    /// <param name="expected">The expected remainder names.</param>
    private static void CheckRemainder(StringBuilder report, List<string> failures, string row, ReasoningModule module, string[] expected, bool rootKeyJoinEnabled = true)
    {
        ClausificationResult result = ContextClausifier.Clausify(module, EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false, rootKeyJoinEnabled);
        List<string> actual = [.. result.Remainder];
        actual.Sort(StringComparer.Ordinal);
        List<string> want = [.. expected];
        want.Sort(StringComparer.Ordinal);

        bool ok = actual.Count == want.Count;
        for(int i = 0; ok && i < actual.Count; i++)
        {
            ok = string.Equals(actual[i], want[i], StringComparison.Ordinal);
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"{row} | {(ok ? "OK" : "MISMATCH")}");
        if(!ok)
        {
            failures.Add($"{row}: expected remainder {{{string.Join(" | ", want)}}}, got {{{string.Join(" | ", actual)}}}");
        }
    }

    /// <summary>Clausifies a module, asserts its reserved-role rejection remainder equals the expected named set AND that the rejection emits no clause (the pre-intake early-return invariant, mutation MU8's kill), and records the row's verdict.</summary>
    /// <param name="report">The report the verdict appends to.</param>
    /// <param name="failures">The failure list a mismatch appends to.</param>
    /// <param name="row">The row label.</param>
    /// <param name="module">The module to clausify.</param>
    /// <param name="expected">The expected reserved-role remainder names.</param>
    private static void CheckReservedRejection(StringBuilder report, List<string> failures, string row, ReasoningModule module, string[] expected)
    {
        ClausificationResult result = ContextClausifier.Clausify(module);
        List<string> actual = [.. result.Remainder];
        actual.Sort(StringComparer.Ordinal);
        List<string> want = [.. expected];
        want.Sort(StringComparer.Ordinal);

        bool ok = actual.Count == want.Count && result.Clauses.Count == 0;
        for(int i = 0; ok && i < actual.Count; i++)
        {
            ok = string.Equals(actual[i], want[i], StringComparison.Ordinal);
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"{row} | {(ok ? "OK" : "MISMATCH")}");
        if(!ok)
        {
            failures.Add($"{row}: expected remainder {{{string.Join(" | ", want)}}} with no clause, got remainder {{{string.Join(" | ", actual)}}} clauses={result.Clauses.Count}");
        }
    }

    /// <summary>A named class reference over a bare local IRI -- the clausifier interns and renders raw strings, so a short name keeps the rendered clauses legible.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The class reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(local)));
    }

    /// <summary>A named object-property expression over a bare local IRI.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(local)));
    }

    /// <summary>The inverse of a named object property, spelled as an <c>ObjectInverseOf</c>.</summary>
    /// <param name="local">The forward role's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(local)));
    }

    /// <summary>A property reference to the reserved <c>owl:topObjectProperty</c> over its vocabulary IRI, not a bare local name.</summary>
    /// <returns>The reference.</returns>
    private static OwlObjectPropertyReference TopProperty()
    {
        return new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty));
    }

    /// <summary>A property reference to the reserved <c>owl:bottomObjectProperty</c> over its vocabulary IRI, not a bare local name.</summary>
    /// <returns>The reference.</returns>
    private static OwlObjectPropertyReference BottomProperty()
    {
        return new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty));
    }

    /// <summary>A named individual over a bare local IRI.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(local));
    }

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The conjuncts.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf(operands);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The disjuncts.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf(operands);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>An enumeration of individuals (<c>ObjectOneOf</c>).</summary>
    /// <param name="individuals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An existential restriction over an inverse role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(InverseProperty(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction over an inverse role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom AllInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(InverseProperty(property), filler);
    }

    /// <summary>A min-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The lower bound.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
    }

    /// <summary>A max-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The upper bound.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A max-cardinality restriction over an inverse role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="cardinality">The upper bound.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxInverse(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, InverseProperty(property), filler);
    }

    /// <summary>An exact-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The exact bound.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Exact(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), filler);
    }

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = AxiomOrigin };
    }

    /// <summary>An equivalence of two class expressions.</summary>
    /// <param name="first">The first class expression.</param>
    /// <param name="second">The second class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom EquivalentClasses(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = AxiomOrigin };
    }

    /// <summary>A pairwise disjointness axiom.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom DisjointClasses(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = AxiomOrigin };
    }

    /// <summary>A disjoint-union axiom defining a named class as the disjoint union of the operands.</summary>
    /// <param name="definedClass">The defined class's local name.</param>
    /// <param name="operands">The member operands.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointUnionAxiom DisjointUnion(string definedClass, params OwlClassExpression[] operands)
    {
        return new OwlDisjointUnionAxiom(new NamedNode(Utf8Strings.From(definedClass)), operands) { Origin = AxiomOrigin };
    }

    /// <summary>A domain axiom over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="domain">The domain class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom Domain(string property, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(Property(property), domain) { Origin = AxiomOrigin };
    }

    /// <summary>A range axiom over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="range">The range class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(property), range) { Origin = AxiomOrigin };
    }

    /// <summary>A subrole inclusion <c>sub [= super</c>.</summary>
    /// <param name="sub">The subrole's local name.</param>
    /// <param name="super">The superrole's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = AxiomOrigin };
    }

    /// <summary>A sub-property-of-inverse inclusion <c>sub [= super^-</c>.</summary>
    /// <param name="sub">The subrole's local name.</param>
    /// <param name="super">The inverted superrole's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubPropertyInverse(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), InverseProperty(super)) { Origin = AxiomOrigin };
    }

    /// <summary>An inverse sub-property inclusion <c>sub^- [= super</c>.</summary>
    /// <param name="sub">The inverted subrole's local name.</param>
    /// <param name="super">The superrole's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom InverseSubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(InverseProperty(sub), Property(super)) { Origin = AxiomOrigin };
    }

    /// <summary>A property-chain sub-role inclusion.</summary>
    /// <param name="superProperty">The superrole's local name.</param>
    /// <param name="links">The chain links' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlPropertyChainAxiom Chain(string superProperty, params string[] links)
    {
        OwlObjectPropertyExpression[] chain = new OwlObjectPropertyExpression[links.Length];
        for(int index = 0; index < links.Length; index++)
        {
            chain[index] = Property(links[index]);
        }

        return new OwlPropertyChainAxiom(chain, Property(superProperty)) { Origin = AxiomOrigin };
    }

    /// <summary>A transitive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>An irreflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>A symmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>A reflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>An asymmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>A pairwise disjoint-object-properties axiom over bare-local-name roles.</summary>
    /// <param name="roles">The mutually disjoint roles' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointObjectPropertiesAxiom DisjointProperties(params string[] roles)
    {
        OwlObjectPropertyExpression[] operands = new OwlObjectPropertyExpression[roles.Length];
        for(int index = 0; index < roles.Length; index++)
        {
            operands[index] = Property(roles[index]);
        }

        return new OwlDisjointObjectPropertiesAxiom(operands) { Origin = AxiomOrigin };
    }

    /// <summary>A functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>An inverse-functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, Property(property)) { Origin = AxiomOrigin };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = AxiomOrigin };
    }

    /// <summary>A negative object-property assertion.</summary>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom NegativeEdge(string source, string role, string target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(Individual(source), Property(role), Individual(target)) { Origin = AxiomOrigin };
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>
    /// A role automaton reconstructed from a chain-elimination clause set: the
    /// initial states (named-carrier seeds), the final states (named-filler
    /// discharges), and the directioned-letter transitions. Acceptance is an
    /// explicit-worklist NFA run over the letters of a word -- no recursion.
    /// </summary>
    private sealed class ReconstructedAutomaton
    {
        /// <summary>The initial states.</summary>
        private HashSet<int> Initial { get; }

        /// <summary>The final states.</summary>
        private HashSet<int> Finals { get; }

        /// <summary>The directioned-letter transitions.</summary>
        private List<(int From, string Name, bool Inverse, int To)> Edges { get; }

        /// <summary>Initialises the reconstructed automaton.</summary>
        /// <param name="initial">The initial states.</param>
        /// <param name="finals">The final states.</param>
        /// <param name="edges">The transitions.</param>
        public ReconstructedAutomaton(HashSet<int> initial, HashSet<int> finals, List<(int From, string Name, bool Inverse, int To)> edges)
        {
            Initial = initial;
            Finals = finals;
            Edges = edges;
        }

        /// <summary>Whether the automaton accepts a role word -- an NFA run consuming each directioned letter from the initial states, accepting when a final state is reached.</summary>
        /// <param name="word">The directioned-letter word.</param>
        /// <returns><see langword="true"/> when a final state is reachable by the word.</returns>
        public bool Accepts((string Name, bool Inverse)[] word)
        {
            HashSet<int> current = [.. Initial];
            foreach((string name, bool inverse) in word)
            {
                HashSet<int> next = [];
                foreach((int from, string edgeName, bool edgeInverse, int to) in Edges)
                {
                    if(current.Contains(from) && edgeInverse == inverse && string.Equals(edgeName, name, StringComparison.Ordinal))
                    {
                        next.Add(to);
                    }
                }

                current = next;
            }

            foreach(int state in current)
            {
                if(Finals.Contains(state))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
