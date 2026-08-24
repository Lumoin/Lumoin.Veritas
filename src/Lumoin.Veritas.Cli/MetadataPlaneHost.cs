using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The replicate command's metadata-plane composition root: one durable node store, the consensus host restored
/// from it (or minted fresh), the runner that owns that host, the serve endpoint its fellows reach it through,
/// the transport binding it reaches its fellows through, and the plane every coordination obligation is driven
/// on. It also owns this host's <see cref="ConfirmedMetadataFacts"/> record, so a reopen with nothing left to
/// coordinate costs no round trip.
/// </summary>
/// <remarks>
/// <para>
/// THE STORE LIVES BESIDE THE HOST IDENTITY AND NEVER INSIDE THE DATA STORE. The node state carries this host's
/// consensus identity, its leader belief, the version it serves and the membership it runs under, and the
/// confirmed-facts record is this host's own memory of what consensus already settled — all of it HOST state.
/// A deployment seeds one data store and copies the directory per replica, so a node store inside that directory
/// would clone one member's consensus host into every replica.
/// </para>
/// <para>
/// DURABILITY IS THE REAL ONE. The store is built with no flush or barrier override, so it takes the production
/// per-host flush and directory barrier: a decided control-plane version is on stable storage before the write
/// returns, which is what lets a peer build the next version on it.
/// </para>
/// <para>
/// A MISSING NODE STATE IS A FRESH HOST AND A TORN ONE IS A REFUSAL. An absent file is the value a first start
/// produces; a file that is present but unreadable propagates, and the command answers it the way it answers an
/// unreadable identity file — one line and a non-zero exit — rather than silently minting a fresh host over
/// state a peer may already count.
/// </para>
/// <para>
/// THE PLANE OUTLIVES THE ENGINE. The host disposes its engine first and this second, because the plane drains
/// its queued obligations on disposal and disposing it under an in-flight consultation would abandon that
/// obligation.
/// </para>
/// </remarks>
internal sealed class MetadataPlaneHost: IAsyncDisposable
{
    /// <summary>Composes the plane over a store the caller names, restoring whatever the store holds.</summary>
    /// <param name="deployment">The chain's genesis: the founders and the minted chain identity.</param>
    /// <param name="self">This host's replica identity axis, which is also its consensus identity on the chain.</param>
    /// <param name="store">The durable home of the consensus host state and this host's confirmed facts.</param>
    /// <param name="restored">The host state this replica comes back from, or <see langword="null"/> for a host starting fresh.</param>
    /// <param name="facts">What this host already knows the deployment settled.</param>
    /// <param name="pool">The pool every frame payload, stream pipe, and store buffer is rented from.</param>
    private MetadataPlaneHost(
        MetadataPlaneDeployment deployment,
        ReplicaAxis self,
        StoreIncarnation incarnation,
        MetadataNodeStore store,
        QuePaxaVersionedNodeState<VeritasMetadataRecord>? restored,
        ConfirmedMetadataFacts facts,
        MemoryPool<byte> pool)
    {
        Deployment = deployment;
        SelfAxis = self;
        Store = store;
        Restored = restored is not null;
        Facts = facts;
        ReaderOptions = new StreamPipeReaderOptions(pool, leaveOpen: true);
        WriterOptions = new StreamPipeWriterOptions(pool, leaveOpen: true);
        Routes = new MetadataRouteTable(deployment, pool);

        //A revived host comes back through the consensus host's own restore, which re-derives the leader, the
        //served version and the membership from the restored record and refuses a snapshot whose stored copies
        //disagree; a fresh one starts from the genesis it was deployed with.
        //
        //The identity carries the store this host holds beside the replica it serves under, and the two reach
        //here from different places: the replica is what the operator deployed, and the incarnation is read
        //from the store's own marker. The restore compares that pair against the host its snapshot names, so a
        //store directory belonging to another replica, or a marker replaced under a snapshot, is refused here
        //rather than answering as a member the membership never admitted.
        HostId identity = new(MetadataPlaneDeployment.ReplicaIdFor(self), incarnation);
        Node = restored is null
            ? new QuePaxaVersionedNode<VeritasMetadataRecord>(deployment.Genesis, identity)
            : QuePaxaVersionedNode<VeritasMetadataRecord>.FromState(deployment.Genesis, identity, restored);
        Runner = new QuePaxaVersionedRunner<VeritasMetadataRecord>(Node);
        RunTask = Runner.RunAsync(store.PersistNode, CancellationToken.None);

        Serve = new PlaneServeBinding(Runner);
        Server = new MetadataChannelServer(
            Serve.Provide,
            VeritasMetadataWireCodec.CreateRecordRequestDeserializer(),
            VeritasMetadataWireCodec.CreateRecordReplySerializer(),
            VeritasMetadataWireCodec.CreateDecidedRecordSerializer(),
            VeritasMetadataWireCodec.CreateDecidedRecordDeserializer(),
            pool);

        Binding = MetadataPlaneTransportBinding.Create(
            deployment,
            self,
            Runner,
            Routes.Resolve,
            VeritasMetadataWireCodec.CreateRecordRequestSerializer(),
            VeritasMetadataWireCodec.CreateRecordReplyDeserializer(),
            VeritasMetadataWireCodec.CreateDecidedRecordSerializer(),
            VeritasMetadataWireCodec.CreateDecidedRecordDeserializer(),
            pool);

        Plane = new VeritasMetadataPlane(
            deployment,
            self,
            Node,
            Runner,
            ReplicateWire.MetadataHedgingBaseDelay,
            ReplicateWire.MetadataAttemptsPerRecorder,
            ReplicateWire.MetadataMemberQueryDeadline,
            TimeProvider.System,
            ProposalPriority.Cryptographic,
            Binding.ResolveRecorder,
            Binding.ResolveCommittedReader,
            Binding.ObserveCommittedVersionAsync,
            Binding.ObserveMemberVersionAsync,
            Binding.PublishCommittedRecordAsync,
            PlaneTracePrinter.Print);
    }

