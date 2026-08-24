using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies the Turtle parser's completion seam: driving the buffered tokens for a caret at the end of the
/// source suspends with the work stack intact, so the open-frame chain reflects where the caret sits in the
/// grammar (the basis for caret-aware Turtle completion), rather than recovering the open productions.
/// </summary>
[TestClass]
internal sealed class TurtleCaretFramesTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The open-frame chain (innermost first) at a caret at the end of the given Turtle text.</summary>
    /// <param name="source">The Turtle text up to the caret.</param>
    /// <returns>The chain as space-separated "Kind@Stage", innermost to outermost.</returns>
    private static string FramesAt(string source, TurtleSyntax syntax = TurtleSyntax.Turtle)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(bytes, pool);
        TurtleParser parser = new(pool, new DocumentId(1), syntax);
        foreach(TurtleToken token in lexer.Tokenize())
        {
            parser.FeedToken(token);
        }

        StringBuilder formatted = new();
        foreach((ParseFrameKind kind, int stage) in parser.SuspendOpenFramesAtEndOfInput())
        {
            if(formatted.Length > 0)
            {
                formatted.Append(' ');
            }

            formatted.Append(kind).Append('@').Append(stage);
        }

        return formatted.ToString();
    }

    /// <summary>
    /// A caret at the end of each statement body opens the expected production chain, innermost first. The
    /// suffix follows a <c>@prefix</c> directive (a clean statement boundary), so the chain belongs to the
    /// partial statement being typed at the caret.
    /// </summary>
    /// <param name="suffix">The statement-body text up to the caret, after the prefix directive.</param>
    /// <param name="expectedChain">The open-frame chain as space-separated "Kind@Stage", innermost to outermost.</param>
    [TestMethod]
    [DataRow("", "")]
    [DataRow("ex:s ", "SubjectStatement@1 Statement@0")]
    [DataRow("ex:s ex:p ", "ObjectList@0 PredicateObject@1 PredicateObjectList@1 SubjectStatement@2 Statement@0")]
    [DataRow("ex:s ex:p ex:o ", "AnnotatedObject@1 ObjectList@1 PredicateObject@1 PredicateObjectList@1 SubjectStatement@2 Statement@0")]
    [DataRow("ex:s ex:p ex:o ; ", "SubjectStatement@2 Statement@0")]
    [DataRow("ex:s ex:p ex:o , ", "AnnotatedObject@0 ObjectList@1 PredicateObject@1 PredicateObjectList@1 SubjectStatement@2 Statement@0")]
    [DataRow("ex:s ex:p ( ", "Collection@0 Term@0 AnnotatedObject@1 ObjectList@1 PredicateObject@1 PredicateObjectList@1 SubjectStatement@2 Statement@0")]
    [DataRow("ex:s ex:p [ ", "BlankNodePropertyList@0 Term@0 AnnotatedObject@1 ObjectList@1 PredicateObject@1 PredicateObjectList@1 SubjectStatement@2 Statement@0")]
    public void CaretFrameChainIsCaretPrecise(string suffix, string expectedChain)
    {
        Assert.AreEqual(expectedChain, FramesAt("@prefix ex: <http://example.org/> .\n" + suffix));
    }

    /// <summary>
    /// TriG graph-block carets, as the parser actually suspends them. An open graph block with content keeps a
    /// <c>GraphBlock</c> frame on the stack; an incomplete or empty block at end of input recovers to a fresh
    /// statement boundary (a lone <c>Statement@0</c>); a labelled block takes the subject-statement label path.
    /// </summary>
    /// <param name="suffix">The statement-body text up to the caret, after the prefix directive.</param>
    /// <param name="expectedChain">The open-frame chain as space-separated "Kind@Stage", innermost to outermost.</param>
    [TestMethod]
    [DataRow("GRAPH ", "Statement@0")]
    [DataRow("{ ", "Statement@0")]
    [DataRow("ex:g { ", "SubjectStatement@3 Statement@0")]
    [DataRow("{ ex:s ex:p ex:o . ", "GraphBlock@1 Statement@0")]
    [DataRow("{ ex:s ", "SubjectStatement@1 GraphBlock@1 Statement@0")]
    public void TrigCaretFrameChainIsCaretPrecise(string suffix, string expectedChain)
    {
        Assert.AreEqual(expectedChain, FramesAt("@prefix ex: <http://example.org/> .\n" + suffix, TurtleSyntax.TriG));
    }
}
