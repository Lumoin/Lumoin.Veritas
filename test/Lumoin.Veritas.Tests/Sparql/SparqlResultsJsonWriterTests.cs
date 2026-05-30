using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Tests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultsJsonWriter"/>: that a <see cref="SparqlResultSet"/> serialized to SPARQL
/// Results JSON and read back by <see cref="SparqlResultsJsonReader"/> is equivalent to the original (round-trip),
/// across the binding value forms including RDF-1.2 triple terms, plus the <c>ASK</c> boolean shape.
/// </summary>
[TestClass]
internal sealed class SparqlResultsJsonWriterTests
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

        SparqlResultSet roundTrip = SparqlResultsJsonReader.Read(SparqlResultsJsonWriter.WriteToBytes(original));

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(original, roundTrip, ordered: true));
    }

    /// <summary>An ASK result writes the boolean member and round-trips.</summary>
    [TestMethod]
    public void AskRoundTrips()
    {
        string json = Encoding.UTF8.GetString(SparqlResultsJsonWriter.WriteToBytes(SparqlResultSet.ForAsk(true)));
        Assert.Contains("\"boolean\": true", json);

        SparqlResultSet roundTrip = SparqlResultsJsonReader.Read(SparqlResultsJsonWriter.WriteToBytes(SparqlResultSet.ForAsk(false)));
        Assert.IsTrue(roundTrip.IsBoolean);
        Assert.IsFalse(roundTrip.Boolean!.Value);
    }
}
