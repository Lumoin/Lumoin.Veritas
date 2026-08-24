using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The V-node cost-face harness: it measures,
/// on a functional-heavy synthetic ladder, the saturation work and wall time the
/// two equality lowerings spend — the published
/// <see cref="EqualityLowering.GeneralClause"/> (a same-owner functional
/// successor merged after the fact through the DL4 counting clause and the Eq
/// rule) against <see cref="EqualityLowering.SuccessorSharing"/> (the V-node
/// reuse of one successor symbol per functional directioned role, merging by
/// construction). Both lowerings are verdict- and subsumption-identical (the
/// correctness face is the soundness battery run under both modes); this harness
/// is the cost differential that rules which is the default and which stays
/// selectable.
/// </summary>
/// <remarks>
/// <para>
/// It is measurement scaffolding, the sibling of
/// <see cref="DelegationRateHarness"/>, not a correctness gate on the normal
/// suite's wall time — so it runs only when the <c>VERITAS_VNODE_MEASURE</c>
/// environment variable names an absolute output file, and otherwise passes
/// without measuring. The correctness differential it does assert — the two
/// lowerings agree on consistency for every ladder module — is a cheap belt held
/// inside the measured run, the strong correctness face living in the soundness
/// battery.
/// </para>
/// <para>
/// The ladder is deterministic: k functional roles × m existentials per owner ×
/// depth d ∈ {1,2,3}. Each owner class carries, per functional role, m
/// superclass existentials whose fillers fold into the next level's owner, so a
/// functional role's m same-owner successors are exactly what the module-level
/// ≤1 forces to coincide — the shape sharing collapses to one symbol and the
/// general clause merges pairwise. Run it in Release in a contiguous block with
/// no concurrent builds or suites for citable wall-time numbers.
/// </para>
/// </remarks>
[TestClass]
internal sealed class VNodeMeasurementHarness
{
    /// <summary>The environment variable naming the absolute output path; unset means the harness passes without measuring.</summary>
    private const string OutputPathVariable = "VERITAS_VNODE_MEASURE";

    /// <summary>The IRI prefix the synthetic classes and roles live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The number of timed runs per module per mode; the reported wall time is the median, discarding first-touch noise.</summary>
    private const int RunsPerMode = 7;

    /// <summary>The functional-role counts (k) the ladder sweeps.</summary>
    private static int[] RoleCounts { get; } = [1, 2];

    /// <summary>The per-owner existential counts (m) over each functional role the ladder sweeps.</summary>
    private static int[] ExistentialCounts { get; } = [2, 3];

    /// <summary>The ladder depths (d) the sweep spans.</summary>
    private static int[] Depths { get; } = [1, 2, 3];

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Measures the general-clause and successor-sharing lowerings across the
    /// functional-heavy ladder, writes the per-mode statistics and wall-median
    /// report to the configured path, and asserts the two lowerings agree on
    /// consistency for every ladder module (the cheap correctness belt).
    /// </summary>
    [TestMethod]
    public void MeasureVNodeCost()
    {
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            //Opt-in measurement scaffolding, not a correctness gate: with no output
            //path configured the harness has nothing to do and the test passes
            //without measuring. Set VERITAS_VNODE_MEASURE to run it.
            TestContext.WriteLine($"Skipping the V-node cost harness: set {OutputPathVariable} to an absolute output path to run it.");

            return;
        }

        List<(string Name, ReasoningModule Module)> ladder = BuildFunctionalLadder();

        //Warm both lowerings on the first module so the timed loop excludes their
        //first-touch JIT and class-load cost.
        Warm(ladder[0].Module);

        StringBuilder report = new();
        report.AppendLine("V-node cost-face harness: general-clause vs successor-sharing on a functional-heavy ladder (k functional roles x m existentials/owner x depth d).");
        report.AppendLine(CultureInfo.InvariantCulture, $"Configuration: {BuildConfiguration()}. Wall = median of {RunsPerMode} runs (ms). Statistics are deterministic per (module, mode).");
        report.AppendLine();
        report.AppendLine("module | mode | decided | consistent | Hyper | Eq | Ineq | ClausesDerived | contexts | MaxCtxClauses | wall_ms");
        report.AppendLine("---|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:");

        MeasuredTotals general = new();
        MeasuredTotals sharing = new();
        int disagreements = 0;

