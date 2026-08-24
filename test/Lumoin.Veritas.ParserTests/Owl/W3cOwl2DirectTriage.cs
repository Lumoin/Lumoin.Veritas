using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The decision-time triage harness for the four Direct-corpus instances the
/// snapshot tableau cannot decide within its practical reach: it times the
/// premise consistency decision under both engines, each capped by the same
/// per-instance budget, and writes the comparison table to the configured
/// output path. It is measurement scaffolding, not a correctness gate — the
/// correctness gate is the parametrized corpus arms in
/// <see cref="W3cOwl2DirectTests"/> — so it runs only when the
/// <c>VERITAS_DIRECT_TRIAGE</c> environment variable names an output file,
/// staying out of the normal suite's wall time. Run it in Release in a
/// contiguous block with no concurrent builds or suites for citable numbers.
/// </summary>
[TestClass]
internal sealed class W3cOwl2DirectTriage
{
    /// <summary>The environment variable naming the absolute output path; unset means the harness skips.</summary>
    private const string OutputPathVariable = "VERITAS_DIRECT_TRIAGE";

    /// <summary>The per-engine, per-instance decision budget; a decision exceeding it records as budget-exceeded rather than wedging the run.</summary>
    private static readonly TimeSpan DecisionBudget = TimeSpan.FromSeconds(45);

    /// <summary>The instances under triage, paired with the manifest arm they live in: the four recorded snapshot practical-reach gaps.</summary>
    private static (string Suite, string Identifier)[] Instances { get; } =
    [
        ("approved", "WebOnt-description-logic-040"),
        ("approved", "WebOnt-description-logic-201"),
        ("approved", "WebOnt-description-logic-208"),
        ("approved", "WebOnt-description-logic-209"),
    ];

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Times the snapshot-engine and SAT-engine consistency decision of each
    /// triaged instance's premise under the per-instance budget and writes
    /// the comparison table to the configured path.
    /// </summary>
    [TestMethod]
    public void MeasurePracticalReachInstances()
    {
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            //Opt-in measurement scaffolding, not a correctness gate: with no
            //output path configured the harness has nothing to do and the test
            //passes without measuring. Set VERITAS_DIRECT_TRIAGE to run it.
            TestContext.WriteLine($"Skipping the triage harness: set {OutputPathVariable} to an absolute output path to run it.");

            return;
        }

        StringBuilder table = new();
        table.AppendLine(CultureInfo.InvariantCulture, $"Direct-corpus practical-reach triage — snapshot vs SAT-backed consistency decision.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Configuration: {BuildConfiguration()}. Premise-alone DecideConsistency, single run per engine, warmup excluded.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Budget: {DecisionBudget.TotalSeconds:F0} s per instance per engine (exceeding it records BUDGET-EXCEEDED). Box load: record at run time.");
        table.AppendLine();
        table.AppendLine("test id | snapshot engine (ms) | SAT engine (ms) | ratio (snapshot/SAT)");
        table.AppendLine("---|---:|---:|---:");

        foreach((string suite, string identifier) in Instances)
        {
            OwlOntologyDocument premise = LoadPremise(suite, identifier);

            //Warm the shared code paths so the timed decisions exclude the
            //first-touch JIT and class-load cost; the SAT engine warms on a
            //trivial module to avoid paying the instance's own decision twice.
            SatTableauModuleReasoner.DecideConsistency(new ReasoningModule([], Violations: []), cancellationToken: TestContext.CancellationToken);

            (string snapshotCell, double? snapshotMilliseconds) = TimeDecision(premise, DecideSnapshot);
            (string satCell, double? satMilliseconds) = TimeDecision(premise, DecideSatBacked);

            string ratioCell = snapshotMilliseconds is double snapshotMs && satMilliseconds is double satMs && satMs > 0.0
                ? "×" + (snapshotMs / satMs).ToString("F1", CultureInfo.InvariantCulture)
                : ">budget";

            table.AppendLine(CultureInfo.InvariantCulture, $"{identifier} | {snapshotCell} | {satCell} | {ratioCell}");
        }

        File.WriteAllText(outputPath, table.ToString());
        TestContext.WriteLine(table.ToString());
    }

    /// <summary>One engine's budget-aware consistency decision over a module.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token the decision honours.</param>
    /// <returns>The verdict.</returns>
    private delegate ModuleVerdict ConsistencyDecision(ReasoningModule module, CancellationToken cancellationToken);

    /// <summary>The snapshot engine's consistency-only entry as a <see cref="ConsistencyDecision"/>.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The verdict.</returns>
    private static ModuleVerdict DecideSnapshot(ReasoningModule module, CancellationToken cancellationToken)
    {
        return AlcModuleReasoner.DecideConsistency(module, cancellationToken);
    }

    /// <summary>The SAT-backed engine's consistency-only entry as a <see cref="ConsistencyDecision"/>.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">The budget token.</param>
    /// <returns>The verdict.</returns>
    private static ModuleVerdict DecideSatBacked(ReasoningModule module, CancellationToken cancellationToken)
    {
        return SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: cancellationToken);
    }

    /// <summary>Times an engine's premise consistency decision under the budget, returning the formatted cell and the milliseconds, or a budget-exceeded marker.</summary>
    /// <param name="premise">The premise to decide.</param>
    /// <param name="decision">The engine's decision entry.</param>
    /// <returns>The formatted cell and the elapsed milliseconds when within budget.</returns>
    private static (string Cell, double? Milliseconds) TimeDecision(OwlOntologyDocument premise, ConsistencyDecision decision)
    {
        using CancellationTokenSource budget = new(DecisionBudget);
        ReasoningModule module = new([.. premise.Axioms], Violations: []);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ModuleVerdict verdict = decision(module, budget.Token);
            stopwatch.Stop();
            Assert.IsEmpty(verdict.UnsupportedConstructs, "The triaged instance sits inside the engine's fragment.");
            double milliseconds = stopwatch.Elapsed.TotalMilliseconds;

            return (milliseconds.ToString("F1", CultureInfo.InvariantCulture), milliseconds);
        }
        catch(OperationCanceledException)
        {
            stopwatch.Stop();

            return ($"BUDGET-EXCEEDED>{DecisionBudget.TotalSeconds:F0}s", null);
        }
    }

    /// <summary>Loads, import-resolves, and maps the named instance's premise the same way the corpus runner does.</summary>
    /// <param name="suite">The manifest arm.</param>
    /// <param name="identifier">The test identifier.</param>
    /// <returns>The mapped premise.</returns>
    private static OwlOntologyDocument LoadPremise(string suite, string identifier)
    {
        ImmutableArray<Owl2TestCase> cases = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", suite, "all.rdf"));
        Owl2TestCase testCase = FindCase(cases, identifier);

        DiagnosticBag diagnostics = new();
        List<Quad> quads =
        [
            .. RdfXmlReader.Read(testCase.RdfXmlPremise!.Value.Memory, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri)),
        ];
        Assert.IsFalse(diagnostics.HasErrors, $"{identifier}: the premise did not parse as RDF/XML.");

        quads = Owl2ImportResolver.Expand(testCase, quads);
        OwlOntologyDocument premise = OwlRdfMapper.Map(quads);
        Assert.IsFalse(premise.Diagnostics.HasErrors, $"{identifier}: the premise did not map to structural form.");

        return premise;
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
