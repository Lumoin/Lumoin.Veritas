using System.Collections.Immutable;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class TurtleParserTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ParseEmptyDocument()
    {
        TurtleDocument document = ParseTurtle(string.Empty);

        Assert.IsEmpty(document.Statements);
    }

    [TestMethod]
    public void ParseSinglePrefixDeclaration()
    {
        TurtleDocument document = ParseTurtle("@prefix ex: <http://example.org/> .");

        Assert.HasCount(1, document.Prefixes);
        Assert.AreEqual("ex", document.Prefixes[0].Prefix.ToString());
        Assert.AreEqual("http://example.org/", document.Prefixes[0].Iri.Value.ToString());
    }

    [TestMethod]
    public void ParseSparqlPrefixDeclaration()
    {
        TurtleDocument document = ParseTurtle("PREFIX ex: <http://example.org/>");

        Assert.HasCount(1, document.Prefixes);
    }

    [TestMethod]
    public void ParseBaseDeclaration()
    {
        TurtleDocument document = ParseTurtle("@base <http://example.org/> .");

        Assert.HasCount(1, document.BaseDeclarations);
    }

    [TestMethod]
    public void ParseVersionDeclaration()
    {
        TurtleDocument document = ParseTurtle("@version \"1.2\" .");

        Assert.HasCount(1, document.Versions);
        Assert.AreEqual("1.2", document.Versions[0].Version.ToString());
    }

    [TestMethod]
    public void AcceptsVersionWithSingleQuotedString()
    {
        TurtleDocument document = ParseTurtle("VERSION '1.2'");

        Assert.HasCount(1, document.Versions);
        Assert.AreEqual("1.2", document.Versions[0].Version.ToString());
    }

    [TestMethod]
    public void AcceptsVersionWithDoubleQuotedString()
    {
        TurtleDocument document = ParseTurtle("VERSION \"1.2\"");

        Assert.HasCount(1, document.Versions);
    }

    [TestMethod]
    public void RejectsVersionWithTripleDoubleQuotedString()
    {
        ParseResult<TurtleDocument> result = ParseTurtleToResult("VERSION \"\"\"1.2\"\"\"");

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Turtle.InvalidVersionArgument));
    }

    [TestMethod]
    public void RejectsVersionWithTripleSingleQuotedString()
    {
        ParseResult<TurtleDocument> result = ParseTurtleToResult("@version '''1.2''' .");

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasCode(result, WellKnownDiagnostics.Turtle.InvalidVersionArgument));
    }

    [TestMethod]
    public void ParseTripleWithAbsoluteIris()
    {
        TurtleDocument document = ParseTurtle("<http://example.org/s> <http://example.org/p> <http://example.org/o> .");

        Assert.HasCount(1, document.Statements);
        TripleStatement triple = (TripleStatement)document.Statements[0];
        Assert.IsInstanceOfType<IriTerm>(triple.Subject);
        Assert.HasCount(1, triple.Predicates);
    }

    [TestMethod]
    public void ParseTripleWithSemicolonSeparator()
    {
        TurtleDocument document = ParseTurtle("<s> <p1> <o1> ; <p2> <o2> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        Assert.HasCount(2, triple.Predicates);
    }

    [TestMethod]
    public void ParseTripleWithCommaSeparator()
    {
        TurtleDocument document = ParseTurtle("<s> <p> <o1> , <o2> , <o3> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        PredicateObject po = triple.Predicates[0];
        Assert.HasCount(3, po.Objects);
    }

    [TestMethod]
    public void ParseRdfTypeShorthand()
    {
        TurtleDocument document = ParseTurtle("<s> a <Type> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        IriTerm predicate = (IriTerm)triple.Predicates[0].Predicate;
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", predicate.Value.ToString());
    }

    [TestMethod]
    public void ParseEmptyCollection()
    {
        TurtleDocument document = ParseTurtle("<s> <p> () .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        CollectionTerm collection = (CollectionTerm)triple.Predicates[0].Objects[0].Object;
        Assert.IsEmpty(collection.Items);
    }

    [TestMethod]
    public void ParseCollectionOfThreeItems()
    {
        TurtleDocument document = ParseTurtle("<s> <p> ( <a> <b> <c> ) .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        CollectionTerm collection = (CollectionTerm)triple.Predicates[0].Objects[0].Object;
        Assert.HasCount(3, collection.Items);
    }

    [TestMethod]
    public void ParseNestedCollection()
    {
        TurtleDocument document = ParseTurtle("<s> <p> ( <a> ( <b> <c> ) <d> ) .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        CollectionTerm outer = (CollectionTerm)triple.Predicates[0].Objects[0].Object;
        CollectionTerm inner = (CollectionTerm)outer.Items[1];
        Assert.HasCount(2, inner.Items);
    }

    [TestMethod]
    public void ParseBlankNodePropertyList()
    {
        TurtleDocument document = ParseTurtle("<s> <p> [ <p1> <o1> ; <p2> <o2> ] .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        BlankNodePropertyListTerm bnpl = (BlankNodePropertyListTerm)triple.Predicates[0].Objects[0].Object;
        Assert.HasCount(2, bnpl.Predicates);
    }

    [TestMethod]
    public void ParseNestedBlankNodePropertyList()
    {
        TurtleDocument document = ParseTurtle("<s> <p> [ <q> [ <r> <o> ] ] .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        BlankNodePropertyListTerm outer = (BlankNodePropertyListTerm)triple.Predicates[0].Objects[0].Object;
        BlankNodePropertyListTerm inner = (BlankNodePropertyListTerm)outer.Predicates[0].Objects[0].Object;
        Assert.HasCount(1, inner.Predicates);
    }

    [TestMethod]
    public void ParseLanguageTaggedLiteral()
    {
        TurtleDocument document = ParseTurtle("<s> <p> \"hello\"@en .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        LiteralTerm literal = (LiteralTerm)triple.Predicates[0].Objects[0].Object;
        Assert.AreEqual("hello", literal.Value.ToString());
        Assert.AreEqual("en", literal.Language!.Value.ToString());
    }

    [TestMethod]
    public void ParseDirectionTaggedLiteral()
    {
        TurtleDocument document = ParseTurtle("<s> <p> \"hello\"@en--rtl .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        LiteralTerm literal = (LiteralTerm)triple.Predicates[0].Objects[0].Object;
        Assert.AreEqual(TextDirection.Rtl, literal.Direction);
    }

    [TestMethod]
    public void ParseTypedLiteral()
    {
        TurtleDocument document = ParseTurtle("<s> <p> \"5\"^^<http://www.w3.org/2001/XMLSchema#integer> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        LiteralTerm literal = (LiteralTerm)triple.Predicates[0].Objects[0].Object;
        IriTerm dt = (IriTerm)literal.Datatype!;
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", dt.Value.ToString());
    }

    [TestMethod]
    public void ParseTripleTerm()
    {
        TurtleDocument document = ParseTurtle("<s> <p> <<( <a> <b> <c> )>> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        TripleTermTerm tt = (TripleTermTerm)triple.Predicates[0].Objects[0].Object;
        Assert.IsInstanceOfType<IriTerm>(tt.Subject);
    }

    [TestMethod]
    public void ParseReifiedTripleAsObject()
    {
        TurtleDocument document = ParseTurtle("<s> <p> << <a> <b> <c> >> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        ReifiedTripleTerm rt = (ReifiedTripleTerm)triple.Predicates[0].Objects[0].Object;
        Assert.IsNull(rt.Reifier);
    }

    [TestMethod]
    public void ParseReifiedTripleWithNamedReifier()
    {
        TurtleDocument document = ParseTurtle("<s> <p> << <a> <b> <c> ~ <reifier> >> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        ReifiedTripleTerm rt = (ReifiedTripleTerm)triple.Predicates[0].Objects[0].Object;
        Assert.IsNotNull(rt.Reifier);
    }

    [TestMethod]
    public void ParseAnnotationOnObject()
    {
        TurtleDocument document = ParseTurtle("<s> <p> <o> ~ <reifier> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        AnnotatedObject annotated = triple.Predicates[0].Objects[0];
        Assert.HasCount(1, annotated.Annotations);
        Assert.IsInstanceOfType<ReifierAnnotation>(annotated.Annotations[0]);
    }

    [TestMethod]
    public void ParseAnnotationBlock()
    {
        TurtleDocument document = ParseTurtle("<s> <p> <o> {| <meta> <value> |} .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        AnnotatedObject annotated = triple.Predicates[0].Objects[0];
        AnnotationBlock block = (AnnotationBlock)annotated.Annotations[0];
        Assert.HasCount(1, block.Predicates);
    }

    [TestMethod]
    public void EveryNodeHasUniqueId()
    {
        TurtleDocument document = ParseTurtle("<s> <p> ( <a> <b> ) .");

        HashSet<int> ids = [];
        foreach(KeyValuePair<int, TurtleAstNode> entry in document.Nodes)
        {
            Assert.IsTrue(ids.Add(entry.Key), "Duplicate node id");
        }
    }

    [TestMethod]
    public void TrigGraphBlockWithKeyword()
    {
        TurtleDocument document = ParseTriG("GRAPH <g> { <s> <p> <o> . }");

        Assert.HasCount(1, document.Statements);
        GraphBlockStatement graph = (GraphBlockStatement)document.Statements[0];
        Assert.IsTrue(graph.HasGraphKeyword);
        Assert.HasCount(1, graph.Triples);
    }

    [TestMethod]
    public void TrigGraphBlockWithoutKeyword()
    {
        TurtleDocument document = ParseTriG("<g> { <s> <p> <o> . }");

        GraphBlockStatement graph = (GraphBlockStatement)document.Statements[0];
        Assert.IsFalse(graph.HasGraphKeyword);
    }

    [TestMethod]
    public void TrigGraphBlockAllowsOmittedFinalPeriod()
    {
        //The trailing '.' of the last triple in a graph block is optional; '}' terminates it.
        TurtleDocument document = ParseTriG("<g> { <s> <p> <o> }");

        GraphBlockStatement graph = (GraphBlockStatement)document.Statements[0];
        Assert.HasCount(1, graph.Triples);
    }

    [TestMethod]
    public void TrigDefaultGraphTriplesMixWithNamedGraph()
    {
        TurtleDocument document = ParseTriG("<s1> <p> <o> . <g> { <s2> <p> <o> . }");

        Assert.HasCount(2, document.Statements);
        Assert.IsInstanceOfType<TripleStatement>(document.Statements[0]);
        Assert.IsInstanceOfType<GraphBlockStatement>(document.Statements[1]);
    }

    [TestMethod]
    public void TripleStatementInTurtleHasNonDefaultSpan()
    {
        TurtleDocument document = ParseTurtle("<s> <p> <o> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        Assert.AreNotEqual(SourceSpan.None, triple.Span);
    }

    [TestMethod]
    public void DocumentGetNodeResolvesByNodeId()
    {
        TurtleDocument document = ParseTurtle("<s> <p> <o> .");

        TripleStatement triple = (TripleStatement)document.Statements[0];
        TurtleAstNode? lookup = document.GetNode(triple.NodeId);
        Assert.AreSame(triple, lookup);
    }

    [TestMethod]
    public void GraphBlockRejectedInPlainTurtle()
    {
        ParseResult<TurtleDocument> result = ParseTurtleToResult("<g> { <s> <p> <o> . }");

        Assert.IsTrue(result.HasErrors);
    }

    private static TurtleDocument ParseTurtle(string source)
    {
        return Parse(source, TurtleSyntax.Turtle);
    }

    private static TurtleDocument ParseTriG(string source)
    {
        return Parse(source, TurtleSyntax.TriG);
    }

    private static TurtleDocument Parse(string source, TurtleSyntax syntax)
    {
        //Pool lifetime matches the returned document — caller treats document as the owning unit.
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), syntax);

        return parser.Parse();
    }

    private static ParseResult<TurtleDocument> ParseTurtleToResult(string source)
    {
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), TurtleSyntax.Turtle);

        return parser.ParseToResult();
    }

    private static bool HasCode(ParseResult<TurtleDocument> result, Utf8String code)
    {
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            if(diagnostic.Code.Equals(code))
            {
                return true;
            }
        }

        return false;
    }
}
