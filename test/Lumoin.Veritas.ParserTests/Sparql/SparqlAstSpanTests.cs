using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Pins the source spans threaded onto the SPARQL AST's grammar-production nodes: each node's
/// <c>Span</c> must slice the exact source substring of the construct it stands for. These are the
/// correctness gate for "Slice S" — they assert real substrings, not merely that a span is present,
/// so a span that points at the wrong extent fails.
/// </summary>
[TestClass]
internal sealed class SparqlAstSpanTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A SELECT with an OPTIONAL and a trailing ORDER BY spans its form head, group graph pattern,
    /// optional member, order clause and condition, and the whole query to the exact source slices.
    /// </summary>
    [TestMethod]
    public void SelectWithOptionalAndOrderSpansEachNode()
    {
        const string text = "PREFIX : <http://e/> SELECT ?s WHERE { ?s :p ?o . OPTIONAL { ?s :q ?r } } ORDER BY ?s";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(text, pool);

        Assert.AreEqual(text, Slice(bytes, query.Span));

        SelectQuery select = (SelectQuery)query.Form;
        Assert.AreEqual("SELECT ?s", Slice(bytes, select.Span));

        Assert.AreEqual("PREFIX : <http://e/>", Slice(bytes, query.Prologue.Span));

        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        Assert.AreEqual("{ ?s :p ?o . OPTIONAL { ?s :q ?r } }", Slice(bytes, group.Span));

        OptionalPattern optional = FindOptional(group);
        Assert.AreEqual("OPTIONAL { ?s :q ?r }", Slice(bytes, optional.Span));

        Assert.IsNotNull(query.Modifier.Order);
        OrderClause order = query.Modifier.Order!;
        Assert.AreEqual("ORDER BY ?s", Slice(bytes, order.Span));

        Assert.HasCount(1, order.Conditions);
        OrderAscending condition = (OrderAscending)order.Conditions[0];
        Assert.AreEqual("?s", Slice(bytes, condition.Span));
    }

    /// <summary>
    /// A SELECT carrying a FILTER spans the filter member and the comparison expression inside it to the
    /// exact source slices.
    /// </summary>
    [TestMethod]
    public void SelectWithFilterSpansFilterAndComparison()
    {
        const string text = "PREFIX : <http://e/> SELECT ?s WHERE { ?s :p ?o FILTER(?o > 5) }";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(text, pool);

        Assert.AreEqual(text, Slice(bytes, query.Span));

        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        FilterPattern filter = FindFilter(group);
        Assert.AreEqual("FILTER(?o > 5)", Slice(bytes, filter.Span));

        ComparisonExpression comparison = (ComparisonExpression)filter.Expression;
        Assert.AreEqual("?o > 5", Slice(bytes, comparison.Span));
        Assert.AreEqual("?o", Slice(bytes, comparison.Left.Span));
        Assert.AreEqual("5", Slice(bytes, comparison.Right.Span));
    }

    /// <summary>The whole-query span of a bare ASK covers the entire request to the exact source slice.</summary>
    [TestMethod]
    public void AskQuerySpansFormAndWhole()
    {
        const string text = "ASK { ?s ?p ?o }";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(text, pool);

        Assert.AreEqual(text, Slice(bytes, query.Span));
        Assert.AreEqual("ASK", Slice(bytes, query.Form.Span));

        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        Assert.AreEqual("{ ?s ?p ?o }", Slice(bytes, group.Span));
    }

    /// <summary>Finds the single <see cref="OptionalPattern"/> member of a group graph pattern.</summary>
    /// <param name="group">The group graph pattern to search.</param>
    /// <returns>The optional member.</returns>
    private static OptionalPattern FindOptional(GroupGraphPattern group)
    {
        foreach(GraphPattern member in group.Members)
        {
            if(member is OptionalPattern optional)
            {
                return optional;
            }
        }

        Assert.Fail("The group graph pattern has no OPTIONAL member.");

        return null!;
    }

    /// <summary>Finds the single <see cref="FilterPattern"/> member of a group graph pattern.</summary>
    /// <param name="group">The group graph pattern to search.</param>
    /// <returns>The filter member.</returns>
    private static FilterPattern FindFilter(GroupGraphPattern group)
    {
        foreach(GraphPattern member in group.Members)
        {
            if(member is FilterPattern filter)
            {
                return filter;
            }
        }

        Assert.Fail("The group graph pattern has no FILTER member.");

        return null!;
    }

    /// <summary>Returns the UTF-8 source substring a span covers.</summary>
    /// <param name="bytes">The UTF-8 source bytes.</param>
    /// <param name="span">The span to slice.</param>
    /// <returns>The decoded substring.</returns>
    private static string Slice(byte[] bytes, SourceSpan span)
    {
        return Encoding.UTF8.GetString(bytes, (int)span.StartByte, (int)(span.EndByte - span.StartByte));
    }

    /// <summary>Lexes and parses a query into its <see cref="SparqlQuery"/> AST.</summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping the parsed handles alive.</param>
    /// <returns>The parsed query.</returns>
    private static SparqlQuery ParseQuery(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);

        return (SparqlQuery)parser.ParseRequest();
    }
}
