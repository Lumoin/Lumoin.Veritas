using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Hand-derived pins for <see cref="ReasoningMaintenance"/>: the durable
/// per-engine maintenance object of the reasoned mutable engine. Each pin drives
/// the open-time build and one or more maintained commits, and asserts the
/// served-store delta against an INDEPENDENT snapshot oracle — the served set of
/// a generation is its asserted base united with the naive RL closure's derived
/// set when consistent, and the asserted base alone when inconsistent — so every
/// commit's applied served delta equals <c>setdiff(new served target, previous
/// served store)</c>, exactly the wiring's universal invariant. The pins span the
/// consistent and inconsistent opens, plain append and retract, the two composed
/// overlap shapes (a prior-overlay fact this commit asserts at a withdrawal, and
/// a base-removed fact that stays derivable at a return), consecutive inconsistent
/// commits, the wholesale-replace and discard-recovery rebuilds, the beyond-RL
/// verdict decay after both a fragment-relative decision and a budget abstention,
/// and the schema-touching floor-facts refresh. A disagreement between the oracle
/// and the recorded delta is a finding to surface, never an expectation to fit.
/// </summary>
[TestClass]
internal sealed class ReasoningMaintenanceTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Open over a consistent base seeds the served overlay and the folded
    /// verdict. Base <c>{subClassOf(A,B), type(x,A)}</c> derives <c>type(x,B)</c>;
    /// the initial state reports the overlay on, a consistent verdict, a positive
    /// derived count, and the derived snapshot as its served-store seed additions.
    /// </summary>
    [TestMethod]
    public async Task OpenOverConsistentBaseSeedsOverlayAndVerdict()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        HashSet<EncodedTriple> baseTriples = [aSubB, xIsA];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseTriples, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningMaintainedCommit initial = maintenance.InitialState;
        Assert.IsTrue(initial.OverlayOn, "A consistent base leaves the overlay on.");
        Assert.IsTrue(initial.IsConsistent);
        Assert.IsNull(initial.InconsistencyRule);
        Assert.IsGreaterThan(0, initial.DerivedCount, "The base derives at least type(x,B).");
        Assert.IsTrue(initial.DetectedProfiles.HasFlag(OwlProfiles.Rl));

        HashSet<EncodedTriple> seed = [.. initial.ServedAdditions];
        Assert.Contains(xIsB, seed, "The served seed carries the cax-sco derivation.");
        Assert.IsTrue(seed.SetEquals(ExpectedDerived(baseTriples, terms, oracle)), "The served seed equals the naive derived set.");
    }

    /// <summary>
    /// Open over an inconsistent base withdraws the overlay and serves
    /// asserted-only. Base <c>{disjointWith(C,D), type(x,C), type(x,D)}</c> fires
    /// the cax-dw falsity; the initial state reports the overlay off, an
    /// inconsistent verdict with the rule named, and no served-seed additions.
    /// </summary>
    [TestMethod]
    public async Task OpenOverInconsistentBaseWithdrawsOverlay()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple disjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);

        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            new HashSet<EncodedTriple> { disjoint, xIsC, xIsD }, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningMaintainedCommit initial = maintenance.InitialState;
        Assert.IsFalse(initial.OverlayOn, "An inconsistent base withdraws the overlay.");
        Assert.IsFalse(initial.IsConsistent);
        Assert.AreEqual(EntailmentRules.CaxDw, initial.InconsistencyRule, "The cax-dw falsity is named.");
        Assert.IsEmpty(initial.ServedAdditions, "A withdrawn overlay seeds no derived additions.");
    }

    /// <summary>
    /// A plain append commit's served delta equals the setdiff of the served
    /// snapshots. Over <c>{subClassOf(A,B), type(x,A)}</c>, adding
    /// <c>type(y,A)</c> serves <c>type(y,A)</c> and its cax-sco derivation
    /// <c>type(y,B)</c>, removes nothing, and reports the incremental mode.
    /// </summary>
    [TestMethod]
    public async Task AppendCommitServedDeltaEqualsSetdiff()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple yIsA = OwlRlBatteryHelpers.Triple(y, terms.Type, classA);
        EncodedTriple yIsB = OwlRlBatteryHelpers.Triple(y, terms.Type, classB);

        HashSet<EncodedTriple> baseBefore = [aSubB, xIsA];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseBefore, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        HashSet<EncodedTriple> baseAfter = [aSubB, xIsA, yIsA];
        HypertrieGraphStore store = await BuildStoreAsync(baseAfter).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [yIsA], [], store, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.AreEqual(ReasoningMaintenanceMode.Incremental, commit.Statistics.Mode, "A consistent append runs the incremental pipeline.");
        Assert.IsFalse(commit.RebuildClass);
        Assert.IsTrue(commit.OverlayOn);
        AssertServedDeltaEqualsSetdiff(commit, ExpectedServed(baseBefore, terms, oracle), ExpectedServed(baseAfter, terms, oracle), "append");
        HashSet<EncodedTriple> added = [.. commit.ServedAdditions];
        Assert.Contains(yIsA, added, "The added base fact enters the served store.");
        Assert.Contains(yIsB, added, "The cax-sco derivation enters the served store.");
    }

    /// <summary>
    /// A retract cascade commit's served delta equals the setdiff of the served
    /// snapshots. Over <c>{subClassOf(A,B), type(x,A), type(y,A)}</c>, retracting
    /// <c>type(y,A)</c> removes it and its derivation <c>type(y,B)</c> from the
    /// served store, adds nothing, and stays incremental.
    /// </summary>
    [TestMethod]
    public async Task RetractCommitServedDeltaEqualsSetdiff()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple yIsA = OwlRlBatteryHelpers.Triple(y, terms.Type, classA);
        EncodedTriple yIsB = OwlRlBatteryHelpers.Triple(y, terms.Type, classB);

        HashSet<EncodedTriple> baseBefore = [aSubB, xIsA, yIsA];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseBefore, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        HashSet<EncodedTriple> baseAfter = [aSubB, xIsA];
        HypertrieGraphStore store = await BuildStoreAsync(baseAfter).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [], [yIsA], store, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.AreEqual(ReasoningMaintenanceMode.Incremental, commit.Statistics.Mode);
        AssertServedDeltaEqualsSetdiff(commit, ExpectedServed(baseBefore, terms, oracle), ExpectedServed(baseAfter, terms, oracle), "retract");
        HashSet<EncodedTriple> removed = [.. commit.ServedRemovals];
        Assert.Contains(yIsA, removed, "The retracted base fact leaves the served store.");
        Assert.Contains(yIsB, removed, "The torn-down derivation leaves the served store.");
    }

    /// <summary>
    /// A falsity-introducing withdrawal that simultaneously asserts a
    /// previously-derived fact keeps it served — the priorOverlay ∩ baseAdded
    /// overlap shape. Base <c>{subClassOf(A,B), type(x,A), disjointWith(B,C)}</c>
    /// derives the overlay fact <c>type(x,B)</c>. The commit asserts
    /// <c>type(x,B)</c> as a base fact AND adds <c>type(x,C)</c>, firing cax-dw:
    /// the overlay withdraws, yet <c>type(x,B)</c> — now asserted — must NOT be in
    /// the served removals, because it stays in the new served target.
    /// </summary>
    [TestMethod]
    public async Task WithdrawalKeepsAssertedFormerlyDerivedFactServed()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple bDisjointC = OwlRlBatteryHelpers.Triple(classB, terms.DisjointWith, classC);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);

        HashSet<EncodedTriple> baseBefore = [aSubB, xIsA, bDisjointC];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseBefore, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.Contains(xIsB, new HashSet<EncodedTriple>(maintenance.InitialState.ServedAdditions), "type(x,B) is a served derivation before the commit.");

        HashSet<EncodedTriple> baseAfter = [aSubB, xIsA, bDisjointC, xIsB, xIsC];
        HypertrieGraphStore store = await BuildStoreAsync(baseAfter).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [xIsB, xIsC], [], store, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsFalse(commit.OverlayOn, "The clashing typing withdraws the overlay.");
        Assert.IsFalse(commit.IsConsistent);
        Assert.DoesNotContain(xIsB, commit.ServedRemovals, "The asserted-was-derived fact must not be withdrawn — it is present in the new served target.");
        AssertServedDeltaEqualsSetdiff(commit, ExpectedServed(baseBefore, terms, oracle), ExpectedServed(baseAfter, terms, oracle), "withdrawal");
    }

    /// <summary>
    /// A consistency-restoring return that base-removes a still-derivable fact
    /// keeps it served — the baseRemoved ∩ newOverlay overlap shape. Base
    /// <c>{subClassOf(A,B), type(x,A), type(x,B), disjointWith(B,C), type(x,C)}</c>
    /// is inconsistent (cax-dw). The commit removes the falsity trigger
    /// <c>type(x,C)</c> AND the base fact <c>type(x,B)</c>; the overlay returns and
    /// <c>type(x,B)</c> — still derivable from cax-sco — must NOT be in the served
    /// removals.
    /// </summary>
    [TestMethod]
    public async Task ReturnKeepsBaseRemovedButDerivableFactServed()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);
        EncodedTriple bDisjointC = OwlRlBatteryHelpers.Triple(classB, terms.DisjointWith, classC);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);

        HashSet<EncodedTriple> baseBefore = [aSubB, xIsA, xIsB, bDisjointC, xIsC];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseBefore, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(maintenance.InitialState.IsConsistent, "The base is inconsistent at open.");

        HashSet<EncodedTriple> baseAfter = [aSubB, xIsA, bDisjointC];
        HypertrieGraphStore store = await BuildStoreAsync(baseAfter).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [], [xIsC, xIsB], store, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsTrue(commit.OverlayOn, "Removing the trigger restores consistency and returns the overlay.");
        Assert.IsTrue(commit.RebuildClass, "The return from an inconsistent state is a rebuild-class commit.");
        Assert.DoesNotContain(xIsB, commit.ServedRemovals, "The base-removed but still-derivable fact must stay served.");
        AssertServedDeltaEqualsSetdiff(commit, ExpectedServed(baseBefore, terms, oracle), ExpectedServed(baseAfter, terms, oracle), "return");
    }

    /// <summary>
    /// Consecutive commits over an inconsistent base serve base-only deltas. Base
    /// <c>{disjointWith(C,D), type(x,C), type(x,D)}</c> is inconsistent; two
    /// commits that each add another instance keep it inconsistent, so the overlay
    /// stays withdrawn and each commit's served delta is exactly its base delta.
    /// </summary>
    [TestMethod]
    public async Task ConsecutiveInconsistentCommitsServeBaseOnly()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");

        EncodedTriple disjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);
        EncodedTriple yIsC = OwlRlBatteryHelpers.Triple(y, terms.Type, classC);
        EncodedTriple zIsD = OwlRlBatteryHelpers.Triple(z, terms.Type, classD);

        HashSet<EncodedTriple> baseZero = [disjoint, xIsC, xIsD];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseZero, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        HashSet<EncodedTriple> baseOne = [disjoint, xIsC, xIsD, yIsC];
        HypertrieGraphStore storeOne = await BuildStoreAsync(baseOne).ConfigureAwait(false);
        ReasoningMaintainedCommit first = await maintenance.MaintainCommit(
            [yIsC], [], storeOne, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsFalse(first.OverlayOn, "The base stays inconsistent, so the overlay stays withdrawn.");
        AssertServedDeltaEqualsSetdiff(first, ExpectedServed(baseZero, terms, oracle), ExpectedServed(baseOne, terms, oracle), "inconsistent commit 1");
        Assert.IsTrue(new HashSet<EncodedTriple>(first.ServedAdditions).SetEquals([yIsC]), "The served addition is exactly the base addition.");

        HashSet<EncodedTriple> baseTwo = [disjoint, xIsC, xIsD, yIsC, zIsD];
        HypertrieGraphStore storeTwo = await BuildStoreAsync(baseTwo).ConfigureAwait(false);
        ReasoningMaintainedCommit second = await maintenance.MaintainCommit(
            [zIsD], [], storeTwo, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsFalse(second.OverlayOn);
        AssertServedDeltaEqualsSetdiff(second, ExpectedServed(baseOne, terms, oracle), ExpectedServed(baseTwo, terms, oracle), "inconsistent commit 2");
    }

    /// <summary>
    /// A wholesale-replace commit rebuilds from the caller's committed base rather
    /// than feeding a degenerate <c>Apply</c>. Replacing <c>{subClassOf(A,B),
    /// type(x,A)}</c> with the disjoint <c>{subClassOf(P,Q), type(m,P)}</c> reports
    /// the rebuild mode and a served delta whose application reaches the new
    /// closure exactly.
    /// </summary>
    [TestMethod]
    public async Task WholesaleReplaceRebuildsFromCallerBase()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId classP = OwlRlBatteryHelpers.Mint(dictionary, "P");
        TermId classQ = OwlRlBatteryHelpers.Mint(dictionary, "Q");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId m = OwlRlBatteryHelpers.Mint(dictionary, "m");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple pSubQ = OwlRlBatteryHelpers.Triple(classP, terms.SubClassOf, classQ);
        EncodedTriple mIsP = OwlRlBatteryHelpers.Triple(m, terms.Type, classP);

        HashSet<EncodedTriple> baseBefore = [aSubB, xIsA];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseBefore, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        HashSet<EncodedTriple> served = [.. maintenance.InitialState.ServedAdditions, .. baseBefore];

        HashSet<EncodedTriple> baseAfter = [pSubQ, mIsP];
        HypertrieGraphStore store = await BuildStoreAsync(baseAfter).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [pSubQ, mIsP], [aSubB, xIsA], store, wholesaleReplace: true, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsTrue(commit.RebuildClass, "A wholesale replace is a rebuild-class commit.");
        Assert.AreEqual(ReasoningMaintenanceMode.RebuildRequested, commit.Statistics.Mode);
        AssertServedDeltaEqualsSetdiff(commit, served, ExpectedServed(baseAfter, terms, oracle), "wholesale replace");

        ApplyServedDelta(served, commit);
        Assert.IsTrue(served.SetEquals(ExpectedServed(baseAfter, terms, oracle)), "Applying the served delta reaches the new closure exactly.");
    }

    /// <summary>
    /// A commit reported as not landed invalidates the instance, and the next
    /// commit rebuilds from the CALLER's committed base — never the closure's own
    /// diverged base. Commit one appends <c>type(y,A)</c> and is reported not
    /// landed; commit two supplies a DIFFERENT base (<c>type(z,A)</c> added, not
    /// <c>type(y,A)</c>) whose served result matches the naive closure over that
    /// base and carries no trace of the discarded append.
    /// </summary>
    [TestMethod]
    public async Task NotLandedCommitRebuildsFromCallerSuppliedBase()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");
        TermId z = OwlRlBatteryHelpers.Mint(dictionary, "z");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple yIsA = OwlRlBatteryHelpers.Triple(y, terms.Type, classA);
        EncodedTriple yIsB = OwlRlBatteryHelpers.Triple(y, terms.Type, classB);
        EncodedTriple zIsA = OwlRlBatteryHelpers.Triple(z, terms.Type, classA);

        HashSet<EncodedTriple> baseZero = [aSubB, xIsA];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseZero, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        HashSet<EncodedTriple> served = [.. maintenance.InitialState.ServedAdditions, .. baseZero];

        //Commit one appends type(y,A); the closure advances but the commit does
        //not land, so its state is discarded and the instance invalidated.
        HashSet<EncodedTriple> divergedBase = [aSubB, xIsA, yIsA];
        HypertrieGraphStore divergedStore = await BuildStoreAsync(divergedBase).ConfigureAwait(false);
        await maintenance.MaintainCommit([yIsA], [], divergedStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: false);

        //Commit two supplies a base that does NOT contain the discarded append.
        HashSet<EncodedTriple> callerBase = [aSubB, xIsA, zIsA];
        HypertrieGraphStore callerStore = await BuildStoreAsync(callerBase).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [zIsA], [], callerStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsTrue(commit.RebuildClass, "An invalidated instance rebuilds from the caller's base.");
        Assert.AreEqual(ReasoningMaintenanceMode.RebuildRequested, commit.Statistics.Mode);

        ApplyServedDelta(served, commit);
        Assert.IsTrue(served.SetEquals(ExpectedServed(callerBase, terms, oracle)), "The rebuild's served store matches the naive closure over the caller's base.");
        Assert.DoesNotContain(yIsA, served, "The discarded append's base fact is absent.");
        Assert.DoesNotContain(yIsB, served, "The discarded append's derivation is absent.");
    }

    /// <summary>
    /// A delegated beyond-RL verdict decays to fragment-relative on a subsequent
    /// assertion-only commit. A stub delegate decides the union module
    /// fragment-relative (a consistent verdict naming an unsupported remainder). A
    /// schema-touching commit re-decides; the following assertion-only commit does
    /// not re-decide, so it INHERITS the outcome, the undecided remainder, and the
    /// reason unchanged, and its consistency claim is fragment-relative
    /// (<see cref="ReasoningMaintainedCommit.IsDecisive"/> false) — never a phantom
    /// whole-module claim.
    /// </summary>
    [TestMethod]
    public async Task DelegatedVerdictDecaysOnAssertionOnlyCommit()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> unionBase = BeyondRlUnionBase(dictionary, terms, out TermId a);
        TermId newClass = OwlRlBatteryHelpers.Mint(dictionary, "d");
        TermId individual = OwlRlBatteryHelpers.Mint(dictionary, "ind");
        EncodedTriple dIsClass = OwlRlBatteryHelpers.Triple(newClass, terms.Type, terms.ClassTerm);
        EncodedTriple indIsA = OwlRlBatteryHelpers.Triple(individual, terms.Type, a);

        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            unionBase, dictionary, ReasoningPolicy.Default, FragmentRelativeDelegate("Remainder"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, maintenance.InitialState.Reason, "The union base delegates at open.");

        //A schema-touching commit re-decides fresh.
        HashSet<EncodedTriple> afterSchema = [.. unionBase, dIsClass];
        HypertrieGraphStore schemaStore = await BuildStoreAsync(afterSchema).ConfigureAwait(false);
        ReasoningMaintainedCommit decided = await maintenance.MaintainCommit(
            [dIsClass], [], schemaStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decided.DecisionOutcome, "The schema commit re-decides fragment-relative.");
        Assert.IsFalse(decided.IsDecisive);
        Assert.IsNotEmpty(decided.UndecidedConstructs);

        //An assertion-only commit does not re-decide: the decision decays.
        HashSet<EncodedTriple> afterAssertion = [.. afterSchema, indIsA];
        HypertrieGraphStore assertionStore = await BuildStoreAsync(afterAssertion).ConfigureAwait(false);
        ReasoningMaintainedCommit decayed = await maintenance.MaintainCommit(
            [indIsA], [], assertionStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.AreEqual(decided.DecisionOutcome, decayed.DecisionOutcome, "The outcome is inherited unchanged.");
        Assert.AreEqual(decided.Reason, decayed.Reason, "The reason is inherited unchanged.");
        Assert.AreSequenceEqual(
            new List<string>(decided.UndecidedConstructs),
            new List<string>(decayed.UndecidedConstructs),
            "The undecided remainder is inherited verbatim.");
        Assert.IsFalse(decayed.IsDecisive, "A decayed claim is fragment-relative, never whole-module.");
    }

    /// <summary>
    /// A budget abstention decays the same way. A stub delegate abstains on its
    /// budget; a schema-touching commit records the abstention, and the following
    /// assertion-only commit inherits the abstained outcome and reason and stays
    /// fragment-relative.
    /// </summary>
    [TestMethod]
    public async Task BudgetAbstentionDecaysOnAssertionOnlyCommit()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> unionBase = BeyondRlUnionBase(dictionary, terms, out TermId a);
        TermId newClass = OwlRlBatteryHelpers.Mint(dictionary, "d");
        TermId individual = OwlRlBatteryHelpers.Mint(dictionary, "ind");
        EncodedTriple dIsClass = OwlRlBatteryHelpers.Triple(newClass, terms.Type, terms.ClassTerm);
        EncodedTriple indIsA = OwlRlBatteryHelpers.Triple(individual, terms.Type, a);

        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            unionBase, dictionary, ReasoningPolicy.Default, AbstainingDelegate(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        HashSet<EncodedTriple> afterSchema = [.. unionBase, dIsClass];
        HypertrieGraphStore schemaStore = await BuildStoreAsync(afterSchema).ConfigureAwait(false);
        ReasoningMaintainedCommit abstained = await maintenance.MaintainCommit(
            [dIsClass], [], schemaStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.DecisionOutcome, "The schema commit records the abstention.");
        Assert.IsFalse(abstained.IsDecisive);

        HashSet<EncodedTriple> afterAssertion = [.. afterSchema, indIsA];
        HypertrieGraphStore assertionStore = await BuildStoreAsync(afterAssertion).ConfigureAwait(false);
        ReasoningMaintainedCommit decayed = await maintenance.MaintainCommit(
            [indIsA], [], assertionStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decayed.DecisionOutcome, "The abstained outcome is inherited unchanged.");
        Assert.AreEqual(abstained.Reason, decayed.Reason, "The reason is inherited unchanged.");
        Assert.IsFalse(decayed.IsDecisive, "A decayed abstention stays fragment-relative.");
    }

    /// <summary>
    /// A schema-touching commit refreshes the floor facts, not merely an
    /// invalidated cache. Opening over class declarations detects a within-RL
    /// floor (no module); a commit adding the union structure re-detects the floor
    /// beyond RL, so the post-commit provenance reports the RL profile withdrawn, a
    /// non-empty module, and the delegate's named undecided remainder.
    /// </summary>
    [TestMethod]
    public async Task SchemaTouchingCommitRefreshesFloorFacts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c1 = OwlRlBatteryHelpers.Mint(dictionary, "c1");

        EncodedTriple aIsClass = OwlRlBatteryHelpers.Triple(a, terms.Type, terms.ClassTerm);
        EncodedTriple bIsClass = OwlRlBatteryHelpers.Triple(b, terms.Type, terms.ClassTerm);
        EncodedTriple c1IsClass = OwlRlBatteryHelpers.Triple(c1, terms.Type, terms.ClassTerm);

        HashSet<EncodedTriple> baseTriples = [aIsClass, bIsClass, c1IsClass];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            baseTriples, dictionary, ReasoningPolicy.Default, FragmentRelativeDelegate("UnionRemainder"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(maintenance.InitialState.DetectedProfiles.HasFlag(OwlProfiles.Rl), "Class declarations are within RL.");
        Assert.AreEqual(0, maintenance.InitialState.ModuleAxiomCount, "The within-RL floor extracts no module.");

        List<EncodedTriple> unionStructure = UnionStructure(dictionary, terms, c1, a, b);
        HashSet<EncodedTriple> baseAfter = [.. baseTriples, .. unionStructure];
        HypertrieGraphStore store = await BuildStoreAsync(baseAfter).ConfigureAwait(false);
        ReasoningMaintainedCommit commit = await maintenance.MaintainCommit(
            [.. unionStructure], [], store, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsFalse(commit.DetectedProfiles.HasFlag(OwlProfiles.Rl), "The union structure withdraws the RL profile membership.");
        Assert.IsGreaterThan(0, commit.ModuleAxiomCount, "The re-detected floor extracts a beyond-RL module.");
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, commit.Reason, "The re-detected beyond-RL floor delegates.");
        Assert.IsNotEmpty(commit.UndecidedConstructs, "The fragment-relative verdict names its remainder on the refreshed floor.");
    }

    /// <summary>
    /// A beyond-RL commit that goes undecided-inconsistent and does NOT land leaves
    /// no stale floor for the next rebuild. This pins the discard rule against the
    /// rendezvous floor cache: opening over the within-RL class declarations
    /// <c>{a, b, c1}</c> detects a within-RL floor; commit one adds the union
    /// structure <c>c1 ⊑ (a ∪ b)</c> — schema-touching and beyond RL — under a stub
    /// delegate that condemns the module (an INCONSISTENT verdict), the refusal
    /// shape, and is reported not landed. The maintenance object invalidates. Commit
    /// two is assertion-only (<c>type(ind, a)</c>) over a fresh committed base that
    /// never carried the union structure. The rebuild must re-detect a WITHIN-RL
    /// floor over that base: <see cref="ReasoningMaintainedCommit.DetectedProfiles"/>
    /// carries RL, <see cref="ReasoningMaintainedCommit.ModuleAxiomCount"/> is zero,
    /// <see cref="ReasoningMaintainedCommit.Strategy"/> is
    /// <see cref="ReasoningStrategy.Rl"/> with no undecided remainder, the verdict is
    /// consistent, and the served delta reaches the naive closure over the caller's
    /// base. Without the discard resetting the rendezvous caches, the never-landed
    /// generation's beyond-RL floor is re-keyed onto the rebuild's store by the
    /// assertion-only <c>Advance</c> and hit by the following floor detection, so the
    /// rebuild would decide on a phantom module — a wrongful inconsistency and
    /// beyond-RL provenance over within-RL content.
    /// </summary>
    [TestMethod]
    public async Task NotLandedBeyondRlCommitLeavesNoStaleFloorForTheNextRebuild()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c1 = OwlRlBatteryHelpers.Mint(dictionary, "c1");
        TermId individual = OwlRlBatteryHelpers.Mint(dictionary, "ind");

        EncodedTriple aIsClass = OwlRlBatteryHelpers.Triple(a, terms.Type, terms.ClassTerm);
        EncodedTriple bIsClass = OwlRlBatteryHelpers.Triple(b, terms.Type, terms.ClassTerm);
        EncodedTriple c1IsClass = OwlRlBatteryHelpers.Triple(c1, terms.Type, terms.ClassTerm);
        EncodedTriple indIsA = OwlRlBatteryHelpers.Triple(individual, terms.Type, a);

        HashSet<EncodedTriple> openBase = [aIsClass, bIsClass, c1IsClass];
        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            openBase, dictionary, ReasoningPolicy.Default, InconsistentDelegate(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(maintenance.InitialState.DetectedProfiles.HasFlag(OwlProfiles.Rl), "The class declarations open within RL.");
        HashSet<EncodedTriple> served = [.. maintenance.InitialState.ServedAdditions, .. openBase];

        //Commit one adds the beyond-RL union structure; the delegate condemns the
        //module, so the commit is inconsistent and reported not landed (the refusal
        //shape). It caches a beyond-RL floor over its never-published tentative store.
        List<EncodedTriple> unionStructure = UnionStructure(dictionary, terms, c1, a, b);
        HashSet<EncodedTriple> refusedBase = [.. openBase, .. unionStructure];
        HypertrieGraphStore refusedStore = await BuildStoreAsync(refusedBase).ConfigureAwait(false);
        ReasoningMaintainedCommit refused = await maintenance.MaintainCommit(
            [.. unionStructure], [], refusedStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(refused.IsConsistent, "The condemning delegate makes the beyond-RL commit inconsistent.");
        Assert.IsFalse(refused.DetectedProfiles.HasFlag(OwlProfiles.Rl), "The union structure detects beyond RL on the refused generation.");
        maintenance.OnCommitOutcome(landed: false);

        //Commit two is assertion-only over a fresh committed base without the union
        //structure. The rebuild must re-detect a within-RL floor, not the discarded
        //generation's beyond-RL one.
        HashSet<EncodedTriple> callerBase = [.. openBase, indIsA];
        HypertrieGraphStore callerStore = await BuildStoreAsync(callerBase).ConfigureAwait(false);
        ReasoningMaintainedCommit rebuilt = await maintenance.MaintainCommit(
            [indIsA], [], callerStore, wholesaleReplace: false, TestContext.CancellationToken).ConfigureAwait(false);
        maintenance.OnCommitOutcome(landed: true);

        Assert.IsTrue(rebuilt.IsConsistent, "The within-RL rebuild is consistent — the discarded beyond-RL verdict must not survive.");
        Assert.IsTrue(rebuilt.DetectedProfiles.HasFlag(OwlProfiles.Rl), "The rebuild re-detects a within-RL floor over the caller's base.");
        Assert.AreEqual(0, rebuilt.ModuleAxiomCount, "A within-RL floor extracts no module — no phantom from the discarded generation.");
        Assert.AreEqual(ReasoningStrategy.Rl, rebuilt.Strategy, "The within-RL rebuild resolves to the RL strategy, not a re-delegation.");
        Assert.IsEmpty(rebuilt.UndecidedConstructs, "A within-RL rebuild names no undecided remainder.");
        AssertServedDeltaEqualsSetdiff(rebuilt, served, ExpectedServed(callerBase, terms, oracle), "within-RL rebuild after refusal");

        ApplyServedDelta(served, rebuilt);
        Assert.IsTrue(served.SetEquals(ExpectedServed(callerBase, terms, oracle)), "The rebuild's served store matches the naive closure over the caller's base.");
    }

    /// <summary>The served set of a base: its triples united with the naive RL closure's derived set when consistent, and its triples alone when inconsistent.</summary>
    /// <param name="baseTriples">The asserted base.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle the maintenance object uses.</param>
    /// <returns>The served set snapshot.</returns>
    private HashSet<EncodedTriple> ExpectedServed(IReadOnlyCollection<EncodedTriple> baseTriples, OwlRlTerms terms, OwlRlDatatypeOracle oracle)
    {
        OwlRlResult naive = OwlRlClosure.ComputeNaive(baseTriples, terms, oracle, cancellationToken: TestContext.CancellationToken);
        HashSet<EncodedTriple> served = [.. baseTriples];
        if(naive.IsConsistent)
        {
            served.UnionWith(naive.Derived);
        }

        return served;
    }

    /// <summary>The naive RL closure's derived set over a consistent base — the served-store seed comparand.</summary>
    /// <param name="baseTriples">The asserted base.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle the maintenance object uses.</param>
    /// <returns>The derived set snapshot.</returns>
    private HashSet<EncodedTriple> ExpectedDerived(IReadOnlyCollection<EncodedTriple> baseTriples, OwlRlTerms terms, OwlRlDatatypeOracle oracle)
    {
        OwlRlResult naive = OwlRlClosure.ComputeNaive(baseTriples, terms, oracle, cancellationToken: TestContext.CancellationToken);

        return [.. naive.Derived];
    }

    /// <summary>Builds a store over the triples with the default hashing.</summary>
    /// <param name="triples">The triples the store holds.</param>
    /// <returns>The built store.</returns>
    private async Task<HypertrieGraphStore> BuildStoreAsync(IReadOnlyCollection<EncodedTriple> triples)
    {
        return await HypertrieGraphStore.BuildAsync([.. triples], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asserts the commit's served additions and removals equal the setdiff of the previous and new served snapshots, with the two disjoint.</summary>
    /// <param name="commit">The maintained commit under test.</param>
    /// <param name="previousServed">The served snapshot before the commit.</param>
    /// <param name="newServed">The served snapshot after the commit.</param>
    /// <param name="label">The commit's name for assertion messages.</param>
    private static void AssertServedDeltaEqualsSetdiff(
        ReasoningMaintainedCommit commit,
        HashSet<EncodedTriple> previousServed,
        HashSet<EncodedTriple> newServed,
        string label)
    {
        HashSet<EncodedTriple> expectedAdded = [.. newServed];
        expectedAdded.ExceptWith(previousServed);
        HashSet<EncodedTriple> expectedRemoved = [.. previousServed];
        expectedRemoved.ExceptWith(newServed);

        HashSet<EncodedTriple> added = [.. commit.ServedAdditions];
        HashSet<EncodedTriple> removed = [.. commit.ServedRemovals];

        Assert.IsTrue(added.SetEquals(expectedAdded), $"{label}: served additions ({added.Count}) must equal the setdiff ({expectedAdded.Count}).");
        Assert.IsTrue(removed.SetEquals(expectedRemoved), $"{label}: served removals ({removed.Count}) must equal the setdiff ({expectedRemoved.Count}).");

        HashSet<EncodedTriple> intersection = [.. added];
        intersection.IntersectWith(removed);
        Assert.IsEmpty(intersection, $"{label}: served additions ∩ removals must be empty.");
    }

    /// <summary>Applies a commit's served delta to a served set — removals first, then additions.</summary>
    /// <param name="served">The served set to evolve.</param>
    /// <param name="commit">The commit whose delta applies.</param>
    private static void ApplyServedDelta(HashSet<EncodedTriple> served, ReasoningMaintainedCommit commit)
    {
        foreach(EncodedTriple triple in commit.ServedRemovals)
        {
            served.Remove(triple);
        }

        foreach(EncodedTriple triple in commit.ServedAdditions)
        {
            served.Add(triple);
        }
    }

    /// <summary>The beyond-RL base <c>c1 ⊑ (a ∪ b)</c> with its class declarations — a union on the superclass side, outside the RL grammar.</summary>
    /// <param name="dictionary">The dictionary the terms mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="a">Receives the first union member class, for a later assertion-only typing.</param>
    /// <returns>The base triples.</returns>
    private static List<EncodedTriple> BeyondRlUnionBase(TermDictionary dictionary, OwlRlTerms terms, out TermId a)
    {
        TermId aClass = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c1 = OwlRlBatteryHelpers.Mint(dictionary, "c1");
        a = aClass;

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(aClass, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(b, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(c1, terms.Type, terms.ClassTerm),
            .. UnionStructure(dictionary, terms, c1, aClass, b),
        ];

        return triples;
    }

    /// <summary>The <c>c1 ⊑ (a ∪ b)</c> union structure triples — the subclass axiom and its two-member rdf list — without the class declarations.</summary>
    /// <param name="dictionary">The dictionary the list nodes mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="c1">The subclass whose superclass is the union.</param>
    /// <param name="a">The first union member.</param>
    /// <param name="b">The second union member.</param>
    /// <returns>The union structure triples.</returns>
    private static List<EncodedTriple> UnionStructure(TermDictionary dictionary, OwlRlTerms terms, TermId c1, TermId a, TermId b)
    {
        TermId union = OwlRlBatteryHelpers.Blank(dictionary, "union");
        TermId list1 = OwlRlBatteryHelpers.Blank(dictionary, "list1");
        TermId list2 = OwlRlBatteryHelpers.Blank(dictionary, "list2");

        return
        [
            OwlRlBatteryHelpers.Triple(c1, terms.SubClassOf, union),
            OwlRlBatteryHelpers.Triple(union, terms.UnionOf, list1),
            OwlRlBatteryHelpers.Triple(list1, terms.First, a),
            OwlRlBatteryHelpers.Triple(list1, terms.Rest, list2),
            OwlRlBatteryHelpers.Triple(list2, terms.First, b),
            OwlRlBatteryHelpers.Triple(list2, terms.Rest, terms.Nil),
        ];
    }

    /// <summary>A stub delegate that decides every module consistent-but-fragment-relative, naming a fixed unsupported remainder.</summary>
    /// <param name="remainder">The unsupported construct the verdict names.</param>
    /// <returns>The delegate.</returns>
    private static DescriptionLogicDelegate FragmentRelativeDelegate(string remainder)
    {
        return (_, _) => ValueTask.FromResult(ModuleDecision.Decided(
            new ModuleVerdict(IsConsistent: true, Subsumptions: []) { UnsupportedConstructs = [remainder] },
            ReasoningDecisionStatistics.Empty));
    }

    /// <summary>A stub delegate that abstains on its budget for every module.</summary>
    /// <returns>The delegate.</returns>
    private static DescriptionLogicDelegate AbstainingDelegate()
    {
        return (_, _) => ValueTask.FromResult(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty));
    }

    /// <summary>A stub delegate that condemns every module — a whole-module inconsistent verdict, the refusal shape.</summary>
    /// <returns>The delegate.</returns>
    private static DescriptionLogicDelegate InconsistentDelegate()
    {
        return (_, _) => ValueTask.FromResult(ModuleDecision.Decided(
            new ModuleVerdict(IsConsistent: false, Subsumptions: []),
            ReasoningDecisionStatistics.Empty));
    }
}
