using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// A cost-breakdown soak for the description-logic deciding engines. It drives
/// the snapshot tableau (<see cref="AlcModuleReasoner"/>), the production
/// SAT-backed engine (<see cref="SatTableauModuleReasoner"/>), the EL
/// pay-as-you-go engine (<see cref="ReasoningEngines.ElCoupled"/>), the
/// consequence-based context-saturation engine over an abstaining sentinel
/// (<see cref="ReasoningEngines.ContextSaturation(ReasoningBudget, DescriptionLogicDelegate?)"/>),
/// and the production composition that layers all three fast paths over the
/// SAT-backed oracle, over a ladder of synthetic ontologies, and attributes each
/// engine's wall-clock to the reasoning phases of <see cref="ReasoningPhase"/> via
/// <see cref="ReasoningInstrumentation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The soak answers a single load-bearing question before any blocking-index
/// kernel is wired into the reasoner: <em>is the blocking check a meaningful
/// fraction of a decision, or is it dwarfed by the SAT solve and the tableau
/// rule loop?</em> The ladder deliberately includes a blocking-stress family
/// (cyclic existential TBoxes that force a deep completion forest the blocking
/// check folds) and a SAT-stress family (pairwise-disjoint disjunction cores
/// that force propositional search), so the breakdown shows where time goes on
/// the workloads most likely to favour each phase.
/// </para>
/// <para>
/// Each engine decides the same module, so the consistency verdicts of the
/// engines that admit it are a differential oracle: the soak prints whether they
/// agree. The context-saturation column abstains as NOT-ADMITTED on a module
/// outside the Horn-ALCHI slice and is excluded from the agreement comparison
/// there. Output is line-oriented and prefixed <c>[owl-classify]</c> for
/// hand-collation.
/// </para>
/// <para>
/// Corpus files named on the command line replace the synthetic ladder: each is
/// read to an ontology document, measured through the whole-ontology EL
/// classification lane of <see cref="ElClassifier.Classify(OwlOntologyDocument, CancellationToken)"/>,
/// and then run through the same five-engine breakdown over the module its
/// axioms form.
/// </para>
/// </remarks>
internal static class OwlClassificationSoak
{
    /// <summary>The example namespace synthetic entities are minted in.</summary>
    private const string Example = "http://example.org/owl-classify/";

    /// <summary>The default wall-clock target a measured engine run accumulates iterations toward, in milliseconds.</summary>
    private const double DefaultTargetMilliseconds = 120.0;

    /// <summary>The default iteration cap a measured engine run stops at regardless of elapsed time, so a slow decision runs at least once and at most this many times.</summary>
    private const int DefaultMaxIterations = 200_000;

    /// <summary>The default per-decision probe timeout, in milliseconds: a single decision that does not complete within it is reported as timed out rather than blocking the soak — the snapshot engine on a branching cyclic TBox can run for minutes.</summary>
    private const int DefaultProbeTimeoutMilliseconds = 15_000;

    /// <summary>The EL pay-as-you-go engine over the default snapshot fallback.</summary>
    private static DescriptionLogicDelegate ElCoupled { get; } = ReasoningEngines.ElCoupled();

    /// <summary>The context-saturation engine over an abstaining sentinel: a module outside the Horn-ALCHI slice surfaces as NOT-ADMITTED rather than borrowing another engine's verdict or cost, mirroring the delegation-rate harness's context column.</summary>
    private static DescriptionLogicDelegate ContextSatWithNotAdmitted { get; } = ReasoningEngines.ContextSaturation(ReasoningBudget.Unbounded, DecideNotAdmitted);

    /// <summary>The production composition: the EL fast path over the context-saturation tier over the SAT-backed oracle — the chain the composition root wires.</summary>
    private static DescriptionLogicDelegate ElCtxSatComposition { get; } = ReasoningEngines.ElCoupled(ReasoningEngines.ContextSaturation(ReasoningBudget.Unbounded, ReasoningEngines.SatBacked(ReasoningBudget.Unbounded)));

