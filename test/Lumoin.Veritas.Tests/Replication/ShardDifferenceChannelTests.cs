using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Tests.Integrity;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The shard-difference channel end to end: a repair pass whose multi-block loss heals through the REAL wire —
/// per-shard sessions over the envelope framing across duplex pipe pairs, a server serving its own shard
/// operand, the typed header handshake both ways — and every refusal arm named as itself: a foreign policy
/// declaration as PolicyMismatch, an honest peer's epoch decline as IncompleteShard, a connection that dies
/// before the header as PeerUndeclared, a mid-session protocol violation as a value decline with the fault
/// class on the trace, and a cap trip as IncompleteShard under the post-join verdict. In-process over pipes,
/// deterministic; the healed-set set-equality doubles as the copy-then-dispose regression pin (decoded items
/// are copied out of the session's pooled arena before disposal — a wrong order would surface as garbage here).
/// </summary>
[TestClass]
internal sealed class ShardDifferenceChannelTests
{
    /// <summary>The symbol budget the generation sketch is persisted at and the per-shard cap the descent rows drive.</summary>
    private const int SymbolCap = 1300;

    /// <summary>The dictionary epoch the staged generation's manifest records (the fixture stamps generation × 11; every row stages generation 7).</summary>
    private const ulong StagedDictionaryEpoch = 77;

    /// <summary>The shard-bit count the rows drive: four shards.</summary>
    private const int ShardBits = 2;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The fixed clock every row's passes read.</summary>
    private FakeTimeProvider Clock { get; } = new();

    /// <summary>Captures shard-difference fault events through a method group, so a row asserts the diagnosis class.</summary>
    private sealed class FaultCapture
    {
        /// <summary>The events captured, in emission order.</summary>
        public List<ShardDifferenceFaultEvent> Events { get; } = [];

        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in ShardDifferenceFaultEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>
    /// An in-process peer endpoint: each opened connection is a fresh duplex pipe pair served by a
    /// <see cref="ShardDifferenceChannelServer"/> over the configured policy, epoch, and served key set. The
    /// serve task rides the connection as its transport, so disposing the connection joins the serve and a
    /// serve-side fault surfaces on the client's teardown path.
    /// </summary>
    /// <param name="policy">The peer's own shard policy.</param>
    /// <param name="servedKeys">The peer's committed key set.</param>
    /// <param name="dictionaryEpoch">The peer's dictionary epoch.</param>
    /// <param name="pool">The pool the peer's sessions rent from.</param>
    private sealed class InProcessShardPeer(PrefixShardPolicy policy, IReadOnlyList<ReadOnlyMemory<byte>> servedKeys, ulong dictionaryEpoch, MemoryPool<byte> pool)
    {
        /// <summary>The server serving every opened connection.</summary>
        private ShardDifferenceChannelServer Server { get; } = new(policy, new FixedSnapshot(servedKeys).Provide, dictionaryEpoch, pool);

        /// <summary>Opens one fresh connection served by this peer — an <see cref="OpenPeerShardConnectionDelegate"/>.</summary>
        /// <param name="shardIndex">The shard the connection will carry; the serve reads it from the header, not from here.</param>
        /// <param name="cancellationToken">Cancels the open; an in-process pair opens synchronously.</param>
        /// <returns>The connection, its serve task riding as the transport.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the serve-completion transport it owns) transfers to the caller per the OpenPeerShardConnectionDelegate contract; the shard-difference client disposes it unconditionally on every exit.")]
        public ValueTask<PeerChannelConnection> OpenAsync(int shardIndex, CancellationToken cancellationToken)
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            Task serve = Server.ServeAsync(requestPipe.Reader, responsePipe.Writer, cancellationToken);

            return new ValueTask<PeerChannelConnection>(new PeerChannelConnection(requestPipe.Writer, responsePipe.Reader, new ServeCompletion(serve)));
        }

        /// <summary>Holds one serve task as the connection's transport, so the connection's disposal joins the serve.</summary>
        /// <param name="serve">The serve task.</param>
        private sealed class ServeCompletion(Task serve): IAsyncDisposable
        {
            /// <summary>Joins the serve; a serve fault surfaces here, on the client's teardown path.</summary>
            /// <returns>The serve task as a value task.</returns>
            public ValueTask DisposeAsync()
            {
                return new ValueTask(serve);
            }
        }

        /// <summary>Binds a fixed key set as the serve-snapshot seam without a lexical closure.</summary>
        /// <param name="keys">The served keys.</param>
        private sealed class FixedSnapshot(IReadOnlyList<ReadOnlyMemory<byte>> keys)
        {
            /// <summary>The served keys.</summary>
            private IReadOnlyList<ReadOnlyMemory<byte>> Keys { get; } = keys;

