using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// A differential property test pinning
/// <see cref="ColumnarBasicGraphPatternEvaluator"/> against the
/// hypertrie engine behind
/// <see cref="HypertrieGraphStore.Query"/>: for a randomly
/// generated basic graph pattern over a randomly generated triple
/// set, both drivers must yield exactly the same solution set.
/// </summary>
/// <remarks>
/// The generator mirrors the hypertrie driver's own differential
/// test: term ids from a small domain so joins and cross products
/// actually occur, one to four patterns mixing constants and three
/// variables, intra-pattern self-joins excluded by the iterator
/// contract. Both drivers run the first-occurrence planner, so any
/// disagreement isolates to the index or cursor machinery.
/// </remarks>
[TestClass]
internal sealed class ColumnarBasicGraphPatternDifferentialTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const int ConstantCount = 4;

    private const int VariableCount = 3;

    //Matches the repo's property-test budget.
    private const long Iterations = 10_000;

    /// <summary>The columnar driver agrees with the hypertrie driver on every generated BGP over every generated graph.</summary>
    [TestMethod]
    public async Task ColumnarDriverAgreesWithHypertrieDriverOverRandomPatterns()
    {
        Gen<int[][]> genTriples = Gen.Int[1, ConstantCount].Array[3].Array[1, 14];
        Gen<int[][]> genPatterns = Gen.Int[0, ConstantCount + VariableCount - 1].Array[3].Array[1, 4];

        await Gen.Select(genTriples, genPatterns)
            .Where(static t => t.Item2.All(NoIntraPatternSelfJoin))
            .SampleAsync(async t =>
            {
                (int[][] tripleRows, int[][] patternRows) = t;

                EncodedTriple[] triples = [.. tripleRows
                    .Select(static r => EncodedTriple.FromEncoded((uint)r[0], (uint)r[1], (uint)r[2]))
                    .Distinct()];

                VariableRegistry registry = new();
                Variable[] variables = [.. Enumerable.Range(0, VariableCount).Select(i => registry.GetOrAdd($"v{i}"))];

                TriplePattern[] patterns = [.. patternRows.Select(row => new TriplePattern(
                    ToPosition(row[0], variables),
                    ToPosition(row[1], variables),
                    ToPosition(row[2], variables)))];

                BasicGraphPattern bgp = new(patterns, registry);
                int[] usedVariables = UsedVariableIndices(patternRows);

                HypertrieGraphStore store = await HypertrieGraphStore
                    .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken)
                    .ConfigureAwait(false);

                //The property loop builds one store per iteration;
                //deterministic disposal returns the pool slabs so
                //ten thousand iterations stay flat in memory
                //instead of racing the garbage collector.
                using NodeStore nodeStore = store.Snapshot.Store;

                ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(triples);
                ColumnarBasicGraphPatternEvaluator evaluator = new(columnar, bgp, Planners.FirstOccurrence(bgp), VeritasClock.System);

                HashSet<string> hypertrieSolutions = await SolveAsync(store.QueryAsync(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken), variables, usedVariables).ConfigureAwait(false);
                HashSet<string> columnarSolutions = await SolveAsync(evaluator.EvaluateAsync(TestContext.CancellationToken), variables, usedVariables).ConfigureAwait(false);

                Assert.IsTrue(
                    hypertrieSolutions.SetEquals(columnarSolutions),
                    $"Drivers disagree over {triples.Length} triples, {patterns.Length} patterns: "
                    + $"hypertrie produced {hypertrieSolutions.Count} solutions, columnar produced {columnarSolutions.Count}.");
            }, iter: Iterations).ConfigureAwait(false);
    }

    //A pattern has no self-join when its variable tokens (those at
    //or above ConstantCount) are all distinct.
    private static bool NoIntraPatternSelfJoin(int[] pattern)
    {
        HashSet<int> seenVariables = [];

        foreach(int token in pattern)
        {
            if(token >= ConstantCount && !seenVariables.Add(token))
            {
                return false;
            }
        }

        return true;
    }

    private static PatternPosition ToPosition(int token, Variable[] variables)
    {
        return token < ConstantCount
            ? PatternPosition.Bound(TermId.FromEncoded((uint)(token + 1)))
            : PatternPosition.OfVariable(variables[token - ConstantCount]);
    }

    //The distinct variable indices the pattern set actually uses,
    //ascending — the canonical-key projection both solution sets
    //are compared under.
    private static int[] UsedVariableIndices(int[][] patternRows)
    {
        SortedSet<int> used = [];

        foreach(int[] row in patternRows)
        {
            foreach(int token in row)
            {
                if(token >= ConstantCount)
                {
                    used.Add(token - ConstantCount);
                }
            }
        }

        return [.. used];
    }

    //Drains an async solution sequence into canonical keys over the
    //used variables.
    private static async Task<HashSet<string>> SolveAsync(IAsyncEnumerable<Solution> solutions, Variable[] variables, int[] usedVariables)
    {
        HashSet<string> keys = [];

        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            keys.Add(string.Join(";", usedVariables.Select(i => $"{i}={solution.Get(variables[i]).Encoded}")));
        }

        return keys;
    }
}
