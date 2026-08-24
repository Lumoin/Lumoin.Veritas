using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultsXmlReader"/>: parsing the SPARQL Query Results XML serialization of
/// <c>SELECT</c> (variables and the binding value forms) and <c>ASK</c> results, including RDF-1.2 triple terms.
/// </summary>
[TestClass]
internal sealed class SparqlResultsXmlReaderTests
{
    /// <summary>Reads the result set from the XML text.</summary>
    /// <param name="xml">The SPARQL Results XML.</param>
    /// <returns>The parsed result set.</returns>
    private static SparqlResultSet Read(string xml)
    {
        return SparqlResultsXmlReader.Read(Encoding.UTF8.GetBytes(xml));
    }

    /// <summary>Returns the value bound to a variable in a solution.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variable">The variable name.</param>
    /// <returns>The bound term.</returns>
    private static RdfTerm Value(SparqlSolution solution, string variable)
    {
        Assert.IsTrue(solution.TryGetValue(new SparqlVariable(Utf8Strings.From(variable)), out RdfTerm value), $"Expected ?{variable} to be bound.");

        return value;
    }

    /// <summary>A <c>SELECT</c> result's head variables are read in declared order.</summary>
    [TestMethod]
    public void ReadsSelectHeadVariablesInOrder()
    {
        SparqlResultSet result = Read(
            """
            <?xml version="1.0"?>
            <sparql xmlns="http://www.w3.org/2005/sparql-results#">
              <head><variable name="x"/><variable name="p"/></head>
              <results></results>
            </sparql>
            """);

        Assert.IsFalse(result.IsBoolean);
        Assert.HasCount(2, result.Variables);
        Assert.AreEqual("x", result.Variables[0].ToString());
        Assert.AreEqual("p", result.Variables[1].ToString());
    }

