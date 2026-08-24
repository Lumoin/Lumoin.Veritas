using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class TurtleLexerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void LexAbsoluteIri()
    {
        List<TurtleToken> tokens = LexAll("<http://example.org/foo>");

        Assert.HasCount(2, tokens);
        Assert.AreEqual(TurtleTokenKind.Iri, tokens[0].Kind);
        Assert.AreEqual("http://example.org/foo", tokens[0].Value.ToString());
        Assert.AreEqual(TurtleTokenKind.EndOfInput, tokens[1].Kind);
    }

    [TestMethod]
    public void LexPrefixedName()
    {
        List<TurtleToken> tokens = LexAll("foaf:name");

        Assert.HasCount(2, tokens);
        Assert.AreEqual(TurtleTokenKind.PrefixedName, tokens[0].Kind);
        Assert.AreEqual("foaf:name", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexDefaultPrefixedName()
    {
        List<TurtleToken> tokens = LexAll(":local");

        Assert.HasCount(2, tokens);
        Assert.AreEqual(TurtleTokenKind.PrefixedName, tokens[0].Kind);
        Assert.AreEqual(":local", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexBlankNodeLabel()
    {
        List<TurtleToken> tokens = LexAll("_:b0");

        Assert.AreEqual(TurtleTokenKind.BlankNodeLabel, tokens[0].Kind);
        Assert.AreEqual("b0", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexShortStringLiteral()
    {
        List<TurtleToken> tokens = LexAll("\"hello\"");

        Assert.AreEqual(TurtleTokenKind.StringLiteral, tokens[0].Kind);
        Assert.AreEqual("hello", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexShortStringWithEscapes()
    {
        List<TurtleToken> tokens = LexAll(@"""he said \""hi\"" \\""");

        Assert.AreEqual(TurtleTokenKind.StringLiteral, tokens[0].Kind);
        Assert.AreEqual("he said \"hi\" \\", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexLongStringLiteral()
    {
        List<TurtleToken> tokens = LexAll("\"\"\"multi\nline\"\"\"");

        Assert.AreEqual(TurtleTokenKind.LongStringLiteral, tokens[0].Kind);
        Assert.AreEqual("multi\nline", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexUnicodeShortEscape()
    {
        List<TurtleToken> tokens = LexAll("\"\\u0041\"");

        Assert.AreEqual("A", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexIntegerLiteral()
    {
        List<TurtleToken> tokens = LexAll("42");

        Assert.AreEqual(TurtleTokenKind.IntegerLiteral, tokens[0].Kind);
        Assert.AreEqual("42", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexDecimalLiteral()
    {
        List<TurtleToken> tokens = LexAll("1.5");

        Assert.AreEqual(TurtleTokenKind.DecimalLiteral, tokens[0].Kind);
        Assert.AreEqual("1.5", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexDoubleLiteral()
    {
        List<TurtleToken> tokens = LexAll("1.5e10");

        Assert.AreEqual(TurtleTokenKind.DoubleLiteral, tokens[0].Kind);
    }

    [TestMethod]
    public void LexLanguageTag()
    {
        List<TurtleToken> tokens = LexAll("@en");

        Assert.AreEqual(TurtleTokenKind.LangTag, tokens[0].Kind);
        Assert.AreEqual("en", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexLanguageTagWithSubtag()
    {
        List<TurtleToken> tokens = LexAll("@en-GB");

        Assert.AreEqual(TurtleTokenKind.LangTag, tokens[0].Kind);
        Assert.AreEqual("en-GB", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexDirLangTag()
    {
        List<TurtleToken> tokens = LexAll("@en--ltr");

        Assert.AreEqual(TurtleTokenKind.DirLangTag, tokens[0].Kind);
        Assert.AreEqual("en--ltr", tokens[0].Value.ToString());
    }

    [TestMethod]
    public void LexTypeMarker()
    {
        List<TurtleToken> tokens = LexAll("^^<http://example.org/dt>");

        Assert.AreEqual(TurtleTokenKind.TypeMarker, tokens[0].Kind);
        Assert.AreEqual(TurtleTokenKind.Iri, tokens[1].Kind);
    }

    [TestMethod]
    public void LexReifiedTripleDelimiters()
    {
        List<TurtleToken> tokens = LexAll("<< >>");

        Assert.AreEqual(TurtleTokenKind.OpenReifiedTriple, tokens[0].Kind);
        Assert.AreEqual(TurtleTokenKind.CloseReifiedTriple, tokens[1].Kind);
    }

    [TestMethod]
    public void LexTripleTermDelimiters()
    {
        List<TurtleToken> tokens = LexAll("<<( )>>");

        Assert.AreEqual(TurtleTokenKind.OpenTripleTerm, tokens[0].Kind);
        Assert.AreEqual(TurtleTokenKind.CloseTripleTerm, tokens[1].Kind);
    }

    [TestMethod]
    public void LexAnnotationDelimiters()
    {
        List<TurtleToken> tokens = LexAll("{| |}");

        Assert.AreEqual(TurtleTokenKind.OpenAnnotation, tokens[0].Kind);
        Assert.AreEqual(TurtleTokenKind.CloseAnnotation, tokens[1].Kind);
    }

    [TestMethod]
    public void LexAtPrefixDirective()
    {
        List<TurtleToken> tokens = LexAll("@prefix");

        Assert.AreEqual(TurtleTokenKind.PrefixKeyword, tokens[0].Kind);
    }

    [TestMethod]
    public void LexSparqlPrefixDirective()
    {
        List<TurtleToken> tokens = LexAll("PREFIX");

        Assert.AreEqual(TurtleTokenKind.PrefixKeyword, tokens[0].Kind);
    }

    [TestMethod]
    public void LexAtVersionDirective()
    {
        List<TurtleToken> tokens = LexAll("@version");

        Assert.AreEqual(TurtleTokenKind.VersionKeyword, tokens[0].Kind);
    }

    [TestMethod]
    public void LexGraphKeyword()
    {
        List<TurtleToken> tokens = LexAll("GRAPH");

        Assert.AreEqual(TurtleTokenKind.GraphKeyword, tokens[0].Kind);
    }

    [TestMethod]
    public void LexBooleanLiterals()
    {
        List<TurtleToken> trueTokens = LexAll("true");
        List<TurtleToken> falseTokens = LexAll("false");

        Assert.AreEqual(TurtleTokenKind.BooleanLiteral, trueTokens[0].Kind);
        Assert.AreEqual(TurtleTokenKind.BooleanLiteral, falseTokens[0].Kind);
    }

    [TestMethod]
    public void LexAKeyword()
    {
        List<TurtleToken> tokens = LexAll("a");

        Assert.AreEqual(TurtleTokenKind.A, tokens[0].Kind);
    }

    [TestMethod]
    public void LexCommentsSkipped()
    {
        List<TurtleToken> tokens = LexAll("# comment line\n<a>");

        Assert.AreEqual(TurtleTokenKind.Iri, tokens[0].Kind);
    }

    [TestMethod]
    public void LexAnonymousBlankNode()
    {
        List<TurtleToken> tokens = LexAll("[]");

        Assert.AreEqual(TurtleTokenKind.AnonymousBlankNode, tokens[0].Kind);
    }

    [TestMethod]
    public void PositionTrackingByteAndLine()
    {
        List<TurtleToken> tokens = LexAll("\n  <a>");

        TurtleToken iri = tokens[0];
        Assert.AreEqual(1, iri.Span.StartLine);
        Assert.AreEqual(2, iri.Span.StartColumn);
        Assert.AreEqual(3, iri.Span.StartByte);
    }

    [TestMethod]
    public void UnterminatedStringRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"open", TurtleLexErrorCode.UnterminatedString);
    }

    [TestMethod]
    public void InvalidEscapeRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\q\"", TurtleLexErrorCode.InvalidEscape);
    }

    [TestMethod]
    public void LoneHighSurrogateEscapeRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\uD83C\"", TurtleLexErrorCode.SurrogateCodePoint);
    }

    [TestMethod]
    public void LoneLowSurrogateEscapeRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\uDCA1\"", TurtleLexErrorCode.SurrogateCodePoint);
    }

    [TestMethod]
    public void SurrogatePairEscapeRecoversAsErrorToken()
    {
        //Turtle does not encode astral characters as a \u surrogate pair; each \u must be a
        //Unicode scalar value, so even a well-formed high/low pair is rejected.
        AssertLexRecoversWithError("\"\\uD83C\\uDCA1\"", TurtleLexErrorCode.SurrogateCodePoint);
    }

    [TestMethod]
    public void CodePointExceedingUnicodeRangeRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\U00110000\"", TurtleLexErrorCode.CodePointOutOfRange);
    }

    [TestMethod]
    public void AcceptsAstralCodePointFromLongEscape()
    {
        List<TurtleToken> tokens = LexAll("\"\\U0001F600\"");

        Assert.AreEqual(TurtleTokenKind.StringLiteral, tokens[0].Kind);
        Assert.AreEqual("\U0001F600", tokens[0].Value.ToString());

        //The long escape decodes directly to its four UTF-8 bytes, no UTF-16 intermediate.
        Assert.AreSequenceEqual(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, tokens[0].Value.Span.ToArray());
    }

    private static void AssertLexRecoversWithError(string source, TurtleLexErrorCode expectedCode)
    {
        (List<TurtleToken> tokens, IReadOnlyList<LexDiagnostic> diagnostics) = LexWithDiagnostics(source);

        Assert.IsTrue(tokens.Exists(static token => token.Kind == TurtleTokenKind.Error), source);
        Assert.HasCount(1, diagnostics, source);
        Assert.AreEqual(expectedCode, diagnostics[0].Code, source);
    }

    private static (List<TurtleToken> Tokens, IReadOnlyList<LexDiagnostic> Diagnostics) LexWithDiagnostics(string source)
    {
        Utf8StringPool pool = new();
        try
        {
            TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
            List<TurtleToken> tokens = [];
            foreach(TurtleToken token in lexer.Tokenize())
            {
                if(token.Kind == TurtleTokenKind.EndOfInput)
                {
                    break;
                }

                tokens.Add(token);
            }

            return (tokens, lexer.Diagnostics);
        }
        finally
        {
            pool.Dispose();
        }
    }

    private static List<TurtleToken> LexAll(string source)
    {
        Utf8StringPool pool = new();
        try
        {
            TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
            List<TurtleToken> result = [];
            foreach(TurtleToken token in lexer.Tokenize())
            {
                result.Add(token);
            }

            return result;
        }
        finally
        {
            pool.Dispose();
        }
    }
}
