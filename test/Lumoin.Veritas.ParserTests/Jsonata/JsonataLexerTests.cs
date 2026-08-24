using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Validates the <see cref="JsonataLexer"/>: every emitted token kind, the
/// decoded-payload-versus-full-lexeme-span contract, longest-match operator disambiguation, string
/// escapes, the variable family, block-comment skipping, recovery, and the locked deferrals (no
/// regex literal, no signature delimiters).
/// </summary>
[TestClass]
internal sealed class JsonataLexerTests
{
    /// <summary>Gets or sets the ambient test context supplied by the framework.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A number literal lexes as <see cref="JsonataTokenKind.Number"/> carrying its raw lexeme.</summary>
    [TestMethod]
    public void LexIntegerNumber()
    {
        List<JsonataToken> tokens = LexAll("42");

        Assert.HasCount(2, tokens);
        Assert.AreEqual(JsonataTokenKind.Number, tokens[0].Kind);
        Assert.AreEqual("42", tokens[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[1].Kind);
    }

    /// <summary>A fractional number with an exponent lexes as a single <see cref="JsonataTokenKind.Number"/>.</summary>
    [TestMethod]
    public void LexFractionalExponentNumber()
    {
        List<JsonataToken> tokens = LexAll("6.02e-23");

        Assert.AreEqual(JsonataTokenKind.Number, tokens[0].Kind);
        Assert.AreEqual("6.02e-23", tokens[0].Value.ToString());
        AssertSpanCoversLexeme(tokens[0], 0, 8);
    }

    /// <summary>A leading sign lexes as a separate <see cref="JsonataTokenKind.Minus"/> ahead of the number.</summary>
    [TestMethod]
    public void LexLeadingMinusIsSeparateFromNumber()
    {
        List<JsonataToken> tokens = LexAll("-1");

        Assert.AreEqual(JsonataTokenKind.Minus, tokens[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Number, tokens[1].Kind);
        Assert.AreEqual("1", tokens[1].Value.ToString());
    }

    /// <summary>A trailing range operator is not folded into the number's fractional part.</summary>
    [TestMethod]
    public void LexNumberFollowedByRange()
    {
        List<JsonataToken> tokens = LexAll("1..5");

        Assert.AreEqual(JsonataTokenKind.Number, tokens[0].Kind);
        Assert.AreEqual("1", tokens[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.DotDot, tokens[1].Kind);
        Assert.AreEqual(JsonataTokenKind.Number, tokens[2].Kind);
        Assert.AreEqual("5", tokens[2].Value.ToString());
    }

    /// <summary>A double-quoted string decodes its contents and spans both quotes.</summary>
    [TestMethod]
    public void LexDoubleQuotedString()
    {
        List<JsonataToken> tokens = LexAll("\"hello\"");

        Assert.AreEqual(JsonataTokenKind.String, tokens[0].Kind);
        Assert.AreEqual("hello", tokens[0].Value.ToString());
        AssertSpanCoversLexeme(tokens[0], 0, 7);
    }

    /// <summary>A single-quoted string lexes the same way as a double-quoted one.</summary>
    [TestMethod]
    public void LexSingleQuotedString()
    {
        List<JsonataToken> tokens = LexAll("'world'");

        Assert.AreEqual(JsonataTokenKind.String, tokens[0].Kind);
        Assert.AreEqual("world", tokens[0].Value.ToString());
    }

    /// <summary>String backslash escapes are decoded into the payload; the span still covers the raw lexeme.</summary>
    [TestMethod]
    public void LexStringWithSimpleEscapes()
    {
        List<JsonataToken> tokens = LexAll("\"a\\t\\n\\\"\\\\b\"");

        Assert.AreEqual(JsonataTokenKind.String, tokens[0].Kind);
        Assert.AreEqual("a\t\n\"\\b", tokens[0].Value.ToString());
        AssertSpanCoversLexeme(tokens[0], 0, 12);
    }

    /// <summary>A <c>\uXXXX</c> escape decodes to the named code unit.</summary>
    [TestMethod]
    public void LexStringWithUnicodeEscape()
    {
        List<JsonataToken> tokens = LexAll("\"\\u0041\"");

        Assert.AreEqual(JsonataTokenKind.String, tokens[0].Kind);
        Assert.AreEqual("A", tokens[0].Value.ToString());
    }

    /// <summary>A <c>\uXXXX</c> surrogate pair combines into one supplementary code point.</summary>
    [TestMethod]
    public void LexStringWithSurrogatePair()
    {
        List<JsonataToken> tokens = LexAll("\"\\uD83D\\uDE00\"");

        Assert.AreEqual(JsonataTokenKind.String, tokens[0].Kind);
        Assert.AreEqual("\U0001F600", tokens[0].Value.ToString());

        //The surrogate pair combines to one scalar emitted directly as its four UTF-8 bytes.
        Assert.AreSequenceEqual(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, tokens[0].Value.Span.ToArray());
    }

    /// <summary>A bare name lexes as <see cref="JsonataTokenKind.Name"/> carrying its text.</summary>
    [TestMethod]
    public void LexBareName()
    {
        List<JsonataToken> tokens = LexAll("price");

        Assert.AreEqual(JsonataTokenKind.Name, tokens[0].Kind);
        Assert.AreEqual("price", tokens[0].Value.ToString());
    }

    /// <summary><c>true</c>, <c>false</c>, and <c>null</c> stay <see cref="JsonataTokenKind.Name"/>.</summary>
    [TestMethod]
    public void LexValueKeywordsStayName()
    {
        Assert.AreEqual(JsonataTokenKind.Name, LexAll("true")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Name, LexAll("false")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Name, LexAll("null")[0].Kind);
    }

    /// <summary>The reserved keyword operators are re-kinded from a name run.</summary>
    [TestMethod]
    public void LexKeywordOperators()
    {
        Assert.AreEqual(JsonataTokenKind.KeywordAnd, LexAll("and")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.KeywordOr, LexAll("or")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.KeywordIn, LexAll("in")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.KeywordFunction, LexAll("function")[0].Kind);
    }

    /// <summary>The lambda alias <c>λ</c> (U+03BB) lexes as <see cref="JsonataTokenKind.Lambda"/>.</summary>
    [TestMethod]
    public void LexLambdaAlias()
    {
        List<JsonataToken> tokens = LexAll("λ");

        Assert.AreEqual(JsonataTokenKind.Lambda, tokens[0].Kind);
    }

    /// <summary>A backtick name carries the inner text only, while the span covers both backticks.</summary>
    [TestMethod]
    public void LexBacktickName()
    {
        List<JsonataToken> tokens = LexAll("`Product Name`");

        Assert.AreEqual(JsonataTokenKind.BacktickName, tokens[0].Kind);
        Assert.AreEqual("Product Name", tokens[0].Value.ToString());
        AssertSpanCoversLexeme(tokens[0], 0, 14);
    }

    /// <summary>A bare <c>$</c> is the current-context focus, with an empty decoded value.</summary>
    [TestMethod]
    public void LexBareContextVariable()
    {
        List<JsonataToken> tokens = LexAll("$");

        Assert.AreEqual(JsonataTokenKind.Variable, tokens[0].Kind);
        Assert.IsTrue(tokens[0].Value.IsEmpty);
        AssertSpanCoversLexeme(tokens[0], 0, 1);
    }

    /// <summary><c>$$</c> is the root variable, distinguished by a single-<c>$</c> decoded value.</summary>
    [TestMethod]
    public void LexRootVariable()
    {
        List<JsonataToken> tokens = LexAll("$$");

        Assert.AreEqual(JsonataTokenKind.Variable, tokens[0].Kind);
        Assert.AreEqual("$", tokens[0].Value.ToString());
        AssertSpanCoversLexeme(tokens[0], 0, 2);
    }

    /// <summary><c>$name</c> is a named variable; the decoded value omits the leading <c>$</c>.</summary>
    [TestMethod]
    public void LexNamedVariable()
    {
        List<JsonataToken> tokens = LexAll("$count");

        Assert.AreEqual(JsonataTokenKind.Variable, tokens[0].Kind);
        Assert.AreEqual("count", tokens[0].Value.ToString());
        AssertSpanCoversLexeme(tokens[0], 0, 6);
    }

    /// <summary>The range operator wins longest-match over the map operator.</summary>
    [TestMethod]
    public void LexDotVersusRange()
    {
        Assert.AreEqual(JsonataTokenKind.Dot, LexAll(".")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.DotDot, LexAll("..")[0].Kind);
    }

    /// <summary>The bind operator wins longest-match over the colon.</summary>
    [TestMethod]
    public void LexColonVersusAssign()
    {
        Assert.AreEqual(JsonataTokenKind.Colon, LexAll(":")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Assign, LexAll(":=")[0].Kind);
    }

    /// <summary>The comparison operators win longest-match over their single-character forms.</summary>
    [TestMethod]
    public void LexComparisonLongestMatch()
    {
        Assert.AreEqual(JsonataTokenKind.Less, LexAll("<")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.LessEqual, LexAll("<=")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Greater, LexAll(">")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.GreaterEqual, LexAll(">=")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Equal, LexAll("=")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.NotEqual, LexAll("!=")[0].Kind);
    }

    /// <summary>The conditional operators win longest-match over the bare question mark.</summary>
    [TestMethod]
    public void LexQuestionLongestMatch()
    {
        Assert.AreEqual(JsonataTokenKind.Question, LexAll("?")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.QuestionColon, LexAll("?:")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.QuestionQuestion, LexAll("??")[0].Kind);
    }

    /// <summary>The power operator wins longest-match over the star, and the chain over a bare tilde.</summary>
    [TestMethod]
    public void LexStarAndChainLongestMatch()
    {
        Assert.AreEqual(JsonataTokenKind.Star, LexAll("*")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.StarStar, LexAll("**")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Chain, LexAll("~>")[0].Kind);
    }

    /// <summary>Every single-character punctuation and operator lexes to its own kind.</summary>
    [TestMethod]
    public void LexSinglePunctuation()
    {
        Assert.AreEqual(JsonataTokenKind.OpenBracket, LexAll("[")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.CloseBracket, LexAll("]")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.OpenBrace, LexAll("{")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.CloseBrace, LexAll("}")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.OpenParen, LexAll("(")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.CloseParen, LexAll(")")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Comma, LexAll(",")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Semicolon, LexAll(";")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Plus, LexAll("+")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Minus, LexAll("-")[0].Kind);

        //A '/' is the divide operator only after a value-ending token; a leading '/' is a regex literal, so
        //the operator is tested in its infix position.
        Assert.AreEqual(JsonataTokenKind.Slash, LexAll("a/b")[1].Kind);
        Assert.AreEqual(JsonataTokenKind.Percent, LexAll("%")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Ampersand, LexAll("&")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Caret, LexAll("^")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Pipe, LexAll("|")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.At, LexAll("@")[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Hash, LexAll("#")[0].Kind);
    }

    /// <summary>Whitespace between tokens produces no token of its own.</summary>
    [TestMethod]
    public void LexWhitespaceSkipped()
    {
        List<JsonataToken> tokens = LexAll("  a   +  b  ");

        Assert.HasCount(4, tokens);
        Assert.AreEqual(JsonataTokenKind.Name, tokens[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Plus, tokens[1].Kind);
        Assert.AreEqual(JsonataTokenKind.Name, tokens[2].Kind);
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[3].Kind);
    }

    /// <summary>A block comment is skipped, not emitted, and lexing continues past it.</summary>
    [TestMethod]
    public void LexBlockCommentSkipped()
    {
        List<JsonataToken> tokens = LexAll("a /* skip this */ b");

        Assert.HasCount(3, tokens);
        Assert.AreEqual(JsonataTokenKind.Name, tokens[0].Kind);
        Assert.AreEqual("a", tokens[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.Name, tokens[1].Kind);
        Assert.AreEqual("b", tokens[1].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[2].Kind);
    }

    /// <summary>An empty input yields only <see cref="JsonataTokenKind.EndOfInput"/>.</summary>
    [TestMethod]
    public void LexEmptyInputIsEndOfInput()
    {
        List<JsonataToken> tokens = LexAll(string.Empty);

        Assert.HasCount(1, tokens);
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[0].Kind);
    }

    /// <summary>Byte and line/column positions advance across line breaks.</summary>
    [TestMethod]
    public void PositionTrackingByteAndLine()
    {
        List<JsonataToken> tokens = LexAll("\n  ab");

        JsonataToken name = tokens[0];
        Assert.AreEqual(1, name.Span.StartLine);
        Assert.AreEqual(2, name.Span.StartColumn);
        Assert.AreEqual(3, name.Span.StartByte);
    }

    /// <summary>An unterminated string recovers as an <see cref="JsonataTokenKind.Error"/> token plus a diagnostic.</summary>
    [TestMethod]
    public void UnterminatedStringRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"open", JsonataLexErrorCode.UnterminatedString);
    }

    /// <summary>An invalid escape recovers as an error token.</summary>
    [TestMethod]
    public void InvalidEscapeRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\q\"", JsonataLexErrorCode.InvalidEscape);
    }