    /// <summary>The chain's genesis: the founders in genesis order and the minted chain identity.</summary>
    public MetadataPlaneDeployment Deployment { get; }

    /// <summary>This host's replica identity axis, which is also the consensus identity it writes under.</summary>
    public ReplicaAxis SelfAxis { get; }

    /// <summary>The plane every coordination obligation of this host is driven on.</summary>
    public VeritasMetadataPlane Plane { get; }

    /// <summary>The endpoint map this host reaches its fellow members through; rebound in place by the <c>metadata-route</c> verb.</summary>
    public MetadataRouteTable Routes { get; }

    /// <summary>Whether the node store held state this host came back from, rather than this being a fresh consensus host.</summary>
    public bool Restored { get; }

    /// <summary>The directory the node state and the confirmed-facts record live in.</summary>
    public string StoreDirectory => Store.DirectoryPath;

    /// <summary>What this host already knows the deployment settled; replaced whenever a further fact is saved.</summary>
    public ConfirmedMetadataFacts Facts { get; private set; }

    /// <summary>The durable home of the consensus host state and this host's confirmed facts.</summary>
    private MetadataNodeStore Store { get; }

    /// <summary>This host's consensus host.</summary>
    private QuePaxaVersionedNode<VeritasMetadataRecord> Node { get; }

