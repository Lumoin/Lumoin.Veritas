using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// A differential property test pinning HyperCube-parallel
/// evaluation against sequential evaluation over the same columnar
/// index: the cells partition every output tuple to exactly one
/// owner, so the merged parallel solution set must equal the
/// sequential one on every generated BGP — same graph, same
/// pattern, several degrees of parallelism including ones that
/// exceed the result count and prime grids that leave one variable
/// unpartitioned.
/// </summary>
[TestClass]
internal sealed class ColumnarHyperCubeDifferentialTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const int ConstantCount = 4;

    private const int VariableCount = 3;

    //Matches the repo's property-test budget.
    private const long Iterations = 10_000;

    /// <summary>The merged cell streams equal the sequential solution set on every generated BGP and parallelism degree.</summary>
    [TestMethod]
    public async Task ParallelCellsAgreeWithSequentialEvaluationOverRandomPatterns()
    {
        Gen<int[][]> genTriples = Gen.Int[1, ConstantCount].Array[3].Array[1, 14];
        Gen<int[][]> genPatterns = Gen.Int[0, ConstantCount + VariableCount - 1].Array[3].Array[1, 4];
        Gen<int> genParallelism = Gen.Int[2, 7];

        await Gen.Select(genTriples, genPatterns, genParallelism)
            .Where(static t => t.Item2.All(NoIntraPatternSelfJoin) && t.Item2.Any(HasVariable))
            .SampleAsync(async t =>
            {
                (int[][] tripleRows, int[][] patternRows, int parallelism) = t;

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
                ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);

                ColumnarBasicGraphPatternEvaluator sequential = new(index, bgp, Planners.FirstOccurrence(bgp), VeritasClock.System);

                HashSet<string> sequentialSolutions = await SolveAsync(sequential.EvaluateAsync(TestContext.CancellationToken), variables, usedVariables).ConfigureAwait(false);
                HashSet<string> parallelSolutions = await SolveAsync(
                    ColumnarHyperCube.QueryAsync(index, bgp, parallelism, VeritasClock.System, cancellationToken: TestContext.CancellationToken),
                    variables,
                    usedVariables).ConfigureAwait(false);

                Assert.IsTrue(
                    sequentialSolutions.SetEquals(parallelSolutions),
                    $"Parallel (dop {parallelism}) and sequential evaluation disagree over {triples.Length} triples, {patterns.Length} patterns: "
                    + $"sequential produced {sequentialSolutions.Count} solutions, parallel produced {parallelSolutions.Count}.");
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

    //A pattern qualifies when at least one token is a variable.
    private static bool HasVariable(int[] pattern)
    {
        return pattern.Any(static token => token >= ConstantCount);
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