    /// <summary>A lone high surrogate escape recovers as an error token.</summary>
    [TestMethod]
    public void UnpairedHighSurrogateRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\uD83D\"", JsonataLexErrorCode.UnpairedSurrogate);
    }

    /// <summary>A lone low surrogate escape recovers as an error token.</summary>
    [TestMethod]
    public void UnpairedLowSurrogateRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\uDE00\"", JsonataLexErrorCode.UnpairedSurrogate);
    }

    /// <summary>An unterminated backtick name recovers as an error token.</summary>
    [TestMethod]
    public void UnterminatedBacktickNameRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("`open", JsonataLexErrorCode.UnterminatedBacktickName);
    }

    /// <summary>An unterminated block comment recovers as an error token.</summary>
    [TestMethod]
    public void UnterminatedBlockCommentRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("/* never closed", JsonataLexErrorCode.UnterminatedBlockComment);
    }

    /// <summary>A bare <c>!</c> recovers as an error token.</summary>
    [TestMethod]
    public void BareExclamationRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("!", JsonataLexErrorCode.BareExclamation);
    }

    /// <summary>A bare <c>~</c> recovers as an error token.</summary>
    [TestMethod]
    public void BareTildeRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("~", JsonataLexErrorCode.BareTilde);
    }

    /// <summary>An unexpected byte that begins no token recovers as an error token.</summary>
    [TestMethod]
    public void UnexpectedByteRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\\", JsonataLexErrorCode.UnexpectedByte);
    }

    /// <summary>
    /// A regex literal in prefix position (here at the start of input) lexes as a single
    /// <see cref="JsonataTokenKind.RegexLiteral"/> whose value is the flags, a <c>/</c> separator, then the
    /// pattern; with no flags the value begins with the separator.
    /// </summary>
    [TestMethod]
    public void RegexInPrefixPositionLexesAsRegexLiteral()
    {
        List<JsonataToken> tokens = LexAll("/re/");

        Assert.AreEqual(JsonataTokenKind.RegexLiteral, tokens[0].Kind);
        Assert.AreEqual("/re", tokens[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[1].Kind);
    }

    /// <summary>A regex literal carries its flags before the <c>/</c> separator: <c>/hat/i</c> decodes to <c>i/hat</c>.</summary>
    [TestMethod]
    public void RegexLiteralCarriesFlagsBeforeSeparator()
    {
        List<JsonataToken> tokens = LexAll("/hat/i");

        Assert.AreEqual(JsonataTokenKind.RegexLiteral, tokens[0].Kind);
        Assert.AreEqual("i/hat", tokens[0].Value.ToString());
    }

    /// <summary>A <c>/</c> after a value-ending token (here a number) is the divide operator, not a regex literal.</summary>
    [TestMethod]
    public void SlashAfterValueLexesAsDivide()
    {
        List<JsonataToken> tokens = LexAll("10/2");

        AssertNoneIsKind(tokens, JsonataTokenKind.RegexLiteral);
        Assert.AreEqual(JsonataTokenKind.Number, tokens[0].Kind);
        Assert.AreEqual(JsonataTokenKind.Slash, tokens[1].Kind);
        Assert.AreEqual(JsonataTokenKind.Number, tokens[2].Kind);
    }

    /// <summary>A character class makes an interior <c>/</c> literal: <c>/[a/b]/</c> is one regex literal.</summary>
    [TestMethod]
    public void RegexCharacterClassKeepsInteriorSlashLiteral()
    {
        List<JsonataToken> tokens = LexAll("/[a/b]/");

        Assert.AreEqual(JsonataTokenKind.RegexLiteral, tokens[0].Kind);
        Assert.AreEqual("/[a/b]", tokens[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[1].Kind);
    }

    /// <summary>An escaped slash inside a regex does not close it: <c>/a\/b/</c> is one regex literal.</summary>
    [TestMethod]
    public void RegexEscapedSlashDoesNotClose()
    {
        List<JsonataToken> tokens = LexAll("/a\\/b/");

        Assert.AreEqual(JsonataTokenKind.RegexLiteral, tokens[0].Kind);
        Assert.AreEqual("/a\\/b", tokens[0].Value.ToString());
    }

    /// <summary>An unterminated regex literal recovers as an error token with the matching diagnostic.</summary>
    [TestMethod]
    public void UnterminatedRegexRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("/never closed", JsonataLexErrorCode.UnterminatedRegex);
    }

    /// <summary>An empty regex literal recovers as an error token with the matching diagnostic.</summary>
    [TestMethod]
    public void EmptyRegexRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("//", JsonataLexErrorCode.EmptyRegex);
    }

    /// <summary>
    /// A signature-shaped function declaration lexes <c>&lt;</c>/<c>&gt;</c> as comparison operators,
    /// never <see cref="JsonataTokenKind.SignatureOpen"/>/<see cref="JsonataTokenKind.SignatureClose"/>
    /// — signature delimiters are not lexed.
    /// </summary>
    [TestMethod]
    public void SignatureShapedInputLexesAngleBracketsAsComparison()
    {
        List<JsonataToken> tokens = LexAll("function($x)<n>{}");

        AssertNoneIsKind(tokens, JsonataTokenKind.SignatureOpen);
        AssertNoneIsKind(tokens, JsonataTokenKind.SignatureClose);
        Assert.IsTrue(tokens.Exists(static token => token.Kind == JsonataTokenKind.Less));
        Assert.IsTrue(tokens.Exists(static token => token.Kind == JsonataTokenKind.Greater));
    }

    /// <summary>An exponent marker with no following digit is not part of the number; it begins the next name.</summary>
    [TestMethod]
    public void LexNumberWithEmptyExponentSplitsName()
    {
        List<JsonataToken> bareExponent = LexAll("1e");

        Assert.AreEqual(JsonataTokenKind.Number, bareExponent[0].Kind);
        Assert.AreEqual("1", bareExponent[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.Name, bareExponent[1].Kind);
        Assert.AreEqual("e", bareExponent[1].Value.ToString());

        List<JsonataToken> exponentName = LexAll("1ea");

        Assert.AreEqual(JsonataTokenKind.Number, exponentName[0].Kind);
        Assert.AreEqual("1", exponentName[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.Name, exponentName[1].Kind);
        Assert.AreEqual("ea", exponentName[1].Value.ToString());

        List<JsonataToken> exponentSign = LexAll("1e+");

        Assert.AreEqual(JsonataTokenKind.Number, exponentSign[0].Kind);
        Assert.AreEqual("1", exponentSign[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.Name, exponentSign[1].Kind);
        Assert.AreEqual("e", exponentSign[1].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.Plus, exponentSign[2].Kind);
    }

    /// <summary>A trailing dot is the map/range operator, not the number's fractional part.</summary>
    [TestMethod]
    public void LexNumberTrailingDotIsSeparate()
    {
        List<JsonataToken> tokens = LexAll("1.");

        Assert.AreEqual(JsonataTokenKind.Number, tokens[0].Kind);
        Assert.AreEqual("1", tokens[0].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.Dot, tokens[1].Kind);
    }

    /// <summary>A non-hexadecimal digit in a <c>\u</c> escape recovers as an error token.</summary>
    [TestMethod]
    public void InvalidHexDigitRecoversAsErrorToken()
    {
        AssertLexRecoversWithError("\"\\uXY12\"", JsonataLexErrorCode.InvalidHexDigit);
    }

    /// <summary>Recovery makes forward progress: tokens after a fault are still emitted and the stream ends cleanly.</summary>
    [TestMethod]
    public void RecoveryMakesForwardProgressToEndOfInput()
    {
        List<JsonataToken> tokens = LexAll("1 ! 2");

        Assert.IsTrue(tokens.Exists(static token => token.Kind == JsonataTokenKind.Error));

        int errorIndex = tokens.FindIndex(static token => token.Kind == JsonataTokenKind.Error);
        JsonataToken errorToken = tokens[errorIndex];

        Assert.AreNotEqual(errorToken.Span.StartByte, errorToken.Span.EndByte);
        Assert.AreEqual(JsonataTokenKind.Number, tokens[errorIndex + 1].Kind);
        Assert.AreEqual("2", tokens[errorIndex + 1].Value.ToString());
        Assert.AreEqual(JsonataTokenKind.EndOfInput, tokens[^1].Kind);
    }

    private static void AssertNoneIsKind(List<JsonataToken> tokens, JsonataTokenKind kind)
    {
        Assert.IsFalse(tokens.Exists(token => token.Kind == kind), kind.ToString());
    }

    private static void AssertSpanCoversLexeme(JsonataToken token, int startByte, int endByte)
    {
        Assert.AreEqual(startByte, token.Span.StartByte);
        Assert.AreEqual(endByte, token.Span.EndByte);
    }

    private static void AssertLexRecoversWithError(string source, JsonataLexErrorCode expectedCode)
    {
        (List<JsonataToken> tokens, IReadOnlyList<JsonataLexDiagnostic> diagnostics) = LexWithDiagnostics(source);

        Assert.IsTrue(tokens.Exists(static token => token.Kind == JsonataTokenKind.Error), source);
        Assert.HasCount(1, diagnostics, source);
        Assert.AreEqual(expectedCode, diagnostics[0].Code, source);
    }

    private static (List<JsonataToken> Tokens, IReadOnlyList<JsonataLexDiagnostic> Diagnostics) LexWithDiagnostics(string source)
    {
        Utf8StringPool pool = new();
        try
        {
            JsonataLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
            List<JsonataToken> tokens = [];
            foreach(JsonataToken token in lexer.Tokenize())
            {
                if(token.Kind == JsonataTokenKind.EndOfInput)
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

    private static List<JsonataToken> LexAll(string source)
    {
        Utf8StringPool pool = new();
        try
        {
            JsonataLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
            List<JsonataToken> result = [];
            foreach(JsonataToken token in lexer.Tokenize())
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