    /// <summary>The loop that owns this host's consensus host.</summary>
    private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; }

    /// <summary>The runner's loop task.</summary>
    private Task RunTask { get; }

    /// <summary>The local seams one served connection dispatches to.</summary>
    private PlaneServeBinding Serve { get; }

    /// <summary>The endpoint this host's fellows are served through.</summary>
    private MetadataChannelServer Server { get; }

    /// <summary>The transport binding this host's plane reaches its fellows through.</summary>
    private MetadataPlaneTransportBinding Binding { get; }

    /// <summary>The stream-pipe reader options a served connection's read side is created under; its buffers come from the host's governed pool.</summary>
    private StreamPipeReaderOptions ReaderOptions { get; }

    /// <summary>The stream-pipe writer options a served connection's write side is created under; its buffers come from the host's governed pool.</summary>
    private StreamPipeWriterOptions WriterOptions { get; }

    /// <summary>
    /// Composes a plane host over the directory the node state and the confirmed facts live in, creating the
    /// directory when it does not exist. Restoring reads files, so it happens here rather than in a constructor.
    /// </summary>
    /// <param name="deployment">The chain's genesis: the founders and the minted chain identity.</param>
    /// <param name="self">This host's replica identity axis, which must be one of the deployment's founders.</param>
    /// <param name="storeDirectory">The directory the node state and the confirmed-facts record live in.</param>
    /// <param name="pool">The pool every frame payload, stream pipe, and store buffer is rented from.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>The composed plane host, its runner already serving and its plane ready for obligations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="deployment"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="storeDirectory"/> is empty or white space, or <paramref name="self"/> names no founder of the deployment.</exception>
    /// <exception cref="InvalidDataException">The stored node state or the stored confirmed-facts record could not be read back.</exception>
    /// <exception cref="MessageDeserializationException">The stored node state could not be decoded.</exception>
    public static async Task<MetadataPlaneHost> CreateAsync(MetadataPlaneDeployment deployment, ReplicaAxis self, string storeDirectory, MemoryPool<byte> pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeDirectory);

        MetadataNodeStore store = new(
            storeDirectory,
            pool,
            VeritasMetadataWireCodec.CreateNodeStateSerializer(),
            VeritasMetadataWireCodec.CreateNodeStateDeserializer());
        //The store's own incarnation before anything read out of it: a store that has one answers under it,
        //and one that has none mints it here, which is the moment a store becomes a store this deployment can
        //admit.
        StoreIncarnation incarnation = await store.EnsureIncarnationAsync(cancellationToken).ConfigureAwait(false);
        QuePaxaVersionedNodeState<VeritasMetadataRecord>? restored = await store.TryLoadAsync(cancellationToken).ConfigureAwait(false);
        ConfirmedMetadataFacts facts = await ConfirmedMetadataFacts.TryLoadAsync(store, cancellationToken).ConfigureAwait(false) ?? ConfirmedMetadataFacts.Unconfirmed;

        return new MetadataPlaneHost(deployment, self, incarnation, store, restored, facts, pool);
    }

    /// <summary>The version this host has learned, or <see langword="null"/> when it has learned none.</summary>
    /// <returns>The learned version, or <see langword="null"/>.</returns>
    public RegisterVersion? LearnedVersion()
    {
        return Plane.HostCommitted?.Version;
    }

    /// <summary>Serves one accepted metadata connection until the peer ends it. One connection carries MANY correlated calls, so this serve is long-lived by design.</summary>
    /// <param name="stream">The accepted connection's stream; its owner closes it.</param>
    /// <param name="cancellationToken">Stops the serve at host shutdown.</param>
    /// <returns>A task that completes when the connection's calls end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public Task ServeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return Server.ServeAsync(PipeReader.Create(stream, ReaderOptions), PipeWriter.Create(stream, WriterOptions), cancellationToken);
    }

    /// <summary>
    /// Records durably that consensus confirmed this host's identity claim, and — reading the coordinated record
    /// back — the confirmed lineage baseline it carries. Nothing is written here that consensus has not already
    /// decided, so the record can only let a later reopen SKIP a round trip and can never take a decision the
    /// deployment did not take.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read-back and the save.</param>
    /// <returns>A task that completes once the record is durable.</returns>
    public async ValueTask SaveConfirmedFactsAsync(CancellationToken cancellationToken)
    {
        ConfirmedMetadataFacts updated = Facts.WithIdentityClaimConfirmed();
        VersionedValue<VeritasMetadataRecord>? committed = await Plane.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
        if(committed?.Value.Baseline is { Confirmation: { } confirmation } baseline)
        {
            updated = updated.WithConfirmedBaseline(baseline.CausalityDigest, confirmation.StateId, confirmation.DictionaryEpoch);
        }

        Facts = updated;
        await updated.SaveAsync(Store, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tears the plane host down: the plane first, so its queued obligations drain; then the channels to its fellows; then the runner that owns the consensus host. Each stage is guarded on its own, because a teardown failure must never replace the command's own exit code.</summary>
    /// <returns>A task that completes once nothing this host started is still running.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Teardown must never raise over the command's own exit code, and each stage is guarded on its own so a stage that fails still leaves the later ones to run; every state this tears down is process-local and already unreachable by the time it runs.")]
    public async ValueTask DisposeAsync()
    {
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
            Runner.Complete();
            await RunTask.ConfigureAwait(false);
        }
        catch(Exception)
        {
            //The loop ends when its queue drains; a loop that already ended reports its own end here.
        }
    }

    /// <summary>
    /// The local seams one served metadata connection dispatches to: the consensus host's recorder, its
    /// committed-record read, and its durable learn of a disseminated record, beside the identity that host
    /// answers a version probe under.
    /// </summary>
    /// <param name="runner">The loop that owns this host's consensus host.</param>
    /// <remarks>
    /// The probe identity is read off the HOST rather than off the frame that asked, so a probe served here
    /// carries the answering host's own claim — which is what the asking register's refusal of a foreign answer
    /// is compared against.
    /// </remarks>
    private sealed class PlaneServeBinding(QuePaxaVersionedRunner<VeritasMetadataRecord> runner)
    {
        /// <summary>The loop that owns this host's consensus host.</summary>
        private QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; } = runner;

        /// <summary>Hands one serve the host's three seams and the identity it answers a version probe under — a <see cref="ProvideMetadataServeBindingDelegate"/>.</summary>
        /// <returns>The binding one serve dispatches through.</returns>
        public MetadataServeBinding Provide()
        {
            return new MetadataServeBinding(Runner.Node.Self, Runner.RecordAsync, Runner.ReadCommittedAsync, OfferAsync);
        }

        /// <summary>Learns one disseminated record DURABLY on this host — an <see cref="OfferMetadataRecordDelegate"/>.</summary>
        /// <param name="committed">The decided record a fellow pushed.</param>
        /// <param name="cancellationToken">Cancels the learn.</param>
        /// <returns>A task that completes once the host has learned the record durably.</returns>
        public async ValueTask OfferAsync(VersionedValue<VeritasMetadataRecord> committed, CancellationToken cancellationToken)
        {
            //Whether the record advanced the host is the learn's own answer and not the offer's: a record the
            //host already held is as fully offered as one that moved it.
            _ = await Runner.LearnAsync(committed, LearnDurability.Durable, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Prints each completed coordination obligation's verdict as one operational line, so an operator watching this process sees what the plane decided beside the open or the verb that drove it.</summary>
    private static class PlaneTracePrinter
    {
        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public static void Print(in MetadataPlaneTraceEvent evt)
        {
            Console.WriteLine(FormattableString.Invariant($"planetrace obligation={evt.Obligation} outcome={evt.OutcomeCode} version={evt.Version.Value} attempts={evt.Attempts}"));
        }
    }
}
