using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Pins the RL/RDF-table completion rules of the closure: eq-ref over the
/// three statement positions, the scm-op / scm-dp property
/// self-subsumptions, and the Table 9 restriction comparisons
/// (scm-svf1/svf2, scm-avf1/avf2, scm-hv) — each rule's conclusion, the
/// direction of every comparison including the contravariant scm-avf2, the
/// late-round premise arrival the semi-naive triggers must cover, and the
/// semi-naive/naive agreement over a graph exercising the whole family.
/// The RDF-Based residue completions ride the same discipline: the
/// inverse-characteristic transfer on both orientations, the
/// singleton-enumeration characteristics with their arity guard, the
/// complement symmetry, and the member-subset comparisons scoped to the
/// order-insensitive constructors. The clash-form completions follow: the
/// rdf:nil structure falsity, the negative-property-assertion clash keyed
/// off the helper triples alone, the Thing-enumeration falsity, and the
/// min-cardinality-1 membership with its scope pins.
/// </summary>
[TestClass]
internal sealed class OwlRlCompletionRuleTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>eq-ref equates every term of a statement with itself.</summary>
    [TestMethod]
    public void EqRefEquatesEveryTermOfAStatementWithItself()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");

        HashSet<EncodedTriple> derived = Derive([OwlRlBatteryHelpers.Triple(a, p, b)], terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(a, terms.SameAs, a), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.SameAs, p), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(b, terms.SameAs, b), derived);
    }

    /// <summary>A reflexive owl:differentFrom contradicts: eq-ref supplies the reflexive sameAs and eq-diff1 fires on the pair.</summary>
    [TestMethod]
    public void EqRefTurnsAReflexiveDifferentFromIntoAContradiction()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(x, terms.DifferentFrom, x)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.EqDiff1, result.InconsistencyRule);
    }

    /// <summary>scm-op: a declared object property is its own sub- and equivalent property.</summary>
    [TestMethod]
    public void ScmOpDerivesSelfSubsumptionForDeclaredObjectProperties()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");

        HashSet<EncodedTriple> derived = Derive([OwlRlBatteryHelpers.Triple(p, terms.Type, terms.ObjectPropertyTerm)], terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, p), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.EquivalentProperty, p), derived);
    }

    /// <summary>scm-dp: a declared datatype property is its own sub- and equivalent property.</summary>
    [TestMethod]
    public void ScmDpDerivesSelfSubsumptionForDeclaredDatatypeProperties()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");

        HashSet<EncodedTriple> derived = Derive([OwlRlBatteryHelpers.Triple(p, terms.Type, terms.DatatypePropertyTerm)], terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, p), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.EquivalentProperty, p), derived);
    }

    /// <summary>scm-svf1: some-values restrictions on one property order by their fillers' subsumption, and only in that direction.</summary>
    [TestMethod]
    public void ScmSvf1OrdersSomeValuesRestrictionsByTheirFillers()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId narrow = OwlRlBatteryHelpers.Blank(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Blank(dictionary, "wide");
        TermId smallFiller = OwlRlBatteryHelpers.Mint(dictionary, "smallFiller");
        TermId largeFiller = OwlRlBatteryHelpers.Mint(dictionary, "largeFiller");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(narrow, terms.SomeValuesFrom, smallFiller),
                OwlRlBatteryHelpers.Triple(narrow, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(wide, terms.SomeValuesFrom, largeFiller),
                OwlRlBatteryHelpers.Triple(wide, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(smallFiller, terms.SubClassOf, largeFiller),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(narrow, terms.SubClassOf, wide), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(wide, terms.SubClassOf, narrow), derived);
    }

    /// <summary>scm-svf2: some-values restrictions on one filler order by their properties' subsumption, and only in that direction.</summary>
    [TestMethod]
    public void ScmSvf2OrdersSomeValuesRestrictionsByTheirProperties()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId sub = OwlRlBatteryHelpers.Mint(dictionary, "sub");
        TermId super = OwlRlBatteryHelpers.Mint(dictionary, "super");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "filler");
        TermId narrow = OwlRlBatteryHelpers.Blank(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Blank(dictionary, "wide");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(narrow, terms.SomeValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(narrow, terms.OnProperty, sub),
                OwlRlBatteryHelpers.Triple(wide, terms.SomeValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(wide, terms.OnProperty, super),
                OwlRlBatteryHelpers.Triple(sub, terms.SubPropertyOf, super),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(narrow, terms.SubClassOf, wide), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(wide, terms.SubClassOf, narrow), derived);
    }

    /// <summary>scm-avf1: all-values restrictions on one property order by their fillers' subsumption, and only in that direction.</summary>
    [TestMethod]
    public void ScmAvf1OrdersAllValuesRestrictionsByTheirFillers()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId narrow = OwlRlBatteryHelpers.Blank(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Blank(dictionary, "wide");
        TermId smallFiller = OwlRlBatteryHelpers.Mint(dictionary, "smallFiller");
        TermId largeFiller = OwlRlBatteryHelpers.Mint(dictionary, "largeFiller");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(narrow, terms.AllValuesFrom, smallFiller),
                OwlRlBatteryHelpers.Triple(narrow, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(wide, terms.AllValuesFrom, largeFiller),
                OwlRlBatteryHelpers.Triple(wide, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(smallFiller, terms.SubClassOf, largeFiller),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(narrow, terms.SubClassOf, wide), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(wide, terms.SubClassOf, narrow), derived);
    }

    /// <summary>scm-avf2 is contravariant: with one shared filler, the superproperty's all-values restriction subsumes under the subproperty's — never the other way.</summary>
    [TestMethod]
    public void ScmAvf2ReversesTheAllValuesComparisonAcrossProperties()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId sub = OwlRlBatteryHelpers.Mint(dictionary, "sub");
        TermId super = OwlRlBatteryHelpers.Mint(dictionary, "super");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "filler");
        TermId onSub = OwlRlBatteryHelpers.Blank(dictionary, "onSub");
        TermId onSuper = OwlRlBatteryHelpers.Blank(dictionary, "onSuper");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(onSub, terms.AllValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(onSub, terms.OnProperty, sub),
                OwlRlBatteryHelpers.Triple(onSuper, terms.AllValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(onSuper, terms.OnProperty, super),
                OwlRlBatteryHelpers.Triple(sub, terms.SubPropertyOf, super),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(onSuper, terms.SubClassOf, onSub), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(onSub, terms.SubClassOf, onSuper), derived);
    }

    /// <summary>scm-hv: has-value restrictions on one value order by their properties' subsumption, and only in that direction.</summary>
    [TestMethod]
    public void ScmHvOrdersHasValueRestrictionsByTheirProperties()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId sub = OwlRlBatteryHelpers.Mint(dictionary, "sub");
        TermId super = OwlRlBatteryHelpers.Mint(dictionary, "super");
        TermId value = OwlRlBatteryHelpers.Mint(dictionary, "value");
        TermId narrow = OwlRlBatteryHelpers.Blank(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Blank(dictionary, "wide");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(narrow, terms.HasValue, value),
                OwlRlBatteryHelpers.Triple(narrow, terms.OnProperty, sub),
                OwlRlBatteryHelpers.Triple(wide, terms.HasValue, value),
                OwlRlBatteryHelpers.Triple(wide, terms.OnProperty, super),
                OwlRlBatteryHelpers.Triple(sub, terms.SubPropertyOf, super),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(narrow, terms.SubClassOf, wide), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(wide, terms.SubClassOf, narrow), derived);
    }

    /// <summary>The comparison fires when its subClassOf bridge is itself derived in a later round — the semi-naive trigger covers premises the first round has not seen.</summary>
    [TestMethod]
    public void CompletionRulesFireOnPremisesDerivedInLaterRounds()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId narrow = OwlRlBatteryHelpers.Blank(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Blank(dictionary, "wide");
        TermId smallFiller = OwlRlBatteryHelpers.Mint(dictionary, "smallFiller");
        TermId middleFiller = OwlRlBatteryHelpers.Mint(dictionary, "middleFiller");
        TermId largeFiller = OwlRlBatteryHelpers.Mint(dictionary, "largeFiller");

        //The bridge smallFiller ⊑ largeFiller is absent from the base and
        //arrives only through scm-sco composition.
        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(narrow, terms.SomeValuesFrom, smallFiller),
                OwlRlBatteryHelpers.Triple(narrow, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(wide, terms.SomeValuesFrom, largeFiller),
                OwlRlBatteryHelpers.Triple(wide, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(smallFiller, terms.SubClassOf, middleFiller),
                OwlRlBatteryHelpers.Triple(middleFiller, terms.SubClassOf, largeFiller),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(smallFiller, terms.SubClassOf, largeFiller), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(narrow, terms.SubClassOf, wide), derived);
    }

    /// <summary>The semi-naive and naive evaluations agree — verdict and derived set — over a graph exercising every completion rule of the rung.</summary>
    [TestMethod]
    public void CompletionRulesAgreeBetweenTheSemiNaiveAndNaiveEvaluations()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId edge = OwlRlBatteryHelpers.Mint(dictionary, "edge");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId objectProperty = OwlRlBatteryHelpers.Mint(dictionary, "objectProperty");
        TermId datatypeProperty = OwlRlBatteryHelpers.Mint(dictionary, "datatypeProperty");
        TermId sub = OwlRlBatteryHelpers.Mint(dictionary, "sub");
        TermId super = OwlRlBatteryHelpers.Mint(dictionary, "super");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "filler");
        TermId smallFiller = OwlRlBatteryHelpers.Mint(dictionary, "smallFiller");
        TermId largeFiller = OwlRlBatteryHelpers.Mint(dictionary, "largeFiller");
        TermId someNarrow = OwlRlBatteryHelpers.Blank(dictionary, "someNarrow");
        TermId someWide = OwlRlBatteryHelpers.Blank(dictionary, "someWide");
        TermId allOnSub = OwlRlBatteryHelpers.Blank(dictionary, "allOnSub");
        TermId allOnSuper = OwlRlBatteryHelpers.Blank(dictionary, "allOnSuper");
        TermId hasOnSub = OwlRlBatteryHelpers.Blank(dictionary, "hasOnSub");
        TermId hasOnSuper = OwlRlBatteryHelpers.Blank(dictionary, "hasOnSuper");

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(a, edge, b),
            OwlRlBatteryHelpers.Triple(objectProperty, terms.Type, terms.ObjectPropertyTerm),
            OwlRlBatteryHelpers.Triple(datatypeProperty, terms.Type, terms.DatatypePropertyTerm),
            OwlRlBatteryHelpers.Triple(someNarrow, terms.SomeValuesFrom, smallFiller),
            OwlRlBatteryHelpers.Triple(someNarrow, terms.OnProperty, edge),
            OwlRlBatteryHelpers.Triple(someWide, terms.SomeValuesFrom, largeFiller),
            OwlRlBatteryHelpers.Triple(someWide, terms.OnProperty, edge),
            OwlRlBatteryHelpers.Triple(smallFiller, terms.SubClassOf, largeFiller),
            OwlRlBatteryHelpers.Triple(allOnSub, terms.AllValuesFrom, filler),
            OwlRlBatteryHelpers.Triple(allOnSub, terms.OnProperty, sub),
            OwlRlBatteryHelpers.Triple(allOnSuper, terms.AllValuesFrom, filler),
            OwlRlBatteryHelpers.Triple(allOnSuper, terms.OnProperty, super),
            OwlRlBatteryHelpers.Triple(hasOnSub, terms.HasValue, b),
            OwlRlBatteryHelpers.Triple(hasOnSub, terms.OnProperty, sub),
            OwlRlBatteryHelpers.Triple(hasOnSuper, terms.HasValue, b),
            OwlRlBatteryHelpers.Triple(hasOnSuper, terms.OnProperty, super),
            OwlRlBatteryHelpers.Triple(sub, terms.SubPropertyOf, super),
        ];

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
        Assert.IsTrue(semiNaiveDerived.SetEquals(naive.Derived));
    }

    /// <summary>A functional property's inverse is inverse functional — and only that: the transfer exchanges the kinds.</summary>
    [TestMethod]
    public void InverseCharacteristicTransferMakesAFunctionalPropertysInverseInverseFunctional()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.InverseOf, q),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(q, terms.Type, terms.FunctionalProperty), derived);
    }

    /// <summary>An inverse-functional property's inverse is functional — and only that: the transfer exchanges the kinds.</summary>
    [TestMethod]
    public void InverseCharacteristicTransferMakesAnInverseFunctionalPropertysInverseFunctional()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.InverseFunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.InverseOf, q),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(q, terms.Type, terms.FunctionalProperty), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty), derived);
    }

    /// <summary>The transfer fires on either orientation of the inverse statement: the characteristic may sit on the statement's object end.</summary>
    [TestMethod]
    public void InverseCharacteristicTransferFiresOnEitherOrientationOfTheInverseStatement()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
                OwlRlBatteryHelpers.Triple(q, terms.InverseOf, p),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty), derived);
    }

    /// <summary>A property whose range is a singleton enumeration is functional; the member is a blank node, whose identity the rule never reads.</summary>
    [TestMethod]
    public void SingletonEnumerationRangeMakesThePropertyFunctional()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId singleton = OwlRlBatteryHelpers.Mint(dictionary, "singleton");
        TermId member = OwlRlBatteryHelpers.Blank(dictionary, "member");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(p, terms.Range, singleton)];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [member], "single");
        triples.Add(OwlRlBatteryHelpers.Triple(singleton, terms.OneOf, head));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(p, terms.Type, terms.InverseFunctionalProperty), derived);
    }

    /// <summary>A property whose domain is a singleton enumeration is inverse functional.</summary>
    [TestMethod]
    public void SingletonEnumerationDomainMakesThePropertyInverseFunctional()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId singleton = OwlRlBatteryHelpers.Mint(dictionary, "singleton");
        TermId member = OwlRlBatteryHelpers.Blank(dictionary, "member");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(p, terms.Domain, singleton)];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [member], "single");
        triples.Add(OwlRlBatteryHelpers.Triple(singleton, terms.OneOf, head));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(p, terms.Type, terms.InverseFunctionalProperty), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty), derived);
    }

    /// <summary>The singleton-enumeration characteristic is arity-guarded: a two-member enumeration range concludes nothing.</summary>
    [TestMethod]
    public void SingletonEnumerationCharacteristicIgnoresLargerEnumerations()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId pair = OwlRlBatteryHelpers.Mint(dictionary, "pair");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(p, terms.Range, pair)];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [a, b], "pair");
        triples.Add(OwlRlBatteryHelpers.Triple(pair, terms.OneOf, head));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty), derived);
    }

    /// <summary>owl:complementOf materialises symmetrically.</summary>
    [TestMethod]
    public void ComplementOfMaterialisesSymmetrically()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c1 = OwlRlBatteryHelpers.Mint(dictionary, "c1");
        TermId c2 = OwlRlBatteryHelpers.Mint(dictionary, "c2");

        HashSet<EncodedTriple> derived = Derive([OwlRlBatteryHelpers.Triple(c1, terms.ComplementOf, c2)], terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(c2, terms.ComplementOf, c1), derived);
    }

    /// <summary>Permuted enumerations over one member set subsume both ways, close into the equivalence through scm-eqc2, and carry the instance across.</summary>
    [TestMethod]
    public void OneOfMemberSubsetDerivesEquivalenceForPermutedEnumerations()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId first = OwlRlBatteryHelpers.Mint(dictionary, "first");
        TermId second = OwlRlBatteryHelpers.Mint(dictionary, "second");
        TermId small = OwlRlBatteryHelpers.Mint(dictionary, "small");
        TermId medium = OwlRlBatteryHelpers.Mint(dictionary, "medium");
        TermId large = OwlRlBatteryHelpers.Mint(dictionary, "large");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "instance");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(instance, terms.Type, first)];
        TermId firstHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [small, medium, large], "ordered");
        triples.Add(OwlRlBatteryHelpers.Triple(first, terms.OneOf, firstHead));
        TermId secondHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [large, medium, small], "permuted");
        triples.Add(OwlRlBatteryHelpers.Triple(second, terms.OneOf, secondHead));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(first, terms.SubClassOf, second), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(second, terms.SubClassOf, first), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(first, terms.EquivalentClass, second), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, terms.Type, second), derived);
    }

    /// <summary>A union whose disjunct set is contained in another's subsumes under it, in that direction only, and carries the instance across.</summary>
    [TestMethod]
    public void UnionOfMemberSubsetDerivesSubsumptionForContainedDisjunctSets()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId narrow = OwlRlBatteryHelpers.Mint(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Mint(dictionary, "wide");
        TermId human = OwlRlBatteryHelpers.Mint(dictionary, "human");
        TermId animal = OwlRlBatteryHelpers.Mint(dictionary, "animal");
        TermId stone = OwlRlBatteryHelpers.Mint(dictionary, "stone");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "instance");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(instance, terms.Type, narrow)];
        TermId narrowHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [human, animal], "narrow");
        triples.Add(OwlRlBatteryHelpers.Triple(narrow, terms.UnionOf, narrowHead));
        TermId wideHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [animal, human, stone], "wide");
        triples.Add(OwlRlBatteryHelpers.Triple(wide, terms.UnionOf, wideHead));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(narrow, terms.SubClassOf, wide), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(wide, terms.SubClassOf, narrow), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, terms.Type, wide), derived);
    }

    /// <summary>The member-subset comparison is scoped to the order-insensitive constructors: permuted property-chain lists compare nowhere.</summary>
    [TestMethod]
    public void MemberSubsetComparisonNeverReadsPropertyChainLists()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId firstChain = OwlRlBatteryHelpers.Mint(dictionary, "firstChain");
        TermId secondChain = OwlRlBatteryHelpers.Mint(dictionary, "secondChain");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");

        List<EncodedTriple> triples = [];
        OwlRlBatteryHelpers.AddChainAxiom(triples, dictionary, terms, firstChain, [p, q], "forward");
        OwlRlBatteryHelpers.AddChainAxiom(triples, dictionary, terms, secondChain, [q, p], "backward");

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(firstChain, terms.SubPropertyOf, secondChain), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(secondChain, terms.SubPropertyOf, firstChain), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(firstChain, terms.SubClassOf, secondChain), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(secondChain, terms.SubClassOf, firstChain), derived);
    }

    /// <summary>An empty enumeration denotes the empty class, so it subsumes under every other enumeration — and nothing subsumes under it but another empty one.</summary>
    [TestMethod]
    public void EmptyEnumerationSubsumesUnderEveryEnumeration()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId empty = OwlRlBatteryHelpers.Mint(dictionary, "empty");
        TermId occupied = OwlRlBatteryHelpers.Mint(dictionary, "occupied");
        TermId member = OwlRlBatteryHelpers.Mint(dictionary, "member");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(empty, terms.OneOf, terms.Nil)];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [member], "occupied");
        triples.Add(OwlRlBatteryHelpers.Triple(occupied, terms.OneOf, head));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(empty, terms.SubClassOf, occupied), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(occupied, terms.SubClassOf, empty), derived);
    }

    /// <summary>The transfer fires on characteristic typings the closure itself derives in later rounds: a transfer conclusion feeds the next transfer across a second inverse statement.</summary>
    [TestMethod]
    public void InverseCharacteristicTransferFiresOnTypingsDerivedInLaterRounds()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId r = OwlRlBatteryHelpers.Mint(dictionary, "r");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
                OwlRlBatteryHelpers.Triple(p, terms.InverseOf, q),
                OwlRlBatteryHelpers.Triple(q, terms.InverseOf, r),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(q, terms.Type, terms.InverseFunctionalProperty), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(r, terms.Type, terms.FunctionalProperty), derived);
    }

    /// <summary>The semi-naive and naive evaluations agree — verdict and derived set — over a graph exercising every residue completion of the rung.</summary>
    [TestMethod]
    public void ResidueCompletionRulesAgreeBetweenTheSemiNaiveAndNaiveEvaluations()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId ranged = OwlRlBatteryHelpers.Mint(dictionary, "ranged");
        TermId singleton = OwlRlBatteryHelpers.Mint(dictionary, "singleton");
        TermId member = OwlRlBatteryHelpers.Blank(dictionary, "member");
        TermId c1 = OwlRlBatteryHelpers.Mint(dictionary, "c1");
        TermId c2 = OwlRlBatteryHelpers.Mint(dictionary, "c2");
        TermId first = OwlRlBatteryHelpers.Mint(dictionary, "first");
        TermId second = OwlRlBatteryHelpers.Mint(dictionary, "second");
        TermId small = OwlRlBatteryHelpers.Mint(dictionary, "small");
        TermId large = OwlRlBatteryHelpers.Mint(dictionary, "large");
        TermId narrow = OwlRlBatteryHelpers.Mint(dictionary, "narrow");
        TermId wide = OwlRlBatteryHelpers.Mint(dictionary, "wide");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "instance");

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(p, terms.InverseOf, q),
            OwlRlBatteryHelpers.Triple(ranged, terms.Range, singleton),
            OwlRlBatteryHelpers.Triple(c1, terms.ComplementOf, c2),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, first),
        ];
        TermId singletonHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [member], "single");
        triples.Add(OwlRlBatteryHelpers.Triple(singleton, terms.OneOf, singletonHead));
        TermId firstHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [small, large], "ordered");
        triples.Add(OwlRlBatteryHelpers.Triple(first, terms.OneOf, firstHead));
        TermId secondHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [large, small], "permuted");
        triples.Add(OwlRlBatteryHelpers.Triple(second, terms.OneOf, secondHead));
        TermId narrowHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [small], "narrow");
        triples.Add(OwlRlBatteryHelpers.Triple(narrow, terms.UnionOf, narrowHead));
        TermId wideHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [small, large], "wide");
        triples.Add(OwlRlBatteryHelpers.Triple(wide, terms.UnionOf, wideHead));

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
        Assert.IsTrue(semiNaiveDerived.SetEquals(naive.Derived));
    }

    /// <summary>An rdf:first edge on rdf:nil contradicts — the empty collection carries no cells.</summary>
    [TestMethod]
    public void NilFirstEdgeContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(terms.Nil, terms.First, a)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.NilStructureClash, result.InconsistencyRule);
    }

    /// <summary>An rdf:rest edge on rdf:nil contradicts — the empty collection carries no cells.</summary>
    [TestMethod]
    public void NilRestEdgeContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(terms.Nil, terms.Rest, a)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.NilStructureClash, result.InconsistencyRule);
    }

    /// <summary>A well-formed list terminated by rdf:nil in object position stays consistent — the falsity is fixed to the subject position.</summary>
    [TestMethod]
    public void NilAsListTailStaysConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");

        List<EncodedTriple> triples = [];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [a, b], "tail");
        triples.Add(OwlRlBatteryHelpers.Triple(c, terms.OneOf, head));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(a, terms.Type, c), derived);
    }

    /// <summary>A subject equated with rdf:nil carries its edges onto rdf:nil through eq-rep, so its rdf:first edge contradicts.</summary>
    [TestMethod]
    public void NilClashFiresOnASameAsAliasedSubject()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId m = OwlRlBatteryHelpers.Mint(dictionary, "m");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(x, terms.SameAs, terms.Nil),
                OwlRlBatteryHelpers.Triple(x, terms.First, m),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.NilStructureClash, result.InconsistencyRule);
    }

    /// <summary>Two rdf:rest edges off one cell equate rdf:nil with the other tail through the functional-rest seed, and the tail's rdf:first edge then contradicts on rdf:nil.</summary>
    [TestMethod]
    public void NilClashFiresThroughTheFunctionalRestCollapse()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId cell = OwlRlBatteryHelpers.Blank(dictionary, "cell");
        TermId z = OwlRlBatteryHelpers.Blank(dictionary, "z");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(cell, terms.Rest, terms.Nil),
                OwlRlBatteryHelpers.Triple(cell, terms.Rest, z),
                OwlRlBatteryHelpers.Triple(z, terms.First, v),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.NilStructureClash, result.InconsistencyRule);
    }

    /// <summary>The negative-property-assertion clash fires off the helper triples alone — no typing antecedent — and reports exactly the matched triples as its premises.</summary>
    [TestMethod]
    public void NpaClashFiresWithoutTheTypingTriple()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s),
                OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p),
                OwlRlBatteryHelpers.Triple(z, terms.TargetIndividual, o),
                OwlRlBatteryHelpers.Triple(s, p, o),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.PrpNpa, result.InconsistencyRule);

        //The reported premises are exactly the matched triples — never a
        //typing triple the graph does not hold.
        HashSet<EncodedTriple> premises = [.. result.InconsistencyPremises];
        HashSet<EncodedTriple> expected =
        [
            OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s),
            OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p),
            OwlRlBatteryHelpers.Triple(z, terms.TargetIndividual, o),
            OwlRlBatteryHelpers.Triple(s, p, o),
        ];
        Assert.IsTrue(premises.SetEquals(expected));
    }

    /// <summary>The negative-assertion clash covers the target-value form: a matching literal-valued edge contradicts without any typing triple.</summary>
    [TestMethod]
    public void NpaTargetValueClashFiresWithoutTheTypingTriple()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId value = OwlRlBatteryHelpers.Literal(dictionary, "data", "http://www.w3.org/2001/XMLSchema#string");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s),
                OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p),
                OwlRlBatteryHelpers.Triple(z, terms.TargetValue, value),
                OwlRlBatteryHelpers.Triple(s, p, value),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.PrpNpa, result.InconsistencyRule);
    }

    /// <summary>The helper triples alone state a negative assertion nothing contradicts — without the matching positive edge the closure stays consistent.</summary>
    [TestMethod]
    public void NpaHelperTriplesAloneStayConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s),
                OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p),
                OwlRlBatteryHelpers.Triple(z, terms.TargetIndividual, o),
            ],
            terms,
            dictionary);

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(s, p, o), derived);
    }

    /// <summary>A node stating several sources and several assertion properties clashes on the one combination whose positive edge is asserted — the check joins every helper combination.</summary>
    [TestMethod]
    public void NpaMultiValuedHelpersClashOnTheMatchingCombination()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId s1 = OwlRlBatteryHelpers.Mint(dictionary, "s1");
        TermId s2 = OwlRlBatteryHelpers.Mint(dictionary, "s2");
        TermId p1 = OwlRlBatteryHelpers.Mint(dictionary, "p1");
        TermId p2 = OwlRlBatteryHelpers.Mint(dictionary, "p2");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s1),
                OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s2),
                OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p1),
                OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p2),
                OwlRlBatteryHelpers.Triple(z, terms.TargetIndividual, o),
                OwlRlBatteryHelpers.Triple(s2, p2, o),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.PrpNpa, result.InconsistencyRule);
    }

    /// <summary>An enumeration of owl:Thing contradicts at any arity — the finite sequence cannot exhaust the infinite RDF-Based universe.</summary>
    [TestMethod]
    public void ThingEnumerationClashesAtAnyArity()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");

        List<EncodedTriple> triples = [];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [a, b], "thing");
        triples.Add(OwlRlBatteryHelpers.Triple(terms.Thing, terms.OneOf, head));

        OwlRlResult result = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, result.InconsistencyRule);
    }

    /// <summary>An empty enumeration of owl:Thing contradicts — the axiom triple alone carries the falsity.</summary>
    [TestMethod]
    public void ThingEnumerationClashesOnTheEmptyList()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(terms.Thing, terms.OneOf, terms.Nil)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, result.InconsistencyRule);
    }

    /// <summary>A cyclic list under the Thing enumeration still contradicts — the falsity never reads the list.</summary>
    [TestMethod]
    public void ThingEnumerationClashesOnACyclicList()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId cell = OwlRlBatteryHelpers.Blank(dictionary, "cycle");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(terms.Thing, terms.OneOf, cell),
                OwlRlBatteryHelpers.Triple(cell, terms.First, a),
                OwlRlBatteryHelpers.Triple(cell, terms.Rest, cell),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, result.InconsistencyRule);
    }

    /// <summary>A class equated with owl:Thing carries its enumeration onto owl:Thing through eq-rep, and the falsity fires on the rewritten axiom.</summary>
    [TestMethod]
    public void ThingEnumerationClashFiresOnASameAsAliasedSubject()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");

        List<EncodedTriple> triples = [OwlRlBatteryHelpers.Triple(c, terms.SameAs, terms.Thing)];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [a], "aliased");
        triples.Add(OwlRlBatteryHelpers.Triple(c, terms.OneOf, head));

        OwlRlResult result = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, result.InconsistencyRule);
    }

    /// <summary>An ordinary enumeration stays consistent even with owl:Thing among its members — the falsity reads the subject position only.</summary>
    [TestMethod]
    public void OrdinaryEnumerationsWithThingAsMemberStayConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");

        List<EncodedTriple> triples = [];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [terms.Thing, a], "member");
        triples.Add(OwlRlBatteryHelpers.Triple(c, terms.OneOf, head));

        HashSet<EncodedTriple> derived = Derive(triples, terms, dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Thing, terms.Type, c), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(a, terms.Type, c), derived);
    }

    /// <summary>An asserted owl:sameAs between two datatypes of disjoint value-space families contradicts — the datatype map denotes them as distinct resources in every interpretation.</summary>
    [TestMethod]
    public void SameAsBetweenDisjointDatatypesContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#integer");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#string");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(integer, terms.SameAs, text)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDisjointIdentity, result.InconsistencyRule);
    }

    /// <summary>A cross-family datatype identity derived through a shared alias contradicts — the equality rules compose the two asserted edges into the direct identity the falsity reads.</summary>
    [TestMethod]
    public void ADerivedSameAsThroughASharedAliasContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId decimalDatatype = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#decimal");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#string");
        TermId alias = OwlRlBatteryHelpers.Mint(dictionary, "sharedAlias");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(decimalDatatype, terms.SameAs, alias),
                OwlRlBatteryHelpers.Triple(text, terms.SameAs, alias)
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDisjointIdentity, result.InconsistencyRule);
    }

    /// <summary>A same-family datatype identity stays consistent — within-family refinement is the interval map's business and the oracle answers unknown.</summary>
    [TestMethod]
    public void ASameFamilyDatatypeIdentityStaysConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#integer");
        TermId decimalDatatype = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#decimal");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(integer, terms.SameAs, decimalDatatype)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
    }

    /// <summary>An identity between a recognized datatype and an unrecognized alias IRI stays consistent — the alias's family is unknown and the falsity stays silent, keeping the alias-retype habitat intact.</summary>
    [TestMethod]
    public void ARecognizedToAliasIdentityStaysConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId decimalDatatype = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#decimal");
        TermId alias = OwlRlBatteryHelpers.Mint(dictionary, "aliasOnly");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(decimalDatatype, terms.SameAs, alias)],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
    }

    /// <summary>Without a datatype oracle the cross-family identity stays consistent — the falsity is oracle-carried knowledge, never a vocabulary read.</summary>
    [TestMethod]
    public void TheNoneOracleLeavesTheDisjointIdentityConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#integer");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#string");

        OwlRlResult result = OwlRlClosure.Compute(
            [OwlRlBatteryHelpers.Triple(integer, terms.SameAs, text)],
            terms,
            OwlRlDatatypeOracle.None,
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
    }

    /// <summary>The disjoint-identity clash agrees between the semi-naive and naive evaluations on the derived-alias shape.</summary>
    [TestMethod]
    public void TheDisjointIdentityClashAgreesBetweenTheSemiNaiveAndNaiveEvaluations()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId decimalDatatype = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#decimal");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, "http://www.w3.org/2001/XMLSchema#string");
        TermId alias = OwlRlBatteryHelpers.Mint(dictionary, "agreementAlias");

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(decimalDatatype, terms.SameAs, alias),
            OwlRlBatteryHelpers.Triple(text, terms.SameAs, alias)
        ];

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(semiNaive.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDisjointIdentity, semiNaive.InconsistencyRule);
        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        Assert.AreEqual(naive.InconsistencyRule, semiNaive.InconsistencyRule);
    }

    /// <summary>One asserted value places the subject in a min-cardinality-1 restriction on the property.</summary>
    [TestMethod]
    public void MinCardinalityOneMembershipDerivesTheRestrictionTyping()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "restriction");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#int");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one),
                OwlRlBatteryHelpers.Triple(u, p, v),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>The membership needs the whole rule body: without the witnessing edge, or without the bound, nothing is concluded.</summary>
    [TestMethod]
    public void MinCardinalityOneMembershipRequiresTheFullRuleBody()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "edgeless");
        TermId y = OwlRlBatteryHelpers.Blank(dictionary, "boundless");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId w = OwlRlBatteryHelpers.Mint(dictionary, "w");
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one),
                OwlRlBatteryHelpers.Triple(y, terms.OnProperty, q),
                OwlRlBatteryHelpers.Triple(w, q, z),
            ],
            terms,
            dictionary);

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(w, terms.Type, y), derived);
    }

    /// <summary>A bound of two concludes nothing — two asserted values need not be distinct individuals under the open world.</summary>
    [TestMethod]
    public void MinCardinalityTwoDerivesNoMembership()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "two");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v1 = OwlRlBatteryHelpers.Mint(dictionary, "v1");
        TermId v2 = OwlRlBatteryHelpers.Mint(dictionary, "v2");
        TermId two = OwlRlBatteryHelpers.Literal(dictionary, "2", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, two),
                OwlRlBatteryHelpers.Triple(u, p, v1),
                OwlRlBatteryHelpers.Triple(u, p, v2),
            ],
            terms,
            dictionary);

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>A bound of zero concludes nothing — the universally true membership stays out of the materialisation.</summary>
    [TestMethod]
    public void MinCardinalityZeroDerivesNoMembership()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "zero");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId zero = OwlRlBatteryHelpers.Literal(dictionary, "0", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, zero),
                OwlRlBatteryHelpers.Triple(u, p, v),
            ],
            terms,
            dictionary);

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>A restriction node carrying a further detail still concludes the min-1 membership — each detail condition determines the extension independently.</summary>
    [TestMethod]
    public void MinCardinalityOneMembershipSurvivesMultiDetailRestrictionNodes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "multi");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "filler");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(u, p, v),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>A restriction carrying two min-cardinality bounds fires the membership completion per asserted one-bound: each bound triple is an independent constraint of the graph, so the one-bound concludes membership regardless of which bound holds the smaller identifier.</summary>
    [TestMethod]
    public void MultiBoundMinCardinalityFiresPerAssertedBound()
    {
        TermDictionary dictionary = new();

        //Minted ahead of the vocabulary, the foreign bound takes the
        //smaller term identifier; the membership still derives through
        //the asserted one-bound.
        TermId two = OwlRlBatteryHelpers.Literal(dictionary, "2", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "multibound");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, two),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one),
                OwlRlBatteryHelpers.Triple(u, p, v),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>The membership fires when the witnessing edge arrives in a later round through the subproperty rule — the semi-naive trigger covers derived edges.</summary>
    [TestMethod]
    public void MinCardinalityOneMembershipFiresOnALateArrivingEdge()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "late");
        TermId sub = OwlRlBatteryHelpers.Mint(dictionary, "sub");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one),
                OwlRlBatteryHelpers.Triple(sub, terms.SubPropertyOf, p),
                OwlRlBatteryHelpers.Triple(u, sub, v),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>The membership fires when the one-bound arrives in a later round through equality substitution on the bound literal.</summary>
    [TestMethod]
    public void MinCardinalityOneMembershipFiresOnALateArrivingBound()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "latebound");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId padded = OwlRlBatteryHelpers.Literal(dictionary, "01", "http://www.w3.org/2001/XMLSchema#integer");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#integer");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, padded),
                OwlRlBatteryHelpers.Triple(padded, terms.SameAs, one),
                OwlRlBatteryHelpers.Triple(u, p, v),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>A self-disjoint min-cardinality-1 restriction with one witnessed edge contradicts through the membership and the class-disjointness falsity, in both evaluations.</summary>
    [TestMethod]
    public void SelfDisjointMinCardinalityRestrictionContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId n = OwlRlBatteryHelpers.Blank(dictionary, "selfdisjoint");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#int");

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(n, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(n, terms.MinCardinality, one),
            OwlRlBatteryHelpers.Triple(n, terms.DisjointWith, n),
            OwlRlBatteryHelpers.Triple(u, p, v),
        ];

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(semiNaive.IsConsistent);
        Assert.AreEqual(EntailmentRules.CaxDw, semiNaive.InconsistencyRule);
        Assert.IsFalse(naive.IsConsistent);
        Assert.AreEqual(EntailmentRules.CaxDw, naive.InconsistencyRule);
    }

    /// <summary>The semi-naive and naive evaluations agree — verdict and derived set — over a consistent graph exercising every clash-form completion's consistent boundary.</summary>
    [TestMethod]
    public void ClashFormRulesAgreeBetweenTheSemiNaiveAndNaiveEvaluations()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "agree");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(x, terms.MinCardinality, one),
            OwlRlBatteryHelpers.Triple(u, p, v),
            OwlRlBatteryHelpers.Triple(z, terms.SourceIndividual, s),
            OwlRlBatteryHelpers.Triple(z, terms.AssertionProperty, p),
            OwlRlBatteryHelpers.Triple(z, terms.TargetIndividual, o),
        ];
        TermId head = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [terms.Thing, a], "agreement");
        triples.Add(OwlRlBatteryHelpers.Triple(c, terms.OneOf, head));

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(semiNaive.IsConsistent);
        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
        Assert.IsTrue(semiNaiveDerived.SetEquals(naive.Derived));
    }

    /// <summary>A restriction carrying two someValuesFrom fillers fires the existential rule per asserted filler: the membership arrives through the filler whose identifier is not the minimum, because each filler triple is an independent fact of the graph.</summary>
    [TestMethod]
    public void DuplicateSomeValuesFillersEachFireTheMembership()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "dupsvf");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId earlyFiller = OwlRlBatteryHelpers.Mint(dictionary, "earlyFiller");
        TermId lateFiller = OwlRlBatteryHelpers.Mint(dictionary, "lateFiller");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, earlyFiller),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, lateFiller),
                OwlRlBatteryHelpers.Triple(u, p, v),
                OwlRlBatteryHelpers.Triple(v, terms.Type, lateFiller),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>The falsity twin of the duplicate-filler membership: a clash reachable only through the non-minimum filler fires — the restriction types the subject into a class disjoint with its asserted class, so reading one canonical filler would answer a false consistent.</summary>
    [TestMethod]
    public void DuplicateSomeValuesFillersReachTheDisjointnessClash()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "dupsvfclash");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId earlyFiller = OwlRlBatteryHelpers.Mint(dictionary, "earlyFiller");
        TermId lateFiller = OwlRlBatteryHelpers.Mint(dictionary, "lateFiller");
        TermId d = OwlRlBatteryHelpers.Mint(dictionary, "d");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, earlyFiller),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, lateFiller),
                OwlRlBatteryHelpers.Triple(x, terms.DisjointWith, d),
                OwlRlBatteryHelpers.Triple(u, p, v),
                OwlRlBatteryHelpers.Triple(v, terms.Type, lateFiller),
                OwlRlBatteryHelpers.Triple(u, terms.Type, d),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.IsNotNull(result.InconsistencyRule);
    }

    /// <summary>A restriction carrying two allValuesFrom fillers confines the reached objects under each asserted filler — both typings derive, because each filler triple is an independent universal constraint.</summary>
    [TestMethod]
    public void DuplicateAllValuesFillersEachConfineTheEdgeObjects()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "dupavf");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId earlyFiller = OwlRlBatteryHelpers.Mint(dictionary, "earlyFiller");
        TermId lateFiller = OwlRlBatteryHelpers.Mint(dictionary, "lateFiller");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.AllValuesFrom, earlyFiller),
                OwlRlBatteryHelpers.Triple(x, terms.AllValuesFrom, lateFiller),
                OwlRlBatteryHelpers.Triple(u, terms.Type, x),
                OwlRlBatteryHelpers.Triple(u, p, v),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(v, terms.Type, earlyFiller), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(v, terms.Type, lateFiller), derived);
    }

    /// <summary>The identical-repeat control: the same filler triple seeded twice is one fact of the graph — the membership derives, the closure stays consistent, and set semantics absorbs the repeat.</summary>
    [TestMethod]
    public void RepeatedIdenticalFillerTriplesDeriveTheMembershipCleanly()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "repeatsvf");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "filler");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");

        HashSet<EncodedTriple> derived = Derive(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, filler),
                OwlRlBatteryHelpers.Triple(u, p, v),
                OwlRlBatteryHelpers.Triple(v, terms.Type, filler),
            ],
            terms,
            dictionary);

        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), derived);
    }

    /// <summary>Computes the closure and answers its derived set; the fixture must be consistent.</summary>
    /// <param name="triples">The base triples.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="dictionary">The dictionary the triples were encoded with.</param>
    /// <returns>The derived triples.</returns>
    private HashSet<EncodedTriple> Derive(List<EncodedTriple> triples, OwlRlTerms terms, TermDictionary dictionary)
    {
        OwlRlResult result = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(result.IsConsistent);

        return [.. result.Derived];
    }
}
