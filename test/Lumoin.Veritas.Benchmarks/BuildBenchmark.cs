using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the cost of constructing a graph store from a
/// pre-materialised triple set. Compares
/// <see cref="InMemoryGraphStore"/> with
/// <see cref="HypertrieGraphStore"/> across a triple-count grid.
/// </summary>
/// <remarks>
/// <para>
/// The triple set itself is generated once per parameter
/// combination in <see cref="GlobalSetup"/>, so its cost is not
/// counted against either store. Each <see cref="Benchmark"/>
/// method exercises only the build path of one store — the input
/// is identical between the two methods within a single run.
/// </para>
/// <para>
/// <b>Sharing density.</b> The chosen distinct-value counts give a
/// non-trivial amount of structural sharing: many subjects and
/// objects but a small predicate vocabulary. This is the regime
/// where dedup pays in the hypertrie. Other shapes (no sharing,
/// all sharing, deep-hierarchy-shaped) are out of scope for this first
/// batch — they belong in scenario-specific benchmark classes that
/// can land alongside this one.
/// </para>
/// <para>
/// <b>Async hypertrie build.</b>
/// <see cref="HypertrieGraphStore.BuildAsync(System.Collections.Generic.IEnumerable{Lumoin.Veritas.Core.EncodedTriple}, VeritasHashing.Default, System.Threading.CancellationToken)"/>
/// is async because it enters the underlying
/// <see cref="NodeStore"/>'s mutation gate. The benchmark method
/// is therefore <c>async Task&lt;HypertrieGraphStore&gt;</c>;
/// BenchmarkDotNet supports async benchmarks natively. In the
/// uncontended single-thread benchmark loop the gate completes
/// synchronously, so the awaiter overhead is one-state-machine-step
/// per call — visible in allocation-tracker output but not
/// distorting the build measurement.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet generates host code into a separate assembly at runtime; that generated assembly inherits from the benchmark class and invokes its benchmark methods. Both must be public for cross-assembly reach. Marking the type internal makes BDN unable to discover or instantiate it.")]
public class BuildBenchmark
{
    private const int RandomSeed = 42;

    /// <summary>Approximate triple counts to measure build at.</summary>
    [Params(1_000, 10_000, 100_000, 1_000_000)]
    public int TripleCount { get; set; }

    /// <summary>The pre-generated triple set, populated by <see cref="GlobalSetup"/>.</summary>
    public EncodedTriple[] Triples { get; set; } = [];

    /// <summary>
    /// Generates the triple set for the current parameter
    /// combination once per benchmark run, before any
    /// <see cref="Benchmark"/> invocation. The size of distinct
    /// value sets scales with <see cref="TripleCount"/> so the
    /// load shape stays comparable across rows of the parameter
    /// grid.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        //Pick distinct-value counts so the value-space ceiling
        //comfortably exceeds TripleCount and the realised graph
        //has both wide subjects/objects and a narrow predicate
        //vocabulary — the regime where dedup is meaningful.
        int distinctSubjects = TripleCount / 4;
        int distinctObjects = TripleCount / 4;
        int distinctPredicates = 16;

        //Defensive lower bound: small TripleCount values would
        //otherwise produce zero distinct subjects/objects.
        if(distinctSubjects < 8) { distinctSubjects = 8; }
        if(distinctObjects < 8) { distinctObjects = 8; }

        Triples = SyntheticGraph.Random(
            targetTripleCount: TripleCount,
            distinctSubjects: distinctSubjects,
            distinctPredicates: distinctPredicates,
            distinctObjects: distinctObjects,
            seed: RandomSeed);
    }

    /// <summary>
    /// Builds an <see cref="InMemoryGraphStore"/> from the
    /// pre-generated triples.
    /// </summary>
    [Benchmark(Baseline = true)]
    public InMemoryGraphStore BuildInMemory()
    {
        return InMemoryGraphStore.Build(Triples);
    }

    /// <summary>
    /// Builds a <see cref="HypertrieGraphStore"/> from the
    /// pre-generated triples, with a fresh
    /// <see cref="NodeStore"/> using the default entry-hash mixer.
    /// </summary>
    [Benchmark]
    public async Task<HypertrieGraphStore> BuildHypertrie()
    {
        return await HypertrieGraphStore.BuildAsync(Triples, VeritasHashing.Default).ConfigureAwait(false);
    }
}
