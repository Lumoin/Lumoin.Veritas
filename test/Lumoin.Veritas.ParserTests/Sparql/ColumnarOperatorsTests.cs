using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Differential tests for <see cref="ColumnarOperators"/>: each columnar table operation is checked against an
/// independent brute-force oracle computed over the decoded rows, so the encoded-id column transforms are proven
/// to match the relational semantics directly (not only through the engine's end-to-end conformance corpus).
/// </summary>
[TestClass]
internal sealed class ColumnarOperatorsTests
{
    /// <summary>The example-namespace prefix the test terms share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The shared term dictionary every table in a test encodes through (so equal names get equal ids, and decoding round-trips).</summary>
    private TermDictionary Dictionary { get; } = new();

    /// <summary>The columnar join keeps exactly the compatible merged pairs on a single shared variable, matching the brute-force nested-loop oracle.</summary>
    [TestMethod]
    public void JoinOnSingleSharedVariableMatchesOracle()
    {
        SolutionTable left = Columnar(["a", "b"], [["a1", "b1"], ["a2", "b2"], ["a3", "b1"]]);
        SolutionTable right = Columnar(["b", "c"], [["b1", "c1"], ["b1", "c2"], ["b9", "c9"]]);

        Assert.IsTrue(ColumnarOperators.TryJoin(left, right, out SolutionTable joined));
        AssertSameMultiset(JoinOracle(left, right), joined);
    }

    /// <summary>The columnar join on two shared variables keeps only the rows agreeing on both, matching the oracle.</summary>
    [TestMethod]
    public void JoinOnTwoSharedVariablesMatchesOracle()
    {
        SolutionTable left = Columnar(["a", "b", "x"], [["a1", "b1", "x1"], ["a1", "b2", "x2"], ["a3", "b3", "x3"]]);
        SolutionTable right = Columnar(["a", "b", "y"], [["a1", "b1", "y1"], ["a1", "b2", "y2"], ["a1", "b9", "y9"]]);

        Assert.IsTrue(ColumnarOperators.TryJoin(left, right, out SolutionTable joined));
        AssertSameMultiset(JoinOracle(left, right), joined);
    }

    /// <summary>A join with no shared variable (a cartesian product) is declined by the columnar fast path; the caller bridges to the row form.</summary>
    [TestMethod]
    public void JoinWithNoSharedVariableIsDeclined()
    {
        SolutionTable left = Columnar(["a"], [["a1"]]);
        SolutionTable right = Columnar(["b"], [["b1"]]);

        Assert.IsFalse(ColumnarOperators.TryJoin(left, right, out _));
    }

    /// <summary>A join whose shared variable is unbound in some row is declined (the all-bound precondition fails); the caller bridges to the row form.</summary>
    [TestMethod]
    public void JoinWithPartiallyBoundSharedVariableIsDeclined()
    {
        SolutionTable left = Columnar(["a", "b"], [["a1", "b1"], ["a2", null]]);
        SolutionTable right = Columnar(["b", "c"], [["b1", "c1"]]);

        Assert.IsFalse(ColumnarOperators.TryJoin(left, right, out _));
    }

    /// <summary>Distinct keeps the first occurrence of each encoded-id row tuple, matching the oracle's first-appearance dedup.</summary>
    [TestMethod]
    public void DistinctMatchesOracle()
    {
        SolutionTable input = Columnar(["a", "b"], [["a1", "b1"], ["a1", "b1"], ["a2", "b2"], ["a1", "b1"], ["a2", "b3"]]);

        SolutionTable distinct = ColumnarOperators.Distinct(input);
        List<string> expected = Rows(input).Distinct().ToList();
        Assert.AreSequenceEqual(expected, Rows(distinct), "Distinct must keep first-appearance order with duplicates removed.");
    }

    /// <summary>Union concatenates the two sides over the merged schema, filling each side's missing variables unbound, matching the oracle.</summary>
    [TestMethod]
    public void UnionMatchesOracle()
    {
        SolutionTable left = Columnar(["a", "b"], [["a1", "b1"], ["a2", "b2"]]);
        SolutionTable right = Columnar(["b", "c"], [["b3", "c3"]]);

        SolutionTable union = ColumnarOperators.Union(left, right);
        List<string> expected = [.. Rows(left), .. Rows(right)];
        Assert.AreSequenceEqual(expected, Rows(union), "Union must be the left rows then the right rows over the merged schema.");
    }

    /// <summary>Projection selects the requested variables in order, dropping the rest and leaving a requested-but-absent variable unbound, matching the oracle.</summary>
    [TestMethod]
    public void ProjectMatchesOracle()
    {
        SolutionTable input = Columnar(["a", "b", "c"], [["a1", "b1", "c1"], ["a2", "b2", "c2"]]);
        SparqlVariable[] projected = [Var("c"), Var("a"), Var("z")];

        SolutionTable result = ColumnarOperators.Project(input, projected);

        //?z is not in the input → unbound in every projected row; ?c and ?a alias their columns.
        List<string> expected =
        [
            RowKey([("a", "a1"), ("c", "c1")]),
            RowKey([("a", "a2"), ("c", "c2")]),
        ];
        Assert.AreSequenceEqual(expected, Rows(result));
    }

