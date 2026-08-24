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
/// The sharded multi-block repair rung's composition contract, driven through a deterministic in-memory fetch
/// stand-in: a clean run composes exactly the peer-only items across every shard wave; an incomplete shard
/// abandons the whole attempt; a difference item that hashes outside the shard that recovered it trips the
/// direction guard; and a composed set the whole-generation faithfulness gate refuses re-ingests nothing. The
/// per-shard peel itself is certified by the reconciliation round-trip battery — these pins cover the rung's
/// wave driving, direction resolution, and gating alone.
/// </summary>
[TestClass]
internal sealed class ShardedPeerRepairRungTests
{
    /// <summary>The structural reconciliation item width in bytes.</summary>
    private const int KeyWidth = 16;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The governed pool the partitions rent from.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>A clean sharded attempt composes exactly the injected loss: every shard completes, every difference item resolves peer-only, and the faithfulness gate passes the composed set through for re-ingest.</summary>
    [TestMethod]
    public async Task RecoveredComposesThePeerOnlyItemsAcrossShards()
    {
        PrefixShardPolicy policy = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(500, seed: 0xACCE_5501UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(60, seed: 0xACCE_5502UL, KeyWidth);
        StubShardFetch fetch = new(policy, lost.Keys);
        StubFaithfulnessGate gate = new(accept: true);
        ShardedPeerRepairRung rung = new(policy, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, gate.Verify, fetch.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.Recovered, result.Outcome);
        Assert.AreEqual(policy.ShardCount, result.ShardsProcessed, "Every shard is driven on the clean path.");
        Assert.AreEqual(-1, result.FailedShardIndex);
        Assert.IsTrue(gate.Verified, "The faithfulness gate must run over the composed set.");

        HashSet<string> recovered = [.. result.RecoveredItems.Select(k => Convert.ToHexString(k.Span))];
        HashSet<string> expected = [.. lost.Keys.Select(k => Convert.ToHexString(k.Span))];
        Assert.IsTrue(recovered.SetEquals(expected), "The composed recovery must equal the injected loss exactly.");
    }

    /// <summary>A shard whose decode does not complete within its cap abandons the whole attempt: nothing is re-ingested and the failed shard is named.</summary>
    [TestMethod]
    public async Task IncompleteShardAbandonsTheAttempt()
    {
        PrefixShardPolicy policy = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(200, seed: 0xACCE_5503UL, KeyWidth);
        StubShardFetch fetch = new(policy, ItemKeyCorpus.Uniform(20, seed: 0xACCE_5504UL, KeyWidth).Keys)
        {
            IncompleteShardIndex = 2,
        };
        StubFaithfulnessGate gate = new(accept: true);
        ShardedPeerRepairRung rung = new(policy, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, gate.Verify, fetch.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.IncompleteShard, result.Outcome);
        Assert.AreEqual(2, result.FailedShardIndex, "The abandoning shard is named.");
        Assert.IsEmpty(result.RecoveredItems, "An abandoned attempt re-ingests nothing.");
        Assert.IsFalse(gate.Verified, "The faithfulness gate never runs over a partial healed set.");
    }

    /// <summary>A difference item that hashes into a different shard than the one that recovered it is a corrupted or adversarial stream: the direction guard rejects the attempt whole.</summary>
    [TestMethod]
    public async Task ForeignShardItemIsRejectedByTheDirectionGuard()
    {
        PrefixShardPolicy policy = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(200, seed: 0xACCE_5505UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(20, seed: 0xACCE_5506UL, KeyWidth);

        //Inject one genuine lost item into the WRONG shard's difference: the shard after its own.
        ReadOnlyMemory<byte> foreign = lost.Keys[0];
        int wrongShard = (policy.ShardOf(foreign.Span) + 1) % policy.ShardCount;
        StubShardFetch fetch = new(policy, lost.Keys)
        {
            ForeignItem = foreign,
            ForeignShardIndex = wrongShard,
        };
        StubFaithfulnessGate gate = new(accept: true);
        ShardedPeerRepairRung rung = new(policy, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, gate.Verify, fetch.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.DirectionGuardRejected, result.Outcome);
        Assert.AreEqual(wrongShard, result.FailedShardIndex, "The rejecting shard is named.");
        Assert.IsEmpty(result.RecoveredItems, "A direction-rejected attempt re-ingests nothing.");
    }

    /// <summary>A composed set the whole-generation faithfulness gate refuses is rejected whole — the authoritative check stands above the per-shard guards.</summary>
    [TestMethod]
    public async Task FaithfulnessRejectionReingestsNothing()
    {
        PrefixShardPolicy policy = new(2, ShardKeyMixing.Avalanche);
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(200, seed: 0xACCE_5507UL, KeyWidth);
        StubShardFetch fetch = new(policy, ItemKeyCorpus.Uniform(20, seed: 0xACCE_5508UL, KeyWidth).Keys);
        StubFaithfulnessGate gate = new(accept: false);
        ShardedPeerRepairRung rung = new(policy, TimeProvider.System);

        ShardedPeerRepairResult result = await rung
            .AttemptShardedPeerReconciliationAsync(survivors.Keys, KeyWidth, shardSymbolCap: 1_000, gate.Verify, fetch.FetchAsync, TimeSpan.Zero, Pool, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(ShardedRepairOutcome.FaithfulnessRejected, result.Outcome);
        Assert.AreEqual(-1, result.FailedShardIndex);
        Assert.IsEmpty(result.RecoveredItems, "A faithfulness-rejected attempt re-ingests nothing.");
        Assert.IsTrue(gate.Verified, "The gate ran and refused.");
    }

    /// <summary>The deterministic fetch stand-in: each shard's decoded difference is the injected loss items that hash into it (a faithful peel's peer-only slice), optionally with one shard reporting an incomplete decode or one foreign item routed into the wrong shard. The explicit binding frame the fetch delegate binds as a method group.</summary>
    private sealed class StubShardFetch
    {
        /// <summary>The shard policy the loss items are routed by.</summary>
        private PrefixShardPolicy Policy { get; }

        /// <summary>The injected loss — the peer-only items a faithful peel recovers.</summary>
        private IReadOnlyList<ReadOnlyMemory<byte>> Lost { get; }

        /// <summary>The shard whose decode reports incomplete, or <see langword="null"/> for none.</summary>
        public int? IncompleteShardIndex { get; init; }

        /// <summary>An item injected into <see cref="ForeignShardIndex"/>'s difference regardless of where it hashes, or <see langword="null"/> for none.</summary>
        public ReadOnlyMemory<byte>? ForeignItem { get; init; }

        /// <summary>The shard <see cref="ForeignItem"/> is injected into.</summary>
        public int ForeignShardIndex { get; init; }

        /// <summary>Constructs the stand-in over the loss it deals out per shard.</summary>
        /// <param name="policy">The shard policy the loss items are routed by.</param>
        /// <param name="lost">The injected loss.</param>
        public StubShardFetch(PrefixShardPolicy policy, IReadOnlyList<ReadOnlyMemory<byte>> lost)
        {
            Policy = policy;
            Lost = lost;
        }

        /// <summary>The <see cref="FetchPeerShardDifferenceDelegate"/> implementation: deals the shard its slice of the loss and declares the peer's fingerprint — the routing policy's own, since this stand-in models a peer on the same policy.</summary>
        /// <param name="shardIndex">The shard being reconciled.</param>
        /// <param name="localFingerprint">The driving policy's declaration; unused — the stand-in models the peer end, whose own declaration rides the result.</param>
        /// <param name="localShardItems">The shard's local operand; unused — the stand-in's difference is precomputed.</param>
        /// <param name="symbolCap">The symbol ceiling; unused.</param>
        /// <param name="pool">The session pool; unused.</param>
        /// <param name="cancellationToken">Unused; the stand-in answers synchronously.</param>
        /// <returns>The shard's difference, completion status, and declared fingerprint.</returns>
        public ValueTask<ShardReconcileResult> FetchAsync(int shardIndex, ShardPolicyFingerprint localFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, int symbolCap, MemoryPool<byte> pool, CancellationToken cancellationToken)
        {
            List<ReadOnlyMemory<byte>> difference = [];
            for(int i = 0; i < Lost.Count; i++)
            {
                if(Policy.ShardOf(Lost[i].Span) == shardIndex)
                {
                    difference.Add(Lost[i]);
                }
            }

            if(ForeignItem is { } foreign && shardIndex == ForeignShardIndex)
            {
                difference.Add(foreign);
            }

            bool completed = IncompleteShardIndex != shardIndex;

            return new ValueTask<ShardReconcileResult>(new ShardReconcileResult(shardIndex, Policy.Fingerprint, difference, completed, difference.Count));
        }
    }

    /// <summary>The whole-generation faithfulness gate stand-in: accepts or refuses per configuration and records that it ran. The explicit binding frame the verify delegate binds as a method group.</summary>
    private sealed class StubFaithfulnessGate
    {
        /// <summary>Whether the gate accepts the composed set.</summary>
        private bool Accept { get; }

        /// <summary>Whether the gate has run.</summary>
        public bool Verified { get; private set; }

        /// <summary>Constructs the gate with its verdict.</summary>
        /// <param name="accept">Whether the gate accepts the composed set.</param>
        public StubFaithfulnessGate(bool accept)
        {
            Accept = accept;
        }

        /// <summary>The <see cref="VerifyHealedSetAgainstGenerationSketchDelegate"/> implementation.</summary>
        /// <param name="recoveredItems">The composed peer-only items; unused beyond recording the run.</param>
        /// <returns>The configured verdict.</returns>
        public bool Verify(IReadOnlyCollection<ReadOnlyMemory<byte>> recoveredItems)
        {
            Verified = true;

            return Accept;
        }
    }
}


