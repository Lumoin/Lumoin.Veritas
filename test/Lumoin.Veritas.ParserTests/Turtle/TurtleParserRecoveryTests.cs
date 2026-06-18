using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies the Turtle parser's value-based error recovery: a malformed construct records a diagnostic
/// (the expected <c>TT####</c> code at a real span) and the parser resynchronises and keeps producing
/// statements rather than throwing, including under incremental ("as if typed") feeding.
/// </summary>
/// <remarks>
/// Each whole-buffer case pairs a malformed construct with a trailing well-formed triple
/// (<c>&lt;http://e/ok&gt; …</c>) and asserts both that the diagnostic fired and that the clean triple
/// still reached the document — recovery is local to the broken frame and the work stack always
/// progresses. Cascade-suppression is checked by bounding the diagnostic count for one unclosed bracket.
/// </remarks>
[TestClass]
internal sealed class TurtleParserRecoveryTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string TrailingOk = "<http://e/ok> <http://e/p> <http://e/o> .";

    [TestMethod]
    public void RecoversFromUnclosedCollectionAndContinues()
    {
        ParseResult<TurtleDocument> result = Parse($"<http://e/s> <http://e/p> ( <http://e/a> <http://e/b> .\n{TrailingOk}");

        AssertRecovered(result, WellKnownDiagnostics.Turtle.UnclosedCollection);
    }

    [TestMethod]
    public void RecoversFromMissingObjectInBlankNodeListAndContinues()
    {
        ParseResult<TurtleDocument> result = Parse($"<http://e/s> <http://e/p> [ <http://e/r> ] .\n{TrailingOk}");

        AssertRecovered(result, WellKnownDiagnostics.Turtle.ExpectedTerm);
    }

    [TestMethod]
    public void RecoversFromUnclosedTripleTermAndContinues()
    {
        ParseResult<TurtleDocument> result = Parse($"<http://e/s> <http://e/p> <<( <http://e/a> <http://e/b> <http://e/c> .\n{TrailingOk}");

        AssertRecovered(result, WellKnownDiagnostics.Turtle.UnclosedTripleTerm);
    }

    [TestMethod]
    public void RecoversFromEmptyAnnotationBlockAndContinues()
    {
        ParseResult<TurtleDocument> result = Parse($"<http://e/s> <http://e/p> <http://e/o> {{| |}} .\n{TrailingOk}");

        AssertRecovered(result, WellKnownDiagnostics.Turtle.EmptyAnnotationBlock);
    }

    [TestMethod]
    public void RecoversFromGraphBlockInPlainTurtleAndContinues()
    {
        ParseResult<TurtleDocument> result = Parse($"<http://e/g> {{ <http://e/s> <http://e/p> <http://e/o> . }}\n{TrailingOk}");

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(ContainsTripleWithSubject(result.Tree, "http://e/ok"), "the trailing clean triple should still parse");
    }

    [TestMethod]
    public void RecoversFromStrayClosingBraceAtTopLevelAndContinues()
    {
        ParseResult<TurtleDocument> result = Parse($"}}\n{TrailingOk}");

        AssertRecovered(result, WellKnownDiagnostics.Turtle.ExpectedTerm);
    }

    [TestMethod]
    public void ReportsMissingPrefixNamespace()
    {
        ParseResult<TurtleDocument> result = Parse("@prefix <http://e/> .");

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Turtle.ExpectedPrefixNamespace));
    }

    [TestMethod]
    public void ReportsMissingStatementTerminatorAndKeepsTriple()
    {
        ParseResult<TurtleDocument> result = Parse("<http://e/ok> <http://e/p> <http://e/o>");

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Turtle.ExpectedDot));
        Assert.IsTrue(ContainsTripleWithSubject(result.Tree, "http://e/ok"), "a triple missing only its terminator should still parse");
    }

    [TestMethod]
    public void CascadeStaysBoundedForUnclosedBracket()
    {
        //One unclosed '[' running to end of input must not emit a diagnostic per skipped token — the
        //structural recovery points cap the count regardless of how many items are inside.
        ParseResult<TurtleDocument> result = Parse("<http://e/s> <http://e/p> [ <http://e/a> <http://e/b> <http://e/c> <http://e/d> <http://e/e>");

        Assert.IsTrue(result.HasErrors);
        Assert.IsLessThanOrEqualTo(5, result.Diagnostics.Count, $"expected a bounded diagnostic count, got {result.Diagnostics.Count}");
    }

    [TestMethod]
    public void EveryDiagnosticCarriesARealSpan()
    {
        ParseResult<TurtleDocument> result = Parse("<http://e/s> <http://e/p> ( <http://e/a> .");

        Assert.IsTrue(result.HasErrors);
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            Assert.AreNotEqual(SourceSpan.None, diagnostic.Span, "a diagnostic should point at the offending input");
        }
    }

    [TestMethod]
    public async Task EmitterReportsUndeclaredPrefix()
    {
        //The emitter is the layer that expands prefixed names, so an undeclared prefix surfaces while
        //the quad stream is consumed — recorded into the same bag, never thrown.
        (List<Quad> quads, DiagnosticBag diagnostics) = await ReadAsync("ex:s ex:p ex:o .").ConfigureAwait(false);

        Assert.IsEmpty(quads);
        Assert.IsTrue(diagnostics.HasErrors);
        Assert.IsTrue(ContainsCode(diagnostics, WellKnownDiagnostics.Turtle.UndeclaredPrefix));
    }

    [TestMethod]
    public void ReactiveEditorErrorNodeThenRecovery()
    {
        //Feed tokens one at a time, as an editor would as the user types: a malformed first statement
        //must yield an error node + diagnostic at a real span, and a following well-formed statement fed
        //afterward must still parse cleanly — the parser recovers and keeps producing.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes($"<http://e/s> <http://e/p> ( <http://e/a> .\n{TrailingOk}"), pool);
        List<TurtleToken> tokens = [.. lexer.Tokenize()];

        TurtleParser parser = new(pool, default, TurtleSyntax.Turtle, blankNodes: null, diagnostics: diagnostics);
        List<Statement> produced = [];
        int next = 0;
        while(true)
        {
            ParseStatus status = parser.TryParseStatement(out Statement? statement);
            if(status == ParseStatus.Produced)
            {
                produced.Add(statement!);

                continue;
            }

            if(status == ParseStatus.Completed)
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

        Assert.IsTrue(diagnostics.HasErrors, "the malformed statement should have recorded a diagnostic");
        Assert.IsTrue(ContainsCode(diagnostics, WellKnownDiagnostics.Turtle.UnclosedCollection));
        Assert.IsTrue(ContainsTripleWithSubject(produced, "http://e/ok"), "the parser should recover and produce the clean trailing triple");
    }

    [TestMethod]
    public void IncompleteInputRecordsNoDiagnosticUntilComplete()
    {
        //An editor feeds tokens as the user types. A well-formed statement that is merely unfinished must
        //never record a diagnostic mid-stream — there is nothing to squiggle yet. The parser suspends
        //(NeedMore) until the terminator arrives, then produces with an empty bag. This pins the editor
        //rule that incomplete is not the same as wrong.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        List<TurtleToken> tokens = [.. new TurtleLexer(Encoding.UTF8.GetBytes("<http://e/s> <http://e/p> <http://e/o> ."), pool).Tokenize()];

        TurtleParser parser = new(pool, default, TurtleSyntax.Turtle, blankNodes: null, diagnostics: diagnostics);
        int produced = 0;
        int next = 0;
        while(true)
        {
            ParseStatus status = parser.TryParseStatement(out Statement? statement);

            Assert.IsFalse(diagnostics.HasErrors, "an unfinished but well-formed statement must not record a diagnostic");

            if(status == ParseStatus.Produced)
            {
                _ = statement;
                produced++;

                continue;
            }

            if(status == ParseStatus.Completed)
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

        Assert.AreEqual(1, produced);
        Assert.IsFalse(diagnostics.HasErrors);
    }

    [TestMethod]
    public void DiagnosticSquigglesTheOffendingTokenAndNodeSpansTheRun()
    {
        //The faulty token gets the tight squiggle — the diagnostic span is exactly that token — while
        //the error node spans the whole failure-to-resync run it stands in for, so an editor can both
        //underline the word and select the broken construct.
        byte[] bytes = Encoding.UTF8.GetBytes("<http://e/s> 42 <http://e/o> .");
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(bytes, pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), TurtleSyntax.Turtle);
        ParseResult<TurtleDocument> result = parser.ParseToResult();

        Assert.IsTrue(result.HasErrors);

        Diagnostic predicate = FindDiagnostic(result, WellKnownDiagnostics.Turtle.ExpectedPredicate);
        Assert.AreEqual("42", Slice(bytes, predicate.Span), "the diagnostic should squiggle exactly the offending token");

        TripleStatement triple = (TripleStatement)result.Tree.Statements[0];
        Assert.IsInstanceOfType<ErrorTerm>(triple.Predicates[0].Predicate);
        ErrorTerm errorTerm = (ErrorTerm)triple.Predicates[0].Predicate;
        Assert.AreEqual(predicate.Span.StartByte, errorTerm.Span.StartByte, "the error node should anchor at the faulty token");
        Assert.IsGreaterThanOrEqualTo(1, errorTerm.SkippedTokens.Length, "the error node should record the skipped run");
    }

    private static Diagnostic FindDiagnostic(ParseResult<TurtleDocument> result, Utf8String code)
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

    private static void AssertRecovered(ParseResult<TurtleDocument> result, Utf8String expectedCode)
    {
        Assert.IsTrue(result.HasErrors, "recovery should have recorded an error diagnostic");
        Assert.IsTrue(HasCode(result, expectedCode), "the expected diagnostic code should be present");
        Assert.IsTrue(ContainsTripleWithSubject(result.Tree, "http://e/ok"), "the parser should resynchronise and parse the trailing clean triple");
    }

    private static ParseResult<TurtleDocument> Parse(string source)
    {
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), TurtleSyntax.Turtle);

        return parser.ParseToResult();
    }

    private static async Task<(List<Quad> Quads, DiagnosticBag Diagnostics)> ReadAsync(string source)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(
            new System.ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(source)),
            TurtleSyntax.Turtle,
            diagnostics,
            pool: null,
            baseIri: null,
            cancellationToken: CancellationToken.None).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return (quads, diagnostics);
    }

    private static bool HasCode(ParseResult<TurtleDocument> result, Utf8String code)
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

    private static bool ContainsTripleWithSubject(TurtleDocument document, string subjectIri)
    {
        return ContainsTripleWithSubject(document.Statements, subjectIri);
    }

    private static bool ContainsTripleWithSubject(IReadOnlyList<Statement> statements, string subjectIri)
    {
        foreach(Statement statement in statements)
        {
            if(statement is TripleStatement triple && triple.Subject is IriTerm iri && iri.Value.ToString() == subjectIri)
            {
                return true;
            }
        }

        return false;
    }
}