    /// <summary>Each binding value form (uri, typed literal, language literal, bnode) parses to the matching RDF term.</summary>
    [TestMethod]
    public void ReadsBindingValueForms()
    {
        SparqlResultSet result = Read(
            """
            <?xml version="1.0"?>
            <sparql xmlns="http://www.w3.org/2005/sparql-results#">
              <head><variable name="iri"/><variable name="num"/><variable name="lang"/><variable name="plain"/><variable name="b"/></head>
              <results>
                <result>
                  <binding name="iri"><uri>http://example/a</uri></binding>
                  <binding name="num"><literal datatype="http://www.w3.org/2001/XMLSchema#integer">42</literal></binding>
                  <binding name="lang"><literal xml:lang="en">hi</literal></binding>
                  <binding name="plain"><literal>plain</literal></binding>
                  <binding name="b"><bnode>b0</bnode></binding>
                </result>
              </results>
            </sparql>
            """);

        Assert.HasCount(1, result.Solutions);
        SparqlSolution solution = result.Solutions[0];

        Assert.AreEqual(new NamedNode(Utf8Strings.From("http://example/a")), Value(solution, "iri"));

        Literal number = (Literal)Value(solution, "num");
        Assert.AreEqual("42", number.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", number.Datatype.Iri.ToString());

        Literal lang = (Literal)Value(solution, "lang");
        Assert.AreEqual("hi", lang.Value.ToString());
        Assert.AreEqual("en", lang.Language?.ToString());
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", lang.Datatype.Iri.ToString());

        Literal plain = (Literal)Value(solution, "plain");
        Assert.AreEqual("plain", plain.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", plain.Datatype.Iri.ToString());

        Assert.AreEqual(new BlankNode(Utf8Strings.From("b0")), Value(solution, "b"));
    }

    /// <summary>A binding omitted from a <c>result</c> row leaves that variable unbound in the solution.</summary>
    [TestMethod]
    public void OmittedBindingIsUnbound()
    {
        SparqlResultSet result = Read(
            """
            <?xml version="1.0"?>
            <sparql xmlns="http://www.w3.org/2005/sparql-results#">
              <head><variable name="x"/><variable name="y"/></head>
              <results>
                <result><binding name="x"><uri>http://example/a</uri></binding></result>
              </results>
            </sparql>
            """);

        SparqlSolution solution = result.Solutions[0];
        Assert.IsTrue(solution.TryGetValue(new SparqlVariable(Utf8Strings.From("x")), out _));
        Assert.IsFalse(solution.TryGetValue(new SparqlVariable(Utf8Strings.From("y")), out _));
    }

    /// <summary>A triple-term binding value parses to a <see cref="TripleTerm"/>.</summary>
    [TestMethod]
    public void ReadsTripleTermBinding()
    {
        SparqlResultSet result = Read(
            """
            <?xml version="1.0"?>
            <sparql xmlns="http://www.w3.org/2005/sparql-results#">
              <head><variable name="t"/></head>
              <results>
                <result>
                  <binding name="t">
                    <triple>
                      <subject><uri>http://example/s</uri></subject>
                      <predicate><uri>http://example/p</uri></predicate>
                      <object><literal>o</literal></object>
                    </triple>
                  </binding>
                </result>
              </results>
            </sparql>
            """);

        TripleTerm triple = (TripleTerm)Value(result.Solutions[0], "t");
        Assert.AreEqual(new NamedNode(Utf8Strings.From("http://example/s")), triple.Subject);
        Assert.AreEqual("http://example/p", triple.Predicate.Iri.ToString());
        Assert.AreEqual("o", ((Literal)triple.Object).Value.ToString());
    }

    /// <summary>A triple-term binding value nested exactly to the cap reads back without throwing.</summary>
    [TestMethod]
    public void ReadsTripleTermNestedToTheLimit()
    {
        SparqlResultSet result = Read(NestedTripleResultsXml(QuotedTripleLimits.MaxNestingDepth));

        Assert.IsInstanceOfType<TripleTerm>(Value(result.Solutions[0], "t"));
    }

    /// <summary>A triple-term binding value nested beyond the cap raises a catchable <see cref="TripleTermDepthLimitException"/> rather than overflowing.</summary>
    [TestMethod]
    public void TripleTermNestedBeyondTheLimitThrows()
    {
        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => Read(NestedTripleResultsXml(QuotedTripleLimits.MaxNestingDepth + 1)));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth + 1, exception.Depth);
        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    /// <summary>Builds a SPARQL Results XML document whose single binding value is a triple term nested <paramref name="depth"/> levels through the object position.</summary>
    /// <param name="depth">The quoted-triple nesting depth.</param>
    /// <returns>The SPARQL Results XML text.</returns>
    private static string NestedTripleResultsXml(int depth)
    {
        StringBuilder builder = new();
        builder.Append("<?xml version=\"1.0\"?><sparql xmlns=\"http://www.w3.org/2005/sparql-results#\"><head><variable name=\"t\"/></head><results><result><binding name=\"t\">");
        for(int i = 0; i < depth; i++)
        {
            builder.Append("<triple><subject><uri>http://e/s</uri></subject><predicate><uri>http://e/p</uri></predicate><object>");
        }

        builder.Append("<uri>http://e/o</uri>");
        for(int i = 0; i < depth; i++)
        {
            builder.Append("</object></triple>");
        }

        builder.Append("</binding></result></results></sparql>");

        return builder.ToString();
    }

    /// <summary>An <c>ASK</c> result parses to its boolean answer.</summary>
    [TestMethod]
    public void ReadsAskBoolean()
    {
        SparqlResultSet trueResult = Read(
            """
            <?xml version="1.0"?>
            <sparql xmlns="http://www.w3.org/2005/sparql-results#"><head/><boolean>true</boolean></sparql>
            """);
        SparqlResultSet falseResult = Read(
            """
            <?xml version="1.0"?>
            <sparql xmlns="http://www.w3.org/2005/sparql-results#"><head/><boolean>false</boolean></sparql>
            """);

        Assert.IsTrue(trueResult.IsBoolean);
        Assert.IsTrue(trueResult.Boolean!.Value);
        Assert.IsFalse(falseResult.Boolean!.Value);
    }
}
