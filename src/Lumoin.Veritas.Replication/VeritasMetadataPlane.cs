using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The deployment's consensus-backed metadata plane: one QuePaxa versioned register over one
/// <see cref="VeritasMetadataRecord"/> chain, behind a single-consumer write queue that serializes every
/// obligation this replica initiates. Replica identity claims, the lineage baseline, the coordination policy and
/// the coordinator lease become facts a quorum decided rather than facts each host declared for itself.
/// </summary>
/// <remarks>
/// <para>
/// THE WRITE QUEUE. The register is single-flight per instance and REFUSES a concurrent second write, so the
/// plane serializes its own write initiation: every obligation enqueues a work item and awaits its own
/// completion, and one loop is the sole consumer that ever touches the register. Two different obligations racing
/// on one plane — a claim and an election — therefore never meet that refusal. The queue buys a second property
/// the write discipline depends on: the register's committed belief, its active membership and its next version
/// move on the loop and nowhere else, so a rule computing a record may read the version that record will be
/// written at.
/// </para>
/// <para>
/// EVERY OBLIGATION WRITES, THE IDEMPOTENT ONES INCLUDED. A rule that changes nothing still proposes the record
/// it read, and the write spends a version. That version is what the plane buys with it: an answer decided by a
/// quorum rather than an answer this replica happened to hold. An already-claimed axis reported from a stale
/// local belief would refuse an open that is legitimately this host's, and a baseline reported already-recorded
/// from a record another replica has since replaced would let two lineages agree to disagree. The only obligation
/// that answers without writing is a membership change on a chain that has decided nothing, which reports
/// <see cref="MembershipChangeOutcome.RequiresBootstrap"/> rather than leaking the register's own refusal to
/// reconfigure a chain it has no value to carry forward from.
/// </para>
/// <para>
/// THE OUTCOME MAPPING. A committed write answers with the decision its rule took while computing the record
/// that was decided. A write refused for membership answers with the ladder's own
/// <c>OutsideConfiguration</c> — a settled fact about this replica, not an unlucky round. Everything else, a
/// write superseded past its budget as much as one that reached no decision, answers <c>Undecided</c>: QuePaxa's
/// definite ignorance, which is not evidence the intent was refused.
/// </para>
/// <para>
/// THE PLANE IS NEVER A LIVENESS DEPENDENCY of the dotted data lane. <c>Undecided</c> is what an unreachable
/// plane produces, and the engine seams that consult the plane proceed on it; only a definite adverse answer
/// refuses an open, because that is correctness rather than liveness. The consensus procedure itself is
/// timeout-free and the plane embeds no health check: the one bound it carries is
/// <see cref="MemberQueryDeadline"/>, which the two READS spend per member so that one silent host costs one
/// member's entry rather than the answer, and which points fail-safe — a member that answers nothing is
/// unreachable, and unreachable subtracts from a gate's quorum instead of clearing it.
/// </para>
/// <para>
/// THE HOST BESIDE IT. The plane writes through its own register and serves peers through the host's runner, and
/// the two meet in one place: before every obligation the register adopts whatever record the host has learned,
/// so a plane built over a restored host — or one whose host was handed a disseminated record — starts from the
/// version it is already at instead of losing a round discovering it. An inbound push lands through
/// <see cref="ApplyDisseminatedRecordAsync"/> as a DURABLE learn, because a control-plane fact a crash could lose
/// is one a peer may already have built the next version on.
/// </para>
/// <para>
/// Every rule is an explicit binding frame holding its operands in properties, so no computation captures an
/// enclosing scope, and each frame records the decision it took for the outcome mapping to read afterwards. The
/// frames are one-shot: one obligation builds one frame, whose recorded decision belongs to the last record it
/// computed, which is the record a committed write decided.
/// </para>
/// </remarks>
public sealed class VeritasMetadataPlane: IAsyncDisposable
{
    /// <summary>
    /// Initializes a plane for one replica of <paramref name="deployment"/> over a host the caller has already
    /// started, wiring the register with every optional delegate the deployment's metadata channel backs.
    /// </summary>
    /// <param name="deployment">The chain's genesis: the founders, the minted chain identity, and the identity mapping between a replica axis and a consensus identity.</param>
    /// <param name="self">This replica's identity axis, which is also its consensus identity on the chain.</param>
    /// <param name="node">The recorder host this replica serves peers from. The plane reads its committed record and never touches it otherwise; the runner owns it.</param>
    /// <param name="runner">The loop that owns <paramref name="node"/>, already started by the host. Inbound pushes are learned through it.</param>
    /// <param name="hedgingBaseDelay">The hedging delay increment per position in the membership order. Zero activates every replica at once.</param>
    /// <param name="attemptsPerRecorder">How many times one protocol step may send to one recorder before abandoning it for that step. At least one.</param>
    /// <param name="memberQueryDeadline">How long ONE member's catch-up query or readiness probe may take before that member is given up on. Positive, or <see cref="Timeout.InfiniteTimeSpan"/> to wait on every member however long it takes.</param>
    /// <param name="timeProvider">The clock the hedging delay runs against and trace events are timestamped from.</param>
    /// <param name="drawPriority">The phase-zero priority source. Production draws cryptographically; a test seeds it deterministically.</param>
    /// <param name="resolveRecorder">Answers which transport reaches one member, called per member per attempt.</param>
    /// <param name="resolveCommittedRecordReader">Answers which committed-read query reaches one member, which is what <see cref="ReadRecordAsync"/> catches up through.</param>
    /// <param name="observeCommittedVersion">Reports the highest committed version known, so a delayed writer stands down instead of running a closed instance.</param>
    /// <param name="observeMemberVersion">Reports one named member's version, which is what <see cref="ReadReadinessAsync"/> is built from.</param>
    /// <param name="publishCommittedRecord">Carries a decided record to the audience the register computes, which is what makes the next version servable.</param>
    /// <param name="trace">The diagnostics sink each completed obligation's verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id emitted events carry.</param>
    /// <exception cref="ArgumentNullException">Thrown if any reference argument is <see langword="null"/>. Every delegate is REQUIRED here even though the register treats four of them as optional: a plane that cannot read a committed record, observe a version, report readiness or disseminate a decision is a plane whose obligations are undecidable for reasons the deployment could have wired away.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="self"/> carries no well-formed identity axis, and if <paramref name="node"/> runs as a member other than the one <paramref name="self"/> names.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptsPerRecorder"/> is less than one, if <paramref name="hedgingBaseDelay"/> is negative or large enough that the last position's delay would not fit in a <see cref="TimeSpan"/>, and if <paramref name="memberQueryDeadline"/> is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    public VeritasMetadataPlane(
        MetadataPlaneDeployment deployment,
        ReplicaAxis self,
        QuePaxaVersionedNode<VeritasMetadataRecord> node,
        QuePaxaVersionedRunner<VeritasMetadataRecord> runner,
        TimeSpan hedgingBaseDelay,
        int attemptsPerRecorder,
        TimeSpan memberQueryDeadline,
        TimeProvider timeProvider,
        ProposalPrioritySourceDelegate drawPriority,
        ResolveRecorderEndpointDelegate<VeritasMetadataRecord> resolveRecorder,
        ResolveCommittedRecordReaderDelegate<VeritasMetadataRecord> resolveCommittedRecordReader,
        ObserveCommittedVersionDelegate observeCommittedVersion,
        ObserveMemberVersionDelegate observeMemberVersion,
        PublishCommittedRecordDelegate<VeritasMetadataRecord> publishCommittedRecord,
        TraceHandler<MetadataPlaneTraceEvent>? trace = null,
        Guid correlationId = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(drawPriority);
        ArgumentNullException.ThrowIfNull(resolveRecorder);
        ArgumentNullException.ThrowIfNull(resolveCommittedRecordReader);
        ArgumentNullException.ThrowIfNull(observeCommittedVersion);
        ArgumentNullException.ThrowIfNull(observeMemberVersion);
        ArgumentNullException.ThrowIfNull(publishCommittedRecord);

