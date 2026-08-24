using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// A differential property test over the worst-case-optimal join driver
/// (<see cref="BasicGraphPatternEvaluator"/>): for a randomly generated basic graph pattern evaluated over a randomly
/// generated triple set, the engine must yield <em>exactly</em> the solution set a naive nested-loop join produces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a property rather than examples.</b> The driver is a stateful machine of per-pattern iterators, an
/// open-frames stack, leapfrog intersection, and a step that re-seeds independent variables' iterators when an outer
/// variable re-binds. Whether that re-seeding is needed at a given step depends on the data and pattern shape — which
/// iterators share which variables, and whether one iterator's domain for a join level outruns another's — in ways
/// that enumerate poorly by hand. Comparing against a reference join over random inputs pins the contract directly:
/// the driver's solution set equals the reference's for every BGP/data pair, and any disagreement shrinks to a
/// minimal counterexample.
/// </para>
/// <para>
/// <b>The reference.</b> A naive left-to-right nested-loop join: start from the single empty mapping, and for each
/// pattern extend every surviving mapping by every data triple it matches (a position bound to a constant must equal
/// it; a variable must be unbound or already agree). De-duplicating per round keeps the partial-assignment set
/// bounded. This is the textbook BGP semantics — solutions are the distinct variable mappings under which every
/// pattern is a triple in the graph — and shares no code with the iterator machinery under test.
/// </para>
/// <para>
/// <b>Generator shape.</b> Term ids are drawn from a small domain (constants <c>1..4</c>) so subjects, predicates,
/// and objects recur across triples and patterns — maximising real joins and cross products, the structure where a
/// driver bug surfaces. Each pattern position is a constant or one of three variables; intra-pattern self-joins (the
/// same variable in two positions of one triple pattern) are excluded because the iterator rejects them by contract.
/// </para>
/// </remarks>
[TestClass]
internal sealed class BasicGraphPatternJoinDifferentialPropertyTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    //Constants occupy tokens 0..ConstantCount-1 (decoding to term ids 1..ConstantCount); variables occupy the
    //tokens above that. The small constant domain forces overlap so joins and cross products actually occur.
    private const int ConstantCount = 4;

    private const int VariableCount = 3;

    //Matches the repo's property-test budget; ample to surface the iterator-ordering pathologies the small term
    //domain makes frequent.
    private const long Iterations = 10_000;

    /// <summary>The driver agrees with a naive nested-loop join on every generated BGP over every generated graph.</summary>
    [TestMethod]
    public async Task DriverAgreesWithNaiveJoinOverRandomPatterns()
    {
        Gen<int[][]> genTriples = Gen.Int[1, ConstantCount].Array[3].Array[1, 14];
        Gen<int[][]> genPatterns = Gen.Int[0, ConstantCount + VariableCount - 1].Array[3].Array[1, 4];

        await Gen.Select(genTriples, genPatterns)
            .Where(t => t.Item2.All(NoIntraPatternSelfJoin))
            .SampleAsync(async t =>
            {
                (int[][] tripleRows, int[][] patternRows) = t;

                EncodedTriple[] triples = [.. tripleRows
                    .Select(static r => EncodedTriple.FromEncoded((uint)r[0], (uint)r[1], (uint)r[2]))
                    .Distinct()];

                HashSet<string> actual = await SolveWithDriverAsync(triples, patternRows).ConfigureAwait(false);
                HashSet<string> expected = SolveNaively(triples, patternRows);

                Assert.IsTrue(
                    expected.SetEquals(actual),
                    $"Driver and naive join disagree. Patterns={DescribePatterns(patternRows)} over {triples.Length} triples: "
                    + $"expected {expected.Count} solutions, driver produced {actual.Count}.");
            }, iter: Iterations).ConfigureAwait(false);
    }

    //A pattern has no self-join when its variable tokens (those at or above ConstantCount) are all distinct.
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

    //Runs the BGP through the production join driver and returns each solution as a canonical key over the variables
    //the BGP actually uses.
    private static async Task<HashSet<string>> SolveWithDriverAsync(EncodedTriple[] triples, int[][] patternRows)
    {
        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, default)
            .ConfigureAwait(false);

        VariableRegistry registry = new();
        Variable[] variables = [.. Enumerable.Range(0, VariableCount).Select(i => registry.GetOrAdd($"v{i}"))];

        TriplePattern[] patterns = [.. patternRows.Select(row => new TriplePattern(
            ToPosition(row[0], variables),
            ToPosition(row[1], variables),
            ToPosition(row[2], variables)))];

        BasicGraphPattern bgp = new(patterns, registry);
        int[] usedVariables = UsedVariableIndices(patternRows);

        List<Solution> solutions = await CollectAsync(store.QueryAsync(bgp, VeritasClock.System)).ConfigureAwait(false);

        HashSet<string> keys = [];
        foreach(Solution solution in solutions)
        {
            keys.Add(string.Join(";", usedVariables.Select(i => $"{i}={solution.Get(variables[i]).Encoded}")));
        }

        return keys;
    }

    //The reference: naive left-to-right nested-loop join, de-duplicating the partial-assignment set each round.
    private static HashSet<string> SolveNaively(EncodedTriple[] triples, int[][] patternRows)
    {
        List<Dictionary<int, uint>> partials = [new Dictionary<int, uint>()];

        foreach(int[] pattern in patternRows)
        {
            Dictionary<string, Dictionary<int, uint>> next = [];
            foreach(Dictionary<int, uint> partial in partials)
            {
                foreach(EncodedTriple triple in triples)
                {
                    Dictionary<int, uint> candidate = new(partial);
                    if(TryMatch(pattern, triple, candidate))
                    {
                        next[CanonicalKey(candidate)] = candidate;
                    }
                }
            }

            partials = [.. next.Values];
        }

        int[] usedVariables = UsedVariableIndices(patternRows);

        HashSet<string> keys = [];
        foreach(Dictionary<int, uint> partial in partials)
        {
            keys.Add(string.Join(";", usedVariables.Select(i => $"{i}={partial[i]}")));
        }

        return keys;
    }

    //Extends `binding` so `triple` matches `pattern`: a constant token must equal the triple's term at that position;
    //a variable token must be unbound (then bind it) or already agree. Returns false on any mismatch.
    private static bool TryMatch(int[] pattern, EncodedTriple triple, Dictionary<int, uint> binding)
    {
        uint[] terms = [triple.Subject.Encoded, triple.Predicate.Encoded, triple.Object.Encoded];

        for(int position = 0; position < 3; position++)
        {
            int token = pattern[position];
            if(token < ConstantCount)
            {
                if(terms[position] != (uint)(token + 1))
                {
                    return false;
                }

                continue;
            }

            int variableIndex = token - ConstantCount;
            if(binding.TryGetValue(variableIndex, out uint bound))
            {
                if(bound != terms[position])
                {
                    return false;
                }
            }
            else
            {
                binding[variableIndex] = terms[position];
            }
        }

        return true;
    }

    //A position token decodes to either a bound constant term (1..ConstantCount) or one of the shared variables.
    private static PatternPosition ToPosition(int token, Variable[] variables) =>
        token < ConstantCount
            ? PatternPosition.Bound(TermId.FromEncoded((uint)(token + 1)))
            : PatternPosition.OfVariable(variables[token - ConstantCount]);

    //The variable indices the BGP references, ascending — the projection axis both solvers share.
    private static int[] UsedVariableIndices(int[][] patternRows)
    {
        HashSet<int> used = [];
        foreach(int[] pattern in patternRows)
        {
            foreach(int token in pattern)
            {
                if(token >= ConstantCount)
                {
                    used.Add(token - ConstantCount);
                }
            }
        }

        return [.. used.OrderBy(static i => i)];
    }

    private static string CanonicalKey(Dictionary<int, uint> binding) =>
        string.Join(";", binding.OrderBy(static kv => kv.Key).Select(static kv => $"{kv.Key}={kv.Value}"));

    private static string DescribePatterns(int[][] patternRows) =>
        string.Join(" ", patternRows.Select(p => $"({p[0]},{p[1]},{p[2]})"));

    private static async Task<List<Solution>> CollectAsync(IAsyncEnumerable<Solution> source)
    {
        List<Solution> solutions = [];
        await foreach(Solution solution in source.ConfigureAwait(false))
        {
            solutions.Add(solution);
        }

        return solutions;
    }
}
