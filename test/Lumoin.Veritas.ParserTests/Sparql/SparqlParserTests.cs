using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;
using CoreTripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.ParserTests.Sparql;

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

    /// <summary>A VERSION declaration is recorded on the prologue with its short-string label.</summary>
    [TestMethod]
    public void RecordsVersionDeclaration()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("VERSION \"1.2\" ASK { ?s ?p ?o }", pool);

        Assert.HasCount(1, query.Prologue.Versions);
        Assert.AreEqual("1.2", query.Prologue.Versions[0].Version.ToString());
    }

    /// <summary>A long (triple-quoted) VERSION argument is rejected.</summary>
    [TestMethod]
    public void RejectsLongStringVersionArgument()
    {
        using Utf8StringPool pool = new();
        AssertParseError("VERSION \"\"\"1.2\"\"\" ASK { ?s ?p ?o }", pool, WellKnownDiagnostics.Sparql.InvalidVersionArgument);
    }

    /// <summary>An unbound prefix is a parse error.</summary>
    [TestMethod]
    public void UnboundPrefixThrows()
    {
        using Utf8StringPool pool = new();
        AssertParseError("SELECT * WHERE { ?s foo:bar ?o }", pool, WellKnownDiagnostics.Sparql.UnboundPrefix);
    }

    /// <summary>A CONSTRUCT query records its template triples and its WHERE pattern.</summary>
    [TestMethod]
    public void ParsesConstruct()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> CONSTRUCT { ?s :p ?o } WHERE { ?s :q ?o }", pool);

        ConstructQuery construct = (ConstructQuery)query.Form;
        Assert.HasCount(1, construct.Template);
        Assert.AreEqual("http://e/p", ((NamedNode)((ConstantTerm)construct.Template[0].Predicate).Term).Iri.ToString());
        Assert.IsInstanceOfType<GroupGraphPattern>(query.Where.Pattern);
    }

    /// <summary>A CONSTRUCT template admits collection and blank-node-list sugar.</summary>
    [TestMethod]
    public void ParsesConstructTemplateWithSugar()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> CONSTRUCT { ?s :p (1 2) } WHERE { ?s ?q ?o }", pool);

        ConstructQuery construct = (ConstructQuery)query.Form;
        Assert.IsInstanceOfType<CollectionTerm>(construct.Template[0].Object);
    }

    /// <summary>The CONSTRUCT WHERE short form uses the WHERE triples as the template.</summary>
    [TestMethod]
    public void ParsesConstructWhereShortForm()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> CONSTRUCT WHERE { ?s :p ?o }", pool);

        ConstructQuery construct = (ConstructQuery)query.Form;
        Assert.HasCount(1, construct.Template);
        Assert.AreEqual("http://e/p", ((NamedNode)((ConstantTerm)construct.Template[0].Predicate).Term).Iri.ToString());
        Assert.HasCount(1, ((BasicGraphPatternBlock)((GroupGraphPattern)query.Where.Pattern).Members[0]).Triples);
    }

    /// <summary>A DESCRIBE query records its IRI and variable targets.</summary>
    [TestMethod]
    public void ParsesDescribeTargets()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("DESCRIBE <http://x/> ?y WHERE { ?y ?p ?o }", pool);

        DescribeQuery describe = (DescribeQuery)query.Form;
        Assert.IsFalse(describe.IsStar);
        Assert.HasCount(2, describe.Targets);
        Assert.AreEqual("http://x/", ((DescribeIri)describe.Targets[0]).Iri.Value.ToString());
        Assert.AreEqual("y", ((DescribeVariable)describe.Targets[1]).Variable.Name.ToString());
    }

    /// <summary>A DESCRIBE * query is flagged as star with no explicit targets.</summary>
    [TestMethod]
    public void ParsesDescribeStar()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("DESCRIBE * WHERE { ?s ?p ?o }", pool);

        DescribeQuery describe = (DescribeQuery)query.Form;
        Assert.IsTrue(describe.IsStar);
        Assert.IsEmpty(describe.Targets);
    }

    /// <summary>A DESCRIBE without a WHERE clause yields an empty group, and trailing modifiers still parse.</summary>
    [TestMethod]
    public void ParsesDescribeWithoutWhere()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("DESCRIBE ?x LIMIT 5", pool);

        DescribeQuery describe = (DescribeQuery)query.Form;
        Assert.HasCount(1, describe.Targets);
        Assert.IsEmpty(((GroupGraphPattern)query.Where.Pattern).Members);
        Assert.AreEqual(5, query.Modifier.Limit);
    }

    /// <summary>Trailing tokens after a complete query are a parse error.</summary>
    [TestMethod]
    public void TrailingTokensThrow()
    {
        using Utf8StringPool pool = new();
        AssertParseError("ASK { ?s ?p ?o } garbage:here", pool, WellKnownDiagnostics.Sparql.ExpectedEndOfQuery);
    }

    /// <summary>A nested group graph pattern is preserved as a member of the enclosing group.</summary>
    [TestMethod]
    public void ParsesNestedGroup()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("ASK { { ?s ?p ?o } }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.HasCount(1, members);
        GroupGraphPattern inner = (GroupGraphPattern)members[0];
        Assert.HasCount(1, ((BasicGraphPatternBlock)inner.Members[0]).Triples);
    }

    /// <summary>An <c>OPTIONAL</c> follows a leading basic graph pattern as a distinct member.</summary>
    [TestMethod]
    public void ParsesOptional()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o OPTIONAL { ?s ?q ?r } }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.HasCount(2, members);
        Assert.IsInstanceOfType<BasicGraphPatternBlock>(members[0]);
        OptionalPattern optional = (OptionalPattern)members[1];
        GroupGraphPattern inner = (GroupGraphPattern)optional.Inner;
        Assert.HasCount(1, ((BasicGraphPatternBlock)inner.Members[0]).Triples);
    }

    /// <summary>A <c>MINUS</c> member wraps its inner group.</summary>
    [TestMethod]
    public void ParsesMinus()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o MINUS { ?s ?q ?r } }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.HasCount(2, members);
        Assert.IsInstanceOfType<MinusPattern>(members[1]);
    }

    /// <summary>A two-arm <c>UNION</c> produces a single union member over two groups.</summary>
    [TestMethod]
    public void ParsesUnion()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { { ?s ?p ?o } UNION { ?a ?b ?c } }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.HasCount(1, members);
        UnionPattern union = (UnionPattern)members[0];
        Assert.IsInstanceOfType<GroupGraphPattern>(union.Left);
        Assert.IsInstanceOfType<GroupGraphPattern>(union.Right);
    }

    /// <summary>A three-arm <c>UNION</c> nests left-associatively.</summary>
    [TestMethod]
    public void ParsesLeftAssociativeUnionChain()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("ASK { { ?s ?p ?o } UNION { ?a ?b ?c } UNION { ?x ?y ?z } }", pool);

        UnionPattern outer = (UnionPattern)Members(query)[0];
        Assert.IsInstanceOfType<UnionPattern>(outer.Left);
        Assert.IsInstanceOfType<GroupGraphPattern>(outer.Right);
    }

    /// <summary>A <c>GRAPH</c> with an IRI designator wraps its inner group.</summary>
    [TestMethod]
    public void ParsesGraphWithIri()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { GRAPH <http://g/> { ?s ?p ?o } }", pool);

        GraphGraphPattern graph = (GraphGraphPattern)Members(query)[0];
        Assert.AreEqual("http://g/", ((GraphIriTerm)graph.GraphTerm).Iri.Value.ToString());
    }

    /// <summary>A <c>GRAPH</c> with a variable designator carries the variable.</summary>
    [TestMethod]
    public void ParsesGraphWithVariable()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { GRAPH ?g { ?s ?p ?o } }", pool);

        GraphGraphPattern graph = (GraphGraphPattern)Members(query)[0];
        Assert.AreEqual("g", ((GraphVariableTerm)graph.GraphTerm).Variable.Name.ToString());
    }

    /// <summary>A <c>SERVICE</c> records its endpoint and is not silent by default.</summary>
    [TestMethod]
    public void ParsesService()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { SERVICE <http://e/> { ?s ?p ?o } }", pool);

        ServicePattern service = (ServicePattern)Members(query)[0];
        Assert.IsFalse(service.IsSilent);
        Assert.AreEqual("http://e/", ((GraphIriTerm)service.Endpoint).Iri.Value.ToString());
    }

    /// <summary>A <c>SERVICE SILENT</c> with a variable endpoint records the silent flag.</summary>
    [TestMethod]
    public void ParsesSilentServiceWithVariableEndpoint()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { SERVICE SILENT ?e { ?s ?p ?o } }", pool);

        ServicePattern service = (ServicePattern)Members(query)[0];
        Assert.IsTrue(service.IsSilent);
        Assert.AreEqual("e", ((GraphVariableTerm)service.Endpoint).Variable.Name.ToString());
    }

    /// <summary>A non-triple member splits surrounding triples into separate basic graph pattern blocks.</summary>
    [TestMethod]
    public void MemberBreaksBasicGraphPatternRun()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o . OPTIONAL { ?x ?y ?z } . ?a ?b ?c }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.HasCount(3, members);
        Assert.IsInstanceOfType<BasicGraphPatternBlock>(members[0]);
        Assert.IsInstanceOfType<OptionalPattern>(members[1]);
        Assert.IsInstanceOfType<BasicGraphPatternBlock>(members[2]);
    }

    /// <summary>An inline one-variable VALUES block is a member with one row per value.</summary>
    [TestMethod]
    public void ParsesInlineValues()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("ASK { VALUES ?x { 1 2 } }", pool);

        ValuesPattern values = (ValuesPattern)Members(query)[0];
        Assert.HasCount(1, values.Data.Variables);
        Assert.AreEqual("x", values.Data.Variables[0].Name.ToString());
        Assert.HasCount(2, values.Data.Rows);
    }

    /// <summary>A trailing full-form VALUES block records its variables, rows, and UNDEF holes.</summary>
    [TestMethod]
    public void ParsesTrailingValuesWithUndef()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o } VALUES (?a ?b) { (1 2) (3 UNDEF) }", pool);

        ValuesClause values = query.Values!;
        Assert.HasCount(2, values.Variables);
        Assert.HasCount(2, values.Rows);
        Assert.IsNotNull(values.Rows[0][0]);
        Assert.IsNull(values.Rows[1][1]);
    }

    /// <summary>A triple term in a one-variable VALUES block parses to a ground Core triple-term value.</summary>
    [TestMethod]
    public void ParsesTripleTermInValues()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { VALUES ?x { <<( :s :p :o )>> } }", pool);

        ValuesPattern values = (ValuesPattern)Members(query)[0];
        CoreTripleTerm value = (CoreTripleTerm)values.Data.Rows[0][0]!;
        Assert.AreEqual("http://e/s", ((NamedNode)value.Subject).Iri.ToString());
        Assert.AreEqual("http://e/p", value.Predicate.Iri.ToString());
        Assert.AreEqual("http://e/o", ((NamedNode)value.Object).Iri.ToString());
    }

    /// <summary>A VALUES triple term admits a nested triple term in its object position.</summary>
    [TestMethod]
    public void ParsesNestedTripleTermInValues()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { VALUES ?x { <<( :x :q <<( :s :p :o )>> )>> } }", pool);

        ValuesPattern values = (ValuesPattern)Members(query)[0];
        CoreTripleTerm outer = (CoreTripleTerm)values.Data.Rows[0][0]!;
        Assert.IsInstanceOfType<CoreTripleTerm>(outer.Object);
    }

    /// <summary>A VALUES triple term rejects a variable in its object position (the data form is variable-free).</summary>
    [TestMethod]
    public void TripleTermInValuesRejectsVariable()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { VALUES ?x { <<( :s :p ?o )>> } }", pool, WellKnownDiagnostics.Sparql.InvalidTripleTermObject);
    }

    /// <summary>A FILTER member carries its constraint expression with comparison operands.</summary>
    [TestMethod]
    public void ParsesFilterComparison()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("ASK { ?s ?p ?o FILTER(?o > 5) }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.IsInstanceOfType<BasicGraphPatternBlock>(members[0]);
        ComparisonExpression comparison = (ComparisonExpression)((FilterPattern)members[1]).Expression;
        Assert.AreEqual(ComparisonOp.GreaterThan, comparison.Op);
        Assert.AreEqual("o", ((VariableExpression)comparison.Left).Variable.Name.ToString());
        Assert.AreEqual("5", ((Literal)((ConstantExpression)comparison.Right).Value).Value.ToString());
    }

    /// <summary>Multiplication binds tighter than addition, and arithmetic binds tighter than comparison.</summary>
    [TestMethod]
    public void RespectsArithmeticPrecedence()
    {
        using Utf8StringPool pool = new();
        ComparisonExpression comparison = (ComparisonExpression)FilterExpression("ASK { FILTER(?a + ?b * ?c = ?d) }", pool);

        Assert.AreEqual(ComparisonOp.Equal, comparison.Op);
        ArithmeticExpression addition = (ArithmeticExpression)comparison.Left;
        Assert.AreEqual(ArithmeticOp.Add, addition.Op);
        Assert.AreEqual("a", ((VariableExpression)addition.Left).Variable.Name.ToString());
        ArithmeticExpression product = (ArithmeticExpression)addition.Right!;
        Assert.AreEqual(ArithmeticOp.Multiply, product.Op);
    }

    /// <summary>Conjunction binds tighter than disjunction.</summary>
    [TestMethod]
    public void RespectsLogicalPrecedence()
    {
        using Utf8StringPool pool = new();
        OrExpression disjunction = (OrExpression)FilterExpression("ASK { FILTER(?a && ?b || ?c) }", pool);

        Assert.IsInstanceOfType<AndExpression>(disjunction.Left);
        Assert.AreEqual("c", ((VariableExpression)disjunction.Right).Variable.Name.ToString());
    }

    /// <summary>A bracketed sub-expression overrides operator precedence.</summary>
    [TestMethod]
    public void BracketsOverridePrecedence()
    {
        using Utf8StringPool pool = new();
        ComparisonExpression comparison = (ComparisonExpression)FilterExpression("ASK { FILTER((?a + ?b) * ?c = ?d) }", pool);

        ArithmeticExpression product = (ArithmeticExpression)comparison.Left;
        Assert.AreEqual(ArithmeticOp.Multiply, product.Op);
        Assert.AreEqual(ArithmeticOp.Add, ((ArithmeticExpression)product.Left).Op);
    }

    /// <summary>The logical-negation unary operator wraps its operand.</summary>
    [TestMethod]
    public void ParsesUnaryNot()
    {
        using Utf8StringPool pool = new();
        ExpressionNode expression = FilterExpression("ASK { FILTER(!?a) }", pool);

        Assert.IsInstanceOfType<NotExpression>(expression);
    }

    /// <summary>Unary minus binds tighter than the surrounding comparison and carries no right operand.</summary>
    [TestMethod]
    public void ParsesUnaryMinus()
    {
        using Utf8StringPool pool = new();
        ComparisonExpression comparison = (ComparisonExpression)FilterExpression("ASK { FILTER(-?x = ?y) }", pool);

        ArithmeticExpression negation = (ArithmeticExpression)comparison.Left;
        Assert.AreEqual(ArithmeticOp.UnaryMinus, negation.Op);
        Assert.IsNull(negation.Right);
    }

    /// <summary>SPARQL comparison operators do not chain.</summary>
    [TestMethod]
    public void ChainedComparisonThrows()
    {
        using Utf8StringPool pool = new();
        AssertParseError("ASK { FILTER(?a < ?b < ?c) }", pool, WellKnownDiagnostics.Sparql.UnexpectedToken);
    }

    /// <summary>A BIND member binds an expression to a variable.</summary>
    [TestMethod]
    public void ParsesBind()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o BIND(?o AS ?x) }", pool);

        BindPattern bind = (BindPattern)Members(query)[1];
        Assert.AreEqual("x", bind.AsVariable.Name.ToString());
        Assert.AreEqual("o", ((VariableExpression)bind.Expression).Variable.Name.ToString());
    }

    /// <summary>A built-in call carries its canonical name and arguments.</summary>
    [TestMethod]
    public void ParsesBuiltInCall()
    {
        using Utf8StringPool pool = new();
        BuiltInCallExpression call = (BuiltInCallExpression)FilterExpression("ASK { FILTER(STR(?x)) }", pool);

        Assert.AreEqual(BuiltInFunction.Str, call.Function);
        Assert.HasCount(1, call.Arguments);
        Assert.AreEqual("x", ((VariableExpression)call.Arguments[0]).Variable.Name.ToString());
    }

    /// <summary>An empty-argument built-in (for example <c>NOW()</c>) yields no arguments.</summary>
    [TestMethod]
    public void ParsesEmptyArgumentBuiltIn()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o BIND(NOW() AS ?t) }", pool);

        BuiltInCallExpression call = (BuiltInCallExpression)((BindPattern)Members(query)[1]).Expression;
        Assert.AreEqual(BuiltInFunction.Now, call.Function);
        Assert.IsEmpty(call.Arguments);
    }

    /// <summary>An IRI followed by an argument list is a function call.</summary>
    [TestMethod]
    public void ParsesFunctionCall()
    {
        using Utf8StringPool pool = new();
        FunctionCallExpression call = (FunctionCallExpression)FilterExpression("ASK { FILTER(<http://f/>(?x, ?y)) }", pool);

        Assert.AreEqual("http://f/", call.Function.Value.ToString());
        Assert.HasCount(2, call.Arguments);
    }

    /// <summary>The <c>IN</c> test carries its value and candidate set.</summary>
    [TestMethod]
    public void ParsesIn()
    {
        using Utf8StringPool pool = new();
        InExpression membership = (InExpression)FilterExpression("ASK { FILTER(?x IN (1, 2, 3)) }", pool);

        Assert.AreEqual("x", ((VariableExpression)membership.Value).Variable.Name.ToString());
        Assert.HasCount(3, membership.Set);
    }

    /// <summary>The <c>NOT IN</c> test is its own node.</summary>
    [TestMethod]
    public void ParsesNotIn()
    {
        using Utf8StringPool pool = new();
        Assert.IsInstanceOfType<NotInExpression>(FilterExpression("ASK { FILTER(?x NOT IN (1)) }", pool));
    }

    /// <summary>The <c>IF</c>, <c>COALESCE</c>, and <c>BOUND</c> special forms build their dedicated nodes.</summary>
    [TestMethod]
    public void ParsesConditionalBuiltIns()
    {
        using Utf8StringPool pool = new();
        Assert.IsInstanceOfType<IfExpression>(FilterExpression("ASK { FILTER(IF(?a, ?b, ?c)) }", pool));
        Assert.HasCount(2, ((CoalesceExpression)FilterExpression("ASK { FILTER(COALESCE(?a, ?b)) }", pool)).Alternatives);
        Assert.AreEqual("x", ((BoundExpression)FilterExpression("ASK { FILTER(BOUND(?x)) }", pool)).Variable.Name.ToString());
    }

    /// <summary>An <c>EXISTS { }</c> and a <c>NOT EXISTS { }</c> wrap an inner graph pattern.</summary>
    [TestMethod]
    public void ParsesExistsAndNotExists()
    {
        using Utf8StringPool pool = new();
        ExistsExpression exists = (ExistsExpression)FilterExpression("ASK { FILTER(EXISTS { ?s ?p ?o }) }", pool);
        Assert.IsInstanceOfType<GroupGraphPattern>(exists.Inner);
        Assert.IsInstanceOfType<NotExistsExpression>(FilterExpression("ASK { FILTER(NOT EXISTS { ?s ?p ?o }) }", pool));
    }

    /// <summary>An <c>(expr AS ?var)</c> projection records the expression and target variable.</summary>
    [TestMethod]
    public void ParsesSelectExpressionAs()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT (?x + 1 AS ?y) WHERE { ?a ?b ?x }", pool);

        SelectExpressionAs projection = (SelectExpressionAs)((SelectQuery)query.Form).Projections[0];
        Assert.AreEqual("y", projection.AsVariable.Name.ToString());
        Assert.IsInstanceOfType<ArithmeticExpression>(projection.Expression);
    }

    /// <summary><c>COUNT(*)</c> is flagged as a count-star aggregate.</summary>
    [TestMethod]
    public void ParsesCountStar()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT (COUNT(*) AS ?n) WHERE { ?s ?p ?o }", pool);

        BuiltInAggregateExpression aggregate = (BuiltInAggregateExpression)((SelectExpressionAs)((SelectQuery)query.Form).Projections[0]).Expression;
        Assert.AreEqual(AggregateFunction.Count, aggregate.Function);
        Assert.IsTrue(aggregate.IsCountStar);
        Assert.IsNull(aggregate.Argument);
    }

    /// <summary>A <c>DISTINCT</c> aggregate records the flag and its argument.</summary>
    [TestMethod]
    public void ParsesDistinctAggregate()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT (SUM(DISTINCT ?x) AS ?s) WHERE { ?a ?b ?x }", pool);

        BuiltInAggregateExpression aggregate = (BuiltInAggregateExpression)((SelectExpressionAs)((SelectQuery)query.Form).Projections[0]).Expression;
        Assert.AreEqual(AggregateFunction.Sum, aggregate.Function);
        Assert.IsTrue(aggregate.IsDistinct);
        Assert.IsFalse(aggregate.IsCountStar);
    }

    /// <summary><c>GROUP_CONCAT</c> records its separator string.</summary>
    [TestMethod]
    public void ParsesGroupConcatSeparator()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT (GROUP_CONCAT(?x ; SEPARATOR=\", \") AS ?g) WHERE { ?a ?b ?x }", pool);

        BuiltInAggregateExpression aggregate = (BuiltInAggregateExpression)((SelectExpressionAs)((SelectQuery)query.Form).Projections[0]).Expression;
        Assert.AreEqual(AggregateFunction.GroupConcat, aggregate.Function);
        Assert.AreEqual(", ", aggregate.GroupConcatSeparator!.Value.ToString());
    }

    /// <summary>A <c>GROUP BY</c> clause records variable, parenthesised-binding, and bare conditions.</summary>
    [TestMethod]
    public void ParsesGroupBy()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "SELECT * WHERE { ?s ?p ?o } GROUP BY ?s (?o + 1 AS ?x) STR(?o)",
            pool);

        GroupClause group = query.Modifier.Group!;
        Assert.HasCount(3, group.Conditions);
        Assert.AreEqual("s", ((GroupVariable)group.Conditions[0]).Variable.Name.ToString());
        Assert.AreEqual("x", ((GroupExpressionAs)group.Conditions[1]).AsVariable.Name.ToString());
        Assert.IsInstanceOfType<GroupExpression>(group.Conditions[2]);
    }

    /// <summary>A <c>HAVING</c> clause records its constraint expressions.</summary>
    [TestMethod]
    public void ParsesHaving()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "SELECT * WHERE { ?s ?p ?o } GROUP BY ?s HAVING (COUNT(?o) > 1)",
            pool);

        HavingClause having = query.Modifier.Having!;
        Assert.HasCount(1, having.Conditions);
        Assert.IsInstanceOfType<ComparisonExpression>(having.Conditions[0]);
    }

    /// <summary>An <c>ORDER BY</c> clause records ascending defaults, explicit ASC, and DESC directions.</summary>
    [TestMethod]
    public void ParsesOrderBy()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "SELECT * WHERE { ?s ?p ?o } ORDER BY ?s ASC(?o) DESC(?p)",
            pool);

        OrderClause order = query.Modifier.Order!;
        Assert.HasCount(3, order.Conditions);
        Assert.IsInstanceOfType<OrderAscending>(order.Conditions[0]);
        Assert.IsInstanceOfType<OrderAscending>(order.Conditions[1]);
        Assert.IsInstanceOfType<OrderDescending>(order.Conditions[2]);
    }

    /// <summary>The modifiers compose in grammar order with the LIMIT/OFFSET slice.</summary>
    [TestMethod]
    public void ParsesAllModifiersTogether()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery(
            "SELECT * WHERE { ?s ?p ?o } GROUP BY ?s HAVING (COUNT(?o) > 1) ORDER BY DESC(?s) LIMIT 10 OFFSET 5",
            pool);

        Assert.IsNotNull(query.Modifier.Group);
        Assert.IsNotNull(query.Modifier.Having);
        Assert.IsNotNull(query.Modifier.Order);
        Assert.AreEqual(10, query.Modifier.Limit);
        Assert.AreEqual(5, query.Modifier.Offset);
    }

    /// <summary>A bare IRI predicate is unwrapped to a constant term, not a property path.</summary>
    [TestMethod]
    public void SimpleIriPredicateIsNotAPath()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p ?o }", pool);

        TriplePatternTerm predicate = SingleBlockTriples(query)[0].Predicate;
        Assert.IsInstanceOfType<ConstantTerm>(predicate);
        Assert.AreEqual("http://e/p", ((NamedNode)((ConstantTerm)predicate).Term).Iri.ToString());
    }

    /// <summary>A sequence path collects its steps in order.</summary>
    [TestMethod]
    public void ParsesSequencePath()
    {
        using Utf8StringPool pool = new();
        PathSequence sequence = (PathSequence)PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s :a/:b ?o }", pool);

        Assert.HasCount(2, sequence.Steps);
    }

    /// <summary>An alternative path collects its alternatives.</summary>
    [TestMethod]
    public void ParsesAlternativePath()
    {
        using Utf8StringPool pool = new();
        PathAlternative alternative = (PathAlternative)PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s :a|:b ?o }", pool);

        Assert.HasCount(2, alternative.Alternatives);
    }

    /// <summary>An inverse path wraps its inner path.</summary>
    [TestMethod]
    public void ParsesInversePath()
    {
        using Utf8StringPool pool = new();
        Assert.IsInstanceOfType<PathInverse>(PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s ^:a ?o }", pool));
    }

    /// <summary>The three path quantifiers map to their dedicated nodes.</summary>
    [TestMethod]
    public void ParsesPathQuantifiers()
    {
        using Utf8StringPool pool = new();
        Assert.IsInstanceOfType<PathZeroOrMore>(PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s :a* ?o }", pool));
        Assert.IsInstanceOfType<PathOneOrMore>(PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s :a+ ?o }", pool));
        Assert.IsInstanceOfType<PathZeroOrOne>(PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s :a? ?o }", pool));
    }

    /// <summary>Grouping overrides path precedence: <c>(a|b)/c</c> is a sequence whose first step is an alternative.</summary>
    [TestMethod]
    public void ParsesGroupedPathPrecedence()
    {
        using Utf8StringPool pool = new();
        PathSequence sequence = (PathSequence)PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s (:a|:b)/:c ?o }", pool);

        Assert.HasCount(2, sequence.Steps);
        Assert.IsInstanceOfType<PathAlternative>(sequence.Steps[0]);
    }

    /// <summary>A negated property set records forward and inverse excluded predicates.</summary>
    [TestMethod]
    public void ParsesNegatedPropertySet()
    {
        using Utf8StringPool pool = new();
        PathNegatedSet set = (PathNegatedSet)PredicatePath("PREFIX : <http://e/> SELECT * WHERE { ?s !(:a|^:b) ?o }", pool);

        Assert.HasCount(2, set.Elements);
        Assert.IsInstanceOfType<PathNegatedForward>(set.Elements[0]);
        Assert.IsInstanceOfType<PathNegatedInverse>(set.Elements[1]);
    }

    /// <summary>The <c>a</c> shorthand is a path step, here followed by a zero-or-more sequence step.</summary>
    [TestMethod]
    public void ParsesAShorthandInPath()
    {
        using Utf8StringPool pool = new();
        PathSequence sequence = (PathSequence)PredicatePath("PREFIX r: <http://r/> SELECT * WHERE { ?s a/r:sub* ?o }", pool);

        PathPredicate first = (PathPredicate)sequence.Steps[0];
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", first.Predicate.Value.ToString());
        Assert.IsInstanceOfType<PathZeroOrMore>(sequence.Steps[1]);
    }

    /// <summary>A collection object collects its items as an un-expanded <see cref="CollectionTerm"/>.</summary>
    [TestMethod]
    public void ParsesCollection()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p (1 2) }", pool);

        CollectionTerm collection = (CollectionTerm)SingleBlockTriples(query)[0].Object;
        Assert.HasCount(2, collection.Items);
    }

    /// <summary>An empty collection <c>()</c> yields a collection term with no items.</summary>
    [TestMethod]
    public void ParsesEmptyCollection()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p () }", pool);

        Assert.IsEmpty(((CollectionTerm)SingleBlockTriples(query)[0].Object).Items);
    }

    /// <summary>Collections nest as terms.</summary>
    [TestMethod]
    public void ParsesNestedCollection()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p (1 (2 3)) }", pool);

        CollectionTerm outer = (CollectionTerm)SingleBlockTriples(query)[0].Object;
        Assert.HasCount(2, outer.Items);
        Assert.HasCount(2, ((CollectionTerm)outer.Items[1]).Items);
    }

    /// <summary>A collection may stand in subject position.</summary>
    [TestMethod]
    public void ParsesCollectionSubject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { (1 2) :p ?o }", pool);

        Assert.IsInstanceOfType<CollectionTerm>(SingleBlockTriples(query)[0].Subject);
    }

    /// <summary>A blank-node property list collects its predicate-object entries un-expanded.</summary>
    [TestMethod]
    public void ParsesBlankNodePropertyList()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p [ :a :b ] }", pool);

        BlankNodePropertyListTerm list = (BlankNodePropertyListTerm)SingleBlockTriples(query)[0].Object;
        Assert.HasCount(1, list.Properties);
        Assert.HasCount(1, list.Properties[0].Objects);
    }

    /// <summary>A blank-node property list parses multiple predicates and object lists.</summary>
    [TestMethod]
    public void ParsesBlankNodePropertyListWithSeveralEntries()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p [ :a :b, :c ; :d :e ] }", pool);

        BlankNodePropertyListTerm list = (BlankNodePropertyListTerm)SingleBlockTriples(query)[0].Object;
        Assert.HasCount(2, list.Properties);
        Assert.HasCount(2, list.Properties[0].Objects);
        Assert.HasCount(1, list.Properties[1].Objects);
    }

    /// <summary>A blank-node property list may stand in subject position.</summary>
    [TestMethod]
    public void ParsesBlankNodePropertyListSubject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { [ :a :b ] :p ?o }", pool);

        Assert.IsInstanceOfType<BlankNodePropertyListTerm>(SingleBlockTriples(query)[0].Subject);
    }

    /// <summary>Compound terms nest: a blank-node property list whose object is a collection.</summary>
    [TestMethod]
    public void ParsesNestedCompoundTerms()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { ?s :p [ :a (1 2) ] }", pool);

        BlankNodePropertyListTerm list = (BlankNodePropertyListTerm)SingleBlockTriples(query)[0].Object;
        Assert.IsInstanceOfType<CollectionTerm>(list.Properties[0].Objects[0]);
    }

    /// <summary>Term spans are full source extents: a collection spans <c>(</c>…<c>)</c>, a variable its lexeme.</summary>
    [TestMethod]
    public void CollectsFullExtentTermSpans()
    {
        using Utf8StringPool pool = new();
        const string Text = "PREFIX : <http://e/> SELECT * WHERE { ?s :p (1 2) }";
        SparqlQuery query = ParseQuery(Text, pool);
        byte[] bytes = Encoding.UTF8.GetBytes(Text);

        TriplePattern triple = SingleBlockTriples(query)[0];
        Assert.AreEqual("?s", Slice(bytes, ((VariableTerm)triple.Subject).Span));
        Assert.AreEqual("(1 2)", Slice(bytes, ((CollectionTerm)triple.Object).Span));
    }

    /// <summary>A sub-SELECT is a group member carrying its own nested query.</summary>
    [TestMethod]
    public void ParsesSubSelectMember()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { ?s ?p ?o { SELECT ?x WHERE { ?x ?y ?z } } }", pool);

        IReadOnlyList<GraphPattern> members = Members(query);
        Assert.IsInstanceOfType<BasicGraphPatternBlock>(members[0]);
        SubSelectPattern sub = (SubSelectPattern)members[1];
        Assert.HasCount(1, ((SelectQuery)sub.InnerQuery.Form).Projections);
    }

    /// <summary>A <c>{ SELECT ... }</c> WHERE block is a sub-SELECT, with its own modifiers.</summary>
    [TestMethod]
    public void ParsesSubSelectAsWhere()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("SELECT * WHERE { SELECT ?x WHERE { ?x ?y ?z } LIMIT 5 }", pool);

        SubSelectPattern sub = (SubSelectPattern)query.Where.Pattern;
        Assert.IsInstanceOfType<SelectQuery>(sub.InnerQuery.Form);
        Assert.AreEqual(5, sub.InnerQuery.Modifier.Limit);
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
    /// <summary>A triple term in subject position parses to an un-expanded triple-term node over its inner triple.</summary>
    [TestMethod]
    public void ParsesTripleTermAsSubject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { <<( :a :b :c )>> :p1 :o1 }", pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        AstTripleTerm tripleTerm = (AstTripleTerm)triple.Subject;
        Assert.AreEqual("http://e/a", IriValue(tripleTerm.Inner.Subject));
        Assert.AreEqual("http://e/b", IriValue(tripleTerm.Inner.Predicate));
        Assert.AreEqual("http://e/c", IriValue(tripleTerm.Inner.Object));
        Assert.AreEqual("http://e/p1", IriValue(triple.Predicate));
    }

    /// <summary>A triple term in object position parses, carrying a variable inner subject.</summary>
    [TestMethod]
    public void ParsesTripleTermAsObject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { :s :r <<( ?x :p :o )>> }", pool);

        AstTripleTerm tripleTerm = (AstTripleTerm)SingleBlockTriples(query)[0].Object;
        Assert.AreEqual("x", VariableName(tripleTerm.Inner.Subject));
        Assert.AreEqual("http://e/o", IriValue(tripleTerm.Inner.Object));
    }

    /// <summary>A triple term nests in the object position of another triple term.</summary>
    [TestMethod]
    public void ParsesNestedTripleTermInObject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { :s :q <<( :s1 :r <<( ?x :p :o )>> )>> }", pool);

        AstTripleTerm outer = (AstTripleTerm)SingleBlockTriples(query)[0].Object;
        Assert.IsInstanceOfType<AstTripleTerm>(outer.Inner.Object);
        Assert.AreEqual("x", VariableName(((AstTripleTerm)outer.Inner.Object).Inner.Subject));
    }

    /// <summary>The <c>a</c> shorthand is accepted as a triple term's verb, resolving to <c>rdf:type</c>.</summary>
    [TestMethod]
    public void ParsesTripleTermWithAVerb()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { :s :p <<( :x a :C )>> }", pool);

        AstTripleTerm tripleTerm = (AstTripleTerm)SingleBlockTriples(query)[0].Object;
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", IriValue(tripleTerm.Inner.Predicate));
    }

    /// <summary>A triple term is accepted as an object inside a blank-node property list.</summary>
    [TestMethod]
    public void ParsesTripleTermInsideBlankNodePropertyList()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { [ :q <<( :s :p :o )>> ] :b :c }", pool);

        BlankNodePropertyListTerm list = (BlankNodePropertyListTerm)SingleBlockTriples(query)[0].Subject;
        Assert.IsInstanceOfType<AstTripleTerm>(list.Properties[0].Objects[0]);
    }

    /// <summary>A bare triple term with no following predicate-object is rejected (a triple term cannot stand alone).</summary>
    [TestMethod]
    public void TripleTermStandaloneThrows()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * WHERE { <<( ?s ?p ?o )>> . }", pool, WellKnownDiagnostics.Sparql.ExpectedVerb);
    }

    /// <summary>A standalone reified triple <c>&lt;&lt; … &gt;&gt; .</c> with no property list is a subject-only assertion held in the block's standalone nodes (not a triple).</summary>
    [TestMethod]
    public void ParsesStandaloneReifiedTriple()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { << ?s ?p ?o ~ :iri >> . }", pool);

        Assert.IsEmpty(SingleBlockTriples(query));
        IReadOnlyList<TriplePatternTerm> standalone = SingleBlockStandaloneNodes(query);
        Assert.HasCount(1, standalone);
        ReifiedTriple reified = (ReifiedTriple)standalone[0];
        Assert.AreEqual("s", VariableName(reified.Inner.Subject));
        Assert.AreEqual("http://e/iri", IriValue(reified.Reifier!));
    }

    /// <summary>A standalone reified triple sits alongside the plain triples of the same basic graph pattern run.</summary>
    [TestMethod]
    public void ParsesStandaloneReifiedTripleAlongsideTriples()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * WHERE { :a :b :c . << ?s ?p ?o >> . }", pool);

        Assert.HasCount(1, SingleBlockTriples(query));
        IReadOnlyList<TriplePatternTerm> standalone = SingleBlockStandaloneNodes(query);
        Assert.HasCount(1, standalone);
        Assert.IsInstanceOfType<ReifiedTriple>(standalone[0]);
    }

    /// <summary>A triple term in an expression (a <c>BIND</c> right-hand side) parses to a <see cref="TripleTermExpression"/> over its inner triple.</summary>
    [TestMethod]
    public void ParsesTripleTermInExpression()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { ?s ?p ?o BIND(<<( ?s ?p ?o )>> AS ?t) }", pool);

        BindPattern bind = (BindPattern)Members(query)[1];
        Assert.AreEqual("t", bind.AsVariable.Name.ToString());
        TripleTermExpression tripleTerm = (TripleTermExpression)bind.Expression;
        Assert.AreEqual("s", VariableName(tripleTerm.Inner.Subject));
        Assert.AreEqual("p", VariableName(tripleTerm.Inner.Predicate));
        Assert.AreEqual("o", VariableName(tripleTerm.Inner.Object));
    }

    /// <summary>An expression triple term admits an IRI subject and a nested triple term in its object position.</summary>
    [TestMethod]
    public void ParsesTripleTermInExpressionWithNestedObject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { ?s ?p ?o BIND(<<( :a :b <<( ?s ?p ?o )>> )>> AS ?t) }", pool);

        TripleTermExpression outer = (TripleTermExpression)((BindPattern)Members(query)[1]).Expression;
        Assert.AreEqual("http://e/a", IriValue(outer.Inner.Subject));
        Assert.IsInstanceOfType<AstTripleTerm>(outer.Inner.Object);
    }

    /// <summary>An expression triple term rejects a nested triple term in its subject position.</summary>
    [TestMethod]
    public void TripleTermInExpressionRejectsNestedSubject()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { BIND(<<( <<( :s :p :o )>> :q :z )>> AS ?X) }", pool, WellKnownDiagnostics.Sparql.InvalidTripleTermSubject);
    }

    /// <summary>An expression triple term rejects a literal in its subject position.</summary>
    [TestMethod]
    public void TripleTermInExpressionRejectsLiteralSubject()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { BIND(<<( \"literal\" :q :z )>> AS ?X) }", pool, WellKnownDiagnostics.Sparql.InvalidTripleTermSubject);
    }

    /// <summary>A triple term's subject may itself be a triple term (nesting in subject position).</summary>
    [TestMethod]
    public void ParsesTripleTermWithNestedSubject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { <<( <<( :s :p :o )>> :q :z )>> :p1 :o1 }", pool);

        AstTripleTerm outer = (AstTripleTerm)SingleBlockTriples(query)[0].Subject;
        Assert.IsInstanceOfType<AstTripleTerm>(outer.Inner.Subject);
    }

    /// <summary>A property path is rejected as a triple term's verb.</summary>
    [TestMethod]
    public void TripleTermPathVerbThrows()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { <<( ?s :p/:q ?o )>> :a :b }", pool, WellKnownDiagnostics.Sparql.UnclosedTripleTerm);
    }

    /// <summary>A blank node is rejected as a triple term's predicate.</summary>
    [TestMethod]
    public void TripleTermBlankNodePredicateThrows()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { <<( ?s [] ?o )>> :a :b }", pool, WellKnownDiagnostics.Sparql.ExpectedTripleTermVerb);
    }

    /// <summary>A reified triple in subject position parses with an explicit IRI reifier.</summary>
    [TestMethod]
    public void ParsesReifiedTripleAsSubjectWithReifier()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { << :a :b :c ~ :iri >> :p1 :o1 }", pool);

        TriplePattern triple = SingleBlockTriples(query)[0];
        ReifiedTriple reified = (ReifiedTriple)triple.Subject;
        Assert.AreEqual("http://e/a", IriValue(reified.Inner.Subject));
        Assert.AreEqual("http://e/iri", IriValue(reified.Reifier!));
        Assert.AreEqual("http://e/p1", IriValue(triple.Predicate));
    }

    /// <summary>A reified triple with no <c>~</c> has a null (anonymous) reifier.</summary>
    [TestMethod]
    public void ParsesReifiedTripleAnonymousReifier()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { << :a :b :c >> :p1 :o1 }", pool);

        ReifiedTriple reified = (ReifiedTriple)SingleBlockTriples(query)[0].Subject;
        Assert.IsNull(reified.Reifier);
    }

    /// <summary>A bare <c>~</c> with no following identity also yields a null (anonymous) reifier.</summary>
    [TestMethod]
    public void ParsesReifiedTripleBareTilde()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { << :a :b :c ~ >> :p1 :o1 }", pool);

        ReifiedTriple reified = (ReifiedTriple)SingleBlockTriples(query)[0].Subject;
        Assert.IsNull(reified.Reifier);
    }

    /// <summary>A reified triple parses in object position with a blank-node reifier.</summary>
    [TestMethod]
    public void ParsesReifiedTripleAsObjectWithBlankNodeReifier()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { :s :p << :a :b ?c ~ _:r >> }", pool);

        ReifiedTriple reified = (ReifiedTriple)SingleBlockTriples(query)[0].Object;
        Assert.IsInstanceOfType<BlankNode>(((ConstantTerm)reified.Reifier!).Term);
    }

    /// <summary>A reified triple nests as the subject of another reified triple.</summary>
    [TestMethod]
    public void ParsesNestedReifiedTripleSubject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { << << :x :r :z >> :p :o >> :q :z2 }", pool);

        ReifiedTriple outer = (ReifiedTriple)SingleBlockTriples(query)[0].Subject;
        Assert.IsInstanceOfType<ReifiedTriple>(outer.Inner.Subject);
    }

    /// <summary>A triple term nests as the subject of a reified triple.</summary>
    [TestMethod]
    public void ParsesTripleTermInsideReifiedTriple()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { << <<( :x :r :z )>> :p :o >> :q :z2 }", pool);

        ReifiedTriple outer = (ReifiedTriple)SingleBlockTriples(query)[0].Subject;
        Assert.IsInstanceOfType<AstTripleTerm>(outer.Inner.Subject);
    }

    /// <summary>A reified triple is rejected in an expression position (only triple terms are values).</summary>
    [TestMethod]
    public void ReifiedTripleInExpressionThrows()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { ?s ?p ?o BIND(<< ?s ?p ?o ~ :iri >> AS ?t) }", pool, WellKnownDiagnostics.Sparql.ExpressionExpected);
    }

    /// <summary>An annotation block on an object parses to an <see cref="AnnotatedObject"/> carrying an <see cref="AnnotationBlock"/>.</summary>
    [TestMethod]
    public void ParsesAnnotationBlockOnObject()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { ?s ?p ?o {| :r ?z |} }", pool);

        AnnotatedObject annotated = (AnnotatedObject)SingleBlockTriples(query)[0].Object;
        Assert.AreEqual("o", VariableName(annotated.Object));
        Assert.HasCount(1, annotated.Annotations);
        AnnotationBlock block = (AnnotationBlock)annotated.Annotations[0];
        Assert.AreEqual("http://e/r", IriValue(block.Properties[0].Verb));
    }

    /// <summary>A reifier and an annotation block attach to the object in source order.</summary>
    [TestMethod]
    public void ParsesReifierThenBlockAnnotation()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { ?s ?p ?o ~ :iri {| :r ?z |} }", pool);

        AnnotatedObject annotated = (AnnotatedObject)SingleBlockTriples(query)[0].Object;
        Assert.HasCount(2, annotated.Annotations);
        Assert.AreEqual("http://e/iri", IriValue(((ReifierAnnotation)annotated.Annotations[0]).Reifier!));
        Assert.IsInstanceOfType<AnnotationBlock>(annotated.Annotations[1]);
    }

    /// <summary>A bare reifier annotation (<c>~</c> with no identity) attaches with a null reifier.</summary>
    [TestMethod]
    public void ParsesBareTildeAnnotation()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { ?s ?p ?o ~ }", pool);

        AnnotatedObject annotated = (AnnotatedObject)SingleBlockTriples(query)[0].Object;
        Assert.IsNull(((ReifierAnnotation)annotated.Annotations[0]).Reifier);
    }

    /// <summary>Multiple reifier annotations attach to the object in order.</summary>
    [TestMethod]
    public void ParsesMultipleReifierAnnotations()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseQuery("PREFIX : <http://e/> SELECT * { ?s ?p ?o ~ :r1 ~ :r2 }", pool);

        AnnotatedObject annotated = (AnnotatedObject)SingleBlockTriples(query)[0].Object;
        Assert.HasCount(2, annotated.Annotations);
    }

    /// <summary>An annotation after a property-path predicate is rejected.</summary>
    [TestMethod]
    public void AnnotationOnPathVerbThrows()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { ?s :p/:q ?o ~ :iri }", pool, WellKnownDiagnostics.Sparql.AnnotationOnPathVerb);
    }

    /// <summary>An empty annotation block is rejected.</summary>
    [TestMethod]
    public void EmptyAnnotationBlockThrows()
    {
        using Utf8StringPool pool = new();

        AssertParseError("PREFIX : <http://e/> SELECT * { ?s ?p ?o {| |} }", pool, WellKnownDiagnostics.Sparql.UnclosedAnnotationBlock);
    }

    private static SparqlQuery ParseQuery(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);

        return (SparqlQuery)parser.ParseRequest();
    }

    /// <summary>
    /// Parses a query through the recovery surface, returning the parse result (the request plus the
    /// diagnostics gathered, with lexer diagnostics bridged in) without throwing on malformed input.
    /// </summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping the parsed handles alive.</param>
    /// <returns>The parse result.</returns>
    private static ParseResult<SparqlRequest> ParseQueryToResult(string text, Utf8StringPool pool)
    {
        DiagnosticBag diagnostics = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        ParseResult<SparqlRequest> result = parser.ParseToResult();

        foreach(SparqlLexDiagnostic lexDiagnostic in lexer.Diagnostics)
        {
            diagnostics.Add(SparqlLexDiagnosticBridge.ToDiagnostic(lexDiagnostic));
        }

        return new ParseResult<SparqlRequest>(result.Tree, diagnostics.Diagnostics, diagnostics.HasErrors);
    }

    /// <summary>
    /// Asserts the recovery parser flags <paramref name="text"/> as malformed, with the expected
    /// diagnostic code among the gathered diagnostics.
    /// </summary>
    /// <param name="text">The malformed SPARQL query text.</param>
    /// <param name="pool">The pool keeping the parsed handles alive.</param>
    /// <param name="expectedCode">The diagnostic code expected to fire.</param>
    private static void AssertParseError(string text, Utf8StringPool pool, Utf8String expectedCode)
    {
        ParseResult<SparqlRequest> result = ParseQueryToResult(text, pool);

        Assert.IsTrue(result.HasErrors, $"Expected '{text}' to be flagged as malformed, but no error diagnostic was recorded.");

        bool found = false;
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            if(diagnostic.Code.Equals(expectedCode))
            {
                found = true;

                break;
            }
        }

        Assert.IsTrue(found, $"Expected diagnostic code '{expectedCode}' for '{text}', but it was not among the recorded diagnostics.");
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
    /// Returns the standalone reified-triple assertions of the single basic graph pattern block in the
    /// query's WHERE group.
    /// </summary>
    /// <param name="query">The parsed query.</param>
    /// <returns>The standalone nodes.</returns>
    private static IReadOnlyList<TriplePatternTerm> SingleBlockStandaloneNodes(SparqlQuery query)
    {
        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;

        return ((BasicGraphPatternBlock)group.Members[0]).StandaloneNodes;
    }

    /// <summary>
    /// Returns the members of the query's WHERE group graph pattern.
    /// </summary>
    /// <param name="query">The parsed query.</param>
    /// <returns>The group members in source order.</returns>
    private static IReadOnlyList<GraphPattern> Members(SparqlQuery query)
    {
        return ((GroupGraphPattern)query.Where.Pattern).Members;
    }

    /// <summary>
    /// Parses a query whose WHERE group's last member is a FILTER, and returns that constraint expression.
    /// </summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping the parsed handles alive.</param>
    /// <returns>The filter constraint expression.</returns>
    private static ExpressionNode FilterExpression(string text, Utf8StringPool pool)
    {
        IReadOnlyList<GraphPattern> members = Members(ParseQuery(text, pool));

        return ((FilterPattern)members[^1]).Expression;
    }

    /// <summary>
    /// Parses a query whose single triple has a property-path predicate, and returns that path.
    /// </summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping the parsed handles alive.</param>
    /// <returns>The property-path expression of the first triple's predicate.</returns>
    private static PropertyPathExpression PredicatePath(string text, Utf8StringPool pool)
    {
        return ((PropertyPathTerm)SingleBlockTriples(ParseQuery(text, pool))[0].Predicate).Path;
    }

    /// <summary>
    /// Decodes the UTF-8 source bytes covered by a span, for asserting that a node's span is its full
    /// source extent.
    /// </summary>
    /// <param name="bytes">The UTF-8 source bytes.</param>
    /// <param name="span">The span to slice.</param>
    /// <returns>The decoded source text the span covers.</returns>
    private static string Slice(byte[] bytes, SourceSpan span)
    {
        return Encoding.UTF8.GetString(bytes, (int)span.StartByte, (int)(span.EndByte - span.StartByte));
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
