using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The HasKey ground engine pins: each pin drives the
/// key machinery DIRECTLY — the full <c>Clausify</c> overload to the engine,
/// below the module survey and with the counting rider passed explicitly — so a
/// half-built join, guard, latch, or rider fails HERE independently of the
/// gates; the seam pins (STAT/BUDGET) drive the survey-gated production
/// composition instead, where the reasoner-loop statistics live. Semantic
/// expectations are transcribed from the pre-registered ground-truth sheet
/// (28 of 28 independently confirmed); row ids in the
/// method prefixes name the ground-truth rows and pins.
/// </summary>
[TestClass]
internal sealed class ContextHasKeyGroundEnginePinTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, roles, properties, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier2bhaskey#";

    /// <summary>The XSD string datatype IRI.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The XSD integer datatype IRI.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The XSD decimal datatype IRI.</summary>
    private const string XsdDecimal = "http://www.w3.org/2001/XMLSchema#decimal";

    /// <summary>KEY-1: the round-0 told join fires the global data key on a shared value — the flagship Keys-001 shape — and the seeded re-clausification collapses the pair into one representative that saturates consistent.</summary>
    [TestMethod]
    public void Key1GlobalDataKeyRoundZeroJoinFires()
    {
        ClausificationResult first = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
            ]);

        Assert.AreEqual(1, first.KeyForcedUnions, "The global key joins the shared ring value at round 0.");
        Assert.HasCount(1, first.KeyUnionPairs, "One union pair rides the seed channel.");
        Assert.IsFalse(first.GroundClash, "No distinctness contradicts the merge.");

        ClausificationResult second = ClausifySeeded(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
            ],
            first.KeyUnionPairs);

        Assert.AreEqual(0, second.KeyForcedUnions, "The seeded round finds the pair already merged.");
        Assert.HasCount(1, second.GroundRepresentatives, "One merged representative remains.");
        ContextSaturationEngine engine = SaturateClausification(second);
        Assert.IsFalse(engine.IsInconsistent, "The merged module is consistent.");
    }

    /// <summary>KDIFF-1 + REASON-P1: the key-forced merge collides with told distinctness — the Keys-002 shape — and the clash reason carries the KEY provenance, not the told pre-merge reason.</summary>
    [TestMethod]
    public void Kdiff1KeyMergeCollisionRefutesWithProvenance()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                Different("idp", "idq"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "The forced merge contradicts told distinctness.");
        Assert.IsNotNull(clausification.GroundClashReason);
        Assert.StartsWith("KeyMergeCollision(", clausification.GroundClashReason, "The collision carries key provenance.");
    }

    /// <summary>KDIFF-2 (control): shared values force nothing without a key axiom.</summary>
    [TestMethod]
    public void Kdiff2NoKeyControlStaysClean()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                Different("idp", "idq"),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "No key, no join.");
        Assert.IsFalse(clausification.GroundClash, "Distinct individuals with shared values are consistent.");
    }

    /// <summary>KEY-2: a class-scoped key never joins an untyped candidate — neither at the told round 0 nor at the post-saturation join, whose derived-certain membership readout answers false (the Keys-004 shape).</summary>
    [TestMethod]
    public void Key2UntypedCandidateNeverJoins()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Class("Wren"), [], ["ring"]),
                ClassAssertion(Class("Wren"), Individual("idp")),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "idq carries no told Wren membership at round 0.");
        ContextSaturationEngine engine = SaturateClausification(clausification);
        List<(Utf8String First, Utf8String Second)> seeds = [];
        ContextSaturationModuleReasoner.PostKeyJoinOutcome outcome = ContextSaturationModuleReasoner.RunPostSaturationKeyJoin(clausification, engine, DatatypeRegistry.Empty, seeds, out _, out int fired);

        Assert.AreEqual(ContextSaturationModuleReasoner.PostKeyJoinOutcome.Clean, outcome, "Every comparison is decisive.");
        Assert.AreEqual(0, fired, "idq is not a derived Wren either; the key never fires.");
        Assert.IsEmpty(seeds, "No pair rides the seed channel.");
    }

    /// <summary>KEY-3: a subclass-derived membership joins at the post-saturation round — the told round 0 cannot see it, the derived-certain readout can.</summary>
    [TestMethod]
    public void Key3DerivedMembershipJoinsAtPostSaturation()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                SubClassOf(Class("Wren"), Class("Heron")),
                HasKey(Class("Heron"), [], ["ring"]),
                ClassAssertion(Class("Wren"), Individual("idp")),
                ClassAssertion(Class("Heron"), Individual("idq")),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "idp's Heron membership is derived, not told — round 0 stays silent.");
        ContextSaturationEngine engine = SaturateClausification(clausification);
        List<(Utf8String First, Utf8String Second)> seeds = [];
        ContextSaturationModuleReasoner.PostKeyJoinOutcome outcome = ContextSaturationModuleReasoner.RunPostSaturationKeyJoin(clausification, engine, DatatypeRegistry.Empty, seeds, out int candidates, out int fired);

        Assert.AreEqual(ContextSaturationModuleReasoner.PostKeyJoinOutcome.Clean, outcome, "Every comparison is decisive.");
        Assert.AreEqual(2, candidates, "Both individuals are derived-certain Herons.");
        Assert.AreEqual(1, fired, "The shared ring value joins the pair at the derived round.");
        Assert.HasCount(1, seeds, "The fired pair rides the seed channel.");
    }

    /// <summary>KEY-4: a composite key demands a shared value on EVERY key property — the wing disagreement blocks the merge despite the shared ring.</summary>
    [TestMethod]
    public void Key4CompositePartialAgreementNeverFires()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring", "wing"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                DataAssertion("idp", "wing", "A", XsdString),
                DataAssertion("idq", "wing", "B", XsdString),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "Partial agreement never fires a composite key.");
    }

    /// <summary>KEY-5: agreement is per-property EXISTENTIAL — one shared value among several suffices on a multi-valued key property.</summary>
    [TestMethod]
    public void Key5MultiValuedSharedValueFires()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idp", "ring", "R-2", XsdString),
                DataAssertion("idq", "ring", "R-2", XsdString),
                DataAssertion("idq", "ring", "R-3", XsdString),
            ]);

        Assert.AreEqual(1, clausification.KeyForcedUnions, "The shared R-2 suffices.");
    }

    /// <summary>KEY-6 + MU-HK-16's semantic face: two HasKey axioms fire INDEPENDENTLY — agreement on all of ONE axiom's properties suffices, so the wing key merges though the ring key disagrees (a concatenated per-class registry would demand both and miss the forced merge).</summary>
    [TestMethod]
    public void Key6IndependentKeysFireIndependently()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                HasKey(Thing, [], ["wing"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idq", "ring", "R-9", XsdString),
                DataAssertion("idp", "wing", "W-4", XsdString),
                DataAssertion("idq", "wing", "W-4", XsdString),
            ]);

        Assert.AreEqual(1, clausification.KeyForcedUnions, "The wing key alone forces the merge.");
    }

    /// <summary>KEY-7: an object key joins on a shared NAMED target read off the closed graph.</summary>
    [TestMethod]
    public void Key7ObjectKeySharedNamedTargetFires()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, ["nests"], []),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ObjectPropertyAssertion("nests", "idq", "idr"),
            ]);

        Assert.AreEqual(1, clausification.KeyForcedUnions, "The shared named target idr fires the object key on the idp/idq pair.");
    }

    /// <summary>KEY-8: the told role hierarchy closes a sub-property edge onto the key property — the join reads the CLOSED graph by the raw role id (the sub-property case).</summary>
    [TestMethod]
    public void Key8ObjectKeyThroughToldSubPropertyFires()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, ["nests"], []),
                SubObjectPropertyOf("tends", "nests"),
                ObjectPropertyAssertion("tends", "idp", "idr"),
                ObjectPropertyAssertion("nests", "idq", "idr"),
            ]);

        Assert.AreEqual(1, clausification.KeyForcedUnions, "The tends edge lifts onto nests inside the closure and shares idr.");
    }

    /// <summary>KEY-C1 (control): distinct values force nothing.</summary>
    [TestMethod]
    public void KeyC1DistinctValuesNeverFire()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
                DataAssertion("idq", "ring", "R-2", XsdString),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "Distinct values never fire the key.");
    }

    /// <summary>KNAMED-2 + NAMED-P1 (the MU-HK-2 killer row): two blank-node-only holders sharing a told value and told-different stay DISTINCT — neither class contains a named member, so the key never fires and the module decides CONSISTENT; forcing the contains-named bit true would merge them into a KeyMergeCollision and flip this verdict.</summary>
    [TestMethod]
    public void Knamed2BnodeOnlyHoldersStayDistinct()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion(Blank("b1"), "ring", "R-7", XsdString),
                DataAssertion(Blank("b2"), "ring", "R-7", XsdString),
                DifferentTerms(Blank("b1"), Blank("b2")),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "Anonymous holders are outside the key's named scope.");
        Assert.IsFalse(clausification.GroundClash, "Nothing forces the anonymous pair together.");
        ContextSaturationEngine engine = SaturateClausification(clausification);
        Assert.IsFalse(engine.IsInconsistent, "The module is consistent — the semantic ground truth the row certifies.");
    }

    /// <summary>KNAMED-3: a told identity onto a NAMED individual makes the anonymous node's equivalence class named — the contains-named bit OR-propagates through the union — so the key fires and collides with told distinctness.</summary>
    [TestMethod]
    public void Knamed3ToldSameNamedMemberMergesAndCollides()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                SameTerms(Blank("b1"), Individual("idp")),
                DataAssertion(Blank("b1"), "ring", "R-7", XsdString),
                DataAssertion("idq", "ring", "R-7", XsdString),
                Different("idp", "idq"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "The named member makes the class a key participant; the merge collides.");
        Assert.IsNotNull(clausification.GroundClashReason);
        Assert.StartsWith("KeyMergeCollision(", clausification.GroundClashReason, "The collision carries key provenance.");
    }

    /// <summary>KDATA-1 (MU-HK-3's semantic face): lexically different integer forms denote one value — the join compares in the value space and the merge collides with told distinctness.</summary>
    [TestMethod]
    public void Kdata1IntegerLexicalVariantsMerge()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "1", XsdInteger),
                DataAssertion("idq", "ring", "01", XsdInteger),
                Different("idp", "idq"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "The integer value 1 is shared regardless of lexical form.");
    }

    /// <summary>KDATA-2: an integer and a string never share a value — the key stays silent and the module stays clean.</summary>
    [TestMethod]
    public void Kdata2IntegerVersusStringNeverFires()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "1", XsdInteger),
                DataAssertion("idq", "ring", "1", XsdString),
                Different("idp", "idq"),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "Cross-family literals are distinct values.");
        Assert.IsFalse(clausification.GroundClash, "Nothing forces the pair together.");
    }

    /// <summary>KDATA-3: integer 1 and decimal 1.0 denote the same numeric value — the numeric family compares across the datatype boundary.</summary>
    [TestMethod]
    public void Kdata3IntegerDecimalSameValueMerges()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "1", XsdInteger),
                DataAssertion("idq", "ring", "1.0", XsdDecimal),
                Different("idp", "idq"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "The shared numeric value forces the merge against told distinctness.");
    }

    /// <summary>KDATA-Indeterminate (MU-HK-4/5's shared face): an unregistered datatype with differing lexical forms compares Indeterminate — the join neither merges nor assumes distinctness, and the module delegates on the named remainder.</summary>
    [TestMethod]
    public void KdataIndeterminateNamesDelegationRemainder()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "a", Example + "custom"),
                DataAssertion("idq", "ring", "b", Example + "custom"),
            ]);

        Assert.AreEqual(0, clausification.KeyForcedUnions, "An indeterminate comparison never merges.");
        Assert.Contains(ContextRemainderNames.KeyValueComparisonIndeterminate(Utf8Strings.From(Example + "ring")), clausification.Remainder, "The delegation remainder names the property.");
    }

    /// <summary>KDISJ-1 + DISJ-P1 + the MU-HK-18 marker-distinctness pin: a key-class membership riding a carried disjunct latches the KEY obligation — distinct from the data obligation — because a forced merge may hide behind either branch.</summary>
    [TestMethod]
    public void Kdisj1KeyMembershipUnderDisjunctLatches()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Class("Wren"), [], ["ring"]),
                HasKey(Class("Crane"), [], ["ring"]),
                ClassAssertion(Union(Class("Wren"), Class("Crane")), Individual("idp")),
                DataAssertion("idp", "ring", "R-1", XsdString),
                ClassAssertion(Class("Wren"), Individual("idq")),
                ClassAssertion(Class("Crane"), Individual("idq")),
                DataAssertion("idq", "ring", "R-1", XsdString),
                Different("idp", "idq"),
            ]);

        ContextSaturationEngine engine = SaturateClausification(clausification);

        Assert.IsTrue(engine.HasUndecidedKeyObligation, "The disjunctive membership latches the key obligation.");
        Assert.IsFalse(engine.HasUndecidedDataObligation, "The key marker is DISTINCT from the data-obligation marker.");
    }

    /// <summary>KDISJ-2 + DISJ-P2: a single-branch key still latches on the possible-but-uncertain membership — the correct engine never risks the wrong INCONSISTENT, and the ground-truth row certifies the module is semantically consistent.</summary>
    [TestMethod]
    public void Kdisj2SingleBranchKeyStillLatches()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Class("Wren"), [], ["ring"]),
                ClassAssertion(Union(Class("Wren"), Class("Crane")), Individual("idp")),
                DataAssertion("idp", "ring", "R-1", XsdString),
                ClassAssertion(Class("Wren"), Individual("idq")),
                DataAssertion("idq", "ring", "R-1", XsdString),
                Different("idp", "idq"),
            ]);

        ContextSaturationEngine engine = SaturateClausification(clausification);

        Assert.IsTrue(engine.HasUndecidedKeyObligation, "The possible Wren membership latches.");
    }

    /// <summary>KDISJ control: a disjunction over classes NO key names never latches — the latch scans key classes only.</summary>
    [TestMethod]
    public void KdisjUnrelatedKeyClassNeverLatches()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Class("Stork"), [], ["ring"]),
                ClassAssertion(Union(Class("Wren"), Class("Crane")), Individual("idp")),
                DataAssertion("idp", "ring", "R-1", XsdString),
            ]);

        ContextSaturationEngine engine = SaturateClausification(clausification);

        Assert.IsFalse(engine.HasUndecidedKeyObligation, "No key class rides the disjunct.");
    }

    /// <summary>ADM-P1 / KADM-1: an empty key list names its defensive remainder — the degenerate all-instances-equal semantics is delegated, never silently decided.</summary>
    [TestMethod]
    public void Kadm1EmptyKeyListNamesRemainder()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Class("Wren"), [], []),
                ClassAssertion(Class("Wren"), Individual("idp")),
                ClassAssertion(Class("Wren"), Individual("idq")),
                Different("idp", "idq"),
            ]);

        Assert.Contains(ContextRemainderNames.HasKeyEmptyKeyList, clausification.Remainder, "The empty key list delegates by name.");
        Assert.AreEqual(0, clausification.KeyForcedUnions, "No join runs on a delegated key.");
    }

    /// <summary>ADM-P2: a non-atomic keyed class names its defensive remainder — its membership is uncomputable under the atom-only readout.</summary>
    [TestMethod]
    public void KadmComplexKeyedClassNamesRemainder()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Intersection(Class("Wren"), Class("Crane")), [], ["ring"]),
                DataAssertion("idp", "ring", "R-1", XsdString),
            ]);

        Assert.Contains(ContextRemainderNames.HasKeyClassNotAtomic(nameof(OwlObjectIntersectionOf)), clausification.Remainder, "The complex keyed class delegates by name.");
    }

    /// <summary>ADM-P3 / MU-HK-13 (F3.1 router): an asserted key property carrying a FunctionalDataProperty axiom is a LIFTED co-occurrence — the module admits, each told value lowers to a value-forcing demand, and the store side still feeds the round-0 join, which merges the two named subjects on the shared value.</summary>
    [TestMethod]
    public void BeltFunctionalDataPropertyLowersToDemands()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                DataAssertion("idq", "ring", "R-77", XsdString),
                new OwlFunctionalDataPropertyAxiom(DataProperty("ring")) { Origin = Origin("functional") },
            ]);

        Assert.IsEmpty(clausification.Remainder, "The lifted functional co-occurrence admits the module.");
        Assert.IsNotEmpty(clausification.DataDemandDescriptors, "The told values lower to value-forcing demands.");
        Assert.AreEqual(1, clausification.KeyForcedUnions, "The store side still joins the shared ring value at round 0.");
    }

    /// <summary>ADM-P6 (the F3.1 router): in a mixed module a LIFTED co-occurrence on one property precedes a KEPT co-occurrence on another in axiom order — the first KEPT hit names the whole-module rejection, and the rejected module lowers nothing.</summary>
    [TestMethod]
    public void BeltMixedOrderNamesFirstKeptHit()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                new OwlDataPropertyRangeAxiom(DataProperty("ring"), new OwlDatatypeReference(new NamedNode(Utf8Strings.From(XsdString)))) { Origin = Origin("range") },
                DataAssertion("idp", "ring", "R-77", XsdString),
                new OwlDisjointDataPropertiesAxiom([DataProperty("tag"), DataProperty("label")]) { Origin = Origin("disjoint") },
                DataAssertion("idp", "tag", "T-1", XsdString),
            ]);

        Assert.HasCount(1, clausification.Remainder, "The first KEPT hit rejects the whole module.");
        Assert.AreEqual(ContextRemainderNames.AssertedDataPropertyBeyondKeys(Utf8Strings.From(Example + "tag")), clausification.Remainder[0], "The KEPT disjointness co-occurrence names the rejection, not the earlier LIFTED range.");
        Assert.IsEmpty(clausification.Clauses, "A whole-module rejection emits no clause.");
    }

    /// <summary>ADM-P4: data-property RBox participation trips the belt — the per-property value store performs no hierarchy closure, so an equivalence could smuggle a shared value past the join.</summary>
    [TestMethod]
    public void BeltEquivalentDataPropertiesDelegatesWholeModule()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Thing, [], ["ring"]),
                DataAssertion("idp", "ring", "R-77", XsdString),
                new OwlEquivalentDataPropertiesAxiom(DataProperty("ring"), DataProperty("tag")) { Origin = Origin("equivalent") },
            ]);

        Assert.HasCount(1, clausification.Remainder, "The belt rejection is the sole remainder entry.");
        Assert.AreEqual(ContextRemainderNames.AssertedDataPropertyBeyondKeys(Utf8Strings.From(Example + "ring")), clausification.Remainder[0], "The belt names the entangled property.");
    }

    /// <summary>ADM-P5 (F3.1 router, the Keys-007 mechanism): an asserted key property used inside a DataHasValue restriction is a LIFTED co-occurrence — the module admits, the told value and the restriction both lower to demands, and no identity is forced (the restriction's filler is never a ground representative).</summary>
    [TestMethod]
    public void BeltDataHasValueExpressionLowersToDemands()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                HasKey(Class("Heron"), [], ["ring"]),
                ClassAssertion(Class("Heron"), Individual("idp")),
                DataAssertion("idp", "ring", "R-77", XsdString),
                ClassAssertion(Some("nests", Intersection(Class("Wren"), new OwlDataHasValue(DataProperty("ring"), StringLiteral("R-77", XsdString)))), Individual("idq")),
            ]);

        Assert.IsEmpty(clausification.Remainder, "The lifted DataHasValue co-occurrence admits the module.");
        Assert.IsNotEmpty(clausification.DataDemandDescriptors, "The told value and the restriction lower to demands.");
        Assert.AreEqual(0, clausification.KeyForcedUnions, "The restriction's filler is not a ground representative; no identity is forced.");
    }

    /// <summary>PIG-1 + REASON-P2 + the counting-remainder suppression: three pairwise told-distinct successors under a told max-2 clash by the pigeonhole — the WebOnt-maxCardinality-001 shape — with the counting remainder SUPPRESSED so the clash surfaces instead of delegating.</summary>
    [TestMethod]
    public void Pig1UnqualifiedToldPigeonholeClashes()
    {
        ClausificationResult clausification = ClausifyRider(
            [
                ClassAssertion(Max("prop", 2, null), Individual("idp")),
                ObjectPropertyAssertion("prop", "idp", "idq"),
                ObjectPropertyAssertion("prop", "idp", "idr"),
                ObjectPropertyAssertion("prop", "idp", "ids"),
                Different("idq", "idr", "ids"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "Three pairwise-distinct successors under max-2 are a pigeonhole.");
        Assert.IsNotNull(clausification.GroundClashReason);
        Assert.StartsWith("GroundCountingPigeonhole(", clausification.GroundClashReason, "The clash names the rider.");
        foreach(string entry in clausification.Remainder)
        {
            Assert.DoesNotStartWith("GroundEdgeOnCountingRole(", entry, "The clashing subject's counting remainder is suppressed.");
        }
    }

    /// <summary>The rider-off control: the SAME pigeonhole module with the rider disabled keeps the counting-edge remainder and decides nothing — delegation, never a verdict, when the search does not run.</summary>
    [TestMethod]
    public void Pig1DarkRiderKeepsRemainderAndDecidesNothing()
    {
        ClausificationResult clausification = ClausifyKeys(
            [
                ClassAssertion(Max("prop", 2, null), Individual("idp")),
                ObjectPropertyAssertion("prop", "idp", "idq"),
                ObjectPropertyAssertion("prop", "idp", "idr"),
                ObjectPropertyAssertion("prop", "idp", "ids"),
                Different("idq", "idr", "ids"),
            ]);

        Assert.IsFalse(clausification.GroundClash, "The dark rider decides nothing.");
        Assert.Contains(ContextRemainderNames.GroundEdgeOnCountingRole(Utf8Strings.From(Example + "prop")), clausification.Remainder, "The counting remainder delegates exactly as before the rider existed.");
    }

    /// <summary>PIG-2 + PIG-P1 + the MU-HK-14 face: insufficient told distinctness finds no clique — the module keeps its remainder and delegates; the rider NEVER claims consistency.</summary>
    [TestMethod]
    public void Pig2InsufficientDistinctnessKeepsRemainder()
    {
        ClausificationResult clausification = ClausifyRider(
            [
                ClassAssertion(Max("prop", 2, null), Individual("idp")),
                ObjectPropertyAssertion("prop", "idp", "idq"),
                ObjectPropertyAssertion("prop", "idp", "idr"),
                ObjectPropertyAssertion("prop", "idp", "ids"),
                Different("idq", "idr"),
            ]);

        Assert.IsFalse(clausification.GroundClash, "Two distinct successors satisfy max-2.");
        Assert.Contains(ContextRemainderNames.GroundEdgeOnCountingRole(Utf8Strings.From(Example + "prop")), clausification.Remainder, "The non-clash case keeps the delegation remainder.");
    }

    /// <summary>PIG-3 (told fillers): two told-distinct told-Wren fillers under a told qualified max-1 clash — the New-Feature-ObjectQCR-001 refutation shape.</summary>
    [TestMethod]
    public void Pig3QualifiedToldFillerClashes()
    {
        ClausificationResult clausification = ClausifyRider(
            [
                ClassAssertion(Max("nests", 1, Class("Wren")), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ClassAssertion(Class("Wren"), Individual("idq")),
                ClassAssertion(Class("Wren"), Individual("idr")),
                Different("idq", "idr"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "Two told-distinct told-Wren fillers under max-1 are a qualified pigeonhole.");
        Assert.IsNotNull(clausification.GroundClashReason);
        Assert.StartsWith("GroundCountingPigeonhole(", clausification.GroundClashReason, "The clash names the rider.");
    }

    /// <summary>PIG-3's complement-wrapped face (the New-Feature-ObjectQCR-001 corpus shape): the harness-style refutation asserts the BARE complement of the qualified min with no NNF of its own, and the rider's engine-side normalization lands it as told max-1, correlates the told edges, told fillers, and told distinctness, and clashes; with the rider off the complement lowers as written and decides nothing.</summary>
    [TestMethod]
    public void Pig3ComplementWrappedMinNormalizesAndClashes()
    {
        OwlAxiom[] axioms =
        [
            ClassAssertion(new OwlObjectComplementOf(new OwlObjectCardinality(OwlCardinalityKind.Min, 2, Property("nests"), Class("Wren"))), Individual("idp")),
            ObjectPropertyAssertion("nests", "idp", "idq"),
            ObjectPropertyAssertion("nests", "idp", "idr"),
            ClassAssertion(Class("Wren"), Individual("idq")),
            ClassAssertion(Class("Wren"), Individual("idr")),
            Different("idq", "idr"),
        ];

        ClausificationResult rider = ClausifyRider(axioms);
        Assert.IsTrue(rider.GroundClash, "The complement-wrapped min-2 normalizes to told max-1; two told-distinct told-Wren fillers clash.");
        Assert.IsNotNull(rider.GroundClashReason);
        Assert.StartsWith("GroundCountingPigeonhole(", rider.GroundClashReason, "The clash names the rider.");

        ClausificationResult dark = ClausifyKeys(axioms);
        Assert.IsFalse(dark.GroundClash, "With the rider off the complement lowers as written and the module keeps delegating.");
    }

    /// <summary>PIG-4: a missing told filler membership drops the successor from the qualified count — no clash, the remainder delegates.</summary>
    [TestMethod]
    public void Pig4MissingToldFillerKeepsRemainder()
    {
        ClausificationResult clausification = ClausifyRider(
            [
                ClassAssertion(Max("nests", 1, Class("Wren")), Individual("idp")),
                ObjectPropertyAssertion("nests", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                ClassAssertion(Class("Wren"), Individual("idq")),
                Different("idq", "idr"),
            ]);

        Assert.IsFalse(clausification.GroundClash, "idr need not be a Wren; the qualified bound counts Wren fillers only.");
    }

    /// <summary>PIG-6: a told sub-property edge counts toward the super-role's bound through the closed graph.</summary>
    [TestMethod]
    public void Pig6SubRoleEdgeCountsTowardBound()
    {
        ClausificationResult clausification = ClausifyRider(
            [
                SubObjectPropertyOf("tends", "nests"),
                ClassAssertion(Max("nests", 1, null), Individual("idp")),
                ObjectPropertyAssertion("tends", "idp", "idq"),
                ObjectPropertyAssertion("nests", "idp", "idr"),
                Different("idq", "idr"),
            ]);

        Assert.IsTrue(clausification.GroundClash, "The tends edge lifts onto nests; two distinct successors under max-1 clash.");
    }

    /// <summary>PIG-P2 + MU-HK-11's above-bound face: one successor past the clique-search ceiling (<see cref="ContextClausifier.GroundCountingCliqueBound"/>) keeps the rider SILENT and the remainder delegating — the counts derive from the constant, so the pin tracks its value structurally.</summary>
    [TestMethod]
    public void PigAboveCliqueBoundStaysSilent()
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Max("prop", ContextClausifier.GroundCountingCliqueBound - 1, null), Individual("idp")),
        ];
        string[] successors = new string[ContextClausifier.GroundCountingCliqueBound + 1];
        for(int i = 0; i < successors.Length; i++)
        {
            successors[i] = $"succ{i}";
            axioms.Add(ObjectPropertyAssertion("prop", "idp", successors[i]));
        }

        axioms.Add(Different(successors));
        ClausificationResult clausification = ClausifyRider([.. axioms]);

        Assert.IsFalse(clausification.GroundClash, "Above the ceiling the search is silent — delegation, never a verdict on an unsearched space.");
        Assert.Contains(ContextRemainderNames.GroundEdgeOnCountingRole(Utf8Strings.From(Example + "prop")), clausification.Remainder, "The remainder keeps delegating.");
    }

    /// <summary>PIG-P3: a decisive clash at EXACTLY the ceiling — <see cref="ContextClausifier.GroundCountingCliqueBound"/> pairwise-distinct successors, one over the told bound — verifies below-bound completeness, not just above-bound silence.</summary>
    [TestMethod]
    public void PigExactCliqueBoundDecides()
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Max("prop", ContextClausifier.GroundCountingCliqueBound - 1, null), Individual("idp")),
        ];
        string[] successors = new string[ContextClausifier.GroundCountingCliqueBound];
        for(int i = 0; i < successors.Length; i++)
        {
            successors[i] = $"succ{i}";
            axioms.Add(ObjectPropertyAssertion("prop", "idp", successors[i]));
        }

        axioms.Add(Different(successors));
        ClausificationResult clausification = ClausifyRider([.. axioms]);

        Assert.IsTrue(clausification.GroundClash, "Sixteen pairwise-distinct successors under max-15 clash exactly at the ceiling.");
        Assert.IsNotNull(clausification.GroundClashReason);
        Assert.StartsWith("GroundCountingPigeonhole(", clausification.GroundClashReason, "The clash names the rider.");
    }

    /// <summary>FIX-1 + STAT-P1's round-0 face: the ring key merges the pair at round 1's told join; the merged class's told Heron membership carries into round 2's told join, which fires the wing key onto idr; round 3 finds the fixpoint — one representative, consistent.</summary>
    [TestMethod]
    public void Fix1CascadeMergesAcrossSeededRounds()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Thing, [], ["ring"]),
            HasKey(Class("Heron"), [], ["wing"]),
            DataAssertion("idp", "ring", "R-1", XsdString),
            DataAssertion("idq", "ring", "R-1", XsdString),
            ClassAssertion(Class("Heron"), Individual("idq")),
            DataAssertion("idp", "wing", "W-1", XsdString),
            ClassAssertion(Class("Heron"), Individual("idr")),
            DataAssertion("idr", "wing", "W-1", XsdString),
        ];

        ClausificationResult first = ClausifyKeys(axioms);
        Assert.AreEqual(1, first.KeyForcedUnions, "Round 1: the global ring key merges idp and idq.");

        List<(Utf8String First, Utf8String Second)> seeds = [.. first.KeyUnionPairs];
        ClausificationResult second = ClausifySeeded(axioms, seeds);
        Assert.AreEqual(1, second.KeyForcedUnions, "Round 2: the merged class is a told Heron and shares the wing value with idr.");

        seeds.AddRange(second.KeyUnionPairs);
        ClausificationResult third = ClausifySeeded(axioms, seeds);
        Assert.AreEqual(0, third.KeyForcedUnions, "Round 3: the fixpoint is dry.");
        Assert.HasCount(1, third.GroundRepresentatives, "All three individuals share one representative.");
        ContextSaturationEngine engine = SaturateClausification(third);
        Assert.IsFalse(engine.IsInconsistent, "The cascaded module is consistent.");
    }

    /// <summary>KDIFF-3: the cascade's second-round merge collides with told distinctness — the seeded round labels the collision with KEY provenance (a told collision would have clashed in the un-seeded first round).</summary>
    [TestMethod]
    public void Kdiff3CascadeCollisionRefutesWithKeyProvenance()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Thing, [], ["ring"]),
            HasKey(Class("Heron"), [], ["wing"]),
            DataAssertion("idp", "ring", "R-1", XsdString),
            DataAssertion("idq", "ring", "R-1", XsdString),
            ClassAssertion(Class("Heron"), Individual("idq")),
            DataAssertion("idp", "wing", "W-1", XsdString),
            ClassAssertion(Class("Heron"), Individual("idr")),
            DataAssertion("idr", "wing", "W-1", XsdString),
            Different("idp", "idr"),
        ];

        ClausificationResult first = ClausifyKeys(axioms);
        Assert.AreEqual(1, first.KeyForcedUnions, "Round 1: the ring key merges idp and idq; idp and idr stay apart.");
        Assert.IsFalse(first.GroundClash, "No collision yet.");

        ClausificationResult second = ClausifySeeded(axioms, first.KeyUnionPairs);
        Assert.IsTrue(second.GroundClash, "Round 2: the wing key merges the class onto idr against told distinctness.");
        Assert.IsNotNull(second.GroundClashReason);
        Assert.StartsWith("KeyMergeCollision(", second.GroundClashReason, "The seeded-round collision carries key provenance.");
    }

    /// <summary>STAT-P1: the reasoner-loop statistics ride the decision through the production seam — the told cascade decides whole and consistent with the fixpoint rounds, the forced unions, and the dry post-saturation join's candidates recorded on the returned totals.</summary>
    [TestMethod]
    public async Task Stat1CascadeStatisticsRideTheSeamDecision()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Thing, [], ["ring"]),
            HasKey(Class("Heron"), [], ["wing"]),
            DataAssertion("idp", "ring", "R-1", XsdString),
            DataAssertion("idq", "ring", "R-1", XsdString),
            ClassAssertion(Class("Heron"), Individual("idq")),
            DataAssertion("idp", "wing", "W-1", XsdString),
            ClassAssertion(Class("Heron"), Individual("idr")),
            DataAssertion("idr", "wing", "W-1", XsdString),
        ];

        ModuleDecision decision = await DecideThroughSeamAsync(axioms, ReasoningBudget.Unbounded).ConfigureAwait(false);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The survey admits the cascade and the seam returns the context verdict.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The cascaded module is consistent.");
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        Assert.IsTrue(totals.ContextDecided, "The context engine produced the verdict.");
        Assert.AreEqual(3, totals.MergeRounds, "The ring key fires at round 1, the wing key at round 2, and round 3 is dry.");
        Assert.AreEqual(2, totals.KeyForcedUnions, "The ring and wing keys each force one union.");
        Assert.AreEqual(2, totals.KeyJoinCandidates, "The dry post-saturation join enumerates the one merged representative under both descriptors.");
        Assert.IsGreaterThan(0, totals.InferenceAttempts, "The deciding round's saturation charged attempts.");
    }

    /// <summary>BUDGET-P1: the derived-join collision measures the cross-round budget on both faces — the unbounded run decides INCONSISTENT at the seeded round with round 1's spent attempts carried past the ground-clash statistics' no-engine zero, and re-running with exactly that spend as the ceiling completes round 1 at the ceiling, fires the join, and the summed running total gates the seeded round into a budget abstention; an engine that reset the total per round would sail on and decide the collision instead.</summary>
    [TestMethod]
    public async Task Budget1SummedAttemptsGateTheSeededRound()
    {
        OwlAxiom[] axioms =
        [
            SubClassOf(Class("Wren"), Class("Heron")),
            HasKey(Class("Heron"), [], ["ring"]),
            ClassAssertion(Class("Wren"), Individual("idp")),
            ClassAssertion(Class("Heron"), Individual("idq")),
            DataAssertion("idp", "ring", "R-77", XsdString),
            DataAssertion("idq", "ring", "R-77", XsdString),
            Different("idp", "idq"),
        ];

        ModuleDecision unbounded = await DecideThroughSeamAsync(axioms, ReasoningBudget.Unbounded).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unbounded.Outcome, "The derived-join collision decides under an unbounded budget.");
        Assert.IsFalse(unbounded.Verdict!.IsConsistent, "The forced merge collides with told distinctness at the seeded round.");
        ContextSaturationStatistics unboundedTotals = unbounded.Statistics.ContextTotals;
        Assert.AreEqual(2, unboundedTotals.MergeRounds, "The post-saturation join fires at round 1 and the seeded round 2 collides.");
        Assert.AreEqual(1, unboundedTotals.KeyForcedUnions, "One derived-membership union fires.");
        Assert.IsGreaterThan(0, unboundedTotals.InferenceAttempts, "Round 1's spent attempts carry into the clash statistics.");

        ModuleDecision starved = await DecideThroughSeamAsync(axioms, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: (int)unboundedTotals.InferenceAttempts)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, starved.Outcome, "Round 1 completes at the inclusive ceiling; the summed running total gates the seeded round into an abstention.");
        ContextSaturationStatistics starvedTotals = starved.Statistics.ContextTotals;
        Assert.IsFalse(starvedTotals.ContextDecided, "A budget abstention is not a context verdict.");
        Assert.AreEqual(unboundedTotals.InferenceAttempts, starvedTotals.InferenceAttempts, "The abstention carries the summed round total.");
        Assert.AreEqual(1, starvedTotals.MergeRounds, "The gate stops the fixpoint before the seeded round runs.");
    }

    /// <summary>
    /// The clause-population axis accumulates across the fixpoint's rounds exactly
    /// as the attempt axis does: each round builds a FRESH engine whose insertion
    /// count restarts at zero, so a ceiling equal to the first round's own
    /// population gates the seeded round at the round boundary rather than inside
    /// round one. An engine that checked only its current round's insertions would
    /// sail past the boundary and decide the seeded round's collision instead of
    /// abstaining.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task PopulationBoundSumsTheDerivedTotalAcrossKeyJoinRounds()
    {
        OwlAxiom[] axioms =
        [
            SubClassOf(Class("Wren"), Class("Heron")),
            HasKey(Class("Heron"), [], ["ring"]),
            ClassAssertion(Class("Wren"), Individual("idp")),
            ClassAssertion(Class("Heron"), Individual("idq")),
            DataAssertion("idp", "ring", "R-77", XsdString),
            DataAssertion("idq", "ring", "R-77", XsdString),
            Different("idp", "idq"),
        ];

        ModuleDecision unbounded = await DecideThroughSeamAsync(axioms, ReasoningBudget.Unbounded).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unbounded.Outcome, "The derived-join collision decides under an unbounded budget.");
        Assert.IsFalse(unbounded.Verdict!.IsConsistent, "The forced merge collides with told distinctness at the seeded round.");

        ModuleDecision attemptStarved = await DecideThroughSeamAsync(axioms, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: (int)unbounded.Statistics.ContextTotals.InferenceAttempts)).ConfigureAwait(false);
        int roundOnePopulation = attemptStarved.Statistics.ContextTotals.ClausesDerived;
        Assert.IsGreaterThan(0, roundOnePopulation, "Round one inserted clauses before its post-saturation join fired.");

        ModuleDecision populationStarved = await DecideThroughSeamAsync(axioms, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: roundOnePopulation)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, populationStarved.Outcome, "Round one completes at the inclusive ceiling; the summed running population gates the seeded round into an abstention.");
        ContextSaturationStatistics populationStarvedTotals = populationStarved.Statistics.ContextTotals;
        Assert.IsFalse(populationStarvedTotals.ContextDecided, "A budget abstention is not a context verdict.");
        Assert.AreEqual(1, populationStarvedTotals.MergeRounds, "The gate stops the fixpoint before the seeded round runs.");
        Assert.AreEqual(roundOnePopulation, populationStarvedTotals.ClausesDerived, "The abstention carries round one's own population, the total the boundary gated on.");
    }

    /// <summary>STAT-P2: the ground-counting clash statistic rides the seam decision and discriminates the deciding clash reason — the PIG-1 pigeonhole decides inconsistent with the counter at one, and a key-forced collision decides inconsistent with the counter at zero.</summary>
    [TestMethod]
    public async Task Stat2PigeonholeClashStatisticRidesTheSeamDecision()
    {
        OwlAxiom[] pigeonhole =
        [
            ClassAssertion(Max("prop", 2, null), Individual("idp")),
            ObjectPropertyAssertion("prop", "idp", "idq"),
            ObjectPropertyAssertion("prop", "idp", "idr"),
            ObjectPropertyAssertion("prop", "idp", "ids"),
            Different("idq", "idr", "ids"),
        ];

        ModuleDecision pigeonholeDecision = await DecideThroughSeamAsync(pigeonhole, ReasoningBudget.Unbounded).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, pigeonholeDecision.Outcome, "The told pigeonhole decides through the seam.");
        Assert.IsFalse(pigeonholeDecision.Verdict!.IsConsistent, "Three pairwise-distinct successors under max-2 are inconsistent.");
        Assert.AreEqual(1, pigeonholeDecision.Statistics.ContextTotals.GroundCountingClashes, "The deciding clash is the rider's pigeonhole.");

        OwlAxiom[] keyCollision =
        [
            HasKey(Thing, [], ["ring"]),
            DataAssertion("idp", "ring", "R-77", XsdString),
            DataAssertion("idq", "ring", "R-77", XsdString),
            Different("idp", "idq"),
        ];

        ModuleDecision keyDecision = await DecideThroughSeamAsync(keyCollision, ReasoningBudget.Unbounded).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, keyDecision.Outcome, "The key collision decides through the seam.");
        Assert.IsFalse(keyDecision.Verdict!.IsConsistent, "The key-forced merge collides with told distinctness.");
        Assert.AreEqual(0, keyDecision.Statistics.ContextTotals.GroundCountingClashes, "A key collision is not a counting clash — the counter reads the deciding reason, not any clash.");
    }

    /// <summary>Clausifies with the key machinery live and the rider off — the pins' rider-disabled face.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The clausification.</returns>
    private static ClausificationResult ClausifyKeys(OwlAxiom[] axioms)
    {
        return ContextClausifier.Clausify(Module(axioms), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false);
    }

    /// <summary>Clausifies with the counting rider ON — the pins' dark-flag override.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The clausification.</returns>
    private static ClausificationResult ClausifyRider(OwlAxiom[] axioms)
    {
        return ContextClausifier.Clausify(Module(axioms), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: true, nominalDeciderEnabled: false);
    }

    /// <summary>Clausifies with earlier rounds' unions seeded, the fixpoint re-entry.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="seeds">The seeded union pairs.</param>
    /// <returns>The clausification.</returns>
    private static ClausificationResult ClausifySeeded(OwlAxiom[] axioms, IReadOnlyList<(Utf8String First, Utf8String Second)> seeds)
    {
        return ContextClausifier.Clausify(Module(axioms), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, seeds, riderEnabled: false, nominalDeciderEnabled: false);
    }

    /// <summary>Decides a module through the production seam — the survey-gated context engine under the given budget with an abstaining sentinel fallback — the entry the STAT/BUDGET seam pins observe.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="budget">The inference budget bounding the decision.</param>
    /// <returns>The seam decision.</returns>
    private async Task<ModuleDecision> DecideThroughSeamAsync(OwlAxiom[] axioms, ReasoningBudget budget)
    {
        DescriptionLogicDelegate seam = ReasoningEngines.ContextSaturation(budget, DecideAbstain);

        return await seam(Module(axioms), TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>The abstaining sentinel fallback behind the seam pins: it decides no module, so a delegation or an exhaustion surfaces as the abstained outcome rather than borrowing an oracle's verdict.</summary>
    /// <param name="module">The module the context engine did not decide.</param>
    /// <param name="cancellationToken">The token, unused because the sentinel does no work.</param>
    /// <returns>An abstaining decision carrying only the module's axiom count.</returns>
    private static ValueTask<ModuleDecision> DecideAbstain(ReasoningModule module, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return new ValueTask<ModuleDecision>(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty with { ModuleAxiomCount = module.Axioms.Count }));
    }

    /// <summary>Builds the engine over a clausification and saturates to the fixpoint under an unbounded budget, running the Self-ghost pass as the production path does after a completed saturation.</summary>
    /// <param name="clausification">The clausification.</param>
    /// <returns>The saturated engine.</returns>
    private ContextSaturationEngine SaturateClausification(ClausificationResult clausification)
    {
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
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

    /// <summary>A max-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The upper bound.</param>
    /// <param name="filler">The qualified filler, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }
}
