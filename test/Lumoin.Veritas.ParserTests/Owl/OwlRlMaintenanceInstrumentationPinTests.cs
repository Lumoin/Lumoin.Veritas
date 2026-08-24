using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Pins for the maintained OWL 2 RL closure's opt-in phase-attribution sink
/// (<see cref="OwlRlMaintenanceInstrumentation"/>). The pins assert only
/// deterministic region counts - never ticks or milliseconds, which carry
/// machine noise and would make the pins flaky - over the fixed modules the
/// counts are hand-derivable from: the default-off path records nothing, an
/// enabled Apply attributes the pipeline phases with the exact per-phase counts
/// the module dictates, and enabling measurement never perturbs the verdict or
/// the statistics of the run it measures.
/// </summary>
/// <remarks>
/// Every test that enables measurement disables and resets the calling thread's
/// accumulators in a <c>finally</c>, so the thread-local state never leaks into
/// a later test on the same thread - the default-off pin depends on that
/// hygiene.
/// </remarks>
[TestClass]
internal sealed class OwlRlMaintenanceInstrumentationPinTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every maintenance phase, so the default-off pin can assert each of them records nothing.</summary>
    private static readonly OwlRlMaintenancePhase[] AllPhases =
    [
        OwlRlMaintenancePhase.OverdeleteGrouping,
        OwlRlMaintenancePhase.OwnerMarking,
        OwlRlMaintenancePhase.OverdeleteEquality,
        OwlRlMaintenancePhase.OverdeleteProperties,
        OwlRlMaintenancePhase.OverdeleteCharacteristicData,
        OwlRlMaintenancePhase.OverdeleteClasses,
        OwlRlMaintenancePhase.OverdeleteMaxPairs,
        OwlRlMaintenancePhase.OverdeleteClassAxioms,
        OwlRlMaintenancePhase.OverdeleteSchema,
        OwlRlMaintenancePhase.PhysicalRemoval,
        OwlRlMaintenancePhase.BaseAdmission,
        OwlRlMaintenancePhase.Rederive,
        OwlRlMaintenancePhase.RederiveEqRep,
        OwlRlMaintenancePhase.OwnerReFire,
        OwlRlMaintenancePhase.InsertRounds,
    ];

    /// <summary>
    /// Default-off neutrality of the sink itself. On a thread that never enabled
    /// measurement, a retract-bearing Apply that genuinely marks and rederives
    /// facts leaves every phase count at zero - a disabled site's
    /// <see cref="OwlRlMaintenanceInstrumentation.Begin"/> returns zero and its
    /// <see cref="OwlRlMaintenanceInstrumentation.End"/> is a no-op, so nothing
    /// accumulates.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B), type(x,A)}</c> derives <c>type(x,B)</c> by
    /// cax-sco. Retracting <c>type(x,A)</c> marks the base fact and its derived
    /// consequence, runs the overdelete fixpoint, the physical removal, the
    /// rederive loop, and the insert rounds - every instrumented site executes.
    /// With measurement disabled the snapshot must nonetheless show all zeros.
    /// </remarks>
    [TestMethod]
    public void DefaultOffApplyRecordsNoPhaseCounts()
    {
        Assert.IsFalse(OwlRlMaintenanceInstrumentation.Enabled, "This pin asserts the default-off path, so the calling thread must not have measurement enabled.");

        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);

        OwlRlMaintainedClosure engine = new([aSubB, xIsA], terms, cancellationToken: TestContext.CancellationToken);
        _ = engine.Apply([], [xIsA], TestContext.CancellationToken);

        OwlRlMaintenanceInstrumentationReport report = OwlRlMaintenanceInstrumentation.Snapshot();
        foreach(OwlRlMaintenancePhase phase in AllPhases)
        {
            Assert.AreEqual(0L, report.Count(phase), $"A disabled Apply must record no regions, but phase {phase} counted some.");
        }
    }

    /// <summary>
    /// Attribution of the pipeline phases. On the fixed module an enabled Apply
    /// of a pure retract attributes the maintenance phases with the exact
    /// per-phase counts the module dictates: the overdelete fixpoint groups at
    /// least one round, the rederive loop checks exactly every marked fact
    /// (all absent after physical removal, since a pure retract admits nothing),
    /// physical removal runs exactly once, and at least one insert round runs.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B), type(x,A)}</c> derives <c>type(x,B)</c>.
    /// Retracting <c>type(x,A)</c> seeds the overdelete frontier with the base
    /// fact and marks its cax-sco consequence <c>type(x,B)</c>, so the marked
    /// set is non-empty and includes a derived fact. Because the op adds
    /// nothing, physical removal empties every marked fact from the closure and
    /// the admission loop admits nothing, so every marked fact reaches the
    /// rederivability check: <c>Count(Rederive)</c> equals
    /// <see cref="OwlRlMaintenanceStatistics.OverdeleteMarked"/>. The module
    /// touches no restriction, negative-property-assertion, or list owner, so
    /// the owner re-fire is consistent and the semi-naive insert loop runs.
    /// </remarks>
    [TestMethod]
    public void EnabledApplyAttributesMaintenancePhaseCounts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);

        OwlRlMaintainedClosure engine = new([aSubB, xIsA], terms, cancellationToken: TestContext.CancellationToken);

        OwlRlMaintenanceInstrumentationReport report;
        OwlRlMaintenanceInstrumentation.Enable();
        try
        {
            _ = engine.Apply([], [xIsA], TestContext.CancellationToken);
            report = OwlRlMaintenanceInstrumentation.Snapshot();
        }
        finally
        {
            OwlRlMaintenanceInstrumentation.Disable();
            OwlRlMaintenanceInstrumentation.Reset();
        }

        OwlRlMaintenanceStatistics statistics = engine.Statistics;
        Assert.IsGreaterThanOrEqualTo(1, statistics.OverdeleteMarked, "The retract must mark at least one fact for the attribution to be meaningful.");
        Assert.IsGreaterThanOrEqualTo(1L, report.Count(OwlRlMaintenancePhase.OverdeleteGrouping), "The overdelete fixpoint must group at least one round.");
        Assert.AreEqual((long)statistics.OverdeleteMarked, report.Count(OwlRlMaintenancePhase.Rederive), "A pure retract admits nothing, so every marked fact is absent after removal and must reach the rederivability check exactly once.");
        Assert.IsGreaterThanOrEqualTo(1L, report.Count(OwlRlMaintenancePhase.InsertRounds), "At least one semi-naive insert round must run.");
        Assert.AreEqual(1L, report.Count(OwlRlMaintenancePhase.PhysicalRemoval), "Physical removal is a single region per Apply.");
    }

    /// <summary>
    /// Measurement neutrality. Two independent closures over the same base apply
    /// the same retract - one with measurement enabled, one with it disabled -
    /// and produce equal statistics and set-equal derived sets: enabling the
    /// sink perturbs neither the verdict nor the deterministic proxies of the
    /// run it measures.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B), type(x,A)}</c>; both twins retract
    /// <c>type(x,A)</c>. The enabled twin's Apply runs under an
    /// <see cref="OwlRlMaintenanceInstrumentation.Enable"/> /
    /// <see cref="OwlRlMaintenanceInstrumentation.Disable"/> window; the disabled
    /// twin's Apply runs with the sink off. Their
    /// <see cref="OwlRlMaintainedClosure.Statistics"/> and derived sets must
    /// agree.
    /// </remarks>
    [TestMethod]
    public void EnabledAndDisabledTwinsAgree()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);

        OwlRlMaintainedClosure enabledTwin = new([aSubB, xIsA], terms, cancellationToken: TestContext.CancellationToken);
        OwlRlMaintainedClosure disabledTwin = new([aSubB, xIsA], terms, cancellationToken: TestContext.CancellationToken);

        HashSet<EncodedTriple> enabledDerived;
        OwlRlMaintenanceInstrumentation.Enable();
        try
        {
            OwlRlResult enabledResult = enabledTwin.Apply([], [xIsA], TestContext.CancellationToken);
            enabledDerived = [.. enabledResult.Derived];
        }
        finally
        {
            OwlRlMaintenanceInstrumentation.Disable();
            OwlRlMaintenanceInstrumentation.Reset();
        }

        OwlRlResult disabledResult = disabledTwin.Apply([], [xIsA], TestContext.CancellationToken);
        HashSet<EncodedTriple> disabledDerived = [.. disabledResult.Derived];

        Assert.AreEqual(disabledTwin.Statistics, enabledTwin.Statistics, "Enabling measurement must not change the pass statistics.");
        Assert.IsTrue(enabledDerived.SetEquals(disabledDerived), "Enabling measurement must not change the derived set.");
    }
}
