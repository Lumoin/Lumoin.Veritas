using Lumoin.Veritas.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class RdfTermTests
{
    [TestMethod]
    public void NamedNodeToStringWrapsIriInAngleBrackets()
    {
        NamedNode node = new(Utf8String.FromString("http://example.org/resource"));

        Assert.AreEqual("<http://example.org/resource>", node.ToString());
    }

    [TestMethod]
    public void BlankNodeToStringPrefixesWithUnderscore()
    {
        BlankNode node = new(Utf8String.FromString("b0"));

        Assert.AreEqual("_:b0", node.ToString());
    }

    [TestMethod]
    public void LiteralToStringIncludesDatatype()
    {
        Literal literal = new(
            Utf8String.FromString("42"),
            new NamedNode(Vocabulary.Xsd.Integer));

        Assert.AreEqual("\"42\"^^<http://www.w3.org/2001/XMLSchema#integer>", literal.ToString());
    }

    [TestMethod]
    public void LanguageTaggedLiteralToStringIncludesTag()
    {
        Literal literal = new(
            Utf8String.FromString("hello"),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8String.FromString("en"));

        Assert.AreEqual("\"hello\"@en", literal.ToString());
    }

    [TestMethod]
    public void RecordEqualityWorksForNamedNodes()
    {
        Utf8String iri = Utf8String.FromString("http://example.org/same");
        NamedNode a = new(iri);
        NamedNode b = new(iri);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void PatternMatchingDispatchesCorrectly()
    {
        RdfTerm term = new BlankNode(Utf8String.FromString("b1"));

        string result = term switch
        {
            NamedNode => "named",
            BlankNode => "blank",
            Literal => "literal",
            _ => "unknown"
        };

        Assert.AreEqual("blank", result);
    }

    [TestMethod]
    public void PatternMatchingDeconstructsValues()
    {
        RdfTerm term = new NamedNode(Utf8String.FromString("http://example.org/test"));

        string iri = term switch
        {
            NamedNode(var i) => i.ToString(),
            _ => ""
        };

        Assert.AreEqual("http://example.org/test", iri);
    }
}