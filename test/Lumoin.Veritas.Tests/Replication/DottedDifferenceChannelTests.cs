using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Verisync.Core;
using DottedElement = Lumoin.Verisync.Core.DottedEntry<Lumoin.Veritas.Core.EncodedTriple>;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The in-process dotted-difference battery: real engines over durable journals, exchanging through the real
/// channel client and serve across pipe pairs — the mechanism rows of the remove-aware lane. Retractions
/// propagate as drops and never resurrect (the cure of the add-only lane's measured resurrection boundary);
/// tombstoned pushes answer as push-drops; concurrent net additions survive under add-wins; the baseline
/// lifecycle, the independent-baseline storm, the pre-baseline coverage boundary, the identity-collision
/// tripwire, the mixed-version refusals, and the interrupted-session durable-prefix posture are each measured
/// by name. The replicas are same-lineage clones of one seeded journal under distinct host identities —
/// deployment's copy-the-store discipline — and the THREE-replica row pins topology-independence: the
/// protocol is identical pairwise anti-entropy whatever the geometry; a geo-distributed deployment differs
/// only in setup (which peers are routed to and how often), which changes convergence latency, never the
/// outcome.
/// </summary>
[TestClass]
internal sealed class DottedDifferenceChannelTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The default symbol cap the rows exchange under; far above any row's dotted difference.</summary>
    private const int RoomySymbolCap = 4096;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A deterministic host identity axis filled with one byte value.</summary>
    /// <param name="fill">The fill byte.</param>
    /// <returns>The identity axis.</returns>
    private static ReplicaAxis Axis(byte fill)
    {
        byte[] bytes = new byte[ReplicaAxis.ByteWidth];
        Array.Fill(bytes, fill);

        return new ReplicaAxis(bytes);
    }

    /// <summary>The identity the lineage seed is created under; every replica then opens under its own axis.</summary>
    private static ReplicaAxis SeedAxis
    {
        get
        {
            return Axis(0xEE);
        }
    }

    /// <summary>The first replica's host identity axis.</summary>
    private static ReplicaAxis AxisA
    {
        get
        {
            return Axis(0x0A);
        }
    }

    /// <summary>The second replica's host identity axis.</summary>
    private static ReplicaAxis AxisB
    {
        get
        {
            return Axis(0x0B);
        }
    }

    /// <summary>The third replica's host identity axis.</summary>
    private static ReplicaAxis AxisC
    {
        get
        {
            return Axis(0x0C);
        }
    }

    /// <summary>The journal-less warm store's host identity axis.</summary>
    private static ReplicaAxis WarmAxis
    {
        get
        {
            return Axis(0x0D);
        }
    }

    /// <summary>An axis belonging to no replica of any row — the foreign minter the posture-violation peer names.</summary>
    private static ReplicaAxis ForeignAxis
    {
        get
        {
            return Axis(0x77);
        }
    }

    /// <summary>Opens a REMOVE-AWARE replica over a store directory: the durable dataset journal inside the directory, the host identity, and optionally the explicit baseline step.</summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="identity">The host identity axis this open mints on.</param>
    /// <param name="baseline">Whether the open performs the explicit baseline step.</param>
    /// <param name="cancellationToken">Aborts the open.</param>
    /// <returns>The opened engine.</returns>
    private static async Task<VeritasEngine> OpenReplicaAsync(string directory, ReplicaAxis identity, bool baseline, CancellationToken cancellationToken)
    {
        VeritasEngineOptions options = new()
        {
            Reasoning = null,
            ReplicaIdentity = identity,
            BaselineReplicationCausality = baseline,
            DatasetJournalPath = Path.Combine(directory, "dataset.journal"),
        };

        return await VeritasEngine.OpenMutableAsync(new FileSystemPersistenceStore(directory), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens an ADD-ONLY replica over a store directory: the durable journal, no host identity.</summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="cancellationToken">Aborts the open.</param>
    /// <returns>The opened engine.</returns>
    private static async Task<VeritasEngine> OpenAddOnlyReplicaAsync(string directory, CancellationToken cancellationToken)
    {
        VeritasEngineOptions options = new()
        {
            Reasoning = null,
            DatasetJournalPath = Path.Combine(directory, "dataset.journal"),
        };

        return await VeritasEngine.OpenMutableAsync(new FileSystemPersistenceStore(directory), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Seeds one REMOVE-AWARE lineage and clones it per replica: replica zero is created under the seed identity with the seed triples committed, and every clone shares the journal — the dictionary epoch, the seed dots, and the creation baseline — while each row opens each copy under its own host identity.</summary>
    /// <param name="root">The battery's temp root.</param>
    /// <param name="replicaCount">The number of replica directories.</param>
    /// <param name="seedLocals">The seed triples as (subject, object) local-name pairs under the shared predicate.</param>
    /// <param name="cancellationToken">Bounds the seeding.</param>
    /// <returns>The replica directories.</returns>
    private static async Task<string[]> SeedLineageAsync(string root, int replicaCount, (string Subject, string Object)[] seedLocals, CancellationToken cancellationToken)
    {
        string[] directories = new string[replicaCount];
        for(int i = 0; i < replicaCount; i++)
        {
            directories[i] = Path.Combine(root, FormattableString.Invariant($"replica-{i}"));
        }

        VeritasEngine seeded = await OpenReplicaAsync(directories[0], SeedAxis, baseline: false, cancellationToken).ConfigureAwait(false);
        try
        {
            await InsertAsync(seeded, seedLocals, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await seeded.DisposeAsync().ConfigureAwait(false);
        }

        for(int i = 1; i < replicaCount; i++)
        {
            CloneDirectory(directories[0], directories[i]);
        }

        return directories;
    }

    /// <summary>Seeds one ADD-ONLY lineage (no identity anywhere) and clones it per replica — the pre-causality history the baseline and boundary rows upgrade.</summary>
    /// <param name="root">The battery's temp root.</param>
    /// <param name="replicaCount">The number of replica directories.</param>
    /// <param name="seedLocals">The seed triples as (subject, object) local-name pairs.</param>
    /// <param name="cancellationToken">Bounds the seeding.</param>
    /// <returns>The replica directories.</returns>
    private static async Task<string[]> SeedAddOnlyLineageAsync(string root, int replicaCount, (string Subject, string Object)[] seedLocals, CancellationToken cancellationToken)
    {
        string[] directories = new string[replicaCount];
        for(int i = 0; i < replicaCount; i++)
        {
            directories[i] = Path.Combine(root, FormattableString.Invariant($"replica-{i}"));
        }

        VeritasEngine seeded = await OpenAddOnlyReplicaAsync(directories[0], cancellationToken).ConfigureAwait(false);
        try
        {
            await InsertAsync(seeded, seedLocals, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await seeded.DisposeAsync().ConfigureAwait(false);
        }

        for(int i = 1; i < replicaCount; i++)
        {
            CloneDirectory(directories[0], directories[i]);
        }

        return directories;
    }

    /// <summary>Copies a flat store directory per replica — the deployment's copy-the-store discipline; identity never rides the copy.</summary>
    /// <param name="source">The seeded directory.</param>
    /// <param name="destination">The replica's directory.</param>
    private static void CloneDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    /// <summary>Commits the given triples as one INSERT DATA update.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="locals">The triples as (subject, object) local-name pairs.</param>
    /// <param name="cancellationToken">Aborts the update.</param>
    /// <returns>A task that completes when the commit landed.</returns>
    private static async Task InsertAsync(VeritasEngine engine, (string Subject, string Object)[] locals, CancellationToken cancellationToken)
    {
        await engine.UpdateAsync(Utf8Strings.From($"INSERT DATA {{ {TriplesBlock(locals)} }}"), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Commits the given triples as one DELETE DATA update — the retraction the lane protects.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="locals">The triples as (subject, object) local-name pairs.</param>
    /// <param name="cancellationToken">Aborts the update.</param>
    /// <returns>A task that completes when the commit landed.</returns>
    private static async Task DeleteAsync(VeritasEngine engine, (string Subject, string Object)[] locals, CancellationToken cancellationToken)
    {
        await engine.UpdateAsync(Utf8Strings.From($"DELETE DATA {{ {TriplesBlock(locals)} }}"), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders the triples block of an update over the shared predicate.</summary>
    /// <param name="locals">The triples as (subject, object) local-name pairs.</param>
    /// <returns>The block text.</returns>
    private static string TriplesBlock((string Subject, string Object)[] locals)
    {
        System.Text.StringBuilder block = new();
        foreach((string subject, string @object) in locals)
        {
            block.Append(FormattableString.Invariant($"<{Ex}{subject}> <{Ex}p> <{Ex}{@object}> . "));
        }

        return block.ToString();
    }

    /// <summary>Whether the engine's committed default graph holds the triple.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="subject">The subject local name.</param>
    /// <param name="object">The object local name.</param>
    /// <param name="cancellationToken">Aborts the read.</param>
    /// <returns>Whether the triple is present.</returns>
    private static async Task<bool> HoldsAsync(VeritasEngine engine, string subject, string @object, CancellationToken cancellationToken)
    {
        return await engine.AskAsync(Utf8Strings.From($"ASK {{ <{Ex}{subject}> <{Ex}p> <{Ex}{@object}> }}"), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one dotted exchange between two engines over a fresh pipe pair: the responder serves through the engine's dotted serve, the initiator drives the engine's remove-aware reconcile through a pipe-backed connection. Both ends' fault events are collected, so a row's failure message names what actually happened.</summary>
    /// <param name="initiator">The initiating engine.</param>
    /// <param name="responder">The serving engine.</param>
    /// <param name="symbolCap">The exchange's symbol cap.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <param name="faults">The collector both ends' fault events land in, or <see langword="null"/> to collect none.</param>
    /// <returns>The initiator's outcome.</returns>
    private static async Task<DottedReconcileOutcome> ExchangeAsync(VeritasEngine initiator, VeritasEngine responder, int symbolCap, CancellationToken cancellationToken, FaultCollector? faults = null)
    {
        Pipe requestPipe = new();
        Pipe responsePipe = new();
        Task serve = responder.ServeDottedDifferenceAsync(requestPipe.Reader, responsePipe.Writer, faults is null ? null : faults.OnFault, cancellationToken);
        PipeConnectionFactory factory = new(requestPipe.Writer, responsePipe.Reader, serve);

        DottedReconcileOutcome outcome = await initiator.ReconcileRemoveAwareFromPeerAsync(factory.OpenAsync, symbolCap, faults is null ? null : faults.OnFault, cancellationToken).ConfigureAwait(false);
        if(faults is not null)
        {
            try
            {
                await serve.ConfigureAwait(false);
            }
            catch(Exception exception)
            {
                faults.RecordServeFault(exception);
            }
        }

        return outcome;
    }

    /// <summary>Collects both ends' fault events for a row's failure diagnostics.</summary>
    private sealed class FaultCollector
    {
        /// <summary>The collected fault kinds, in arrival order.</summary>
        private System.Collections.Concurrent.ConcurrentQueue<DottedDifferenceFaultKind> Kinds { get; } = new();

        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public void OnFault(in DottedDifferenceFaultEvent evt)
        {
            Kinds.Enqueue(evt.Kind);
        }

        /// <summary>The serve task's fault, when it faulted.</summary>
        private Exception? serveFault;

        /// <summary>Records the serve task's fault for the diagnostics.</summary>
        /// <param name="exception">The serve fault.</param>
        public void RecordServeFault(Exception exception)
        {
            serveFault = exception;
        }

        /// <summary>The collected kinds and any serve fault as one diagnostic token.</summary>
        /// <returns>The joined kinds, or <c>none</c>.</returns>
        public string Describe()
        {
            string kinds = Kinds.IsEmpty ? "none" : string.Join(",", Kinds);

            return serveFault is null ? kinds : FormattableString.Invariant($"{kinds}; serve fault: {serveFault}");
        }
    }

    /// <summary>Binds one pipe pair and its serve task as the connection factory for one exchange, without a lexical closure.</summary>
    /// <param name="requestWriter">The initiator's request pipe writer.</param>
    /// <param name="responseReader">The initiator's response pipe reader.</param>
    /// <param name="serve">The responder's serve task, joined on the connection's teardown.</param>
    private sealed class PipeConnectionFactory(PipeWriter requestWriter, PipeReader responseReader, Task serve)
    {
        /// <summary>The initiator's request pipe writer.</summary>
        private PipeWriter RequestWriter { get; } = requestWriter;

        /// <summary>The initiator's response pipe reader.</summary>
        private PipeReader ResponseReader { get; } = responseReader;

        /// <summary>The responder's serve task.</summary>
        private Task Serve { get; } = serve;

        /// <summary>Opens the one connection this factory carries.</summary>
        /// <param name="cancellationToken">Unused; an in-process pair opens synchronously.</param>
        /// <returns>The connection.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the serve-completion transport it owns) transfers to the caller per the OpenPeerDottedConnectionDelegate contract; the dotted client disposes it unconditionally on every exit.")]
        public ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<PeerChannelConnection>(new PeerChannelConnection(RequestWriter, ResponseReader, new ServeCompletion(Serve)));
        }

        /// <summary>Joins the serve on the connection's teardown, so a serve fault surfaces on the client's teardown path.</summary>
        /// <param name="serve">The serve task.</param>
        private sealed class ServeCompletion(Task serve): IAsyncDisposable
        {
            /// <summary>Joins the serve.</summary>
            /// <returns>The serve task as a value task.</returns>
            public ValueTask DisposeAsync()
            {
                return new ValueTask(serve);
            }
        }
    }

    /// <summary>The dotted no-resurrect row — the cure of the add-only lane's measured resurrection boundary: a locally-retracted triple does NOT come back through a dotted exchange against a peer that still holds it; the peer drops it instead, and a further exchange moves nothing.</summary>
    [TestMethod]
    public async Task ADottedExchangeDoesNotResurrectALocallyRetractedTriple()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-noresurrect-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 2, [("a", "b"), ("a", "c")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    await DeleteAsync(replicaA, [("a", "c")], TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsFalse(await HoldsAsync(replicaA, "a", "c", TestContext.CancellationToken).ConfigureAwait(false), "The local retraction lands before the exchange.");

                    FaultCollector faults = new();
                    DottedReconcileOutcome first = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken, faults).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Converged, first.Kind, FormattableString.Invariant($"The exchange converges (faults: {faults.Describe()}; A entries={replicaA.CommitLedger!.Snapshot().Entries.Length}; B entries={replicaB.CommitLedger!.Snapshot().Entries.Length}; B generation={replicaB.CommitLedger!.Generation})."));
                    Assert.AreEqual(1, first.PushedDropDots, "The peer's copy of the retracted triple is answered as a push-drop, never re-added.");
                    Assert.AreEqual(0, first.AdoptedAdditions, "Nothing resurrects onto the retracting replica.");

                    Assert.IsFalse(await HoldsAsync(replicaA, "a", "c", TestContext.CancellationToken).ConfigureAwait(false), "The retraction stands on the initiator — the add-only lane's resurrection is cured on the dotted lane.");
                    Assert.IsFalse(await HoldsAsync(replicaB, "a", "c", TestContext.CancellationToken).ConfigureAwait(false), "The retraction propagated to the peer as a drop.");
                    Assert.IsTrue(await HoldsAsync(replicaB, "a", "b", TestContext.CancellationToken).ConfigureAwait(false), "The untouched seed triple stays.");

                    DottedReconcileOutcome second = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.AlreadyConsistent, second.Kind, "A further exchange moves nothing — the tombstone is stable knowledge, not a churn.");
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The session-projection compatibility pin, below the channel: two raw remove-aware sessions cross-wired in memory over ledger projections — no channel, no pipes, no engine — exchange a one-versus-two-entry difference and answer the tombstoned entry as a push-drop. A failure here is the session binding or the projection, never the wire.</summary>
    [TestMethod]
    public async Task RawSessionsOverLedgerProjectionsAnswerATombstonedEntryAsAPushDrop()
    {
        using Lumoin.Veritas.Core.Memory.VeritasMemoryPool<byte> pool = new();
        EncodedTriple first = EncodedTriple.FromEncoded(1, 10, 2);
        EncodedTriple second = EncodedTriple.FromEncoded(1, 10, 3);
        CausalContext contextA = new();
        contextA.FoldContiguous(SeedAxis, 2);
        CausalContext contextB = new();
        contextB.FoldContiguous(SeedAxis, 2);
        DottedLedgerSnapshot snapshotA = new([SeedAxis], [new DottedTripleAssignment(first, [new CausalDot(SeedAxis, 1)])], contextA, new Lumoin.Veritas.Core.Hypertrie.Storage.NodeIdentifier(1));
        DottedLedgerSnapshot snapshotB = new([SeedAxis], [new DottedTripleAssignment(first, [new CausalDot(SeedAxis, 1)]), new DottedTripleAssignment(second, [new CausalDot(SeedAxis, 2)])], contextB, new Lumoin.Veritas.Core.Hypertrie.Storage.NodeIdentifier(1));
        DottedLedgerProjection projectionA = new(snapshotA, pool);
        DottedLedgerProjection projectionB = new(snapshotB, pool);

        using AntiEntropySession<DottedElement> initiator = new(AntiEntropyRole.Initiator, projectionA.Projection.Contract, projectionA.Projection.Items, 64, pool, projectionA.Projection.Context);
        using AntiEntropySession<DottedElement> responder = new(AntiEntropyRole.Responder, projectionB.Projection.Contract, projectionB.Projection.Items, 64, pool, projectionB.Projection.Context);

        RawSeams seamsA = new(projectionA, responder);
        RawSeams seamsB = new(projectionB, initiator);
        Task runInitiator = initiator.RunAsync(seamsA.SendAsync, seamsA.Resolve, serveFetch: null, seamsA.ApplyElementsAsync, RawSeams.ApplyDropsAsync, RawSeams.MergeContextAsync, TestContext.CancellationToken);
        Task runResponder = responder.RunAsync(seamsB.SendAsync, resolveDifference: null, seamsB.Serve, seamsB.ApplyElementsAsync, RawSeams.ApplyDropsAsync, RawSeams.MergeContextAsync, TestContext.CancellationToken);

        //The responder streams only on host triggers (the library adds no timers), so the row supplies a
        //budget of batches far above the one-item difference's decode need — generous slack, not a tuning.
        const int RawSessionTriggerBudget = 80;
        for(int i = 0; i < RawSessionTriggerBudget; i++)
        {
            await responder.TriggerBatchAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }

        await runInitiator.ConfigureAwait(false);
        responder.Complete();
        await runResponder.ConfigureAwait(false);

        Assert.AreEqual(AntiEntropySessionState.Completed, initiator.State, "The raw initiator completes.");
        Assert.IsTrue(initiator.IsConverged, "The raw initiator converges.");
        Assert.AreEqual(1, seamsA.PushDropCount, "The initiator answers its tombstoned entry as one push-drop.");
    }

    /// <summary>Temporary raw-session seams: cross-submitting sends and the minimal uniform-apply classification.</summary>
    private sealed class RawSeams(DottedLedgerProjection projection, AntiEntropySession<DottedElement> peer)
    {
        /// <summary>The local pinned projection.</summary>
        private DottedLedgerProjection Projection { get; } = projection;

        /// <summary>The peer session inbound envelopes are cross-submitted to.</summary>
        private AntiEntropySession<DottedElement> Peer { get; } = peer;

        /// <summary>The push-drop dots answered.</summary>
        public int PushDropCount { get; private set; }

        /// <summary>Cross-submits one envelope to the peer session.</summary>
        /// <param name="envelope">The envelope.</param>
        /// <param name="cancellationToken">Cancels the submit.</param>
        /// <returns>The submit task.</returns>
        public ValueTask SendAsync(ReconciliationEnvelope<DottedElement> envelope, CancellationToken cancellationToken)
        {
            return Peer.SubmitAsync(envelope, cancellationToken);
        }

        /// <summary>The minimal classification: resolvable-and-covered is a local drop, resolvable-and-uncovered a push, unresolvable a fetch.</summary>
        /// <param name="decodedItems">The decoded difference.</param>
        /// <param name="peerContextState">The peer's exchanged context.</param>
        /// <returns>The resolution.</returns>
        public ReconciliationDifferenceResolution<DottedElement> Resolve(System.Collections.Generic.IReadOnlyList<ReadOnlyMemory<byte>> decodedItems, VectorClockState peerContextState)
        {
            CausalContext peerContext = DottedLedgerProjection.ToCausalContext(peerContextState);
            System.Collections.Immutable.ImmutableArray<ReadOnlyMemory<byte>>.Builder fetch = System.Collections.Immutable.ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>();
            System.Collections.Immutable.ImmutableArray<ReconciliationElementEntry<DottedElement>>.Builder push = System.Collections.Immutable.ImmutableArray.CreateBuilder<ReconciliationElementEntry<DottedElement>>();
            System.Collections.Immutable.ImmutableArray<DotState>.Builder drops = System.Collections.Immutable.ImmutableArray.CreateBuilder<DotState>();
            foreach(ReadOnlyMemory<byte> item in decodedItems)
            {
                if(Projection.Projection.TryResolve(item, out DottedElement? entry))
                {
                    if(peerContext.Covers(DottedLedgerProjection.ToCausalDot(entry)))
                    {
                        drops.Add(new DotState(entry.Replica, entry.Counter));
                    }
                    else
                    {
                        push.Add(new ReconciliationElementEntry<DottedElement>(item, entry));
                    }
                }
                else
                {
                    fetch.Add(item);
                }
            }

            return new ReconciliationDifferenceResolution<DottedElement>(fetch.DrainToImmutable(), push.DrainToImmutable(), drops.DrainToImmutable());
        }

        /// <summary>Serves a fetch from the pinned projection; the concrete list binds covariantly to the fetch-serve delegate.</summary>
        /// <param name="items">The requested items.</param>
        /// <returns>The served entries.</returns>
        public System.Collections.Generic.List<ReconciliationElementEntry<DottedElement>> Serve(System.Collections.Generic.IReadOnlyList<ReadOnlyMemory<byte>> items)
        {
            System.Collections.Generic.List<ReconciliationElementEntry<DottedElement>> served = new(items.Count);
            foreach(ReadOnlyMemory<byte> item in items)
            {
                Assert.IsTrue(Projection.Projection.TryResolve(item, out DottedElement? entry), "The raw serve resolves every requested item.");
                served.Add(new ReconciliationElementEntry<DottedElement>(item, entry!));
            }

            return served;
        }

        /// <summary>The minimal uniform apply: covered dots answer as push-drops, the rest are recorded adopts.</summary>
        /// <param name="entries">The entries to apply.</param>
        /// <param name="peerContextState">The peer's exchanged context.</param>
        /// <param name="cancellationToken">Unused.</param>
        /// <returns>The push-drop dots.</returns>
        public ValueTask<System.Collections.Immutable.ImmutableArray<DotState>> ApplyElementsAsync(System.Collections.Generic.IReadOnlyList<ReconciliationElementEntry<DottedElement>> entries, VectorClockState peerContextState, CancellationToken cancellationToken)
        {
            System.Collections.Immutable.ImmutableArray<DotState>.Builder pushDrops = System.Collections.Immutable.ImmutableArray.CreateBuilder<DotState>();
            foreach(ReconciliationElementEntry<DottedElement> entry in entries)
            {
                if(Projection.SnapshotContext.Covers(DottedLedgerProjection.ToCausalDot(entry.Element)))
                {
                    pushDrops.Add(new DotState(entry.Element.Replica, entry.Element.Counter));
                }
            }

            PushDropCount += pushDrops.Count;

            return new ValueTask<System.Collections.Immutable.ImmutableArray<DotState>>(pushDrops.DrainToImmutable());
        }

        /// <summary>Records drops as applied.</summary>
        /// <param name="dots">The dots to drop.</param>
        /// <param name="peerContextState">The peer's exchanged context.</param>
        /// <param name="cancellationToken">Unused.</param>
        /// <returns>A completed task.</returns>
        public static ValueTask ApplyDropsAsync(System.Collections.Generic.IReadOnlyList<DotState> dots, VectorClockState peerContextState, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        /// <summary>Records the terminal fold.</summary>
        /// <param name="peerContextState">The peer's exchanged context.</param>
        /// <param name="cancellationToken">Unused.</param>
        /// <returns>A completed task.</returns>
        public static ValueTask MergeContextAsync(VectorClockState peerContextState, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The reverse direction: the replica still HOLDING the retracted triple initiates; the peer's exchanged context proves the entry observed-and-removed, so the initiator drops it locally — retraction knowledge propagates whichever side dials.</summary>
    [TestMethod]
    public async Task TheHoldingSideInitiatingDropsItsOwnObservedRemovedEntry()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-reverse-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 2, [("a", "b"), ("a", "c")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    await DeleteAsync(replicaA, [("a", "c")], TestContext.CancellationToken).ConfigureAwait(false);

                    DottedReconcileOutcome outcome = await ExchangeAsync(replicaB, replicaA, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Converged, outcome.Kind, "The reverse exchange converges.");
                    Assert.AreEqual(1, outcome.AdoptedDrops, "The initiator drops its own observed-removed entry.");
                    Assert.AreEqual(0, outcome.AdoptedAdditions, "Nothing is adopted; the difference was one tombstoned entry.");

                    Assert.IsFalse(await HoldsAsync(replicaB, "a", "c", TestContext.CancellationToken).ConfigureAwait(false), "The retraction propagated to the holding side.");
                    Assert.IsFalse(await HoldsAsync(replicaA, "a", "c", TestContext.CancellationToken).ConfigureAwait(false), "The retracting side is unchanged.");
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Add-wins over assertion events: a retraction on one replica concurrent with a NET re-assertion on the other (a retract-then-reinsert, minting a fresh dot) leaves the triple standing on both replicas under exactly the fresh dot — a retraction cannot cancel an assertion event it never observed.</summary>
    [TestMethod]
    public async Task AddWinsAConcurrentFreshAssertSurvivesTheRetraction()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-addwins-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 2, [("a", "x")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    //Concurrent histories: A retracts; B retracts AND re-asserts — a NET addition minting a
                    //fresh dot on B's axis, the assertion event A's retraction never observed.
                    await DeleteAsync(replicaA, [("a", "x")], TestContext.CancellationToken).ConfigureAwait(false);
                    await DeleteAsync(replicaB, [("a", "x")], TestContext.CancellationToken).ConfigureAwait(false);
                    await InsertAsync(replicaB, [("a", "x")], TestContext.CancellationToken).ConfigureAwait(false);

                    DottedReconcileOutcome outcome = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Converged, outcome.Kind, "The exchange converges.");
                    Assert.AreEqual(1, outcome.AdoptedAdditions, "The fresh assertion event is adopted.");

                    Assert.IsTrue(await HoldsAsync(replicaA, "a", "x", TestContext.CancellationToken).ConfigureAwait(false), "The concurrent fresh assert survives on the retracting replica — add-wins.");
                    Assert.IsTrue(await HoldsAsync(replicaB, "a", "x", TestContext.CancellationToken).ConfigureAwait(false), "The asserting replica keeps the triple.");

                    DottedLedgerSnapshot snapshot = replicaA.CommitLedger!.Snapshot();
                    foreach(DottedTripleAssignment entry in snapshot.Entries)
                    {
                        foreach(CausalDot dot in entry.Dots)
                        {
                            Assert.IsTrue(snapshot.Context.Covers(dot), "The context dominates every present dot — the standing invariant.");
                        }
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Three replicas, pairwise exchanges only: a retraction on the first reaches the third TRANSITIVELY through the middle one, and a final exchange moves nothing. The protocol is identical pairwise anti-entropy whatever the topology — ring, star, or mesh; a geo-distributed deployment differs only in which peers are routed to and how often, which changes convergence latency, never the outcome.</summary>
    [TestMethod]
    public async Task ThreeReplicasConvergeTransitivelyThroughPairwiseExchanges()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-threeway-").FullName;
        try
        {
            //The seed carries every term any later write recombines: writes stay dictionary-stable over the
            //seed's terms — the same-lineage posture the same-epoch dotted binding inherits, whose runtime
            //check is the status surface's term count.
            string[] directories = await SeedLineageAsync(root, 3, [("a", "b"), ("a", "x"), ("z", "z")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    VeritasEngine replicaC = await OpenReplicaAsync(directories[2], AxisC, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                    await using(replicaC.ConfigureAwait(false))
                    {
                        //Divergence at both ends of the chain: A retracts x; C authors a new triple z.
                        await DeleteAsync(replicaA, [("a", "x")], TestContext.CancellationToken).ConfigureAwait(false);
                        await InsertAsync(replicaC, [("a", "z")], TestContext.CancellationToken).ConfigureAwait(false);

                        Assert.AreEqual(DottedReconcileOutcomeKind.Converged, (await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false)).Kind, "The first hop converges.");
                        Assert.AreEqual(DottedReconcileOutcomeKind.Converged, (await ExchangeAsync(replicaB, replicaC, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false)).Kind, "The second hop converges.");
                        Assert.AreEqual(DottedReconcileOutcomeKind.Converged, (await ExchangeAsync(replicaA, replicaC, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false)).Kind, "The third hop converges.");

                        foreach(VeritasEngine replica in new[] { replicaA, replicaB, replicaC })
                        {
                            Assert.IsFalse(await HoldsAsync(replica, "a", "x", TestContext.CancellationToken).ConfigureAwait(false), "The retraction reached every replica transitively and nothing resurrected it.");
                            Assert.IsTrue(await HoldsAsync(replica, "a", "z", TestContext.CancellationToken).ConfigureAwait(false), "The new triple reached every replica.");
                            Assert.IsTrue(await HoldsAsync(replica, "a", "b", TestContext.CancellationToken).ConfigureAwait(false), "The untouched seed stays everywhere.");
                        }

                        Assert.AreEqual(DottedReconcileOutcomeKind.AlreadyConsistent, (await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false)).Kind, "A further exchange moves nothing — the three replicas converged.");
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The persistence round-trip: mint, retract, persist a generation, reopen — and an exchange after the reopen still pushes the surviving addition and resurrects nothing; the recovered ledger carries the tombstone knowledge across the restart.</summary>
    [TestMethod]
    public async Task APersistedReopenExchangesWithoutResurrection()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-persist-").FullName;
        try
        {
            //The seed carries every term the later write recombines — the dictionary-stable posture.
            string[] directories = await SeedLineageAsync(root, 2, [("a", "b"), ("a", "x"), ("y", "y")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine before = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            try
            {
                await InsertAsync(before, [("a", "y")], TestContext.CancellationToken).ConfigureAwait(false);
                await DeleteAsync(before, [("a", "x")], TestContext.CancellationToken).ConfigureAwait(false);
                _ = before.Persist(new FileSystemPersistenceStore(directories[0]));
            }
            finally
            {
                await before.DisposeAsync().ConfigureAwait(false);
            }

            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                Assert.AreEqual(ReplicationCausalityState.RemoveAware, replicaA.ReadReplicationStatus().CausalityState, "The reopen recovers remove-awareness from the persisted causality pair.");

                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    FaultCollector faults = new();
                    DottedReconcileOutcome outcome = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken, faults).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Converged, outcome.Kind, "The post-reopen exchange converges.");
                    Assert.AreEqual(1, outcome.PushedEntries, "The surviving addition is pushed to the peer.");
                    Assert.AreEqual(1, outcome.PushedDropDots, "The peer's copy of the retracted triple is answered as a push-drop.");

                    Assert.IsFalse(await HoldsAsync(replicaA, "a", "x", TestContext.CancellationToken).ConfigureAwait(false), "The pre-persist retraction stands after the reopen and the exchange.");
                    Assert.IsFalse(await HoldsAsync(replicaB, "a", "x", TestContext.CancellationToken).ConfigureAwait(false), "The retraction propagated to the peer.");
                    Assert.IsTrue(await HoldsAsync(replicaB, "a", "y", TestContext.CancellationToken).ConfigureAwait(false), FormattableString.Invariant($"The pre-persist addition propagated to the peer (faults: {faults.Describe()}; B generation={replicaB.CommitLedger!.Generation}; B entries={replicaB.CommitLedger!.Snapshot().Entries.Length}; B covers A1={replicaB.CommitLedger!.Snapshot().Context.Covers(new CausalDot(AxisA, 1))})."));
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The local refusal ladder: a store without a host identity refuses the dotted reconcile as not-remove-aware; a store with an identity awaiting the baseline refuses the same way (the created-without-identity second arm); and a remove-aware store WITHOUT a durable journal refuses as not-durable — the dotted wire exchanges only crash-durable causal history. Every refusal is a value; no connection is ever dialed.</summary>
    [TestMethod]
    public async Task TheLocalRefusalLadderNamesEveryPreDialCondition()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-localrefusals-").FullName;
        try
        {
            //Arm 1: no identity at all — add-only.
            VeritasEngine addOnly = await OpenAddOnlyReplicaAsync(Path.Combine(root, "addonly"), TestContext.CancellationToken).ConfigureAwait(false);
            await using(addOnly.ConfigureAwait(false))
            {
                CountingConnectionFactory neverDialed = new();
                DottedReconcileOutcome outcome = await addOnly.ReconcileRemoveAwareFromPeerAsync(neverDialed.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(DottedReconcileOutcomeKind.LocalNotRemoveAware, outcome.Kind, "An identity-less store refuses by name.");
                Assert.AreEqual(0, neverDialed.Opened, "A local refusal never dials.");
            }

            //Arm 2 (the creation-baseline row's second arm): a store CREATED without identity stays add-only
            //when later opened WITH one — awaiting the explicit baseline step, and refusing the dotted lane by
            //the same name until it runs.
            string awaitingDirectory = Path.Combine(root, "awaiting");
            VeritasEngine created = await OpenAddOnlyReplicaAsync(awaitingDirectory, TestContext.CancellationToken).ConfigureAwait(false);
            try
            {
                await InsertAsync(created, [("a", "b")], TestContext.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await created.DisposeAsync().ConfigureAwait(false);
            }

            VeritasEngine awaiting = await OpenReplicaAsync(awaitingDirectory, AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(awaiting.ConfigureAwait(false))
            {
                Assert.AreEqual(ReplicationCausalityState.AwaitingBaseline, awaiting.ReadReplicationStatus().CausalityState, "An identity-supplied open of a pre-causality store awaits the explicit baseline.");
                CountingConnectionFactory neverDialed = new();
                DottedReconcileOutcome outcome = await awaiting.ReconcileRemoveAwareFromPeerAsync(neverDialed.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(DottedReconcileOutcomeKind.LocalNotRemoveAware, outcome.Kind, "An awaiting-baseline store refuses the dotted lane by name until the explicit step runs.");
                Assert.AreEqual(0, neverDialed.Opened, "A local refusal never dials.");
            }

            //Arm 3: remove-aware but journal-less — an in-memory create with identity has no durable journal,
            //so its dots would not survive a crash and the wire refuses them.
            VeritasEngine warm = await VeritasEngine.OpenMutableAsync(Array.Empty<DataTriple>(), new VeritasEngineOptions { Reasoning = null, ReplicaIdentity = WarmAxis }, TestContext.CancellationToken).ConfigureAwait(false);
            await using(warm.ConfigureAwait(false))
            {
                Assert.AreEqual(ReplicationCausalityState.RemoveAware, warm.ReadReplicationStatus().CausalityState, "The warm store is remove-aware locally.");
                CountingConnectionFactory neverDialed = new();
                DottedReconcileOutcome outcome = await warm.ReconcileRemoveAwareFromPeerAsync(neverDialed.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(DottedReconcileOutcomeKind.LocalNotDurable, outcome.Kind, "A journal-less remove-aware store refuses the wire by name — dots that can cross the wire must be crash-durable.");
                Assert.AreEqual(0, neverDialed.Opened, "A local refusal never dials.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A connection factory that counts opens and always throws — the never-dialed assertion's witness.</summary>
    private sealed class CountingConnectionFactory
    {
        /// <summary>The number of open attempts.</summary>
        public int Opened { get; private set; }

        /// <summary>Counts and refuses the open.</summary>
        /// <param name="cancellationToken">Unread.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Always.</exception>
        public ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            Opened++;

            throw new IOException("The battery expected no dial.");
        }
    }

    /// <summary>The peer-side named refusals: an add-only peer's serve declines with NotRemoveAware on the reply header, and a journal-less remove-aware peer's serve declines with NotDurable — the requesting operator sees a name, never a silent close.</summary>
    [TestMethod]
    public async Task AnUnsupportingPeerDeclinesByNameOnTheReplyHeader()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-peerdecline-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 1, [("a", "b")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                VeritasEngine addOnlyPeer = await VeritasEngine.OpenMutableAsync(Array.Empty<DataTriple>(), new VeritasEngineOptions { Reasoning = null }, TestContext.CancellationToken).ConfigureAwait(false);
                await using(addOnlyPeer.ConfigureAwait(false))
                {
                    DottedReconcileOutcome declined = await ExchangeAsync(replicaA, addOnlyPeer, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.PeerDeclined, declined.Kind, "The add-only peer declines rather than closing silently.");
                    Assert.AreEqual(DottedDifferenceDeclineReason.NotRemoveAware, declined.PeerDeclineReason, "The decline carries its name.");
                }

                VeritasEngine warmPeer = await VeritasEngine.OpenMutableAsync(Array.Empty<DataTriple>(), new VeritasEngineOptions { Reasoning = null, ReplicaIdentity = WarmAxis }, TestContext.CancellationToken).ConfigureAwait(false);
                await using(warmPeer.ConfigureAwait(false))
                {
                    DottedReconcileOutcome declined = await ExchangeAsync(replicaA, warmPeer, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.PeerDeclined, declined.Kind, "The journal-less peer declines rather than closing silently.");
                    Assert.AreEqual(DottedDifferenceDeclineReason.NotDurable, declined.PeerDeclineReason, "The decline names the durability gate.");
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The unknown-selector split, client side: the peer's EXPLICIT refusal signal yields the remove-aware-unsupported outcome, while a peer that dies without answering yields peer-unavailable — the split is never inferred from silence.</summary>
    [TestMethod]
    public async Task TheRefusalSignalAndPeerDeathAreDistinctOutcomes()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-split-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 1, [("a", "b")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                DottedReconcileOutcome refused = await replicaA.ReconcileRemoveAwareFromPeerAsync(RefusedFactory.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(DottedReconcileOutcomeKind.PeerRemoveAwareUnsupported, refused.Kind, "The explicit refusal signal is the ONE evidence for the unsupported outcome.");

                DottedReconcileOutcome dead = await replicaA.ReconcileRemoveAwareFromPeerAsync(DeadFactory.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(DottedReconcileOutcomeKind.PeerUnavailable, dead.Kind, "An absent reply is peer death, never inferred as unsupported.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A connection factory whose peer answered the explicit unknown-service refusal.</summary>
    private static class RefusedFactory
    {
        /// <summary>Raises the typed refusal signal.</summary>
        /// <param name="cancellationToken">Unread.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="PeerServiceRefusedException">Always.</exception>
        public static ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            throw new PeerServiceRefusedException();
        }
    }

    /// <summary>A connection factory whose peer died without answering.</summary>
    private static class DeadFactory
    {
        /// <summary>Raises the ordinary I/O fault.</summary>
        /// <param name="cancellationToken">Unread.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Always.</exception>
        public static ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            throw new IOException("The peer died without answering.");
        }
    }

    /// <summary>The identity-collision tripwire: a peer presenting causal coverage on THIS replica's own axis beyond its own maximum proves a second minter under the same identity, and the exchange refuses by name before the colliding knowledge reaches the session.</summary>
    [TestMethod]
    public async Task AForgedContextBeyondTheOwnAxisMaximumIsRefusedByName()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-tripwire-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 1, [("a", "b")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                ForgedContextPeer forged = new(replicaA.Dictionary.Epoch, replicaA.CommitLedger!.Identity, forgedCounter: 1000);
                DottedReconcileOutcome outcome = await replicaA.ReconcileRemoveAwareFromPeerAsync(forged.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(DottedReconcileOutcomeKind.IdentityCollision, outcome.Kind, "Coverage beyond the own-axis maximum proves a second minter and refuses by name.");
                Assert.AreEqual(0, outcome.AdoptedAdditions, "Nothing applied.");
                Assert.AreEqual(0, outcome.AdoptedDrops, "Nothing dropped.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A hand-rolled peer whose serve accepts the exchange and then presents a causal context claiming coverage on the DIALING replica's own identity axis beyond its maximum — the second-minter evidence the tripwire refuses.</summary>
    /// <param name="dictionaryEpoch">The epoch the reply declares, matching the dialing replica's.</param>
    /// <param name="localAxis">The dialing replica's own identity axis the forged coverage claims.</param>
    /// <param name="forgedCounter">The forged coverage counter, beyond any real maximum.</param>
    private sealed class ForgedContextPeer(ulong dictionaryEpoch, ReplicaAxis localAxis, int forgedCounter)
    {
        /// <summary>The epoch the reply declares.</summary>
        private ulong DictionaryEpoch { get; } = dictionaryEpoch;

        /// <summary>The dialing replica's own identity axis.</summary>
        private ReplicaAxis LocalAxis { get; } = localAxis;

        /// <summary>The forged coverage counter.</summary>
        private int ForgedCounter { get; } = forgedCounter;

        /// <summary>Opens a connection whose serve replies accepted and then sends the forged context.</summary>
        /// <param name="cancellationToken">Cancels the serve.</param>
        /// <returns>The connection.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the serve-completion transport it owns) transfers to the caller per the OpenPeerDottedConnectionDelegate contract; the dotted client disposes it unconditionally on every exit.")]
        public ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            Task serve = ServeForgedAsync(requestPipe.Reader, responsePipe.Writer, cancellationToken);

            return new ValueTask<PeerChannelConnection>(new PeerChannelConnection(requestPipe.Writer, responsePipe.Reader, new SwallowedServe(serve)));
        }

        /// <summary>Writes the accepted reply header, then the forged context envelope, then holds the stream open until the client tears down.</summary>
        /// <param name="requestReader">The request pipe; drained and discarded.</param>
        /// <param name="responseWriter">The response pipe the frames are written to.</param>
        /// <param name="cancellationToken">Cancels the serve.</param>
        /// <returns>The serve task.</returns>
        private async Task ServeForgedAsync(PipeReader requestReader, PipeWriter responseWriter, CancellationToken cancellationToken)
        {
            DottedDifferenceFraming<DottedElement> framing = new(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
            MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer = new(responseWriter, framing.WriteFrame, MessageChannel.DefaultMaxFrameLength);
            await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForReplyHeader(new DottedDifferenceReplyHeader(Accepted: true, DictionaryEpoch, ReconciliationOffer.FromContract(DottedReconciliationContract.Value), DottedDifferenceDeclineReason.None)), cancellationToken).ConfigureAwait(false);

            VectorClockState forged = new([new ReplicaCounterEntry([.. LocalAxis.Bytes.Span], ForgedCounter)]);
            await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForEnvelope(ReconciliationEnvelope<DottedElement>.ForContext(new ReconciliationContext(forged))), cancellationToken).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            await requestReader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A mid-session posture violation — a drop frame arriving at an initiator — converts to the named protocol-fault outcome, never a silent wind-down counted as success.</summary>
    [TestMethod]
    public async Task AMidSessionPostureViolationConvertsToAProtocolFault()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-violation-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 1, [("a", "b")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                DropIntoInitiatorPeer violating = new(replicaA.Dictionary.Epoch);
                DottedReconcileOutcome outcome = await replicaA.ReconcileRemoveAwareFromPeerAsync(violating.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(DottedReconcileOutcomeKind.ProtocolFault, outcome.Kind, "A drop into an initiator is a genuine posture violation, named as a protocol fault.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A hand-rolled peer whose serve accepts and then sends a drop frame — illegal into an initiator — under a FOREIGN axis so the tripwire passes and the session's own posture guard fires.</summary>
    /// <param name="dictionaryEpoch">The epoch the reply declares, matching the dialing replica's.</param>
    private sealed class DropIntoInitiatorPeer(ulong dictionaryEpoch)
    {
        /// <summary>The epoch the reply declares.</summary>
        private ulong DictionaryEpoch { get; } = dictionaryEpoch;

        /// <summary>Opens a connection whose serve replies accepted and then sends the illegal drop.</summary>
        /// <param name="cancellationToken">Cancels the serve.</param>
        /// <returns>The connection.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the serve-completion transport it owns) transfers to the caller per the OpenPeerDottedConnectionDelegate contract; the dotted client disposes it unconditionally on every exit.")]
        public ValueTask<PeerChannelConnection> OpenAsync(CancellationToken cancellationToken)
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            Task serve = ServeViolationAsync(requestPipe.Reader, responsePipe.Writer, cancellationToken);

            return new ValueTask<PeerChannelConnection>(new PeerChannelConnection(requestPipe.Writer, responsePipe.Reader, new SwallowedServe(serve)));
        }

        /// <summary>Writes the accepted reply header, then the illegal drop envelope.</summary>
        /// <param name="requestReader">The request pipe; drained and discarded.</param>
        /// <param name="responseWriter">The response pipe the frames are written to.</param>
        /// <param name="cancellationToken">Cancels the serve.</param>
        /// <returns>The serve task.</returns>
        private async Task ServeViolationAsync(PipeReader requestReader, PipeWriter responseWriter, CancellationToken cancellationToken)
        {
            DottedDifferenceFraming<DottedElement> framing = new(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
            MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer = new(responseWriter, framing.WriteFrame, MessageChannel.DefaultMaxFrameLength);
            await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForReplyHeader(new DottedDifferenceReplyHeader(Accepted: true, DictionaryEpoch, ReconciliationOffer.FromContract(DottedReconciliationContract.Value), DottedDifferenceDeclineReason.None)), cancellationToken).ConfigureAwait(false);

            await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForEnvelope(ReconciliationEnvelope<DottedElement>.ForDrop(new ReconciliationDrop([new DotState([.. ForeignAxis.Bytes.Span], 1)]))), cancellationToken).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            await requestReader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Joins a fake peer's serve on disposal; the fakes' serves never fault.</summary>
    /// <param name="serve">The serve task.</param>
    private sealed class SwallowedServe(Task serve): IAsyncDisposable
    {
        /// <summary>Joins the serve.</summary>
        /// <returns>The serve task as a value task.</returns>
        public ValueTask DisposeAsync()
        {
            return new ValueTask(serve);
        }
    }

    /// <summary>The durable-prefix posture: an exchange whose completion frame is torn away mid-session leaves the responder holding the already-committed prefix — the pushed triple present, its causal knowledge folded with the same commit, nothing torn — and re-running the session converges with no double-apply.</summary>
    [TestMethod]
    public async Task AnInterruptedExchangeLeavesAConsistentPrefixAndARerunConverges()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-interrupt-").FullName;
        try
        {
            //The seed carries every term the later write recombines — the dictionary-stable posture.
            string[] directories = await SeedLineageAsync(root, 2, [("a", "b"), ("y", "y")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    await InsertAsync(replicaA, [("a", "y")], TestContext.CancellationToken).ConfigureAwait(false);

                    //The interrupted exchange: a middleman forwards the initiator's frames but tears the
                    //connection down INSTEAD of delivering the completion frame, so the responder applied the
                    //push (the durable prefix) and never saw the exchange complete.
                    Pipe clientToMiddleman = new();
                    Pipe middlemanToServer = new();
                    Pipe responsePipe = new();
                    Task serve = replicaB.ServeDottedDifferenceAsync(middlemanToServer.Reader, responsePipe.Writer, trace: null, TestContext.CancellationToken);
                    Task middleman = ForwardUntilCompletionAsync(clientToMiddleman.Reader, middlemanToServer.Writer, TestContext.CancellationToken);
                    PipeConnectionFactory factory = new(clientToMiddleman.Writer, responsePipe.Reader, serve);
                    DottedReconcileOutcome interrupted = await replicaA.ReconcileRemoveAwareFromPeerAsync(factory.OpenAsync, RoomySymbolCap, trace: null, TestContext.CancellationToken).ConfigureAwait(false);
                    await middleman.ConfigureAwait(false);

                    Assert.IsTrue(await HoldsAsync(replicaB, "a", "y", TestContext.CancellationToken).ConfigureAwait(false), "The pushed triple committed on the responder before the tear — the durable prefix stands.");
                    DottedLedgerSnapshot prefix = replicaB.CommitLedger!.Snapshot();
                    foreach(DottedTripleAssignment entry in prefix.Entries)
                    {
                        foreach(CausalDot dot in entry.Dots)
                        {
                            Assert.IsTrue(prefix.Context.Covers(dot), "The prefix is causally self-consistent: the context dominates every present dot.");
                        }
                    }

                    //The re-run is idempotent: the difference is already applied, so the sets agree and the
                    //adopted triple carries exactly its one original dot — no double-apply.
                    DottedReconcileOutcome rerun = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.AlreadyConsistent, rerun.Kind, FormattableString.Invariant($"The re-run converges with nothing to move (first outcome was {interrupted.Kind})."));
                    foreach(DottedTripleAssignment entry in replicaB.CommitLedger!.Snapshot().Entries)
                    {
                        Assert.HasCount(1, entry.Dots, "No entry carries a duplicated dot after the re-run.");
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Forwards initiator frames to the responder until the completion frame (which it drops), then completes the responder's inbound side — the deterministic mid-session tear.</summary>
    /// <param name="fromInitiator">The initiator's outbound frames.</param>
    /// <param name="toResponder">The responder's inbound side.</param>
    /// <param name="cancellationToken">Cancels the forwarding.</param>
    /// <returns>A task that completes when the tear is done.</returns>
    private static async Task ForwardUntilCompletionAsync(PipeReader fromInitiator, PipeWriter toResponder, CancellationToken cancellationToken)
    {
        DottedDifferenceFraming<DottedElement> framing = new(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
        MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader = new(fromInitiator, framing.ReadFrame, MessageChannel.DefaultMaxFrameLength);
        MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer = new(toResponder, framing.WriteFrame, MessageChannel.DefaultMaxFrameLength);
        await foreach(DottedDifferenceFrame<DottedElement> frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if(frame.Envelope is { Completion: not null })
            {
                break;
            }

            await writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }

        await writer.CompleteAsync().ConfigureAwait(false);
        await fromInitiator.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The baseline lifecycle and the coverage boundary, pinned honestly: two clones of one ADD-ONLY lineage
    /// baseline INDEPENDENTLY, so their shared triples carry different dots — the first dotted exchange is the
    /// full symmetric difference in the dotted domain, which a small symbol cap bounds into the named
    /// interrupted refusal (the storm made loud, never an unbounded exchange). With room it converges as a dot
    /// union. A retraction from BEFORE the baselines is outside observed-remove knowledge and comes back on
    /// that first exchange — knowledge that never existed cannot be claimed, exactly as the add-only regime
    /// resurrects on EVERY exchange — while a retraction AFTER the baseline survives the identical exchange:
    /// from the baseline onward every retraction is protected.
    /// </summary>
    [TestMethod]
    public async Task IndependentBaselinesStormIsCapBoundedAndOnlyPreBaselineRetractionsAreExposed()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-baseline-").FullName;
        try
        {
            //Sized so the independent-baseline symmetric difference (two dotted copies of every shared triple)
            //cannot decode inside one symbol batch: the tiny cap then trips DETERMINISTICALLY, because a decode
            //of 128 items needs more symbols than the single batch the cap admits.
            (string Subject, string Object)[] seeds = new (string, string)[64];
            for(int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = ("s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "o" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            string[] directories = await SeedAddOnlyLineageAsync(root, 2, seeds, TestContext.CancellationToken).ConfigureAwait(false);

            //The PRE-BASELINE retraction: an add-only open retracts one triple before any causality exists.
            VeritasEngine preBaseline = await OpenAddOnlyReplicaAsync(directories[0], TestContext.CancellationToken).ConfigureAwait(false);
            try
            {
                await DeleteAsync(preBaseline, [seeds[0]], TestContext.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await preBaseline.DisposeAsync().ConfigureAwait(false);
            }

            VeritasEngine replicaA = await OpenReplicaAsync(directories[0], AxisA, baseline: true, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replicaA.ConfigureAwait(false))
            {
                Assert.AreEqual(ReplicationBaselineOutcome.Baselined, replicaA.ReplicationBaseline, "The explicit baseline step ran on the pre-causality store.");

                VeritasEngine replicaB = await OpenReplicaAsync(directories[1], AxisB, baseline: true, TestContext.CancellationToken).ConfigureAwait(false);
                await using(replicaB.ConfigureAwait(false))
                {
                    Assert.AreEqual(ReplicationBaselineOutcome.Baselined, replicaB.ReplicationBaseline, "The clone baselined independently.");

                    //The storm, cap-bounded: the whole shared set differs in the dotted domain, and the tiny
                    //cap converts the unbounded exchange into the named interrupted outcome.
                    DottedReconcileOutcome capped = await ExchangeAsync(replicaA, replicaB, symbolCap: 8, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Interrupted, capped.Kind, "The independent-baseline storm trips the cap into the named bounded refusal.");

                    //With room, the first exchange converges as a dot union — and the PRE-baseline retraction
                    //comes back: its removal predates observed-remove knowledge, the documented coverage
                    //boundary (the add-only regime resurrected it on every exchange; the dotted lane does so
                    //once, before protection begins).
                    DottedReconcileOutcome union = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Converged, union.Kind, "The roomy exchange converges the independently-dotted sets.");
                    Assert.IsTrue(await HoldsAsync(replicaA, seeds[0].Subject, seeds[0].Object, TestContext.CancellationToken).ConfigureAwait(false), "The PRE-baseline retraction is outside observed-remove knowledge and comes back — the coverage boundary, stated honestly.");

                    //POST-baseline, the identical scenario is protected: retract, exchange, and the
                    //retraction stands on both replicas.
                    await DeleteAsync(replicaA, [seeds[0]], TestContext.CancellationToken).ConfigureAwait(false);
                    DottedReconcileOutcome protectedExchange = await ExchangeAsync(replicaA, replicaB, RoomySymbolCap, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(DottedReconcileOutcomeKind.Converged, protectedExchange.Kind, "The post-baseline exchange converges.");
                    Assert.IsFalse(await HoldsAsync(replicaA, seeds[0].Subject, seeds[0].Object, TestContext.CancellationToken).ConfigureAwait(false), "The post-baseline retraction stands on the retracting side.");
                    Assert.IsFalse(await HoldsAsync(replicaB, seeds[0].Subject, seeds[0].Object, TestContext.CancellationToken).ConfigureAwait(false), "The post-baseline retraction propagated and stands on the peer — from the baseline onward every retraction is protected.");

                    //Both contexts now dominate both baseline axes — the dot union is durable knowledge.
                    Assert.IsTrue(replicaA.CommitLedger!.Snapshot().Context.Covers(new CausalDot(AxisB, 1)), "The initiator's context covers the peer's baseline axis.");
                    Assert.IsTrue(replicaB.CommitLedger!.Snapshot().Context.Covers(new CausalDot(AxisA, 1)), "The responder's context covers the initiator's baseline axis.");
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>An out-of-range symbol cap is declined by name before any envelope crosses: the cap ceiling keeps both endpoints' trigger-budget and wind-down arithmetic inside the integer range, so a hostile or mistaken cap wraps into a named refusal, never a hung or falsely wound-down exchange.</summary>
    [TestMethod]
    public async Task AnOutOfRangeSymbolCapIsDeclinedByName()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dotted-capbound-").FullName;
        try
        {
            string[] directories = await SeedLineageAsync(root, 1, [("a", "b")], TestContext.CancellationToken).ConfigureAwait(false);
            VeritasEngine replica = await OpenReplicaAsync(directories[0], AxisA, baseline: false, TestContext.CancellationToken).ConfigureAwait(false);
            await using(replica.ConfigureAwait(false))
            {
                Pipe requestPipe = new();
                Pipe responsePipe = new();
                Task serve = replica.ServeDottedDifferenceAsync(requestPipe.Reader, responsePipe.Writer, trace: null, TestContext.CancellationToken);

                //The raw request header carries a cap above the ceiling — a frame the channel client refuses
                //to send, so only a foreign implementation can produce it; the serve answers it by name.
                DottedDifferenceFraming<DottedElement> framing = new(DottedReconciliationContract.Value, DottedLedgerProjection.WriteElement, DottedLedgerProjection.ReadElement);
                MessageChannelWriter<DottedDifferenceFrame<DottedElement>> writer = new(requestPipe.Writer, framing.WriteFrame, MessageChannel.DefaultMaxFrameLength);
                await writer.WriteAsync(DottedDifferenceFrame<DottedElement>.ForRequestHeader(new DottedDifferenceRequestHeader(replica.Dictionary.Epoch, ReconciliationOffer.FromContract(DottedReconciliationContract.Value), int.MaxValue)), TestContext.CancellationToken).ConfigureAwait(false);

                MessageChannelReader<DottedDifferenceFrame<DottedElement>> reader = new(responsePipe.Reader, framing.ReadFrame, MessageChannel.DefaultMaxFrameLength);
                DottedDifferenceReplyHeader? reply = null;
                await foreach(DottedDifferenceFrame<DottedElement> frame in reader.ReadAllAsync(TestContext.CancellationToken).ConfigureAwait(false))
                {
                    reply = frame.ReplyHeader;

                    break;
                }

                Assert.IsNotNull(reply, "The serve answers the out-of-range request with a reply header.");
                Assert.IsFalse(reply.Accepted, "An out-of-range symbol cap is refused.");
                Assert.AreEqual(DottedDifferenceDeclineReason.SymbolCapInvalid, reply.DeclineReason, "The refusal names the invalid cap.");

                await writer.CompleteAsync().ConfigureAwait(false);
                await serve.ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
