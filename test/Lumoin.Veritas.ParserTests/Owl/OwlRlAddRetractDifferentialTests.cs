using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The add/retract battery: a randomized op-sequence harness over the OWL 2
/// RL closure that certifies the maintained closure of a base evolved by an
/// arbitrary history of add-sets and retract-sets against the from-scratch
/// naive oracle (<see cref="OwlRlClosure.ComputeNaive"/>) after EVERY op.
/// The maintained side is <see cref="OwlRlMaintainedClosure"/>, driven
/// through the <see cref="MaintainClosureDelegate"/> seam by
/// <see cref="MaintainedClosureAdapter"/> — one engine per op sequence,
/// constructed at the first checkpoint and incrementally applied thereafter —
/// so every family is the incremental engine's certification.
/// <see cref="MaintainByRecompute"/> stays available at the same seam as the
/// reference strategy: routing a failing family through it separates an
/// engine defect from a harness defect in one edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The checkpoint contract</b> is the Phase-0 battery's: on a consistent
/// base the maintained and naive derived sets are equal in both directions;
/// on an inconsistent base both report inconsistency with a named falsity.
/// Rule-name and premise identity stay out of contract.
/// </para>
/// <para>
/// <b>Determinism.</b> The randomized families drive CsCheck op-sequence
/// generators under one pinned seed each, so every run replays identically;
/// breadth comes from deliberate seed bumps, never unpinned iteration. The
/// six hand-built adversarial shapes carry expected fact sets hand-derived
/// in their doc comments — ground-truth pins independent of any engine, not
/// differential assertions. A disagreement between a hand-derivation and the
/// engine is a finding to surface, never an expectation to fit to the engine.
/// </para>
/// </remarks>
[TestClass]
internal sealed class OwlRlAddRetractDifferentialTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The <c>xsd:integer</c> datatype IRI numeric literals carry.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    //Pinned CsCheck seeds, one per randomized family. Each is a valid PCG
    //seed string (round-trips through CsCheck's PCG.Parse), so the generated
    //op sequence is byte-identical every run; breadth is bumped by editing a
    //seed, never by unpinned CI variance.
    private const string SchemaSeed = "000000000fF1";

    private const string CharacteristicSeed = "000000000vh1";

    private const string InverseChainSeed = "000000000KV1";

    private const string EqualitySeed = "000000000_x1";

    private const string AddOnlySeed = "000000001e91";

    /// <summary>The iterations each pinned-seed family samples — modest, for suite-time discipline; the corpus soak owns scale.</summary>
    private const long FamilyIterations = 40;

    /// <summary>The maximum ops in a generated sequence.</summary>
    private const int MaxOps = 8;

    /// <summary>The maximum triples an add-set or retract-set touches in one op.</summary>
    private const int MaxSetSize = 3;

    /// <summary>The maximum triples the generated initial base draws from the pool.</summary>
    private const int MaxInitial = 10;

    /// <summary>
    /// The maintenance seam: produces the RL closure after an op given the
    /// previous closure, the op's added and retracted triples, and the
    /// resulting base. <see cref="MaintainedClosureAdapter"/> drives the
    /// incremental engine through it; <see cref="MaintainByRecompute"/>
    /// ignores the deltas and recomputes from <paramref name="currentBase"/>.
    /// </summary>
    /// <param name="previousClosure">The closure the previous checkpoint produced, or <see langword="null"/> at the first checkpoint.</param>
    /// <param name="addedTriples">The base triples this op added (empty for a retract op).</param>
    /// <param name="retractedTriples">The base triples this op removed (empty for an add op).</param>
    /// <param name="currentBase">The base after the op — what the closure must now hold over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the dt-* falsities, or <see langword="default"/> to disable them.</param>
    /// <param name="cancellationToken">A token that aborts derivation.</param>
    /// <returns>The maintained closure of <paramref name="currentBase"/>.</returns>
    internal delegate OwlRlResult MaintainClosureDelegate(
        OwlRlResult? previousClosure,
        IReadOnlyCollection<EncodedTriple> addedTriples,
        IReadOnlyCollection<EncodedTriple> retractedTriples,
        IReadOnlyCollection<EncodedTriple> currentBase,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle,
        CancellationToken cancellationToken);

    /// <summary>One op in a sequence: an add-set (pool indices) or a retract-set (live-base positions), resolved at replay.</summary>
    /// <param name="IsRetract">Whether the op retracts from the live base; otherwise it adds from the pool.</param>
    /// <param name="Indices">The pool indices (add) or live-base positions (retract), each taken modulo the target's count.</param>
    private readonly record struct OpSpec(bool IsRetract, int[] Indices);

    /// <summary>
    /// Drives one <see cref="OwlRlMaintainedClosure"/> across a single op
    /// sequence through the <see cref="MaintainClosureDelegate"/> seam: the
    /// first checkpoint builds the engine from the initial base, and every
    /// later checkpoint applies the op's add-set and retract-set. The
    /// delegate's <c>currentBase</c> is construction input at the first
    /// checkpoint and validation input thereafter — the engine tracks its own
    /// base. One adapter belongs to one sequence; the caller mints a fresh one
    /// per sequence.
    /// </summary>
    private sealed class MaintainedClosureAdapter
    {
        /// <summary>The engine, built at the first checkpoint and applied to thereafter.</summary>
        private OwlRlMaintainedClosure? Engine { get; set; }

        /// <summary>The served set (base ∪ derived) after the previous checkpoint — the "before" image the recorded <see cref="OwlRlMaintainedClosure.AllDelta"/> is checked against.</summary>
        private HashSet<EncodedTriple> PreviousServed { get; } = [];

        /// <summary>The derived set after the previous checkpoint — the "before" image the recorded <see cref="OwlRlMaintainedClosure.DerivedDelta"/> is checked against.</summary>
        private HashSet<EncodedTriple> PreviousDerived { get; } = [];

        /// <summary>Builds the engine at the first checkpoint and applies the op at every later one, the <see cref="MaintainClosureDelegate"/> the runner calls; every op additionally verifies the engine's recorded membership deltas equal its served-set and derived-set snapshot diffs.</summary>
        /// <param name="previousClosure">The previous checkpoint's closure; <see langword="null"/> at the first checkpoint.</param>
        /// <param name="addedTriples">The op's added triples.</param>
        /// <param name="retractedTriples">The op's retracted triples.</param>
        /// <param name="currentBase">The base after the op — construction input at the first checkpoint, and the served-set base thereafter.</param>
        /// <param name="terms">The resolved RL vocabulary.</param>
        /// <param name="datatypeOracle">The datatype oracle for the dt-* falsities.</param>
        /// <param name="cancellationToken">A token that aborts the pass.</param>
        /// <returns>The maintained closure over the edited base.</returns>
        public OwlRlResult Maintain(
            OwlRlResult? previousClosure,
            IReadOnlyCollection<EncodedTriple> addedTriples,
            IReadOnlyCollection<EncodedTriple> retractedTriples,
            IReadOnlyCollection<EncodedTriple> currentBase,
            OwlRlTerms terms,
            OwlRlDatatypeOracle datatypeOracle,
            CancellationToken cancellationToken)
        {
            if(Engine is null)
            {
                Engine = new OwlRlMaintainedClosure(currentBase, terms, datatypeOracle, cancellationToken);
                RecordServedSnapshot(currentBase, Engine.Current.Derived);

                return Engine.Current;
            }

            OwlRlResult result = Engine.Apply(addedTriples, retractedTriples, cancellationToken);
            AssertRecordedDeltasMatchSnapshotDiff(currentBase, result.Derived);
            RecordServedSnapshot(currentBase, result.Derived);

            return result;
        }

        /// <summary>Verifies the engine's recorded deltas equal the served-set and derived-set diffs from the previous checkpoint; a rebuild-class Apply records nothing, so its diffing is left to the caller and skipped here.</summary>
        /// <param name="currentBase">The post-op base — the served set is this united with the post-op derived set.</param>
        /// <param name="derivedAfter">The post-op derived set (a live view over the engine).</param>
        private void AssertRecordedDeltasMatchSnapshotDiff(IReadOnlyCollection<EncodedTriple> currentBase, IReadOnlyCollection<EncodedTriple> derivedAfter)
        {
            if(!Engine!.HasRecordedDeltas)
            {
                return;
            }

            HashSet<EncodedTriple> servedAfter = [.. currentBase, .. derivedAfter];
            HashSet<EncodedTriple> derived = [.. derivedAfter];
            AssertDeltaEqualsDiff(PreviousServed, servedAfter, Engine.AllDelta, "AllDelta");
            AssertDeltaEqualsDiff(PreviousDerived, derived, Engine.DerivedDelta, "DerivedDelta");
        }

        /// <summary>Records the current served set (base ∪ derived) and derived set as the next checkpoint's "before" image.</summary>
        /// <param name="currentBase">The current base.</param>
        /// <param name="derived">The current derived set (a live view over the engine).</param>
        private void RecordServedSnapshot(IReadOnlyCollection<EncodedTriple> currentBase, IReadOnlyCollection<EncodedTriple> derived)
        {
            PreviousServed.Clear();
            PreviousServed.UnionWith(currentBase);
            PreviousServed.UnionWith(derived);
            PreviousDerived.Clear();
            PreviousDerived.UnionWith(derived);
        }

        /// <summary>Asserts a recorded delta equals the before/after snapshot diff, with entered ∩ left empty (the net-fold invariant).</summary>
        /// <param name="before">The tracked set before the op.</param>
        /// <param name="after">The tracked set after the op.</param>
        /// <param name="delta">The recorded delta to check.</param>
        /// <param name="label">The delta's name for assertion messages.</param>
        private static void AssertDeltaEqualsDiff(
            HashSet<EncodedTriple> before,
            HashSet<EncodedTriple> after,
            OwlRlMembershipDelta delta,
            string label)
        {
            HashSet<EncodedTriple> expectedEntered = [.. after];
            expectedEntered.ExceptWith(before);
            HashSet<EncodedTriple> expectedLeft = [.. before];
            expectedLeft.ExceptWith(after);

            HashSet<EncodedTriple> entered = [.. delta.Entered];
            HashSet<EncodedTriple> left = [.. delta.Left];

            Assert.IsTrue(
                entered.SetEquals(expectedEntered),
                $"{label} entered ({entered.Count}) must equal the snapshot diff ({expectedEntered.Count}).");
            Assert.IsTrue(
                left.SetEquals(expectedLeft),
                $"{label} left ({left.Count}) must equal the snapshot diff ({expectedLeft.Count}).");

            HashSet<EncodedTriple> intersection = [.. entered];
            intersection.IntersectWith(left);
            Assert.IsEmpty(intersection, $"{label} entered ∩ left must be empty (the net-fold invariant).");
        }
    }


    /// <summary>
    /// CyclicOrphan. A transitive property <c>p</c> over the two-cycle
    /// <c>a→b</c>, <c>b→a</c> with external support <c>s→a</c>. Retracting the
    /// support strands the cycle's own facts but keeps those still derivable
    /// from the surviving edges; retracting a cycle edge then leaves nothing
    /// a vanished cycle could derive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. prp-trp is <c>p(x,y) ∧ p(y,z) → p(x,z)</c>; it computes
    /// the transitive closure of the edge relation, and Derived is that closure
    /// minus the asserted edges. A transitive property additionally materializes
    /// its <c>p∘p⊑p</c> structure on deterministic list nodes — five triples
    /// (the propertyChainAxiom head, both list cells' rdf:first/rdf:rest) —
    /// plus its <c>owl:ObjectProperty</c> typing through cax-sco over the
    /// axiomatic characteristic subsumption and the scm-op self-subsumption
    /// pair that typing feeds; all eight are present whenever <c>p</c> is
    /// typed owl:TransitiveProperty, so they belong to every op's derived set
    /// here.
    /// </para>
    /// <para>
    /// Op0, base edges {a→b, b→a, s→a}. Reachability: a reaches {a,b}, b
    /// reaches {a,b}, s reaches {a,b}. Closure = {a→a, a→b, b→a, b→b, s→a,
    /// s→b}; asserted are {a→b, b→a, s→a}, so the edge derivations are {a→a,
    /// b→b, s→b}, plus the five chain triples.
    /// </para>
    /// <para>
    /// Op1 retracts s→a. Base edges {a→b, b→a}: a reaches {a,b}, b reaches
    /// {a,b}, s reaches nothing. Edge derivations = {a→a, b→b}. The a↔b cycle's
    /// facts survive (still derivable); every s-fact is gone — s→b was
    /// supported only by the retracted s→a.
    /// </para>
    /// <para>
    /// Op2 retracts a→b. Base edges {b→a}: b reaches {a}, a reaches nothing, no
    /// cycle. Closure = {b→a} (all asserted); no edge derivation remains — only
    /// the five chain triples (p is still transitive). Nothing a vanished cycle
    /// could derive survives.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void CyclicOrphanRetractStrandsThenEmpties()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");

        EncodedTriple transitive = OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty);
        EncodedTriple ab = OwlRlBatteryHelpers.Triple(a, p, b);
        EncodedTriple ba = OwlRlBatteryHelpers.Triple(b, p, a);
        EncodedTriple sa = OwlRlBatteryHelpers.Triple(s, p, a);

        //The five p∘p⊑p list triples a transitive property always materializes,
        //on the deterministic chain nodes the engine reuses every round.
        TermId chainHead = terms.TransitivityChainNode(p, 0);
        TermId chainTail = terms.TransitivityChainNode(p, 1);
        EncodedTriple[] transChain =
        [
            OwlRlBatteryHelpers.Triple(p, terms.PropertyChainAxiom, chainHead),
            OwlRlBatteryHelpers.Triple(chainHead, terms.First, p),
            OwlRlBatteryHelpers.Triple(chainHead, terms.Rest, chainTail),
            OwlRlBatteryHelpers.Triple(chainTail, terms.First, p),
            OwlRlBatteryHelpers.Triple(chainTail, terms.Rest, terms.Nil),
        ];

        EncodedTriple pTyping = OwlRlBatteryHelpers.Triple(p, terms.Type, terms.ObjectPropertyTerm);
        EncodedTriple pSelfSub = OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, p);
        EncodedTriple pSelfEquivalent = OwlRlBatteryHelpers.Triple(p, terms.EquivalentProperty, p);
        HashSet<EncodedTriple> currentBase = [transitive, ab, ba, sa];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [.. transChain, pTyping, pSelfSub, pSelfEquivalent, PP(a, p, a), PP(b, p, b), PP(s, p, b)],
            forbidden: [PP(s, p, s), PP(a, p, s), PP(b, p, s)],
            maintained: engine.Current);

        currentBase.Remove(sa);
        OwlRlResult afterSa = engine.Apply([], [sa], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [.. transChain, pTyping, pSelfSub, pSelfEquivalent, PP(a, p, a), PP(b, p, b)],
            forbidden: [PP(s, p, a), PP(s, p, b), PP(s, p, s), PP(a, p, s), PP(b, p, s)],
            maintained: afterSa);

        currentBase.Remove(ab);
        OwlRlResult afterAb = engine.Apply([], [ab], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [.. transChain, pTyping, pSelfSub, pSelfEquivalent],
            forbidden: [PP(a, p, a), PP(b, p, b), PP(a, p, b), PP(s, p, a), PP(s, p, b)],
            maintained: afterAb);
    }

    /// <summary>
    /// AlternateDerivationSurvival. <c>x rdf:type C</c> has two independent
    /// cax-sco derivations — via <c>A⊑C</c> from <c>x:A</c> and via
    /// <c>B⊑C</c> from <c>x:B</c>. Retracting one premise leaves the fact
    /// standing on the other; retracting the second removes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. cax-sco is <c>subClassOf(c1,c2) ∧ type(x,c1) →
    /// type(x,c2)</c>. The classes are never typed owl:Class, so scm-cls adds
    /// no reflexive subclass edges; A⊑C and B⊑C share no chain, so scm-sco
    /// fires nothing. The only derivation is <c>x:C</c>.
    /// </para>
    /// <para>
    /// Op0, base {A⊑C, B⊑C, x:A, x:B}: Derived = {x:C} (derivable two ways).
    /// Op1 retracts x:A, base {A⊑C, B⊑C, x:B}: Derived = {x:C} (still via
    /// B⊑C). Op2 retracts x:B, base {A⊑C, B⊑C}: no x-typing remains, Derived
    /// = ∅.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AlternateDerivationSurvivesFirstRetractVanishesOnSecond()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubC = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classC);
        EncodedTriple bSubC = OwlRlBatteryHelpers.Triple(classB, terms.SubClassOf, classC);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        HashSet<EncodedTriple> currentBase = [aSubC, bSubC, xIsA, xIsB];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [OwlRlBatteryHelpers.Triple(x, terms.Type, classC)],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(classC, terms.SubClassOf, classA),
                OwlRlBatteryHelpers.Triple(classC, terms.SubClassOf, classB),
                OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB),
                OwlRlBatteryHelpers.Triple(classB, terms.SubClassOf, classA),
            ],
            maintained: engine.Current);

        currentBase.Remove(xIsA);
        OwlRlResult afterFirst = engine.Apply([], [xIsA], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [OwlRlBatteryHelpers.Triple(x, terms.Type, classC)],
            forbidden: [xIsA],
            maintained: afterFirst);

        currentBase.Remove(xIsB);
        OwlRlResult afterSecond = engine.Apply([], [xIsB], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(x, terms.Type, classC),
                xIsA,
                xIsB,
            ],
            maintained: afterSecond);
    }

    /// <summary>
    /// MinCardinalityMembershipLifecycle. A min-cardinality-1 restriction's
    /// membership rides one witnessing edge: a second edge keeps it alive
    /// through the first retract, the last edge's retract removes it, and
    /// retracting the bound removes it even with an edge present.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. The min-cardinality-1 membership is
    /// <c>edge(u,p,v) ∧ minCardinality(x,1) ∧ onProperty(x,p) →
    /// type(u,x)</c>. The restriction node carries no other detail and no
    /// typing, so no other rule fires on the shape; the only derivation is
    /// <c>u:x</c> whenever both the bound and at least one <c>p</c>-edge
    /// off <c>u</c> are present.
    /// </para>
    /// <para>
    /// Op0, base {onProperty, minCardinality 1, u→v}: Derived = {u:x}.
    /// Op1 adds u→w: Derived = {u:x} (a second witness changes nothing).
    /// Op2 retracts u→v: Derived = {u:x} (rederived from the surviving
    /// u→w). Op3 retracts u→w: no witness remains, Derived = ∅. Op4 adds
    /// u→v back and retracts the minCardinality triple in the same op: no
    /// bound remains, Derived = ∅ with the edge present.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void MinCardinalityMembershipSurvivesRetractsExactlyWhileWitnessed()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "minone");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId w = OwlRlBatteryHelpers.Mint(dictionary, "w");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdInteger);

        EncodedTriple onProperty = OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p);
        EncodedTriple minCardinality = OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one);
        EncodedTriple uv = OwlRlBatteryHelpers.Triple(u, p, v);
        EncodedTriple uw = OwlRlBatteryHelpers.Triple(u, p, w);
        EncodedTriple membership = OwlRlBatteryHelpers.Triple(u, terms.Type, x);

        HashSet<EncodedTriple> currentBase = [onProperty, minCardinality, uv];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [membership], forbidden: [uw], maintained: engine.Current);

        currentBase.Add(uw);
        OwlRlResult afterSecondWitness = engine.Apply([uw], [], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [membership], forbidden: [], maintained: afterSecondWitness);

        currentBase.Remove(uv);
        OwlRlResult afterFirstRetract = engine.Apply([], [uv], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [membership], forbidden: [uv], maintained: afterFirstRetract);

        currentBase.Remove(uw);
        OwlRlResult afterLastWitnessRetract = engine.Apply([], [uw], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [], forbidden: [membership, uv, uw], maintained: afterLastWitnessRetract);

        currentBase.Add(uv);
        currentBase.Remove(minCardinality);
        OwlRlResult afterBoundRetract = engine.Apply([uv], [minCardinality], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [], forbidden: [membership, minCardinality], maintained: afterBoundRetract);
    }

    /// <summary>
    /// ClashFormFalsityCycle. Each clash-form falsity flips a consistent
    /// maintained closure inconsistent through an incremental add, and the
    /// following retract rebuilds it back to the consistent base — the
    /// nil structure clash, the Thing-enumeration clash, and the de-gated
    /// negative-assertion clash, in one op history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. The standing base is the single unrelated edge
    /// <c>a q b</c>, which no rule consumes: its closure derives nothing.
    /// Each falsity op adds exactly the offending fact(s): an
    /// <c>rdf:first</c> edge on <c>rdf:nil</c>; an <c>owl:oneOf</c> of
    /// <c>owl:Thing</c> over the empty list; the three negative-assertion
    /// helper triples with the matching positive edge. Each is
    /// inconsistent under its own rule, and each retract restores the
    /// empty derivation. The final op retracts only the positive edge, so
    /// the helper triples remain and derive nothing.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ClashFormFalsitiesFlipAndRebuildTheMaintainedClosure()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId m = OwlRlBatteryHelpers.Mint(dictionary, "m");
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");

        EncodedTriple standing = OwlRlBatteryHelpers.Triple(a, q, b);
        EncodedTriple nilFirst = OwlRlBatteryHelpers.Triple(terms.Nil, terms.First, m);
        EncodedTriple thingEnumeration = OwlRlBatteryHelpers.Triple(terms.Thing, terms.OneOf, terms.Nil);
        EncodedTriple sourceHelper = OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s);
        EncodedTriple propertyHelper = OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p);
        EncodedTriple targetHelper = OwlRlBatteryHelpers.Triple(z, terms.TargetIndividual, o);
        EncodedTriple positiveEdge = OwlRlBatteryHelpers.Triple(s, p, o);

        HashSet<EncodedTriple> currentBase = [standing];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [], forbidden: [nilFirst], maintained: engine.Current);

        currentBase.Add(nilFirst);
        OwlRlResult nilClash = engine.Apply([nilFirst], [], TestContext.CancellationToken);
        AssertInconsistent(currentBase, terms, default, EntailmentRules.NilStructureClash, nilClash);

        currentBase.Remove(nilFirst);
        OwlRlResult afterNilRetract = engine.Apply([], [nilFirst], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [], forbidden: [nilFirst], maintained: afterNilRetract);

        currentBase.Add(thingEnumeration);
        OwlRlResult thingClash = engine.Apply([thingEnumeration], [], TestContext.CancellationToken);
        AssertInconsistent(currentBase, terms, default, EntailmentRules.ThingEnumerationClash, thingClash);

        currentBase.Remove(thingEnumeration);
        OwlRlResult afterThingRetract = engine.Apply([], [thingEnumeration], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [], forbidden: [thingEnumeration], maintained: afterThingRetract);

        currentBase.Add(sourceHelper);
        currentBase.Add(propertyHelper);
        currentBase.Add(targetHelper);
        currentBase.Add(positiveEdge);
        OwlRlResult npaClash = engine.Apply([sourceHelper, propertyHelper, targetHelper, positiveEdge], [], TestContext.CancellationToken);
        AssertInconsistent(currentBase, terms, default, EntailmentRules.PrpNpa, npaClash);

        currentBase.Remove(positiveEdge);
        OwlRlResult afterEdgeRetract = engine.Apply([], [positiveEdge], TestContext.CancellationToken);
        AssertExactClosure(currentBase, terms, default, expectedDerived: [], forbidden: [positiveEdge], maintained: afterEdgeRetract);
    }

    /// <summary>
    /// SameAsUnMerge. Two two-member cliques (<c>a1≡a2</c>, <c>b1≡b2</c>)
    /// bridged by <c>a2≡b1</c> merge into one four-member congruence class;
    /// retracting the bridge tears them back into two, each keeping its own
    /// congruence and no cross-clique fact surviving.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. eq-sym and eq-trans close owl:sameAs to the full
    /// symmetric-transitive relation over the connected members, so a merged
    /// clique of members M closes owl:sameAs to the full M×M product. eq-rep
    /// replays each data triple onto every member of the subject's class.
    /// eq-ref adds the reflexive owl:sameAs of every mentioned term — members,
    /// non-members, and vocabulary terms alike — which the harness completion
    /// folds into the expectation; no data replay reaches a non-member.
    /// </para>
    /// <para>
    /// Merged over M={a1,a2,b1,b2}, base sameAs {a1≡a2, b1≡b2, a2≡b1} and data
    /// {a1 P u, b2 P w}: owl:sameAs = all 16 ordered pairs (3 asserted, 13
    /// derived); data = every member with P u and P w (2 asserted, 6 derived).
    /// Derived = 13 + 6 = 19 clique facts beside the folded reflexives. No
    /// equality between the non-members u and w ever appears.
    /// </para>
    /// <para>
    /// After retracting a2≡b1: cliques {a1,a2} and {b1,b2}. Each closes
    /// owl:sameAs to its 2×2 product (1 asserted, 3 derived) and keeps its own
    /// datum on both members (1 asserted, 1 derived). Derived = 6 owl:sameAs
    /// {a1≡a1, a2≡a2, a2≡a1, b1≡b1, b2≡b2, b2≡b1} + 2 data {a2 P u, b1 P w} =
    /// 8 facts. No cross-clique owl:sameAs and no cross datum (b1/b2 P u, a1/a2
    /// P w) remains.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void SameAsUnMergeKeepsPerCliqueCongruenceOnly()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a1 = OwlRlBatteryHelpers.Mint(dictionary, "a1");
        TermId a2 = OwlRlBatteryHelpers.Mint(dictionary, "a2");
        TermId b1 = OwlRlBatteryHelpers.Mint(dictionary, "b1");
        TermId b2 = OwlRlBatteryHelpers.Mint(dictionary, "b2");
        TermId prop = OwlRlBatteryHelpers.Mint(dictionary, "P");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId w = OwlRlBatteryHelpers.Mint(dictionary, "w");

        EncodedTriple Same(TermId x, TermId y) => OwlRlBatteryHelpers.Triple(x, terms.SameAs, y);
        EncodedTriple Data(TermId x, TermId value) => OwlRlBatteryHelpers.Triple(x, prop, value);

        EncodedTriple bridge = Same(a2, b1);
        HashSet<EncodedTriple> currentBase = [Same(a1, a2), Same(b1, b2), bridge, Data(a1, u), Data(b2, w)];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);

        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                Same(a1, a1), Same(a2, a2), Same(b1, b1), Same(b2, b2),
                Same(a2, a1),
                Same(a1, b1), Same(b1, a1),
                Same(a1, b2), Same(b2, a1),
                Same(b1, a2),
                Same(a2, b2), Same(b2, a2),
                Same(b2, b1),
                Data(a2, u), Data(b1, u), Data(b2, u),
                Data(a1, w), Data(a2, w), Data(b1, w),
            ],
            forbidden: [Same(u, w), Same(w, u)],
            maintained: engine.Current);

        currentBase.Remove(bridge);
        OwlRlResult afterUnMerge = engine.Apply([], [bridge], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                Same(a1, a1), Same(a2, a2), Same(a2, a1),
                Same(b1, b1), Same(b2, b2), Same(b2, b1),
                Data(a2, u), Data(b1, w),
            ],
            forbidden:
            [
                Same(a1, b1), Same(a1, b2), Same(a2, b1), Same(a2, b2),
                Same(b1, a2), Same(b2, a1),
                Data(b1, u), Data(b2, u), Data(a1, w), Data(a2, w),
            ],
            maintained: afterUnMerge);
    }

    /// <summary>
    /// InverseCharacteristicRetract. A functional <c>p</c> with
    /// <c>p owl:inverseOf q</c> transfers inverse-functionality onto
    /// <c>q</c>; retracting the inverse statement removes the transferred
    /// typing and everything downstream of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. The transfer concludes <c>q rdf:type
    /// owl:InverseFunctionalProperty</c>. Both characteristic typings feed
    /// cax-sco over the axiomatic characteristic subsumptions, typing
    /// <c>p</c> and <c>q</c> <c>owl:ObjectProperty</c>, and each object
    /// property gains its scm-op self-subsumption pair. No data edges
    /// exist, so no prp-* rule fires.
    /// </para>
    /// <para>
    /// After retracting the inverse statement only <c>p</c>'s side
    /// survives: the transferred typing, <c>q</c>'s object-property typing,
    /// and <c>q</c>'s self-subsumption pair all vanish.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void InverseCharacteristicRetractRemovesTheTransferredTyping()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");

        EncodedTriple functional = OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty);
        EncodedTriple inverse = OwlRlBatteryHelpers.Triple(p, terms.InverseOf, q);

        HashSet<EncodedTriple> currentBase = [functional, inverse];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, p),
                OwlRlBatteryHelpers.Triple(p, terms.EquivalentProperty, p),
                OwlRlBatteryHelpers.Triple(q, terms.SubPropertyOf, q),
                OwlRlBatteryHelpers.Triple(q, terms.EquivalentProperty, q),
            ],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.FunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.InverseFunctionalProperty),
            ],
            maintained: engine.Current);

        currentBase.Remove(inverse);
        OwlRlResult afterInverse = engine.Apply([], [inverse], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, p),
                OwlRlBatteryHelpers.Triple(p, terms.EquivalentProperty, p),
            ],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(q, terms.SubPropertyOf, q),
                OwlRlBatteryHelpers.Triple(q, terms.EquivalentProperty, q),
            ],
            maintained: afterInverse);
    }

    /// <summary>
    /// EnumerationComparisonRetract. Two singleton enumerations over one
    /// member subsume both ways and close into the equivalence; a
    /// complement statement materialises symmetrically. Retracting the
    /// second enumeration axiom tears down the comparison family, and
    /// retracting the complement statement removes its mate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. The member-subset comparison concludes
    /// <c>c1⊑c2</c> and <c>c2⊑c1</c> (equal singleton sets), scm-eqc2
    /// closes the mutual subsumption into both equivalence orientations,
    /// and cls-oo types the shared member into both enumerations. The
    /// mutual pair composes through scm-sco into each class's reflexive
    /// subsumption, which scm-eqc2 closes into each reflexive
    /// equivalence. The classes are never typed <c>owl:Class</c>, so
    /// scm-cls stays silent.
    /// </para>
    /// <para>
    /// After retracting <c>c2</c>'s enumeration axiom a single enumeration
    /// remains, so the whole comparison family and <c>c2</c>'s membership
    /// vanish while <c>c1</c>'s cls-oo membership stands; the orphaned
    /// list cells of <c>c2</c> derive nothing. After retracting the
    /// complement statement its symmetric mate vanishes too.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EnumerationComparisonRetractTearsDownTheSubsumptions()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c1 = OwlRlBatteryHelpers.Mint(dictionary, "c1");
        TermId c2 = OwlRlBatteryHelpers.Mint(dictionary, "c2");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId d1 = OwlRlBatteryHelpers.Mint(dictionary, "d1");
        TermId d2 = OwlRlBatteryHelpers.Mint(dictionary, "d2");

        List<EncodedTriple> assembled = [];
        TermId firstHead = OwlRlBatteryHelpers.AddList(assembled, dictionary, terms, [a], "first");
        TermId secondHead = OwlRlBatteryHelpers.AddList(assembled, dictionary, terms, [a], "second");
        EncodedTriple firstEnumeration = OwlRlBatteryHelpers.Triple(c1, terms.OneOf, firstHead);
        EncodedTriple secondEnumeration = OwlRlBatteryHelpers.Triple(c2, terms.OneOf, secondHead);
        EncodedTriple complement = OwlRlBatteryHelpers.Triple(d1, terms.ComplementOf, d2);

        HashSet<EncodedTriple> currentBase = [.. assembled, firstEnumeration, secondEnumeration, complement];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                OwlRlBatteryHelpers.Triple(c1, terms.SubClassOf, c2),
                OwlRlBatteryHelpers.Triple(c2, terms.SubClassOf, c1),
                OwlRlBatteryHelpers.Triple(c1, terms.SubClassOf, c1),
                OwlRlBatteryHelpers.Triple(c2, terms.SubClassOf, c2),
                OwlRlBatteryHelpers.Triple(c1, terms.EquivalentClass, c2),
                OwlRlBatteryHelpers.Triple(c2, terms.EquivalentClass, c1),
                OwlRlBatteryHelpers.Triple(c1, terms.EquivalentClass, c1),
                OwlRlBatteryHelpers.Triple(c2, terms.EquivalentClass, c2),
                OwlRlBatteryHelpers.Triple(a, terms.Type, c1),
                OwlRlBatteryHelpers.Triple(a, terms.Type, c2),
                OwlRlBatteryHelpers.Triple(d2, terms.ComplementOf, d1),
            ],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(c1, terms.SubClassOf, d1),
                OwlRlBatteryHelpers.Triple(d1, terms.SubClassOf, c1),
            ],
            maintained: engine.Current);

        currentBase.Remove(secondEnumeration);
        OwlRlResult afterEnumeration = engine.Apply([], [secondEnumeration], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                OwlRlBatteryHelpers.Triple(a, terms.Type, c1),
                OwlRlBatteryHelpers.Triple(d2, terms.ComplementOf, d1),
            ],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(c1, terms.SubClassOf, c2),
                OwlRlBatteryHelpers.Triple(c2, terms.SubClassOf, c1),
                OwlRlBatteryHelpers.Triple(c1, terms.SubClassOf, c1),
                OwlRlBatteryHelpers.Triple(c2, terms.SubClassOf, c2),
                OwlRlBatteryHelpers.Triple(c1, terms.EquivalentClass, c2),
                OwlRlBatteryHelpers.Triple(c2, terms.EquivalentClass, c1),
                OwlRlBatteryHelpers.Triple(c1, terms.EquivalentClass, c1),
                OwlRlBatteryHelpers.Triple(c2, terms.EquivalentClass, c2),
                OwlRlBatteryHelpers.Triple(a, terms.Type, c2),
            ],
            maintained: afterEnumeration);

        currentBase.Remove(complement);
        OwlRlResult afterComplement = engine.Apply([], [complement], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [OwlRlBatteryHelpers.Triple(a, terms.Type, c1)],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(d2, terms.ComplementOf, d1),
                OwlRlBatteryHelpers.Triple(a, terms.Type, c2),
            ],
            maintained: afterComplement);
    }

    /// <summary>
    /// SingletonEnumerationListCellRetract. A singleton-enumeration range
    /// makes <c>p</c> functional and the transfer carries the exchanged
    /// characteristic across <c>p owl:inverseOf q</c>; retracting the
    /// enumeration's <c>rdf:first</c> cell breaks the list and both
    /// characteristics vanish with everything downstream — the cascade
    /// crosses two residue completions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. The singleton range concludes <c>p rdf:type
    /// owl:FunctionalProperty</c>, the transfer concludes <c>q rdf:type
    /// owl:InverseFunctionalProperty</c>, cax-sco over the axiomatic
    /// characteristic subsumptions types both <c>owl:ObjectProperty</c>,
    /// scm-op adds each self-subsumption pair, and cls-oo types the member
    /// into the enumeration. No data edges exist.
    /// </para>
    /// <para>
    /// Retracting the <c>rdf:first</c> cell is a list-structure edit: the
    /// walk answers no list, so every conclusion above vanishes — nothing
    /// remains derived.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void SingletonEnumerationListCellRetractRemovesTheCharacteristic()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId singleton = OwlRlBatteryHelpers.Mint(dictionary, "singleton");
        TermId member = OwlRlBatteryHelpers.Blank(dictionary, "member");

        List<EncodedTriple> assembled = [];
        TermId head = OwlRlBatteryHelpers.AddList(assembled, dictionary, terms, [member], "single");
        EncodedTriple firstCell = OwlRlBatteryHelpers.Triple(head, terms.First, member);

        HashSet<EncodedTriple> currentBase =
        [
            .. assembled,
            OwlRlBatteryHelpers.Triple(p, terms.Range, singleton),
            OwlRlBatteryHelpers.Triple(singleton, terms.OneOf, head),
            OwlRlBatteryHelpers.Triple(p, terms.InverseOf, q),
        ];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, p),
                OwlRlBatteryHelpers.Triple(p, terms.EquivalentProperty, p),
                OwlRlBatteryHelpers.Triple(q, terms.SubPropertyOf, q),
                OwlRlBatteryHelpers.Triple(q, terms.EquivalentProperty, q),
                OwlRlBatteryHelpers.Triple(member, terms.Type, singleton),
            ],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.InverseFunctionalProperty),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.FunctionalProperty),
            ],
            maintained: engine.Current);

        currentBase.Remove(firstCell);
        OwlRlResult afterCell = engine.Apply([], [firstCell], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived: [],
            forbidden:
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(q, terms.Type, terms.ObjectPropertyTerm),
                OwlRlBatteryHelpers.Triple(member, terms.Type, singleton),
            ],
            maintained: afterCell);
    }

    /// <summary>
    /// FalsityRetract. <c>x</c> typed into disjoint <c>C</c> and <c>D</c>
    /// fires the cax-dw falsity; retracting one clashing typing flips the
    /// verdict to consistent and surfaces the derivations the halted fixpoint
    /// had suppressed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-derivation. cax-dw both derives the symmetric <c>D disjointWith
    /// C</c> and, on <c>type(x,C) ∧ type(x,D)</c>, reports inconsistency. Op0,
    /// base {C⊥D, C⊑E, x:C, x:D}: inconsistent, falsity rule cax-dw.
    /// </para>
    /// <para>
    /// Op1 retracts x:D, base {C⊥D, C⊑E, x:C}: consistent. cax-dw still
    /// derives <c>D disjointWith C</c>, and cax-sco derives <c>x:E</c> from
    /// C⊑E and x:C — the typing suppressed while the fixpoint was halted.
    /// Derived = {D⊥C, x:E}.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void FalsityRetractFlipsConsistentAndSurfacesSuppressed()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId classE = OwlRlBatteryHelpers.Mint(dictionary, "E");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple disjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple cSubE = OwlRlBatteryHelpers.Triple(classC, terms.SubClassOf, classE);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);

        HashSet<EncodedTriple> currentBase = [disjoint, cSubE, xIsC, xIsD];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertInconsistent(currentBase, terms, default, EntailmentRules.CaxDw, maintained: engine.Current);

        currentBase.Remove(xIsD);
        OwlRlResult flipped = engine.Apply([], [xIsD], TestContext.CancellationToken);
        AssertExactClosure(
            currentBase,
            terms,
            default,
            expectedDerived:
            [
                OwlRlBatteryHelpers.Triple(classD, terms.DisjointWith, classC),
                OwlRlBatteryHelpers.Triple(x, terms.Type, classE),
            ],
            forbidden:
            [
                xIsD,
                OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classC),
                OwlRlBatteryHelpers.Triple(classD, terms.DisjointWith, classD),
            ],
            maintained: flipped);

        //The retract from an inconsistent base rides the from-scratch rebuild.
        Assert.AreEqual(OwlRlMaintenanceMode.RebuildInconsistent, engine.Statistics.Mode, "A retract from an inconsistent base must rebuild from scratch.");
    }


    /// <summary>Random op sequences over subclass/subproperty/domain/range closures, equivalentClass two-cycles and class declarations keep the maintained closure equal to the naive oracle after every op.</summary>
    [TestMethod]
    public void SchemaClosureOpSequencesMatchOracle()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        SampleFamily(SchemaPool(dictionary, terms), terms, default, SchemaSeed);
    }

    /// <summary>Random op sequences over property characteristics — transitive, symmetric, functional, inverse-functional, irreflexive, asymmetric — some driving inconsistency, keep the maintained closure equal to the naive oracle after every op.</summary>
    [TestMethod]
    public void CharacteristicOpSequencesMatchOracle()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        SampleFamily(CharacteristicPool(dictionary, terms), terms, default, CharacteristicSeed);
    }

    /// <summary>Random op sequences over inverses, equivalences, subproperties and property chains (2-link and p∘p⊑p) keep the maintained closure equal to the naive oracle after every op, including bases with half-retracted chain lists.</summary>
    [TestMethod]
    public void InverseAndChainOpSequencesMatchOracle()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        SampleFamily(InverseChainPool(dictionary, terms), terms, default, InverseChainSeed);
    }

    /// <summary>Random op sequences over sameAs chains and cliques with per-entity data, differentFrom, a functional-property merge and numeric literals under the dictionary oracle keep the maintained closure equal to the naive oracle after every op.</summary>
    [TestMethod]
    public void EqualityChurnOpSequencesMatchOracle()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        SampleFamily(EqualityPool(dictionary, terms), terms, oracle, EqualitySeed);
    }

    /// <summary>An all-adds sequence reproduces the Phase-0 add-only differential behaviour — the sanity bridge to the semi-naive battery — with the checkpoint contract holding at every growth step.</summary>
    [TestMethod]
    public void AddOnlySequenceMatchesOracle()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        IReadOnlyList<EncodedTriple> pool = SchemaPool(dictionary, terms);

        Gen<int> genIndex = Gen.Int[0, pool.Count - 1];
        Gen<OpSpec> genAdd = genIndex.Array[1, MaxSetSize].Select(indices => new OpSpec(false, indices));
        Gen<(int[] Initial, OpSpec[] Ops)> gen =
            genIndex.Array[0, MaxInitial].SelectMany(initial => genAdd.Array[1, MaxOps].Select(ops => (initial, ops)));

        gen.Sample(
            spec =>
            {
                MaintainedClosureAdapter adapter = new();
                RunOpSequence(terms, default, pool, spec.Initial, spec.Ops, adapter.Maintain, TestContext.CancellationToken);
            },
            seed: AddOnlySeed,
            iter: FamilyIterations);
    }


    /// <summary>Two op sequences with the same net delta but different orderings — including a mid-sequence add/retract cancellation — converge to the same closure, and the checkpoint contract holds throughout both.</summary>
    [TestMethod]
    public void ReorderedSequencesWithEqualNetDeltaConverge()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(a, terms.SubClassOf, b);
        EncodedTriple bSubC = OwlRlBatteryHelpers.Triple(b, terms.SubClassOf, c);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, a);
        EncodedTriple yIsB = OwlRlBatteryHelpers.Triple(y, terms.Type, b);
        EncodedTriple transient = OwlRlBatteryHelpers.Triple(x, terms.Type, c);

        //Order one: build up, then drop the transient assertion that a
        //cax-sco derivation already covers. Order two: interleave the same
        //adds and the transient's add/retract differently. Both nets are
        //{aSubB, bSubC, xIsA, yIsB}.
        OwlRlResult end1 = PlaySequence(terms, default,
        [
            (Adds: [aSubB, transient], Retracts: []),
            (Adds: [xIsA, bSubC], Retracts: []),
            (Adds: [yIsB], Retracts: [transient]),
        ]);

        OwlRlResult end2 = PlaySequence(terms, default,
        [
            (Adds: [yIsB], Retracts: []),
            (Adds: [bSubC, xIsA], Retracts: []),
            (Adds: [transient], Retracts: []),
            (Adds: [aSubB], Retracts: [transient]),
        ]);

        HashSet<EncodedTriple> derived1 = [.. end1.Derived];
        Assert.IsTrue(derived1.SetEquals(end2.Derived), "Equal net deltas must reach equal closures regardless of op ordering.");
        Assert.AreEqual(end1.IsConsistent, end2.IsConsistent);
    }

    /// <summary>Re-adding a present triple and retracting an absent one are closure no-ops, and two independent add-sets commute.</summary>
    [TestMethod]
    public void IdempotentReAddAbsentRetractAndCommutativeAdds()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(a, terms.SubClassOf, b);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, a);
        EncodedTriple absent = OwlRlBatteryHelpers.Triple(x, terms.Type, b);

        HashSet<EncodedTriple> baseSet = [aSubB, xIsA];
        OwlRlMaintainedClosure engine = new(baseSet, terms, cancellationToken: TestContext.CancellationToken);
        HashSet<EncodedTriple> reference = [.. engine.Current.Derived];

        //Re-adding aSubB (already present) is a base no-op, so the closure is
        //unchanged.
        engine.Apply([aSubB], [], TestContext.CancellationToken);
        HashSet<EncodedTriple> afterReAdd = [.. engine.Current.Derived];
        Assert.IsTrue(reference.SetEquals(afterReAdd), "Re-adding a present triple must not change the closure.");

        //Retracting `absent` (never in the base — it is a derived conclusion,
        //not a base triple) is a base no-op, so the closure is unchanged.
        engine.Apply([], [absent], TestContext.CancellationToken);
        HashSet<EncodedTriple> afterAbsentRetract = [.. engine.Current.Derived];
        Assert.IsTrue(reference.SetEquals(afterAbsentRetract), "Retracting a derived-but-not-base triple must not change the closure.");

        //Two independent add-sets commute: {p domain A} then {y p x} equals
        //the reverse, applied through two engines over the same base.
        EncodedTriple pDomainA = OwlRlBatteryHelpers.Triple(p, terms.Domain, a);
        EncodedTriple ypx = OwlRlBatteryHelpers.Triple(y, p, x);

        OwlRlMaintainedClosure forward = new(baseSet, terms, cancellationToken: TestContext.CancellationToken);
        forward.Apply([pDomainA], [], TestContext.CancellationToken);
        forward.Apply([ypx], [], TestContext.CancellationToken);
        HashSet<EncodedTriple> forwardDerived = [.. forward.Current.Derived];

        OwlRlMaintainedClosure backward = new(baseSet, terms, cancellationToken: TestContext.CancellationToken);
        backward.Apply([ypx], [], TestContext.CancellationToken);
        backward.Apply([pDomainA], [], TestContext.CancellationToken);
        HashSet<EncodedTriple> backwardDerived = [.. backward.Current.Derived];

        Assert.IsTrue(forwardDerived.SetEquals(backwardDerived), "Independent add-sets must commute to the same closure.");
    }

    /// <summary>Retracting arbitrary base triples never drops the engine-seeded datatype-hierarchy facts nor the property-chain list nodes: both survive in the closure of the remaining base.</summary>
    /// <remarks>
    /// The datatype hierarchy is seeded into every closure independent of the
    /// base (it is entailed by the empty graph), so <see cref="OwlRlClosure.Compute"/>
    /// over the empty base yields exactly that axiomatic set; it must remain a
    /// subset of any consistent base's closure. The property-chain list nodes
    /// are base triples the retract leaves in place, so they and the chain's
    /// derivations survive.
    /// </remarks>
    [TestMethod]
    public void SeedAndChainImmuneToArbitraryRetracts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId noise1 = OwlRlBatteryHelpers.Mint(dictionary, "noise1");
        TermId noise2 = OwlRlBatteryHelpers.Mint(dictionary, "noise2");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "filler");

        HashSet<EncodedTriple> seedSet = [.. OwlRlClosure.Compute([], terms, oracle, cancellationToken: TestContext.CancellationToken).Derived];
        Assert.IsNotEmpty(seedSet, "The datatype hierarchy must seed a non-empty axiomatic set.");

        List<EncodedTriple> chainBase = [];
        OwlRlBatteryHelpers.AddChainAxiom(chainBase, dictionary, terms, p, [p, p], "immunity");
        EncodedTriple listHeadFirst = chainBase[0];
        EncodedTriple ab = OwlRlBatteryHelpers.Triple(a, p, b);
        EncodedTriple bc = OwlRlBatteryHelpers.Triple(b, p, c);
        EncodedTriple noiseEdge1 = OwlRlBatteryHelpers.Triple(noise1, p, filler);
        EncodedTriple noiseEdge2 = OwlRlBatteryHelpers.Triple(noise2, p, filler);

        HashSet<EncodedTriple> fullBase = [.. chainBase, ab, bc, noiseEdge1, noiseEdge2];

        //Drive an engine through the two noise retracts; the chain axiom, its
        //list nodes and the a→b→c edges stay.
        OwlRlMaintainedClosure engine = new(fullBase, terms, oracle, TestContext.CancellationToken);
        engine.Apply([], [noiseEdge1], TestContext.CancellationToken);
        engine.Apply([], [noiseEdge2], TestContext.CancellationToken);

        HashSet<EncodedTriple> currentBase = [.. fullBase];
        currentBase.Remove(noiseEdge1);
        currentBase.Remove(noiseEdge2);

        OwlRlResult closure = OwlRlClosure.Compute(currentBase, terms, oracle, cancellationToken: TestContext.CancellationToken);
        HashSet<EncodedTriple> present = [.. currentBase, .. closure.Derived];

        Assert.IsTrue(closure.IsConsistent, "The remaining base must stay consistent.");
        Assert.IsTrue(present.IsSupersetOf(seedSet), "A base retract must never drop the engine-seeded datatype-hierarchy facts.");
        Assert.Contains(listHeadFirst, present, "The property-chain list node must survive an unrelated retract.");
        Assert.Contains(OwlRlBatteryHelpers.Triple(a, p, c), present, "The chain p∘p⊑p must still derive a→c over the surviving edges.");

        OwlRlResult maintainedClosure = engine.Current;
        HashSet<EncodedTriple> maintainedPresent = [.. currentBase, .. maintainedClosure.Derived];
        Assert.IsTrue(maintainedClosure.IsConsistent, "The maintained closure over the remaining base must stay consistent.");
        Assert.IsTrue(maintainedPresent.IsSupersetOf(seedSet), "A maintained base retract must never drop the engine-seeded datatype-hierarchy facts.");
        Assert.Contains(listHeadFirst, maintainedPresent, "The maintained property-chain list node must survive an unrelated retract.");
        Assert.Contains(OwlRlBatteryHelpers.Triple(a, p, c), maintainedPresent, "The maintained chain p∘p⊑p must still derive a→c over the surviving edges.");
    }


    /// <summary>
    /// The exact composition the maintenance benchmark uses as its correct-but-slow
    /// oracle: an op sequence driven through the world-DAG — build a
    /// <see cref="MutableSparqlDataset"/>, fork a world, and per op open a
    /// session, apply the delta, and commit — then materialize the RL closure
    /// over the fork with <see cref="OwlRlMaterialization.MaterializeAndCommitAsync"/>
    /// and compare, at each step, against a direct <see cref="OwlRlClosure.Compute"/>
    /// over a mirrored base. The world's net delta from its origin is also
    /// pinned through <see cref="MutableSparqlDataset.DiffFrom"/>.
    /// </summary>
    [TestMethod]
    public async Task ForkApplyDeltaRematMatchesDirectComputePerOp()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "wp");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "wa");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "wb");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "wc");
        TermId d = OwlRlBatteryHelpers.Mint(dictionary, "wd");
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "WA");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "WC");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "wx");
        TermId n1 = OwlRlBatteryHelpers.Mint(dictionary, "wn1");
        TermId n2 = OwlRlBatteryHelpers.Mint(dictionary, "wn2");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "wq");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "wv");

        (EncodedTriple[] Adds, EncodedTriple[] Removals)[] sequence =
        [
            ([OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty), OwlRlBatteryHelpers.Triple(a, p, b), OwlRlBatteryHelpers.Triple(b, p, c)], []),
            ([OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classC), OwlRlBatteryHelpers.Triple(x, terms.Type, classA)], []),
            ([OwlRlBatteryHelpers.Triple(n1, terms.SameAs, n2), OwlRlBatteryHelpers.Triple(n1, q, v)], []),
            ([OwlRlBatteryHelpers.Triple(c, p, d)], [OwlRlBatteryHelpers.Triple(a, p, b)]),
            ([], [OwlRlBatteryHelpers.Triple(n1, terms.SameAs, n2)]),
        ];

        MutableSparqlDataset dataset = await MutableSparqlDataset.CreateAsync(dictionary, [], cancellationToken: cancellationToken).ConfigureAwait(false);
        MutableSparqlDataset world = await dataset.ForkAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        HashSet<EncodedTriple> mirror = [];
        OwlRlMaintainedClosure engine = new([], terms, cancellationToken: cancellationToken);

        foreach((EncodedTriple[] adds, EncodedTriple[] removals) in sequence)
        {
            mirror.UnionWith(adds);
            mirror.ExceptWith(removals);

            DatasetEditSession session = await world.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            await using(session.ConfigureAwait(false))
            {
                await session.ApplyDeltaAsync(TermId.None, adds, removals, cancellationToken).ConfigureAwait(false);
                await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            OwlRlMaterializationResult materialized = await OwlRlMaterialization
                .MaterializeAndCommitAsync(world.DefaultGraph, terms, cancellationToken: cancellationToken).ConfigureAwait(false);
            OwlRlResult direct = OwlRlClosure.Compute([.. mirror], terms, cancellationToken: cancellationToken);
            OwlRlResult maintained = engine.Apply(adds, removals, cancellationToken);

            Assert.IsTrue(direct.IsConsistent, "The mirrored base must stay consistent through the sequence.");
            Assert.IsTrue(materialized.IsConsistent, "The world materialization must agree the base is consistent.");
            Assert.IsTrue(maintained.IsConsistent, "The maintained closure must agree the base is consistent.");

            HashSet<EncodedTriple> worldClosure = [.. materialized.Store.Match(TermId.None, TermId.None, TermId.None)];
            HashSet<EncodedTriple> expected = [.. mirror, .. direct.Derived];
            Assert.IsTrue(
                worldClosure.SetEquals(expected),
                $"Fork+apply-delta+remat must equal direct Compute over the mirror: world {worldClosure.Count} vs expected {expected.Count}.");

            HashSet<EncodedTriple> maintainedClosure = [.. mirror, .. maintained.Derived];
            Assert.IsTrue(
                maintainedClosure.SetEquals(expected),
                $"Maintained apply-delta must equal direct Compute over the mirror: maintained {maintainedClosure.Count} vs expected {expected.Count}.");
        }

        //The world's committed default graph diffed from its origin is exactly
        //the net base the mirror holds — the delta the benchmark feeds a remat.
        System.Collections.Immutable.ImmutableArray<DatasetGraphTransition> transitions = world.DiffFrom(dataset);
        HashSet<EncodedTriple> diffAdditions = [];
        foreach(DatasetGraphTransition transition in transitions)
        {
            if(transition.Graph.IsNone)
            {
                diffAdditions = [.. transition.Additions];
            }
        }

        Assert.IsTrue(diffAdditions.SetEquals(mirror), "DiffFrom the origin world must recover the net base applied to the fork.");
    }


    /// <summary>
    /// Certifies the harness's op bookkeeping through the maintenance seam: a
    /// maintain delegate that verifies the added, retracted and current-base
    /// arguments it receives are exactly what the runner should have computed,
    /// then recomputes. This is what proves the battery is not vacuous today —
    /// the checkpoint discipline the future incremental engine relies on is
    /// itself under test.
    /// </summary>
    [TestMethod]
    public void MaintainDelegateReceivesCorrectDeltaBookkeeping()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        IReadOnlyList<EncodedTriple> pool = SchemaPool(dictionary, terms);
        HashSet<EncodedTriple> mirror = [];
        int checkpoints = 0;

        MaintainClosureDelegate verifying = (previous, added, retracted, currentBase, closureTerms, oracle, token) =>
        {
            foreach(EncodedTriple triple in added)
            {
                Assert.DoesNotContain(triple, mirror, "An added triple must have been absent from the pre-op mirror.");
            }

            foreach(EncodedTriple triple in retracted)
            {
                Assert.Contains(triple, mirror, "A retracted triple must have been present in the pre-op mirror.");
            }

            mirror.ExceptWith(retracted);
            mirror.UnionWith(added);

            HashSet<EncodedTriple> observed = [.. currentBase];
            Assert.IsTrue(observed.SetEquals(mirror), "The current base handed to the seam must equal the folded delta history.");
            checkpoints++;

            return OwlRlClosure.Compute(currentBase, closureTerms, oracle, cancellationToken: token);
        };

        //One fixed op sequence over pool positions; the point is the seam
        //bookkeeping, so the ops are hand-listed rather than sampled.
        OpSpec[] ops =
        [
            new OpSpec(false, [0, 1, 2]),
            new OpSpec(false, [3, 4]),
            new OpSpec(true, [0]),
            new OpSpec(false, [2, 5]),
            new OpSpec(true, [1, 2]),
        ];

        RunOpSequence(terms, default, pool, [0, 1], ops, verifying, TestContext.CancellationToken);
        Assert.AreEqual(ops.Length + 1, checkpoints, "The seam must be consulted once per op plus the initial base.");
    }


    /// <summary>The reference maintain strategy: a full from-scratch closure over the current base. Routing a failing family through this instead of the engine adapter separates an engine defect from a harness defect in one edit.</summary>
    /// <param name="previousClosure">Ignored by the recompute strategy.</param>
    /// <param name="addedTriples">Ignored by the recompute strategy.</param>
    /// <param name="retractedTriples">Ignored by the recompute strategy.</param>
    /// <param name="currentBase">The base to close over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the dt-* falsities.</param>
    /// <param name="cancellationToken">A token that aborts derivation.</param>
    /// <returns>The closure of <paramref name="currentBase"/>.</returns>
    private static OwlRlResult MaintainByRecompute(
        OwlRlResult? previousClosure,
        IReadOnlyCollection<EncodedTriple> addedTriples,
        IReadOnlyCollection<EncodedTriple> retractedTriples,
        IReadOnlyCollection<EncodedTriple> currentBase,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle,
        CancellationToken cancellationToken)
    {
        return OwlRlClosure.Compute(currentBase, terms, datatypeOracle, cancellationToken: cancellationToken);
    }

    /// <summary>Samples a family's CsCheck op-sequence generator under its pinned seed, asserting the checkpoint contract after every op of every sampled sequence.</summary>
    /// <param name="pool">The family's candidate base triples.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle, or <see langword="default"/> to disable the dt-* falsities.</param>
    /// <param name="seed">The pinned PCG seed making the sample deterministic.</param>
    private void SampleFamily(IReadOnlyList<EncodedTriple> pool, OwlRlTerms terms, OwlRlDatatypeOracle oracle, string seed)
    {
        Gen<int> genIndex = Gen.Int[0, pool.Count - 1];
        Gen<int[]> genSet = genIndex.Array[1, MaxSetSize];
        Gen<OpSpec> genOp = Gen.Bool.SelectMany(isRetract => genSet.Select(indices => new OpSpec(isRetract, indices)));
        Gen<(int[] Initial, OpSpec[] Ops)> gen =
            genIndex.Array[0, MaxInitial].SelectMany(initial => genOp.Array[1, MaxOps].Select(ops => (initial, ops)));

        gen.Sample(
            spec =>
            {
                MaintainedClosureAdapter adapter = new();
                RunOpSequence(terms, oracle, pool, spec.Initial, spec.Ops, adapter.Maintain, TestContext.CancellationToken);
            },
            seed: seed,
            iter: FamilyIterations);
    }

    /// <summary>Replays an initial base and an op stream through the maintain seam, asserting the checkpoint contract after the initial base and after every op.</summary>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle, or <see langword="default"/> to disable the dt-* falsities.</param>
    /// <param name="pool">The candidate base triples add-ops draw from.</param>
    /// <param name="initialIndices">The pool positions seeding the initial base.</param>
    /// <param name="ops">The op stream.</param>
    /// <param name="maintain">The maintenance seam (default recompute, or an incremental engine).</param>
    /// <param name="cancellationToken">A token that aborts derivation.</param>
    private static void RunOpSequence(
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        IReadOnlyList<EncodedTriple> pool,
        IReadOnlyList<int> initialIndices,
        IReadOnlyList<OpSpec> ops,
        MaintainClosureDelegate maintain,
        CancellationToken cancellationToken)
    {
        HashSet<EncodedTriple> currentBase = [];
        List<EncodedTriple> initialAdds = [];
        foreach(int index in initialIndices)
        {
            EncodedTriple triple = pool[index % pool.Count];
            if(currentBase.Add(triple))
            {
                initialAdds.Add(triple);
            }
        }

        OwlRlResult previous = Checkpoint(null, initialAdds, [], currentBase, terms, oracle, maintain, cancellationToken);

        foreach(OpSpec op in ops)
        {
            List<EncodedTriple> added = [];
            List<EncodedTriple> retracted = [];

            if(op.IsRetract)
            {
                List<EncodedTriple> live = [.. currentBase];
                live.Sort();
                if(live.Count > 0)
                {
                    foreach(int index in op.Indices)
                    {
                        EncodedTriple triple = live[index % live.Count];
                        if(currentBase.Remove(triple))
                        {
                            retracted.Add(triple);
                        }
                    }
                }
            }
            else
            {
                foreach(int index in op.Indices)
                {
                    EncodedTriple triple = pool[index % pool.Count];
                    if(currentBase.Add(triple))
                    {
                        added.Add(triple);
                    }
                }
            }

            previous = Checkpoint(previous, added, retracted, currentBase, terms, oracle, maintain, cancellationToken);
        }
    }

    /// <summary>Runs one checkpoint: the maintained closure through the seam, the naive oracle from scratch, and the equality contract between them.</summary>
    /// <param name="previous">The previous checkpoint's closure, or <see langword="null"/> at the first.</param>
    /// <param name="added">The op's added triples.</param>
    /// <param name="retracted">The op's retracted triples.</param>
    /// <param name="currentBase">The base after the op.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="maintain">The maintenance seam.</param>
    /// <param name="cancellationToken">A token that aborts derivation.</param>
    /// <returns>The maintained closure, to carry into the next checkpoint.</returns>
    private static OwlRlResult Checkpoint(
        OwlRlResult? previous,
        IReadOnlyCollection<EncodedTriple> added,
        IReadOnlyCollection<EncodedTriple> retracted,
        IReadOnlyCollection<EncodedTriple> currentBase,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        MaintainClosureDelegate maintain,
        CancellationToken cancellationToken)
    {
        List<EncodedTriple> baseList = [.. currentBase];
        OwlRlResult maintained = maintain(previous, added, retracted, baseList, terms, oracle, cancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(baseList, terms, oracle, cancellationToken: cancellationToken);
        AssertCheckpoint(maintained, naive);

        return maintained;
    }

    /// <summary>Asserts the maintained-vs-naive checkpoint contract: equal derived sets on consistent bases, both inconsistent with a named falsity otherwise.</summary>
    /// <param name="maintained">The maintained closure.</param>
    /// <param name="naive">The naive oracle's closure.</param>
    private static void AssertCheckpoint(OwlRlResult maintained, OwlRlResult naive)
    {
        if(maintained.IsConsistent && naive.IsConsistent)
        {
            HashSet<EncodedTriple> maintainedDerived = [.. maintained.Derived];
            HashSet<EncodedTriple> naiveDerived = [.. naive.Derived];

            Assert.IsTrue(
                maintainedDerived.SetEquals(naiveDerived),
                $"Maintained derived {maintainedDerived.Count} triples; naive derived {naiveDerived.Count}. The maintained closure must equal the naive set.");
            Assert.IsTrue(
                naiveDerived.SetEquals(maintainedDerived),
                $"Naive derived {naiveDerived.Count} triples; maintained derived {maintainedDerived.Count}. The maintained closure must not invent a derivation.");

            return;
        }

        Assert.IsFalse(maintained.IsConsistent, "The naive oracle reported an inconsistency the maintained closure missed.");
        Assert.IsFalse(naive.IsConsistent, "The maintained closure reported an inconsistency the naive oracle missed.");
        Assert.IsNotNull(maintained.InconsistencyRule, "The maintained closure reported no falsity rule for an inconsistent base.");
        Assert.IsNotNull(naive.InconsistencyRule, "The naive oracle reported no falsity rule for an inconsistent base.");
    }

    /// <summary>Plays a fixed add/retract sequence and returns the final closure.</summary>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="ops">The ordered op stream as add/retract lists.</param>
    /// <returns>The closure of the final base.</returns>
    private OwlRlResult PlaySequence(
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        (EncodedTriple[] Adds, EncodedTriple[] Retracts)[] ops)
    {
        HashSet<EncodedTriple> currentBase = [];
        OwlRlMaintainedClosure engine = new(currentBase, terms, oracle, TestContext.CancellationToken);
        OwlRlResult result = engine.Current;

        foreach((EncodedTriple[] adds, EncodedTriple[] retracts) in ops)
        {
            currentBase.UnionWith(adds);
            currentBase.ExceptWith(retracts);
            OwlRlResult maintained = engine.Apply(adds, retracts, TestContext.CancellationToken);
            OwlRlResult naive = OwlRlClosure.ComputeNaive(currentBase, terms, oracle, cancellationToken: TestContext.CancellationToken);
            AssertCheckpoint(maintained, naive);
            result = maintained;
        }

        return result;
    }

    /// <summary>Asserts a consistent base's closure exactly: both engines derive precisely <paramref name="expectedDerived"/>, and none of <paramref name="forbidden"/> appears in the base or the closure.</summary>
    /// <param name="baseTriples">The base to close over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="expectedDerived">The hand-derived derived set both engines must produce exactly.</param>
    /// <param name="forbidden">Facts that must appear in neither the base nor the closure.</param>
    /// <param name="maintained">The maintained engine's result to hold to the same hand-derived set, or <see langword="null"/> to skip the maintained lane.</param>
    private void AssertExactClosure(
        IReadOnlyCollection<EncodedTriple> baseTriples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        IReadOnlyCollection<EncodedTriple> expectedDerived,
        IReadOnlyCollection<EncodedTriple> forbidden,
        OwlRlResult? maintained = null)
    {
        List<EncodedTriple> baseList = [.. baseTriples];
        OwlRlResult semiNaive = OwlRlClosure.Compute(baseList, terms, oracle, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(baseList, terms, oracle, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(semiNaive.IsConsistent, "The hand-built consistent shape must close consistently under Compute.");
        Assert.IsTrue(naive.IsConsistent, "The hand-built consistent shape must close consistently under the naive oracle.");

        //Every closure carries the datatype-hierarchy seed set as axiomatic
        //knowledge entailed by the empty graph, independent of the base. The
        //hand-built shapes use a vocabulary disjoint from the xsd datatypes,
        //so isolating the shape-specific derivations from that constant
        //baseline pins them exactly while the seeds are asserted present.
        HashSet<EncodedTriple> seedClosure = [.. OwlRlClosure.Compute([], terms, oracle, cancellationToken: TestContext.CancellationToken).Derived];
        HashSet<EncodedTriple> expected = [.. expectedDerived];

        //eq-ref forces the reflexive owl:sameAs of every term the base and
        //the hand-derived shape mention. The completion is definitional —
        //built here from the fixture's own triples, never read off the
        //engine — and rows the seed baseline already carries drop with the
        //seed exclusion below.
        AddSelfEqualities(expected, baseList, terms);
        AddSelfEqualities(expected, expectedDerived, terms);
        expected.ExceptWith(seedClosure);

        HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
        HashSet<EncodedTriple> naiveDerived = [.. naive.Derived];

        Assert.IsTrue(semiNaiveDerived.IsSupersetOf(seedClosure), "The engine-seeded datatype hierarchy must be present in the closure.");

        HashSet<EncodedTriple> semiNaiveShape = [.. semiNaiveDerived];
        semiNaiveShape.ExceptWith(seedClosure);
        HashSet<EncodedTriple> naiveShape = [.. naiveDerived];
        naiveShape.ExceptWith(seedClosure);

        Assert.IsTrue(
            semiNaiveShape.SetEquals(expected),
            $"Compute derived {semiNaiveShape.Count} shape triples (seeds excluded); the hand-derivation expected {expected.Count}. {Describe(expected, semiNaiveShape)}");
        Assert.IsTrue(
            naiveShape.SetEquals(expected),
            $"The naive oracle derived {naiveShape.Count} shape triples (seeds excluded); the hand-derivation expected {expected.Count}. {Describe(expected, naiveShape)}");

        HashSet<EncodedTriple> present = [.. baseList, .. semiNaive.Derived];
        foreach(EncodedTriple absent in forbidden)
        {
            Assert.DoesNotContain(absent, present, $"A forbidden fact appeared in the closure: ({absent.Subject.Encoded} {absent.Predicate.Encoded} {absent.Object.Encoded}).");
        }

        if(maintained is null)
        {
            return;
        }

        //The maintained engine's derived set, seeds excluded, must equal the
        //same hand-derived shape the from-scratch engines produced, and none of
        //the forbidden facts may appear.
        Assert.IsTrue(maintained.IsConsistent, "The maintained closure must close the consistent shape consistently.");
        HashSet<EncodedTriple> maintainedDerived = [.. maintained.Derived];
        Assert.IsTrue(maintainedDerived.IsSupersetOf(seedClosure), "The maintained closure must carry the engine-seeded datatype hierarchy.");

        HashSet<EncodedTriple> maintainedShape = [.. maintainedDerived];
        maintainedShape.ExceptWith(seedClosure);
        Assert.IsTrue(
            maintainedShape.SetEquals(expected),
            $"The maintained engine derived {maintainedShape.Count} shape triples (seeds excluded); the hand-derivation expected {expected.Count}. {Describe(expected, maintainedShape)}");

        HashSet<EncodedTriple> maintainedPresent = [.. baseList, .. maintained.Derived];
        foreach(EncodedTriple absent in forbidden)
        {
            Assert.DoesNotContain(absent, maintainedPresent, $"A forbidden fact appeared in the maintained closure: ({absent.Subject.Encoded} {absent.Predicate.Encoded} {absent.Object.Encoded}).");
        }
    }

    /// <summary>Appends the eq-ref self-equality of every term position of <paramref name="triples"/> to the expected set — the definitional completion the reflexivity rule forces over a shape's vocabulary.</summary>
    /// <param name="expectedToAppendTo">The expected derived set being completed.</param>
    /// <param name="triples">The triples whose terms gain a reflexive <c>owl:sameAs</c>.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    private static void AddSelfEqualities(HashSet<EncodedTriple> expectedToAppendTo, IEnumerable<EncodedTriple> triples, OwlRlTerms terms)
    {
        foreach(EncodedTriple triple in triples)
        {
            expectedToAppendTo.Add(OwlRlBatteryHelpers.Triple(triple.Subject, terms.SameAs, triple.Subject));
            expectedToAppendTo.Add(OwlRlBatteryHelpers.Triple(triple.Predicate, terms.SameAs, triple.Predicate));
            expectedToAppendTo.Add(OwlRlBatteryHelpers.Triple(triple.Object, terms.SameAs, triple.Object));
        }
    }

    /// <summary>Asserts an inconsistent base: both engines report inconsistency with the expected named falsity.</summary>
    /// <param name="baseTriples">The base to close over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="expectedRule">The falsity rule both engines must report.</param>
    /// <param name="maintained">The maintained engine's result to hold to the same falsity, or <see langword="null"/> to skip the maintained lane.</param>
    private void AssertInconsistent(
        IReadOnlyCollection<EncodedTriple> baseTriples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        string expectedRule,
        OwlRlResult? maintained = null)
    {
        List<EncodedTriple> baseList = [.. baseTriples];
        OwlRlResult semiNaive = OwlRlClosure.Compute(baseList, terms, oracle, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(baseList, terms, oracle, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(semiNaive.IsConsistent, "The hand-built clash must be inconsistent under Compute.");
        Assert.IsFalse(naive.IsConsistent, "The hand-built clash must be inconsistent under the naive oracle.");
        Assert.AreEqual(expectedRule, semiNaive.InconsistencyRule, "Compute must report the hand-derived falsity rule.");
        Assert.AreEqual(expectedRule, naive.InconsistencyRule, "The naive oracle must report the hand-derived falsity rule.");

        if(maintained is null)
        {
            return;
        }

        Assert.IsFalse(maintained.IsConsistent, "The maintained closure must report the hand-built clash inconsistent.");
        Assert.AreEqual(expectedRule, maintained.InconsistencyRule, "The maintained closure must report the hand-derived falsity rule.");
    }

    /// <summary>Describes the symmetric difference between an expected and an actual triple set for an assertion message.</summary>
    /// <param name="expected">The expected set.</param>
    /// <param name="actual">The actual set.</param>
    /// <returns>The missing and extra triples.</returns>
    private static string Describe(HashSet<EncodedTriple> expected, HashSet<EncodedTriple> actual)
    {
        HashSet<EncodedTriple> missing = [.. expected];
        missing.ExceptWith(actual);
        HashSet<EncodedTriple> extra = [.. actual];
        extra.ExceptWith(expected);

        return $"missing {missing.Count}, extra {extra.Count}.";
    }

    /// <summary>An encoded p-edge, a shorthand for the transitive-closure shapes.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <param name="object">The object.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple PP(TermId subject, TermId predicate, TermId @object)
    {
        return OwlRlBatteryHelpers.Triple(subject, predicate, @object);
    }


    /// <summary>Builds the schema-closure family pool: subsumptions, an equivalentClass two-cycle, subproperties, domain/range, class declarations and instance edges.</summary>
    /// <param name="dictionary">The dictionary the terms enter.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns>The candidate base triples.</returns>
    private static IReadOnlyList<EncodedTriple> SchemaPool(TermDictionary dictionary, OwlRlTerms terms)
    {
        TermId a0 = OwlRlBatteryHelpers.Mint(dictionary, "sc0");
        TermId a1 = OwlRlBatteryHelpers.Mint(dictionary, "sc1");
        TermId a2 = OwlRlBatteryHelpers.Mint(dictionary, "sc2");
        TermId a3 = OwlRlBatteryHelpers.Mint(dictionary, "sc3");
        TermId a4 = OwlRlBatteryHelpers.Mint(dictionary, "sc4");
        TermId p0 = OwlRlBatteryHelpers.Mint(dictionary, "sp0");
        TermId p1 = OwlRlBatteryHelpers.Mint(dictionary, "sp1");
        TermId i0 = OwlRlBatteryHelpers.Mint(dictionary, "si0");
        TermId i1 = OwlRlBatteryHelpers.Mint(dictionary, "si1");
        TermId i2 = OwlRlBatteryHelpers.Mint(dictionary, "si2");

        return
        [
            OwlRlBatteryHelpers.Triple(a0, terms.EquivalentClass, a1),
            OwlRlBatteryHelpers.Triple(a1, terms.EquivalentClass, a0),
            OwlRlBatteryHelpers.Triple(a2, terms.SubClassOf, a3),
            OwlRlBatteryHelpers.Triple(a3, terms.SubClassOf, a4),
            OwlRlBatteryHelpers.Triple(a1, terms.SubClassOf, a2),
            OwlRlBatteryHelpers.Triple(p0, terms.SubPropertyOf, p1),
            OwlRlBatteryHelpers.Triple(p1, terms.EquivalentProperty, p0),
            OwlRlBatteryHelpers.Triple(p0, terms.Domain, a0),
            OwlRlBatteryHelpers.Triple(p0, terms.Range, a2),
            OwlRlBatteryHelpers.Triple(a0, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(a3, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(i0, terms.Type, a0),
            OwlRlBatteryHelpers.Triple(i1, terms.Type, a2),
            OwlRlBatteryHelpers.Triple(i0, p0, i1),
            OwlRlBatteryHelpers.Triple(i2, p1, i0),
            OwlRlBatteryHelpers.Triple(i1, terms.Type, a3),
        ];
    }

    /// <summary>Builds the characteristic family pool: property characteristics some of which drive inconsistency, named individuals and edges.</summary>
    /// <param name="dictionary">The dictionary the terms enter.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns>The candidate base triples.</returns>
    private static IReadOnlyList<EncodedTriple> CharacteristicPool(TermDictionary dictionary, OwlRlTerms terms)
    {
        TermId cp0 = OwlRlBatteryHelpers.Mint(dictionary, "cp0");
        TermId cp1 = OwlRlBatteryHelpers.Mint(dictionary, "cp1");
        TermId cp2 = OwlRlBatteryHelpers.Mint(dictionary, "cp2");
        TermId n0 = OwlRlBatteryHelpers.Mint(dictionary, "cn0");
        TermId n1 = OwlRlBatteryHelpers.Mint(dictionary, "cn1");
        TermId n2 = OwlRlBatteryHelpers.Mint(dictionary, "cn2");
        TermId n3 = OwlRlBatteryHelpers.Mint(dictionary, "cn3");
        TermId n4 = OwlRlBatteryHelpers.Mint(dictionary, "cn4");

        return
        [
            OwlRlBatteryHelpers.Triple(cp0, terms.Type, terms.TransitiveProperty),
            OwlRlBatteryHelpers.Triple(cp0, terms.Type, terms.IrreflexiveProperty),
            OwlRlBatteryHelpers.Triple(cp1, terms.Type, terms.SymmetricProperty),
            OwlRlBatteryHelpers.Triple(cp1, terms.Type, terms.AsymmetricProperty),
            OwlRlBatteryHelpers.Triple(cp2, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(cp2, terms.Type, terms.InverseFunctionalProperty),
            OwlRlBatteryHelpers.Triple(n0, terms.Type, terms.NamedIndividual),
            OwlRlBatteryHelpers.Triple(n2, terms.Type, terms.NamedIndividual),
            OwlRlBatteryHelpers.Triple(n0, cp0, n1),
            OwlRlBatteryHelpers.Triple(n1, cp0, n2),
            OwlRlBatteryHelpers.Triple(n2, cp0, n0),
            OwlRlBatteryHelpers.Triple(n0, cp1, n3),
            OwlRlBatteryHelpers.Triple(n2, cp2, n3),
            OwlRlBatteryHelpers.Triple(n2, cp2, n4),
            OwlRlBatteryHelpers.Triple(n1, cp0, n1),
        ];
    }

    /// <summary>Builds the inverse/chain family pool: inverses, equivalences, subproperties, a 2-link chain and the transitivity chain p∘p⊑p with hand-built lists, and edges.</summary>
    /// <param name="dictionary">The dictionary the terms enter.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns>The candidate base triples.</returns>
    private static List<EncodedTriple> InverseChainPool(TermDictionary dictionary, OwlRlTerms terms)
    {
        TermId ip0 = OwlRlBatteryHelpers.Mint(dictionary, "ip0");
        TermId ip1 = OwlRlBatteryHelpers.Mint(dictionary, "ip1");
        TermId ip2 = OwlRlBatteryHelpers.Mint(dictionary, "ip2");
        TermId ip3 = OwlRlBatteryHelpers.Mint(dictionary, "ip3");
        TermId ip5 = OwlRlBatteryHelpers.Mint(dictionary, "ip5");
        TermId m0 = OwlRlBatteryHelpers.Mint(dictionary, "im0");
        TermId m1 = OwlRlBatteryHelpers.Mint(dictionary, "im1");
        TermId m2 = OwlRlBatteryHelpers.Mint(dictionary, "im2");
        TermId m3 = OwlRlBatteryHelpers.Mint(dictionary, "im3");

        List<EncodedTriple> pool =
        [
            OwlRlBatteryHelpers.Triple(ip0, terms.InverseOf, ip1),
            OwlRlBatteryHelpers.Triple(ip0, terms.EquivalentProperty, ip2),
            OwlRlBatteryHelpers.Triple(ip3, terms.SubPropertyOf, ip0),
            OwlRlBatteryHelpers.Triple(m0, ip0, m1),
            OwlRlBatteryHelpers.Triple(m1, ip1, m2),
            OwlRlBatteryHelpers.Triple(m0, ip5, m1),
            OwlRlBatteryHelpers.Triple(m1, ip5, m2),
            OwlRlBatteryHelpers.Triple(m2, ip5, m3),
            OwlRlBatteryHelpers.Triple(m0, ip3, m2),
        ];

        OwlRlBatteryHelpers.AddChainAxiom(pool, dictionary, terms, ip5, [ip5, ip5], "ppchain");
        OwlRlBatteryHelpers.AddChainAxiom(pool, dictionary, terms, ip3, [ip0, ip1], "twolink");

        return pool;
    }

    /// <summary>Builds the equality-churn family pool: sameAs chains and a clique, per-entity data, differentFrom, a functional-property merge and numeric literals for the dt-diff oracle.</summary>
    /// <param name="dictionary">The dictionary the terms enter.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns>The candidate base triples.</returns>
    private static IReadOnlyList<EncodedTriple> EqualityPool(TermDictionary dictionary, OwlRlTerms terms)
    {
        TermId e0 = OwlRlBatteryHelpers.Mint(dictionary, "eq0");
        TermId e1 = OwlRlBatteryHelpers.Mint(dictionary, "eq1");
        TermId e2 = OwlRlBatteryHelpers.Mint(dictionary, "eq2");
        TermId e3 = OwlRlBatteryHelpers.Mint(dictionary, "eq3");
        TermId e4 = OwlRlBatteryHelpers.Mint(dictionary, "eq4");
        TermId ep0 = OwlRlBatteryHelpers.Mint(dictionary, "ep0");
        TermId ep1 = OwlRlBatteryHelpers.Mint(dictionary, "ep1");
        TermId d0 = OwlRlBatteryHelpers.Mint(dictionary, "ed0");
        TermId d1 = OwlRlBatteryHelpers.Mint(dictionary, "ed1");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdInteger);
        TermId two = OwlRlBatteryHelpers.Literal(dictionary, "2", XsdInteger);

        return
        [
            OwlRlBatteryHelpers.Triple(e0, terms.SameAs, e1),
            OwlRlBatteryHelpers.Triple(e1, terms.SameAs, e2),
            OwlRlBatteryHelpers.Triple(e3, terms.SameAs, e4),
            OwlRlBatteryHelpers.Triple(e0, ep0, d0),
            OwlRlBatteryHelpers.Triple(e1, ep1, d1),
            OwlRlBatteryHelpers.Triple(e2, ep0, d0),
            OwlRlBatteryHelpers.Triple(e0, terms.DifferentFrom, e2),
            OwlRlBatteryHelpers.Triple(ep0, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(e3, ep0, d0),
            OwlRlBatteryHelpers.Triple(e3, ep0, d1),
            OwlRlBatteryHelpers.Triple(one, terms.SameAs, e4),
            OwlRlBatteryHelpers.Triple(e4, terms.SameAs, two),
        ];
    }


    //The production-path certification: the SAME op sequences the
    //battery drives against the engine-level maintained closure, driven instead
    //through a reasoned MUTABLE ENGINE (VeritasEngine.OpenMutableAsync + SPARQL
    //UPDATE per op), reading the served store back per op and asserting the
    //branched serving oracle: a consistent generation serves
    //base ∪ ComputeNaive(base).Derived, an inconsistent one serves base alone.
    //Each op additionally asserts verdict agreement and the D-COUNTERS side-by-side
    //identity — the facade served store equals the engine-level maintained closure.
    //
    //The facade-battery bridge. The battery holds its history as EncodedTriple sets
    //over its own TermDictionary; the reasoned mutable engine opens over decoded
    //DataTriples and evolves through INSERT DATA/DELETE DATA. RDF-term value equality
    //bridges the two dictionaries: a served triple read back through SELECT and a
    //battery-oracle triple both decode to value-equatable NamedNode/Literal terms, so
    //blank-free triples compare exactly across the dictionary boundary. The only blank
    //nodes RL materialises are the transitivity chain list nodes, whose labels embed
    //the property's dictionary-scoped id and so cannot cross that boundary verbatim;
    //those triples are compared by COUNT here (their exact shape rides the engine-level
    //lane's exact set equality, asserted side-by-side on the same sequence). The facade
    //vocabulary is simple IRIs — the families driven facade-side use no literals, so the
    //literal carve-out does not arise here; the equality family (numeric literals, dt-*
    //falsities) stays engine-side, where the existing battery certifies it exactly.
    //
    //What runs facade-side vs engine-side. Four of the hand-built adversarial shapes, the
    //AddOnly regression, and the Schema family run facade-side (the smallest representative
    //set inside suite-time discipline — the facade lane pays a SPARQL parse, a maintained
    //apply, and a whole-graph scan per op). Every family at full iteration count, the
    //characteristic/inverse-chain/equality families, and the exact blank-bearing chain
    //structure stay engine-side on the existing battery, which this lane leaves untouched.

    /// <summary>The reduced per-family iteration count the facade lane samples — the facade path is slower than the engine-level lane, so it certifies the smallest representative set while the full-count families stay engine-side.</summary>
    private const long FacadeFamilyIterations = 4;

    /// <summary>One resolved step of an op sequence: the triples this op added and retracted, and the base after it.</summary>
    /// <param name="Added">The triples the op added to the base.</param>
    /// <param name="Retracted">The triples the op retracted from the base.</param>
    /// <param name="BaseAfter">The base after the op — a distinct snapshot per step.</param>
    private readonly record struct SequenceStep(
        IReadOnlyList<EncodedTriple> Added,
        IReadOnlyList<EncodedTriple> Retracted,
        HashSet<EncodedTriple> BaseAfter);

    /// <summary>CyclicOrphan through the production path: a transitive property over a two-cycle with external support, its support then a cycle edge retracted, served and asserted through a reasoned mutable engine.</summary>
    [TestMethod]
    public async Task CyclicOrphanThroughProductionPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "cop");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "coa");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "cob");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "cos");

        EncodedTriple transitive = OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty);
        EncodedTriple ab = OwlRlBatteryHelpers.Triple(a, p, b);
        EncodedTriple ba = OwlRlBatteryHelpers.Triple(b, p, a);
        EncodedTriple sa = OwlRlBatteryHelpers.Triple(s, p, a);

        List<SequenceStep> steps = StepsFromExplicit(
            [transitive, ab, ba, sa],
            [([], [sa]), ([], [ab])]);
        await RunProductionPathSequenceAsync(terms, oracle, dictionary, steps, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>AlternateDerivationSurvival through the production path: a fact with two independent cax-sco derivations, one premise then the other retracted, served through a reasoned mutable engine.</summary>
    [TestMethod]
    public async Task AlternateDerivationThroughProductionPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "adA");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "adB");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "adC");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "adx");

        EncodedTriple aSubC = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classC);
        EncodedTriple bSubC = OwlRlBatteryHelpers.Triple(classB, terms.SubClassOf, classC);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        List<SequenceStep> steps = StepsFromExplicit(
            [aSubC, bSubC, xIsA, xIsB],
            [([], [xIsA]), ([], [xIsB])]);
        await RunProductionPathSequenceAsync(terms, oracle, dictionary, steps, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>SameAsUnMerge through the production path: two bridged sameAs cliques whose bridge is retracted, tearing the congruence class, served through a reasoned mutable engine.</summary>
    [TestMethod]
    public async Task SameAsUnMergeThroughProductionPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId a1 = OwlRlBatteryHelpers.Mint(dictionary, "sua1");
        TermId a2 = OwlRlBatteryHelpers.Mint(dictionary, "sua2");
        TermId b1 = OwlRlBatteryHelpers.Mint(dictionary, "sub1");
        TermId b2 = OwlRlBatteryHelpers.Mint(dictionary, "sub2");
        TermId prop = OwlRlBatteryHelpers.Mint(dictionary, "suP");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "suu");
        TermId w = OwlRlBatteryHelpers.Mint(dictionary, "suw");

        EncodedTriple Same(TermId x, TermId y) => OwlRlBatteryHelpers.Triple(x, terms.SameAs, y);
        EncodedTriple Data(TermId x, TermId value) => OwlRlBatteryHelpers.Triple(x, prop, value);

        EncodedTriple bridge = Same(a2, b1);
        List<SequenceStep> steps = StepsFromExplicit(
            [Same(a1, a2), Same(b1, b2), bridge, Data(a1, u), Data(b2, w)],
            [([], [bridge])]);
        await RunProductionPathSequenceAsync(terms, oracle, dictionary, steps, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>FalsityRetract through the production path: an inconsistent base opens serving asserted-only, then a clashing typing is retracted and the overlay returns, served through a reasoned mutable engine.</summary>
    [TestMethod]
    public async Task FalsityRetractThroughProductionPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "frC");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "frD");
        TermId classE = OwlRlBatteryHelpers.Mint(dictionary, "frE");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "frx");

        EncodedTriple disjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple cSubE = OwlRlBatteryHelpers.Triple(classC, terms.SubClassOf, classE);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);

        List<SequenceStep> steps = StepsFromExplicit(
            [disjoint, cSubE, xIsC, xIsD],
            [([], [xIsD])]);
        await RunProductionPathSequenceAsync(terms, oracle, dictionary, steps, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>The AddOnly regression through the production path: pinned all-adds sequences over the schema pool, served through a reasoned mutable engine, at the reduced facade iteration count.</summary>
    [TestMethod]
    public async Task AddOnlySequenceThroughProductionPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        IReadOnlyList<EncodedTriple> pool = SchemaPool(dictionary, terms);

        Gen<int> genIndex = Gen.Int[0, pool.Count - 1];
        Gen<OpSpec> genAdd = genIndex.Array[1, MaxSetSize].Select(indices => new OpSpec(false, indices));
        Gen<(int[] Initial, OpSpec[] Ops)> gen =
            genIndex.Array[0, MaxInitial].SelectMany(initial => genAdd.Array[1, MaxOps].Select(ops => (initial, ops)));

        await RunFacadeFamilyAsync(gen, pool, terms, oracle, dictionary, AddOnlySeed).ConfigureAwait(false);
    }

    /// <summary>The schema-closure family through the production path: pinned random op sequences over subclass/subproperty/domain/range closures and equivalentClass cycles, served through a reasoned mutable engine, at the reduced facade iteration count.</summary>
    [TestMethod]
    public async Task SchemaFamilyThroughProductionPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        IReadOnlyList<EncodedTriple> pool = SchemaPool(dictionary, terms);

        Gen<int> genIndex = Gen.Int[0, pool.Count - 1];
        Gen<int[]> genSet = genIndex.Array[1, MaxSetSize];
        Gen<OpSpec> genOp = Gen.Bool.SelectMany(isRetract => genSet.Select(indices => new OpSpec(isRetract, indices)));
        Gen<(int[] Initial, OpSpec[] Ops)> gen =
            genIndex.Array[0, MaxInitial].SelectMany(initial => genOp.Array[1, MaxOps].Select(ops => (initial, ops)));

        await RunFacadeFamilyAsync(gen, pool, terms, oracle, dictionary, SchemaSeed).ConfigureAwait(false);
    }

    /// <summary>Collects a family's pinned-seed op-sequence samples, then drives each through the production path — the collect-then-drive split keeps the CsCheck sampling synchronous while the facade run stays fully asynchronous.</summary>
    /// <param name="generator">The family's op-sequence generator.</param>
    /// <param name="pool">The family's candidate base triples.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="dictionary">The dictionary the family's triples encode with.</param>
    /// <param name="seed">The pinned PCG seed making the sample deterministic.</param>
    private async Task RunFacadeFamilyAsync(
        Gen<(int[] Initial, OpSpec[] Ops)> generator,
        IReadOnlyList<EncodedTriple> pool,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        TermDictionary dictionary,
        string seed)
    {
        List<(int[] Initial, OpSpec[] Ops)> samples = [];
        generator.Sample(sample => samples.Add(sample), seed: seed, iter: FacadeFamilyIterations);

        foreach((int[] initial, OpSpec[] ops) in samples)
        {
            List<SequenceStep> steps = ResolveSteps(pool, initial, ops);
            await RunProductionPathSequenceAsync(terms, oracle, dictionary, steps, TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves a pool-indexed op sequence into concrete add/retract steps, folding the base exactly as the engine-level runner does so the facade and engine-level lanes share one history.</summary>
    /// <param name="pool">The candidate base triples add-ops draw from.</param>
    /// <param name="initialIndices">The pool positions seeding the initial base.</param>
    /// <param name="ops">The op stream.</param>
    /// <returns>The resolved steps, the initial base first.</returns>
    private static List<SequenceStep> ResolveSteps(IReadOnlyList<EncodedTriple> pool, IReadOnlyList<int> initialIndices, IReadOnlyList<OpSpec> ops)
    {
        HashSet<EncodedTriple> currentBase = [];
        List<EncodedTriple> initialAdds = [];
        foreach(int index in initialIndices)
        {
            EncodedTriple triple = pool[index % pool.Count];
            if(currentBase.Add(triple))
            {
                initialAdds.Add(triple);
            }
        }

        List<SequenceStep> steps = [new SequenceStep(initialAdds, [], [.. currentBase])];

        foreach(OpSpec op in ops)
        {
            List<EncodedTriple> added = [];
            List<EncodedTriple> retracted = [];

            if(op.IsRetract)
            {
                List<EncodedTriple> live = [.. currentBase];
                live.Sort();
                if(live.Count > 0)
                {
                    foreach(int index in op.Indices)
                    {
                        EncodedTriple triple = live[index % live.Count];
                        if(currentBase.Remove(triple))
                        {
                            retracted.Add(triple);
                        }
                    }
                }
            }
            else
            {
                foreach(int index in op.Indices)
                {
                    EncodedTriple triple = pool[index % pool.Count];
                    if(currentBase.Add(triple))
                    {
                        added.Add(triple);
                    }
                }
            }

            steps.Add(new SequenceStep(added, retracted, [.. currentBase]));
        }

        return steps;
    }

    /// <summary>Builds resolved steps from an explicit initial base and add/retract op lists, for the hand-built adversarial shapes.</summary>
    /// <param name="initialBase">The initial base triples.</param>
    /// <param name="ops">The ordered op stream as add/retract lists.</param>
    /// <returns>The resolved steps, the initial base first.</returns>
    private static List<SequenceStep> StepsFromExplicit(
        IReadOnlyList<EncodedTriple> initialBase,
        IReadOnlyList<(EncodedTriple[] Adds, EncodedTriple[] Retracts)> ops)
    {
        HashSet<EncodedTriple> currentBase = [.. initialBase];
        List<SequenceStep> steps = [new SequenceStep([.. currentBase], [], [.. currentBase])];

        foreach((EncodedTriple[] adds, EncodedTriple[] retracts) in ops)
        {
            List<EncodedTriple> added = [];
            List<EncodedTriple> retracted = [];
            foreach(EncodedTriple triple in adds)
            {
                if(currentBase.Add(triple))
                {
                    added.Add(triple);
                }
            }

            foreach(EncodedTriple triple in retracts)
            {
                if(currentBase.Remove(triple))
                {
                    retracted.Add(triple);
                }
            }

            steps.Add(new SequenceStep(added, retracted, [.. currentBase]));
        }

        return steps;
    }

    /// <summary>Drives one resolved op sequence through a reasoned mutable engine, asserting the branched serving oracle, verdict agreement, and the engine-level side-by-side identity after the open and every op.</summary>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="dictionary">The dictionary the sequence's triples encode with.</param>
    /// <param name="steps">The resolved steps, the initial base first.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    private static async Task RunProductionPathSequenceAsync(
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        TermDictionary dictionary,
        IReadOnlyList<SequenceStep> steps,
        CancellationToken cancellationToken)
    {
        SequenceStep initial = steps[0];
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(DecodeToData(dictionary, initial.BaseAfter), cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The engine-level maintained closure drives the same sequence side-by-side; the
        //D-COUNTERS certification asserts both lanes end at identical served closures.
        OwlRlMaintainedClosure engineLevel = new(initial.BaseAfter, terms, oracle, cancellationToken);
        await AssertProductionCheckpointAsync(database, engineLevel.Current, terms, oracle, dictionary, initial.BaseAfter, cancellationToken).ConfigureAwait(false);

        for(int i = 1; i < steps.Count; i++)
        {
            SequenceStep step = steps[i];
            if(step.Added.Count > 0)
            {
                await database.UpdateAsync(Utf8Strings.From(UpdateText(dictionary, "INSERT DATA", step.Added)), cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if(step.Retracted.Count > 0)
            {
                await database.UpdateAsync(Utf8Strings.From(UpdateText(dictionary, "DELETE DATA", step.Retracted)), cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            OwlRlResult engineResult = engineLevel.Apply(step.Added, step.Retracted, cancellationToken);
            await AssertProductionCheckpointAsync(database, engineResult, terms, oracle, dictionary, step.BaseAfter, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Asserts one checkpoint: the served store equals the branched oracle (base ∪ ComputeNaive.Derived when consistent, base otherwise), the verdict agrees with the naive oracle and the engine-level lane, and the served store equals the engine-level maintained closure.</summary>
    /// <param name="database">The reasoned mutable engine under test.</param>
    /// <param name="engineLevelResult">The engine-level maintained closure's result for this generation.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="dictionary">The dictionary the sequence's triples encode with.</param>
    /// <param name="baseAfter">The asserted base at this checkpoint.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    private static async Task AssertProductionCheckpointAsync(
        VeritasEngine database,
        OwlRlResult engineLevelResult,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        TermDictionary dictionary,
        HashSet<EncodedTriple> baseAfter,
        CancellationToken cancellationToken)
    {
        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "A reasoned mutable engine surfaces the provenance for every served generation.");

        OwlRlResult naive = OwlRlClosure.ComputeNaive(baseAfter, terms, oracle, cancellationToken: cancellationToken);
        Assert.AreEqual(naive.IsConsistent, provenance.IsConsistent, "The served generation's verdict must agree with the naive oracle.");
        Assert.AreEqual(engineLevelResult.IsConsistent, provenance.IsConsistent, "The served generation's verdict must agree with the engine-level maintained lane.");

        List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> served = await ProductionPathServedReader
            .ReadServedAsync(database, cancellationToken).ConfigureAwait(false);
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> servedPortable = ProductionPathServedReader.PortablePortion(served);
        int servedScoped = ProductionPathServedReader.DictionaryScopedCount(served);

        //The branched serving oracle: a consistent generation serves base ∪ derived;
        //an inconsistent one withdraws the overlay and serves the asserted base alone.
        List<EncodedTriple> expected = [.. baseAfter];
        if(provenance.IsConsistent)
        {
            expected.AddRange(naive.Derived);
        }

        List<(RdfTerm, RdfTerm, RdfTerm)> expectedTerms = DecodeToTerms(dictionary, expected);
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> expectedPortable = ProductionPathServedReader.PortablePortion(expectedTerms);
        int expectedScoped = ProductionPathServedReader.DictionaryScopedCount(expectedTerms);

        Assert.IsTrue(
            servedPortable.SetEquals(expectedPortable),
            $"The served store (portable {servedPortable.Count}) must equal the branched oracle (portable {expectedPortable.Count}).");
        Assert.AreEqual(expectedScoped, servedScoped, "The served store's dictionary-scoped chain triples must match the oracle by count.");

        //D-COUNTERS side-by-side: the facade served store equals the engine-level maintained
        //closure over the same sequence — both lanes end at identical served closures.
        List<EncodedTriple> engineExpected = [.. baseAfter];
        if(engineLevelResult.IsConsistent)
        {
            engineExpected.AddRange(engineLevelResult.Derived);
        }

        List<(RdfTerm, RdfTerm, RdfTerm)> engineTerms = DecodeToTerms(dictionary, engineExpected);
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> enginePortable = ProductionPathServedReader.PortablePortion(engineTerms);
        int engineScoped = ProductionPathServedReader.DictionaryScopedCount(engineTerms);

        Assert.IsTrue(servedPortable.SetEquals(enginePortable), "The facade served store must equal the engine-level maintained closure (portable portion).");
        Assert.AreEqual(engineScoped, servedScoped, "The facade and engine-level lanes must carry identical dictionary-scoped chain structure by count.");
    }

    /// <summary>Renders an <c>INSERT DATA</c>/<c>DELETE DATA</c> operation over decoded base triples; the facade families are IRI-only, so every term serialises as a named node.</summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="keyword">The operation keyword — <c>INSERT DATA</c> or <c>DELETE DATA</c>.</param>
    /// <param name="triples">The base triples the op adds or retracts.</param>
    /// <returns>The update text.</returns>
    private static string UpdateText(TermDictionary dictionary, string keyword, IReadOnlyList<EncodedTriple> triples)
    {
        StringBuilder builder = new();
        builder.Append(keyword).Append(" { ");
        foreach(EncodedTriple triple in triples)
        {
            builder
                .Append(ProductionPathServedReader.SparqlTerm(dictionary.Resolve(triple.Subject.Encoded))).Append(' ')
                .Append(ProductionPathServedReader.SparqlTerm(dictionary.Resolve(triple.Predicate.Encoded))).Append(' ')
                .Append(ProductionPathServedReader.SparqlTerm(dictionary.Resolve(triple.Object.Encoded))).Append(" . ");
        }

        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>Decodes encoded triples to the data triples a mutable-engine open takes.</summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="triples">The encoded triples.</param>
    /// <returns>The decoded data triples.</returns>
    private static List<DataTriple> DecodeToData(TermDictionary dictionary, IEnumerable<EncodedTriple> triples)
    {
        List<DataTriple> decoded = [];
        foreach(EncodedTriple triple in triples)
        {
            decoded.Add(new DataTriple(
                dictionary.Resolve(triple.Subject.Encoded),
                dictionary.Resolve(triple.Predicate.Encoded),
                dictionary.Resolve(triple.Object.Encoded)));
        }

        return decoded;
    }

    /// <summary>Decodes encoded triples to value-equatable RDF-term tuples, comparable across term dictionaries.</summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="triples">The encoded triples.</param>
    /// <returns>The decoded term tuples.</returns>
    private static List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> DecodeToTerms(TermDictionary dictionary, IEnumerable<EncodedTriple> triples)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> decoded = [];
        foreach(EncodedTriple triple in triples)
        {
            decoded.Add((
                dictionary.Resolve(triple.Subject.Encoded),
                dictionary.Resolve(triple.Predicate.Encoded),
                dictionary.Resolve(triple.Object.Encoded)));
        }

        return decoded;
    }
}
