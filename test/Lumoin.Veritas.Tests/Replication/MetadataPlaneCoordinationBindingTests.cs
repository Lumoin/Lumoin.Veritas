using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The adapter that binds one metadata plane to the three consultations an engine's mutable open makes: each
/// seam forwards the obligation's own value-based outcome and nothing else — a claim taken, a claim another
/// replica holds, a baseline intent and its confirm — the three seams reach ONE plane, so what one of them wrote
/// is what the next one reads; a quorum the deployment cannot assemble is answered UNDECIDED by value with
/// nothing raised, because the plane is never a liveness dependency of the data lane; and the attempt budget the
/// adapter was built with is the budget the obligation spends.
/// </summary>
/// <remarks>
/// <para>
/// THE PARALLEL FOOTPRINT of one row is at most four recorder-host loops and four plane loops, all of them idle
/// channel readers between obligations, held by one <see cref="MetadataPlaneHarness"/> that is disposed with the
/// row. No row binds a port, touches a file, or reads a mutable static.
/// </para>
/// <para>
/// NO ROW DEPENDS ON WALL TIME. Every plane runs with a hedging base delay of <see cref="TimeSpan.Zero"/> over
/// the harness's pinned clock, and what each row awaits is the consultation's own completion, which IS the
/// transition it asserts on.
/// </para>
/// <para>
/// THE UNDECIDED ROWS REACH DEFINITE IGNORANCE BY CONSTRUCTION rather than by a schedule. They admit two
/// replicas the bench runs no host for, which moves the quorum above the number of hosts that exist: every
/// attempt then reaches the two live recorders and no more, so the obligation exhausts its budget and reports
/// ignorance on every run. The admissions themselves are decided by the OUTGOING membership, so each of them
/// lands before the one after it raises the bar.
/// </para>
/// </remarks>
[TestClass]
internal sealed class MetadataPlaneCoordinationBindingTests
{
    /// <summary>How many consensus attempts one obligation may spend. Generous, so an uncontended row converges rather than reporting ignorance.</summary>
    private const int AttemptBudget = 16;

    /// <summary>How many times one protocol step may send to one recorder before abandoning it for that step.</summary>
    private const int AttemptsPerRecorder = 2;

    /// <summary>The causality digest the baseline consultations of this battery carry; above two to the fifty-third.</summary>
    private const ulong CausalityDigestValue = 0x9E3779B97F4A7C15UL;

    /// <summary>The dataset StateId the baseline confirm of this battery carries; likewise above two to the fifty-third.</summary>
    private const ulong StateIdValue = 0xFEEDFACECAFEBEEFUL;

    /// <summary>The term-dictionary epoch the baseline confirm of this battery carries.</summary>
    private const long DictionaryEpochValue = 42L;

