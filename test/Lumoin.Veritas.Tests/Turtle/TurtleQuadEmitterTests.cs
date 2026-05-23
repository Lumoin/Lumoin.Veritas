using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Emission;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Turtle;

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

        NamedNode rdfFirst = new(Utf8String.FromString("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"));
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
    public void EmitReifiedTripleProducesAssertedAndReification()
    {
        string turtle = "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> >> .";
        List<EmittedQuad> quads = EmitAll(turtle, TurtleSyntax.Turtle);

        //Asserted inner triple + reifies link + main triple binding subject/predicate to the reifier (3 quads).
        Assert.HasCount(3, quads);

        bool sawReifies = false;
        foreach(EmittedQuad q in quads)
        {
            if(q.Quad.Predicate.Iri.ToString() == "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")
            {
                sawReifies = true;
                break;
            }
        }

        Assert.IsTrue(sawReifies);
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

    private static List<EmittedQuad> EmitAll(string source, TurtleSyntax syntax)
    {
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), syntax);
        TurtleDocument document = parser.Parse();
        TurtleQuadEmitter emitter = new(document, pool);

        List<EmittedQuad> result = [];
        foreach(EmittedQuad q in emitter.Emit())
        {
            result.Add(q);
        }

        return result;
    }
}
