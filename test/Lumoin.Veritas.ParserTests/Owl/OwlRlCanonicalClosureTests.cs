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
        TermId one = dictionary.GetOrAdd((RdfTerm)new Literal(Utf8String.FromString("1"), new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#integer"))));
        TermId two = dictionary.GetOrAdd((RdfTerm)new Literal(Utf8String.FromString("2"), new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#integer"))));
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

    /// <summary>Mints an IRI in the example namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8String.FromString(Example + local)));
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