    /// <summary>The abstaining fallback for the context column: it decides no module, returning an on-budget abstention with a null verdict so a context-non-admitted module surfaces as NOT-ADMITTED.</summary>
    /// <param name="module">The module the context engine did not admit.</param>
    /// <param name="cancellationToken">The budget token, unused because the fallback does no work.</param>
    /// <returns>An abstaining decision carrying only the module's axiom count.</returns>
    private static ValueTask<ModuleDecision> DecideNotAdmitted(ReasoningModule module, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return new ValueTask<ModuleDecision>(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty with { ModuleAxiomCount = module.Axioms.Count }));
    }

    /// <summary>Runs the cost breakdown across the five engines: over the corpus files named on the command line when any were named, and over the synthetic ladder otherwise.</summary>
    /// <param name="args">The full command-line arguments; recognised options after the profile selector tune the measurement and name corpus files (see <see cref="ParseSettings"/>).</param>
    /// <returns>The soak's completion.</returns>
    public static async Task RunClassificationSoak(string[] args)
    {
        SoakSettings settings = ParseSettings(args);

        //The file count is named only when files were supplied, so a ladder run's
        //settings line carries exactly the three tunables.
        string fileSuffix = settings.Files.Count > 0 ? $" files={settings.Files.Count:N0}" : string.Empty;
        Console.WriteLine($"[owl-classify] stopwatch frequency={Stopwatch.Frequency:N0} Hz  high-resolution={Stopwatch.IsHighResolution}");
        Console.WriteLine($"[owl-classify] settings: target-ms={settings.TargetMilliseconds:F0} max-iters={settings.MaxIterations:N0} timeout-ms={settings.ProbeTimeoutMilliseconds:N0}{fileSuffix}");

        //Named corpus files replace the synthetic ladder: the run measures exactly the
        //files given, in the order they were given.
        if(settings.Files.Count > 0)
        {
            foreach(string path in settings.Files)
            {
                await RunFileWorkload(path, settings).ConfigureAwait(false);
            }

            return;
        }

        foreach(Workload workload in Ladder())
        {
            await RunWorkload(workload, settings).ConfigureAwait(false);
        }
    }

    /// <summary>Parses the tunable measurement settings from the command line, falling back to the defaults for any option not given.</summary>
    /// <param name="args">The full command-line arguments. Recognised options: <c>--target-ms &lt;double&gt;</c>, <c>--max-iters &lt;int&gt;</c>, <c>--timeout-ms &lt;int&gt;</c>, and <c>--file &lt;path&gt;</c>, which repeats to name several corpus files and is collected in the order given.</param>
    /// <returns>The parsed settings.</returns>
    private static SoakSettings ParseSettings(string[] args)
    {
        double targetMilliseconds = DefaultTargetMilliseconds;
        int maxIterations = DefaultMaxIterations;
        int probeTimeoutMilliseconds = DefaultProbeTimeoutMilliseconds;
        List<string> files = [];

        for(int index = 0; index < args.Length - 1; index++)
        {
            switch(args[index])
            {
                case ("--target-ms"):
                {
                    if(double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                    {
                        targetMilliseconds = parsed;
                    }

                    break;
                }
                case ("--max-iters"):
                {
                    if(int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        maxIterations = parsed;
                    }

                    break;
                }
                case ("--timeout-ms"):
                {
                    if(int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        probeTimeoutMilliseconds = parsed;
                    }

                    break;
                }
                case ("--file"):
                {
                    files.Add(args[index + 1]);

                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return new SoakSettings(targetMilliseconds, maxIterations, probeTimeoutMilliseconds, files);
    }

    /// <summary>The ladder of synthetic workloads, ordered to surface each phase in turn.</summary>
    /// <returns>The workloads.</returns>
    private static IEnumerable<Workload> Ladder()
    {
        //Blocking stress: cyclic existential chains the completion folds by
        //blocking. A single-role cycle grows a linear forest of roughly twice
        //the layer count before the pairwise repeat folds it; the blocking
        //check is recomputed over that whole forest every iteration.
        yield return CyclicExistential(layers: 8, branching: 1);
        yield return CyclicExistential(layers: 16, branching: 1);
        yield return CyclicExistential(layers: 32, branching: 1);
        yield return CyclicExistential(layers: 64, branching: 1);
        yield return CyclicExistential(layers: 4, branching: 2);

        //SAT / branching stress: pairwise-disjoint atoms with disjunctive class
        //assertions, an exact-cover core the SAT engine searches and the
        //snapshot engine branches over.
        yield return DisjunctionCore(atoms: 6, clauses: 10);
        yield return DisjunctionCore(atoms: 8, clauses: 24);
        yield return DisjunctionCore(atoms: 10, clauses: 40);

        //Classification: a subclass taxonomy within the 16-class subsumption
        //cap, so the decision runs the full pairwise subsumption sweep.
        yield return SubclassTaxonomy(classes: 8);
        yield return SubclassTaxonomy(classes: 16);

        //EL fast path: conjunction-and-existential TBoxes the EL engine
        //saturates in polynomial time without tableau branching.
        yield return ElConjunctiveExistential(classes: 32);
        yield return ElConjunctiveExistential(classes: 128);
        yield return ElConjunctiveExistential(classes: 512);

        //EL fast path with property ranges: range-bearing existential chains the
        //EL engine decides through the sound per-edge range rule.
        yield return ElRangeExistential(classes: 128);
        yield return ElRangeExistential(classes: 512);

        //EL fast path with an inverse-role range: range(r⁻) = domain(r) types every
        //existential's source through the owner-independent forward reduction, decided
        //where the inverse-blind tableau drops the axiom.
        yield return ElInverseRangeExistential(classes: 128);
        yield return ElInverseRangeExistential(classes: 512);

        //EL fast path with data restrictions: an existential chain where each class
        //also demands a data value in a satisfiable range, decided by the value-space
        //checker in the EL engine.
        yield return ElDatatypeExistential(classes: 128);
        yield return ElDatatypeExistential(classes: 512);

        //EL fast path with local/global reflexivity: ObjectHasSelf demands with a
        //self-elimination, and a reflexive role whose range types every node, decided
        //through reflexive self-edges where the self-blind tableau abstains. These
        //families stay consistent, so the engines AGREE while the EL path runs far
        //faster; the verdict-changing capability gain (a Self-forced inconsistency the
        //tableau misses) is pinned in the unit tests, not the soak.
        yield return ElSelfExistential(classes: 128);
        yield return ElSelfExistential(classes: 512);
        yield return ElReflexiveExistential(classes: 128);
        yield return ElReflexiveExistential(classes: 512);

        //EL fast path with a single-individual nominal: an existential chain plus an asserted
        //ObjectHasValue edge from the seed individual to one shared individual hub, decided
        //through the asserted nominal edge routed to that individual node where the
        //nominal-blind tableau abstains. The family stays consistent, so the engines AGREE
        //while the EL path runs far faster; the verdict-changing capability gain (a
        //nominal-forced inconsistency the tableau misses) is pinned in the unit tests, not the soak.
        yield return ElNominalExistential(classes: 128);
        yield return ElNominalExistential(classes: 512);

        //EL fast path with a SUPERCLASS single-individual nominal: an existential chain whose
        //deepest class carries A ⊑ ∃hasHub.{hub}, with the chain inhabited from the seed
        //individual. The carrier becomes live only by liveness propagating forward along the
        //chain (its existential edges are created before it is inhabited), then the fresh proxy
        //becomes live and the merge pools onto the individual — exercising the reachability gate.
        //Consistent, so the engines AGREE while the EL path runs far faster; the verdict-changing
        //gains (the uninhabited-carrier consistency and the inhabited-carrier inconsistency) are
        //pinned in the unit tests, not the soak.
        yield return ElSuperclassNominalExistential(classes: 128);
        yield return ElSuperclassNominalExistential(classes: 512);

        //EL fast path with a symmetric role over the asserted ground graph: a chain of asserted
        //edges over a symmetric role whose reverse the classifier mirrors, with the role's range
        //typing every node. Consistent, so the engines AGREE while the EL path decides the symmetry
        //the tableau drops; the verdict-changing capability gain (a symmetry-forced inconsistency the
        //tableau misses) is pinned in the unit tests, not the soak.
        yield return ElSymmetricAssertedGraph(individuals: 128);
        yield return ElSymmetricAssertedGraph(individuals: 512);

        //EL fast path with an inverse pairing over the asserted ground graph: a chain of asserted
        //edges over r with InverseObjectProperties(r, s), so the classifier mirrors each edge as the
        //reverse s-edge whose range types the source. Consistent, so the engines AGREE while the EL
        //path decides the inverse the tableau drops; the verdict-changing capability gain (an
        //inverse-forced inconsistency the tableau misses) is pinned in the unit tests, not the soak.
        yield return ElInverseAssertedGraph(individuals: 128);
        yield return ElInverseAssertedGraph(individuals: 512);

        //EL fast path with a functional role over the asserted ground graph: a star of asserted
        //successors over a functional role, all unioned onto one node by the pre-merge collapse.
        //Consistent, so the engines AGREE while the EL path decides the functionality the tableau
        //drops; the verdict-changing capability gain (a functionality-forced merge collision the
        //tableau misses) is pinned in the unit tests, not the soak.
        yield return ElFunctionalStar(successors: 128);
        yield return ElFunctionalStar(successors: 512);

        //EL fast path with an asymmetric role over the asserted ground graph: a one-directional chain of
        //asserted edges over an asymmetric role, decided by the EL engine over the asserted post-merge
        //graph where the characteristic-blind tableau drops the characteristic. No edge has a reverse, so
        //the family stays consistent and the engines AGREE while the EL path runs far faster; the
        //verdict-changing capability gain (an asymmetry-forced inconsistency the tableau misses) is pinned
        //in the unit tests, not the soak.
        yield return ElAsymmetricAssertedGraph(individuals: 128);
        yield return ElAsymmetricAssertedGraph(individuals: 512);

        //EL fast path with an irreflexive role over the asserted ground graph: a chain of asserted non-self
        //edges over an irreflexive role, decided by the EL engine where the characteristic-blind tableau
        //drops the characteristic. No edge is a self-edge, so the family stays consistent and the engines
        //AGREE while the EL path runs far faster; the verdict-changing capability gain (an irreflexivity-forced
        //inconsistency the tableau misses) is pinned in the unit tests, not the soak.
        yield return ElIrreflexiveAssertedGraph(individuals: 128);
        yield return ElIrreflexiveAssertedGraph(individuals: 512);

        //EL fast path with a superclass-position inverse existential: a depth-N backward chain
        //(C0 ⊑ ∃r⁻.C1, C1 ⊑ ∃r⁻.C2, …) inhabited from a seed individual, decided through the eager
        //generator reduction that mints each owner's r-predecessor as a forward g-successor. The chain
        //measures witness-population growth along the depth axis under the content-keyed shared mint.
        //Consistent, so the engines AGREE while the EL path decides the backward existentials the
        //inverse-blind tableau drops; the verdict-changing capability gain is pinned in the unit tests,
        //not the soak.
        yield return ElBackwardChainExistential(classes: 128);
        yield return ElBackwardChainExistential(classes: 512);

        //Backward consumption, sequential growth: the mutual 2-cycle plus a depth-N ladder of left
        //existentials over the mirror role. Each rung is a distinct decoration consumed into the witness
        //it fires on, so every position refines once per rung before the decorations saturate and the
        //cycle folds again. Consistent, so the engines AGREE; the curve is the node-growth measurement
        //that gates the mint-budget increment.
        yield return ElBackwardLadder(rungs: 8);
        yield return ElBackwardLadder(rungs: 16);
        yield return ElBackwardLadder(rungs: 32);

        //Backward consumption, simultaneous growth: one owner carrying K independent triggers on a single
        //witness. The batched consumption folds all K decorations into one refined node per firing; an
        //unbatched one explores the subset lattice of the K triggers, which is what K = 12 measures.
        yield return ElBackwardFanout(triggers: 4);
        yield return ElBackwardFanout(triggers: 8);
        yield return ElBackwardFanout(triggers: 12);

        //Context-saturation ground slice: a forward-universal owner over an asserted
        //edge graph. The EL and snapshot arms decide it
        //too, but only the context tier mints the per-representative ground contexts —
        //so the ctx phase is non-zero and the ground-context counters are observable.
        yield return ContextGroundSlice(individuals: 16);
        yield return ContextGroundSlice(individuals: 64);

        //Context-saturation inverse chain: independent forward-existential /
        //inverse-universal units (∃rel.Mid, Mid ⊑ ∀rel⁻.Back), the context engine's
        //distinct reach past EL and the inverse-blind tableau. Context-decided, so the
        //ctx phase and the derived-clause counters are non-zero.
        yield return ContextInverseChain(units: 8);
        yield return ContextInverseChain(units: 24);

        //Context-saturation equality / counting: a functional role over several
        //existential demands the counting tier merges onto one successor (the
        //Eq-merge shape). Context-decided, so the ctx phase is non-zero.
        yield return ContextFunctionalMerge(successors: 4);
        yield return ContextFunctionalMerge(successors: 8);
    }

    /// <summary>Measures the five engines on one workload and prints the breakdown.</summary>
    /// <param name="workload">The workload to run.</param>
    /// <param name="settings">The tunable measurement settings.</param>
    /// <returns>The workload run's completion.</returns>
    private static async Task RunWorkload(Workload workload, SoakSettings settings)
    {
        ReasoningModule module = workload.Module;
        Console.WriteLine($"[owl-classify] {workload.Name}  axioms={module.Axioms.Count:N0}");

        EngineRun snapshot = await MeasureEngine("snapshot ", static (m, token) => new ValueTask<ModuleDecision>(AlcModuleReasoner.DecideModule(m, token)), module, settings).ConfigureAwait(false);
        EngineRun satBacked = await MeasureEngine("satbacked", static (m, token) => new ValueTask<ModuleDecision>(SatTableauModuleReasoner.DecideModule(m, ReasoningBudget.Unbounded, cancellationToken: token)), module, settings).ConfigureAwait(false);
        EngineRun el = await MeasureEngine("elcoupled", ElCoupled, module, settings).ConfigureAwait(false);
        EngineRun contextSat = await MeasureEngine("contextst", ContextSatWithNotAdmitted, module, settings).ConfigureAwait(false);
        EngineRun elCtxSat = await MeasureEngine("elctxsat ", ElCtxSatComposition, module, settings).ConfigureAwait(false);

        ReportEngine(snapshot, settings);
        ReportEngine(satBacked, settings);
        ReportEngine(el, settings);
        ReportEngine(contextSat, settings);
        ReportEngine(elCtxSat, settings);

        //Only the engines that reached a verdict are compared: a timed-out engine
        //carries no verdict, and the context column abstains as NOT-ADMITTED
        //(AbstainedBudget, null verdict) on a module outside the Horn-ALCHI slice —
        //excluded from the agreement so its non-admission is not read as a verdict.
        List<bool> decided = [];
        foreach(EngineRun run in new[] { snapshot, satBacked, el, contextSat, elCtxSat })
        {
            if(run.Decision?.Verdict?.IsConsistent is bool value)
            {
                decided.Add(value);
            }
        }

        bool agree = decided.Count == 0 || decided.TrueForAll(value => value == decided[0]);
        Console.WriteLine(
            $"[owl-classify]   verdicts: snapshot={Show(snapshot)} satbacked={Show(satBacked)} elcoupled={Show(el)} contextsat={Show(contextSat)} elctxsat={Show(elCtxSat)}  {(agree ? "AGREE" : "DISAGREE")}");
    }

    /// <summary>
    /// Measures one supplied corpus file: it reads the file to an ontology
    /// document, reports the whole-ontology EL classification lane over it, and
    /// then runs the same five-engine breakdown over the module the document's
    /// axioms form. A file that does not read, or whose mapping recorded errors,
    /// is reported and skipped rather than failing the run.
    /// </summary>
    /// <param name="path">The corpus file's path, as given on the command line.</param>
    /// <param name="settings">The tunable measurement settings; the probe timeout also bounds the classification.</param>
    /// <returns>The file run's completion.</returns>
    private static async Task RunFileWorkload(string path, SoakSettings settings)
    {
        string name = Path.GetFileName(path);
        OwlOntologyDocument? loaded = await LoadDocument(path).ConfigureAwait(false);
        if(loaded is not OwlOntologyDocument document)
        {
            Console.WriteLine($"[owl-classify] file={name} LOAD-FAILED");

            return;
        }

        Console.WriteLine($"[owl-classify] file={name}  axioms={document.Axioms.Length:N0}");
        ReportClassification(document, settings);

        //A document carrying mapping errors is not a well-formed ontology, so the
        //module the engines would decide is not the file's content.
        if(document.Diagnostics.HasErrors)
        {
            Console.WriteLine("[owl-classify]   module skipped (document carries mapping diagnostics)");

            return;
        }

        await RunWorkload(new Workload(name, new ReasoningModule([.. document.Axioms], Violations: [])), settings).ConfigureAwait(false);
    }

    /// <summary>Times and measures one document's whole-ontology EL classification under the probe timeout and prints the lane's line, reporting a timeout rather than wedging the run. The classifier runs synchronously on the calling thread, so the thread-local allocation delta covers exactly the classification.</summary>
    /// <param name="document">The document to classify.</param>
    /// <param name="settings">The tunable measurement settings; the probe timeout bounds the single classification.</param>
    private static void ReportClassification(OwlOntologyDocument document, SoakSettings settings)
    {
        using CancellationTokenSource budget = new(settings.ProbeTimeoutMilliseconds);
        Stopwatch stopwatch = new();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();
        try
        {
            ElClassification classification = ElClassifier.Classify(document, budget.Token);
            stopwatch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine(
                $"[owl-classify]   classify : {stopwatch.Elapsed.TotalMilliseconds,9:F4} ms  alloc {allocated / 1048576.0,9:F1} MB  classes={classification.Classes.Count:N0}  coherent={(classification.IsCoherent ? "yes" : "NO")}  undecided={classification.UnsupportedConstructs.Count:N0}");
        }
        catch(OperationCanceledException)
        {
            stopwatch.Stop();
            Console.WriteLine($"[owl-classify]   classify : TIMEOUT (single classification exceeded {settings.ProbeTimeoutMilliseconds:N0} ms)");
        }
    }

    /// <summary>
    /// Reads one corpus file to an ontology document the way the matching
    /// front-end does, reporting failure through a null result instead of
    /// throwing so an unreadable file is reported and skipped. A <c>.ofn</c> file
    /// is read as functional syntax, a <c>.ttl</c> file is drained through the
    /// Turtle reader, and every other extension is read as RDF/XML; the two
    /// graph syntaxes then map to structural form.
    /// </summary>
    /// <param name="path">The corpus file's path.</param>
    /// <returns>The document when the file read and mapped cleanly; <see langword="null"/> otherwise.</returns>
    private static async Task<OwlOntologyDocument?> LoadDocument(string path)
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
                ? await DrainTurtle(bytes, diagnostics, baseIri).ConfigureAwait(false)
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

    /// <summary>Drains the never-throwing Turtle reader's async quad iterator over an in-memory source into a quad list.</summary>
    /// <param name="source">The UTF-8 Turtle source bytes.</param>
    /// <param name="diagnostics">The bag lexical and parse diagnostics accumulate into.</param>
    /// <param name="baseIri">The document base IRI for resolving relative references.</param>
    /// <returns>The parsed quads.</returns>
    private static async Task<List<Quad>> DrainTurtle(ReadOnlyMemory<byte> source, DiagnosticBag diagnostics, string baseIri)
    {
        List<Quad> quads = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(source, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseIri).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }

    /// <summary>Formats an engine run's consistency verdict, or the timed-out / not-admitted marker.</summary>
    /// <param name="run">The measured run.</param>
    /// <returns>The display string: the timeout marker, the not-admitted marker for a context abstention, the consistency verdict, or <c>none</c>.</returns>
    private static string Show(EngineRun run)
    {
        if(run.TimedOut)
        {
            return "timeout";
        }

        if(run.Decision is { Outcome: ReasoningDecisionOutcome.AbstainedBudget, Verdict: null })
        {
            return "not-admitted";
        }

        return run.Decision?.Verdict?.IsConsistent.ToString() ?? "none";
    }

    /// <summary>Prints one engine's per-decision cost and phase breakdown.</summary>
    /// <param name="run">The measured run.</param>
    private static void ReportEngine(EngineRun run, SoakSettings settings)
    {
        if(run.TimedOut || run.Decision is null || run.Report is null)
        {
            Console.WriteLine($"[owl-classify]   {run.Engine}: TIMEOUT (single decision exceeded {settings.ProbeTimeoutMilliseconds:N0} ms)");

            return;
        }

        //The context column's abstaining sentinel returns AbstainedBudget with a null
        //verdict on a module the context engine does not admit. Under the Unbounded
        //budget the engine's own budget abstention is unreachable, so this shape is
        //unambiguously non-admission: report it as NOT-ADMITTED, without a cost/phase
        //row, and let RunWorkload exclude it from the verdict-agreement comparison.
        if(run.Decision is { Outcome: ReasoningDecisionOutcome.AbstainedBudget, Verdict: null })
        {
            Console.WriteLine($"[owl-classify]   {run.Engine}: NOT-ADMITTED (context engine did not admit the module)");

            return;
        }

        ReasoningDecisionStatistics statistics = run.Decision.Statistics;
        double wall = run.WallMillisecondsPerDecision;
        Console.WriteLine(
            $"[owl-classify]   {run.Engine}: {wall,9:F4} ms/dec  alloc {run.AllocBytesPerDecision / 1024.0,9:F1} KB  iters={run.Iterations:N0}  axioms={statistics.ModuleAxiomCount}");

        AlcTableauStatistics tableau = statistics.TableauTotals;
        SatSolveStatistics solver = statistics.SolverTotals;
        ElSaturationStatistics elTotals = statistics.ElTotals;
        ContextSaturationStatistics contextTotals = statistics.ContextTotals;
        Console.WriteLine(
            $"[owl-classify]      counters: solveCount={statistics.SolveCount} tableauRuns={tableau.TableauRuns} ruleApps={tableau.RuleApplications:N0} branches={tableau.Branches} clashes={tableau.Clashes} maxNodes={tableau.MaxNodes} | propagations={solver.Propagations:N0} conflicts={solver.Conflicts} | elDecided={elTotals.ElDecided} completionRules={elTotals.CompletionRuleApplications:N0} completionEdges={elTotals.CompletionEdges:N0} | ctxDecided={contextTotals.ContextDecided} ctxRuleApps={contextTotals.RuleApplications:N0} clausesDerived={contextTotals.ClausesDerived:N0} groundContexts={contextTotals.GroundContextsCreated:N0}");

        ReasoningInstrumentationReport report = run.Report;
        double totalWall = wall * run.Iterations;
        double otherMilliseconds = Math.Max(0.0, totalWall - report.TotalAttributedMilliseconds);
        Console.WriteLine(
            $"[owl-classify]      breakdown: blocking {Percent(report.BlockingMilliseconds, totalWall)} ({report.BlockingCount:N0}x)  sat {Percent(report.SatSolveMilliseconds, totalWall)} ({report.SatSolveCount:N0}x)  rule {Percent(report.TableauRuleMilliseconds, totalWall)} ({report.TableauRuleCount:N0}x)  data {Percent(report.DataConsistencyMilliseconds, totalWall)}  el {Percent(report.ElSaturationMilliseconds, totalWall)}  ctx {Percent(report.ContextSaturationMilliseconds, totalWall)} ({report.ContextSaturationCount:N0}x)  other {Percent(otherMilliseconds, totalWall)}");
    }

    /// <summary>Formats a phase milliseconds value as a percentage of the total wall-clock.</summary>
    /// <param name="phaseMilliseconds">The phase's accumulated milliseconds.</param>
    /// <param name="totalMilliseconds">The total wall-clock the fraction is taken against.</param>
    /// <returns>The percentage, formatted to one decimal place.</returns>
    private static string Percent(double phaseMilliseconds, double totalMilliseconds)
    {
        double fraction = totalMilliseconds > 0.0 ? phaseMilliseconds / totalMilliseconds * 100.0 : 0.0;

        return $"{fraction,5:F1}%";
    }

    /// <summary>Warms up then time-boxes a measured run of one engine over a module, with phase instrumentation enabled.</summary>
    /// <param name="engineName">The display label.</param>
    /// <param name="decide">The engine under test; the deciding engines complete synchronously, so each returned value task is already resolved.</param>
    /// <param name="module">The module to decide.</param>
    /// <param name="settings">The tunable measurement settings.</param>
    /// <returns>The measured run.</returns>
    private static async Task<EngineRun> MeasureEngine(string engineName, DescriptionLogicDelegate decide, ReasoningModule module, SoakSettings settings)
    {
        //Probe with a timeout: the probe warms the path and gates out a
        //pathological decision (the snapshot engine can run for minutes on a
        //branching cyclic TBox) before the unbounded measured loop begins.
        ModuleDecision warm;
        using(CancellationTokenSource probe = new(settings.ProbeTimeoutMilliseconds))
        {
            try
            {
                warm = await decide(module, probe.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
                return EngineRun.CreateTimedOut(engineName);
            }
        }

        ReasoningInstrumentation.Enable();
        long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        int iterations = 0;
        ModuleDecision last = warm;
        do
        {
            last = await decide(module, CancellationToken.None).ConfigureAwait(false);
            iterations++;
        }
        while(Stopwatch.GetElapsedTime(start).TotalMilliseconds < settings.TargetMilliseconds && iterations < settings.MaxIterations);

        double elapsedMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long alloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
        ReasoningInstrumentationReport report = ReasoningInstrumentation.Snapshot();
        ReasoningInstrumentation.Disable();

        return new EngineRun(engineName, elapsedMilliseconds / iterations, (double)alloc / iterations, iterations, last, report, TimedOut: false);
    }

    /// <summary>Builds a cyclic existential blocking-stress workload: each layer is subsumed by an existential into the next, closing a cycle the completion folds by blocking.</summary>
    /// <param name="layers">The number of layer classes in the cycle.</param>
    /// <param name="branching">The number of distinct existential successors per layer (1 grows a linear forest; higher branches it).</param>
    /// <returns>The workload.</returns>
    private static Workload CyclicExistential(int layers, int branching)
    {
        List<OwlAxiom> axioms = [];
        for(int layer = 0; layer < layers; layer++)
        {
            List<OwlClassExpression> successors = [];
            for(int branch = 0; branch < branching; branch++)
            {
                int next = (layer + 1 + branch) % layers;
                successors.Add(new OwlObjectSomeValuesFrom(PropertyExpression($"r{branch}"), ClassReference($"L{next}")));
            }

            OwlClassExpression superClass = successors.Count == 1 ? successors[0] : new OwlObjectIntersectionOf(successors);
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"L{layer}"), superClass) { Origin = Origin($"cycle{layer}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("L0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=cyclic layers={layers} branch={branching}", Module(axioms));
    }

    /// <summary>Builds a SAT-stress workload: a set of pairwise-disjoint atoms with disjunctive class assertions on one individual, an exact-cover core the propositional search must resolve.</summary>
    /// <param name="atoms">The number of pairwise-disjoint atom classes.</param>
    /// <param name="clauses">The number of three-atom disjunction assertions.</param>
    /// <returns>The workload.</returns>
    private static Workload DisjunctionCore(int atoms, int clauses)
    {
        List<OwlClassExpression> atomReferences = [];
        for(int atom = 0; atom < atoms; atom++)
        {
            atomReferences.Add(ClassReference($"A{atom}"));
        }

        List<OwlAxiom> axioms = [new OwlDisjointClassesAxiom(atomReferences) { Origin = Origin("disjoint") }];

        for(int clause = 0; clause < clauses; clause++)
        {
            int x = clause % atoms;
            int y = (x + 1 + clause / atoms) % atoms;
            if(y == x)
            {
                y = (y + 1) % atoms;
            }

            int z = (y + 1) % atoms;
            if(z == x || z == y)
            {
                z = (z + 1) % atoms;
            }

            if(z == x || z == y)
            {
                z = (z + 1) % atoms;
            }

            OwlObjectUnionOf disjunction = new([atomReferences[x], atomReferences[y], atomReferences[z]]);
            axioms.Add(new OwlClassAssertionAxiom(disjunction, Named("a")) { Origin = Origin($"clause{clause}") });
        }

        return new Workload($"family=disjunction atoms={atoms} clauses={clauses}", Module(axioms));
    }

    /// <summary>Builds a classification workload: a subclass taxonomy within the 16-class subsumption cap, so the decision runs the full pairwise subsumption sweep.</summary>
    /// <param name="classes">The number of classes in the taxonomy (kept at or below the 16-class subsumption cap).</param>
    /// <returns>The workload.</returns>
    private static Workload SubclassTaxonomy(int classes)
    {
        List<OwlAxiom> axioms = [];
        for(int node = 1; node < classes; node++)
        {
            int parent = node / 2;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"C{node}"), ClassReference($"C{parent}")) { Origin = Origin($"sub{node}") });
        }

        return new Workload($"family=taxonomy classes={classes}", Module(axioms));
    }

    /// <summary>Builds an EL fast-path workload: a conjunction-and-existential TBox the EL engine saturates without tableau branching.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElConjunctiveExistential(int classes)
    {
        List<OwlAxiom> axioms = [];
        for(int node = 0; node < classes; node++)
        {
            int conjunct = (node + 1) % classes;
            int filler = (node + 2) % classes;
            OwlObjectIntersectionOf superClass = new(
                [ClassReference($"E{conjunct}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))]);
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), superClass) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el classes={classes}", Module(axioms));
    }

    /// <summary>Builds a range-bearing EL fast-path workload: an existential chain over a role that carries a property range, decided through the sound per-edge range rule.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElRangeExistential(int classes)
    {
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyRangeAxiom(PropertyExpression("r"), ClassReference("Ranged")) { Origin = Origin("range") },
        ];

        for(int node = 0; node < classes; node++)
        {
            int filler = (node + 1) % classes;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-range classes={classes}", Module(axioms));
    }

    /// <summary>Builds an inverse-range EL fast-path workload: an existential chain over a role whose inverse carries a property range, so <c>range(r⁻) = domain(r)</c> types every existential's source through the owner-independent forward reduction, decided where the inverse-blind tableau abstains.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElInverseRangeExistential(int classes)
    {
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyRangeAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), ClassReference("Sourced")) { Origin = Origin("inverse-range") },
        ];

        for(int node = 0; node < classes; node++)
        {
            int filler = (node + 1) % classes;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-inverse-range classes={classes}", Module(axioms));
    }

    /// <summary>Builds a backward-chain EL fast-path workload: a depth-N chain of superclass-position inverse existentials (<c>E0 ⊑ ∃r⁻.E1</c>, <c>E1 ⊑ ∃r⁻.E2</c>, …) inhabited from a seed individual, decided through the eager generator reduction that mints each owner's <c>r</c>-predecessor as a forward <c>g</c>-successor. The chain measures witness-population growth along the depth axis under the content-keyed shared mint, decided where the inverse-blind tableau drops every backward existential.</summary>
    /// <param name="classes">The number of classes in the backward chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElBackwardChainExistential(int classes)
    {
        List<OwlAxiom> axioms = [];

        for(int node = 0; node < classes - 1; node++)
        {
            OwlObjectSomeValuesFrom backward = new(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), ClassReference($"E{node + 1}"));
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), backward) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-backward classes={classes}", Module(axioms));
    }

    /// <summary>Builds a backward-ladder workload: the mutually recursive 2-cycle <c>P ⊑ ∃r⁻.Q</c>, <c>Q ⊑ ∃r⁻.P</c> seeded from an individual carrying <c>T0</c>, plus a depth-N ladder of left existentials over the mirror role (<c>∃r.T0 ⊑ T1</c>, …). Each rung is a distinct backward decoration consumed into the witness it fires on, so a position refines once per rung before the decorations saturate and the cycle folds again — the sequential growth axis of the decoration lattice.</summary>
    /// <param name="rungs">The number of ladder rungs, and so of distinct backward decorations.</param>
    /// <returns>The workload.</returns>
    private static Workload ElBackwardLadder(int rungs)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlSubClassOfAxiom(ClassReference("P"), new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(role), ClassReference("Q"))) { Origin = Origin("cycle-p") },
            new OwlSubClassOfAxiom(ClassReference("Q"), new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(role), ClassReference("P"))) { Origin = Origin("cycle-q") },
            new OwlClassAssertionAxiom(ClassReference("P"), Named("a")) { Origin = Origin("seed") },
            new OwlClassAssertionAxiom(ClassReference("T0"), Named("a")) { Origin = Origin("trigger") },
        ];

        for(int rung = 0; rung < rungs; rung++)
        {
            OwlObjectSomeValuesFrom consumer = new(PropertyExpression("r"), ClassReference($"T{rung}"));
            axioms.Add(new OwlSubClassOfAxiom(consumer, ClassReference($"T{rung + 1}")) { Origin = Origin($"rung{rung}") });
        }

        return new Workload($"family=el-backward-ladder rungs={rungs}", Module(axioms));
    }

    /// <summary>Builds a backward-fanout workload: one owner carrying K independent triggers on a single witness (<c>A ⊑ ∃r⁻.C</c>, <c>a : A</c>, <c>a : T1 … a : TK</c>, <c>∃r.Ti ⊑ Yi</c>). Every trigger fires on the same witness in one join, so the K decorations are consumed in one batch and one refined node is minted; an unbatched consumption explores the subset lattice of the K triggers instead — the simultaneous growth axis.</summary>
    /// <param name="triggers">The number of independent triggers on the single witness.</param>
    /// <returns>The workload.</returns>
    private static Workload ElBackwardFanout(int triggers)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlSubClassOfAxiom(ClassReference("A"), new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(role), ClassReference("C"))) { Origin = Origin("owner") },
            new OwlClassAssertionAxiom(ClassReference("A"), Named("a")) { Origin = Origin("seed") },
        ];

        for(int trigger = 0; trigger < triggers; trigger++)
        {
            axioms.Add(new OwlClassAssertionAxiom(ClassReference($"T{trigger}"), Named("a")) { Origin = Origin($"type{trigger}") });
            OwlObjectSomeValuesFrom consumer = new(PropertyExpression("r"), ClassReference($"T{trigger}"));
            axioms.Add(new OwlSubClassOfAxiom(consumer, ClassReference($"Y{trigger}")) { Origin = Origin($"consumer{trigger}") });
        }

        return new Workload($"family=el-backward-fanout triggers={triggers}", Module(axioms));
    }

    /// <summary>Builds a context-saturation ground-slice workload: a forward-universal owner (<c>Owner ⊑ ∀link.Marked</c>) over a chain of asserted <c>link</c> edges seeded from an <c>Owner</c> individual. Every engine decides it, but only the context tier mints a ground context per individual representative, so the ctx phase and ground-context counters are non-zero.</summary>
    /// <param name="individuals">The number of individuals in the asserted edge chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ContextGroundSlice(int individuals)
    {
        NamedNode link = new(Utf8Strings.From(Example + "link"));
        List<OwlAxiom> axioms =
        [
            new OwlSubClassOfAxiom(ClassReference("Owner"), new OwlObjectAllValuesFrom(PropertyExpression("link"), ClassReference("Marked"))) { Origin = Origin("universal") },
            new OwlClassAssertionAxiom(ClassReference("Owner"), Named("a0")) { Origin = Origin("seed") },
        ];

        for(int node = 0; node < individuals - 1; node++)
        {
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Named($"a{node}"), link, Named($"a{node + 1}")) { Origin = Origin($"edge{node}") });
        }

        return new Workload($"family=ctx-ground individuals={individuals}", Module(axioms));
    }

    /// <summary>Builds a context-saturation inverse-chain workload: a set of independent forward-existential / inverse-universal units (<c>Root ⊑ ∃rel.Mid</c>, <c>Mid ⊑ ∀rel⁻.Back</c>), the beyond-EL, inverse-blind-tableau reach the context tier alone decides. Context-decided, so the ctx phase and derived-clause counters are non-zero.</summary>
    /// <param name="units">The number of independent inverse-chain units.</param>
    /// <returns>The workload.</returns>
    private static Workload ContextInverseChain(int units)
    {
        NamedNode rel = new(Utf8Strings.From(Example + "rel"));
        List<OwlAxiom> axioms = [];

        for(int unit = 0; unit < units; unit++)
        {
            OwlObjectSomeValuesFrom forward = new(new OwlObjectPropertyReference(rel), ClassReference($"Mid{unit}"));
            OwlObjectAllValuesFrom inverse = new(new OwlInverseObjectProperty(rel), ClassReference($"Back{unit}"));
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"Root{unit}"), forward) { Origin = Origin($"forward{unit}") });
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"Mid{unit}"), inverse) { Origin = Origin($"inverse{unit}") });
        }

        return new Workload($"family=ctx-inverse units={units}", Module(axioms));
    }

    /// <summary>Builds a context-saturation equality/counting workload: a functional role over several existential demands (<c>Start ⊑ ∃f.T{i}</c> with <c>Functional(f)</c>) the counting tier merges onto one successor, closing <c>T0 ⊑ Marked</c> onto the merged node — the Eq-merge shape. Context-decided, so the ctx phase is non-zero.</summary>
    /// <param name="successors">The number of existential demands over the functional role.</param>
    /// <returns>The workload.</returns>
    private static Workload ContextFunctionalMerge(int successors)
    {
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, PropertyExpression("f")) { Origin = Origin("functional") },
        ];

        for(int node = 0; node < successors; node++)
        {
            axioms.Add(new OwlSubClassOfAxiom(ClassReference("Start"), new OwlObjectSomeValuesFrom(PropertyExpression("f"), ClassReference($"T{node}"))) { Origin = Origin($"succ{node}") });
        }

        axioms.Add(new OwlSubClassOfAxiom(ClassReference("T0"), ClassReference("Marked")) { Origin = Origin("marker") });
        axioms.Add(new OwlClassAssertionAxiom(ClassReference("Start"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=ctx-counting successors={successors}", Module(axioms));
    }

    /// <summary>Builds a datatype-bearing EL fast-path workload: an existential chain where every class also demands a data value in a satisfiable integer range, decided by the EL engine through the value-space checker instead of falling back to the SAT engine.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElDatatypeExistential(int classes)
    {
        OwlDataRange integer = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
        NamedNode hasValue = new(Utf8Strings.From(Example + "hasValue"));
        List<OwlAxiom> axioms = [];

        for(int node = 0; node < classes; node++)
        {
            int filler = (node + 1) % classes;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))) { Origin = Origin($"el{node}") });
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlDataSomeValuesFrom([hasValue], integer)) { Origin = Origin($"data{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-datatype classes={classes}", Module(axioms));
    }

    /// <summary>Builds a local-reflexivity EL fast-path workload: an existential chain where every class also demands a self-edge that a self-elimination reads back into a marker, decided by the EL engine through reflexive self-edges where the self-blind tableau abstains.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElSelfExistential(int classes)
    {
        List<OwlAxiom> axioms =
        [
            new OwlSubClassOfAxiom(new OwlObjectHasSelf(PropertyExpression("s")), ClassReference("Linked")) { Origin = Origin("self-elim") },
        ];

        for(int node = 0; node < classes; node++)
        {
            int filler = (node + 1) % classes;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))) { Origin = Origin($"el{node}") });
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectHasSelf(PropertyExpression("s"))) { Origin = Origin($"self{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-self classes={classes}", Module(axioms));
    }

    /// <summary>Builds a global-reflexivity EL fast-path workload: a reflexive role whose range types every node through its self-edge, over an existential chain, decided by the EL engine where the self-blind tableau drops the characteristic.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElReflexiveExistential(int classes)
    {
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, PropertyExpression("s")) { Origin = Origin("reflexive") },
            new OwlObjectPropertyRangeAxiom(PropertyExpression("s"), ClassReference("Ranged")) { Origin = Origin("range") },
        ];

        for(int node = 0; node < classes; node++)
        {
            int filler = (node + 1) % classes;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-reflexive classes={classes}", Module(axioms));
    }

    /// <summary>Builds a nominal-bearing EL fast-path workload: an existential chain plus an asserted <c>ObjectHasValue</c> edge from the seed individual to one shared individual hub, decided by the EL engine through the asserted nominal edge routed to that individual node where the nominal-blind tableau abstains.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElNominalExistential(int classes)
    {
        NamedNode hub = Named("hub");
        List<OwlAxiom> axioms = [];

        for(int node = 0; node < classes; node++)
        {
            int filler = (node + 1) % classes;
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{filler}"))) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });
        axioms.Add(new OwlClassAssertionAxiom(new OwlObjectHasValue(PropertyExpression("hasHub"), hub), Named("a")) { Origin = Origin("nominal") });

        return new Workload($"family=el-nominal classes={classes}", Module(axioms));
    }

    /// <summary>Builds a superclass-nominal EL fast-path workload: an existential chain whose deepest class carries a superclass nominal <c>∃hasHub.{hub}</c>, with the chain inhabited from the seed individual, so the carrier becomes live by forward liveness propagation along the chain and the fresh proxy then merges onto the individual node — decided by the EL engine where the nominal-blind tableau abstains.</summary>
    /// <param name="classes">The number of classes in the EL chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElSuperclassNominalExistential(int classes)
    {
        NamedNode hub = Named("hub");
        List<OwlAxiom> axioms = [];

        for(int node = 0; node < classes - 1; node++)
        {
            axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{node}"), new OwlObjectSomeValuesFrom(PropertyExpression("r"), ClassReference($"E{node + 1}"))) { Origin = Origin($"el{node}") });
        }

        axioms.Add(new OwlSubClassOfAxiom(ClassReference($"E{classes - 1}"), new OwlObjectHasValue(PropertyExpression("hasHub"), hub)) { Origin = Origin("superclass-nominal") });
        axioms.Add(new OwlClassAssertionAxiom(ClassReference("E0"), Named("a")) { Origin = Origin("seed") });

        return new Workload($"family=el-superclass-nominal classes={classes}", Module(axioms));
    }

    /// <summary>Builds a symmetric-role EL fast-path workload: a chain of asserted edges over a symmetric role whose reverse the classifier mirrors, with the role's range typing every node, decided by the EL engine where the symmetry-blind tableau drops the characteristic.</summary>
    /// <param name="individuals">The number of individuals in the asserted edge chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElSymmetricAssertedGraph(int individuals)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, PropertyExpression("r")) { Origin = Origin("symmetric") },
            new OwlObjectPropertyRangeAxiom(PropertyExpression("r"), ClassReference("Linked")) { Origin = Origin("range") },
        ];

        for(int node = 0; node < individuals - 1; node++)
        {
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Named($"a{node}"), role, Named($"a{node + 1}")) { Origin = Origin($"edge{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("Seed"), Named("a0")) { Origin = Origin("seed") });

        return new Workload($"family=el-symmetric individuals={individuals}", Module(axioms));
    }

    /// <summary>Builds an inverse-pairing EL fast-path workload: a chain of asserted edges over a role with an <c>InverseObjectProperties</c> pairing, so the classifier mirrors each as the reverse edge under the inverse role whose range types the source, decided by the EL engine where the inverse-blind tableau drops the pairing.</summary>
    /// <param name="individuals">The number of individuals in the asserted edge chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElInverseAssertedGraph(int individuals)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlInverseObjectPropertiesAxiom(PropertyExpression("r"), PropertyExpression("s")) { Origin = Origin("inverse") },
            new OwlObjectPropertyRangeAxiom(PropertyExpression("s"), ClassReference("Source")) { Origin = Origin("range") },
        ];

        for(int node = 0; node < individuals - 1; node++)
        {
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Named($"a{node}"), role, Named($"a{node + 1}")) { Origin = Origin($"edge{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("Seed"), Named("a0")) { Origin = Origin("seed") });

        return new Workload($"family=el-inverse individuals={individuals}", Module(axioms));
    }

    /// <summary>Builds a functional-role EL fast-path workload: a star of asserted successors over a functional role, all unioned onto one node by the pre-merge collapse, decided by the EL engine where the functionality-blind tableau drops the characteristic.</summary>
    /// <param name="successors">The number of asserted successors over the functional role.</param>
    /// <returns>The workload.</returns>
    private static Workload ElFunctionalStar(int successors)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, PropertyExpression("r")) { Origin = Origin("functional") },
        ];

        for(int node = 0; node < successors; node++)
        {
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Named("hub"), role, Named($"b{node}")) { Origin = Origin($"edge{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("Seed"), Named("hub")) { Origin = Origin("seed") });

        return new Workload($"family=el-functional successors={successors}", Module(axioms));
    }

    /// <summary>Builds an asymmetric-role EL fast-path workload: a one-directional chain of asserted edges over an asymmetric role, decided by the EL engine over the asserted post-merge ground graph where the characteristic-blind tableau drops the characteristic. No edge has a reverse, so the graph is consistent.</summary>
    /// <param name="individuals">The number of individuals in the asserted edge chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElAsymmetricAssertedGraph(int individuals)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, PropertyExpression("r")) { Origin = Origin("asymmetric") },
        ];

        for(int node = 0; node < individuals - 1; node++)
        {
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Named($"a{node}"), role, Named($"a{node + 1}")) { Origin = Origin($"edge{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("Seed"), Named("a0")) { Origin = Origin("seed") });

        return new Workload($"family=el-asymmetric individuals={individuals}", Module(axioms));
    }

    /// <summary>Builds an irreflexive-role EL fast-path workload: a chain of asserted non-self edges over an irreflexive role, decided by the EL engine over the asserted post-merge ground graph where the characteristic-blind tableau drops the characteristic. No edge is a self-edge, so the graph is consistent.</summary>
    /// <param name="individuals">The number of individuals in the asserted edge chain.</param>
    /// <returns>The workload.</returns>
    private static Workload ElIrreflexiveAssertedGraph(int individuals)
    {
        NamedNode role = new(Utf8Strings.From(Example + "r"));
        List<OwlAxiom> axioms =
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, PropertyExpression("r")) { Origin = Origin("irreflexive") },
        ];

        for(int node = 0; node < individuals - 1; node++)
        {
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Named($"a{node}"), role, Named($"a{node + 1}")) { Origin = Origin($"edge{node}") });
        }

        axioms.Add(new OwlClassAssertionAxiom(ClassReference("Seed"), Named("a0")) { Origin = Origin("seed") });

        return new Workload($"family=el-irreflexive individuals={individuals}", Module(axioms));
    }

    /// <summary>Wraps axioms in a module with no profile violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(List<OwlAxiom> axioms)
    {
        return new ReasoningModule(axioms, Violations: []);
    }

    /// <summary>A named-class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference ClassReference(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object-property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference PropertyExpression(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A distinct origin quad for the marker name, so each axiom anchors to its own triple.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(Named(marker), Named("p"), Named("o"), Graph: null);
    }

    /// <summary>The tunable per-invocation measurement settings.</summary>
    /// <param name="TargetMilliseconds">The wall-clock a measured run accumulates iterations toward before stopping.</param>
    /// <param name="MaxIterations">The iteration cap a measured run stops at regardless of elapsed time.</param>
    /// <param name="ProbeTimeoutMilliseconds">The per-decision probe timeout; a single decision exceeding it is reported as timed out, and it also bounds a single whole-ontology classification.</param>
    /// <param name="Files">The corpus files named on the command line, in the order given; empty when none was named, which runs the synthetic ladder instead.</param>
    private sealed record SoakSettings(double TargetMilliseconds, int MaxIterations, int ProbeTimeoutMilliseconds, IReadOnlyList<string> Files);

    /// <summary>One synthetic workload: a descriptive name and the module to decide.</summary>
    /// <param name="Name">The descriptive name, carrying the family and its parameters.</param>
    /// <param name="Module">The module the engines decide.</param>
    private sealed record Workload(string Name, ReasoningModule Module);

    /// <summary>One engine's measured run over a workload, or a timed-out marker.</summary>
    /// <param name="Engine">The engine label.</param>
    /// <param name="WallMillisecondsPerDecision">The mean wall-clock per decision.</param>
    /// <param name="AllocBytesPerDecision">The mean managed allocation per decision.</param>
    /// <param name="Iterations">The number of measured decisions.</param>
    /// <param name="Decision">The last decision, read for its verdict and counters, or <c>null</c> when the run timed out.</param>
    /// <param name="Report">The phase instrumentation accumulated across the measured decisions, or <c>null</c> when the run timed out.</param>
    /// <param name="TimedOut">Whether a single probe decision exceeded the timeout, so the measured loop was skipped.</param>
    private sealed record EngineRun(
        string Engine,
        double WallMillisecondsPerDecision,
        double AllocBytesPerDecision,
        int Iterations,
        ModuleDecision? Decision,
        ReasoningInstrumentationReport? Report,
        bool TimedOut)
    {
        /// <summary>A timed-out run: the probe decision did not complete within the timeout, so no measurement was taken.</summary>
        /// <param name="engine">The engine label.</param>
        /// <returns>The timed-out run.</returns>
        public static EngineRun CreateTimedOut(string engine)
        {
            return new EngineRun(engine, WallMillisecondsPerDecision: 0.0, AllocBytesPerDecision: 0.0, Iterations: 0, Decision: null, Report: null, TimedOut: true);
        }
    }
}