        foreach((string name, ReasoningModule module) in ladder)
        {
            Measured generalRow = Measure(module, EqualityLowering.GeneralClause);
            Measured sharingRow = Measure(module, EqualityLowering.SuccessorSharing);

            if(generalRow.Consistent != sharingRow.Consistent)
            {
                disagreements++;
            }

            AppendRow(report, name, "general", generalRow);
            AppendRow(report, name, "sharing", sharingRow);

            general.Add(generalRow);
            sharing.Add(sharingRow);
        }

        report.AppendLine();
        report.AppendLine("== totals across the ladder ==");
        report.AppendLine("mode | Hyper | Eq | Ineq | ClausesDerived | contexts | MaxCtxClauses(sum) | wall_ms(sum)");
        report.AppendLine("---|---:|---:|---:|---:|---:|---:|---:");
        AppendTotals(report, "general", general);
        AppendTotals(report, "sharing", sharing);
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Eq delta (general - sharing) = {general.Eq - sharing.Eq}; ClausesDerived delta = {general.ClausesDerived - sharing.ClausesDerived}; wall delta = {(general.WallMilliseconds - sharing.WallMilliseconds).ToString("F2", CultureInfo.InvariantCulture)} ms.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Consistency disagreements between the two lowerings = {disagreements} (must be 0).");

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine(report.ToString());

