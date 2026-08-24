using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database.Completion;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The composed editor-completion corpus: the core vocabularies render as prefixed names, a contributed
/// prefix-paired group renders after them in the order it was handed, and the registered value-datatype IRIs
/// form the full-IRI lane — an IRI a handed group already carries rides that group's prefixed name alone,
/// while an uncovered IRI renders angle-bracketed, and the uncovered IRIs answer in ordinal byte order. The
/// closing row composes the geospatial groups with a registry built from the geospatial datatype module, so
/// the house grid's datatype reaches the corpus as a full IRI through the one mechanism the hosts use.
/// </summary>
[TestClass]
internal sealed class EditorVocabularyTests
{
    /// <summary>The house grid datatype's full-IRI candidate, exactly as the corpus must spell it.</summary>
    private const string HouseGridCandidate = "\"<https://lumoin.com/veritas/dggs/a5Literal>\"";

    /// <summary>The GeoSPARQL ontology namespace, used to assert that no geospatial datatype takes the full-IRI lane.</summary>
    private const string GeoSparqlNamespace = "http://www.opengis.net/ont/geosparql#";

    /// <summary>An example-namespace term a contributed group carries.</summary>
    private static Utf8String AlphaTerm { get; } = Utf8Strings.From("http://example.org/vocab#alpha");

    /// <summary>A second example-namespace term a contributed group carries.</summary>
    private static Utf8String BetaTerm { get; } = Utf8Strings.From("http://example.org/vocab#beta");

    /// <summary>An example datatype IRI no group covers, ordinally before <see cref="LaterUncoveredIri"/>.</summary>
    private static Utf8String EarlierUncoveredIri { get; } = Utf8Strings.From("http://example.org/datatype/alpha");

    /// <summary>An example datatype IRI no group covers, ordinally after <see cref="EarlierUncoveredIri"/>.</summary>
    private static Utf8String LaterUncoveredIri { get; } = Utf8Strings.From("http://example.org/datatype/zeta");

    /// <summary>Every core vocabulary reaches the array as its conventional prefixed name, and the document is a JSON array.</summary>
    [TestMethod]
    public void CoreGroupsRenderAsPrefixedNames()
    {
        string json = EditorVocabulary.ToJson([], []);

        Assert.StartsWith("[", json, StringComparison.Ordinal);
        Assert.EndsWith("]", json, StringComparison.Ordinal);
        Assert.Contains(Candidate("xsd", Vocabulary.Xsd.String), json);
        Assert.Contains(Candidate("rdf", Vocabulary.Rdf.Type), json);
        Assert.Contains(Candidate("rdfs", RdfVocabulary.Rdfs.Class), json);
        Assert.Contains(Candidate("owl", OwlVocabulary.All[0]), json);
        Assert.Contains(Candidate("sh", ShaclCoreVocabulary.All[0]), json);
    }

    /// <summary>A contributed group renders its own prefixed names, after every core group.</summary>
    [TestMethod]
    public void ContributedGroupsRenderAfterTheCoreGroups()
    {
        List<(string Prefix, IReadOnlyList<Utf8String> Terms)> contributed = [("ex", new[] { AlphaTerm, BetaTerm })];

        string json = EditorVocabulary.ToJson(contributed, []);

        Assert.Contains("\"ex:alpha\"", json);
        Assert.Contains("\"ex:beta\"", json);
        Assert.IsGreaterThan(
            json.IndexOf(Candidate("xsd", Vocabulary.Xsd.String), StringComparison.Ordinal),
            json.IndexOf("\"ex:alpha\"", StringComparison.Ordinal),
            "A contributed group renders after the core groups.");
    }

    /// <summary>A datatype IRI a handed group already carries rides that group's prefixed name and never also takes the full-IRI lane.</summary>
    [TestMethod]
    public void ARosterCoveredDatatypeIriRidesItsPrefixedNameOnly()
    {
        string json = EditorVocabulary.ToJson(GeoEditorVocabulary.Groups, [GeoVocabulary.Geo.WktLiteral]);

        Assert.AreEqual(1, CountOccurrences(json, "\"geo:wktLiteral\""));
        Assert.DoesNotContain(GeoSparqlNamespace, json);
    }

