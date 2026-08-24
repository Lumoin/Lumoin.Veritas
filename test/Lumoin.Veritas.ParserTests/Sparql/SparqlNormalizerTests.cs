using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlNormalizer"/>: the early-expansion pass lowering RDF 1.2 syntactic sugar
/// (collections, blank-node property lists, reified triples, annotations, and standalone reified triples)
/// to plain triple patterns over the four core term cases.
/// </summary>
[TestClass]
internal sealed class SparqlNormalizerTests
{
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";
    private const string RdfReifies = "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies";
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>An empty collection <c>()</c> lowers to the single <c>rdf:nil</c> term with no auxiliary triples.</summary>
    [TestMethod]
    public void LowersEmptyCollectionToNil()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p () }", pool);

        Assert.HasCount(1, triples);
        Assert.AreEqual($"{Ex}p", IriValue(triples[0].Predicate));
        Assert.AreEqual(RdfNil, IriValue(triples[0].Object));
    }

    /// <summary>A two-item collection lowers to an <c>rdf:first</c>/<c>rdf:rest</c> chain terminated by <c>rdf:nil</c>.</summary>
    [TestMethod]
    public void LowersCollectionToFirstRestChain()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ( :a :b ) }", pool);

        //One subject triple (?s :p head), two rdf:first, two rdf:rest (one to a fresh cell, one to rdf:nil).
        Assert.HasCount(5, triples);
        Assert.AreEqual(2, CountByPredicate(triples, RdfFirst));
        Assert.AreEqual(2, CountByPredicate(triples, RdfRest));
        Assert.AreEqual(1, CountByPredicate(triples, $"{Ex}p"));

        //The chain head is a fresh blank node distinct from the second cell.
        TriplePattern subjectTriple = SingleByPredicate(triples, $"{Ex}p");
        string head = BlankNodeLabel(subjectTriple.Object);

        //rdf:first carries the two items :a and :b; the head's first cell is :a.
        TriplePattern headFirst = SingleBySubjectPredicate(triples, head, RdfFirst);
        Assert.AreEqual($"{Ex}a", IriValue(headFirst.Object));

        //The head's rest points to a second, distinct blank-node cell.
        TriplePattern headRest = SingleBySubjectPredicate(triples, head, RdfRest);
        string second = BlankNodeLabel(headRest.Object);
        Assert.AreNotEqual(head, second);

        //The second cell holds :b and terminates at rdf:nil.
        Assert.AreEqual($"{Ex}b", IriValue(SingleBySubjectPredicate(triples, second, RdfFirst).Object));
        Assert.AreEqual(RdfNil, IriValue(SingleBySubjectPredicate(triples, second, RdfRest).Object));
    }

    /// <summary>A blank-node property list lowers to a fresh blank node carrying its predicate-object triples.</summary>
    [TestMethod]
    public void LowersBlankNodePropertyList()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p [ :a :b ] }", pool);

        Assert.HasCount(2, triples);

        TriplePattern subjectTriple = SingleByPredicate(triples, $"{Ex}p");
        string node = BlankNodeLabel(subjectTriple.Object);

        TriplePattern property = SingleBySubjectPredicate(triples, node, $"{Ex}a");
        Assert.AreEqual($"{Ex}b", IriValue(property.Object));
    }

    /// <summary>
    /// A standalone blank-node property list (a <c>TriplesNodePath</c> with an empty trailing property list,
    /// e.g. <c>{ [ :a :b ] }</c>) lowers to its own predicate-object triple on a fresh blank node.
    /// </summary>
    [TestMethod]
    public void LowersStandaloneBlankNodePropertyList()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { [ :a :b ] }", pool);

        Assert.HasCount(1, triples);

        TriplePattern property = SingleByPredicate(triples, $"{Ex}a");
        Assert.AreEqual($"{Ex}b", IriValue(property.Object));
        _ = BlankNodeLabel(property.Subject);
    }

    /// <summary>A standalone collection (<c>{ ( :a ) }</c>) lowers to its <c>rdf:first</c>/<c>rdf:rest</c> chain.</summary>
    [TestMethod]
    public void LowersStandaloneCollection()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { ( :a ) }", pool);

        Assert.HasCount(2, triples);

        TriplePattern first = SingleByPredicate(triples, RdfFirst);
        Assert.AreEqual($"{Ex}a", IriValue(first.Object));
        Assert.AreEqual(RdfNil, IriValue(SingleByPredicate(triples, RdfRest).Object));
    }

    /// <summary>A standalone <c>TriplesNode</c> in a CONSTRUCT template lowers to its own template triples.</summary>
    [TestMethod]
    public void LowersStandaloneNodeInConstructTemplate()
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> CONSTRUCT { [ :a :b ] } WHERE { ?s ?p ?o }"), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());
        ConstructQuery construct = (ConstructQuery)query.Form;

        Assert.HasCount(1, construct.Template);
        Assert.AreEqual($"{Ex}a", IriValue(construct.Template[0].Predicate));
        Assert.AreEqual($"{Ex}b", IriValue(construct.Template[0].Object));
        Assert.IsEmpty(construct.TemplateStandaloneNodes);
    }

    /// <summary>By default a reified triple lowers to only the <c>rdf:reifies</c> reification triple; the inner triple is NOT asserted.</summary>
    [TestMethod]
    public void LowersReifiedTripleWithoutAssertingInnerTriple()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p << :a :b :c >> }", pool);

        //?s :p reifier, plus reifier rdf:reifies <<( :a :b :c )>>. The base :a :b :c is absent.
        Assert.HasCount(2, triples);
        Assert.AreEqual(0, CountByPredicate(triples, $"{Ex}b"));

        TriplePattern reifies = SingleByPredicate(triples, RdfReifies);
        AstTripleTerm tripleTerm = (AstTripleTerm)reifies.Object;
        Assert.AreEqual($"{Ex}a", IriValue(tripleTerm.Inner.Subject));
        Assert.AreEqual($"{Ex}b", IriValue(tripleTerm.Inner.Predicate));
        Assert.AreEqual($"{Ex}c", IriValue(tripleTerm.Inner.Object));

        //The subject triple's object is the reifier, which is the reifies triple's subject.
        TriplePattern subjectTriple = SingleByPredicate(triples, $"{Ex}p");
        Assert.AreEqual(BlankNodeLabel(subjectTriple.Object), BlankNodeLabel(reifies.Subject));
    }

    /// <summary>With the opt-in assert flag a reified triple additionally asserts its inner base triple.</summary>
    [TestMethod]
    public void LowersReifiedTripleAssertingInnerTripleUnderOptIn()
    {
        using Utf8StringPool pool = new();
        SparqlNormalizerOptions options = new() { AssertReifiedTripleInnerTriple = true };
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p << :a :b :c >> }", pool, options);

        //?s :p reifier, reifier rdf:reifies <<( :a :b :c )>>, AND the asserted base :a :b :c.
        Assert.HasCount(3, triples);
        Assert.AreEqual(1, CountByPredicate(triples, RdfReifies));

        TriplePattern baseTriple = SingleByPredicate(triples, $"{Ex}b");
        Assert.AreEqual($"{Ex}a", IriValue(baseTriple.Subject));
        Assert.AreEqual($"{Ex}c", IriValue(baseTriple.Object));
    }

    /// <summary>A standalone reified triple <c>&lt;&lt; s p o ~r &gt;&gt;</c> lowers to the single reification triple with the explicit reifier.</summary>
    [TestMethod]
    public void LowersStandaloneReifiedTriple()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { << :a :b :c ~ :r >> }", pool);

        Assert.HasCount(1, triples);
        TriplePattern reifies = triples[0];
        Assert.AreEqual($"{Ex}r", IriValue(reifies.Subject));
        Assert.AreEqual(RdfReifies, IriValue(reifies.Predicate));

        AstTripleTerm tripleTerm = (AstTripleTerm)reifies.Object;
        Assert.AreEqual($"{Ex}a", IriValue(tripleTerm.Inner.Subject));
        Assert.AreEqual($"{Ex}c", IriValue(tripleTerm.Inner.Object));
    }

    /// <summary>The annotation syntax <c>{| … |}</c> both asserts the base triple and reifies it.</summary>
    [TestMethod]
    public void AnnotationBlockAssertsBaseTripleAndReifies()
    {
        using Utf8StringPool pool = new();
        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples("PREFIX : <http://example.org/> SELECT * WHERE { :a :b :c {| :d :e |} }", pool);

        //Asserted base :a :b :c, reifier rdf:reifies <<( :a :b :c )>>, reifier :d :e.
        Assert.HasCount(3, triples);

        TriplePattern baseTriple = SingleByPredicate(triples, $"{Ex}b");
        Assert.AreEqual($"{Ex}a", IriValue(baseTriple.Subject));
        Assert.AreEqual($"{Ex}c", IriValue(baseTriple.Object));

        TriplePattern reifies = SingleByPredicate(triples, RdfReifies);
        TriplePattern annotation = SingleByPredicate(triples, $"{Ex}d");
        Assert.AreEqual($"{Ex}e", IriValue(annotation.Object));

        //The reifier carrying the annotation is the reifies triple's subject.
        Assert.AreEqual(BlankNodeLabel(reifies.Subject), BlankNodeLabel(annotation.Subject));
    }

    /// <summary>Every lowered collection triple carries the source span of the collection that produced it.</summary>
    [TestMethod]
    public void PreservesSourceSpanOntoLoweredTriples()
    {
        using Utf8StringPool pool = new();
        const string text = "PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ( :a ) }";

        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlRequest request = parser.ParseRequest();

        CollectionTerm collection = (CollectionTerm)((BasicGraphPatternBlock)((GroupGraphPattern)((SparqlQuery)request).Where.Pattern).Members[0]).Triples[0].Object;
        SourceSpan collectionSpan = collection.Span;

        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(request);
        IReadOnlyList<TriplePattern> triples = ((BasicGraphPatternBlock)((GroupGraphPattern)normalized.Where.Pattern).Members[0]).Triples;

        TriplePattern first = SingleByPredicate(triples, RdfFirst);
        Assert.AreEqual(collectionSpan, first.Span);
    }

    /// <summary>Nested patterns (here a UNION) are recursed into so their basic blocks are normalized too.</summary>
    [TestMethod]
    public void NormalizesNestedPatterns()
    {
        using Utf8StringPool pool = new();

        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> SELECT * WHERE { { ?s :p ( :a ) } UNION { ?s :q [ :b :c ] } }"), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        UnionPattern union = (UnionPattern)((GroupGraphPattern)query.Where.Pattern).Members[0];
        IReadOnlyList<TriplePattern> left = ((BasicGraphPatternBlock)((GroupGraphPattern)union.Left).Members[0]).Triples;
        IReadOnlyList<TriplePattern> right = ((BasicGraphPatternBlock)((GroupGraphPattern)union.Right).Members[0]).Triples;

        //Left: ?s :p head + rdf:first + rdf:rest(nil) = 3 triples; right: ?s :q node + node :b :c = 2 triples.
        Assert.HasCount(3, left);
        Assert.AreEqual(1, CountByPredicate(left, RdfFirst));
        Assert.HasCount(2, right);
        Assert.AreEqual(1, CountByPredicate(right, $"{Ex}b"));
    }

    /// <summary>Deeply nested collections lower iteratively — far past the depth a recursive term-lowering would overflow.</summary>
    [TestMethod]
    public void DeeplyNestedCollectionsLowerWithoutOverflow()
    {
        using Utf8StringPool pool = new();
        const int depth = 10_000;
        string query = "PREFIX : <http://example.org/> SELECT * WHERE { ?s :p " + new string('(', depth) + " :a " + new string(')', depth) + " }";

        IReadOnlyList<TriplePattern> triples = NormalizedBlockTriples(query, pool);

        //Each of the `depth` nested one-item collections lowers to an rdf:first + rdf:rest pair, plus the one
        //?s :p head triple.
        Assert.HasCount((depth * 2) + 1, triples);
    }

    /// <summary>Parses a query, runs the normalizer, and returns the triples of the single basic block in the WHERE group.</summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping parsed and lowered handles alive.</param>
    /// <param name="options">The normalizer options; defaults to spec-faithful.</param>
    /// <returns>The lowered triples of the WHERE group's first basic block.</returns>
    private static IReadOnlyList<TriplePattern> NormalizedBlockTriples(string text, Utf8StringPool pool, SparqlNormalizerOptions? options = null)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlNormalizer normalizer = new(pool, options: options);
        SparqlQuery query = (SparqlQuery)normalizer.Normalize(parser.ParseRequest());
        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;

        return ((BasicGraphPatternBlock)group.Members[0]).Triples;
    }

    /// <summary>Counts the triples whose predicate is the named IRI.</summary>
    /// <param name="triples">The triples to scan.</param>
    /// <param name="iri">The predicate IRI to match.</param>
    /// <returns>The count of matching triples.</returns>
    private static int CountByPredicate(IReadOnlyList<TriplePattern> triples, string iri)
    {
        int count = 0;
        foreach(TriplePattern triple in triples)
        {
            if(triple.Predicate is ConstantTerm { Term: NamedNode named } && named.Iri.ToString() == iri)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Returns the single triple whose predicate is the named IRI, asserting exactly one exists.</summary>
    /// <param name="triples">The triples to scan.</param>
    /// <param name="iri">The predicate IRI to match.</param>
    /// <returns>The single matching triple.</returns>
    private static TriplePattern SingleByPredicate(IReadOnlyList<TriplePattern> triples, string iri)
    {
        TriplePattern? found = null;
        foreach(TriplePattern triple in triples)
        {
            if(triple.Predicate is ConstantTerm { Term: NamedNode named } && named.Iri.ToString() == iri)
            {
                Assert.IsNull(found, $"More than one triple has predicate <{iri}>.");
                found = triple;
            }
        }

        Assert.IsNotNull(found, $"No triple has predicate <{iri}>.");

        return found;
    }

    /// <summary>Returns the single triple with the given blank-node subject label and predicate IRI.</summary>
    /// <param name="triples">The triples to scan.</param>
    /// <param name="subjectLabel">The blank-node subject label to match.</param>
    /// <param name="predicateIri">The predicate IRI to match.</param>
    /// <returns>The single matching triple.</returns>
    private static TriplePattern SingleBySubjectPredicate(IReadOnlyList<TriplePattern> triples, string subjectLabel, string predicateIri)
    {
        TriplePattern? found = null;
        foreach(TriplePattern triple in triples)
        {
            bool subjectMatches = triple.Subject is ConstantTerm { Term: BlankNode blank } && blank.Label.ToString() == subjectLabel;
            bool predicateMatches = triple.Predicate is ConstantTerm { Term: NamedNode named } && named.Iri.ToString() == predicateIri;
            if(subjectMatches && predicateMatches)
            {
                Assert.IsNull(found, $"More than one triple matches _:{subjectLabel} <{predicateIri}>.");
                found = triple;
            }
        }

        Assert.IsNotNull(found, $"No triple matches _:{subjectLabel} <{predicateIri}>.");

        return found;
    }

    /// <summary>Returns the absolute IRI of a constant named-node term.</summary>
    /// <param name="term">The term, expected to be a <see cref="ConstantTerm"/> over a <see cref="NamedNode"/>.</param>
    /// <returns>The IRI.</returns>
    private static string IriValue(TriplePatternTerm term)
    {
        return ((NamedNode)((ConstantTerm)term).Term).Iri.ToString();
    }

    /// <summary>Returns the label of a constant blank-node term.</summary>
    /// <param name="term">The term, expected to be a <see cref="ConstantTerm"/> over a <see cref="BlankNode"/>.</param>
    /// <returns>The blank-node label.</returns>
    private static string BlankNodeLabel(TriplePatternTerm term)
    {
        return ((BlankNode)((ConstantTerm)term).Term).Label.ToString();
    }
}
