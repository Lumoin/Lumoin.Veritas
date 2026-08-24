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
/// chain graph across path shapes whose semantics make sense over a
/// linear topology: forward transitive closure from one end, an
/// alternating-predicate Kleene from one end, and a leading
/// predicate with a trailing transitive closure.
/// </summary>
/// <remarks>
/// <para>
/// The chain graph (<see cref="SyntheticGraph.GenerateChain"/>) is
/// the natural fit for depth-stressing path shapes: the result-set
/// size of <c>:p+</c> from one end equals the chain length, so the
/// benchmark measures cost per reachable node directly. The sparse
/// dead-end <c>:q</c> branches inflate the alternation BFS without
/// changing the depth profile.
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
public class PropertyPathChainBenchmark
{
    private const int ChainBranchEvery = 8;

    private GraphMatchOps ops;

    private PropertyPath path = null!;

    private TermId start;

    /// <summary>The number of <see cref="SyntheticGraph.PathPredicateP"/> edges in the main chain.</summary>
    [Params(100_000, 1_000_000)]
    public int Size { get; set; }

    /// <summary>The path shape: <c>:p+</c>, <c>(:p|:q)+</c>, or <c>:p / :q*</c>.</summary>
    [Params("PlusFromStart", "AlternationPlus", "SequenceWithKleene")]
    public string PathShape { get; set; } = null!;

    /// <summary>The backing graph store implementation.</summary>
    [Params("InMemory", "Hypertrie")]
    public string Store { get; set; } = null!;

    /// <summary>
    /// Builds the chain graph, materialises it into the chosen
    /// store, and constructs the path AST and start node for the
    /// configured <see cref="PathShape"/>.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        EncodedTriple[] triples = SyntheticGraph.GenerateChain(Size, ChainBranchEvery, seed: 42);

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
            "PlusFromStart" => new OneOrMorePath(pPath),
            "AlternationPlus" => new OneOrMorePath(new AlternativePath([pPath, qPath])),
            "SequenceWithKleene" => new SequencePath([pPath, new ZeroOrMorePath(qPath)]),
            _ => throw new System.NotSupportedException($"Unknown PathShape '{PathShape}'."),
        };

        //Start at node 0 — the chain's head. Every path shape gets
        //its longest reach from this start.
        start = TermId.FromEncoded(50_000_000U);
    }

    /// <summary>
    /// Drains the configured path evaluation into a counter. The
    /// counter is the benchmark's published value so the JIT cannot
    /// elide the enumeration.
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
