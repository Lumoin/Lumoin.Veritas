using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Cbor.CborLd;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Tests for <see cref="CborLdRegistryJsonLoader"/>: parses W3C CBOR-LD 1.0
/// registry JSON into <see cref="CborLdRegistryEntry"/> instances and
/// recognises the <c>"callerProvidedTable"</c> sentinel.
/// </summary>
[TestClass]
internal sealed class CborLdRegistryJsonLoaderTests
{
    public required TestContext TestContext { get; set; }

    private static Utf8String Utf8(string s) => new(Encoding.UTF8.GetBytes(s));

    [TestMethod]
    public void LoadsMinimalEntryWithIdOnly()
    {
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {"id": 42}
            """));

        Assert.AreEqual(42, entry.RegistryEntryId);
        Assert.IsEmpty(entry.Keywords);
        Assert.IsEmpty(entry.Terms);
        Assert.IsEmpty(entry.TypeTables);
        Assert.AreEqual("default", entry.ProcessingModel);
        Assert.IsFalse(entry.Provisional);
    }

    [TestMethod]
    public void LoadsKeywordsTable()
    {
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 1,
              "keywords": { "@context": 0, "@id": 2, "@type": 4 }
            }
            """));

        Assert.HasCount(3, entry.Keywords);
        Assert.AreEqual(0, entry.Keywords["@context"].CborId);
        Assert.AreEqual(2, entry.Keywords["@id"].CborId);
        Assert.AreEqual(4, entry.Keywords["@type"].CborId);
    }

    [TestMethod]
    public void LoadsCompactFormTerms()
    {
        //Compact form: "termName": <int>. Sets the codec id with no Type.
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 1,
              "terms": { "name": 100, "age": 102 }
            }
            """));

        Assert.HasCount(2, entry.Terms);
        Assert.AreEqual(100, entry.Terms["name"].CborId);
        Assert.IsNull(entry.Terms["name"].Type);
        Assert.AreEqual(102, entry.Terms["age"].CborId);
    }

    [TestMethod]
    public void LoadsFullFormTypedTerm()
    {
        //Full form: "termName": { "id": <int>, "type": <string|null> }
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 1,
              "terms": {
                "link": { "id": 100, "type": "url" }
              }
            }
            """));

        Assert.HasCount(1, entry.Terms);
        Assert.AreEqual(100, entry.Terms["link"].CborId);
        Assert.AreEqual("url", entry.Terms["link"].Type);
    }

    [TestMethod]
    public void LoadsRegistryProvidedTypeTable()
    {
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 1,
              "typeTables": {
                "url": { "https://example.org/a": 200, "https://example.org/b": 202 }
              }
            }
            """));

        Assert.HasCount(1, entry.TypeTables);
        CborLdTypeTableSource source = entry.TypeTables["url"];
        Assert.IsFalse(source.IsCallerProvided);
        IReadOnlyDictionary<string, int>? mappings = source.Mappings;
        Assert.IsNotNull(mappings);
        Assert.AreEqual(200, mappings["https://example.org/a"]);
        Assert.AreEqual(202, mappings["https://example.org/b"]);
    }

    [TestMethod]
    public void LoadsCallerProvidedTableSentinel()
    {
        //W3C CBOR-LD 1.0: the typeTables object's value may be the literal
        //string "callerProvidedTable" indicating the table is caller-supplied.
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 1,
              "typeTables": {
                "url": "callerProvidedTable"
              }
            }
            """));

        Assert.HasCount(1, entry.TypeTables);
        Assert.IsTrue(entry.TypeTables["url"].IsCallerProvided);
        Assert.IsNull(entry.TypeTables["url"].Mappings);
    }

    [TestMethod]
    public void LoadsMixedTypeTables()
    {
        //One registry-provided, one caller-provided in the same entry.
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 1,
              "typeTables": {
                "url": "callerProvidedTable",
                "xsd:date": { "1970-01-01": 0, "2000-01-01": 10957 }
              }
            }
            """));

        Assert.HasCount(2, entry.TypeTables);
        Assert.IsTrue(entry.TypeTables["url"].IsCallerProvided);
        Assert.IsFalse(entry.TypeTables["xsd:date"].IsCallerProvided);
        Assert.AreEqual(10957, entry.TypeTables["xsd:date"].Mappings!["2000-01-01"]);
    }

    [TestMethod]
    public void LoadsProcessingModelAndProvisional()
    {
        CborLdRegistryEntry entry = LoadEntry(Utf8(/*lang=json,strict*/ """
            {
              "id": 7,
              "processingModel": "experimental",
              "provisional": true
            }
            """));

        Assert.AreEqual("experimental", entry.ProcessingModel);
        Assert.IsTrue(entry.Provisional);
    }

    [TestMethod]
    public void LoadEntriesParsesArray()
    {
        IReadOnlyList<CborLdRegistryEntry> entries = LoadEntries(Utf8(/*lang=json,strict*/ """
            [
              { "id": 1, "keywords": { "@id": 2 } },
              { "id": 2, "terms": { "name": 100 } },
              { "id": 3, "typeTables": { "url": "callerProvidedTable" } }
            ]
            """));

        Assert.HasCount(3, entries);
        Assert.AreEqual(1, entries[0].RegistryEntryId);
        Assert.AreEqual(100, entries[1].Terms["name"].CborId);
        Assert.IsTrue(entries[2].TypeTables["url"].IsCallerProvided);
    }

    [TestMethod]
    public void MalformedJsonThrowsProcessingException()
    {
        CborLdProcessingException ex = Assert.ThrowsExactly<CborLdProcessingException>(() =>
            LoadEntry(Utf8("{ this is not valid json")));

        Assert.AreEqual("invalid registry json", ex.ErrorCode);
    }

    [TestMethod]
    public void MissingIdThrowsProcessingException()
    {
        CborLdProcessingException ex = Assert.ThrowsExactly<CborLdProcessingException>(() =>
            LoadEntry(Utf8(/*lang=json,strict*/ """
                {"keywords": {}}
                """)));

        Assert.AreEqual("invalid registry entry", ex.ErrorCode);
        Assert.Contains("id", ex.Message);
    }

    [TestMethod]
    public void UnknownStringSentinelInTypeTableThrows()
    {
        //Any string value in typeTables other than the spec sentinel
        //"callerProvidedTable" is an error.
        CborLdProcessingException ex = Assert.ThrowsExactly<CborLdProcessingException>(() =>
            LoadEntry(Utf8(/*lang=json,strict*/ """
                {
                  "id": 1,
                  "typeTables": { "url": "somethingElse" }
                }
                """)));

        Assert.AreEqual("invalid registry entry", ex.ErrorCode);
        Assert.Contains("somethingElse", ex.Message);
    }

    [TestMethod]
    public void NonObjectTopLevelInLoadEntriesThrows()
    {
        CborLdProcessingException ex = Assert.ThrowsExactly<CborLdProcessingException>(() =>
            LoadEntries(Utf8(/*lang=json,strict*/ """
                {"id": 1}
                """)));

        Assert.AreEqual("invalid registry document", ex.ErrorCode);
    }

    [TestMethod]
    public void ArrayItemErrorMentionsIndex()
    {
        //Second entry (index 1) is malformed; the error message must
        //identify the index so the caller can locate the bad entry.
        CborLdProcessingException ex = Assert.ThrowsExactly<CborLdProcessingException>(() =>
            LoadEntries(Utf8(/*lang=json,strict*/ """
                [
                  {"id": 1},
                  {"keywords": {}}
                ]
                """)));

        Assert.Contains("index 1", ex.Message);
    }

    /// <summary>Parses a single registry entry, supplying the System.Text.Json-backed parser.</summary>
    private static CborLdRegistryEntry LoadEntry(Utf8String utf8Json)
        => CborLdRegistryJsonLoader.LoadEntry(utf8Json, StjJsonAdapter.Parse);

    /// <summary>Parses an array of registry entries, supplying the System.Text.Json-backed parser.</summary>
    private static IReadOnlyList<CborLdRegistryEntry> LoadEntries(Utf8String utf8Json)
        => CborLdRegistryJsonLoader.LoadEntries(utf8Json, StjJsonAdapter.Parse);
}
