using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The delegation-rate KPI harness: it aggregates, over one or more labelled
/// suites of reasoning modules, the fraction the polynomial EL pay-as-you-go
/// path DELEGATES to the tableau oracle versus DECIDES by saturation — the
/// pay-as-you-go axis of the roadmap scoreboard. The raw decide/delegate
/// signal exists per decision on
/// <see cref="ElSaturationStatistics.ElDecided"/>; this harness is the offline
/// aggregation of it into a rate that a rung's effect diffs between runs.
/// </summary>
/// <remarks>
/// <para>
/// It is the measurement-scaffolding peer of <see cref="W3cOwl2DirectTriage"/>
/// and <see cref="SatSessionReasonerTriage"/> — not a correctness gate on the
/// normal suite's wall time — so the measurement runs only when the
/// <c>VERITAS_DELEGATION_RATE</c> environment variable names an absolute output
/// file, and otherwise passes without measuring. The vendored-corpus
/// load-integrity pin is the exception: it runs unconditionally, because a
/// vendored module file that stops parsing or mapping is a correctness
/// regression in its own right, not a measurement.
/// </para>
/// <para>
/// Two suite sources feed it. An inline synthetic ladder of ~12 modules with a
/// known decide/delegate split — several EL-decidable shapes that MUST decide
/// by saturation and several beyond-EL shapes that MUST delegate — is the
/// self-check that gives the KPI a nonzero-and-nonfull baseline and proves the
/// aggregation works. Any vendored corpus rooted under
/// <c>Material/Benchmark/&lt;name&gt;/</c> is loaded and included labelled by
/// corpus; when none is vendored the harness runs the synthetic suite alone and
/// notes the absence rather than failing.
/// </para>
/// <para>
/// Each module is decided by the five engine columns — the production
/// composition (the KPI signal), the SAT-backed engine, the snapshot engine,
/// the context-saturation engine's own column, and the retained prior
/// EL-over-SAT chain — under a per-module wall budget that records a timeout
/// rather than wedging the run. The KPI arm composes the context tier and the
/// SAT-backed oracle behind the
/// <see cref="ElCoupledModuleReasoner.CreateDelegate"/> seam: the
/// decide/delegate split is surveyed before any oracle runs, so the fallback
/// choice cannot move the KPI, but it decides
/// whether a delegated module terminates — the vendored OWL2Bench TBoxes all
/// wedge the snapshot tableau past any usable budget while the SAT-backed
/// oracle decides them in milliseconds, and a timeout would blind the rate on
/// exactly the modules the corpus is vendored to measure. The standalone
/// snapshot column keeps that cost visible.
/// Its one reasoner-correctness claim is the differential oracle: the KPI
/// arm's consistency verdict agrees with the independent comparand that also
/// reached a verdict — standalone SAT-backed for an EL-decided module, the
/// snapshot tableau for a delegated one, whose KPI verdict already came from
/// the SAT-backed oracle and would compare against itself. Run it in Release
/// in a contiguous block with no concurrent builds or suites for citable
/// wall-time numbers.
/// </para>
/// </remarks>
[TestClass]
internal sealed class DelegationRateHarness
{
    /// <summary>The environment variable naming the absolute output path; unset means the KPI measurement passes without measuring, while the load-integrity pin runs regardless.</summary>
    private const string OutputPathVariable = "VERITAS_DELEGATION_RATE";

    /// <summary>The environment variable naming an integer per-engine per-module budget in seconds; absent or malformed leaves the default in place, a pure measurement-path configuration read, never an execution gate.</summary>
    private const string BudgetSecondsVariable = "VERITAS_DELEGATION_BUDGET_SECONDS";

    /// <summary>The environment variable naming an absolute directory whose subdirectories are machine-local corpus roots, probed beside the vendored roots; absent means only the vendored corpora are measured.</summary>
    private const string CorpusRootVariable = "VERITAS_BENCHMARK_CORPUS";

    /// <summary>The default per-engine, per-module decision budget in seconds when the override is absent or malformed.</summary>
    private const int DefaultBudgetSeconds = 30;

    /// <summary>The per-engine, per-module decision budget; a decision exceeding it records as a timeout rather than wedging the run. The default holds unless the override names a valid positive second count.</summary>
    private static TimeSpan DecisionBudget { get; } = ResolveDecisionBudget();

    /// <summary>The IRI prefix the synthetic classes, roles, and individuals live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The library subfolder under <c>Material</c> whose per-corpus subdirectories are probed for vendored benchmark modules.</summary>
    private const string BenchmarkLibraryFolder = "Benchmark";

    /// <summary>The file extensions a vendored corpus root is scanned for; each maps to a module through the matching reader.</summary>
    private static string[] CorpusFileExtensions { get; } = [".owl", ".ofn", ".ttl", ".rdf"];

    /// <summary>The shared empty module used to warm the engines and as the out-parameter placeholder for a failed corpus load.</summary>
    private static ReasoningModule EmptyModule { get; } = new([], Violations: []);

    /// <summary>The fixed-⊥ class reference, <c>owl:Nothing</c>.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>The <c>xsd:integer</c> datatype as a data range, a satisfiable filler for a data existential.</summary>
    private static OwlDatatypeReference IntegerRange { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>
    /// Resolves the per-engine per-module budget: the override's positive integer
    /// second count when present and valid, otherwise the default. A malformed or
    /// non-positive value is an expected operator condition, not an invariant
    /// violation, so it falls back rather than throws.
    /// </summary>
    /// <returns>The effective decision budget.</returns>
    private static TimeSpan ResolveDecisionBudget()
    {
        string? configured = Environment.GetEnvironmentVariable(BudgetSecondsVariable);
        if(!string.IsNullOrWhiteSpace(configured)
            && int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(DefaultBudgetSeconds);
    }

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Aggregates the EL decide/delegate split across the synthetic suite and
    /// any vendored corpus, writes the delegation-rate report to the configured
    /// path, and asserts the synthetic baseline is nonzero-and-nonfull with its
    /// known mix intact and that the KPI arm never disagrees on consistency
    /// with the independent comparand engine that also reached a verdict.
    /// </summary>
    [TestMethod]
    public async Task MeasureDelegationRate()
    {
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            //Opt-in measurement scaffolding, not a correctness gate: with no
            //output path configured the harness has nothing to do and the test
            //passes without measuring. Set VERITAS_DELEGATION_RATE to run it.
            TestContext.WriteLine($"Skipping the delegation-rate harness: set {OutputPathVariable} to an absolute output path to run it.");

            return;
        }

        //Warm every engine column on the empty module, a context-admitted
        //pure-TBox module, and every synthetic shape so the timed decisions
        //exclude first-touch JIT — the admitted module is what warms the context
        //engine's deep rule bodies the ABox-bearing shapes bypass.
        await WarmDecisionPathsAsync().ConfigureAwait(false);

        StringBuilder report = new();
        report.AppendLine("Delegation-rate KPI harness — the fraction of modules the polynomial EL path delegates to the tableau, per suite.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Configuration: {BuildConfiguration()}. Five engine columns per module: ElCtxSat (the KPI arm — the production composition, EL over the context-saturation tier over the SAT-backed oracle behind the seam), standalone SatBacked and Snapshot (wall-time comparands and the differential oracle), ContextSat (the context-saturation engine's own reach and cost), and ElSat (the retained prior chain, EL over the SAT-backed oracle — the measured alternative that keeps the pre-flip KPI trend interpretable).");
        report.AppendLine("KPI arm flip (increment 6): the KPI column (index 0) is the production composition ElCtxSat; the prior EL-over-SAT chain is retained as the ElSat column (index 4), its delegation rate freshly measured beside the new arm's from the same run so the trend bridge is a live measured fact, not a carried-forward constant.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Budget: {DecisionBudget.TotalSeconds:F0} s per module per engine (exceeding it records TIMEOUT; the default is {DefaultBudgetSeconds} s, overridable by {BudgetSecondsVariable}). DECIDED = the EL or context-saturation fast-path decided the module; DELEGATED = the verdict fell through both to the SAT oracle; the ContextSat column renders ADMITTED-DELEGATED where the survey admits the module but the reasoner delegates its whole verdict and NOT-ADMITTED where the survey rejects it, and a trailing backstop column records the off-fold-equality head count the RootEqualityOutsideFold latch drove per module. Each suite reports wall time (ms) then allocation (KB) per column; a corpus suite adds the EL classification lane (MB). Box load: record at run time.");
        report.AppendLine("Legend: DECIDED/DELEGATED and the delegation rate are the KPI arm's split; admitted+decided, admitted-delegated (the survey admits but the reasoner delegates the whole verdict — the EL flip made visible), and not-admitted (the survey rejects) are the ContextSat engine's orthogonal three-way axis and do not sum into the KPI split. The ContextSat speed and allocation medians scope to the modules it admitted and decided. The ElSat column runs the prior EL-over-SAT chain: on an EL-decided module it runs the identical EL fast-path as the KPI arm and agrees within noise, and on a module the EL path delegates it falls straight to the SAT oracle where the new KPI arm instead admits it through the context tier — the source of the delegation-rate gap between the two composition columns.");

        List<SuiteResult> suites = [];
        suites.Add(await MeasureSuiteAsync("synthetic", BuildSyntheticSuite(), report).ConfigureAwait(false));

        int corpusRootCount = await MeasureCorporaAsync(report, suites).ConfigureAwait(false);
        if(corpusRootCount == 0)
        {
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"No corpus present under {W3cCorpusPath.LibraryDirectory(BenchmarkLibraryFolder)} or the machine-local root; the synthetic suite alone establishes the baseline.");
        }

        AppendTotals(report, suites);

        await File.WriteAllTextAsync(outputPath, report.ToString(), TestContext.CancellationToken).ConfigureAwait(false);
        TestContext.WriteLine(report.ToString());

        SuiteResult synthetic = suites[0];
        int disagreements = 0;
        foreach(SuiteResult suite in suites)
        {
            disagreements += suite.DisagreementCount;
        }

        Assert.AreNotEqual(0, synthetic.DecidedCount, "The synthetic suite must decide at least one module by the EL or context fast-path, or the delegation rate cannot be nonfull.");
        Assert.AreNotEqual(0, synthetic.DelegatedCount, "The synthetic suite must delegate at least one module to the SAT oracle, or the delegation rate cannot be nonzero.");
        Assert.AreEqual(0, synthetic.ExpectationMismatchCount, "A synthetic module landed on the opposite side of the KPI fast-path boundary from its known-mix tag.");
        Assert.AreEqual(0, disagreements, "The KPI arm and its independent comparand engine disagreed on consistency for at least one module both decided.");
    }

    /// <summary>
    /// Pins the vendored benchmark corpora against bitrot: every module file
    /// under <c>Material/Benchmark/</c> parses and maps to a nonempty module.
    /// Unlike the measurement this runs unconditionally — the corpus is
    /// committed, so at least one corpus root must exist, and a file the
    /// loader would silently parse-skip fails the pin by name instead.
    /// </summary>
    [TestMethod]
    public async Task VendoredBenchmarkCorporaLoadAndMapCleanly()
    {
        string benchmarkRoot = W3cCorpusPath.LibraryDirectory(BenchmarkLibraryFolder);
        Assert.IsTrue(Directory.Exists(benchmarkRoot), $"The vendored benchmark library folder is missing: {benchmarkRoot}.");

        string[] corpusRoots = Directory.GetDirectories(benchmarkRoot);
        Assert.IsNotEmpty(corpusRoots, $"No corpus is vendored under {benchmarkRoot}; the OWL2Bench per-profile TBoxes are expected at minimum.");

        int moduleCount = 0;
        foreach(string corpusRoot in corpusRoots)
        {
            string corpusName = Path.GetFileName(corpusRoot);
            int rootModuleCount = 0;
            foreach(MeasuredCase measuredCase in await LoadCorpusModulesAsync(corpusRoot).ConfigureAwait(false))
            {
                moduleCount++;
                rootModuleCount++;
                Assert.IsFalse(measuredCase.ParseFailed, $"The vendored corpus file {corpusName}/{measuredCase.Name} no longer parses and maps cleanly.");
                Assert.IsNotEmpty(measuredCase.Module!.Axioms, $"The vendored corpus file {corpusName}/{measuredCase.Name} mapped to an empty module.");

                //Always-on census smoke pin: the full-construct walker stays alive on every vendored module,
                //returning a non-empty census with at least one axiom-layer key — no environment gate.
                IReadOnlyList<(string Key, int Count)> census = OwlConstructCensus.Count(measuredCase.Module!);
                Assert.IsNotEmpty(census, $"The full-construct census of {corpusName}/{measuredCase.Name} is empty.");
                Assert.IsTrue(ContainsAxiomLayerKey(census), $"The full-construct census of {corpusName}/{measuredCase.Name} carries no axiom-layer key.");
            }

            //Per-root non-emptiness: a global count alone would let one vendored
            //corpus mask another emptied by an extension-filter regression — an
            //all-functional-syntax root vanishes while a sibling of a different
            //syntax keeps the global count nonzero. Each vendored root must load.
            Assert.AreNotEqual(0, rootModuleCount, $"The vendored corpus root {corpusName} under {benchmarkRoot} loaded no module files; an extension-filter or reader regression may have emptied it.");
        }

        Assert.AreNotEqual(0, moduleCount, $"The vendored corpus roots under {benchmarkRoot} hold no module files.");
    }