    /// <summary>The equality term filter keeps exactly the rows whose column equals the term id, decoding nothing.</summary>
    [TestMethod]
    public void FilterByTermEqualKeepsMatchingRows()
    {
        SolutionTable input = Columnar(["x", "y"], [["x1", "y1"], ["x2", "y2"], ["x1", "y3"]]);

        SolutionTable filtered = ColumnarOperators.FilterByTerm(input, columnIndex: 0, termId: TermId("x1"), keepEqual: true);

        Assert.AreSequenceEqual(
            new List<string> { RowKey([("x", "x1"), ("y", "y1")]), RowKey([("x", "x1"), ("y", "y3")]) },
            Rows(filtered));
    }

    /// <summary>The inequality term filter keeps bound rows that differ and drops unbound rows (the comparison is a type error on an unbound value).</summary>
    [TestMethod]
    public void FilterByTermNotEqualKeepsBoundDifferentDropsUnbound()
    {
        SolutionTable input = Columnar(["x"], [["x1"], ["x2"], [null], ["x3"]]);

        SolutionTable filtered = ColumnarOperators.FilterByTerm(input, columnIndex: 0, termId: TermId("x1"), keepEqual: false);

        Assert.AreSequenceEqual(
            new List<string> { RowKey([("x", "x2")]), RowKey([("x", "x3")]) },
            Rows(filtered));
    }

    /// <summary>An equality filter against a term id absent from the dictionary (0) keeps nothing — no bound term equals an absent constant.</summary>
    [TestMethod]
    public void FilterByTermEqualAbsentConstantKeepsNone()
    {
        SolutionTable input = Columnar(["x"], [["x1"], ["x2"]]);
        uint absent = Dictionary.GetIdOrDefault(new NamedNode(Utf8Strings.From(Ex + "never-added"))).Encoded;
        Assert.AreEqual(0u, absent, "Precondition: the constant must be absent from the dictionary.");

        SolutionTable filtered = ColumnarOperators.FilterByTerm(input, columnIndex: 0, termId: absent, keepEqual: true);

        Assert.AreEqual(0, filtered.Count);
    }

    /// <summary>The condition-free LEFT JOIN extends each left row by its compatible right rows and carries an unmatched left row with the right-only columns unbound.</summary>
    [TestMethod]
    public void LeftJoinExtendsMatchesAndCarriesUnmatched()
    {
        SolutionTable left = Columnar(["a", "b"], [["a1", "b1"], ["a2", "b2"]]);
        SolutionTable right = Columnar(["b", "c"], [["b1", "c1"], ["b1", "c2"], ["b9", "c9"]]);

        Assert.IsTrue(ColumnarOperators.TryLeftJoin(left, right, out SolutionTable result));

        //(a1,b1) matches right ?b=b1 twice; (a2,b2) has no match → carried with ?c unbound (decoded row omits ?c).
        Assert.AreSequenceEqual(
            new List<string>
            {
                RowKey([("a", "a1"), ("b", "b1"), ("c", "c1")]),
                RowKey([("a", "a1"), ("b", "b1"), ("c", "c2")]),
                RowKey([("a", "a2"), ("b", "b2")]),
            },
            Rows(result), SequenceOrder.InAnyOrder);
    }

    /// <summary>MINUS removes the left rows whose shared-variable key matches a right row, keeping the rest.</summary>
    [TestMethod]
    public void MinusRemovesRowsWithMatchingSharedKey()
    {
        SolutionTable left = Columnar(["a", "b"], [["a1", "b1"], ["a2", "b2"], ["a3", "b3"]]);
        SolutionTable right = Columnar(["b", "c"], [["b2", "c2"], ["b9", "c9"]]);

        Assert.IsTrue(ColumnarOperators.TryMinus(left, right, out SolutionTable result));

        //Shared ?b; right binds ?b ∈ {b2, b9}, so the (a2,b2) left row is removed.
        Assert.AreSequenceEqual(
            new List<string> { RowKey([("a", "a1"), ("b", "b1")]), RowKey([("a", "a3"), ("b", "b3")]) },
            Rows(result));
    }

    /// <summary>MINUS with no shared variable keeps every left row (the disjoint-domain exception).</summary>
    [TestMethod]
    public void MinusWithNoSharedVariableKeepsAllLeftRows()
    {
        SolutionTable left = Columnar(["a"], [["a1"], ["a2"]]);
        SolutionTable right = Columnar(["b"], [["b1"]]);

        Assert.IsTrue(ColumnarOperators.TryMinus(left, right, out SolutionTable result));
        Assert.AreSequenceEqual(Rows(left), Rows(result));
    }

    /// <summary>The OFFSET/LIMIT window keeps exactly the requested row range, matching the oracle.</summary>
    [TestMethod]
    public void SliceMatchesOracle()
    {
        SolutionTable input = Columnar(["a"], [["a1"], ["a2"], ["a3"], ["a4"], ["a5"]]);

        SolutionTable windowed = ColumnarOperators.Slice(input, offset: 1, limit: 2);

        List<string> expected = Rows(input).Skip(1).Take(2).ToList();
        Assert.AreSequenceEqual(expected, Rows(windowed));
    }

