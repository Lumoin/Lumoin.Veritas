using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Differential tests for <see cref="OwlRlCanonicalClosure"/>: the
/// union-find variant expanded back to a full materialization is exactly
/// the rule-based <see cref="OwlRlClosure"/> result, and the falsity
/// verdicts match under collapse — the oracle the production
/// canonicalization is measured against.
/// </summary>
[TestClass]
internal sealed class OwlRlCanonicalClosureTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    /// <summary>The expanded canonical closure equals the rule-based materialization, derived merges included.</summary>
    [TestMethod]
    public void ExpansionMatchesTheRuleBasedClosure()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a1 = Mint(dictionary, "a1");
        TermId a2 = Mint(dictionary, "a2");
        TermId a3 = Mint(dictionary, "a3");
        TermId b = Mint(dictionary, "b");
        TermId c = Mint(dictionary, "c");
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");
        TermId f = Mint(dictionary, "f");
        TermId u = Mint(dictionary, "u");
        TermId v1 = Mint(dictionary, "v1");
        TermId v2 = Mint(dictionary, "v2");
        TermId r = Mint(dictionary, "r");
        TermId w = Mint(dictionary, "w");

        //A three-member input clique, data on both ends of it, and a
        //functional property deriving a fresh merge that carries data.
        List<EncodedTriple> triples =
        [
            Triple(a1, terms.SameAs, a2),
            Triple(a2, terms.SameAs, a3),
            Triple(a1, p, b),
            Triple(c, q, a3),
            Triple(f, terms.Type, terms.FunctionalProperty),
            Triple(u, f, v1),
            Triple(u, f, v2),
            Triple(v1, r, w),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(ruleBased.IsConsistent);
        Assert.IsTrue(canonical.Result.IsConsistent);
        Assert.IsTrue(canonical.Equivalence.AreEquivalent(v1, v2), "The functional-property merge reached the equivalence store.");

        HashSet<EncodedTriple> ruleTotal = [.. triples, .. ruleBased.Derived];
        HashSet<EncodedTriple> expanded = [.. OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms)];

        Assert.IsTrue(ruleTotal.SetEquals(expanded), $"Expanded canonical closure ({expanded.Count}) must equal the rule-based materialization ({ruleTotal.Count}).");
    }

    /// <summary>A transitive property inside a sameAs clique — vocabulary alignment — still expands to exactly the rule-based materialization, the trans-chain structures of every clique member included.</summary>
    [TestMethod]
    public void TransitivePropertyInACliqueMatches()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");
        TermId a = Mint(dictionary, "a");
        TermId b = Mint(dictionary, "b");
        TermId c = Mint(dictionary, "c");

        //Two aligned vocabularies: q is declared the same property as the
        //transitive p, and the data uses both names.
        List<EncodedTriple> triples =
        [
            Triple(p, terms.Type, terms.TransitiveProperty),
            Triple(p, terms.SameAs, q),
            Triple(a, p, b),
            Triple(b, q, c),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(ruleBased.IsConsistent);
        Assert.IsTrue(canonical.Result.IsConsistent);

        HashSet<EncodedTriple> ruleTotal = [.. triples, .. ruleBased.Derived];
        HashSet<EncodedTriple> expanded = [.. OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms)];

        Assert.IsTrue(ruleTotal.SetEquals(expanded), $"Expanded canonical closure ({expanded.Count}) must equal the rule-based materialization ({ruleTotal.Count}) with the per-member trans-chain structures synthesized.");
    }

    /// <summary>The canonical closure materialises no quadratic permutations: its triple count stays below the rule-based one on a clique workload.</summary>
    [TestMethod]
    public void CanonicalFormStaysCompact()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = Mint(dictionary, "p");
        List<EncodedTriple> triples = [];
        for(int i = 0; i < 8; i++)
        {
            TermId member = Mint(dictionary, $"m{i}");
            TermId next = Mint(dictionary, $"m{(i + 1) % 8}");
            if(i < 7)
            {
                triples.Add(Triple(member, terms.SameAs, next));
            }

            triples.Add(Triple(member, p, Mint(dictionary, $"value{i}")));
        }

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        int ruleTotal = triples.Count + ruleBased.Derived.Count;
        int canonicalTotal = canonical.CanonicalBase.Count + canonical.Result.Derived.Count;

        Assert.IsLessThan(ruleTotal, canonicalTotal, "An eight-member clique materialises 64 sameAs permutations and eight copies of every member triple; the canonical form holds one representative row each.");
        Assert.AreEqual(1, canonical.Equivalence.CliqueCount);
        Assert.HasCount(8, canonical.Equivalence.EquivalentTo(Mint(dictionary, "m0")));
    }

    /// <summary>A differentFrom collapsing onto one representative is the eq-diff1 contradiction on both paths.</summary>
    [TestMethod]
    public void CollapsedDifferentFromIsEqDiff1()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = Mint(dictionary, "x");
        TermId y = Mint(dictionary, "y");

        List<EncodedTriple> triples =
        [
            Triple(x, terms.SameAs, y),
            Triple(x, terms.DifferentFrom, y),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.EqDiff1, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.EqDiff1, canonical.Result.InconsistencyRule);
    }

    /// <summary>The collapsed-differentFrom premise is the asserted triple, never the collapsed rewrite — the rewrite exists in no graph.</summary>
    [TestMethod]
    public void CollapsedDifferentFromReportsTheAssertedPremise()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = Mint(dictionary, "x");
        TermId y = Mint(dictionary, "y");
        EncodedTriple asserted = Triple(x, terms.DifferentFrom, y);

        List<EncodedTriple> triples =
        [
            Triple(x, terms.SameAs, y),
            asserted,
        ];

        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.Contains(asserted, canonical.Result.InconsistencyPremises);
    }

    /// <summary>Find answers in canonical space: a protected term is its own representative, and an example clique's members resolve to one typed representative.</summary>
    [TestMethod]
    public void FindAnswersTheProtectedRepresentativeTyped()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId alias = Mint(dictionary, "alias");
        OwlSameAsEquivalence equivalence = new(terms.IdentityReadTerms);

        Assert.IsTrue(equivalence.Union(alias, terms.Nothing));

        CanonicalTermId representative = equivalence.Find(alias);

        Assert.AreEqual(terms.Nothing, representative.Id);
        Assert.AreEqual(representative, equivalence.Find(terms.Nothing));
    }

    /// <summary>A merge derived mid-closure that contradicts a differentFrom matches the rule-based verdict.</summary>
    [TestMethod]
    public void DerivedMergeContradictionMatches()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId f = Mint(dictionary, "f");
        TermId u = Mint(dictionary, "u");
        TermId v1 = Mint(dictionary, "v1");
        TermId v2 = Mint(dictionary, "v2");

        List<EncodedTriple> triples =
        [
            Triple(f, terms.Type, terms.FunctionalProperty),
            Triple(u, f, v1),
            Triple(u, f, v2),
            Triple(v1, terms.DifferentFrom, v2),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.EqDiff1, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.EqDiff1, canonical.Result.InconsistencyRule);
    }

    /// <summary>An owl:AllDifferent list whose members collapse fires eq-diff2 on both paths.</summary>
    [TestMethod]
    public void AllDifferentCollisionMatches()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId node = Mint(dictionary, "n");
        TermId list1 = Mint(dictionary, "l1");
        TermId list2 = Mint(dictionary, "l2");
        TermId m1 = Mint(dictionary, "m1");
        TermId m2 = Mint(dictionary, "m2");

        List<EncodedTriple> triples =
        [
            Triple(node, terms.Type, terms.AllDifferent),
            Triple(node, terms.Members, list1),
            Triple(list1, terms.First, m1),
            Triple(list1, terms.Rest, list2),
            Triple(list2, terms.First, m2),
            Triple(list2, terms.Rest, terms.Nil),
            Triple(m1, terms.SameAs, m2),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.EqDiff2, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.EqDiff2, canonical.Result.InconsistencyRule);
    }

    /// <summary>A merge joining literals the oracle knows distinct is the dt-diff contradiction on both paths — through the clique, not just the asserted pair.</summary>
    [TestMethod]
    public void DistinctLiteralMergeIsDtDiff()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId one = dictionary.GetOrAdd((RdfTerm)new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"))));
        TermId two = dictionary.GetOrAdd((RdfTerm)new Literal(Utf8Strings.From("2"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"))));
        TermId bridge = Mint(dictionary, "bridge");

        //The literals never meet in one asserted pair: each is sameAs the
        //bridge, so only the transitive clique joins them.
        List<EncodedTriple> triples =
        [
            Triple(one, terms.SameAs, bridge),
            Triple(bridge, terms.SameAs, two),
        ];

        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, oracle, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, oracle, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDiff, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.DtDiff, canonical.Result.InconsistencyRule);
    }

    /// <summary>A merge joining two datatypes of disjoint value-space families is the dt-disjoint-identity contradiction on both paths — the merge walk is the canonical engine's only detection point, since canonicalization consumes the sameAs edge before the inner closure runs.</summary>
    [TestMethod]
    public void DisjointDatatypeMergeIsDtDisjointIdentity()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId decimalDatatype = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#decimal")));
        TermId text = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));
        TermId bridge = Mint(dictionary, "datatypeBridge");

        //The datatypes never meet in one asserted pair: each is sameAs the
        //bridge, so only the transitive clique joins them.
        List<EncodedTriple> triples =
        [
            Triple(decimalDatatype, terms.SameAs, bridge),
            Triple(bridge, terms.SameAs, text),
        ];

        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, oracle, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, oracle, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDisjointIdentity, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.DtDisjointIdentity, canonical.Result.InconsistencyRule);
    }

    /// <summary>A list whose tail edge names a sameAs alias of rdf:nil still terminates on both paths: rdf:nil wins the representative choice, so the canonical list parses and the enumeration's member typings match the rule-based materialization.</summary>
    [TestMethod]
    public void NilAliasedListTailMatchesTheRuleBasedClosure()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId n1 = Mint(dictionary, "n1");
        TermId n2 = Mint(dictionary, "n2");
        TermId n3 = Mint(dictionary, "n3");
        TermId c = Mint(dictionary, "c");
        TermId a = Mint(dictionary, "a");
        TermId cell = Mint(dictionary, "cell");

        //The alias clique forms before rdf:nil joins it, so a size-weighted
        //choice would have rewritten rdf:nil away; the protected choice
        //keeps it, and the list's tail edge canonicalizes onto rdf:nil.
        List<EncodedTriple> triples =
        [
            Triple(n1, terms.SameAs, n2),
            Triple(n2, terms.SameAs, n3),
            Triple(n3, terms.SameAs, terms.Nil),
            Triple(cell, terms.First, a),
            Triple(cell, terms.Rest, n3),
            Triple(c, terms.OneOf, cell),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(ruleBased.IsConsistent);
        Assert.IsTrue(canonical.Result.IsConsistent);

        HashSet<EncodedTriple> ruleTotal = [.. triples, .. ruleBased.Derived];
        HashSet<EncodedTriple> expanded = [.. OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms)];

        Assert.Contains(Triple(a, terms.Type, c), expanded);
        Assert.IsTrue(ruleTotal.SetEquals(expanded), $"Expanded canonical closure ({expanded.Count}) must equal the rule-based materialization ({ruleTotal.Count}) with the aliased list tail terminated.");
    }

    /// <summary>An instance typed with a sameAs alias of owl:Nothing clashes on both paths: owl:Nothing wins the representative choice, so the canonical typing reaches cls-nothing2 exactly as the materialized eq-rep copy does.</summary>
    [TestMethod]
    public void NothingAliasedTypingClashesOnBothPaths()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x1 = Mint(dictionary, "x1");
        TermId x2 = Mint(dictionary, "x2");
        TermId x3 = Mint(dictionary, "x3");
        TermId i = Mint(dictionary, "i");

        List<EncodedTriple> triples =
        [
            Triple(x1, terms.SameAs, x2),
            Triple(x2, terms.SameAs, x3),
            Triple(x3, terms.SameAs, terms.Nothing),
            Triple(i, terms.Type, x1),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ClsNothing2, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.ClsNothing2, canonical.Result.InconsistencyRule);
    }

    /// <summary>An enumeration asserted on a sameAs alias of owl:Thing clashes on both paths: owl:Thing wins the representative choice, so the canonical axiom reaches the Thing-enumeration falsity.</summary>
    [TestMethod]
    public void ThingAliasedEnumerationClashesOnBothPaths()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c1 = Mint(dictionary, "c1");
        TermId c2 = Mint(dictionary, "c2");
        TermId c3 = Mint(dictionary, "c3");
        TermId a = Mint(dictionary, "a");
        TermId cell = Mint(dictionary, "cell");

        List<EncodedTriple> triples =
        [
            Triple(c1, terms.SameAs, c2),
            Triple(c2, terms.SameAs, c3),
            Triple(c3, terms.SameAs, terms.Thing),
            Triple(cell, terms.First, a),
            Triple(cell, terms.Rest, terms.Nil),
            Triple(c1, terms.OneOf, cell),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, canonical.Result.InconsistencyRule);
    }

    /// <summary>A predicate clique absorbing rdf:type keeps rdf:type as the representative, so a typing asserted through the alias still drives the typing-keyed rules — the functional characteristic fires and its merge matches the rule-based materialization.</summary>
    [TestMethod]
    public void TypeAliasedPredicateStillDrivesTheTypingRules()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId q1 = Mint(dictionary, "q1");
        TermId q2 = Mint(dictionary, "q2");
        TermId p = Mint(dictionary, "p");
        TermId s = Mint(dictionary, "s");
        TermId o1 = Mint(dictionary, "o1");
        TermId o2 = Mint(dictionary, "o2");

        //The alias clique forms first, then absorbs rdf:type; the typing
        //asserted through q1 must still index the property as functional.
        List<EncodedTriple> triples =
        [
            Triple(q1, terms.SameAs, q2),
            Triple(q2, terms.SameAs, terms.Type),
            Triple(p, q1, terms.FunctionalProperty),
            Triple(s, p, o1),
            Triple(s, p, o2),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(ruleBased.IsConsistent);
        Assert.IsTrue(canonical.Result.IsConsistent);
        Assert.IsTrue(canonical.Equivalence.AreEquivalent(o1, o2), "The functional-property merge must reach the equivalence store through the aliased typing.");

        HashSet<EncodedTriple> ruleTotal = [.. triples, .. ruleBased.Derived];
        HashSet<EncodedTriple> expanded = [.. OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms)];

        Assert.IsTrue(ruleTotal.SetEquals(expanded), $"Expanded canonical closure ({expanded.Count}) must equal the rule-based materialization ({ruleTotal.Count}) with the typing-keyed rules driven through the alias.");
    }

    /// <summary>Two identity-read terms sharing one clique bridge the non-representative one back as an explicit equality, so its fixed-identifier reads survive — an enumeration of owl:Thing merged with rdf:nil clashes on both paths.</summary>
    [TestMethod]
    public void BridgedProtectedTermKeepsItsIdentityReads()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = Mint(dictionary, "a");
        TermId cell = Mint(dictionary, "cell");

        //rdf:nil wins the representative choice between the two protected
        //terms, so the enumeration's subject rewrites away from owl:Thing;
        //the bridge equality restores the read inside the delegate.
        List<EncodedTriple> triples =
        [
            Triple(terms.Thing, terms.SameAs, terms.Nil),
            Triple(cell, terms.First, a),
            Triple(cell, terms.Rest, terms.Nil),
            Triple(terms.Thing, terms.OneOf, cell),
        ];

        OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(ruleBased.IsConsistent);
        Assert.IsFalse(canonical.Result.IsConsistent);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, ruleBased.InconsistencyRule);
        Assert.AreEqual(EntailmentRules.ThingEnumerationClash, canonical.Result.InconsistencyRule);
    }

    /// <summary>Mints an IRI in the example namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>An encoded triple from term identifiers.</summary>
    /// <param name="subject">The subject identifier.</param>
    /// <param name="predicate">The predicate identifier.</param>
    /// <param name="object">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }
}