    /// <summary>
    /// Pins the differential-oracle predicate: two engines that both reached a
    /// verdict and conflict on consistency are a disagreement, two that agree are
    /// not, and a comparand that abstained carries no verdict and is skipped
    /// rather than scored — since the composition-preview column is verdict-identical
    /// to the KPI arm by construction, no natural corpus disagreement can seed this,
    /// so the predicate is pinned directly. Always-on: the oracle is a correctness
    /// gate, not a measurement.
    /// </summary>
    [TestMethod]
    public void DisagreesScoresConsistencyConflictAndSkipsAbstention()
    {
        ModuleDecision consistent = ModuleDecision.Decided(new ModuleVerdict(true, []), ReasoningDecisionStatistics.Empty);
        ModuleDecision inconsistent = ModuleDecision.Decided(new ModuleVerdict(false, []), ReasoningDecisionStatistics.Empty);
        ModuleDecision abstained = ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty);

        Assert.IsTrue(Disagrees(consistent, inconsistent), "Two engines that both reached a verdict and conflict on consistency must count as a disagreement.");
        Assert.IsFalse(Disagrees(consistent, consistent), "Two engines that agree on consistency must not count as a disagreement.");
        Assert.IsFalse(Disagrees(consistent, abstained), "An abstaining comparand carries no verdict and must be skipped, not scored as agreement or disagreement.");
        Assert.IsFalse(Disagrees(abstained, inconsistent), "A KPI arm with no verdict must be skipped rather than scored against a comparand.");
    }

    /// <summary>
    /// Pins the sync-completion invariant that keeps the thread-local allocation
    /// measurement exact: an engine that returns before completing would hand
    /// back a delta polluted by another thread's work, so the timing method
    /// rejects it. A deliberately asynchronous fake engine must trip the
    /// invariant. Always-on: it protects the measurement's validity.
    /// </summary>
    [TestMethod]
    public async Task MeasureRejectsAnEngineThatDoesNotCompleteSynchronously()
    {
        //The fake stays pending until the test releases it, so the timing
        //method's completion check reads incomplete DETERMINISTICALLY — a
        //yield-based fake races the thread pool, whose continuation can finish
        //before the check under a parallel suite and flip the row flaky.
        PendingEngine pending = new();
        bool threw = false;
        try
        {
            await MeasureAsync(EmptyModule, pending.Decide).ConfigureAwait(false);
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        pending.Release();
        Assert.IsTrue(threw, "The timing method must reject an engine that does not complete synchronously, protecting the thread-local allocation measurement.");
    }

    /// <summary>A deliberately incomplete engine: its decision stays pending until <see cref="Release"/>, so the timing method's sync-completion check observes an incomplete decision without racing the thread pool.</summary>
    private sealed class PendingEngine
    {
        /// <summary>The completion the pending decision rides; continuations run asynchronously so the release never inlines into the releasing thread.</summary>
        private TaskCompletionSource<ModuleDecision> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Returns the pending decision — never complete before <see cref="Release"/>.</summary>
        /// <param name="module">The module to decide, unused.</param>
        /// <param name="cancellationToken">The budget token, unused.</param>
        /// <returns>The pending decision.</returns>
        public ValueTask<ModuleDecision> Decide(ReasoningModule module, CancellationToken cancellationToken)
        {
            _ = module;
            _ = cancellationToken;

            return new ValueTask<ModuleDecision>(Source.Task);
        }

        /// <summary>Completes the pending decision after the assertion, so no orphaned task outlives the row.</summary>
        public void Release()
        {
            Source.TrySetResult(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty));
        }

    }

    /// <summary>
    /// Pins the context column's admitted branch on a pure-TBox Horn-ALCHI module —
    /// the branch whose admitted-scoped speed and allocation medians and query-context
    /// saturation telemetry are exercised deterministically, independent of which
    /// ABox-bearing modules the ground slice admits. A module the engine admits and
    /// decides is recognised as context-decided and reports saturation work. Always-on:
    /// it covers the column's scoping and telemetry path deterministically.
    /// </summary>
    [TestMethod]
    public async Task ContextColumnDecidesAnAdmittedTboxModule()
    {
        ReasoningModule admitted = ContextAdmittedModule();

        //The engine is called directly with the test token — the measurement wrapper's
        //deadline belongs to the opt-in KPI lane, never to this always-on pin.
        ModuleDecision decision = await DecideContextSat(admitted, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The context engine must admit and decide a pure-TBox Horn-ALCHI module, so its admitted-scoped medians and telemetry are exercised.");
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The decision rides the context path, not a delegation.");
        Assert.IsGreaterThan(0, decision.Statistics.ContextTotals.ClausesDerived, "An admitted, decided module must report saturation work on its context telemetry.");
    }

    /// <summary>Which side of the KPI arm's decide/delegate boundary a synthetic module is known to fall on, the self-check tag the measured bucket is compared against. The KPI arm is the production composition, so a beyond-EL module the context-saturation tier admits and decides is tagged <see cref="Decided"/>, not delegated.</summary>
    private enum ExpectedKpiPath
    {
        /// <summary>The EL or context-saturation fast-path decides the module whole.</summary>
        Decided,
        /// <summary>The module falls through both fast-path tiers and is delegated to the SAT oracle.</summary>
        Delegated,
    }

    /// <summary>One suite entry: a labelled module to measure, its known-mix tag when synthetic, the source document a corpus entry retains for the classification lane, and whether its source failed to parse or map.</summary>
    /// <param name="Name">The module's label in the report.</param>
    /// <param name="Expected">The known-mix tag for a synthetic module; <see langword="null"/> for a corpus module carrying no expectation.</param>
    /// <param name="Module">The module to decide; <see langword="null"/> when <paramref name="ParseFailed"/> is set.</param>
    /// <param name="ParseFailed">Whether the source failed to parse or map and so is counted as a parse-skip rather than measured.</param>
    /// <param name="Document">The source ontology document a corpus entry retains so the classification lane can classify it; <see langword="null"/> for a synthetic entry built from axioms directly.</param>
    private sealed record MeasuredCase(string Name, ExpectedKpiPath? Expected, ReasoningModule? Module, bool ParseFailed, OwlOntologyDocument? Document = null);

    /// <summary>One engine's timed decision of a module: the decision, elapsed milliseconds, and thread-local allocated bytes within budget, or a timeout marker when the budget was exceeded.</summary>
    /// <param name="Decision">The reached decision within budget; <see langword="null"/> on timeout.</param>
    /// <param name="Milliseconds">The elapsed milliseconds within budget; <see langword="null"/> on timeout.</param>
    /// <param name="AllocatedBytes">The thread-local bytes the decision allocated within budget; <see langword="null"/> on timeout, where the partial work is not comparable.</param>
    /// <param name="TimedOut">Whether the decision exceeded the budget.</param>
    private readonly record struct TimedDecision(ModuleDecision? Decision, double? Milliseconds, long? AllocatedBytes, bool TimedOut);

    /// <summary>One engine column of the measurement matrix: its report label, its decision entry, and whether it is the context-saturation column whose median scopes to admitted-and-decided modules and whose non-admitted cell renders distinctly.</summary>
    /// <param name="Label">The column's header label.</param>
    /// <param name="Decide">The engine's decision entry.</param>
    /// <param name="IsContextColumn">Whether this is the context-saturation column: its abstained modules render <c>ADMITTED-DELEGATED</c> (survey admits, reasoner delegates the whole verdict) or <c>NOT-ADMITTED</c> (survey rejects) split by the production-default survey, and its speed/allocation medians scope to the modules it actually decided.</param>
    private sealed record EngineColumn(string Label, EngineDecision Decide, bool IsContextColumn);

    /// <summary>One suite's aggregated measurement: the decide/delegate counts, the skip/timeout counts, the known-mix mismatch and differential-disagreement counts, and the context engine's own admitted-versus-not-admitted partition.</summary>
    /// <param name="Label">The suite's label.</param>
    /// <param name="ModuleCount">The number of modules measured (parse-skips excluded).</param>
    /// <param name="DecidedCount">The number the KPI arm decided by the EL fast-path.</param>
    /// <param name="DelegatedCount">The number the KPI arm delegated to the tableau oracle.</param>
    /// <param name="FragmentRelativeCount">The number whose EL-coupled decision is scoped to the supported fragment.</param>
    /// <param name="TimeoutCount">The number whose EL-coupled decision exceeded the budget.</param>
    /// <param name="ParseSkipCount">The number of sources skipped for a parse or mapping failure.</param>
    /// <param name="ExpectationMismatchCount">The number of synthetic modules that landed on the opposite side of the boundary from their tag.</param>
    /// <param name="DisagreementCount">The number of consistency disagreements between the KPI arm and a comparand that also decided its module whole, summed over the selected standalone comparand(s), the context column, and the ElSat column under the whole-verdict admission gate.</param>
    /// <param name="ContextDecidedCount">The number the context-saturation engine admitted and decided by saturation — an axis orthogonal to the KPI decide/delegate split.</param>
    /// <param name="ContextAdmittedDelegatedCount">The number the context-saturation survey admitted yet the reasoner delegated the whole verdict on (cand-C): the EL admitted+decided flip, recorded distinctly from a survey rejection that a single NOT-ADMITTED cell conflates with it.</param>
    /// <param name="ContextNotAdmittedCount">The number the context-saturation survey did not admit and passed to its abstaining fallback.</param>
    /// <param name="ContextBackstopLatchHeads">The off-fold-equality head count the backstop latched summed over the suite's modules — the corpus census the boolean latch alone could not carry.</param>
    /// <param name="ContextBackstopLatchModules">The number of the suite's modules the off-fold equality backstop latched on.</param>
    /// <param name="ElSatDecidedCount">The number the retained prior chain (ElSat) decided by its EL fast-path — its own decide/delegate split, freshly measured beside the KPI arm's.</param>
    /// <param name="ElSatDelegatedCount">The number the retained prior chain delegated to the SAT oracle.</param>
    /// <param name="BatteryOnlyCount">The number of context-decided modules with no whole automated comparand — counted battery-only and named, the certified battery being their sole oracle.</param>
    private sealed record SuiteResult(
        string Label,
        int ModuleCount,
        int DecidedCount,
        int DelegatedCount,
        int FragmentRelativeCount,
        int TimeoutCount,
        int ParseSkipCount,
        int ExpectationMismatchCount,
        int DisagreementCount,
        int ContextDecidedCount,
        int ContextAdmittedDelegatedCount,
        int ContextNotAdmittedCount,
        long ContextBackstopLatchHeads,
        int ContextBackstopLatchModules,
        int ElSatDecidedCount,
        int ElSatDelegatedCount,
        int BatteryOnlyCount)
    {
        /// <summary>The number of modules the KPI arm classified either way — the delegation-rate denominator.</summary>
        public int ClassifiedCount => DecidedCount + DelegatedCount;
    }

    /// <summary>One engine's budget-aware full decision of a module.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token the decision honours.</param>
    /// <returns>The decision.</returns>
    private delegate ValueTask<ModuleDecision> EngineDecision(ReasoningModule module, CancellationToken cancellationToken);

    /// <summary>
    /// The ElSat column: the retained prior chain — the EL pay-as-you-go engine
    /// composed directly with the SAT-backed oracle behind the seam, with no
    /// context tier between them. It is the measured alternative kept beside the
    /// production composition so the delegation trend stays interpretable and
    /// column distinctness is a live measured fact; a module the EL fast-path
    /// delegates falls straight to the SAT oracle here, where the KPI arm instead
    /// admits it through the context tier.
    /// </summary>
    internal static DescriptionLogicDelegate ElSatComposition { get; } =
        ElCoupledModuleReasoner.CreateDelegate(SatTableauModuleReasoner.CreateDelegate(SatSearchMode.ConflictLearning, ReasoningBudget.Unbounded, useIncrementalSession: false));

    /// <summary>The context-saturation engine composed with an abstaining fallback: the column that measures the engine's own reach and cost, where a module the seam does not decide falls to <see cref="DecideNotAdmitted"/> and surfaces as an <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> the report renders ADMITTED-DELEGATED (the survey admitted the module but the reasoner delegated its whole verdict) or NOT-ADMITTED (the survey rejected it), split by the production-default survey. Under an unbounded budget the saturation never abstains on budget, so the only abstention is the fallback's — cleanly disjoint from a timeout and from a decision.</summary>
    private static DescriptionLogicDelegate ContextSatWithNotAdmitted { get; } =
        ReasoningEngines.ContextSaturation(ReasoningBudget.Unbounded, DecideNotAdmitted);

    /// <summary>The KPI arm: the EL-coupled engine over the context-saturation tier over the SAT-backed oracle — the production composition wired at the composition root. The EL fast-path decides an EL module, the context tier admits and decides a beyond-EL Horn-ALCHI module, and only a module beyond both falls through to the SAT oracle; the delegation rate is the fraction that falls through.</summary>
    internal static DescriptionLogicDelegate ElCtxSatComposition { get; } =
        ReasoningEngines.ElCoupled(ReasoningEngines.ContextSaturation(ReasoningBudget.Unbounded, ReasoningEngines.SatBacked(ReasoningBudget.Unbounded)));

    /// <summary>The five engine columns of the measurement matrix, in report order: the KPI arm (the production composition), the two standalone comparands, the context-saturation engine's own column, and the retained prior chain.</summary>
    private static IReadOnlyList<EngineColumn> EngineColumns { get; } =
    [
        new EngineColumn("ElCtxSat", DecideElCtxSat, IsContextColumn: false),
        new EngineColumn("SatBacked", DecideSatBacked, IsContextColumn: false),
        new EngineColumn("Snapshot", DecideSnapshot, IsContextColumn: false),
        new EngineColumn("ContextSat", DecideContextSat, IsContextColumn: true),
        new EngineColumn("ElSat", DecideElSat, IsContextColumn: false),
    ];

    /// <summary>The column index of the KPI arm, the production composition and the reference of the differential oracle.</summary>
    private const int KpiColumnIndex = 0;

    /// <summary>The column index of the standalone SAT-backed comparand, the independent oracle for an EL-decided module and one of the two folds for a context-decided module.</summary>
    private const int SatColumnIndex = 1;

    /// <summary>The column index of the standalone snapshot comparand, the independent oracle for a SAT-delegated module and one of the two folds for a context-decided module.</summary>
    private const int SnapshotColumnIndex = 2;

    /// <summary>The column index of the context-saturation engine's own column.</summary>
    private const int ContextColumnIndex = 3;

    /// <summary>The column index of the retained prior chain (the ElSat column), the measured alternative whose delegation rate is reported beside the KPI arm's.</summary>
    private const int ElSatColumnIndex = 4;

    /// <summary>The retained prior chain's full decision (the ElSat column): the EL pay-as-you-go engine over the SAT-backed oracle, no context tier.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The decision.</returns>
    private static ValueTask<ModuleDecision> DecideElSat(ReasoningModule module, CancellationToken cancellationToken)
    {
        return ElSatComposition(module, cancellationToken);
    }

    /// <summary>The SAT-backed engine's full decision, the independent differential comparand; the synchronous CPU-bound work is wrapped in a completed decision to match the engine seam.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The decision.</returns>
    private static ValueTask<ModuleDecision> DecideSatBacked(ReasoningModule module, CancellationToken cancellationToken)
    {
        return new ValueTask<ModuleDecision>(SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession: false, cancellationToken));
    }

    /// <summary>The snapshot tableau engine's full decision, the wall-time comparand the EL-coupled engine delegates to; the synchronous CPU-bound work is wrapped in a completed decision to match the engine seam.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The decision.</returns>
    private static ValueTask<ModuleDecision> DecideSnapshot(ReasoningModule module, CancellationToken cancellationToken)
    {
        return new ValueTask<ModuleDecision>(AlcModuleReasoner.DecideModule(module, cancellationToken));
    }

    /// <summary>The context-saturation engine's full decision behind its abstaining fallback: a decision when the engine admits the module, an on-budget abstention rendered NOT-ADMITTED when it does not.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The decision.</returns>
    private static ValueTask<ModuleDecision> DecideContextSat(ReasoningModule module, CancellationToken cancellationToken)
    {
        return ContextSatWithNotAdmitted(module, cancellationToken);
    }

    /// <summary>The KPI arm's full decision (the production composition): the EL fast-path, else the context-saturation tier, else the SAT oracle.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The decision.</returns>
    private static ValueTask<ModuleDecision> DecideElCtxSat(ReasoningModule module, CancellationToken cancellationToken)
    {
        return ElCtxSatComposition(module, cancellationToken);
    }

    /// <summary>The abstaining fallback for the context column: it decides no module, returning an on-budget abstention so a context-non-admitted module surfaces as NOT-ADMITTED rather than borrowing another engine's verdict or cost.</summary>
    /// <param name="module">The module the context engine did not admit.</param>
    /// <param name="cancellationToken">The budget token, unused because the fallback does no work.</param>
    /// <returns>An abstaining decision carrying only the module's axiom count.</returns>
    private static ValueTask<ModuleDecision> DecideNotAdmitted(ReasoningModule module, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return new ValueTask<ModuleDecision>(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty with { ModuleAxiomCount = module.Axioms.Count }));
    }

    /// <summary>
    /// Times and measures one engine's decision of the module under a fresh
    /// per-module budget: the thread-local allocated bytes and elapsed wall time
    /// bracket the synchronous decision, and a decision exceeding the budget
    /// records a timeout rather than wedging the run. The engine seam is
    /// contractually synchronous, which is what makes the thread-local delta
    /// exact; a decision that had not completed synchronously is an invariant
    /// violation, caught before any polluted delta is recorded.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="engine">The engine's decision entry.</param>
    /// <returns>The timed and measured decision.</returns>
    /// <exception cref="InvalidOperationException">The engine returned before completing synchronously.</exception>
    private static async Task<TimedDecision> MeasureAsync(ReasoningModule module, EngineDecision engine)
    {
        using CancellationTokenSource budget = new(DecisionBudget);
        //Allocate the stopwatch before the allocation baseline so the harness's
        //own stopwatch does not enter the engine's measured delta, matching the
        //hoisted cancellation source above it.
        Stopwatch stopwatch = new();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();
        ValueTask<ModuleDecision> pending;
        try
        {
            pending = engine(module, budget.Token);
        }
        catch(OperationCanceledException)
        {
            stopwatch.Stop();

            return new TimedDecision(Decision: null, Milliseconds: null, AllocatedBytes: null, TimedOut: true);
        }

        stopwatch.Stop();
        if(!pending.IsCompleted)
        {
            throw new InvalidOperationException("A timed engine returned a decision that had not completed synchronously; the thread-local allocation measurement requires synchronous completion at the engine seam.");
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            ModuleDecision decision = await pending.ConfigureAwait(false);

            return new TimedDecision(decision, stopwatch.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore, TimedOut: false);
        }
        catch(OperationCanceledException)
        {
            return new TimedDecision(Decision: null, Milliseconds: null, AllocatedBytes: null, TimedOut: true);
        }
    }

    /// <summary>Whether the KPI arm and a comparand engine that both reached a verdict disagree on consistency; a comparand that abstained (no verdict) or timed out is not a disagreement — silence is neither agreement nor conflict.</summary>
    /// <param name="kpi">The KPI arm's decision, the reference.</param>
    /// <param name="comparand">The comparand engine's decision.</param>
    /// <returns><see langword="true"/> when both carry a verdict and their consistency differs.</returns>
    internal static bool Disagrees(ModuleDecision kpi, ModuleDecision comparand)
    {
        return kpi.Verdict is ModuleVerdict kpiVerdict
            && comparand.Verdict is ModuleVerdict comparandVerdict
            && kpiVerdict.IsConsistent != comparandVerdict.IsConsistent;
    }

    /// <summary>
    /// Warms every engine column on the empty module, on a context-admitted
    /// pure-TBox module, and on every synthetic shape so the timed loop excludes
    /// first-touch JIT. The ground slice admits the synthetic shapes' object ABox,
    /// so the synthetics now warm the context engine's ground-context and
    /// asserted-edge paths; the pure-TBox admitted module additionally warms the
    /// query-context deep join, successor, predecessor, and equality rule bodies a
    /// name-free module drives before they are timed.
    /// </summary>
    private static async Task WarmDecisionPathsAsync()
    {
        using CancellationTokenSource budget = new(DecisionBudget);
        IReadOnlyList<(string Name, ReasoningModule Module)> warmupModules = SyntheticSuiteModules();
        ReasoningModule admitted = ContextAdmittedModule();
        foreach(EngineColumn column in EngineColumns)
        {
            await column.Decide(EmptyModule, budget.Token).ConfigureAwait(false);
            await column.Decide(admitted, budget.Token).ConfigureAwait(false);
            foreach((string _, ReasoningModule module) in warmupModules)
            {
                await column.Decide(module, budget.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A pure-TBox Horn-ALCHI module the context survey admits and the engine saturates — no ABox, so it exercises the context engine's query-context saturation rule bodies without a ground-context short-circuit.</summary>
    /// <returns>The admitted module.</returns>
    private static ReasoningModule ContextAdmittedModule()
    {
        return Module(
            SubClassOf(Class("A"), Class("B")),
            SubClassOf(Class("B"), Some("r", Class("C"))),
            SubClassOf(Some("r", Class("C")), Class("A")),
            Transitive("r"));
    }

    /// <summary>
    /// Measures one suite: decides every module with the five engine columns,
    /// renders the per-module table and the suite summary — including the two
    /// per-composition delegation-rate lines — into the report, and returns the
    /// suite's aggregated result.
    /// </summary>
    /// <param name="label">The suite's label.</param>
    /// <param name="cases">The suite's entries.</param>
    /// <param name="report">The report the table and summary append to.</param>
    /// <returns>The suite's aggregated result.</returns>
    private static async Task<SuiteResult> MeasureSuiteAsync(string label, IReadOnlyList<MeasuredCase> cases, StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"== suite {label} ==");
        report.AppendLine("module | expected | actual | match | outcome | consistent | ElCtxSat (ms) | SatBacked (ms) | Snapshot (ms) | ContextSat (ms) | ElSat (ms) | backstop");
        report.AppendLine("---|---|---|:---:|---|:---:|---:|---:|---:|---:|---:|---:");

        int moduleCount = 0;
        int decided = 0;
        int delegated = 0;
        int elSatDecided = 0;
        int elSatDelegated = 0;
        int batteryOnly = 0;
        int fragmentRelative = 0;
        int timeouts = 0;
        int parseSkips = 0;
        int mismatches = 0;
        int disagreements = 0;
        int contextDecided = 0;
        int contextAdmittedDelegated = 0;
        int contextNotAdmitted = 0;
        int contextMaxClauses = 0;
        long contextBackstopLatchHeads = 0;
        int contextBackstopLatchModules = 0;
        List<string> batteryOnlyModules = [];
        List<double>[] columnMilliseconds = NewColumnAccumulators();
        List<double>[] columnKilobytes = NewColumnAccumulators();
        List<double> contextClausesDerived = [];
        Dictionary<string, int> remainderCensus = new(StringComparer.Ordinal);
        Dictionary<string, int> constructAggregate = new(StringComparer.Ordinal);
        List<(string Name, IReadOnlyList<(string Key, int Count)> Census)> constructCensuses = [];
        StringBuilder allocationTable = new();
        allocationTable.AppendLine("module | ElCtxSat (KB) | SatBacked (KB) | Snapshot (KB) | ContextSat (KB) | ElSat (KB)");
        allocationTable.AppendLine("---|---:|---:|---:|---:|---:");

        foreach(MeasuredCase measuredCase in cases)
        {
            if(measuredCase.Module is not ReasoningModule module)
            {
                parseSkips++;
                report.AppendLine(CultureInfo.InvariantCulture, $"{measuredCase.Name} | {FormatExpected(measuredCase.Expected)} | PARSE-SKIP | - | - | - | - | - | - | - | - | -");
                allocationTable.AppendLine(CultureInfo.InvariantCulture, $"{measuredCase.Name} | - | - | - | - | -");

                continue;
            }

            moduleCount++;
            IReadOnlyList<(string Key, int Count)> moduleCensus = OwlConstructCensus.Count(module);
            constructCensuses.Add((measuredCase.Name, moduleCensus));
            foreach((string key, int count) in moduleCensus)
            {
                constructAggregate[key] = constructAggregate.TryGetValue(key, out int seen) ? seen + count : count;
            }

            TimedDecision[] measured = new TimedDecision[EngineColumns.Count];
            for(int columnIndex = 0; columnIndex < EngineColumns.Count; columnIndex++)
            {
                measured[columnIndex] = await MeasureAsync(module, EngineColumns[columnIndex].Decide).ConfigureAwait(false);
            }

            for(int columnIndex = 0; columnIndex < EngineColumns.Count; columnIndex++)
            {
                TimedDecision cell = measured[columnIndex];
                bool countForMedian = EngineColumns[columnIndex].IsContextColumn ? IsContextDecided(cell) : cell.Milliseconds is not null;
                if(countForMedian && cell.Milliseconds is double cellMilliseconds)
                {
                    columnMilliseconds[columnIndex].Add(cellMilliseconds);
                }

                if(countForMedian && cell.AllocatedBytes is long cellBytes)
                {
                    columnKilobytes[columnIndex].Add(cellBytes / 1024.0);
                }
            }

            TimedDecision context = measured[ContextColumnIndex];

            //cand-A: the off-fold equality backstop delegates via the seam, so its latch
            //surfaces on the delegated decision's context totals (never on a bare return);
            //the count reads per module — zero on a decision and on every delegation the
            //backstop did not drive.
            bool contextAdmitted = ContextModuleSurvey.Survey(module).Admitted;
            long moduleBackstopHeads = !IsContextDecided(context) && context.Decision is ModuleDecision contextRun ? contextRun.Statistics.ContextTotals.RootEqualityOutsideFoldHeads : 0;
            if(moduleBackstopHeads > 0)
            {
                contextBackstopLatchHeads += moduleBackstopHeads;
                contextBackstopLatchModules++;
            }

            if(IsContextDecided(context) && context.Decision is ModuleDecision contextDecision)
            {
                contextDecided++;
                ContextSaturationStatistics contextTotals = contextDecision.Statistics.ContextTotals;
                contextClausesDerived.Add(contextTotals.ClausesDerived);
                contextMaxClauses = Math.Max(contextMaxClauses, contextTotals.MaxContextClauses);
            }
            else if(context.Decision is ModuleDecision abstained && abstained.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
            {
                //cand-C: the context column's fallback abstains for BOTH a survey rejection
                //and an admitted-but-delegated module (DecideModule delegated the whole
                //verdict), so the outcome is split by the production-default survey admission
                //— the EL admitted+decided flip surfaces as ADMITTED-DELEGATED distinctly from
                //a survey rejection, which a single NOT-ADMITTED cell conflates.
                if(contextAdmitted)
                {
                    contextAdmittedDelegated++;
                }
                else
                {
                    contextNotAdmitted++;
                }
            }

            allocationTable.AppendLine(CultureInfo.InvariantCulture, $"{measuredCase.Name} | {ModuleKilobyteCells(measured, contextAdmitted)}");

            if(measured[KpiColumnIndex].Decision is not ModuleDecision kpiDecision)
            {
                timeouts++;
                report.AppendLine(CultureInfo.InvariantCulture, $"{measuredCase.Name} | {FormatExpected(measuredCase.Expected)} | TIMEOUT | - | - | - | {ModuleMillisecondCells(measured, contextAdmitted)} | {moduleBackstopHeads.ToString(CultureInfo.InvariantCulture)}");

                continue;
            }

            //The KPI arm's decide/delegate split and the ElSat column's own split
            //both come from the same extracted bucketing unit, so the two
            //composition columns' delegation rates are measured from one run.
            KpiBucket bucket = BucketDecision(kpiDecision);
            switch(bucket)
            {
                case KpiBucket.Decided:
                {
                    decided++;

                    break;
                }
                case KpiBucket.Delegated:
                {
                    delegated++;

                    break;
                }
                default:
                {
                    break;
                }
            }

            if(measured[ElSatColumnIndex].Decision is ModuleDecision elSatDecision)
            {
                switch(BucketDecision(elSatDecision))
                {
                    case KpiBucket.Decided:
                    {
                        elSatDecided++;

                        break;
                    }
                    case KpiBucket.Delegated:
                    {
                        elSatDelegated++;

                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }

            if(kpiDecision.Outcome == ReasoningDecisionOutcome.DecidedFragmentRelative)
            {
                fragmentRelative++;
            }

            //The delegated-module remainder census reads the KPI arm's own verdict:
            //a context-decided module carries an empty remainder and drops out
            //of the census, and the context survey's remainder is never attached to a
            //returned verdict.
            if(kpiDecision.Verdict is ModuleVerdict remainderVerdict)
            {
                foreach(string construct in remainderVerdict.UnsupportedConstructs)
                {
                    remainderCensus[construct] = remainderCensus.TryGetValue(construct, out int seen) ? seen + 1 : 1;
                }
            }

            string matchCell = "-";
            if(measuredCase.Expected is ExpectedKpiPath expected)
            {
                bool matched = (expected == ExpectedKpiPath.Decided) == (bucket == KpiBucket.Decided);
                if(!matched)
                {
                    mismatches++;
                }

                matchCell = matched ? "yes" : "NO";
            }

            //The KPI arm's consistency is cross-checked under the whole-verdict rule:
            //the standalone comparand(s) the producer selects — SatBacked for an
            //EL-decided verdict, both standalone comparands for a context-decided
            //verdict, Snapshot for a SAT-decided one (never the SatBacked oracle the
            //verdict came from) — plus the context and ElSat columns. Every fold
            //passes the whole-verdict admission gate, so a fragment-relative or
            //abstaining comparand is dropped, not scored. A context-decided module no
            //standalone comparand admits is battery-only and named, the certified
            //battery being its sole oracle.
            VerdictProducer producer = DetectProducer(kpiDecision.Statistics);
            ComparandCandidates candidates = SelectComparands(producer);
            ComparandScore satScore = candidates.Sat ? ScoreComparand(measured[SatColumnIndex], kpiDecision) : default;
            ComparandScore snapshotScore = candidates.Snapshot ? ScoreComparand(measured[SnapshotColumnIndex], kpiDecision) : default;
            disagreements += satScore.Disagreements + snapshotScore.Disagreements;
            disagreements += DisagreementIncrement(kpiDecision, measured[ContextColumnIndex]);
            disagreements += DisagreementIncrement(kpiDecision, measured[ElSatColumnIndex]);
            if(IsBatteryOnly(producer, satScore.Admitted || snapshotScore.Admitted))
            {
                batteryOnly++;
                batteryOnlyModules.Add(measuredCase.Name);
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"{measuredCase.Name} | {FormatExpected(measuredCase.Expected)} | {FormatBucket(bucket)} | {matchCell} | {kpiDecision.Outcome} | {FormatConsistency(kpiDecision.Verdict)} | {ModuleMillisecondCells(measured, contextAdmitted)} | {moduleBackstopHeads.ToString(CultureInfo.InvariantCulture)}");
        }

        report.AppendLine();
        report.Append(allocationTable);

        int classified = decided + delegated;
        double rate = classified == 0 ? 0.0 : 100.0 * delegated / classified;
        int elSatClassified = elSatDecided + elSatDelegated;
        double elSatRate = elSatClassified == 0 ? 0.0 : 100.0 * elSatDelegated / elSatClassified;
        double? medianContextClauses = Median(contextClausesDerived);

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"modules = {moduleCount}; DECIDED = {decided}; DELEGATED = {delegated}; fragment-relative = {fragmentRelative}; timeouts = {timeouts}; parse-skips = {parseSkips}");
        report.AppendLine(CultureInfo.InvariantCulture, $"delegation_rate (ElCtxSat, KPI arm) = {rate.ToString("F1", CultureInfo.InvariantCulture)}% ({delegated} delegated / {classified} total)");
        report.AppendLine(CultureInfo.InvariantCulture, $"delegation_rate (ElSat, prior chain) = {elSatRate.ToString("F1", CultureInfo.InvariantCulture)}% ({elSatDelegated} delegated / {elSatClassified} total)");
        report.AppendLine(CultureInfo.InvariantCulture, $"context engine (orthogonal to the KPI split; the three states do not sum into decided+delegated): admitted+decided = {contextDecided}; admitted-delegated = {contextAdmittedDelegated}; not-admitted = {contextNotAdmitted}");
        report.AppendLine(CultureInfo.InvariantCulture, $"backstop latch (RootEqualityOutsideFold off-fold-equality heads): {contextBackstopLatchHeads.ToString(CultureInfo.InvariantCulture)} heads across {contextBackstopLatchModules} modules");
        AppendBatteryOnly(report, batteryOnlyModules);
        report.AppendLine(CultureInfo.InvariantCulture, $"median ms/module: ElCtxSat={FormatMilliseconds(Median(columnMilliseconds[KpiColumnIndex]))} SatBacked={FormatMilliseconds(Median(columnMilliseconds[SatColumnIndex]))} Snapshot={FormatMilliseconds(Median(columnMilliseconds[SnapshotColumnIndex]))} ContextSat(admitted)={FormatMilliseconds(Median(columnMilliseconds[ContextColumnIndex]))} ElSat={FormatMilliseconds(Median(columnMilliseconds[ElSatColumnIndex]))}");
        report.AppendLine(CultureInfo.InvariantCulture, $"median KB/module: ElCtxSat={FormatOptionalKilobytes(Median(columnKilobytes[KpiColumnIndex]))} SatBacked={FormatOptionalKilobytes(Median(columnKilobytes[SatColumnIndex]))} Snapshot={FormatOptionalKilobytes(Median(columnKilobytes[SnapshotColumnIndex]))} ContextSat(admitted)={FormatOptionalKilobytes(Median(columnKilobytes[ContextColumnIndex]))} ElSat={FormatOptionalKilobytes(Median(columnKilobytes[ElSatColumnIndex]))}");
        if(contextDecided > 0)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"context saturation (admitted+decided): median ClausesDerived={(medianContextClauses is double clauses ? clauses.ToString("F0", CultureInfo.InvariantCulture) : "-")}; max MaxContextClauses={contextMaxClauses}");
        }

        AppendRemainderCensus(report, remainderCensus);
        AppendConstructCensus(report, constructCensuses, constructAggregate);
        AppendClassificationLane(report, cases);

        return new SuiteResult(label, moduleCount, decided, delegated, fragmentRelative, timeouts, parseSkips, mismatches, disagreements, contextDecided, contextAdmittedDelegated, contextNotAdmitted, contextBackstopLatchHeads, contextBackstopLatchModules, elSatDecided, elSatDelegated, batteryOnly);
    }

    /// <summary>Builds an empty per-column accumulator array sized to the engine matrix.</summary>
    /// <returns>One empty list per engine column.</returns>
    private static List<double>[] NewColumnAccumulators()
    {
        List<double>[] accumulators = new List<double>[EngineColumns.Count];
        for(int columnIndex = 0; columnIndex < accumulators.Length; columnIndex++)
        {
            accumulators[columnIndex] = [];
        }

        return accumulators;
    }

    /// <summary>Whether the context-saturation column admitted and decided the module by saturation, the scope its speed and allocation medians are computed over.</summary>
    /// <param name="context">The context column's timed decision.</param>
    /// <returns><see langword="true"/> when the engine decided the module rather than abstaining to its fallback or timing out.</returns>
    private static bool IsContextDecided(TimedDecision context)
    {
        return context.Decision is ModuleDecision decision
            && decision.Outcome == ReasoningDecisionOutcome.Decided
            && decision.Statistics.ContextTotals.ContextDecided;
    }

    /// <summary>The saturation tier that produced a composed-chain decision's verdict, read from the two mutually exclusive fast-path flags on the decision's statistics.</summary>
    internal enum VerdictProducer
    {
        /// <summary>The EL pay-as-you-go fast-path decided the module.</summary>
        ElSaturation,

        /// <summary>The context-saturation tier decided the module.</summary>
        ContextSaturation,

        /// <summary>The verdict fell through both fast-path tiers to the SAT oracle.</summary>
        SatOracle,
    }

    /// <summary>Which side of the KPI arm's decide/delegate split a decision falls on.</summary>
    internal enum KpiBucket
    {
        /// <summary>The EL or context-saturation fast-path decided the module.</summary>
        Decided,

        /// <summary>The verdict fell through both fast-path tiers to the SAT oracle.</summary>
        Delegated,

        /// <summary>The decision reached no verdict (a budget abstention); outside the delegation-rate denominator, as a timeout is.</summary>
        Unclassified,
    }

    /// <summary>The standalone comparand columns the whole-verdict rule folds against a KPI decision, before each is passed through the whole-verdict admission gate.</summary>
    /// <param name="Sat">Whether the standalone SAT-backed column is a candidate comparand.</param>
    /// <param name="Snapshot">Whether the standalone snapshot column is a candidate comparand.</param>
    internal readonly record struct ComparandCandidates(bool Sat, bool Snapshot);

    /// <summary>One standalone comparand's contribution to a KPI decision's differential: whether it cleared the whole-verdict admission gate, and the disagreement it scored if it did.</summary>
    /// <param name="Disagreements">One when the comparand cleared the gate and conflicts with the KPI verdict on consistency; zero otherwise.</param>
    /// <param name="Admitted">Whether the comparand cleared the whole-verdict admission gate.</param>
    private readonly record struct ComparandScore(int Disagreements, bool Admitted);

    /// <summary>
    /// Detects which saturation tier produced a composed-chain decision's verdict
    /// from its statistics. The two fast-path flags are mutually exclusive by
    /// construction — each tier fills its counters only when it decided the module
    /// — so an EL-decided flag names the EL tier, a context-decided flag names the
    /// context tier, and neither names the SAT oracle the chain fell through to.
    /// </summary>
    /// <param name="statistics">The decision's statistics.</param>
    /// <returns>The producing tier.</returns>
    internal static VerdictProducer DetectProducer(ReasoningDecisionStatistics statistics)
    {
        return (statistics.ElTotals.ElDecided, statistics.ContextTotals.ContextDecided) switch
        {
            (true, _) => VerdictProducer.ElSaturation,
            (_, true) => VerdictProducer.ContextSaturation,
            _ => VerdictProducer.SatOracle,
        };
    }

    /// <summary>
    /// Buckets a KPI decision for the delegation rate: a decision the EL or context
    /// fast-path produced is DECIDED, one the SAT oracle produced is DELEGATED, and
    /// a decision that reached no verdict is unclassified and outside the rate's
    /// denominator — the same treatment a timeout receives. The same unit buckets
    /// both composition columns, so their rates are measured from one run.
    /// </summary>
    /// <param name="decision">A composition column's decision.</param>
    /// <returns>The decide/delegate bucket.</returns>
    internal static KpiBucket BucketDecision(ModuleDecision decision)
    {
        return decision.Outcome switch
        {
            ReasoningDecisionOutcome.Decided or ReasoningDecisionOutcome.DecidedFragmentRelative =>
                DetectProducer(decision.Statistics) == VerdictProducer.SatOracle ? KpiBucket.Delegated : KpiBucket.Decided,
            _ => KpiBucket.Unclassified,
        };
    }

    /// <summary>
    /// Selects the standalone comparand columns the whole-verdict rule folds for a KPI
    /// producer: an EL-decided verdict is cross-checked against standalone SatBacked, a
    /// context-decided verdict against BOTH standalone comparands (the
    /// shared-fragment differential), and a SAT-decided verdict against standalone
    /// Snapshot — never against the SatBacked oracle it came from. Each candidate
    /// still clears the whole-verdict admission gate at the fold.
    /// </summary>
    /// <param name="producer">The KPI decision's producing tier.</param>
    /// <returns>The candidate comparand columns.</returns>
    internal static ComparandCandidates SelectComparands(VerdictProducer producer)
    {
        return producer switch
        {
            VerdictProducer.ElSaturation => new ComparandCandidates(Sat: true, Snapshot: false),
            VerdictProducer.ContextSaturation => new ComparandCandidates(Sat: true, Snapshot: true),
            _ => new ComparandCandidates(Sat: false, Snapshot: true),
        };
    }

    /// <summary>
    /// Whether a comparand decision is a whole comparand admissible under the
    /// whole-verdict rule: it reached a verdict inside its own fragment
    /// (<see cref="ReasoningDecisionOutcome.Decided"/>), so a fragment-relative
    /// verdict (a consistency claim scoped to a sub-fragment) and a budget
    /// abstention (no verdict) are both dropped at the fold rather than scored
    /// against the KPI arm's whole verdict.
    /// </summary>
    /// <param name="comparand">The comparand's decision.</param>
    /// <returns><see langword="true"/> when the comparand decided its module whole.</returns>
    internal static bool IsAdmissibleComparand(ModuleDecision comparand)
    {
        return comparand.Outcome == ReasoningDecisionOutcome.Decided;
    }

    /// <summary>Whether an admitted comparand scores a disagreement against the KPI decision under the whole-verdict rule: a comparand that did not decide its module whole is dropped at the admission gate and never scores, and an admitted comparand scores exactly when it conflicts on consistency.</summary>
    /// <param name="kpi">The KPI arm's decision.</param>
    /// <param name="comparand">The comparand's decision.</param>
    /// <returns><see langword="true"/> when the comparand is admissible and conflicts on consistency.</returns>
    internal static bool ScoresDisagreement(ModuleDecision kpi, ModuleDecision comparand)
    {
        return IsAdmissibleComparand(comparand) && Disagrees(kpi, comparand);
    }

    /// <summary>
    /// Whether a context-decided module has no valid automated comparand — its
    /// producer is the context tier and neither standalone comparand cleared the
    /// whole-verdict admission gate (both declined or decided only fragment-relative,
    /// the inverse-heavy case beyond the ALC fallback). Such a module is counted
    /// battery-only and named in the report, the certified battery being its sole
    /// oracle.
    /// </summary>
    /// <param name="producer">The KPI decision's producing tier.</param>
    /// <param name="anyComparandAdmitted">Whether any standalone comparand cleared the admission gate.</param>
    /// <returns><see langword="true"/> when the module is battery-only.</returns>
    internal static bool IsBatteryOnly(VerdictProducer producer, bool anyComparandAdmitted)
    {
        return producer == VerdictProducer.ContextSaturation && !anyComparandAdmitted;
    }

    /// <summary>Scores one standalone comparand column against the KPI decision under the whole-verdict admission gate: a comparand that did not decide its module whole (or timed out) is not admitted and scores no disagreement, and an admitted comparand contributes a disagreement exactly when it conflicts on consistency.</summary>
    /// <param name="comparand">The comparand column's timed decision.</param>
    /// <param name="kpi">The KPI arm's decision.</param>
    /// <returns>The comparand's admission and disagreement contribution.</returns>
    private static ComparandScore ScoreComparand(TimedDecision comparand, ModuleDecision kpi)
    {
        if(comparand.Decision is not ModuleDecision decision)
        {
            return new ComparandScore(Disagreements: 0, Admitted: false);
        }

        return new ComparandScore(ScoresDisagreement(kpi, decision) ? 1 : 0, IsAdmissibleComparand(decision));
    }

    /// <summary>The disagreement contribution of one always-folded comparand column (the context or ElSat column) under the whole-verdict admission gate: one when it decided its module whole and conflicts with the KPI arm on consistency, zero when it did not decide whole, abstained, or timed out.</summary>
    /// <param name="kpi">The KPI arm's decision.</param>
    /// <param name="comparand">The comparand's timed decision.</param>
    /// <returns>One on a scored disagreement, zero otherwise.</returns>
    private static int DisagreementIncrement(ModuleDecision kpi, TimedDecision comparand)
    {
        return comparand.Decision is ModuleDecision decision && ScoresDisagreement(kpi, decision) ? 1 : 0;
    }

    /// <summary>Formats the KPI arm's decide/delegate bucket for a report row.</summary>
    /// <param name="bucket">The bucket.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatBucket(KpiBucket bucket)
    {
        return bucket switch
        {
            KpiBucket.Decided => "DECIDED",
            KpiBucket.Delegated => "DELEGATED",
            _ => "UNCLASSIFIED",
        };
    }

    /// <summary>Appends the suite's battery-only modules: the context-decided modules no standalone comparand admits, each named, the certified battery being their sole oracle. Appends nothing when every context-decided module had a whole automated comparand.</summary>
    /// <param name="report">The report the section appends to.</param>
    /// <param name="batteryOnlyModules">The battery-only module names, in measurement order.</param>
    private static void AppendBatteryOnly(StringBuilder report, List<string> batteryOnlyModules)
    {
        if(batteryOnlyModules.Count == 0)
        {
            return;
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"battery-only modules (context-decided, no whole automated comparand; the certified battery is the sole oracle) = {batteryOnlyModules.Count}:");
        foreach(string name in batteryOnlyModules)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {name}");
        }
    }

    /// <summary>Renders the module's five wall-time cells in column order, joined for a report row.</summary>
    /// <param name="measured">The per-column timed decisions.</param>
    /// <param name="contextAdmitted">Whether the context-saturation survey admitted the module at the production default, so its abstained context cell renders ADMITTED-DELEGATED rather than NOT-ADMITTED.</param>
    /// <returns>The pipe-joined millisecond cells.</returns>
    private static string ModuleMillisecondCells(TimedDecision[] measured, bool contextAdmitted)
    {
        string[] cells = new string[EngineColumns.Count];
        for(int columnIndex = 0; columnIndex < EngineColumns.Count; columnIndex++)
        {
            cells[columnIndex] = FormatCellMilliseconds(measured[columnIndex], EngineColumns[columnIndex].IsContextColumn, contextAdmitted);
        }

        return string.Join(" | ", cells);
    }

    /// <summary>Renders the module's five allocation cells in column order, joined for a report row.</summary>
    /// <param name="measured">The per-column timed decisions.</param>
    /// <param name="contextAdmitted">Whether the context-saturation survey admitted the module at the production default, so its abstained context cell renders ADMITTED-DELEGATED rather than NOT-ADMITTED.</param>
    /// <returns>The pipe-joined kilobyte cells.</returns>
    private static string ModuleKilobyteCells(TimedDecision[] measured, bool contextAdmitted)
    {
        string[] cells = new string[EngineColumns.Count];
        for(int columnIndex = 0; columnIndex < EngineColumns.Count; columnIndex++)
        {
            cells[columnIndex] = FormatCellKilobytes(measured[columnIndex], EngineColumns[columnIndex].IsContextColumn, contextAdmitted);
        }

        return string.Join(" | ", cells);
    }

    /// <summary>One document's timed and measured EL classification: the classification, elapsed milliseconds, and thread-local allocated bytes, or a timeout marker when the budget was exceeded.</summary>
    /// <param name="Classification">The reached classification within budget; <see langword="null"/> on timeout.</param>
    /// <param name="Milliseconds">The elapsed milliseconds within budget; <see langword="null"/> on timeout.</param>
    /// <param name="AllocatedBytes">The thread-local bytes the classification allocated within budget; <see langword="null"/> on timeout.</param>
    /// <param name="TimedOut">Whether the classification exceeded the budget.</param>
    private readonly record struct ClassificationMeasurement(ElClassification? Classification, double? Milliseconds, long? AllocatedBytes, bool TimedOut);

    /// <summary>
    /// Appends the classification lane for a suite whose cases retained their
    /// source document — the corpus suites. It times and measures the EL
    /// consequence-based whole-ontology classification per file, the only engine
    /// that classifies today; the module engines' pairwise subsumption sweep is
    /// capped and is not classification, so it is not reported here. Appends
    /// nothing for a suite whose cases carry no document, such as the synthetic
    /// suite built from axioms directly.
    /// </summary>
    /// <param name="report">The report the lane appends to.</param>
    /// <param name="cases">The suite's entries.</param>
    private static void AppendClassificationLane(StringBuilder report, IReadOnlyList<MeasuredCase> cases)
    {
        List<(string Name, OwlOntologyDocument Document)> documented = [];
        foreach(MeasuredCase measuredCase in cases)
        {
            if(measuredCase.Document is OwlOntologyDocument document)
            {
                documented.Add((measuredCase.Name, document));
            }
        }

        if(documented.Count == 0)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("classification lane (EL consequence-based whole-ontology classification only; the module engines' capped subsumption sweep is not classification):");
        report.AppendLine("file | classify (ms) | classify (MB) | named classes | coherent | undecided constructs");
        report.AppendLine("---|---:|---:|---:|:---:|---:");

        List<double> classifyMilliseconds = [];
        List<double> classifyMegabytes = [];
        foreach((string name, OwlOntologyDocument document) in documented)
        {
            ClassificationMeasurement measurement = MeasureClassification(document);
            if(measurement.Classification is not ElClassification classification)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"{name} | TIMEOUT>{DecisionBudget.TotalSeconds:F0}s | - | - | - | -");

                continue;
            }

            if(measurement.Milliseconds is double milliseconds)
            {
                classifyMilliseconds.Add(milliseconds);
            }

            double? megabytes = measurement.AllocatedBytes is long bytes ? bytes / 1048576.0 : null;
            if(megabytes is double megabyteValue)
            {
                classifyMegabytes.Add(megabyteValue);
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"{name} | {FormatMilliseconds(measurement.Milliseconds)} | {FormatOptionalMegabytes(megabytes)} | {classification.Classes.Count} | {(classification.IsCoherent ? "yes" : "NO")} | {classification.UnsupportedConstructs.Count}");
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"classification median: ms={FormatMilliseconds(Median(classifyMilliseconds))} MB={FormatOptionalMegabytes(Median(classifyMegabytes))}");
    }

    /// <summary>Times and measures one document's EL classification under a fresh budget, recording a timeout rather than wedging the run. The classifier returns synchronously, so the thread-local allocation delta is exact without a completion pin.</summary>
    /// <param name="document">The document to classify.</param>
    /// <returns>The timed and measured classification.</returns>
    private static ClassificationMeasurement MeasureClassification(OwlOntologyDocument document)
    {
        using CancellationTokenSource budget = new(DecisionBudget);
        Stopwatch stopwatch = new();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();
        try
        {
            ElClassification classification = ElClassifier.Classify(document, budget.Token);
            stopwatch.Stop();
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            return new ClassificationMeasurement(classification, stopwatch.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore, TimedOut: false);
        }
        catch(OperationCanceledException)
        {
            stopwatch.Stop();

            return new ClassificationMeasurement(Classification: null, Milliseconds: null, AllocatedBytes: null, TimedOut: true);
        }
    }

    /// <summary>
    /// Measures every corpus root: the vendored roots under
    /// <c>Material/Benchmark/</c> and, when the machine-local corpus-root
    /// environment variable names an existing directory, its subdirectories too.
    /// A configured but missing machine-local root is noted in the report rather
    /// than failing the run — the cache is optional and machine-specific.
    /// </summary>
    /// <param name="report">The report each corpus suite appends to.</param>
    /// <param name="suites">The suite-results list each corpus suite is added to.</param>
    /// <returns>The number of corpus roots measured across both locations.</returns>
    private static async Task<int> MeasureCorporaAsync(StringBuilder report, List<SuiteResult> suites)
    {
        int roots = 0;
        roots += await MeasureCorpusRootsAsync(report, suites, W3cCorpusPath.LibraryDirectory(BenchmarkLibraryFolder)).ConfigureAwait(false);

        string? cacheRoot = Environment.GetEnvironmentVariable(CorpusRootVariable);
        if(!string.IsNullOrWhiteSpace(cacheRoot))
        {
            if(Directory.Exists(cacheRoot))
            {
                roots += await MeasureCorpusRootsAsync(report, suites, cacheRoot).ConfigureAwait(false);
            }
            else
            {
                report.AppendLine();
                report.AppendLine(CultureInfo.InvariantCulture, $"The configured machine-local corpus root does not exist: {cacheRoot}.");
            }
        }

        return roots;
    }

    /// <summary>Probes each corpus subdirectory under one root that exists, loads every module file it holds, and measures the subdirectory as one suite labelled by its directory name.</summary>
    /// <param name="report">The report each corpus suite appends to.</param>
    /// <param name="suites">The suite-results list each corpus suite is added to.</param>
    /// <param name="root">The directory whose subdirectories are corpus roots.</param>
    /// <returns>The number of corpus subdirectories measured under the root — zero when the root does not exist.</returns>
    private static async Task<int> MeasureCorpusRootsAsync(StringBuilder report, List<SuiteResult> suites, string root)
    {
        if(!Directory.Exists(root))
        {
            return 0;
        }

        string[] corpusRoots = Directory.GetDirectories(root);
        Array.Sort(corpusRoots, StringComparer.Ordinal);
        foreach(string corpusRoot in corpusRoots)
        {
            string name = Path.GetFileName(corpusRoot);
            List<MeasuredCase> corpusCases = await LoadCorpusModulesAsync(corpusRoot).ConfigureAwait(false);
            suites.Add(await MeasureSuiteAsync($"corpus:{name}", corpusCases, report).ConfigureAwait(false));
        }

        return corpusRoots.Length;
    }

    /// <summary>Loads every module file under a corpus root, mapping each through its reader and recording a parse-skip entry for any that fails.</summary>
    /// <param name="corpusRoot">The corpus root directory.</param>
    /// <returns>The corpus's suite entries, ordinal-ordered by relative path.</returns>
    private static async Task<List<MeasuredCase>> LoadCorpusModulesAsync(string corpusRoot)
    {
        //Enumerate once and filter by exact extension: a 3-character search
        //pattern such as *.rdf also matches longer extensions sharing the
        //prefix (.rdfs) on Windows yet misses differently-cased names on
        //case-sensitive file systems, so pattern matching cannot be the filter.
        List<string> files = [];
        foreach(string file in Directory.GetFiles(corpusRoot, "*", SearchOption.AllDirectories))
        {
            string fileExtension = Path.GetExtension(file);
            foreach(string corpusExtension in CorpusFileExtensions)
            {
                if(string.Equals(fileExtension, corpusExtension, StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(file);

                    break;
                }
            }
        }

        files.Sort(StringComparer.Ordinal);
        List<MeasuredCase> cases = new(files.Count);
        foreach(string file in files)
        {
            string name = Path.GetRelativePath(corpusRoot, file).Replace('\\', '/');
            OwlOntologyDocument? document = await LoadCorpusDocumentAsync(file).ConfigureAwait(false);
            cases.Add(document is OwlOntologyDocument parsed && TryModuleFromDocument(parsed, out ReasoningModule module)
                ? new MeasuredCase(name, Expected: null, module, ParseFailed: false, parsed)
                : new MeasuredCase(name, Expected: null, Module: null, ParseFailed: true));
        }

        return cases;
    }

    /// <summary>
    /// Loads and reads one corpus file to an ontology document the way the
    /// matching front-end does, reporting failure instead of throwing so an
    /// unparseable file is skipped and counted rather than failing the run. The
    /// document is retained so the classification lane can classify it; the
    /// caller derives the reasoning module from it.
    /// </summary>
    /// <param name="path">The corpus file's path.</param>
    /// <returns>The parsed document when the file read cleanly; <see langword="null"/> otherwise.</returns>
    private static async Task<OwlOntologyDocument?> LoadCorpusDocumentAsync(string path)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            string extension = Path.GetExtension(path);
            if(string.Equals(extension, ".ofn", StringComparison.OrdinalIgnoreCase))
            {
                return OwlFunctionalSyntaxReader.Read(bytes);
            }

            string baseIri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            DiagnosticBag diagnostics = new();
            IReadOnlyList<Quad> quads = string.Equals(extension, ".ttl", StringComparison.OrdinalIgnoreCase)
                ? await DrainTurtleAsync(bytes, diagnostics, baseIri).ConfigureAwait(false)
                : RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri));
            if(diagnostics.HasErrors)
            {
                return null;
            }

            return OwlRdfMapper.Map(quads);
        }
        catch(Exception exception) when(exception is FormatException or InvalidOperationException or ArgumentException or IOException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Builds a module from a mapped document, reporting failure when the document carries mapping errors.</summary>
    /// <param name="document">The mapped ontology document.</param>
    /// <param name="module">The module when the document mapped cleanly; the empty module otherwise.</param>
    /// <returns><see langword="true"/> when the document mapped without errors.</returns>
    private static bool TryModuleFromDocument(OwlOntologyDocument document, out ReasoningModule module)
    {
        if(document.Diagnostics.HasErrors)
        {
            module = EmptyModule;

            return false;
        }

        module = new ReasoningModule([.. document.Axioms], Violations: []);

        return true;
    }

    /// <summary>Drains the never-throwing Turtle reader's async quad iterator over an in-memory source into a quad list.</summary>
    /// <param name="source">The UTF-8 Turtle source bytes.</param>
    /// <param name="diagnostics">The bag lexical and parse diagnostics accumulate into.</param>
    /// <param name="baseIri">The document base IRI for resolving relative references.</param>
    /// <returns>The parsed quads.</returns>
    private static async Task<List<Quad>> DrainTurtleAsync(ReadOnlyMemory<byte> source, DiagnosticBag diagnostics, string baseIri)
    {
        List<Quad> quads = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(source, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseIri).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }

    /// <summary>
    /// Appends the suite's remainder census: every distinct unsupported-construct
    /// reason the KPI arm's verdicts carried, with its occurrence count across
    /// the suite's verdicts (a verdict names a construct once per axiom) — the
    /// actionable "what the deciding calculus could not cover" list a capability
    /// rung is scoped against. Appends nothing when every verdict covered its
    /// module whole.
    /// </summary>
    /// <param name="report">The report the census appends to.</param>
    /// <param name="remainderCensus">The distinct reasons with their occurrence counts.</param>
    private static void AppendRemainderCensus(StringBuilder report, Dictionary<string, int> remainderCensus)
    {
        if(remainderCensus.Count == 0)
        {
            return;
        }

        List<KeyValuePair<string, int>> entries = [.. remainderCensus];
        entries.Sort(static (left, right) =>
        {
            int byCount = right.Value.CompareTo(left.Value);

            return byCount != 0 ? byCount : string.CompareOrdinal(left.Key, right.Key);
        });

        report.AppendLine("remainder constructs (KPI arm verdicts, occurrences across the suite):");
        foreach(KeyValuePair<string, int> entry in entries)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {entry.Value} x {entry.Key}");
        }
    }

    /// <summary>
    /// Appends the suite's full-construct census: a per-module table of the
    /// polarity-qualified semantic constructs
    /// each module uses — walked over the raw axioms by
    /// <see cref="OwlConstructCensus"/>, independent of any engine verdict — and
    /// the per-suite aggregate, each rendered <c>{count} x {key}</c> sorted by
    /// descending count then ascending ordinal key, matching the remainder
    /// section. Appends nothing when no module was measured.
    /// </summary>
    /// <param name="report">The report the census appends to.</param>
    /// <param name="constructCensuses">The per-module censuses, in measurement order.</param>
    /// <param name="constructAggregate">The per-suite aggregated key counts.</param>
    private static void AppendConstructCensus(StringBuilder report, List<(string Name, IReadOnlyList<(string Key, int Count)> Census)> constructCensuses, Dictionary<string, int> constructAggregate)
    {
        if(constructCensuses.Count == 0)
        {
            return;
        }

        report.AppendLine("full-construct census (raw axioms, polarity-qualified; per module):");
        foreach((string name, IReadOnlyList<(string Key, int Count)> census) in constructCensuses)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  module {name}:");
            foreach((string key, int count) in census)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {count} x {key}");
            }
        }

        List<KeyValuePair<string, int>> aggregate = [.. constructAggregate];
        aggregate.Sort(static (left, right) =>
        {
            int byCount = right.Value.CompareTo(left.Value);

            return byCount != 0 ? byCount : string.CompareOrdinal(left.Key, right.Key);
        });

        report.AppendLine("full-construct census (suite aggregate):");
        foreach(KeyValuePair<string, int> entry in aggregate)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {entry.Value} x {entry.Key}");
        }
    }

    /// <summary>The census keys of the axiom layer with a fixed spelling; the parameterized axiom-layer keys (<c>ObjectPropertyCharacteristic(...)</c>, <c>DifferentIndividuals(n)</c>) are matched by prefix instead.</summary>
    private static HashSet<string> AxiomLayerCensusKeys { get; } = new(StringComparer.Ordinal)
    {
        "SubClassOf",
        "EquivalentClasses",
        "DisjointClasses",
        "DisjointUnion",
        "SubObjectPropertyOf",
        "SubObjectPropertyOf(chain)",
        "EquivalentObjectProperties",
        "DisjointObjectProperties",
        "InverseObjectProperties",
        "ObjectPropertyDomain",
        "ObjectPropertyRange",
        "SubDataPropertyOf",
        "EquivalentDataProperties",
        "DisjointDataProperties",
        "DataPropertyDomain",
        "DataPropertyRange",
        "FunctionalDataProperty",
        "DatatypeDefinition",
        "HasKey",
        "ClassAssertion",
        "ObjectPropertyAssertion",
        "NegativeObjectPropertyAssertion",
        "DataPropertyAssertion",
        "NegativeDataPropertyAssertion",
        "SameIndividual",
    };

    /// <summary>Whether a census carries at least one axiom-layer key — the smoke pin's aliveness signal that the walker still names whole axioms, not just nested expressions.</summary>
    /// <param name="census">The census entries.</param>
    /// <returns><see langword="true"/> when an axiom-layer key is present.</returns>
    private static bool ContainsAxiomLayerKey(IReadOnlyList<(string Key, int Count)> census)
    {
        foreach((string key, int _) in census)
        {
            if(AxiomLayerCensusKeys.Contains(key)
                || key.StartsWith("ObjectPropertyCharacteristic(", StringComparison.Ordinal)
                || key.StartsWith("DifferentIndividuals(", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Appends the across-suites totals line, including both composition columns' aggregate delegation rates and the battery-only total.</summary>
    /// <param name="report">The report the totals append to.</param>
    /// <param name="suites">The measured suites.</param>
    private static void AppendTotals(StringBuilder report, List<SuiteResult> suites)
    {
        int modules = 0;
        int decided = 0;
        int delegated = 0;
        int elSatDecided = 0;
        int elSatDelegated = 0;
        int batteryOnly = 0;
        int fragmentRelative = 0;
        int timeouts = 0;
        int parseSkips = 0;
        int contextDecided = 0;
        int contextAdmittedDelegated = 0;
        int contextNotAdmitted = 0;
        long backstopHeads = 0;
        int backstopModules = 0;
        foreach(SuiteResult suite in suites)
        {
            modules += suite.ModuleCount;
            decided += suite.DecidedCount;
            delegated += suite.DelegatedCount;
            elSatDecided += suite.ElSatDecidedCount;
            elSatDelegated += suite.ElSatDelegatedCount;
            batteryOnly += suite.BatteryOnlyCount;
            fragmentRelative += suite.FragmentRelativeCount;
            timeouts += suite.TimeoutCount;
            parseSkips += suite.ParseSkipCount;
            contextDecided += suite.ContextDecidedCount;
            contextAdmittedDelegated += suite.ContextAdmittedDelegatedCount;
            contextNotAdmitted += suite.ContextNotAdmittedCount;
            backstopHeads += suite.ContextBackstopLatchHeads;
            backstopModules += suite.ContextBackstopLatchModules;
        }

        int classified = decided + delegated;
        double rate = classified == 0 ? 0.0 : 100.0 * delegated / classified;
        int elSatClassified = elSatDecided + elSatDelegated;
        double elSatRate = elSatClassified == 0 ? 0.0 : 100.0 * elSatDelegated / elSatClassified;

        report.AppendLine();
        report.AppendLine("== totals across suites ==");
        report.AppendLine(CultureInfo.InvariantCulture, $"modules = {modules}; DECIDED = {decided}; DELEGATED = {delegated}; fragment-relative = {fragmentRelative}; timeouts = {timeouts}; parse-skips = {parseSkips}");
        report.AppendLine(CultureInfo.InvariantCulture, $"delegation_rate (ElCtxSat, KPI arm) = {rate.ToString("F1", CultureInfo.InvariantCulture)}% ({delegated} delegated / {classified} total)");
        report.AppendLine(CultureInfo.InvariantCulture, $"delegation_rate (ElSat, prior chain) = {elSatRate.ToString("F1", CultureInfo.InvariantCulture)}% ({elSatDelegated} delegated / {elSatClassified} total)");
        report.AppendLine(CultureInfo.InvariantCulture, $"context engine (orthogonal to the KPI split): admitted+decided = {contextDecided}; admitted-delegated = {contextAdmittedDelegated}; not-admitted = {contextNotAdmitted}");
        report.AppendLine(CultureInfo.InvariantCulture, $"backstop latch (RootEqualityOutsideFold off-fold-equality heads): {backstopHeads.ToString(CultureInfo.InvariantCulture)} heads across {backstopModules} modules");
        report.AppendLine(CultureInfo.InvariantCulture, $"battery-only modules (context-decided, no whole automated comparand) = {batteryOnly}");
    }

    /// <summary>The median of the values, or <see langword="null"/> when there are none; the mean of the two middle values for an even count.</summary>
    /// <param name="values">The values, sorted in place.</param>
    /// <returns>The median, or <see langword="null"/> for an empty input.</returns>
    private static double? Median(List<double> values)
    {
        if(values.Count == 0)
        {
            return null;
        }

        values.Sort();
        int middle = values.Count / 2;
        double median = values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;

        return median;
    }

    /// <summary>Formats a known-mix tag, or a dash for a corpus module carrying none.</summary>
    /// <param name="expected">The tag, or <see langword="null"/>.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatExpected(ExpectedKpiPath? expected)
    {
        return expected switch
        {
            ExpectedKpiPath.Decided => "DECIDED",
            ExpectedKpiPath.Delegated => "DELEGATED",
            _ => "-",
        };
    }

    /// <summary>Formats one engine column's wall-time cell: the timeout marker when the budget was exceeded, then for the context column ADMITTED-DELEGATED when the survey admitted the module yet the reasoner delegated the whole verdict and NOT-ADMITTED when the survey rejected it (cand-C, the two the single NOT-ADMITTED cell had conflated), otherwise the elapsed milliseconds.</summary>
    /// <param name="cell">The column's timed decision.</param>
    /// <param name="contextColumn">Whether this is the context-saturation column, whose fallback abstention renders a survey-split marker rather than a meaningless near-zero.</param>
    /// <param name="contextAdmitted">Whether the context-saturation survey admitted the module at the production default; read only for the context column's abstention.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatCellMilliseconds(TimedDecision cell, bool contextColumn, bool contextAdmitted)
    {
        if(cell.TimedOut)
        {
            return $"TIMEOUT>{DecisionBudget.TotalSeconds:F0}s";
        }

        if(contextColumn && cell.Decision is ModuleDecision decision && decision.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
        {
            return contextAdmitted ? "ADMITTED-DELEGATED" : "NOT-ADMITTED";
        }

        return cell.Milliseconds is double milliseconds
            ? milliseconds.ToString("F2", CultureInfo.InvariantCulture)
            : "-";
    }

    /// <summary>Formats one engine column's allocation cell in kilobytes: a dash on timeout, where the partial work is not comparable, then for the context column ADMITTED-DELEGATED when the survey admitted the module yet the reasoner delegated the whole verdict and NOT-ADMITTED when the survey rejected it, otherwise the thread-local kilobytes.</summary>
    /// <param name="cell">The column's timed decision.</param>
    /// <param name="contextColumn">Whether this is the context-saturation column.</param>
    /// <param name="contextAdmitted">Whether the context-saturation survey admitted the module at the production default; read only for the context column's abstention.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatCellKilobytes(TimedDecision cell, bool contextColumn, bool contextAdmitted)
    {
        if(cell.TimedOut)
        {
            return "-";
        }

        if(contextColumn && cell.Decision is ModuleDecision decision && decision.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
        {
            return contextAdmitted ? "ADMITTED-DELEGATED" : "NOT-ADMITTED";
        }

        return cell.AllocatedBytes is long bytes
            ? FormatKilobytes(bytes / 1024.0)
            : "-";
    }

    /// <summary>
    /// Formats a kilobytes value to at least three significant figures in fixed
    /// point, never scientific: the cheapest EL decisions allocate sub-kilobyte,
    /// which two fixed decimals would floor toward zero and erase the allocation
    /// trend the report exists to carry.
    /// </summary>
    /// <param name="kilobytes">The kilobytes value.</param>
    /// <returns>The formatted value.</returns>
    private static string FormatKilobytes(double kilobytes)
    {
        if(kilobytes <= 0)
        {
            return "0";
        }

        int magnitude = (int)Math.Floor(Math.Log10(kilobytes));
        int decimals = Math.Max(0, 2 - magnitude);

        return kilobytes.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a median milliseconds cell, or a dash when no module was timed.</summary>
    /// <param name="milliseconds">The median milliseconds, or <see langword="null"/>.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatMilliseconds(double? milliseconds)
    {
        return milliseconds is double value
            ? value.ToString("F2", CultureInfo.InvariantCulture)
            : "-";
    }

    /// <summary>Formats a median kilobytes cell to at least three significant figures, or a dash when no module was measured.</summary>
    /// <param name="kilobytes">The median kilobytes, or <see langword="null"/>.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatOptionalKilobytes(double? kilobytes)
    {
        return kilobytes is double value
            ? FormatKilobytes(value)
            : "-";
    }

    /// <summary>Formats a megabytes cell for the classification lane, whose corpus-scale allocation is reported in megabytes; a dash when absent.</summary>
    /// <param name="megabytes">The megabytes, or <see langword="null"/>.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatOptionalMegabytes(double? megabytes)
    {
        return megabytes is double value
            ? value.ToString("F2", CultureInfo.InvariantCulture)
            : "-";
    }

    /// <summary>Formats a verdict's consistency cell, or a dash when the decision reached no verdict.</summary>
    /// <param name="verdict">The verdict, or <see langword="null"/>.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatConsistency(ModuleVerdict? verdict)
    {
        return verdict is ModuleVerdict reached
            ? (reached.IsConsistent ? "consistent" : "INCONSISTENT")
            : "-";
    }

    /// <summary>The synthetic ladder's labelled modules, projected for sibling differentials (the ELH-degeneracy differential sweeps them as a fixture population); parse-skip entries carry no module and are omitted.</summary>
    /// <returns>The name and module of every synthetic-suite entry.</returns>
    internal static IReadOnlyList<(string Name, ReasoningModule Module)> SyntheticSuiteModules()
    {
        List<(string Name, ReasoningModule Module)> modules = [];
        foreach(MeasuredCase entry in BuildSyntheticSuite())
        {
            if(entry.Module is ReasoningModule module)
            {
                modules.Add((entry.Name, module));
            }
        }

        return modules;
    }

    /// <summary>
    /// Builds the inline synthetic ladder: six EL-decidable shapes the EL
    /// fast-path decides, and thirteen beyond-EL shapes — eleven the
    /// context-saturation tier admits and decides (including the
    /// disjunctive faces: the positive union, the max-cardinality merge, and the
    /// disjoint-union covering; the HasKey ground-merge face; the
    /// nominal-enumeration face; the HasKey-beside-nominal face the root key join
    /// decides past the lifted key-on-nominal guard; and the constant-instantiated
    /// root data demand the per-constant root arm decides per ≈-class — the
    /// <see cref="ContextSaturationEngine.RootDataDemandObserved"/> census
    /// statistic records the landing the arm bypasses), one whose key-class
    /// membership rides a carried disjunct so the key latch delegates it, and one
    /// that falls through both fast-path tiers to the SAT oracle — each tagged
    /// with the side of the KPI arm's decide/delegate boundary it is known to
    /// fall on so the measured bucket self-checks the aggregation.
    /// Every consistent shape is consistent under every column and every
    /// inconsistent shape inconsistent under every column, so the differential
    /// oracle holds across the whole ladder.
    /// </summary>
    /// <returns>The synthetic suite's entries.</returns>
    private static IReadOnlyList<MeasuredCase> BuildSyntheticSuite()
    {
        return
        [
            //A conjunctive existential taxonomy is pure EL: told subsumptions and
            //a superclass existential over a named filler, decided by saturation.
            Decides("conjunctive-existential-taxonomy", Module(
                SubClassOf(Class("Car"), Class("Vehicle")),
                SubClassOf(Class("Vehicle"), Class("Artifact")),
                SubClassOf(Class("Car"), Some("hasPart", Class("Wheel"))),
                ClassAssertion(Class("Car"), Individual("c")))),

            //A transitive role composing a range-typed existential chain stays in
            //EL⊥; consistent, so the range-and-transitive-blind comparand agrees.
            Decides("range-transitive-chain", Module(
                Transitive("partOf"),
                Range("partOf", Class("Component")),
                SubClassOf(Class("Bolt"), Some("partOf", Class("Assembly"))),
                SubClassOf(Class("Assembly"), Some("partOf", Class("Machine"))),
                ClassAssertion(Class("Bolt"), Individual("b")))),

            //A symmetric role over an asserted ground edge is decided by the
            //ground-graph tier; consistent, so the symmetry-blind comparand agrees.
            Decides("symmetric-asserted-graph", Module(
                Symmetric("connectedTo"),
                Edge("a", "connectedTo", "b"),
                ClassAssertion(Class("Node"), Individual("a")),
                ClassAssertion(Class("Node"), Individual("b")))),

            //A superclass-side data existential over a satisfiable datatype is a
            //value demand the EL fast-path decides consistent.
            Decides("datatype-existential", Module(
                SubClassOf(Class("Person"), DataSome("age", IntegerRange)),
                ClassAssertion(Class("Person"), Individual("p")))),

            //An existential into the empty class propagates ⊥ back over the edge
            //to condemn the owner — a core EL⊥ inconsistency the comparand shares.
            Decides("existential-into-bottom", Module(
                SubClassOf(Class("C"), Some("r", Class("D"))),
                SubClassOf(Class("D"), NothingReference),
                ClassAssertion(Class("C"), Individual("x")))),

            //Two disjoint types on one individual clash — a core inconsistency
            //both the EL fast-path and the comparand find.
            Decides("disjoint-abox-clash", Module(
                Disjoint(Class("A"), Class("B")),
                ClassAssertion(Class("A"), Individual("x")),
                ClassAssertion(Class("B"), Individual("x")))),

            //A disjunction on the superclass side is a positive union the
            //disjunctive context survey admits, so the context tier decides it
            //whole by ordered resolution over the DL1 multi-literal head.
            Decides("disjunction-superclass", Module(
                SubClassOf(Class("A"), Union(Class("B"), Class("C"))),
                ClassAssertion(Class("A"), Individual("x")))),

            //A universal restriction leaves EL, but the context-saturation tier
            //admits a positive universal and decides it, so the KPI arm decides it
            //whole.
            Decides("universal-restriction", Module(
                SubClassOf(Class("A"), All("r", Class("B"))),
                ClassAssertion(Class("A"), Individual("x")))),

            //A qualified min-cardinality above one is beyond EL and ALC(H); the
            //context tier admits a positive min-cardinality of any bound and
            //decides it.
            Decides("min-cardinality-two", Module(
                SubClassOf(Class("A"), MinCardinality(2, "r", Class("B"))),
                ClassAssertion(Class("A"), Individual("x")))),

            //A max-cardinality bound above one with three forced successors under
            //partial distinctness is the counting face: the DL4 merge runs
            //(the B3-compatible pair merges) and the context tier decides the
            //module whole, consistent under every column.
            Decides("max-cardinality-merge", Module(
                SubClassOf(Class("A"), MaxCardinality(2, "r", null)),
                SubClassOf(Class("A"), Some("r", Class("B1"))),
                SubClassOf(Class("A"), Some("r", Class("B2"))),
                SubClassOf(Class("A"), Some("r", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                ClassAssertion(Class("A"), Individual("x")))),

            //A disjoint-union covering is the whole-axiom face: the
            //covering half lands as a positive union, the members and pairwise
            //disjointness as Horn clauses, and the context tier decides the
            //module whole.
            Decides("disjoint-union-covering", Module(
                DisjointUnion("A", Class("B"), Class("C")),
                ClassAssertion(Class("B"), Individual("x")))),

            //A functional role with an existential is a cardinality constraint
            //beyond EL; the context tier admits the functional characteristic and
            //its cardinality-one counting and decides it.
            Decides("functional-role-existential", Module(
                Functional("r"),
                SubClassOf(Class("X"), Some("r", Class("B"))),
                ClassAssertion(Class("X"), Individual("x")))),

            //A functional super-role of a property chain makes the role non-simple;
            //the context survey admits both characteristics, but the clausifier's
            //simple-role second gate delegates the module to the SAT oracle.
            Delegates("functional-chain-conclusion", Module(
                Functional("t"),
                Chain("t", "r", "s"),
                ClassAssertion(Class("A"), Individual("x")))),

            //An inverse-property axiom spelled over an inverse-role expression is
            //admitted by the context survey in any spelling and decided by the
            //context tier.
            Decides("inverse-axiom-over-inverse-role", Module(
                new OwlInverseObjectPropertiesAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Property("s")) { Origin = Origin("inverse") },
                ClassAssertion(Class("A"), Individual("x")))),

            //A global key over a data property with two shared-value holders is
            //the HasKey ground rung's merge face: the survey admits the key and
            //the assertions, the round-0 told join merges the pair, and the
            //context tier decides the module whole and consistent.
            Decides("haskey-ground-merge-decides", Module(
                HasKey(Thing, [], ["id"]),
                DataAssertion("a", "id", "K-1"),
                DataAssertion("b", "id", "K-1"))),

            //A key-class membership riding a carried disjunct is uncertain per
            //branch, so the key latch marks the obligation and the context tier
            //delegates the module to the SAT oracle rather than risk a wrong
            //verdict; consistent under every column.
            Delegates("haskey-under-disjunct-delegates", Module(
                HasKey(Class("K"), [], ["id"]),
                ClassAssertion(Union(Class("K"), Class("L")), Individual("x")),
                DataAssertion("x", "id", "K-1"))),

            //A multi-member positive enumeration with an asserted member is the
            //nominal face: the survey admits the enumeration, the asserted
            //individual collapses onto one of the two named constants without a
            //unique-name assumption, and the context tier decides the module whole
            //and consistent.
            Decides("nominal-enumeration-decides", Module(
                SubClassOf(Class("NominalHost"), OneOf("nh1", "nh2")),
                ClassAssertion(Class("NominalHost"), Individual("nh3")))),

            //A HasKey axiom co-occurring with a nominal enumeration is the
            //key-on-nominal co-occurrence face: the root key join routes the
            //module past the lifted key-on-nominal guard into intake and decides
            //its keyed candidates on the root tier — a single keyed candidate
            //joins no pair, so the module decides CONSISTENT.
            Decides("nominal-key-cooccurrence-decides", Module(
                HasKey(Class("Keyed"), [], ["id"]),
                SubClassOf(Class("Keyed"), OneOf("ky1")),
                DataAssertion("ky1", "id", "K-1"))),

            //A data demand instantiated at a named individual is the root
            //data-demand face (mirroring the Grd-1 pin): the survey admits the nominal, the
            //constant-instantiated demand lands on the root context, and the
            //per-constant root arm decides its ≈-class off the pooled read-time
            //union — a lone satisfiable integer existential realizes, so the
            //module decides CONSISTENT. The stand measures this decide.
            Decides("root-data-demand-decides", Module(
                SubClassOf(OneOf("o"), DataSome("dp", IntegerRange)))),
        ];
    }

    /// <summary>A synthetic entry tagged to be decided by the KPI arm's EL or context fast-path.</summary>
    /// <param name="name">The module's label.</param>
    /// <param name="module">The module.</param>
    /// <returns>The entry.</returns>
    private static MeasuredCase Decides(string name, ReasoningModule module)
    {
        return new MeasuredCase(name, ExpectedKpiPath.Decided, module, ParseFailed: false);
    }

    /// <summary>A synthetic entry tagged to fall through both fast-path tiers and be delegated to the SAT oracle.</summary>
    /// <param name="name">The module's label.</param>
    /// <param name="module">The module.</param>
    /// <returns>The entry.</returns>
    private static MeasuredCase Delegates(string name, ReasoningModule module)
    {
        return new MeasuredCase(name, ExpectedKpiPath.Delegated, module, ParseFailed: false);
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A distinct origin quad for the marker name, so each axiom carries an origin the reporting path can name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A union of class expressions (<c>ObjectUnionOf</c>).</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf(operands);
    }

    /// <summary>An enumeration of named individuals (<c>ObjectOneOf</c>) in the example namespace.</summary>
    /// <param name="locals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf OneOf(params string[] locals)
    {
        RdfTerm[] terms = new RdfTerm[locals.Length];
        for(int index = 0; index < locals.Length; index++)
        {
            terms[index] = Individual(locals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>A qualified minimum-cardinality restriction over a forward role.</summary>
    /// <param name="cardinality">The non-negative lower bound.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MinCardinality(int cardinality, string property, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a forward role.</summary>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxCardinality(int cardinality, string property, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A disjoint-union axiom defining a class as the disjoint union of its operands.</summary>
    /// <param name="definedClass">The defined class's local name.</param>
    /// <param name="operands">The member expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointUnionAxiom DisjointUnion(string definedClass, params OwlClassExpression[] operands)
    {
        return new OwlDisjointUnionAxiom(new NamedNode(Utf8Strings.From(Example + definedClass)), operands) { Origin = Origin("disjointunion") };
    }

    /// <summary>A single-property data existential over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Utf8Strings.From(Example + property))], range);
    }

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An asserted role edge between two individuals.</summary>
    /// <param name="from">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="to">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string from, string role, string to)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(from), Individual(role), Individual(to)) { Origin = Origin($"edge-{from}-{to}") };
    }

    /// <summary>A pairwise disjointness axiom.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = Origin("disjoint") };
    }

    /// <summary>A transitive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = Origin("transitive") };
    }

    /// <summary>A symmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(property)) { Origin = Origin("symmetric") };
    }

    /// <summary>A functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(property)) { Origin = Origin("functional") };
    }

    /// <summary>A range axiom typing every target of the role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="range">The range class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(property), range) { Origin = Origin("range") };
    }

    /// <summary>A property-chain sub-role inclusion whose links compose into the super-role.</summary>
    /// <param name="superProperty">The super-property's local name.</param>
    /// <param name="links">The chain links' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlPropertyChainAxiom Chain(string superProperty, params string[] links)
    {
        OwlObjectPropertyExpression[] chain = new OwlObjectPropertyExpression[links.Length];
        for(int index = 0; index < links.Length; index++)
        {
            chain[index] = Property(links[index]);
        }

        return new OwlPropertyChainAxiom(chain, Property(superProperty)) { Origin = Origin("chain") };
    }

    /// <summary>The <c>owl:Thing</c> reference — the global key's class.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>A named data property node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A <c>HasKey</c> axiom over a keyed class, object key properties, and data key properties in the example namespace.</summary>
    /// <param name="keyedClass">The keyed class expression.</param>
    /// <param name="objectProperties">The object key properties' local names.</param>
    /// <param name="dataProperties">The data key properties' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlHasKeyAxiom HasKey(OwlClassExpression keyedClass, string[] objectProperties, string[] dataProperties)
    {
        List<OwlObjectPropertyExpression> objects = [];
        foreach(string local in objectProperties)
        {
            objects.Add(Property(local));
        }

        List<NamedNode> data = [];
        foreach(string local in dataProperties)
        {
            data.Add(DataProperty(local));
        }

        return new OwlHasKeyAxiom(keyedClass, objects, data) { Origin = Origin("haskey") };
    }

    /// <summary>A data-property assertion of an <c>xsd:string</c> literal over a named subject.</summary>
    /// <param name="subject">The subject individual's local name.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The string literal's lexical form.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom DataAssertion(string subject, string property, string value)
    {
        Literal literal = new(Utf8Strings.From(value), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));

        return new OwlDataPropertyAssertionAxiom(Individual(subject), DataProperty(property), literal) { Origin = Origin("data") };
    }

    /// <summary>The build configuration the harness runs under, for the report header.</summary>
    /// <returns><c>Release</c> or <c>Debug</c>.</returns>
    private static string BuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
