using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Emission;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class TurtleQuadEmitterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmitSingleTriple()
    {
        List<EmittedQuad> quads = EmitAll("<http://example.org/s> <http://example.org/p> <http://example.org/o> .", TurtleSyntax.Turtle);

        Assert.HasCount(1, quads);
        NamedNode subject = (NamedNode)quads[0].Quad.Subject;
        Assert.AreEqual("http://example.org/s", subject.Iri.ToString());
    }

    [TestMethod]
    public void EmitPrefixedNameExpands()
    {
        string turtle = "@prefix ex: <http://example.org/> . ex:s ex:p ex:o .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        NamedNode subject = (NamedNode)quads[0].Quad.Subject;
        Assert.AreEqual("http://example.org/s", subject.Iri.ToString());
    }

    [TestMethod]
    public void EmitCollectionExpansion()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> ( <http://example.org/a> <http://example.org/b> ) .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //One triple for s p list-head; two rdf:first + two rdf:rest links.
        Assert.HasCount(5, quads);

        NamedNode rdfFirst = new(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"));
        int firstCount = 0;
        foreach(EmittedQuad q in quads)
        {
            if(q.Quad.Predicate.Iri.Equals(rdfFirst.Iri))
            {
                firstCount++;
            }
        }

        Assert.AreEqual(2, firstCount);
    }

    [TestMethod]
    public void EmitBlankNodePropertyListExpansion()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> [ <http://example.org/q> <http://example.org/o> ] .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //Expansion produces an inner triple (bnode q o) and an outer triple (s p bnode); auxiliary quads emit before the main triple.
        Assert.HasCount(2, quads);
        Assert.IsInstanceOfType<BlankNode>(quads[0].Quad.Subject);
        Assert.IsInstanceOfType<BlankNode>(quads[1].Quad.Object);
    }

    [TestMethod]
    public void EmitTripleTermAsObject()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<TripleTerm>(quads[0].Quad.Object);
    }

    [TestMethod]
    public void EmitReifiedTripleProducesReificationWithoutAssertingInnerTriple()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> >> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //A reified triple as an object yields the main statement (s p reifier) and the
        //reification (reifier rdf:reifies <<( a b c )>>); the inner triple is not asserted.
        Assert.HasCount(2, quads);

        bool sawReifies = false;
        bool sawInnerAssertion = false;
        foreach(EmittedQuad q in quads)
        {
            if(q.Quad.Predicate.Iri.ToString() == "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")
            {
                sawReifies = true;
            }

            if(q.Quad.Predicate.Iri.ToString() == "http://example.org/b")
            {
                sawInnerAssertion = true;
            }
        }

        Assert.IsTrue(sawReifies);
        Assert.IsFalse(sawInnerAssertion, "The inner triple of a reified triple must not be asserted.");
    }

    [TestMethod]
    public void EmitAnnotationOnObjectProducesReification()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <http://example.org/o> ~ <http://example.org/r> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //Main triple + one reification (r rdf:reifies <<( s p o )>>).
        Assert.HasCount(2, quads);
    }

    [TestMethod]
    public void EmitAnnotationBlockExpandsPredicates()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <http://example.org/o> {| <http://example.org/m> <http://example.org/v> |} .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //Main triple + reification + the annotation predicate.
        Assert.HasCount(3, quads);
    }

    [TestMethod]
    public void EmitTrigNamedGraph()
    {
        string trig = "<http://example.org/g> { <http://example.org/s> <http://example.org/p> <http://example.org/o> . }";
        List<EmittedQuad> quads = EmitAll(trig, TurtleSyntax.TriG);

        Assert.HasCount(1, quads);
        NamedNode graph = (NamedNode)quads[0].Quad.Graph!;
        Assert.AreEqual("http://example.org/g", graph.Iri.ToString());
    }

    [TestMethod]
    public void EmittedQuadCarriesSourceReference()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <http://example.org/o> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        Assert.IsNotNull(quads[0].Source);
    }

    [TestMethod]
    public void EmitsReificationForReifiedTripleWithIriReifier()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> ~ <http://example.org/r> >> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        EmittedQuad reification = FindReifies(quads);
        Assert.AreEqual("http://example.org/r", ((NamedNode)reification.Quad.Subject).Iri.ToString());
        Assert.IsInstanceOfType<TripleTerm>(reification.Quad.Object);
    }

    [TestMethod]
    public void EmitsReificationForReifiedTripleWithBlankNodeReifier()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> ~ _:r >> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        EmittedQuad reification = FindReifies(quads);
        Assert.AreEqual("r", ((BlankNode)reification.Quad.Subject).Label.ToString());
    }

    [TestMethod]
    public void AllocatesFreshBlankNodeForBareReifier()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <http://example.org/o> ~ .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        EmittedQuad reification = FindReifies(quads);
        Assert.IsInstanceOfType<BlankNode>(reification.Quad.Subject);
    }

    [TestMethod]
    public void DoesNotEmitReificationForTripleTermSyntax()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<TripleTerm>(quads[0].Quad.Object);
        foreach(EmittedQuad q in quads)
        {
            Assert.AreNotEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies", q.Quad.Predicate.Iri.ToString());
        }
    }

    [TestMethod]
    public void HandlesMultipleAnnotationBlocks()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> <http://example.org/o> {| <http://example.org/m1> <http://example.org/v1> |} {| <http://example.org/m2> <http://example.org/v2> |} .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //Main triple + two reifications + two annotation predicates.
        Assert.HasCount(5, quads);
    }

    [TestMethod]
    public void HandlesNestedAnnotationBlock()
    {
        //The inner block annotates the outer block's annotation triple (reifier :a :b).
        string turtle = "<http://example.org/s> <http://example.org/p> <http://example.org/o> {| <http://example.org/a> <http://example.org/b> {| <http://example.org/a2> <http://example.org/b2> |} |} .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //Main triple, outer reification, outer :a :b, inner reification, inner :a2 :b2.
        Assert.HasCount(5, quads);

        int reifiesCount = 0;
        bool sawInnerAnnotation = false;
        foreach(EmittedQuad q in quads)
        {
            if(q.Quad.Predicate.Iri.ToString() == "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")
            {
                reifiesCount++;
            }

            if(q.Quad.Predicate.Iri.ToString() == "http://example.org/a2")
            {
                sawInnerAnnotation = true;
            }
        }

        Assert.AreEqual(2, reifiesCount, "Expected two reification quads (outer and inner).");
        Assert.IsTrue(sawInnerAnnotation, "Expected the nested annotation predicate to be emitted.");
    }

    private static EmittedQuad FindReifies(List<EmittedQuad> quads)
    {
        foreach(EmittedQuad q in quads)
        {
            if(q.Quad.Predicate.Iri.ToString() == "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")
            {
                return q;
            }
        }

        Assert.Fail("No rdf:reifies quad was emitted.");

        return default!;
    }

    private static List<EmittedQuad> EmitAll(string source, TurtleSyntax syntax)
    {
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), syntax);
        TurtleDocument document = parser.Parse();
        TurtleQuadEmitter emitter = new(document, pool, new DiagnosticBag());

        List<EmittedQuad> result = [];
        foreach(EmittedQuad q in emitter.Emit())
        {
            result.Add(q);
        }

        return result;
    }
}
