using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Workbench;

/// <summary>
/// Soak scenarios for the <see cref="HypertrieGraphStore"/> build
/// and query paths. Each scenario is a static async method that
/// runs a timed loop against a freshly-generated synthetic corpus
/// and returns a <see cref="SoakResult"/>. Scenarios produce no
/// console output; the caller (typically <c>Program.Main</c>) is
/// responsible for reporting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why static, why no console output.</b> The same scenarios are
/// invoked from <c>Program.Main</c> (with the production triple
/// count and the operator-supplied duration) and from in-process
/// tests (with a small corpus and a short duration). Returning a
/// <see cref="SoakResult"/> keeps both call sites symmetric: the
/// CLI formats the result, tests assert against it. No
/// <c>Console.SetOut</c> capture is needed, no temporary files,
/// no log scraping.
/// </para>
/// <para>
/// <b>Setup excluded from the timing loop.</b> Both scenarios
/// generate the synthetic triple corpus before drawing the loop's
/// starting timestamp from the injected clock, so corpus-generation
/// cost does not appear in <see cref="SoakResult.Elapsed"/>. The query soak
/// additionally builds the graph store once before the loop, since
/// repeated query iterations against a re-built store would
/// measure mostly the build path; only the
/// <see cref="HypertrieGraphStore.Query"/> path is timed.
/// </para>
/// <para>
/// <b>Async signature.</b> The production
/// <see cref="HypertrieGraphStore.BuildAsync(IEnumerable{EncodedTriple}, VeritasHash, System.Threading.CancellationToken)"/>
/// returns a <see cref="ValueTask{TResult}"/>, and
/// <see cref="HypertrieGraphStore.Query"/> returns an
/// <see cref="IAsyncEnumerable{T}"/>. Each soak therefore awaits
/// the production calls directly rather than blocking via
/// <c>GetAwaiter().GetResult()</c> on the
/// <see cref="ValueTask{TResult}"/>, which the
/// <c>CA2012</c> analyzer would flag and which is unsafe in
/// general for value tasks.
/// </para>
/// </remarks>
[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Soak scenarios use deterministic non-secure randomness for reproducible synthetic input. No security boundary applies.")]
internal static class HypertrieSoak
{
    /// <summary>
    /// The default triple count for the production CLI. Tests pass
    /// a much smaller value to keep per-test wall time low.
    /// </summary>
    public const int DefaultTripleCount = 100_000;

    /// <summary>
    /// One-shot allocation probe. Builds the
    /// <see cref="HypertrieGraphStore"/> exactly once from a fresh
    /// synthetic corpus and reports the wall-clock build time, the
    /// total bytes the build allocated on the current thread, and
    /// the gen2 heap size after a full collect. Use this when
    /// comparing allocation profiles across implementation changes.
    /// </summary>
    /// <param name="tripleCount">The synthetic corpus size to build against.</param>
    /// <param name="cancellationToken">Aborts the probe's build cooperatively.</param>
    /// <returns>The single-iteration allocation and timing result.</returns>
    public static async Task<AllocationResult> RunBuildAllocationProbeAsync(int tripleCount = DefaultTripleCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tripleCount);

        EncodedTriple[] triples = GenerateTriples(tripleCount);

        //Collect before measuring so the baseline allocation count
        //is free of corpus-generation churn.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        long peakGen2 = GC.GetGeneration(store) == 2 ? GC.GetTotalMemory(forceFullCollection: false) : GC.GetTotalMemory(forceFullCollection: false);

        store.Snapshot.Store.Dispose();

        return new AllocationResult(sw.Elapsed, allocAfter - allocBefore, peakGen2);
    }

    /// <summary>
    /// Repeatedly builds a fresh
    /// <see cref="HypertrieGraphStore"/> from a pre-generated
    /// triple corpus for the requested duration. Each iteration
    /// constructs a new store from scratch so the build path's
    /// allocation and CPU profile is sampled every iteration.
    /// </summary>
    /// <param name="duration">
    /// The minimum time the loop should run, on <paramref name="clock"/>.
    /// The loop permits its last-started iteration to complete, so
    /// actual elapsed time may exceed <paramref name="duration"/> by
    /// up to one iteration's worth of work.
    /// </param>
    /// <param name="clock">
    /// The clock the loop's deadline checks and the reported
    /// <see cref="SoakResult.Elapsed"/> are measured on. The CLI
    /// passes <see cref="TimeProvider.System"/>; tests inject a
    /// stepping clock so the iteration count and elapsed value are
    /// exact. The elapsed value is a deliberate measured-at-exit
    /// read, one timestamp draw after the failing deadline check.
    /// </param>
    /// <param name="tripleCount">
    /// The number of synthetic triples to load per build. Defaults
    /// to <see cref="DefaultTripleCount"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Aborts the soak cooperatively between and inside iterations;
    /// the per-iteration build observes it.
    /// </param>
    /// <returns>
    /// The iteration count, elapsed time on the clock, and zero
    /// auxiliary count.
    /// </returns>
    public static async Task<SoakResult> RunBuildSoakAsync(TimeSpan duration, TimeProvider clock, int tripleCount = DefaultTripleCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegative(tripleCount);

        EncodedTriple[] triples = GenerateTriples(tripleCount);

        long iterations = 0;
        long start = clock.GetTimestamp();
        while(clock.GetElapsedTime(start) < duration)
        {
            HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
            //Release the store before the next iteration so
            //retention measurements reflect the storage layer's
            //steady state. The parameterless BuildAsync overload
            //transfers NodeStore ownership through Snapshot.Store.
            store.Snapshot.Store.Dispose();
            iterations++;
        }

        return new SoakResult(iterations, clock.GetElapsedTime(start), 0L);
    }

    /// <summary>
    /// Repeatedly runs a single triple pattern against a pre-built
    /// <see cref="HypertrieGraphStore"/> for the requested
    /// duration. The store is built once before the timing loop
    /// starts, so only the query-execution path is sampled.
    /// </summary>
    /// <param name="duration">
    /// The minimum time the loop should run, on <paramref name="clock"/>.
    /// </param>
    /// <param name="clock">
    /// The clock the loop's deadline checks and the reported
    /// <see cref="SoakResult.Elapsed"/> are measured on. The CLI
    /// passes <see cref="TimeProvider.System"/>; tests inject a
    /// stepping clock so the iteration count and elapsed value are
    /// exact. The elapsed value is a deliberate measured-at-exit
    /// read, one timestamp draw after the failing deadline check.
    /// </param>
    /// <param name="tripleCount">
    /// The number of synthetic triples to load into the store.
    /// Defaults to <see cref="DefaultTripleCount"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Aborts the soak cooperatively between and inside iterations;
    /// the store build and every query enumeration observe it.
    /// </param>
    /// <returns>
    /// The iteration count, elapsed time on the clock, and total
    /// solutions emitted across every iteration.
    /// </returns>
    public static async Task<SoakResult> RunQuerySoakAsync(TimeSpan duration, TimeProvider clock, int tripleCount = DefaultTripleCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegative(tripleCount);

        EncodedTriple[] triples = GenerateTriples(tripleCount);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        try
        {
            BasicGraphPattern pattern = BuildSinglePattern();

            long iterations = 0;
            long solutionCount = 0;
            long start = clock.GetTimestamp();
            while(clock.GetElapsedTime(start) < duration)
            {
                await foreach(Solution _ in store.QueryAsync(pattern, VeritasClock.System, cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    solutionCount++;
                }

                iterations++;
            }

            return new SoakResult(iterations, clock.GetElapsedTime(start), solutionCount);
        }
        finally
        {
            //Release the store before returning. The parameterless
            //BuildAsync overload transfers NodeStore ownership
            //through Snapshot.Store.
            store.Snapshot.Store.Dispose();
        }
    }

    /// <summary>
    /// Generates the Seed=1 synthetic corpus that the build and
    /// query soaks consume. Exposed for the
    /// <c>--profile-edgemap-distribution --synthetic</c> workbench
    /// scenario so the same RNG draws drive both the soak traces and
    /// the EdgeMap distribution survey.
    /// </summary>
    /// <param name="targetTripleCount">The number of distinct triples to materialise.</param>
    /// <returns>An array of distinct synthetic triples sized to <paramref name="targetTripleCount"/>.</returns>
    public static EncodedTriple[] GenerateSyntheticTriples(int targetTripleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetTripleCount);

        return GenerateTriples(targetTripleCount);
    }

    private static EncodedTriple[] GenerateTriples(int targetTripleCount)
    {
        //Many subjects and objects, a narrow predicate vocabulary —
        //the regime where hypertrie dedup is meaningful and where
        //BuildBenchmark also operates. Defensive lower bound: small
        //triple counts would otherwise collapse the distinct-value
        //sets to single digits.
        int distinctSubjects = targetTripleCount / 4;
        int distinctObjects = targetTripleCount / 4;
        const int DistinctPredicates = 16;
        if(distinctSubjects < 8) { distinctSubjects = 8; }
        if(distinctObjects < 8) { distinctObjects = 8; }

        Random rng = new(Seed: 1);
        List<EncodedTriple> triples = new(capacity: targetTripleCount);
        HashSet<EncodedTriple> seen = new(capacity: targetTripleCount);
        while(triples.Count < targetTripleCount)
        {
            uint s = (uint)rng.Next(1, distinctSubjects + 1);
            uint p = (uint)rng.Next(1_000_000, 1_000_000 + DistinctPredicates);
            uint o = (uint)rng.Next(2_000_000, 2_000_000 + distinctObjects);
            EncodedTriple triple = new(TermId.FromEncoded(s), TermId.FromEncoded(p), TermId.FromEncoded(o));
            if(seen.Add(triple))
            {
                triples.Add(triple);
            }
        }

        return [.. triples];
    }

    private static BasicGraphPattern BuildSinglePattern()
    {
        //Single (?s ?p ?o) pattern. Constructed via the in-house
        //idiom from QueryBenchmark.GlobalSetup: a VariableRegistry
        //allocates variables, PatternPosition.OfVariable lifts each
        //into a triple position, and BasicGraphPattern wraps the
        //triple-pattern list together with the registry. The
        //pattern matches every triple in the store.
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable p = registry.GetOrAdd("p");
        Variable o = registry.GetOrAdd("o");

        TriplePattern triple = new(
            PatternPosition.OfVariable(s),
            PatternPosition.OfVariable(p),
            PatternPosition.OfVariable(o));

        return new BasicGraphPattern([triple], registry);
    }
}
