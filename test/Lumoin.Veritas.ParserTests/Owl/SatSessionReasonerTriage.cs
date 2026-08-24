using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The incremental-session triage harness for the SAT-backed reasoner: it
/// decides every deduped Direct-corpus premise twice — once through the
/// stateless per-solve engine and once through the reused incremental
/// <see cref="SatSolverSession"/> path — each under the same per-instance
/// wall-clock budget, and writes a session-vs-stateless comparison table
/// sorted worst-first by the session/stateless time ratio to the configured
/// output path. It is measurement scaffolding for the default-off
/// <c>useIncrementalSession</c> seam — the peer of
/// <see cref="W3cOwl2DirectTriage"/> — not a correctness gate: it runs only
/// when the <c>VERITAS_SAT_SESSION_TRIAGE</c> environment variable names an
/// output file, staying out of the normal suite's wall time. Its correctness
/// assertions stop at the consistency agreement over premises both paths
/// decided within budget and at the manifest-integrity precondition that the
/// pinned warm and probe instances load; everything else is measurement, not
/// gating. Both artifacts print a warm-evidence line — a whole-process JIT
/// decay signal — beside their tables. A second probe, gated the same way on
/// <c>VERITAS_SAT_SESSION_PROBE</c>, sweeps solve-capped decisions of fixed
/// pathological instances — a capped decision abstains gracefully at its cap
/// and still reports its statistics — so the per-cap trajectory separates
/// per-solve cost growth from solve-count blowup. Run either in Release in a
/// contiguous block with no concurrent builds or suites for citable numbers.
/// </summary>
[TestClass]
internal sealed class SatSessionReasonerTriage
{
    /// <summary>The environment variable naming the absolute output path; unset means the harness skips.</summary>
    private const string OutputPathVariable = "VERITAS_SAT_SESSION_TRIAGE";

    /// <summary>The per-path, per-instance decision budget; a decision exceeding it records as budget-exceeded rather than wedging the run.</summary>
    private static readonly TimeSpan DecisionBudget = TimeSpan.FromSeconds(10);

    /// <summary>The environment variable naming the solve-cap probe's absolute output path; unset means the probe skips.</summary>
    private const string ProbeOutputPathVariable = "VERITAS_SAT_SESSION_PROBE";

    /// <summary>The per-run wall-clock ceiling for the capped probe runs; a run exceeding it records as wall-cancelled rather than wedging the sweep.</summary>
    private static readonly TimeSpan ProbeWallBudget = TimeSpan.FromSeconds(120);

    /// <summary>The solve caps the probe sweeps, each an inclusive ceiling on the decision's world solves through <see cref="ReasoningBudget.MaxSolves"/>.</summary>
    private static int[] ProbeSolveCaps { get; } = [200, 400, 800, 1600, 3200, 6400];

    /// <summary>The fixed approved-suite instances the probe sweeps: the two session budget-exceeded cases and two large in-budget references.</summary>
    private static string[] ProbeInstanceIdentifiers { get; } =
    [
        "WebOnt-description-logic-208",
        "WebOnt-description-logic-209",
        "WebOnt-description-logic-201",
        "WebOnt-description-logic-040",
    ];

    /// <summary>
    /// The manifest identifier of the pinned heavy warm instance the ladder
    /// decides through both paths: its solver work is deterministic and heavy
    /// enough that one decision drives the hot solver methods far past the
    /// tier-1 promotion call count, and choosing it over its equal-work twin
    /// keeps the probe's first-measured reference a warm-transfer check
    /// rather than a self-warmed number.
    /// </summary>
    private const string WarmInstanceIdentifier = "WebOnt-description-logic-209";

    /// <summary>The bound on heavy warm iterations when the JIT-quiescence signal never fires; reaching it prints the NOT QUIESCED marker in the warm-evidence line.</summary>
    private const int HeavyWarmIterationBound = 8;

    /// <summary>
    /// The per-iteration process JIT-time delta at or below which the heavy
    /// warm counts as quiesced. The value sits between the measured post-ramp
    /// background-compile trickle (at most a couple hundred milliseconds of
    /// JIT across one steady iteration) and the ramp's multi-second
    /// first-iteration delta; it gates only how much warm-up runs, never any
    /// test outcome, and every iteration's delta prints in the evidence line.
    /// </summary>
    private static readonly TimeSpan HeavyWarmJitQuiescence = TimeSpan.FromMilliseconds(250);

