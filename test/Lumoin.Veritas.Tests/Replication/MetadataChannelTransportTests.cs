using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;
using Lumoin.Verisync.Json;
using Microsoft.Win32.SafeHandles;
using CommittedRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The metadata plane over REAL LOOPBACK SOCKETS: three members, each serving its own runner behind an ephemeral
/// TCP listener through <see cref="MetadataChannelServer"/>, each reaching its fellows through one
/// <see cref="MetadataChannelClient"/> per member, and every payload crossing the wire through the consensus
/// library's own JSON envelopes composed with this battery's value codec. What the in-memory plane battery pins
/// about the write discipline this one pins about the transport: a bootstrap and a claim decided over sockets
/// land on every member as a record that is VALUE-equal after a codec round trip; the push's inbound half is a
/// DURABLE learn, so what it landed survives a member's stop exactly where the persist face is a real store and
/// is lost where it is the documented no-op; a majority cut from the routes answers <c>Undecided</c> by value and
/// neither throws nor hangs; a governance denial costs a member and only a MAJORITY denial costs the decision; a
/// serve binding that refuses one call answers a fault frame the register absorbs while the write still decides
/// over the members that remain; the version probe assembles a readiness report whose reachability and quorum gate
/// follow the routes as they are cut and healed; a member whose serve refuses its probe, and a member whose probe
/// the governance gate denies, are each reported UNREACHABLE and carry no version at all; three members' planes
/// writing AT ONCE — held together by a gate on the served exchanges — land their effects exactly once each with a
/// recorded history to check it against; a member stopped and revived from its store answers its fellows again
/// over the route they were told to find it at; the version report's fixed wire body round-trips at the codec and
/// is refused at every other width and out-of-range version; a probe call carrying a payload is answered with the
/// malformed-payload fault and the connection outlives it; and a probe route pointed at another member's host is
/// refused by the register's identity check rather than counted as the member it was aimed at.
/// </summary>
/// <remarks>
/// <para>
/// THE FOOTPRINT, NAMED. Every row stands up THREE runner loops, three listeners and at most six member
/// connections — a row that restarts a member binds one further listener and closes the one it replaced — and
/// every one of them is torn down inside the row. Nothing is shared between rows: each builds
/// its own pool, its own ephemeral ports (bound at port zero and read back), its own temporary directories and
/// its own deployment, so the battery is safe under method-level parallelism beside the rest of the suite.
/// </para>
/// <para>
/// NO ROW DEPENDS ON WALL TIME. The hedging base delay is zero, which activates every member at once, so the
/// injected clock is never waited on and <see cref="TimeProvider.System"/> is inert here; every settle point is
/// the completion that IS the transition — an obligation's own task, a catch-up read, the completion a refusing
/// serve binding sets, or the arrival of the writer a gate is waiting on — and the only bounded waits are
/// backstops, which exist so a regression surfaces as a failure rather than as a hang. A row that re-issues an
/// observation or an obligation is driven by the previous ANSWER and bounded by a count, never by a duration.
/// </para>
/// <para>
/// A CONTENDED ROW ASSERTS SAFETY ALWAYS AND CONTENTION BY CONSTRUCTION. Contention over a real transport is
/// otherwise the operating system's to grant, so the multi-writer row holds every SERVED record exchange until
/// three distinct proposers have reached one: a quorum is two of three and a writer's own leg never leaves its
/// process, so a held writer cannot decide, and the three are released together. That row asserts the gate
/// opened on arrivals and that the recorded intervals overlap, so a backstop that opened it instead is a
/// failure rather than a silent downgrade to a sequential workload.
/// </para>
/// <para>
/// A ROW'S PRIORITY DRAWS ARE SEEDED AND THE SEED IS PRINTED, so a failing row replays the identical draws. The
/// codec seams are this file's own copy on purpose: the sibling batteries carry theirs, and one shared file
/// would make two batteries' wire formats move together for reasons neither of them stated. One row of the
/// wire-codec battery pins this copy against the deployment's production codec byte for byte, so the copy is a
/// check on the format the cross-process wire rides rather than a second format beside it.
/// </para>
/// </remarks>
[TestClass]
internal sealed class MetadataChannelTransportTests
{
    /// <summary>The prefix every temporary store directory in this battery is created under.</summary>
    private const string DirectoryPrefix = "veritas-metadata-socket-";

    /// <summary>The number of members every row stands up; the footprint the battery's remarks name.</summary>
    private const int MemberCount = 3;

    /// <summary>How many times one protocol step may send to one member before abandoning it for that step.</summary>
    private const int AttemptsPerRecorder = 3;

    /// <summary>The attempt budget an obligation that is expected to decide is given.</summary>
    private const int DecidingAttemptBudget = 4;

    /// <summary>The attempt budget an obligation that is expected to reach no decision is given, so a partitioned row spends little to prove it.</summary>
    private const int NarrowAttemptBudget = 2;

    /// <summary>The seed the end-to-end row draws its proposal priorities from.</summary>
    private const ulong EndToEndSeed = 0x5EED0001UL;

    /// <summary>The seed the durable-learn row draws its proposal priorities from.</summary>
    private const ulong DurabilitySeed = 0x5EED0002UL;

    /// <summary>The seed the partition row draws its proposal priorities from.</summary>
    private const ulong PartitionSeed = 0x5EED0003UL;

    /// <summary>The seed the governance row draws its proposal priorities from.</summary>
    private const ulong GovernanceSeed = 0x5EED0004UL;

    /// <summary>The seed the fault-frame row draws its proposal priorities from.</summary>
    private const ulong FaultFrameSeed = 0x5EED0005UL;

    /// <summary>The seed the readiness-probe row draws its proposal priorities from.</summary>
    private const ulong ReadinessSeed = 0x5EED0006UL;

    /// <summary>The seed the multi-writer row draws its proposal priorities from.</summary>
    private const ulong ContentionSeed = 0x5EED0007UL;

    /// <summary>The seed the rejoin row draws its proposal priorities from.</summary>
    private const ulong RejoinSeed = 0x5EED0008UL;

    /// <summary>The seed the misrouted-probe row draws its proposal priorities from.</summary>
    private const ulong MisrouteSeed = 0x5EED0009UL;

    /// <summary>The seed the refused-probe row draws its proposal priorities from.</summary>
    private const ulong ProbeFaultSeed = 0x5EED000AUL;

    /// <summary>The seed the denied-probe row draws its proposal priorities from.</summary>
    private const ulong ProbeDenialSeed = 0x5EED000BUL;

    /// <summary>
    /// How many times a row re-issues an observation or an obligation that answered ignorance.
    /// </summary>
    /// <remarks>
    /// Every re-issue here is driven by the previous ANSWER and never by a clock, and its bound is a count
    /// rather than a duration. A writer superseded at a version it addressed adopts the winner and composes on
    /// it; a probe over a connection a cut tore is answered as a fault once and dials afresh on the call after
    /// it.
    /// </remarks>
    private const int SettleRetries = 6;

    /// <summary>The MSTest-supplied per-test context, read for the per-test cancellation token and for printing each row's seed.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The bound a teardown join waits under. It is a BACKSTOP and never a cadence: nothing in a passing row reaches it, and a regression that would hang surfaces here as a failure instead. It is the ladder's teardown bound, which stands outside every in-flight bound a row waits under.</summary>
    private static TimeSpan TeardownBackstop { get; } = MetadataBatteryBackstops.Teardown;

    /// <summary>The bound an in-flight observation waits under — one that crosses cut routes, or one that waits on a completion a refusing serve sets — a BACKSTOP for the same reason and never a cadence: an answer or a fault is what ends every one of them.</summary>
    private static TimeSpan ObservationBackstop { get; } = MetadataBatteryBackstops.InFlight;

    /// <summary>
    /// The deadline every plane of this battery gives ONE member to answer a catch-up query or a readiness probe
    /// before that member is given up on. A cut route here answers with a fault rather than with silence, so no
    /// row of this battery reaches it; it stands inside <see cref="ObservationBackstop"/> so that a member which
    /// did fall silent would be reported unreachable while the row observing it is still waiting.
    /// </summary>
    private static TimeSpan MemberQueryDeadline { get; } = MetadataBatteryBackstops.MemberQuery;

