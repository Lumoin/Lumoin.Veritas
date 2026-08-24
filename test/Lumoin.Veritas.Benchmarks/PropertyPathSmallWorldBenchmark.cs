using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Rdf;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures <see cref="PropertyPathEvaluator.EvaluateAsync"/> on a
/// small-world graph across path shapes whose semantics exercise
/// cycle handling: reflexive transitive closure from a random
/// start, an alternating-predicate Kleene from a random start, and
/// a leading predicate with a trailing transitive closure.
/// </summary>
/// <remarks>
/// <para>
/// The small-world graph
/// (<see cref="SyntheticGraph.GenerateSmallWorld"/>) is the natural
/// fit for cycle-stressing path shapes: every node lies on many
/// short cycles, so <c>:p*</c>'s visited-set membership test
/// dominates work. The diameter of the graph stays small even at
/// 1M nodes thanks to the rewired long-range edges.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "BenchmarkDotNet instantiates this class via reflection.")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public types and members for its reflection-based runner.")]
public class PropertyPathSmallWorldBenchmark
{
    private const int SmallWorldNeighbours = 4;

    private const double SmallWorldRewireFraction = 0.05;

    private GraphMatchOps ops;

    private PropertyPath path = null!;

    private TermId start;

    /// <summary>The approximate number of total triples in the small-world graph.</summary>
    [Params(100_000, 1_000_000)]
    public int Size { get; set; }

    /// <summary>The path shape: <c>:p*</c>, <c>(:p|:q)+</c>, or <c>:p / :q*</c>.</summary>
    [Params("StarRandomStart", "AlternationPlus", "SequenceWithKleene")]
    public string PathShape { get; set; } = null!;

    /// <summary>The backing graph store implementation.</summary>
    [Params("InMemory", "Hypertrie")]
    public string Store { get; set; } = null!;

    /// <summary>
    /// Builds the small-world graph, materialises it into the chosen
    /// store, and constructs the path AST and start node for the
    /// configured <see cref="PathShape"/>.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        //GenerateSmallWorld produces nodeCount × (2 × neighbours + 1)
        //triples, so size = nodeCount × 9 with neighbours=4. Pick
        //nodeCount such that the realised triple count is close to
        //the requested Size.
        int nodeCount = Size / (2 * SmallWorldNeighbours + 1);
        EncodedTriple[] triples = SyntheticGraph.GenerateSmallWorld(
            nodeCount, SmallWorldNeighbours, SmallWorldRewireFraction, seed: 42);

        if(Store == "InMemory")
        {
            InMemoryGraphStore inMemory = InMemoryGraphStore.Build(triples);
            ops = inMemory.AsMatchOps();
        }
        else
        {
            HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false);
            ops = hypertrie.AsMatchOps();
        }

        IriId p = IriId.FromUnchecked(TermId.FromEncoded(SyntheticGraph.PathPredicateP));
        IriId q = IriId.FromUnchecked(TermId.FromEncoded(SyntheticGraph.PathPredicateQ));

        PredicatePath pPath = new(p);
        PredicatePath qPath = new(q);

        path = PathShape switch
        {
            "StarRandomStart" => new ZeroOrMorePath(pPath),
            "AlternationPlus" => new OneOrMorePath(new AlternativePath([pPath, qPath])),
            "SequenceWithKleene" => new SequencePath([pPath, new ZeroOrMorePath(qPath)]),
            _ => throw new System.NotSupportedException($"Unknown PathShape '{PathShape}'."),
        };

        //Deterministic random start: pick the node at index
        //nodeCount/3 so the start is comfortably inside the graph
        //but not at the seam where the circular base wraps.
        start = TermId.FromEncoded(50_000_000U + (uint)(nodeCount / 3));
    }

    /// <summary>
    /// Drains the configured path evaluation into a counter.
    /// </summary>
    [Benchmark]
    public async Task<int> EvaluateAsync()
    {
        int count = 0;
        await foreach(TermId _ in PropertyPathEvaluator.EvaluateAsync(
            start, path, ops, CancellationToken.None).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }
}
