using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;
using Lumoin.Verisync.Json;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The metadata plane's durable home: a consensus host state persisted through the store's persist face loads
/// back and reconstructs a host whose committed record VALUE-equals what was written — the record crossed a
/// codec, so equality here is structural and never reference identity. An empty directory is the fresh-host
/// answer and reports itself as a value; a state file that is present but torn is an invariant violation and is
/// refused loudly by the codec rather than read as a fresh host. Beside the node state the confirmed-facts
/// record round-trips through its own fixed layout, answers the routine-reopen question from the two facts it
/// carries, and fails closed on any byte pattern that layout could not have written. The durability point
/// itself is observable: a flush that refuses faults the persist and publishes nothing, and a write that
/// succeeds has invoked both the content flush and the directory barrier. Beyond what this store's own codec
/// refuses, a payload the codec accepted is refused one layer up by the host's restore when the copies it
/// carries disagree with the record beside them, which is the tear a snapshot written in two parts leaves.
/// </summary>
[TestClass]
internal sealed class MetadataNodeStoreTests
{
    /// <summary>The prefix every temporary store directory in this battery is created under.</summary>
    private const string DirectoryPrefix = "veritas-metadata-store-";

    /// <summary>The causality digest the persisted baseline carries; above two to the fifty-third, so a codec that routed it through a double would lose it.</summary>
    private const ulong CausalityDigestValue = 0x9E3779B97F4A7C15UL;

    /// <summary>The dataset StateId the persisted baseline's confirmation carries; likewise above two to the fifty-third.</summary>
    private const ulong StateIdValue = 0xFEEDFACECAFEBEEFUL;

    /// <summary>The term-dictionary epoch the persisted baseline's confirmation carries.</summary>
    private const long DictionaryEpochValue = 42L;

