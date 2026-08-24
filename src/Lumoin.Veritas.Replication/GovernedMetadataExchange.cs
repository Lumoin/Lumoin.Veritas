using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Network;
using Lumoin.Verisync.Core;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Governs the consensus metadata exchanges with one cluster member: a closure-free decorator that runs the
/// network-governance gate at <see cref="NetworkBoundary.ConsensusExchange"/> before each of the four inner
/// operations and, on a permit, performs it. Its <see cref="RecordAsync"/>, <see cref="ReadCommittedAsync"/>,
/// <see cref="PushRecordAsync"/> and <see cref="ObserveVersionAsync"/> carry the same faces as the operations
/// they wrap, so it composes in front
/// of any implementation — the <see cref="MetadataChannelClient"/> transport or an in-memory test seam — and a
/// register wires the decorated faces exactly where it would wire the undecorated ones.
/// </summary>
/// <remarks>
/// <para>
/// A DENY IS AN UNREACHABLE MEMBER, AND ON THIS SURFACE THAT IS A FAULT. The replication fetch seam declines by
/// value because its result type HAS an unavailable value; these four do not, and none of the alternatives is
/// truthful: a record reply cannot represent a refusal, because the consensus protocol has no refusal path and
/// every reply it carries is an answer that counts toward a quorum; a committed read answering
/// <see langword="null"/> would assert that the member has learned no record, which is a claim about the
/// member the gate knows nothing about; a version report answering
/// <see cref="RegisterVersion.Unwritten"/> would make the same claim in the one place a readiness report must
/// not confuse it with a member nothing reaches; and a silent push would report a dissemination that never
/// left. So the
/// gate's own exception path is taken and a deny raises
/// <see cref="NetworkGovernanceDeniedException"/> naming the boundary — which is exactly what the consensus
/// surface already absorbs. A recorder endpoint that faults reaches a proposer as an unreachable recorder,
/// retried within its attempt budget and otherwise concluding a missed quorum; a catch-up read skips a faulting
/// host, since one honest host settles a committed fact and no quorum is counted; a faulting version probe is
/// recorded as an unreachable member, which is the answer a denied route deserves; and a faulting dissemination
/// leaves the decided write decided, because the register returns its outcome whatever dissemination does. A
/// denied member is therefore a slower cluster and never a wrong one.
/// </para>
/// <para>
/// The member is per-connection, so the peer key and access context are construction state rather than
/// per-call arguments — the four faces keep the exact signatures the register expects. This decorator owns the
/// peer key and disposes it with itself: the key's pooled bytes are read inside the asynchronous governance
/// decision, so a single owner whose lifetime is the connection's keeps them valid for the decision's whole
/// duration. Disposing this decorator while a call is in flight is the ordinary use-after-dispose misuse, not a
/// separate-owner race that could silently return the buffer to the pool mid-decision. As an explicit binding
/// frame it captures nothing, so it holds no lexical closure.
/// </para>
/// </remarks>
public sealed class GovernedMetadataExchange: IDisposable
{
    /// <summary>The recorder endpoint this governs — invoked on a permit.</summary>
    private VersionedRecorderEndpointDelegate<CommittedMetadataRecord> Record { get; }

    /// <summary>The catch-up read this governs — invoked on a permit.</summary>
    private ReadCommittedRecordDelegate<VeritasMetadataRecord> ReadCommitted { get; }

    /// <summary>The dissemination offer this governs — invoked on a permit.</summary>
    private OfferMetadataRecordDelegate OfferRecord { get; }

    /// <summary>The version probe this governs — invoked on a permit.</summary>
    private ObserveMetadataVersionDelegate ObserveVersion { get; }

    /// <summary>The policy consulted before each call.</summary>
    private NetworkGovernanceDelegate Governance { get; }

    /// <summary>The member this decorator reaches; owned and disposed with this decorator.</summary>
    private NetworkPeerKey Peer { get; }

    /// <summary>The opaque access context identifying the local node to the policy, or <see langword="null"/>.</summary>
    private AccessContext? Context { get; }

    /// <summary>The clock a delayed call backs off against and the emitted event is timestamped with.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The diagnostics sink each governance verdict is emitted to, or <see langword="null"/> to emit nothing.</summary>
    private TraceHandler<NetworkGovernanceTraceEvent>? Trace { get; }

    /// <summary>The correlation id the emitted events carry.</summary>
    private Guid CorrelationId { get; }

    //A naked field: the trace sequence is advanced with Interlocked, which needs a by-ref target.
    private long sequence;

