using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Parser tests for <see cref="SparqlParser"/> covering the first slice: the
/// prologue, the SELECT and ASK forms, dataset clauses, the WHERE group graph
/// pattern, basic graph pattern triples (predicate-object and object lists), term
/// and literal forms, prefix and base IRI resolution, and the LIMIT/OFFSET slice.
/// </summary>
[TestClass]
internal sealed class SparqlParserTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A bare <c>SELECT * WHERE { ?s ?p ?o }</c> yields a star projection over one triple.</summary>
    [TestMethod]
    public void ParsesSelectStarSingleTriple()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o }", pool);

        SelectQuery select = (SelectQuery)query.Form;
        Assert.IsTrue(select.IsStar);
        Assert.IsFalse(select.IsDistinct);
        Assert.IsEmpty(select.Projections);

        IReadOnlyList<TriplePattern> triples = SingleBlockTriples(query);
        Assert.HasCount(1, triples);
        Assert.AreEqual("s", VariableName(triples[0].Subject));
        Assert.AreEqual("p", VariableName(triples[0].Predicate));
        Assert.AreEqual("o", VariableName(triples[0].Object));
    }

    /// <summary>An explicit projection list keeps each variable in order.</summary>
    [TestMethod]
    public void ParsesSelectVariableList()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT ?a ?b WHERE { ?a ?p ?b }", pool);

        SelectQuery select = (SelectQuery)query.Form;
        Assert.IsFalse(select.IsStar);
        Assert.HasCount(2, select.Projections);
        Assert.AreEqual("a", ((SelectVariable)select.Projections[0]).Variable.Name.ToString());
        Assert.AreEqual("b", ((SelectVariable)select.Projections[1]).Variable.Name.ToString());
    }

    /// <summary>The <c>DISTINCT</c> modifier is recorded on the select head.</summary>
    [TestMethod]
    public void ParsesSelectDistinct()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT DISTINCT ?s WHERE { ?s ?p ?o }", pool);

        SelectQuery select = (SelectQuery)query.Form;
        Assert.IsTrue(select.IsDistinct);
        Assert.IsFalse(select.IsReduced);
    }

    /// <summary>An <c>ASK</c> query parses to an <see cref="AskQuery"/> head over its pattern.</summary>
    [TestMethod]
    public void ParsesAsk()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("ASK { ?s ?p ?o }", pool);

        Assert.IsInstanceOfType<AskQuery>(query.Form);
        Assert.HasCount(1, SingleBlockTriples(query));
    }

    /// <summary>An empty group graph pattern produces a group with no members.</summary>
    [TestMethod]
    public void ParsesEmptyGroup()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("ASK { }", pool);

        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        Assert.IsEmpty(group.Members);
    }

    /// <summary>A bound prefix expands a prefixed name to its absolute IRI.</summary>
    [TestMethod]
    public void ResolvesPrefixedName()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "PREFIX ex: <http://example.org/> SELECT * WHERE { ?s ex:knows ?o }",
            pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        Assert.AreEqual("http://example.org/knows", IriValue(triple.Predicate));
    }

    /// <summary>The empty prefix resolves against its <c>PREFIX :</c> binding.</summary>
    [TestMethod]
    public void ResolvesEmptyPrefix()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "PREFIX : <http://example.org/> SELECT * WHERE { ?s :p :Thing }",
            pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        Assert.AreEqual("http://example.org/p", IriValue(triple.Predicate));
        Assert.AreEqual("http://example.org/Thing", IriValue(triple.Object));
    }

    /// <summary>A <c>BASE</c> declaration resolves a relative IRI reference.</summary>
    [TestMethod]
    public void ResolvesRelativeIriAgainstBase()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "BASE <http://example.org/dir/> SELECT * WHERE { <a> <b> <c> }",
            pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        Assert.AreEqual("http://example.org/dir/a", IriValue(triple.Subject));
        Assert.AreEqual("http://example.org/dir/b", IriValue(triple.Predicate));
        Assert.AreEqual("http://example.org/dir/c", IriValue(triple.Object));
    }

    /// <summary>The <c>a</c> shorthand expands to <c>rdf:type</c>.</summary>
    [TestMethod]
    public void ExpandsAShorthandToRdfType()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s a ?type }", pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", IriValue(triple.Predicate));
    }

    /// <summary>A predicate-object list separated by ';' shares the subject across triples.</summary>
    [TestMethod]
    public void ParsesPredicateObjectList()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o ; :q ?r }",
            pool);

        IReadOnlyList<TriplePattern> triples = SingleBlockTriples(query);
        Assert.HasCount(2, triples);
        Assert.AreEqual("http://example.org/p", IriValue(triples[0].Predicate));
        Assert.AreEqual("http://example.org/q", IriValue(triples[1].Predicate));
        Assert.AreEqual("s", VariableName(triples[1].Subject));
    }

    /// <summary>An object list separated by ',' repeats the subject and predicate.</summary>
    [TestMethod]
    public void ParsesObjectList()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?a, ?b }",
            pool);

        IReadOnlyList<TriplePattern> triples = SingleBlockTriples(query);
        Assert.HasCount(2, triples);
        Assert.AreEqual("a", VariableName(triples[0].Object));
        Assert.AreEqual("b", VariableName(triples[1].Object));
    }

    /// <summary>Two triple blocks separated by '.' merge into a single basic graph pattern block.</summary>
    [TestMethod]
    public void MergesContiguousTripleBlocks()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o . ?a ?b ?c }", pool);

        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        Assert.HasCount(1, group.Members);
        Assert.HasCount(2, ((BasicGraphPatternBlock)group.Members[0]).Triples);
    }

    /// <summary>A plain string object is an <c>xsd:string</c> literal carrying its lexical form.</summary>
    [TestMethod]
    public void ParsesPlainStringLiteral()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p \"hello\" }", pool);

        Literal literal = LiteralObject(query);
        Assert.AreEqual("hello", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", literal.Datatype.Iri.ToString());
    }

    /// <summary>A language-tagged string is an <c>rdf:langString</c> with the tag recorded.</summary>
    [TestMethod]
    public void ParsesLanguageTaggedLiteral()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p \"chat\"@en }", pool);

        Literal literal = LiteralObject(query);
        Assert.AreEqual("chat", literal.Value.ToString());
        Assert.AreEqual("en", literal.Language!.Value.ToString());
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", literal.Datatype.Iri.ToString());
    }

    /// <summary>A typed literal carries its explicit datatype IRI.</summary>
    [TestMethod]
    public void ParsesTypedLiteral()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "SELECT * WHERE { ?s ?p \"5\"^^<http://example.org/dt> }",
            pool);

        Literal literal = LiteralObject(query);
        Assert.AreEqual("5", literal.Value.ToString());
        Assert.AreEqual("http://example.org/dt", literal.Datatype.Iri.ToString());
    }

    /// <summary>An integer literal is tagged <c>xsd:integer</c>; a boolean is tagged <c>xsd:boolean</c>.</summary>
    [TestMethod]
    public void ParsesNumericAndBooleanLiterals()
    {
        using Utf8StringPool pool = new();
        SparqlQuery integerQuery = ParseQuery("SELECT * WHERE { ?s ?p 42 }", pool);
        Literal integerLiteral = LiteralObject(integerQuery);
        Assert.AreEqual("42", integerLiteral.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", integerLiteral.Datatype.Iri.ToString());

        SparqlQuery booleanQuery = ParseQuery("SELECT * WHERE { ?s ?p true }", pool);
        Literal booleanLiteral = LiteralObject(booleanQuery);
        Assert.AreEqual("true", booleanLiteral.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#boolean", booleanLiteral.Datatype.Iri.ToString());
    }

    /// <summary>A blank-node label object becomes a <see cref="BlankNode"/> keeping its label.</summary>
    [TestMethod]
    public void ParsesBlankNodeLabel()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p _:b0 }", pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        BlankNode blank = (BlankNode)((ConstantTerm)triple.Object).Term;
        Assert.AreEqual("b0", blank.Label.ToString());
    }

    /// <summary>The LIMIT and OFFSET slice is captured on the solution modifier.</summary>
    [TestMethod]
    public void ParsesLimitAndOffset()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o } LIMIT 10 OFFSET 5", pool);

        Assert.AreEqual(10, query.Modifier.Limit);
        Assert.AreEqual(5, query.Modifier.Offset);
    }

    /// <summary>FROM and FROM NAMED populate the dataset clause.</summary>
    [TestMethod]
    public void ParsesDatasetClauses()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "SELECT * FROM <http://g/> FROM NAMED <http://n/> WHERE { ?s ?p ?o }",
            pool);

        Assert.AreEqual("http://g/", query.Dataset.DefaultGraphs[0].Value.ToString());
        Assert.AreEqual("http://n/", query.Dataset.NamedGraphs[0].Value.ToString());
    }

    /// <summary>The prologue's PREFIX declarations are preserved on the request.</summary>
    [TestMethod]
    public void RecordsPrologueDeclarations()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "PREFIX ex: <http://example.org/> SELECT * WHERE { ?s ex:p ?o }",
            pool);

        Assert.HasCount(1, query.Prologue.Prefixes);
        Assert.AreEqual("ex:", query.Prologue.Prefixes[0].Prefix.ToString());
        Assert.AreEqual("http://example.org/", query.Prologue.Prefixes[0].Namespace.Value.ToString());
    }

    /// <summary>An unbound prefix is a parse error.</summary>
    [TestMethod]
    public void UnboundPrefixThrows()
    {
        using Utf8StringPool pool = new();
        Assert.Throws<SparqlParseException>(() => ParseQuery("SELECT * WHERE { ?s foo:bar ?o }", pool));
    }

    /// <summary>A query form this build does not yet implement reports a parse error.</summary>
    [TestMethod]
    public void ConstructFormNotYetImplementedThrows()
    {
        using Utf8StringPool pool = new();
        Assert.Throws<SparqlParseException>(() => ParseQuery("CONSTRUCT { ?s ?p ?o } WHERE { ?s ?p ?o }", pool));
    }

    /// <summary>Trailing tokens after a complete query are a parse error.</summary>
    [TestMethod]
    public void TrailingTokensThrow()
    {
        using Utf8StringPool pool = new();
        Assert.Throws<SparqlParseException>(() => ParseQuery("ASK { ?s ?p ?o } garbage:here", pool));
    }

    /// <summary>
    /// Feeding tokens one at a time, the parser suspends with <see cref="ParseStatus.NeedMore"/>
    /// before the stream completes and resumes to the same AST a whole-buffer parse produces — proof
    /// the driver is genuinely resumable and does not require the token stream up front.
    /// </summary>
    [TestMethod]
    public void ParsesIncrementallyTokenByToken()
    {
        using Utf8StringPool pool = new();
        const string Text = "PREFIX ex: <http://example.org/> SELECT ?s WHERE { ?s ex:p ?o ; ex:q ?r . ?a ex:b ?c } LIMIT 5";

        List<SparqlToken> tokens = [];
        foreach(SparqlToken token in new SparqlLexer(Encoding.UTF8.GetBytes(Text), pool).Tokenize())
        {
            tokens.Add(token);
        }

        SparqlParser parser = new(pool);
        SparqlRequest? request = null;
        ParseStatus status = ParseStatus.NeedMore;
        bool suspendedBeforeEnd = false;

        for(int i = 0; i < tokens.Count; i++)
        {
            parser.FeedToken(tokens[i]);
            status = parser.TryParseRequest(out request);

            if(status == ParseStatus.NeedMore && i < tokens.Count - 1)
            {
                suspendedBeforeEnd = true;
            }
        }

        Assert.IsTrue(suspendedBeforeEnd, "The parser should suspend with NeedMore before the whole stream is fed.");
        Assert.AreEqual(ParseStatus.Produced, status);

        SparqlQuery query = (SparqlQuery)request!;
        Assert.HasCount(1, ((SelectQuery)query.Form).Projections);
        Assert.AreEqual(5, query.Modifier.Limit);

        //"?s ex:p ?o ; ex:q ?r" is two triples; ". ?a ex:b ?c" is a third; the run is contiguous,
        //so it merges into a single basic graph pattern block of three triples.
        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        Assert.HasCount(1, group.Members);
        Assert.HasCount(3, ((BasicGraphPatternBlock)group.Members[0]).Triples);
    }

    /// <summary>
    /// Lexes and parses <paramref name="text"/> into a <see cref="SparqlQuery"/>.
    /// </summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping the parsed term handles alive for the test's assertions.</param>
    /// <returns>The parsed query.</returns>
    private static SparqlQuery ParseQuery(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);

        return (SparqlQuery)parser.ParseRequest();
    }

    /// <summary>
    /// Returns the triples of the single basic graph pattern block in the query's WHERE group.
    /// </summary>
    /// <param name="query">The parsed query.</param>
    /// <returns>The triple patterns.</returns>
    private static IReadOnlyList<TriplePattern> SingleBlockTriples(SparqlQuery query)
    {
        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;

        return ((BasicGraphPatternBlock)group.Members[0]).Triples;
    }

    /// <summary>
    /// Returns the literal object of the single triple in the query.
    /// </summary>
    /// <param name="query">The parsed query.</param>
    /// <returns>The literal term.</returns>
    private static Literal LiteralObject(SparqlQuery query)
    {
        return (Literal)((ConstantTerm)SingleBlockTriples(query)[0].Object).Term;
    }

    /// <summary>
    /// Returns the variable name of a variable triple-pattern term.
    /// </summary>
    /// <param name="term">The term, expected to be a <see cref="VariableTerm"/>.</param>
    /// <returns>The variable name.</returns>
    private static string VariableName(TriplePatternTerm term)
    {
        return ((VariableTerm)term).Variable.Name.ToString();
    }

    /// <summary>
    /// Returns the absolute IRI of a constant named-node triple-pattern term.
    /// </summary>
    /// <param name="term">The term, expected to be a <see cref="ConstantTerm"/> over a <see cref="NamedNode"/>.</param>
    /// <returns>The IRI.</returns>
    private static string IriValue(TriplePatternTerm term)
    {
        return ((NamedNode)((ConstantTerm)term).Term).Iri.ToString();
    }
}
