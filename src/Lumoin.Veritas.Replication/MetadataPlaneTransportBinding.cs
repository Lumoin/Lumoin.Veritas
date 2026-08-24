using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Network;
using Lumoin.Verisync.Core;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Composes one metadata channel per cluster member into exactly the five seams
/// <see cref="VeritasMetadataPlane"/> is constructed with: the recorder resolver, the committed-record reader
/// resolver, the aggregate version observation, the per-member version observation, and the dissemination
/// fan-out. It is the composition root's glue and holds no protocol rule of its own — the write discipline is
/// the plane's, the consensus procedure is the register's, and the framing is the channel's.
/// </summary>
/// <remarks>
/// <para>
/// SELF IS SERVED BY THE LOCAL HOST AND NEVER BY A SOCKET TO ITSELF. A register writes to every member of its
/// membership, this replica included, so the member that IS this replica resolves to the runner that owns the
/// local host: its record path, its committed-record read, a durable learn for a decided record, and a version
/// probe answered under the identity that host itself runs as. A
/// deployment that dialed itself would pay a connection, a codec round trip and a second serialization point
/// for a call that is already in the process, and it would make the local host's availability depend on its own
/// listener.
/// </para>
/// <para>
/// ONE CHANNEL PER FELLOW MEMBER, OWNED HERE. Every other member gets one <see cref="MetadataChannelClient"/>,
/// keyed by consensus identity, and this binding disposes them all. The client dials on its first call and
/// reuses one connection, so composing a binding opens no socket and a member that is never addressed costs a
/// client and nothing more.
/// </para>
/// <para>
/// THE FOUNDER SET IS PLACED AT COMPOSITION, A LATER MEMBER ON FIRST ASK. Every fellow founder's route is
/// looked up before a single client is built, so a deployment that cannot place one of its own founders fails
/// where it was written down rather than half-composed. A member a configuration change introduced is not in
/// that list and is placed the first time the register asks about it, through the same seam and kept
/// afterwards, because a joiner is learned from the record that installed it and no genesis list can name it.
/// </para>
/// <para>
/// AN UNPLACEABLE MEMBER THROWS AND THE REGISTER KEEPS ITS SLOT. The two resolvers report a member the
/// deployment cannot route to by raising what the seam raised, which reaches a register as an unreachable
/// recorder — the case the protocol already handles — so the quorum stays counted over the membership the
/// record names rather than shrinking to the members that happened to resolve. The per-member version query
/// raises for the same reason: a readiness report separates a member that has learned nothing from a member
/// nothing reaches, and the register is what turns the fault into the second answer.
/// </para>
/// <para>
/// A VERSION PROBE IS ANSWERED BY THE HOST IT REACHED, WHICH IS WHAT MAKES THE ANSWER WORTH COUNTING. The
/// identity in a probe's answer is asserted by the serving side — the reached member's own serve loop, or the
/// local host's own node for the member that is this replica — and this binding passes it through rather than
/// writing back the member it aimed at. A readiness report is counted over distinct members, so the register
/// refuses an answer naming another member, and that refusal is the only thing standing between an endpoint map
/// whose two routes reach one host and a decommission gate cleared on fewer distinct replicas than it claims.
/// </para>
/// <para>
/// DISSEMINATION ABSORBS ITS LEGS' FAULTS. Every member of the audience is offered the decided record on its
/// own leg, a faulting leg neither aborts the others nor surfaces, and the fan-out completes when every leg has
/// settled. A decided write is decided whatever dissemination does — the register returns the outcome either
/// way — so a leg that did not land is a slower cluster, and a caller told its committed write failed would
/// retry a write that had already landed.
/// </para>
/// <para>
/// GOVERNANCE DECORATES THE FELLOWS AND NOT THE LOCAL HOST. <see cref="CreateGoverned"/> wraps each member's
/// four operations in a <see cref="GovernedMetadataExchange"/> that owns the member's peer key, because the
/// boundary the policy is consulted at is a network one and the local host is not reached across it.
/// </para>
/// <para>
/// Every seam is a method or a method group and every piece of state sits in an explicit frame, so nothing here
/// captures an enclosing scope.
/// </para>
/// </remarks>
public sealed class MetadataPlaneTransportBinding: IAsyncDisposable
{
    /// <summary>Initializes an empty binding over the local host and the factories its members are built with.</summary>
    /// <param name="runner">The loop that owns the local host, which the member that is this replica resolves to.</param>
    /// <param name="channels">The factory one member's transport is built with.</param>
    /// <param name="governance">The factory one member's governance decoration is built with, or <see langword="null"/> when members are reached ungoverned.</param>
    private MetadataPlaneTransportBinding(
        QuePaxaVersionedRunner<VeritasMetadataRecord> runner,
        MemberChannelFactory channels,
        MemberGovernanceFactory? governance)
    {
        Runner = runner;
        Channels = channels;
        Governance = governance;
    }

