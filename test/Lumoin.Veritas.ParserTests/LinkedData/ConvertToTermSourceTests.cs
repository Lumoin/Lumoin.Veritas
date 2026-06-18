using System;
using System.Collections.Generic;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.ParserTests.LinkedData;

[TestClass]
internal sealed class ConvertToTermSourceTests
{
    [TestMethod]
    public void NullValueProducesRemovalMarker()
    {
        LinkedDataTermSource source = ContextProcessing.ConvertToTermSource("t", value: null, syntheticKey: "k");

        Assert.AreEqual("k", source.SyntheticKey);
        Assert.IsNull(source.Iri);
        Assert.IsNull(source.Type);
    }

    [TestMethod]
    public void StringValueProducesSimpleIriTerm()
    {
        LinkedDataTermSource source = ContextProcessing.ConvertToTermSource(
            "name", value: "http://schema.org/name", syntheticKey: "k");

        Assert.AreEqual("http://schema.org/name", source.Iri);
    }

    [TestMethod]
    public void DictionaryValueProducesExpandedTerm()
    {
        Dictionary<string, object?> dict = new()
        {
            ["@id"] = "http://example.org/foo",
            ["@type"] = "http://www.w3.org/2001/XMLSchema#integer",
            ["@protected"] = true,
            ["@prefix"] = false,
            ["@language"] = "en",
            ["@direction"] = "ltr"
        };

        LinkedDataTermSource source = ContextProcessing.ConvertToTermSource("foo", dict, "k");

        Assert.AreEqual("http://example.org/foo", source.Iri);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", source.Type);
        Assert.IsTrue(source.Protected);
        Assert.IsFalse(source.Prefix);
        Assert.AreEqual("en", source.Language);
        Assert.IsTrue(source.HasLanguageMapping);
        Assert.AreEqual("ltr", source.Direction);
    }

    [TestMethod]
    public void DictionaryContainerArrayCollectsEntries()
    {
        Dictionary<string, object?> dict = new()
        {
            ["@id"] = "http://example.org/tags",
            ["@container"] = new List<object?> { "@set", "@index" }
        };

        LinkedDataTermSource source = ContextProcessing.ConvertToTermSource("tags", dict, "k");

        Assert.IsNotNull(source.Containers);
        Assert.HasCount(2, source.Containers);
        Assert.Contains("@set", source.Containers);
        Assert.Contains("@index", source.Containers);
    }

    [TestMethod]
    public void DictionaryContainerStringSingleEntry()
    {
        Dictionary<string, object?> dict = new() { ["@container"] = "@set" };

        LinkedDataTermSource source = ContextProcessing.ConvertToTermSource("t", dict, "k");

        Assert.IsNotNull(source.Containers);
        Assert.HasCount(1, source.Containers);
        Assert.AreEqual("@set", source.Containers[0]);
    }

    [TestMethod]
    public void DictionaryReverseProducesReverseTerm()
    {
        Dictionary<string, object?> dict = new() { ["@reverse"] = "http://example.org/inverse" };

        LinkedDataTermSource source = ContextProcessing.ConvertToTermSource("t", dict, "k");

        Assert.IsTrue(source.Reverse);
        Assert.AreEqual("http://example.org/inverse", source.ReverseIri);
    }

    [TestMethod]
    public void IntegerValueIsRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => ContextProcessing.ConvertToTermSource("t", value: 42, syntheticKey: "k"));
    }
}