            /// <summary>Answers the fixed set.</summary>
            /// <returns>The served keys.</returns>
            public IReadOnlyList<ReadOnlyMemory<byte>> Provide()
            {
                return Keys;
            }
        }
    }

    /// <summary>A connection factory that always throws — the connection-refused arm, before any header crosses.</summary>
    private static class RefusingConnectionFactory
    {
        /// <summary>Throws the transport fault.</summary>
        /// <param name="shardIndex">The shard the connection would carry.</param>
        /// <param name="cancellationToken">Unread; the fault fires first.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Always.</exception>
        public static ValueTask<PeerChannelConnection> OpenAsync(int shardIndex, CancellationToken cancellationToken)
        {
            throw new IOException("The peer refused the connection.");
        }
    }

    /// <summary>
    /// A peer whose endpoint answers a well-formed reply header and then one garbage frame — the mid-session
    /// protocol-violation arm. The bytes are written directly (a length-prefixed frame whose payload carries an
    /// unknown kind byte), so the client's framing refuses it after the handshake accepted.
    /// </summary>
    /// <param name="policy">The policy whose fingerprint the reply declares.</param>
    /// <param name="dictionaryEpoch">The epoch the reply declares.</param>
    private sealed class GarbageAfterHeaderPeer(PrefixShardPolicy policy, ulong dictionaryEpoch)
    {
        /// <summary>The policy whose fingerprint the reply declares.</summary>
        private PrefixShardPolicy Policy { get; } = policy;

        /// <summary>The epoch the reply declares.</summary>
        private ulong DictionaryEpoch { get; } = dictionaryEpoch;

        /// <summary>Opens a connection whose serve replies then writes garbage.</summary>
        /// <param name="shardIndex">The shard the connection would carry.</param>
        /// <param name="cancellationToken">Cancels the serve.</param>
        /// <returns>The connection.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection (and the serve-completion transport it owns) transfers to the caller per the OpenPeerShardConnectionDelegate contract; the shard-difference client disposes it unconditionally on every exit.")]
        public ValueTask<PeerChannelConnection> OpenAsync(int shardIndex, CancellationToken cancellationToken)
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            Task serve = ServeGarbageAsync(requestPipe.Reader, responsePipe.Writer, cancellationToken);

            return new ValueTask<PeerChannelConnection>(new PeerChannelConnection(requestPipe.Writer, responsePipe.Reader, new SwallowedServe(serve)));
        }

        /// <summary>The channel's length-prefix width: a four-byte big-endian frame length ahead of every payload.</summary>
        private const int FrameLengthPrefixWidth = sizeof(int);

        /// <summary>The framing's reply-header kind byte, mirrored here so the hand-rolled frame matches the wire contract the client reads.</summary>
        private const byte ReplyHeaderKindByte = 2;

        /// <summary>The reply header's accept byte for an accepted exchange.</summary>
        private const byte AcceptByte = 1;

        /// <summary>The epoch's wire width: eight big-endian bytes.</summary>
        private const int EpochByteWidth = sizeof(ulong);

        /// <summary>A kind byte no framing version assigns, so the frame is refused as unknown.</summary>
        private const byte UnknownKindByte = 255;

        /// <summary>Writes a valid reply header, then one frame whose payload is a single unknown kind byte, then completes.</summary>
        /// <param name="requestReader">The request pipe; drained and discarded.</param>
        /// <param name="responseWriter">The response pipe the frames are written to.</param>
        /// <param name="cancellationToken">Cancels the serve.</param>
        /// <returns>The serve task.</returns>
        private async Task ServeGarbageAsync(PipeReader requestReader, PipeWriter responseWriter, CancellationToken cancellationToken)
        {
            byte[] fingerprint = new byte[ShardPolicyFingerprint.EncodedByteLength];
            Policy.Fingerprint.Write(fingerprint);
            int payloadLength = sizeof(byte) + sizeof(byte) + fingerprint.Length + EpochByteWidth;
            byte[] reply = new byte[FrameLengthPrefixWidth + payloadLength];
            BinaryPrimitives.WriteInt32BigEndian(reply, payloadLength);
            reply[FrameLengthPrefixWidth] = ReplyHeaderKindByte;
            reply[FrameLengthPrefixWidth + 1] = AcceptByte;
            fingerprint.CopyTo(reply, FrameLengthPrefixWidth + 2);
            BinaryPrimitives.WriteUInt64BigEndian(reply.AsSpan(FrameLengthPrefixWidth + 2 + fingerprint.Length), DictionaryEpoch);
            await responseWriter.WriteAsync(reply, cancellationToken).ConfigureAwait(false);

            byte[] garbage = new byte[FrameLengthPrefixWidth + 1];
            BinaryPrimitives.WriteInt32BigEndian(garbage, 1);
            garbage[FrameLengthPrefixWidth] = UnknownKindByte;
            await responseWriter.WriteAsync(garbage, cancellationToken).ConfigureAwait(false);
            await responseWriter.CompleteAsync().ConfigureAwait(false);
            await requestReader.CompleteAsync().ConfigureAwait(false);
        }

