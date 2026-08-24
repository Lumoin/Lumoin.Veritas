using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Tests.MemoryPool;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The sharded multi-block descent of the peer-reconciliation rung: a repair pass whose system-of-record lost
/// MORE than one block routes to the sharded body, drives per-shard add-only sessions through the host-bound
/// fetch seam, gates the composed recovered set on exact key widths, the lost ranges' total item count, and the
/// whole-generation faithfulness peel, and re-ingests the healed set. A single-block loss prefers the
/// single-block body when its source is present and still heals through the sharded body when only that
/// transport is bound. Every refused attempt is named on the trace — a shard-policy mismatch as the deployment
/// misconfiguration it is, never as corruption — and declines to a named loss. Geometry is the fixture's
/// (10-item blocks, 64-byte aligned, XxHash3); every buffer is pool-rented.
/// </summary>
[TestClass]
internal sealed class ShardedPeerRepairDescentTests
{
    /// <summary>The symbol budget the generation sketch and the faithfulness peel are built at — far above the fixture item counts, so a faithful composed set always peels completely.</summary>
    private const int SymbolCap = 1300;

    /// <summary>The dictionary epoch the staged generation's manifest records: the fixture stamps generation × 11 and every test here stages generation 7.</summary>
    private const long StagedDictionaryEpoch = 77;

    /// <summary>The shard-bit count the descent rows drive: four shards, so a multi-block difference exercises several sessions.</summary>
    private const int ShardBits = 2;

    /// <summary>The MSTest execution context, for the per-test cancellation token every pass and turn observes so a test run is genuinely cancellable.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The fixed clock every row's passes and turns read — nothing here depends on synchronized or advancing time, so one pinned clock per test instance keeps the rows deterministic.</summary>
    private FakeTimeProvider Clock { get; } = new();