        Deployment = deployment;
        SelfAxis = ValidateAxis(self, nameof(self));

        //The plane coordinates as the member its axis names, and its readiness reads probe the local host
        //through a seam that answers under the host's own identity; a host running as another member would have
        //every such read refused as answered by the wrong host, permanently. That is a composition fault and it
        //raises here, where the composition was written down.
        if(!MetadataPlaneDeployment.ReplicaIdFor(SelfAxis).Equals(node.Self.Replica))
        {
            throw new ArgumentException("A metadata plane coordinates as the member its identity axis names, and the consensus host it was handed runs as another member; every readiness read of this replica would be refused as answered by the wrong host.", nameof(node));
        }

        //The deadline the register spends per member is the deployment's patience policy, so a value that names
        //no patience at all — zero, or a negative span that is not the infinite one — is a composition fault and
        //raises here rather than on the first read that would have carried it.
        if(memberQueryDeadline <= TimeSpan.Zero && memberQueryDeadline != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(memberQueryDeadline), memberQueryDeadline, "A metadata plane gives one member a positive span to answer a catch-up query or a readiness probe in, or the infinite span to wait on it however long it takes.");
        }

        Node = node;
        Runner = runner;
        MemberQueryDeadline = memberQueryDeadline;
        TimeProvider = timeProvider;
        Trace = trace;
        CorrelationId = correlationId;

        Register = new QuePaxaVersionedRegister<VeritasMetadataRecord>(
            deployment.Genesis,
            MetadataPlaneDeployment.ReplicaIdFor(SelfAxis),
            hedgingBaseDelay,
            resolveRecorder,
            drawPriority,
            attemptsPerRecorder,
            timeProvider,
            observeCommittedVersion,
            resolveCommittedRecordReader,
            publishCommittedRecord,
            observeMemberVersion);

        //The loop is started last, after every state it reads is set, and it is the sole consumer of the queue
        //for the plane's whole life.
        Loop = DrainAsync();
    }

    /// <summary>The chain's genesis: the founders in genesis order, the minted chain identity, and the identity mapping.</summary>
    public MetadataPlaneDeployment Deployment { get; }

    /// <summary>This replica's identity axis, which is also the consensus identity it writes under.</summary>
    public ReplicaAxis SelfAxis { get; }

    /// <summary>The chain identity minted from the founder list — the deployment's chain name, surfaced so an operator can tell two chains apart by value.</summary>
    public ClusterId Cluster => Deployment.Cluster;

    /// <summary>
    /// How long ONE member has to answer before this plane gives up on it, spent per member on the catch-up read
    /// <see cref="ReadRecordAsync"/> runs and on the readiness probe <see cref="ReadReadinessAsync"/> is built
    /// from, or <see cref="Timeout.InfiniteTimeSpan"/> when the plane waits on every member however long it takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is local patience policy and not a protocol parameter, wired at composition beside the hedging delay:
    /// two replicas of one deployment may hold different values without disagreeing about anything the chain
    /// decided. A member that answers nothing past it is the SAME entry a member whose query faults produces —
    /// unreachable, carrying no version — because a readiness report exists to say whether a named replica has
    /// learned a version, and silence and a fault answer that identically. There is no third state a gate could
    /// act differently on.
    /// </para>
    /// <para>
    /// THE DIRECTION IS FAIL-SAFE. Interference that delays, denies or drops a member's answer makes that member
    /// report unreachable, and an unreachable member subtracts from the quorum a decommission gate needs, so
    /// interference holds such a gate CLOSED and never opens one. What it costs is availability — a gate that
    /// will not clear — which is the side the plane's iron constraint on liveness puts the residue on.
    /// </para>
    /// </remarks>
    public TimeSpan MemberQueryDeadline { get; }

    /// <summary>The record the local host has learned, or <see langword="null"/> when it has learned none. One reference read, so it answers stale but never torn while the host serves.</summary>
    public VersionedValue<VeritasMetadataRecord>? HostCommitted => Node.Committed;

    /// <summary>The recorder host this replica serves peers from. The runner owns it; the plane only reads the record it has learned.</summary>
    private QuePaxaVersionedNode<VeritasMetadataRecord> Node { get; }

    /// <summary>The loop that owns the host, through which an inbound push is learned.</summary>
    private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; }

    /// <summary>The register every obligation writes through. It is touched on the queue's loop and nowhere else.</summary>
    private QuePaxaVersionedRegister<VeritasMetadataRecord> Register { get; }

    /// <summary>The clock trace events are timestamped from.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The diagnostics sink each completed obligation's verdict is emitted to, or <see langword="null"/> when nothing is emitted.</summary>
    private TraceHandler<MetadataPlaneTraceEvent>? Trace { get; }

    /// <summary>The correlation id emitted events carry.</summary>
    private Guid CorrelationId { get; }

    /// <summary>The monotonic sequence number the next emitted event carries. It is advanced on the loop, which is the plane's only emitter.</summary>
    private long Sequence { get; set; }

    /// <summary>The obligation queue: unbounded, single-reader, and the plane's one path to the register.</summary>
    private Channel<PlaneWorkItem> Work { get; } = Channel.CreateUnbounded<PlaneWorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    /// <summary>The loop that serves the queue, which ends when the queue is completed.</summary>
    private Task Loop { get; }

    /// <summary>
    /// Bootstraps the chain by committing the deterministic initial record under the genesis membership. Every
    /// founder may call it: the proposals are identical values, so the race resolves without anyone's state being
    /// lost, and a founder that observes a record already committed reports
    /// <see cref="PlaneBootstrapOutcome.AlreadyBootstrapped"/>.
    /// </summary>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    public Task<MetadataPlaneResult<PlaneBootstrapOutcome>> BootstrapAsync(int attemptBudget, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<PlaneBootstrapOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new BootstrapItem(attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Claims a replica identity axis on the record, which is what makes axis distinctness proactive: the claim
    /// is taken before its owner mints a dot under the axis, so a second minter is refused at claim time rather
    /// than detected once colliding dots have crossed the wire.
    /// </summary>
    /// <param name="axis">The axis to claim, which is the axis the caller will mint under.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="axis"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <remarks>
    /// A claim already on the record is <see cref="IdentityClaimOutcome.AlreadyClaimedBySelf"/> when the claimed
    /// axis IS this plane's own identity and <see cref="IdentityClaimOutcome.RefusedHeldByOther"/> otherwise. The
    /// axis is the identity, so a claim carries no separate claimant field and needs none: the only claim a
    /// replica can prove is its own is the one naming the axis it is. A caller that claims an axis it does not
    /// mint under therefore lands the claim once and is refused afterwards, which is the reading the obligation
    /// exists for — the second call is a second minter under an axis this host does not own.
    /// </remarks>
    public Task<MetadataPlaneResult<IdentityClaimOutcome>> ClaimIdentityAsync(ReplicaAxis axis, int attemptBudget, CancellationToken cancellationToken)
    {
        _ = ValidateAxis(axis, nameof(axis));
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<IdentityClaimOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ClaimIdentityItem(axis, attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Records the lineage baseline's INTENT: the claimant axis and the causality digest, written BEFORE the
    /// minting replica's local durable commit, because the digest over the minted commit causality is the only
    /// lineage identity that exists at that point. A second independent intent for the lineage is refused here,
    /// which is where the independent-baseline storm closes.
    /// </summary>
    /// <param name="claimantAxis">The replica identity axis the baseline dots are minted on.</param>
    /// <param name="causalityDigest">The content fingerprint of the minted baseline causality.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="claimantAxis"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <remarks>
    /// Minting a baseline is deterministic given the identity and the present triples, so a replica that crashed
    /// between its intent and its own commit reproduces the digest on the next open and its retry lands
    /// <see cref="BaselineRecordOutcome.AlreadyRecorded"/>. Only a genuinely different claimant or digest is a
    /// <see cref="BaselineRecordOutcome.ConflictingLineage"/>, and that refusal is loud because the alternative is
    /// two lineages silently agreeing to disagree.
    /// </remarks>
    public Task<MetadataPlaneResult<BaselineRecordOutcome>> RecordBaselineIntentAsync(ReplicaAxis claimantAxis, NodeIdentifier causalityDigest, int attemptBudget, CancellationToken cancellationToken)
    {
        _ = ValidateAxis(claimantAxis, nameof(claimantAxis));
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<BaselineRecordOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new BaselineIntentItem(claimantAxis, causalityDigest, attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Confirms the lineage baseline AFTER the minting replica's local durable commit: the dataset StateId and the
    /// dictionary epoch are filled together on the baseline the digest matches, which is what a clone gates on.
    /// </summary>
    /// <param name="causalityDigest">The content fingerprint of the minted baseline causality, which is what matches this write to its intent.</param>
    /// <param name="stateId">The dataset StateId the committed baseline produced.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <remarks>
    /// A confirm against a record carrying NO baseline records the confirmed baseline whole, naming this replica
    /// as the claimant. That arm is what closes the fail-open path: an intent that answered
    /// <see cref="BaselineRecordOutcome.Undecided"/> lets the open proceed, and the commit that follows must be
    /// recordable afterwards or the lineage would stay unagreed for a plane outage. The storm is then caught one
    /// phase later — a second replica confirming against a baseline already carrying a different digest is a
    /// <see cref="BaselineRecordOutcome.ConflictingLineage"/> — which is the price the iron constraint on
    /// liveness names.
    /// </remarks>
    public Task<MetadataPlaneResult<BaselineRecordOutcome>> ConfirmBaselineAsync(NodeIdentifier causalityDigest, NodeIdentifier stateId, long dictionaryEpoch, int attemptBudget, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<BaselineRecordOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new BaselineConfirmItem(causalityDigest, stateId, dictionaryEpoch, attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Amends the coordination policy — the facts every host of the deployment must read identically — to
    /// <paramref name="policy"/>.
    /// </summary>
    /// <param name="policy">The policy to install.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <remarks>
    /// An amendment composes from the initial record — a chain that has decided nothing amends the default
    /// policy — so unlike a membership change it needs no bootstrap pre-check and its ladder carries no
    /// bootstrap value.
    /// </remarks>
    public Task<MetadataPlaneResult<PolicyAmendmentOutcome>> AmendPolicyAsync(CoordinationPolicy policy, int attemptBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<PolicyAmendmentOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new AmendPolicyItem(policy, attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Takes or refreshes the coordinator lease under <paramref name="self"/>. Succession is settled by the write
    /// discipline: a vacant lease is taken, a lease already held under this axis is refreshed at a new term, a
    /// lease held by another CURRENT member is not usurped, and a lease held by a replica the membership no longer
    /// lists is taken over.
    /// </summary>
    /// <param name="self">The identity axis the lease is taken under, which is the calling replica's own.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="self"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <remarks>
    /// Usurpation is tied to the membership obligation this plane already coordinates: retiring a dead holder
    /// through <see cref="RetireMemberAsync"/> is what unlocks its lease. Deciding that a holder IS dead is an
    /// application-level health signal outside this plane, and the plane embeds none — a lease term is a register
    /// version and never a clock reading.
    /// </remarks>
    public Task<MetadataPlaneResult<CoordinatorElectionOutcome>> ElectCoordinatorAsync(ReplicaAxis self, int attemptBudget, CancellationToken cancellationToken)
    {
        _ = ValidateAxis(self, nameof(self));
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<CoordinatorElectionOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ElectCoordinatorItem(self, attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Vacates the coordinator lease held under <paramref name="self"/>, so any member may elect next. A lease
    /// already vacant reports <see cref="CoordinatorElectionOutcome.Released"/>, and a lease held under another
    /// axis reports <see cref="CoordinatorElectionOutcome.HeldByOther"/>: only a holder releases its own lease.
    /// </summary>
    /// <param name="self">The identity axis the lease is held under, which is the calling replica's own.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="self"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    public Task<MetadataPlaneResult<CoordinatorElectionOutcome>> ReleaseCoordinatorAsync(ReplicaAxis self, int attemptBudget, CancellationToken cancellationToken)
    {
        _ = ValidateAxis(self, nameof(self));
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<CoordinatorElectionOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ReleaseCoordinatorItem(self, attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Admits <paramref name="member"/> to the chain's membership. The change is a DELTA — add this replica — so a
    /// change re-applied against a record that won composes with a concurrent operator's change instead of
    /// silently undoing it.
    /// </summary>
    /// <param name="member">The joiner: its identity axis beside the incarnation its store minted, which the operator reads out of that store before the change is asked for.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="member"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <remarks>
    /// A joiner is admitted, disseminated to, and only then written through:
    /// <see cref="ReadReadinessAsync"/> reports whether a quorum has learned the record that installed it, which
    /// is the gate an operator clears before acting on the new membership.
    /// </remarks>
    public Task<MetadataPlaneResult<MembershipChangeOutcome>> AdmitMemberAsync(MetadataFounder member, int attemptBudget, CancellationToken cancellationToken)
    {
        _ = ValidateAxis(member.Axis, nameof(member));
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<MembershipChangeOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new AdmitMemberItem(member.ToHostId(), attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Retires <paramref name="member"/> from the chain's membership, which is also what unlocks a coordinator
    /// lease its holder can no longer release. The change is a DELTA — remove this replica — for the same reason
    /// an admission is.
    /// </summary>
    /// <param name="member">The identity axis of the replica to retire.</param>
    /// <param name="attemptBudget">How many consensus attempts the obligation may spend. At least one.</param>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued obligation still runs.</param>
    /// <returns>The outcome, with the decided record and its version when this replica's own write committed.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="member"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="attemptBudget"/> is less than one.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further obligations.</exception>
    /// <exception cref="InvalidOperationException">Thrown through the returned task if <paramref name="member"/> is the membership's last member: a chain with no members can neither decide nor be reconfigured back into existence, so emptying it is a deployment fault rather than an outcome.</exception>
    /// <remarks>
    /// A replica is decommissioned only once a quorum that EXCLUDES it has learned the record that removed it,
    /// which <see cref="ReadReadinessAsync"/> is what reports.
    /// </remarks>
    public Task<MetadataPlaneResult<MembershipChangeOutcome>> RetireMemberAsync(ReplicaAxis member, int attemptBudget, CancellationToken cancellationToken)
    {
        _ = ValidateAxis(member, nameof(member));
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptBudget, 1);

        TaskCompletionSource<MetadataPlaneResult<MembershipChangeOutcome>> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new RetireMemberItem(MetadataPlaneDeployment.ReplicaIdFor(member), attemptBudget, source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Applies a record disseminated by the replica that decided it, learning it DURABLY on the local host. This
    /// is the inbound half of the metadata channel's record push.
    /// </summary>
    /// <param name="record">The decided record a peer pushed.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns><see langword="true"/> when the record advanced the host.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="record"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Control-plane facts are always learned durably: a host that adopted a record in memory and crashed would
    /// come back serving an instance a peer has already built the next version on, and a record that installs a
    /// membership may be the only copy of it inside the membership it installs.
    /// </para>
    /// <para>
    /// It goes to the HOST's queue rather than this plane's, because the two serialize different things — the
    /// runner serializes the node, this plane serializes the register — so a push is served while an obligation is
    /// in flight. What the register makes of the push arrives on the next obligation, which adopts the host's
    /// record before it writes. Readiness reported through <see cref="ReadReadinessAsync"/> is an operational gate
    /// whose confirmation is the next consensus write itself; no irreversible act rides readiness alone.
    /// </para>
    /// </remarks>
    public ValueTask<bool> ApplyDisseminatedRecordAsync(VersionedValue<VeritasMetadataRecord> record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Runner.LearnAsync(record, LearnDurability.Durable, cancellationToken);
    }

    /// <summary>
    /// Catches up on versions this replica missed by asking the members of the active membership what they have
    /// learned, and reports the highest committed record known afterwards.
    /// </summary>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued read still runs.</param>
    /// <returns>The highest committed record known after the round, or <see langword="null"/> when none is known.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further work.</exception>
    /// <remarks>
    /// It takes no quorum and no consensus step: a committed record is a decided fact, so one honest host settles
    /// it. Learning nothing new does not prove currency — a writer that committed and stopped before telling
    /// anyone leaves the same signature — and what resolves that is writing, which is why every obligation here
    /// writes rather than reads. The read is queued like an obligation, because the register it advances is the
    /// same one every obligation writes through. A member that answers nothing within
    /// <see cref="MemberQueryDeadline"/> is skipped exactly as a failing one is, so a silent host costs the round
    /// one member and never the catch-up.
    /// </remarks>
    public Task<VersionedValue<VeritasMetadataRecord>?> ReadRecordAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<VersionedValue<VeritasMetadataRecord>?> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ReadRecordItem(source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Asks every member of the active membership how far it has caught up, which is the observable a membership
    /// change is gated on.
    /// </summary>
    /// <param name="cancellationToken">The caller's token. The returned task completes when it signals, though the queued read still runs.</param>
    /// <returns>One entry per member, in the membership's own order, beside the membership it was measured over.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the plane has been disposed, so its queue accepts no further work.</exception>
    /// <remarks>
    /// A member that does not answer is reported unreachable rather than reported at
    /// <see cref="RegisterVersion.Unwritten"/>: a host that has learned nothing and a host that cannot be reached
    /// are different situations, and a decommission gate that confused them would clear against a silent cluster.
    /// The plane requires the per-member version query at construction, so this always reports. A probe that
    /// answers nothing at all is that same unreachable entry once <see cref="MemberQueryDeadline"/> has passed:
    /// the probe is RACED against the deadline rather than merely told about it, so a query that never returns
    /// and ignores its token costs one member's entry instead of the whole report.
    /// </remarks>
    public Task<RegisterReadiness> ReadReadinessAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<RegisterReadiness> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ReadReadinessItem(source, cancellationToken));

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Completes the obligation queue and awaits the loop, which drains what was already queued and returns. The
    /// host's node and runner are not this plane's to dispose.
    /// </summary>
    /// <returns>A task that completes once the loop has drained and ended.</returns>
    /// <remarks>
    /// An obligation enqueued after this faults fast rather than hanging on a loop that will never dispatch it. A
    /// loop that ended on a failure of its own rethrows here, because a plane whose loop is gone must not be
    /// disposed silently while a host still believes its coordination is running.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        _ = Work.Writer.TryComplete();

        await Loop.ConfigureAwait(false);
    }

    /// <summary>Validates an identity axis: the default axis carries no bytes and names no replica.</summary>
    /// <param name="axis">The axis to validate.</param>
    /// <param name="parameterName">The name of the caller's parameter the exception names.</param>
    /// <returns>The validated axis.</returns>
    /// <exception cref="ArgumentException">Thrown if the axis is not exactly <see cref="ReplicaAxis.ByteWidth"/> bytes.</exception>
    private static ReplicaAxis ValidateAxis(ReplicaAxis axis, string parameterName)
    {
        if(axis.Bytes.Length != ReplicaAxis.ByteWidth)
        {
            throw new ArgumentException("A metadata-plane obligation names a well-formed replica identity axis; the default axis carries no bytes and names no replica.", parameterName);
        }

        return axis;
    }

    /// <summary>The result an obligation reports for one write outcome.</summary>
    /// <typeparam name="TOutcome">The obligation's own outcome ladder.</typeparam>
    /// <param name="outcome">The ladder value the outcome mapping produced.</param>
    /// <param name="written">What the register's write established.</param>
    /// <returns>The result, carrying the decided record and its version exactly when this replica's own write committed.</returns>
    /// <remarks>
    /// The record and the version are carried only for a committed write. A superseded or undecided attempt
    /// answers <c>Undecided</c>, and pairing that with a record would offer a value the obligation did not
    /// establish.
    /// </remarks>
    private static MetadataPlaneResult<TOutcome> ResultFor<TOutcome>(TOutcome outcome, QuePaxaWriteOutcome<VeritasMetadataRecord> written)
        where TOutcome: struct, Enum
    {
        return written.Status == QuePaxaWriteStatus.Committed
            ? new MetadataPlaneResult<TOutcome>(outcome, written.Value, written.Version)
            : new MetadataPlaneResult<TOutcome>(outcome, null, RegisterVersion.Unwritten);
    }

    /// <summary>Queues one work item for the loop.</summary>
    /// <param name="item">The item to queue.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the queue is completed, which only disposal does.</exception>
    /// <remarks>
    /// The queue is unbounded, so the write is synchronous and never waits; the only way it refuses is a
    /// completed queue, and a use-after-dispose is a defect rather than an expected condition an outcome ladder
    /// should have a value for.
    /// </remarks>
    private void Enqueue(PlaneWorkItem item)
    {
        if(!Work.Writer.TryWrite(item))
        {
            throw new ObjectDisposedException(nameof(VeritasMetadataPlane), "The metadata plane has been disposed and accepts no further obligations.");
        }
    }

    /// <summary>
    /// Dispatches queued work against the register, one item at a time, until the queue is completed and drained.
    /// </summary>
    /// <returns>A task that completes when the queue is drained after completion.</returns>
    /// <remarks>
    /// A refusal an obligation earns is that obligation's own answer and leaves the loop serving, which is what
    /// makes concurrent different obligations independent. The loop ends only when the queue is completed or when
    /// the loop's own machinery fails, and the second case is fail-closed: every unanswered obligation is faulted
    /// with the loop failure as inner exception, the queue is completed so later callers fault fast, and the
    /// failure then propagates to whoever awaits disposal.
    /// </remarks>
    private async Task DrainAsync()
    {
        PlaneWorkItem? inFlight = null;
        try
        {
            await foreach(PlaneWorkItem item in Work.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                inFlight = item;
                await DispatchAsync(item).ConfigureAwait(false);
                inFlight = null;
            }
        }
        catch(Exception failure)
        {
            Abandon(inFlight, failure);

            throw;
        }
    }

    /// <summary>Serves one item and answers it, whatever the register made of it.</summary>
    /// <param name="item">The item to serve.</param>
    /// <returns>A task that completes once the item has been answered.</returns>
    /// <remarks>
    /// A cancelled call completes cancelled under the token that cancelled it, and a failing one faults with what
    /// failed. Both are that call's answer alone: the queued work of a cancelled call still ran, because an
    /// obligation is idempotent by its own write discipline and re-running it is the identity.
    /// </remarks>
    private async ValueTask DispatchAsync(PlaneWorkItem item)
    {
        try
        {
            await ServeAsync(item).ConfigureAwait(false);
        }
        catch(OperationCanceledException cancellation)
        {
            item.Cancel(cancellation.CancellationToken);
        }
        catch(Exception failure)
        {
            item.Fail(failure);
        }
    }

    /// <summary>Runs one item's obligation against the register and completes it with the verdict.</summary>
    /// <param name="item">The item to serve.</param>
    /// <returns>A task that completes once the obligation has been answered.</returns>
    private async ValueTask ServeAsync(PlaneWorkItem item)
    {
        AdoptHostRecord();

        switch(item)
        {
            case BootstrapItem bootstrap:
            {
                BootstrapRule rule = new();
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, bootstrap.AttemptBudget, bootstrap.Token).ConfigureAwait(false);
                PlaneBootstrapOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => PlaneBootstrapOutcome.OutsideConfiguration,
                    _ => PlaneBootstrapOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.Bootstrap, (int)verdict, written.Version, written.Attempts);
                _ = bootstrap.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case ClaimIdentityItem claim:
            {
                ClaimIdentityRule rule = new(Register, claim.Axis, SelfAxis);
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, claim.AttemptBudget, claim.Token).ConfigureAwait(false);
                IdentityClaimOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => IdentityClaimOutcome.OutsideConfiguration,
                    _ => IdentityClaimOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.IdentityClaim, (int)verdict, written.Version, written.Attempts);
                _ = claim.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case BaselineIntentItem intent:
            {
                BaselineIntentRule rule = new(Register, intent.ClaimantAxis, intent.CausalityDigest);
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, intent.AttemptBudget, intent.Token).ConfigureAwait(false);
                BaselineRecordOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => BaselineRecordOutcome.OutsideConfiguration,
                    _ => BaselineRecordOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.BaselineIntent, (int)verdict, written.Version, written.Attempts);
                _ = intent.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case BaselineConfirmItem confirm:
            {
                BaselineConfirmRule rule = new(Register, SelfAxis, confirm.CausalityDigest, confirm.StateId, confirm.DictionaryEpoch);
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, confirm.AttemptBudget, confirm.Token).ConfigureAwait(false);
                BaselineRecordOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => BaselineRecordOutcome.OutsideConfiguration,
                    _ => BaselineRecordOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.BaselineConfirm, (int)verdict, written.Version, written.Attempts);
                _ = confirm.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case AmendPolicyItem amend:
            {
                AmendPolicyRule rule = new(amend.Policy);
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, amend.AttemptBudget, amend.Token).ConfigureAwait(false);
                PolicyAmendmentOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => PolicyAmendmentOutcome.OutsideConfiguration,
                    _ => PolicyAmendmentOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.PolicyAmendment, (int)verdict, written.Version, written.Attempts);
                _ = amend.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case ElectCoordinatorItem elect:
            {
                ElectCoordinatorRule rule = new(Register, elect.Holder);
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, elect.AttemptBudget, elect.Token).ConfigureAwait(false);
                CoordinatorElectionOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => CoordinatorElectionOutcome.OutsideConfiguration,
                    _ => CoordinatorElectionOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.CoordinatorElection, (int)verdict, written.Version, written.Attempts);
                _ = elect.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case ReleaseCoordinatorItem release:
            {
                ReleaseCoordinatorRule rule = new(release.Holder);
                QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.WriteAsync(rule.Compute, release.AttemptBudget, release.Token).ConfigureAwait(false);
                CoordinatorElectionOutcome verdict = written.Status switch
                {
                    QuePaxaWriteStatus.Committed => rule.Decision,
                    QuePaxaWriteStatus.OutsideConfiguration => CoordinatorElectionOutcome.OutsideConfiguration,
                    _ => CoordinatorElectionOutcome.Undecided
                };

                Emit(MetadataPlaneObligation.CoordinatorRelease, (int)verdict, written.Version, written.Attempts);
                _ = release.Source.TrySetResult(ResultFor(verdict, written));
                break;
            }

            case AdmitMemberItem admit:
            {
                await ServeMembershipAsync(new AdmitMemberRule(admit.Host), MetadataPlaneObligation.MemberAdmission, admit).ConfigureAwait(false);
                break;
            }

            case RetireMemberItem retire:
            {
                await ServeMembershipAsync(new RetireMemberRule(retire.Member), MetadataPlaneObligation.MemberRetirement, retire).ConfigureAwait(false);
                break;
            }

            case ReadRecordItem read:
            {
                VersionedValue<VeritasMetadataRecord>? record = await Register.ReadAsync(MemberQueryDeadline, read.Token).ConfigureAwait(false);
                _ = read.Source.TrySetResult(record);
                break;
            }

            case ReadReadinessItem readiness:
            {
                RegisterReadiness report = await Register.ReadReadinessAsync(MemberQueryDeadline, readiness.Token).ConfigureAwait(false);
                _ = readiness.Source.TrySetResult(report);
                break;
            }

            default:
            {
                //The fail-closed backstop for an item the loop does not serve, which would otherwise leave its
                //caller waiting on a completion nothing will set.
                item.Fail(new InvalidOperationException("The metadata plane's write loop was handed work it does not serve."));
                break;
            }
        }
    }

    /// <summary>Runs one membership delta through the register and completes its item with the verdict.</summary>
    /// <param name="rule">The delta to apply, which reports afterwards whether it changed anything.</param>
    /// <param name="obligation">Which obligation this is, for the emitted trace event.</param>
    /// <param name="item">The queued item to answer.</param>
    /// <returns>A task that completes once the obligation has been answered.</returns>
    /// <remarks>
    /// The bootstrap check comes first and answers by value. A reconfiguration carries the committed value
    /// forward and refuses when there is none, and that refusal is an expected condition here — an operator may
    /// legitimately reach a chain nobody has bootstrapped — so it is reported as
    /// <see cref="MembershipChangeOutcome.RequiresBootstrap"/> rather than raised.
    /// </remarks>
    private async ValueTask ServeMembershipAsync(MembershipDeltaRule rule, MetadataPlaneObligation obligation, MembershipItem item)
    {
        if(await RequiresBootstrapAsync(item.Token).ConfigureAwait(false))
        {
            Emit(obligation, (int)MembershipChangeOutcome.RequiresBootstrap, RegisterVersion.Unwritten, 0);
            _ = item.Source.TrySetResult(new MetadataPlaneResult<MembershipChangeOutcome>(MembershipChangeOutcome.RequiresBootstrap, null, RegisterVersion.Unwritten));

            return;
        }

        QuePaxaWriteOutcome<VeritasMetadataRecord> written = await Register.ReconfigureAsync(rule.Change, item.AttemptBudget, item.Token).ConfigureAwait(false);
        MembershipChangeOutcome verdict = written.Status switch
        {
            QuePaxaWriteStatus.Committed => rule.WasNoOp ? MembershipChangeOutcome.Unchanged : MembershipChangeOutcome.Changed,
            QuePaxaWriteStatus.OutsideConfiguration => MembershipChangeOutcome.OutsideConfiguration,
            _ => MembershipChangeOutcome.Undecided
        };

        Emit(obligation, (int)verdict, written.Version, written.Attempts);
        _ = item.Source.TrySetResult(ResultFor(verdict, written));
    }

    /// <summary>Whether the chain has decided nothing yet, so a membership change has no value to carry forward.</summary>
    /// <param name="cancellationToken">The obligation's token.</param>
    /// <returns><see langword="true"/> when neither this register nor a member of the active membership knows of a committed record.</returns>
    /// <remarks>
    /// A register that holds no record is asked to catch up before the answer is given, because holding none is
    /// equally what a replica that has never read looks like. Learning nothing then is not proof the chain is
    /// unbootstrapped either, and that residue is exactly why the answer is a value the caller retries on rather
    /// than a refusal.
    /// </remarks>
    private async ValueTask<bool> RequiresBootstrapAsync(CancellationToken cancellationToken)
    {
        if(Register.Committed is not null)
        {
            return false;
        }

        _ = await Register.ReadAsync(MemberQueryDeadline, cancellationToken).ConfigureAwait(false);

        return Register.Committed is null;
    }

    /// <summary>
    /// Seeds the register's belief from the host this plane runs beside, so an obligation starts at the version
    /// the host already knows of.
    /// </summary>
    /// <remarks>
    /// The host's committed record is a decided fact whether it was restored from the store, served to a peer, or
    /// pushed in by the replica that decided it, and a register that had to discover it by being superseded would
    /// spend a round per obligation to learn what sits beside it. Adoption only ever runs forward: a record that
    /// does not advance the register is ignored.
    /// </remarks>
    private void AdoptHostRecord()
    {
        if(Node.Committed is { } local)
        {
            _ = Register.Learn(local);
        }
    }

    /// <summary>Emits one completed obligation's verdict, when a sink is attached.</summary>
    /// <param name="obligation">Which obligation completed.</param>
    /// <param name="outcomeCode">The numeric value of that obligation's own outcome ladder.</param>
    /// <param name="version">The register version the obligation addressed, or <see cref="RegisterVersion.Unwritten"/> when it answered without writing.</param>
    /// <param name="attempts">The consensus attempts the obligation spent.</param>
    /// <remarks>
    /// The loop is the plane's only emitter, so the sequence number advances without synchronization and the
    /// numbers run in obligation-completion order.
    /// </remarks>
    private void Emit(MetadataPlaneObligation obligation, int outcomeCode, RegisterVersion version, int attempts)
    {
        TraceHandler<MetadataPlaneTraceEvent>? handler = Trace;
        if(handler is null)
        {
            return;
        }

        MetadataPlaneTraceEvent evt = new(Sequence, TimeProvider.GetUtcNow().UtcTicks, CorrelationId, obligation, outcomeCode, version, attempts);
        Sequence += 1;
        handler(in evt);
    }

    /// <summary>Answers every obligation the loop will no longer serve, then leaves the queue closed.</summary>
    /// <param name="inFlight">The item the loop was serving when it failed, or <see langword="null"/> when it failed between items.</param>
    /// <param name="failure">What ended the loop.</param>
    private void Abandon(PlaneWorkItem? inFlight, Exception failure)
    {
        //Completing the writer first makes a later caller fault fast, and lets the drain below observe every
        //enqueue that had already succeeded.
        _ = Work.Writer.TryComplete();

        InvalidOperationException fault = new("The metadata plane's write loop ended before the obligation completed; the inner exception is the loop failure.", failure);
        inFlight?.Fail(fault);

        while(Work.Reader.TryRead(out PlaneWorkItem? item))
        {
            item.Fail(fault);
        }
    }

    /// <summary>One unit of work the plane's loop serves, carrying whatever completes its caller.</summary>
    /// <remarks>
    /// The completion is typed per item, so answering an abandoned item is the item's own act rather than a
    /// switch the abandon path would have to keep in step with the dispatch.
    /// </remarks>
    private abstract record PlaneWorkItem
    {
        /// <summary>Faults this item's caller.</summary>
        /// <param name="failure">What failed.</param>
        public abstract void Fail(Exception failure);

        /// <summary>Cancels this item's caller under the token that cancelled it.</summary>
        /// <param name="cancellationToken">The token the cancellation carried.</param>
        public abstract void Cancel(CancellationToken cancellationToken);
    }

    /// <summary>One queued obligation, whose answer is one outcome ladder's value.</summary>
    /// <typeparam name="TOutcome">The obligation's own outcome ladder.</typeparam>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token, which the register's write runs under.</param>
    private abstract record PlaneObligationItem<TOutcome>(
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<TOutcome>> Source,
        CancellationToken Token): PlaneWorkItem
        where TOutcome: struct, Enum
    {
        /// <inheritdoc/>
        public override void Fail(Exception failure) => _ = Source.TrySetException(failure);

        /// <inheritdoc/>
        public override void Cancel(CancellationToken cancellationToken) => _ = Source.TrySetCanceled(cancellationToken);
    }

    /// <summary>A queued bootstrap.</summary>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record BootstrapItem(
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<PlaneBootstrapOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<PlaneBootstrapOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued identity claim.</summary>
    /// <param name="Axis">The axis to claim.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record ClaimIdentityItem(
        ReplicaAxis Axis,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<IdentityClaimOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<IdentityClaimOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued lineage-baseline intent.</summary>
    /// <param name="ClaimantAxis">The replica identity axis the baseline dots are minted on.</param>
    /// <param name="CausalityDigest">The content fingerprint of the minted baseline causality.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record BaselineIntentItem(
        ReplicaAxis ClaimantAxis,
        NodeIdentifier CausalityDigest,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<BaselineRecordOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<BaselineRecordOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued lineage-baseline confirmation.</summary>
    /// <param name="CausalityDigest">The digest this confirmation is matched to its intent by.</param>
    /// <param name="StateId">The dataset StateId the committed baseline produced.</param>
    /// <param name="DictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record BaselineConfirmItem(
        NodeIdentifier CausalityDigest,
        NodeIdentifier StateId,
        long DictionaryEpoch,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<BaselineRecordOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<BaselineRecordOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued policy amendment.</summary>
    /// <param name="Policy">The policy to install.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record AmendPolicyItem(
        CoordinationPolicy Policy,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<PolicyAmendmentOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<PolicyAmendmentOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued coordinator election.</summary>
    /// <param name="Holder">The identity axis the lease is taken under.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record ElectCoordinatorItem(
        ReplicaAxis Holder,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<CoordinatorElectionOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<CoordinatorElectionOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued coordinator release.</summary>
    /// <param name="Holder">The identity axis the lease is held under.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record ReleaseCoordinatorItem(
        ReplicaAxis Holder,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<CoordinatorElectionOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<CoordinatorElectionOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued membership delta, which is an admission or a retirement of one named replica.</summary>
    /// <param name="Member">The consensus identity the delta names.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private abstract record MembershipItem(
        ReplicaId Member,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<MembershipChangeOutcome>> Source,
        CancellationToken Token): PlaneObligationItem<MembershipChangeOutcome>(AttemptBudget, Source, Token);

    /// <summary>A queued admission.</summary>
    /// <param name="Host">The host to admit: the consensus identity beside the store admitted to answer for it.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record AdmitMemberItem(
        HostId Host,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<MembershipChangeOutcome>> Source,
        CancellationToken Token): MembershipItem(Host.Replica, AttemptBudget, Source, Token);

    /// <summary>A queued retirement.</summary>
    /// <param name="Member">The consensus identity to retire.</param>
    /// <param name="AttemptBudget">How many consensus attempts the obligation may spend.</param>
    /// <param name="Source">The completion this obligation answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record RetireMemberItem(
        ReplicaId Member,
        int AttemptBudget,
        TaskCompletionSource<MetadataPlaneResult<MembershipChangeOutcome>> Source,
        CancellationToken Token): MembershipItem(Member, AttemptBudget, Source, Token);

    /// <summary>A queued catch-up read.</summary>
    /// <param name="Source">The completion this read answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record ReadRecordItem(
        TaskCompletionSource<VersionedValue<VeritasMetadataRecord>?> Source,
        CancellationToken Token): PlaneWorkItem
    {
        /// <inheritdoc/>
        public override void Fail(Exception failure) => _ = Source.TrySetException(failure);

        /// <inheritdoc/>
        public override void Cancel(CancellationToken cancellationToken) => _ = Source.TrySetCanceled(cancellationToken);
    }

    /// <summary>A queued readiness report.</summary>
    /// <param name="Source">The completion this read answers on.</param>
    /// <param name="Token">The caller's token.</param>
    private sealed record ReadReadinessItem(
        TaskCompletionSource<RegisterReadiness> Source,
        CancellationToken Token): PlaneWorkItem
    {
        /// <inheritdoc/>
        public override void Fail(Exception failure) => _ = Source.TrySetException(failure);

        /// <inheritdoc/>
        public override void Cancel(CancellationToken cancellationToken) => _ = Source.TrySetCanceled(cancellationToken);
    }

    /// <summary>
    /// The bootstrap rule: the deterministic initial record when the chain has decided nothing, and the record
    /// already committed otherwise.
    /// </summary>
    /// <remarks>
    /// Every founder computes the same initial value, so founders racing to bootstrap propose one value and the
    /// race resolves without anyone's state being lost. As an explicit binding frame it captures nothing, so it
    /// holds no lexical closure.
    /// </remarks>
    private sealed class BootstrapRule
    {
        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public PlaneBootstrapOutcome Decision { get; private set; } = PlaneBootstrapOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            if(current is null)
            {
                Decision = PlaneBootstrapOutcome.Bootstrapped;

                return VeritasMetadataRecord.Initial;
            }

            Decision = PlaneBootstrapOutcome.AlreadyBootstrapped;

            return current;
        }
    }

    /// <summary>
    /// The identity-claim rule: append the axis when the record does not carry it, and otherwise leave the record
    /// as it is and report whether the standing claim is this replica's own.
    /// </summary>
    /// <param name="register">The register the claim is written through, read for the version the claim will be decided at.</param>
    /// <param name="axis">The axis being claimed.</param>
    /// <param name="selfAxis">This replica's own identity axis, which is what makes a standing claim its own.</param>
    /// <remarks>
    /// A claim is appended and never rewritten, so the version it carries names the write that first settled the
    /// axis. As an explicit binding frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private sealed class ClaimIdentityRule(QuePaxaVersionedRegister<VeritasMetadataRecord> register, ReplicaAxis axis, ReplicaAxis selfAxis)
    {
        /// <summary>The register the claim is written through.</summary>
        private QuePaxaVersionedRegister<VeritasMetadataRecord> Register { get; } = register;

        /// <summary>The axis being claimed.</summary>
        private ReplicaAxis Axis { get; } = axis;

        /// <summary>This replica's own identity axis.</summary>
        private ReplicaAxis SelfAxis { get; } = selfAxis;

        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public IdentityClaimOutcome Decision { get; private set; } = IdentityClaimOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            VeritasMetadataRecord held = current ?? VeritasMetadataRecord.Initial;
            for(int i = 0; i < held.IdentityClaims.Length; i++)
            {
                if(held.IdentityClaims[i].Axis.Equals(Axis))
                {
                    Decision = Axis.Equals(SelfAxis) ? IdentityClaimOutcome.AlreadyClaimedBySelf : IdentityClaimOutcome.RefusedHeldByOther;

                    return held;
                }
            }

            Decision = IdentityClaimOutcome.Claimed;

            //The version read here is the one this proposal will carry: the register captured its instance
            //before it asked for this value, and the plane's queue is what keeps that capture from moving
            //underneath the read.
            return held with { IdentityClaims = held.IdentityClaims.Add(new ReplicaIdentityClaim(Axis, Register.NextVersion)) };
        }
    }

    /// <summary>
    /// The baseline-intent rule: set the baseline when the record carries none, report a byte-identical repeat as
    /// already recorded, and refuse a different one.
    /// </summary>
    /// <param name="register">The register the intent is written through, read for the version the intent will be decided at.</param>
    /// <param name="claimantAxis">The replica identity axis the baseline dots are minted on.</param>
    /// <param name="causalityDigest">The content fingerprint of the minted baseline causality.</param>
    /// <remarks>
    /// The repeat arm is the crash-retry path: minting a baseline is deterministic given the identity and the
    /// present triples, so a replica reproduces its digest and its retry changes nothing. As an explicit binding
    /// frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private sealed class BaselineIntentRule(QuePaxaVersionedRegister<VeritasMetadataRecord> register, ReplicaAxis claimantAxis, NodeIdentifier causalityDigest)
    {
        /// <summary>The register the intent is written through.</summary>
        private QuePaxaVersionedRegister<VeritasMetadataRecord> Register { get; } = register;

        /// <summary>The replica identity axis the baseline dots are minted on.</summary>
        private ReplicaAxis ClaimantAxis { get; } = claimantAxis;

        /// <summary>The content fingerprint of the minted baseline causality.</summary>
        private NodeIdentifier CausalityDigest { get; } = causalityDigest;

        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public BaselineRecordOutcome Decision { get; private set; } = BaselineRecordOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            VeritasMetadataRecord held = current ?? VeritasMetadataRecord.Initial;
            if(held.Baseline is not { } recorded)
            {
                Decision = BaselineRecordOutcome.Recorded;

                return held with { Baseline = LineageBaseline.Intent(ClaimantAxis, CausalityDigest, Register.NextVersion) };
            }

            if(recorded.ClaimantAxis.Equals(ClaimantAxis) && recorded.CausalityDigest == CausalityDigest)
            {
                Decision = BaselineRecordOutcome.AlreadyRecorded;

                return held;
            }

            Decision = BaselineRecordOutcome.ConflictingLineage;

            return held;
        }
    }

    /// <summary>
    /// The baseline-confirm rule: fill the dataset StateId and the dictionary epoch together on the baseline whose
    /// digest matches, record the whole confirmed baseline when the record carries none, report an identical
    /// refill as already recorded, and refuse a digest that does not match or fields already filled differently.
    /// </summary>
    /// <param name="register">The register the confirmation is written through, read for the version it will be decided at.</param>
    /// <param name="claimantAxis">This replica's own identity axis, which names the claimant when the confirmation records the baseline whole.</param>
    /// <param name="causalityDigest">The digest this confirmation is matched to its intent by.</param>
    /// <param name="stateId">The dataset StateId the committed baseline produced.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
    /// <remarks>
    /// The absent-baseline arm records the confirmed baseline whole rather than refusing, because an intent that
    /// answered undecided lets the open proceed by the plane's own liveness rule, and a commit that followed one
    /// must still be recordable. As an explicit binding frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private sealed class BaselineConfirmRule(
        QuePaxaVersionedRegister<VeritasMetadataRecord> register,
        ReplicaAxis claimantAxis,
        NodeIdentifier causalityDigest,
        NodeIdentifier stateId,
        long dictionaryEpoch)
    {
        /// <summary>The register the confirmation is written through.</summary>
        private QuePaxaVersionedRegister<VeritasMetadataRecord> Register { get; } = register;

        /// <summary>This replica's own identity axis.</summary>
        private ReplicaAxis ClaimantAxis { get; } = claimantAxis;

        /// <summary>The digest this confirmation is matched to its intent by.</summary>
        private NodeIdentifier CausalityDigest { get; } = causalityDigest;

        /// <summary>The dataset StateId the committed baseline produced.</summary>
        private NodeIdentifier StateId { get; } = stateId;

        /// <summary>The term-dictionary epoch the committed baseline was written under.</summary>
        private long DictionaryEpoch { get; } = dictionaryEpoch;

        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public BaselineRecordOutcome Decision { get; private set; } = BaselineRecordOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            VeritasMetadataRecord held = current ?? VeritasMetadataRecord.Initial;
            if(held.Baseline is not { } recorded)
            {
                Decision = BaselineRecordOutcome.Confirmed;

                return held with { Baseline = new LineageBaseline(ClaimantAxis, CausalityDigest, new LineageConfirmation(StateId, DictionaryEpoch), Register.NextVersion) };
            }

            if(recorded.CausalityDigest != CausalityDigest)
            {
                Decision = BaselineRecordOutcome.ConflictingLineage;

                return held;
            }

            if(recorded.IsConfirmed)
            {
                Decision = recorded.StateId == StateId && recorded.DictionaryEpoch == DictionaryEpoch
                    ? BaselineRecordOutcome.AlreadyRecorded
                    : BaselineRecordOutcome.ConflictingLineage;

                return held;
            }

            Decision = BaselineRecordOutcome.Confirmed;

            return held with { Baseline = recorded.Confirm(StateId, DictionaryEpoch, Register.NextVersion) };
        }
    }

    /// <summary>
    /// The policy-amendment rule: install the policy when the record carries a different one, and leave the record
    /// as it is when it already carries this one.
    /// </summary>
    /// <param name="policy">The policy to install.</param>
    /// <remarks>
    /// As an explicit binding frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private sealed class AmendPolicyRule(CoordinationPolicy policy)
    {
        /// <summary>The policy to install.</summary>
        private CoordinationPolicy Policy { get; } = policy;

        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public PolicyAmendmentOutcome Decision { get; private set; } = PolicyAmendmentOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            VeritasMetadataRecord held = current ?? VeritasMetadataRecord.Initial;
            if(held.Policy.Equals(Policy))
            {
                Decision = PolicyAmendmentOutcome.Unchanged;

                return held;
            }

            Decision = PolicyAmendmentOutcome.Amended;

            return held with { Policy = Policy };
        }
    }

    /// <summary>
    /// The election rule, which is the succession discipline itself: a vacant lease is taken, a lease already held
    /// under this axis is refreshed at a new term, a lease held by another CURRENT member is left alone, and a
    /// lease held by a replica the membership no longer lists is taken over.
    /// </summary>
    /// <param name="register">The register the election is written through, read for the version the term takes and for the membership the holder is tested against.</param>
    /// <param name="holder">The identity axis the lease is taken under.</param>
    /// <remarks>
    /// The membership test is what ties usurpation to the retirement obligation this plane already coordinates,
    /// and it is read from the register's active membership, which is a memo of the committed record rather than a
    /// setting. As an explicit binding frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private sealed class ElectCoordinatorRule(QuePaxaVersionedRegister<VeritasMetadataRecord> register, ReplicaAxis holder)
    {
        /// <summary>The register the election is written through.</summary>
        private QuePaxaVersionedRegister<VeritasMetadataRecord> Register { get; } = register;

        /// <summary>The identity axis the lease is taken under.</summary>
        private ReplicaAxis Holder { get; } = holder;

        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public CoordinatorElectionOutcome Decision { get; private set; } = CoordinatorElectionOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            VeritasMetadataRecord held = current ?? VeritasMetadataRecord.Initial;
            if(held.Coordinator is not { } lease)
            {
                Decision = CoordinatorElectionOutcome.Elected;

                return held with { Coordinator = new CoordinatorLease(Holder, Register.NextVersion) };
            }

            if(lease.Holder.Equals(Holder))
            {
                Decision = CoordinatorElectionOutcome.Refreshed;

                return held with { Coordinator = new CoordinatorLease(Holder, Register.NextVersion) };
            }

            if(Register.ActiveConfiguration.Contains(MetadataPlaneDeployment.ReplicaIdFor(lease.Holder)))
            {
                Decision = CoordinatorElectionOutcome.HeldByOther;

                return held;
            }

            Decision = CoordinatorElectionOutcome.Elected;

            return held with { Coordinator = new CoordinatorLease(Holder, Register.NextVersion) };
        }
    }

    /// <summary>
    /// The release rule: a lease held under this axis is vacated, a lease already vacant is left vacant, and a
    /// lease held under another axis is left alone — only a holder releases its own lease.
    /// </summary>
    /// <param name="holder">The identity axis the lease is held under.</param>
    /// <remarks>
    /// As an explicit binding frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private sealed class ReleaseCoordinatorRule(ReplicaAxis holder)
    {
        /// <summary>The identity axis the lease is held under.</summary>
        private ReplicaAxis Holder { get; } = holder;

        /// <summary>What this rule decided while computing the record it last proposed.</summary>
        public CoordinatorElectionOutcome Decision { get; private set; } = CoordinatorElectionOutcome.Undecided;

        /// <summary>Computes the record to propose.</summary>
        /// <param name="current">The record this register believes committed, or <see langword="null"/> when it believes none is.</param>
        /// <returns>The record to propose.</returns>
        public VeritasMetadataRecord Compute(VeritasMetadataRecord? current)
        {
            VeritasMetadataRecord held = current ?? VeritasMetadataRecord.Initial;
            if(held.Coordinator is not { } lease)
            {
                Decision = CoordinatorElectionOutcome.Released;

                return held;
            }

            if(!lease.Holder.Equals(Holder))
            {
                Decision = CoordinatorElectionOutcome.HeldByOther;

                return held;
            }

            Decision = CoordinatorElectionOutcome.Released;

            return held with { Coordinator = null };
        }
    }

    /// <summary>
    /// One membership delta, applied to whatever membership the reconfiguring attempt is under and reporting
    /// afterwards whether it changed anything.
    /// </summary>
    /// <param name="member">The consensus identity the delta names.</param>
    /// <remarks>
    /// A delta and never an absolute set: it is re-applied against the membership that won a superseded attempt,
    /// so two operators changing membership concurrently compose instead of undoing each other. As an explicit
    /// binding frame it captures nothing, so it holds no lexical closure.
    /// </remarks>
    private abstract class MembershipDeltaRule(ReplicaId member)
    {
        /// <summary>The consensus identity the delta names.</summary>
        protected ReplicaId Member { get; } = member;

        /// <summary>Whether the delta this rule last applied left the membership as it found it.</summary>
        public bool WasNoOp { get; private set; }

        /// <summary>Applies the delta and records whether it changed the membership.</summary>
        /// <param name="current">The membership the reconfiguring attempt captured.</param>
        /// <returns>The membership to install, which is <paramref name="current"/> when the delta is already installed.</returns>
        public QuePaxaConfiguration Change(QuePaxaConfiguration current)
        {
            QuePaxaConfiguration next = Apply(current);
            WasNoOp = next.Equals(current);

            return next;
        }

        /// <summary>Applies this rule's own delta.</summary>
        /// <param name="current">The membership the reconfiguring attempt captured.</param>
        /// <returns>The membership the delta computes.</returns>
        protected abstract QuePaxaConfiguration Apply(QuePaxaConfiguration current);
    }

    /// <summary>The admission delta: add the named host, or leave the membership as it is when it is already listed.</summary>
    /// <param name="host">The host to admit: the consensus identity beside the store admitted to answer for it.</param>
    /// <remarks>
    /// The delta names a host and not a replica, because a membership admits the store answering for a replica
    /// and not the replica alone. An addition naming a listed replica under another store is refused by the
    /// membership itself: replacing a member's store is a retirement and an admission, so an operator who
    /// rebuilt a member's store retires it first.
    /// </remarks>
    private sealed class AdmitMemberRule(HostId host): MembershipDeltaRule(host.Replica)
    {
        /// <summary>The host this delta admits.</summary>
        private HostId Host { get; } = host;

        /// <inheritdoc/>
        protected override QuePaxaConfiguration Apply(QuePaxaConfiguration current) => current.With(Host);
    }

    /// <summary>The retirement delta: remove the named replica, or leave the membership as it is when it is not listed.</summary>
    /// <param name="member">The consensus identity to retire.</param>
    private sealed class RetireMemberRule(ReplicaId member): MembershipDeltaRule(member)
    {
        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">Thrown when the named replica is the membership's last member, because a membership can never be emptied.</exception>
        protected override QuePaxaConfiguration Apply(QuePaxaConfiguration current) => current.Without(Member);
    }
}