    /// <summary>Builds a columnar table over the given variables from rows of term local names (a <see langword="null"/> cell is unbound).</summary>
    /// <param name="variables">The schema variable names.</param>
    /// <param name="rows">The rows, each a term local name (or <see langword="null"/>) per variable.</param>
    /// <returns>The columnar table.</returns>
    private SolutionTable Columnar(string[] variables, string?[][] rows)
    {
        List<SparqlVariable> schema = [.. variables.Select(Var)];
        uint[][] columns = new uint[variables.Length][];
        for(int column = 0; column < variables.Length; column++)
        {
            uint[] values = new uint[rows.Length];
            for(int row = 0; row < rows.Length; row++)
            {
                string? name = rows[row][column];
                values[row] = name is null ? 0u : Dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Ex + name))).Encoded;
            }

            columns[column] = values;
        }

        return SolutionTable.Columnar(schema, columns, rows.Length, Dictionary);
    }

    /// <summary>The brute-force join oracle: every compatible (agree on shared bound variables) merged pair, one from each side, in left-major order.</summary>
    /// <param name="left">The left table.</param>
    /// <param name="right">The right table.</param>
    /// <returns>The expected joined rows as canonical keys.</returns>
    private static List<string> JoinOracle(SolutionTable left, SolutionTable right)
    {
        List<string> result = [];
        foreach(SparqlSolution outer in left.AsRows())
        {
            foreach(SparqlSolution inner in right.AsRows())
            {
                if(Compatible(outer, inner))
                {
                    result.Add(MergeKey(outer, inner));
                }
            }
        }

        return result;
    }

    /// <summary>Whether two solutions agree on every variable bound in both (the SPARQL compatibility relation).</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when no shared variable disagrees.</returns>
    private static bool Compatible(SparqlSolution left, SparqlSolution right)
    {
        foreach(SparqlBinding binding in left.Bindings)
        {
            if(right.TryGetValue(binding.Variable, out RdfTerm other) && !other.Equals(binding.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The canonical key of merging two compatible solutions (their union of bindings).</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns>The merged row's canonical key.</returns>
    private static string MergeKey(SparqlSolution left, SparqlSolution right)
    {
        Dictionary<string, string> merged = [];
        foreach(SparqlBinding binding in left.Bindings)
        {
            merged[binding.Variable.Name.ToString()] = TermKey(binding.Value);
        }

        foreach(SparqlBinding binding in right.Bindings)
        {
            merged[binding.Variable.Name.ToString()] = TermKey(binding.Value);
        }

        return RowKeyFromPairs(merged);
    }

    /// <summary>Decodes a columnar table's rows to canonical keys, preserving row order.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The per-row canonical keys.</returns>
    private static List<string> Rows(SolutionTable table)
    {
        List<string> rows = [];
        foreach(SparqlSolution solution in table.AsRows())
        {
            Dictionary<string, string> bindings = [];
            foreach(SparqlBinding binding in solution.Bindings)
            {
                bindings[binding.Variable.Name.ToString()] = TermKey(binding.Value);
            }

            rows.Add(RowKeyFromPairs(bindings));
        }

        return rows;
    }

    /// <summary>Asserts two row multisets are equal, ignoring order (join output order is unspecified).</summary>
    /// <param name="expected">The expected rows.</param>
    /// <param name="actual">The actual columnar table.</param>
    private static void AssertSameMultiset(List<string> expected, SolutionTable actual)
    {
        Assert.AreSequenceEqual(expected, Rows(actual), SequenceOrder.InAnyOrder);
    }

    /// <summary>A canonical row key from explicit variable/local-name pairs.</summary>
    /// <param name="pairs">The variable-to-local-name pairs.</param>
    /// <returns>The canonical key.</returns>
    private static string RowKey((string Variable, string Local)[] pairs)
    {
        Dictionary<string, string> bindings = [];
        foreach((string variable, string local) in pairs)
        {
            bindings[variable] = Ex + local;
        }

        return RowKeyFromPairs(bindings);
    }

    /// <summary>A canonical, order-insensitive key for a row's variable-to-term bindings.</summary>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The canonical key.</returns>
    private static string RowKeyFromPairs(Dictionary<string, string> bindings)
    {
        return string.Join("|", bindings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>The canonical key of a decoded term (its IRI for the named nodes these tests use).</summary>
    /// <param name="term">The term.</param>
    /// <returns>The term key.</returns>
    private static string TermKey(RdfTerm term)
    {
        return ((NamedNode)term).Iri.ToString();
    }

    /// <summary>A SPARQL variable from its name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Var(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }

    /// <summary>The encoded term id of an example-namespace IRI, interning it into the shared dictionary.</summary>
    /// <param name="name">The IRI local name.</param>
    /// <returns>The encoded term id.</returns>
    private uint TermId(string name)
    {
        return Dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Ex + name))).Encoded;
    }
}
