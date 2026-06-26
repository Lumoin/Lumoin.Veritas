using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Pins the caret→open-frame mapping the completion seam builds on: driving a fresh parser over a source
/// prefix — as if a caret sat at its end — and reading
/// <see cref="SparqlParser.SuspendOpenFramesAtEndOfInput"/> yields the productions open at the caret,
/// innermost first, suspended at the end-of-input token rather than recovered into error nodes. Each row is a
/// caret in a distinct production; the expected string is the frame chain formatted "Kind@Stage", innermost to
/// outermost. These fixtures double as the regression guard for the flag-guarded caret suspension.
/// </summary>
[TestClass]
internal sealed class SparqlCaretFramesTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Drives a fresh parser over the finalized source and formats the open-frame chain at its end.</summary>
    /// <param name="source">The query text up to the caret.</param>
    /// <returns>The open-frame chain as space-separated "Kind@Stage", innermost to outermost.</returns>
    private static string FramesAt(string source)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(pool);
        SparqlParser parser = new(pool);
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        lexer.FeedDecodedSource(bytes, isFinal: true);

        while(true)
        {
            SparqlLexStatus status = lexer.TryLexNext(out SparqlToken token);
            if(status == SparqlLexStatus.NeedMore)
            {
                break;
            }

            parser.FeedToken(token);
            if(token.Kind == SparqlTokenKind.EndOfInput)
            {
                break;
            }
        }

        IReadOnlyList<(ParseFrameKind Kind, int Stage)> frames = parser.SuspendOpenFramesAtEndOfInput();
        StringBuilder formatted = new();
        foreach((ParseFrameKind kind, int stage) in frames)
        {
            if(formatted.Length > 0)
            {
                formatted.Append(' ');
            }

            formatted.Append(kind).Append('@').Append(stage);
        }

        return formatted.ToString();
    }

    /// <summary>A caret at the end of each prefix opens the expected production chain, innermost first.</summary>
    /// <param name="source">The query text up to the caret.</param>
    /// <param name="expectedChain">The open-frame chain as space-separated "Kind@Stage", innermost to outermost.</param>
    [TestMethod]
    [DataRow("", "Request@0")]
    [DataRow("SELECT ", "SelectClause@1 Request@2")]
    [DataRow("SELECT * WHERE { ?s ?p ?o", "Triple@6 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ", "Triple@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o . FILTER(", "Expression@0 Expression@4 Filter@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o ; ", "GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { GRAPH ?g { ", "GraphPattern@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o OPTIONAL { ", "OptionalPattern@1 GroupGraphPattern@1 Request@5")]
    [DataRow("PREFIX e: <http://e/> SELECT * WHERE { ?s e:p ", "PathSequence@1 PropertyPath@1 Triple@3 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o } ORDER BY ", "OrderBy@1 Request@11")]
    [DataRow("SELECT * WHERE { ?s ?p ?o }", "Request@5")]
    public void CaretFrameChainIsCaretPrecise(string source, string expectedChain)
    {
        Assert.AreEqual(expectedChain, FramesAt(source));
    }
}