        /// <summary>Joins the serve on disposal, swallowing nothing — the fake's serve never faults.</summary>
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
    }

    /// <summary>Builds the sharded restoring source over the wire binding against an in-process peer.</summary>
    /// <param name="policy">The driving policy.</param>
    /// <param name="openConnection">The connection factory.</param>
    /// <param name="pool">The pool the recover seam and sessions rent from.</param>
    /// <param name="trace">The fault-event sink, or <see langword="null"/>.</param>
    /// <returns>The sharded source.</returns>
    private ShardedPeerReconciliationSource WireSource(PrefixShardPolicy policy, OpenPeerShardConnectionDelegate openConnection, MemoryPool<byte> pool, TraceHandler<ShardDifferenceFaultEvent>? trace = null)
    {
        return ShardedPeerTransportBinding.CreateSource(policy, openConnection, pool, StagedDictionaryEpoch, SymbolCap, TimeSpan.Zero, Clock, trace);
    }

    /// <summary>Wraps one pre-built sharded source as the pass's provider seam.</summary>
    /// <param name="source">The source every invocation answers.</param>
    /// <returns>The bound provider delegate.</returns>
    private static ProvideShardedPeerReconciliationSourceDelegate Provide(ShardedPeerReconciliationSource source)
    {
        return new FixedShardedSourceProvider(source).ProvideAsync;
    }

    /// <summary>Binds one pre-built sharded source as the provider seam without a lexical closure.</summary>
    /// <param name="source">The source every invocation answers.</param>
    private sealed class FixedShardedSourceProvider(ShardedPeerReconciliationSource source)
    {
        /// <summary>The source every invocation answers.</summary>
        private ShardedPeerReconciliationSource Source { get; } = source;

        /// <summary>Answers the fixed source.</summary>
        /// <param name="commitGeneration">The damaged generation under repair.</param>
        /// <param name="dictionaryEpoch">The generation's dictionary epoch.</param>
        /// <param name="cancellationToken">Unused by a fixed binding.</param>
        /// <returns>The fixed source.</returns>
        public ValueTask<ShardedPeerReconciliationSource?> ProvideAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
        {
            return new ValueTask<ShardedPeerReconciliationSource?>(Source);
        }
    }

    /// <summary>A two-block loss heals through the REAL wire: per-shard sessions over the envelope framing across pipe pairs, the peer serving its pre-damage key set, and the healed system-of-record holding exactly the original triple set — the copy-then-dispose pin rides the set equality.</summary>
    [TestMethod]
    public async Task AMultiBlockLossHealsOverTheRealWire()
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
            InProcessShardPeer peer = new(policy, ProjectedKeys(triples), StagedDictionaryEpoch, bytePool);
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfigWithRealCodec(bytePool, triplePool), null, null, Guid.Empty, Clock, null, Provide(WireSource(policy, peer.OpenAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsEmpty(repair.NamedLosses, "The wire-backed sharded body healed the loss.");
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            Assert.IsTrue(ItemSegment.RunVerifyRound(restored.Image.Span).IsClean, "The healed system-of-record must verify clean.");
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            HashSet<EncodedTriple> recoveredSet = [.. recovered.Span];
            Assert.IsTrue(recoveredSet.SetEquals(triples), "The healed system-of-record must hold exactly the original triple set.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A peer driving a FOREIGN policy declares it on the reply header; the client skips the session fast-path and the rung refuses PolicyMismatch — the wire half of the typed handshake pinned end to end.</summary>
    [TestMethod]
    public async Task AForeignPolicyDeclarationOverTheWireIsRefusedAsAPolicyMismatch()
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
            InProcessShardPeer peer = new(foreign, ProjectedKeys(triples), StagedDictionaryEpoch, bytePool);
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfigWithRealCodec(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(WireSource(policy, peer.OpenAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.PolicyMismatch, refused.ByteOffset, "The wire declaration is refused as the deployment misconfiguration it is.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An honest same-policy peer that declines for a foreign epoch: the declaration matches, so the attempt falls through the fingerprint check and abandons as IncompleteShard — never mislabelled a policy mismatch.</summary>
    [TestMethod]
    public async Task AnHonestEpochDeclineOverTheWireAbandonsAsIncompleteShard()
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
            InProcessShardPeer peer = new(policy, ProjectedKeys(triples), StagedDictionaryEpoch + 1, bytePool);
            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfigWithRealCodec(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(WireSource(policy, peer.OpenAsync, bytePool)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.IncompleteShard, refused.ByteOffset, "An honest decline is incompleteness, not a policy mismatch.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A connection that dies before the peer ever declares: the client value-declines with a null declaration, the fault is named Transport on the trace, and the rung refuses PeerUndeclared — a network blip is never a deployment misconfiguration.</summary>
    [TestMethod]
    public async Task APreHeaderConnectionFaultIsRefusedAsPeerUndeclared()
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
            StorageTraceCapture trace = new();
            FaultCapture faults = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfigWithRealCodec(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(WireSource(policy, RefusingConnectionFactory.OpenAsync, bytePool, faults.Capture)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.PeerUndeclared, refused.ByteOffset, "A pre-header fault is a missing declaration, named as itself.");
            Assert.ContainsSingle(faults.Events, "One fault event names the declined fetch.");
            Assert.AreEqual(ShardDifferenceFaultKind.Transport, faults.Events[0].Kind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A peer that violates the protocol after a valid header: the client converts the malformed frame to a value decline with the Protocol fault class on the trace, and the attempt abandons as IncompleteShard — the declaration arrived and matched, so no mismatch is claimed.</summary>
    [TestMethod]
    public async Task AMidSessionProtocolViolationDeclinesWithTheProtocolFaultClass()
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
            GarbageAfterHeaderPeer peer = new(policy, StagedDictionaryEpoch);
            StorageTraceCapture trace = new();
            FaultCapture faults = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfigWithRealCodec(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(WireSource(policy, peer.OpenAsync, bytePool, faults.Capture)), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.IncompleteShard, refused.ByteOffset, "A protocol violation after an honest matching declaration abandons as incompleteness.");
            Assert.IsNotEmpty(faults.Events, "The fault class is named on the trace.");
            Assert.AreEqual(ShardDifferenceFaultKind.Protocol, faults.Events[0].Kind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A symbol cap far below the difference trips the client's post-join wind-down: the shard reports Completed=false and the attempt abandons as IncompleteShard — the cap bounds a decode the difference outruns.</summary>
    [TestMethod]
    public async Task ACapTripOverTheWireAbandonsAsIncompleteShard()
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

            //The peer serves a set disjoint from the survivors and sized so every shard's symmetric difference
            //exceeds the WHOLE symbol stream the tiny cap admits (the responder's finite trigger budget): a
            //decode needs at least one symbol per difference item, so no scheduling can ever complete it and
            //the cap trips into the named abandon — the cap is a resource bound, and a decode that completes
            //within the admitted stream legitimately reports completion instead.
            EncodedTriple[] foreign = new EncodedTriple[1600];
            for(uint i = 0; i < foreign.Length; i++)
            {
                foreign[i] = EncodedTriple.FromEncoded(10_000 + i, 7, 20_000 + i);
            }

            InProcessShardPeer peer = new(policy, ProjectedKeys(foreign), StagedDictionaryEpoch, bytePool);
            StorageTraceCapture trace = new();

            //The zero drain window is sound here BECAUSE the decode provably cannot complete: the wind-down
            //needs no scheduling slack for a completion that cannot arrive, and the row stays off the wall
            //clock the fixed test clock never advances.
            ShardedPeerReconciliationSource capped = ShardedPeerTransportBinding.CreateSource(policy, peer.OpenAsync, bytePool, StagedDictionaryEpoch, shardSymbolCap: 2, TimeSpan.Zero, Clock, decodeDrainWindow: TimeSpan.Zero);
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfigWithRealCodec(bytePool, triplePool), null, trace.Capture, Guid.Empty, Clock, null, Provide(capped), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotEmpty(repair.NamedLosses);
            StorageTraceEvent refused = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.ShardedRepairRefused);
            Assert.AreEqual((long)ShardedRepairOutcome.IncompleteShard, refused.ByteOffset, "The cap bounds the decode into a named abandon.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The repair configuration bound to the REAL rateless encoder, so the faithfulness peel reconciles genuinely.</summary>
    /// <param name="bytePool">The byte pool the pass rents from.</param>
    /// <param name="triplePool">The triple pool the feed and healed item set rent from.</param>
    /// <returns>The configuration.</returns>
    private static RepairConfiguration RepairConfigWithRealCodec(MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
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
}
