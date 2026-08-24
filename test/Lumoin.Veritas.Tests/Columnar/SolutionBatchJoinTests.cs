using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The batched hash join's contract: scan-and-join pipelines over
/// acyclic shapes produce exactly the leapfrog evaluator's
/// solutions — two-pattern joins, left-deep three-pattern chains,
/// and fan-out heavy keys crossing batch boundaries.
/// </summary>
[TestClass]
internal sealed class SolutionBatchJoinTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    /// <summary>A fixture with heavy key fan-out so joins multiply across batch boundaries.</summary>
    /// <returns>The index.</returns>
    private static ColumnarTripleIndex Fixture()
    {
        List<EncodedTriple> triples = [];
        ulong state = 33;
        for(int i = 0; i < 4_000; i++)
        {
            state = Mix(state);
            uint subject = 100 + (uint)(state % 30);
            triples.Add(EncodedTriple.FromEncoded(subject, 200, 300 + (uint)((state >> 8) % 50)));
            triples.Add(EncodedTriple.FromEncoded(subject, 201, 400 + (uint)((state >> 16) % 20)));
            triples.Add(EncodedTriple.FromEncoded(300 + (uint)((state >> 8) % 50), 202, 500 + (uint)((state >> 24) % 10)));
        }

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>Flattens a batch stream into sorted row fingerprints over its schema.</summary>
    /// <param name="batches">The stream.</param>
    /// <param name="schema">The stream's schema.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> Flatten(IEnumerable<SolutionBatch> batches, List<Variable> schema)
    {
        List<string> rows = [];
        foreach(SolutionBatch batch in batches)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                List<string> parts = [];
                for(int column = 0; column < schema.Count; column++)
                {
                    parts.Add($"{schema[column].Id}={batch.ColumnOf(column)[row]}");
                }

                parts.Sort(StringComparer.Ordinal);
                rows.Add(string.Join(";", parts));
            }
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    /// <summary>The leapfrog reference: the evaluator's solutions as sorted fingerprints.</summary>
    /// <param name="index">The index.</param>
    /// <param name="query">The pattern.</param>
    /// <returns>The sorted fingerprints.</returns>
    private async Task<List<string>> ReferenceAsync(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        List<string> rows = [];
        ColumnarBasicGraphPatternEvaluator evaluator = new(index, query, Planners.FirstOccurrence(query), TimeProvider.System);
        await foreach(Solution solution in evaluator.EvaluateAsync(TestContext.CancellationToken).ConfigureAwait(false))
        {
            rows.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}").Order(StringComparer.Ordinal)));
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    [TestMethod]
    public async Task TwoPatternJoinAgreesWithLeapfrog()
    {
        ColumnarTripleIndex index = Fixture();
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");

        TriplePattern left = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o));
        TriplePattern right = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2));

        IReadOnlyList<Variable> leftSchema = ColumnarBatchScan.ScanSchemaOf(index, left);
        IReadOnlyList<Variable> rightSchema = ColumnarBatchScan.ScanSchemaOf(index, right);
        Assert.IsTrue(SolutionBatchJoin.CanJoin(leftSchema, rightSchema));

        List<Variable> outputSchema = [.. leftSchema];
        outputSchema.AddRange(rightSchema.Where(variable => !leftSchema.Contains(variable)));

        List<string> joined = Flatten(
            SolutionBatchJoin.HashJoin(
                ColumnarBatchScan.Scan(index, left), leftSchema,
                ColumnarBatchScan.Scan(index, right), rightSchema),
            outputSchema);

        List<string> reference = await ReferenceAsync(index, new BasicGraphPattern([left, right], registry)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, reference.Count);
        Assert.AreSequenceEqual(reference, joined);
    }

    [TestMethod]
    public async Task LeftDeepChainAgreesWithLeapfrog()
    {
        ColumnarTripleIndex index = Fixture();
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");
        Variable tail = registry.GetOrAdd("t");

        TriplePattern first = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o));
        TriplePattern second = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2));
        TriplePattern third = new(PatternPosition.OfVariable(o), PatternPosition.Bound(TermId.FromEncoded(202)), PatternPosition.OfVariable(tail));

        IReadOnlyList<Variable> firstSchema = ColumnarBatchScan.ScanSchemaOf(index, first);
        IReadOnlyList<Variable> secondSchema = ColumnarBatchScan.ScanSchemaOf(index, second);
        IReadOnlyList<Variable> thirdSchema = ColumnarBatchScan.ScanSchemaOf(index, third);

        List<Variable> intermediateSchema = [.. firstSchema];
        intermediateSchema.AddRange(secondSchema.Where(variable => !firstSchema.Contains(variable)));
        List<Variable> outputSchema = [.. intermediateSchema];
        outputSchema.AddRange(thirdSchema.Where(variable => !intermediateSchema.Contains(variable)));

        List<string> joined = Flatten(
            SolutionBatchJoin.HashJoin(
                SolutionBatchJoin.HashJoin(
                    ColumnarBatchScan.Scan(index, first), firstSchema,
                    ColumnarBatchScan.Scan(index, second), secondSchema),
                intermediateSchema,
                ColumnarBatchScan.Scan(index, third), thirdSchema),
            outputSchema);

        List<string> reference = await ReferenceAsync(index, new BasicGraphPattern([first, second, third], registry)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, reference.Count);
        Assert.AreSequenceEqual(reference, joined);
    }

    [TestMethod]
    public void DisjointAndOverWideSchemasAreRejected()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        Assert.IsFalse(SolutionBatchJoin.CanJoin([a, b], [c, d]));
        Assert.IsFalse(SolutionBatchJoin.CanJoin([a, b, c], [a, b, c]));
        Assert.IsTrue(SolutionBatchJoin.CanJoin([a, b], [b, c]));
    }
}
