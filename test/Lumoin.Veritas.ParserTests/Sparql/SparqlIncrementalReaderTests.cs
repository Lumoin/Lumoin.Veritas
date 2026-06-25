using System;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Verifies the byte-fed <see cref="SparqlIncrementalReader"/> editor surface: feeding a query in arbitrary chunks
/// (down to one byte at a time, the hardest byte-cut case) produces the identical <see cref="SparqlRequest"/> the
/// whole-buffer <see cref="SparqlParser.ParseRequest(System.ReadOnlyMemory{byte}, Utf8StringPool, Utf8String?)"/>
/// facade produces, that incompleteness is the <see cref="IncrementalParseStatus.NeedMore"/> status (never a throw),
/// and that malformed input is recovered into the result rather than thrown.
/// </summary>
[TestClass]
internal sealed class SparqlIncrementalReaderTests
{
    /// <summary>A star projection over one triple round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void SelectStarMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("SELECT * WHERE { ?s ?p ?o }");
    }

    /// <summary>A query exercising prefixes, a FILTER, ordering, and a limit round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void PrefixedFilterOrderLimitMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("PREFIX e: <http://example.org/> SELECT ?a ?b WHERE { ?a e:knows ?b . FILTER(?a != ?b) } ORDER BY ?a LIMIT 10");
    }

    /// <summary>An ASK over a language-tagged literal round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void AskWithLanguageLiteralMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("ASK WHERE { ?s <http://e/p> \"hello\"@en }");
    }

    /// <summary>A CONSTRUCT round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void ConstructMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("CONSTRUCT { ?s ?p ?o } WHERE { ?s ?p ?o }");
    }

    /// <summary>An aggregate with GROUP BY round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void AggregateGroupByMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("SELECT (COUNT(?x) AS ?c) WHERE { ?x ?p ?o } GROUP BY ?p");
    }

    /// <summary>A unicode \\u escape spanning chunk boundaries (a string with a non-ASCII IRI) round-trips identically.</summary>
    [TestMethod]
    public void UnicodeEscapeMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("SELECT ?x WHERE { ?x <http://e/\\u00E9> ?o }");
    }

    /// <summary>A query that is still being typed reports NeedMore; the lexer/parser never throw mid-input.</summary>
    [TestMethod]
    public void PartialInputReportsNeedMore()
    {
        SparqlIncrementalReader reader = new();
        IncrementalParseStatus status = reader.Feed(Encoding.UTF8.GetBytes("SELECT ?x WHERE {"));

        Assert.AreEqual(IncrementalParseStatus.NeedMore, status, "an unterminated query must report NeedMore, not error");
    }

    /// <summary>Completing a truncated query recovers into a result with diagnostics rather than throwing.</summary>
    [TestMethod]
    public void CompletingTruncatedInputRecovers()
    {
        SparqlIncrementalReader reader = new();
        reader.Feed(Encoding.UTF8.GetBytes("SELECT ?x WHERE { ?x ?p"));
        ParseResult<SparqlRequest> result = reader.Complete();

        Assert.IsTrue(result.HasErrors, "a truncated query must surface diagnostics, recovered not thrown");
        Assert.IsNotNull(result.Tree);
    }

    /// <summary>Feeding after completion is rejected (the OWL incremental-reader contract).</summary>
    [TestMethod]
    public void FeedAfterCompleteThrows()
    {
        SparqlIncrementalReader reader = new();
        reader.Feed(Encoding.UTF8.GetBytes("SELECT * WHERE { ?s ?p ?o }"));
        _ = reader.Complete();

        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Feed(Encoding.UTF8.GetBytes(" ")));
    }

    /// <summary>Feeds a query one byte at a time and asserts the resulting request renders identically to the whole-buffer parse over the same pool — a deep structural render (source spans included), so list-instance identity and interning do not mask a real divergence.</summary>
    /// <param name="query">The SPARQL query text (without anonymous blank nodes, so blank-node labelling does not diverge).</param>
    private static void AssertByteByByteMatchesWholeBuffer(string query)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(query);
        using Utf8StringPool pool = new();
        SparqlRequest whole = SparqlParser.ParseRequest(bytes, pool).Tree;

        SparqlIncrementalReader reader = new(pool);
        for(int i = 0; i < bytes.Length; i++)
        {
            reader.Feed(bytes.AsSpan(i, 1));
        }

        ParseResult<SparqlRequest> incremental = reader.Complete();

        Assert.IsFalse(incremental.HasErrors, "the well-formed query must parse without diagnostics");
        Assert.AreEqual(AstStructuralRenderer.Render(whole), AstStructuralRenderer.Render(incremental.Tree), "byte-by-byte incremental parse must render identically to the whole-buffer parse");
    }
}
