using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql;
using Lumoin.Veritas.Sparql.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tokeniser tests for <see cref="SparqlLexer"/>: the SPARQL 1.2 terminals, the
/// context-sensitive disambiguations (<c>&lt;</c> as IRI vs. less-than, <c>?</c>
/// as variable vs. path quantifier), case-insensitive keyword and function
/// recognition, and error reporting.
/// </summary>
[TestClass]
internal sealed class SparqlLexerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A basic SELECT tokenises to the expected kind sequence, and variable
    /// payloads drop their <c>?</c> marker.
    /// </summary>
    [TestMethod]
    public void TokenizesSelectStarWhere()
    {
        List<SparqlToken> tokens = Lex("SELECT * WHERE { ?s ?p ?o }");

        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.SelectKeyword, SparqlTokenKind.Star, SparqlTokenKind.WhereKeyword,
                SparqlTokenKind.OpenBrace, SparqlTokenKind.Variable, SparqlTokenKind.Variable,
                SparqlTokenKind.Variable, SparqlTokenKind.CloseBrace,
            },
            Kinds(tokens));

        Assert.AreEqual("s", tokens[4].Value.ToString());
        Assert.AreEqual("o", tokens[6].Value.ToString());
    }

    /// <summary>Keywords are recognised regardless of source casing.</summary>
    [TestMethod]
    public void KeywordsAreCaseInsensitive()
    {
        Assert.AreEqual(SparqlTokenKind.SelectKeyword, Lex("select")[0].Kind);
        Assert.AreEqual(SparqlTokenKind.SelectKeyword, Lex("SeLeCt")[0].Kind);
        Assert.AreEqual(SparqlTokenKind.OptionalKeyword, Lex("optional")[0].Kind);
    }

    /// <summary>
    /// A <c>&lt;</c> followed by a non-IRI body is the less-than operator; a
    /// well-formed <c>&lt;...&gt;</c> is an IRI.
    /// </summary>
    [TestMethod]
    public void DistinguishesIriFromLessThan()
    {
        Assert.AreSequenceEqual(
            new[] { SparqlTokenKind.Variable, SparqlTokenKind.LessThan, SparqlTokenKind.Variable },
            Kinds(Lex("?a < ?b")));

        List<SparqlToken> iri = Lex("<http://example.org/x>");
        Assert.AreEqual(SparqlTokenKind.Iri, iri[0].Kind);
        Assert.AreEqual("http://example.org/x", iri[0].Value.ToString());

        Assert.AreEqual(SparqlTokenKind.LessOrEqual, Lex("?a <= ?b")[1].Kind);
    }

    /// <summary>The multi-character comparison and logical operators lex as single tokens.</summary>
    [TestMethod]
    public void LexesOperators()
    {
        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.LogicalAnd, SparqlTokenKind.LogicalOr, SparqlTokenKind.LessOrEqual,
                SparqlTokenKind.GreaterOrEqual, SparqlTokenKind.NotEquals, SparqlTokenKind.Equals,
                SparqlTokenKind.Bang, SparqlTokenKind.Pipe,
            },
            Kinds(Lex("&& || <= >= != = ! |")));
    }

    /// <summary>Prefixed names, the <c>a</c> shorthand, and an empty-prefix name lex distinctly.</summary>
    [TestMethod]
    public void LexesPrefixedNameAndAKeyword()
    {
        List<SparqlToken> tokens = Lex("foaf:name a :Thing");

        Assert.AreSequenceEqual(
            new[] { SparqlTokenKind.PrefixedName, SparqlTokenKind.A, SparqlTokenKind.PrefixedName },
            Kinds(tokens));

        Assert.AreEqual("foaf:name", tokens[0].Value.ToString());
        Assert.AreEqual(":Thing", tokens[2].Value.ToString());
    }

    /// <summary>
    /// Built-in and aggregate function names lex as their dedicated kinds with the
    /// canonical upper-case payload, regardless of source casing.
    /// </summary>
    [TestMethod]
    public void LexesBuiltInAndAggregateFunctionNames()
    {
        List<SparqlToken> tokens = Lex("str(?x) COUNT(?y) isIRI(?z)");

        //str ( ?x ) COUNT ( ?y ) isIRI ( ?z ) — indices 0,4,8 carry the function names.
        Assert.AreEqual(SparqlTokenKind.BuiltInFunctionName, tokens[0].Kind);
        Assert.AreEqual("STR", tokens[0].Value.ToString());
        Assert.AreEqual(SparqlTokenKind.AggregateFunctionName, tokens[4].Kind);
        Assert.AreEqual("COUNT", tokens[4].Value.ToString());
        Assert.AreEqual(SparqlTokenKind.BuiltInFunctionName, tokens[8].Kind);
        Assert.AreEqual("ISIRI", tokens[8].Value.ToString());
    }

    /// <summary>
    /// The RDF 1.2 reification delimiters (<c>&lt;&lt;</c>, <c>&lt;&lt;(</c>, <c>)&gt;&gt;</c>,
    /// <c>&gt;&gt;</c>), the reifier marker <c>~</c>, the annotation brackets <c>{|</c> / <c>|}</c>,
    /// and the <c>VERSION</c> keyword lex as their dedicated kinds.
    /// </summary>
    [TestMethod]
    public void LexesRdf12ReificationTerminals()
    {
        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.OpenReifiedTriple, SparqlTokenKind.OpenTripleTerm,
                SparqlTokenKind.CloseTripleTerm, SparqlTokenKind.CloseReifiedTriple,
                SparqlTokenKind.Tilde, SparqlTokenKind.OpenAnnotation, SparqlTokenKind.CloseAnnotation,
                SparqlTokenKind.VersionKeyword,
            },
            Kinds(Lex("<< <<( )>> >> ~ {| |} VERSION")));
    }

    /// <summary>A lone <c>{</c> and a lone <c>|</c> still lex as brace and pipe, not annotation brackets.</summary>
    [TestMethod]
    public void LexesLoneBraceAndPipeWithoutAnnotation()
    {
        Assert.AreSequenceEqual(
            new[] { SparqlTokenKind.OpenBrace, SparqlTokenKind.Pipe, SparqlTokenKind.CloseBrace },
            Kinds(Lex("{ | }")));
    }

    /// <summary>String literals and the three numeric literal forms lex distinctly.</summary>
    [TestMethod]
    public void LexesStringAndNumericLiterals()
    {
        List<SparqlToken> tokens = Lex("\"hi\" 42 1.5 1.0e3");

        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.StringLiteral, SparqlTokenKind.IntegerLiteral,
                SparqlTokenKind.DecimalLiteral, SparqlTokenKind.DoubleLiteral,
            },
            Kinds(tokens));

        Assert.AreEqual("hi", tokens[0].Value.ToString());
    }

    /// <summary>The RDF 1.2 reified-triple and triple-term delimiters lex as single tokens.</summary>
    [TestMethod]
    public void LexesReifiedTripleAndTripleTermDelimiters()
    {
        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.OpenReifiedTriple, SparqlTokenKind.CloseReifiedTriple,
                SparqlTokenKind.OpenTripleTerm, SparqlTokenKind.CloseTripleTerm,
            },
            Kinds(Lex("<< >> <<( )>>")));
    }

    /// <summary>A <c>$</c>-prefixed variable lexes with its name payload.</summary>
    [TestMethod]
    public void LexesDollarVariable()
    {
        List<SparqlToken> tokens = Lex("$value");

        Assert.AreEqual(SparqlTokenKind.Variable, tokens[0].Kind);
        Assert.AreEqual("value", tokens[0].Value.ToString());
    }

    /// <summary>
    /// A <c>?</c> not followed by a variable-name start is the zero-or-one path
    /// quantifier, not a variable.
    /// </summary>
    [TestMethod]
    public void QuestionMarkIsPathQuantifierWhenNotAVariable()
    {
        Assert.AreSequenceEqual(
            new[] { SparqlTokenKind.PrefixedName, SparqlTokenKind.Question },
            Kinds(Lex(":p ?")));
    }

    /// <summary>The final token is always <see cref="SparqlTokenKind.EndOfInput"/>.</summary>
    [TestMethod]
    public void EmitsEndOfInput()
    {
        List<SparqlToken> all = [];
        using Utf8StringPool pool = new();
        foreach(SparqlToken token in new SparqlLexer(Encoding.UTF8.GetBytes("ASK { }"), pool).Tokenize())
        {
            all.Add(token);
        }

        Assert.AreEqual(SparqlTokenKind.EndOfInput, all[^1].Kind);
    }

    /// <summary>
    /// An unterminated string literal recovers as a single <see cref="SparqlTokenKind.Error"/> token
    /// carrying an <see cref="SparqlLexErrorCode.UnterminatedString"/> diagnostic — the lexer no longer
    /// throws.
    /// </summary>
    [TestMethod]
    public void UnterminatedStringRecoversAsErrorToken()
    {
        (List<SparqlToken> tokens, IReadOnlyList<SparqlLexDiagnostic> diagnostics) = LexWithDiagnostics("\"abc");

        Assert.AreSequenceEqual(new[] { SparqlTokenKind.Error }, Kinds(tokens));
        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.UnterminatedString, diagnostics[0].Code);
    }

    /// <summary>
    /// A lone <c>&amp;</c> (no second <c>&amp;</c>) recovers as an <see cref="SparqlTokenKind.Error"/>
    /// token, keeping the variables on either side and recording one diagnostic.
    /// </summary>
    [TestMethod]
    public void LoneAmpersandRecoversAsErrorToken()
    {
        (List<SparqlToken> tokens, IReadOnlyList<SparqlLexDiagnostic> diagnostics) = LexWithDiagnostics("?a & ?b");

        Assert.AreSequenceEqual(
            new[] { SparqlTokenKind.Variable, SparqlTokenKind.Error, SparqlTokenKind.Variable },
            Kinds(tokens));
        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.ExpectedSecondAmpersand, diagnostics[0].Code);
    }

    /// <summary>
    /// A query encoded entirely in codepoint escapes (SPARQL 1.2 §19.2) decodes before tokenisation, so
    /// <c>ASK {}</c> lexes as <c>ASK {}</c>.
    /// </summary>
    [TestMethod]
    public void CodepointEscapesDecodeBeforeTokenising()
    {
        Assert.AreSequenceEqual(
            new[] { SparqlTokenKind.AskKeyword, SparqlTokenKind.OpenBrace, SparqlTokenKind.CloseBrace },
            Kinds(Lex("\\u0041\\u0053\\u004B\\u0020\\u007B\\u007D")));
    }

    /// <summary>A codepoint-escaped quotation mark becomes a structural quote that opens a string literal.</summary>
    [TestMethod]
    public void CodepointEscapedQuoteOpensStringLiteral()
    {
        List<SparqlToken> tokens = Lex("\\u0022value\"");

        Assert.AreEqual(SparqlTokenKind.StringLiteral, tokens[0].Kind);
        Assert.AreEqual("value", tokens[0].Value.ToString());
    }

    /// <summary>
    /// Codepoint decoding precedes string-escape interpretation: <c>"\\u0041"</c> decodes to the
    /// invalid string escape <c>\A</c> and is rejected, while <c>"\\u0074"</c> decodes to the valid
    /// escape <c>\t</c> (a tab).
    /// </summary>
    [TestMethod]
    public void DecodedBackslashEscapeIsValidatedAsStringEscape()
    {
        (List<SparqlToken> bad, IReadOnlyList<SparqlLexDiagnostic> diagnostics) = LexWithDiagnostics("\"\\\\u0041\"");
        Assert.AreSequenceEqual(new[] { SparqlTokenKind.Error }, Kinds(bad));
        Assert.AreEqual(SparqlLexErrorCode.InvalidEscape, diagnostics[0].Code);

        List<SparqlToken> good = Lex("\"\\\\u0074\"");
        Assert.AreEqual(SparqlTokenKind.StringLiteral, good[0].Kind);
        Assert.AreEqual("\t", good[0].Value.ToString());
    }

    /// <summary>A backslash escaping a non-<c>PN_LOCAL_ESC</c> character (here <c>\:</c>) in a local name is rejected.</summary>
    [TestMethod]
    public void InvalidPnLocalEscapeIsRejected()
    {
        (_, IReadOnlyList<SparqlLexDiagnostic> diagnostics) = LexWithDiagnostics("ns:a\\:b");

        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.InvalidPrefixedNameEscape, diagnostics[0].Code);
    }

    /// <summary>
    /// A token following a codepoint escape reports its true source coordinates, not decoded ones: after
    /// the six-byte <c> </c> the variable <c>?x</c> begins at source byte and column six.
    /// </summary>
    [TestMethod]
    public void SpanReportsSourceCoordinatesAfterEscape()
    {
        List<SparqlToken> tokens = Lex("\\u0020?x");

        Assert.AreEqual(SparqlTokenKind.Variable, tokens[0].Kind);
        Assert.AreEqual(6, tokens[0].Span.StartByte);
        Assert.AreEqual(6, tokens[0].Span.StartColumn);
    }

    /// <summary>The pipe-driven path decodes codepoint escapes and yields the same tokens as the whole-buffer path.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    public async Task TokenizeAsyncDecodesCodepointEscapes()
    {
        List<SparqlToken> tokens = await TokenizeAsync([Encoding.UTF8.GetBytes("\\u0041\\u0053\\u004B\\u0020\\u007B\\u007D")]).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.AskKeyword, SparqlTokenKind.OpenBrace, SparqlTokenKind.CloseBrace,
                SparqlTokenKind.EndOfInput,
            },
            Kinds(tokens));
    }

    /// <summary>
    /// A numeric codepoint escape split across two pipe reads decodes once the remaining bytes arrive: the
    /// held partial escape is re-presented on the next read, so <c>ASK {}</c> still yields <c>ASK {}</c>.
    /// </summary>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    public async Task TokenizeAsyncHandlesEscapeSplitAcrossReads()
    {
        //The first read ends mid-escape ("\u00"); the second supplies "7D", completing } = '}'.
        List<SparqlToken> tokens = await TokenizeAsync(
        [
            Encoding.UTF8.GetBytes("ASK {\\u00"),
            Encoding.UTF8.GetBytes("7D"),
        ]).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new[]
            {
                SparqlTokenKind.AskKeyword, SparqlTokenKind.OpenBrace, SparqlTokenKind.CloseBrace,
                SparqlTokenKind.EndOfInput,
            },
            Kinds(tokens));
    }

    /// <summary>
    /// Tokenises the supplied source chunks through the pipe-driven path, one chunk delivered per
    /// <see cref="PipeReader.ReadAsync"/>, returning every token including the end-of-input sentinel.
    /// </summary>
    /// <param name="chunks">The source byte chunks, delivered in order.</param>
    /// <returns>The tokens produced by <see cref="SparqlLexer.TokenizeAsync"/>.</returns>
    private static async Task<List<SparqlToken>> TokenizeAsync(IReadOnlyList<byte[]> chunks)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(pool);
        List<SparqlToken> tokens = [];
        await foreach(SparqlToken token in lexer.TokenizeAsync(new ChunkedPipeReader(chunks)).ConfigureAwait(false))
        {
            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// Lexes <paramref name="query"/> to a materialised token list with the trailing
    /// <see cref="SparqlTokenKind.EndOfInput"/> removed.
    /// </summary>
    /// <param name="query">The SPARQL query text.</param>
    /// <returns>The tokens, excluding the end-of-input sentinel.</returns>
    private static List<SparqlToken> Lex(string query)
    {
        using Utf8StringPool pool = new();
        List<SparqlToken> tokens = [];
        foreach(SparqlToken token in new SparqlLexer(Encoding.UTF8.GetBytes(query), pool).Tokenize())
        {
            if(token.Kind == SparqlTokenKind.EndOfInput)
            {
                break;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// Lexes <paramref name="query"/> with recovery on, returning both the tokens (without the trailing
    /// <see cref="SparqlTokenKind.EndOfInput"/>) and the diagnostics the lexer recorded.
    /// </summary>
    /// <param name="query">The SPARQL query text.</param>
    /// <returns>The tokens and the recorded lexical diagnostics.</returns>
    private static (List<SparqlToken> Tokens, IReadOnlyList<SparqlLexDiagnostic> Diagnostics) LexWithDiagnostics(string query)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(query), pool);
        List<SparqlToken> tokens = [];
        foreach(SparqlToken token in lexer.Tokenize())
        {
            if(token.Kind == SparqlTokenKind.EndOfInput)
            {
                break;
            }

            tokens.Add(token);
        }

        return (tokens, lexer.Diagnostics);
    }

    /// <summary>Projects the token kinds for sequence assertions.</summary>
    /// <param name="tokens">The tokens to project.</param>
    /// <returns>The kinds in order.</returns>
    private static SparqlTokenKind[] Kinds(List<SparqlToken> tokens)
    {
        SparqlTokenKind[] kinds = new SparqlTokenKind[tokens.Count];
        for(int i = 0; i < tokens.Count; i++)
        {
            kinds[i] = tokens[i].Kind;
        }

        return kinds;
    }

    /// <summary>
    /// A <see cref="PipeReader"/> that delivers a fixed list of byte chunks one chunk per
    /// <see cref="ReadAsync"/>, accumulating unconsumed bytes across reads. It makes the pipe-driven
    /// lexer's read boundaries deterministic, so an escape can be split across two reads on purpose.
    /// </summary>
    /// <param name="chunks">The source byte chunks, delivered in order.</param>
    private sealed class ChunkedPipeReader(IReadOnlyList<byte[]> chunks) : PipeReader
    {
        private byte[] buffered = [];
        private int nextChunk;
        private ReadOnlySequence<byte> current;

        /// <inheritdoc/>
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if(nextChunk < chunks.Count)
            {
                byte[] next = chunks[nextChunk];
                nextChunk++;
                byte[] combined = new byte[buffered.Length + next.Length];
                buffered.CopyTo(combined, 0);
                next.CopyTo(combined, buffered.Length);
                buffered = combined;
            }

            current = new ReadOnlySequence<byte>(buffered);

            return new ValueTask<ReadResult>(new ReadResult(current, isCanceled: false, isCompleted: nextChunk >= chunks.Count));
        }

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            buffered = buffered[(int)current.GetOffset(consumed)..];
        }

        /// <inheritdoc/>
        public override void CancelPendingRead()
        {
        }

        /// <inheritdoc/>
        public override void Complete(Exception? exception = null)
        {
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            result = default;

            return false;
        }
    }
}
