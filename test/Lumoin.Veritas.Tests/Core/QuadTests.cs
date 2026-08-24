using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class QuadTests
{
    [TestMethod]
    public void EncodeAndDecodeRoundTrips()
    {
        TermDictionary dictionary = new();
        NamedNode subject = new(Utf8Strings.From("http://example.org/s"));
        NamedNode predicate = new(Utf8Strings.From("http://example.org/p"));
        Literal @object = new(
            Utf8Strings.From("value"),
            new NamedNode(Vocabulary.Xsd.String));

        Quad original = new(subject, predicate, @object);
        EncodedQuad encoded = original.Encode(dictionary);
        Quad decoded = Quad.Decode(encoded, dictionary);

        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void DefaultGraphEncodesAsNegativeOne()
    {
        TermDictionary dictionary = new();
        Quad quad = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")));

        EncodedQuad encoded = quad.Encode(dictionary);

        Assert.AreEqual(EncodedQuad.DefaultGraph, encoded.Graph);
    }

    [TestMethod]
    public void NamedGraphEncodesCorrectly()
    {
        TermDictionary dictionary = new();
        NamedNode graph = new(Utf8Strings.From("http://example.org/graph1"));
        Quad quad = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")),
            graph);

        EncodedQuad encoded = quad.Encode(dictionary);

        Assert.AreNotEqual(EncodedQuad.DefaultGraph, encoded.Graph);
    }

    [TestMethod]
    public void IsValidReturnsTrueForNamedNodeSubject()
    {
        Quad quad = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")));

        Assert.IsTrue(quad.IsValid());
    }

    [TestMethod]
    public void IsValidReturnsTrueForBlankNodeSubject()
    {
        Quad quad = new(
            new BlankNode(Utf8Strings.From("b0")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")));

        Assert.IsTrue(quad.IsValid());
    }

    [TestMethod]
    public void IsValidReturnsFalseForLiteralSubject()
    {
        Quad quad = new(
            new Literal(Utf8Strings.From("bad"), new NamedNode(Vocabulary.Xsd.String)),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")));

        Assert.IsFalse(quad.IsValid());
    }

    [TestMethod]
    public void EncodedQuadAsTripleDropsGraph()
    {
        EncodedQuad quad = EncodedQuad.FromEncoded(1, 2, 3, 4);
        EncodedTriple triple = quad.AsTriple();

        Assert.AreEqual(TermId.FromEncoded(1), triple.Subject);
        Assert.AreEqual(TermId.FromEncoded(2), triple.Predicate);
        Assert.AreEqual(TermId.FromEncoded(3), triple.Object);
    }
}
