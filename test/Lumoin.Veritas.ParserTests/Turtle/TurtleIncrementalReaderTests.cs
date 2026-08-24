using System;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies the editor contract of <see cref="TurtleIncrementalReader"/>: source fed one byte at a time produces the
/// same <see cref="TurtleDocument"/> AST — rendered deeply, source spans and node table included — as the whole-buffer
/// <see cref="TurtleReader.ReadWithSourceAsync(System.ReadOnlyMemory{byte}, TurtleSyntax, DocumentId, Utf8StringPool?, string?, System.Threading.CancellationToken)"/>,
/// and reports <see cref="IncrementalParseStatus.NeedMore"/> until <see cref="TurtleIncrementalReader.Complete"/>.
/// </summary>
/// <remarks>
/// Feeding a single byte at a time forces a token to straddle nearly every chunk boundary, exercising the lexer's
/// re-presentation of the unconsumed tail. The byte-cut render equality (which record equality cannot give, since
/// <c>List&lt;T&gt;</c> fields compare by reference) is the oracle the SPARQL, Jsonata, and OWL incremental readers use.
/// </remarks>
[TestClass]
internal sealed class TurtleIncrementalReaderTests
{
    /// <summary>Before <see cref="TurtleIncrementalReader.Complete"/> every feed reports <see cref="IncrementalParseStatus.NeedMore"/> (a Turtle document has no terminator); afterwards the status is <see cref="IncrementalParseStatus.Complete"/>.</summary>
    [TestMethod]
    public void StatusIsNeedMoreUntilComplete()
    {
        TurtleIncrementalReader reader = new(TurtleSyntax.Turtle);

        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("<http://e/s> <http://e/p> "u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Status);
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("<http://e/o> .\n"u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Status);

        _ = reader.Complete();

        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Status);
    }

    /// <summary>Feeding after <see cref="TurtleIncrementalReader.Complete"/> is rejected.</summary>
    [TestMethod]
    public void FeedAfterCompleteThrows()
    {
        TurtleIncrementalReader reader = new(TurtleSyntax.Turtle);
        reader.Feed("<http://e/s> <http://e/p> <http://e/o> .\n"u8);
        _ = reader.Complete();

        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Feed(" "u8));
    }

    /// <summary>An empty document reads identically byte-fed and whole-buffer.</summary>
    [TestMethod]
    public void EmptyDocument()
    {
        AssertByteByByteMatchesWholeBuffer(string.Empty, TurtleSyntax.Turtle);
    }

    /// <summary>Directives and multi-predicate, multi-object triples.</summary>
    [TestMethod]
    public void DirectivesAndTriples()
    {
        AssertByteByByteMatchesWholeBuffer(
            "@prefix ex: <http://example.org/> .\n"
            + "@base <http://example.org/base/> .\n"
            + "PREFIX p: <http://other.example/>\n"
            + "ex:alice ex:knows ex:bob ; ex:age 42 .\n"
            + "ex:bob ex:name \"Bob\"@en , \"Bobby\" ; p:flag true .\n",
            TurtleSyntax.Turtle);
    }

    /// <summary>Collections and blank-node property lists (anonymous blank nodes allocated identically in document order on both paths).</summary>
    [TestMethod]
    public void CollectionsAndBlankNodePropertyLists()
    {
        AssertByteByByteMatchesWholeBuffer(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:s ex:p ( ex:a ex:b ex:c ) .\n"
            + "ex:s2 ex:q [ ex:r ex:o ; ex:r2 -3.0e-4 ] .\n"
            + "[ ex:lonely ex:value ] .\n",
            TurtleSyntax.Turtle);
    }

    /// <summary>Literals: language tags, datatypes, numbers, booleans, and a long string.</summary>
    [TestMethod]
    public void LiteralForms()
    {
        AssertByteByByteMatchesWholeBuffer(
            "@prefix ex: <http://example.org/> .\n"
            + "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\n"
            + "ex:s ex:int 42 ; ex:dec -3.14 ; ex:dbl 6.022e23 ; ex:bool false ;\n"
            + "  ex:typed \"2026-06-24\"^^xsd:date ; ex:lang \"hej\"@sv ;\n"
            + "  ex:long \"\"\"a\nmulti-line\nliteral\"\"\" .\n",
            TurtleSyntax.Turtle);
    }

    /// <summary>RDF 1.2 reified triples, triple terms, and annotations.</summary>
    [TestMethod]
    public void ReifiedTriplesTripleTermsAndAnnotations()
    {
        AssertByteByByteMatchesWholeBuffer(
            "<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>> .\n"
            + "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> ~ <http://example.org/r> >> .\n"
            + "<http://example.org/s> <http://example.org/p> <http://example.org/o> {| <http://example.org/m> <http://example.org/v> |} .\n",
            TurtleSyntax.Turtle);
    }

    /// <summary>TriG graph blocks in every shape.</summary>
    [TestMethod]
    public void TriGGraphBlocks()
    {
        AssertByteByByteMatchesWholeBuffer(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:default ex:p ex:o .\n"
            + "<http://example.org/g> { ex:s ex:p ex:o . ex:s2 ex:p2 ex:o2 }\n"
            + "GRAPH ex:g2 { ex:s3 ex:p3 ex:o3 . }\n"
            + "{ ex:s4 ex:p4 ex:o4 . }\n",
            TurtleSyntax.TriG);
    }

    /// <summary>A malformed document recovers into the same error-bearing AST on both paths.</summary>
    [TestMethod]
    public void MalformedRecoversIdentically()
    {
        AssertByteByByteMatchesWholeBuffer(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:s ex:p .\n"
            + "ex:ok ex:p ex:o .\n",
            TurtleSyntax.Turtle);
    }

    /// <summary>Feeds the source one byte at a time and asserts the resulting document renders identically to the whole-buffer parse over the same pool and document id; a deep structural render (source spans and node table included), so list-instance identity, interning, and node-id assignment are all compared.</summary>
    /// <param name="source">The Turtle/TriG source text.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    private static void AssertByteByByteMatchesWholeBuffer(string source, TurtleSyntax syntax)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        using Utf8StringPool pool = new();
        DocumentId documentId = new(1);

        (ParseResult<TurtleDocument> whole, _) = TurtleReader.ReadWithSourceAsync(bytes, syntax, documentId, pool);

        TurtleIncrementalReader reader = new(syntax, documentId, pool);
        for(int i = 0; i < bytes.Length; i++)
        {
            reader.Feed(bytes.AsSpan(i, 1));
        }

        ParseResult<TurtleDocument> incremental = reader.Complete();

        Assert.AreEqual(whole.HasErrors, incremental.HasErrors, "the byte-fed parse must agree with the whole-buffer parse on whether the document has errors");
        Assert.AreEqual(AstStructuralRenderer.Render(whole.Tree), AstStructuralRenderer.Render(incremental.Tree), "the byte-by-byte incremental parse must render identically to the whole-buffer parse");
    }
}
