using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Tests.Conformance;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultsXmlWriter"/>: that a <see cref="SparqlResultSet"/> serialized to SPARQL Results
/// XML and read back by <see cref="SparqlResultsXmlReader"/> is equivalent to the original (round-trip), across the
/// binding value forms including RDF-1.2 triple terms, plus the <c>ASK</c> boolean shape.
/// </summary>
[TestClass]
internal sealed class SparqlResultsXmlWriterTests
{
    private static NamedNode Iri(string iri) => new(Utf8String.FromString(iri));

    private static Literal Typed(string value, string datatype) => new(Utf8String.FromString(value), Iri(datatype));

    private static Literal Lang(string value, string language) => new(Utf8String.FromString(value), new NamedNode(Vocabulary.Rdf.LangString), Utf8String.FromString(language));

    private static SparqlBinding Bind(string variable, RdfTerm value) => new(new SparqlVariable(Utf8String.FromString(variable)), value);

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
            Bind("b", new BlankNode(Utf8String.FromString("b0"))),
            Bind("t", new TripleTerm(Iri("http://example/s"), Iri("http://example/p"), Typed("o", "http://www.w3.org/2001/XMLSchema#string")))
        ]);
        List<Utf8String> variables = [Utf8String.FromString("iri"), Utf8String.FromString("num"), Utf8String.FromString("lang"), Utf8String.FromString("plain"), Utf8String.FromString("b"), Utf8String.FromString("t")];
        SparqlResultSet original = SparqlResultSet.ForSelect(variables, [solution]);

        SparqlResultSet roundTrip = SparqlResultsXmlReader.Read(SparqlResultsXmlWriter.WriteToUtf8String(original).Memory);

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(original, roundTrip, ordered: true));
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