    /// <summary>The repair configuration for the descent tests: the fixture's geometry bound to the REAL rateless encoder, so the faithfulness peel the sharded body runs reconciles genuinely.</summary>
    /// <param name="bytePool">The byte pool the pass rents from.</param>
    /// <param name="triplePool">The triple pool the feed and healed item set rent from.</param>
    /// <returns>The configuration.</returns>
    private static RepairConfiguration DescentRepairConfig(MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
    {
        return new RepairConfiguration(
            ChecksumAlgorithm.XxHash3,
            bytePool,
            triplePool,
            SketchContract.Structural,
            symbolBudget: 16,
            StructuralReconciliationProjection.Projection,
            new RatelessSketchCodec(bytePool).Encode);
    }

    /// <summary>Builds the sharded restoring source over a fetch seam: the rung at the descent's shard policy, the real rateless recover seam, the structural inverse, and the epoch under test.</summary>
    /// <param name="policy">The shard policy driving the attempt.</param>
    /// <param name="fetch">The per-shard fetch seam.</param>
    /// <param name="pool">The pool the recover seam rents from.</param>
    /// <param name="dictionaryEpoch">The epoch the source declares; defaults to the staged generation's.</param>
    /// <returns>The sharded source.</returns>
    private static ShardedPeerReconciliationSource ShardedSource(PrefixShardPolicy policy, FetchPeerShardDifferenceDelegate fetch, MemoryPool<byte> pool, long dictionaryEpoch = StagedDictionaryEpoch)
    {
        return new ShardedPeerReconciliationSource(
            new ShardedPeerRepairRung(policy, TimeProvider.System),
            fetch,
            new RatelessSketchCodec(pool).Recover,
            StructuralReconciliationProjection.Inversion,
            SymbolCap,
            TimeSpan.Zero,
            dictionaryEpoch);
    }

    /// <summary>Wraps one pre-built sharded source as the pass's provider seam.</summary>
    /// <param name="source">The source every invocation answers.</param>
    /// <returns>The bound provider delegate.</returns>
    private static ProvideShardedPeerReconciliationSourceDelegate Provide(ShardedPeerReconciliationSource source)
    {
        return new FixedShardedSourceProvider(source).ProvideAsync;
    }

    /// <summary>Binds one pre-built sharded source as the provider seam without a lexical closure: the source travels as instance state and <see cref="ProvideAsync"/> is the bound method group.</summary>
    /// <param name="source">The source every invocation answers.</param>
    private sealed class FixedShardedSourceProvider(ShardedPeerReconciliationSource source)
    {
        /// <summary>The source every invocation answers.</summary>
        private ShardedPeerReconciliationSource Source { get; } = source;

        /// <summary>Answers the fixed source; a fixed binding reads none of the seam's parameters.</summary>
        /// <param name="commitGeneration">The damaged generation under repair.</param>
        /// <param name="dictionaryEpoch">The generation's dictionary epoch.</param>
        /// <param name="cancellationToken">Unused by a fixed binding.</param>
        /// <returns>The fixed source.</returns>
        public ValueTask<ShardedPeerReconciliationSource?> ProvideAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
        {
            return new ValueTask<ShardedPeerReconciliationSource?>(Source);
        }
    }

    /// <summary>A provider that always throws — the transport-fault arm of the provider seam's contract.</summary>
    private static class ThrowingShardedSourceProvider
    {
        /// <summary>Throws the transport fault.</summary>
        /// <param name="commitGeneration">The damaged generation under repair.</param>
        /// <param name="dictionaryEpoch">The generation's dictionary epoch.</param>
        /// <param name="cancellationToken">Unread; the fault fires first.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Always.</exception>
        public static ValueTask<ShardedPeerReconciliationSource?> ProvideAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
        {
            throw new IOException("The peer transport is unreachable.");
        }
    }

    /// <summary>
    /// Drives the sharded fetch seam without a transport: per shard it answers, from the configured difference
    /// items, exactly those hashing into the requested shard under the routing policy, declaring the configured
    /// fingerprint and completion, and counting invocations. An honest peer configures the routing policy's own
    /// fingerprint; the mismatch row configures a foreign one.
    /// </summary>
    /// <param name="routing">The policy the fake routes difference items by.</param>
    /// <param name="declared">The fingerprint every result declares as the peer's own.</param>
    /// <param name="differenceItems">The whole symmetric difference the peer holds.</param>
    /// <param name="completed">The completion status every result reports.</param>
    private sealed class ShardedFetchFake(PrefixShardPolicy routing, ShardPolicyFingerprint declared, IReadOnlyList<ReadOnlyMemory<byte>> differenceItems, bool completed = true)
    {
        /// <summary>The policy the fake routes difference items by.</summary>
        private PrefixShardPolicy Routing { get; } = routing;

        /// <summary>The fingerprint every result declares as the peer's own.</summary>
        private ShardPolicyFingerprint Declared { get; } = declared;

        /// <summary>The whole symmetric difference the peer holds.</summary>
        private IReadOnlyList<ReadOnlyMemory<byte>> DifferenceItems { get; } = differenceItems;

        /// <summary>The completion status every result reports.</summary>
        private bool Completed { get; } = completed;

        /// <summary>How many shard fetches were driven.</summary>
        public int Invocations { get; private set; }

        /// <summary>Answers one shard's difference: the configured items hashing into <paramref name="shardIndex"/> under the routing policy.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's fingerprint; a fake transport reads nothing from it.</param>
        /// <param name="localShardItems">The shard's local operand; a fake transport reads nothing from it.</param>
        /// <param name="symbolCap">The shard's symbol ceiling; unused by a fake that decodes nothing.</param>
        /// <param name="pool">The session pool; unused by a fake that decodes nothing.</param>
        /// <param name="cancellationToken">Cancels the exchange.</param>
        /// <returns>The shard's declared difference.</returns>
        public ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations++;
            List<ReadOnlyMemory<byte>> shardItems = [];
            for(int i = 0; i < DifferenceItems.Count; i++)
            {
                if(Routing.ShardOf(DifferenceItems[i].Span) == shardIndex)
                {
                    shardItems.Add(DifferenceItems[i]);
                }
            }

            return new ValueTask<ShardReconcileResult>(new ShardReconcileResult(shardIndex, Declared, shardItems, Completed, shardItems.Count));
        }
    }

    /// <summary>A fetch seam whose result carries NO peer declaration — the transport faulted before the peer's header ever arrived, so there is no declared fingerprint to compare. The rung must refuse it as <see cref="ShardedRepairOutcome.PeerUndeclared"/> ahead of the fingerprint comparison, never as a policy mismatch.</summary>
    private sealed class UndeclaringFetchFake
    {
        /// <summary>How many shard fetches were driven.</summary>
        public int Invocations { get; private set; }

        /// <summary>Answers a declaration-free result: null fingerprint, nothing recovered, completion claimed — none of which may be consumed without a declaration.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's fingerprint; a faulted transport never transmitted it.</param>
        /// <param name="localShardItems">The shard's local operand; unread.</param>
        /// <param name="symbolCap">The shard's symbol ceiling; unread.</param>
        /// <param name="pool">The session pool; unread.</param>
        /// <param name="cancellationToken">Cancels the exchange.</param>
        /// <returns>The declaration-free result.</returns>
        public ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations++;