    /// <summary>The number of leading processed triage rows re-measured after the sweep for the early-row steady check.</summary>
    private const int EarlySteadyCheckRowCount = 5;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Decides every deduped Direct-corpus premise under the stateless and
    /// the incremental-session SAT paths within the per-instance budget and
    /// writes the worst-first comparison table to the configured path,
    /// asserting the two paths agree on consistency where both finished.
    /// </summary>
    [TestMethod]
    public void MeasureIncrementalSessionAcrossDirectCorpus()
    {
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            //Opt-in measurement scaffolding, not a correctness gate: with no
            //output path configured the harness has nothing to do and the test
            //passes without measuring. Set VERITAS_SAT_SESSION_TRIAGE to run it.
            TestContext.WriteLine($"Skipping the triage harness: set {OutputPathVariable} to an absolute output path to run it.");

            return;
        }

        ImmutableArray<Owl2TestCase> cases = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        //Warm the shared code paths — the trivial module for class-load and
        //first-touch cost, then the pinned heavy instance to the JIT-quiescence
        //signal — so the timed decisions start from the tier-1 steady state
        //rather than absorbing the tiering ramp inside the first heavy rows.
        string warmEvidence = WarmDecisionPaths(cases);

        List<TriageRow> rows = [];
        List<(TriageRow Row, ReasoningModule Module)> earlyRows = [];
        HashSet<Utf8String> seenPremises = [];
        int premiseCases = 0;
        int dedupedCount = 0;
        int skippedCount = 0;
        int disagreementCount = 0;
        foreach(Owl2TestCase testCase in cases)
        {
            if(testCase.RdfXmlPremise is not { } premiseText)
            {
                continue;
            }

            premiseCases++;

            //First case name wins for identical premise text; a later case
            //carrying byte-identical premise adds no new solve profile.
            if(!seenPremises.Add(premiseText))
            {
                dedupedCount++;

                continue;
            }

            if(!TryLoadModule(testCase, out ReasoningModule module))
            {
                skippedCount++;

                continue;
            }

            PathOutcome stateless = TimePath(module, useIncrementalSession: false);
            PathOutcome session = TimePath(module, useIncrementalSession: true);

            //Both paths decide the same satisfiability, so their consistency
            //verdicts must agree whenever both reached one within budget. This
            //is the harness's only correctness claim.
            if(stateless.Decision?.Verdict is ModuleVerdict statelessVerdict && session.Decision?.Verdict is ModuleVerdict sessionVerdict)
            {
                if(statelessVerdict.IsConsistent != sessionVerdict.IsConsistent)
                {
                    disagreementCount++;
                }
            }

            TriageRow row = new(testCase.Identifier, module.Axioms.Count, stateless, session);
            rows.Add(row);
            if(earlyRows.Count < EarlySteadyCheckRowCount)
            {
                earlyRows.Add((row, module));
            }

            TestContext.WriteLine(FormatProgressLine(row));
        }

        string earlySteadyCheck = BuildEarlySteadyCheck(earlyRows);
        rows.Sort(CompareWorstFirst);

        string table = BuildTable(rows, cases.Length, premiseCases, dedupedCount, skippedCount, warmEvidence, earlySteadyCheck);
        File.WriteAllText(outputPath, table);
        TestContext.WriteLine(table);

