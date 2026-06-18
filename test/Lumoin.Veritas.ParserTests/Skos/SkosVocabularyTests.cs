using Lumoin.Veritas.Skos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Skos;

[TestClass]
internal sealed class SkosVocabularyTests
{
    [TestMethod]
    public void CoreConceptIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#Concept",
            SkosVocabulary.Core.Concept.ToString());
    }

    [TestMethod]
    public void CoreConceptSchemeIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#ConceptScheme",
            SkosVocabulary.Core.ConceptScheme.ToString());
    }

    [TestMethod]
    public void CoreBroaderIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#broader",
            SkosVocabulary.Core.Broader.ToString());
    }

    [TestMethod]
    public void CoreNarrowerIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#narrower",
            SkosVocabulary.Core.Narrower.ToString());
    }

    [TestMethod]
    public void CorePrefLabelIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#prefLabel",
            SkosVocabulary.Core.PrefLabel.ToString());
    }

    [TestMethod]
    public void CoreInSchemeIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#inScheme",
            SkosVocabulary.Core.InScheme.ToString());
    }

    [TestMethod]
    public void CoreExactMatchIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2004/02/skos/core#exactMatch",
            SkosVocabulary.Core.ExactMatch.ToString());
    }

    [TestMethod]
    public void XlLabelIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2008/05/skos-xl#Label",
            SkosVocabulary.Xl.Label.ToString());
    }

    [TestMethod]
    public void XlLiteralFormIriIsCorrect()
    {
        Assert.AreEqual(
            "http://www.w3.org/2008/05/skos-xl#literalForm",
            SkosVocabulary.Xl.LiteralForm.ToString());
    }

    [TestMethod]
    public void CoreConstantsAreDistinct()
    {
        string[] iris =
        [
            SkosVocabulary.Core.Concept.ToString(),
            SkosVocabulary.Core.ConceptScheme.ToString(),
            SkosVocabulary.Core.Collection.ToString(),
            SkosVocabulary.Core.PrefLabel.ToString(),
            SkosVocabulary.Core.AltLabel.ToString(),
            SkosVocabulary.Core.Broader.ToString(),
            SkosVocabulary.Core.Narrower.ToString(),
            SkosVocabulary.Core.Related.ToString(),
            SkosVocabulary.Core.InScheme.ToString(),
            SkosVocabulary.Core.ExactMatch.ToString(),
        ];

        Assert.HasCount(iris.Length, iris.Distinct());
    }

    [TestMethod]
    public void AllCoreIrisStartWithCoreNamespace()
    {
        string[] iris =
        [
            SkosVocabulary.Core.Concept.ToString(),
            SkosVocabulary.Core.ConceptScheme.ToString(),
            SkosVocabulary.Core.Collection.ToString(),
            SkosVocabulary.Core.OrderedCollection.ToString(),
            SkosVocabulary.Core.PrefLabel.ToString(),
            SkosVocabulary.Core.AltLabel.ToString(),
            SkosVocabulary.Core.HiddenLabel.ToString(),
            SkosVocabulary.Core.Broader.ToString(),
            SkosVocabulary.Core.Narrower.ToString(),
            SkosVocabulary.Core.Related.ToString(),
            SkosVocabulary.Core.BroaderTransitive.ToString(),
            SkosVocabulary.Core.NarrowerTransitive.ToString(),
            SkosVocabulary.Core.InScheme.ToString(),
            SkosVocabulary.Core.HasTopConcept.ToString(),
            SkosVocabulary.Core.TopConceptOf.ToString(),
            SkosVocabulary.Core.CloseMatch.ToString(),
            SkosVocabulary.Core.ExactMatch.ToString(),
            SkosVocabulary.Core.BroadMatch.ToString(),
            SkosVocabulary.Core.NarrowMatch.ToString(),
            SkosVocabulary.Core.RelatedMatch.ToString(),
            SkosVocabulary.Core.Notation.ToString(),
        ];

        foreach(string iri in iris)
        {
            Assert.IsTrue(
                iri.StartsWith(SkosVocabulary.Core.Namespace, StringComparison.Ordinal),
                $"IRI '{iri}' does not start with SKOS Core namespace.");
        }
    }
}
