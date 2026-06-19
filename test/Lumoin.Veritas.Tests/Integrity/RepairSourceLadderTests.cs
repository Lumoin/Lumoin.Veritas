using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The repair-source ladder descends the restoring rungs in a fixed order, returns the first that restores a
/// corruption, and terminates at a named loss when every restoring rung declines — and the named loss is an
/// <see cref="UnrecoverableItemReport.ItemSet"/> naming the exact lost system-of-record range. No wall-clock
/// or background work is involved, so the assertions are pure logic.
/// </summary>
[TestClass]
internal sealed class RepairSourceLadderTests
{
    /// <summary>A per-rung attempt that records the descent order and succeeds at one configured rung — a method group binds it as the attempt delegate, so the test body holds no closure.</summary>
    private sealed class RungProbe
    {
        /// <summary>The rung at which the attempt succeeds; <see langword="null"/> declines every rung.</summary>
        public RepairRung? SucceedAt { get; init; }

        /// <summary>The rungs attempted, in order.</summary>
        public List<RepairRung> Attempted { get; } = [];

        /// <summary>Records the attempt and succeeds only at <see cref="SucceedAt"/>.</summary>
        /// <param name="rung">The rung being attempted.</param>
        /// <returns>Whether this rung restored the corruption.</returns>
        public bool Attempt(RepairRung rung)
        {
            Attempted.Add(rung);

            return SucceedAt == rung;
        }
    }

    /// <summary>The restoring rungs are the three non-terminal rungs, in descent order; the terminal named loss is not among them.</summary>
    [TestMethod]
    public void RestoringRungsAreTheThreeNonTerminalRungsInOrder()
    {
        Assert.IsTrue(
            new[] { RepairRung.RederiveLocally, RepairRung.LocalParity, RepairRung.PeerReconciliation }
                .AsSpan().SequenceEqual([.. RepairSourceLadder.RestoringRungs]),
            "RestoringRungs must be RederiveLocally, LocalParity, PeerReconciliation in order.");
        Assert.DoesNotContain(RepairRung.NamedLoss, RepairSourceLadder.RestoringRungs);
    }

    /// <summary>The first restoring rung that succeeds wins, and no later rung is attempted.</summary>
    [TestMethod]
    public void DescendReturnsTheFirstRestoringRungThatSucceeds()
    {
        RungProbe probe = new() { SucceedAt = RepairRung.RederiveLocally };

        RepairRung outcome = RepairSourceLadder.Descend(probe.Attempt);

        Assert.AreEqual(RepairRung.RederiveLocally, outcome);
        Assert.IsTrue(new[] { RepairRung.RederiveLocally }.AsSpan().SequenceEqual([.. probe.Attempted]), "Only the first rung should be attempted when it succeeds.");
    }

    /// <summary>Declining rungs are skipped in order until one succeeds; the descent stops there.</summary>
    [TestMethod]
    public void DescendSkipsDecliningRungsToTheOneThatSucceeds()
    {
        RungProbe probe = new() { SucceedAt = RepairRung.PeerReconciliation };

        RepairRung outcome = RepairSourceLadder.Descend(probe.Attempt);

        Assert.AreEqual(RepairRung.PeerReconciliation, outcome);
        Assert.IsTrue(
            new[] { RepairRung.RederiveLocally, RepairRung.LocalParity, RepairRung.PeerReconciliation }.AsSpan().SequenceEqual([.. probe.Attempted]),
            "The ladder must attempt the rungs in order up to the one that succeeds.");
    }

    /// <summary>When every restoring rung declines, the ladder terminates at a named loss after attempting all three.</summary>
    [TestMethod]
    public void DescendNamesTheLossWhenEveryRestoringRungDeclines()
    {
        RungProbe probe = new() { SucceedAt = null };

        RepairRung outcome = RepairSourceLadder.Descend(probe.Attempt);

        Assert.AreEqual(RepairRung.NamedLoss, outcome);
        Assert.IsTrue(
            new[] { RepairRung.RederiveLocally, RepairRung.LocalParity, RepairRung.PeerReconciliation }.AsSpan().SequenceEqual([.. probe.Attempted]),
            "Every restoring rung must be attempted before naming the loss.");
    }

    /// <summary>A null attempt is rejected.</summary>
    [TestMethod]
    public void DescendRejectsANullAttempt()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => RepairSourceLadder.Descend(null!));
    }

    /// <summary>The terminal named loss is an item-set report naming the exact lost range and generation.</summary>
    [TestMethod]
    public void ItemSetNamesTheLostRangeAndGeneration()
    {
        UnrecoverableItemReport report = UnrecoverableItemReport.ItemSet(commitGeneration: 12, lostItemStart: 340, lostItemCount: 10);

        Assert.AreEqual(UnrecoverableItemReportKind.ItemSet, report.Kind);
        Assert.AreEqual(12, report.CommitGeneration);
        Assert.AreEqual(340, report.LostItemStart);
        Assert.AreEqual(10, report.LostItemCount);
    }

    /// <summary>An item-set report must name a real loss at a real generation: a negative generation or start, or a non-positive count, is rejected.</summary>
    [TestMethod]
    public void ItemSetRejectsAnInvalidRange()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UnrecoverableItemReport.ItemSet(-1, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UnrecoverableItemReport.ItemSet(0, -1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UnrecoverableItemReport.ItemSet(0, 0, 0));
    }
}