        Assert.AreEqual(0, disagreements, "The general-clause and successor-sharing lowerings disagreed on consistency for at least one ladder module — the sharing mode is unsound and lands selectable-but-flagged.");
    }

    /// <summary>One module-and-mode measurement: whether the context engine decided it, its consistency, the saturation counters, and the median wall time.</summary>
    /// <param name="Decided">Whether the context engine produced the verdict.</param>
    /// <param name="Consistent">The consistency verdict.</param>
    /// <param name="Hyper">The Hyper-rule applications.</param>
    /// <param name="Eq">The Eq-rule applications.</param>
    /// <param name="Ineq">The Ineq-rule applications.</param>
    /// <param name="ClausesDerived">The context clauses derived.</param>
    /// <param name="Contexts">The contexts created plus reused.</param>
    /// <param name="MaxContextClauses">The largest single context's clause count.</param>
    /// <param name="WallMilliseconds">The median wall time in milliseconds.</param>
    private readonly record struct Measured(
        bool Decided,
        bool Consistent,
        long Hyper,
        long Eq,
        long Ineq,
        int ClausesDerived,
        int Contexts,
        int MaxContextClauses,
        double WallMilliseconds);

    /// <summary>The running totals a mode accumulates across the ladder.</summary>
    private sealed class MeasuredTotals
    {
        /// <summary>The summed Hyper-rule applications.</summary>
        public long Hyper { get; private set; }

        /// <summary>The summed Eq-rule applications.</summary>
        public long Eq { get; private set; }

        /// <summary>The summed Ineq-rule applications.</summary>
        public long Ineq { get; private set; }

        /// <summary>The summed derived clauses.</summary>
        public long ClausesDerived { get; private set; }

        /// <summary>The summed contexts created plus reused.</summary>
        public long Contexts { get; private set; }

        /// <summary>The summed per-module largest context clause counts.</summary>
        public long MaxContextClauses { get; private set; }

        /// <summary>The summed median wall time in milliseconds.</summary>
        public double WallMilliseconds { get; private set; }

        /// <summary>Adds one measured row into the totals.</summary>
        /// <param name="row">The measured row.</param>
        public void Add(Measured row)
        {
            Hyper += row.Hyper;
            Eq += row.Eq;
            Ineq += row.Ineq;
            ClausesDerived += row.ClausesDerived;
            Contexts += row.Contexts;
            MaxContextClauses += row.MaxContextClauses;
            WallMilliseconds += row.WallMilliseconds;
        }
    }

    /// <summary>Decides the module under a mode over several runs, reading the deterministic statistics once and returning the median wall time.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="lowering">The equality lowering to measure.</param>
    /// <returns>The measurement.</returns>
    private Measured Measure(ReasoningModule module, EqualityLowering lowering)
    {
        List<double> walls = new(RunsPerMode);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, lowering, TestContext.CancellationToken);
        for(int run = 0; run < RunsPerMode; run++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            decision = ContextSaturationModuleReasoner.DecideModule(module, lowering, TestContext.CancellationToken);
            stopwatch.Stop();
            walls.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        return new Measured(
            totals.ContextDecided,
            decision.Verdict!.IsConsistent,
            totals.HyperApplications,
            totals.EqApplications,
            totals.IneqApplications,
            totals.ClausesDerived,
            totals.ContextsCreated + totals.ContextsReused,
            totals.MaxContextClauses,
            Median(walls));
    }

    /// <summary>Decides the module once under each lowering to warm the JIT before the timed loop.</summary>
    /// <param name="module">The module to warm on.</param>
    private void Warm(ReasoningModule module)
    {
        ContextSaturationModuleReasoner.DecideModule(module, EqualityLowering.GeneralClause, TestContext.CancellationToken);
        ContextSaturationModuleReasoner.DecideModule(module, EqualityLowering.SuccessorSharing, TestContext.CancellationToken);
    }

    /// <summary>Appends one measured row to the report.</summary>
    /// <param name="report">The report the row appends to.</param>
    /// <param name="name">The module's label.</param>
    /// <param name="mode">The mode's short label.</param>
    /// <param name="row">The measured row.</param>
    private static void AppendRow(StringBuilder report, string name, string mode, Measured row)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"{name} | {mode} | {(row.Decided ? "yes" : "NO")} | {(row.Consistent ? "yes" : "NO")} | {row.Hyper} | {row.Eq} | {row.Ineq} | {row.ClausesDerived} | {row.Contexts} | {row.MaxContextClauses} | {row.WallMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Appends one mode's totals to the report.</summary>
    /// <param name="report">The report the totals append to.</param>
    /// <param name="mode">The mode's short label.</param>
    /// <param name="totals">The mode's totals.</param>
    private static void AppendTotals(StringBuilder report, string mode, MeasuredTotals totals)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"{mode} | {totals.Hyper} | {totals.Eq} | {totals.Ineq} | {totals.ClausesDerived} | {totals.Contexts} | {totals.MaxContextClauses} | {totals.WallMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Builds the deterministic functional-heavy ladder: every (k, m, d) combination as one labelled module.</summary>
    /// <returns>The ladder's labelled modules.</returns>
    private static List<(string Name, ReasoningModule Module)> BuildFunctionalLadder()
    {
        List<(string Name, ReasoningModule Module)> ladder = [];
        foreach(int k in RoleCounts)
        {
            foreach(int m in ExistentialCounts)
            {
                foreach(int d in Depths)
                {
                    ladder.Add(($"k{k}-m{m}-d{d}", BuildLadderModule(k, m, d)));
                }
            }
        }

        return ladder;
    }

    /// <summary>
    /// Builds one ladder module: k functional roles, and at each of d depth levels
    /// an owner class carrying m superclass existentials over each functional role,
    /// every filler folded into the next level's owner. The m same-owner successors
    /// over one functional role are what the module-level ≤1 forces to coincide —
    /// the merge the general clause reaches pairwise through DL4 + Eq and successor
    /// sharing reaches by construction. Consistent (no disjointness, no bottom), so
    /// both lowerings decide it consistent and the differential is pure cost.
    /// </summary>
    /// <param name="roleCount">The number of functional roles (k).</param>
    /// <param name="existentialCount">The per-owner existentials over each functional role (m).</param>
    /// <param name="depth">The ladder depth (d).</param>
    /// <returns>The module.</returns>
    private static ReasoningModule BuildLadderModule(int roleCount, int existentialCount, int depth)
    {
        List<OwlAxiom> axioms = [];
        for(int i = 0; i < roleCount; i++)
        {
            axioms.Add(Functional($"r{i}"));
        }

        for(int level = 0; level < depth; level++)
        {
            string owner = level == 0 ? "Owner" : $"L{level}";
            string next = $"L{level + 1}";
            for(int i = 0; i < roleCount; i++)
            {
                for(int j = 0; j < existentialCount; j++)
                {
                    string filler = $"F{level}_{i}_{j}";
                    axioms.Add(SubClassOf(Class(owner), Some($"r{i}", Class(filler))));
                    axioms.Add(SubClassOf(Class(filler), Class(next)));
                }
            }
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The median of the values, or zero when there are none; the mean of the two middle values for an even count.</summary>
    /// <param name="values">The values, sorted in place.</param>
    /// <returns>The median.</returns>
    private static double Median(List<double> values)
    {
        if(values.Count == 0)
        {
            return 0.0;
        }

        values.Sort();
        int middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;
    }

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property reference.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A subclass inclusion carrying a distinct origin.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(property)) { Origin = Origin("functional") };
    }

    /// <summary>A distinct origin quad for the marker name, so each axiom carries an origin.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
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
