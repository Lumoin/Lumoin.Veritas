using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Verifies the SPARQL parser's value-based error recovery: a malformed construct records a diagnostic
/// (the expected <c>SP####</c> code at a real span) and the parser resynchronises and keeps producing a
/// request rather than throwing, including under incremental ("as if typed") feeding.
/// </summary>
/// <remarks>
/// Recovery is local to the broken frame: an error node slots into the position its product base
/// expected, so the surrounding query still assembles. Cascade-suppression is checked by bounding the
/// diagnostic count for one unclosed group full of stray tokens.
/// </remarks>
[TestClass]
internal sealed class SparqlParserRecoveryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ReportsMissingQueryForm()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("WHERE { ?s ?p ?o }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.ExpectedQueryForm));
    }

    [TestMethod]
    public void RecoversFromUnclosedGroupGraphPattern()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p ?o", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.UnclosedGroupGraphPattern));
    }

    [TestMethod]
    public void RecoversFromUnclosedCollection()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p ( ?a ?b }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.UnclosedCollection));
    }

    [TestMethod]
    public void RecoversFromUnclosedBlankNodePropertyList()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p [ ?x ?y }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.UnclosedBlankNodePropertyList));
    }

    [TestMethod]
    public void RecoversFromMissingObjectThenContinues()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p . ?a ?b ?c }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsGreaterThanOrEqualTo(1, CountTriples((SparqlQuery)result.Tree), "the trailing valid triple should still parse after recovery");
    }

    [TestMethod]
    public void RecoversFromStrayTokenAtMemberPositionThenContinues()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ) ?a ?b ?c }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsGreaterThanOrEqualTo(1, CountTriples((SparqlQuery)result.Tree), "the valid triple after a stray token should still parse");
    }

    [TestMethod]
    public void ReportsUnboundPrefix()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s foo:bar ?o }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.UnboundPrefix));
    }

    [TestMethod]
    public void CascadeStaysBoundedForStrayTokenRun()
    {
        //A run of stray member-position tokens before the group closes is reported once (the group's
        //skip loop), not once per token — the diagnostic count stays bounded regardless of run length.
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ) ) ) ) ) }", pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsLessThanOrEqualTo(4, result.Diagnostics.Count, $"expected a bounded diagnostic count, got {result.Diagnostics.Count}");
    }

    [TestMethod]
    public void EveryDiagnosticCarriesARealSpan()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p ( ?a ?b }", pool);

        Assert.IsTrue(result.HasErrors);
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            Assert.AreNotEqual(SourceSpan.None, diagnostic.Span, "a diagnostic should point at the offending input");
        }
    }

    [TestMethod]
    public void ReactiveEditorErrorNodeUnderIncrementalFeed()
    {
        //Feed tokens one at a time, as an editor would as the user types: a malformed query must still
        //produce a (possibly error-node-carrying) request and surface the diagnostic, never throw or hang.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        List<SparqlToken> tokens = [.. new SparqlLexer(Encoding.UTF8.GetBytes("SELECT * WHERE { ?s ?p ( ?a ?b }"), pool).Tokenize()];

        SparqlParser parser = new(pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        SparqlRequest? request = null;
        int next = 0;
        while(true)
        {
            ParseStatus status = parser.TryParseRequest(out request);
            if(status == ParseStatus.Produced)
            {
                break;
            }

            if(next >= tokens.Count)
            {
                Assert.Fail("the parser asked for more tokens than the stream holds");
            }

            parser.FeedToken(tokens[next]);
            next++;
        }

        Assert.IsNotNull(request);
        Assert.IsTrue(diagnostics.HasErrors, "the malformed query should have recorded a diagnostic");
        Assert.IsTrue(ContainsCode(diagnostics, WellKnownDiagnostics.Sparql.UnclosedCollection));
    }

    [TestMethod]
    public void IncompleteInputRecordsNoDiagnosticUntilComplete()
    {
        //An editor feeds tokens as the user types. A well-formed query that is merely unfinished must
        //never record a diagnostic mid-stream — nothing to squiggle yet. The parser suspends (NeedMore)
        //until enough has arrived, then produces with an empty bag: incomplete is not the same as wrong.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        List<SparqlToken> tokens = [.. new SparqlLexer(Encoding.UTF8.GetBytes("SELECT * WHERE { ?s ?p ?o }"), pool).Tokenize()];

        SparqlParser parser = new(pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        SparqlRequest? request = null;
        int next = 0;
        while(true)
        {
            ParseStatus status = parser.TryParseRequest(out request);

            Assert.IsFalse(diagnostics.HasErrors, "an unfinished but well-formed query must not record a diagnostic");

            if(status == ParseStatus.Produced)
            {
                break;
            }

            if(next >= tokens.Count)
            {
                Assert.Fail("the parser asked for more tokens than the stream holds");
            }

            parser.FeedToken(tokens[next]);
            next++;
        }

        Assert.IsNotNull(request);
        Assert.IsFalse(diagnostics.HasErrors);
    }

    [TestMethod]
    public void DiagnosticSquigglesTheOffendingTokenAndNodeSpansTheRun()
    {
        //The faulty token gets the tight squiggle — the diagnostic span is exactly that token — while
        //the error node spans the failure-to-resync run it stands in for, so an editor can both underline
        //the word and select the broken construct.
        byte[] bytes = Encoding.UTF8.GetBytes("SELECT * WHERE { ?s ?p ) }");
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = SparqlParser.ParseRequest(new System.ReadOnlyMemory<byte>(bytes), pool);

        Assert.IsTrue(result.HasErrors);

        Diagnostic term = FindDiagnostic(result, WellKnownDiagnostics.Sparql.ExpectedTerm);
        Assert.AreEqual(")", Slice(bytes, term.Span), "the diagnostic should squiggle exactly the offending token");

        TriplePattern triple = FirstTriple((SparqlQuery)result.Tree);
        Assert.IsInstanceOfType<ErrorTriplePatternTerm>(triple.Object);
        ErrorTriplePatternTerm errorTerm = (ErrorTriplePatternTerm)triple.Object;
        Assert.AreEqual(term.Span.StartByte, errorTerm.Span.StartByte, "the error node should anchor at the faulty token");
    }

    [TestMethod]
    public void ThrowIfInvalidThrowsCarryingDiagnostics()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p }", pool);

        Assert.IsTrue(result.HasErrors);

        SparqlInvalidRequestException exception = Assert.Throws<SparqlInvalidRequestException>(
            () => SparqlInvalidRequestException.ThrowIfInvalid(result));

        Assert.IsNotEmpty(exception.Diagnostics);
    }

    [TestMethod]
    public void ThrowIfInvalidDoesNotThrowOnCleanRequest()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse("SELECT * WHERE { ?s ?p ?o }", pool);

        Assert.IsFalse(result.HasErrors);

        //A clean parse is executable; the guard must not throw.
        SparqlInvalidRequestException.ThrowIfInvalid(result);
    }

    [TestMethod]
    public void MaxDiagnosticsCapsParserDiagnostics()
    {
        //Three malformed objects would record three diagnostics; a cap of two records two plus the
        //ExcessDiagnostics marker and suppresses the rest, bounding a runaway-error parse.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        List<SparqlToken> tokens = [.. new SparqlLexer(Encoding.UTF8.GetBytes("SELECT * WHERE { ?s ?p ) . ?a ?b ) . ?c ?d ) }"), pool).Tokenize()];
        SparqlParser parser = new(tokens, pool, baseIri: null, blankNodes: null, diagnostics: diagnostics, maxDiagnostics: 2);
        _ = parser.ParseToResult();

        int realErrors = 0;
        bool sawExcess = false;
        foreach(Diagnostic diagnostic in diagnostics.Diagnostics)
        {
            if(diagnostic.Code.Equals(WellKnownDiagnostics.Sparql.ExcessDiagnostics))
            {
                sawExcess = true;
            }
            else
            {
                realErrors++;
            }
        }

        Assert.IsLessThanOrEqualTo(2, realErrors, "parser diagnostics should be capped at MaxDiagnostics");
        Assert.IsTrue(sawExcess, "the ExcessDiagnostics marker should be recorded once the cap is reached");
    }

    [TestMethod]
    public void QuotedTripleAtTheNestingLimitParsesWithoutADepthDiagnostic()
    {
        //A quoted triple nested exactly to the cap is legal: the parser must assemble it without the
        //nesting diagnostic (strict '>' means depth MaxNestingDepth is allowed and only a deeper one trips).
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse(NestedTripleTermQuery(QuotedTripleLimits.MaxNestingDepth), pool);

        Assert.IsFalse(result.HasErrors, "a quoted triple nested exactly to the limit is legal and must parse with no diagnostics");
        Assert.IsFalse(HasCode(result, WellKnownDiagnostics.Sparql.QuotedTripleNestingTooDeep), "a quoted triple nested to the limit must parse without the nesting diagnostic");
    }

    [TestMethod]
    public void QuotedTripleBeyondTheNestingLimitRecordsTheNestingDiagnostic()
    {
        //One level past the cap collapses the over-deep triple term to an error node and records the
        //recoverable nesting diagnostic rather than throwing.
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse(NestedTripleTermQuery(QuotedTripleLimits.MaxNestingDepth + 1), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.QuotedTripleNestingTooDeep), "a quoted triple nested beyond the limit must record the nesting diagnostic and recover");
    }

    [TestMethod]
    public void DeeplyNestedQuotedTripleRecoversRatherThanOverflowing()
    {
        //A pathologically deep quoted triple must surface the nesting diagnostic and resynchronise, never
        //exhaust the stack: the iterative parser bounds the work stack one frame past the cap.
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse(NestedTripleTermQuery(4000), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Sparql.QuotedTripleNestingTooDeep));
    }

    [TestMethod]
    public void DeeplyNestedCollectionIsNotCappedByTheQuotedTripleLimit()
    {
        //Collections are list-bearing AST nodes whose equality compares the item list by reference, so deep
        //nesting cannot overflow; they parse iteratively to any depth and are not subject to the quoted-triple
        //nesting cap (only triple terms and reified triples, whose AST embeds a record member, are bounded).
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> result = Parse(NestedCollectionQuery(QuotedTripleLimits.MaxNestingDepth + 200), pool);

        Assert.IsFalse(result.HasErrors, "a deeply nested collection is valid and must parse without errors");
        Assert.IsFalse(HasCode(result, WellKnownDiagnostics.Sparql.QuotedTripleNestingTooDeep), "a deeply nested collection must not trigger the quoted-triple nesting cap");
    }

    private static string NestedTripleTermQuery(int depth)
    {
        StringBuilder builder = new("PREFIX : <http://e/> SELECT * WHERE { :s :p ");
        for(int i = 0; i < depth; i++)
        {
            builder.Append("<<( :a :b ");
        }

        builder.Append(":o");
        for(int i = 0; i < depth; i++)
        {
            builder.Append(" )>>");
        }

        builder.Append(" }");

        return builder.ToString();
    }

    private static string NestedCollectionQuery(int depth)
    {
        StringBuilder builder = new("PREFIX : <http://e/> SELECT * WHERE { :s :p ");
        for(int i = 0; i < depth; i++)
        {
            builder.Append('(');
        }

        builder.Append(" :o ");
        for(int i = 0; i < depth; i++)
        {
            builder.Append(')');
        }

        builder.Append(" }");

        return builder.ToString();
    }

    private static ParseResult<SparqlRequest> Parse(string text, Utf8StringPool pool)
    {
        return SparqlParser.ParseRequest(new System.ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text)), pool);
    }

    private static TriplePattern FirstTriple(SparqlQuery query)
    {
        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;

        return ((BasicGraphPatternBlock)group.Members[0]).Triples[0];
    }

    private static Diagnostic FindDiagnostic(ParseResult<SparqlRequest> result, Utf8String code)
    {
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            if(diagnostic.Code.Equals(code))
            {
                return diagnostic;
            }
        }

        Assert.Fail($"Expected a diagnostic with code '{code}', but none was recorded.");

        return default;
    }

    private static string Slice(byte[] bytes, SourceSpan span)
    {
        return Encoding.UTF8.GetString(bytes, (int)span.StartByte, (int)(span.EndByte - span.StartByte));
    }

    private static int CountTriples(SparqlQuery query)
    {
        int count = 0;
        if(query.Where.Pattern is GroupGraphPattern group)
        {
            foreach(GraphPattern member in group.Members)
            {
                if(member is BasicGraphPatternBlock block)
                {
                    count += block.Triples.Count;
                }
            }
        }

        return count;
    }

    private static bool HasCode(ParseResult<SparqlRequest> result, Utf8String code)
    {
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            if(diagnostic.Code.Equals(code))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCode(DiagnosticBag diagnostics, Utf8String code)
    {
        foreach(Diagnostic diagnostic in diagnostics.Diagnostics)
        {
            if(diagnostic.Code.Equals(code))
            {
                return true;
            }
        }

        return false;
    }
}
