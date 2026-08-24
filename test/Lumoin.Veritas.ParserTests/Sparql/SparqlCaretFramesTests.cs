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
/// caret at a distinct grammatical position; the expected string is the frame chain formatted "Kind@Stage",
/// innermost to outermost. The stage carries the position within the production, so a repetition before its
/// first item and the same repetition once satisfied are separate rows. These fixtures double as the
/// regression guard for the flag-guarded caret suspension.
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
    [DataRow("SELECT ?s ?p ?o ", "SelectClause@3 Request@2")]
    [DataRow("SELECT (?x AS ?y) ", "SelectClause@3 Request@2")]
    [DataRow("SELECT * WHERE { ?s ?p ?o } ORDER BY ?s ", "OrderBy@4 Request@11")]
    [DataRow("SELECT ?p WHERE { ?s ?p ?o } GROUP BY ?p ", "GroupBy@4 Request@7")]
    [DataRow("SELECT * WHERE { { SELECT ?x WHERE { ?x ?p ?o } ", "Request@5 GroupGraphPattern@2 UnionPattern@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o } VALUES ?v { 1 } ", "Request@17")]
    [DataRow("SELECT * WHERE { ?s ?p ?o", "Triple@6 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ", "Triple@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o . FILTER(", "Expression@0 Expression@4 Filter@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o ; ", "GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { GRAPH ?g { ", "GroupGraphPattern@1 GraphPattern@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o OPTIONAL { ", "GroupGraphPattern@1 OptionalPattern@1 GroupGraphPattern@1 Request@5")]
    [DataRow("PREFIX e: <http://e/> SELECT * WHERE { ?s e:p ", "PathSequence@1 PropertyPath@1 Triple@3 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ?o } ORDER BY ", "OrderBy@1 Request@11")]
    [DataRow("SELECT * WHERE { ?s ?p ?o }", "Request@5")]
    [DataRow("SELECT * WHERE { VALUES ?v { ", "Values@1 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p ( ", "Collection@1 Triple@5 GroupGraphPattern@1 Request@5")]
    [DataRow("SELECT * WHERE { ?s ?p [ ", "BlankNodePropertyList@1 Triple@5 GroupGraphPattern@1 Request@5")]
    [DataRow("CONSTRUCT { ", "ConstructTemplate@1 Request@14")]
    [DataRow("CONSTRUCT { ?s ?p ?o } ", "Request@14")]
    [DataRow("DESCRIBE ", "Request@15")]
    [DataRow("DESCRIBE ?s ", "Request@20")]
    [DataRow("DESCRIBE <http://e/s> ", "Request@20")]
    [DataRow("DESCRIBE * ", "Request@21")]
    [DataRow("DESCRIBE ?s FROM <http://g/> ", "Request@21")]
    [DataRow("INSERT DATA { ", "Quads@1 UpdateOperation@1 Request@19")]
    [DataRow("INSERT DATA { <http://e/s> <http://e/p> <http://e/o> } ", "UpdateOperation@1 Request@19")]
    [DataRow("DELETE WHERE { ", "Quads@1 UpdateOperation@2 Request@19")]
    [DataRow("DELETE { ", "Quads@1 Modify@1 UpdateOperation@3 Request@19")]
    public void CaretFrameChainIsCaretPrecise(string source, string expectedChain)
    {
        Assert.AreEqual(expectedChain, FramesAt(source));
    }
}
