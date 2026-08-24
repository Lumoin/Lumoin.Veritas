using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The SROIQ ground-slice clausifier batteries, exercised through
/// direct <see cref="ContextClausifier.Clausify"/>
/// calls independent of the module-survey admission gate (which admits the
/// object ABox of the ground slice). The families pin the pre-merge
/// union-find and its representative-collision clash, the asserted-edge closure
/// clash consumers (negative assertions, asymmetry, irreflexivity, role
/// disjointness) over the told RBox, the composition and reserved-role remainders,
/// and the marker/edge lowering structure. Row axioms transcribe the certified
/// battery table verbatim.
/// </summary>
internal sealed partial class ContextClausifierTests
{
    /// <summary>
    /// The pre-merge ground-individual battery: a
    /// <c>SameIndividual</c>/<c>DifferentIndividuals</c> representative collision
    /// (including union transitivity and the degenerate repeated-term pair) decides
    /// a ground clash at clausification, an n-ary distinct list does not, and a
    /// co-occurring reserved-role assertion wins precedence —
    /// the reserved remainder, not a ground clash.
    /// </summary>
    [TestMethod]
    public void GroundPreMergeClashBattery()
    {
        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("pre-merge rows: row | verdict");

        //GM1 {a=b, !=(a,b)}: the union collapses a and b; the pairwise distinctness then collides.
        CheckGroundClash(report, failures, "GM1", Module(
            Same("gm1a", "gm1b"),
            Different("gm1a", "gm1b")),
            "GroundMergeCollision");

        //GM2 {a=b, b=c, !=(a,c)}: union transitivity brings a and c to one representative.
        CheckGroundClash(report, failures, "GM2", Module(
            Same("gm2a", "gm2b"),
            Same("gm2b", "gm2c"),
            Different("gm2a", "gm2c")),
            "GroundMergeCollision");

        //GM7 {a=b, c=d, b=d, !=(a,d)}: two same-trees merge through the bridge b=d, attaching root c under
        //a and leaving the depth-2 chain d->c->a; the collision surfaces only when Find walks to the root.
        CheckGroundClash(report, failures, "GM7", Module(
            Same("gm7a", "gm7b"),
            Same("gm7c", "gm7d"),
            Same("gm7b", "gm7d"),
            Different("gm7a", "gm7d")),
            "GroundMergeCollision");

        //GM4 {!=(a,a)}: the same term twice is a representative collision with itself.
        CheckGroundClash(report, failures, "GM4", Module(
            Different("gm4a", "gm4a")),
            "GroundMergeCollision");

        //GM5 {!=(i01..i28), i01:A}: the QL shape -- 28 distinct individuals, no union, no collision.
        List<OwlAxiom> gm5 = [Different(
            "gm5i01", "gm5i02", "gm5i03", "gm5i04", "gm5i05", "gm5i06", "gm5i07", "gm5i08", "gm5i09", "gm5i10",
            "gm5i11", "gm5i12", "gm5i13", "gm5i14", "gm5i15", "gm5i16", "gm5i17", "gm5i18", "gm5i19", "gm5i20",
            "gm5i21", "gm5i22", "gm5i23", "gm5i24", "gm5i25", "gm5i26", "gm5i27", "gm5i28"),
            ClassAssertion(Class("Gm5A"), Individual("gm5i01"))];
        CheckNoGroundClash(report, failures, "GM5", Module([.. gm5]));

        //GH5 {top(a,b), c=d, !=(c,d)}: the reserved-role scan precedes the pre-merge, so the module
        //delegates on the reserved remainder and never reaches the collision decision.
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(
                TopEdge("gh5a", "gh5b"),
                Same("gh5c", "gh5d"),
                Different("gh5c", "gh5d")));
            bool ok = !result.GroundClash && RemainderHasPrefix(result, "ReservedRoleInObjectPropertyAssertion");
            report.AppendLine(CultureInfo.InvariantCulture, $"GH5 | {(ok ? "OK" : "MISMATCH")}");
            if(!ok)
            {
                failures.Add($"GH5: expected no ground clash + reserved remainder, got clash={result.GroundClash} remainder={{{string.Join(" | ", result.Remainder)}}}");
            }
        }

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The asserted-edge closure battery: the pure-edge clash
    /// consumers decided over the RBox-closed asserted-edge graph — a directly,
    /// hierarchy-, inverse-, symmetric-, transitive-, or chain-entailed negative
    /// assertion; an asymmetry two-cycle and diagonal; an irreflexive self-loop and
    /// a reflexive loop lifted to an irreflexive super-role; and disjoint parallel
    /// edges direct and via a sub-role. Each inconsistent shape is paired with a
    /// consistent twin the closure does not reach.
    /// </summary>
    [TestMethod]
    public void GroundClosureClashBattery()
    {
        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("closure rows: row | verdict");

        //GE1 {r(a,b), !r(a,b)}: the base edge is exactly the denied one.
        CheckGroundClash(report, failures, "GE1", Module(
            Edge("ge1a", "ge1r", "ge1b"),
            NegativeEdge("ge1a", "ge1r", "ge1b")),
            "NegativeEdgeEntailed");

        //GE2 {s(a,b), s[=r, !r(a,b)}: hierarchy lifts s(a,b) to r(a,b).
        CheckGroundClash(report, failures, "GE2", Module(
            Edge("ge2a", "ge2s", "ge2b"),
            SubProperty("ge2s", "ge2r"),
            NegativeEdge("ge2a", "ge2r", "ge2b")),
            "NegativeEdgeEntailed");

        //GE3 {r(b,a), !(r^-)(a,b)}: the directioned mirror carries r(b,a) as r^-(a,b), the inverse-normalized denial.
        CheckGroundClash(report, failures, "GE3", Module(
            Edge("ge3b", "ge3r", "ge3a"),
            NegativeInverseEdge("ge3a", "ge3r", "ge3b")),
            "NegativeEdgeEntailed");

        //GE4 {Sym(r), r(b,a), !r(a,b)}: symmetry lifts the mirror r^-(a,b) to r(a,b).
        CheckGroundClash(report, failures, "GE4", Module(
            Symmetric("ge4r"),
            Edge("ge4b", "ge4r", "ge4a"),
            NegativeEdge("ge4a", "ge4r", "ge4b")),
            "NegativeEdgeEntailed");

        //GE5 {Trans(r), r(a,c), r(c,b), !r(a,b)}: the transitive path composes to r(a,b).
        CheckGroundClash(report, failures, "GE5", Module(
            Transitive("ge5r"),
            Edge("ge5a", "ge5r", "ge5c"),
            Edge("ge5c", "ge5r", "ge5b"),
            NegativeEdge("ge5a", "ge5r", "ge5b")),
            "NegativeEdgeEntailed");

        //GE6 {p o q [= r, p(a,c), q(c,b), !r(a,b)}: the chain composes to r(a,b).
        CheckGroundClash(report, failures, "GE6", Module(
            Chain("ge6r", "ge6p", "ge6q"),
            Edge("ge6a", "ge6p", "ge6c"),
            Edge("ge6c", "ge6q", "ge6b"),
            NegativeEdge("ge6a", "ge6r", "ge6b")),
            "NegativeEdgeEntailed");

        //GE7 {r(a,c), !r(a,b)}: the asserted pair is not the denied pair.
        CheckNoGroundClash(report, failures, "GE7", Module(
            Edge("ge7a", "ge7r", "ge7c"),
            NegativeEdge("ge7a", "ge7r", "ge7b")));

        //GE8 {p o q [= r, p(a,c), q(d,b), !r(a,b)}: the path is broken (c != d), no composition.
        CheckNoGroundClash(report, failures, "GE8", Module(
            Chain("ge8r", "ge8p", "ge8q"),
            Edge("ge8a", "ge8p", "ge8c"),
            Edge("ge8d", "ge8q", "ge8b"),
            NegativeEdge("ge8a", "ge8r", "ge8b")));

        //GE9 {Asy(r), r(a,b), r(b,a)}: the two-cycle carries both r(a,b) and its reverse.
        CheckGroundClash(report, failures, "GE9", Module(
            Asymmetric("ge9r"),
            Edge("ge9a", "ge9r", "ge9b"),
            Edge("ge9b", "ge9r", "ge9a")),
            "DisjointRolesViolated");

        //GE10 {Asy(r), r(a,a)}: asymmetry entails irreflexivity; the diagonal violates it.
        CheckGroundClash(report, failures, "GE10", Module(
            Asymmetric("ge10r"),
            Edge("ge10a", "ge10r", "ge10a")),
            "DisjointRolesViolated");

        //GE11 {Irr(r), r(a,a)}: the asserted self-loop violates irreflexivity.
        CheckGroundClash(report, failures, "GE11", Module(
            Irreflexive("ge11r"),
            Edge("ge11a", "ge11r", "ge11a")),
            "IrreflexivityViolated");

        //GE12 {a:C, Ref(s), s[=r, Irr(r)}: the reflexive loop on the individual lifts s->r and meets Irr(r).
        CheckGroundClash(report, failures, "GE12", Module(
            ClassAssertion(Class("Ge12C"), Individual("ge12a")),
            Reflexive("ge12s"),
            SubProperty("ge12s", "ge12r"),
            Irreflexive("ge12r")),
            "IrreflexivityViolated");

        //GE13 {Dis(r,s), r(a,b), s(a,b)}: the shared pair lies in both disjoint roles.
        CheckGroundClash(report, failures, "GE13", Module(
            DisjointProperties("ge13r", "ge13s"),
            Edge("ge13a", "ge13r", "ge13b"),
            Edge("ge13a", "ge13s", "ge13b")),
            "DisjointRolesViolated");

        //GE14 {Dis(r,s), t(a,b), t[=r, s(a,b)}: hierarchy lifts t(a,b) to r(a,b), colliding with s(a,b).
        CheckGroundClash(report, failures, "GE14", Module(
            DisjointProperties("ge14r", "ge14s"),
            Edge("ge14a", "ge14t", "ge14b"),
            SubProperty("ge14t", "ge14r"),
            Edge("ge14a", "ge14s", "ge14b")),
            "DisjointRolesViolated");

        //B3t {p o q [= r, a:∃p.Self, q(a,b), !r(a,c)}: the Self loop is a ghost-pass augmentation, absent from
        //the clausification-time closure, and no asserted word reaches (a,c) -- no ground clash at that point.
        CheckNoGroundClash(report, failures, "B3t", Module(
            Chain("b3tr", "b3tp", "b3tq"),
            ClassAssertion(new OwlObjectHasSelf(Property("b3tp")), Individual("b3ta")),
            Edge("b3ta", "b3tq", "b3tb"),
            NegativeEdge("b3ta", "b3tr", "b3tc")));

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The ground-remainder battery: an
    /// asserted edge over a counting-capable role — functional, decomposed exact, or
    /// laundered through a max-cardinality super-role — names the counting remainder;
    /// a reserved-role object or negative assertion names its reserved remainder; and
    /// a literal in an individual position names the literal remainder. Each row
    /// delegates without deciding a ground clash.
    /// </summary>
    [TestMethod]
    public void GroundRemainderBattery()
    {
        List<string> failures = [];
        StringBuilder report = new();
        report.AppendLine("remainder rows: row | verdict");

        //GH1 {Func(r), r(a,b), r(a,c), !=(b,c)}: r is a counting target via Func; the asserted edge delegates.
        CheckRemainderPrefix(report, failures, "GH1", Module(
            Functional("gh1r"),
            Edge("gh1a", "gh1r", "gh1b"),
            Edge("gh1a", "gh1r", "gh1c"),
            Different("gh1b", "gh1c")),
            "GroundEdgeOnCountingRole");

        //B5 {a:=1 r.T (Exact), r(a,b)}: the decomposed exact fills the DL4 targets, so r is in the family.
        CheckRemainderPrefix(report, failures, "B5", Module(
            ClassAssertion(ExactUnqualified("b5r", 1), Individual("b5a")),
            Edge("b5a", "b5r", "b5b")),
            "GroundEdgeOnCountingRole");

        //GH2 {A[=<=1 s.T, r[=s, r(a,b), a:A}: r reaches the family through its super-role s (down-closure).
        CheckRemainderPrefix(report, failures, "GH2", Module(
            SubClassOf(Class("Gh2A"), MaxUnqualified("gh2s", 1)),
            SubProperty("gh2r", "gh2s"),
            Edge("gh2a", "gh2r", "gh2b"),
            ClassAssertion(Class("Gh2A"), Individual("gh2a"))),
            "GroundEdgeOnCountingRole");

        //GH3 {top(a,b)}: a reserved object-property assertion delegates on its named remainder.
        CheckReservedRejection(report, failures, "GH3", Module(
            TopEdge("gh3a", "gh3b")),
            [ReservedObjectPropertyAssertionName(OwlVocabulary.TopObjectProperty)]);

        //GH4 {!bottom(a,b)}: a reserved negative object-property assertion delegates on its named remainder.
        CheckReservedRejection(report, failures, "GH4", Module(
            new OwlNegativeObjectPropertyAssertionAxiom(Individual("gh4a"), BottomProperty(), Individual("gh4b")) { Origin = AxiomOrigin }),
            [ReservedNegativeObjectPropertyAssertionName(OwlVocabulary.BottomObjectProperty)]);

        //GH6 {"5":A}: a literal in the individual position of a class assertion names the literal remainder.
        CheckRemainderPrefix(report, failures, "GH6", Module(
            new OwlClassAssertionAxiom(Class("Gh6A"), IntegerLiteral("5")) { Origin = AxiomOrigin }),
            ContextRemainderNames.GroundIndividualIsLiteral);

        Assert.IsEmpty(failures, report.ToString() + "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The marker and edge lowering structure: a class
    /// assertion lowers to the marker GCI <c>O_a ⊑ C</c>; an asserted edge emits the
    /// body-guarded successor clause <c>O_a(x) → r(x, f_ab(x))</c> and records the
    /// function's designated target; and a <c>SameIndividual</c> union routes both
    /// individuals through one representative marker and counts the merge.
    /// </summary>
    [TestMethod]
    public void GroundLoweringStructurePins()
    {
        List<string> failures = [];

        //Class assertion -> marker GCI O_a(x) -> C(x).
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(
                ClassAssertion(Class("Gp1C"), Individual("gp1a"))));
            int marker = result.GroundMarkers[Utf8Strings.From("gp1a")];
            int classAtom = result.Symbols.AtomOf(Utf8Strings.From("Gp1C"));
            if(!HasSimpleConceptGci(result, marker, classAtom))
            {
                failures.Add("marker GCI: expected O_gp1a(x) -> Gp1C(x) among the emitted clauses.");
            }

            if(result.GroundClash || result.Remainder.Count != 0)
            {
                failures.Add($"marker GCI: expected clean clausification, got clash={result.GroundClash} remainder={{{string.Join(" | ", result.Remainder)}}}");
            }
        }

        //Asserted edge -> O_a(x) -> r(x, f_ab(x)), with the function's target recorded.
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(
                Edge("gp2a", "gp2r", "gp2b")));
            int marker = result.GroundMarkers[Utf8Strings.From("gp2a")];
            RawRoleId role = result.Symbols.RoleOf(Utf8Strings.From("gp2r"));
            if(!HasGroundEdgeClause(result, marker, ContextSymbolTable.Forward(role.Value), out int function))
            {
                failures.Add("edge clause: expected O_gp2a(x) -> gp2r(x, f(x)) among the emitted clauses.");
            }
            else if(!result.GroundTargetByFunction.TryGetValue(function, out Utf8String target) || !target.Equals(Utf8Strings.From("gp2b")))
            {
                failures.Add("edge clause: the ground-edge function does not resolve to target gp2b.");
            }

            if(!result.GroundRepresentatives.Contains(Utf8Strings.From("gp2a")) || !result.GroundRepresentatives.Contains(Utf8Strings.From("gp2b")))
            {
                failures.Add("edge clause: both individuals should be registered representatives.");
            }
        }

        //SameIndividual union -> one shared representative marker, one counted merge. The marker map is keyed
        //by representative, so the union collapses gp3a and gp3b to a single marker entry, and both class
        //assertions lower onto it.
        {
            ClausificationResult result = ContextClausifier.Clausify(Module(
                Same("gp3a", "gp3b"),
                ClassAssertion(Class("Gp3C"), Individual("gp3a")),
                ClassAssertion(Class("Gp3D"), Individual("gp3b"))));
            if(result.PreMergeUnions != 1)
            {
                failures.Add($"same-individual: expected exactly one pre-merge union, got {result.PreMergeUnions}.");
            }

            if(result.GroundRepresentatives.Count != 1 || result.GroundMarkers.Count != 1)
            {
                failures.Add($"same-individual: expected one representative and one marker, got reps={result.GroundRepresentatives.Count} markers={result.GroundMarkers.Count}.");
            }
            else
            {
                int marker = result.GroundMarkers[result.GroundRepresentatives[0]];
                int cAtom = result.Symbols.AtomOf(Utf8Strings.From("Gp3C"));
                int dAtom = result.Symbols.AtomOf(Utf8Strings.From("Gp3D"));
                if(!HasSimpleConceptGci(result, marker, cAtom) || !HasSimpleConceptGci(result, marker, dAtom))
                {
                    failures.Add("same-individual: both class assertions should lower onto the one representative marker.");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    /// <summary>
    /// The individual-origin residual over the shared interning key space: an engine-minted individual keyed by
    /// its deterministic Skolem IRI re-interns idempotently to the one id and records nothing, while an input
    /// IRI spelling that same key interns to the SAME id — the keys coincide — and records the origin
    /// collision, the spoof residual the key-join candidacy layer reads. The recorded origin is never
    /// overwritten, so the spoofed id stays barred from key-join candidacy.
    /// </summary>
    [TestMethod]
    public void EngineMintedIndividualKeyReinternedAsIriRecordsTheOriginResidual()
    {
        ContextSymbolTable symbols = new();
        Utf8String key = Utf8Strings.From("urn:veritas:genid:2:7:11:13:17");

        int minted = symbols.InternIndividual(key, IndividualOrigin.EngineMinted);
        int reminted = symbols.InternIndividual(key, IndividualOrigin.EngineMinted);

        Assert.AreEqual(minted, reminted, "Re-interning an engine-minted key under the same origin returns the one id.");
        Assert.IsFalse(symbols.HasIndividualOriginConflict, "An agreeing re-intern records no origin collision.");
        Assert.IsFalse(symbols.ConflictingIndividualKey is Utf8String, "No key witnesses a collision while every re-intern agrees on origin.");

        int spoofed = symbols.InternIndividual(key, IndividualOrigin.IriDenoted);

        Assert.AreEqual(minted, spoofed, "An input IRI spelling the engine mint's key interns to the same id — the keys coincide.");
        Assert.IsTrue(symbols.HasIndividualOriginConflict, "The disagreeing origin records the key-collision residual.");
        Assert.IsTrue(symbols.ConflictingIndividualKey is Utf8String conflicting && conflicting.Equals(key), "The recorded witness is the colliding key.");
        Assert.AreEqual(IndividualOrigin.EngineMinted, symbols.OriginOf(minted), "The recorded origin is never overwritten by the later disagreeing intern.");
        Assert.IsFalse(symbols.IsKeyJoinCandidateOrigin(minted), "The engine-minted origin stays barred from key-join candidacy despite the IRI-denoted re-intern.");
    }

    /// <summary>Clausifies a module and asserts it decided a ground clash whose reason carries the expected leading identifier.</summary>
    /// <param name="report">The report the verdict appends to.</param>
    /// <param name="failures">The failure list a mismatch appends to.</param>
    /// <param name="row">The row label.</param>
    /// <param name="module">The module to clausify.</param>
    /// <param name="reasonPrefix">The expected clash-reason leading identifier.</param>
    private static void CheckGroundClash(StringBuilder report, List<string> failures, string row, ReasoningModule module, string reasonPrefix)
    {
        ClausificationResult result = ContextClausifier.Clausify(module);
        bool ok = result.GroundClash && result.GroundClashReason is string reason && reason.StartsWith(reasonPrefix, StringComparison.Ordinal);
        report.AppendLine(CultureInfo.InvariantCulture, $"{row} | {(ok ? "OK" : "MISMATCH")}");
        if(!ok)
        {
            failures.Add($"{row}: expected ground clash '{reasonPrefix}...', got clash={result.GroundClash} reason={result.GroundClashReason ?? "(none)"}");
        }
    }

    /// <summary>Clausifies a module and asserts it decided no ground clash.</summary>
    /// <param name="report">The report the verdict appends to.</param>
    /// <param name="failures">The failure list a mismatch appends to.</param>
    /// <param name="row">The row label.</param>
    /// <param name="module">The module to clausify.</param>
    private static void CheckNoGroundClash(StringBuilder report, List<string> failures, string row, ReasoningModule module)
    {
        ClausificationResult result = ContextClausifier.Clausify(module);
        bool ok = !result.GroundClash;
        report.AppendLine(CultureInfo.InvariantCulture, $"{row} | {(ok ? "OK" : "MISMATCH")}");
        if(!ok)
        {
            failures.Add($"{row}: expected no ground clash, got clash reason={result.GroundClashReason ?? "(none)"}");
        }
    }

    /// <summary>Clausifies a module and asserts its remainder contains a name with the expected prefix and that it decided no ground clash.</summary>
    /// <param name="report">The report the verdict appends to.</param>
    /// <param name="failures">The failure list a mismatch appends to.</param>
    /// <param name="row">The row label.</param>
    /// <param name="module">The module to clausify.</param>
    /// <param name="prefix">The expected remainder-name prefix.</param>
    private static void CheckRemainderPrefix(StringBuilder report, List<string> failures, string row, ReasoningModule module, string prefix)
    {
        ClausificationResult result = ContextClausifier.Clausify(module);
        bool ok = !result.GroundClash && RemainderHasPrefix(result, prefix);
        report.AppendLine(CultureInfo.InvariantCulture, $"{row} | {(ok ? "OK" : "MISMATCH")}");
        if(!ok)
        {
            failures.Add($"{row}: expected remainder '{prefix}...' and no clash, got clash={result.GroundClash} remainder={{{string.Join(" | ", result.Remainder)}}}");
        }
    }

    /// <summary>Whether a result's remainder contains a name beginning with a prefix.</summary>
    /// <param name="result">The clausification result.</param>
    /// <param name="prefix">The prefix to match.</param>
    /// <returns><see langword="true"/> when some remainder name starts with the prefix.</returns>
    private static bool RemainderHasPrefix(ClausificationResult result, string prefix)
    {
        foreach(string name in result.Remainder)
        {
            if(name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a result carries a single-concept-body, single-concept-head GCI over the central variable.</summary>
    /// <param name="result">The clausification result.</param>
    /// <param name="bodyAtom">The expected body concept-atom id.</param>
    /// <param name="headAtom">The expected head concept-atom id.</param>
    /// <returns><see langword="true"/> when a matching clause is present.</returns>
    private static bool HasSimpleConceptGci(ClausificationResult result, int bodyAtom, int headAtom)
    {
        foreach(DlClause clause in result.Clauses)
        {
            if(clause.Body.Length == 1 && clause.Head.Length == 1
                && clause.Body[0].Kind == DlLiteralKind.Concept && clause.Body[0].Symbol == bodyAtom && clause.Body[0].First.IsCentral
                && clause.Head[0].Kind == DlLiteralKind.Concept && clause.Head[0].Symbol == headAtom && clause.Head[0].First.IsCentral)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a result carries the ground-edge clause <c>marker(x) → role(x, f(x))</c>, returning the successor function symbol.</summary>
    /// <param name="result">The clausification result.</param>
    /// <param name="marker">The expected body marker concept-atom id.</param>
    /// <param name="roleForward">The expected forward directioned role id of the head role atom.</param>
    /// <param name="function">The successor function symbol id of the matched clause.</param>
    /// <returns><see langword="true"/> when a matching clause is present.</returns>
    private static bool HasGroundEdgeClause(ClausificationResult result, int marker, int roleForward, out int function)
    {
        foreach(DlClause clause in result.Clauses)
        {
            if(clause.Body.Length == 1 && clause.Head.Length == 1
                && clause.Body[0].Kind == DlLiteralKind.Concept && clause.Body[0].Symbol == marker && clause.Body[0].First.IsCentral
                && clause.Head[0].Kind == DlLiteralKind.Role && clause.Head[0].Symbol == roleForward && clause.Head[0].First.IsCentral && clause.Head[0].Second.IsFunction)
            {
                function = clause.Head[0].Second.Index;

                return true;
            }
        }

        function = -1;

        return false;
    }

    /// <summary>The reserved object-property-assertion remainder name for a reserved role IRI.</summary>
    /// <param name="iri">The reserved role IRI.</param>
    /// <returns>The remainder name.</returns>
    private static string ReservedObjectPropertyAssertionName(Utf8String iri)
    {
        return ContextRemainderNames.ReservedRoleInObjectPropertyAssertion(iri);
    }

    /// <summary>The reserved negative-object-property-assertion remainder name for a reserved role IRI.</summary>
    /// <param name="iri">The reserved role IRI.</param>
    /// <returns>The remainder name.</returns>
    private static string ReservedNegativeObjectPropertyAssertionName(Utf8String iri)
    {
        return ContextRemainderNames.ReservedRoleInNegativeObjectPropertyAssertion(iri);
    }

    /// <summary>An object-property assertion over bare-local-name individuals and a forward role.</summary>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string source, string role, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(Utf8Strings.From(role)), Individual(target)) { Origin = AxiomOrigin };
    }

    /// <summary>An object-property assertion over the reserved <c>owl:topObjectProperty</c>.</summary>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom TopEdge(string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(OwlVocabulary.TopObjectProperty), Individual(target)) { Origin = AxiomOrigin };
    }

    /// <summary>A negative object-property assertion over an inverse role.</summary>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="role">The forward role's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom NegativeInverseEdge(string source, string role, string target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(Individual(source), InverseProperty(role), Individual(target)) { Origin = AxiomOrigin };
    }

    /// <summary>A <c>SameIndividual</c> axiom over two bare-local-name individuals.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = AxiomOrigin };
    }

    /// <summary>A <c>DifferentIndividuals</c> axiom over bare-local-name individuals.</summary>
    /// <param name="individuals">The individuals' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlDifferentIndividualsAxiom(terms) { Origin = AxiomOrigin };
    }

    /// <summary>An unqualified max-cardinality restriction over a forward role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="cardinality">The upper bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxUnqualified(string role, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(role), null);
    }

    /// <summary>An unqualified exact-cardinality restriction over a forward role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="cardinality">The exact bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality ExactUnqualified(string role, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(role), null);
    }

    /// <summary>An <c>xsd:integer</c> literal term.</summary>
    /// <param name="value">The lexical value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
    }
}