    /// <summary>A datatype IRI no group carries renders as an angle-bracketed full IRI.</summary>
    [TestMethod]
    public void AnUncoveredDatatypeIriRendersAsAFullIri()
    {
        string json = EditorVocabulary.ToJson([], [A5DggsVocabulary.DatatypeIri]);

        Assert.Contains(HouseGridCandidate, json);
    }

    /// <summary>The full-IRI lane answers in ordinal byte order whatever order the caller enumerated in.</summary>
    [TestMethod]
    public void UncoveredDatatypeIrisRenderInOrdinalByteOrder()
    {
        string json = EditorVocabulary.ToJson([], [LaterUncoveredIri, EarlierUncoveredIri]);

        int earlier = json.IndexOf("\"<http://example.org/datatype/alpha>\"", StringComparison.Ordinal);
        int later = json.IndexOf("\"<http://example.org/datatype/zeta>\"", StringComparison.Ordinal);

        Assert.IsGreaterThan(0, earlier);
        Assert.IsGreaterThan(earlier, later);
    }

    /// <summary>One rendered candidate never appears twice, even when two handed groups carry the same term.</summary>
    [TestMethod]
    public void ARepeatedTermRendersOnce()
    {
        List<(string Prefix, IReadOnlyList<Utf8String> Terms)> contributed = [("ex", new[] { AlphaTerm }), ("ex", new[] { AlphaTerm })];

        string json = EditorVocabulary.ToJson(contributed, []);

        Assert.AreEqual(1, CountOccurrences(json, "\"ex:alpha\""));
    }

    /// <summary>
    /// The composition both hosts run: the geospatial groups plus the registry the geospatial datatype module
    /// builds. Every geospatial datatype the roster carries rides its prefixed name once, and the house grid's
    /// datatype — which no roster carries — reaches the corpus as its exact full-IRI candidate.
    /// </summary>
    [TestMethod]
    public void TheComposedCorpusOffersTheGeospatialNamesAndTheHouseGridFullIri()
    {
        ValueDatatypeRegistryBuilder builder = new();
        GeoExtensionModule.RegisterValueDatatypes(builder);
        ValueDatatypeRegistry registry = builder.Build();

        string json = EditorVocabulary.ToJson(GeoEditorVocabulary.Groups, registry.DatatypeIris);

        Assert.AreEqual(1, CountOccurrences(json, "\"geo:wktLiteral\""));
        Assert.AreEqual(1, CountOccurrences(json, "\"geo:dggsLiteral\""));
        Assert.AreEqual(1, CountOccurrences(json, HouseGridCandidate));
        Assert.DoesNotContain(GeoSparqlNamespace, json);
    }

    /// <summary>The quoted prefixed-name candidate a term renders as, derived from the term itself so a row cannot drift from the vocabulary constants.</summary>
    /// <param name="prefix">The conventional prefix.</param>
    /// <param name="iri">The term IRI.</param>
    /// <returns>The quoted candidate.</returns>
    private static string Candidate(string prefix, Utf8String iri)
    {
        string text = iri.ToString();
        int separator = Math.Max(text.LastIndexOf('#'), text.LastIndexOf('/'));
        string localName = separator >= 0 && separator < text.Length - 1 ? text[(separator + 1)..] : text;

        return $"\"{prefix}:{localName}\"";
    }

    /// <summary>How many times a candidate occurs in the rendered array.</summary>
    /// <param name="json">The rendered array.</param>
    /// <param name="candidate">The quoted candidate to count.</param>
    /// <returns>The occurrence count.</returns>
    private static int CountOccurrences(string json, string candidate)
    {
        int count = 0;
        int index = json.IndexOf(candidate, StringComparison.Ordinal);
        while(index >= 0)
        {
            count++;
            index = json.IndexOf(candidate, index + candidate.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
