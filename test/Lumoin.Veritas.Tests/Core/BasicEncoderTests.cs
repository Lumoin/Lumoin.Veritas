using System.Collections.Generic;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verifies RDF 1.2 basic-encoding and decoding against the
/// <see href="https://www.w3.org/TR/rdf12-interop/#basic-encoding">RDF 1.2 Interoperability §3</see>
/// algorithm: each triple term becomes a fresh blank node plus four <c>rdf:PropositionForm</c>
/// assertions, identical triple terms in a graph collapse to one blank node, and decoding restores the
/// original Full dataset. Malformed Basic input is rejected.
/// </summary>
[TestClass]
internal sealed class BasicEncoderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EncodesSimpleTripleTermAsBlankNodeAndFourAssertions()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), tripleTerm)];

        List<Quad> encoded = BasicEncoder.Encode(input, pool);

        BlankNode marker = Blank(pool, "e0");
        Quad[] expected =
        [
            new(marker, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), Iri(pool, "http://example/a")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c")),
            new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), marker)
        ];

        AssertSameQuadSet(expected, encoded, "simple triple term");
    }

    [TestMethod]
    public void EncodesNestedTripleTermInnermostFirst()
    {
        using Utf8StringPool pool = new();
        TripleTerm inner = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        TripleTerm outer = new(Iri(pool, "http://example/x"), Iri(pool, "http://example/y"), inner);
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), outer)];

        List<Quad> encoded = BasicEncoder.Encode(input, pool);

        //Two markers (inner e0, outer e1), eight assertions, plus the rewritten triple referencing the outer.
        Assert.HasCount(9, encoded);

        BlankNode innerMarker = Blank(pool, "e0");
        BlankNode outerMarker = Blank(pool, "e1");
        Assert.Contains(new Quad(innerMarker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c")), encoded);
        Assert.Contains(new Quad(outerMarker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), innerMarker), encoded);
        Assert.Contains(new Quad(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), outerMarker), encoded);
    }

    [TestMethod]
    public void EncodesTripleTermMarkersInContainingGraph()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        NamedNode graph = Iri(pool, "http://example/g");
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), tripleTerm, graph)];

        List<Quad> encoded = BasicEncoder.Encode(input, pool);

        foreach(Quad quad in encoded)
        {
            Assert.AreEqual(graph, quad.Graph, "every encoded quad stays in the containing graph");
        }
    }

    [TestMethod]
    public void EncodesIdenticalTripleTermsToOneBlankNode()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] input =
        [
            new(Iri(pool, "http://example/s1"), Iri(pool, "http://example/p"), tripleTerm),
            new(Iri(pool, "http://example/s2"), Iri(pool, "http://example/p"), tripleTerm)
        ];

        List<Quad> encoded = BasicEncoder.Encode(input, pool);

        //Four assertions for the single shared blank node plus two rewritten triples.
        Assert.HasCount(6, encoded);
        BlankNode marker = Blank(pool, "e0");
        Assert.Contains(new Quad(Iri(pool, "http://example/s1"), Iri(pool, "http://example/p"), marker), encoded);
        Assert.Contains(new Quad(Iri(pool, "http://example/s2"), Iri(pool, "http://example/p"), marker), encoded);
    }

    [TestMethod]
    public void EncodedBlankNodeLabelsAvoidExistingLabels()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] input = [new(Blank(pool, "e0"), Iri(pool, "http://example/p"), tripleTerm)];

        List<Quad> encoded = BasicEncoder.Encode(input, pool);

        //The pre-existing _:e0 forces the minted marker to a free label.
        Assert.Contains(new Quad(Blank(pool, "e0"), Iri(pool, "http://example/p"), Blank(pool, "e1")), encoded);
    }

    [TestMethod]
    public void EncodesEmptyDatasetToEmpty()
    {
        using Utf8StringPool pool = new();

        List<Quad> encoded = BasicEncoder.Encode([], pool);

        Assert.IsEmpty(encoded);
    }

    [TestMethod]
    public void EncodesDatasetWithoutTripleTermsUnchanged()
    {
        using Utf8StringPool pool = new();
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), Iri(pool, "http://example/o"))];

        List<Quad> encoded = BasicEncoder.Encode(input, pool);

        AssertSameQuadSet(input, encoded, "no triple terms");
    }

    [TestMethod]
    public void DecodesHandBuiltMarkerToTripleTerm()
    {
        using Utf8StringPool pool = new();
        BlankNode marker = Blank(pool, "m");
        Quad[] input =
        [
            new(marker, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), Iri(pool, "http://example/a")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c")),
            new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), marker)
        ];

        List<Quad> decoded = BasicEncoder.Decode(input);

        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] expected = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), tripleTerm)];
        AssertSameQuadSet(expected, decoded, "hand-built marker");
    }

    [TestMethod]
    public void DecodesDatasetWithoutMarkersUnchanged()
    {
        using Utf8StringPool pool = new();
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), Iri(pool, "http://example/o"))];

        List<Quad> decoded = BasicEncoder.Decode(input);

        AssertSameQuadSet(input, decoded, "no markers");
    }

    [TestMethod]
    public void DecodingThrowsOnMissingSubjectAssertion()
    {
        using Utf8StringPool pool = new();
        BlankNode marker = Blank(pool, "m");
        Quad[] input =
        [
            new(marker, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c"))
        ];

        Assert.Throws<BasicEncodingException>(() => BasicEncoder.Decode(input));
    }

    [TestMethod]
    public void DecodingThrowsOnDuplicateSubjectAssertion()
    {
        using Utf8StringPool pool = new();
        BlankNode marker = Blank(pool, "m");
        Quad[] input =
        [
            new(marker, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), Iri(pool, "http://example/a")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), Iri(pool, "http://example/a2")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c"))
        ];

        Assert.Throws<BasicEncodingException>(() => BasicEncoder.Decode(input));
    }

    [TestMethod]
    public void DecodingThrowsWhenPredicatePositionIsNotAnIri()
    {
        using Utf8StringPool pool = new();
        BlankNode marker = Blank(pool, "m");
        Quad[] input =
        [
            new(marker, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), Iri(pool, "http://example/a")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), new Literal(pool.Intern("not an iri"), new NamedNode(Vocabulary.Xsd.String))),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c"))
        ];

        Assert.Throws<BasicEncodingException>(() => BasicEncoder.Decode(input));
    }

    [TestMethod]
    public void DecodingThrowsOnMixedTripleTermAndMarker()
    {
        using Utf8StringPool pool = new();
        BlankNode marker = Blank(pool, "m");
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] input =
        [
            new(marker, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), Iri(pool, "http://example/a")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(marker, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c")),
            new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), tripleTerm)
        ];

        Assert.Throws<BasicEncodingException>(() => BasicEncoder.Decode(input));
    }

    [TestMethod]
    public void DecodingThrowsOnMarkerCycle()
    {
        using Utf8StringPool pool = new();
        BlankNode first = Blank(pool, "m0");
        BlankNode second = Blank(pool, "m1");
        Quad[] input =
        [
            new(first, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(first, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), second),
            new(first, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(first, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c")),
            new(second, PropositionFormType(pool), new NamedNode(Vocabulary.Rdf.PropositionForm)),
            new(second, new NamedNode(Vocabulary.Rdf.PropositionFormSubject), first),
            new(second, new NamedNode(Vocabulary.Rdf.PropositionFormPredicate), Iri(pool, "http://example/b")),
            new(second, new NamedNode(Vocabulary.Rdf.PropositionFormObject), Iri(pool, "http://example/c"))
        ];

        Assert.Throws<BasicEncodingException>(() => BasicEncoder.Decode(input));
    }

    [TestMethod]
    public void RoundTripRestoresSimpleTripleTerm()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), tripleTerm)];

        AssertRoundTrips(input, pool, "simple");
    }

    [TestMethod]
    public void RoundTripRestoresNestedTripleTerm()
    {
        using Utf8StringPool pool = new();
        TripleTerm inner = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        TripleTerm outer = new(Iri(pool, "http://example/x"), Iri(pool, "http://example/y"), inner);
        Quad[] input = [new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), outer)];

        AssertRoundTrips(input, pool, "nested");
    }

    [TestMethod]
    public void RoundTripRestoresTripleTermInNamedGraphWithExistingBlankNodes()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Blank(pool, "shared"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        NamedNode graph = Iri(pool, "http://example/g");
        Quad[] input =
        [
            new(Blank(pool, "shared"), Iri(pool, "http://example/q"), Iri(pool, "http://example/o"), graph),
            new(Iri(pool, "http://example/s"), Iri(pool, "http://example/p"), tripleTerm, graph)
        ];

        AssertRoundTrips(input, pool, "named graph with existing blank nodes");
    }

    [TestMethod]
    public void RoundTripRestoresRepeatedTripleTerm()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(Iri(pool, "http://example/a"), Iri(pool, "http://example/b"), Iri(pool, "http://example/c"));
        Quad[] input =
        [
            new(Iri(pool, "http://example/s1"), Iri(pool, "http://example/p"), tripleTerm),
            new(Iri(pool, "http://example/s2"), Iri(pool, "http://example/p"), tripleTerm)
        ];

        AssertRoundTrips(input, pool, "repeated triple term");
    }

    private static void AssertRoundTrips(IReadOnlyList<Quad> input, Utf8StringPool pool, string message)
    {
        List<Quad> encoded = BasicEncoder.Encode(input, pool);
        List<Quad> decoded = BasicEncoder.Decode(encoded);

        AssertSameQuadSet(input, decoded, message);
    }

    private static NamedNode Iri(Utf8StringPool pool, string iri)
    {
        return new NamedNode(pool.Intern(iri));
    }

    private static BlankNode Blank(Utf8StringPool pool, string label)
    {
        return new BlankNode(pool.Intern(label));
    }

    private static NamedNode PropositionFormType(Utf8StringPool pool)
    {
        return new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"));
    }

    private static void AssertSameQuadSet(IReadOnlyList<Quad> expected, IReadOnlyList<Quad> actual, string message)
    {
        Assert.HasCount(expected.Count, actual, message);

        Dictionary<Quad, int> counts = [];
        foreach(Quad quad in expected)
        {
            counts[quad] = counts.TryGetValue(quad, out int existing) ? existing + 1 : 1;
        }

        foreach(Quad quad in actual)
        {
            Assert.IsTrue(counts.TryGetValue(quad, out int remaining) && remaining > 0, message);
            counts[quad] = remaining - 1;
        }
    }
}
