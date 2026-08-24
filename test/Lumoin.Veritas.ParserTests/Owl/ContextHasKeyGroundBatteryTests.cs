using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The HasKey ground module-level battery:
/// every semantic row of the pre-registered ground-truth sheet
/// (28 of 28 independently confirmed) drives
/// <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>
/// at MODULE level through the opened gates — survey, belt, clausifier with the
/// round-0 key join, saturation, the derived-merge fixpoint, the counting
/// rider, verdict reader — and checks consistency, the context-decided path,
/// and the EXACT module-local subsumption set. Entailment queries land
/// as their refutation encodings: a sameAs conclusion as premise plus
/// <c>DifferentIndividuals</c> (unsatisfiable iff entailed), a
/// class-membership or min-cardinality conclusion as premise plus the
/// complemented assertion. Two ground-truth sheet row pairs coincide on one module by
/// construction (KEY-1's refutation IS KDIFF-1; FIX-1's refutation IS KDIFF-3)
/// and ride one battery row each, named for both. The ALC(H) tableau has NO
/// keys reach — no row here carries a tableau comparand (the honesty flag,
/// stated as on the cardinality rows; the ground-truth sheet is the oracle)
/// — so the automated comparands are the RL-closure
/// differential on the lexical-join carve-out rows and the EL-degeneracy
/// zero-movement assert, each its own test below. The rows the engine honestly
/// ABSTAINS on ride the delegation-rows test, where the pinned observable is
/// the delegation itself.
/// </summary>
[TestClass]
internal sealed class ContextHasKeyGroundBatteryTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, data properties, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier2bhaskeybattery#";

    /// <summary>The XSD string datatype IRI.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The XSD integer datatype IRI.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The XSD decimal datatype IRI.</summary>
    private const string XsdDecimal = "http://www.w3.org/2001/XMLSchema#decimal";

    /// <summary>
    /// The semantic battery: every decidable ground-truth sheet row at module level, with
    /// verdict, context-decided path, and exact subsumption set checked. The
    /// loop reports every offender and fails once with the whole table.
    /// </summary>
    [TestMethod]
    public void HasKeyGroundSemanticBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, string[] ExpectedSubsumptions)[] rows = BatteryRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | true | final | contextDecided | subs | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, string[] expectedSubsumptions) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool contextDecided = decision.Statistics.ContextTotals.ContextDecided;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;

            List<string> expected = [.. expectedSubsumptions];
            expected.Sort(StringComparer.Ordinal);
            List<string> actual = SubsumptionKeys(decision.Verdict);
            bool subsOk = KeysEqual(expected, actual);
            string subsNote = subsOk ? "ok" : DiffKeys(expected, actual);

            bool ok = verdictOk && contextDecided && subsOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + contextDecided + " | " + subsNote + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " contextDecided=" + contextDecided + " subs=" + subsNote);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The honest-abstention rows: each module carries a shape the context arm
    /// delegates by design — the S8 admission grammar (an empty key list), the
    /// S2 belt's KEPT positions (an asserted key property paired into a
    /// data-property equivalence, whose value propagation the per-property
    /// store does not close), the S3 key latch (key-class membership
    /// riding a carried disjunct), or the ground-counting remainder (no
    /// told-distinct clique, or a missing told filler) — so the module DELEGATES
    /// whole to the fallback oracle, whose verdict stays fragment-relative over
    /// a non-empty unsupported-construct remainder. The consistency boolean is
    /// recorded, never asserted; the pinned observable is the delegation
    /// itself. The per-shape mechanics are pinned below the gates in
    /// <see cref="ContextHasKeyGroundEnginePinTests"/>.
    /// </summary>
    [TestMethod]
    public void HasKeyGroundDelegationRows()
    {
        (string Name, ReasoningModule Module, string Mechanism)[] rows = DelegationRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | mechanism | contextDecided | remainder | consistent (recorded)");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, string mechanism) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool contextDecided = decision.Statistics.ContextTotals.ContextDecided;
            bool remainderNamed = decision.Verdict!.UnsupportedConstructs.Count > 0;
            report.AppendLine(name + " | " + mechanism + " | " + contextDecided + " | " + remainderNamed + " | " + decision.Verdict.IsConsistent);
            if(contextDecided || !remainderNamed)
            {
                mismatches.Add(name + ": contextDecided=" + contextDecided + " remainderNamed=" + remainderNamed);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The RL-engine differential with the carve-out asserted: the RL
    /// closure's <c>prp-key</c> joins by TermId identity — a LEXICAL join with
    /// no positive datatype equality — so agreement with the battery's certified
    /// verdicts is asserted ONLY on rows where the lexical and value-space joins
    /// coincide: told class memberships (RL's <c>prp-key</c> conditions on told
    /// <c>rdf:type</c>, so the battery's <c>owl:Thing</c> global keys land here
    /// as class-scoped told-membership analogs of the same join shape), told
    /// values, and identical lexical forms. The final row pins the carve-out's
    /// factual basis: on the KDATA-1 lexical-variant integers the RL closure
    /// derives NO equality and stays consistent while the semantic
    /// truth is inconsistent — RL is not a valid oracle there, and the
    /// battery row (KDATA-1 above, decided by the value-space join) is the sole
    /// oracle for the KDATA family.
    /// </summary>
    [TestMethod]
    public void HasKeyRlEngineDifferential()
    {
        StringBuilder report = new();
        report.AppendLine("\nrow | sameAs | consistent | verdict");
        List<string> mismatches = [];
        foreach(RlRow row in RlDifferentialRows())
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            List<EncodedTriple> baseTriples = [];
            foreach((string individual, string typedClass) in row.Memberships)
            {
                baseTriples.Add(OwlRlBatteryHelpers.Triple(OwlRlBatteryHelpers.Mint(dictionary, individual), terms.Type, OwlRlBatteryHelpers.Mint(dictionary, typedClass)));
            }

            int keyIndex = 0;
            foreach((string keyedClass, string[] keyProperties) in row.Keys)
            {
                TermId[] properties = new TermId[keyProperties.Length];
                for(int i = 0; i < keyProperties.Length; i++)
                {
                    properties[i] = OwlRlBatteryHelpers.Mint(dictionary, keyProperties[i]);
                }

                TermId head = OwlRlBatteryHelpers.AddList(baseTriples, dictionary, terms, properties, row.Name + "-key" + keyIndex);
                baseTriples.Add(OwlRlBatteryHelpers.Triple(OwlRlBatteryHelpers.Mint(dictionary, keyedClass), terms.HasKey, head));
                keyIndex++;
            }

            foreach((string sub, string super) in row.SubProperties)
            {
                baseTriples.Add(OwlRlBatteryHelpers.Triple(OwlRlBatteryHelpers.Mint(dictionary, sub), terms.SubPropertyOf, OwlRlBatteryHelpers.Mint(dictionary, super)));
            }

            foreach((string individual, string property, string value, string? datatype) in row.Values)
            {
                TermId valueTerm = datatype is string typed
                    ? OwlRlBatteryHelpers.Literal(dictionary, value, typed)
                    : OwlRlBatteryHelpers.Mint(dictionary, value);
                baseTriples.Add(OwlRlBatteryHelpers.Triple(OwlRlBatteryHelpers.Mint(dictionary, individual), OwlRlBatteryHelpers.Mint(dictionary, property), valueTerm));
            }

            foreach((string first, string second) in row.DifferentPairs)
            {
                baseTriples.Add(OwlRlBatteryHelpers.Triple(OwlRlBatteryHelpers.Mint(dictionary, first), terms.DifferentFrom, OwlRlBatteryHelpers.Mint(dictionary, second)));
            }

            OwlRlResult result = OwlRlClosure.Compute(baseTriples, terms, cancellationToken: TestContext.CancellationToken);

            bool consistencyOk = result.IsConsistent == row.ExpectConsistent;
            bool sameAsOk = true;
            string sameAsNote = "skipped (clash short-circuit)";
            if(row.SameAsEntailed is bool entailed)
            {
                bool derived = ContainsSameAs(result, terms, OwlRlBatteryHelpers.Mint(dictionary, row.ProbedPair.First), OwlRlBatteryHelpers.Mint(dictionary, row.ProbedPair.Second));
                sameAsOk = derived == entailed;
                sameAsNote = "expected=" + entailed + " derived=" + derived;
            }

            bool ok = consistencyOk && sameAsOk;
            report.AppendLine(row.Name + " | " + sameAsNote + " | expected=" + row.ExpectConsistent + " actual=" + result.IsConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(row.Name + " (" + row.Basis + "): sameAs " + sameAsNote + ", consistent expected=" + row.ExpectConsistent + " actual=" + result.IsConsistent);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The EL-degeneracy differential, asserted rather than assumed: keys,
    /// key-scoped data assertions, and ground counting assertions never reach
    /// the EL arm's fragment — <see cref="ElModuleSurvey.IsElDecidable"/>
    /// answers <see langword="false"/> on EVERY battery module, decided and
    /// delegated alike, so the key machinery cannot move any EL-decided verdict
    /// and the EL fast path's behavior is unchanged by construction.
    /// </summary>
    [TestMethod]
    public void HasKeyElDegeneracyZeroMovement()
    {
        List<string> reached = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in BatteryRows())
        {
            if(ElModuleSurvey.IsElDecidable(module.Axioms))
            {
                reached.Add(name);
            }
        }

        foreach((string name, ReasoningModule module, string _) in DelegationRows())
        {
            if(ElModuleSurvey.IsElDecidable(module.Axioms))
            {
                reached.Add(name);
            }
        }

        Assert.IsEmpty(reached, "Key-bearing modules reached the EL fragment: " + string.Join(",", reached) + " — the EL zero-movement invariant is broken.");
    }

    /// <summary>
    /// The allocation-delta arm: on Horn/no-keys
    /// modules — a pure Horn TBox and a Horn ground slice, neither carrying a
    /// key, a data assertion, or a counting edge — the key machinery is off the
    /// decision path, so after a warm-up the per-decision allocation must be
    /// steady: the spread across same-run measured windows stays within a small
    /// tolerance. No absolute byte count is pinned and no cross-run baseline is
    /// compared — both are machine- and JIT-fragile; the pinned observable is
    /// same-run steadiness itself. The windows read a thread-local allocation
    /// counter, which also counts a fresh buffer this thread must allocate when a
    /// shared pool is momentarily drained by other workers, so the row runs
    /// outside the parallel suite: the measurement is of the decision path, not
    /// of pool contention.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    public void HasKeyOffPathAllocationDeltaStaysWithinTolerance()
    {
        (string Name, ReasoningModule Module)[] rows =
        [
            ("horn-tbox", Module(
                SubClassOf(Class("Wren"), Class("Heron")),
                SubClassOf(Class("Heron"), Class("Crane")))),
            ("horn-ground", Module(
                SubClassOf(Class("Wren"), Class("Heron")),
                ClassAssertion(Class("Wren"), Individual("idp")),
                ObjectPropertyAssertion("likes", "idp", "idq"))),
        ];

        const int warmUpDecisions = 3;
        const int measuredWindows = 3;
        const long spreadToleranceBytes = 1024;

        StringBuilder report = new();
        report.AppendLine("\nmodule | windows (bytes) | spread");
        List<string> offenders = [];
        foreach((string name, ReasoningModule module) in rows)
        {
            for(int i = 0; i < warmUpDecisions; i++)
            {
                ModuleDecision warm = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
                Assert.IsTrue(warm.Statistics.ContextTotals.ContextDecided, name + ": the off-path module must be context-decided for the measurement to be of the decision path.");
                Assert.IsTrue(warm.Verdict!.IsConsistent, name + ": the off-path module is consistent.");
            }

            long[] windows = new long[measuredWindows];
            for(int i = 0; i < measuredWindows; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                _ = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
                windows[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            long minimum = windows[0];
            long maximum = windows[0];
            foreach(long window in windows)
            {
                minimum = Math.Min(minimum, window);
                maximum = Math.Max(maximum, window);
            }

            long spread = maximum - minimum;
            report.AppendLine(name + " | " + string.Join(",", windows) + " | " + spread);
            if(spread > spreadToleranceBytes)
            {
                offenders.Add(name + ": spread " + spread + " > " + spreadToleranceBytes);
            }
        }

        Assert.IsEmpty(offenders, report.ToString());
    }

    /// <summary>
    /// The merge-cascade growth curve: the cascade
    /// micro-family at depths 2..5 — depth <c>d</c> chains <c>d</c> key-forced
    /// unions, each enabled by the previous round's merge, over <c>d + 1</c>
    /// individuals — drives the derived-merge fixpoint through <c>d</c> firing
    /// rounds plus the dry round. The semantic face is asserted per depth
    /// (decided, consistent, exact <c>MergeRounds</c> and
    /// <c>KeyForcedUnions</c>); the per-decision allocation is measured after a
    /// warm-up and recorded in the test output as the growth curve — never
    /// asserted, since absolute bytes are machine- and JIT-fragile.
    /// </summary>
    [TestMethod]
    public void HasKeyMergeCascadeAllocationGrowthCurve()
    {
        StringBuilder report = new();
        report.AppendLine("\ndepth | individuals | mergeRounds | keyForcedUnions | allocatedBytes");
        List<string> offenders = [];
        for(int depth = 2; depth <= 5; depth++)
        {
            ReasoningModule module = CascadeModule(depth);
            ModuleDecision warm = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);

            long before = GC.GetAllocatedBytesForCurrentThread();
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            bool ok = totals.ContextDecided
                && decision.Verdict!.IsConsistent
                && totals.MergeRounds == depth + 1
                && totals.KeyForcedUnions == depth
                && warm.Verdict!.IsConsistent == decision.Verdict.IsConsistent;
            report.AppendLine(depth + " | " + (depth + 1) + " | " + totals.MergeRounds + " | " + totals.KeyForcedUnions + " | " + allocated);
            if(!ok)
            {
                offenders.Add("depth " + depth + ": contextDecided=" + totals.ContextDecided + " consistent=" + decision.Verdict!.IsConsistent + " mergeRounds=" + totals.MergeRounds + " keyForcedUnions=" + totals.KeyForcedUnions);
            }
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(offenders, report.ToString());
    }

    /// <summary>
    /// Builds the depth-<c>d</c> merge cascade: a global key on <c>chain0</c>
    /// merges <c>cid0</c> and <c>cid1</c> at round 1, and each level
    /// <c>i &gt;= 1</c> adds a class-scoped key whose join is enabled only by
    /// the previous round's union — the merged representative holds the level's
    /// told membership through <c>cid{i}</c> and the shared value through
    /// <c>cid0</c>, and <c>cid{i+1}</c> is the level's second candidate — so
    /// exactly one union fires per round.
    /// </summary>
    /// <param name="depth">The number of key-forced unions to chain.</param>
    /// <returns>The cascade module.</returns>
    private static ReasoningModule CascadeModule(int depth)
    {
        List<OwlAxiom> axioms =
        [
            HasKey(Thing, [], ["chain0"]),
            DataAssertion("cid0", "chain0", "V-0", XsdString),
            DataAssertion("cid1", "chain0", "V-0", XsdString),
        ];

        for(int level = 1; level < depth; level++)
        {
            string keyProperty = "chain" + level;
            string sharedValue = "V-" + level;
            axioms.Add(HasKey(Class("Link" + level), [], [keyProperty]));
            axioms.Add(ClassAssertion(Class("Link" + level), Individual("cid" + level)));
            axioms.Add(DataAssertion("cid0", keyProperty, sharedValue, XsdString));
            axioms.Add(ClassAssertion(Class("Link" + level), Individual("cid" + (level + 1))));
            axioms.Add(DataAssertion("cid" + (level + 1), keyProperty, sharedValue, XsdString));
        }

        return Module([.. axioms]);
    }

    /// <summary>
    /// The decidable battery rows: ground-truth sheet id (refutation-encoded ids carry the
    /// <c>r</c> suffix; coinciding row pairs are named for both), module,
    /// ground-truth consistency per the certified ground-truth sheet, and the exact
    /// expected module-local subsumption set (empty on every row — every
    /// consistent row's signature holds at most one class, and an inconsistent
    /// row's set is empty by convention — so the exact-set check guards
    /// against phantoms).
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, string[] ExpectedSubsumptions)[] BatteryRows()
    {
        return
        [
            //KEY-1r=KDIFF-1: the flagship global-data-key join (the Keys-001 shape) refutation-encoded —
            //the shared ring value forces idp=idq against told distinctness (the Keys-002 shape).
            ("KEY-1r=KDIFF-1", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                Different("idp", "idq")),
                false, []),

            //KEY-2r: a class-scoped key never joins an untyped candidate — a countermodel keeps idq
            //outside Wren (the Keys-004 shape) — so the refutation module is consistent.
            ("KEY-2r", Module(
                HasKey(Class("Wren"), [], ["ring"]),
                ClassAssertion(Class("Wren"), Individual("idp")),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                Different("idp", "idq")),
                true, []),

            //KEY-3r: the subclass-derived membership fires the key at the derived round — Wren(idp)
            //forces Heron(idp) — and the seeded merge collides with told distinctness.
            ("KEY-3r", Module(
                SubClassOf(Class("Wren"), Class("Heron")),
                HasKey(Class("Heron"), [], ["ring"]),
                ClassAssertion(Class("Wren"), Individual("idp")),
                ClassAssertion(Class("Heron"), Individual("idq")),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                Different("idp", "idq")),
                false, []),

            //KEY-4r: a composite key needs a shared value on EVERY key property; the wing disagreement
            //blocks the merge, so distinctness is satisfiable.
            ("KEY-4r", Module(
                HasKey(Thing, [], ["ring", "wing"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                DataAssertion("idp", "wing", "A", XsdString),
                DataAssertion("idq", "wing", "B", XsdString),
                Different("idp", "idq")),
                true, []),

            //KEY-5r: per-property EXISTENTIAL agreement — the shared R-2 among multi-valued assertions
            //suffices, and the forced merge collides.
            ("KEY-5r", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idp", "ring", "R-2", XsdString),
                DataAssertion("idq", "ring", "R-2", XsdString),
                DataAssertion("idq", "ring", "R-3", XsdString),
                Different("idp", "idq")),
                false, []),

            //KEY-6r: two INDEPENDENT keys fire independently — agreement on all of the wing axiom's
            //properties suffices though the ring key disagrees.
            ("KEY-6r", Module(
                HasKey(Thing, [], ["ring"]),
                HasKey(Thing, [], ["wing"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idq", "ring", "R-9", XsdString),
                DataAssertion("idp", "wing", "W-4", XsdString),
                DataAssertion("idq", "wing", "W-4", XsdString),
                Different("idp", "idq")),
                false, []),

            //KEY-7r: an object key joins on the shared NAMED target idr, and the merge collides.
            ("KEY-7r", Module(
                HasKey(Thing, ["nests"], []),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ObjectPropertyAssertion("nests", "idq", "idr"),
                Different("idp", "idq")),
                false, []),

            //KEY-8r: the told role hierarchy closes the tends edge onto nests; the closed graph shares
            //idr and the merge collides.
            ("KEY-8r", Module(
                HasKey(Thing, ["nests"], []),
                SubObjectPropertyOf("tends", "nests"),
                ObjectPropertyAssertion("tends", "idp", "idr"),
                ObjectPropertyAssertion("nests", "idq", "idr"),
                Different("idp", "idq")),
                false, []),

            //KEY-C1r (control): distinct values force nothing; distinctness is satisfiable.
            ("KEY-C1r", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idq", "ring", "R-2", XsdString),
                Different("idp", "idq")),
                true, []),

            //FIX-1r=KDIFF-3: the two-step cascade refutation-encoded — the ring key merges idp=idq, the
            //merged class's told Heron membership fires the wing key onto idr at the seeded round, and
            //the cascaded merge collides with told distinctness.
            ("FIX-1r=KDIFF-3", Module(
                HasKey(Thing, [], ["ring"]),
                HasKey(Class("Heron"), [], ["wing"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idq", "ring", "R-1", XsdString),
                ClassAssertion(Class("Heron"), Individual("idq")),
                DataAssertion("idp", "wing", "W-1", XsdString),
                ClassAssertion(Class("Heron"), Individual("idr")),
                DataAssertion("idr", "wing", "W-1", XsdString),
                Different("idp", "idr")),
                false, []),

            //KDIFF-2 (control): shared values force nothing without a key axiom.
            ("KDIFF-2", Module(
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                Different("idp", "idq")),
                true, []),

            //KNAMED-2 (the MU-HK-2 killer row): two blank-node-only holders sharing a told value and
            //told-different stay DISTINCT — neither equivalence class contains a named member, so the
            //key never fires and the module decides CONSISTENT.
            ("KNAMED-2", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion(Blank("b1"), "ring", "R-7", XsdString),
                DataAssertion(Blank("b2"), "ring", "R-7", XsdString),
                DifferentTerms(Blank("b1"), Blank("b2"))),
                true, []),

            //KNAMED-3: the told identity onto the NAMED idp makes the anonymous node's class named —
            //the contains-named bit OR-propagates — so the key fires and collides.
            ("KNAMED-3", Module(
                HasKey(Thing, [], ["ring"]),
                SameTerms(Blank("b1"), Individual("idp")),
                DataAssertion(Blank("b1"), "ring", "R-7", XsdString),
                DataAssertion("idq", "ring", "R-7", XsdString),
                Different("idp", "idq")),
                false, []),

            //KNAMED-1r: the Keys-007 shape refutation-encoded — ring is ASSERTED (on idp) and used
            //inside a DataHasValue class expression (idq's existential), a LIFTED belt position, so the
            //F3.1 router admits the module and the engine decides the semantic ground truth: the existential
            //filler is never a ground representative, the key forces no identity, and the membership
            //refutation is CONSISTENT (not entailed).
            ("KNAMED-1r", Module(
                HasKey(Class("Heron"), [], ["ring"]),
                SubClassOf(Class("Wren"), Class("Heron")),
                ClassAssertion(Class("Heron"), Individual("idp")),
                DataAssertion("idp", "ring", "R-77", XsdString),
                ClassAssertion(Some("nests", Intersection(Class("Wren"), new OwlDataHasValue(DataProperty("ring"), StringLiteral("R-77", XsdString)))), Individual("idq")),
                ClassAssertion(Complement(Class("Wren")), Individual("idp"))),
                true, [Example + "Wren->" + Example + "Heron"]),

            //KDATA-1: lexically different integer forms denote ONE value — the join compares in the
            //value space and the forced merge collides.
            ("KDATA-1", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "1", XsdInteger),
                DataAssertion("idq", "ring", "01", XsdInteger),
                Different("idp", "idq")),
                false, []),

            //KDATA-2: integer 1 and string "1" are DIFFERENT data values; the key never fires.
            ("KDATA-2", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "1", XsdInteger),
                DataAssertion("idq", "ring", "1", XsdString),
                Different("idp", "idq")),
                true, []),

            //KDATA-3: integer 1 and decimal 1.0 denote the same numeric value across the datatype
            //boundary; the forced merge collides.
            ("KDATA-3", Module(
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "1", XsdInteger),
                DataAssertion("idq", "ring", "1.0", XsdDecimal),
                Different("idp", "idq")),
                false, []),

            //PIG-1: three pairwise told-distinct successors under a told max-2 — the unqualified
            //pigeonhole (the WebOnt-maxCardinality-001 shape).
            ("PIG-1", Module(
                ClassAssertion(Max("nests", 2, null), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ObjectPropertyAssertion("nests", "idp", "ids"),
                Different("idq", "idr", "ids")),
                false, []),

            //PIG-3: two told-distinct told-Wren fillers under a told qualified max-1 — the qualified
            //pigeonhole (the New-Feature-ObjectQCR-001 refutation shape).
            ("PIG-3", Module(
                ClassAssertion(Max("nests", 1, Class("Wren")), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ClassAssertion(Class("Wren"), Individual("idq")),
                ClassAssertion(Class("Wren"), Individual("idr")),
                Different("idq", "idr")),
                false, []),

            //PIG-5r: the min-cardinality entailment refutation-encoded as the BARE complement of the
            //qualified min with no harness-side NNF (the corpus refutation-builder shape) — the
            //rider's engine-side intake NNF lands it as told max-1 and the qualified pigeonhole
            //clashes.
            ("PIG-5r", Module(
                ClassAssertion(Complement(Min("nests", 2, Class("Wren"))), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ClassAssertion(Class("Wren"), Individual("idq")),
                ClassAssertion(Class("Wren"), Individual("idr")),
                Different("idq", "idr")),
                false, []),

            //PIG-6: the told sub-property edge counts toward the super-role's bound through the closed
            //graph; two distinct successors under max-1 clash.
            ("PIG-6", Module(
                SubObjectPropertyOf("tends", "nests"),
                ClassAssertion(Max("nests", 1, null), Individual("idp")),
                ObjectPropertyAssertion("tends", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                Different("idq", "idr")),
                false, []),
        ];
    }

    /// <summary>
    /// The delegation rows: ground-truth sheet id, module, and the delegation mechanism
    /// the row exercises (named in the offender report; the mechanics are
    /// pinned below the gates in the engine pin battery).
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module, string Mechanism)[] DelegationRows()
    {
        return
        [
            //KBELT-1: an asserted key property paired into an EquivalentDataProperties axiom — a KEPT
            //belt position (the per-property value store performs no hierarchy closure) — so the S2
            //belt delegates the whole module before any join; the semantic truth (the equivalence
            //propagates the shared value and the key collides with told distinctness, INCONSISTENT) is
            //recorded on the ground-truth sheet, and the engine honestly abstains.
            ("KBELT-1", Module(
                HasKey(Class("Heron"), [], ["ring"]),
                ClassAssertion(Class("Heron"), Individual("idp")),
                ClassAssertion(Class("Heron"), Individual("idq")),
                new OwlEquivalentDataPropertiesAxiom(DataProperty("ring"), DataProperty("tag")) { Origin = Origin("equivalent") },
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "tag", "R-77", XsdString),
                Different("idp", "idq")),
                "S2 belt"),

            //KDISJ-1: key-class membership rides the carried disjunct on BOTH branches — whichever
            //holds, the key fires against idq and collides (semantically INCONSISTENT) —
            //but a disjunctive membership is uncertain per branch, so the S3 latch delegates.
            ("KDISJ-1", Module(
                HasKey(Class("Wren"), [], ["ring"]),
                HasKey(Class("Crane"), [], ["ring"]),
                ClassAssertion(Union(Class("Wren"), Class("Crane")), Individual("idp")),
                DataAssertion("idp", "ring", "R-1", XsdString),
                ClassAssertion(Class("Wren"), Individual("idq")),
                ClassAssertion(Class("Crane"), Individual("idq")),
                DataAssertion("idq", "ring", "R-1", XsdString),
                Different("idp", "idq")),
                "S3 key latch"),

            //KDISJ-2: the Crane branch carries no key, so the module is semantically CONSISTENT
            //— the possible-but-uncertain Wren membership still latches, because the
            //correct engine never risks the wrong INCONSISTENT.
            ("KDISJ-2", Module(
                HasKey(Class("Wren"), [], ["ring"]),
                ClassAssertion(Union(Class("Wren"), Class("Crane")), Individual("idp")),
                DataAssertion("idp", "ring", "R-1", XsdString),
                ClassAssertion(Class("Wren"), Individual("idq")),
                DataAssertion("idq", "ring", "R-1", XsdString),
                Different("idp", "idq")),
                "S3 key latch"),

            //KADM-1: the EMPTY key list degenerates to all-named-instances-equal (semantically
            //INCONSISTENT against the told distinctness), which the value join cannot
            //express — the S8 admission grammar delegates the axiom whole.
            ("KADM-1", Module(
                HasKey(Class("Wren"), [], []),
                ClassAssertion(Class("Wren"), Individual("idp")),
                ClassAssertion(Class("Wren"), Individual("idq")),
                Different("idp", "idq")),
                "S8 admission"),

            //PIG-2: only one told-distinct pair among three successors — no clique, max-2 satisfiable
            //(semantically CONSISTENT) — and the context arm NEVER claims consistency
            //on a counting-remainder module, so the remainder delegates.
            ("PIG-2", Module(
                ClassAssertion(Max("nests", 2, null), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ObjectPropertyAssertion("nests", "idp", "ids"),
                Different("idq", "idr")),
                "counting remainder"),

            //PIG-4: idr carries no told Wren membership, so the qualified count has one filler and no
            //clique (semantically CONSISTENT); the remainder delegates.
            ("PIG-4", Module(
                ClassAssertion(Max("nests", 1, Class("Wren")), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ClassAssertion(Class("Wren"), Individual("idq")),
                Different("idq", "idr")),
                "counting remainder"),
        ];
    }

    /// <summary>
    /// One RL-differential row: the base is assembled from told memberships,
    /// per-axiom key-property lists, told sub-property statements, value edges
    /// (typed literals or named targets), and told distinctness, then closed by
    /// <see cref="OwlRlClosure.Compute"/> and checked against the certified
    /// expectations.
    /// </summary>
    /// <param name="Name">The row's label.</param>
    /// <param name="Keys">The <c>owl:hasKey</c> axioms as keyed-class local name and key-property local names, one list per axiom.</param>
    /// <param name="Memberships">The told <c>rdf:type</c> facts as individual and class local names.</param>
    /// <param name="SubProperties">The told <c>rdfs:subPropertyOf</c> statements as sub and super local names.</param>
    /// <param name="Values">The value edges as individual, property, value, and datatype IRI — a <see langword="null"/> datatype makes the value a named target.</param>
    /// <param name="DifferentPairs">The told <c>owl:differentFrom</c> pairs.</param>
    /// <param name="ProbedPair">The individual pair whose derived equality the row probes.</param>
    /// <param name="SameAsEntailed">Whether the probed equality must be derived; <see langword="null"/> skips the probe (an inconsistent closure short-circuits).</param>
    /// <param name="ExpectConsistent">The expected RL consistency verdict.</param>
    /// <param name="Basis">The ground-truth sheet row(s) the expectations are certified by.</param>
    private sealed record RlRow(
        string Name,
        (string KeyedClass, string[] KeyProperties)[] Keys,
        (string Individual, string TypedClass)[] Memberships,
        (string Sub, string Super)[] SubProperties,
        (string Individual, string Property, string Value, string? Datatype)[] Values,
        (string First, string Second)[] DifferentPairs,
        (string First, string Second) ProbedPair,
        bool? SameAsEntailed,
        bool ExpectConsistent,
        string Basis);

    /// <summary>
    /// The RL-differential rows — every expectation transcribed from a
    /// ground-truth sheet row whose shape lies inside the carve-out (told
    /// memberships, told values, identical lexical forms), plus the carve-out
    /// demonstration row where RL's lexical join is silent BY CONSTRUCTION.
    /// </summary>
    /// <returns>The rows.</returns>
    private static RlRow[] RlDifferentialRows()
    {
        return
        [
            //KEY-1's join face, class-scoped: told memberships and one shared string value derive the
            //equality.
            new RlRow("rl-told-join",
                [("Wren", ["ring"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "R-77", XsdString), ("idq", "ring", "R-77", XsdString)],
                [],
                ("idp", "idq"), SameAsEntailed: true, ExpectConsistent: true, "KEY-1"),

            //KDIFF-1: the derived equality against told distinctness is the eq-diff1 falsity.
            new RlRow("rl-told-collision",
                [("Wren", ["ring"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "R-77", XsdString), ("idq", "ring", "R-77", XsdString)],
                [("idp", "idq")],
                ("idp", "idq"), SameAsEntailed: null, ExpectConsistent: false, "KDIFF-1"),

            //KEY-4: a composite key with a disagreeing wing never fires.
            new RlRow("rl-composite-partial",
                [("Wren", ["ring", "wing"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "R-77", XsdString), ("idq", "ring", "R-77", XsdString), ("idp", "wing", "A", XsdString), ("idq", "wing", "B", XsdString)],
                [],
                ("idp", "idq"), SameAsEntailed: false, ExpectConsistent: true, "KEY-4"),

            //KEY-5: per-property existential agreement — the shared R-2 suffices.
            new RlRow("rl-multivalued",
                [("Wren", ["ring"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "R-1", XsdString), ("idp", "ring", "R-2", XsdString), ("idq", "ring", "R-2", XsdString), ("idq", "ring", "R-3", XsdString)],
                [],
                ("idp", "idq"), SameAsEntailed: true, ExpectConsistent: true, "KEY-5"),

            //KEY-6: two independent axioms fire per occurrence — the wing key alone derives the
            //equality.
            new RlRow("rl-independent-axioms",
                [("Wren", ["ring"]), ("Wren", ["wing"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "R-1", XsdString), ("idq", "ring", "R-9", XsdString), ("idp", "wing", "W-4", XsdString), ("idq", "wing", "W-4", XsdString)],
                [],
                ("idp", "idq"), SameAsEntailed: true, ExpectConsistent: true, "KEY-6"),

            //KEY-7: an object key over the shared named target idr.
            new RlRow("rl-object-target",
                [("Wren", ["nests"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "nests", "idr", null), ("idq", "nests", "idr", null)],
                [],
                ("idp", "idq"), SameAsEntailed: true, ExpectConsistent: true, "KEY-7"),

            //KEY-8: prp-spo1 lifts the tends edge onto nests before the key joins.
            new RlRow("rl-subproperty-closure",
                [("Wren", ["nests"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [("tends", "nests")],
                [("idp", "tends", "idr", null), ("idq", "nests", "idr", null)],
                [],
                ("idp", "idq"), SameAsEntailed: true, ExpectConsistent: true, "KEY-8"),

            //KEY-C1: distinct values derive nothing.
            new RlRow("rl-distinct-values",
                [("Wren", ["ring"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "R-1", XsdString), ("idq", "ring", "R-2", XsdString)],
                [],
                ("idp", "idq"), SameAsEntailed: false, ExpectConsistent: true, "KEY-C1"),

            //FIX-1/KDIFF-3: the cascade — the ring key equates idp/idq, eq-rep carries idq's Heron
            //membership onto idp, the wing key equates idp/idr, and told distinctness fires eq-diff1.
            new RlRow("rl-cascade-collision",
                [("Wren", ["ring"]), ("Heron", ["wing"])],
                [("idp", "Wren"), ("idq", "Wren"), ("idq", "Heron"), ("idr", "Heron")],
                [],
                [("idp", "ring", "R-1", XsdString), ("idq", "ring", "R-1", XsdString), ("idp", "wing", "W-1", XsdString), ("idr", "wing", "W-1", XsdString)],
                [("idp", "idr")],
                ("idp", "idr"), SameAsEntailed: null, ExpectConsistent: false, "FIX-1/KDIFF-3"),

            //The carve-out pinned: on KDATA-1's lexical-variant integers ("1" vs "01") the RL
            //closure derives NO equality and stays consistent — the lexical TermId join cannot see the
            //shared integer value the row's INCONSISTENT verdict rests on, so RL is excluded
            //as an oracle for the KDATA family and the battery row above is the sole oracle.
            new RlRow("rl-kdata-lexical-carveout",
                [("Wren", ["ring"])],
                [("idp", "Wren"), ("idq", "Wren")],
                [],
                [("idp", "ring", "1", XsdInteger), ("idq", "ring", "01", XsdInteger)],
                [("idp", "idq")],
                ("idp", "idq"), SameAsEntailed: false, ExpectConsistent: true, "KDATA-1 (RL-silent by design)"),
        ];
    }

    /// <summary>Whether the closure derived the equality between the two individuals in either orientation.</summary>
    /// <param name="result">The RL closure result.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    /// <returns><see langword="true"/> when <c>owl:sameAs</c> was derived between the pair.</returns>
    private static bool ContainsSameAs(OwlRlResult result, OwlRlTerms terms, TermId first, TermId second)
    {
        foreach(EncodedTriple triple in result.Derived)
        {
            if(triple.Predicate == terms.SameAs && ((triple.Subject == first && triple.Object == second) || (triple.Subject == second && triple.Object == first)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The verdict's subsumption pairs as sorted comparison keys, one <c>subIri-&gt;superIri</c> string per pair.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The keys, sorted ordinally.</returns>
    private static List<string> SubsumptionKeys(ModuleVerdict verdict)
    {
        List<string> keys = new(verdict.Subsumptions.Count);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add($"{subClass.Iri}->{superClass.Iri}");
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>Whether two sorted key lists hold the same keys in the same order.</summary>
    /// <param name="expected">The expected sorted keys.</param>
    /// <param name="actual">The actual sorted keys.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool KeysEqual(List<string> expected, List<string> actual)
    {
        if(expected.Count != actual.Count)
        {
            return false;
        }

        for(int i = 0; i < expected.Count; i++)
        {
            if(!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The missing (expected, absent) and phantom (present, unexpected) keys between an expected and an actual sorted key list, for the offender report.</summary>
    /// <param name="expected">The expected sorted keys.</param>
    /// <param name="actual">The actual sorted keys.</param>
    /// <returns>The rendered difference.</returns>
    private static string DiffKeys(List<string> expected, List<string> actual)
    {
        List<string> missing = [];
        foreach(string key in expected)
        {
            if(!actual.Contains(key))
            {
                missing.Add(key);
            }
        }

        List<string> phantom = [];
        foreach(string key in actual)
        {
            if(!expected.Contains(key))
            {
                phantom.Add(key);
            }
        }

        return "missing=[" + string.Join(",", missing) + "] phantom=[" + string.Join(",", phantom) + "]";
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference — the global key's class.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named data property node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An anonymous individual by label.</summary>
    /// <param name="label">The blank-node label.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>A typed literal.</summary>
    /// <param name="value">The lexical form.</param>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal StringLiteral(string value, string datatypeIri)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Utf8Strings.From(datatypeIri)));
    }

    /// <summary>A <c>HasKey</c> axiom over a keyed class, object key properties, and data key properties in the example namespace.</summary>
    /// <param name="keyedClass">The keyed class expression.</param>
    /// <param name="objectProperties">The object key properties' local names.</param>
    /// <param name="dataProperties">The data key properties' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlHasKeyAxiom HasKey(OwlClassExpression keyedClass, string[] objectProperties, string[] dataProperties)
    {
        List<OwlObjectPropertyExpression> objects = [];
        foreach(string local in objectProperties)
        {
            objects.Add(Property(local));
        }

        List<NamedNode> data = [];
        foreach(string local in dataProperties)
        {
            data.Add(DataProperty(local));
        }

        return new OwlHasKeyAxiom(keyedClass, objects, data) { Origin = Origin("haskey") };
    }

    /// <summary>A data-property assertion over a named subject.</summary>
    /// <param name="subject">The subject individual's local name.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The literal's lexical form.</param>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom DataAssertion(string subject, string property, string value, string datatypeIri)
    {
        return DataAssertion(Individual(subject), property, value, datatypeIri);
    }

    /// <summary>A data-property assertion over an arbitrary subject term.</summary>
    /// <param name="subject">The subject term.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The literal's lexical form.</param>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom DataAssertion(RdfTerm subject, string property, string value, string datatypeIri)
    {
        return new OwlDataPropertyAssertionAxiom(subject, DataProperty(property), StringLiteral(value, datatypeIri)) { Origin = Origin("data") };
    }

    /// <summary>An object-property assertion over named individuals.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom ObjectPropertyAssertion(string property, string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(Utf8Strings.From(Example + property)), Individual(target)) { Origin = Origin("edge") };
    }

    /// <summary>A sub-object-property axiom over named roles.</summary>
    /// <param name="sub">The subproperty's local name.</param>
    /// <param name="super">The superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubObjectPropertyOf(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = Origin("subrole") };
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A class assertion.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>A same-individual axiom over arbitrary terms.</summary>
    /// <param name="first">The first term.</param>
    /// <param name="second">The second term.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom SameTerms(RdfTerm first, RdfTerm second)
    {
        return new OwlSameIndividualAxiom(first, second) { Origin = Origin("same") };
    }

    /// <summary>A different-individuals axiom over named individuals.</summary>
    /// <param name="individuals">The pairwise-distinct individuals' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int i = 0; i < individuals.Length; i++)
        {
            terms[i] = Individual(individuals[i]);
        }

        return new OwlDifferentIndividualsAxiom(terms) { Origin = Origin("different") };
    }

    /// <summary>A different-individuals axiom over arbitrary terms.</summary>
    /// <param name="terms">The pairwise-distinct terms.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom DifferentTerms(params RdfTerm[] terms)
    {
        return new OwlDifferentIndividualsAxiom(terms) { Origin = Origin("different") };
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf(operands);
    }

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf(operands);
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>A qualified or unqualified minimum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }
}
