using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Hand-derived pins for the maintained OWL 2 RL closure
/// (<see cref="OwlRlMaintainedClosure"/>) under add and retract edits: each
/// pin carries the full hand-derivation of its expected facts in its
/// documentation and asserts the maintained engine against that ground truth,
/// plus the checkpoint contract that the maintained derived set equals the
/// from-scratch naive oracle over the current base after every op. The pins
/// target the corners the randomized differential battery cannot reliably
/// reach: canonical-choice unmasking and takeover, falsity born from a pure
/// retract, base demotion and promotion, seed and structure-node handling,
/// partial alternate support under un-merge, cancellation recovery, and the
/// producer-versus-symmetry inventory. A disagreement between a hand-derivation
/// and the engine is a finding to surface, never an expectation to fit.
/// </summary>
[TestClass]
internal sealed class OwlRlMaintainedClosurePinTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The <c>xsd:integer</c> datatype IRI the one-cardinality bound literal carries.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>
    /// Canonical-choice unmask by retract. A restriction on <c>p</c> carries
    /// two <c>owl:someValuesFrom</c> fillers; the canonical read is the
    /// smaller-id filler, so retracting its structural triple flips the read to
    /// the other filler, tearing down or standing up the derived typing
    /// depending on which filler the edge value carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cls-svf1 is <c>onProperty(x,p) ∧ someValuesFrom(x,F) ∧ p(u,v) ∧
    /// type(v,F) → type(u,x)</c>, and the maintained engine reads the canonical
    /// filler <c>SingleObjectOf(x, someValuesFrom) = min(F1,F2)</c>. F1 is
    /// minted first, so it is the canonical filler while both structural triples
    /// are present.
    /// </para>
    /// <para>
    /// Vanish. Base <c>{onProperty(x,p), someValuesFrom(x,F1),
    /// someValuesFrom(x,F2), p(u,v), type(v,F1)}</c>: the F1 filler triple
    /// supports cls-svf1, so <c>type(u,x)</c> derives. Retract
    /// <c>someValuesFrom(x,F1)</c>: the remaining F2 filler gives no support
    /// (<c>type(v,F2)</c> does not hold) and no other rule concludes
    /// <c>type(u,x)</c> — it vanishes.
    /// </para>
    /// <para>
    /// Survive. Base with <c>type(v,F2)</c> in place of <c>type(v,F1)</c>:
    /// the F2 filler supports the typing, so it derives while both fillers
    /// stand. Retract <c>someValuesFrom(x,F1)</c>: the supporting F2 triple
    /// still stands, so the typing survives — each asserted filler is an
    /// independent rule instance, and retracting one never disturbs a
    /// derivation another supports.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void PerValueFillersSupportAndTearDownIndependently()
    {
        //Vanish: v carries the canonical filler only.
        TermDictionary vanishDictionary = new();
        OwlRlTerms vanishTerms = new(vanishDictionary);
        TermId vp = OwlRlBatteryHelpers.Mint(vanishDictionary, "p");
        TermId vx = OwlRlBatteryHelpers.Mint(vanishDictionary, "x");
        TermId vu = OwlRlBatteryHelpers.Mint(vanishDictionary, "u");
        TermId vv = OwlRlBatteryHelpers.Mint(vanishDictionary, "v");
        TermId vf1 = OwlRlBatteryHelpers.Mint(vanishDictionary, "F1");
        TermId vf2 = OwlRlBatteryHelpers.Mint(vanishDictionary, "F2");
        Assert.IsLessThan(vf2.Encoded, vf1.Encoded, "F1 must be minted before F2 so it is the canonical (minimum-id) filler.");

        EncodedTriple vSvf1 = OwlRlBatteryHelpers.Triple(vx, vanishTerms.SomeValuesFrom, vf1);
        EncodedTriple vMembership = OwlRlBatteryHelpers.Triple(vu, vanishTerms.Type, vx);
        HashSet<EncodedTriple> vanishBase =
        [
            OwlRlBatteryHelpers.Triple(vx, vanishTerms.OnProperty, vp),
            vSvf1,
            OwlRlBatteryHelpers.Triple(vx, vanishTerms.SomeValuesFrom, vf2),
            OwlRlBatteryHelpers.Triple(vu, vp, vv),
            OwlRlBatteryHelpers.Triple(vv, vanishTerms.Type, vf1),
        ];

        OwlRlMaintainedClosure vanishEngine = new(vanishBase, vanishTerms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(vMembership, vanishEngine.Current.Derived, "The F1 filler triple must drive cls-svf1 while it stands.");
        AssertCheckpointContract(vanishEngine.Current, vanishBase, vanishTerms, default);

        vanishBase.Remove(vSvf1);
        OwlRlResult vanished = vanishEngine.Apply([], [vSvf1], TestContext.CancellationToken);
        Assert.DoesNotContain(vMembership, vanished.Derived, "Retracting the supporting filler removes the typing's only support, so it vanishes.");
        AssertCheckpointContract(vanished, vanishBase, vanishTerms, default);

        //Survive: v carries the second filler only.
        TermDictionary appearDictionary = new();
        OwlRlTerms appearTerms = new(appearDictionary);
        TermId ap = OwlRlBatteryHelpers.Mint(appearDictionary, "p");
        TermId ax = OwlRlBatteryHelpers.Mint(appearDictionary, "x");
        TermId au = OwlRlBatteryHelpers.Mint(appearDictionary, "u");
        TermId av = OwlRlBatteryHelpers.Mint(appearDictionary, "v");
        TermId af1 = OwlRlBatteryHelpers.Mint(appearDictionary, "F1");
        TermId af2 = OwlRlBatteryHelpers.Mint(appearDictionary, "F2");
        Assert.IsLessThan(af2.Encoded, af1.Encoded, "F1 must be minted before F2 so it is the canonical (minimum-id) filler.");

        EncodedTriple aSvf1 = OwlRlBatteryHelpers.Triple(ax, appearTerms.SomeValuesFrom, af1);
        EncodedTriple aMembership = OwlRlBatteryHelpers.Triple(au, appearTerms.Type, ax);
        HashSet<EncodedTriple> appearBase =
        [
            OwlRlBatteryHelpers.Triple(ax, appearTerms.OnProperty, ap),
            aSvf1,
            OwlRlBatteryHelpers.Triple(ax, appearTerms.SomeValuesFrom, af2),
            OwlRlBatteryHelpers.Triple(au, ap, av),
            OwlRlBatteryHelpers.Triple(av, appearTerms.Type, af2),
        ];

        OwlRlMaintainedClosure appearEngine = new(appearBase, appearTerms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(aMembership, appearEngine.Current.Derived, "The F2 filler triple supports the typing while both fillers stand — each asserted filler fires its own rule instance.");
        AssertCheckpointContract(appearEngine.Current, appearBase, appearTerms, default);

        appearBase.Remove(aSvf1);
        OwlRlResult appeared = appearEngine.Apply([], [aSvf1], TestContext.CancellationToken);
        Assert.Contains(aMembership, appeared.Derived, "Retracting the non-supporting filler never disturbs a derivation the other filler supports.");
        AssertCheckpointContract(appeared, appearBase, appearTerms, default);
    }

    /// <summary>
    /// Adding a second filler is monotone. A restriction reads the only
    /// <c>owl:someValuesFrom</c> filler F2 and derives a typing; adding a
    /// smaller-id filler F1 the edge value does not carry adds a rule
    /// instance and disturbs nothing — the typing survives, because each
    /// asserted filler fires independently and an add never removes support.
    /// </summary>
    /// <remarks>
    /// Base <c>{onProperty(x,p), someValuesFrom(x,F2), p(u,v), type(v,F2)}</c>:
    /// the F2 filler supports cls-svf1, so <c>type(u,x)</c> derives. Adding
    /// <c>someValuesFrom(x,F1)</c> with <c>F1 &lt; F2</c> leaves the F2
    /// support standing, so the typing persists through the incremental
    /// apply.
    /// </remarks>
    [TestMethod]
    public void AddingASecondFillerNeverTearsDownTheTyping()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId f1 = OwlRlBatteryHelpers.Mint(dictionary, "F1");
        TermId f2 = OwlRlBatteryHelpers.Mint(dictionary, "F2");
        Assert.IsLessThan(f2.Encoded, f1.Encoded, "F1 is minted before F2, so the add exercises the smaller-identifier position the historic canonical pick favoured.");

        EncodedTriple svf1 = OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, f1);
        EncodedTriple membership = OwlRlBatteryHelpers.Triple(u, terms.Type, x);
        HashSet<EncodedTriple> currentBase =
        [
            OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, f2),
            OwlRlBatteryHelpers.Triple(u, p, v),
            OwlRlBatteryHelpers.Triple(v, terms.Type, f2),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(membership, engine.Current.Derived, "The sole filler F2 drives cls-svf1 before the add.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Add(svf1);
        OwlRlResult afterAdd = engine.Apply([svf1], [], TestContext.CancellationToken);
        Assert.Contains(membership, afterAdd.Derived, "Adding the smaller-id filler leaves the F2 support standing; an add never removes a derivation.");
        AssertCheckpointContract(afterAdd, currentBase, terms, default);
    }

    /// <summary>
    /// A falsity fires through every asserted bound. A restriction carries a
    /// harmless <c>owl:maxCardinality</c> bound and a zero bound; the zero
    /// bound contradicts the instance's edge immediately, no matter which
    /// bound holds the smaller identifier — a harmless sibling never masks
    /// an asserted falsity, so no pure retract can ever give birth to one.
    /// </summary>
    /// <remarks>
    /// cls-maxc1 fires once per asserted bound of the restriction. Base
    /// carries <c>maxCardinality(x,"5")</c> and <c>maxCardinality(x,"0")</c>
    /// with the harmless "5" minted ahead of the vocabulary so it holds the
    /// smaller identifier the historic canonical pick favoured, plus
    /// <c>onProperty(x,q)</c>, the instance typing <c>type(u,x)</c>, and the
    /// edge <c>q(u,v)</c>. The zero bound fires cls-maxc1 from the first
    /// closure: the falsity is monotone in the asserted set, so it can only
    /// be present from the start, never born by a retract.
    /// </remarks>
    [TestMethod]
    public void FalsityFiresThroughEveryAssertedBound()
    {
        TermDictionary dictionary = new();

        //Minted ahead of the vocabulary, the harmless bound takes the
        //smaller term identifier — the position the historic canonical
        //pick read, pinning that it no longer masks the zero bound.
        TermId five = OwlRlBatteryHelpers.Literal(dictionary, "5", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "maxbound");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId zero = OwlRlBatteryHelpers.Literal(dictionary, "0", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");
        Assert.IsLessThan(zero.Encoded, five.Encoded, "The harmless bound is minted before the zero bound, taking the smaller identifier the historic pick favoured.");

        HashSet<EncodedTriple> currentBase =
        [
            OwlRlBatteryHelpers.Triple(x, terms.OnProperty, q),
            OwlRlBatteryHelpers.Triple(x, terms.MaxCardinality, five),
            OwlRlBatteryHelpers.Triple(x, terms.MaxCardinality, zero),
            OwlRlBatteryHelpers.Triple(u, terms.Type, x),
            OwlRlBatteryHelpers.Triple(u, q, v),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(engine.Current.IsConsistent, "The zero bound contradicts the instance's edge from the first closure; the harmless sibling masks nothing.");
        Assert.AreEqual(EntailmentRules.ClsMaxc1, engine.Current.InconsistencyRule, "The immediate falsity is cls-maxc1 through the asserted zero bound.");
    }

    /// <summary>
    /// Base demotion then promotion. Adding a base fact equal to a derived one
    /// demotes it out of the derived set; retracting that base fact rederives it
    /// as a derived fact again.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B), type(x,A)}</c>: cax-sco derives
    /// <c>type(x,B)</c>. Applying <c>add {type(x,B)}</c> makes it a base fact,
    /// so it leaves the derived set (a demotion) while remaining in the closure;
    /// the naive oracle over the new base, whose derived set is beyond-base,
    /// likewise excludes it. Applying <c>retract {type(x,B)}</c> removes the base
    /// fact, whose cax-sco derivation survives, so the head-bound matcher
    /// rederives it directly back into the derived set.
    /// </remarks>
    [TestMethod]
    public void BaseDemotionThenPromotion()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        HashSet<EncodedTriple> currentBase = [aSubB, xIsA];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(xIsB, engine.Current.Derived, "cax-sco must derive type(x,B) over the initial base.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Add(xIsB);
        OwlRlResult afterAdd = engine.Apply([xIsB], [], TestContext.CancellationToken);
        Assert.DoesNotContain(xIsB, afterAdd.Derived, "Adding the derived fact as base demotes it out of the derived set.");
        Assert.AreEqual(1, engine.Statistics.BaseDemotions, "The add must count exactly one base demotion.");
        AssertCheckpointContract(afterAdd, currentBase, terms, default);

        currentBase.Remove(xIsB);
        OwlRlResult afterRetract = engine.Apply([], [xIsB], TestContext.CancellationToken);
        Assert.Contains(xIsB, afterRetract.Derived, "Retracting the base fact rederives it as a derived fact.");
        Assert.IsGreaterThanOrEqualTo(1, engine.Statistics.DirectlyRederived, "The retract must rederive at least one fact directly.");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);
    }

    /// <summary>
    /// Seeded-triple retract. A datatype-hierarchy seed triple that is also a
    /// base fact stays in the closure on retract, moving from base to derived —
    /// deletion never propagates through a seed.
    /// </summary>
    /// <remarks>
    /// The built-in datatype map seeds <c>subClassOf(xsd:byte, xsd:short)</c>
    /// into every closure as axiomatic knowledge. Constructing the engine over a
    /// base that also states it makes it a base fact, so it is not in the derived
    /// set at first. Retracting it removes the base membership but, being a seed,
    /// it stays in the closure and enters the derived set (a promotion); the
    /// resulting closure equals the naive oracle over the emptied base, which
    /// carries the same seed set.
    /// </remarks>
    [TestMethod]
    public void SeededTripleRetractStaysInClosure()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        (TermId subType, TermId superType) = terms.DatatypeHierarchy[0];
        EncodedTriple seed = OwlRlBatteryHelpers.Triple(subType, terms.SubClassOf, superType);

        HashSet<EncodedTriple> currentBase = [seed];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.DoesNotContain(seed, engine.Current.Derived, "While the seed is also a base fact it is not in the derived set.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(seed);
        OwlRlResult afterRetract = engine.Apply([], [seed], TestContext.CancellationToken);
        Assert.Contains(seed, afterRetract.Derived, "Retracting a seeded base fact moves it into the derived set rather than dropping it.");
        Assert.AreEqual(1, engine.Statistics.BasePromotions, "The seeded retract must count exactly one base promotion.");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);
    }

    /// <summary>
    /// Transitivity-typing retract and re-add. Retracting the
    /// <c>owl:TransitiveProperty</c> typing tears down the composed edge and the
    /// five deterministic property-chain triples; re-adding it re-mints exactly
    /// the same chain nodes.
    /// </summary>
    /// <remarks>
    /// Base <c>{type(p,TransitiveProperty), p(a,b), p(b,c)}</c>: prp-trp composes
    /// <c>p(a,c)</c>, and a transitive property materialises its <c>p∘p⊑p</c>
    /// structure on the deterministic chain nodes as five triples
    /// (<c>propertyChainAxiom(p,head)</c>, both cells' <c>rdf:first</c> and
    /// <c>rdf:rest</c>). Retracting the typing removes the transitivity, so
    /// <c>p(a,c)</c> and all five chain triples vanish. Re-adding the typing
    /// re-derives <c>p(a,c)</c> and re-materialises the same five chain triples
    /// on the same content-keyed nodes.
    /// </remarks>
    [TestMethod]
    public void TransitivityTypingRetractAndReAdd()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");

        EncodedTriple transitive = OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty);
        EncodedTriple ac = OwlRlBatteryHelpers.Triple(a, p, c);
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

        HashSet<EncodedTriple> currentBase = [transitive, OwlRlBatteryHelpers.Triple(a, p, b), OwlRlBatteryHelpers.Triple(b, p, c)];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(ac, engine.Current.Derived, "prp-trp must compose p(a,c) while p is transitive.");
        foreach(EncodedTriple chainTriple in transChain)
        {
            Assert.Contains(chainTriple, engine.Current.Derived, "The transitive property must materialise its chain structure.");
        }

        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(transitive);
        OwlRlResult afterRetract = engine.Apply([], [transitive], TestContext.CancellationToken);
        Assert.DoesNotContain(ac, afterRetract.Derived, "Retracting the transitivity typing tears down the composed edge.");
        foreach(EncodedTriple chainTriple in transChain)
        {
            Assert.DoesNotContain(chainTriple, afterRetract.Derived, "Retracting the transitivity typing tears down the chain structure.");
        }

        AssertCheckpointContract(afterRetract, currentBase, terms, default);

        currentBase.Add(transitive);
        OwlRlResult afterReAdd = engine.Apply([transitive], [], TestContext.CancellationToken);
        Assert.Contains(ac, afterReAdd.Derived, "Re-adding the transitivity typing re-composes p(a,c).");
        foreach(EncodedTriple chainTriple in transChain)
        {
            Assert.Contains(chainTriple, afterReAdd.Derived, "Re-adding the transitivity typing re-materialises the same deterministic chain nodes.");
        }

        AssertCheckpointContract(afterReAdd, currentBase, terms, default);
    }

    /// <summary>
    /// List-cell retract from an intersection. Retracting the
    /// <c>rdf:first</c> of an <c>owl:intersectionOf</c> list's second cell
    /// malforms the list, so the intersection axiom derives nothing and its
    /// instance typing vanishes.
    /// </summary>
    /// <remarks>
    /// Base states <c>intersectionOf(c, [A,B])</c> as a two-cell collection with
    /// <c>type(x,A)</c> and <c>type(x,B)</c>: cls-int1 derives <c>type(x,c)</c>
    /// (and scm-int the member subclassings). Retracting <c>rdf:first</c> of the
    /// second cell leaves the list unwalkable, so the axiom parses to no members;
    /// cls-int1 no longer fires and <c>type(x,c)</c> vanishes, matching the naive
    /// oracle over the malformed base.
    /// </remarks>
    [TestMethod]
    public void IntersectionListCellRetract()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId cell0 = OwlRlBatteryHelpers.Blank(dictionary, "int-cell-0");
        TermId cell1 = OwlRlBatteryHelpers.Blank(dictionary, "int-cell-1");

        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, c);
        EncodedTriple cell1First = OwlRlBatteryHelpers.Triple(cell1, terms.First, classB);
        HashSet<EncodedTriple> currentBase =
        [
            OwlRlBatteryHelpers.Triple(c, terms.IntersectionOf, cell0),
            OwlRlBatteryHelpers.Triple(cell0, terms.First, classA),
            OwlRlBatteryHelpers.Triple(cell0, terms.Rest, cell1),
            cell1First,
            OwlRlBatteryHelpers.Triple(cell1, terms.Rest, terms.Nil),
            OwlRlBatteryHelpers.Triple(x, terms.Type, classA),
            OwlRlBatteryHelpers.Triple(x, terms.Type, classB),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(xIsC, engine.Current.Derived, "cls-int1 must type x into the intersection while the list is well-formed.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(cell1First);
        OwlRlResult afterRetract = engine.Apply([], [cell1First], TestContext.CancellationToken);
        Assert.DoesNotContain(xIsC, afterRetract.Derived, "Malforming the list leaves the intersection axiom with no members, so type(x,c) vanishes.");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);
    }

    /// <summary>
    /// SameAs-bridge retract with partial alternate support. An orbit merged by
    /// three bridges survives the retract of one bridge on the remaining two,
    /// then splits when the alternate support is also retracted, and the datum on
    /// the detaching member stops replaying onto the survivors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// eq-sym / eq-trans / eq-rep close <c>owl:sameAs</c> over a connected orbit
    /// to its full member × member product, eq-rep replays a datum onto every
    /// member of the subject's class, and eq-ref keeps a reflexive
    /// <c>owl:sameAs</c> on every mentioned term — the datum's q and val, and
    /// the detached c, included.
    /// </para>
    /// <para>
    /// Step 0. Base sameAs <c>{a≡b, b≡c, a≡c}</c> and datum <c>{q(c,val)}</c>:
    /// the orbit is <c>{a,b,c}</c>, so sameAs is all nine pairs; the three
    /// asserted leave six derived <c>{(a,a),(b,a),(b,b),(c,a),(c,b),(c,c)}</c>,
    /// the datum replays onto a and b as <c>{q(a,val),q(b,val)}</c>, and eq-ref
    /// adds <c>{q≡q, val≡val}</c>.
    /// </para>
    /// <para>
    /// Step 1. Retract <c>b≡c</c>: a still bridges to both b and c, so the orbit
    /// stands and sameAs is all nine pairs; the two asserted leave seven derived
    /// <c>{(a,a),(b,a),(b,b),(b,c),(c,a),(c,b),(c,c)}</c>, and the datum still
    /// replays onto a and b beside the same reflexives.
    /// </para>
    /// <para>
    /// Step 2. Retract <c>a≡c</c>: only <c>a≡b</c> remains, so c detaches; sameAs
    /// over <c>{a,b}</c> leaves three derived <c>{(a,a),(b,a),(b,b)}</c>, c keeps
    /// only the reflexive its datum mention forces, no cross equality to a or b
    /// survives, and the datum on c no longer replays, so
    /// <c>{q(a,val),q(b,val)}</c> vanish.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void SameAsBridgeRetractWithPartialAlternateSupport()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId val = OwlRlBatteryHelpers.Mint(dictionary, "val");

        EncodedTriple Same(TermId left, TermId right) => OwlRlBatteryHelpers.Triple(left, terms.SameAs, right);
        EncodedTriple datum = OwlRlBatteryHelpers.Triple(c, q, val);
        EncodedTriple bridgeBc = Same(b, c);
        EncodedTriple bridgeAc = Same(a, c);

        HashSet<EncodedTriple> currentBase = [Same(a, b), bridgeBc, bridgeAc, datum];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        AssertDerivedShapeEquals(
            engine.Current,
            terms,
            default,
            [
                Same(a, a), Same(b, a), Same(b, b), Same(c, a), Same(c, b), Same(c, c),
                Same(q, q), Same(val, val),
                OwlRlBatteryHelpers.Triple(a, q, val), OwlRlBatteryHelpers.Triple(b, q, val),
            ]);
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(bridgeBc);
        OwlRlResult afterFirst = engine.Apply([], [bridgeBc], TestContext.CancellationToken);
        AssertDerivedShapeEquals(
            afterFirst,
            terms,
            default,
            [
                Same(a, a), Same(b, a), Same(b, b), Same(b, c), Same(c, a), Same(c, b), Same(c, c),
                Same(q, q), Same(val, val),
                OwlRlBatteryHelpers.Triple(a, q, val), OwlRlBatteryHelpers.Triple(b, q, val),
            ]);
        AssertCheckpointContract(afterFirst, currentBase, terms, default);

        currentBase.Remove(bridgeAc);
        OwlRlResult afterSecond = engine.Apply([], [bridgeAc], TestContext.CancellationToken);
        AssertDerivedShapeEquals(
            afterSecond,
            terms,
            default,
            [Same(a, a), Same(b, a), Same(b, b), Same(c, c), Same(q, q), Same(val, val)]);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(a, q, val), afterSecond.Derived, "The detached member's datum must stop replaying onto a.");
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(b, q, val), afterSecond.Derived, "The detached member's datum must stop replaying onto b.");
        Assert.DoesNotContain(Same(c, a), afterSecond.Derived, "The detached member keeps no equality to a.");
        Assert.DoesNotContain(Same(c, b), afterSecond.Derived, "The detached member keeps no equality to b.");
        AssertCheckpointContract(afterSecond, currentBase, terms, default);
    }

    /// <summary>
    /// Cancellation poisoning and recovery. An Apply cancelled mid-pipeline
    /// leaves the state poisoned with its atomic base edit already applied; the
    /// next Apply rebuilds from scratch over that post-edit base.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B), type(x,A)}</c> derives <c>type(x,B)</c>.
    /// Applying <c>retract {type(x,A)}</c> with an already-cancelled token edits
    /// the base atomically to <c>{subClassOf(A,B)}</c> before the first
    /// cancellation check throws, leaving the engine poisoned. A following empty
    /// Apply rebuilds from scratch over <c>{subClassOf(A,B)}</c> — no instance
    /// remains, so <c>type(x,B)</c> is gone and the closure equals the naive
    /// oracle over that base, confirming the cancelled edit stood.
    /// </remarks>
    [TestMethod]
    public void CancellationPoisonsThenRebuildsOverEditedBase()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        OwlRlMaintainedClosure engine = new([aSubB, xIsA], terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(xIsB, engine.Current.Derived, "cax-sco must derive type(x,B) before the cancellation.");

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => engine.Apply([], [xIsA], cancelled.Token), "A cancelled Apply must throw.");

        //The post-op base after the cancelled edit is {subClassOf(A,B)}: the
        //atomic base edit removed type(x,A) before any throw point.
        HashSet<EncodedTriple> postOpBase = [aSubB];
        OwlRlResult recovered = engine.Apply([], [], TestContext.CancellationToken);
        Assert.AreEqual(OwlRlMaintenanceMode.RebuildPoisoned, engine.Statistics.Mode, "The Apply after a poisoned state must rebuild from scratch.");
        Assert.IsTrue(recovered.IsConsistent, "The rebuild over the edited base must be consistent.");
        Assert.DoesNotContain(xIsB, recovered.Derived, "The cancelled retract of type(x,A) stood, so type(x,B) is no longer derived.");
        AssertCheckpointContract(recovered, postOpBase, terms, default);
    }

    /// <summary>
    /// AllDisjointClasses retract removes the materialised pairwise disjointness.
    /// Retracting the <c>owl:AllDisjointClasses</c> typing, or a members-list
    /// cell, tears down both pairwise <c>owl:disjointWith</c> facts, so a
    /// subsequently added shared instance does not report inconsistency.
    /// </summary>
    /// <remarks>
    /// An <c>owl:AllDisjointClasses</c> node over <c>[C,D]</c> materialises
    /// <c>disjointWith(C,D)</c> via cax-adc and <c>disjointWith(D,C)</c> via
    /// cax-dw symmetry. Removing the typing (stream 1) or malforming the members
    /// list (stream 2) removes both facts, so adding <c>type(x,C)</c> and
    /// <c>type(x,D)</c> leaves the closure consistent with no stale disjointness.
    /// Each checkpoint equals the naive oracle over the current base.
    /// </remarks>
    [TestMethod]
    public void AllDisjointClassesRetractRemovesMaterialisedDisjointness()
    {
        //Stream 1: retract the AllDisjointClasses typing.
        TermDictionary typingDictionary = new();
        OwlRlTerms typingTerms = new(typingDictionary);
        RunAllDisjointClassesStream(typingDictionary, typingTerms, retractTyping: true);

        //Stream 2: retract a members-list cell instead.
        TermDictionary listDictionary = new();
        OwlRlTerms listTerms = new(listDictionary);
        RunAllDisjointClassesStream(listDictionary, listTerms, retractTyping: false);
    }

    /// <summary>Runs one AllDisjointClasses retract stream: build the node over [C,D], retract either its typing or a list cell, then add a shared instance and assert consistency.</summary>
    /// <param name="dictionary">The dictionary the terms mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="retractTyping">Whether to retract the <c>owl:AllDisjointClasses</c> typing; otherwise a members-list cell.</param>
    private void RunAllDisjointClassesStream(TermDictionary dictionary, OwlRlTerms terms, bool retractTyping)
    {
        TermId node = OwlRlBatteryHelpers.Mint(dictionary, "adc");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId cell0 = OwlRlBatteryHelpers.Blank(dictionary, "adc-cell-0");
        TermId cell1 = OwlRlBatteryHelpers.Blank(dictionary, "adc-cell-1");

        EncodedTriple typing = OwlRlBatteryHelpers.Triple(node, terms.Type, terms.AllDisjointClasses);
        EncodedTriple cell1First = OwlRlBatteryHelpers.Triple(cell1, terms.First, classD);
        EncodedTriple cdDisjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple dcDisjoint = OwlRlBatteryHelpers.Triple(classD, terms.DisjointWith, classC);

        HashSet<EncodedTriple> currentBase =
        [
            typing,
            OwlRlBatteryHelpers.Triple(node, terms.Members, cell0),
            OwlRlBatteryHelpers.Triple(cell0, terms.First, classC),
            OwlRlBatteryHelpers.Triple(cell0, terms.Rest, cell1),
            cell1First,
            OwlRlBatteryHelpers.Triple(cell1, terms.Rest, terms.Nil),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(cdDisjoint, engine.Current.Derived, "cax-adc must materialise disjointWith(C,D).");
        Assert.Contains(dcDisjoint, engine.Current.Derived, "cax-dw must materialise the symmetric disjointWith(D,C).");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        EncodedTriple removed = retractTyping ? typing : cell1First;
        currentBase.Remove(removed);
        OwlRlResult afterRetract = engine.Apply([], [removed], TestContext.CancellationToken);
        Assert.DoesNotContain(cdDisjoint, afterRetract.Derived, "Retracting the disjointness source must remove disjointWith(C,D).");
        Assert.DoesNotContain(dcDisjoint, afterRetract.Derived, "Retracting the disjointness source must remove disjointWith(D,C).");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);

        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);
        currentBase.Add(xIsC);
        currentBase.Add(xIsD);
        OwlRlResult afterShared = engine.Apply([xIsC, xIsD], [], TestContext.CancellationToken);
        Assert.IsTrue(afterShared.IsConsistent, "With no stale disjointness the shared instance must not report inconsistency.");
        AssertCheckpointContract(afterShared, currentBase, terms, default);
    }

    /// <summary>
    /// Symmetric-orbit co-deletion survives on an alternate producer. A
    /// <c>owl:sameAs</c> pair held by two producers survives the retract of one
    /// producer's premise, and both orientations remain; likewise a materialised
    /// <c>owl:disjointWith</c> survives the retract of its explicit statement on
    /// its cax-adc producer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SameAs. <c>o1≡o2</c> holds by prp-fp (functional q with <c>q(s,o1)</c>,
    /// <c>q(s,o2)</c>) and by cls-maxc2 (a max-1 restriction on r with
    /// <c>type(u,x)</c>, <c>r(u,o1)</c>, <c>r(u,o2)</c>). Retracting the
    /// functional typing removes the prp-fp support, but cls-maxc2 still equates
    /// the values, so <c>o1≡o2</c> and its eq-sym flip <c>o2≡o1</c> both survive.
    /// </para>
    /// <para>
    /// DisjointWith. An <c>owl:AllDisjointClasses</c> node over <c>[C,D]</c> and
    /// an explicit base <c>disjointWith(C,D)</c> both assert the pair; while the
    /// explicit statement is base, cax-adc's materialisation of it is redundant
    /// and the derived set carries only the cax-dw symmetric <c>disjointWith(D,C)</c>.
    /// Retracting the explicit statement lets cax-adc re-derive
    /// <c>disjointWith(C,D)</c> and cax-dw re-derive <c>disjointWith(D,C)</c>, so
    /// both orientations survive.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void SymmetricOrbitCoDeletionSurvivesOnAlternateProducer()
    {
        //SameAs held by prp-fp and cls-maxc2.
        TermDictionary sameDictionary = new();
        OwlRlTerms sameTerms = new(sameDictionary);
        TermId q = OwlRlBatteryHelpers.Mint(sameDictionary, "q");
        TermId s = OwlRlBatteryHelpers.Mint(sameDictionary, "s");
        TermId r = OwlRlBatteryHelpers.Mint(sameDictionary, "r");
        TermId x = OwlRlBatteryHelpers.Mint(sameDictionary, "x");
        TermId u = OwlRlBatteryHelpers.Mint(sameDictionary, "u");
        TermId o1 = OwlRlBatteryHelpers.Mint(sameDictionary, "o1");
        TermId o2 = OwlRlBatteryHelpers.Mint(sameDictionary, "o2");
        TermId one = OwlRlBatteryHelpers.Literal(sameDictionary, "1", XsdInteger);

        EncodedTriple functional = OwlRlBatteryHelpers.Triple(q, sameTerms.Type, sameTerms.FunctionalProperty);
        EncodedTriple sameForward = OwlRlBatteryHelpers.Triple(o1, sameTerms.SameAs, o2);
        EncodedTriple sameBackward = OwlRlBatteryHelpers.Triple(o2, sameTerms.SameAs, o1);
        HashSet<EncodedTriple> sameBase =
        [
            functional,
            OwlRlBatteryHelpers.Triple(s, q, o1),
            OwlRlBatteryHelpers.Triple(s, q, o2),
            OwlRlBatteryHelpers.Triple(x, sameTerms.OnProperty, r),
            OwlRlBatteryHelpers.Triple(x, sameTerms.MaxCardinality, one),
            OwlRlBatteryHelpers.Triple(u, sameTerms.Type, x),
            OwlRlBatteryHelpers.Triple(u, r, o1),
            OwlRlBatteryHelpers.Triple(u, r, o2),
        ];

        OwlRlMaintainedClosure sameEngine = new(sameBase, sameTerms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(sameForward, sameEngine.Current.Derived, "Both producers must derive o1≡o2.");
        Assert.Contains(sameBackward, sameEngine.Current.Derived, "eq-sym must derive o2≡o1.");
        AssertCheckpointContract(sameEngine.Current, sameBase, sameTerms, default);

        sameBase.Remove(functional);
        OwlRlResult afterFunctional = sameEngine.Apply([], [functional], TestContext.CancellationToken);
        Assert.Contains(sameForward, afterFunctional.Derived, "o1≡o2 must survive on the cls-maxc2 support after the functional typing is retracted.");
        Assert.Contains(sameBackward, afterFunctional.Derived, "o2≡o1 must survive on the cls-maxc2 support after the functional typing is retracted.");
        AssertCheckpointContract(afterFunctional, sameBase, sameTerms, default);

        //DisjointWith held by cax-adc and an explicit statement.
        TermDictionary disjointDictionary = new();
        OwlRlTerms disjointTerms = new(disjointDictionary);
        TermId node = OwlRlBatteryHelpers.Mint(disjointDictionary, "adc");
        TermId classC = OwlRlBatteryHelpers.Mint(disjointDictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(disjointDictionary, "D");
        TermId cell0 = OwlRlBatteryHelpers.Blank(disjointDictionary, "adc-cell-0");
        TermId cell1 = OwlRlBatteryHelpers.Blank(disjointDictionary, "adc-cell-1");

        EncodedTriple explicitDisjoint = OwlRlBatteryHelpers.Triple(classC, disjointTerms.DisjointWith, classD);
        EncodedTriple symmetricDisjoint = OwlRlBatteryHelpers.Triple(classD, disjointTerms.DisjointWith, classC);
        HashSet<EncodedTriple> disjointBase =
        [
            OwlRlBatteryHelpers.Triple(node, disjointTerms.Type, disjointTerms.AllDisjointClasses),
            OwlRlBatteryHelpers.Triple(node, disjointTerms.Members, cell0),
            OwlRlBatteryHelpers.Triple(cell0, disjointTerms.First, classC),
            OwlRlBatteryHelpers.Triple(cell0, disjointTerms.Rest, cell1),
            OwlRlBatteryHelpers.Triple(cell1, disjointTerms.First, classD),
            OwlRlBatteryHelpers.Triple(cell1, disjointTerms.Rest, disjointTerms.Nil),
            explicitDisjoint,
        ];

        OwlRlMaintainedClosure disjointEngine = new(disjointBase, disjointTerms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(symmetricDisjoint, disjointEngine.Current.Derived, "cax-dw must materialise the symmetric disjointWith(D,C).");
        AssertCheckpointContract(disjointEngine.Current, disjointBase, disjointTerms, default);

        disjointBase.Remove(explicitDisjoint);
        OwlRlResult afterExplicit = disjointEngine.Apply([], [explicitDisjoint], TestContext.CancellationToken);
        Assert.Contains(explicitDisjoint, afterExplicit.Derived, "disjointWith(C,D) must survive via cax-adc after the explicit statement is retracted.");
        Assert.Contains(symmetricDisjoint, afterExplicit.Derived, "disjointWith(D,C) must survive via cax-dw after the explicit statement is retracted.");
        AssertCheckpointContract(afterExplicit, disjointBase, disjointTerms, default);
    }

    /// <summary>
    /// Cascade-deleted structural triple. A <c>someValuesFrom</c> triple that
    /// is DERIVED — not asserted — can be the canonical read of a restriction;
    /// when a retract tears it down by propagation rather than by naming it in
    /// the op, the restriction's conclusions through that read must fall with
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// G is minted before F, so G is the smaller id. Base
    /// <c>{onProperty(x,p), someValuesFrom(x,F), sameAs(F,G), type(v,G),
    /// p(u,v)}</c>. eq-rep-o applies <c>sameAs(F,G)</c> to
    /// <c>someValuesFrom(x,F)</c> and derives <c>someValuesFrom(x,G)</c>; the
    /// canonical filler read is then <c>min(F,G) = G</c>, and cls-svf1 fires
    /// <c>type(u,x)</c> from <c>p(u,v) ∧ type(v,G)</c>.
    /// </para>
    /// <para>
    /// Retract <c>sameAs(F,G)</c> — the op names ONLY the equality, never the
    /// structural triple. From-scratch over <c>{onProperty(x,p),
    /// someValuesFrom(x,F), type(v,G), p(u,v)}</c>: the sole filler read is F,
    /// v does not carry F, so <c>type(u,x)</c> is NOT derivable — it must
    /// vanish even though its supporting <c>someValuesFrom(x,G)</c> died only
    /// as a cascade of the equality's deletion.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void CascadeDeletedStructuralTripleTearsDownItsChoiceConclusions()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId fillerG = OwlRlBatteryHelpers.Mint(dictionary, "G");
        TermId fillerF = OwlRlBatteryHelpers.Mint(dictionary, "F");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        Assert.IsLessThan(fillerF.Encoded, fillerG.Encoded, "G must be minted before F so the derived someValuesFrom(x,G) is the canonical (minimum-id) read.");

        EncodedTriple bridge = OwlRlBatteryHelpers.Triple(fillerF, terms.SameAs, fillerG);
        EncodedTriple membership = OwlRlBatteryHelpers.Triple(u, terms.Type, x);
        HashSet<EncodedTriple> currentBase =
        [
            OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, fillerF),
            bridge,
            OwlRlBatteryHelpers.Triple(v, terms.Type, fillerG),
            OwlRlBatteryHelpers.Triple(u, p, v),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, fillerG), engine.Current.Derived, "eq-rep-o must derive the someValuesFrom(x,G) structural triple from the equality.");
        Assert.Contains(membership, engine.Current.Derived, "cls-svf1 must fire type(u,x) through the canonical derived filler G.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(bridge);
        OwlRlResult afterRetract = engine.Apply([], [bridge], TestContext.CancellationToken);
        Assert.DoesNotContain(membership, afterRetract.Derived, "type(u,x) must vanish when its canonical filler read dies as a cascade of the equality's deletion.");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);
    }

    /// <summary>
    /// Cascade-deleted value read. A <c>hasValue</c> triple that is DERIVED can
    /// be the canonical value read of a restriction; when it dies by
    /// propagation, the value edges cls-hv1 minted through it must fall, while
    /// the edges the surviving asserted read supports are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// W is minted before V, so W is the smaller id. Base
    /// <c>{onProperty(x,p), hasValue(x,V), sameAs(V,W), type(u,x)}</c>.
    /// eq-rep-o derives <c>hasValue(x,W)</c>; the canonical value read is
    /// <c>min(V,W) = W</c>, so cls-hv1 fires <c>p(u,W)</c>, and eq-rep (via the
    /// eq-sym flip <c>sameAs(W,V)</c>) replays it to <c>p(u,V)</c>.
    /// </para>
    /// <para>
    /// Retract <c>sameAs(V,W)</c>. From-scratch over <c>{onProperty(x,p),
    /// hasValue(x,V), type(u,x)}</c>: the sole value read is V, so cls-hv1
    /// derives <c>p(u,V)</c> and nothing derives <c>p(u,W)</c> — the W edge
    /// must vanish with its cascade-deleted read while the V edge stands.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void CascadeDeletedValueReadDropsItsMintedEdgeAndKeepsTheAssertedOne()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId valueW = OwlRlBatteryHelpers.Mint(dictionary, "W");
        TermId valueV = OwlRlBatteryHelpers.Mint(dictionary, "V");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        Assert.IsLessThan(valueV.Encoded, valueW.Encoded, "W must be minted before V so the derived hasValue(x,W) is the canonical (minimum-id) read.");

        EncodedTriple bridge = OwlRlBatteryHelpers.Triple(valueV, terms.SameAs, valueW);
        EncodedTriple edgeW = OwlRlBatteryHelpers.Triple(u, p, valueW);
        EncodedTriple edgeV = OwlRlBatteryHelpers.Triple(u, p, valueV);
        HashSet<EncodedTriple> currentBase =
        [
            OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(x, terms.HasValue, valueV),
            bridge,
            OwlRlBatteryHelpers.Triple(u, terms.Type, x),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(edgeW, engine.Current.Derived, "cls-hv1 must mint the value edge through the canonical derived hasValue(x,W).");
        Assert.Contains(edgeV, engine.Current.Derived, "eq-rep must replay the minted edge onto the asserted value V.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(bridge);
        OwlRlResult afterRetract = engine.Apply([], [bridge], TestContext.CancellationToken);
        Assert.DoesNotContain(edgeW, afterRetract.Derived, "p(u,W) must vanish when its canonical value read dies as a cascade of the equality's deletion.");
        Assert.Contains(edgeV, afterRetract.Derived, "p(u,V) must stand on the surviving asserted hasValue(x,V) read.");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);
    }

    /// <summary>
    /// Directional-producer survival for the sameAs rederive entries. One
    /// equality carries two independent supports — a functional property and an
    /// inverse-functional property — and each stream retracts one support, so
    /// the equality must survive solely through the OTHER family's head-bound
    /// entry. The two entries read the candidate's terms in opposite triple
    /// positions, so a transposed presence probe in either entry fails exactly
    /// this pin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Base <c>{type(q, InverseFunctionalProperty), type(w, FunctionalProperty),
    /// q(s1,v), q(s2,v), w(u,s1), w(u,s2)}</c>. prp-ifp on q (two subjects, one
    /// object) and prp-fp on w (one subject, two objects) each derive
    /// <c>sameAs(s1,s2)</c> and <c>sameAs(s2,s1)</c>; the equality closure adds
    /// the reflexive pairs, and eq-rep replays land only on triples already in
    /// the base.
    /// </para>
    /// <para>
    /// Stream A retracts <c>type(w, FunctionalProperty)</c>: prp-fp loses its
    /// characteristic premise, so the equality survives only through prp-ifp —
    /// the entry that reads <c>Present(candidate.Object, q, sharedObject)</c>,
    /// the transposed direction of the prp-fp read.
    /// </para>
    /// <para>
    /// Stream B retracts the edge <c>q(s2,v)</c>: prp-ifp loses a premise, so
    /// the direct rederivation goes through prp-fp's
    /// <c>Present(sharedSubject, w, candidate.Object)</c>; the retracted edge
    /// itself then reappears as DERIVED via eq-rep over the restored equality,
    /// which the checkpoint contract confirms against the naive oracle.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void FunctionalAndInverseFunctionalAlternateSupportSurvivesRetract()
    {
        //Stream A: retract the functional typing; prp-ifp alone restores.
        TermDictionary aDictionary = new();
        OwlRlTerms aTerms = new(aDictionary);
        TermId aq = OwlRlBatteryHelpers.Mint(aDictionary, "q");
        TermId aw = OwlRlBatteryHelpers.Mint(aDictionary, "w");
        TermId as1 = OwlRlBatteryHelpers.Mint(aDictionary, "s1");
        TermId as2 = OwlRlBatteryHelpers.Mint(aDictionary, "s2");
        TermId av = OwlRlBatteryHelpers.Mint(aDictionary, "v");
        TermId au = OwlRlBatteryHelpers.Mint(aDictionary, "u");

        EncodedTriple aFunctional = OwlRlBatteryHelpers.Triple(aw, aTerms.Type, aTerms.FunctionalProperty);
        EncodedTriple aEquality = OwlRlBatteryHelpers.Triple(as1, aTerms.SameAs, as2);
        EncodedTriple aReverse = OwlRlBatteryHelpers.Triple(as2, aTerms.SameAs, as1);
        HashSet<EncodedTriple> aBase =
        [
            OwlRlBatteryHelpers.Triple(aq, aTerms.Type, aTerms.InverseFunctionalProperty),
            aFunctional,
            OwlRlBatteryHelpers.Triple(as1, aq, av),
            OwlRlBatteryHelpers.Triple(as2, aq, av),
            OwlRlBatteryHelpers.Triple(au, aw, as1),
            OwlRlBatteryHelpers.Triple(au, aw, as2),
        ];

        OwlRlMaintainedClosure aEngine = new(aBase, aTerms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(aEquality, aEngine.Current.Derived, "prp-fp and prp-ifp must both conclude the equality while both supports stand.");
        AssertCheckpointContract(aEngine.Current, aBase, aTerms, default);

        aBase.Remove(aFunctional);
        OwlRlResult afterFunctionalRetract = aEngine.Apply([], [aFunctional], TestContext.CancellationToken);
        Assert.Contains(aEquality, afterFunctionalRetract.Derived, "The equality must survive the functional-typing retract solely through prp-ifp's transposed presence read.");
        Assert.Contains(aReverse, afterFunctionalRetract.Derived, "The reverse equality survives through the same entry or eq-sym over the restored pair.");
        AssertCheckpointContract(afterFunctionalRetract, aBase, aTerms, default);

        //Stream B: retract the inverse-functional edge; prp-fp alone restores.
        TermDictionary bDictionary = new();
        OwlRlTerms bTerms = new(bDictionary);
        TermId bq = OwlRlBatteryHelpers.Mint(bDictionary, "q");
        TermId bw = OwlRlBatteryHelpers.Mint(bDictionary, "w");
        TermId bs1 = OwlRlBatteryHelpers.Mint(bDictionary, "s1");
        TermId bs2 = OwlRlBatteryHelpers.Mint(bDictionary, "s2");
        TermId bv = OwlRlBatteryHelpers.Mint(bDictionary, "v");
        TermId bu = OwlRlBatteryHelpers.Mint(bDictionary, "u");

        EncodedTriple bEdge = OwlRlBatteryHelpers.Triple(bs2, bq, bv);
        EncodedTriple bEquality = OwlRlBatteryHelpers.Triple(bs1, bTerms.SameAs, bs2);
        HashSet<EncodedTriple> bBase =
        [
            OwlRlBatteryHelpers.Triple(bq, bTerms.Type, bTerms.InverseFunctionalProperty),
            OwlRlBatteryHelpers.Triple(bw, bTerms.Type, bTerms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(bs1, bq, bv),
            bEdge,
            OwlRlBatteryHelpers.Triple(bu, bw, bs1),
            OwlRlBatteryHelpers.Triple(bu, bw, bs2),
        ];

        OwlRlMaintainedClosure bEngine = new(bBase, bTerms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(bEquality, bEngine.Current.Derived, "prp-fp and prp-ifp must both conclude the equality while both supports stand.");
        AssertCheckpointContract(bEngine.Current, bBase, bTerms, default);

        bBase.Remove(bEdge);
        OwlRlResult afterEdgeRetract = bEngine.Apply([], [bEdge], TestContext.CancellationToken);
        Assert.Contains(bEquality, afterEdgeRetract.Derived, "The equality must survive the edge retract solely through prp-fp's presence read.");
        Assert.Contains(bEdge, afterEdgeRetract.Derived, "The retracted base edge must reappear as derived via eq-rep over the restored equality.");
        AssertCheckpointContract(afterEdgeRetract, bBase, bTerms, default);
    }

    /// <summary>
    /// Transitive parallel-path survival. A composed transitive edge has two
    /// independent two-hop derivations; retracting one hop must restore the
    /// composition through the survivor. The composed-transitivity entries are
    /// engine-redundant — prp-trp and the seeded transitivity-chain mirror
    /// (prp-spo2) confirm the same composition — so this pin holds if EITHER
    /// entry stands; it pins alternate-path survival, not one entry's probe
    /// direction.
    /// </summary>
    /// <remarks>
    /// Base <c>{type(p, TransitiveProperty), p(a,b1), p(b1,c), p(a,b2),
    /// p(b2,c)}</c> derives only <c>p(a,c)</c> (via b1 and via b2). Retracting
    /// <c>p(a,b1)</c> marks the composition through the deleted-edge walk; the
    /// direct rederivation confirms the surviving two-hop path through
    /// <c>b2</c>, so the composition survives while the retracted hop stays
    /// gone.
    /// </remarks>
    [TestMethod]
    public void TransitiveParallelPathSurvivesEdgeRetract()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b1 = OwlRlBatteryHelpers.Mint(dictionary, "b1");
        TermId b2 = OwlRlBatteryHelpers.Mint(dictionary, "b2");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");

        EncodedTriple hop = OwlRlBatteryHelpers.Triple(a, p, b1);
        EncodedTriple composition = OwlRlBatteryHelpers.Triple(a, p, c);
        HashSet<EncodedTriple> currentBase =
        [
            OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty),
            hop,
            OwlRlBatteryHelpers.Triple(b1, p, c),
            OwlRlBatteryHelpers.Triple(a, p, b2),
            OwlRlBatteryHelpers.Triple(b2, p, c),
        ];

        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(composition, engine.Current.Derived, "prp-trp must compose p(a,c) while both two-hop paths stand.");
        AssertCheckpointContract(engine.Current, currentBase, terms, default);

        currentBase.Remove(hop);
        OwlRlResult afterRetract = engine.Apply([], [hop], TestContext.CancellationToken);
        Assert.Contains(composition, afterRetract.Derived, "The composition must survive through the parallel path via the composed-transitivity entries over the surviving middle.");
        Assert.DoesNotContain(hop, afterRetract.Derived, "The retracted hop has no producer and must stay gone.");
        AssertCheckpointContract(afterRetract, currentBase, terms, default);
    }

    /// <summary>Asserts the checkpoint contract: the maintained derived set equals the from-scratch naive oracle over the current base, both directions on a consistent base, and both report an inconsistency otherwise.</summary>
    /// <param name="maintained">The maintained engine's result.</param>
    /// <param name="currentBase">The base the closure must hold over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle, or <see langword="default"/> to disable the dt-* falsities.</param>
    private void AssertCheckpointContract(
        OwlRlResult maintained,
        IReadOnlyCollection<EncodedTriple> currentBase,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle)
    {
        OwlRlResult naive = OwlRlClosure.ComputeNaive([.. currentBase], terms, oracle, cancellationToken: TestContext.CancellationToken);
        if(maintained.IsConsistent && naive.IsConsistent)
        {
            HashSet<EncodedTriple> maintainedDerived = [.. maintained.Derived];
            HashSet<EncodedTriple> naiveDerived = [.. naive.Derived];
            Assert.IsTrue(
                maintainedDerived.SetEquals(naiveDerived),
                $"Maintained derived {maintainedDerived.Count} triples; naive derived {naiveDerived.Count}. The maintained closure must equal the naive set. {Describe(naiveDerived, maintainedDerived)}");
            Assert.IsTrue(
                naiveDerived.SetEquals(maintainedDerived),
                "The maintained closure must not invent a derivation the naive oracle lacks.");

            return;
        }

        Assert.IsFalse(maintained.IsConsistent, "The naive oracle reported an inconsistency the maintained closure missed.");
        Assert.IsFalse(naive.IsConsistent, "The maintained closure reported an inconsistency the naive oracle missed.");
        Assert.IsNotNull(maintained.InconsistencyRule, "The maintained closure reported no falsity rule for an inconsistent base.");
        Assert.IsNotNull(naive.InconsistencyRule, "The naive oracle reported no falsity rule for an inconsistent base.");
    }

    /// <summary>Asserts the maintained derived set, the axiomatic datatype seeds excluded, equals a hand-derived shape exactly.</summary>
    /// <param name="maintained">The maintained engine's result.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle, or <see langword="default"/> to disable the dt-* falsities.</param>
    /// <param name="expectedShape">The hand-derived derived set beyond the seeds.</param>
    private void AssertDerivedShapeEquals(
        OwlRlResult maintained,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        IReadOnlyCollection<EncodedTriple> expectedShape)
    {
        Assert.IsTrue(maintained.IsConsistent, "A shape assertion applies only to a consistent closure.");
        HashSet<EncodedTriple> seeds = [.. OwlRlClosure.Compute([], terms, oracle, cancellationToken: TestContext.CancellationToken).Derived];
        HashSet<EncodedTriple> shape = [.. maintained.Derived];
        shape.ExceptWith(seeds);
        HashSet<EncodedTriple> expected = [.. expectedShape];
        Assert.IsTrue(
            shape.SetEquals(expected),
            $"The maintained engine derived {shape.Count} shape triples (seeds excluded); the hand-derivation expected {expected.Count}. {Describe(expected, shape)}");
    }

    /// <summary>Describes the symmetric difference between an expected and an actual triple set for an assertion message.</summary>
    /// <param name="expected">The expected set.</param>
    /// <param name="actual">The actual set.</param>
    /// <returns>The missing and extra triple counts.</returns>
    private static string Describe(HashSet<EncodedTriple> expected, HashSet<EncodedTriple> actual)
    {
        HashSet<EncodedTriple> missing = [.. expected];
        missing.ExceptWith(actual);
        HashSet<EncodedTriple> extra = [.. actual];
        extra.ExceptWith(expected);

        return $"missing {missing.Count}, extra {extra.Count}.";
    }
}
