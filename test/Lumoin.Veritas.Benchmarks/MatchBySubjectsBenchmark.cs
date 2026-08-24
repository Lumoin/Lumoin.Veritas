using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the wall-clock and allocation difference between
/// <see cref="HypertrieGraphStore.MatchBySubjects"/> and an equivalent
/// per-subject loop calling <see cref="HypertrieGraphStore.Match"/>.
/// The batched primitive performs one predicate-rooted descent and
/// N subject probes; the loop performs N predicate-rooted descents.
/// </summary>
[MemoryDiagnoser]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "BenchmarkDotNet instantiates this class via reflection.")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public types and members for its reflection-based runner.")]
public class MatchBySubjectsBenchmark
{
    private HypertrieGraphStore store = null!;

    private TermId[] subjects = null!;

    private TermId knowsPredicate;

    /// <summary>The number of distinct subjects in the synthetic graph.</summary>
    [Params(100_000)]
    public int SubjectCount { get; set; }

    /// <summary>The size of the subject set passed to the batched primitive.</summary>
    [Params(1_000)]
    public int SubjectSetSize { get; set; }

    /// <summary>
    /// Per-benchmark setup — builds the social graph and prepares the
    /// subject-set probe input.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        EncodedTriple[] triples = SyntheticGraph.GenerateSocial(SubjectCount, seed: 42);
        store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false);
        knowsPredicate = TermId.FromEncoded(SyntheticGraph.KnowsPredicate);

        //Take an arithmetic progression through the subject id range
        //so the lookup hits a representative spread (not all early
        //or all dense). Subjects start at 1 in SyntheticGraph and
        //extend through SubjectCount.
        subjects = new TermId[SubjectSetSize];
        int stride = Math.Max(1, SubjectCount / SubjectSetSize);
        for(int i = 0; i < SubjectSetSize; i++)
        {
            uint subjectId = (uint)(1 + i * stride);
            subjects[i] = TermId.FromEncoded(subjectId);
        }
    }

    /// <summary>
    /// Baseline: N predicate-rooted descents — one per subject. This
    /// is what callers do today when they iterate subjects and call
    /// <see cref="HypertrieGraphStore.Match"/> per element.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int PerSubjectLoop()
    {
        int count = 0;
        for(int i = 0; i < subjects.Length; i++)
        {
            foreach(EncodedTriple triple in store.Match(subjects[i], knowsPredicate, TermId.None))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Batched: one predicate-rooted descent followed by N subject
    /// probes against the resulting depth-2 mapping.
    /// </summary>
    [Benchmark]
    public int Batched()
    {
        int count = 0;
        foreach(EncodedTriple triple in store.MatchBySubjects(subjects, knowsPredicate, TermId.None))
        {
            count++;
        }

        return count;
    }
}
