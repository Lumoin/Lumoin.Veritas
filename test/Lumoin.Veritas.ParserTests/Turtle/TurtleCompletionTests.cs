using System.Text;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Completion;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Tests <see cref="TurtleCompletion.Describe"/>: the store-free completion context at a caret. Each case
/// places the caret at the end of a statement body (after a <c>@prefix</c> boundary) and checks the expected
/// next tokens (from the innermost open production) and the innermost enclosing production.
/// </summary>
[TestClass]
internal sealed class TurtleCompletionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A clean statement boundary the case bodies follow.</summary>
    private const string Prefix = "@prefix ex: <http://example.org/> .\n";

    /// <summary>Describes the completion context at the end of the prefix plus the given statement body.</summary>
    /// <param name="body">The statement-body text up to the caret.</param>
    /// <returns>The completion context at the caret.</returns>
    private static CompletionContext DescribeAtEnd(string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Prefix + body);

        return TurtleCompletion.Describe(bytes, bytes.Length);
    }

    /// <summary>At a statement boundary the caret expects a directive keyword or a subject term, with no open production.</summary>
    [TestMethod]
    public void StatementBoundaryExpectsDirectiveOrSubject()
    {
        CompletionContext context = DescribeAtEnd("");

        Assert.Contains(TurtleTokenKind.PrefixKeyword, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.IsEmpty(context.EnclosingProductions);
    }

    /// <summary>After a subject the caret expects a verb: the <c>a</c> shorthand, an IRI, or a prefixed name.</summary>
    [TestMethod]
    public void AfterSubjectExpectsVerb()
    {
        CompletionContext context = DescribeAtEnd("ex:s ");

        Assert.Contains(TurtleTokenKind.A, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.SubjectStatement, context.EnclosingProductions[^1]);
    }

    /// <summary>After a verb the caret expects an object term — an IRI, a literal, or a compound term.</summary>
    [TestMethod]
    public void AfterVerbExpectsObject()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p ");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.StringLiteral, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.OpenBracket, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.ObjectList, context.EnclosingProductions[^1]);
    }

    /// <summary>Directly after a string literal's <c>^^</c> the caret sits at the datatype position, which admits exactly an IRI or a prefixed name.</summary>
    [TestMethod]
    public void AfterDatatypeMarkerExpectsAnIriOrPrefixedName()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p \"x\"^^");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.DoesNotContain(TurtleTokenKind.Comma, context.ExpectedTokens);
        Assert.DoesNotContain(TurtleTokenKind.StringLiteral, context.ExpectedTokens);
    }

    /// <summary>The datatype position answers the same way behind a long (triple-quoted) string literal.</summary>
    [TestMethod]
    public void AfterDatatypeMarkerOnALongStringExpectsAnIriOrPrefixedName()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p \"\"\"x\"\"\"^^");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.DoesNotContain(TurtleTokenKind.Comma, context.ExpectedTokens);
    }

    /// <summary>After a complete object the caret expects a continuation: an annotation, <c>,</c>, <c>;</c>, or <c>.</c>.</summary>
    [TestMethod]
    public void AfterObjectExpectsContinuation()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p ex:o ");

        Assert.Contains(TurtleTokenKind.Tilde, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Comma, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Semicolon, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Period, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.AnnotatedObject, context.EnclosingProductions[^1]);
    }

    /// <summary>After a <c>;</c> the caret expects another verb or the statement terminator <c>.</c>.</summary>
    [TestMethod]
    public void AfterSemicolonExpectsVerbOrTerminator()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p ex:o ; ");

        Assert.Contains(TurtleTokenKind.A, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Period, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.SubjectStatement, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a collection the caret expects an item term or the closing parenthesis.</summary>
    [TestMethod]
    public void InsideCollectionExpectsItemOrClose()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p ( ");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.CloseParen, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Collection, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a blank-node property list the caret expects a verb or the closing bracket.</summary>
    [TestMethod]
    public void InsideBlankNodePropertyListExpectsVerbOrClose()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p [ ");

        Assert.Contains(TurtleTokenKind.A, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.CloseBracket, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.BlankNodePropertyList, context.EnclosingProductions[^1]);
    }

    /// <summary>The caret offset is echoed back, clamped to the buffer.</summary>
    [TestMethod]
    public void CaretByteOffsetIsEchoed()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Prefix + "ex:s ");

        Assert.AreEqual(bytes.Length, TurtleCompletion.Describe(bytes, bytes.Length).CaretByteOffset);
    }

    /// <summary>Describes the TriG completion context at the end of the prefix plus the given statement body.</summary>
    /// <param name="body">The statement-body text up to the caret.</param>
    /// <returns>The completion context at the caret, parsed as TriG.</returns>
    private static CompletionContext DescribeAtEndTriG(string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Prefix + body);

        return TurtleCompletion.Describe(bytes, bytes.Length, TurtleSyntax.TriG);
    }

    /// <summary>In TriG a statement boundary additionally expects a graph block — the <c>GRAPH</c> keyword or an anonymous block <c>{</c> — alongside the directives and subject terms.</summary>
    [TestMethod]
    public void TrigStatementBoundaryExpectsGraphBlock()
    {
        CompletionContext context = DescribeAtEndTriG("");

        Assert.Contains(TurtleTokenKind.GraphKeyword, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.OpenBrace, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
    }

    /// <summary>In plain Turtle a statement boundary does not offer the TriG graph-block tokens.</summary>
    [TestMethod]
    public void TurtleStatementBoundaryOmitsGraphBlock()
    {
        CompletionContext context = DescribeAtEnd("");

        Assert.DoesNotContain(TurtleTokenKind.GraphKeyword, context.ExpectedTokens);
        Assert.DoesNotContain(TurtleTokenKind.OpenBrace, context.ExpectedTokens);
    }

    /// <summary>Inside a TriG graph block, at a triple boundary, the caret expects another triple subject or the closing <c>}</c>.</summary>
    [TestMethod]
    public void InsideGraphBlockExpectsTripleOrClose()
    {
        CompletionContext context = DescribeAtEndTriG("{ ex:s ex:p ex:o . ");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.GraphBlock, context.EnclosingProductions[^1]);
    }

    /// <summary>After an incomplete <c>GRAPH</c> statement the caret recovers to a statement boundary, which in TriG again offers a graph block.</summary>
    [TestMethod]
    public void AfterGraphKeywordRecoversToTrigStatementBoundary()
    {
        CompletionContext context = DescribeAtEndTriG("GRAPH ");

        Assert.Contains(TurtleTokenKind.GraphKeyword, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.OpenBrace, context.ExpectedTokens);
    }

    /// <summary>Inside a triple term, before the subject, the caret expects a named term (IRI or prefixed name) — the sound, vocabulary-bearing set for a position whose role is not fixed by the frame.</summary>
    [TestMethod]
    public void InsideTripleTermSubjectExpectsNamedTerm()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p <<( ");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.TripleTerm, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a triple term, before the object, the caret expects a named term.</summary>
    [TestMethod]
    public void InsideTripleTermObjectExpectsNamedTerm()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p <<( ex:a ex:b ");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Term, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a reified triple, before the subject, the caret expects a named term.</summary>
    [TestMethod]
    public void InsideReifiedTripleExpectsNamedTerm()
    {
        CompletionContext context = DescribeAtEnd("ex:s ex:p << ");

        Assert.Contains(TurtleTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(TurtleTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.ReifiedTriple, context.EnclosingProductions[^1]);
    }
}