        Assert.AreEqual(0, disagreementCount, "The stateless and incremental-session paths disagreed on consistency for at least one premise both decided within budget.");
    }

    /// <summary>
    /// Sweeps solve-capped decisions of the fixed probe instances under both
    /// the stateless and the incremental-session paths and writes one
    /// caps-by-path table per instance to the configured path. A capped
    /// decision abstains gracefully once it reaches
    /// <see cref="ReasoningBudget.MaxSolves"/> and still returns its
    /// statistics, so the per-cap trajectory discriminates mechanisms without
    /// unbounded runs: session wall time growing at equal solve counts
    /// isolates per-solve cost, while the session abstaining at caps the
    /// stateless path completes under isolates solve-count divergence. Two
    /// uncapped stateless runs bracket each instance's capped sweep — the
    /// first and the steady re-measure — and the steady one is the citable
    /// completion reference; their disagreement is the run's own visible
    /// residual-ramp evidence.
    /// </summary>
    [TestMethod]
    public void ProbeSolveCapTrajectories()
    {
        string? outputPath = Environment.GetEnvironmentVariable(ProbeOutputPathVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            //Opt-in measurement scaffolding, not a correctness gate: with no
            //output path configured the probe has nothing to do and the test
            //passes without measuring. Set VERITAS_SAT_SESSION_PROBE to run it.
            TestContext.WriteLine($"Skipping the solve-cap probe: set {ProbeOutputPathVariable} to an absolute output path to run it.");

            return;
        }

        ImmutableArray<Owl2TestCase> cases = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));
        string warmEvidence = WarmDecisionPaths(cases);

        StringBuilder report = new();
        report.AppendLine("SAT-backed reasoner solve-cap probe — stateless vs reused-session trajectories over fixed Direct-corpus instances.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Configuration: {BuildConfiguration()}. Per run: DecideModule(new ReasoningBudget(cap, 0, 0), ConflictLearning); a zero bound is unbounded on that axis, so conflicts and saturation inferences are uncapped.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Wall ceiling: {ProbeWallBudget.TotalSeconds:F0} s per capped run, {DecisionBudget.TotalSeconds:F0} s for the uncapped references; a fresh token per run. Box load: record at run time.");
        report.AppendLine(warmEvidence);
        report.AppendLine("Stats tuple per run: [solves; decisions; propagations; conflicts; learned; maxDecisionLevel]. ABSTAINED = solve cap reached; COMPLETED = decided under the cap; WALL-CANCELLED = wall token fired first.");

        foreach(string identifier in ProbeInstanceIdentifiers)
        {
            Owl2TestCase testCase = FindCase(cases, identifier);
            Assert.IsTrue(TryLoadModule(testCase, out ReasoningModule module), $"{identifier}: the premise did not parse and map.");

            ProbeOutcome firstReference = RunProbe(module, ReasoningBudget.Unbounded, useIncrementalSession: false, DecisionBudget);
            StringBuilder sweepRows = new();
            foreach(int cap in ProbeSolveCaps)
            {
                ProbeOutcome stateless = RunProbe(module, new ReasoningBudget(cap, 0, 0), useIncrementalSession: false, ProbeWallBudget);
                ProbeOutcome session = RunProbe(module, new ReasoningBudget(cap, 0, 0), useIncrementalSession: true, ProbeWallBudget);
                sweepRows.AppendLine(CultureInfo.InvariantCulture, $"{cap} | {stateless.Milliseconds:F1} | {stateless.Marker} | {FormatStats(stateless.Decision)} | {session.Milliseconds:F1} | {session.Marker} | {FormatStats(session.Decision)}");
                TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{identifier} cap={cap}: stateless={stateless.Milliseconds:F1}ms {stateless.Marker}, session={session.Milliseconds:F1}ms {session.Marker}"));
            }

            //The steady re-measure runs after the capped sweep, at maximum
            //distance from any residual ramp; its agreement with the first
            //reference is the run's own visible residual-ramp evidence.
            ProbeOutcome steadyReference = RunProbe(module, ReasoningBudget.Unbounded, useIncrementalSession: false, DecisionBudget);
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"== {identifier} (axioms={module.Axioms.Count}) ==");
            report.AppendLine(CultureInfo.InvariantCulture, $"Uncapped stateless reference (first): {firstReference.Milliseconds:F1} ms {firstReference.Marker} [{FormatStats(firstReference.Decision)}]");
            report.AppendLine(CultureInfo.InvariantCulture, $"Uncapped stateless reference (steady, re-measured after the capped sweep; the citable completion anchor): {steadyReference.Milliseconds:F1} ms {steadyReference.Marker} [{FormatStats(steadyReference.Decision)}]");
            report.AppendLine("cap | stateless (ms) | stateless outcome | stateless stats | session (ms) | session outcome | session stats");
            report.AppendLine("---:|---:|---|---|---:|---|---");
            report.Append(sweepRows);
        }

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    /// <summary>One premise's paired measurement: the identifier, axiom count, and each path's outcome.</summary>
    /// <param name="Identifier">The premise's Direct-corpus test identifier.</param>
    /// <param name="AxiomCount">The number of axioms in the decided module.</param>
    /// <param name="Stateless">The stateless per-solve path's outcome.</param>
    /// <param name="Session">The reused-incremental-session path's outcome.</param>
    private sealed record TriageRow(string Identifier, int AxiomCount, PathOutcome Stateless, PathOutcome Session);

    /// <summary>One capped probe run's result: the wall milliseconds, the outcome marker, and the decision (carrying its statistics) when the run was not wall-cancelled.</summary>
    /// <param name="Milliseconds">The elapsed wall milliseconds; for a wall-cancelled run, the elapsed at cancellation.</param>
    /// <param name="Marker"><c>ABSTAINED</c> (solve cap reached), <c>COMPLETED</c> (decided under the cap), or <c>WALL-CANCELLED</c> (wall token fired first).</param>
    /// <param name="Decision">The returned decision, or <see langword="null"/> when the run was wall-cancelled.</param>
    private sealed record ProbeOutcome(double Milliseconds, string Marker, ModuleDecision? Decision);

    /// <summary>Runs one decision under a solve cap and a fresh wall token, classifying the outcome as completed, abstained at the cap, or wall-cancelled.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The work-based bound on the decision; <see cref="ReasoningBudget.Unbounded"/> for the reference run.</param>
    /// <param name="useIncrementalSession">Whether the shared-CNF world solves reuse one incremental session across the decision.</param>
    /// <param name="wallBudget">The wall-clock ceiling for the run.</param>
    /// <returns>The run's result.</returns>
    private static ProbeOutcome RunProbe(ReasoningModule module, ReasoningBudget budget, bool useIncrementalSession, TimeSpan wallBudget)
    {
        using CancellationTokenSource wall = new(wallBudget);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ModuleDecision decision = SatTableauModuleReasoner.DecideModule(module, budget, SatSearchMode.ConflictLearning, useIncrementalSession, wall.Token);
            stopwatch.Stop();
            string marker = decision.Outcome switch
            {
                ReasoningDecisionOutcome.AbstainedBudget => "ABSTAINED",
                _ => "COMPLETED",
            };

            return new ProbeOutcome(stopwatch.Elapsed.TotalMilliseconds, marker, decision);
        }
        catch(OperationCanceledException)
        {
            stopwatch.Stop();

            return new ProbeOutcome(stopwatch.Elapsed.TotalMilliseconds, Marker: "WALL-CANCELLED", Decision: null);
        }
    }

    /// <summary>Finds the test case with the given identifier in the loaded manifest, failing loudly when absent.</summary>
    /// <param name="cases">The loaded test cases.</param>
    /// <param name="identifier">The identifier to find.</param>
    /// <returns>The matching test case.</returns>
    private static Owl2TestCase FindCase(ImmutableArray<Owl2TestCase> cases, string identifier)
    {
        foreach(Owl2TestCase testCase in cases)
        {
            if(testCase.Identifier == identifier)
            {
                return testCase;
            }
        }

        throw new InvalidOperationException($"The manifest does not declare a test case '{identifier}'.");
    }

    /// <summary>One path's outcome for one premise: the elapsed milliseconds and decision when within budget, or both absent when the path exceeded the budget.</summary>
    /// <param name="Milliseconds">The elapsed milliseconds when the path finished within budget; <see langword="null"/> when it exceeded the budget.</param>
    /// <param name="Decision">The reached decision, carrying its statistics, when within budget; <see langword="null"/> when the path exceeded the budget.</param>
    private readonly record struct PathOutcome(double? Milliseconds, ModuleDecision? Decision);

    /// <summary>One warm step's observed process-wide JIT movement: the step label, the compiled-method and JIT-time deltas across the step, and whether a decision in the step was cut by its budget.</summary>
    /// <param name="Label">The step's label in the evidence line.</param>
    /// <param name="MethodDelta">The process compiled-method count delta across the step.</param>
    /// <param name="JitDelta">The process JIT-time delta across the step.</param>
    /// <param name="BudgetCut">Whether a decision in the step hit its budget and was recorded rather than thrown.</param>
    private readonly record struct WarmStep(string Label, long MethodDelta, TimeSpan JitDelta, bool BudgetCut);

    /// <summary>
    /// Warms both decision paths — first on a trivial module for class-load
    /// and first-touch cost, then on the pinned heavy instance through a
    /// bounded ladder that stops once an iteration's process JIT-time delta
    /// falls to <see cref="HeavyWarmJitQuiescence"/> — so the timed loops
    /// start from the JIT steady state instead of absorbing the tier-0 to
    /// tier-1 ramp. The returned evidence line reads from
    /// <see cref="JitInfo"/>'s whole-process cumulative counters (all tiers,
    /// all assemblies, test host included), a coarse decay proxy for the hot
    /// methods' promotion rather than a per-method measurement; a ladder that
    /// reaches its bound still hot prints NOT QUIESCED, and a budget-cut step
    /// prints BUDGET-CUT.
    /// </summary>
    /// <param name="cases">The loaded manifest, carrying the pinned warm instance.</param>
    /// <returns>The formatted warm-evidence line both artifacts print.</returns>
    private static string WarmDecisionPaths(ImmutableArray<Owl2TestCase> cases)
    {
        List<WarmStep> steps = [];
        long previousMethods = JitInfo.GetCompiledMethodCount();
        TimeSpan previousJit = JitInfo.GetCompilationTime();

        ReasoningModule trivialModule = new([], Violations: []);
        bool trivialStatelessCompleted = WarmPath(trivialModule, useIncrementalSession: false);
        bool trivialSessionCompleted = WarmPath(trivialModule, useIncrementalSession: true);
        long currentMethods = JitInfo.GetCompiledMethodCount();
        TimeSpan currentJit = JitInfo.GetCompilationTime();
        steps.Add(new WarmStep("trivial", currentMethods - previousMethods, currentJit - previousJit, !trivialStatelessCompleted || !trivialSessionCompleted));
        previousMethods = currentMethods;
        previousJit = currentJit;

        Owl2TestCase warmCase = FindCase(cases, WarmInstanceIdentifier);
        Assert.IsTrue(TryLoadModule(warmCase, out ReasoningModule heavyModule), $"The pinned warm instance '{WarmInstanceIdentifier}' did not parse and map; the vendored corpus carrying it is a precondition of the warm protocol.");

        bool quiesced = false;
        for(int iteration = 1; iteration <= HeavyWarmIterationBound && !quiesced; iteration++)
        {
            bool statelessCompleted = WarmPath(heavyModule, useIncrementalSession: false);
            bool sessionCompleted = WarmPath(heavyModule, useIncrementalSession: true);
            currentMethods = JitInfo.GetCompiledMethodCount();
            currentJit = JitInfo.GetCompilationTime();
            TimeSpan jitDelta = currentJit - previousJit;
            steps.Add(new WarmStep(string.Create(CultureInfo.InvariantCulture, $"heavy{iteration}"), currentMethods - previousMethods, jitDelta, !statelessCompleted || !sessionCompleted));
            previousMethods = currentMethods;
            previousJit = currentJit;
            quiesced = jitDelta <= HeavyWarmJitQuiescence;
        }

        List<string> stepParts = [];
        foreach(WarmStep step in steps)
        {
            string cutMarker = step.BudgetCut ? " BUDGET-CUT" : string.Empty;
            stepParts.Add(string.Create(CultureInfo.InvariantCulture, $"{step.Label} +{step.MethodDelta}/+{step.JitDelta.TotalMilliseconds:F0}{cutMarker}"));
        }

        WarmStep lastStep = steps[^1];
        string quiesceSummary = quiesced
            ? string.Create(CultureInfo.InvariantCulture, $"QUIESCED (last heavy delta {lastStep.JitDelta.TotalMilliseconds:F0} ms <= {HeavyWarmJitQuiescence.TotalMilliseconds:F0} ms)")
            : string.Create(CultureInfo.InvariantCulture, $"NOT QUIESCED at the bound ({HeavyWarmIterationBound} heavy steps; last delta {lastStep.JitDelta.TotalMilliseconds:F0} ms > {HeavyWarmJitQuiescence.TotalMilliseconds:F0} ms)");

        return string.Create(CultureInfo.InvariantCulture, $"Warm: trivial + {WarmInstanceIdentifier} both paths per step; process JIT deltas (methods/ms): {string.Join(", ", stepParts)}; {quiesceSummary}; process totals before the timed region: {previousMethods} methods / {previousJit.TotalMilliseconds:F0} ms.");
    }

    /// <summary>Runs one warm decision of the module through the given path under its own fresh budget token, reporting a budget cut as a value rather than letting the cancellation escape.</summary>
    /// <param name="module">The module to warm on.</param>
    /// <param name="useIncrementalSession">Whether the shared-CNF world solves reuse one incremental session across the decision.</param>
    /// <returns><see langword="true"/> when the decision completed within the budget; <see langword="false"/> when the budget cut it.</returns>
    private static bool WarmPath(ReasoningModule module, bool useIncrementalSession)
    {
        using CancellationTokenSource budget = new(DecisionBudget);
        try
        {
            SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession, budget.Token);

            return true;
        }
        catch(OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Re-measures both paths for the leading processed rows after the sweep
    /// and formats the early-row steady check block. The table rows keep
    /// their first measurements, so a residual first-touch cost near the
    /// warm boundary — or on construct paths the single warm instance does
    /// not exercise — shows here as a first-vs-steady disagreement instead
    /// of hiding in the rows.
    /// </summary>
    /// <param name="earlyRows">The leading processed rows with their loaded modules.</param>
    /// <returns>The formatted block.</returns>
    private static string BuildEarlySteadyCheck(List<(TriageRow Row, ReasoningModule Module)> earlyRows)
    {
        StringBuilder block = new();
        block.AppendLine(CultureInfo.InvariantCulture, $"Early-row steady check, milliseconds (first {earlyRows.Count} processed rows re-measured after the sweep; table rows keep the first measurement):");
        foreach((TriageRow row, ReasoningModule module) in earlyRows)
        {
            PathOutcome steadyStateless = TimePath(module, useIncrementalSession: false);
            PathOutcome steadySession = TimePath(module, useIncrementalSession: true);
            block.AppendLine(CultureInfo.InvariantCulture, $"{row.Identifier}: stateless first {FormatMilliseconds(row.Stateless.Milliseconds)} steady {FormatMilliseconds(steadyStateless.Milliseconds)}; session first {FormatMilliseconds(row.Session.Milliseconds)} steady {FormatMilliseconds(steadySession.Milliseconds)}");
        }

        return block.ToString();
    }

    /// <summary>Times one path's decision of the module under a fresh per-instance budget, recording a budget-exceeded outcome rather than wedging the run.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="useIncrementalSession">Whether the shared-CNF world solves reuse one incremental session across the decision.</param>
    /// <returns>The path's outcome: the elapsed milliseconds and decision within budget, or both absent when the budget was exceeded.</returns>
    private static PathOutcome TimePath(ReasoningModule module, bool useIncrementalSession)
    {
        using CancellationTokenSource budget = new(DecisionBudget);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ModuleDecision decision = SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession, budget.Token);
            stopwatch.Stop();

            return new PathOutcome(stopwatch.Elapsed.TotalMilliseconds, decision);
        }
        catch(OperationCanceledException)
        {
            stopwatch.Stop();

            return new PathOutcome(Milliseconds: null, Decision: null);
        }
    }

    /// <summary>
    /// Loads, import-resolves, and maps the case's premise to a reasoning
    /// module the same way the corpus runner does, reporting failure instead
    /// of asserting so an unparseable or unmappable premise is skipped and
    /// counted rather than failing the run.
    /// </summary>
    /// <param name="testCase">The case whose premise to load.</param>
    /// <param name="module">The mapped module when the load succeeded; an empty module otherwise.</param>
    /// <returns><see langword="true"/> when the premise parsed and mapped cleanly.</returns>
    private static bool TryLoadModule(Owl2TestCase testCase, out ReasoningModule module)
    {
        module = new ReasoningModule([], Violations: []);
        try
        {
            DiagnosticBag diagnostics = new();
            List<Quad> quads =
            [
                .. RdfXmlReader.Read(testCase.RdfXmlPremise!.Value.Memory, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri)),
            ];
            if(diagnostics.HasErrors)
            {
                return false;
            }

            quads = Owl2ImportResolver.Expand(testCase, quads);
            OwlOntologyDocument premise = OwlRdfMapper.Map(quads);
            if(premise.Diagnostics.HasErrors)
            {
                return false;
            }

            module = new ReasoningModule([.. premise.Axioms], Violations: []);

            return true;
        }
        catch(Exception exception) when(exception is FormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Orders rows worst-first: premises whose incremental-session path
    /// exceeded the budget lead — among them the most dramatic (smallest
    /// stateless time) first — then the rest by descending session/stateless
    /// time ratio.
    /// </summary>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    /// <returns>A negative value when <paramref name="first"/> is the worse (earlier) row, a positive value when <paramref name="second"/> is, zero when neither.</returns>
    private static int CompareWorstFirst(TriageRow first, TriageRow second)
    {
        bool firstSessionExceeded = first.Session.Milliseconds is null;
        bool secondSessionExceeded = second.Session.Milliseconds is null;
        if(firstSessionExceeded != secondSessionExceeded)
        {
            return firstSessionExceeded ? -1 : 1;
        }

        if(firstSessionExceeded && secondSessionExceeded)
        {
            double firstStateless = first.Stateless.Milliseconds ?? double.PositiveInfinity;
            double secondStateless = second.Stateless.Milliseconds ?? double.PositiveInfinity;

            return firstStateless.CompareTo(secondStateless);
        }

        return SessionRatio(second).CompareTo(SessionRatio(first));
    }

    /// <summary>The session/stateless time ratio for a row whose session path finished; a row whose stateless path exceeded the budget while the session finished ranks best (ratio zero).</summary>
    /// <param name="row">The row to rank.</param>
    /// <returns>The ratio, or zero when the stateless time is unavailable or non-positive.</returns>
    private static double SessionRatio(TriageRow row)
    {
        if(row.Stateless.Milliseconds is not double statelessMilliseconds || statelessMilliseconds <= 0.0)
        {
            return 0.0;
        }

        return row.Session.Milliseconds!.Value / statelessMilliseconds;
    }

    /// <summary>Formats a one-line progress note for a decided premise, written to the test context as the loop advances.</summary>
    /// <param name="row">The premise's paired measurement.</param>
    /// <returns>The progress line.</returns>
    private static string FormatProgressLine(TriageRow row)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{row.Identifier}: axioms={row.AxiomCount} stateless={FormatMilliseconds(row.Stateless.Milliseconds)} session={FormatMilliseconds(row.Session.Milliseconds)} ratio={FormatRatio(row)}");
    }

    /// <summary>Builds the full worst-first comparison table with the header, the warm-evidence line, the per-premise rows, the totals line, and the early-row steady check block.</summary>
    /// <param name="rows">The measured rows, already ordered worst-first.</param>
    /// <param name="totalCases">The number of cases in the loaded manifest.</param>
    /// <param name="premiseCases">The number of cases carrying a non-null RDF/XML premise.</param>
    /// <param name="dedupedCount">The number of premises skipped as duplicates of an earlier case.</param>
    /// <param name="skippedCount">The number of premises skipped for failing RDF/XML parse or structural mapping.</param>
    /// <param name="warmEvidence">The warm-evidence line the warm protocol returned.</param>
    /// <param name="earlySteadyCheck">The formatted early-row steady check block.</param>
    /// <returns>The rendered table.</returns>
    private static string BuildTable(List<TriageRow> rows, int totalCases, int premiseCases, int dedupedCount, int skippedCount, string warmEvidence, string earlySteadyCheck)
    {
        int sessionExceeded = 0;
        int statelessExceeded = 0;
        foreach(TriageRow row in rows)
        {
            if(row.Session.Milliseconds is null)
            {
                sessionExceeded++;
            }

            if(row.Stateless.Milliseconds is null)
            {
                statelessExceeded++;
            }
        }

        StringBuilder table = new();
        table.AppendLine("SAT-backed reasoner incremental-session triage — stateless vs reused-session consistency decision over the Direct corpus.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Configuration: {BuildConfiguration()}. Per premise: DecideModule(Unbounded, ConflictLearning) twice, useIncrementalSession false then true, warmup excluded.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Budget: {DecisionBudget.TotalSeconds:F0} s per premise per path (exceeding it records BUDGET-EXCEEDED). Box load: record at run time.");
        table.AppendLine(warmEvidence);
        table.AppendLine("Stats tuple per path: [solves; decisions; propagations; conflicts; learned; maxDecisionLevel].");
        table.AppendLine();
        table.AppendLine("rank | test id | axioms | stateless (ms) | session (ms) | ratio (session/stateless) | agree | stateless stats | session stats");
        table.AppendLine("---:|---|---:|---:|---:|---:|:---:|---|---");

        int rank = 1;
        foreach(TriageRow row in rows)
        {
            table.AppendLine(CultureInfo.InvariantCulture, $"{rank} | {row.Identifier} | {row.AxiomCount} | {FormatMilliseconds(row.Stateless.Milliseconds)} | {FormatMilliseconds(row.Session.Milliseconds)} | {FormatRatio(row)} | {FormatAgreement(row)} | {FormatStats(row.Stateless.Decision)} | {FormatStats(row.Session.Decision)}");
            rank++;
        }

        table.AppendLine();
        table.AppendLine(CultureInfo.InvariantCulture, $"Totals: {rows.Count} premises measured; {sessionExceeded} session BUDGET-EXCEEDED; {statelessExceeded} stateless BUDGET-EXCEEDED.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Corpus: {totalCases} manifest cases; {premiseCases} with a premise; {dedupedCount} deduped; {skippedCount} skipped (parse/map failure).");
        table.AppendLine();
        table.Append(earlySteadyCheck);

        return table.ToString();
    }

    /// <summary>Formats a path's elapsed milliseconds, or the budget-exceeded marker when the path was cut off.</summary>
    /// <param name="milliseconds">The elapsed milliseconds, or <see langword="null"/> when the budget was exceeded.</param>
    /// <returns>The formatted cell.</returns>
    private static string FormatMilliseconds(double? milliseconds)
    {
        return milliseconds is double value
            ? value.ToString("F1", CultureInfo.InvariantCulture)
            : $"BUDGET-EXCEEDED>{DecisionBudget.TotalSeconds:F0}s";
    }

    /// <summary>Formats the session/stateless ratio cell, naming which path exceeded the budget when one did.</summary>
    /// <param name="row">The premise's paired measurement.</param>
    /// <returns>The formatted ratio cell.</returns>
    private static string FormatRatio(TriageRow row)
    {
        if(row.Session.Milliseconds is null)
        {
            return "SESSION>budget";
        }

        if(row.Stateless.Milliseconds is not double statelessMilliseconds || statelessMilliseconds <= 0.0)
        {
            return "STATELESS>budget";
        }

        return "×" + (row.Session.Milliseconds!.Value / statelessMilliseconds).ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the consistency-agreement cell: a mark when both paths decided within budget, a dash otherwise.</summary>
    /// <param name="row">The premise's paired measurement.</param>
    /// <returns>The formatted agreement cell.</returns>
    private static string FormatAgreement(TriageRow row)
    {
        if(row.Stateless.Decision?.Verdict is ModuleVerdict statelessVerdict && row.Session.Decision?.Verdict is ModuleVerdict sessionVerdict)
        {
            return statelessVerdict.IsConsistent == sessionVerdict.IsConsistent ? "yes" : "NO";
        }

        return "-";
    }

    /// <summary>Formats a decision's statistics as the compact per-path tuple, or a dash when the path exceeded the budget.</summary>
    /// <param name="decision">The decision, or <see langword="null"/> when the path exceeded the budget.</param>
    /// <returns>The formatted stats cell.</returns>
    private static string FormatStats(ModuleDecision? decision)
    {
        if(decision is null)
        {
            return "-";
        }

        ReasoningDecisionStatistics statistics = decision.Statistics;
        SatSolveStatistics totals = statistics.SolverTotals;

        return string.Create(CultureInfo.InvariantCulture, $"{statistics.SolveCount}; {totals.Decisions}; {totals.Propagations}; {totals.Conflicts}; {totals.LearnedClauses}; {totals.MaxDecisionLevel}");
    }

    /// <summary>The build configuration the harness runs under, for the table header.</summary>
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