    /// <summary>The MSTest-supplied per-test context, read for the row's cancellation token and its seed line.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The three seams forward their own outcomes and reach ONE plane: the claim is taken and then reports
    /// itself already held by this replica, the baseline intent is recorded, and the confirm that follows it
    /// lands on the record the intent wrote.
    /// </summary>
    [TestMethod]
    public async Task TheThreeSeamsForwardTheirOutcomesOffOnePlane()
    {
        const int prioritySeed = 20260901;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneCoordinationBinding binding = new(harness.Plane(0), AttemptBudget);
            MetadataCoordinationSeams seams = binding.Seams;

            IdentityClaimOutcome claimed = await seams.ClaimIdentity(harness.Axis(0), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Claimed, claimed, "An axis the record does not carry is claimed, and the adapter answers the obligation's own ladder value.");

            IdentityClaimOutcome repeated = await seams.ClaimIdentity(harness.Axis(0), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.AlreadyClaimedBySelf, repeated, "The repeat reads the claim the first consultation wrote, which is what binding all three seams to one plane buys.");

            NodeIdentifier digest = new(CausalityDigestValue);
            BaselineRecordOutcome recorded = await seams.RecordBaselineIntent(harness.Axis(0), digest, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.Recorded, recorded, "A chain carrying no baseline records the intent this open declared.");

            BaselineRecordOutcome confirmed = await seams.ConfirmBaseline(digest, new NodeIdentifier(StateIdValue), DictionaryEpochValue, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.Confirmed, confirmed, "The confirm matches its intent by a byte-identical digest, which it can only do on the record the intent seam wrote.");

            VersionedValue<VeritasMetadataRecord>? settled = await harness.Plane(0).ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(settled, "The chain carries the record the three consultations built.");
            Assert.AreEqual(1, ClaimsOf(settled!.Value, harness.Axis(0)), "The axis is claimed exactly once: a claim is appended and never rewritten, so a second write of one effect would show as a second entry.");
            Assert.IsNotNull(settled.Value.Baseline, "The settled record carries the baseline the intent seam recorded.");
            Assert.AreEqual(digest, settled.Value.Baseline!.CausalityDigest, "The settled baseline names the lineage the intent declared.");
            Assert.IsNotNull(settled.Value.Baseline.Confirmation, "The settled baseline carries the confirmation the confirm seam wrote, so the two-phase ladder ran over one record.");
            Assert.AreEqual(new NodeIdentifier(StateIdValue), settled.Value.Baseline.Confirmation!.StateId, "The confirmation names the dataset StateId the confirm consultation supplied.");
            Assert.AreEqual(DictionaryEpochValue, settled.Value.Baseline.Confirmation.DictionaryEpoch, "The confirmation names the dictionary epoch the confirm consultation supplied.");
        }
    }

    /// <summary>
    /// A claim on an axis ANOTHER replica already took is forwarded as the definite refusal, while the same
    /// binding's claim on its own axis is taken — so the refusal is about the axis and never about the adapter.
    /// </summary>
    [TestMethod]
    public async Task ARefusedClaimIsForwardedAsTheDefiniteRefusal()
    {
        const int prioritySeed = 20260902;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<IdentityClaimOutcome> taken = await harness.Plane(0).ClaimIdentityAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Claimed, taken.Outcome, "The first replica takes its own axis, which is the standing claim the second one meets.");

            MetadataPlaneCoordinationBinding binding = new(harness.Plane(1), AttemptBudget);
            MetadataCoordinationSeams seams = binding.Seams;

            IdentityClaimOutcome refused = await seams.ClaimIdentity(harness.Axis(0), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.RefusedHeldByOther, refused, "A claim on an axis the coordinated record holds for another minter is refused definitely, which is the one arm that refuses an open.");

            IdentityClaimOutcome own = await seams.ClaimIdentity(harness.Axis(1), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Claimed, own, "The same binding takes its own axis, so the refusal above is the axis's fact and not the adapter's.");
        }
    }

    /// <summary>
    /// A quorum the deployment cannot assemble is answered UNDECIDED by every seam, by VALUE and with nothing
    /// raised — the fail-open arm the engine's open reads as "proceed".
    /// </summary>
    [TestMethod]
    public async Task AnUnreachableQuorumIsForwardedAsUndecidedByEverySeam()
    {
        const int prioritySeed = 20260903;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            await RaiseQuorumBeyondTheHostsAsync(harness).ConfigureAwait(false);

            MetadataPlaneCoordinationBinding binding = new(harness.Plane(0), AttemptBudget);
            MetadataCoordinationSeams seams = binding.Seams;

            IdentityClaimOutcome claim = await seams.ClaimIdentity(harness.Axis(0), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Undecided, claim, "A claim that assembles no quorum reports definite ignorance as a value; only the definite refusal refuses an open.");

            NodeIdentifier digest = new(CausalityDigestValue);
            BaselineRecordOutcome intent = await seams.RecordBaselineIntent(harness.Axis(0), digest, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.Undecided, intent, "The intent fails open on the same rule: the open proceeds with the intent pending.");

            BaselineRecordOutcome confirm = await seams.ConfirmBaseline(digest, new NodeIdentifier(StateIdValue), DictionaryEpochValue, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.Undecided, confirm, "The confirm leaves the standing pending for the next open's retry rather than raising over a commit that already happened.");
        }
    }

    /// <summary>
    /// The attempt budget an adapter was built with is the budget its obligation spends: two adapters over ONE
    /// plane, built with different budgets and driven to the same undecided answer, spend their own budgets.
    /// </summary>
    /// <remarks>
    /// The budget is only observable where the obligation cannot commit, because a write that commits stops at
    /// the attempt that committed it. That is why this row runs against a quorum the deployment cannot assemble:
    /// each claim then spends every attempt it was given, and the two counts read off the plane's own trace are
    /// the two budgets. An adapter that ignored its budget would report one count twice.
    /// </remarks>
    [TestMethod]
    public async Task EachBindingSpendsTheAttemptBudgetItWasBuiltWith()
    {
        const int prioritySeed = 20260904;
        const int narrowBudget = 2;
        const int wideBudget = 5;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            await RaiseQuorumBeyondTheHostsAsync(harness).ConfigureAwait(false);

            MetadataPlaneCoordinationBinding narrow = new(harness.Plane(0), narrowBudget);
            MetadataPlaneCoordinationBinding wide = new(harness.Plane(0), wideBudget);

            IdentityClaimOutcome first = await narrow.Seams.ClaimIdentity(harness.Axis(0), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Undecided, first, "The narrow adapter's claim assembles no quorum, which is what makes its spending observable.");

            IdentityClaimOutcome second = await wide.Seams.ClaimIdentity(harness.Axis(0), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Undecided, second, "The wide adapter's claim assembles no quorum either, so the two answers differ in nothing but the budget.");

            List<int> spent = ClaimAttemptsOf(harness.TraceOf(0));
            Assert.HasCount(2, spent, "The plane emitted one verdict per claim consultation, in completion order.");
            Assert.AreEqual(narrowBudget, spent[0], "The first claim spent the budget the narrow adapter was built with, and stopped there.");
            Assert.AreEqual(wideBudget, spent[1], "The second claim spent the budget the wide adapter was built with, so the budget travels with the adapter rather than with the plane.");
        }
    }

    /// <summary>An adapter names one plane and one usable budget: a null plane is no coordination, and a budget below one permits no attempt at all.</summary>
    [TestMethod]
    public async Task AnAdapterRefusesANullPlaneAndAnUnusableBudget()
    {
        const int prioritySeed = 20260905;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 1, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            Assert.IsTrue(RefusesNullPlane(AttemptBudget), "An adapter over no plane is refused at construction rather than raising at the first consultation.");
            Assert.IsTrue(RefusesBudget(harness.Plane(0), 0), "A budget of zero permits no attempt and is refused, so no consultation can be built that reports ignorance for a reason nobody chose.");
            Assert.IsTrue(RefusesBudget(harness.Plane(0), -1), "A negative budget is refused on the same rule.");
        }
    }

    /// <summary>
    /// Bootstraps the chain and then admits two replicas the bench runs no host for, which puts the quorum above
    /// the number of hosts that exist and makes every later write report definite ignorance.
    /// </summary>
    /// <param name="harness">The bench to raise the quorum on; a two-founder bench.</param>
    /// <returns>A task that completes once the second admission has landed.</returns>
    /// <remarks>
    /// Each admission is decided by the OUTGOING membership, so the first lands under a quorum of two hosted
    /// replicas and the second under a quorum of two out of three. The membership that results names four
    /// replicas and needs three of them, which the bench's two hosts cannot supply.
    /// </remarks>
    private async Task RaiseQuorumBeyondTheHostsAsync(MetadataPlaneHarness harness)
    {
        MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain decides its first record, which a membership change carries forward.");

        MetadataPlaneResult<MembershipChangeOutcome> third = await harness.Plane(0).AdmitMemberAsync(MetadataPlaneHarness.FounderFor(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(MembershipChangeOutcome.Changed, third.Outcome, "The first admission is decided by the two hosted founders, whose quorum is two.");

        MetadataPlaneResult<MembershipChangeOutcome> fourth = await harness.Plane(0).AdmitMemberAsync(MetadataPlaneHarness.FounderFor(3), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(MembershipChangeOutcome.Changed, fourth.Outcome, "The second admission is decided by a three-member membership whose quorum the two hosted founders still make.");
    }

    /// <summary>How many claims on one record name <paramref name="axis"/>.</summary>
    /// <param name="record">The record to count over.</param>
    /// <param name="axis">The axis to count.</param>
    /// <returns>The number of claims naming that axis.</returns>
    /// <remarks>
    /// A claim is appended and never rewritten, so a duplicate would be a second write of one consultation's
    /// effect rather than an overwrite, and only a count can see it.
    /// </remarks>
    private static int ClaimsOf(VeritasMetadataRecord record, ReplicaAxis axis)
    {
        int found = 0;
        for(int i = 0; i < record.IdentityClaims.Length; i++)
        {
            if(record.IdentityClaims[i].Axis.Equals(axis))
            {
                found += 1;
            }
        }

        return found;
    }

    /// <summary>The attempt counts the identity-claim verdicts of one plane's trace carry, in completion order.</summary>
    /// <param name="emitted">The plane's captured verdicts.</param>
    /// <returns>The attempt counts.</returns>
    private static List<int> ClaimAttemptsOf(IReadOnlyList<MetadataPlaneTraceEvent> emitted)
    {
        List<int> attempts = [];
        for(int i = 0; i < emitted.Count; i++)
        {
            if(emitted[i].Obligation == MetadataPlaneObligation.IdentityClaim)
            {
                attempts.Add(emitted[i].Attempts);
            }
        }

        return attempts;
    }

    /// <summary>Builds an adapter over no plane and reports whether the construction was refused.</summary>
    /// <param name="attemptBudget">The budget the refused construction was given.</param>
    /// <returns><see langword="true"/> when the construction was refused.</returns>
    /// <remarks>
    /// The refusal is answered as a value rather than through an assertion callback, so the operands reach the
    /// constructor as explicit arguments and nothing here captures an enclosing scope.
    /// </remarks>
    private static bool RefusesNullPlane(int attemptBudget)
    {
        try
        {
            _ = new MetadataPlaneCoordinationBinding(plane: null!, attemptBudget);

            return false;
        }
        catch(ArgumentNullException)
        {
            return true;
        }
    }

    /// <summary>Builds an adapter over one plane with an unusable budget and reports whether the construction was refused.</summary>
    /// <param name="plane">The plane the refused construction was given.</param>
    /// <param name="attemptBudget">The budget the refused construction was given.</param>
    /// <returns><see langword="true"/> when the construction was refused.</returns>
    private static bool RefusesBudget(VeritasMetadataPlane plane, int attemptBudget)
    {
        try
        {
            _ = new MetadataPlaneCoordinationBinding(plane, attemptBudget);

            return false;
        }
        catch(ArgumentOutOfRangeException)
        {
            return true;
        }
    }
}