    /// <summary>The MSTest-supplied per-test context, read for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A persisted host state loads back and reconstructs a host whose committed record equals the one that was written, by value across the codec rather than by reference.</summary>
    [TestMethod]
    public async Task PersistedNodeStateRestoresValueEqualThroughTheCodec()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataPlaneDeployment deployment = Deployment();
            ReplicaAxis selfAxis = deployment.Founders[0].Axis;
            HostId selfHost = deployment.Founders[0].ToHostId();
            ReplicaId self = selfHost.Replica;
            VersionedValue<VeritasMetadataRecord> committed = new(RegisterVersion.First, self, deployment.Genesis, RecordFor(selfAxis));
            QuePaxaVersionedNodeState<VeritasMetadataRecord> state = StateFor(deployment, committed);

            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);
            await store.PersistNode(state, TestContext.CancellationToken).ConfigureAwait(false);

            QuePaxaVersionedNodeState<VeritasMetadataRecord>? restoredState = await store.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(restoredState, "A persisted state loads back rather than reporting a fresh host.");
            Assert.AreEqual(state.RecorderVersion, restoredState!.RecorderVersion, "The restored snapshot serves the version the persisted one served.");
            Assert.AreEqual(state.ActiveConfiguration, restoredState.ActiveConfiguration, "The restored snapshot carries the membership the persisted one carried.");

            //FromState is the safety net the store leans on: it re-derives the leader, the version and the
            //membership from the restored record and refuses a snapshot whose stored copies disagree.
            QuePaxaVersionedNode<VeritasMetadataRecord> restored = QuePaxaVersionedNode<VeritasMetadataRecord>.FromState(deployment.Genesis, selfHost, restoredState);
            VersionedValue<VeritasMetadataRecord>? restoredCommitted = restored.Committed;
            Assert.IsNotNull(restoredCommitted, "The restored host holds the committed record the snapshot carried.");
            Assert.AreEqual(committed.Version, restoredCommitted!.Version, "The restored record stands at the version it was written at.");
            Assert.AreEqual(committed.Value, restoredCommitted.Value, "The restored coordinated record equals the persisted one by value.");
            Assert.AreNotSame(committed.Value, restoredCommitted.Value, "The record came back through the codec, so the equality above is structural and not reference identity.");
            Assert.AreEqual(committed.Value.Baseline, restoredCommitted.Value.Baseline, "The two-phase baseline, its digest and its confirmation survive the round trip.");
            Assert.AreEqual(committed, restoredCommitted, "The whole decided record — version, writer, membership and value — equals what was persisted.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// A state this store persisted and loaded back whole is refused by the HOST'S OWN RESTORE once one of the
    /// copies it carries disagrees with the record beside it: a recorder serving another instance's version, a
    /// configured leader other than the one the restored record derives, and a membership other than the one that
    /// record implies. The same state untorn restores, so each refusal is the tear and never the artifact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusals are the consensus library's cross-checks and the reason this store's artifact is ONE durable
    /// write of five fields rather than two writes of some of them: a host that wrote the record and the register
    /// separately and crashed between them comes back holding a register from one instance beside a record from
    /// another, and that is exactly the pairing torn here. The sibling rows above pin what this store's own codec
    /// refuses; this one pins what the restore refuses about a payload the codec accepted.
    /// </para>
    /// <para>
    /// The membership tear is the one a snapshot written in two parts across a reconfiguration leaves behind, and
    /// it is torn over the SAME chain: a configuration naming another chain is refused on a different rule — a
    /// store attached to the wrong cluster — which would pass this row while proving nothing about the tear.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TornNodeStateIsRefusedByTheHostsOwnRestore()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataPlaneDeployment deployment = Deployment();
            ReplicaAxis selfAxis = deployment.Founders[0].Axis;
            HostId selfHost = deployment.Founders[0].ToHostId();
            ReplicaId self = selfHost.Replica;
            VersionedValue<VeritasMetadataRecord> committed = new(RegisterVersion.First, self, deployment.Genesis, RecordFor(selfAxis));

            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);
            await store.PersistNode(StateFor(deployment, committed), TestContext.CancellationToken).ConfigureAwait(false);

            QuePaxaVersionedNodeState<VeritasMetadataRecord>? restored = await store.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(restored, "The persisted state loads back, which is what gives the tears below something whole to be torn from.");

            //The control: what this store wrote restores, so each refusal below is about the field that was
            //torn and not about a payload the restore would have refused either way.
            QuePaxaVersionedNode<VeritasMetadataRecord> whole = QuePaxaVersionedNode<VeritasMetadataRecord>.FromState(deployment.Genesis, selfHost, restored!);
            Assert.AreEqual(committed.Version, whole.Committed!.Version, "The untorn state restores a host holding the record it was persisted with.");

            QuePaxaVersionedNodeState<VeritasMetadataRecord> tornVersion = restored! with { RecorderVersion = restored!.RecorderVersion.Next() };
            ArgumentException? refusedVersion = RestoreRefusalOf(deployment, selfHost, tornVersion);
            Assert.IsNotNull(
                refusedVersion,
                "A recorder serving a version other than the one the restored record implies is a register from one instance beside a record from another, and the restore refuses it rather than starting on it.");
            Assert.Contains(
                "must serve the version after",
                refusedVersion!.Message,
                "The refusal names the rule it broke: the version a host serves is derived from the record it holds.");

            //A leader other than the derived one is the divergence hazard the derivation exists to close: two
            //hosts whose records imply different leaders for one instance admit two reserved claims.
            QuePaxaVersionedNodeState<VeritasMetadataRecord> tornLeader = restored! with
            {
                ConfiguredLeader = new ProposerLane(MetadataPlaneDeployment.ReplicaIdFor(deployment.Founders[1].Axis), 0)
            };

            ArgumentException? refusedLeader = RestoreRefusalOf(deployment, selfHost, tornLeader);
            Assert.IsNotNull(
                refusedLeader,
                "A configured leader other than the one the restored record derives is refused rather than joined as a second leader on one instance.");
            Assert.Contains(
                "configured leader must be the one the schedule derives",
                refusedLeader!.Message,
                "The refusal names the rule it broke: the leader is a function of the record the host holds.");

            //The membership of the SAME chain minus a founder: the record beside it implies the genesis three, so
            //this is the two-part-write tear rather than a store attached to the wrong cluster.
            QuePaxaVersionedNodeState<VeritasMetadataRecord> tornMembership = restored! with
            {
                ActiveConfiguration = QuePaxaConfiguration.Create(
                    deployment.Cluster,
                    [deployment.Founders[0].ToHostId(), deployment.Founders[1].ToHostId()])
            };

            ArgumentException? refusedMembership = RestoreRefusalOf(deployment, selfHost, tornMembership);
            Assert.IsNotNull(
                refusedMembership,
                "A membership other than the one the restored record implies is a register from one instance beside a configuration from another, and the restore refuses it rather than serving a quorum counted over the wrong member set.");
            Assert.Contains(
                "must be the one the restored record implies",
                refusedMembership!.Message,
                "The refusal names the rule it broke: the membership an instance runs under is derived from the record the host holds.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A store over a directory nothing has been persisted into reports a fresh host as a value rather than as a failure.</summary>
    [TestMethod]
    public async Task EmptyDirectoryReportsAFreshHost()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);

            QuePaxaVersionedNodeState<VeritasMetadataRecord>? state = await store.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNull(state, "A host that has persisted nothing is fresh, which is a value and not a failure.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A live state file whose bytes were altered in place is refused by the codec rather than read as a fresh host — a torn store is an invariant violation and is loud.</summary>
    [TestMethod]
    public async Task ByteFlippedNodeStateIsRefusedLoudly()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);
            await PersistOneStateAsync(store, TestContext.CancellationToken).ConfigureAwait(false);

            string path = Path.Combine(directory, MetadataNodeStore.NodeStateFileName);
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsGreaterThan(64, bytes.Length, "The persisted state is long enough for the corrupted window to fall inside it.");

            //Every byte of the window is inverted, which puts it outside the range a structural character, a
            //digit or a well-formed encoded character can occupy, so the payload cannot decode as some other
            //valid state.
            for(int index = bytes.Length / 3; index < (bytes.Length / 3) + 16; index++)
            {
                bytes[index] ^= 0xFF;
            }

            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            bool refused = false;
            try
            {
                _ = await store.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }
            catch(MessageDeserializationException)
            {
                refused = true;
            }

            Assert.IsTrue(refused, "A state file that is present but unreadable is refused by the codec, never mapped to the fresh-host answer.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A live state file shortened to half its length is refused for the same reason: the store reads the length the file reports, so a shortened file is a complete read of an incomplete payload and the codec is what names it.</summary>
    [TestMethod]
    public async Task TruncatedNodeStateIsRefusedLoudly()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);
            await PersistOneStateAsync(store, TestContext.CancellationToken).ConfigureAwait(false);

            string path = Path.Combine(directory, MetadataNodeStore.NodeStateFileName);
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            byte[] truncated = bytes[..(bytes.Length / 2)];
            await File.WriteAllBytesAsync(path, truncated, TestContext.CancellationToken).ConfigureAwait(false);

            bool refused = false;
            try
            {
                _ = await store.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }
            catch(MessageDeserializationException)
            {
                refused = true;
            }

            Assert.IsTrue(refused, "A truncated state is refused rather than restored from the part that survived.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The confirmed-facts record round-trips through the store's own artifact: what a save wrote is what a load reads back, by value.</summary>
    [TestMethod]
    public async Task ConfirmedFactsRoundTripThroughTheStore()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);
            ConfirmedMetadataFacts saved = ConfirmedMetadataFacts.Unconfirmed
                .WithIdentityClaimConfirmed()
                .WithConfirmedBaseline(new NodeIdentifier(CausalityDigestValue), new NodeIdentifier(StateIdValue), DictionaryEpochValue);

            await saved.SaveAsync(store, TestContext.CancellationToken).ConfigureAwait(false);

            ConfirmedMetadataFacts? loaded = await ConfirmedMetadataFacts.TryLoadAsync(store, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(loaded, "A saved confirmed-facts record loads back.");
            Assert.AreEqual(saved, loaded, "The loaded record equals the saved one by value across its fixed layout.");
            Assert.AreNotSame(saved, loaded, "The record came back through its bytes, so the equality above is structural and not reference identity.");
            Assert.AreEqual(StateIdValue, loaded!.ConfirmedStateId!.Value.Value, "A StateId whose high bit is set survives the fixed layout unchanged.");
            Assert.AreEqual(DictionaryEpochValue, loaded.ConfirmedDictionaryEpoch!.Value, "The dictionary epoch survives beside it.");
            Assert.IsTrue(loaded.AllowsRoutineReopen, "A host whose claim and baseline are both confirmed may reopen without consulting the plane.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A store into which no confirmed-facts record has been written reports so as a value, which the caller reads as the unconfirmed record.</summary>
    [TestMethod]
    public async Task ConfirmedFactsLoadNothingBeforeAnythingIsSaved()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);

            ConfirmedMetadataFacts? loaded = await ConfirmedMetadataFacts.TryLoadAsync(store, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNull(loaded, "A record that was never written is absent, which is a value the caller reads as having confirmed nothing.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The routine-reopen answer reads both facts and neither alone: a confirmed claim without a confirmed baseline, and a confirmed baseline without a confirmed claim, each still consult the plane.</summary>
    [TestMethod]
    public void RoutineReopenNeedsBothConfirmedFacts()
    {
        ConfirmedMetadataFacts unconfirmed = ConfirmedMetadataFacts.Unconfirmed;
        ConfirmedMetadataFacts claimOnly = unconfirmed.WithIdentityClaimConfirmed();
        ConfirmedMetadataFacts baselineOnly = unconfirmed.WithConfirmedBaseline(new NodeIdentifier(CausalityDigestValue), new NodeIdentifier(StateIdValue), DictionaryEpochValue);
        ConfirmedMetadataFacts both = claimOnly.WithConfirmedBaseline(new NodeIdentifier(CausalityDigestValue), new NodeIdentifier(StateIdValue), DictionaryEpochValue);

        Assert.IsFalse(unconfirmed.IsBaselineConfirmed, "A host that confirmed nothing holds no baseline.");
        Assert.IsFalse(unconfirmed.AllowsRoutineReopen, "A host that confirmed nothing consults the plane.");
        Assert.IsFalse(claimOnly.AllowsRoutineReopen, "A confirmed claim alone leaves the lineage baseline to settle.");
        Assert.IsTrue(baselineOnly.IsBaselineConfirmed, "The baseline's three facts are filled together, so it reads as confirmed.");
        Assert.IsFalse(baselineOnly.AllowsRoutineReopen, "A confirmed baseline alone leaves the identity claim to settle.");
        Assert.IsTrue(both.AllowsRoutineReopen, "Both facts confirmed is the whole of what a routine reopen needs.");
    }

    /// <summary>The confirmed-facts layout fails closed: an unknown layout version, an undefined flag bit, a value under a flag that calls the field absent, and a length the fixed layout cannot produce are each refused rather than read past.</summary>
    [TestMethod]
    public async Task ConfirmedFactsRefuseBytesThisLayoutCouldNotHaveWritten()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, NoOpFlush, NoOpBarrier);

            bool unknownVersion = await RefusesFactsAsync(store, Layout(version: 2, flags: 0, causalityDigest: 0, stateId: 0, dictionaryEpoch: 0), TestContext.CancellationToken).ConfigureAwait(false);
            bool undefinedFlag = await RefusesFactsAsync(store, Layout(version: 1, flags: 0b0001_0000, causalityDigest: 0, stateId: 0, dictionaryEpoch: 0), TestContext.CancellationToken).ConfigureAwait(false);
            bool absentButValued = await RefusesFactsAsync(store, Layout(version: 1, flags: 0b0000_0001, causalityDigest: CausalityDigestValue, stateId: 0, dictionaryEpoch: 0), TestContext.CancellationToken).ConfigureAwait(false);
            bool foreignLength = await RefusesFactsAsync(store, new byte[ConfirmedMetadataFacts.SerializedLength - 1], TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsTrue(unknownVersion, "A layout version this build does not read is refused, so a record written by another build is not overwritten on a misreading.");
            Assert.IsTrue(undefinedFlag, "A flag bit this layout version does not define means the bytes were not written by this layout.");
            Assert.IsTrue(absentButValued, "A value in a field the flags call absent means the bytes were not written by this layout.");
            Assert.IsTrue(foreignLength, "A fixed-layout artifact published atomically has exactly one length, so any other length is foreign or torn.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The durability point is load-bearing: a flush that refuses faults the persist, publishes no live artifact, and leaves the store reporting a fresh host.</summary>
    [TestMethod]
    public async Task PersistFaultsWhenTheDurabilityFlushRefuses()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            MetadataNodeStore store = NewStore(directory, pool, FailingFlush, NoOpBarrier);
            MetadataPlaneDeployment deployment = Deployment();
            ReplicaAxis selfAxis = deployment.Founders[0].Axis;
            VersionedValue<VeritasMetadataRecord> committed = new(RegisterVersion.First, MetadataPlaneDeployment.ReplicaIdFor(selfAxis), deployment.Genesis, RecordFor(selfAxis));
            QuePaxaVersionedNodeState<VeritasMetadataRecord> state = StateFor(deployment, committed);

            bool faulted = false;
            try
            {
                await store.PersistNode(state, TestContext.CancellationToken).ConfigureAwait(false);
            }
            catch(IOException)
            {
                faulted = true;
            }

            Assert.IsTrue(faulted, "A persist whose durability point failed faults, so no reply that depends on the state can leave over a write that was never made durable.");
            Assert.IsFalse(File.Exists(Path.Combine(directory, MetadataNodeStore.NodeStateFileName)), "The staged bytes are never published under the live name when the flush refuses.");

            QuePaxaVersionedNodeState<VeritasMetadataRecord>? loaded = await store.TryLoadAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNull(loaded, "A failed persist leaves the store exactly as it was, which here is a fresh host.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Both durability seams are on the write path of both artifacts: each durable write flushes the staged bytes and applies the directory barrier after the rename that makes them live.</summary>
    [TestMethod]
    public async Task DurableWritesInvokeTheFlushAndTheBarrier()
    {
        string directory = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
        using VeritasMemoryPool<byte> pool = new();
        try
        {
            DurabilitySeamCounter seams = new();
            MetadataNodeStore store = NewStore(directory, pool, seams.Flush, seams.Barrier);
            Assert.AreEqual(0, seams.FlushCount, "Constructing a store writes nothing.");

            await PersistOneStateAsync(store, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(1, seams.FlushCount, "The node-state write flushes its staged bytes to stable storage exactly once.");
            Assert.AreEqual(1, seams.BarrierCount, "The node-state write applies the directory barrier after the rename that makes it live.");

            await ConfirmedMetadataFacts.Unconfirmed.WithIdentityClaimConfirmed().SaveAsync(store, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(2, seams.FlushCount, "The confirmed-facts write goes through the same durability point as the node state.");
            Assert.AreEqual(2, seams.BarrierCount, "The confirmed-facts write goes through the same atomic publish as the node state.");
        }
        finally
        {
            pool.TrimExcess();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Counts the durability seams a store invoked, holding the counts in an explicit frame so the seams are method groups and capture nothing.</summary>
    private sealed class DurabilitySeamCounter
    {
        /// <summary>How many times the file-content durability flush was invoked.</summary>
        public int FlushCount { get; private set; }

        /// <summary>How many times the directory durability barrier was invoked.</summary>
        public int BarrierCount { get; private set; }

        /// <summary>Counts one file-content flush and makes nothing durable — a <see cref="DurableFlushDelegate"/>.</summary>
        /// <param name="handle">The open handle to the written file whose bytes would be flushed.</param>
        public void Flush(SafeFileHandle handle)
        {
            FlushCount++;
        }

        /// <summary>Counts one directory barrier and makes nothing durable — a <see cref="DurabilityBarrierDelegate"/>.</summary>
        /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
        public void Barrier(string directoryPath)
        {
            BarrierCount++;
        }
    }

    /// <summary>A file-content flush that does nothing, so a row that is not about durability does not depend on a real device flush.</summary>
    /// <param name="handle">The open handle to the written file whose bytes would be flushed.</param>
    private static void NoOpFlush(SafeFileHandle handle)
    {
    }

    /// <summary>A directory durability barrier that does nothing, so a row that is not about durability does not depend on a real directory flush.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>A file-content flush that refuses, standing in for a host whose storage cannot make a write durable.</summary>
    /// <param name="handle">The open handle to the written file whose bytes would be flushed.</param>
    /// <exception cref="IOException">Always, which is what the store's caller sees when the durability point fails.</exception>
    private static void FailingFlush(SafeFileHandle handle)
    {
        throw new IOException("The injected durability flush refuses, standing in for a host whose storage cannot make the staged bytes durable.");
    }

    /// <summary>A deterministic replica axis whose 32 bytes all carry <paramref name="seed"/>.</summary>
    /// <param name="seed">The byte every position of the identity carries.</param>
    /// <returns>The axis.</returns>
    private static ReplicaAxis Axis(byte seed)
    {
        byte[] bytes = new byte[ReplicaAxis.ByteWidth];
        Array.Fill(bytes, seed);

        return new ReplicaAxis(bytes);
    }


    /// <summary>
    /// One founder of this battery: an axis of repeated bytes beside a store derived from the same seed, so a
    /// row building the same deployment twice builds the same chain. A real store mints its incarnation; a
    /// battery whose subject is the store binding states its incarnations explicitly instead.
    /// </summary>
    /// <param name="seed">The byte the axis and the store are filled with.</param>
    /// <returns>The founder.</returns>
    private static MetadataFounder Founder(byte seed)
    {
        Span<byte> store = stackalloc byte[StoreIncarnation.Size];
        store.Fill(seed);

        return new MetadataFounder(Axis(seed), StoreIncarnation.FromSpan(store));
    }

    /// <summary>The three-founder deployment every row of this battery mints its chain from, in explicit founder order.</summary>
    /// <returns>The deployment.</returns>
    private static MetadataPlaneDeployment Deployment()
    {
        return MetadataPlaneDeployment.Create([Founder(0x11), Founder(0x22), Founder(0x33)]);
    }

    /// <summary>A coordinated record carrying one of every member the value has: a claim, a CONFIRMED baseline, an amended policy and a held lease.</summary>
    /// <param name="claimant">The replica identity axis the claim, the baseline and the lease name.</param>
    /// <returns>The record.</returns>
    private static VeritasMetadataRecord RecordFor(ReplicaAxis claimant)
    {
        LineageBaseline baseline = LineageBaseline
            .Intent(claimant, new NodeIdentifier(CausalityDigestValue), RegisterVersion.First)
            .Confirm(new NodeIdentifier(StateIdValue), DictionaryEpochValue, RegisterVersion.First);

        return new VeritasMetadataRecord(
            IdentityClaims: [new ReplicaIdentityClaim(claimant, RegisterVersion.First)],
            Baseline: baseline,
            Policy: new CoordinationPolicy(HealCadenceClass: 2, SymbolBudgetTier: 3),
            Coordinator: new CoordinatorLease(claimant, RegisterVersion.First));
    }

    /// <summary>Builds the durable snapshot of a real host that has learned <paramref name="committed"/>, which is exactly what the consensus runner hands a persist face.</summary>
    /// <param name="deployment">The chain the host runs on.</param>
    /// <param name="committed">The decided record the host learns.</param>
    /// <returns>The host's durable state.</returns>
    private static QuePaxaVersionedNodeState<VeritasMetadataRecord> StateFor(MetadataPlaneDeployment deployment, VersionedValue<VeritasMetadataRecord> committed)
    {
        QuePaxaVersionedNode<VeritasMetadataRecord> node = new(deployment.Genesis, deployment.Founders[0].ToHostId());
        Assert.IsTrue(node.Learn(committed), "The host adopts a record it has not seen, which is what moves it to the next instance.");

        return node.ToState();
    }

    /// <summary>Persists one host state through <paramref name="store"/>, for the rows that need a live state file rather than the state itself.</summary>
    /// <param name="store">The store to persist through.</param>
    /// <param name="cancellationToken">A token that aborts the write.</param>
    /// <returns>A task that completes once the state is live under its own name.</returns>
    private static async Task PersistOneStateAsync(MetadataNodeStore store, CancellationToken cancellationToken)
    {
        MetadataPlaneDeployment deployment = Deployment();
        ReplicaAxis selfAxis = deployment.Founders[0].Axis;
        VersionedValue<VeritasMetadataRecord> committed = new(RegisterVersion.First, MetadataPlaneDeployment.ReplicaIdFor(selfAxis), deployment.Genesis, RecordFor(selfAxis));

        await store.PersistNode(StateFor(deployment, committed), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores a host from one snapshot expecting the restore to refuse it, and answers the refusal by value.
    /// </summary>
    /// <param name="deployment">The chain the restoring host was deployed with, whose genesis the restore is handed.</param>
    /// <param name="self">The identity the restoring host runs as.</param>
    /// <param name="state">The snapshot to restore from.</param>
    /// <returns>The refusal, or <see langword="null"/> when the restore accepted the snapshot.</returns>
    /// <remarks>
    /// The refusal is a value rather than an assertion callback so the operands reach the restore as explicit
    /// arguments and no delegate here captures an enclosing scope. A row reads two things off the answer: that
    /// there WAS a refusal, and which rule it names.
    /// </remarks>
    private static ArgumentException? RestoreRefusalOf(MetadataPlaneDeployment deployment, HostId self, QuePaxaVersionedNodeState<VeritasMetadataRecord> state)
    {
        try
        {
            _ = QuePaxaVersionedNode<VeritasMetadataRecord>.FromState(deployment.Genesis, self, state);

            return null;
        }
        catch(ArgumentException refused)
        {
            return refused;
        }
    }

    /// <summary>Writes hand-crafted bytes over the store's confirmed-facts artifact and reports whether loading them is refused.</summary>
    /// <param name="store">The store whose artifact is overwritten.</param>
    /// <param name="layout">The bytes to write.</param>
    /// <param name="cancellationToken">A token that aborts the write or the read.</param>
    /// <returns><see langword="true"/> when the load refused the bytes.</returns>
    private static async Task<bool> RefusesFactsAsync(MetadataNodeStore store, byte[] layout, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(Path.Combine(store.DirectoryPath, MetadataNodeStore.ConfirmedFactsFileName), layout, cancellationToken).ConfigureAwait(false);

        try
        {
            _ = await ConfirmedMetadataFacts.TryLoadAsync(store, cancellationToken).ConfigureAwait(false);
        }
        catch(InvalidDataException)
        {
            return true;
        }

        return false;
    }

    /// <summary>Lays out a confirmed-facts record by hand, so a row can write byte patterns the type's own writer never produces.</summary>
    /// <param name="version">The layout version byte.</param>
    /// <param name="flags">The flag byte.</param>
    /// <param name="causalityDigest">The causality digest field.</param>
    /// <param name="stateId">The dataset StateId field.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch field.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Layout(byte version, byte flags, ulong causalityDigest, ulong stateId, long dictionaryEpoch)
    {
        byte[] layout = new byte[ConfirmedMetadataFacts.SerializedLength];
        layout[0] = version;
        layout[1] = flags;
        BinaryPrimitives.WriteUInt64LittleEndian(layout.AsSpan(2), causalityDigest);
        BinaryPrimitives.WriteUInt64LittleEndian(layout.AsSpan(10), stateId);
        BinaryPrimitives.WriteInt64LittleEndian(layout.AsSpan(18), dictionaryEpoch);

        return layout;
    }

    /// <summary>Creates a store over a temporary directory, wired with the node-state codec this battery encodes with.</summary>
    /// <param name="directoryPath">The directory the artifacts live in.</param>
    /// <param name="pool">The pool every transient buffer is rented from.</param>
    /// <param name="flush">The file-content durability flush to inject.</param>
    /// <param name="barrier">The directory durability barrier to inject.</param>
    /// <returns>The store.</returns>
    private static MetadataNodeStore NewStore(string directoryPath, MemoryPool<byte> pool, DurableFlushDelegate flush, DurabilityBarrierDelegate barrier)
    {
        return new MetadataNodeStore(
            directoryPath,
            pool,
            QuePaxaMessageJson.CreateVersionedNodeStateSerializer<VeritasMetadataRecord>(WriteMetadataRecord),
            QuePaxaMessageJson.CreateVersionedNodeStateDeserializer<VeritasMetadataRecord>(ReadMetadataRecord),
            flush,
            barrier);
    }

    /// <summary>
    /// Writes one coordinated metadata record as the application value inside a host-state payload. Every
    /// identifier that is 64 bits wide is written as a decimal STRING rather than as a bare number, so a value
    /// above two to the fifty-third survives a reader that would parse a JSON number as a double.
    /// </summary>
    /// <param name="writer">The writer the value is written into.</param>
    /// <param name="record">The record to write.</param>
    private static void WriteMetadataRecord(Utf8JsonWriter writer, VeritasMetadataRecord record)
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
    /// Reads one coordinated metadata record back. Nothing here refuses a payload on a rule of its own: a
    /// missing field, a malformed identifier or a value a domain constructor rejects each surfaces from this
    /// body and reaches the caller as the codec's own fail-closed refusal.
    /// </summary>
    /// <param name="element">The element the value was written into.</param>
    /// <returns>The record the payload carries.</returns>
    private static VeritasMetadataRecord ReadMetadataRecord(JsonElement element)
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
}
