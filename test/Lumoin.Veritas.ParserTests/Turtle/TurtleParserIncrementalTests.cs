using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Emission;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies that the statement-incremental parser — fed one token at a time so every statement is
/// forced to suspend and resume across token boundaries — produces the same statements, in the same
/// order, as the whole-stream parser, and that those statements emit the same quads.
/// </summary>
/// <remarks>
/// Feeding a single token per <see cref="ParseStatus.NeedMore"/> exercises the suspend-resume path at
/// every boundary: a statement that runs out of buffered tokens must pause mid-parse, keep its work
/// stack, and continue once the next token arrives, never re-parsing or unwinding.
/// </remarks>
[TestClass]
internal sealed class TurtleParserIncrementalTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyDocument()
    {
        AssertIncrementalMatchesWhole(string.Empty, TurtleSyntax.Turtle);
    }

    [TestMethod]
    public void DirectivesAndMultiPredicateTriples()
    {
        AssertIncrementalMatchesWhole(
            "@prefix ex: <http://example.org/> .\n"
            + "@base <http://example.org/base/> .\n"
            + "PREFIX p: <http://other.example/>\n"
            + "ex:alice ex:knows ex:bob ; ex:age 42 .\n"
            + "ex:bob ex:name \"Bob\"@en , \"Bobby\" ; p:flag true .\n",
            TurtleSyntax.Turtle);
    }

    [TestMethod]
    public void CollectionsAndBlankNodePropertyLists()
    {
        AssertIncrementalMatchesWhole(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:s ex:p ( ex:a ex:b ex:c ) .\n"
            + "ex:s2 ex:q [ ex:r ex:o ; ex:r2 -3.0e-4 ] .\n"
            + "[ ex:lonely ex:value ] .\n",
            TurtleSyntax.Turtle);
    }

    [TestMethod]
    public void ReifiedTriplesTripleTermsAndAnnotations()
    {
        AssertIncrementalMatchesWhole(
            "<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>> .\n"
            + "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> ~ <http://example.org/r> >> .\n"
            + "<http://example.org/s> <http://example.org/p> <http://example.org/o> {| <http://example.org/m> <http://example.org/v> |} .\n"
            + "<http://example.org/s> <http://example.org/p> <http://example.org/o> ~ <http://example.org/r2> .\n",
            TurtleSyntax.Turtle);
    }

    [TestMethod]
    public void TrigGraphBlocks()
    {
        AssertIncrementalMatchesWhole(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:default ex:p ex:o .\n"
            + "<http://example.org/g> { ex:s ex:p ex:o . ex:s2 ex:p2 ex:o2 }\n"
            + "GRAPH ex:g2 { ex:s3 ex:p3 ex:o3 . }\n"
            + "{ ex:s4 ex:p4 ex:o4 . }\n",
            TurtleSyntax.TriG);
    }

    private static void AssertIncrementalMatchesWhole(string source, TurtleSyntax syntax)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);

        //One pool backs both parsers and both emitters, so equal byte sequences intern to the same
        //Utf8String and the resulting quads compare by value.
        using Utf8StringPool pool = new();
        DocumentId documentId = new(1);

        TurtleLexer lexer = new(bytes, pool);
        List<TurtleToken> tokens = [];
        foreach(TurtleToken token in lexer.Tokenize())
        {
            tokens.Add(token);
        }

        TurtleDocument whole = new TurtleParser(tokens, pool, documentId, syntax).Parse();
        List<Statement> produced = DriveOneTokenAtATime(tokens, pool, documentId, syntax);

        //The incremental parser resets node ids per statement (statement-local, bounded memory), so
        //node ids are not compared to the whole-stream parser's monotonic ids; type, span, and the
        //emitted quads are what must match.
        Assert.HasCount(whole.Statements.Length, produced, "statement count differs");
        for(int i = 0; i < produced.Count; i++)
        {
            string where = string.Create(CultureInfo.InvariantCulture, $"statement {i}");

            Assert.AreEqual(whole.Statements[i].GetType(), produced[i].GetType(), where);
            Assert.AreEqual(whole.Statements[i].Span, produced[i].Span, where);
        }

        List<Quad> expected = EmitWhole(whole, pool);
        List<Quad> actual = EmitPerStatement(whole, produced, pool);

        Assert.HasCount(expected.Count, actual, "quad count differs");
        for(int i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i], actual[i], string.Create(CultureInfo.InvariantCulture, $"quad {i}"));
        }
    }

    private static List<Statement> DriveOneTokenAtATime(List<TurtleToken> tokens, Utf8StringPool pool, DocumentId documentId, TurtleSyntax syntax)
    {
        TurtleParser parser = new(pool, documentId, syntax);
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

        return produced;
    }

    private static List<Quad> EmitWhole(TurtleDocument document, Utf8StringPool pool)
    {
        TurtleQuadEmitter emitter = new(document, pool, new DiagnosticBag());
        List<Quad> quads = [];
        foreach(EmittedQuad emitted in emitter.Emit())
        {
            quads.Add(emitted.Quad);
        }

        return quads;
    }

    private static List<Quad> EmitPerStatement(TurtleDocument document, List<Statement> statements, Utf8StringPool pool)
    {
        //Feeding statements one at a time to EmitStatement reproduces the whole-document emit: the
        //emitter carries the prefix and base context forward, and only the document identity is read
        //from the document itself.
        TurtleQuadEmitter emitter = new(document, pool, new DiagnosticBag());
        List<Quad> quads = [];
        foreach(Statement statement in statements)
        {
            foreach(EmittedQuad emitted in emitter.EmitStatement(statement))
            {
                quads.Add(emitted.Quad);
            }
        }

        return quads;
    }
}
