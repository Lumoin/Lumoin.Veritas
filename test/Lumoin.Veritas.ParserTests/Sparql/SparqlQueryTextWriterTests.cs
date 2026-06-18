using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Serialization;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlQueryTextWriter"/>: a normalised WHERE pattern renders back to a self-contained
/// <c>SELECT * WHERE { … }</c> query — with absolute IRIs (no prologue) — that re-parses cleanly. This is the
/// query text a <c>SERVICE</c> federation step transmits to a remote endpoint.
/// </summary>
[TestClass]
internal sealed class SparqlQueryTextWriterTests
{
    /// <summary>A basic graph pattern round-trips and the rendered query re-parses.</summary>
    [TestMethod]
    public void RendersBasicGraphPattern()
    {
        string rendered = SerializeWhere("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o }");

        Assert.Contains("?s", rendered);
        Assert.Contains("<http://example.org/p>", rendered);
        Assert.Contains("?o", rendered);
        Assert.IsTrue(ParsesCleanly(rendered), $"Rendered query did not re-parse: {rendered}");
    }

    /// <summary>Prefixed names are rendered as absolute IRIs so the query needs no PREFIX prologue.</summary>
    [TestMethod]
    public void ExpandsPrefixedNamesToAbsoluteIris()
    {
        string rendered = SerializeWhere("PREFIX ex: <http://example.org/> SELECT * WHERE { ex:s ex:p ex:o }");

        Assert.Contains("<http://example.org/s>", rendered);
        Assert.Contains("<http://example.org/p>", rendered);
        Assert.IsFalse(rendered.Contains("ex:", System.StringComparison.Ordinal), $"Rendered query kept a prefixed name: {rendered}");
        Assert.IsTrue(ParsesCleanly(rendered));
    }

    /// <summary>FILTER, OPTIONAL, and UNION render to re-parseable SPARQL.</summary>
    [TestMethod]
    public void RendersFilterOptionalAndUnion()
    {
        string rendered = SerializeWhere(
            "PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o . OPTIONAL { ?s :q ?x } { ?s :a ?y } UNION { ?s :b ?y } FILTER(?o > 5) }");

        Assert.Contains("OPTIONAL", rendered);
        Assert.Contains("UNION", rendered);
        Assert.Contains("FILTER(", rendered);
        Assert.IsTrue(ParsesCleanly(rendered), $"Rendered query did not re-parse: {rendered}");
    }

    /// <summary>A property path (alternative + one-or-more) renders to re-parseable SPARQL.</summary>
    [TestMethod]
    public void RendersPropertyPath()
    {
        string rendered = SerializeWhere("PREFIX : <http://example.org/> SELECT * WHERE { ?s (:p|:q)+ ?o }");

        Assert.IsTrue(ParsesCleanly(rendered), $"Rendered query did not re-parse: {rendered}");
        Assert.Contains("<http://example.org/p>", rendered);
    }

    /// <summary>GRAPH and inline VALUES render to re-parseable SPARQL.</summary>
    [TestMethod]
    public void RendersGraphAndValues()
    {
        string rendered = SerializeWhere(
            "PREFIX : <http://example.org/> SELECT * WHERE { GRAPH ?g { ?s :p ?o } VALUES ?o { 1 2 } }");

        Assert.Contains("GRAPH ?g", rendered);
        Assert.Contains("VALUES", rendered);
        Assert.IsTrue(ParsesCleanly(rendered), $"Rendered query did not re-parse: {rendered}");
    }

    /// <summary>Parses and normalises a query and renders its WHERE pattern to a self-contained query string.</summary>
    /// <param name="query">The SPARQL query text.</param>
    /// <returns>The rendered SELECT query.</returns>
    private static string SerializeWhere(string query)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(query), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlQueryTextWriter.ToSelectQuery(normalized.Where.Pattern);
    }

    /// <summary>Whether the text parses as a SPARQL query without any error diagnostic.</summary>
    /// <param name="query">The query text.</param>
    /// <returns><see langword="true"/> when it parses cleanly.</returns>
    private static bool ParsesCleanly(string query)
    {
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(query), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        ParseResult<SparqlRequest> result = parser.ParseToResult();

        return !result.HasErrors && !diagnostics.HasErrors;
    }
}
