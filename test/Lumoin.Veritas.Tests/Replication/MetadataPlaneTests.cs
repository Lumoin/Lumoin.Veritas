using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;
using Lumoin.Verisync.Json;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The consensus-backed metadata plane over in-process endpoints: writers held at ONE version by a rendezvous
/// resolve the founder race on one deterministic initial record, land two distinct claims exactly once each,
/// leave exactly one coordinator lease, and close the independent-baseline storm on one lineage — each of them
/// with the superseded writer's extra attempt as the witness that the race happened; a duplicate claim is refused
/// at claim time; two different obligations racing on one plane both answer, because the plane serializes its own
/// write initiation; the lineage baseline walks its two-phase ladder, its crash-retry idempotence and its
/// fail-open confirm arm; the coordinator lease is taken, refreshed, refused to a rival and released, and a
/// retired holder's lease is usurped; an admitted replica catches up and then writes, and a retired one's own next
/// write is refused for nothing; a readiness report separates a member that answered nothing from one that
/// answered unwritten and refuses a probe another host answered, completes over a member whose probe hangs and
/// honours no token, spends its deadline per member so an early hang costs no later member, and gives a hung
/// member and a faulting one the identical entry; a retirement's decommission gate is answered over
/// the membership the change installed and clears only on a quorum that excludes the retired replica; membership
/// changes report the bootstrap they need and cost no round when they change nothing; a policy amendment installs
/// and then reports unchanged; and the decided record survives the consensus JSON codec by value.
/// </summary>
/// <remarks>
/// <para>
/// THE PARALLEL FOOTPRINT of one row is at most four recorder-host loops and four plane loops, all of them idle
/// channel readers between obligations, held by one <see cref="MetadataPlaneHarness"/> that is disposed with
/// the row. No row binds a port, touches a file, or reads or writes a mutable static, so rows run beside each
/// other and beside the rest of the suite without sharing anything.
/// </para>
/// <para>
/// NO ROW DEPENDS ON WALL TIME. Every plane is built with a hedging base delay of <see cref="TimeSpan.Zero"/>
/// over a pinned clock the harness owns, so nothing here waits and no outcome can turn on how fast a machine
/// ran. What each row awaits is the obligation's own completion, which IS the transition it asserts on, and a
/// contended row's writers are released by each other's arrival rather than by a clock. The three rows that
/// assert the per-member deadline reach it the same way: they hang one member's probe, await that probe's own
/// report that it was entered, and then MOVE the pinned clock past the deadline, so the transition arrives
/// because the row advanced the clock and never because a machine waited. The bounded waits in this battery are
/// backstops — the harness teardown's on a runner loop that was told to complete, the rendezvous's on a writer
/// that never arrives, a held row's on the writer reaching the hold, the hold's own on a release the row never
/// performed, and a hung row's on the report its advance should have released — which turn a wedged loop into a
/// failed row rather than a hung suite. They are three derived bounds and not five numbers: a member is given up
/// on before a row gives up on the report, and a teardown join stands outside every in-flight wait, both by
/// construction, so a row fails on what it asserted rather than on a bound.
/// </para>
/// <para>
/// A CONTENDED ROW ASSERTS SAFETY ALWAYS AND CONTENTION BY CONSTRUCTION. The safety claims — one lease, one
/// lineage, one claim per axis, one decided initial record — hold under every interleaving and are asserted
/// unconditionally. Contention itself is not left to the scheduler: the rendezvous holds each named writer at its
/// FIRST record exchange until all of them have arrived, so their proposals address one version, one of them is
/// necessarily superseded there, and the extra attempt that writer spends is an observable no serialized
/// execution can produce. Each such row asserts that the gate did open on arrivals, so a backstop that opened it
/// instead is a failure and never a silent downgrade.
/// </para>
/// <para>
/// EVERY ROW PRINTS ITS PRIORITY SEED. The consensus procedure is randomized, and every draw a row makes comes
/// from a per-replica stream mixed from that one number, so a failing row replays its decisions from what it
/// printed.
/// </para>
/// <para>
/// NO ROW PINS Undecided, and the four contended rows tolerate it. A bench whose endpoints always answer cannot
/// produce QuePaxa's definite ignorance on demand, so a row that asserted it would be asserting a schedule; the
/// deterministic Undecided coverage belongs to the socket battery, where a majority can be partitioned outright.
/// It can still arise here transiently — a writer whose version a rival closed before its own proposal was
/// recorded is answered by hosts that have moved on and assembles no quorum — and the four contended rows answer
/// it the way the design says a host does, by re-issuing the idempotent obligation rather than by treating
/// ignorance as refusal. A row whose exact claim holds only where no writer reported ignorance says so and
/// takes the ignorant branch by name, rather than weakening the claim for every run.
/// </para>
/// </remarks>
[TestClass]
internal sealed class MetadataPlaneTests
{
    /// <summary>How many consensus attempts one obligation may spend. Generous, so a contended row converges rather than reporting ignorance.</summary>
    private const int AttemptBudget = 16;

    /// <summary>How many times one protocol step may send to one recorder before abandoning it for that step.</summary>
    private const int AttemptsPerRecorder = 2;

    /// <summary>
    /// How many times a contended row re-issues an obligation that answered <c>Undecided</c>.
    /// </summary>
    /// <remarks>
    /// A writer whose version a rival closed before its own proposal was recorded is answered by hosts that have
    /// moved on, so it assembles no quorum and reports definite ignorance rather than a refusal. What resolves it
    /// is the host's own coordination loop calling the idempotent obligation again, which adopts the record its
    /// host has since learned and composes on the winner. Every retry here is driven by the previous ANSWER and
    /// never by a clock, and its bound is a count rather than a duration.
    /// </remarks>
    private const int SettleRetries = 4;

    /// <summary>The MSTest-supplied per-test context, read for the row's cancellation token and its seed line.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The bound a row waits under for a writer it deliberately held to reach the hold. It is a BACKSTOP and
    /// never a cadence: the arrival is the transition being awaited and a passing row reaches it at once, while
    /// a regression that never sends surfaces here as a failure instead of as a hung suite. It is the ladder's
    /// in-flight bound, which the teardown bound stands outside of.
    /// </summary>
    private static TimeSpan HoldBackstop { get; } = MetadataBatteryBackstops.InFlight;

    /// <summary>Two founders held at ONE version by the write rendezvous both end on the bootstrap ladder, EXACTLY one of them reporting that it decided the chain wherever neither writer reported ignorance, one of them having spent a further attempt recomposing on the winner, and the committed record is the deterministic initial record by value.</summary>
    /// <remarks>
    /// <para>
    /// AT MOST ONE FOUNDER CAN REPORT THE BOOTSTRAP, and that holds under every interleaving. A founder answers
    /// it only when its own write committed a record it computed against a chain that had decided nothing, and
    /// only the first version is written against such a chain; a writer that adopts a winner recomputes against
    /// the record it adopted and lands on the already-bootstrapped arm instead.
    /// </para>
    /// <para>
    /// AT LEAST ONE FOUNDER REPORTS IT whenever neither writer reported ignorance, which is why the exact claim
    /// is the one asserted there. The first version carries one writer's record, and that writer's own attempt is
    /// answered committed rather than superseded — a supersession is the version carrying ANOTHER writer's
    /// record — so a writer that is not answered ignorance and whose record the version carries reports the
    /// bootstrap it computed. The other writer is superseded there, adopts the winner and reports it already
    /// bootstrapped.
    /// </para>
    /// <para>
    /// WHAT BREAKS THE PAIRING IS DEFINITE IGNORANCE, which is a writer that spent its whole attempt budget
    /// without observing a decision. Its re-issue adopts the record the chain has since carried and reports it
    /// already bootstrapped, so both founders can end on that arm and none report the bootstrap. That branch is
    /// taken by name and printed rather than folded into a weaker claim for every run, because the weaker claim
    /// would pass just as happily against a plane that had stopped reporting the bootstrap at all.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ContendedBootstrapAtOneVersionLeavesTheInitialRecordCommitted()
    {
        const int prioritySeed = 20260814;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            //The gate is what makes this row a race on every run rather than on a lucky one: neither founder
            //sends its first record exchange until both have reached one, so both proposals address the first
            //version and one of them must be superseded there.
            WriterRendezvous<int> rendezvous = harness.ArmWriteRendezvous(0, 1);

            Task<MetadataPlaneResult<PlaneBootstrapOutcome>> first = harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken);
            Task<MetadataPlaneResult<PlaneBootstrapOutcome>> second = harness.Plane(1).BootstrapAsync(AttemptBudget, TestContext.CancellationToken);

