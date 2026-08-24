using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Completion;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Turtle;

/// <summary>
/// The Turtle / TriG completion-context wire writer and the syntax-token mapping that rides the same wire:
/// the caret byte offset with the expected token kinds and the enclosing productions as their own names,
/// pinned as whole documents; and the editor's syntax token resolving to the parser flavour, with anything
/// the token vocabulary does not name falling back to Turtle rather than refusing.
/// </summary>
[TestClass]
internal sealed class TurtleCompletionJsonTests
{
    /// <summary>An empty context renders the three members with empty arrays.</summary>
    [TestMethod]
    public void AnEmptyContextRendersEveryMemberEmpty()
    {
        string json = TurtleCompletionJson.Write(new CompletionContext(0, [], []));

        Assert.AreEqual("{\"caret\":0,\"expectedTokens\":[],\"enclosingProductions\":[]}", json);
    }

    /// <summary>A populated context renders the caret, the expected tokens, and the production chain in order.</summary>
    [TestMethod]
    public void APopulatedContextRendersEveryAxis()
    {
        CompletionContext context = new(
            5,
            [TurtleTokenKind.Iri, TurtleTokenKind.A],
            [ParseFrameKind.Statement, ParseFrameKind.PredicateObjectList]);

        string json = TurtleCompletionJson.Write(context);

        Assert.AreEqual("{\"caret\":5,\"expectedTokens\":[\"Iri\",\"A\"],\"enclosingProductions\":[\"Statement\",\"PredicateObjectList\"]}", json);
    }

    /// <summary>The TriG token resolves to the TriG flavour.</summary>
    [TestMethod]
    public void TheTriGTokenResolvesToTheTriGFlavour()
    {
        Assert.AreEqual(TurtleSyntax.TriG, TurtleCompletionJson.ParseSyntax("trig"));
    }

    /// <summary>Every token the vocabulary does not name resolves to Turtle, the flavour a plain buffer parses as.</summary>
    /// <param name="syntax">The syntax token the editor sent.</param>
    [TestMethod]
    [DataRow("turtle")]
    [DataRow("TriG")]
    [DataRow("shacl")]
    [DataRow("")]
    public void AnUnnamedTokenResolvesToTurtle(string syntax)
    {
        Assert.AreEqual(TurtleSyntax.Turtle, TurtleCompletionJson.ParseSyntax(syntax));
    }
}