            return new ValueTask<ShardReconcileResult>(new ShardReconcileResult(shardIndex, PeerFingerprint: null, [], Completed: true, AbsorbedSymbolCount: 0));
        }
    }

    /// <summary>A fetch seam that throws a raw transport fault — the escape a wire binding failed to convert to a value decline. The pass aborts, and the round bracket must still terminate.</summary>
    private static class RawThrowingFetchFake
    {
        /// <summary>Throws the raw transport fault.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's fingerprint; unread.</param>
        /// <param name="localShardItems">The shard's local operand; unread.</param>
        /// <param name="symbolCap">The shard's symbol ceiling; unread.</param>
        /// <param name="pool">The session pool; unread.</param>
        /// <param name="cancellationToken">Unread; the fault fires first.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Always.</exception>
        public static ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            throw new IOException("The shard transport tore down mid-exchange.");
        }
    }

    /// <summary>A fetch seam that cancels the attempt on its second invocation — the mid-wave cancellation arm.</summary>
    /// <param name="tokenSource">The source the second invocation cancels.</param>
    private sealed class CancellingFetchFake(CancellationTokenSource tokenSource)
    {
        /// <summary>The source the second invocation cancels.</summary>
        private CancellationTokenSource TokenSource { get; } = tokenSource;

        /// <summary>How many shard fetches were driven.</summary>
        public int Invocations { get; private set; }

        /// <summary>Answers an empty completed shard on the first invocation and cancels on the second.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's fingerprint, echoed as an honest peer's own declaration for the shards before the cancellation.</param>
        /// <param name="localShardItems">The shard's local operand; unread.</param>
        /// <param name="symbolCap">The shard's symbol ceiling; unread.</param>
        /// <param name="pool">The session pool; unread.</param>
        /// <param name="cancellationToken">The attempt's token; observed after the second invocation cancels it.</param>
        /// <returns>The empty completed shard, until the cancellation propagates.</returns>
        public async ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            Invocations++;
            if(Invocations >= 2)
            {
                await TokenSource.CancelAsync().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new ShardReconcileResult(shardIndex, localFingerprint, [], Completed: true, AbsorbedSymbolCount: 0);
        }
    }

    /// <summary>A two-block loss with no parity heals through the sharded body: every shard session answers its slice of the lost items, the composed set passes the width, count, and faithfulness gates, and the healed system-of-record holds exactly the original triple set.</summary>
    [TestMethod]
    public async Task TheShardedBodyRestoresAMultiBlockLoss()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 0, 2)));
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsEmpty(repair.NamedLosses);
            Assert.AreEqual(policy.ShardCount, fetch.Invocations, "Every shard is driven exactly once.");
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            Assert.IsTrue(ItemSegment.RunVerifyRound(restored.Image.Span).IsClean, "The healed system-of-record must verify clean.");
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            HashSet<EncodedTriple> recoveredSet = [.. recovered.Span];
            Assert.IsTrue(recoveredSet.SetEquals(triples), "The healed system-of-record must hold exactly the original triple set.");
            StorageTraceEvent reingested = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Reingested);
            Assert.AreEqual(30, reingested.ItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A single-block loss heals through the sharded body when only the sharded transport is bound — the presence-aware routing keeps single-block peer repair for a sharded-only deployment.</summary>
    [TestMethod]
    public async Task ASingleBlockLossHealsThroughTheShardedBodyWhenOnlyThatTransportIsBound()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 1)));
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, null, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsEmpty(repair.NamedLosses);
            Assert.IsGreaterThan(0, fetch.Invocations, "The sharded body served the single-block loss.");
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            HashSet<EncodedTriple> recoveredSet = [.. recovered.Span];
            Assert.IsTrue(recoveredSet.SetEquals(triples), "The healed system-of-record must hold exactly the original triple set.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A single-block loss with BOTH sources bound routes to the single-block body — the strictly stronger one there — and the sharded transport is never driven.</summary>
    [TestMethod]
    public async Task ASingleBlockLossPrefersTheSingleBlockBodyWhenBothSourcesAreBound()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 1)));
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, null, Guid.Empty, Clock, ProvidePeer(SingleBlockPeerSource(triples, bytePool)), Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsEmpty(repair.NamedLosses, "The single-block body healed the loss.");
            Assert.AreEqual(0, fetch.Invocations, "The sharded transport is never driven when the single-block body serves.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Builds a single-block peer source over a peer replica's full triple set: its verified sketch, the rateless recover seam, the structural inverse, the symbol cap, and the epoch.</summary>
    /// <param name="peerTriples">The peer replica's full triple set.</param>
    /// <param name="pool">The pool the peer sketch persist rents from.</param>
    /// <returns>The single-block peer source.</returns>
    private static PeerReconciliationSource SingleBlockPeerSource(EncodedTriple[] peerTriples, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, SymbolCap, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);
        VerifiedSketch peerSketch = SketchPersistence.LoadVerifiedSketch(writer.WrittenSpan, SketchContract.Structural);

        return new PeerReconciliationSource(peerSketch, new RatelessSketchCodec(pool).Recover, StructuralReconciliationProjection.Inversion, SymbolCap, StagedDictionaryEpoch);
    }

    /// <summary>Wraps one pre-built single-block peer source as the pass's provider seam.</summary>
    /// <param name="source">The source every invocation answers.</param>
    /// <returns>The bound provider delegate.</returns>
    private static ProvidePeerReconciliationSourceDelegate ProvidePeer(PeerReconciliationSource source)
    {
        return new FixedPeerSourceProvider(source).ProvideAsync;
    }

    /// <summary>Binds one pre-built single-block peer source as the provider seam without a lexical closure: the source travels as instance state and <see cref="ProvideAsync"/> is the bound method group.</summary>
    /// <param name="source">The source every invocation answers.</param>
    private sealed class FixedPeerSourceProvider(PeerReconciliationSource source)
    {
        /// <summary>The source every invocation answers.</summary>
        private PeerReconciliationSource Source { get; } = source;

        /// <summary>Answers the fixed source; a fixed binding reads none of the seam's parameters.</summary>
        /// <param name="commitGeneration">The damaged generation under repair.</param>
        /// <param name="dictionaryEpoch">The generation's dictionary epoch.</param>
        /// <param name="cancellationToken">Unused by a fixed binding.</param>
        /// <returns>The fixed source.</returns>
        public ValueTask<PeerReconciliationSource?> ProvideAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
        {
            return new ValueTask<PeerReconciliationSource?>(Source);
        }
    }

    /// <summary>A declared shard-policy mismatch refuses the attempt whole and is named on the trace as itself: the refused event carries the policy-mismatch outcome code, and the loss is named rather than wrong content consumed.</summary>
    [TestMethod]
    public async Task APolicyMismatchIsRefusedAndNamedAsItself()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            PrefixShardPolicy foreign = new(ShardBits + 1, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, foreign.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 0, 2)));
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsNotEmpty(repair.NamedLosses, "A refused sharded attempt descends to named losses.");
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.PolicyMismatch, refused.ByteOffset, "The refusal names the policy mismatch as itself.");
            Assert.AreEqual(0, trace.Events.Count(static e => e.Kind == StorageTraceEventKind.Reingested), "Nothing from a mismatched session is consumed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A result carrying no peer declaration is refused as itself — PeerUndeclared, never PolicyMismatch: a transport blip is not a deployment misconfiguration. The refused event carries the outcome code, the loss is named, and nothing is consumed.</summary>
    [TestMethod]
    public async Task AnUndeclaredPeerIsRefusedAsItselfNotAsAPolicyMismatch()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            UndeclaringFetchFake fetch = new();
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses, "An undeclared peer abandons the attempt to named losses.");
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.PeerUndeclared, refused.ByteOffset, "The refusal names the missing declaration as itself, not as a policy mismatch.");
            Assert.AreEqual(0, trace.Events.Count(static e => e.Kind == StorageTraceEventKind.Reingested), "Nothing from an undeclared session is consumed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A raw transport fault escaping the fetch seam still terminates the round bracket: the abandoned marker follows the began marker before the fault propagates, so the trace never shows an unterminated round.</summary>
    [TestMethod]
    public async Task ARawTransportFaultStillTerminatesTheRoundBracket()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            StorageTraceCapture trace = new();
            ScrubTurn turn = new(store, null, DescentRepairConfig(bytePool, triplePool), NoRoundSink, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, RawThrowingFetchFake.FetchAsync, bytePool)));
            await Assert.ThrowsExactlyAsync<IOException>(async () => await turn.RunAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.ScrubRoundBegan));
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.ScrubRoundAbandoned), "The fault path closes the began bracket with the terminal marker.");
            Assert.AreEqual(0, trace.Events.Count(static e => e.Kind == StorageTraceEventKind.ScrubRoundCompleted));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An incomplete shard decode abandons the attempt: the refused event carries the incomplete-shard outcome and the loss is named.</summary>
    [TestMethod]
    public async Task AnIncompleteShardAbandonsTheAttempt()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 0, 2)), completed: false);
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.IncompleteShard, refused.ByteOffset);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A composed set short of the lost ranges' total item count declines with the count-mismatch diagnosis — the multi-block form of the single-block per-block count gate.</summary>
    [TestMethod]
    public async Task ACountShortComposedSetDeclinesWithTheCountMismatchDiagnosis()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            EncodedTriple[] lost = BlockItems(triples, 10, 0, 2);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(lost.Take(lost.Length - 1)));
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.CountMismatch, refused.ByteOffset, "The precise coordinator diagnosis travels on the refused event.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An unloadable generation sketch leaves the sharded body unsourced and the attempt declines fail-closed: no shard session is ever driven and the loss is named. The staged sketch is the fixture's opaque stub, which no verifying load under the structural contract accepts.</summary>
    [TestMethod]
    public async Task AnUnloadableGenerationSketchDeclinesTheShardedBodyFailClosed()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 0, 2)));
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, null, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            Assert.AreEqual(0, fetch.Invocations, "An unsourced faithfulness gate never drives a session.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A sharded source keyed to a foreign dictionary epoch declines before any session is driven.</summary>
    [TestMethod]
    public async Task AForeignEpochShardedSourceDeclines()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            ShardedFetchFake fetch = new(policy, policy.Fingerprint, ProjectedKeys(BlockItems(triples, 10, 0, 2)));
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, null, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool, dictionaryEpoch: StagedDictionaryEpoch + 1)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            Assert.AreEqual(0, fetch.Invocations, "A foreign-epoch source never drives a session.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A faulting sharded provider degrades the round to local-only: the fault is named on the trace, the pass survives, and the loss is named rather than the round aborting.</summary>
    [TestMethod]
    public async Task AFaultingProviderDegradesTheRoundToLocalOnly()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, ThrowingShardedSourceProvider.ProvideAsync, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused, "A provider fault never aborts the round.");
            Assert.IsNotEmpty(repair.NamedLosses, "The round continued local-only and named the loss.");
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.PeerSourceUnavailable), "The provider fault is named on the trace exactly once.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Cancellation mid-wave propagates out of the pass and every pooled rental is released exactly once — the pool's dispose is the leak-and-double-dispose assertion.</summary>
    [TestMethod]
    public async Task CancellationMidWaveReleasesEveryRentalOnce()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, Clock);
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            CancellingFetchFake fetch = new(cancellation);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => _ = await ScrubRound.RunRepairPassAsync(store, verify, DescentRepairConfig(bytePool, triplePool), null, null, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)), cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.AreEqual(2, fetch.Invocations, "The second wave observed the cancellation.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A round the token cancels mid-repair emits the abandoned marker before the cancellation propagates, so the trace bracket always terminates.</summary>
    [TestMethod]
    public async Task ACancelledRoundEmitsTheAbandonedMarker()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool, SymbolCap);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            PrefixShardPolicy policy = new(ShardBits, ShardKeyMixing.Avalanche);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            CancellingFetchFake fetch = new(cancellation);
            StorageTraceCapture trace = new();
            ScrubTurn turn = new(store, null, DescentRepairConfig(bytePool, triplePool), NoRoundSink, trace.Capture, Guid.Empty, Clock, null, Provide(ShardedSource(policy, fetch.FetchAsync, bytePool)));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await turn.RunAsync(cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.ScrubRoundBegan));
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.ScrubRoundAbandoned), "The began bracket reached its terminal marker.");
            Assert.AreEqual(0, trace.Events.Count(static e => e.Kind == StorageTraceEventKind.ScrubRoundCompleted));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The round-result sink a cancelled-round row never reaches.</summary>
    /// <param name="verifyReport">The round's verify verdict.</param>
    /// <param name="repairReport">The round's repair verdict.</param>
    /// <param name="cancellationToken">The round's token.</param>
    /// <returns>A completed task.</returns>
    private static ValueTask NoRoundSink(ScrubRoundReport verifyReport, RepairPassReport repairReport, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
