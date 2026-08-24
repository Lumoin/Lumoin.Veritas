using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultsXmlWriter"/>: that a <see cref="SparqlResultSet"/> serialized to SPARQL Results
/// XML and read back by <see cref="SparqlResultsXmlReader"/> is equivalent to the original (round-trip), across the
/// binding value forms including RDF-1.2 triple terms, plus the <c>ASK</c> boolean shape.
/// </summary>
[TestClass]
internal sealed class SparqlResultsXmlWriterTests
{
    private static NamedNode Iri(string iri) => new(Utf8Strings.From(iri));

    private static Literal Typed(string value, string datatype) => new(Utf8Strings.From(value), Iri(datatype));

    private static Literal Lang(string value, string language) => new(Utf8Strings.From(value), new NamedNode(Vocabulary.Rdf.LangString), Utf8Strings.From(language));

    private static Literal DirLang(string value, string language, TextDirection direction) => new(Utf8Strings.From(value), new NamedNode(Vocabulary.Rdf.DirLangString), Utf8Strings.From(language), direction);

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

        SparqlResultSet roundTrip = SparqlResultsXmlReader.Read(SparqlResultsXmlWriter.WriteToUtf8String(original).Memory);

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(original, roundTrip, ordered: true));
    }

    /// <summary>A directional language-tagged literal (RDF 1.2) writes <c>its:dir</c> alongside <c>xml:lang</c> and round-trips both the language tag and the base direction.</summary>
    [TestMethod]
    public void DirectionalLanguageLiteralRoundTrips()
    {
        SparqlResultSet original = SparqlResultSet.ForSelect(
            [Utf8Strings.From("d")],
            [new SparqlSolution([Bind("d", DirLang("قطة", "ar", TextDirection.Rtl))])]);

        string xml = SparqlResultsXmlWriter.WriteToUtf8String(original).ToString();
        Assert.Contains("xml:lang=\"ar\"", xml);
        Assert.Contains("its:dir=\"rtl\"", xml);

        SparqlResultSet roundTrip = SparqlResultsXmlReader.Read(SparqlResultsXmlWriter.WriteToUtf8String(original).Memory);
        Literal read = (Literal)roundTrip.Solutions[0].Bindings[0].Value;
        Assert.AreEqual("ar", read.Language?.ToString());
        Assert.AreEqual(TextDirection.Rtl, read.BaseDirection);
    }

    /// <summary>An ASK result writes the boolean element and round-trips.</summary>
    [TestMethod]
    public void AskRoundTrips()
    {
        string xml = SparqlResultsXmlWriter.WriteToUtf8String(SparqlResultSet.ForAsk(true)).ToString();
        Assert.Contains("<boolean>true</boolean>", xml);

        SparqlResultSet roundTrip = SparqlResultsXmlReader.Read(SparqlResultsXmlWriter.WriteToUtf8String(SparqlResultSet.ForAsk(false)).Memory);
        Assert.IsTrue(roundTrip.IsBoolean);
        Assert.IsFalse(roundTrip.Boolean!.Value);
    }
}