            PlaneBootstrapOutcome firstOutcome = (await first.ConfigureAwait(false)).Outcome;
            PlaneBootstrapOutcome secondOutcome = (await second.ConfigureAwait(false)).Outcome;

            //Whether either founder spent its whole budget without observing a decision is read BEFORE the
            //re-issues that answer it, because it is what separates the exact claim below from the one branch
            //the register's ignorance can produce.
            bool ignoranceReported = firstOutcome == PlaneBootstrapOutcome.Undecided || secondOutcome == PlaneBootstrapOutcome.Undecided;

            for(int retry = 0; retry < SettleRetries && firstOutcome == PlaneBootstrapOutcome.Undecided; retry++)
            {
                firstOutcome = (await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            for(int retry = 0; retry < SettleRetries && secondOutcome == PlaneBootstrapOutcome.Undecided; retry++)
            {
                secondOutcome = (await harness.Plane(1).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            Assert.IsTrue(
                firstOutcome is PlaneBootstrapOutcome.Bootstrapped or PlaneBootstrapOutcome.AlreadyBootstrapped,
                FormattableString.Invariant($"A founder in the race either bootstraps the chain or observes it already bootstrapped, and the first answered {firstOutcome}."));
            Assert.IsTrue(
                secondOutcome is PlaneBootstrapOutcome.Bootstrapped or PlaneBootstrapOutcome.AlreadyBootstrapped,
                FormattableString.Invariant($"A founder in the race either bootstraps the chain or observes it already bootstrapped, and the second answered {secondOutcome}."));
            int reportedBootstrapped = (firstOutcome == PlaneBootstrapOutcome.Bootstrapped ? 1 : 0) + (secondOutcome == PlaneBootstrapOutcome.Bootstrapped ? 1 : 0);
            if(ignoranceReported)
            {
                //The one branch the exact claim does not cover, entered by name so a run that took it says so.
                TestContext.WriteLine("A founder spent its whole attempt budget without observing a decision, so this run took the ignorant branch and the count of reported bootstraps is bounded rather than exact.");
                Assert.IsLessThan(
                    2,
                    reportedBootstrapped,
                    "The initial record is decided at one version and one version is decided once, so at most one founder can have committed it, and a founder answered ignorance about a write that may have landed.");
            }
            else
            {
                Assert.AreEqual(
                    1,
                    reportedBootstrapped,
                    "Neither founder reported ignorance, so the first version carries one writer's record, that writer was answered committed against a chain that had decided nothing, and the other adopted the winner: exactly one founder reports the bootstrap.");
            }

            Assert.IsTrue(
                rendezvous.EveryParticipantArrived,
                "Both founders reached the gate, so the outcomes above were taken by two writers that met at one version rather than by two writers whose windows never overlapped.");
            Assert.IsGreaterThan(
                1,
                Math.Max(harness.HighestAttemptsOf(0), harness.HighestAttemptsOf(1)),
                "One of the two founders was superseded at the first version and spent a further attempt recomposing on the winner, which is the observable a fully serialized execution cannot produce: a founder that starts after the other committed adopts the decided record and reports it already bootstrapped in one attempt.");

            CommittedMetadataRecord? committed = await harness.Plane(2).ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(committed, "The race leaves a committed record on the chain, read here off the founder that took no part in it.");
            Assert.AreEqual(
                VeritasMetadataRecord.Initial,
                committed!.Value,
                "Every founder proposes the same deterministic value, so the record the chain carries equals the initial record by value whichever founder decided it.");
        }
    }

    /// <summary>Two planes held at ONE version by the write rendezvous claim distinct axes, both claims land exactly once on the final record at two distinct versions, and the superseded writer spent a further attempt recomposing against the winner's record rather than replaying its own.</summary>
    [TestMethod]
    public async Task ContendedClaimsFromDistinctAxesEachLandExactlyOnce()
    {
        const int prioritySeed = 20260815;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            //Armed after the bootstrap, because the gate holds every record exchange a named writer makes and
            //a setup write would otherwise wait for a race that has not started.
            WriterRendezvous<int> rendezvous = harness.ArmWriteRendezvous(0, 1);

            Task<MetadataPlaneResult<IdentityClaimOutcome>> first = harness.Plane(0).ClaimIdentityAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken);
            Task<MetadataPlaneResult<IdentityClaimOutcome>> second = harness.Plane(1).ClaimIdentityAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken);

            IdentityClaimOutcome firstOutcome = (await first.ConfigureAwait(false)).Outcome;
            IdentityClaimOutcome secondOutcome = (await second.ConfigureAwait(false)).Outcome;

            for(int retry = 0; retry < SettleRetries && firstOutcome == IdentityClaimOutcome.Undecided; retry++)
            {
                firstOutcome = (await harness.Plane(0).ClaimIdentityAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            for(int retry = 0; retry < SettleRetries && secondOutcome == IdentityClaimOutcome.Undecided; retry++)
            {
                secondOutcome = (await harness.Plane(1).ClaimIdentityAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            //Either ladder value is a claim that landed in this writer's own name. The second arm is what a
            //writer sees when its own proposal was decided while its attempt assembled no quorum: it reports
            //ignorance, re-issues, and finds its own axis standing on the record it then adopts.
            Assert.IsTrue(
                firstOutcome is IdentityClaimOutcome.Claimed or IdentityClaimOutcome.AlreadyClaimedBySelf,
                FormattableString.Invariant($"A claim on an axis nobody else holds lands however the two writers were ordered, and its writer either takes it or finds it standing in its own name; the first answered {firstOutcome}."));
            Assert.IsTrue(
                secondOutcome is IdentityClaimOutcome.Claimed or IdentityClaimOutcome.AlreadyClaimedBySelf,
                FormattableString.Invariant($"The claim that was superseded recomputes against the winner's record, finds its own axis still absent, and lands too; the second answered {secondOutcome}."));

            Assert.IsTrue(
                rendezvous.EveryParticipantArrived,
                "Both writers reached the gate, so both claims addressed one version rather than two windows that never overlapped.");
            Assert.IsGreaterThan(
                1,
                Math.Max(harness.HighestAttemptsOf(0), harness.HighestAttemptsOf(1)),
                "One claim was superseded at the version both addressed and spent a further attempt recomposing on the winner's record, which is what a serialized execution never shows.");

            CommittedMetadataRecord? committed = await harness.Plane(0).ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(committed, "The two claims leave a committed record on the chain.");
            Assert.HasCount(2, committed!.Value.IdentityClaims, "Both axes stand claimed on one record, because a claim is appended and never rewritten.");
            Assert.AreEqual(1, ClaimsOf(committed.Value, harness.Axis(0)), "The first writer's axis is claimed exactly once, so the race appended no duplicate of it.");
            Assert.AreEqual(1, ClaimsOf(committed.Value, harness.Axis(1)), "The second writer's axis is claimed exactly once too.");
            Assert.AreNotEqual(
                committed.Value.IdentityClaims[0].ClaimedAt,
                committed.Value.IdentityClaims[1].ClaimedAt,
                "Each claim carries the version its own write was decided at, and two writes are decided at two versions, so a shared version here would be one write's effect written twice.");
        }
    }

    /// <summary>Two hosts electing THEMSELVES at once, held at one version by the write rendezvous, resolve to exactly one lease: the record carries one holder, that holder's own candidate took or refreshed it, and the other candidate is refused against a member the membership still lists.</summary>
    /// <remarks>
    /// The answers are checked against the record rather than the record against the answers, for the reason the
    /// bootstrap row states: an attempt that assembled no quorum reports ignorance even when what a quorum decided
    /// was its own proposal, so a candidate can hold the lease and still not be the one that reported taking it.
    /// </remarks>
    [TestMethod]
    public async Task ContendedSelfElectionsLeaveExactlyOneLease()
    {
        const int prioritySeed = 20260825;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            WriterRendezvous<int> rendezvous = harness.ArmWriteRendezvous(0, 1);

            Task<MetadataPlaneResult<CoordinatorElectionOutcome>> first = harness.Plane(0).ElectCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken);
            Task<MetadataPlaneResult<CoordinatorElectionOutcome>> second = harness.Plane(1).ElectCoordinatorAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken);

            CoordinatorElectionOutcome firstOutcome = (await first.ConfigureAwait(false)).Outcome;
            CoordinatorElectionOutcome secondOutcome = (await second.ConfigureAwait(false)).Outcome;

            for(int retry = 0; retry < SettleRetries && firstOutcome == CoordinatorElectionOutcome.Undecided; retry++)
            {
                firstOutcome = (await harness.Plane(0).ElectCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            for(int retry = 0; retry < SettleRetries && secondOutcome == CoordinatorElectionOutcome.Undecided; retry++)
            {
                secondOutcome = (await harness.Plane(1).ElectCoordinatorAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            Assert.IsTrue(
                rendezvous.EveryParticipantArrived,
                "Both candidates reached the gate, so both elections addressed the same vacant lease rather than one following the other.");
            Assert.IsGreaterThan(
                1,
                Math.Max(harness.HighestAttemptsOf(0), harness.HighestAttemptsOf(1)),
                "The candidate that lost the version was superseded there and spent a further attempt recomposing, which is where it learned the lease had been taken.");

            //The check reads the WITNESS first and the answers against it, rather than deciding from the answers
            //who won. A candidate whose own proposal was decided while its attempt assembled no quorum reports
            //ignorance and, on re-issue, finds the lease standing under its own axis and refreshes it, so the
            //holder is what says who won and the ladder value is only required to be consistent with that.
            CommittedMetadataRecord? committed = await harness.Plane(2).ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(committed, "The race leaves a committed record on the chain, read here off the member that took no part in it.");
            CoordinatorLease? lease = committed!.Value.Coordinator;
            Assert.IsNotNull(lease, "One lease stands on the record the two elections left behind.");
            Assert.IsTrue(
                lease!.Holder.Equals(harness.Axis(0)) || lease.Holder.Equals(harness.Axis(1)),
                "The standing lease is held under one of the two candidates' own axes, because each of them elected itself and nobody else was elected at all.");

            bool firstHolds = lease.Holder.Equals(harness.Axis(0));
            CoordinatorElectionOutcome holderOutcome = firstHolds ? firstOutcome : secondOutcome;
            CoordinatorElectionOutcome rivalOutcome = firstHolds ? secondOutcome : firstOutcome;

            Assert.IsTrue(
                holderOutcome is CoordinatorElectionOutcome.Elected or CoordinatorElectionOutcome.Refreshed,
                FormattableString.Invariant($"The candidate whose axis holds the standing lease took it or refreshed its own, and it answered {holderOutcome}."));
            Assert.AreEqual(
                CoordinatorElectionOutcome.HeldByOther,
                rivalOutcome,
                FormattableString.Invariant($"The other candidate is refused against a holder the membership still lists rather than usurping it, and it answered {rivalOutcome}."));
        }
    }

    /// <summary>Two replicas recording INDEPENDENT baseline intents at once, held at one version by the write rendezvous, resolve to one lineage: the record carries one digest and its claimant, and the replica whose digest it is not is refused a conflicting lineage — the independent-baseline storm closing under contention rather than in sequence.</summary>
    /// <remarks>
    /// The answers are checked against the record rather than the record against the answers, for the reason the
    /// bootstrap row states: a claimant whose intent was decided while its attempt assembled no quorum reports
    /// ignorance, and its re-issue meets its own byte-identical intent as the idempotent repeat.
    /// </remarks>
    [TestMethod]
    public async Task ContendedIndependentBaselineIntentsResolveToOneLineage()
    {
        const int prioritySeed = 20260826;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            NodeIdentifier firstDigest = new(0x11FF22EE33DD44CCUL);
            NodeIdentifier secondDigest = new(0x99AA88BB77CC66DDUL);

            WriterRendezvous<int> rendezvous = harness.ArmWriteRendezvous(0, 1);

            Task<MetadataPlaneResult<BaselineRecordOutcome>> first = harness.Plane(0).RecordBaselineIntentAsync(harness.Axis(0), firstDigest, AttemptBudget, TestContext.CancellationToken);
            Task<MetadataPlaneResult<BaselineRecordOutcome>> second = harness.Plane(1).RecordBaselineIntentAsync(harness.Axis(1), secondDigest, AttemptBudget, TestContext.CancellationToken);

            BaselineRecordOutcome firstOutcome = (await first.ConfigureAwait(false)).Outcome;
            BaselineRecordOutcome secondOutcome = (await second.ConfigureAwait(false)).Outcome;

            for(int retry = 0; retry < SettleRetries && firstOutcome == BaselineRecordOutcome.Undecided; retry++)
            {
                firstOutcome = (await harness.Plane(0).RecordBaselineIntentAsync(harness.Axis(0), firstDigest, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            for(int retry = 0; retry < SettleRetries && secondOutcome == BaselineRecordOutcome.Undecided; retry++)
            {
                secondOutcome = (await harness.Plane(1).RecordBaselineIntentAsync(harness.Axis(1), secondDigest, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false)).Outcome;
            }

            Assert.IsTrue(
                rendezvous.EveryParticipantArrived,
                "Both claimants reached the gate, so both intents addressed a record carrying no baseline rather than one intent seeing the other's.");
            Assert.IsGreaterThan(
                1,
                Math.Max(harness.HighestAttemptsOf(0), harness.HighestAttemptsOf(1)),
                "The intent that lost the version was superseded there and spent a further attempt recomposing, which is where it met the lineage that had been recorded.");

            //The check reads the WITNESS first and the answers against it. A claimant whose own intent was decided
            //while its attempt assembled no quorum reports ignorance and, on re-issue, meets its own byte-identical
            //intent and answers the idempotent repeat, so the digest on the record is what says whose lineage
            //stands and the ladder value is only required to be consistent with that.
            CommittedMetadataRecord? committed = await harness.Plane(2).ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(committed, "The race leaves a committed record on the chain.");
            LineageBaseline? baseline = committed!.Value.Baseline;
            Assert.IsNotNull(baseline, "One lineage stands on the record the two intents left behind.");
            Assert.IsFalse(baseline!.IsConfirmed, "An intent is recorded unconfirmed, which is the state a confirm later fills.");
            Assert.IsTrue(
                baseline.CausalityDigest == firstDigest || baseline.CausalityDigest == secondDigest,
                "The standing lineage is one of the two the claimants proposed, because no third digest was ever offered.");

            bool firstStands = baseline.CausalityDigest == firstDigest;
            Assert.AreEqual(
                firstStands ? harness.Axis(0) : harness.Axis(1),
                baseline.ClaimantAxis,
                "The standing lineage names as its claimant the replica whose digest it carries, so the two halves of one intent were written together.");

            BaselineRecordOutcome standingOutcome = firstStands ? firstOutcome : secondOutcome;
            BaselineRecordOutcome refusedOutcome = firstStands ? secondOutcome : firstOutcome;

            Assert.IsTrue(
                standingOutcome is BaselineRecordOutcome.Recorded or BaselineRecordOutcome.AlreadyRecorded,
                FormattableString.Invariant($"The claimant whose lineage stands recorded it or met its own byte-identical repeat, and it answered {standingOutcome}."));
            Assert.AreEqual(
                BaselineRecordOutcome.ConflictingLineage,
                refusedOutcome,
                FormattableString.Invariant($"The second independent intent is refused AT THE INTENT, which is where the independent-baseline storm closes, and it answered {refusedOutcome}."));
        }
    }

    /// <summary>A second plane claiming an axis another replica already claimed is refused by consensus at claim time, and its refusal changes nothing about the standing claim.</summary>
    [TestMethod]
    public async Task DuplicateClaimFromAnotherPlaneIsRefusedHeldByOther()
    {
        const int prioritySeed = 20260816;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<IdentityClaimOutcome> claimed = await harness.Plane(0).ClaimIdentityAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(IdentityClaimOutcome.Claimed, claimed.Outcome, "The first replica claims the axis it will mint under.");

            MetadataPlaneResult<IdentityClaimOutcome> refused = await harness.Plane(1).ClaimIdentityAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                IdentityClaimOutcome.RefusedHeldByOther,
                refused.Outcome,
                "A second minter under a claimed axis is refused at claim time by consensus rather than detected afterwards, once colliding dots have crossed the wire.");

            VeritasMetadataRecord? decided = refused.Record;
            Assert.IsNotNull(decided, "The refusal is a decided answer, so it names the record it was decided against.");
            Assert.HasCount(1, decided!.IdentityClaims, "The refused claim left the record as it found it: the axis is claimed exactly once.");
            Assert.AreEqual(harness.Axis(0), decided.IdentityClaims[0].Axis, "The standing claim is the one the first replica took.");
        }
    }

    /// <summary>An election queued on one plane while that plane's claim is STOPPED inside its own record exchange answers on its own ladder instead of meeting the register's refusal of a second write, and composes on the record the claim went on to decide.</summary>
    /// <remarks>
    /// The hold is what makes the second obligation's queueing simultaneous with the first's flight rather than
    /// merely hopeful. A write needs a quorum and a held writer reaches no member at all, so the claim
    /// demonstrably has not completed when the election is queued — which is the state the plane's write queue
    /// exists to make survivable, and a state that two calls started in a row need not reach at all.
    /// </remarks>
    [TestMethod]
    public async Task AnObligationQueuedWhileAnotherHoldsTheRegisterStillAnswers()
    {
        const int prioritySeed = 20260817;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneRecordHold hold = harness.HoldRecordExchanges(0);

            Task<MetadataPlaneResult<IdentityClaimOutcome>> claim = harness.Plane(0).ClaimIdentityAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken);

            //The arrival IS the transition rather than a guess at one: the claim is inside a record exchange
            //that reaches no member, so what is queued next is queued against an occupied register.
            await hold.Reached.WaitAsync(HoldBackstop, TimeProvider.System, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(
                claim.IsCompleted,
                "The claim is stopped inside its own record exchange and a write needs a quorum, so it has established nothing and the obligation queued next is queued while this one holds the register.");

            Task<MetadataPlaneResult<CoordinatorElectionOutcome>> election = harness.Plane(0).ElectCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken);
            hold.Release();

            MetadataPlaneResult<IdentityClaimOutcome> claimed = await claim.ConfigureAwait(false);
            MetadataPlaneResult<CoordinatorElectionOutcome> elected = await election.ConfigureAwait(false);

            Assert.AreEqual(
                IdentityClaimOutcome.Claimed,
                claimed.Outcome,
                "The plane serializes its own write initiation, so two different obligations in flight at once never meet the register's refusal to start a second write.");
            Assert.AreEqual(CoordinatorElectionOutcome.Elected, elected.Outcome, "The second obligation answered on its own ladder rather than failing behind the first.");
            Assert.IsGreaterThan(claimed.Version.Value, elected.Version.Value, "The queue is first-in-first-out, so the election was decided at a version after the claim's.");

            VeritasMetadataRecord? decided = elected.Record;
            Assert.IsNotNull(decided, "A committed election names the record it decided.");
            Assert.HasCount(1, decided!.IdentityClaims, "The election recomputed against the record the claim committed rather than against the one the plane held when both were queued.");
            Assert.IsNotNull(decided.Coordinator, "The election installed the lease on that same record.");
        }
    }

    /// <summary>The two-phase baseline walks its whole ladder: an intent is recorded, an identical retry changes nothing, a different one conflicts, the confirm fills the state, an identical reconfirm changes nothing and a different one conflicts.</summary>
    [TestMethod]
    public async Task BaselineIntentAndConfirmLadderWalksItsOutcomes()
    {
        const int prioritySeed = 20260818;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            VeritasMetadataPlane plane = harness.Plane(0);
            ReplicaAxis claimant = harness.Axis(0);
            NodeIdentifier digest = new(0xA51CE7B0C4D3E2F1UL);
            NodeIdentifier rivalDigest = new(0x0F1E2D3C4B5A6978UL);
            NodeIdentifier stateId = new(0xC0FFEE0BADF00D11UL);
            NodeIdentifier rivalStateId = new(0x0000000000000041UL);
            const long dictionaryEpoch = 7L;

            MetadataPlaneResult<BaselineRecordOutcome> recorded = await plane.RecordBaselineIntentAsync(claimant, digest, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.Recorded, recorded.Outcome, "An intent against a record carrying no baseline is recorded, which is where a lineage becomes an agreed fact.");

            MetadataPlaneResult<BaselineRecordOutcome> repeated = await plane.RecordBaselineIntentAsync(claimant, digest, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                BaselineRecordOutcome.AlreadyRecorded,
                repeated.Outcome,
                "Minting a baseline is deterministic given the identity and the present triples, so a replica that crashed between its intent and its own commit reproduces the digest and its retry is an identical repeat.");

            MetadataPlaneResult<BaselineRecordOutcome> rivalIntent = await plane.RecordBaselineIntentAsync(claimant, rivalDigest, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                BaselineRecordOutcome.ConflictingLineage,
                rivalIntent.Outcome,
                "A second independent intent for the lineage is refused at the intent, which is where the independent-baseline storm closes.");

            MetadataPlaneResult<BaselineRecordOutcome> confirmed = await plane.ConfirmBaselineAsync(digest, stateId, dictionaryEpoch, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.Confirmed, confirmed.Outcome, "The confirm matched its intent by a byte-identical digest and filled the state the durable commit produced.");

            VeritasMetadataRecord? decided = confirmed.Record;
            Assert.IsNotNull(decided, "A committed confirm names the record it decided.");
            LineageBaseline? baseline = decided!.Baseline;
            Assert.IsNotNull(baseline, "The decided record carries the baseline the confirm amended.");
            Assert.IsTrue(baseline!.IsConfirmed, "A confirmed baseline is what a clone gates on, and the tri-state says so structurally rather than by a zero sentinel.");
            Assert.AreEqual(stateId, baseline.StateId!.Value, "The confirmed baseline carries the dataset state the commit produced.");
            Assert.AreEqual(dictionaryEpoch, baseline.DictionaryEpoch!.Value, "The confirmed baseline carries the dictionary epoch it was written under.");
            Assert.AreEqual(digest, baseline.CausalityDigest, "The confirm amended the intent it matched rather than starting a second lineage.");

            MetadataPlaneResult<BaselineRecordOutcome> reconfirmed = await plane.ConfirmBaselineAsync(digest, stateId, dictionaryEpoch, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(BaselineRecordOutcome.AlreadyRecorded, reconfirmed.Outcome, "An identical reconfirmation is the same idempotent repeat one phase later.");

            MetadataPlaneResult<BaselineRecordOutcome> rivalConfirm = await plane.ConfirmBaselineAsync(digest, rivalStateId, dictionaryEpoch, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                BaselineRecordOutcome.ConflictingLineage,
                rivalConfirm.Outcome,
                "A confirmation that would overwrite an already-filled state with a different one is refused loudly, because the alternative is two lineages agreeing to disagree.");
        }
    }

    /// <summary>A confirm against a record carrying no baseline records the confirmed baseline whole, naming the confirming replica the claimant, which is the arm that closes the fail-open path an undecided intent leaves.</summary>
    [TestMethod]
    public async Task ConfirmWithoutIntentRecordsTheBaselineWhole()
    {
        const int prioritySeed = 20260819;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            NodeIdentifier digest = new(0xBADC0FFEE0DDF00DUL);
            NodeIdentifier stateId = new(0x123456789ABCDEF0UL);
            const long dictionaryEpoch = 3L;

            MetadataPlaneResult<BaselineRecordOutcome> confirmed = await harness.Plane(0)
                .ConfirmBaselineAsync(digest, stateId, dictionaryEpoch, AttemptBudget, TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.AreEqual(
                BaselineRecordOutcome.Confirmed,
                confirmed.Outcome,
                "An intent that answered undecided lets the open proceed by the plane's own liveness rule, so the commit that followed one must still be recordable afterwards.");

            VeritasMetadataRecord? decided = confirmed.Record;
            Assert.IsNotNull(decided, "A committed confirm names the record it decided.");
            LineageBaseline? baseline = decided!.Baseline;
            Assert.IsNotNull(baseline, "The confirm recorded the baseline whole rather than refusing for want of an intent.");
            Assert.IsTrue(baseline!.IsConfirmed, "What it recorded is a confirmed baseline and not an intent.");
            Assert.AreEqual(harness.Axis(0), baseline.ClaimantAxis, "The confirming replica names itself the claimant when it records the baseline whole.");
            Assert.AreEqual(digest, baseline.CausalityDigest, "The recorded baseline carries the digest the confirm was matched by.");
            Assert.AreEqual(stateId, baseline.StateId!.Value, "The recorded baseline carries the dataset state the commit produced.");
        }
    }

    /// <summary>The coordinator lease is taken by a member, refreshed at a new term by its holder, refused to a rival while a current member holds it, released only by its holder, and then taken by the rival.</summary>
    [TestMethod]
    public async Task CoordinatorLeaseTakesRefreshesRefusesAndReleases()
    {
        const int prioritySeed = 20260820;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<CoordinatorElectionOutcome> elected = await harness.Plane(0).ElectCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CoordinatorElectionOutcome.Elected, elected.Outcome, "A vacant lease is taken by any member.");
            VeritasMetadataRecord? takenRecord = elected.Record;
            Assert.IsNotNull(takenRecord, "A committed election names the record it decided.");
            Assert.IsNotNull(takenRecord!.Coordinator, "The decided record carries the lease the election installed.");

            MetadataPlaneResult<CoordinatorElectionOutcome> refreshed = await harness.Plane(0).ElectCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CoordinatorElectionOutcome.Refreshed, refreshed.Outcome, "A lease already held under this axis is refreshed rather than taken again.");
            VeritasMetadataRecord? refreshedRecord = refreshed.Record;
            Assert.IsNotNull(refreshedRecord, "A committed refresh names the record it decided.");
            Assert.IsGreaterThan(
                takenRecord.Coordinator!.Term.Value,
                refreshedRecord!.Coordinator!.Term.Value,
                "A term is a register version and never a clock reading, so a refresh moves it forward by writing.");

            MetadataPlaneResult<CoordinatorElectionOutcome> refused = await harness.Plane(1).ElectCoordinatorAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CoordinatorElectionOutcome.HeldByOther, refused.Outcome, "A lease held by another CURRENT member is not usurped; the living holder keeps it.");

