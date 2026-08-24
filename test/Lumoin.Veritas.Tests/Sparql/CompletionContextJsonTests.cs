using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// The SPARQL completion-context wire writer: the caret byte offset, the expected token kinds and the
/// enclosing productions as their own names, the in-scope variables with their resolved datatype and its
/// provenance, and the variable-to-predicate pairs. Every row pins the whole document, so the shape an editor
/// popup reads is a fact rather than a substring; an unresolved datatype answers the JSON null, never an
/// empty string.
/// </summary>
[TestClass]
internal sealed class CompletionContextJsonTests
{
    /// <summary>An empty context renders the five members with empty arrays.</summary>
    [TestMethod]
    public void AnEmptyContextRendersEveryMemberEmpty()
    {
        string json = CompletionContextJson.Write(new CompletionContext(0, [], [], [], []));

        Assert.AreEqual("{\"caret\":0,\"expectedTokens\":[],\"enclosingProductions\":[],\"inScopeVariables\":[],\"variablePredicates\":[]}", json);
    }

    /// <summary>A populated context renders every axis in order, the datatype and its source among them.</summary>
    [TestMethod]
    public void APopulatedContextRendersEveryAxis()
    {
        CompletionContext context = new(
            12,
            [new ScopeVariable(new SparqlVariable(Utf8Strings.From("age")), Vocabulary.Xsd.Integer, DatatypeSource.RdfsRange)],
            [SparqlTokenKind.Variable, SparqlTokenKind.Iri],
            [ParseFrameKind.Request, ParseFrameKind.WhereClause],
            [new VariablePredicate(new SparqlVariable(Utf8Strings.From("age")), Utf8Strings.From("http://example.org/age"), TermPosition.Object)]);

        string json = CompletionContextJson.Write(context);

        Assert.AreEqual(
            "{\"caret\":12,\"expectedTokens\":[\"Variable\",\"Iri\"],\"enclosingProductions\":[\"Request\",\"WhereClause\"],"
            + "\"inScopeVariables\":[{\"name\":\"age\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"datatypeSource\":\"RdfsRange\"}],"
            + "\"variablePredicates\":[{\"variable\":\"age\",\"predicate\":\"http://example.org/age\",\"position\":\"Object\"}]}",
            json);
    }

    /// <summary>A variable whose datatype no resolver reached renders the JSON null and the unknown provenance.</summary>
    [TestMethod]
    public void AnUnresolvedDatatypeRendersNull()
    {
        CompletionContext context = new(
            3,
            [new ScopeVariable(new SparqlVariable(Utf8Strings.From("s")), null, DatatypeSource.Unknown)],
            [],
            [],
            []);

        string json = CompletionContextJson.Write(context);

        Assert.AreEqual(
            "{\"caret\":3,\"expectedTokens\":[],\"enclosingProductions\":[],"
            + "\"inScopeVariables\":[{\"name\":\"s\",\"datatype\":null,\"datatypeSource\":\"Unknown\"}],\"variablePredicates\":[]}",
            json);
    }

    /// <summary>A variable name carrying a JSON metacharacter escapes per RFC 8259, so a buffer can never break the document.</summary>
    [TestMethod]
    public void AVariableNameEscapesItsMetacharacters()
    {
        CompletionContext context = new(
            1,
            [new ScopeVariable(new SparqlVariable(Utf8Strings.From("a\"b\\c")), null, DatatypeSource.Unknown)],
            [],
            [],
            []);

        string json = CompletionContextJson.Write(context);

        Assert.Contains("\"name\":\"a\\\"b\\\\c\"", json);
    }
}
