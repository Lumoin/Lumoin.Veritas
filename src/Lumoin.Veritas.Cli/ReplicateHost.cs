using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Cli;

/// <summary>The <c>replicate</c> command's parsed configuration.</summary>
/// <param name="StoreDirectory">The store directory the replica opens; created when missing.</param>
/// <param name="DataPaths">The documents seeding an EMPTY store as the lineage seed; empty seeds nothing.</param>
/// <param name="ListenPort">The loopback port to serve replication on, 0 for an ephemeral port, or <see langword="null"/> to run without a listener.</param>
/// <param name="Peer">The initial peer endpoint as <c>host:port</c>, or <see langword="null"/> to bind one later with the <c>peer</c> verb.</param>
/// <param name="ReconcileIntervalSeconds">Seconds between automatic reconcile pulls, or <see langword="null"/> to pull only on the <c>reconcile</c> verb.</param>
/// <param name="SelfHeal">Whether the background storage self-heal loop runs, with both peer-repair provider seams over the bound peer.</param>
/// <param name="HealIntervalSeconds">Seconds between self-heal rounds as a fixed jitter-free cadence, or <see langword="null"/> for the reliability-model default.</param>
/// <param name="IdentityDirectory">The directory the HOST's replica identity is persisted in — deliberately outside the store directory, so copying a store to seed a peer cannot copy who the replica is; <see langword="null"/> uses the host's per-user configuration location. Distinct replicas on one machine each need their own directory.</param>
/// <param name="Baseline">Whether the open performs the explicit causality baseline step on a resumed store that is not already remove-aware; the outcome is reported by name and a refusal never stops the host.</param>
/// <param name="MetadataFounders">The consensus metadata chain's founding members as <c>&lt;axis-hex&gt;:&lt;store-hex&gt;</c>, one per founder; empty runs the host planeless. Its presence is what turns the plane on, and this host's own axis must be among them. Both halves are needed because a membership admits the store answering for a replica, so each founder's store must exist and have printed its incarnation before the list can be written.</param>
/// <param name="MetadataRoutes">The metadata endpoint map as <c>&lt;64hex&gt;=&lt;host:port&gt;</c> pairs bound at start; a founder with no route is still placed and dialing it reports an unreachable member. Further routes bind with the <c>metadata-route</c> verb.</param>
/// <param name="MetadataStoreDirectory">The directory the consensus host state and this host's confirmed facts live in, or <see langword="null"/> for a <c>metadata</c> directory beside the replica identity — HOST state, deliberately outside the data store directory a deployment copies per replica.</param>
/// <param name="MetadataAttempts">How many consensus attempts one coordination obligation may spend before it answers undecided, or <see langword="null"/> for the command's own budget.</param>
internal sealed record ReplicateSettings(
    string StoreDirectory,
    IReadOnlyList<string> DataPaths,
    int? ListenPort,
    string? Peer,
    int? ReconcileIntervalSeconds,
    bool SelfHeal,
    int? HealIntervalSeconds,
    string? IdentityDirectory,
    bool Baseline,
    IReadOnlyList<string> MetadataFounders,
    IReadOnlyList<string> MetadataRoutes,
    string? MetadataStoreDirectory,
    int? MetadataAttempts);

/// <summary>
/// The <c>replicate</c> command's host: one store-backed REMOVE-AWARE mutable engine serving replication to
/// loopback peers and driving reconciles and repairs against one bound peer. It runs under a per-host replica
/// identity (minted on first use, persisted outside the store directory) with a durable dataset journal inside
/// the store directory, so the dotted lane's causal history is crash-durable. It listens on a loopback TCP
/// port, answering each accepted connection's service-selector byte with the one-byte service verdict and
/// routing to the sketch, shard-difference, dotted-difference, or consensus metadata serve — an unknown selector
/// is answered with the named refusal byte before the close, and an engine-backed selector dialed before the
/// open finished is answered with the named not-ready byte; it reads verbs from standard input (<c>ingest</c>,
/// <c>update</c>, <c>reconcile</c>, <c>reconcile-addonly</c>, <c>peer</c>, <c>status</c>, <c>fingerprint</c>,
/// <c>metadata-route</c>, <c>metadata-claim</c>, <c>metadata-status</c>, <c>quit</c>), answering each with one
/// machine-parseable output line; and with self-heal enabled it wires both peer-repair provider seams over the
/// bound peer, so a damaged store generation heals over the wire. Standard output is this command's operational
/// surface, like the serve command's.
/// </summary>
/// <remarks>
/// <para>
/// The listener binds LOOPBACK ONLY: this command replicates between processes on one host; a governed
/// connection factory and a production bind policy are the deployment surface's own concern. Replication is
/// structural and same-lineage — a deployment seeds one store with <c>--data</c>, copies the directory, and
/// starts each replica over its copy, so the replicas share the dictionary and its epoch; the wire's epoch
/// stamps refuse a cross-lineage peer by name. Writes stay dictionary-stable (new triples over already shared
/// terms) for replicas to converge; the <c>status</c> verb's term count is the runtime check of that posture.
/// </para>
/// <para>
/// WITH <c>--metadata-founder</c> THE HOST ALSO COMPOSES THE CONSENSUS METADATA PLANE, and that decides the
/// start-up order: the engine's identity claim and its two-phase lineage baseline are consulted INSIDE the open,
/// so the plane, its runner, its serve endpoint and the listener are all alive before the store-backed open. The
/// plane is never a liveness dependency of the data lane — an undecided consultation fails open and the host
/// serves — and only a definite adverse answer (the identity held by another minter, a lineage already
/// descending from a different baseline) refuses the open, loudly and with a non-zero exit.
/// </para>
/// </remarks>
internal sealed class ReplicateHost: IDisposable
{
    /// <summary>The parsed command configuration.</summary>
    private readonly ReplicateSettings settings;

    /// <summary>The governed pool every channel, codec, and serve on this host rents from; owned and disposed by the host.</summary>
    private readonly VeritasMemoryPool<byte> pool = new();

    /// <summary>The shard policy this replica drives and declares on the sharded repair wire; both replicas run this executable, so the policies agree by construction.</summary>
    private readonly PrefixShardPolicy policy = new(ReplicateWire.ShardBits, ShardKeyMixing.Avalanche);

    /// <summary>The open engine the three engine-backed serves read, or <see langword="null"/> while the open has not finished. A naked volatile field because the accept loop is already serving connections when the open publishes it.</summary>
    private volatile VeritasEngine? servedEngine;

    /// <summary>The shard-difference server every routed connection is served by, or <see langword="null"/> while the open has not finished. A naked volatile field for the same reason as <see cref="servedEngine"/>.</summary>
    private volatile ShardDifferenceChannelServer? servedShardServer;

    /// <summary>The late-bound peer seam every outbound surface dials through.</summary>
    private ReplicationPeerBinding Binding { get; }

