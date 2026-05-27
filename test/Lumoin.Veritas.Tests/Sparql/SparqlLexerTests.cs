using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql;
using Lumoin.Veritas.Sparql.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

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

        CollectionAssert.AreEqual(
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
        CollectionAssert.AreEqual(
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
        CollectionAssert.AreEqual(
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

        CollectionAssert.AreEqual(
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
        CollectionAssert.AreEqual(
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
        CollectionAssert.AreEqual(
            new[] { SparqlTokenKind.OpenBrace, SparqlTokenKind.Pipe, SparqlTokenKind.CloseBrace },
            Kinds(Lex("{ | }")));
    }

    /// <summary>String literals and the three numeric literal forms lex distinctly.</summary>
    [TestMethod]
    public void LexesStringAndNumericLiterals()
    {
        List<SparqlToken> tokens = Lex("\"hi\" 42 1.5 1.0e3");

        CollectionAssert.AreEqual(
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
        CollectionAssert.AreEqual(
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
        CollectionAssert.AreEqual(
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

        CollectionAssert.AreEqual(new[] { SparqlTokenKind.Error }, Kinds(tokens));
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

        CollectionAssert.AreEqual(
            new[] { SparqlTokenKind.Variable, SparqlTokenKind.Error, SparqlTokenKind.Variable },
            Kinds(tokens));
        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.ExpectedSecondAmpersand, diagnostics[0].Code);
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
}