    /// <summary>
    /// The end-to-end row: a bootstrap and an identity claim decided over real loopback sockets, and the decided
    /// record on EVERY member afterwards — the writer's own, and the two that received it as a disseminated push
    /// and answered a catch-up read with it. The equality is structural, because the record two of the three
    /// members hold was rebuilt from bytes.
    /// </summary>
    [TestMethod]
    public async Task BootstrapAndClaimDecideOverLoopbackSocketsAndReachAQuorumOfMembers()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {EndToEndSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(new MetadataClusterOptions { Pool = pool, PrioritySeed = EndToEndSeed }).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember writer = cluster.Members[0];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await writer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The bootstrap leader commits the deterministic initial record over the wire.");

                MetadataPlaneResult<IdentityClaimOutcome> claimed = await writer.Plane.ClaimIdentityAsync(writer.Axis, DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(IdentityClaimOutcome.Claimed, claimed.Outcome, "The claim of an axis no record carries is appended by a decision a quorum took over sockets.");

                VeritasMetadataRecord? decided = claimed.Record;
                Assert.IsNotNull(decided, "A committed write carries the record it decided.");
                Assert.HasCount(1, decided!.IdentityClaims, "The decided record carries exactly the one claim this row took.");
                Assert.AreEqual(writer.Axis, decided.IdentityClaims[0].Axis, "The claim on the decided record names the axis that was claimed.");

                //The push is awaited before the committed write returns, but each leg absorbs its own
                //faults, so what the protocol owes is a quorum of members holding the pushed record and
                //never every member by name.
                List<MetadataSocketMember> holding = [];
                foreach(MetadataSocketMember member in cluster.Members)
                {
                    CommittedRecord? learned = member.Plane.HostCommitted;
                    if(learned is null)
                    {
                        continue;
                    }

                    Assert.AreEqual(claimed.Version, learned.Version, FormattableString.Invariant($"Member {member.Axis} holds the record at the version the claim was decided at."));
                    Assert.AreEqual(decided, learned.Value, FormattableString.Invariant($"Member {member.Axis}'s learned record equals the decided one by value."));
                    holding.Add(member);
                }

                Assert.IsGreaterThanOrEqualTo((MemberCount / 2) + 1, holding.Count, FormattableString.Invariant($"Only {holding.Count} of {MemberCount} members hold the pushed record, which is below the quorum the push made servable."));

                //The catch-up is the settle point rather than a wait, and it IS a census safely: each
                //member's read is its own awaited exchange that any one honest holder can answer, so every
                //member converges through it whatever the push reached.
                for(int index = 0; index < cluster.Members.Count; index++)
                {
                    MetadataSocketMember member = cluster.Members[index];
                    CommittedRecord? caughtUp = await member.Plane.ReadRecordAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsNotNull(caughtUp, FormattableString.Invariant($"Member {index}'s catch-up answers with a committed record."));
                    Assert.AreEqual(claimed.Version, caughtUp!.Version, FormattableString.Invariant($"Member {index}'s catch-up answers at the decided version."));
                    Assert.AreEqual(decided, caughtUp.Value, FormattableString.Invariant($"Member {index}'s catch-up answers with a record equal to the decided one by value."));
                }

                //A fellow is picked from the members the floor proved holding rather than named by index,
                //and the read is guarded so a missed push fails by name rather than as a null dereference.
                MetadataSocketMember? fellow = null;
                foreach(MetadataSocketMember member in holding)
                {
                    if(!ReferenceEquals(member, writer))
                    {
                        fellow = member;
                        break;
                    }
                }

                Assert.IsNotNull(fellow, "The quorum floor proved at least one member beside the writer holding the pushed record.");
                CommittedRecord? rebuilt = fellow!.Plane.HostCommitted;
                Assert.IsNotNull(rebuilt, FormattableString.Invariant($"The fellow member {fellow.Axis} no longer holds the record it held at the floor."));
                Assert.AreNotSame(decided, rebuilt!.Value, "The fellow member's record was rebuilt from bytes, so the equality above is structural and not reference identity.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The durability contrast: the SAME decided record is landed on two members through the inbound half of the
    /// push — the contract a real push lands on, offered here by name — and only the member whose persist face is
    /// a real store still holds it after its host is torn down and restored OFFLINE from what was written. The
    /// member whose persist face is the documented no-op has nothing to restore from, which is what makes the
    /// durability naming on that contract observable rather than declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record is offered by name rather than left to the write's own fan-out, because every leg of that
    /// fan-out absorbs its own faults by design: a row whose contrast rested on two best-effort legs having both
    /// landed would be resting on something the protocol never promised. The offer is the identical durable learn
    /// a push arrives through, and it is idempotent where the push already landed it.
    /// </para>
    /// <para>
    /// The evidence here is the bytes and nothing beyond them: the restored host is read in this process and is
    /// never put back on a socket, which is deliberate, because what this row is about is the persist face. A
    /// revived member answering its fellows again is the rejoin row beside it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheSameDurableLearnSurvivesARestartOnlyWhereThePersistFaceIsDurable()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {DurabilitySeed}."));
        string root = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataClusterOptions options = new()
            {
                Pool = pool,
                PrioritySeed = DurabilitySeed,
                StoreRoot = root,
                DurableMembers = [1]
            };

            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(options).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember writer = cluster.Members[0];
                MetadataSocketMember durable = cluster.Members[1];
                MetadataSocketMember ephemeral = cluster.Members[2];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await writer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped before the row's own write.");

                MetadataPlaneResult<IdentityClaimOutcome> claimed = await writer.Plane.ClaimIdentityAsync(writer.Axis, DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(IdentityClaimOutcome.Claimed, claimed.Outcome, "The claim decides, which is what produces the push both members receive.");

                VeritasMetadataRecord? decided = claimed.Record;
                Assert.IsNotNull(decided, "A committed write carries the record it decided.");

                //The push legs absorb their own faults, so each member's learn is made required by name:
                //the writer's own local learn is durable by the binding's contract and supplies the record,
                //and the offer below is the same contract a real push lands on, idempotent where the push
                //already landed it, and awaited. The row's durability contrast then rests on the two
                //persist faces and never on two best-effort legs having both landed.
                CommittedRecord? pushed = writer.Plane.HostCommitted;
                Assert.IsNotNull(pushed, "The writer's own local learn is durable before its committed write returns.");
                _ = await durable.Plane.ApplyDisseminatedRecordAsync(pushed!, TestContext.CancellationToken).ConfigureAwait(false);
                _ = await ephemeral.Plane.ApplyDisseminatedRecordAsync(pushed!, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(decided, durable.Plane.HostCommitted!.Value, "The record is landed on the durable member.");
                Assert.AreEqual(decided, ephemeral.Plane.HostCommitted!.Value, "The identical record is landed on the member whose persist face writes nothing.");

                //The stop is this row's crash: the plane, the member's channels, its listener and its runner all
                //end, and what is left is whatever the persist face had already written.
                await cluster.StopMemberAsync(1).ConfigureAwait(false);

                QuePaxaVersionedNodeState<VeritasMetadataRecord>? restoredState = await durable.Store!.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
                Assert.IsNotNull(restoredState, "The durable member's push was written through its persist face, so a restore finds a state rather than a fresh host.");

                QuePaxaVersionedNode<VeritasMetadataRecord> revived = QuePaxaVersionedNode<VeritasMetadataRecord>.FromState(
                    cluster.Deployment.Genesis,
                    restoredState!.Host,
                    restoredState!);
                CommittedRecord? survived = revived.Committed;
                Assert.IsNotNull(survived, "The restored host holds the committed record its snapshot carried.");
                Assert.AreEqual(claimed.Version, survived!.Version, "The record survived at the version it was decided at.");
                Assert.AreEqual(decided, survived.Value, "The pushed record survived the restart by value.");
                Assert.AreNotSame(decided, survived.Value, "The survivor came back through the store's codec, so the equality above is structural and not reference identity.");

                await cluster.StopMemberAsync(2).ConfigureAwait(false);

                QuePaxaVersionedNodeState<VeritasMetadataRecord>? nothing = await ephemeral.Store!.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
                Assert.IsNull(nothing, "The member whose persist face is the documented no-op has nothing to restore from: the same durable learn wrote no bytes.");
            }
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The deterministic <c>Undecided</c>: with a MAJORITY of the membership cut from the routes, a claim spends
    /// its small budget, reaches no decision, and answers by value — no exception, no hang, no record and no
    /// version. Healing the routes and claiming again decides, which is what makes the first answer evidence
    /// about the cut rather than about a battery that could not decide at all.
    /// </summary>
    [TestMethod]
    public async Task AMajorityCutFromTheRoutesAnswersUndecidedAndDecidesOnceHealed()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {PartitionSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(new MetadataClusterOptions { Pool = pool, PrioritySeed = PartitionSeed }).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember writer = cluster.Members[0];

                //Two of three members are unreachable, so no quorum this writer can gather exists. The cut is
                //taken at the open-connection seam, which is the one place a deployment's routing lives.
                cluster.Cut(1);
                cluster.Cut(2);

                MetadataPlaneResult<IdentityClaimOutcome> undecided = await writer.Plane.ClaimIdentityAsync(writer.Axis, NarrowAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(IdentityClaimOutcome.Undecided, undecided.Outcome, "A majority cut from the routes is definite ignorance, which the plane answers by value and never by raising.");
                Assert.IsNull(undecided.Record, "An undecided attempt establishes no record, so none is handed back.");
                Assert.AreEqual(RegisterVersion.Unwritten, undecided.Version, "An undecided attempt decided at no version.");

                cluster.Heal(1);
                cluster.Heal(2);

                MetadataPlaneResult<IdentityClaimOutcome> healed = await writer.Plane.ClaimIdentityAsync(writer.Axis, DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(IdentityClaimOutcome.Claimed, healed.Outcome, "The same claim decides once a quorum is reachable again, so the answer above was the cut and not a battery that cannot decide.");
                Assert.IsNotNull(healed.Record, "The healed claim carries the record it decided.");
                Assert.AreEqual(writer.Axis, healed.Record!.IdentityClaims[0].Axis, "The healed claim landed the axis it names.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The governance path: the writer reaches its fellows through
    /// <see cref="GovernedMetadataExchange"/>, and a denial is the gate's own — emitted at
    /// <see cref="NetworkBoundary.ConsensusExchange"/> against the denied member's key. One denied member is a
    /// slower cluster and not a wrong one, because the quorum that remains still decides; a MAJORITY denied is
    /// the same definite ignorance a cut route produces, because a denied member IS an unreachable one on this
    /// surface.
    /// </summary>
    [TestMethod]
    public async Task GovernanceDenialCostsAMemberAndOnlyAMajorityDenialCostsTheDecision()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {GovernanceSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            NetworkFirewall firewall = new();
            GovernanceTraceCapture governance = new();
            MetadataClusterOptions options = new()
            {
                Pool = pool,
                PrioritySeed = GovernanceSeed,
                GovernedMember = 0,
                Governance = firewall.Decide,
                GovernanceTrace = governance.Capture
            };

            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(options).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember writer = cluster.Members[0];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await writer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "An ungoverned-by-policy cluster bootstraps exactly as the undecorated one does.");
                Assert.AreEqual(0, governance.DeniedPeerCount, "Nothing is denied before the denylist names a member.");

                firewall.Deny(NetworkPeerKeyKind.ReplicaId, cluster.Members[2].Axis.Bytes.Span);

                MetadataPlaneResult<IdentityClaimOutcome> stillDecides = await writer.Plane.ClaimIdentityAsync(writer.Axis, DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(IdentityClaimOutcome.Claimed, stillDecides.Outcome, "One denied member leaves a quorum reachable, so the write still decides.");
                Assert.IsGreaterThan(0, governance.DenialCount, "The denied member's exchanges were refused by the gate rather than silently skipped.");
                Assert.IsTrue(governance.EveryDenialNamesTheConsensusBoundary, "Every denial was taken at the consensus-exchange boundary.");
                Assert.AreEqual(1, governance.DeniedPeerCount, "Exactly one member's key was denied, so the gate refused that member and no other.");

                firewall.Deny(NetworkPeerKeyKind.ReplicaId, cluster.Members[1].Axis.Bytes.Span);

                MetadataPlaneResult<IdentityClaimOutcome> undecided = await writer.Plane.ClaimIdentityAsync(cluster.Members[1].Axis, NarrowAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(IdentityClaimOutcome.Undecided, undecided.Outcome, "A denied majority is an unreachable majority, which is the same definite ignorance a cut route produces.");
                Assert.IsNull(undecided.Record, "An undecided attempt establishes no record.");
                Assert.AreEqual(2, governance.DeniedPeerCount, "Both denied members were named by the gate's own verdicts.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The fault-frame answer: a serve binding that refuses one call is answered with a FAULT FRAME rather than
    /// with a torn connection, so the caller's next call is served over the same connection; and when the refused
    /// call is a consensus record exchange, the register reads it as one unreachable member and the write decides
    /// over the members that remain.
    /// </summary>
    [TestMethod]
    public async Task AFaultingServeBindingAnswersAFaultFrameAndTheWriteStillDecides()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {FaultFrameSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataClusterOptions options = new() { Pool = pool, PrioritySeed = FaultFrameSeed };
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(options).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember writer = cluster.Members[0];
                MetadataSocketMember refusing = cluster.Members[2];

                refusing.Faults.ArmReadFaults(1);
                MetadataChannelClient probe = new(
                    cluster.RouteTo(2),
                    cluster.Codecs.SerializeRequest,
                    cluster.Codecs.DeserializeReply,
                    cluster.Codecs.SerializeRecord,
                    cluster.Codecs.DeserializeRecord,
                    pool);
                await using(probe.ConfigureAwait(false))
                {
                    bool refused = false;
                    try
                    {
                        _ = await probe.ReadCommittedAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    }
                    catch(IOException)
                    {
                        refused = true;
                    }

                    Assert.IsTrue(refused, "A call the host accepted and could not complete is raised to the caller as the I/O fault the consensus seams read as an unreachable member.");

                    //The call that follows is the whole point of a fault FRAME: a well-formed answer leaves the
                    //frame stream in step, so the connection keeps serving instead of being dialed afresh.
                    CommittedRecord? afterFault = await probe.ReadCommittedAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsNull(afterFault, "The next call on the same connection is served, and the host has learned no record yet.");
                }

                Assert.AreEqual(1, refusing.ReadFaults, "The refusing binding refused exactly the one call it was armed for.");
                Assert.AreEqual(1, refusing.AcceptedConnections, "One connection carried both calls, which a torn connection could not have done.");

                refusing.Faults.ArmRecordFaults(1);
                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await writer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);

                //The completion the refusal sets is the transition itself, so the counts below are read after the
                //fault frame was answered rather than after a wait that guessed at it.
                await refusing.Faults.RecordFaultAnswered.WaitAsync(ObservationBackstop, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The write decided over the members that remained while one answered a fault frame.");
                Assert.AreEqual(1, refusing.RecordFaults, "Exactly the armed record exchange was refused; the rest were served.");

                MetadataPlaneResult<IdentityClaimOutcome> claimed = await writer.Plane.ClaimIdentityAsync(writer.Axis, DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(IdentityClaimOutcome.Claimed, claimed.Outcome, "The member that answered a fault frame serves the next write, so the fault cost one call and not the connection.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The version probe over real sockets: a readiness report assembled from one probe exchange per member
    /// reports every member at the decided version; cutting one member's routes drops it to unreachable —
    /// carrying no version at all rather than a version of zero — while the quorum gate still clears; cutting a
    /// MAJORITY makes that gate refuse; and healing the routes brings the report back, so the two refusals above
    /// are evidence about the cuts and not about a report that could never be full.
    /// </summary>
    [TestMethod]
    public async Task TheVersionProbeReportsReadinessOverSocketsAndTheQuorumGateFollowsTheCuts()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {ReadinessSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(new MetadataClusterOptions { Pool = pool, PrioritySeed = ReadinessSeed }).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember observer = cluster.Members[0];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await observer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, which is what gives the members a version to report.");

                RegisterReadiness whole = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.HasCount(MemberCount, whole.Members, "The report carries one entry per member of the membership it was measured over.");
                Assert.AreEqual(MemberCount, whole.Reachable, "Every member answered a probe exchange over its own connection, the local one included.");

                //Reachability is a census the probe owes, because every probe is its own awaited exchange
                //on a live route. Which members hold the version is not: the push legs absorb their own
                //faults, so the version claims are a quorum floor plus the observer's own entry, which its
                //local durable learn owes by name.
                int atVersion = MembersAtVersion(whole, bootstrapped.Version);

                Assert.IsGreaterThanOrEqualTo((MemberCount / 2) + 1, atVersion, FormattableString.Invariant($"Only {atVersion} of {MemberCount} members hold the decided version, which is below the quorum the push made servable."));
                Assert.AreEqual(
                    bootstrapped.Version,
                    ReadinessOf(whole, observer.Axis).Version!.Value,
                    "The observer's own local learn is durable before its write returns, so its entry is owed by name.");

                Assert.IsTrue(whole.QuorumHasLearned(bootstrapped.Version), "A quorum has learned the decided version, so a write at the version after it can gather one.");

                //The member to cut is picked so the two members left reachable are members the report PROVED
                //holding: the floor above allows one member to have missed the push, and cutting a holder while
                //the misser stayed reachable would fail the quorum assertion below with nothing regressed.
                int cutIndex = CutTargetFrom(whole, bootstrapped.Version, cluster);
                cluster.Cut(cutIndex);

                RegisterReadiness cut = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount - 1, cut.Reachable, "The member whose routes are cut answered nothing, so two of three members are reachable.");

                MemberReadiness silent = ReadinessOf(cut, cluster.Members[cutIndex].Axis);
                Assert.IsFalse(silent.Reachable, "A member whose probe could not cross is unreachable.");
                Assert.IsNull(
                    silent.Version,
                    "A member that did not answer carries no version at all, which is what keeps it distinguishable from the unwritten answer a host that has learned nothing gives.");
                Assert.IsTrue(
                    cut.QuorumHasLearned(bootstrapped.Version),
                    "The two members that answered are a quorum of three and both had learned the decided version, so the gate clears with one member silent.");

                //The second cut takes the remaining fellow, so the only member left is the observer, whose
                //probe never leaves the process.
                int secondCut = cutIndex == 1 ? 2 : 1;
                cluster.Cut(secondCut);

                RegisterReadiness majority = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(1, majority.Reachable, "With a majority cut, the only member left to answer is the one whose probe never leaves the process.");
                Assert.IsFalse(
                    majority.QuorumHasLearned(bootstrapped.Version),
                    "One member is not a quorum of three, so the gate refuses rather than clearing against a cluster that mostly answered nothing.");

                cluster.Heal(secondCut);
                cluster.Heal(cutIndex);

                RegisterReadiness healed = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                for(int retry = 0; retry < SettleRetries && healed.Reachable < MemberCount; retry++)
                {
                    //A connection the cut tore is answered as a fault once and dialled afresh on the call after
                    //it, so the re-read is driven by the previous ANSWER and never by a clock.
                    healed = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                }

                Assert.AreEqual(MemberCount, healed.Reachable, "Every member answers again once its routes are healed, so the unreachable entries above were the cuts and not a probe that never worked.");
                Assert.IsTrue(healed.QuorumHasLearned(bootstrapped.Version), "The healed cluster clears the gate it refused while a majority was cut.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The probe a serving binding REFUSES: the member holds the decided version, its serve answers the probe
    /// with a fault frame, and the report records it unreachable carrying no version at all rather than a version
    /// of zero. The refusal is armed for one call, so the report after it counts the member at the version it
    /// never lost, over the connection the fault frame left in step.
    /// </summary>
    /// <remarks>
    /// The probe is served through the host's own catch-up seam, which is what makes arming that seam the way to
    /// refuse a probe rather than a read; the count of refusals the binding took is what says the armed refusal
    /// was spent on the probe and not on some other call. The record is landed on the refusing member by name
    /// first, because a member that had never held a version would report the unwritten one and the row's whole
    /// distinction would be vacuous.
    /// </remarks>
    [TestMethod]
    public async Task AProbeTheServingBindingRefusesReportsItsMemberUnreachableRatherThanAtVersionZero()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {ProbeFaultSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(new MetadataClusterOptions { Pool = pool, PrioritySeed = ProbeFaultSeed }).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember observer = cluster.Members[0];
                MetadataSocketMember refusing = cluster.Members[2];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await observer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped, which is what gives the refusing member a version it could have reported.");

                CommittedRecord? decided = observer.Plane.HostCommitted;
                Assert.IsNotNull(decided, "The writer's own local learn is durable before its committed write returns.");
                _ = await refusing.Plane.ApplyDisseminatedRecordAsync(decided!, TestContext.CancellationToken).ConfigureAwait(false);

                RegisterReadiness whole = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount, whole.Reachable, "Every member answers its own probe before anything is armed.");
                Assert.AreEqual(
                    bootstrapped.Version,
                    ReadinessOf(whole, refusing.Axis).Version!.Value,
                    "The member about to refuse holds the decided version and reports it, so what the report says next is the refusal and never an empty host.");
                Assert.AreEqual(0, refusing.ReadFaults, "Nothing has been refused before the row arms a refusal.");

                refusing.Faults.ArmReadFaults(1);

                RegisterReadiness refused = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(1, refusing.ReadFaults, "The armed refusal was spent on the probe, which is the only call this report makes.");
                Assert.AreEqual(MemberCount - 1, refused.Reachable, "The member whose probe was refused answered nothing, so two of three members are reachable.");

                MemberReadiness silent = ReadinessOf(refused, refusing.Axis);
                Assert.IsFalse(silent.Reachable, "A member whose serve refused its probe is unreachable to the report.");
                Assert.IsNull(
                    silent.Version,
                    "A refused probe carries no version at all rather than the unwritten one, which is what keeps a member that would not answer distinguishable from a member holding nothing — the distinction a decommission gate rests on.");
                Assert.IsTrue(
                    refused.QuorumHasLearned(bootstrapped.Version),
                    "The two members that answered are a quorum of three and both had learned the decided version, so one refused probe costs a member and never the gate.");

                RegisterReadiness after = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount, after.Reachable, "The refusal was armed for one call, so the next report counts the member again and the entry above was the fault rather than a route that stopped working.");
                Assert.AreEqual(
                    bootstrapped.Version,
                    ReadinessOf(after, refusing.Axis).Version!.Value,
                    "The member answers with the version it held throughout, which the refusal never touched.");
                Assert.AreEqual(1, refusing.AcceptedConnections, "One connection carried the probe before the fault, the fault frame itself, and the probe after it, because a well-formed fault answer leaves the frame stream in step.");

                //The same cluster from the REFUSING member's own point of view. Unreachability is a property of
                //one asker's query and never of the cluster, so the member whose serve refused one caller's probe
                //assembles a whole report of its own.
                RegisterReadiness fromRefusing = await ReadinessOverSocketsAsync(refusing, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount, fromRefusing.Reachable, "The member that refused one caller's probe counts every member from its own side, so the unreachable entry above was one asker's query and not a fact about the cluster.");
                Assert.AreEqual(
                    bootstrapped.Version,
                    ReadinessOf(fromRefusing, refusing.Axis).Version!.Value,
                    "It reports itself at the version it holds, which is the same version the observer read off it before and after the refusal.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The probe the GOVERNANCE GATE denies: the member holds the decided version, the gate refuses the call
    /// before it leaves the observer, and the report records that member unreachable carrying no version at all
    /// rather than a version of zero — the one place a denial must not be written down as a claim about the
    /// denied host's own state.
    /// </summary>
    /// <remarks>
    /// A denial is the gate's own and is emitted against the denied member's key at the consensus-exchange
    /// boundary, which the captured verdicts say rather than the outcome merely implying. The record is landed on
    /// both fellows by name first, so the entry the denial produces is measured against a member that had a
    /// version to report and the quorum gate has a majority that genuinely learned it.
    /// </remarks>
    [TestMethod]
    public async Task AProbeTheGovernanceGateDeniesReportsItsMemberUnreachableRatherThanAtVersionZero()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {ProbeDenialSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            NetworkFirewall firewall = new();
            GovernanceTraceCapture governance = new();
            MetadataClusterOptions options = new()
            {
                Pool = pool,
                PrioritySeed = ProbeDenialSeed,
                GovernedMember = 0,
                Governance = firewall.Decide,
                GovernanceTrace = governance.Capture
            };

            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(options).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember observer = cluster.Members[0];
                MetadataSocketMember denied = cluster.Members[2];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await observer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "An ungoverned-by-policy cluster bootstraps exactly as the undecorated one does.");

                CommittedRecord? decided = observer.Plane.HostCommitted;
                Assert.IsNotNull(decided, "The writer's own local learn is durable before its committed write returns.");
                _ = await cluster.Members[1].Plane.ApplyDisseminatedRecordAsync(decided!, TestContext.CancellationToken).ConfigureAwait(false);
                _ = await denied.Plane.ApplyDisseminatedRecordAsync(decided!, TestContext.CancellationToken).ConfigureAwait(false);

                RegisterReadiness whole = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount, whole.Reachable, "Every member answers its probe while the policy denies nothing.");
                Assert.AreEqual(
                    bootstrapped.Version,
                    ReadinessOf(whole, denied.Axis).Version!.Value,
                    "The member about to be denied holds the decided version and reports it, so what the report says next is the denial and never an empty host.");
                Assert.AreEqual(0, governance.DeniedPeerCount, "Nothing is denied before the denylist names a member.");

                firewall.Deny(NetworkPeerKeyKind.ReplicaId, denied.Axis.Bytes.Span);

                RegisterReadiness report = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount - 1, report.Reachable, "The denied member's probe never left the observer, so two of three members are reachable.");

                MemberReadiness refused = ReadinessOf(report, denied.Axis);
                Assert.IsFalse(refused.Reachable, "A denied route is an unreachable member, which is what the report says about a call the gate refused to make.");
                Assert.IsNull(
                    refused.Version,
                    "A denied probe carries no version at all rather than the unwritten one: answering unwritten would write the gate's own refusal down as a fact about the denied host's state, in the one place a report must not confuse the two.");
                Assert.IsGreaterThan(0, governance.DenialCount, "The denied member's probe was refused by the gate rather than silently skipped.");
                Assert.IsTrue(governance.EveryDenialNamesTheConsensusBoundary, "Every denial was taken at the consensus-exchange boundary.");
                Assert.AreEqual(1, governance.DeniedPeerCount, "Exactly one member's key was denied, so the gate refused that member's probe and no other's.");
                Assert.IsTrue(
                    report.QuorumHasLearned(bootstrapped.Version),
                    "The two members that answered are a quorum of three and both had learned the decided version, so one denied member costs a member and never the gate.");

                //The same cluster from a FELLOW's point of view, whose channels this policy does not govern. A
                //denial is one host's own refusal to make a call, so the member it denies is unreachable to the
                //denying host alone and is answering everybody else.
                RegisterReadiness fromFellow = await ReadinessOverSocketsAsync(cluster.Members[1], TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(MemberCount, fromFellow.Reachable, "The ungoverned fellow counts every member, so the denied entry above is the observer's own gate and never a fact about the denied member's host.");
                Assert.AreEqual(
                    bootstrapped.Version,
                    ReadinessOf(fromFellow, denied.Axis).Version!.Value,
                    "The denied member answers the fellow with the decided version it holds, which is the version the denying observer is no longer allowed to ask for.");
                Assert.AreEqual(1, governance.DeniedPeerCount, "The fellow's own probes passed no gate at all, so nothing it asked added a denial.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The multi-writer row: THREE members' planes each claim their own axis at once, on real threads over real
    /// sockets, and the recorded history is checked against the record the hosts hold — every committed
    /// obligation's effect present exactly once, no two effects sharing a version, and every obligation that
    /// completed before another was invoked decided at an earlier version than that one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST WAVE CONTENDS BY CONSTRUCTION. A quorum here is two of three and a writer's own leg never leaves
    /// its process, so a writer whose served exchanges are held cannot decide; the gate releases the three of
    /// them only once all three have arrived, which makes their intervals overlap on every run rather than on a
    /// lucky one. The row asserts that the gate opened on arrivals, so a backstop that opened it instead is a
    /// failure and never a silent downgrade to a sequential workload.
    /// </para>
    /// <para>
    /// THE SECOND WAVE IS WHAT MAKES THE PRECEDENCE CHECK BITE. Every first-wave obligation has completed before
    /// any second-wave obligation is invoked, so the real-time rule has pairs to range over; a row of one
    /// contended wave would leave that check quantified over nothing. The second wave is also the idempotent
    /// repeat: each writer re-claims the axis it already holds, which must still leave one claim per axis.
    /// </para>
    /// <para>
    /// The workload's outcomes are not assumed: each obligation is re-issued on definite ignorance after a
    /// catch-up, which is what the design says a host does, and every operation records the interval it ran over
    /// and the version it was decided at. What is asserted afterwards is safety over whatever interleaving the
    /// operating system produced, plus the one liveness claim a healthy cluster owes: writers with a rounds
    /// budget do not retire a claim unlanded.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ContendedObligationsFromDistinctMembersLandExactlyOnceOverSockets()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {ContentionSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(new MetadataClusterOptions { Pool = pool, PrioritySeed = ContentionSeed }).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await cluster.Members[0].Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped before the writers start, so every claim below composes on a record and none of them races the bootstrap.");

                //Armed after the bootstrap, because the gate holds every served exchange and the bootstrap would
                //otherwise wait for a race that has not started.
                WriterRendezvous<ReplicaId> rendezvous = cluster.ArmWriterRendezvous(MemberCount);

                Task<ClaimOperation>[] contending =
                [
                    ClaimOnItsOwnThreadAsync(cluster.Members[0], "wave-1 member-0", TestContext.CancellationToken),
                    ClaimOnItsOwnThreadAsync(cluster.Members[1], "wave-1 member-1", TestContext.CancellationToken),
                    ClaimOnItsOwnThreadAsync(cluster.Members[2], "wave-1 member-2", TestContext.CancellationToken)
                ];

                ClaimOperation[] contended = await Task.WhenAll(contending).ConfigureAwait(false);

                Assert.IsTrue(
                    rendezvous.EveryParticipantArrived,
                    "All three writers reached the gate, so the outcomes below were taken by three proposers whose windows overlapped rather than by three that never met.");

                //The second wave starts only once every operation of the first has completed, which is what gives
                //the real-time precedence rule below pairs to range over.
                Task<ClaimOperation>[] following =
                [
                    ClaimOnItsOwnThreadAsync(cluster.Members[0], "wave-2 member-0", TestContext.CancellationToken),
                    ClaimOnItsOwnThreadAsync(cluster.Members[1], "wave-2 member-1", TestContext.CancellationToken),
                    ClaimOnItsOwnThreadAsync(cluster.Members[2], "wave-2 member-2", TestContext.CancellationToken)
                ];

                ClaimOperation[] followed = await Task.WhenAll(following).ConfigureAwait(false);
                ClaimOperation[] history = [.. contended, .. followed];
                foreach(ClaimOperation operation in history)
                {
                    TestContext.WriteLine(FormattableString.Invariant(
                        $"{operation.Label}: invoked {operation.Invoked}, completed {operation.Completed}, outcome {operation.Outcome}, version {operation.Version.Value}."));
                }

                //The witness is read off the HOSTS over their own connections, so it is what the cluster holds
                //rather than what any writer believed it had established.
                CommittedRecord?[] held = await HeldOverSocketsAsync(cluster, pool, TestContext.CancellationToken).ConfigureAwait(false);
                CommittedRecord witness = Highest(held) ?? throw new InvalidOperationException("No member of the cluster held a record after the workload, so there is nothing for the history to be checked against.");

                Assert.HasCount(
                    MemberCount,
                    witness.Value.IdentityClaims,
                    "Every writer's claim landed and none landed twice, which is the exactly-once the append discipline owes: a claim is appended and never rewritten, so a lost update is a missing entry and a replay is a duplicate one.");

                foreach(ClaimOperation operation in history)
                {
                    Assert.IsTrue(
                        operation.Outcome is IdentityClaimOutcome.Claimed or IdentityClaimOutcome.AlreadyClaimedBySelf,
                        FormattableString.Invariant($"{operation.Label} answered {operation.Outcome}; a healthy cluster with a rounds budget does not retire a claim unlanded."));
                    Assert.AreEqual(
                        1,
                        ClaimsOf(witness.Value, operation.Axis),
                        FormattableString.Invariant($"{operation.Label}'s axis stands claimed exactly once on the record the hosts hold."));
                }

                //The recorded intervals of the contended wave overlap pairwise, which is the history's own record
                //of the gate having done its work: three obligations were outstanding at one instant.
                for(int outer = 0; outer < contended.Length; outer++)
                {
                    for(int inner = outer + 1; inner < contended.Length; inner++)
                    {
                        Assert.IsTrue(
                            contended[outer].Invoked < contended[inner].Completed && contended[inner].Invoked < contended[outer].Completed,
                            FormattableString.Invariant($"{contended[outer].Label} and {contended[inner].Label} ran over overlapping intervals, because neither could be served until both had arrived at the gate."));
                    }
                }

                //A version carries one writer's effect: two claims decided at one version would be two writers
                //believing they had written the same version, which is the lost update this check is for.
                for(int outer = 0; outer < witness.Value.IdentityClaims.Length; outer++)
                {
                    for(int inner = outer + 1; inner < witness.Value.IdentityClaims.Length; inner++)
                    {
                        Assert.AreNotEqual(
                            witness.Value.IdentityClaims[outer].ClaimedAt,
                            witness.Value.IdentityClaims[inner].ClaimedAt,
                            "Each claim carries the version its own write was decided at, and one version is decided once, so two claims sharing one would be one version carrying two writers' effects.");
                    }
                }

                //Real-time precedence: an operation that had already completed when another was invoked cannot
                //have been decided at a later version than that one, whatever the wire did in between.
                int ordered = 0;
                foreach(ClaimOperation earlier in history)
                {
                    foreach(ClaimOperation later in history)
                    {
                        if(earlier.Completed < later.Invoked && earlier.Version != RegisterVersion.Unwritten && later.Version != RegisterVersion.Unwritten)
                        {
                            ordered += 1;
                            Assert.IsGreaterThan(
                                earlier.Version.Value,
                                later.Version.Value,
                                FormattableString.Invariant($"{earlier.Label} completed before {later.Label} was invoked, so it must have been decided at an earlier version."));
                        }
                    }
                }

                //The rule above is quantified over the pairs the history actually separates in real time, and a
                //workload that produced none would have satisfied it by having nothing to check.
                Assert.IsGreaterThan(
                    0,
                    ordered,
                    "The two waves put every first-wave operation wholly before every second-wave one, so the precedence rule ranged over real pairs rather than over none.");

                int holding = 0;
                foreach(CommittedRecord? record in held)
                {
                    if(record is not null && record.Version >= witness.Version)
                    {
                        holding += 1;
                    }
                }

                //A quorum floor rather than a census: a dissemination leg that failed is absorbed by design and
                //observed through a readiness report, never through a write's result, so a member may legitimately
                //lag the final version by one push.
                Assert.IsGreaterThan(
                    MemberCount / 2,
                    holding,
                    "A quorum of members holds the final record over their own connections, which is what makes the next version servable at all.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The rejoin: a member is stopped, comes back through the host's own restore from what its store held, binds
    /// a fresh listener the route is moved to, and is then observed ANSWERING — its own probe exchange reports the
    /// version it restored, its catch-up read serves the record behind it, and the cluster's readiness report
    /// counts it again once the connection the crash tore has been dialled afresh.
    /// </summary>
    /// <remarks>
    /// What separates this from the durability row beside it is where the evidence is read. That row proves the
    /// bytes survived by restoring a host offline and reading it; this one puts the revived host back on a socket
    /// and makes its fellows ask it, which is the only form in which a rejoin is observable to a deployment.
    /// </remarks>
    [TestMethod]
    public async Task ARestartedMemberRejoinsAndAnswersOverItsNewRoute()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {RejoinSeed}."));
        string root = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataClusterOptions options = new()
            {
                Pool = pool,
                PrioritySeed = RejoinSeed,
                StoreRoot = root,
                DurableMembers = [1]
            };

            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(options).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                MetadataSocketMember writer = cluster.Members[0];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await writer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The chain is bootstrapped before the row's own write.");

                MetadataPlaneResult<IdentityClaimOutcome> claimed = await writer.Plane.ClaimIdentityAsync(writer.Axis, DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(IdentityClaimOutcome.Claimed, claimed.Outcome, "The claim decides, which is what gives the durable member a record worth coming back with.");

                VeritasMetadataRecord? decided = claimed.Record;
                Assert.IsNotNull(decided, "A committed write carries the record it decided.");

                //The push legs absorb their own faults, so the record the restart must find durable is landed
                //by name: the writer's own local learn supplies it, and the offer is the same contract a real
                //push lands on, idempotent where the push already landed it, and awaited. The rejoin evidence
                //below then rests on the persist face and the revived listener, never on a best-effort leg
                //having landed.
                CommittedRecord? pushed = writer.Plane.HostCommitted;
                Assert.IsNotNull(pushed, "The writer's own local learn is durable before its committed write returns.");
                _ = await cluster.Members[1].Plane.ApplyDisseminatedRecordAsync(pushed!, TestContext.CancellationToken).ConfigureAwait(false);

                //The stop and the revive are one act here: the member's plane, channels, listener and runner all
                //end, and what comes back is whatever its persist face had written, on a listener of its own.
                MetadataSocketMember rejoined = await cluster.RestartMemberAsync(1, TestContext.CancellationToken).ConfigureAwait(false);

                MetadataChannelClient probe = new(
                    cluster.RouteTo(1),
                    cluster.Codecs.SerializeRequest,
                    cluster.Codecs.DeserializeReply,
                    cluster.Codecs.SerializeRecord,
                    cluster.Codecs.DeserializeRecord,
                    pool);
                await using(probe.ConfigureAwait(false))
                {
                    MemberVersionReport reported = await probe.ObserveVersionAsync(TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.AreEqual(
                        MetadataPlaneDeployment.ReplicaIdFor(rejoined.Axis),
                        reported.Recorder.Replica,
                        "The host that answered the probe asserts the identity of the member that was restarted, so the route was moved to the member it names and not to some other host.");
                    Assert.AreEqual(
                        claimed.Version,
                        reported.Version,
                        "The rejoined member reports the version it restored, which is the version it had learned durably before it stopped.");

                    CommittedRecord? served = await probe.ReadCommittedAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsNotNull(served, "The rejoined member serves a catch-up read rather than answering as a fresh host.");
                    Assert.AreEqual(claimed.Version, served!.Version, "What it serves stands at the version the claim was decided at.");
                    Assert.AreEqual(decided, served.Value, "What it serves equals the decided record by value, having come back through the store's codec and then across the wire.");
                    Assert.AreNotSame(decided, served.Value, "The served record was rebuilt from bytes twice over, so the equality above is structural and not reference identity.");
                }

                //The fellows' own view heals the same way: the connection the crash tore is answered as a fault
                //once and dialled afresh on the call after it.
                RegisterReadiness healed = await ReadinessOverSocketsAsync(writer, TestContext.CancellationToken).ConfigureAwait(false);
                for(int retry = 0; retry < SettleRetries && healed.Reachable < MemberCount; retry++)
                {
                    healed = await ReadinessOverSocketsAsync(writer, TestContext.CancellationToken).ConfigureAwait(false);
                }

                Assert.AreEqual(MemberCount, healed.Reachable, "Every member answers the cluster's own readiness report again, the rejoined one included.");
                Assert.AreEqual(
                    claimed.Version,
                    ReadinessOf(healed, rejoined.Axis).Version!.Value,
                    "The cluster's own report places the rejoined member at the version it restored, which is what an operator gating the next change reads.");
            }
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The version report's wire body, pinned at the codec: a written report reads back value-identical, and
    /// the reader refuses every body that is not exactly the report's fixed width holding a version in the
    /// register's range — one byte short, one byte long, and a version above the register's maximum, each of
    /// them the peer's frame to fix.
    /// </summary>
    [TestMethod]
    public void TheVersionReportBodyRoundTripsAndItsReaderRefusesWidthAndRange()
    {
        HostId self = Founder(0xC7).ToHostId();
        MemberVersionReport report = new(self, new RegisterVersion(3));

        ArrayBufferWriter<byte> written = new(MetadataChannelFraming.VersionReportByteLength);
        MetadataChannelFraming.WriteVersionReport(report, written);
        Assert.AreEqual(MetadataChannelFraming.VersionReportByteLength, written.WrittenCount, "The report's body is its fixed width and nothing more.");

        MemberVersionReport read = MetadataChannelFraming.ReadVersionReport(new ReadOnlySequence<byte>(written.WrittenMemory));
        Assert.AreEqual(report.Recorder, read.Recorder, "The identity reads back as the identity that was written.");
        Assert.AreEqual(report.Version, read.Version, "The version reads back as the version that was written.");

        Assert.IsNotNull(
            RefusalOf(new ReadOnlySequence<byte>(written.WrittenMemory[..(MetadataChannelFraming.VersionReportByteLength - 1)])),
            "A body one byte short of the report's width is refused rather than sliced.");

        ArrayBufferWriter<byte> oversized = new(MetadataChannelFraming.VersionReportByteLength + 1);
        MetadataChannelFraming.WriteVersionReport(report, oversized);
        oversized.GetSpan(1)[0] = 0;
        oversized.Advance(1);
        Assert.IsNotNull(
            RefusalOf(new ReadOnlySequence<byte>(oversized.WrittenMemory)),
            "A body one byte past the report's width is refused rather than truncated.");

        ArrayBufferWriter<byte> outOfRange = new(MetadataChannelFraming.VersionReportByteLength);
        Span<byte> body = outOfRange.GetSpan(MetadataChannelFraming.VersionReportByteLength);
        self.Replica.CopyTo(body);
        self.Incarnation.CopyTo(body[ReplicaId.Size..]);
        BinaryPrimitives.WriteUInt64BigEndian(body[(ReplicaId.Size + StoreIncarnation.Size)..MetadataChannelFraming.VersionReportByteLength], ulong.MaxValue);
        outOfRange.Advance(MetadataChannelFraming.VersionReportByteLength);
        Assert.IsNotNull(
            RefusalOf(new ReadOnlySequence<byte>(outOfRange.WrittenMemory)),
            "A version no host can hold is the peer's frame to fix and is refused as such.");
    }

    /// <summary>
    /// The probe call's contract at the serving side: a version probe that carries a payload is answered with
    /// the malformed-payload fault — the peer's frame to fix, named as a class and never as the host's own
    /// failure — and the fault answers ONE call: the same connection then serves a well-formed probe, answered
    /// under the serving binding's own identity with the unwritten version its empty host holds.
    /// </summary>
    [TestMethod]
    public async Task AVersionProbeCarryingAPayloadIsAnsweredWithAMalformedPayloadFaultAndTheServeContinues()
    {
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            HostId self = Founder(0xD9).ToHostId();
            MetadataWireCodecs codecs = CreateWireCodecs();
            FixedServeBinding binding = new(new MetadataServeBinding(self, RefuseRecordExchange, ReadNoCommittedRecord, RefuseOfferedRecord));
            MetadataChannelServer server = new(binding.Provide, codecs.DeserializeRequest, codecs.SerializeReply, codecs.SerializeRecord, codecs.DeserializeRecord, pool);

            Pipe calls = new();
            Pipe answers = new();
            Task serve = server.ServeAsync(calls.Reader, answers.Writer, TestContext.CancellationToken);

            MessageChannelWriter<MetadataChannelFraming.OutboundFrame> writer = new(calls.Writer, MetadataChannelFraming.WriteFrame, MessageChannel.DefaultMaxFrameLength);
            OwnedMessageChannelReader<MetadataChannelFraming.InboundFrame> reader = new(answers.Reader, MetadataChannelFraming.ReadOwnedFrame, pool, MessageChannel.DefaultMaxFrameLength);
            IAsyncEnumerator<MetadataChannelFraming.InboundFrame> frames = reader.ReadAllAsync(TestContext.CancellationToken).GetAsyncEnumerator(TestContext.CancellationToken);
            await using(frames.ConfigureAwait(false))
            {
                await writer.WriteAsync(MetadataChannelFraming.OutboundFrame.ForPayload(1, MetadataChannelFraming.VersionProbeKind, WriteOneStrayByte), TestContext.CancellationToken).ConfigureAwait(false);

                Assert.IsTrue(await frames.MoveNextAsync().ConfigureAwait(false), "The serve answers the malformed call rather than ending the connection.");
                using(MetadataChannelFraming.InboundFrame fault = frames.Current)
                {
                    Assert.AreEqual(1UL, fault.CorrelationId, "The answer names the call it answers.");
                    Assert.IsTrue(fault.IsFault, "A probe carrying a payload is answered with a fault frame.");
                    Assert.AreEqual(MetadataChannelFraming.MalformedPayloadFault, fault.FaultCode, "The fault names the peer's frame as the failure's class, never the host's own serve.");
                }

                await writer.WriteAsync(MetadataChannelFraming.OutboundFrame.ForAbsent(2, MetadataChannelFraming.VersionProbeKind), TestContext.CancellationToken).ConfigureAwait(false);

                Assert.IsTrue(await frames.MoveNextAsync().ConfigureAwait(false), "The connection outlives the fault, so the next call on it is served.");
                using(MetadataChannelFraming.InboundFrame answer = frames.Current)
                {
                    Assert.AreEqual(2UL, answer.CorrelationId, "The answer names the call it answers.");
                    Assert.IsFalse(answer.IsFault, "A well-formed probe on the same connection is answered rather than faulted.");
                    Assert.IsTrue(answer.HasPayload, "A probe answer carries the report body.");

                    MemberVersionReport served = MetadataChannelFraming.ReadVersionReport(answer.Payload);
                    Assert.AreEqual(self, served.Recorder, "The answer asserts the serving binding's own identity, never one echoed off the call.");
                    Assert.AreEqual(RegisterVersion.Unwritten, served.Version, "A host that has learned nothing answers the unwritten version over a working route.");
                }
            }

            await writer.CompleteAsync().ConfigureAwait(false);
            await serve.ConfigureAwait(false);
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>
    /// The register's identity refusal over the REAL transport: member 2's route is pointed at member 1's
    /// listener before anything dials — the hand-wired endpoint map whose two routes land on one host — and a
    /// readiness read refuses by raising rather than counting one host in two slots of the report a
    /// decommission gate clears on. Correcting the map and closing the stale connection brings the report back
    /// whole, so the refusal is evidence about the map and never about a report that could not be read.
    /// </summary>
    [TestMethod]
    public async Task AReadinessReadRefusesAProbeRouteLandingOnAnotherMembersHost()
    {
        TestContext.WriteLine(FormattableString.Invariant($"Proposal-priority seed: {MisrouteSeed}."));
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataSocketCluster cluster = await MetadataSocketCluster.StartAsync(new MetadataClusterOptions { Pool = pool, PrioritySeed = MisrouteSeed }).ConfigureAwait(false);
            await using(cluster.ConfigureAwait(false))
            {
                //The misroute is wired before anything dials, so every exchange aimed at member 2 — the
                //consensus legs and the probe alike — reaches member 1's serve loop.
                cluster.MisrouteTo(2, 1);

                MetadataSocketMember observer = cluster.Members[0];

                MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await observer.Plane.BootstrapAsync(DecidingAttemptBudget, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(PlaneBootstrapOutcome.Bootstrapped, bootstrapped.Outcome, "The bootstrap decides over the two honestly routed members: a misrouted recorder's replies name the wrong host and are absorbed as that member's unavailability, never counted as its answers.");

                InvalidOperationException? refusal = null;
                try
                {
                    _ = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                }
                catch(InvalidOperationException refused)
                {
                    refusal = refused;
                }

                Assert.IsNotNull(refusal, "The probe aimed at member 2 was answered by a host asserting member 1's identity, and the register refuses the report rather than letting one host fill two slots of a count a decommission gate clears on.");

                //The map is corrected and the stale connection the misroute left open is closed, so the next
                //probe dials member 2's own listener.
                cluster.MisrouteTo(2, 2);
                cluster.Cut(2);
                cluster.Heal(2);

                RegisterReadiness healed = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                for(int retry = 0; retry < SettleRetries && healed.Reachable < MemberCount; retry++)
                {
                    //A connection the cut tore is answered as a fault once and dialled afresh on the call
                    //after it, so the re-read is driven by the previous ANSWER and never by a clock.
                    healed = await ReadinessOverSocketsAsync(observer, TestContext.CancellationToken).ConfigureAwait(false);
                }

                Assert.AreEqual(MemberCount, healed.Reachable, "Every member answers over its own route once the map is corrected, so the refusal above was the mis-wiring and not a report that could never be read.");
            }
        }
        finally
        {
            pool.TrimExcess();
        }
    }

    /// <summary>A deterministic replica axis whose 32 bytes all carry <paramref name="seed"/>.</summary>
    /// <param name="seed">The byte every position of the identity carries.</param>
    /// <returns>The axis.</returns>
    private static MetadataFounder Founder(byte seed)
    {
        Span<byte> store = stackalloc byte[StoreIncarnation.Size];
        store.Fill(seed);

        return new MetadataFounder(Axis(seed), StoreIncarnation.FromSpan(store));
    }


    /// <summary>The store this bench admits for the member built from one seed, derived so a row is reproducible.</summary>
    /// <param name="seed">The byte the store is filled with.</param>
    /// <returns>The store incarnation.</returns>
    private static StoreIncarnation Store(byte seed)
    {
        Span<byte> store = stackalloc byte[StoreIncarnation.Size];
        store.Fill(seed);

        return StoreIncarnation.FromSpan(store);
    }


    private static ReplicaAxis Axis(byte seed)
    {
        byte[] bytes = new byte[ReplicaAxis.ByteWidth];
        Array.Fill(bytes, seed);

        return new ReplicaAxis(bytes);
    }

    /// <summary>Reads one readiness report through a member's plane, under the bound every observation of this battery runs under.</summary>
    /// <param name="observer">The member whose plane assembles the report.</param>
    /// <param name="cancellationToken">The row's token.</param>
    /// <returns>The report.</returns>
    /// <remarks>
    /// The bound is a backstop over a probe that crosses a cut route: a cut refuses an open and closes what was
    /// open, so a probe faults rather than waiting, and nothing in a passing row reaches the bound.
    /// </remarks>
    private static async Task<RegisterReadiness> ReadinessOverSocketsAsync(MetadataSocketMember observer, CancellationToken cancellationToken)
    {
        return await observer.Plane
            .ReadReadinessAsync(cancellationToken)
            .WaitAsync(ObservationBackstop, TimeProvider.System, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The entry one named member reported in a readiness report, found by identity rather than by position.</summary>
    /// <param name="readiness">The report to read.</param>
    /// <param name="axis">The member whose entry is wanted.</param>
    /// <returns>That member's entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the report carries no entry for that member.</exception>
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

        throw new InvalidOperationException($"The readiness report carries no entry for {member}, so it was measured over a membership that does not list that member.");
    }

    /// <summary>How many entries of a readiness report claim <paramref name="version"/>.</summary>
    /// <param name="readiness">The report to count over.</param>
    /// <param name="version">The version an entry must claim to count.</param>
    /// <returns>The number of members reporting that version.</returns>
    private static int MembersAtVersion(RegisterReadiness readiness, RegisterVersion version)
    {
        int count = 0;
        foreach(MemberReadiness entry in readiness.Members)
        {
            if(entry.Version == version)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The member to cut so that every member left reachable is one the report proved holding
    /// <paramref name="version"/>: the misser when the quorum floor left one member unproven, and the last
    /// member when the report was a census.
    /// </summary>
    /// <param name="readiness">The report the holding set is read from.</param>
    /// <param name="version">The decided version a holder claims.</param>
    /// <param name="cluster">The cluster whose founder order names the members.</param>
    /// <returns>The index of the member to cut.</returns>
    private static int CutTargetFrom(RegisterReadiness readiness, RegisterVersion version, MetadataSocketCluster cluster)
    {
        for(int index = 0; index < cluster.Members.Count; index++)
        {
            if(ReadinessOf(readiness, cluster.Members[index].Axis).Version != version)
            {
                return index;
            }
        }

        return cluster.Members.Count - 1;
    }

    /// <summary>Reads one version report body expecting the reader to refuse it, and answers the refusal by value.</summary>
    /// <param name="payload">The body to read.</param>
    /// <returns>The refusal, or <see langword="null"/> when the reader accepted the body.</returns>
    private static InvalidDataException? RefusalOf(ReadOnlySequence<byte> payload)
    {
        try
        {
            _ = MetadataChannelFraming.ReadVersionReport(payload);

            return null;
        }
        catch(InvalidDataException refused)
        {
            return refused;
        }
    }

    /// <summary>Answers a record exchange the probe rows never send — reaching it is a row mis-driving its own serve.</summary>
    /// <param name="request">The versioned record request that should never arrive.</param>
    /// <param name="cancellationToken">The serving call's token.</param>
    /// <returns>Never returns.</returns>
    private static ValueTask<VersionedRecordReply<CommittedRecord>> RefuseRecordExchange(VersionedRecordRequest<CommittedRecord> request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("The probe rows send only version probes, so their serve binding's recorder seam is never reached.");
    }

    /// <summary>Answers a catch-up read with no record, which is what makes a probe's answer the unwritten version.</summary>
    /// <param name="cancellationToken">The serving call's token.</param>
    /// <returns>No record.</returns>
    private static ValueTask<CommittedRecord?> ReadNoCommittedRecord(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<CommittedRecord?>(null);
    }

    /// <summary>Learns an offered record the probe rows never push — reaching it is a row mis-driving its own serve.</summary>
    /// <param name="committed">The decided record that should never arrive.</param>
    /// <param name="cancellationToken">The serving call's token.</param>
    /// <returns>Never returns.</returns>
    private static ValueTask RefuseOfferedRecord(CommittedRecord committed, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("The probe rows send only version probes, so their serve binding's dissemination seam is never reached.");
    }

    /// <summary>Writes the one stray byte a malformed probe call carries — a <see cref="MetadataChannelFraming.WriteFramePayloadDelegate"/> bound as a static method group.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    private static void WriteOneStrayByte(IBufferWriter<byte> output)
    {
        Span<byte> stray = output.GetSpan(1);
        stray[0] = 0;
        output.Advance(1);
    }

    /// <summary>Runs one member's identity claim on its own thread, recording the interval it ran over and what it established.</summary>
    /// <param name="member">The member whose plane writes.</param>
    /// <param name="label">The name this operation carries in the printed history.</param>
    /// <param name="cancellationToken">The row's token.</param>
    /// <returns>The completed operation.</returns>
    /// <remarks>
    /// The retry is the design's own answer to definite ignorance and not a workaround: an undecided attempt
    /// learned nothing, so the catch-up is what lets the re-issue compose on the record the cluster actually
    /// holds instead of proposing again at a version someone else has closed. Both stamps are taken on this
    /// thread around the whole obligation, so the interval they bound is the one an outside observer would see.
    /// </remarks>
    private static Task<ClaimOperation> ClaimOnItsOwnThreadAsync(MetadataSocketMember member, string label, CancellationToken cancellationToken)
    {
        ClaimWorkload workload = new(member, label, cancellationToken);

        return Task.Run(workload.RunAsync, cancellationToken);
    }

    /// <summary>
    /// Binds one member's identity claim to the thread that runs it as an explicit frame, so the thread's
    /// workload captures nothing.
    /// </summary>
    /// <param name="member">The member whose plane writes.</param>
    /// <param name="label">The name the operation carries in the printed history.</param>
    /// <param name="cancellationToken">The row's token.</param>
    private sealed class ClaimWorkload(MetadataSocketMember member, string label, CancellationToken cancellationToken)
    {
        /// <summary>The member whose plane writes.</summary>
        private MetadataSocketMember Member { get; } = member;

        /// <summary>The name the operation carries in the printed history.</summary>
        private string Label { get; } = label;

        /// <summary>The row's token.</summary>
        private CancellationToken CancellationToken { get; } = cancellationToken;

        /// <summary>Runs the claim with its catch-up retries, stamping the interval on the running thread.</summary>
        /// <returns>The completed operation.</returns>
        public async Task<ClaimOperation> RunAsync()
        {
            long invoked = Stopwatch.GetTimestamp();

            MetadataPlaneResult<IdentityClaimOutcome> result = await Member.Plane.ClaimIdentityAsync(Member.Axis, DecidingAttemptBudget, CancellationToken).ConfigureAwait(false);
            for(int retry = 0; retry < SettleRetries && result.Outcome == IdentityClaimOutcome.Undecided; retry++)
            {
                _ = await Member.Plane.ReadRecordAsync(CancellationToken).ConfigureAwait(false);
                result = await Member.Plane.ClaimIdentityAsync(Member.Axis, DecidingAttemptBudget, CancellationToken).ConfigureAwait(false);
            }

            return new ClaimOperation(Label, Member.Axis, invoked, Stopwatch.GetTimestamp(), result.Outcome, result.Version);
        }
    }

    /// <summary>Binds one fixed serve binding to the per-serve seam as an explicit frame, so the provider captures nothing.</summary>
    /// <param name="binding">The binding every serve of this endpoint dispatches through.</param>
    private sealed class FixedServeBinding(MetadataServeBinding binding)
    {
        /// <summary>The binding every serve dispatches through.</summary>
        private MetadataServeBinding Binding { get; } = binding;

        /// <summary>Hands the fixed binding to one serve — a <see cref="ProvideMetadataServeBindingDelegate"/>.</summary>
        /// <returns>The binding.</returns>
        public MetadataServeBinding Provide()
        {
            return Binding;
        }
    }

    /// <summary>What each member of the cluster holds, asked over that member's own connection.</summary>
    /// <param name="cluster">The cluster to ask.</param>
    /// <param name="pool">The pool the probe connections rent their buffers from.</param>
    /// <param name="cancellationToken">The row's token.</param>
    /// <returns>One entry per member in founder order: the record it served, or <see langword="null"/> when it has learned none.</returns>
    /// <remarks>
    /// Each member is asked through a connection of this row's own rather than through a writer's channels, so
    /// the answers are the hosts' and carry nothing a writer's own belief could have supplied.
    /// </remarks>
    private static async Task<CommittedRecord?[]> HeldOverSocketsAsync(MetadataSocketCluster cluster, MemoryPool<byte> pool, CancellationToken cancellationToken)
    {
        CommittedRecord?[] held = new CommittedRecord?[cluster.Members.Count];
        for(int index = 0; index < cluster.Members.Count; index++)
        {
            MetadataChannelClient probe = new(
                cluster.RouteTo(index),
                cluster.Codecs.SerializeRequest,
                cluster.Codecs.DeserializeReply,
                cluster.Codecs.SerializeRecord,
                cluster.Codecs.DeserializeRecord,
                pool);
            await using(probe.ConfigureAwait(false))
            {
                held[index] = await probe.ReadCommittedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return held;
    }

    /// <summary>The highest record among what the members served, which is the chain every operation must sit on.</summary>
    /// <param name="held">What each member served.</param>
    /// <returns>The highest record, or <see langword="null"/> when no member served one.</returns>
    private static CommittedRecord? Highest(CommittedRecord?[] held)
    {
        CommittedRecord? highest = null;
        foreach(CommittedRecord? record in held)
        {
            if(record is not null && (highest is null || record.Version > highest.Version))
            {
                highest = record;
            }
        }

        return highest;
    }

    /// <summary>How many claims on one record name <paramref name="axis"/>.</summary>
    /// <param name="record">The record to count over.</param>
    /// <param name="axis">The axis to count.</param>
    /// <returns>The number of claims naming that axis.</returns>
    /// <remarks>
    /// A claim is appended and never rewritten, so one writer's effect written twice is a duplicate entry rather
    /// than an overwrite, and only a count can see it.
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

    /// <summary>A file-content flush that does nothing, so a row about the persist face being wired does not also depend on a real device flush.</summary>
    /// <param name="handle">The open handle to the written file whose bytes would be flushed.</param>
    private static void NoOpFlush(SafeFileHandle handle)
    {
    }

    /// <summary>A directory durability barrier that does nothing, for the same reason <see cref="NoOpFlush"/> does nothing.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>
    /// Builds the six wire codecs one member's channel is composed from, all of them the consensus library's own
    /// JSON envelopes over this battery's coordinated-record value seam.
    /// </summary>
    /// <returns>The codecs.</returns>
    /// <remarks>
    /// The two value-seam shapes the JSON factories take live only as method-local values here, and the
    /// bare decided-record pair is bound through an explicit frame, so nothing in this file holds one in a field
    /// and nothing captures an enclosing scope.
    /// </remarks>
    private static MetadataWireCodecs CreateWireCodecs()
    {
        WriteValueDelegate<Utf8JsonWriter, CommittedRecord> writeDecided = QuePaxaMessageJson.CreateVersionedValueWriter<VeritasMetadataRecord>(WriteMetadataRecord);
        ReadValueDelegate<JsonElement, CommittedRecord> readDecided = QuePaxaMessageJson.CreateVersionedValueReader<VeritasMetadataRecord>(ReadMetadataRecord);
        DecidedRecordCodec decided = new(new WriteDecidedRecordDelegate(writeDecided), new ReadDecidedRecordDelegate(readDecided));

        return new MetadataWireCodecs(
            QuePaxaMessageJson.CreateVersionedRequestSerializer(writeDecided),
            QuePaxaMessageJson.CreateVersionedRequestDeserializer(readDecided),
            QuePaxaMessageJson.CreateVersionedReplySerializer(writeDecided),
            QuePaxaMessageJson.CreateVersionedReplyDeserializer(readDecided),
            decided.Serialize,
            decided.Deserialize);
    }

    /// <summary>
    /// Writes one coordinated metadata record as the application value inside a consensus payload. Every
    /// identifier that is 64 bits wide is written as a decimal STRING rather than as a bare number, so a value
    /// above two to the fifty-third survives a reader that would parse a JSON number as a double.
    /// </summary>
    /// <param name="writer">The writer the value is written into.</param>
    /// <param name="record">The record to write.</param>
    /// <remarks>
    /// It is reachable across the suite so that ONE row can pin this body against the deployment's production
    /// codec byte for byte. That row is the whole reason the two copies may be compared at all: this file's
    /// codec seams stay its own, and the pin is what keeps an independent copy from becoming a second format.
    /// </remarks>
    internal static void WriteMetadataRecord(Utf8JsonWriter writer, VeritasMetadataRecord record)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("identityClaims");
        foreach(ReplicaIdentityClaim claim in record.IdentityClaims)
        {
            writer.WriteStartObject();
            writer.WriteString("axis", Convert.ToHexStringLower(claim.Axis.Bytes.Span));
            writer.WriteNumber("claimedAt", claim.ClaimedAt.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        if(record.Baseline is { } baseline)
        {
            writer.WritePropertyName("baseline");
            writer.WriteStartObject();
            writer.WriteString("claimantAxis", Convert.ToHexStringLower(baseline.ClaimantAxis.Bytes.Span));
            writer.WriteString("causalityDigest", baseline.CausalityDigest.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("recordedAt", baseline.RecordedAt.Value);
            if(baseline.Confirmation is { } confirmation)
            {
                writer.WritePropertyName("confirmation");
                writer.WriteStartObject();
                writer.WriteString("stateId", confirmation.StateId.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("dictionaryEpoch", confirmation.DictionaryEpoch.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            else
            {
                //An unconfirmed intent is written as an explicit null, so absence stays distinguishable from a
                //field the payload never carried — the tri-state the baseline keeps.
                writer.WriteNull("confirmation");
            }

            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("baseline");
        }

        writer.WritePropertyName("policy");
        writer.WriteStartObject();
        writer.WriteNumber("healCadenceClass", record.Policy.HealCadenceClass);
        writer.WriteNumber("symbolBudgetTier", record.Policy.SymbolBudgetTier);
        writer.WriteEndObject();

        if(record.Coordinator is { } lease)
        {
            writer.WritePropertyName("coordinator");
            writer.WriteStartObject();
            writer.WriteString("holder", Convert.ToHexStringLower(lease.Holder.Bytes.Span));
            writer.WriteNumber("term", lease.Term.Value);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("coordinator");
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads one coordinated metadata record back. Nothing here refuses a payload on a rule of its own: a missing
    /// field, a malformed identifier or a value a domain constructor rejects each surfaces from this body and
    /// reaches the caller as the codec's own fail-closed refusal.
    /// </summary>
    /// <param name="element">The element the value was written into.</param>
    /// <returns>The record the payload carries.</returns>
    /// <remarks>
    /// It is reachable across the suite for the same reason <see cref="WriteMetadataRecord"/> is: one row reads
    /// back what the production codec wrote, so the pin covers both directions of the format rather than the
    /// encoded bytes alone.
    /// </remarks>
    internal static VeritasMetadataRecord ReadMetadataRecord(JsonElement element)
    {
        JsonElement claimsElement = element.GetProperty("identityClaims");
        ImmutableArray<ReplicaIdentityClaim>.Builder claims = ImmutableArray.CreateBuilder<ReplicaIdentityClaim>(claimsElement.GetArrayLength());
        foreach(JsonElement claim in claimsElement.EnumerateArray())
        {
            claims.Add(new ReplicaIdentityClaim(
                new ReplicaAxis(Convert.FromHexString(claim.GetProperty("axis").GetString()!)),
                new RegisterVersion(claim.GetProperty("claimedAt").GetUInt64())));
        }

        JsonElement baselineElement = element.GetProperty("baseline");
        JsonElement policyElement = element.GetProperty("policy");
        JsonElement coordinatorElement = element.GetProperty("coordinator");

        return new VeritasMetadataRecord(
            IdentityClaims: claims.MoveToImmutable(),
            Baseline: baselineElement.ValueKind == JsonValueKind.Null ? null : ReadBaseline(baselineElement),
            Policy: new CoordinationPolicy(policyElement.GetProperty("healCadenceClass").GetInt32(), policyElement.GetProperty("symbolBudgetTier").GetInt32()),
            Coordinator: coordinatorElement.ValueKind == JsonValueKind.Null ? null : ReadLease(coordinatorElement));
    }

    /// <summary>Reads the lineage baseline, whose confirmation is present as a whole or absent as a whole.</summary>
    /// <param name="element">The baseline element.</param>
    /// <returns>The baseline.</returns>
    private static LineageBaseline ReadBaseline(JsonElement element)
    {
        JsonElement confirmationElement = element.GetProperty("confirmation");
        LineageConfirmation? confirmation = confirmationElement.ValueKind == JsonValueKind.Null
            ? null
            : new LineageConfirmation(
                new NodeIdentifier(ulong.Parse(confirmationElement.GetProperty("stateId").GetString()!, CultureInfo.InvariantCulture)),
                long.Parse(confirmationElement.GetProperty("dictionaryEpoch").GetString()!, CultureInfo.InvariantCulture));

        return new LineageBaseline(
            ClaimantAxis: new ReplicaAxis(Convert.FromHexString(element.GetProperty("claimantAxis").GetString()!)),
            CausalityDigest: new NodeIdentifier(ulong.Parse(element.GetProperty("causalityDigest").GetString()!, CultureInfo.InvariantCulture)),
            Confirmation: confirmation,
            RecordedAt: new RegisterVersion(element.GetProperty("recordedAt").GetUInt64()));
    }

    /// <summary>Reads the coordinator lease.</summary>
    /// <param name="element">The lease element.</param>
    /// <returns>The lease.</returns>
    private static CoordinatorLease ReadLease(JsonElement element)
    {
        return new CoordinatorLease(
            new ReplicaAxis(Convert.FromHexString(element.GetProperty("holder").GetString()!)),
            new RegisterVersion(element.GetProperty("term").GetUInt64()));
    }

    /// <summary>
    /// One completed obligation of the multi-writer workload, recorded for the history that row checks: which
    /// writer it was, the real-time interval it ran over, and what it established.
    /// </summary>
    /// <param name="Label">The writer's name in the printed history.</param>
    /// <param name="Axis">The identity axis the obligation claimed, which is what its effect is looked up by on the final record.</param>
    /// <param name="Invoked">The timestamp the obligation was invoked at, taken on the writer's own thread.</param>
    /// <param name="Completed">The timestamp it completed at, taken on that same thread.</param>
    /// <param name="Outcome">The ladder value it was answered with.</param>
    /// <param name="Version">The version its write was decided at, or <see cref="RegisterVersion.Unwritten"/> when it established nothing.</param>
    /// <remarks>
    /// The stamps are <see cref="Stopwatch.GetTimestamp"/> readings and are compared only with each other, which
    /// is what a real-time precedence check needs and all it needs: no assertion reads one as a duration, so
    /// nothing here turns on the counter's frequency or on how fast the machine ran.
    /// </remarks>
    private sealed record ClaimOperation(string Label, ReplicaAxis Axis, long Invoked, long Completed, IdentityClaimOutcome Outcome, RegisterVersion Version);

    /// <summary>Writes one decided metadata record onto a JSON writer — the value seam a decided-record frame carries.</summary>
    /// <param name="writer">The writer the record is written into.</param>
    /// <param name="record">The decided record.</param>
    private delegate void WriteDecidedRecordDelegate(Utf8JsonWriter writer, CommittedRecord record);

    /// <summary>Reads one decided metadata record back from a JSON element — the counterpart of <see cref="WriteDecidedRecordDelegate"/>.</summary>
    /// <param name="element">The element the record was written into.</param>
    /// <returns>The decided record.</returns>
    private delegate CommittedRecord ReadDecidedRecordDelegate(JsonElement element);

    /// <summary>
    /// The six codecs one metadata channel is composed from: the versioned request and reply pair a consensus
    /// record exchange carries, and the bare decided-record pair a catch-up answer and a dissemination push
    /// carry.
    /// </summary>
    /// <param name="SerializeRequest">Writes one consensus record request.</param>
    /// <param name="DeserializeRequest">Reads one consensus record request back.</param>
    /// <param name="SerializeReply">Writes a host's record reply.</param>
    /// <param name="DeserializeReply">Reads a member's record reply back.</param>
    /// <param name="SerializeRecord">Writes one decided record.</param>
    /// <param name="DeserializeRecord">Reads one decided record back.</param>
    private sealed record MetadataWireCodecs(
        SerializeMessageDelegate<VersionedRecordRequest<CommittedRecord>> SerializeRequest,
        DeserializeMessageDelegate<VersionedRecordRequest<CommittedRecord>> DeserializeRequest,
        SerializeMessageDelegate<VersionedRecordReply<CommittedRecord>> SerializeReply,
        DeserializeMessageDelegate<VersionedRecordReply<CommittedRecord>> DeserializeReply,
        SerializeMessageDelegate<CommittedRecord> SerializeRecord,
        DeserializeMessageDelegate<CommittedRecord> DeserializeRecord);

    /// <summary>
    /// Binds the decided-record value seams to the message codecs the channel expects, as an explicit frame so
    /// neither face captures an enclosing scope.
    /// </summary>
    /// <param name="write">The value seam that writes one decided record.</param>
    /// <param name="read">The value seam that reads one decided record back.</param>
    private sealed class DecidedRecordCodec(WriteDecidedRecordDelegate write, ReadDecidedRecordDelegate read)
    {
        /// <summary>The value seam that writes one decided record.</summary>
        private WriteDecidedRecordDelegate Write { get; } = write;

        /// <summary>The value seam that reads one decided record back.</summary>
        private ReadDecidedRecordDelegate Read { get; } = read;

        /// <summary>Writes one decided record into a frame's channel buffer — a <see cref="SerializeMessageDelegate{TMessage}"/>.</summary>
        /// <param name="record">The record to write.</param>
        /// <param name="output">The buffer to write into.</param>
        public void Serialize(CommittedRecord record, IBufferWriter<byte> output)
        {
            //The writer's disposal is what flushes the encoded bytes into the frame's buffer, so it ends inside
            //this call and not at some later point the caller would have to know about.
            using Utf8JsonWriter writer = new(output);
            Write(writer, record);
        }

        /// <summary>Reads one decided record back from a frame's payload — a <see cref="DeserializeMessageDelegate{TMessage}"/>.</summary>
        /// <param name="payload">The payload to read.</param>
        /// <returns>The decided record.</returns>
        public CommittedRecord Deserialize(ReadOnlySequence<byte> payload)
        {
            using JsonDocument document = JsonDocument.Parse(payload);

            return Read(document.RootElement);
        }
    }

    /// <summary>
    /// A deterministic ordinary-priority source: xorshift64 over a seed the row prints, so a failing row replays
    /// the identical draws on any runtime. One source per member, drawn only on that member's own write path,
    /// which the plane's queue serializes.
    /// </summary>
    /// <param name="seed">The seed the stream starts from.</param>
    private sealed class SeededPrioritySource(ulong seed)
    {
        /// <summary>The xorshift state; a naked field because the shifts rewrite it in place, and zero is replaced because xorshift is stuck there.</summary>
        private ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        /// <summary>Draws the next ordinary priority — a <see cref="ProposalPrioritySourceDelegate"/>.</summary>
        /// <returns>A priority satisfying the source's ordinary-value contract.</returns>
        public ProposalPriority Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;

            //The two reserved endpoints are excluded, so the draw honours the source's contract exactly.
            ulong value = state == 0 || state == ulong.MaxValue ? 0x0123456789ABCDEFUL : state;

            return new ProposalPriority(value);
        }
    }

    /// <summary>Collects the governance verdicts the writer's decorated channels produced, so a row reads what the gate decided rather than inferring it from an outcome alone.</summary>
    /// <remarks>
    /// The verdicts arrive from the dissemination fan-out's legs as well as from the write path, so the queue is
    /// concurrent; the handler is a method group and captures nothing.
    /// </remarks>
    private sealed class GovernanceTraceCapture
    {
        /// <summary>The verdicts, in arrival order.</summary>
        private ConcurrentQueue<NetworkGovernanceTraceEvent> Verdicts { get; } = new();

        /// <summary>How many verdicts denied a call.</summary>
        public int DenialCount
        {
            get
            {
                int denials = 0;
                foreach(NetworkGovernanceTraceEvent verdict in Verdicts)
                {
                    if(verdict.Outcome == NetworkGovernanceKind.Deny)
                    {
                        denials += 1;
                    }
                }

                return denials;
            }
        }

        /// <summary>How many DISTINCT peers were denied, which is what tells a row that the gate refused the member it named and no other.</summary>
        public int DeniedPeerCount
        {
            get
            {
                HashSet<long> peers = [];
                foreach(NetworkGovernanceTraceEvent verdict in Verdicts)
                {
                    if(verdict.Outcome == NetworkGovernanceKind.Deny)
                    {
                        _ = peers.Add(verdict.PeerKeyHash);
                    }
                }

                return peers.Count;
            }
        }

        /// <summary>Whether every denial was taken at the consensus-exchange boundary.</summary>
        public bool EveryDenialNamesTheConsensusBoundary
        {
            get
            {
                foreach(NetworkGovernanceTraceEvent verdict in Verdicts)
                {
                    if(verdict.Outcome == NetworkGovernanceKind.Deny && verdict.Boundary != NetworkBoundary.ConsensusExchange)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Captures one verdict — a <see cref="TraceHandler{TEvent}"/>.</summary>
        /// <param name="evt">The emitted verdict.</param>
        public void Capture(in NetworkGovernanceTraceEvent evt)
        {
            Verdicts.Enqueue(evt);
        }
    }

    /// <summary>Everything one socket cluster is stood up from, so a row states its shape rather than threading nine arguments through a factory.</summary>
    private sealed class MetadataClusterOptions
    {
        /// <summary>The pool every frame payload, every stream pipe and every store buffer is rented from.</summary>
        public required MemoryPool<byte> Pool { get; init; }

        /// <summary>The seed each member's proposal-priority source is derived from; the row prints it.</summary>
        public required ulong PrioritySeed { get; init; }

        /// <summary>The directory each member's store lives under, or <see langword="null"/> when no member has a store.</summary>
        public string? StoreRoot { get; init; }

        /// <summary>The members whose runner is given the store's real persist face; every other member is given the documented no-op face.</summary>
        public ImmutableArray<int> DurableMembers { get; init; } = [];

        /// <summary>The member whose fellow channels run behind the network-governance gate, or a negative index when no member is governed.</summary>
        public int GovernedMember { get; init; } = -1;

        /// <summary>The policy the governed member's channels consult, or <see langword="null"/> when no member is governed.</summary>
        public NetworkGovernanceDelegate? Governance { get; init; }

        /// <summary>The sink the governed member's verdicts are emitted to, or <see langword="null"/> to emit nothing.</summary>
        public TraceHandler<NetworkGovernanceTraceEvent>? GovernanceTrace { get; init; }

        /// <summary>Whether the member at <paramref name="index"/> persists through a real store.</summary>
        /// <param name="index">The member index.</param>
        /// <returns><see langword="true"/> when that member's runner is given the store's persist face.</returns>
        public bool IsDurable(int index)
        {
            return DurableMembers.Contains(index);
        }
    }

    /// <summary>
    /// One member's route: the ephemeral loopback port its listener is bound to, the connections opened over it,
    /// and the cut that makes it unreachable. Cutting refuses every later open AND closes what is already open,
    /// so a partition holds whether or not a channel had already dialed.
    /// </summary>
    /// <param name="port">The loopback port the member's listener is bound to.</param>
    /// <param name="pool">The pool the connection's stream pipes rent their buffers from.</param>
    private sealed class MemberRoute(int port, MemoryPool<byte> pool)
    {
        /// <summary>The loopback port the member's listener is bound to, which a restart moves to the listener the revived member bound.</summary>
        private int Port { get; set; } = port;

        /// <summary>The pool the connection's stream pipes rent their buffers from.</summary>
        private MemoryPool<byte> Pool { get; } = pool;

        /// <summary>The gate the cut flag and the opened-connection list are read and written under.</summary>
        private Lock Gate { get; } = new();

        /// <summary>The connections opened over this route, so a cut closes what a channel had already dialed.</summary>
        private List<TcpClient> Opened { get; } = [];

        /// <summary>Whether the route is cut; a naked field because it is written and read only under <see cref="Gate"/>.</summary>
        private bool cut;

        /// <summary>Cuts the route: later opens are refused and every connection already open is closed.</summary>
        public void Cut()
        {
            List<TcpClient> live;
            lock(Gate)
            {
                cut = true;
                live = [.. Opened];
                Opened.Clear();
            }

            foreach(TcpClient connection in live)
            {
                Close(connection);
            }
        }

        /// <summary>Restores the route, so the next open dials again.</summary>
        public void Heal()
        {
            lock(Gate)
            {
                cut = false;
            }
        }

        /// <summary>Points this route at the port a revived member's fresh listener is bound to.</summary>
        /// <param name="port">The new loopback port.</param>
        /// <remarks>
        /// A restarted member binds port zero again and is answered a port of the platform's choosing, so a
        /// deployment's routing has to be told where the member went. That is the operator act a real
        /// deployment performs through its own locator, and this is the one place this battery's routing lives.
        /// </remarks>
        public void Rebind(int port)
        {
            lock(Gate)
            {
                Port = port;
            }
        }

        /// <summary>Opens one duplex connection to the member's metadata endpoint — an <see cref="OpenPeerMetadataConnectionDelegate"/>.</summary>
        /// <param name="cancellationToken">Cancels the connection attempt.</param>
        /// <returns>The opened connection; ownership transfers to the caller.</returns>
        /// <exception cref="IOException">The route is cut, which is what a partition looks like to a channel.</exception>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection and the socket transport it owns transfer to the caller per the OpenPeerMetadataConnectionDelegate contract; the metadata channel client disposes the connection on every fault, cancellation and teardown path, and a cut closes whatever is still open.")]
        public async ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            int target;
            lock(Gate)
            {
                if(cut)
                {
                    throw new IOException("The battery cut the route to this member, so nothing reaches it.");
                }

                target = Port;
            }

            TcpClient connection = new();
            try
            {
                await connection.ConnectAsync(IPAddress.Loopback, target, cancellationToken).ConfigureAwait(false);
                lock(Gate)
                {
                    if(cut)
                    {
                        throw new IOException("The battery cut the route to this member while the connection was being opened.");
                    }

                    Opened.Add(connection);
                }
            }
            catch(Exception)
            {
                connection.Dispose();

                throw;
            }

            NetworkStream stream = connection.GetStream();

            return new PeerChannelConnection(
                PipeWriter.Create(stream, new StreamPipeWriterOptions(Pool, leaveOpen: true)),
                PipeReader.Create(stream, new StreamPipeReaderOptions(Pool, leaveOpen: true)),
                new SocketTransport(connection));
        }

        /// <summary>Closes one connection, absorbing whatever a socket the runtime has already reclaimed reports.</summary>
        /// <param name="connection">The connection to close.</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cutting a route is the battery's partition injection and must never raise over a row's own outcome: a socket the runtime has already reclaimed, or one whose peer closed first, is exactly the state the cut asks for.")]
        private static void Close(TcpClient connection)
        {
            try
            {
                connection.Dispose();
            }
            catch(Exception)
            {
                //A socket already gone is a socket the cut has nothing left to do to.
            }
        }
    }

    /// <summary>Owns one connected socket on behalf of a <see cref="PeerChannelConnection"/>, so the connection's teardown closes the transport under it.</summary>
    /// <param name="connection">The connected socket.</param>
    private sealed class SocketTransport(TcpClient connection): IAsyncDisposable
    {
        /// <summary>The connected socket this transport owns.</summary>
        private TcpClient Connection { get; } = connection;

        /// <summary>Closes the socket. Disposal is idempotent, as the connection's own is.</summary>
        /// <returns>A completed task; closing a socket has no asynchronous form.</returns>
        public ValueTask DisposeAsync()
        {
            Connection.Dispose();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// The local seams one member's serve dispatches to, with an ARMED REFUSAL in front of two of them: a call
    /// taken while a refusal is armed raises, which the serve turns into an opaque fault frame. Nothing is armed
    /// unless a row arms it, so the same frame serves every member of every row.
    /// </summary>
    /// <param name="runner">The loop that owns the member's host.</param>
    /// <param name="cluster">The cluster this member belongs to, asked whether a row has armed a writer rendezvous.</param>
    private sealed class MemberServeBinding(QuePaxaVersionedRunner<VeritasMetadataRecord> runner, MetadataSocketCluster cluster)
    {
        /// <summary>The loop that owns the member's host.</summary>
        private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; } = runner;

        /// <summary>The cluster this member belongs to, whose writer rendezvous every served record exchange passes through.</summary>
        private MetadataSocketCluster Cluster { get; } = cluster;

        /// <summary>The completion the first refused record exchange sets, which is the transition a row awaits rather than a wait it guesses at.</summary>
        private TaskCompletionSource RecordFaultSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>How many further record exchanges refuse; a naked field because it is taken down with an interlocked exchange.</summary>
        private int armedRecordFaults;

        /// <summary>How many further committed reads refuse; a naked field for the same reason.</summary>
        private int armedReadFaults;

        /// <summary>How many record exchanges this binding refused; a naked field because it is advanced with an interlocked increment.</summary>
        private int recordFaults;

        /// <summary>How many committed reads this binding refused; a naked field for the same reason.</summary>
        private int readFaults;

        /// <summary>How many record exchanges this binding refused.</summary>
        public int RecordFaults => Volatile.Read(ref recordFaults);

        /// <summary>How many committed reads this binding refused.</summary>
        public int ReadFaults => Volatile.Read(ref readFaults);

        /// <summary>The completion the first refused record exchange sets.</summary>
        public Task RecordFaultAnswered => RecordFaultSource.Task;

        /// <summary>Arms the next <paramref name="calls"/> record exchanges to refuse.</summary>
        /// <param name="calls">How many record exchanges refuse.</param>
        public void ArmRecordFaults(int calls)
        {
            Volatile.Write(ref armedRecordFaults, calls);
        }

        /// <summary>Arms the next <paramref name="calls"/> committed reads to refuse.</summary>
        /// <param name="calls">How many committed reads refuse.</param>
        public void ArmReadFaults(int calls)
        {
            Volatile.Write(ref armedReadFaults, calls);
        }

        /// <summary>Hands one serve the host's three seams and the identity it answers a version probe under — a <see cref="ProvideMetadataServeBindingDelegate"/>.</summary>
        /// <returns>The binding this serve dispatches through.</returns>
        /// <remarks>
        /// The identity is read off the host the runner owns rather than off the member a caller asks about, so
        /// a probe served here carries the answering host's own claim, which is what the register's refusal of a
        /// foreign answer is compared against.
        /// </remarks>
        public MetadataServeBinding Provide()
        {
            return new MetadataServeBinding(Runner.Node.Self, RecordAsync, ReadCommittedAsync, OfferAsync);
        }

        /// <summary>Serves one consensus record exchange, refusing it while a refusal is armed and holding it while a writer rendezvous is.</summary>
        /// <param name="request">The versioned record request.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The host's reply.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="IOException">A refusal was armed, which the serve answers with a fault frame.</exception>
        /// <remarks>
        /// The rendezvous is consulted BEFORE the host's loop, so a held exchange occupies no recorder loop and
        /// this member keeps serving every other writer's connection while one is held.
        /// </remarks>
        public async ValueTask<VersionedRecordReply<CommittedRecord>> RecordAsync(VersionedRecordRequest<CommittedRecord> request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if(TakeFault(ref armedRecordFaults, ref recordFaults))
            {
                _ = RecordFaultSource.TrySetResult();

                throw new IOException("The battery's serve binding refuses this record exchange, so the host answers a fault frame rather than a reply.");
            }

            if(Cluster.Writers is { } gate)
            {
                await gate.ArriveAsync(request.Request.Proposal.Key.Owner.Replica, cancellationToken).ConfigureAwait(false);
            }

            return await Runner.RecordAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Serves one catch-up read, refusing it while a refusal is armed.</summary>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The host's committed record, or <see langword="null"/> when it has learned none.</returns>
        /// <exception cref="IOException">A refusal was armed, which the serve answers with a fault frame.</exception>
        public ValueTask<CommittedRecord?> ReadCommittedAsync(CancellationToken cancellationToken)
        {
            if(TakeFault(ref armedReadFaults, ref readFaults))
            {
                throw new IOException("The battery's serve binding refuses this committed read, so the host answers a fault frame rather than a record.");
            }

            return Runner.ReadCommittedAsync(cancellationToken);
        }

        /// <summary>Learns one disseminated record DURABLY on the member's host — an <see cref="OfferMetadataRecordDelegate"/>.</summary>
        /// <param name="committed">The decided record a peer pushed.</param>
        /// <param name="cancellationToken">Cancels the learn.</param>
        /// <returns>A task that completes once the host has learned the record durably.</returns>
        public async ValueTask OfferAsync(CommittedRecord committed, CancellationToken cancellationToken)
        {
            //Whether the record advanced the host is the learn's own answer and not the offer's: a record the
            //host already held is as fully offered as one that moved it.
            _ = await Runner.LearnAsync(committed, LearnDurability.Durable, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Consumes one armed refusal and counts it, never taking the armed counter below zero.</summary>
        /// <param name="armed">The armed counter to take from.</param>
        /// <param name="taken">The counter the consumed refusal is recorded on.</param>
        /// <returns><see langword="true"/> when a refusal was armed and this call consumed it.</returns>
        private static bool TakeFault(ref int armed, ref int taken)
        {
            while(true)
            {
                int remaining = Volatile.Read(ref armed);
                if(remaining <= 0)
                {
                    return false;
                }

                if(Interlocked.CompareExchange(ref armed, remaining - 1, remaining) == remaining)
                {
                    _ = Interlocked.Increment(ref taken);

                    return true;
                }
            }
        }
    }

    /// <summary>
    /// One member of a socket cluster: its consensus host and the loop that owns it, the listener its fellows
    /// dial, the accept loop that serves them, the transport binding its plane reaches its fellows through, and
    /// the plane itself. Everything the member owns is torn down by its own disposal, in the order that lets a
    /// SINGLE member be stopped while the rest of the cluster keeps running.
    /// </summary>
    private sealed class MetadataSocketMember: IAsyncDisposable
    {
        /// <summary>Composes one member over a listener the cluster has already bound.</summary>
        /// <param name="cluster">The cluster this member belongs to, asked which route reaches a named fellow.</param>
        /// <param name="options">The cluster's shape.</param>
        /// <param name="index">This member's position in the founder order.</param>
        /// <param name="axis">This member's identity axis.</param>
        /// <param name="listener">The bound listener this member serves its fellows through.</param>
        /// <param name="restoredState">The host state this member comes back from, or <see langword="null"/> for a member starting fresh.</param>
        /// <exception cref="ArgumentException">Thrown by the host's own restore when <paramref name="restoredState"/> is torn or names another chain.</exception>
        public MetadataSocketMember(MetadataSocketCluster cluster, MetadataClusterOptions options, int index, ReplicaAxis axis, TcpListener listener, QuePaxaVersionedNodeState<VeritasMetadataRecord>? restoredState = null)
        {
            Axis = axis;
            Pool = options.Pool;
            Listener = listener;
            Lifetime = cluster.Lifetime.Token;

            string? storeRoot = options.StoreRoot;
            MetadataNodeStore? store = storeRoot is null
                ? null
                : new MetadataNodeStore(
                    Path.Combine(storeRoot, FormattableString.Invariant($"member-{index}")),
                    options.Pool,
                    QuePaxaMessageJson.CreateVersionedNodeStateSerializer<VeritasMetadataRecord>(WriteMetadataRecord),
                    QuePaxaMessageJson.CreateVersionedNodeStateDeserializer<VeritasMetadataRecord>(ReadMetadataRecord),
                    NoOpFlush,
                    NoOpBarrier);
            Store = store;

            //A revived member comes back through the host's own restore, which re-derives the leader, the served
            //version and the membership from the restored record and refuses a snapshot whose stored copies
            //disagree; a fresh one starts from the genesis it was deployed with.
            //The bench admits one store per member, so a revived member restores under the identity it was
            //composed with rather than under one derived at the restore.
            HostId self = new(MetadataPlaneDeployment.ReplicaIdFor(axis), Store((byte)(0xA0 + index)));
            Node = restoredState is null
                ? new QuePaxaVersionedNode<VeritasMetadataRecord>(cluster.Deployment.Genesis, self)
                : QuePaxaVersionedNode<VeritasMetadataRecord>.FromState(cluster.Deployment.Genesis, self, restoredState);
            Runner = new QuePaxaVersionedRunner<VeritasMetadataRecord>(Node);

            //A member the row did not name durable is given the documented no-op persist face rather than none at
            //all, so the durability contrast is the face and never the presence of a store.
            PersistVersionedNodeDelegate<VeritasMetadataRecord> persist = store is not null && options.IsDurable(index)
                ? store.PersistNode
                : MetadataNodeStore.NoDurability;
            RunTask = Runner.RunAsync(persist, CancellationToken.None);

            Faults = new MemberServeBinding(Runner, cluster);
            Server = new MetadataChannelServer(
                Faults.Provide,
                cluster.Codecs.DeserializeRequest,
                cluster.Codecs.SerializeReply,
                cluster.Codecs.SerializeRecord,
                cluster.Codecs.DeserializeRecord,
                options.Pool);

            Priorities = new SeededPrioritySource(options.PrioritySeed + (ulong)index);
            NetworkGovernanceDelegate? governance = options.Governance;
            Binding = options.GovernedMember == index && governance is not null
                ? MetadataPlaneTransportBinding.CreateGoverned(
                    cluster.Deployment,
                    axis,
                    Runner,
                    cluster.Resolve,
                    cluster.Codecs.SerializeRequest,
                    cluster.Codecs.DeserializeReply,
                    cluster.Codecs.SerializeRecord,
                    cluster.Codecs.DeserializeRecord,
                    options.Pool,
                    governance,
                    context: null,
                    TimeProvider.System,
                    options.GovernanceTrace)
                : MetadataPlaneTransportBinding.Create(
                    cluster.Deployment,
                    axis,
                    Runner,
                    cluster.Resolve,
                    cluster.Codecs.SerializeRequest,
                    cluster.Codecs.DeserializeReply,
                    cluster.Codecs.SerializeRecord,
                    cluster.Codecs.DeserializeRecord,
                    options.Pool);

            Plane = new VeritasMetadataPlane(
                cluster.Deployment,
                axis,
                Node,
                Runner,
                TimeSpan.Zero,
                AttemptsPerRecorder,
                MemberQueryDeadline,
                TimeProvider.System,
                Priorities.Next,
                Binding.ResolveRecorder,
                Binding.ResolveCommittedReader,
                Binding.ObserveCommittedVersionAsync,
                Binding.ObserveMemberVersionAsync,
                Binding.PublishCommittedRecordAsync);

            //The accept loop starts last, after every state it reads is set. No fellow can have dialed yet: a
            //channel dials on its first call, and the cluster performs none while it composes.
            AcceptTask = AcceptAsync();
        }

        /// <summary>This member's identity axis, which is also the consensus identity it writes under.</summary>
        public ReplicaAxis Axis { get; }

        /// <summary>This member's plane.</summary>
        public VeritasMetadataPlane Plane { get; }

        /// <summary>This member's durable home, or <see langword="null"/> when the row gave the cluster no store root.</summary>
        public MetadataNodeStore? Store { get; }

        /// <summary>The serve binding a row arms refusals on.</summary>
        public MemberServeBinding Faults { get; }

        /// <summary>How many record exchanges this member's serve binding refused.</summary>
        public int RecordFaults => Faults.RecordFaults;

        /// <summary>How many committed reads this member's serve binding refused.</summary>
        public int ReadFaults => Faults.ReadFaults;

        /// <summary>How many connections this member has accepted, which is what tells a row that one connection carried two calls.</summary>
        public int AcceptedConnections
        {
            get
            {
                lock(ServeGate)
                {
                    return Accepted.Count;
                }
            }
        }

        /// <summary>The pool this member's stream pipes rent their buffers from.</summary>
        private MemoryPool<byte> Pool { get; }

        /// <summary>The bound listener this member's fellows dial.</summary>
        private TcpListener Listener { get; }

        /// <summary>The cluster's lifetime token, which the accept and serve loops run under.</summary>
        private CancellationToken Lifetime { get; }

        /// <summary>This member's consensus host.</summary>
        private QuePaxaVersionedNode<VeritasMetadataRecord> Node { get; }

        /// <summary>The loop that owns this member's host.</summary>
        private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; }

        /// <summary>The runner's loop task.</summary>
        private Task RunTask { get; }

        /// <summary>The endpoint this member's fellows are served through.</summary>
        private MetadataChannelServer Server { get; }

        /// <summary>The transport binding this member's plane reaches its fellows through.</summary>
        private MetadataPlaneTransportBinding Binding { get; }

        /// <summary>This member's proposal-priority source.</summary>
        private SeededPrioritySource Priorities { get; }

        /// <summary>The accept loop's task.</summary>
        private Task AcceptTask { get; }

        /// <summary>The gate the accepted connections and their serve tasks are read and written under.</summary>
        private Lock ServeGate { get; } = new();

        /// <summary>The connections this member accepted, closed by its own teardown so a stopped member's serves end even while its peers still hold their ends.</summary>
        private List<TcpClient> Accepted { get; } = [];

        /// <summary>The serve tasks, one per accepted connection.</summary>
        private List<Task> Serves { get; } = [];

        /// <summary>Whether this member has already been torn down, so a row that stops one member and then disposes the cluster tears it down once.</summary>
        private bool Stopped { get; set; }

        /// <summary>
        /// Tears this member down: its plane first (which drains the obligations it queued), then its channels to
        /// its fellows, then its listener and accept loop, then the connections it accepted — which is what ends
        /// its serves while its peers still hold their ends — and its runner last.
        /// </summary>
        /// <returns>A task that completes once nothing this member started is still running.</returns>
        /// <remarks>
        /// Every stage is guarded on its own and reports nothing, because a teardown failure here would mask the
        /// row's own outcome; the bounded joins are backstops that turn a regression that would hang into a
        /// failure.
        /// </remarks>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Teardown must never raise over a row's own outcome, and every stage is guarded on its own so a stage that fails still leaves the later ones to run.")]
        public async ValueTask DisposeAsync()
        {
            if(Stopped)
            {
                return;
            }

            Stopped = true;

            try
            {
                await Plane.DisposeAsync().ConfigureAwait(false);
            }
            catch(Exception)
            {
                //A plane whose loop already ended has nothing left to drain.
            }

            try
            {
                await Binding.DisposeAsync().ConfigureAwait(false);
            }
            catch(Exception)
            {
                //A channel whose connection is already gone is torn down either way.
            }

            try
            {
                Listener.Dispose();
            }
            catch(Exception)
            {
                //A listener the runtime has already reclaimed accepts nothing further either.
            }

            try
            {
                await AcceptTask.WaitAsync(TeardownBackstop).ConfigureAwait(false);
            }
            catch(Exception)
            {
                //The accept loop ends on the listener's disposal; the bound covers a loop that did not.
            }

            List<TcpClient> accepted;
            List<Task> serves;
            lock(ServeGate)
            {
                accepted = [.. Accepted];
                serves = [.. Serves];
            }

            foreach(TcpClient connection in accepted)
            {
                try
                {
                    connection.Dispose();
                }
                catch(Exception)
                {
                    //A socket already gone needs no closing.
                }
            }

            try
            {
                await Task.WhenAll(serves).WaitAsync(TeardownBackstop).ConfigureAwait(false);
            }
            catch(Exception)
            {
                //A serve that faulted as its connection closed is the state the closing asked for.
            }

            Runner.Complete();
            try
            {
                await RunTask.WaitAsync(TeardownBackstop).ConfigureAwait(false);
            }
            catch(Exception)
            {
                //The loop ends when its queue drains; the bound covers a loop that did not.
            }
        }

        /// <summary>Accepts connections until the listener is torn down, serving each on its own task.</summary>
        /// <returns>A task that completes when this member accepts nothing further.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The accept loop ends on whatever a torn-down listener reports — a disposed socket, a cancelled wait, or the platform's own refusal — and all of them are the same normal end of accepting rather than conditions a row should be told apart.")]
        private async Task AcceptAsync()
        {
            while(!Lifetime.IsCancellationRequested)
            {
                TcpClient accepted;
                try
                {
                    accepted = await Listener.AcceptTcpClientAsync(Lifetime).ConfigureAwait(false);
                }
                catch(Exception)
                {
                    //The listener is gone, which is the normal end of accepting.
                    break;
                }

                //The connection is tracked before its serve starts, so a teardown that races the accept still
                //finds it to close.
                lock(ServeGate)
                {
                    Accepted.Add(accepted);
                }

                Task serve = ServeConnectionAsync(accepted);
                lock(ServeGate)
                {
                    Serves.Add(serve);
                }
            }
        }

        /// <summary>Serves one accepted connection until the peer ends it, isolating whatever that connection met.</summary>
        /// <param name="accepted">The accepted connection; this serve closes it.</param>
        /// <returns>A task that completes when the connection's calls end.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One connection's fault is isolated here exactly as an accept loop isolates it in a deployment: a peer that tore its end down, a stream that could not be framed, and a cancelled serve are all this connection's own end and never the member's.")]
        private async Task ServeConnectionAsync(TcpClient accepted)
        {
            try
            {
                NetworkStream stream = accepted.GetStream();
                PipeReader requests = PipeReader.Create(stream, new StreamPipeReaderOptions(Pool, leaveOpen: true));
                PipeWriter responses = PipeWriter.Create(stream, new StreamPipeWriterOptions(Pool, leaveOpen: true));

                await Server.ServeAsync(requests, responses, Lifetime).ConfigureAwait(false);
            }
            catch(Exception)
            {
                //A serve that ended on a fault is this connection's own end; the member keeps serving the rest.
            }
            finally
            {
                accepted.Dispose();
            }
        }
    }

    /// <summary>
    /// A cluster of metadata-plane members over loopback TCP: every listener is bound to port zero and its port
    /// read back, every accept loop is running before any member composes a channel, and every channel dials only
    /// when a row first drives an obligation. The routes are the cluster's own, so a row cuts and heals a member
    /// at the one seam a deployment's routing lives at.
    /// </summary>
    private sealed class MetadataSocketCluster: IAsyncDisposable
    {
        /// <summary>Creates a cluster over listeners the factory has already bound.</summary>
        /// <param name="deployment">The chain's genesis.</param>
        /// <param name="options">The cluster's shape.</param>
        /// <param name="listeners">The bound listeners, one per member, in founder order.</param>
        /// <param name="routes">The routes reaching each member, in founder order.</param>
        private MetadataSocketCluster(MetadataPlaneDeployment deployment, MetadataClusterOptions options, TcpListener[] listeners, MemberRoute[] routes)
        {
            Deployment = deployment;
            Options = options;
            Listeners = listeners;
            Routes = routes;
            Codecs = CreateWireCodecs();
        }

        /// <summary>The chain every member of this cluster runs on.</summary>
        public MetadataPlaneDeployment Deployment { get; }

        /// <summary>The wire codecs every member's channel and every probe of this cluster is composed from.</summary>
        public MetadataWireCodecs Codecs { get; }

        /// <summary>The members, in founder order.</summary>
        public List<MetadataSocketMember> Members => Composed;

        /// <summary>The token every accept loop and every serve of this cluster runs under.</summary>
        public CancellationTokenSource Lifetime { get; } = new();

        /// <summary>
        /// The gate every SERVED record exchange passes through, or <see langword="null"/> while no row has armed
        /// one, which is the state every other row runs in.
        /// </summary>
        /// <remarks>
        /// It is written by the row's own thread before the writers are started and read afterwards by the serve
        /// loops, and a reference field is written and read whole, so no serve observes a half-built gate.
        /// </remarks>
        public WriterRendezvous<ReplicaId>? Writers { get; private set; }

        /// <summary>The cluster's shape.</summary>
        private MetadataClusterOptions Options { get; }

        /// <summary>The bound listeners, in founder order.</summary>
        private TcpListener[] Listeners { get; }

        /// <summary>The routes reaching each member, in founder order.</summary>
        private MemberRoute[] Routes { get; }

        /// <summary>The composed members, in founder order.</summary>
        private List<MetadataSocketMember> Composed { get; } = new(MemberCount);

        /// <summary>Whether this cluster has already been torn down.</summary>
        private bool Stopped { get; set; }

        /// <summary>
        /// Stands up a whole cluster: every listener is bound and its ephemeral port read back FIRST, so every
        /// member's route is placeable before any member composes its channels, and only then is each member
        /// built with its accept loop running.
        /// </summary>
        /// <param name="options">The cluster's shape.</param>
        /// <returns>The running cluster.</returns>
        /// <remarks>
        /// Nothing here waits on the network — binding a listener and reading its port are synchronous — so the
        /// one await is the teardown of a cluster that failed while composing, which has no owner to await it
        /// later.
        /// </remarks>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The listeners are handed to the cluster, which disposes them through its members' teardown, and a failure before that cluster exists disposes every listener this factory started.")]
        public static async Task<MetadataSocketCluster> StartAsync(MetadataClusterOptions options)
        {
            ReplicaAxis[] axes = new ReplicaAxis[MemberCount];
            TcpListener[] listeners = new TcpListener[MemberCount];
            MemberRoute[] routes = new MemberRoute[MemberCount];
            int bound = 0;
            try
            {
                for(int index = 0; index < MemberCount; index++)
                {
                    axes[index] = Axis((byte)(0xA0 + index));

                    //Port zero binds an ephemeral port the platform chooses and the listener reports back, so two
                    //rows running side by side never contend for one number.
                    TcpListener listener = new(IPAddress.Loopback, 0);
                    listeners[index] = listener;
                    bound = index + 1;
                    listener.Start();
                    routes[index] = new MemberRoute(((IPEndPoint)listener.LocalEndpoint).Port, options.Pool);
                }
            }
            catch(Exception)
            {
                for(int index = 0; index < bound; index++)
                {
                    listeners[index].Dispose();
                }

                throw;
            }

            MetadataFounder[] founders = new MetadataFounder[MemberCount];
            for(int index = 0; index < founders.Length; index++)
            {
                founders[index] = Founder((byte)(0xA0 + index));
            }

            MetadataSocketCluster cluster = new(MetadataPlaneDeployment.Create([.. founders]), options, listeners, routes);
            try
            {
                cluster.Compose(axes);
            }
            catch(Exception)
            {
                await cluster.DisposeAsync().ConfigureAwait(false);

                throw;
            }

            return cluster;
        }

        /// <summary>Answers which connection seam reaches <paramref name="member"/> — a <see cref="ResolvePeerMetadataConnectionDelegate"/>.</summary>
        /// <param name="member">The member to reach.</param>
        /// <returns>That member's connection seam.</returns>
        /// <exception cref="InvalidOperationException">No member of this cluster carries that axis, which is how the resolver reports one it cannot place.</exception>
        public OpenPeerMetadataConnectionDelegate Resolve(ReplicaAxis member)
        {
            for(int index = 0; index < Routes.Length; index++)
            {
                if(Deployment.Founders[index].Axis.Equals(member))
                {
                    return Routes[index].OpenAsync;
                }
            }

            throw new InvalidOperationException("No member of this cluster carries the identity axis the register asked to reach.");
        }

        /// <summary>The connection seam reaching one member by position, for a row that dials a member directly.</summary>
        /// <param name="index">The member's position in the founder order.</param>
        /// <returns>That member's connection seam.</returns>
        public OpenPeerMetadataConnectionDelegate RouteTo(int index)
        {
            return Routes[index].OpenAsync;
        }

        /// <summary>
        /// Points one member's route at ANOTHER member's listener — the hand-wired endpoint map whose two
        /// routes land on one host — and back at its own when <paramref name="member"/> equals
        /// <paramref name="target"/>. Connections already open keep the host they dialed; a row that needs
        /// them re-dialed cuts and heals the route.
        /// </summary>
        /// <param name="member">The member whose route is repointed.</param>
        /// <param name="target">The member whose listener the route lands on.</param>
        public void MisrouteTo(int member, int target)
        {
            Routes[member].Rebind(((IPEndPoint)Listeners[target].LocalEndpoint).Port);
        }

        /// <summary>
        /// Arms the gate every served record exchange stops at until <paramref name="writers"/> DISTINCT
        /// proposers have reached one, so a row's writers are in flight together rather than in whatever order
        /// the operating system happened to run them.
        /// </summary>
        /// <param name="writers">How many distinct proposers must arrive; at least two and at most the membership.</param>
        /// <returns>The armed gate, which reports afterwards whether that many actually arrived.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the count is below two or above the membership.</exception>
        /// <remarks>
        /// <para>
        /// A quorum here is two of three and a writer's own leg never leaves its process, so a writer whose
        /// remote exchanges are held cannot decide: every writer therefore reaches the gate, and none of them
        /// gets past it until all of them have.
        /// </para>
        /// <para>
        /// THE IDENTITY THE GATE HOLDS BY IS THE PROPOSAL'S OWNER, which for a first-round proposal is the
        /// sending replica's own lane. A proposal carried forward from another proposer keeps that proposer's
        /// identity, and that is harmless here: carrying one forward requires having been answered, which a
        /// held writer has not been, so every arrival while this gate is closed is a proposer arriving under
        /// its own name.
        /// </para>
        /// </remarks>
        public WriterRendezvous<ReplicaId> ArmWriterRendezvous(int writers)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(writers, 2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(writers, MemberCount);

            WriterRendezvous<ReplicaId> armed = new(writers);
            Writers = armed;

            return armed;
        }

        /// <summary>Cuts every route to one member, so nothing reaches it until it is healed.</summary>
        /// <param name="index">The member's position in the founder order.</param>
        public void Cut(int index)
        {
            Routes[index].Cut();
        }

        /// <summary>Restores every route to one member.</summary>
        /// <param name="index">The member's position in the founder order.</param>
        public void Heal(int index)
        {
            Routes[index].Heal();
        }

        /// <summary>Stops ONE member while the rest of the cluster keeps running — this battery's crash.</summary>
        /// <param name="index">The member's position in the founder order.</param>
        /// <returns>A task that completes once nothing that member started is still running.</returns>
        public ValueTask StopMemberAsync(int index)
        {
            return Composed[index].DisposeAsync();
        }

        /// <summary>
        /// Stops ONE member and brings it back on a fresh listener from whatever its store held — this battery's
        /// restart, which is a crash followed by a rejoin rather than a snapshot inspected offline.
        /// </summary>
        /// <param name="index">The member's position in the founder order.</param>
        /// <param name="cancellationToken">The row's token, which the load runs under.</param>
        /// <returns>The revived member, now serving its fellows again at the port the route was moved to.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the member has no store, so there is nothing for it to come back from.</exception>
        /// <remarks>
        /// The listener is a NEW one bound at port zero, because the platform does not hand a restarting process
        /// its old port back, and the route is moved to it: a deployment tells its locator where a member went,
        /// and nothing else about the cluster changes. The member's fellows keep their channels, so the first
        /// call each of them makes over a connection the crash tore is answered as a fault and the one after it
        /// dials the member's new home, which is the rejoin as an operator would meet it.
        /// </remarks>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The listener is handed to the revived member, which disposes it through its own teardown, and the cluster disposes every listener it holds afterwards; a failure between binding and handing over leaves the listener in the cluster's own array, which its teardown closes.")]
        public async Task<MetadataSocketMember> RestartMemberAsync(int index, CancellationToken cancellationToken)
        {
            MetadataSocketMember stopped = Composed[index];
            MetadataNodeStore store = stopped.Store
                ?? throw new InvalidOperationException("A member with no store has nothing to come back from, so a row that restarts one gives the cluster a store root and names the member durable.");

            await stopped.DisposeAsync().ConfigureAwait(false);

            QuePaxaVersionedNodeState<VeritasMetadataRecord>? restored = await store.TryLoadAsync(cancellationToken).ConfigureAwait(false);

            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            Listeners[index] = listener;
            Routes[index].Rebind(((IPEndPoint)listener.LocalEndpoint).Port);

            MetadataSocketMember revived = new(this, Options, index, stopped.Axis, listener, restored);
            Composed[index] = revived;

            return revived;
        }

        /// <summary>Tears every member down and ends the lifetime the accept and serve loops ran under.</summary>
        /// <returns>A task that completes once nothing this cluster started is still running.</returns>
        /// <remarks>
        /// The listeners are closed again after the members, because a cluster that failed while composing has
        /// listeners no member ever took over, and closing one a member already closed is a no-op.
        /// </remarks>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Teardown must never raise over a row's own outcome: a listener a member already closed, or one the runtime has reclaimed, is exactly the state this stage asks for.")]
        public async ValueTask DisposeAsync()
        {
            if(Stopped)
            {
                return;
            }

            Stopped = true;

            foreach(MetadataSocketMember member in Composed)
            {
                await member.DisposeAsync().ConfigureAwait(false);
            }

            foreach(TcpListener listener in Listeners)
            {
                try
                {
                    listener.Dispose();
                }
                catch(Exception)
                {
                    //A listener already closed accepts nothing further either.
                }
            }

            await Lifetime.CancelAsync().ConfigureAwait(false);
            Lifetime.Dispose();
        }

        /// <summary>Composes every member over the bound listeners, in founder order.</summary>
        /// <param name="axes">The members' identity axes, in founder order.</param>
        private void Compose(ReplicaAxis[] axes)
        {
            for(int index = 0; index < MemberCount; index++)
            {
                Composed.Add(new MetadataSocketMember(this, Options, index, axes[index], Listeners[index]));
            }
        }
    }
}