    /// <summary>Creates a governed exchange over the four inner operations for one member.</summary>
    /// <param name="record">The recorder endpoint this governs — the transport (or in-memory) endpoint invoked on a permit.</param>
    /// <param name="readCommitted">The catch-up read this governs.</param>
    /// <param name="offerRecord">The dissemination offer this governs.</param>
    /// <param name="observeVersion">The version probe this governs.</param>
    /// <param name="governance">The policy consulted before each call.</param>
    /// <param name="peer">The member this reaches; <see cref="NetworkPeerKey.None"/> when unidentified. Ownership transfers to this decorator, which disposes it; the caller must not dispose it or use it elsewhere.</param>
    /// <param name="context">The opaque access context identifying the local node to the policy, or <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock a delayed call backs off against and the event is timestamped with.</param>
    /// <param name="trace">The diagnostics sink each governance verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted events carry.</param>
    /// <exception cref="ArgumentNullException">A required operation, the policy, the peer key, or the clock is <see langword="null"/>.</exception>
    public GovernedMetadataExchange(
        VersionedRecorderEndpointDelegate<CommittedMetadataRecord> record,
        ReadCommittedRecordDelegate<VeritasMetadataRecord> readCommitted,
        OfferMetadataRecordDelegate offerRecord,
        ObserveMetadataVersionDelegate observeVersion,
        NetworkGovernanceDelegate governance,
        NetworkPeerKey peer,
        AccessContext? context,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace = null,
        Guid correlationId = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(readCommitted);
        ArgumentNullException.ThrowIfNull(offerRecord);
        ArgumentNullException.ThrowIfNull(observeVersion);
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Record = record;
        ReadCommitted = readCommitted;
        OfferRecord = offerRecord;
        ObserveVersion = observeVersion;
        Governance = governance;
        Peer = peer;
        Context = context;
        TimeProvider = timeProvider;
        Trace = trace;
        CorrelationId = correlationId;
    }

    /// <summary>Disposes the peer key this decorator owns. Do not dispose while a call is in flight.</summary>
    public void Dispose()
    {
        Peer.Dispose();
    }

    /// <summary>Governs then records: consults the policy for a consensus exchange and, on a permit, sends the record request to the member. A <see cref="VersionedRecorderEndpointDelegate{TValue}"/> over the decided record — bind it where a register resolves a member's recorder endpoint.</summary>
    /// <param name="request">The versioned record request to send.</param>
    /// <param name="cancellationToken">Cancels the governance decision or the call.</param>
    /// <returns>The member's reply on a permit.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the call; a register reads the fault as an unreachable recorder.</exception>
    public async ValueTask<VersionedRecordReply<CommittedMetadataRecord>> RecordAsync(VersionedRecordRequest<CommittedMetadataRecord> request, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);

        return await Record(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Governs then reads: consults the policy for a consensus exchange and, on a permit, asks the member for the committed record it has learned. A <see cref="ReadCommittedRecordDelegate{TValue}"/> — bind it where a register resolves a member's catch-up reader.</summary>
    /// <param name="cancellationToken">Cancels the governance decision or the call.</param>
    /// <returns>The member's committed record on a permit, or <see langword="null"/> when the member has learned none.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the call; a catch-up read skips the member as it skips any faulting host.</exception>
    public async ValueTask<CommittedMetadataRecord?> ReadCommittedAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);

        return await ReadCommitted(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Governs then offers: consults the policy for a consensus exchange and, on a permit, offers the decided record to the member. An <see cref="OfferMetadataRecordDelegate"/> — bind it as one leg of a plane's dissemination fan-out.</summary>
    /// <param name="committed">The decided record to offer.</param>
    /// <param name="cancellationToken">Cancels the governance decision or the call.</param>
    /// <returns>A task that completes when the member has learned the record durably.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the call; the decided write stands and dissemination reaches one member fewer.</exception>
    public async ValueTask PushRecordAsync(CommittedMetadataRecord committed, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        await OfferRecord(committed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Governs then probes: consults the policy for a consensus exchange and, on a permit, asks the member which committed version it holds. An <see cref="ObserveMetadataVersionDelegate"/> — bind it as the per-member leg a readiness report is assembled from.</summary>
    /// <param name="cancellationToken">Cancels the governance decision or the call.</param>
    /// <returns>The member's report on a permit: the version it holds beside the identity the answering host asserts for itself.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the call; the report records the member as unreachable, which is what a denied route is.</exception>
    /// <remarks>
    /// The report is passed through untouched. A decorator that filled the identity in from the member it was
    /// composed for would make every answer pass the register's mis-wiring refusal, whichever host produced it.
    /// </remarks>
    public async ValueTask<MemberVersionReport> ObserveVersionAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);

        return await ObserveVersion(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Consults the policy for one consensus exchange with this member and honors the verdict: a permit (or a delay, after its back-off) returns, a deny throws.</summary>
    /// <param name="cancellationToken">Cancels the decision or the back-off.</param>
    /// <returns>A task that completes when the call may proceed.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the call.</exception>
    private ValueTask EnterAsync(CancellationToken cancellationToken)
    {
        //A consensus exchange carries one control-plane message whose weight tells a size policy nothing, so
        //the size hint is the documented unknown rather than an invented number, and the exchange belongs to
        //no partition.
        NetworkGovernanceRequest request = new(NetworkBoundary.ConsensusExchange, Context, Peer, OperationSizeHint: 0, PartitionCoordinate: -1);

        return NetworkGovernanceGate.EnterOrThrowAsync(Governance, request, TimeProvider, Trace, CorrelationId, Interlocked.Increment(ref sequence), cancellationToken);
    }
}
