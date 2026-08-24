using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Tests.Reconciliation;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The shard-policy handshake's refusal contract: a peer declaring a different <see cref="PrefixShardPolicy"/>
/// than the one driving the attempt is refused by name (<see cref="ShardedRepairOutcome.PolicyMismatch"/>)
/// before anything from that session is consumed — a mixing mismatch, either direction of a bit-count mismatch,
/// the sparse-survivor corner where the unguarded shape launders silent loss, and a mismatch declared mid-wave
/// under concurrent shards. The controls pin the identical-policy recovery law, the fingerprint's wire encoding,
/// and the transmit half of the handshake. The peer stand-in is honest: it partitions its own set under its own
/// policy, answers every requested index, and returns the exact symmetric difference a completed peel decodes.
/// </summary>
[TestClass]
internal sealed class ShardPolicyMismatchTests
{
    /// <summary>The structural reconciliation item width in bytes.</summary>
    private const int KeyWidth = 16;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The governed pool the partitions rent from.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>A mixing-only mismatch (same bit count) is named as the deployment misconfiguration it is, with nothing recovered — not misdiagnosed as a corrupted or adversarial stream.</summary>
    [TestMethod]
    public async Task MixingMismatchIsRefusedAsAPolicyMismatch()
    {
        PrefixShardPolicy local = new(2, ShardKeyMixing.Avalanche);
        PrefixShardPolicy peer = new(2, ShardKeyMixing.Identity);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(300, seed: 0xF17E_0001UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(40, seed: 0xF17E_0002UL, KeyWidth);
        HonestPeerHost host = new(peer, survivors.With(lost).Keys);
        ShardedPeerRepairRung rung = new(local, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, host.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.PolicyMismatch, result.Outcome, "A peer on a different mixing must be refused by name, not misdiagnosed.");
        Assert.IsEmpty(result.RecoveredItems, "Nothing from a mismatched session may be consumed.");
    }

    /// <summary>A peer sharding finer than the local policy (larger bit count, same mixing) is refused by name.</summary>
    [TestMethod]
    public async Task PeerFinerBitCountMismatchIsRefusedAsAPolicyMismatch()
    {
        PrefixShardPolicy local = new(2, ShardKeyMixing.Avalanche);
        PrefixShardPolicy peer = new(3, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(300, seed: 0xF17E_0003UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(40, seed: 0xF17E_0004UL, KeyWidth);
        HonestPeerHost host = new(peer, survivors.With(lost).Keys);
        ShardedPeerRepairRung rung = new(local, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, host.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.PolicyMismatch, result.Outcome, "A finer-sharding peer must be refused by name.");
        Assert.IsEmpty(result.RecoveredItems, "Nothing from a mismatched session may be consumed.");
    }

    /// <summary>The sparse-survivor corner: with empty local shards passing the direction guard vacuously and the faithfulness gate standing anchorless, a mixing mismatch must still be refused by name — never laundered into a recovery claim.</summary>
    [TestMethod]
    public async Task SparseSurvivorMixingMismatchIsRefusedNotLaundered()
    {
        PrefixShardPolicy local = new(2, ShardKeyMixing.Avalanche);
        PrefixShardPolicy peer = new(2, ShardKeyMixing.Identity);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(3, seed: 0xF17E_0005UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(30, seed: 0xF17E_0006UL, KeyWidth);
        HonestPeerHost host = new(peer, survivors.With(lost).Keys);
        ShardedPeerRepairRung rung = new(local, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, host.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.PolicyMismatch, result.Outcome, "The vacuous-guard corner must refuse the mismatch by name, not report a recovery.");
        Assert.IsEmpty(result.RecoveredItems, "Nothing from a mismatched session may be consumed.");
    }

    /// <summary>The reverse bit-count direction: the local policy shards finer than the peer, whose honest transport answers the out-of-range indices with its declaration and an empty operand — the declaration alone must trip the refusal.</summary>
    [TestMethod]
    public async Task LocalFinerBitCountMismatchIsRefusedAsAPolicyMismatch()
    {
        PrefixShardPolicy local = new(3, ShardKeyMixing.Avalanche);
        PrefixShardPolicy peer = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(300, seed: 0xF17E_0007UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(40, seed: 0xF17E_0008UL, KeyWidth);
        HonestPeerHost host = new(peer, survivors.With(lost).Keys);
        ShardedPeerRepairRung rung = new(local, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, host.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.PolicyMismatch, result.Outcome, "A coarser-sharding peer must be refused by name.");
        Assert.IsEmpty(result.RecoveredItems, "Nothing from a mismatched session may be consumed.");
    }

    /// <summary>Under a concurrent shard window, a mismatch declared by a non-first lane's peer is still caught after the wave completes, and the refusal names that shard.</summary>
    [TestMethod]
    public async Task AMismatchDeclaredMidWaveIsNamedWithItsShard()
    {
        PrefixShardPolicy local = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(120, seed: 0xF17E_0009UL, KeyWidth);
        WaveHeterogeneousFetch fetch = new(local.Fingerprint, new ShardPolicyFingerprint(2, ShardKeyMixing.Identity), mismatchedShardIndex: 2);
        ShardedPeerRepairRung rung = new(local, TimeProvider.System, maxConcurrentShards: 4);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, fetch.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.PolicyMismatch, result.Outcome, "A mid-wave mismatch must be refused by name.");
        Assert.AreEqual(2, result.FailedShardIndex, "The refusal names the mismatching shard.");
        Assert.IsEmpty(result.RecoveredItems, "Nothing from a mismatched attempt may be consumed.");
    }

    /// <summary>An earlier lane's non-empty recovery is discarded whole when a later lane of the same wave declares a mismatch: nothing consumed before the refusal survives into the result.</summary>
    [TestMethod]
    public async Task AnEarlierLanesRecoveryIsDiscardedByALaterLanesMismatch()
    {
        PrefixShardPolicy local = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(120, seed: 0xF17E_000DUL, KeyWidth);

        //A peer-only item routed to shard 0 under the local policy, so lane 0 recovers it before lane 2's
        //mismatch is reached in the per-result check loop.
        ItemKeyCorpus candidates = ItemKeyCorpus.Uniform(32, seed: 0xF17E_000EUL, KeyWidth);
        ReadOnlyMemory<byte> recoverable = default;
        for(int i = 0; i < candidates.Keys.Count; i++)
        {
            if(local.ShardOf(candidates.Keys[i].Span) == 0)
            {
                recoverable = candidates.Keys[i];
                break;
            }
        }

        Assert.IsFalse(recoverable.IsEmpty, "Precondition: a shard-zero peer-only item must exist among the candidates.");
        WaveHeterogeneousFetch fetch = new(local.Fingerprint, new ShardPolicyFingerprint(2, ShardKeyMixing.Identity), mismatchedShardIndex: 2)
        {
            RecoverableItem = recoverable,
            RecoverableShardIndex = 0,
        };
        ShardedPeerRepairRung rung = new(local, TimeProvider.System, maxConcurrentShards: 4);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, fetch.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.PolicyMismatch, result.Outcome, "The later lane's mismatch refuses the attempt whole.");
        Assert.AreEqual(2, result.FailedShardIndex, "The refusal names the mismatching shard.");
        Assert.IsEmpty(result.RecoveredItems, "The earlier lane's recovery is discarded whole — nothing consumed before the refusal survives.");
    }

    /// <summary>The identical-policy control: through the same honest peer host, byte-identical policies still recover exactly the injected loss.</summary>
    [TestMethod]
    public async Task IdenticalPoliciesRecoverTheInjectedLossExactlyThroughTheHonestPeerHost()
    {
        PrefixShardPolicy policy = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(300, seed: 0xF17E_000AUL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(40, seed: 0xF17E_000BUL, KeyWidth);
        HonestPeerHost host = new(policy, survivors.With(lost).Keys);
        ShardedPeerRepairRung rung = new(policy, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, host.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.Recovered, result.Outcome, "Identical policies must keep recovering.");
        HashSet<string> recovered = [.. result.RecoveredItems.Select(k => Convert.ToHexString(k.Span))];
        HashSet<string> expected = [.. lost.Keys.Select(k => Convert.ToHexString(k.Span))];
        Assert.IsTrue(recovered.SetEquals(expected), "The composed recovery must equal the injected loss exactly.");
    }

    /// <summary>The fingerprint's wire contract: the canonical encoding round-trips across the field-space corners and a foreign mixing code, refuses short frames and unknown versions structurally, and the default value equals no constructible policy's fingerprint.</summary>
    [TestMethod]
    public void FingerprintEncodingRoundTripsAndRefusesOnlyStructurally()
    {
        ShardPolicyFingerprint[] corners =
        [
            new(0, ShardKeyMixing.Identity),
            new(PrefixShardPolicy.MaximumShardBitCount, ShardKeyMixing.Avalanche),
            new(2, ShardKeyMixing.Identity),
            new(2, ShardKeyMixing.Avalanche),
            new(5, new ShardKeyMixing(99)),
        ];
        Span<byte> frame = stackalloc byte[ShardPolicyFingerprint.EncodedByteLength];
        foreach(ShardPolicyFingerprint corner in corners)
        {
            corner.Write(frame);
            Assert.IsTrue(ShardPolicyFingerprint.TryRead(frame, out ShardPolicyFingerprint read), "A canonical frame must parse.");
            Assert.AreEqual(corner, read, "The encoding must round-trip identically, foreign mixing codes included.");
        }

        //The literal byte layout is a wire contract host transports carry verbatim — pinned against an
        //accidental symmetric change to Write and TryRead that an internal round-trip cannot see.
        new ShardPolicyFingerprint(2, ShardKeyMixing.Avalanche).Write(frame);
        Assert.AreEqual((byte)1, frame[0], "Byte zero is the encoding version.");
        Assert.AreEqual((byte)2, frame[1], "Byte one is the shard-bit count.");
        Assert.AreEqual((byte)0, frame[2], "The mixing code is big-endian across bytes two through five.");
        Assert.AreEqual((byte)0, frame[3], "The mixing code is big-endian across bytes two through five.");
        Assert.AreEqual((byte)0, frame[4], "The mixing code is big-endian across bytes two through five.");
        Assert.AreEqual((byte)2, frame[5], "The mixing code is big-endian across bytes two through five.");

        corners[0].Write(frame);
        Assert.IsFalse(ShardPolicyFingerprint.TryRead(frame[..(ShardPolicyFingerprint.EncodedByteLength - 1)], out _), "A short frame is refused.");
        frame[0] = 2;
        Assert.IsFalse(ShardPolicyFingerprint.TryRead(frame, out _), "An unknown encoding version is refused.");

        for(int bits = 0; bits <= PrefixShardPolicy.MaximumShardBitCount; bits++)
        {
            Assert.AreNotEqual(default(ShardPolicyFingerprint), new PrefixShardPolicy(bits, ShardKeyMixing.Identity).Fingerprint, "No constructible policy's fingerprint may equal the default declaration.");
            Assert.AreNotEqual(default(ShardPolicyFingerprint), new PrefixShardPolicy(bits, ShardKeyMixing.Avalanche).Fingerprint, "No constructible policy's fingerprint may equal the default declaration.");
        }
    }

    /// <summary>The transmit half of the handshake: the rung hands the driving policy's own fingerprint to the fetch delegate on every shard call.</summary>
    [TestMethod]
    public async Task TheRungTransmitsTheDrivingPolicysFingerprintOnEveryShardCall()
    {
        PrefixShardPolicy policy = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(80, seed: 0xF17E_000CUL, KeyWidth);
        HonestPeerHost host = new(policy, survivors.Keys);
        ShardedPeerRepairRung rung = new(policy, TimeProvider.System);

        await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, AcceptAnyHealedSet, host.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.HasCount(policy.ShardCount, host.ReceivedFingerprints, "Every shard call carries a declaration.");
        foreach(ShardPolicyFingerprint received in host.ReceivedFingerprints)
        {
            Assert.AreEqual(policy.Fingerprint, received, "The transmitted declaration is the driving policy's own fingerprint.");
        }
    }

    /// <summary>The vacuous faithfulness gate: accepts every composed set, standing for the recorded lost-sketch corner where the whole-generation anchor is gone.</summary>
    /// <param name="recoveredItems">The composed peer-only items; unused.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    private static bool AcceptAnyHealedSet(IReadOnlyCollection<ReadOnlyMemory<byte>> recoveredItems)
    {
        return true;
    }

    /// <summary>
    /// An honest peer host stand-in: it holds its own item set, partitions it under ITS OWN policy on demand,
    /// answers every requested shard index (serving an empty operand for a shard it does not hold), declares its
    /// own policy fingerprint on every result, and returns the exact symmetric difference a completed peel
    /// decodes. It records each received local declaration so the transmit half of the handshake is observable.
    /// The explicit binding frame the fetch delegate binds as a method group.
    /// </summary>
    private sealed class HonestPeerHost
    {
        /// <summary>The peer's own shard policy, which routes its items and stamps its declarations.</summary>
        private PrefixShardPolicy PeerPolicy { get; }

        /// <summary>The peer replica's full item set.</summary>
        private IReadOnlyList<ReadOnlyMemory<byte>> PeerItems { get; }

        /// <summary>The local declarations received, one per shard call, in call order.</summary>
        public List<ShardPolicyFingerprint> ReceivedFingerprints { get; } = [];

        /// <summary>Constructs the host over its policy and items.</summary>
        /// <param name="peerPolicy">The peer's own shard policy.</param>
        /// <param name="peerItems">The peer replica's full item set.</param>
        public HonestPeerHost(PrefixShardPolicy peerPolicy, IReadOnlyList<ReadOnlyMemory<byte>> peerItems)
        {
            PeerPolicy = peerPolicy;
            PeerItems = peerItems;
        }

        /// <summary>The <see cref="FetchPeerShardDifferenceDelegate"/> implementation: serves the requested shard from the peer's own partition and returns the exact symmetric difference of the two operands.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's declaration, recorded for the transmit pin.</param>
        /// <param name="localShardItems">The local operand for this shard.</param>
        /// <param name="symbolCap">The symbol ceiling; unused — the stand-in's difference is exact.</param>
        /// <param name="pool">The session pool; unused.</param>
        /// <param name="cancellationToken">Unused; the stand-in answers synchronously.</param>
        /// <returns>The shard's difference, completion status, and the peer's own declaration.</returns>
        public ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            ReceivedFingerprints.Add(localFingerprint);

            //The peer's own shard-index membership under ITS policy; an index past its range holds nothing.
            List<ReadOnlyMemory<byte>> peerShard = [];
            for(int i = 0; i < PeerItems.Count; i++)
            {
                if(PeerPolicy.ShardOf(PeerItems[i].Span) == shardIndex)
                {
                    peerShard.Add(PeerItems[i]);
                }
            }

            //The exact symmetric difference of the two operands — what a completed peel decodes.
            HashSet<string> localHex = [.. localShardItems.Select(k => Convert.ToHexString(k.Span))];
            HashSet<string> peerHex = [.. peerShard.Select(k => Convert.ToHexString(k.Span))];
            List<ReadOnlyMemory<byte>> difference = [];
            for(int i = 0; i < localShardItems.Count; i++)
            {
                if(!peerHex.Contains(Convert.ToHexString(localShardItems[i].Span)))
                {
                    difference.Add(localShardItems[i]);
                }
            }

            for(int i = 0; i < peerShard.Count; i++)
            {
                if(!localHex.Contains(Convert.ToHexString(peerShard[i].Span)))
                {
                    difference.Add(peerShard[i]);
                }
            }

            return new ValueTask<ShardReconcileResult>(new ShardReconcileResult(shardIndex, PeerPolicy.Fingerprint, difference, true, difference.Count));
        }
    }

    /// <summary>A heterogeneous-deployment stand-in: every shard's peer declares the expected fingerprint except one, which declares a different policy. All sessions serve empty differences, so the declaration is the only signal. The explicit binding frame the fetch delegate binds as a method group.</summary>
    private sealed class WaveHeterogeneousFetch
    {
        /// <summary>The declaration the conforming shards answer with.</summary>
        private ShardPolicyFingerprint Matching { get; }

        /// <summary>The declaration the odd shard answers with.</summary>
        private ShardPolicyFingerprint Mismatched { get; }

        /// <summary>The shard whose peer declares <see cref="Mismatched"/>.</summary>
        private int MismatchedShardIndex { get; }

        /// <summary>A peer-only item served as <see cref="RecoverableShardIndex"/>'s difference, or an empty default for none — the recovery an earlier lane consumes before a later lane's mismatch.</summary>
        public ReadOnlyMemory<byte> RecoverableItem { get; init; }

        /// <summary>The shard whose difference carries <see cref="RecoverableItem"/>.</summary>
        public int RecoverableShardIndex { get; init; }

        /// <summary>Constructs the stand-in over its declarations.</summary>
        /// <param name="matching">The declaration the conforming shards answer with.</param>
        /// <param name="mismatched">The declaration the odd shard answers with.</param>
        /// <param name="mismatchedShardIndex">The shard whose peer declares the mismatch.</param>
        public WaveHeterogeneousFetch(ShardPolicyFingerprint matching, ShardPolicyFingerprint mismatched, int mismatchedShardIndex)
        {
            Matching = matching;
            Mismatched = mismatched;
            MismatchedShardIndex = mismatchedShardIndex;
        }

        /// <summary>The <see cref="FetchPeerShardDifferenceDelegate"/> implementation: an empty, completed session carrying the configured declaration.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's declaration; unused.</param>
        /// <param name="localShardItems">The local operand; unused.</param>
        /// <param name="symbolCap">The symbol ceiling; unused.</param>
        /// <param name="pool">The session pool; unused.</param>
        /// <param name="cancellationToken">Unused; the stand-in answers synchronously.</param>
        /// <returns>An empty completed result with the configured declaration.</returns>
        public ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            ShardPolicyFingerprint declared = shardIndex == MismatchedShardIndex ? Mismatched : Matching;
            List<ReadOnlyMemory<byte>> difference = [];
            if(!RecoverableItem.IsEmpty && shardIndex == RecoverableShardIndex)
            {
                difference.Add(RecoverableItem);
            }

            return new ValueTask<ShardReconcileResult>(new ShardReconcileResult(shardIndex, declared, difference, true, difference.Count));
        }
    }
}