            MetadataPlaneResult<CoordinatorElectionOutcome> released = await harness.Plane(0).ReleaseCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CoordinatorElectionOutcome.Released, released.Outcome, "Only a holder releases its own lease, and this release was the holder's.");
            VeritasMetadataRecord? releasedRecord = released.Record;
            Assert.IsNotNull(releasedRecord, "A committed release names the record it decided.");
            Assert.IsNull(releasedRecord!.Coordinator, "A released lease is vacant on the record consensus decided.");

            MetadataPlaneResult<CoordinatorElectionOutcome> succeeded = await harness.Plane(1).ElectCoordinatorAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CoordinatorElectionOutcome.Elected, succeeded.Outcome, "The vacated lease is taken by the member that was refused while it stood.");
            VeritasMetadataRecord? succeededRecord = succeeded.Record;
            Assert.IsNotNull(succeededRecord, "A committed election names the record it decided.");
            Assert.AreEqual(harness.Axis(1), succeededRecord!.Coordinator!.Holder, "The lease is now held under the succeeding member's own axis.");
        }
    }

    /// <summary>Retiring the lease holder from the membership is what unlocks its lease: the succession refusal a rival earns against a living member turns into an election once the holder is no longer listed.</summary>
    [TestMethod]
    public async Task RetiringTheHolderUnlocksTheCoordinatorLease()
    {
        const int prioritySeed = 20260821;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<CoordinatorElectionOutcome> held = await harness.Plane(0).ElectCoordinatorAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CoordinatorElectionOutcome.Elected, held.Outcome, "The first member takes the vacant lease.");

            RegisterReadiness readiness = await harness.Plane(1).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsTrue(
                readiness.QuorumHasLearned(held.Version),
                "A membership change is gated on a quorum having learned the record it composes from, and dissemination is a durable learn, so the gate clears before the retirement is written.");

            MetadataPlaneResult<MembershipChangeOutcome> retired = await harness.Plane(1).RetireMemberAsync(harness.Axis(0), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(MembershipChangeOutcome.Changed, retired.Outcome, "The retirement is decided under the outgoing membership, which still lists the replica being retired.");

            MetadataPlaneResult<CoordinatorElectionOutcome> usurped = await harness.Plane(1).ElectCoordinatorAsync(harness.Axis(1), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                CoordinatorElectionOutcome.Elected,
                usurped.Outcome,
                "A lease held by a replica the membership no longer lists is taken over, which is what ties usurpation to the retirement obligation the plane already coordinates.");

            VeritasMetadataRecord? decided = usurped.Record;
            Assert.IsNotNull(decided, "A committed election names the record it decided.");
            Assert.AreEqual(harness.Axis(1), decided!.Coordinator!.Holder, "The lease is now held under the succeeding member's own axis.");
        }
    }

    /// <summary>
    /// The decommission gate ACROSS THE CHANGE BOUNDARY: a quorum has learned the record the retirement composes
    /// from before it is written, the retirement installs a membership the retired replica is no longer part of,
    /// and the gate an operator decommissions on is answered over THAT membership — cleared by a quorum that
    /// excludes the retired replica, refused for a version the chain has not decided, and refused again once one
    /// of the two members that remain answers nothing.
    /// </summary>
    /// <remarks>
    /// The report taken after the boundary carries no entry for the retired replica at all, which is what "a
    /// quorum that excludes it" is as an observation rather than as a subtraction the caller performs: the report
    /// is measured over the membership the change installed. A quorum of two members is both of them, so cutting
    /// one member's probe is what makes the gate refuse a version every remaining member has in fact learned —
    /// only the probe is cut, so what the gate loses is an answer and never a version, and it cleared on that
    /// same version a moment earlier.
    /// </remarks>
    [TestMethod]
    public async Task RetirementClearsItsDecommissionGateOnlyOnAQuorumExcludingTheRetiredMember()
    {
        const int prioritySeed = 20260830;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, which is the record the retirement composes from.");

            RegisterReadiness before = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(3, before.Members, "Before the boundary the report is measured over the outgoing membership, which still lists the replica about to be retired.");
            Assert.IsTrue(
                ReadinessOf(before, harness.Axis(2)).Reachable,
                "The replica about to be retired answers its own probe while the membership lists it, so what the report says after the change is about the change and not about a host that was already silent.");
            Assert.IsTrue(
                before.QuorumHasLearned(bootstrapped.Version),
                "A membership change is gated on a quorum having learned the record it composes from, and that gate clears before the retirement is written.");

            MetadataPlaneResult<MembershipChangeOutcome> retired = await harness.Plane(0).RetireMemberAsync(harness.Axis(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(MembershipChangeOutcome.Changed, retired.Outcome, "The retirement is decided under the outgoing membership, which still lists the replica being retired.");

            RegisterReadiness after = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(2, after.Members, "After the boundary the report is measured over the membership the change installed.");
            Assert.AreEqual(2, after.Reachable, "Both members of the installed membership answered their own probes.");
            Assert.IsFalse(
                ReportsEntryFor(after, harness.Axis(2)),
                "The report carries no entry for the retired replica, which is what makes the gate below a quorum that EXCLUDES it rather than a count an operator has to subtract from.");

            Assert.IsTrue(
                after.QuorumHasLearned(retired.Version),
                "The decommission gate clears once a quorum of the installed membership has learned the record that removed the replica, which is what an operator decommissions a host on.");
            Assert.IsFalse(
                after.QuorumHasLearned(retired.Version.Next()),
                "The gate answers about the version it was asked about, and no member has learned a version the chain has not decided.");

            //The same gate from the point of view of the member that took no part in writing the change. It is
            //measured over the membership the RECORD installs, so a member that only learned that record reads
            //the same report as the one that wrote it.
            RegisterReadiness fromFellow = await harness.Plane(1).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(2, fromFellow.Members, "The member that did not write the change measures over the membership that change installed, because it learned the record that installed it.");
            Assert.IsFalse(
                ReportsEntryFor(fromFellow, harness.Axis(2)),
                "The retired replica is absent from the fellow's report too, so the exclusion is the record's and not one writer's memory of what it asked for.");
            Assert.IsTrue(
                fromFellow.QuorumHasLearned(retired.Version),
                "The decommission gate clears for either surviving member, which is what lets an operator read it off whichever host it reaches.");

            //Only the probe is cut, so the member keeps its record and keeps serving: what the gate loses here is
            //an answer and never a version.
            harness.CutVersionProbe(1);

            RegisterReadiness silent = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(1, silent.Reachable, "One of the two members that remain answered nothing.");
            Assert.IsFalse(
                silent.QuorumHasLearned(retired.Version),
                "One member of two is not a quorum, so the gate refuses rather than clearing against a membership that mostly answered nothing — and it cleared on this very version a moment earlier, so the refusal is the silence.");
        }
    }

    /// <summary>A membership change on a chain that decided nothing reports the bootstrap it needs as a value; after bootstrap the admission lands, and repeating it costs no round at all.</summary>
    [TestMethod]
    public async Task MembershipChangeRequiresBootstrapThenAdmitsOnce()
    {
        const int prioritySeed = 20260822;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            //The joiner is an axis this bench runs no host for, which is what this row is about: an admission is
            //decided by the OUTGOING membership, so it lands whether or not anybody is hosting the joiner yet. A
            //joiner that is running, catches up and then writes is the row beside it.
            MetadataFounder joiner = MetadataPlaneHarness.FounderFor(2);

            MetadataPlaneResult<MembershipChangeOutcome> premature = await harness.Plane(0).AdmitMemberAsync(joiner, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                MembershipChangeOutcome.RequiresBootstrap,
                premature.Outcome,
                "A reconfiguration carries the committed value forward and a chain that decided nothing has none, which is reported as a value the operator retries on rather than as the register's own refusal.");
            Assert.AreEqual(RegisterVersion.Unwritten, premature.Version, "The pre-bootstrap answer establishes nothing and names no version.");
            Assert.IsNull(premature.Record, "The pre-bootstrap answer establishes nothing and names no record.");

            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<MembershipChangeOutcome> admitted = await harness.Plane(0).AdmitMemberAsync(joiner, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                MembershipChangeOutcome.Changed,
                admitted.Outcome,
                "The admission is decided by a quorum of the OUTGOING membership, so a joiner nobody hosts yet never has to answer for its own admission.");

            MetadataPlaneResult<MembershipChangeOutcome> repeated = await harness.Plane(0).AdmitMemberAsync(joiner, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                MembershipChangeOutcome.Unchanged,
                repeated.Outcome,
                "A delta that computes the membership it was given asks for a state the chain is already in, and a consensus instance run to decide that would cost a round and change nothing.");
            Assert.AreEqual(admitted.Version, repeated.Version, "The repeat names the record that installed the membership rather than deciding a new one.");
        }
    }

    /// <summary>A joiner this bench RUNS A HOST FOR is admitted, catches up through the dissemination the admission owed it — which its own readiness entry reports — and then writes an obligation of its own that commits under the membership that admitted it.</summary>
    [TestMethod]
    public async Task AnAdmittedMemberCatchesUpAndThenWritesItsOwnObligation()
    {
        const int prioritySeed = 20260827;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        //The joiner runs a host and a plane from the start and is simply not a founder, which is the deployment
        //an admission describes: the replica exists, is admitted, is disseminated to, and only then writes.
        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 1, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<IdentityClaimOutcome> beforeAdmission = await harness.Plane(2).ClaimIdentityAsync(harness.Axis(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                IdentityClaimOutcome.OutsideConfiguration,
                beforeAdmission.Outcome,
                "The joiner is refused before it is admitted, which is what makes the write it lands afterwards evidence about the admission rather than about a replica that could always write.");

            MetadataPlaneResult<MembershipChangeOutcome> admitted = await harness.Plane(0).AdmitMemberAsync(MetadataPlaneHarness.FounderFor(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(MembershipChangeOutcome.Changed, admitted.Outcome, "The admission is decided by a quorum of the outgoing membership.");

            RegisterReadiness readiness = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(3, readiness.Members, "The report is measured over the membership the admission installed, which lists the joiner.");
            Assert.AreEqual(3, readiness.Reachable, "Every member of the installed membership answered, the joiner included.");

            MemberReadiness joiner = ReadinessOf(readiness, harness.Axis(2));
            Assert.IsTrue(joiner.Reachable, "The joiner answered its own version probe, so it is reported reachable rather than as a member nothing reaches.");
            Assert.AreEqual(
                admitted.Version,
                joiner.Version!.Value,
                "The joiner caught up to the record that admitted it, because the audience a membership change is disseminated to is the union of the outgoing membership and the incoming one.");
            Assert.IsTrue(
                readiness.QuorumHasLearned(admitted.Version),
                "A quorum of the installed membership has learned the record the next write composes from, which is the gate the operator waits on before writing again.");

            MetadataPlaneResult<IdentityClaimOutcome> claimed = await harness.Plane(2).ClaimIdentityAsync(harness.Axis(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                IdentityClaimOutcome.Claimed,
                claimed.Outcome,
                "The admitted replica writes its own obligation through the membership that now lists it, which is what an admission is for.");

            VeritasMetadataRecord? decided = claimed.Record;
            Assert.IsNotNull(decided, "A committed claim names the record it decided.");
            Assert.AreEqual(1, ClaimsOf(decided!, harness.Axis(2)), "The joiner's axis stands claimed exactly once on the record its own write decided.");

            CommittedMetadataRecord? committed = await harness.Plane(1).ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(committed, "The founder that did not write reads the chain the joiner advanced.");
            Assert.AreEqual(
                claimed.Version,
                committed!.Version,
                "The joiner's write is the chain's latest version as a founder sees it, so the joiner wrote through the same chain rather than beside it.");
        }
    }

    /// <summary>A retired replica's OWN next write answers the outside-configuration value of its ladder and spends no consensus attempt, because the record that retired it is the record it learned.</summary>
    [TestMethod]
    public async Task ARetiredMembersOwnNextWriteAnswersOutsideTheConfiguration()
    {
        const int prioritySeed = 20260828;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<IdentityClaimOutcome> whileListed = await harness.Plane(2).ClaimIdentityAsync(harness.Axis(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(
                IdentityClaimOutcome.Claimed,
                whileListed.Outcome,
                "The replica writes while the membership lists it, so the refusal below is the retirement's doing and not a replica that never could write.");

            MetadataPlaneResult<MembershipChangeOutcome> retired = await harness.Plane(0).RetireMemberAsync(harness.Axis(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(MembershipChangeOutcome.Changed, retired.Outcome, "The retirement is decided under the outgoing membership, which still lists the replica being retired.");

            //The retired replica is in the OUTGOING membership, so the record that removed it was disseminated
            //to it: a replica learns it is out from the very record that put it out.
            CommittedMetadataRecord? learned = harness.Plane(2).HostCommitted;
            Assert.IsNotNull(learned, "The retired replica's host holds a record.");
            Assert.AreEqual(retired.Version, learned!.Version, "The record the retired replica holds is the one that retired it.");

            MetadataPlaneResult<PolicyAmendmentOutcome> afterRetirement = await harness.Plane(2)
                .AmendPolicyAsync(new CoordinationPolicy(HealCadenceClass: 5, SymbolBudgetTier: 6), AttemptBudget, TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.AreEqual(
                PolicyAmendmentOutcome.OutsideConfiguration,
                afterRetirement.Outcome,
                "A retired replica's own next write is refused on a settled fact about itself rather than on an unlucky round, so the answer is its own ladder value and not ignorance.");
            Assert.IsNull(afterRetirement.Record, "A write that established nothing carries no record.");
            Assert.AreEqual(RegisterVersion.Unwritten, afterRetirement.Version, "A write that established nothing names no version.");

            IReadOnlyList<MetadataPlaneTraceEvent> emitted = harness.TraceOf(2);
            MetadataPlaneTraceEvent refusal = emitted[^1];
            Assert.AreEqual(MetadataPlaneObligation.PolicyAmendment, refusal.Obligation, "The last verdict this replica emitted is the amendment it was refused.");
            Assert.AreEqual((int)PolicyAmendmentOutcome.OutsideConfiguration, refusal.OutcomeCode, "The emitted verdict carries the ladder value the caller was answered with.");
            Assert.AreEqual(0, refusal.Attempts, "The membership is classified before anything is proposed, so a retired replica's write costs the cluster nothing at all.");

            MetadataPlaneResult<PolicyAmendmentOutcome> fromSurvivor = await harness.Plane(0)
                .AmendPolicyAsync(new CoordinationPolicy(HealCadenceClass: 5, SymbolBudgetTier: 6), AttemptBudget, TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.AreEqual(
                PolicyAmendmentOutcome.Amended,
                fromSurvivor.Outcome,
                "The two members that remain still decide, so the refusal above was about the retired replica and not about a cluster that had lost its quorum.");
        }
    }

    /// <summary>The readiness report separates its two silences and refuses a misrouted probe: a member that has learned nothing answers unwritten and is reachable, a member whose probe is cut answers nothing and is reported unreachable while a quorum gate still clears, and a probe another host answers fails the report loudly.</summary>
    [TestMethod]
    public async Task ReadinessSeparatesUnreachableFromUnwrittenAndRefusesAMisroutedProbe()
    {
        const int prioritySeed = 20260829;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            RegisterReadiness fresh = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(3, fresh.Members, "The report carries one entry per member of the membership it was measured over.");
            Assert.AreEqual(3, fresh.Reachable, "Every host of a chain nobody has bootstrapped still answers, because having learned nothing is an answer.");
            foreach(MemberReadiness entry in fresh.Members)
            {
                Assert.IsTrue(entry.Reachable, FormattableString.Invariant($"Member {entry.Member} answered its probe before anything was decided."));
                Assert.AreEqual(
                    RegisterVersion.Unwritten,
                    entry.Version!.Value,
                    FormattableString.Invariant($"Member {entry.Member} has learned nothing, which is the unwritten version and never the absent answer a silent host gives."));
            }

            Assert.IsFalse(
                fresh.QuorumHasLearned(RegisterVersion.First),
                "No member has learned the first version yet, so the gate a write at the second version needs does not clear.");

            MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, which is what gives the members a version to report.");

            RegisterReadiness learned = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(3, learned.Reachable, "Every member still answers.");
            foreach(MemberReadiness entry in learned.Members)
            {
                Assert.AreEqual(
                    bootstrapped.Version,
                    entry.Version!.Value,
                    FormattableString.Invariant($"Member {entry.Member} learned the decided record through the dissemination the write awaited before it returned."));
            }

            //Only the probe is cut. The member keeps its record and keeps serving, which is what makes the
            //entry below a statement about one query rather than about a host that is gone.
            harness.CutVersionProbe(2);

            RegisterReadiness cut = await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(2, cut.Reachable, "The member whose probe is cut answered nothing, so two of three members are reachable.");

            MemberReadiness silent = ReadinessOf(cut, harness.Axis(2));
            Assert.IsFalse(silent.Reachable, "A member that did not answer is unreachable.");
            Assert.IsNull(
                silent.Version,
                "A member that did not answer carries no version at all, which is what keeps it distinguishable from the unwritten answer a host that has learned nothing gives.");
            Assert.IsTrue(
                cut.QuorumHasLearned(bootstrapped.Version),
                "The two members that answered are a quorum of three and both had learned the decided version, so the gate clears with one member silent.");

            //Two entries of the endpoint map landing on one host: the probe aimed at the second member reaches
            //the first, which answers for itself.
            harness.MisrouteVersionProbe(1, 0);

            ConsensusRefusedException refused = await Assert.ThrowsExactlyAsync<ConsensusRefusedException>(
                async () => await harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken).ConfigureAwait(false),
                "A probe answered by another host fails the report loudly rather than being counted, because a report counted over distinct members would otherwise let one replica fill two slots.").ConfigureAwait(false);

            Assert.AreEqual(
                ConsensusRefusal.ProbeAnsweredByAnotherMember,
                refused.Refusal,
                "The refusal names the rule it fired on as a VALUE, so a caller acting on it reads the rule rather than the sentence.");
            Assert.Contains(
                "was answered by",
                refused.Message,
                "The refusal names the mis-wiring it found: which member was asked and which host answered.");
            Assert.Contains(
                "endpoint map",
                refused.Message,
                "The refusal names the deployment fault rather than reporting the member unreachable.");
        }
    }

    /// <summary>A readiness report one member's probe hangs in — answering nothing and honouring no token — still COMPLETES: that member is the unreachable entry and every other member reports the version it learned, because each probe is raced against the plane's per-member deadline rather than waited on.</summary>
    /// <remarks>
    /// This is the row a probe that merely refused could not stand in for. A cut probe answers with a fault, so a
    /// report assembled from it proves only that a fault becomes an unreachable entry; a hung probe hands the
    /// report nothing at all and observes no cancellation, so the report can be assembled only because the plane
    /// gave up on that member of its own accord. A plane that told the probe about its deadline instead of racing
    /// it would never answer here.
    /// </remarks>
    [TestMethod]
    public async Task ReadinessCompletesWhenOneProbeHangsAndReportsThatMemberUnreachable()
    {
        const int prioritySeed = 20260901;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, which is what gives the members a version to report.");

            MetadataPlaneProbeHang hung = harness.HangVersionProbe(2);

            //The read is started rather than awaited, because the transition this row is about happens while it
            //is in flight: the probe reports that it was entered, and only then is the deadline a thing the clock
            //can be moved past.
            Task<RegisterReadiness> reading = harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken);
            await hung.Reached.WaitAsync(HoldBackstop, TestContext.CancellationToken).ConfigureAwait(false);
            harness.AdvancePastMemberQueryDeadline();

            RegisterReadiness report = await reading.WaitAsync(HoldBackstop, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(3, report.Members, "The report carries one entry per member of the membership it was measured over, the hung one included.");
            Assert.AreEqual(2, report.Reachable, "The member whose probe hangs answered nothing, so two of three members are reachable.");

            MemberReadiness silent = ReadinessOf(report, harness.Axis(2));
            Assert.IsFalse(silent.Reachable, "A member that answered nothing at all is unreachable.");
            Assert.IsNull(silent.Version, "A member that answered nothing carries no version, which is what keeps it distinguishable from the unwritten answer a host that has learned nothing gives.");

            Assert.AreEqual(
                bootstrapped.Version,
                ReadinessOf(report, harness.Axis(0)).Version!.Value,
                "The first member answered its own probe, so one member's silence cost the report that member and nothing else.");
            Assert.AreEqual(
                bootstrapped.Version,
                ReadinessOf(report, harness.Axis(1)).Version!.Value,
                "The second member answered its own probe too, on the same rule.");
            Assert.IsTrue(
                report.QuorumHasLearned(bootstrapped.Version),
                "The two members that answered are a quorum of three and both had learned the decided version, so a gate clears with one member hung.");
        }
    }

    /// <summary>The per-member deadline is spent PER MEMBER: a member hung FIRST in the membership order costs the report its own entry alone, and every member asked after it still reports the version it learned.</summary>
    /// <remarks>
    /// A deadline bounding the REPORT rather than each member would drain on the first hang and mark every member
    /// after it unreachable — a report about the caller's patience instead of about those members, and a
    /// decommission gate reading it would see a cluster that had answered nothing. The hang is placed first
    /// deliberately, because a hang placed last cannot tell the two spends apart.
    /// </remarks>
    [TestMethod]
    public async Task ReadinessSpendsItsDeadlinePerMemberSoAnEarlyHangCostsNoLaterMember()
    {
        const int prioritySeed = 20260902;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, which is what gives the members a version to report.");

            //The founders are in axis order, so the replica this row hangs is the FIRST member the report asks.
            MetadataPlaneProbeHang hung = harness.HangVersionProbe(0);

            Task<RegisterReadiness> reading = harness.Plane(1).ReadReadinessAsync(TestContext.CancellationToken);
            await hung.Reached.WaitAsync(HoldBackstop, TestContext.CancellationToken).ConfigureAwait(false);
            harness.AdvancePastMemberQueryDeadline();

            RegisterReadiness report = await reading.WaitAsync(HoldBackstop, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(3, report.Members, "The report carries one entry per member of the membership it was measured over.");
            Assert.IsFalse(ReadinessOf(report, harness.Axis(0)).Reachable, "The member whose probe hangs is the unreachable entry.");

            Assert.AreEqual(
                bootstrapped.Version,
                ReadinessOf(report, harness.Axis(1)).Version!.Value,
                "The member asked AFTER the hung one still reports the version it learned, so the deadline the hang spent was that member's own and not the report's.");
            Assert.AreEqual(
                bootstrapped.Version,
                ReadinessOf(report, harness.Axis(2)).Version!.Value,
                "The member asked last reports its version too, so no member was marked unreachable for a silence that was not its own.");
            Assert.AreEqual(2, report.Reachable, "Exactly the hung member is missing from the report's reachable count.");
        }
    }

    /// <summary>A hung member and a faulting member produce the IDENTICAL entry: silence and a fault are one state, and a gate reading the report is given no third one to act differently on.</summary>
    /// <remarks>
    /// The report exists to say whether a named replica has learned a version, and a host that answered nothing
    /// and a host that refused answer that with the same thing — this member did not tell us. Two entries that
    /// differed would offer a gate a distinction it cannot act on and could only mis-read as partial information.
    /// </remarks>
    [TestMethod]
    public async Task AHungProbeAndACutProbeProduceTheIdenticalEntry()
    {
        const int prioritySeed = 20260903;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 3, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, so both silenced members had a version to report and withheld it in different ways.");

            MetadataPlaneProbeHang hung = harness.HangVersionProbe(1);
            harness.CutVersionProbe(2);

            Task<RegisterReadiness> reading = harness.Plane(0).ReadReadinessAsync(TestContext.CancellationToken);
            await hung.Reached.WaitAsync(HoldBackstop, TestContext.CancellationToken).ConfigureAwait(false);
            harness.AdvancePastMemberQueryDeadline();

            RegisterReadiness report = await reading.WaitAsync(HoldBackstop, TestContext.CancellationToken).ConfigureAwait(false);

            MemberReadiness silent = ReadinessOf(report, harness.Axis(1));
            MemberReadiness refused = ReadinessOf(report, harness.Axis(2));

            Assert.AreEqual(refused.Reachable, silent.Reachable, "A member that answered nothing and a member that refused are reachable to exactly the same degree.");
            Assert.AreEqual(refused.Version, silent.Version, "Neither carries a version, so neither can be mistaken for a host that has learned nothing.");
            Assert.AreEqual(
                refused with { Member = silent.Member },
                silent,
                "Set aside the member each entry names and the two are equal by value, so the report offers a decommission gate no third state to act differently on.");
            Assert.AreEqual(1, report.Reachable, "Only the reading replica answered, which is short of the quorum of three a gate needs.");
            Assert.IsFalse(
                report.QuorumHasLearned(bootstrapped.Version),
                "Two silenced members hold the gate CLOSED, which is the direction interference must point: it can cost availability and never clear a gate that should not have cleared.");
        }
    }

    /// <summary>A plane whose own axis the membership does not list is refused on a settled fact about itself, and the refusal spends no consensus attempt.</summary>
    [TestMethod]
    public async Task WriteFromOutsideTheMembershipSpendsNothing()
    {
        const int prioritySeed = 20260823;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 1, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            MetadataPlaneResult<IdentityClaimOutcome> outside = await harness.Plane(2).ClaimIdentityAsync(harness.Axis(2), AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(
                IdentityClaimOutcome.OutsideConfiguration,
                outside.Outcome,
                "A replica the membership does not list is refused on a settled fact about itself rather than on an unlucky round, so the answer is its own ladder value and not ignorance.");
            Assert.IsNull(outside.Record, "A write that established nothing carries no record.");
            Assert.AreEqual(RegisterVersion.Unwritten, outside.Version, "A write that established nothing names no version.");

            IReadOnlyList<MetadataPlaneTraceEvent> emitted = harness.TraceOf(2);
            Assert.HasCount(1, emitted, "The outsider's plane emitted exactly the one obligation it was asked for.");
            Assert.AreEqual(MetadataPlaneObligation.IdentityClaim, emitted[0].Obligation, "The emitted verdict names the obligation that earned it.");
            Assert.AreEqual((int)IdentityClaimOutcome.OutsideConfiguration, emitted[0].OutcomeCode, "The emitted verdict carries the ladder value the caller was answered with.");
            Assert.AreEqual(0, emitted[0].Attempts, "The membership is classified before anything is proposed and before any delay is waited, so the refusal spends no consensus attempt at all.");
        }
    }

    /// <summary>A policy amendment installs the agreed facts and a repeat of the same amendment reports the record unchanged, still spending a version because every obligation writes.</summary>
    [TestMethod]
    public async Task PolicyAmendmentInstallsThenReportsUnchanged()
    {
        const int prioritySeed = 20260824;
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {prioritySeed}."));

        MetadataPlaneHarness harness = new(founderCount: 2, outsiderCount: 0, prioritySeed: prioritySeed, attemptsPerRecorder: AttemptsPerRecorder);
        await using(harness.ConfigureAwait(false))
        {
            _ = await harness.Plane(0).BootstrapAsync(AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

            CoordinationPolicy amended = new(HealCadenceClass: 2, SymbolBudgetTier: 3);

            MetadataPlaneResult<PolicyAmendmentOutcome> installed = await harness.Plane(0).AmendPolicyAsync(amended, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PolicyAmendmentOutcome.Amended, installed.Outcome, "An amendment naming a policy the record does not carry installs it.");
            VeritasMetadataRecord? decided = installed.Record;
            Assert.IsNotNull(decided, "A committed amendment names the record it decided.");
            Assert.AreEqual(amended, decided!.Policy, "The agreed policy is the value the amendment named, read off the record consensus decided rather than off this host's configuration.");

            MetadataPlaneResult<PolicyAmendmentOutcome> repeated = await harness.Plane(0).AmendPolicyAsync(amended, AttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(PolicyAmendmentOutcome.Unchanged, repeated.Outcome, "An amendment naming the policy the record already carries changes nothing.");
            Assert.IsGreaterThan(
                installed.Version.Value,
                repeated.Version.Value,
                "The idempotent amendment still wrote, which is what buys an answer a quorum decided rather than one this host happened to hold.");
        }
    }

    /// <summary>The decided record survives the consensus JSON codec by value: what was decoded equals what was encoded on the record's hand-written element-wise equality, and does so as a different instance.</summary>
    [TestMethod]
    public void MetadataRecordSurvivesTheVersionedJsonCodecByValue()
    {
        ReplicaAxis claimant = MetadataPlaneHarness.AxisFor(0);
        ReplicaAxis holder = MetadataPlaneHarness.AxisFor(1);
        MetadataPlaneDeployment deployment = MetadataPlaneDeployment.Create([MetadataPlaneHarness.FounderFor(0), MetadataPlaneHarness.FounderFor(1)]);

        VeritasMetadataRecord original = new(
            IdentityClaims: [new ReplicaIdentityClaim(claimant, new RegisterVersion(1UL)), new ReplicaIdentityClaim(holder, new RegisterVersion(4UL))],
            Baseline: new LineageBaseline(claimant, new NodeIdentifier(0xF1E2D3C4B5A69788UL), new LineageConfirmation(new NodeIdentifier(0x8000000000000001UL), 42L), new RegisterVersion(6UL)),
            Policy: new CoordinationPolicy(HealCadenceClass: 2, SymbolBudgetTier: 3),
            Coordinator: new CoordinatorLease(holder, new RegisterVersion(9UL)));

        CommittedMetadataRecord committed = new(new RegisterVersion(9UL), MetadataPlaneDeployment.ReplicaIdFor(claimant), deployment.Genesis, original);

        //The two seams the consensus codec exposes for an application value, in the library's own named value
        //shapes, held only as locals here rather than appearing in any signature of ours.
        WriteValueDelegate<Utf8JsonWriter, CommittedMetadataRecord> write = QuePaxaMessageJson.CreateVersionedValueWriter<VeritasMetadataRecord>(MetadataRecordJson.Write);
        ReadValueDelegate<JsonElement, CommittedMetadataRecord> read = QuePaxaMessageJson.CreateVersionedValueReader<VeritasMetadataRecord>(MetadataRecordJson.Read);

        ArrayBufferWriter<byte> buffer = new();
        using(Utf8JsonWriter writer = new(buffer))
        {
            write(writer, committed);
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);

        Assert.AreEqual(
            JsonValueKind.String,
            document.RootElement.GetProperty("value").GetProperty("baseline").GetProperty("causalityDigest").ValueKind,
            "A digest spanning sixty-four bits crosses as text, so no consumer that reparses the payload through an IEEE double can collapse two lineages into one.");

        CommittedMetadataRecord decoded = read(document.RootElement);

        Assert.AreEqual(
            original,
            decoded.Value,
            "The record's hand-written element-wise equality survives the codec, which is what a recorder comparing a carried proposal against the one it holds depends on.");
        Assert.AreNotSame(
            original,
            decoded.Value,
            "The decoded record is a different instance, so the equality above is structural and not the reference identity a synthesized equality over the claim array would have compared.");
        Assert.AreEqual(committed, decoded, "The whole decided record — version, writer, membership and value — equals what was encoded.");
    }

    /// <summary>How many claims on one record name <paramref name="axis"/>.</summary>
    /// <param name="record">The record to count over.</param>
    /// <param name="axis">The axis to count.</param>
    /// <returns>The number of claims naming that axis, which every row that races claims requires to be one.</returns>
    /// <remarks>
    /// A claim is appended and never rewritten, so a duplicate would be a second write of one obligation's
    /// effect rather than an overwrite, and only a count can see it.
    /// </remarks>
    private static int ClaimsOf(VeritasMetadataRecord record, ReplicaAxis axis)
    {
        int found = 0;
        for(int index = 0; index < record.IdentityClaims.Length; index++)
        {
            if(record.IdentityClaims[index].Axis.Equals(axis))
            {
                found += 1;
            }
        }

        return found;
    }

    /// <summary>The entry one named replica reported in a readiness report, found by identity rather than by position.</summary>
    /// <param name="readiness">The report to read.</param>
    /// <param name="axis">The replica whose entry is wanted.</param>
    /// <returns>That replica's entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the report carries no entry for that replica, which means it was measured over a membership that does not list it.</exception>
    /// <remarks>
    /// The entries are in the membership's own order and a row that indexed into them would still pass if the
    /// membership were built in another order, so the lookup is by the identity the entry names.
    /// </remarks>
    private static MemberReadiness ReadinessOf(RegisterReadiness readiness, ReplicaAxis axis)
    {
        ReplicaId member = MetadataPlaneDeployment.ReplicaIdFor(axis);
        foreach(MemberReadiness entry in readiness.Members)
        {
            if(entry.Member.Equals(member))
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"The readiness report carries no entry for {member}, so it was measured over a membership that does not list that replica.");
    }

    /// <summary>Whether a readiness report carries an entry for one named replica at all.</summary>
    /// <param name="readiness">The report to read.</param>
    /// <param name="axis">The replica to look for.</param>
    /// <returns><see langword="true"/> when the report was measured over a membership that lists that replica.</returns>
    /// <remarks>
    /// Absence is a value here rather than the refusal <see cref="ReadinessOf"/> raises, because a row that
    /// asserts a membership no longer lists a replica is asking about the report's shape and not reading an
    /// entry it expected to find.
    /// </remarks>
    private static bool ReportsEntryFor(RegisterReadiness readiness, ReplicaAxis axis)
    {
        ReplicaId member = MetadataPlaneDeployment.ReplicaIdFor(axis);
        foreach(MemberReadiness entry in readiness.Members)
        {
            if(entry.Member.Equals(member))
            {
                return true;
            }
        }

        return false;
    }
}