    /// <summary>Creates the host over its parsed configuration.</summary>
    /// <param name="settings">The parsed command configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    public ReplicateHost(ReplicateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.settings = settings;
        Binding = new ReplicationPeerBinding(policy, pool, ShardFaultPrinter.Print);
    }

    /// <summary>Builds the seed open's configuration off the application's shared <see cref="VeritasOperations.EngineOptions"/> base (the Geo extension-function and value-datatype surface rides every engine this application opens): no reasoning machinery and no semantic query rewrite — replication describes the asserted graph, so no derived matching enters this host's pipelines — no store binding (the seed engine persists once into the store and closes), and the host identity, so the seeded generation is remove-aware from birth: the seed's Initial entry IS its baseline, and the persisted generation carries the causality artifact every same-lineage copy recovers from.</summary>
    /// <param name="identity">The host replica identity the seed's creation baseline mints on.</param>
    /// <returns>The seed engine options.</returns>
    private static VeritasEngineOptions BuildSeedEngineOptions(ReplicaAxis identity)
    {
        return VeritasOperations.EngineOptions with { Reasoning = null, SparqlExecution = SparqlEnginePolicy.Default, ReplicaIdentity = identity };
    }

    /// <summary>Runs the replica until <c>quit</c>, end of input, or cancellation.</summary>
    /// <param name="cancellationToken">Stops the host.</param>
    /// <returns>The process exit code.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The listener is disposed unconditionally in the teardown's nested finally, after the accept and reconcile loops have joined on every path; the analyzer does not model the await-bearing finally that reaches the dispose.")]
    [SuppressMessage("Reliability", "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed", Justification = "The teardown cancels the loops, unblocks the pending accept with Stop, and JOINS both loop tasks before the listener's dispose in the nested finally; the analyzer does not model the await-bearing finally that orders the joins ahead of the dispose.")]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        bool storeHasArtifacts = Directory.Exists(settings.StoreDirectory) && Directory.EnumerateFileSystemEntries(settings.StoreDirectory).Any();
        if(settings.DataPaths.Count > 0 && storeHasArtifacts)
        {
            await Console.Error.WriteLineAsync("--data seeds an empty store only; the store directory already holds artifacts.").ConfigureAwait(false);

            return 1;
        }

        string identityDirectory = ResolveIdentityDirectory(settings.IdentityDirectory);
        ReplicaAxis identity;
        try
        {
            identity = LoadOrMintIdentity(identityDirectory);
        }
        catch(InvalidDataException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);

            return 1;
        }

        //The full-width axis and this host's store before anything else: an operator starts each host once to
        //read this line, then starts them all with the founder list built from the lines they printed. Both
        //halves are needed, because a membership admits the store answering for a replica and not the replica
        //alone; the store mints its incarnation here, on the run that reads it.
        StoreIncarnation metadataStore = await EnsureMetadataStoreIncarnationAsync(MetadataStoreDirectoryFor(identityDirectory), cancellationToken).ConfigureAwait(false);

        await Console.Out.WriteLineAsync(FormattableString.Invariant($"axis {Convert.ToHexStringLower(identity.Bytes.Span)} store {Convert.ToHexStringLower(metadataStore.AsSpan())}")).ConfigureAwait(false);

        FileSystemPersistenceStore store = new(settings.StoreDirectory);
        VeritasEngine? engine = null;
        MetadataPlaneHost? planeHost = null;
        try
        {
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            TcpListener? listener = null;
            Task? acceptLoop = null;
            Task? reconcileLoop = null;
            try
            {
                if(settings.MetadataFounders.Count > 0)
                {
                    if(!TryParseFounders(settings.MetadataFounders, identity, out ImmutableArray<MetadataFounder> founders, out string founderError))
                    {
                        await Console.Error.WriteLineAsync(founderError).ConfigureAwait(false);

                        return 1;
                    }

                    //Canonical order, so operators who agree on a SET of founders — in any order — mint one
                    //chain; the bootstrap leader is then the byte-smallest founder rather than a listing accident.
                    MetadataPlaneDeployment deployment = MetadataPlaneDeployment.CreateCanonical(founders);
                    try
                    {
                        planeHost = await MetadataPlaneHost.CreateAsync(deployment, identity, MetadataStoreDirectoryFor(identityDirectory), pool, cancellationToken).ConfigureAwait(false);
                    }
                    catch(InvalidDataException exception)
                    {
                        await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);

                        return 1;
                    }
                    catch(MessageDeserializationException exception)
                    {
                        await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);

                        return 1;
                    }
                    catch(StateRestoreException exception)
                    {
                        //A store this host cannot start under is an operator condition and never this host's
                        //defect: a store attached to the wrong identity directory, one restored under a founder
                        //list naming a different chain, or one whose marker and node state disagree. It reads
                        //as the refusal it is rather than as an unhandled fault.
                        await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);

                        return 1;
                    }

                    await WritePlaneLineAsync(planeHost).ConfigureAwait(false);
                    if(planeHost.Facts.AllowsRoutineReopen)
                    {
                        //Named, because the identity line's standing would otherwise read the same as a host that
                        //has no plane at all: this open skipped the consultation rather than never having one.
                        await Console.Out.WriteLineAsync("plane coordination=skipped reason=RoutineReopen").ConfigureAwait(false);
                    }

                    foreach(string route in settings.MetadataRoutes)
                    {
                        await BindMetadataRouteAsync(planeHost, route).ConfigureAwait(false);
                    }
                }

                if(settings.ListenPort is int listenPort)
                {
                    listener = new TcpListener(IPAddress.Loopback, listenPort);
                    listener.Start();
                    int resolvedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                    await Console.Out.WriteLineAsync(FormattableString.Invariant($"listening {resolvedPort}")).ConfigureAwait(false);
                    acceptLoop = AcceptLoopAsync(listener, planeHost, stop.Token);
                }

                if(planeHost is not null)
                {
                    //Every founder may bootstrap: the proposals are identical values, so the race resolves
                    //without anyone's state being lost, and a chain nobody bootstrapped still takes claims.
                    MetadataPlaneResult<PlaneBootstrapOutcome> bootstrapped = await planeHost.Plane.BootstrapAsync(AttemptBudget, cancellationToken).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane bootstrap={bootstrapped.Outcome} version={bootstrapped.Version.Value}")).ConfigureAwait(false);
                    await CatchUpAsync(planeHost, cancellationToken).ConfigureAwait(false);
                }

                if(settings.DataPaths.Count > 0)
                {
                    DurableSystemOfRecordCommit seeded;
                    try
                    {
                        seeded = await SeedStoreAsync(store, identity, cancellationToken).ConfigureAwait(false);
                    }
                    catch(DataDocumentException exception)
                    {
                        await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);

                        return 1;
                    }

                    await Console.Out.WriteLineAsync(FormattableString.Invariant($"seeded generation={seeded.Generation} triples={seeded.TripleCount} terms={seeded.TermCount}")).ConfigureAwait(false);
                }

                try
                {
                    engine = await VeritasEngine.OpenMutableAsync(store, BuildEngineOptions(identity, CoordinationSeamsFor(planeHost)), cancellationToken).ConfigureAwait(false);
                }
                catch(InvalidOperationException exception)
                {
                    //The definite adverse coordination answers: this identity is held by another minter, or the
                    //lineage already descends from a different baseline. Both are correctness rather than
                    //liveness, so the host never starts and says why.
                    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);

                    return 1;
                }

                Binding.Engine = engine;
                servedEngine = engine;
                await Console.Out.WriteLineAsync(FormattableString.Invariant($"identity {IdentityHexPrefix(identity)} state={engine.ReadReplicationStatus().CausalityState} baseline={engine.ReplicationBaseline} coordination={engine.MetadataCoordination}")).ConfigureAwait(false);
                if(planeHost is not null && engine.MetadataCoordination == MetadataCoordinationStanding.Confirmed)
                {
                    await planeHost.SaveConfirmedFactsAsync(cancellationToken).ConfigureAwait(false);
                }

                ProvideShardServeSnapshotDelegate serveSnapshot = engine.CreateShardServeSnapshotProvider();
                ShardDifferenceChannelServer shardServer = new(policy, serveSnapshot, engine.Dictionary.Epoch, pool);
                servedShardServer = shardServer;

                if(settings.Peer is string initialPeer)
                {
                    if(!TryParsePeer(initialPeer, out string host, out int port))
                    {
                        await Console.Error.WriteLineAsync(FormattableString.Invariant($"invalid --peer '{initialPeer}'; expected host:port.")).ConfigureAwait(false);

                        return 1;
                    }

                    Binding.SetPeer(host, port);
                }

                if(settings.ReconcileIntervalSeconds is int reconcileSeconds && reconcileSeconds > 0)
                {
                    reconcileLoop = ReconcileLoopAsync(engine, TimeSpan.FromSeconds(reconcileSeconds), stop.Token);
                }

                return await RunVerbLoopAsync(engine, store, serveSnapshot, planeHost, identity, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                //Stop unblocks a pending accept and the cancellation stops the loops; the listener is disposed
                //only after both loops have joined — in the nested finally, so it is disposed even when a join
                //surfaces a genuine loop fault — and no loop ever touches a disposed listener.
                await stop.CancelAsync().ConfigureAwait(false);
                listener?.Stop();
                try
                {
                    await JoinQuietlyAsync(acceptLoop).ConfigureAwait(false);
                    await JoinQuietlyAsync(reconcileLoop).ConfigureAwait(false);
                }
                finally
                {
                    listener?.Dispose();
                }
            }
        }
        finally
        {
            //The engine goes first and the plane after it: the plane drains its queued obligations on disposal,
            //and disposing it under a consultation the open or a verb still holds would abandon that obligation.
            if(engine is not null)
            {
                await engine.DisposeAsync().ConfigureAwait(false);
            }

            if(planeHost is not null)
            {
                await planeHost.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>How many consensus attempts one coordination obligation of this host may spend: the operator's <c>--metadata-attempts</c>, else the command's own budget.</summary>
    private int AttemptBudget => settings.MetadataAttempts is int attempts && attempts >= 1 ? attempts : ReplicateWire.MetadataAttemptBudget;

    /// <summary>Returns the host's governed pool; the engine and background loops were already torn down by <see cref="RunAsync"/>'s own completion.</summary>
    public void Dispose()
    {
        pool.Dispose();
    }

    /// <summary>Seeds the empty store — the lineage seed: parse the documents into a fresh mutable engine opened WITH the host identity (so the seed's Initial entry is its creation baseline and the persisted generation carries the causality artifact), persist its one generation into the store, and let the store-backed reopen serve it. A deployment seeds one store, copies the directory, and starts each replica over its copy under its OWN host identity, so the replicas share the dictionary, its epoch, and the seed's causal baseline.</summary>
    /// <param name="store">The empty store the seed generation is persisted into.</param>
    /// <param name="identity">The host replica identity the seed's creation baseline mints on.</param>
    /// <param name="cancellationToken">Aborts the seed.</param>
    /// <returns>The persist receipt.</returns>
    private async Task<DurableSystemOfRecordCommit> SeedStoreAsync(FileSystemPersistenceStore store, ReplicaAxis identity, CancellationToken cancellationToken)
    {
        VeritasEngine seeded = await VeritasEngine.OpenMutableAsync(VeritasOperations.StreamQuadsAsync(settings.DataPaths, cancellationToken), BuildSeedEngineOptions(identity), cancellationToken).ConfigureAwait(false);
        try
        {
            return seeded.Persist(store);
        }
        finally
        {
            await seeded.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Builds the store-backed open's configuration off the application's shared <see cref="VeritasOperations.EngineOptions"/> base (the Geo extension-function and value-datatype surface rides every engine this application opens): no reasoning machinery and no semantic query rewrite — replication describes the asserted graph, so no derived matching enters this host's query or update pipelines — the host identity and the durable dataset journal INSIDE the store directory — causal history is store state and travels with a copy, while identity is host state and never does — the explicit baseline step when <c>--baseline</c> asked for it, the self-heal policy when enabled, and the storage trace printed as operational lines.</summary>
    /// <param name="identity">The host replica identity this open mints causal dots on.</param>
    /// <param name="coordination">The metadata-plane seams the open's identity claim and two-phase lineage baseline are consulted through, or <see langword="null"/> to open planeless.</param>
    /// <returns>The engine options.</returns>
    private VeritasEngineOptions BuildEngineOptions(ReplicaAxis identity, MetadataCoordinationSeams? coordination)
    {
        SelfHealOptions? selfHeal = null;
        if(settings.SelfHeal)
        {
            selfHeal = new SelfHealOptions
            {
                ProvidePeerSource = Binding.ProvideSingleBlockPeerSourceAsync,
                ProvideShardedPeerSource = Binding.ProvideShardedPeerSourceAsync,
            };
            if(settings.HealIntervalSeconds is int healSeconds)
            {
                selfHeal = selfHeal with
                {
                    CadenceEstimator = new FixedCadence(TimeSpan.FromSeconds(healSeconds)).Estimate,
                    JitterFraction = 0.0,
                };
            }
        }

        return VeritasOperations.EngineOptions with
        {
            Reasoning = null,
            SparqlExecution = SparqlEnginePolicy.Default,
            ReplicaIdentity = identity,
            BaselineReplicationCausality = settings.Baseline,
            DatasetJournalPath = Path.Combine(settings.StoreDirectory, "dataset.journal"),
            SelfHeal = selfHeal,
            StorageTrace = StorageTracePrinter.Print,
            MetadataCoordination = coordination
        };
    }

    /// <summary>
    /// The coordination seams this open is consulted through: the plane's three, bound to ONE plane instance and
    /// one attempt budget — or none at all when the host runs planeless, and none when this host's confirmed
    /// facts say its identity claim and its lineage baseline are both settled, because a reopen with nothing left
    /// to coordinate has nothing to ask. The cached record only reports facts consensus already decided, so
    /// consulting it can skip a round trip and can never take a decision the deployment did not take.
    /// </summary>
    /// <param name="planeHost">The composed plane host, or <see langword="null"/> when the host runs planeless.</param>
    /// <returns>The seams, or <see langword="null"/> to open planeless.</returns>
    private MetadataCoordinationSeams? CoordinationSeamsFor(MetadataPlaneHost? planeHost)
    {
        if(planeHost is null || planeHost.Facts.AllowsRoutineReopen)
        {
            return null;
        }

        return new MetadataPlaneCoordinationBinding(planeHost.Plane, AttemptBudget).Seams;
    }

    /// <summary>
    /// Parses and validates the founder list: every token a well-formed identity axis, no founder listed twice,
    /// and THIS host's own axis among them. The last check is refused here rather than left to the transport
    /// binding, because a host absent from the chain's membership decides nothing it proposes and would start
    /// looking coordinated while every obligation answered that it stands outside the configuration.
    /// </summary>
    /// <param name="tokens">The <c>--metadata-founder</c> values.</param>
    /// <param name="identity">This host's replica identity axis.</param>
    /// <param name="founders">The parsed founders on success.</param>
    /// <param name="error">The named refusal on failure.</param>
    /// <returns>Whether the founder list parsed and named this host.</returns>
    private static bool TryParseFounders(IReadOnlyList<string> tokens, ReplicaAxis identity, out ImmutableArray<MetadataFounder> founders, out string error)
    {
        founders = [];
        error = string.Empty;
        List<MetadataFounder> parsed = new(tokens.Count);
        Span<byte> axisBytes = stackalloc byte[ReplicaAxis.ByteWidth];
        Span<byte> storeBytes = stackalloc byte[StoreIncarnation.Size];
        for(int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];

            //The axis is fixed-width hex and carries no colon, so the pair splits on the FIRST one and each
            //half is then a whole hex value rather than a prefix of one.
            int split = token.IndexOf(':', StringComparison.Ordinal);
            if(split < 0
                || !TryParseAxisHex(token.AsSpan(0, split), axisBytes)
                || !TryParseHex(token.AsSpan(split + 1), storeBytes))
            {
                error = FormattableString.Invariant($"invalid --metadata-founder '{token}'; expected {ReplicaAxis.ByteWidth * 2} hex characters, a colon, and {StoreIncarnation.Size * 2} hex characters, which is what the 'axis ... store ...' startup line prints.");

                return false;
            }

            MetadataFounder founder = new(new ReplicaAxis(axisBytes.ToArray()), StoreIncarnation.FromSpan(storeBytes));
            for(int j = 0; j < parsed.Count; j++)
            {
                //Refused over the AXIS and not over the pair: two stores listed under one axis is exactly the
                //duplicate a quorum counted over replicas cannot see.
                if(parsed[j].Axis.Equals(founder.Axis))
                {
                    error = FormattableString.Invariant($"--metadata-founder lists '{token}' twice; a replica listed twice, under one store or under two, would answer twice and count twice, and a decision would be taken by fewer replicas than the arithmetic claims.");

                    return false;
                }
            }

            parsed.Add(founder);
        }

        if(parsed.Count == 0)
        {
            error = "--metadata-founder names at least one founder; a chain with no members can neither decide nor be reconfigured into existence.";

            return false;
        }

        bool namesSelf = false;
        for(int i = 0; i < parsed.Count; i++)
        {
            if(parsed[i].Axis.Equals(identity))
            {
                namesSelf = true;

                break;
            }
        }

        if(!namesSelf)
        {
            error = FormattableString.Invariant($"--metadata-founder does not name this host's own axis {Convert.ToHexStringLower(identity.Bytes.Span)}; a host outside the chain's membership decides nothing it proposes.");

            return false;
        }

        founders = [.. parsed];

        return true;
    }

    /// <summary>
    /// This host's metadata store incarnation: read back when the store already holds one, and minted, written
    /// durably and returned when it does not. It is what the startup line prints beside the axis, because a
    /// founder list names the store admitted for a replica and cannot be written before that store exists.
    /// </summary>
    /// <param name="storeDirectory">The metadata store directory.</param>
    /// <param name="cancellationToken">The token that cancels the read or the write.</param>
    /// <returns>The incarnation this host's metadata store answers under.</returns>
    private async Task<StoreIncarnation> EnsureMetadataStoreIncarnationAsync(string storeDirectory, CancellationToken cancellationToken)
    {
        MetadataNodeStore store = new(
            storeDirectory,
            pool,
            VeritasMetadataWireCodec.CreateNodeStateSerializer(),
            VeritasMetadataWireCodec.CreateNodeStateDeserializer());

        return await store.EnsureIncarnationAsync(cancellationToken).ConfigureAwait(false);
    }


    /// <summary>The directory the consensus host state and this host's confirmed facts live in: the operator's <c>--metadata-store</c>, else a <c>metadata</c> directory beside the replica identity — never inside the data store directory a deployment copies per replica.</summary>
    /// <param name="identityDirectory">The resolved replica-identity directory.</param>
    /// <returns>The metadata store directory.</returns>
    private string MetadataStoreDirectoryFor(string identityDirectory)
    {
        return settings.MetadataStoreDirectory is { Length: > 0 } configured ? configured : Path.Combine(identityDirectory, "metadata");
    }

    /// <summary>Writes the composed plane's one startup line: the chain it runs on, this host on it, the derived quorum, whether the consensus host came back from its store and at what version, and where that store is.</summary>
    /// <param name="planeHost">The composed plane host.</param>
    /// <returns>A task that completes when the line is written.</returns>
    private static async Task WritePlaneLineAsync(MetadataPlaneHost planeHost)
    {
        int founders = planeHost.Deployment.Founders.Length;
        int quorum = (founders / 2) + 1;
        string version = planeHost.LearnedVersion() is RegisterVersion learned
            ? learned.Value.ToString(CultureInfo.InvariantCulture)
            : "unwritten";
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane chain={Convert.ToHexStringLower(planeHost.Deployment.Cluster.AsSpan())} self={IdentityHexPrefix(planeHost.SelfAxis)} founders={founders} quorum={quorum} restored={planeHost.Restored} version={version} store={planeHost.StoreDirectory}")).ConfigureAwait(false);
    }

    /// <summary>
    /// Catches this host up to the version the chain has already decided and writes the version it reached: one
    /// read over the fellows, which takes no quorum and no consensus step because a committed record is a decided
    /// fact and one honest holder settles it, and then a DURABLE learn of whatever it found onto this host's own
    /// consensus host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH HALVES ARE LOAD-BEARING, and a late starter needs them for different reasons. The read advances the
    /// register, so an obligation proposes at the version the chain is actually at rather than at the one this
    /// host happened to know — a proposal the recorders would supersede, which reads as undecided when the chain
    /// was reachable all along. The learn advances the local HOST, which is one of the recorders every one of
    /// this host's own writes counts: a host that has learned nothing serves only the chain's first instance and
    /// refuses every later one, so its own quorum is short by exactly itself.
    /// </para>
    /// <para>
    /// It stands ahead of the engine's open and ahead of the claim verb, which is where a late starter's
    /// coordination would otherwise fail open for a reason that is not ignorance.
    /// </para>
    /// </remarks>
    /// <param name="planeHost">The composed plane host.</param>
    /// <param name="cancellationToken">Cancels the catch-up.</param>
    /// <returns>A task that completes when the catch-up line is written.</returns>
    private static async Task CatchUpAsync(MetadataPlaneHost planeHost, CancellationToken cancellationToken)
    {
        VersionedValue<VeritasMetadataRecord>? caughtUp = await planeHost.Plane.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
        string version = "unwritten";
        if(caughtUp is VersionedValue<VeritasMetadataRecord> committed)
        {
            _ = await planeHost.Plane.ApplyDisseminatedRecordAsync(committed, cancellationToken).ConfigureAwait(false);
            version = committed.Version.Value.ToString(CultureInfo.InvariantCulture);
        }

        await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane catchup version={version}")).ConfigureAwait(false);
    }

    /// <summary>Binds one <c>&lt;64hex&gt;=&lt;host:port&gt;</c> metadata route and writes its acknowledgement line. A malformed pair, or one naming no founder, is named on its own line and never stops the host — routing is per-host wiring that changes on a restart, while the founder list is the chain's identity.</summary>
    /// <param name="planeHost">The composed plane host whose endpoint map is bound.</param>
    /// <param name="pair">The route argument.</param>
    /// <returns>A task that completes when the line is written.</returns>
    private static async Task BindMetadataRouteAsync(MetadataPlaneHost planeHost, string pair)
    {
        if(!TryParseMetadataRoute(pair, out ReplicaAxis member, out string host, out int port) || !planeHost.Routes.TryRebind(member, host, port))
        {
            await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane route error {pair}")).ConfigureAwait(false);

            return;
        }

        await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane route ok {IdentityHexPrefix(member)} {host}:{port}")).ConfigureAwait(false);
    }

    /// <summary>Parses one <c>&lt;64hex&gt;=&lt;host:port&gt;</c> metadata route argument. The identity axis is fixed-width hex and carries no equals sign, so the pair splits on the FIRST one and the address keeps whatever the rest holds.</summary>
    /// <param name="pair">The route argument.</param>
    /// <param name="member">The parsed member on success.</param>
    /// <param name="host">The parsed host on success.</param>
    /// <param name="port">The parsed port on success.</param>
    /// <returns>Whether the argument parsed.</returns>
    private static bool TryParseMetadataRoute(string pair, out ReplicaAxis member, out string host, out int port)
    {
        member = default;
        host = string.Empty;
        port = 0;
        int equals = pair.IndexOf('=', StringComparison.Ordinal);
        if(equals != ReplicaAxis.ByteWidth * 2 || equals == pair.Length - 1)
        {
            return false;
        }

        Span<byte> axisBytes = stackalloc byte[ReplicaAxis.ByteWidth];
        if(!TryParseAxisHex(pair.AsSpan(0, equals), axisBytes))
        {
            return false;
        }

        if(!TryParsePeer(pair[(equals + 1)..], out host, out port))
        {
            return false;
        }

        member = new ReplicaAxis(axisBytes.ToArray());

        return true;
    }

    /// <summary>
    /// Loads the host's persisted replica identity, minting and persisting a fresh one on first use. The
    /// identity lives in the HOST's configuration location — never the store directory — so copying a store
    /// directory to seed a peer cannot copy who the replica is; replica-identity distinctness across hosts (and
    /// across replicas sharing one machine, via <c>--identity-dir</c>) is the deployment obligation the dotted
    /// lane's tripwire narrows but cannot replace.
    /// </summary>
    /// <param name="directory">The resolved identity directory, created when missing.</param>
    /// <returns>The host's replica identity.</returns>
    /// <exception cref="InvalidDataException">A persisted identity file exists but is not exactly an identity's width.</exception>
    private static ReplicaAxis LoadOrMintIdentity(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "replica-identity");
        if(File.Exists(path))
        {
            byte[] existing = File.ReadAllBytes(path);
            if(existing.Length != ReplicaAxis.ByteWidth)
            {
                throw new InvalidDataException(FormattableString.Invariant($"The persisted replica identity at '{path}' is {existing.Length} bytes; an identity is exactly {ReplicaAxis.ByteWidth}. Restore the file or remove it to mint a fresh identity."));
            }

            return new ReplicaAxis(existing);
        }

        RandomnessValue value = VeritasRandomness.System(new RandomnessRequest(RandomnessKind.Bytes, default, ReplicaAxis.ByteWidth, default));
        byte[] minted = value.Bytes.ToArray();
        File.WriteAllBytes(path, minted);

        return new ReplicaAxis(minted);
    }

    /// <summary>Resolves the directory the host's replica identity and its metadata-plane state live in: the operator's <c>--identity-dir</c>, else the host's per-user configuration location.</summary>
    /// <param name="identityDirectory">The configured identity directory, or <see langword="null"/> for the per-user default.</param>
    /// <returns>The resolved directory path.</returns>
    private static string ResolveIdentityDirectory(string? identityDirectory)
    {
        return identityDirectory is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumoin.Veritas", "replicate");
    }

    /// <summary>The identity's display prefix: the first eight identity bytes as uppercase hex — pseudonymous public protocol state, printed so an operator can tell replicas apart without the full 64 hex characters.</summary>
    /// <param name="identity">The host replica identity.</param>
    /// <returns>The sixteen-character hex prefix.</returns>
    private static string IdentityHexPrefix(ReplicaAxis identity)
    {
        return Convert.ToHexString(identity.Bytes.Span[..8]);
    }

    /// <summary>Accepts loopback connections until stopped, serving each on its own task. Per-connection faults are isolated inside the connection task, so a misbehaving client never takes the accept loop down. The loop runs from before the engine open, so a connection it accepts reads the engine-backed state through the host's own published fields rather than through arguments fixed when the loop started.</summary>
    /// <param name="listener">The started listener.</param>
    /// <param name="planeHost">The composed metadata-plane host, or <see langword="null"/> when the host runs planeless.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop has stopped and every tracked connection has ended.</returns>
    private async Task AcceptLoopAsync(TcpListener listener, MetadataPlaneHost? planeHost, CancellationToken cancellationToken)
    {
        List<Task> connections = [];
        try
        {
            while(!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch(OperationCanceledException)
                {
                    break;
                }
                catch(SocketException) when(cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch(ObjectDisposedException) when(cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                connections.Add(ServeConnectionAsync(client, planeHost, cancellationToken));
                connections.RemoveAll(static connection => connection.IsCompleted);
            }
        }
        finally
        {
            //Every connection task isolates its own faults, so this join surfaces nothing but completion.
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serves one accepted connection: reads the one service-selector byte and routes to the sketch serve, the
    /// shard-difference serve, the dotted-difference serve, or the metadata-plane serve. An unknown selector is
    /// answered with the named refusal byte, an engine-backed selector dialed while the open has not finished is
    /// answered with the named not-ready byte, and an immediate end closes the connection.
    /// </summary>
    /// <param name="client">The accepted client; owned and disposed by this serve.</param>
    /// <param name="planeHost">The composed metadata-plane host, or <see langword="null"/> when the host runs planeless and knows no such service.</param>
    /// <param name="cancellationToken">Stops the serve at host shutdown.</param>
    /// <returns>A task that completes when the connection ends.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The accept loop's fault isolation: a misbehaving or vanishing client must never take the listener or the process down, and a connection's teardown is the channel protocols' normal end of serve, so any fault is contained to the connection it happened on.")]
    private async Task ServeConnectionAsync(TcpClient client, MetadataPlaneHost? planeHost, CancellationToken cancellationToken)
    {
        using(client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                int service = await ReadServiceByteAsync(stream, cancellationToken).ConfigureAwait(false);
                if(service == ReplicateWire.MetadataService)
                {
                    if(planeHost is not MetadataPlaneHost plane)
                    {
                        //A planeless host does not know this service at all, which is the unknown-service
                        //refusal rather than an availability answer.
                        await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceRefusedUnknown, cancellationToken).ConfigureAwait(false);

                        return;
                    }

                    await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceAccepted, cancellationToken).ConfigureAwait(false);
                    await plane.ServeAsync(stream, cancellationToken).ConfigureAwait(false);

                    return;
                }

                if(service is not (ReplicateWire.SketchService or ReplicateWire.ShardDifferenceService or ReplicateWire.DottedDifferenceService))
                {
                    if(service >= 0)
                    {
                        //The named unknown-service refusal: one verdict byte before the close, so a dialing peer
                        //distinguishes service-unknown from network death — an absent verdict is death, never
                        //inferred as unsupported.
                        await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceRefusedUnknown, cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }

                if(servedEngine is not VeritasEngine engine || servedShardServer is not ShardDifferenceChannelServer shardServer)
                {
                    //The listener runs from before the store-backed open so the metadata plane is reachable
                    //during it; a peer that dials an engine-backed service inside that window is told the
                    //service is not ready, which is availability and never capability.
                    await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceUnavailableNotReady, cancellationToken).ConfigureAwait(false);

                    return;
                }

                if(service == ReplicateWire.SketchService)
                {
                    await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceAccepted, cancellationToken).ConfigureAwait(false);
                    await engine.ServeSketchChannelAsync(PipeReader.Create(stream, ReplicateWire.LeaveOpenReaderOptions), PipeWriter.Create(stream, ReplicateWire.LeaveOpenWriterOptions), cancellationToken).ConfigureAwait(false);
                }
                else if(service == ReplicateWire.ShardDifferenceService)
                {
                    await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceAccepted, cancellationToken).ConfigureAwait(false);
                    await shardServer.ServeAsync(PipeReader.Create(stream, ReplicateWire.LeaveOpenReaderOptions), PipeWriter.Create(stream, ReplicateWire.LeaveOpenWriterOptions), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteServiceVerdictAsync(stream, ReplicateWire.ServiceAccepted, cancellationToken).ConfigureAwait(false);
                    await engine.ServeDottedDifferenceAsync(PipeReader.Create(stream, ReplicateWire.LeaveOpenReaderOptions), PipeWriter.Create(stream, ReplicateWire.LeaveOpenWriterOptions), DottedFaultPrinter.Print, cancellationToken).ConfigureAwait(false);
                }
            }
            catch(OperationCanceledException)
            {
                //Host shutdown: the connection closes with the listener.
            }
            catch(Exception)
            {
                //Contained: the client observes its connection ending; the serve state is per-connection only.
            }
        }
    }

    /// <summary>Reads the connection's one service-selector byte, or -1 when the client closed without sending one.</summary>
    /// <param name="stream">The connection's stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The selector byte, or -1.</returns>
    private async ValueTask<int> ReadServiceByteAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using IMemoryOwner<byte> selector = pool.Rent(1);
        int read = await stream.ReadAsync(selector.Memory[..1], cancellationToken).ConfigureAwait(false);

        return read == 1 ? selector.Memory.Span[0] : -1;
    }

    /// <summary>Writes the connection's one service-verdict byte — accepted before a routed serve's frames, or the named unknown-service refusal before the close.</summary>
    /// <param name="stream">The connection's stream.</param>
    /// <param name="verdict">The verdict byte.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the verdict is written.</returns>
    private async ValueTask WriteServiceVerdictAsync(NetworkStream stream, byte verdict, CancellationToken cancellationToken)
    {
        using IMemoryOwner<byte> buffer = pool.Rent(1);
        buffer.Memory.Span[0] = verdict;
        await stream.WriteAsync(buffer.Memory[..1], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one reconcile pull per interval tick, printing each outcome line, until stopped. The loop is its own fault boundary: a cycle that fails for any reason answers one error line and the next tick runs, so automatic reconciliation never dies silently; cancellation still propagates and stops the loop.</summary>
    /// <param name="engine">The open engine the pulls run against.</param>
    /// <param name="interval">The tick interval.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop is stopped.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The interval loop is the background reconcile's fault boundary: a cycle that fails for any reason answers one error line and the loop keeps its cadence, mirroring the verb loop's survive-a-bad-round discipline; cancellation still propagates and stops the loop.")]
    private async Task ReconcileLoopAsync(VeritasEngine engine, TimeSpan interval, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(interval, TimeProvider.System);
        while(await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await RunReconcileAsync(engine, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
                throw;
            }
            catch(Exception exception)
            {
                await Console.Out.WriteLineAsync(FormattableString.Invariant($"error reconcile {exception.GetType().Name}: {exception.Message}")).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs one reconcile against the bound peer and prints its outcome line. A REMOVE-AWARE store drives the
    /// dotted lane — retractions propagate as drops, tombstones answer pushes as push-drops, and a peer that
    /// cannot serve it is a NAMED refusal on the outcome line, never a silent downgrade; the operator's explicit
    /// add-only fallback is the <c>reconcile-addonly</c> verb. A store that is not remove-aware runs the
    /// add-only pull it always has.
    /// </summary>
    /// <param name="engine">The open engine the reconcile runs against.</param>
    /// <param name="cancellationToken">Cancels the reconcile.</param>
    /// <returns>A task that completes when the outcome line is written.</returns>
    private async Task RunReconcileAsync(VeritasEngine engine, CancellationToken cancellationToken)
    {
        if(engine.ReadReplicationStatus().CausalityState == ReplicationCausalityState.RemoveAware)
        {
            DottedReconcileOutcome outcome = await engine.ReconcileRemoveAwareFromPeerAsync(Binding.OpenPeerDottedConnectionAsync, ReplicateWire.DottedSymbolCap, DottedFaultPrinter.Print, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(FormattableString.Invariant($"reconcile kind={outcome.Kind} reason={outcome.PeerDeclineReason} adopted={outcome.AdoptedAdditions} dropped={outcome.AdoptedDrops} pushed={outcome.PushedEntries} pushdrops={outcome.PushedDropDots}")).ConfigureAwait(false);

            return;
        }

        await RunReconcileAddOnlyAsync(engine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one ADD-ONLY reconcile pull against the bound peer and prints its outcome line — the operator-explicit fallback lane, and the only lane of a store that is not remove-aware. The caller passes its OWN epoch, so the wire stamp check inside the session is the authoritative cross-lineage fence and its refusal is reported by name on the outcome.</summary>
    /// <param name="engine">The open engine the pull runs against.</param>
    /// <param name="cancellationToken">Cancels the pull.</param>
    /// <returns>A task that completes when the outcome line is written.</returns>
    private async Task RunReconcileAddOnlyAsync(VeritasEngine engine, CancellationToken cancellationToken)
    {
        PeerReconcileOutcome outcome = await engine.ReconcileFromPeerAsync(Binding.FetchPeerSketchAsync, engine.Dictionary.Epoch, ReplicationPolicy.Default, ReplicateWire.ReconcileMaxRounds, cancellationToken).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"reconcile converged={outcome.Converged} rounds={outcome.Rounds} writeback={outcome.WriteBack} outcome={outcome.LastOutcome}")).ConfigureAwait(false);
    }

    /// <summary>Runs the standard-input verb loop until <c>quit</c> or end of input, answering each verb with one machine-parseable line; both exits persist the committed state so the store carries it. The loop is the command's fault boundary.</summary>
    /// <param name="engine">The open engine the verbs run against.</param>
    /// <param name="store">The store <c>quit</c> persists into.</param>
    /// <param name="serveSnapshot">The committed-set snapshot seam the <c>fingerprint</c> verb folds.</param>
    /// <param name="planeHost">The composed metadata-plane host the three metadata verbs drive, or <see langword="null"/> when the host runs planeless.</param>
    /// <param name="identity">The host replica identity the <c>metadata-claim</c> verb claims.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>The process exit code.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The verb loop is the command's fault boundary: a verb that fails for any reason answers one error line and the daemon continues serving its peer, mirroring the self-heal loop's survive-a-bad-round discipline; cancellation still propagates and stops the host.")]
    private async Task<int> RunVerbLoopAsync(VeritasEngine engine, FileSystemPersistenceStore store, ProvideShardServeSnapshotDelegate serveSnapshot, MetadataPlaneHost? planeHost, ReplicaAxis identity, CancellationToken cancellationToken)
    {
        while(!cancellationToken.IsCancellationRequested)
        {
            string? line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if(line is null)
            {
                break;
            }

            string trimmed = line.Trim();
            if(trimmed.Length == 0)
            {
                continue;
            }

            int split = trimmed.IndexOf(' ', StringComparison.Ordinal);
            string verb = split < 0 ? trimmed : trimmed[..split];
            string argument = split < 0 ? string.Empty : trimmed[(split + 1)..].Trim();

            try
            {
                switch(verb)
                {
                    case "ingest":
                    {
                        await RunIngestAsync(engine, argument, cancellationToken).ConfigureAwait(false);

                        break;
                    }

                    case "update":
                    {
                        await RunUpdateAsync(engine, argument, cancellationToken).ConfigureAwait(false);

                        break;
                    }

                    case "reconcile":
                    {
                        await RunReconcileAsync(engine, cancellationToken).ConfigureAwait(false);

                        break;
                    }

                    case "reconcile-addonly":
                    {
                        await RunReconcileAddOnlyAsync(engine, cancellationToken).ConfigureAwait(false);

                        break;
                    }

                    case "peer":
                    {
                        await RunPeerAsync(argument).ConfigureAwait(false);

                        break;
                    }

                    case "metadata-route":
                    {
                        if(planeHost is not MetadataPlaneHost routed)
                        {
                            await Console.Out.WriteLineAsync("error metadata-route expects a metadata plane; start the host with --metadata-founder").ConfigureAwait(false);

                            break;
                        }

                        await BindMetadataRouteAsync(routed, argument).ConfigureAwait(false);

                        break;
                    }

                    case "metadata-claim":
                    {
                        if(planeHost is not MetadataPlaneHost claiming)
                        {
                            await Console.Out.WriteLineAsync("error metadata-claim expects a metadata plane; start the host with --metadata-founder").ConfigureAwait(false);

                            break;
                        }

                        await RunMetadataClaimAsync(claiming, identity, cancellationToken).ConfigureAwait(false);

                        break;
                    }

                    case "metadata-status":
                    {
                        if(planeHost is not MetadataPlaneHost reporting)
                        {
                            await Console.Out.WriteLineAsync("error metadata-status expects a metadata plane; start the host with --metadata-founder").ConfigureAwait(false);

                            break;
                        }

                        await RunMetadataStatusAsync(reporting, engine, cancellationToken).ConfigureAwait(false);

                        break;
                    }

                    case "status":
                    {
                        VeritasReplicationStatus status = engine.ReadReplicationStatus();
                        await Console.Out.WriteLineAsync(FormattableString.Invariant($"status triples={status.CommittedTripleCount} epoch={status.DictionaryEpoch:X16} generation={status.SketchGeneration} terms={status.TermCount} causality={status.CausalityState} ledger={status.LedgerGeneration}")).ConfigureAwait(false);

                        break;
                    }

                    case "fingerprint":
                    {
                        IReadOnlyList<ReadOnlyMemory<byte>> keys = serveSnapshot();
                        await Console.Out.WriteLineAsync(FormattableString.Invariant($"fingerprint {ComputeFingerprint(keys)} count={keys.Count}")).ConfigureAwait(false);

                        break;
                    }

                    case "quit":
                    {
                        DurableSystemOfRecordCommit committed = engine.Persist(store);
                        await Console.Out.WriteLineAsync(FormattableString.Invariant($"quit persisted={committed.Generation}")).ConfigureAwait(false);

                        return 0;
                    }

                    default:
                    {
                        await Console.Out.WriteLineAsync(FormattableString.Invariant($"error unknown verb '{verb}'")).ConfigureAwait(false);

                        break;
                    }
                }
            }
            catch(OperationCanceledException)
            {
                throw;
            }
            catch(Exception exception)
            {
                await Console.Out.WriteLineAsync(FormattableString.Invariant($"error {verb} {exception.GetType().Name}: {exception.Message}")).ConfigureAwait(false);
            }
        }

        //End of input without quit still persists, so the store carries the final committed state.
        DurableSystemOfRecordCommit final = engine.Persist(store);
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"quit persisted={final.Generation}")).ConfigureAwait(false);

        return 0;
    }

    /// <summary>Ingests one data file's default-graph triples as one journalled commit and prints the receipt line, with the post-ingest term count — the runtime check of the dictionary-stable write posture.</summary>
    /// <param name="engine">The open engine.</param>
    /// <param name="path">The data-file path.</param>
    /// <param name="cancellationToken">Aborts the ingest.</param>
    /// <returns>A task that completes when the receipt line is written.</returns>
    private static async Task RunIngestAsync(VeritasEngine engine, string path, CancellationToken cancellationToken)
    {
        if(path.Length == 0)
        {
            await Console.Out.WriteLineAsync("error ingest expects a data-file path").ConfigureAwait(false);

            return;
        }

        IngestReceipt receipt = await engine.IngestAsync(VeritasOperations.StreamQuadsAsync([path], cancellationToken), cancellationToken).ConfigureAwait(false);
        VeritasReplicationStatus status = engine.ReadReplicationStatus();
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"ingest writeback={receipt.WriteBack} triples={receipt.TripleCount} terms={status.TermCount}")).ConfigureAwait(false);
    }

    /// <summary>Executes one SPARQL update file as one journalled commit and prints the receipt line with the post-update term count — the retraction surface of the replicate host (a <c>DELETE DATA</c> file retracts), and the runtime check that updates stay dictionary-stable.</summary>
    /// <param name="engine">The open engine.</param>
    /// <param name="path">The SPARQL update file path.</param>
    /// <param name="cancellationToken">Aborts the update.</param>
    /// <returns>A task that completes when the receipt line is written.</returns>
    private static async Task RunUpdateAsync(VeritasEngine engine, string path, CancellationToken cancellationToken)
    {
        if(path.Length == 0)
        {
            await Console.Out.WriteLineAsync("error update expects a SPARQL update file path").ConfigureAwait(false);

            return;
        }

        string sparql = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        await engine.UpdateAsync(Utf8Strings.From(sparql), cancellationToken: cancellationToken).ConfigureAwait(false);
        VeritasReplicationStatus status = engine.ReadReplicationStatus();
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"update ok triples={status.CommittedTripleCount} terms={status.TermCount}")).ConfigureAwait(false);
    }

    /// <summary>Binds the peer address the outbound surfaces dial and prints the acknowledgement line.</summary>
    /// <param name="argument">The <c>host:port</c> argument.</param>
    /// <returns>A task that completes when the line is written.</returns>
    private async Task RunPeerAsync(string argument)
    {
        if(!TryParsePeer(argument, out string host, out int port))
        {
            await Console.Out.WriteLineAsync(FormattableString.Invariant($"error peer expects host:port, got '{argument}'")).ConfigureAwait(false);

            return;
        }

        Binding.SetPeer(host, port);
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"peer ok {host}:{port}")).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-issues this host's identity claim on the coordinated record and prints its outcome line. The claim is
    /// idempotent, so this is what an operator drives after binding routes an open legitimately answered
    /// undecided over — the plane is never a liveness dependency, so an open with unreachable fellows proceeds
    /// and the claim is settled afterwards. A claim consensus decided in this host's favour is saved to the
    /// host's confirmed facts, which is what lets a later routine reopen cost no round trip.
    /// </summary>
    /// <param name="planeHost">The composed metadata-plane host.</param>
    /// <param name="identity">The host replica identity being claimed.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>A task that completes when the outcome line is written.</returns>
    private async Task RunMetadataClaimAsync(MetadataPlaneHost planeHost, ReplicaAxis identity, CancellationToken cancellationToken)
    {
        await CatchUpAsync(planeHost, cancellationToken).ConfigureAwait(false);
        MetadataPlaneResult<IdentityClaimOutcome> claimed = await planeHost.Plane.ClaimIdentityAsync(identity, AttemptBudget, cancellationToken).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane claim={claimed.Outcome} version={ClaimedVersionOf(claimed, identity)}")).ConfigureAwait(false);
        if(claimed.Outcome is IdentityClaimOutcome.Claimed or IdentityClaimOutcome.AlreadyClaimedBySelf)
        {
            await planeHost.SaveConfirmedFactsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Prints one readiness line for the coordinated chain: how many members of the membership answered their
    /// own version probe, whether a quorum of them has learned the version this host holds, that version, and
    /// the standing this host's open established. A member that answers nothing past the plane's per-member
    /// deadline counts exactly as a member nothing reaches, so an unreachable fellow subtracts from the quorum
    /// rather than being read as agreement.
    /// </summary>
    /// <param name="planeHost">The composed metadata-plane host.</param>
    /// <param name="engine">The open engine whose coordination standing the line carries.</param>
    /// <param name="cancellationToken">Cancels the readiness read.</param>
    /// <returns>A task that completes when the status line is written.</returns>
    private static async Task RunMetadataStatusAsync(MetadataPlaneHost planeHost, VeritasEngine engine, CancellationToken cancellationToken)
    {
        RegisterVersion version = planeHost.LearnedVersion() ?? RegisterVersion.Unwritten;
        RegisterReadiness readiness = await planeHost.Plane.ReadReadinessAsync(cancellationToken).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(FormattableString.Invariant($"plane status chain={Convert.ToHexStringLower(planeHost.Deployment.Cluster.AsSpan())} reachable={readiness.Reachable}/{readiness.Members.Length} quorum={readiness.QuorumHasLearned(version)} version={version.Value} standing={engine.MetadataCoordination}")).ConfigureAwait(false);
    }

    /// <summary>
    /// The register version an axis's claim is RECORDED at, read off the record the obligation decided. It is
    /// the version the claim first settled at and not the version of the write that reported it, so a re-issued
    /// claim answers at the version its first one took — which is what makes two claims sharing a version
    /// evidence that the claim list was overwritten rather than appended to.
    /// </summary>
    /// <param name="claimed">The claim obligation's result.</param>
    /// <param name="axis">The claimed axis.</param>
    /// <returns>The recorded version, or the unwritten version when the obligation decided nothing.</returns>
    private static ulong ClaimedVersionOf(MetadataPlaneResult<IdentityClaimOutcome> claimed, ReplicaAxis axis)
    {
        if(claimed.Record is VeritasMetadataRecord record)
        {
            for(int i = 0; i < record.IdentityClaims.Length; i++)
            {
                if(record.IdentityClaims[i].Axis.Equals(axis))
                {
                    return record.IdentityClaims[i].ClaimedAt.Value;
                }
            }
        }

        return RegisterVersion.Unwritten.Value;
    }

    /// <summary>Parses a full-width replica identity axis written as hex, in either case, into its bytes. A wrong length or a character outside the hex alphabet is a value-based refusal, because a mistyped option is an expected condition and never an invariant violation.</summary>
    /// <param name="token">The hex token.</param>
    /// <param name="destination">The buffer the identity bytes are written into; at least an identity's width.</param>
    /// <returns>Whether the token parsed.</returns>
    private static bool TryParseAxisHex(ReadOnlySpan<char> token, Span<byte> destination)
    {
        return TryParseHex(token, destination.Length < ReplicaAxis.ByteWidth ? destination : destination[..ReplicaAxis.ByteWidth]);
    }


    /// <summary>
    /// Parses a fixed-width value written as hex, in either case, into the whole of <paramref name="destination"/>.
    /// A wrong length or a character outside the hex alphabet is a value-based refusal, because a mistyped
    /// option is an expected condition and never an invariant violation.
    /// </summary>
    /// <param name="token">The hex token, which must be exactly twice the destination's width.</param>
    /// <param name="destination">The buffer the bytes are written into, filled entirely.</param>
    /// <returns><see langword="true"/> when the token was parsed.</returns>
    private static bool TryParseHex(ReadOnlySpan<char> token, Span<byte> destination)
    {
        if(token.Length != destination.Length * 2)
        {
            return false;
        }

        for(int i = 0; i < destination.Length; i++)
        {
            if(!TryParseNibble(token[i * 2], out int high) || !TryParseNibble(token[(i * 2) + 1], out int low))
            {
                return false;
            }

            destination[i] = (byte)((high << 4) | low);
        }

        return true;
    }

    /// <summary>Parses one hex digit in either case.</summary>
    /// <param name="character">The character to parse.</param>
    /// <param name="value">The nibble's value on success.</param>
    /// <returns>Whether the character is a hex digit.</returns>
    private static bool TryParseNibble(char character, out int value)
    {
        value = character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1
        };

        return value >= 0;
    }

    /// <summary>Parses a <c>host:port</c> peer argument on its LAST colon, so a bracketed or scoped address keeps its own colons.</summary>
    /// <param name="argument">The argument text.</param>
    /// <param name="host">The parsed host on success.</param>
    /// <param name="port">The parsed port on success.</param>
    /// <returns>Whether the argument parsed.</returns>
    private static bool TryParsePeer(string argument, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        int colon = argument.LastIndexOf(':');
        if(colon <= 0 || colon == argument.Length - 1)
        {
            return false;
        }

        if(!int.TryParse(argument.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > ushort.MaxValue)
        {
            return false;
        }

        host = argument[..colon];

        return true;
    }

    /// <summary>Folds the committed set's structural keys into the order-independent 128-bit XOR fingerprint, rendered as uppercase hex — the cross-replica content-equality check the convergence rows compare.</summary>
    /// <param name="keys">The committed set's projected keys.</param>
    /// <returns>The fingerprint hex string.</returns>
    private static string ComputeFingerprint(IReadOnlyList<ReadOnlyMemory<byte>> keys)
    {
        Span<byte> fold = stackalloc byte[ContentKey128.ByteWidth];
        for(int k = 0; k < keys.Count; k++)
        {
            ReadOnlySpan<byte> key = keys[k].Span;
            for(int i = 0; i < fold.Length; i++)
            {
                fold[i] ^= key[i];
            }
        }

        return Convert.ToHexString(fold);
    }

    /// <summary>Joins a background loop at shutdown, swallowing only its cooperative cancellation; any genuine loop fault surfaces loudly.</summary>
    /// <param name="loop">The loop task, or <see langword="null"/> when the loop never started.</param>
    /// <returns>A task that completes when the loop has ended.</returns>
    private static async Task JoinQuietlyAsync(Task? loop)
    {
        if(loop is null)
        {
            return;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            //The cooperative stop.
        }
    }

    /// <summary>A fixed-interval cadence estimator injected by <c>--heal-interval</c> through the self-heal options' delegate seam, so a bounded poll observes a heal round without the reliability model's much longer default interval; carried as explicit state so the estimator binds as a method group.</summary>
    /// <param name="interval">The fixed interval between rounds.</param>
    private sealed class FixedCadence(TimeSpan interval)
    {
        /// <summary>The fixed interval between rounds.</summary>
        private TimeSpan Interval { get; } = interval;

        /// <summary>Answers the fixed interval regardless of the deployment facts.</summary>
        /// <param name="context">The deployment facts; unread by a fixed cadence.</param>
        /// <returns>The interval.</returns>
        public TimeSpan Estimate(ScrubCadenceContext context)
        {
            return Interval;
        }
    }

    /// <summary>Prints each storage self-heal trace event as one operational line, so a host observing this process sees the round verdicts and the healed-generation publish as they happen.</summary>
    private static class StorageTracePrinter
    {
        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public static void Print(in StorageTraceEvent evt)
        {
            Console.WriteLine(FormattableString.Invariant($"heal kind={evt.Kind} generation={evt.CommitGeneration} role={evt.RoleCode} block={evt.BlockIndex} detail={evt.ByteOffset} items={evt.ItemCount}"));
        }
    }

    /// <summary>Prints each declined shard fetch's fault class as one operational line, so a value-declined shard exchange is visible beside the round that drove it.</summary>
    private static class ShardFaultPrinter
    {
        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public static void Print(in ShardDifferenceFaultEvent evt)
        {
            Console.WriteLine(FormattableString.Invariant($"shardfault kind={evt.Kind} shard={evt.ShardIndex}"));
        }
    }

    /// <summary>Prints each dotted exchange's fault class as one operational line, so a refused or interrupted dotted exchange is visible beside the reconcile or serve that drove it.</summary>
    private static class DottedFaultPrinter
    {
        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public static void Print(in DottedDifferenceFaultEvent evt)
        {
            Console.WriteLine(FormattableString.Invariant($"dottedfault kind={evt.Kind}"));
        }
    }
}
