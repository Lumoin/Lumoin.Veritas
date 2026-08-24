using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lumoin.Veritas.Rdf.Json;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultsJsonWriter"/>: that a <see cref="SparqlResultSet"/> serialized to SPARQL
/// Results JSON and read back by <see cref="SparqlResultsJsonReader"/> is equivalent to the original (round-trip),
/// across the binding value forms including RDF-1.2 triple terms, plus the <c>ASK</c> boolean shape.
/// </summary>
[TestClass]
internal sealed class SparqlResultsJsonWriterTests
{
    private static NamedNode Iri(string iri) => new(Utf8Strings.From(iri));

    private static Literal Typed(string value, string datatype) => new(Utf8Strings.From(value), Iri(datatype));

    private static Literal Lang(string value, string language) => new(Utf8Strings.From(value), new NamedNode(Vocabulary.Rdf.LangString), Utf8Strings.From(language));

    private static Literal LangDir(string value, string language, TextDirection direction) => new(Utf8Strings.From(value), new NamedNode(Vocabulary.Rdf.DirLangString), Utf8Strings.From(language), direction);

    private static SparqlBinding Bind(string variable, RdfTerm value) => new(new SparqlVariable(Utf8Strings.From(variable)), value);

    /// <summary>A SELECT result with every binding value form round-trips through write then read.</summary>
    [TestMethod]
    public void SelectRoundTripsAcrossValueForms()
    {
        SparqlSolution solution = new(
        [
            Bind("iri", Iri("http://example/a")),
            Bind("num", Typed("42", "http://www.w3.org/2001/XMLSchema#integer")),
            Bind("lang", Lang("hi", "en")),
            Bind("plain", Typed("plain", "http://www.w3.org/2001/XMLSchema#string")),
            Bind("b", new BlankNode(Utf8Strings.From("b0"))),
            Bind("t", new TripleTerm(Iri("http://example/s"), Iri("http://example/p"), Typed("o", "http://www.w3.org/2001/XMLSchema#string")))
        ]);
        List<Utf8String> variables = [Utf8Strings.From("iri"), Utf8Strings.From("num"), Utf8Strings.From("lang"), Utf8Strings.From("plain"), Utf8Strings.From("b"), Utf8Strings.From("t")];
        SparqlResultSet original = SparqlResultSet.ForSelect(variables, [solution]);

        SparqlResultSet roundTrip = SparqlResultsJsonReader.Read(SparqlResultsJsonWriter.WriteToUtf8String(original).Memory);

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(original, roundTrip, ordered: true));
    }

    /// <summary>A directional language-tagged literal (RDF 1.2) writes <c>its:dir</c> and round-trips with its base direction intact.</summary>
    [TestMethod]
    public void DirectionalLanguageLiteralRoundTrips()
    {
        SparqlSolution solution = new([Bind("d", LangDir("abc", "en", TextDirection.Ltr))]);
        SparqlResultSet original = SparqlResultSet.ForSelect([Utf8Strings.From("d")], [solution]);

        Utf8String document = SparqlResultsJsonWriter.WriteToUtf8String(original);
        Assert.Contains("\"its:dir\": \"ltr\"", document.ToString());

        SparqlResultSet roundTrip = SparqlResultsJsonReader.Read(document.Memory);
        Assert.IsTrue(SparqlResultComparer.AreEquivalent(original, roundTrip, ordered: true));
    }

    /// <summary>An ASK result writes the boolean member and round-trips.</summary>
    [TestMethod]
    public void AskRoundTrips()
    {
        string json = SparqlResultsJsonWriter.WriteToUtf8String(SparqlResultSet.ForAsk(true)).ToString();
        Assert.Contains("\"boolean\": true", json);

        SparqlResultSet roundTrip = SparqlResultsJsonReader.Read(SparqlResultsJsonWriter.WriteToUtf8String(SparqlResultSet.ForAsk(false)).Memory);
        Assert.IsTrue(roundTrip.IsBoolean);
        Assert.IsFalse(roundTrip.Boolean!.Value);
    }

    /// <summary>A triple-term binding nested at the depth limit serializes to completion (one "triple" marker per level).</summary>
    [TestMethod]
    public void DeepTripleTermBindingAtTheNestingLimitWrites()
    {
        SparqlSolution solution = new([Bind("t", NestSubject(QuotedTripleLimits.MaxNestingDepth))]);
        SparqlResultSet original = SparqlResultSet.ForSelect([Utf8Strings.From("t")], [solution]);

        string json = SparqlResultsJsonWriter.WriteToUtf8String(original).ToString();

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, json.Split("\"triple\"").Length - 1);
    }

    /// <summary>A triple-term binding nested beyond the limit throws the catchable depth exception, not a stack overflow.</summary>
    [TestMethod]
    public void DeepTripleTermBindingBeyondTheNestingLimitThrows()
    {
        SparqlSolution solution = new([Bind("t", NestSubject(QuotedTripleLimits.MaxNestingDepth + 1))]);
        SparqlResultSet original = SparqlResultSet.ForSelect([Utf8Strings.From("t")], [solution]);

        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => SparqlResultsJsonWriter.WriteToUtf8String(original));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth + 1, exception.Depth);
        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    /// <summary>Builds a triple term nested <paramref name="depth"/> quoted triples deep through the subject.</summary>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term.</returns>
    private static RdfTerm NestSubject(int depth)
    {
        NamedNode predicate = Iri("http://example/p");
        RdfTerm leaf = Typed("o", "http://www.w3.org/2001/XMLSchema#string");

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }
}
