using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;
using System.Security.Cryptography;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class RdfCanonicalizerTests
{
    public TestContext TestContext { get; set; } = null!;

    //SHA-256 is the hash function required by RDFC-1.0.
    private static HashDelegate Sha256 { get; } = SHA256.HashData;

    [TestMethod]
    public void CanonicalizationOfEmptyDatasetProducesEmptyString()
    {
        string result = RdfCanonicalizer.Canonicalize([], Sha256);

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void CanonicalizationOfDatasetWithNoBlankNodesProducesSortedNQuads()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s2")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o"))),
            new(
                new NamedNode(pool.Intern("http://example.org/s1")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        string[] lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, lines);
        //Lines must be in lexicographic order.
        Assert.IsLessThan(0, string.Compare(lines[0], lines[1], StringComparison.Ordinal),
            "Output lines must be in lexicographic order.");
    }

    [TestMethod]
    public void CanonicalizationIsIdempotent()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new BlankNode(pool.Intern("b0")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o")))
        ];

        string first = RdfCanonicalizer.Canonicalize(quads, Sha256);

        //Re-parse and re-canonicalize.
        List<Quad> reparsed = ParseNQuads(first, pool);
        string second = RdfCanonicalizer.Canonicalize(reparsed, Sha256);

        Assert.AreEqual(first, second, "Canonicalization must be idempotent.");
    }

    [TestMethod]
    public void BlankNodeNestedInTripleTermGetsCanonicalLabel()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(
            new BlankNode(pool.Intern("inner")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new NamedNode(pool.Intern("http://example.org/o")));
        Quad[] quads =
        [
            new(
                new BlankNode(pool.Intern("reifier")),
                new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")),
                tripleTerm)
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        //The blank node inside the triple term must be relabelled, not left as _:inner.
        Assert.DoesNotContain("_:inner", result, StringComparison.Ordinal);
        Assert.Contains("_:c14n", result, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SharedBlankNodeAcrossTopLevelAndTripleTermCanonicalisesConsistently()
    {
        using Utf8StringPool pool = new();
        //_:a appears both at top level and inside a triple term; both occurrences must receive
        //the same canonical label so the two datasets below are isomorphic.
        Quad[] datasetA =
        [
            new(
                new BlankNode(pool.Intern("a")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new TripleTerm(
                    new BlankNode(pool.Intern("a")),
                    new NamedNode(pool.Intern("http://example.org/p")),
                    new NamedNode(pool.Intern("http://example.org/o"))))
        ];
        Quad[] datasetB =
        [
            new(
                new BlankNode(pool.Intern("z")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new TripleTerm(
                    new BlankNode(pool.Intern("z")),
                    new NamedNode(pool.Intern("http://example.org/p")),
                    new NamedNode(pool.Intern("http://example.org/o"))))
        ];

        string canonicalA = RdfCanonicalizer.Canonicalize(datasetA, Sha256);
        string canonicalB = RdfCanonicalizer.Canonicalize(datasetB, Sha256);

        Assert.AreEqual(canonicalA, canonicalB);
    }

    [TestMethod]
    public void SingleBlankNodeSubjectGetsCanonicalLabel()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new BlankNode(pool.Intern("arbitrary-label")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        //The canonical label must start with _:c14n.
        Assert.IsTrue(result.StartsWith("_:c14n", StringComparison.Ordinal),
            $"Expected canonical blank node label starting with '_:c14n', got: {result}");
    }

    [TestMethod]
    public void TwoDatasetsWithSameStructureButDifferentBlankNodeLabelsProduceSameOutput()
    {
        using Utf8StringPool pool = new();

        //Dataset A: blank nodes labelled x, y.
        Quad[] dataseta =
        [
            new(
                new BlankNode(pool.Intern("x")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new BlankNode(pool.Intern("y"))),
            new(
                new BlankNode(pool.Intern("y")),
                new NamedNode(pool.Intern("http://example.org/q")),
                new NamedNode(pool.Intern("http://example.org/o")))
        ];

        //Dataset B: same structure, different labels.
        Quad[] datasetB =
        [
            new(
                new BlankNode(pool.Intern("foo")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new BlankNode(pool.Intern("bar"))),
            new(
                new BlankNode(pool.Intern("bar")),
                new NamedNode(pool.Intern("http://example.org/q")),
                new NamedNode(pool.Intern("http://example.org/o")))
        ];

        string resultA = RdfCanonicalizer.Canonicalize(dataseta, Sha256);
        string resultB = RdfCanonicalizer.Canonicalize(datasetB, Sha256);

        Assert.AreEqual(resultA, resultB,
            "Isomorphic datasets must produce identical canonical output.");
    }

    [TestMethod]
    public void TwoDatasetsWithDifferentStructuresProduceDifferentOutput()
    {
        using Utf8StringPool pool = new();

        Quad[] datasetA =
        [
            new(
                new BlankNode(pool.Intern("b0")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o1")))
        ];

        Quad[] datasetB =
        [
            new(
                new BlankNode(pool.Intern("b0")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o2")))
        ];

        string resultA = RdfCanonicalizer.Canonicalize(datasetA, Sha256);
        string resultB = RdfCanonicalizer.Canonicalize(datasetB, Sha256);

        Assert.AreNotEqual(resultA, resultB,
            "Datasets with different graph structure must produce different canonical output.");
    }

    [TestMethod]
    public void BlankNodeInObjectPositionGetsCanonicalLabel()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new BlankNode(pool.Intern("obj-blank")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.Contains("_:c14n", result, StringComparison.Ordinal,
            "Canonical blank node label must appear in output.");
    }

    [TestMethod]
    public void BlankNodeInNamedGraphPositionGetsCanonicalLabel()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o")),
                new BlankNode(pool.Intern("graph-blank")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.Contains("_:c14n", result, StringComparison.Ordinal,
            "Canonical blank node label must appear in output for graph position.");
    }

    [TestMethod]
    public void MultipleBlankNodesGetSequentialCanonicalLabels()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new BlankNode(pool.Intern("b0")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o1"))),
            new(
                new BlankNode(pool.Intern("b1")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o2"))),
            new(
                new BlankNode(pool.Intern("b2")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o3")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.Contains("_:c14n0", result, StringComparison.Ordinal, "Expected _:c14n0");
        Assert.Contains("_:c14n1", result, StringComparison.Ordinal, "Expected _:c14n1");
        Assert.Contains("_:c14n2", result, StringComparison.Ordinal, "Expected _:c14n2");
    }

    [TestMethod]
    public void OutputLinesAreInLexicographicOrder()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/z")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o"))),
            new(
                new NamedNode(pool.Intern("http://example.org/a")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);
        string[] lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.HasCount(2, lines);
        Assert.IsLessThanOrEqualTo(0, string.Compare(lines[0], lines[1], StringComparison.Ordinal),
            "Lines must be in lexicographic order.");
    }

    [TestMethod]
    public void LiteralValuesArePreservedInOutput()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/name")),
                new Literal(
                    pool.Intern("Alice"),
                    new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.Contains("\"Alice\"", result, StringComparison.Ordinal,
            "Literal value must be preserved in canonical output.");
    }

    [TestMethod]
    public void LanguageTaggedLiteralIsPreservedInOutput()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/name")),
                new Literal(
                    pool.Intern("Bonjour"),
                    new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")),
                    pool.Intern("fr")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.Contains("\"Bonjour\"@fr", result, StringComparison.Ordinal,
            "Language-tagged literal must be preserved in canonical output.");
    }

    [TestMethod]
    public void W3CTestVectorCase1SingleTripleNoBlankNodes()
    {
        //W3C RDFC-1.0 test: rdfc10-manifest.jsonld test case 0001.
        //Input: <http://example.org/#p> <http://example.org/#q> <http://example.org/#r> .
        //Expected canonical output: same line (no blank nodes, single triple).
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/#p")),
                new NamedNode(pool.Intern("http://example.org/#q")),
                new NamedNode(pool.Intern("http://example.org/#r")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.AreEqual(
            "<http://example.org/#p> <http://example.org/#q> <http://example.org/#r> .\n",
            result);
    }

    [TestMethod]
    public void W3CTestVectorCase2SingleBlankNodeSubject()
    {
        //W3C RDFC-1.0 test: single blank node subject.
        //Two quads with the same blank node subject but different predicates.
        //Canonical output assigns _:c14n0 to the blank node.
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new BlankNode(pool.Intern("e0")),
                new NamedNode(pool.Intern("http://example.org/#p1")),
                new NamedNode(pool.Intern("http://example.org/#o1"))),
            new(
                new BlankNode(pool.Intern("e0")),
                new NamedNode(pool.Intern("http://example.org/#p2")),
                new NamedNode(pool.Intern("http://example.org/#o2")))
        ];

        string result = RdfCanonicalizer.Canonicalize(quads, Sha256);

        Assert.Contains("_:c14n0", result, StringComparison.Ordinal);
        //Both lines must use the same canonical label.
        string[] lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, lines);
        Assert.IsTrue(lines.All(l => l.StartsWith("_:c14n0", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Two isomorphic graphs whose blank nodes are fully automorphic — two interchangeable 2-cycles, every node
    /// colliding at first degree — canonicalise to identical output regardless of the input blank-node labels. This
    /// is the n-degree disambiguation (permutations + path-dependent recursion) the rewrite implements.
    /// </summary>
    [TestMethod]
    public void AutomorphicTwoCyclesCanonicaliseIndependentlyOfLabels()
    {
        using Utf8StringPool pool = new();
        string formA = "_:a <http://example.org/p> _:b .\n_:b <http://example.org/p> _:a .\n_:c <http://example.org/p> _:d .\n_:d <http://example.org/p> _:c .\n";
        string formB = "_:w <http://example.org/p> _:x .\n_:x <http://example.org/p> _:w .\n_:y <http://example.org/p> _:z .\n_:z <http://example.org/p> _:y .\n";

        string a = RdfCanonicalizer.Canonicalize(ParseNQuads(formA, pool), Sha256);
        string b = RdfCanonicalizer.Canonicalize(ParseNQuads(formB, pool), Sha256);

        Assert.AreEqual(a, b);
    }

    /// <summary>
    /// A directed blank-node chain whose interior nodes collide at first degree canonicalises identically under any
    /// blank-node relabelling, exercising the recursive n-degree path (the deeper structure enters the hash through
    /// recursion into newly-issued related nodes).
    /// </summary>
    [TestMethod]
    public void BlankNodeChainCanonicalisesIndependentlyOfLabels()
    {
        using Utf8StringPool pool = new();
        string formA = "_:a <http://example.org/p> _:b .\n_:b <http://example.org/p> _:c .\n_:c <http://example.org/p> _:d .\n";
        string formB = "_:p1 <http://example.org/p> _:p2 .\n_:p2 <http://example.org/p> _:p3 .\n_:p3 <http://example.org/p> _:p4 .\n";

        string a = RdfCanonicalizer.Canonicalize(ParseNQuads(formA, pool), Sha256);
        string b = RdfCanonicalizer.Canonicalize(ParseNQuads(formB, pool), Sha256);

        Assert.AreEqual(a, b);
        Assert.Contains("_:c14n0", a, StringComparison.Ordinal);
    }

    /// <summary>An RDF dataset is a set, so duplicate input quads are removed: the canonical output carries each statement exactly once.</summary>
    [TestMethod]
    public void DuplicateQuadsAreRemoved()
    {
        using Utf8StringPool pool = new();
        string input = "<http://example.org/s> <http://example.org/p> <http://example.org/o> .\n<http://example.org/s> <http://example.org/p> <http://example.org/o> .\n";

        string result = RdfCanonicalizer.Canonicalize(ParseNQuads(input, pool), Sha256);

        Assert.HasCount(1, result.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    public void NullArgumentsThrow()
    {
        using Utf8StringPool pool = new();

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RdfCanonicalizer.Canonicalize(null!, Sha256));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RdfCanonicalizer.Canonicalize([], null!));
    }

    private static List<Quad> ParseNQuads(string nquads, Utf8StringPool pool)
    {
        List<Quad> quads = [];
        foreach(string line in nquads.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            quads.Add(ParseLine(line.Trim(), pool));
        }

        return quads;
    }

    private static Quad ParseLine(string line, Utf8StringPool pool)
    {
        //Minimal parser for canonical N-Quads (no escaping needed in canonical output).
        string[] parts = line.TrimEnd('.').Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        RdfTerm subject = ParseTerm(parts[0], pool);
        NamedNode predicate = (NamedNode)ParseTerm(parts[1], pool);
        RdfTerm obj = ParseTerm(parts[2], pool);

        return new Quad(subject, predicate, obj);
    }

    private static RdfTerm ParseTerm(string term, Utf8StringPool pool)
    {
        if(term.StartsWith("_:", StringComparison.Ordinal))
        {
            return new BlankNode(pool.Intern(term[2..]));
        }

        if(term.StartsWith('<') && term.EndsWith('>'))
        {
            return new NamedNode(pool.Intern(term[1..^1]));
        }

        //Plain literal: "value"^^<datatype>.
        int quoteEnd = term.LastIndexOf('"');
        string value = term[1..quoteEnd];
        string datatypeIri = term[(quoteEnd + 4)..^1];
        return new Literal(pool.Intern(value), new NamedNode(pool.Intern(datatypeIri)));
    }
}
