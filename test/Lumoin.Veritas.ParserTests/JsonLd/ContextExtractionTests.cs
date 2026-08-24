using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.JsonLd;

[TestClass]
internal sealed class ContextExtractionTests
{
    [TestMethod]
    public void ExtractSingleUrlString()
    {
        JsonNode contextNode = ParseJson("\"http://example.org/ctx\"");
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        Assert.HasCount(1, entries);
        Assert.AreEqual("http://example.org/ctx", entries[0].Url);
        Assert.IsNull(entries[0].Terms);
    }

    [TestMethod]
    public void ExtractNullEntryProducesReset()
    {
        JsonNode contextNode = ParseJson("null");
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        Assert.HasCount(1, entries);
        Assert.IsTrue(entries[0].IsReset);
    }

    [TestMethod]
    public void ExtractInlineObjectWithSeveralTerms()
    {
        const string json = """
            {
                "@vocab": "http://schema.org/",
                "name": "http://schema.org/name",
                "age": {"@id": "http://schema.org/age", "@type": "http://www.w3.org/2001/XMLSchema#integer"}
            }
            """;
        JsonNode contextNode = ParseJson(json);
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        Assert.HasCount(1, entries);
        LinkedDataContextEntry entry = entries[0];
        Assert.AreEqual("http://schema.org/", entry.Vocab);
        Assert.IsTrue(entry.HasVocab);
        Assert.IsNotNull(entry.Terms);
        Assert.HasCount(2, entry.Terms);
        Assert.AreEqual("http://schema.org/name", entry.Terms["name"].Iri);
        Assert.AreEqual("http://schema.org/age", entry.Terms["age"].Iri);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", entry.Terms["age"].Type);
    }

    [TestMethod]
    public void ExtractArrayOfThreeContextsFlatten()
    {
        const string json = """
            [
                "http://example.org/c1",
                null,
                {"name": "http://schema.org/name"}
            ]
            """;
        JsonNode contextNode = ParseJson(json);
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        Assert.HasCount(3, entries);
        Assert.AreEqual("http://example.org/c1", entries[0].Url);
        Assert.IsTrue(entries[1].IsReset);
        Assert.IsNotNull(entries[2].Terms);
        Assert.AreEqual("http://schema.org/name", entries[2].Terms!["name"].Iri);
    }

    [TestMethod]
    public void ExtractNestedScopedContext()
    {
        const string json = """
            {
                "outer": {
                    "@id": "http://example.org/outer",
                    "@context": {
                        "inner": "http://example.org/inner"
                    }
                }
            }
            """;
        JsonNode contextNode = ParseJson(json);
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        Assert.HasCount(1, entries);
        LinkedDataTermSource outerTerm = entries[0].Terms!["outer"];
        Assert.AreEqual("http://example.org/outer", outerTerm.Iri);
        Assert.IsNotNull(outerTerm.ScopedContext);
        Assert.HasCount(1, outerTerm.ScopedContext);
        Assert.IsNotNull(outerTerm.ScopedContext[0].Terms);
        Assert.AreEqual("http://example.org/inner", outerTerm.ScopedContext[0].Terms!["inner"].Iri);
    }

    [TestMethod]
    public void ExtractContainerArrayFromTermDefinition()
    {
        const string json = """
            {
                "tags": {
                    "@id": "http://example.org/tags",
                    "@container": ["@set", "@index"]
                }
            }
            """;
        JsonNode contextNode = ParseJson(json);
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        LinkedDataTermSource tags = entries[0].Terms!["tags"];
        Assert.IsNotNull(tags.Containers);
        Assert.HasCount(2, tags.Containers);
        Assert.Contains("@set", tags.Containers);
        Assert.Contains("@index", tags.Containers);
    }

    [TestMethod]
    public void ExtractSyntheticKeysAreUnique()
    {
        const string json = """
            {
                "name": "http://schema.org/name",
                "age": "http://schema.org/age"
            }
            """;
        JsonNode contextNode = ParseJson(json);
        int keyCounter = 0;

        IReadOnlyList<LinkedDataContextEntry> entries = ContextExtraction.ExtractEntries(
            contextNode, baseUrl: null, ref keyCounter);

        LinkedDataContextEntry entry = entries[0];
        string nameKey = entry.Terms!["name"].SyntheticKey;
        string ageKey = entry.Terms["age"].SyntheticKey;
        Assert.AreNotEqual(nameKey, ageKey);
        Assert.AreNotEqual(entry.SyntheticKey, nameKey);
    }

    private static JsonNode ParseJson(string json)
    {
        Utf8String utf8 = Utf8String.WithoutPrecomputedHash(Encoding.UTF8.GetBytes(json));
        return StjJsonAdapter.Parse(utf8);
    }
}
