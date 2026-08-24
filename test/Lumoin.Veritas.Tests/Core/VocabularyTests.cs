using Lumoin.Veritas.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class VocabularyTests
{
    [TestMethod]
    public void XsdStringHasCorrectIri()
    {
        Assert.AreEqual(
            "http://www.w3.org/2001/XMLSchema#string",
            Vocabulary.Xsd.String.ToString());
    }

    [TestMethod]
    public void XsdBooleanHasCorrectIri()
    {
        Assert.AreEqual(
            "http://www.w3.org/2001/XMLSchema#boolean",
            Vocabulary.Xsd.Boolean.ToString());
    }

    [TestMethod]
    public void RdfTypeHasCorrectIri()
    {
        Assert.AreEqual(
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#type",
            Vocabulary.Rdf.Type.ToString());
    }

    [TestMethod]
    public void RdfLangStringHasCorrectIri()
    {
        Assert.AreEqual(
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString",
            Vocabulary.Rdf.LangString.ToString());
    }

    [TestMethod]
    public void VocabularyConstantsAreStable()
    {
        //Same property access returns equal values across calls.
        Utf8String first = Vocabulary.Xsd.Integer;
        Utf8String second = Vocabulary.Xsd.Integer;

        Assert.AreEqual(first, second);
    }
}