    /// <summary>The loop that owns the local host: the member that is this replica is served through it, and the aggregate version observation reads it.</summary>
    private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; }

    /// <summary>The factory one member's transport is built with, which also answers which seam reaches a member.</summary>
    private MemberChannelFactory Channels { get; }

    /// <summary>The factory one member's governance decoration is built with, or <see langword="null"/> when members are reached ungoverned.</summary>
    private MemberGovernanceFactory? Governance { get; }

    /// <summary>The seams reaching each member placed so far, keyed by consensus identity. Read without the gate and written only under it.</summary>
    private ConcurrentDictionary<ReplicaId, MemberLeg> Members { get; } = new();

    /// <summary>The gate every placement and the disposal are taken under, so a member is built once and never after disposal.</summary>
    private Lock Gate { get; } = new();

    /// <summary>Whether this binding has been disposed, which is what stops a later ask from building a member nothing would dispose. Read and written only under <see cref="Gate"/>.</summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Composes the five plane seams over ungoverned metadata channels: one channel per fellow member of
    /// <paramref name="deployment"/>, and the local host for the member that is <paramref name="self"/>.
    /// </summary>
    /// <param name="deployment">The chain's genesis, whose founders are the members placed at composition.</param>
    /// <param name="self">This replica's identity axis, which is also the consensus identity the local host serves under.</param>
    /// <param name="runner">The loop that owns the local host, already built by the caller. The binding neither starts nor disposes it.</param>
    /// <param name="resolveConnection">The seam that answers which connection reaches one named member.</param>
    /// <param name="serializeRecordRequest">The codec that writes one consensus record request.</param>
    /// <param name="deserializeRecordReply">The codec that reads a member's record reply back.</param>
    /// <param name="serializeRecord">The codec that writes one decided record for the dissemination push.</param>
    /// <param name="deserializeRecord">The codec that reads a decided record back from a catch-up answer.</param>
    /// <param name="pool">The pool inbound frame payloads are copied into; the engine's governed pool.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match every member's.</param>
    /// <returns>The binding, ready to hand its five seams to a plane.</returns>
    /// <exception cref="ArgumentNullException">A required deployment, host, seam, codec or pool is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="self"/> carries no well-formed identity axis, or <paramref name="runner"/> owns a host running as a member other than the one <paramref name="self"/> names.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public static MetadataPlaneTransportBinding Create(
        MetadataPlaneDeployment deployment,
        ReplicaAxis self,
        QuePaxaVersionedRunner<VeritasMetadataRecord> runner,
        ResolvePeerMetadataConnectionDelegate resolveConnection,
        SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serializeRecordRequest,
        DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> deserializeRecordReply,
        SerializeMessageDelegate<CommittedMetadataRecord> serializeRecord,
        DeserializeMessageDelegate<CommittedMetadataRecord> deserializeRecord,
        MemoryPool<byte> pool,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        MemberChannelFactory channels = ChannelsFor(
            deployment,
            self,
            runner,
            resolveConnection,
            serializeRecordRequest,
            deserializeRecordReply,
            serializeRecord,
            deserializeRecord,
            pool,
            maxFrameLength);

        return Build(deployment, self, runner, channels, governance: null);
    }

    /// <summary>
    /// Composes the five plane seams over GOVERNED metadata channels: each fellow member's four operations run
    /// behind the network-governance gate at <see cref="NetworkBoundary.ConsensusExchange"/>, and the member
    /// that is <paramref name="self"/> is served by the local host ungoverned, because the local host is not
    /// reached across a network boundary.
    /// </summary>
    /// <param name="deployment">The chain's genesis, whose founders are the members placed at composition.</param>
    /// <param name="self">This replica's identity axis, which is also the consensus identity the local host serves under.</param>
    /// <param name="runner">The loop that owns the local host, already built by the caller. The binding neither starts nor disposes it.</param>
    /// <param name="resolveConnection">The seam that answers which connection reaches one named member.</param>
    /// <param name="serializeRecordRequest">The codec that writes one consensus record request.</param>
    /// <param name="deserializeRecordReply">The codec that reads a member's record reply back.</param>
    /// <param name="serializeRecord">The codec that writes one decided record for the dissemination push.</param>
    /// <param name="deserializeRecord">The codec that reads a decided record back from a catch-up answer.</param>
    /// <param name="pool">The pool inbound frame payloads are copied into and each member's peer key is rented from; the engine's governed pool.</param>
    /// <param name="governance">The policy consulted before every exchange with a member.</param>
    /// <param name="context">The opaque access context identifying the local node to the policy, or <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock a delayed exchange backs off against and governance events are timestamped with.</param>
    /// <param name="trace">The diagnostics sink each governance verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id emitted governance events carry.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match every member's.</param>
    /// <returns>The binding, ready to hand its five seams to a plane.</returns>
    /// <exception cref="ArgumentNullException">A required deployment, host, seam, codec, pool, policy or clock is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="self"/> carries no well-formed identity axis, or <paramref name="runner"/> owns a host running as a member other than the one <paramref name="self"/> names.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    /// <remarks>
    /// A denied exchange faults, which is what the consensus surface already absorbs: a denied recorder is an
    /// unreachable one inside the attempt budget, a denied catch-up read is a skipped host, and a denied push
    /// leaves the decided write decided. A denied member is therefore a slower cluster and never a wrong one.
    /// </remarks>
    public static MetadataPlaneTransportBinding CreateGoverned(
        MetadataPlaneDeployment deployment,
        ReplicaAxis self,
        QuePaxaVersionedRunner<VeritasMetadataRecord> runner,
        ResolvePeerMetadataConnectionDelegate resolveConnection,
        SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serializeRecordRequest,
        DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> deserializeRecordReply,
        SerializeMessageDelegate<CommittedMetadataRecord> serializeRecord,
        DeserializeMessageDelegate<CommittedMetadataRecord> deserializeRecord,
        MemoryPool<byte> pool,
        NetworkGovernanceDelegate governance,
        AccessContext? context,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace = null,
        Guid correlationId = default,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        MemberChannelFactory channels = ChannelsFor(
            deployment,
            self,
            runner,
            resolveConnection,
            serializeRecordRequest,
            deserializeRecordReply,
            serializeRecord,
            deserializeRecord,
            pool,
            maxFrameLength);

        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return Build(deployment, self, runner, channels, new MemberGovernanceFactory(governance, context, timeProvider, trace, correlationId, pool));
    }

    /// <summary>
    /// Answers which recorder endpoint reaches <paramref name="member"/>, which is this binding's
    /// <see cref="ResolveRecorderEndpointDelegate{TValue}"/>.
    /// </summary>
    /// <param name="member">The member to reach.</param>
    /// <returns>That member's recorder endpoint: the local host's record path for this replica, and the member's channel otherwise.</returns>
    /// <exception cref="ObjectDisposedException">This binding has been disposed, so it builds no further member.</exception>
    /// <remarks>
    /// A member the deployment cannot place reports so by raising what the connection seam raised, which a
    /// register reads as an unreachable recorder whose slot it keeps. Dropping the slot instead would decide on
    /// a smaller majority than the membership names.
    /// </remarks>
    public VersionedRecorderEndpointDelegate<CommittedMetadataRecord> ResolveRecorder(ReplicaId member)
    {
        return LegFor(member).Record;
    }

    /// <summary>
    /// Answers which catch-up read reaches <paramref name="member"/>, which is this binding's
    /// <see cref="ResolveCommittedRecordReaderDelegate{TValue}"/>.
    /// </summary>
    /// <param name="member">The member to ask.</param>
    /// <returns>That member's committed-record read: the local host's sequenced read for this replica, and the member's channel otherwise.</returns>
    /// <exception cref="ObjectDisposedException">This binding has been disposed, so it builds no further member.</exception>
    /// <remarks>
    /// A member the deployment cannot place raises, which a catch-up skips as it skips any failing host: the
    /// read takes no quorum, so reaching fewer hosts is a weaker result rather than a wrong one.
    /// </remarks>
    public ReadCommittedRecordDelegate<VeritasMetadataRecord> ResolveCommittedReader(ReplicaId member)
    {
        return LegFor(member).ReadCommitted;
    }

    /// <summary>
    /// Reports the highest committed version the LOCAL host knows of, which is this binding's
    /// <see cref="ObserveCommittedVersionDelegate"/> — what a delayed writer stands down on rather than running
    /// an instance that is already closed.
    /// </summary>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The local host's highest committed version, or <see cref="RegisterVersion.Unwritten"/> when it has learned none.</returns>
    /// <remarks>
    /// It is read through the host's own queue, so the version reported is one the host's store holds rather
    /// than one a crash could take back. A stale answer costs a redundant attempt and never costs safety.
    /// </remarks>
    public async ValueTask<RegisterVersion> ObserveCommittedVersionAsync(CancellationToken cancellationToken)
    {
        CommittedMetadataRecord? held = await Runner.ReadCommittedAsync(cancellationToken).ConfigureAwait(false);

        return held is null ? RegisterVersion.Unwritten : held.Version;
    }

    /// <summary>
    /// Reports which version <paramref name="member"/> has learned, asked over that member's own route through a
    /// dedicated probe exchange, which is this binding's <see cref="ObserveMemberVersionDelegate"/> and what a
    /// readiness report is built from.
    /// </summary>
    /// <param name="member">The member to ask.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>That member's answer: the highest committed version it holds, or <see cref="RegisterVersion.Unwritten"/> when it has learned none, beside the identity the ANSWERING host asserts for itself.</returns>
    /// <exception cref="ObjectDisposedException">This binding has been disposed, so it builds no further member.</exception>
    /// <remarks>
    /// <para>
    /// The question crosses the member's own route rather than reading state behind it, which is what lets a
    /// report separate a member that has learned nothing from a member nothing reaches: the first answers
    /// unwritten over a working route and the second does not answer at all. A member that cannot be placed or
    /// cannot be reached therefore raises here rather than reporting
    /// <see cref="RegisterVersion.Unwritten"/>, and the register turns that into the unreachable entry a
    /// decommission gate must not confuse with a host that has simply learned nothing.
    /// </para>
    /// <para>
    /// THE IDENTITY IN THE ANSWER IS THE ANSWERING HOST'S OWN and never <paramref name="member"/> written back
    /// out. It reaches this method as the serving side put it on the wire — from the local runner's own host for
    /// the member that is this replica — and is passed through untouched, because the register refuses a report
    /// naming a member other than the one it asked. That refusal is what catches an endpoint map whose two
    /// routes land on one host, which would otherwise let one replica fill two slots of a report counted over
    /// distinct members; a binding that labelled the answer with the member it aimed at would make every answer
    /// pass and the mis-wiring go unseen. The claim is the host's own and is not authentication.
    /// </para>
    /// </remarks>
    public async ValueTask<MemberVersionReport> ObserveMemberVersionAsync(ReplicaId member, CancellationToken cancellationToken)
    {
        return await LegFor(member).ObserveVersion(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Offers a decided record to every host in <paramref name="audience"/>, which is this binding's
    /// <see cref="PublishCommittedRecordDelegate{TValue}"/> — the push that makes the next version servable.
    /// </summary>
    /// <param name="committed">The decided record.</param>
    /// <param name="audience">The hosts to offer it to: the union of the membership that decided it and the membership it installs, as the register computed it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>A task that completes once every leg has settled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="committed"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Every leg runs on its own and absorbs whatever it met, cancellation included, so one member that cannot
    /// be placed, cannot be reached or refuses the push neither aborts the remaining legs nor reaches the
    /// register. That is the contract the push seam states: the register awaits dissemination after the
    /// decision is taken and returns the decided outcome whatever happens here, because a caller told its
    /// committed write failed would retry a write that had already landed. What a push did not reach is
    /// observed through a readiness report and not through a write's result.
    /// </para>
    /// <para>
    /// The member that is this replica learns the record DURABLY on the local host, exactly as a member
    /// receiving the push over the wire does: a control-plane fact a crash could lose is one a peer may already
    /// have built the next version on.
    /// </para>
    /// </remarks>
    public async ValueTask PublishCommittedRecordAsync(CommittedMetadataRecord committed, ImmutableArray<ReplicaId> audience, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(committed);

        if(audience.IsDefaultOrEmpty)
        {
            return;
        }

        Task[] legs = new Task[audience.Length];
        for(int index = 0; index < audience.Length; index++)
        {
            //Each leg is started before the next is, so the placement below runs on this thread and the fan-out
            //is over the offers alone.
            legs[index] = OfferQuietlyAsync(this, audience[index], committed, cancellationToken);
        }

        await Task.WhenAll(legs).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes every member channel this binding built, and the governance decoration each carried. The local
    /// host's runner is the caller's and is not disposed here.
    /// </summary>
    /// <returns>A task that completes once every member's transport is torn down.</returns>
    /// <remarks>
    /// Disposal is idempotent, and a member asked for afterwards is refused rather than built, because a
    /// channel nothing would dispose is a connection nothing would close. Do not dispose while a plane still
    /// holds these seams — that is the ordinary use-after-dispose misuse.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        List<MemberLeg> placed;
        lock(Gate)
        {
            if(Disposed)
            {
                return;
            }

            Disposed = true;
            placed = new List<MemberLeg>(Members.Values);
            Members.Clear();
        }

        foreach(MemberLeg leg in placed)
        {
            await leg.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Validates every input the two factories share and builds the transport factory from them.</summary>
    /// <param name="deployment">The chain's genesis.</param>
    /// <param name="self">This replica's identity axis.</param>
    /// <param name="runner">The loop that owns the local host.</param>
    /// <param name="resolveConnection">The seam that answers which connection reaches one named member.</param>
    /// <param name="serializeRecordRequest">The codec that writes one consensus record request.</param>
    /// <param name="deserializeRecordReply">The codec that reads a member's record reply back.</param>
    /// <param name="serializeRecord">The codec that writes one decided record.</param>
    /// <param name="deserializeRecord">The codec that reads a decided record back.</param>
    /// <param name="pool">The pool inbound frame payloads are copied into.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes.</param>
    /// <returns>The factory one member's transport is built with.</returns>
    /// <exception cref="ArgumentNullException">A required deployment, host, seam, codec or pool is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="self"/> carries no well-formed identity axis.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    private static MemberChannelFactory ChannelsFor(
        MetadataPlaneDeployment deployment,
        ReplicaAxis self,
        QuePaxaVersionedRunner<VeritasMetadataRecord> runner,
        ResolvePeerMetadataConnectionDelegate resolveConnection,
        SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serializeRecordRequest,
        DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> deserializeRecordReply,
        SerializeMessageDelegate<CommittedMetadataRecord> serializeRecord,
        DeserializeMessageDelegate<CommittedMetadataRecord> deserializeRecord,
        MemoryPool<byte> pool,
        int maxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(resolveConnection);
        ArgumentNullException.ThrowIfNull(serializeRecordRequest);
        ArgumentNullException.ThrowIfNull(deserializeRecordReply);
        ArgumentNullException.ThrowIfNull(serializeRecord);
        ArgumentNullException.ThrowIfNull(deserializeRecord);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        if(self.Bytes.Length != ReplicaAxis.ByteWidth)
        {
            throw new ArgumentException("A metadata-plane transport binding names a well-formed replica identity axis for the host it composes; the default axis carries no bytes and names no replica.", nameof(self));
        }

        //The fourth seam makes this agreement load-bearing: the local version probe answers under the identity
        //the runner's node runs as, and the register refuses a readiness answer naming a member other than the
        //one it asked, so a binding composed for one member over a host running as another would fail every
        //readiness read of this replica for the plane's whole life. That is a composition fault and it raises
        //here, where the composition was written down.
        if(!MetadataPlaneDeployment.ReplicaIdFor(self).Equals(runner.Node.Self.Replica))
        {
            throw new ArgumentException("A metadata-plane transport binding serves the member its identity axis names through the local host, and the host this runner owns runs as another member; every version probe of this replica would be refused as answered by the wrong host.", nameof(runner));
        }

        return new MemberChannelFactory(
            resolveConnection,
            serializeRecordRequest,
            deserializeRecordReply,
            serializeRecord,
            deserializeRecord,
            pool,
            maxFrameLength);
    }

    /// <summary>Builds the binding and places the local host beside every fellow founder of the deployment.</summary>
    /// <param name="deployment">The chain's genesis, whose founders are placed here.</param>
    /// <param name="self">This replica's identity axis, whose member resolves to the local host.</param>
    /// <param name="runner">The loop that owns the local host.</param>
    /// <param name="channels">The factory one member's transport is built with.</param>
    /// <param name="governance">The factory one member's governance decoration is built with, or <see langword="null"/> when members are reached ungoverned.</param>
    /// <returns>The composed binding.</returns>
    /// <remarks>
    /// Every fellow founder's route is looked up, and under governance every peer key is rented, before the
    /// first client is built: the two placement faults a deployment can meet — a founder it cannot place and a
    /// key pool that refuses a rent — both raise while nothing beyond the raising step is owned, and the
    /// placement loop after them constructs only over arguments already produced and validated, so half a
    /// cluster's channels are never left without an owner to dispose them.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of each created channel transfers to the member leg placed over it on the same statement or the one after; the leg's disposal (owned by the binding) is the channel's disposal, and the placement loop constructs only over arguments already produced and validated, so no path exists between a channel's creation and its leg taking ownership.")]
    private static MetadataPlaneTransportBinding Build(
        MetadataPlaneDeployment deployment,
        ReplicaAxis self,
        QuePaxaVersionedRunner<VeritasMetadataRecord> runner,
        MemberChannelFactory channels,
        MemberGovernanceFactory? governance)
    {
        ReplicaId selfMember = MetadataPlaneDeployment.ReplicaIdFor(self);
        ImmutableArray<MetadataFounder> founders = deployment.Founders;
        List<ReplicaAxis> fellows = new(founders.Length);
        List<OpenPeerMetadataConnectionDelegate> routes = new(founders.Length);
        for(int index = 0; index < founders.Length; index++)
        {
            //A route is placed per REPLICA and not per host: the transport reaches a machine by the identity an
            //operator addresses, and which store answers there is what the answer itself states.
            ReplicaAxis founder = founders[index].Axis;
            if(MetadataPlaneDeployment.ReplicaIdFor(founder).Equals(selfMember))
            {
                continue;
            }

            fellows.Add(founder);
            routes.Add(channels.Resolve(founder));
        }

        MetadataPlaneTransportBinding binding = new(runner, channels, governance);
        binding.Members[selfMember] = MemberLeg.ForLocalHost(runner);
        if(governance is null)
        {
            for(int index = 0; index < fellows.Count; index++)
            {
                binding.Members[MetadataPlaneDeployment.ReplicaIdFor(fellows[index])] = MemberLeg.ForChannel(channels.Create(routes[index]));
            }

            return binding;
        }

        NetworkPeerKey[] peers = RentPeerKeys(governance, fellows);
        for(int index = 0; index < fellows.Count; index++)
        {
            MetadataChannelClient client = channels.Create(routes[index]);
            binding.Members[MetadataPlaneDeployment.ReplicaIdFor(fellows[index])] = MemberLeg.ForGovernedChannel(client, governance.Decorate(client, peers[index]));
        }

        return binding;
    }

    /// <summary>
    /// Rents every fellow's peer key before any member leg exists, so a rent the pool refuses raises while the
    /// keys rented so far are still this method's to release and no leg owns anything yet.
    /// </summary>
    /// <param name="governance">The factory the keys are rented from.</param>
    /// <param name="fellows">The fellow founders, answered one key per member in order.</param>
    /// <returns>The rented keys; ownership transfers to the decorations built over them.</returns>
    private static NetworkPeerKey[] RentPeerKeys(MemberGovernanceFactory governance, List<ReplicaAxis> fellows)
    {
        NetworkPeerKey[] peers = new NetworkPeerKey[fellows.Count];
        int rented = 0;
        try
        {
            for(int index = 0; index < fellows.Count; index++)
            {
                peers[index] = governance.RentPeerKey(fellows[index]);
                rented++;
            }
        }
        catch
        {
            for(int index = 0; index < rented; index++)
            {
                peers[index].Dispose();
            }

            throw;
        }

        return peers;
    }

    /// <summary>Offers a decided record on one leg and absorbs whatever that leg met.</summary>
    /// <param name="binding">The binding whose member is offered the record.</param>
    /// <param name="member">The member to offer it to.</param>
    /// <param name="committed">The decided record.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>A task that completes once this leg has settled, whatever it settled as.</returns>
    /// <remarks>
    /// It is static and takes its operands, so a leg captures no enclosing scope.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Dissemination is operability and never safety: the register returns the decided outcome whatever a leg does, and a decided write reported as failed would be retried by a caller whose write had already landed. A leg that cannot be placed, cannot be reached, refuses the push or is cancelled is therefore absorbed here so the remaining legs still settle, and what a push did not reach is observed through a readiness report instead.")]
    private static async Task OfferQuietlyAsync(MetadataPlaneTransportBinding binding, ReplicaId member, CommittedMetadataRecord committed, CancellationToken cancellationToken)
    {
        try
        {
            await binding.LegFor(member).OfferRecord(committed, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception)
        {
            //A member the push did not reach is a slower cluster and never a wrong one.
        }
    }

    /// <summary>Returns the seams reaching <paramref name="member"/>, placing that member the first time it is asked for.</summary>
    /// <param name="member">The member to reach.</param>
    /// <returns>That member's seams.</returns>
    /// <exception cref="ObjectDisposedException">This binding has been disposed, so it builds no further member.</exception>
    /// <remarks>
    /// A member already placed is answered without the gate, which is every ask after the first. A member the
    /// founder list did not name — the joiner a configuration change installed — is placed here through the
    /// same connection seam and kept, so the route is looked up once however often the register asks.
    /// </remarks>
    private MemberLeg LegFor(ReplicaId member)
    {
        if(Members.TryGetValue(member, out MemberLeg? placed))
        {
            return placed;
        }

        lock(Gate)
        {
            if(Members.TryGetValue(member, out MemberLeg? raced))
            {
                return raced;
            }

            ObjectDisposedException.ThrowIf(Disposed, this);

            ReplicaAxis axis = MetadataPlaneDeployment.AxisFor(member);
            MemberLeg built = LegOver(axis, Channels.Resolve(axis));
            Members[member] = built;

            return built;
        }
    }

    /// <summary>Builds the seams reaching one fellow member over its route, decorating them when this binding is governed.</summary>
    /// <param name="member">The member the route reaches, which is also the peer a governance decision names.</param>
    /// <param name="route">The seam that opens that member's connection.</param>
    /// <returns>That member's seams, owning the channel they were built over.</returns>
    /// <remarks>
    /// The peer key is rented here rather than passed in, so a governed binding cannot compose a member without
    /// one: a leg built ungoverned because a key went missing would be a silent bypass of the boundary the
    /// factory was chosen for.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of the created channel transfers to the returned member leg on every path, and the leg's disposal (owned by this binding) is the channel's disposal. The channel is async-disposable only and dials lazily, so no construction path holds a connection; the sole pre-transfer throw sites are argument validation over locals this method just produced, and the one genuinely disposable intermediate (the rented peer key) is released in the finally until the decoration owns it.")]
    private MemberLeg LegOver(ReplicaAxis member, OpenPeerMetadataConnectionDelegate route)
    {
        MemberGovernanceFactory? governance = Governance;
        if(governance is null)
        {
            return MemberLeg.ForChannel(Channels.Create(route));
        }

        //The peer key is rented BEFORE the channel exists, so the one disposable a failed composition could
        //strand is the key, released in the finally until the decoration owns it; the channel is created after
        //and immediately owned by the leg (it dials lazily, so an undisposed construction path holds no
        //connection).
        NetworkPeerKey? peer = governance.RentPeerKey(member);
        try
        {
            MetadataChannelClient client = Channels.Create(route);
            GovernedMetadataExchange governed = governance.Decorate(client, peer);
            peer = null;

            return MemberLeg.ForGovernedChannel(client, governed);
        }
        finally
        {
            peer?.Dispose();
        }
    }

    /// <summary>
    /// The four seams reaching ONE member, beside whatever this binding owns on that member's behalf. The
    /// local host's leg owns nothing, because the runner behind it is the caller's.
    /// </summary>
    private sealed class MemberLeg: IAsyncDisposable
    {
        /// <summary>Creates a leg over four seams and whatever they were built from.</summary>
        /// <param name="record">The recorder endpoint reaching the member.</param>
        /// <param name="readCommitted">The committed-record read reaching the member.</param>
        /// <param name="offerRecord">The dissemination offer reaching the member.</param>
        /// <param name="observeVersion">The version probe reaching the member, which answers under the identity the reached host asserts for itself.</param>
        /// <param name="channel">The channel this leg owns, or <see langword="null"/> for the local host's leg.</param>
        /// <param name="governed">The governance decoration this leg owns, or <see langword="null"/> when the leg is undecorated.</param>
        private MemberLeg(
            VersionedRecorderEndpointDelegate<CommittedMetadataRecord> record,
            ReadCommittedRecordDelegate<VeritasMetadataRecord> readCommitted,
            OfferMetadataRecordDelegate offerRecord,
            ObserveMetadataVersionDelegate observeVersion,
            MetadataChannelClient? channel,
            GovernedMetadataExchange? governed)
        {
            Record = record;
            ReadCommitted = readCommitted;
            OfferRecord = offerRecord;
            ObserveVersion = observeVersion;
            Channel = channel;
            Governed = governed;
        }

        /// <summary>The recorder endpoint reaching the member.</summary>
        public VersionedRecorderEndpointDelegate<CommittedMetadataRecord> Record { get; }

        /// <summary>The committed-record read reaching the member.</summary>
        public ReadCommittedRecordDelegate<VeritasMetadataRecord> ReadCommitted { get; }

        /// <summary>The dissemination offer reaching the member.</summary>
        public OfferMetadataRecordDelegate OfferRecord { get; }

        /// <summary>The version probe reaching the member, answered under the identity the reached host asserts for itself.</summary>
        public ObserveMetadataVersionDelegate ObserveVersion { get; }

        /// <summary>The channel this leg owns, or <see langword="null"/> for the local host's leg.</summary>
        private MetadataChannelClient? Channel { get; }

        /// <summary>The governance decoration this leg owns, or <see langword="null"/> when the leg is undecorated.</summary>
        private GovernedMetadataExchange? Governed { get; }

        /// <summary>Builds the leg reaching the member that IS this replica, which the local host serves directly.</summary>
        /// <param name="runner">The loop that owns the local host.</param>
        /// <returns>The local host's leg, which owns nothing.</returns>
        /// <remarks>
        /// The runner's own record path and committed-record read ARE the two consensus seams, and the offer is
        /// its durable learn, so the local member needs no transport and no adapter beyond naming the
        /// durability the push seam states. The probe is the one seam the runner does not carry whole, because
        /// an answer owes the asker an identity beside the version, and the identity it answers with is the
        /// local host's own — read off the host rather than off the member the caller named, exactly as a
        /// remote host's serve loop states its own.
        /// </remarks>
        public static MemberLeg ForLocalHost(QuePaxaVersionedRunner<VeritasMetadataRecord> runner)
        {
            LocalRecordApply apply = new(runner);
            LocalVersionProbe probe = new(runner);

            return new MemberLeg(runner.RecordAsync, runner.ReadCommittedAsync, apply.OfferAsync, probe.ObserveAsync, channel: null, governed: null);
        }

        /// <summary>Builds the leg reaching a member over its own channel.</summary>
        /// <param name="channel">The member's channel; ownership transfers to the leg.</param>
        /// <returns>The member's leg.</returns>
        public static MemberLeg ForChannel(MetadataChannelClient channel)
        {
            return new MemberLeg(channel.RecordAsync, channel.ReadCommittedAsync, channel.PushRecordAsync, channel.ObserveVersionAsync, channel, governed: null);
        }

        /// <summary>Builds the leg reaching a member over its own channel behind the governance gate.</summary>
        /// <param name="channel">The member's channel; ownership transfers to the leg.</param>
        /// <param name="governed">The decoration over that channel, owning the member's peer key; ownership transfers to the leg.</param>
        /// <returns>The member's leg, whose four seams are the decorated ones.</returns>
        public static MemberLeg ForGovernedChannel(MetadataChannelClient channel, GovernedMetadataExchange governed)
        {
            return new MemberLeg(governed.RecordAsync, governed.ReadCommittedAsync, governed.PushRecordAsync, governed.ObserveVersionAsync, channel, governed);
        }

        /// <summary>Tears the member's channel down and releases the peer key its decoration owned.</summary>
        /// <returns>A task that completes once the channel is torn down.</returns>
        /// <remarks>
        /// The channel goes first and the decoration after it, so the pooled key stays valid until the last
        /// call that could have read it is gone.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if(Channel is not null)
            {
                await Channel.DisposeAsync().ConfigureAwait(false);
            }

            Governed?.Dispose();
        }
    }

    /// <summary>
    /// Binds the local host's runner to the dissemination seam as an explicit frame, so the offer captures
    /// nothing.
    /// </summary>
    /// <param name="runner">The loop that owns the local host.</param>
    private sealed class LocalRecordApply(QuePaxaVersionedRunner<VeritasMetadataRecord> runner)
    {
        /// <summary>The loop that owns the local host.</summary>
        private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; } = runner;

        /// <summary>Learns a decided record durably on the local host — an <see cref="OfferMetadataRecordDelegate"/>.</summary>
        /// <param name="committed">The decided record.</param>
        /// <param name="cancellationToken">The caller's token.</param>
        /// <returns>A task that completes once the local host has learned the record durably.</returns>
        /// <remarks>
        /// Whether the record advanced the host is the learn's own answer and not the push seam's: a record the
        /// host already held is as fully offered as one that moved it.
        /// </remarks>
        public async ValueTask OfferAsync(CommittedMetadataRecord committed, CancellationToken cancellationToken)
        {
            _ = await Runner.LearnAsync(committed, LearnDurability.Durable, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Binds the local host's runner to the version probe as an explicit frame, so the probe captures nothing.
    /// </summary>
    /// <param name="runner">The loop that owns the local host.</param>
    private sealed class LocalVersionProbe(QuePaxaVersionedRunner<VeritasMetadataRecord> runner)
    {
        /// <summary>The loop that owns the local host.</summary>
        private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; } = runner;

        /// <summary>Reports what the local host holds — an <see cref="ObserveMetadataVersionDelegate"/>.</summary>
        /// <param name="cancellationToken">The caller's token.</param>
        /// <returns>The local host's report: the version it holds, or <see cref="RegisterVersion.Unwritten"/> when it has learned none, beside its own identity.</returns>
        /// <remarks>
        /// The version is read through the host's own queue, the same seam a catch-up read is served from, so it
        /// is a version the store holds rather than one a crash could take back. The identity is the host's own,
        /// taken off the node the runner owns rather than off the member the caller asked about, which is what
        /// keeps the local answer as honest as a remote one — and it agrees with the member the binding placed
        /// this leg under, because the composition refuses a runner whose host runs as another member before any
        /// leg is built.
        /// </remarks>
        public async ValueTask<MemberVersionReport> ObserveAsync(CancellationToken cancellationToken)
        {
            CommittedMetadataRecord? held = await Runner.ReadCommittedAsync(cancellationToken).ConfigureAwait(false);

            return new MemberVersionReport(Runner.Node.Self, held is null ? RegisterVersion.Unwritten : held.Version);
        }
    }

    /// <summary>
    /// Holds everything one member's transport is built from, so a member placed after composition is built
    /// exactly as a founder was.
    /// </summary>
    /// <param name="resolveConnection">The seam that answers which connection reaches one named member.</param>
    /// <param name="serializeRecordRequest">The codec that writes one consensus record request.</param>
    /// <param name="deserializeRecordReply">The codec that reads a member's record reply back.</param>
    /// <param name="serializeRecord">The codec that writes one decided record.</param>
    /// <param name="deserializeRecord">The codec that reads a decided record back.</param>
    /// <param name="pool">The pool inbound frame payloads are copied into.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes.</param>
    private sealed class MemberChannelFactory(
        ResolvePeerMetadataConnectionDelegate resolveConnection,
        SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serializeRecordRequest,
        DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> deserializeRecordReply,
        SerializeMessageDelegate<CommittedMetadataRecord> serializeRecord,
        DeserializeMessageDelegate<CommittedMetadataRecord> deserializeRecord,
        MemoryPool<byte> pool,
        int maxFrameLength)
    {
        /// <summary>The seam that answers which connection reaches one named member.</summary>
        private ResolvePeerMetadataConnectionDelegate ResolveConnection { get; } = resolveConnection;

        /// <summary>The codec that writes one consensus record request.</summary>
        private SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> SerializeRecordRequest { get; } = serializeRecordRequest;

        /// <summary>The codec that reads a member's record reply back.</summary>
        private DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> DeserializeRecordReply { get; } = deserializeRecordReply;

        /// <summary>The codec that writes one decided record.</summary>
        private SerializeMessageDelegate<CommittedMetadataRecord> SerializeRecord { get; } = serializeRecord;

        /// <summary>The codec that reads a decided record back.</summary>
        private DeserializeMessageDelegate<CommittedMetadataRecord> DeserializeRecord { get; } = deserializeRecord;

        /// <summary>The pool inbound frame payloads are copied into.</summary>
        private MemoryPool<byte> Pool { get; } = pool;

        /// <summary>The largest frame accepted or produced, in bytes.</summary>
        private int MaxFrameLength { get; } = maxFrameLength;

        /// <summary>Looks up the seam that opens one member's connection.</summary>
        /// <param name="member">The member to reach.</param>
        /// <returns>That member's connection seam.</returns>
        public OpenPeerMetadataConnectionDelegate Resolve(ReplicaAxis member)
        {
            //The resolver's contract is that an unplaceable member reports so by raising; a null answer is the
            //resolver breaking that contract, and it is refused here rather than surfacing later as a channel
            //construction fault inside a placement whose earlier legs already own their channels.
            return ResolveConnection(member) ?? throw new InvalidOperationException("A metadata connection resolver answers the seam that opens one member's connection, and an unplaceable member reports so by raising; this resolver answered null, which opens nothing and names no fault.");
        }

        /// <summary>Builds one member's channel over its connection seam. The channel dials on its first call.</summary>
        /// <param name="route">The seam that opens the member's connection.</param>
        /// <returns>The member's channel; ownership transfers to the caller.</returns>
        public MetadataChannelClient Create(OpenPeerMetadataConnectionDelegate route)
        {
            return new MetadataChannelClient(route, SerializeRecordRequest, DeserializeRecordReply, SerializeRecord, DeserializeRecord, Pool, MaxFrameLength);
        }
    }

    /// <summary>
    /// Holds everything one member's governance decoration is built from, so a member placed after composition
    /// is governed exactly as a founder is.
    /// </summary>
    /// <param name="governance">The policy consulted before every exchange with a member.</param>
    /// <param name="context">The opaque access context identifying the local node to the policy, or <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock a delayed exchange backs off against and events are timestamped with.</param>
    /// <param name="trace">The diagnostics sink each governance verdict is emitted to, or <see langword="null"/> to emit nothing.</param>
    /// <param name="correlationId">The correlation id emitted events carry.</param>
    /// <param name="pool">The pool each member's peer key is rented from.</param>
    private sealed class MemberGovernanceFactory(
        NetworkGovernanceDelegate governance,
        AccessContext? context,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace,
        Guid correlationId,
        MemoryPool<byte> pool)
    {
        /// <summary>The policy consulted before every exchange with a member.</summary>
        private NetworkGovernanceDelegate Governance { get; } = governance;

        /// <summary>The opaque access context identifying the local node to the policy, or <see langword="null"/>.</summary>
        private AccessContext? Context { get; } = context;

        /// <summary>The clock a delayed exchange backs off against and events are timestamped with.</summary>
        private TimeProvider TimeProvider { get; } = timeProvider;

        /// <summary>The diagnostics sink each governance verdict is emitted to, or <see langword="null"/> to emit nothing.</summary>
        private TraceHandler<NetworkGovernanceTraceEvent>? Trace { get; } = trace;

        /// <summary>The correlation id emitted events carry.</summary>
        private Guid CorrelationId { get; } = correlationId;

        /// <summary>The pool each member's peer key is rented from.</summary>
        private MemoryPool<byte> Pool { get; } = pool;

        /// <summary>Rents the peer key naming one member to the policy, copying that member's identity bytes into a pooled buffer.</summary>
        /// <param name="member">The member the key names.</param>
        /// <returns>The rented key; ownership transfers to the decoration built over it.</returns>
        public NetworkPeerKey RentPeerKey(ReplicaAxis member)
        {
            return NetworkPeerKey.RentReplicaId(Pool, member.Bytes.Span);
        }

        /// <summary>Decorates one member's channel with the governance gate.</summary>
        /// <param name="channel">The member's channel, whose four operations are governed.</param>
        /// <param name="peer">The key naming that member to the policy; ownership transfers to the decoration.</param>
        /// <returns>The decoration, carrying the same four faces the channel does.</returns>
        public GovernedMetadataExchange Decorate(MetadataChannelClient channel, NetworkPeerKey peer)
        {
            return new GovernedMetadataExchange(
                channel.RecordAsync,
                channel.ReadCommittedAsync,
                channel.PushRecordAsync,
                channel.ObserveVersionAsync,
                Governance,
                peer,
                Context,
                TimeProvider,
                Trace,
                CorrelationId);
        }
    }
}
