using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Tests for <see cref="OwlRlEntailment"/>: the refutation duals — a ground
/// <c>differentFrom</c> through the <c>sameAs</c> negation, complement
/// membership through the positive typing, an <c>owl:AllDifferent</c> block
/// through its pairwise atoms — and the negative control where the negation
/// stays consistent.
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
            new Quad(Named("stewie"), new NamedNode(Utf8String.FromString(RdfNs + "type")), Blank("c"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8String.FromString(OwlNs + "complementOf")), Named("Girl"), Graph: null),
            new Quad(Blank("c"), new NamedNode(Utf8String.FromString(RdfNs + "type")), new NamedNode(Utf8String.FromString(OwlNs + "Class")), Graph: null),
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
            new Quad(Blank("d"), new NamedNode(Utf8String.FromString(RdfNs + "type")), new NamedNode(Utf8String.FromString(OwlNs + "AllDifferent")), Graph: null),
            new Quad(Blank("d"), new NamedNode(Utf8String.FromString(OwlNs + "members")), Blank("l1"), Graph: null),
            new Quad(Blank("l1"), new NamedNode(Utf8String.FromString(RdfNs + "first")), Named("a"), Graph: null),
            new Quad(Blank("l1"), new NamedNode(Utf8String.FromString(RdfNs + "rest")), Blank("l2"), Graph: null),
            new Quad(Blank("l2"), new NamedNode(Utf8String.FromString(RdfNs + "first")), Named("b"), Graph: null),
            new Quad(Blank("l2"), new NamedNode(Utf8String.FromString(RdfNs + "rest")), new NamedNode(Utf8String.FromString(RdfNs + "nil")), Graph: null),
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

    /// <summary>A ground triple in the example namespace; absolute IRIs pass through.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate, absolute.</param>
    /// <param name="object">The object, absolute.</param>
    /// <returns>The quad.</returns>
    private static Quad Triple(string subject, string predicate, string @object)
    {
        return new Quad(
            Named(subject),
            new NamedNode(Utf8String.FromString(predicate)),
            new NamedNode(Utf8String.FromString(@object)),
            Graph: null);
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8String.FromString(Example + local));
    }

    /// <summary>A blank node with the label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8String.FromString(label));
    }
}
