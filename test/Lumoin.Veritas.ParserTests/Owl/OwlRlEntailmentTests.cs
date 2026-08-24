using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="OwlRlEntailment"/>: the refutation duals — a ground
/// <c>differentFrom</c> through the <c>sameAs</c> negation, complement
/// membership through the positive typing, an <c>owl:AllDifferent</c> block
/// through its pairwise atoms — the negative control where the negation
/// stays consistent, and the reduction-soundness rows: a detached membership
/// never proves through a free blank, and a block blank mentioned outside
/// its block never splits its existential binding.
/// </summary>
[TestClass]
internal sealed class OwlRlEntailmentTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";
    private const string OwlNs = "http://www.w3.org/2002/07/owl#";
    private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>Functionality plus distinct values refutes the merged pair: the contrapositive differentFrom follows.</summary>
    [TestMethod]
    public void FunctionalityRefutesTheMergedPair()
    {
        //f functional; y1 f x1; y2 f x2; x1 differentFrom x2 ⊨ y1 differentFrom y2:
        //asserting y1 sameAs y2 forces x1 sameAs x2 against the distinctness.
        List<Quad> premise =
        [
            Triple("f", RdfNs + "type", OwlNs + "FunctionalProperty"),
            Triple("y1", Example + "f", Example + "x1"),
            Triple("y2", Example + "f", Example + "x2"),
            Triple("x1", OwlNs + "differentFrom", Example + "x2"),
        ];
        List<Quad> conclusion = [Triple("y1", OwlNs + "differentFrom", Example + "y2")];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>Disjointness refutes the shared instance: complement membership follows.</summary>
    [TestMethod]
    public void DisjointnessRefutesTheSharedInstance()
    {
        //Disjoint(Boy, Girl); stewie: Boy ⊨ stewie: ¬Girl — asserting
        //stewie: Girl trips cax-dw.
        List<Quad> premise =
        [
            Triple("Boy", OwlNs + "disjointWith", Example + "Girl"),
            Triple("stewie", RdfNs + "type", Example + "Boy"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(RdfNs + "type")), new NamedNode(Utf8Strings.From(OwlNs + "Class")), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>An AllDifferent block reduces to pairwise atoms, each refuted through disjoint properties sharing a value.</summary>
    [TestMethod]
    public void AllDifferentBlockReducesToPairwiseRefutations()
    {
        //p propertyDisjointWith q; a p v; b q v ⊨ AllDifferent(a, b):
        //merging a and b puts the shared pair on both disjoint properties.
        List<Quad> premise =
        [
            Triple("p", OwlNs + "propertyDisjointWith", Example + "q"),
            Triple("a", Example + "p", Example + "v"),
            Triple("b", Example + "q", Example + "v"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Blank("d"), new NamedNode(Utf8Strings.From(RdfNs + "type")), new NamedNode(Utf8Strings.From(OwlNs + "AllDifferent")), Graph: null),
            new Quad(Blank("d"), new NamedNode(Utf8Strings.From(OwlNs + "members")), Blank("l1"), Graph: null),
            new Quad(Blank("l1"), new NamedNode(Utf8Strings.From(RdfNs + "first")), Named("a"), Graph: null),
            new Quad(Blank("l1"), new NamedNode(Utf8Strings.From(RdfNs + "rest")), Blank("l2"), Graph: null),
            new Quad(Blank("l2"), new NamedNode(Utf8Strings.From(RdfNs + "first")), Named("b"), Graph: null),
            new Quad(Blank("l2"), new NamedNode(Utf8Strings.From(RdfNs + "rest")), new NamedNode(Utf8Strings.From(RdfNs + "nil")), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>An unrelated typing of the member never satisfies a detached complement membership: the atom's blank binds only against its own block context, so the unprovable conclusion stays unproven and named.</summary>
    [TestMethod]
    public void UnrelatedTypingNeverProvesComplementMembership()
    {
        //stewie: Boy alone says nothing about Girl; stewie: ¬Girl has models
        //on both sides, so the complement membership must not follow.
        List<Quad> premise = [Triple("stewie", RdfNs + "type", Example + "Boy")];
        Quad membership = new(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null);
        List<Quad> conclusion =
        [
            membership,
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(RdfNs + "type")), new NamedNode(Utf8Strings.From(OwlNs + "Class")), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.TryEntail(premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled, cancellationToken: TestContext.CancellationToken));
        Assert.ContainsSingle(unsettled);
        Assert.AreEqual(membership, unsettled[0]);
    }

    /// <summary>The comprehension mode grants the complement class's existence, never membership in it: an unrelated typing still leaves the complement membership unproven.</summary>
    [TestMethod]
    public void UnrelatedTypingNeverProvesComplementMembershipUnderComprehension()
    {
        List<Quad> premise = [Triple("stewie", RdfNs + "type", Example + "Boy")];
        List<Quad> conclusion =
        [
            new Quad(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>A complement-block blank mentioned outside its block is one existential: no split binding proves the conjunction when no single witness satisfies every mention.</summary>
    [TestMethod]
    public void ComplementBlankMentionedElsewhereNeverSplitsItsBinding()
    {
        //C witnesses the membership and the complement, B witnesses the
        //mention — but the conclusion asks for ONE node doing all three.
        List<Quad> premise =
        [
            Triple("stewie", RdfNs + "type", Example + "C"),
            Triple("C", OwlNs + "complementOf", Example + "Girl"),
            Triple("lois", Example + "knows", Example + "B"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
            new Quad(Named("lois"), new NamedNode(Utf8Strings.From(Example + "knows")), Blank("c"), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>An AllDifferent block node mentioned outside its block keeps the whole block residual: the pairwise atoms never detach from the extra mention.</summary>
    [TestMethod]
    public void AllDifferentBlockMentionedElsewhereStaysResidual()
    {
        //The pairwise distinctness holds, but the conclusion also claims the
        //block node itself stands in a relation nothing in the premise
        //grounds for such a node.
        List<Quad> premise =
        [
            Triple("p", OwlNs + "propertyDisjointWith", Example + "q"),
            Triple("a", Example + "p", Example + "v"),
            Triple("b", Example + "q", Example + "v"),
            Triple("lois", Example + "knows", Example + "Z"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Blank("d"), new NamedNode(Utf8Strings.From(RdfNs + "type")), new NamedNode(Utf8Strings.From(OwlNs + "AllDifferent")), Graph: null),
            new Quad(Blank("d"), new NamedNode(Utf8Strings.From(OwlNs + "members")), Blank("l1"), Graph: null),
            new Quad(Blank("l1"), new NamedNode(Utf8Strings.From(RdfNs + "first")), Named("a"), Graph: null),
            new Quad(Blank("l1"), new NamedNode(Utf8Strings.From(RdfNs + "rest")), Blank("l2"), Graph: null),
            new Quad(Blank("l2"), new NamedNode(Utf8Strings.From(RdfNs + "first")), Named("b"), Graph: null),
            new Quad(Blank("l2"), new NamedNode(Utf8Strings.From(RdfNs + "rest")), new NamedNode(Utf8Strings.From(RdfNs + "nil")), Graph: null),
            new Quad(Named("lois"), new NamedNode(Utf8Strings.From(Example + "knows")), Blank("d"), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>Documents the standing abstention: a complement block whose blank is mentioned outside the block stays residual even when the closure carries a genuine joint witness whose membership leg only refutation could prove — the surface answers unproven rather than split the binding. Closing the gap takes a grounded-witness decomposition; this row flips red the day one lands.</summary>
    [TestMethod]
    public void JointWitnessBehindRefutationStaysUnprovenToday()
    {
        //Entailed with witness C: disjointness puts stewie outside Girl,
        //complementOf pins C to exactly that complement, and lois knows C is
        //asserted. The surface still abstains: the membership leg never
        //materializes forward, and the unconfined block never reduces.
        List<Quad> premise =
        [
            Triple("Boy", OwlNs + "disjointWith", Example + "Girl"),
            Triple("stewie", RdfNs + "type", Example + "Boy"),
            Triple("C", OwlNs + "complementOf", Example + "Girl"),
            Triple("lois", Example + "knows", Example + "C"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
            new Quad(Named("lois"), new NamedNode(Utf8Strings.From(Example + "knows")), Blank("c"), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.TryEntail(premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled, cancellationToken: TestContext.CancellationToken));
        Assert.IsNotEmpty(unsettled);
    }

    /// <summary>Complement membership the closure carries embeds directly, the blank bound to the genuine complement class.</summary>
    [TestMethod]
    public void AssertedComplementMembershipEmbedsDirectly()
    {
        List<Quad> premise =
        [
            Triple("stewie", RdfNs + "type", Example + "C"),
            Triple("C", OwlNs + "complementOf", Example + "Girl"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>One complement block serves several memberships: every mention of the blank is a reducing membership or a block triple, so each membership proves on its own atom.</summary>
    [TestMethod]
    public void SharedComplementBlockServesEveryMembership()
    {
        List<Quad> premise =
        [
            Triple("Boy", OwlNs + "disjointWith", Example + "Girl"),
            Triple("stewie", RdfNs + "type", Example + "Boy"),
            Triple("meg", RdfNs + "type", Example + "Boy"),
        ];
        List<Quad> conclusion =
        [
            new Quad(Named("stewie"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Named("meg"), new NamedNode(Utf8Strings.From(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8Strings.From(OwlNs + "complementOf")), Named("Girl"), Graph: null),
        ];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>A differentFrom whose negation stays consistent is not entailed.</summary>
    [TestMethod]
    public void ConsistentNegationMeansNoEntailment()
    {
        List<Quad> premise =
        [
            Triple("y1", Example + "f", Example + "x1"),
            Triple("y2", Example + "f", Example + "x2"),
        ];
        List<Quad> conclusion = [Triple("y1", OwlNs + "differentFrom", Example + "y2")];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>An inconsistent premise entails everything.</summary>
    [TestMethod]
    public void InconsistentPremiseEntailsEverything()
    {
        List<Quad> premise =
        [
            Triple("x", OwlNs + "sameAs", Example + "y"),
            Triple("x", OwlNs + "differentFrom", Example + "y"),
        ];
        List<Quad> conclusion = [Triple("anything", Example + "p", Example + "atAll")];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.Entails(premise, conclusion, dictionary, new OwlRlTerms(dictionary), cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>A settled entailment answers an empty unsettled remainder through the refutation path.</summary>
    [TestMethod]
    public void TryEntailAnswersEmptyOnSuccess()
    {
        List<Quad> premise =
        [
            Triple("f", RdfNs + "type", OwlNs + "FunctionalProperty"),
            Triple("y1", Example + "f", Example + "x1"),
            Triple("y2", Example + "f", Example + "x2"),
            Triple("x1", OwlNs + "differentFrom", Example + "x2"),
        ];
        List<Quad> conclusion = [Triple("y1", OwlNs + "differentFrom", Example + "y2")];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.TryEntail(premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled, cancellationToken: TestContext.CancellationToken));
        Assert.IsEmpty(unsettled);
    }

    /// <summary>A refutation atom whose negation stays consistent is named in the unsettled remainder.</summary>
    [TestMethod]
    public void TryEntailNamesTheUnprovenRefutationAtom()
    {
        List<Quad> premise =
        [
            Triple("y1", Example + "f", Example + "x1"),
            Triple("y2", Example + "f", Example + "x2"),
        ];
        Quad atom = Triple("y1", OwlNs + "differentFrom", Example + "y2");
        List<Quad> conclusion = [atom];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.TryEntail(premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled, cancellationToken: TestContext.CancellationToken));
        Assert.ContainsSingle(unsettled);
        Assert.AreEqual(atom, unsettled[0]);
    }

    /// <summary>A vocabulary resolved through a different dictionary is rejected fail-fast: independent dictionaries mint colliding identifiers, so the pairing throws instead of answering wrongly.</summary>
    [TestMethod]
    public void MismatchedDictionaryPairingThrows()
    {
        Assert.Throws<ArgumentException>(static () => OwlRlEntailment.Entails([], [], new TermDictionary(), new OwlRlTerms(new TermDictionary()), cancellationToken: CancellationToken.None));
    }

    /// <summary>A conclusion without refutation atoms reports the straight embedding's remainder.</summary>
    [TestMethod]
    public void TryEntailNamesTheStraightRemainderWithoutAtoms()
    {
        List<Quad> premise = [Triple("a", Example + "p", Example + "b")];
        Quad present = Triple("a", Example + "p", Example + "b");
        Quad missing = Triple("a", Example + "p", Example + "c");
        List<Quad> conclusion = [present, missing];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.TryEntail(premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled, cancellationToken: TestContext.CancellationToken));
        Assert.ContainsSingle(unsettled);
        Assert.AreEqual(missing, unsettled[0]);
    }

    /// <summary>An inconsistent premise entails everything with an empty remainder.</summary>
    [TestMethod]
    public void TryEntailAnswersEmptyOnInconsistentPremise()
    {
        List<Quad> premise =
        [
            Triple("x", OwlNs + "sameAs", Example + "y"),
            Triple("x", OwlNs + "differentFrom", Example + "y"),
        ];
        List<Quad> conclusion = [Triple("anything", Example + "p", Example + "atAll")];

        TermDictionary dictionary = new();
        Assert.IsTrue(OwlRlEntailment.TryEntail(premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled, cancellationToken: TestContext.CancellationToken));
        Assert.IsEmpty(unsettled);
    }

    /// <summary>The census reason renderer names the count, renders in order, caps at eight triples, and folds whitespace to keep the sink line whole.</summary>
    [TestMethod]
    public void DescribeUnsettledCapsAndFoldsWhitespace()
    {
        List<Quad> two =
        [
            Triple("a", Example + "p", Example + "b"),
            new Quad(Named("c"), new NamedNode(Utf8Strings.From(Example + "q")), new Literal(Utf8Strings.From("line\nbreak\tand"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"))), Graph: null),
        ];
        string reason = W3cOwl2RdfBasedTests.DescribeUnsettled(two);
        Assert.AreEqual(
            "unsettled 2: <http://example.org/a> <http://example.org/p> <http://example.org/b>; "
            + "<http://example.org/c> <http://example.org/q> \"line break and\"^^<http://www.w3.org/2001/XMLSchema#string>",
            reason);

        List<Quad> ten = [];
        for(int i = 0; i < 10; i++)
        {
            ten.Add(Triple("s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Example + "p", Example + "o"));
        }

        string capped = W3cOwl2RdfBasedTests.DescribeUnsettled(ten);
        Assert.StartsWith("unsettled 10: ", capped);
        Assert.EndsWith("; +2 more", capped);
        Assert.DoesNotContain("\n", capped);
        Assert.DoesNotContain("\t", capped);
    }

    /// <summary>A ground triple in the example namespace; absolute IRIs pass through.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate, absolute.</param>
    /// <param name="object">The object, absolute.</param>
    /// <returns>The quad.</returns>
    private static Quad Triple(string subject, string predicate, string @object)
    {
        return new Quad(
            Named(subject),
            new NamedNode(Utf8Strings.From(predicate)),
            new NamedNode(Utf8Strings.From(@object)),
            Graph: null);
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A blank node with the label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }
}
