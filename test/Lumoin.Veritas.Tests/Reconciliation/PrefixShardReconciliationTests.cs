using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Reconciliation;

/// <summary>
/// The prefix-shard policy's balance and completeness laws, and a per-shard two-replica reconciliation
/// round-trip over the in-memory encoder/decoder transport. The round-trip mirrors the single-block rung's
/// sketch peel — encode both operands, subtract the streams symbol-wise, absorb until complete — run once per
/// shard, and asserts the composed peer-only items equal the injected loss exactly. The contract width here is
/// the structural reconciliation shape the codec pins (16-byte item, eight-byte keyed checksum, well-known key);
/// production wiring drives each shard through the real session transport under the same contract.
/// </summary>
[TestClass]
internal sealed class PrefixShardReconciliationTests
{
    /// <summary>The structural reconciliation item width in bytes.</summary>
    private const int KeyWidth = 16;

    /// <summary>The governed pool every partition, encoder, and decoder in the suite rents from — the tracked allocation path production uses.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>The structural reconciliation contract every shard shares, byte-matching the codec's pinned shape.</summary>
    private static ReconciliationContract Contract { get; } =
        new(ReconciliationItemDomain.Structural, KeyWidth, 8, ReconciliationContract.WellKnownChecksumKeyLow, ReconciliationContract.WellKnownChecksumKeyHigh);

    /// <summary>Uniform keys land in balanced shards under identity mixing — the content-hash regime where raw prefixes already balance.</summary>
    [TestMethod]
    public void UniformKeysPartitionIntoBalancedShards()
    {
        //256000 uniform keys across 256 shards is a 1000-item mean per shard; the deviation bound below is many
        //standard deviations wide, so the assertion is robust yet the imbalance of a broken policy fails it.
        const int shardBits = 8;
        const int count = 256_000;
        ItemKeyCorpus corpus = ItemKeyCorpus.Uniform(count, seed: 0x1234_5678UL, KeyWidth);

        PrefixShardPolicy policy = new(shardBits, ShardKeyMixing.Identity);
        using PrefixShardPartition partition = policy.Partition(corpus.Keys, KeyWidth, Pool);

        int mean = count / policy.ShardCount;
        int low = (mean * 3) / 4;
        int high = (mean * 5) / 4;
        for(int s = 0; s < partition.ShardCount; s++)
        {
            int occupancy = partition.Occupancy(s);
            Assert.IsTrue(occupancy >= low && occupancy <= high, $"Shard {s} held {occupancy} items, outside [{low}, {high}].");
        }
    }

    /// <summary>Real-layout structural keys with few distinct subjects concentrate under identity mixing into exactly that few shards — the subject low byte IS the shard prefix — while avalanche mixing balances the same keys across all shards.</summary>
    [TestMethod]
    public void StructuralKeysNeedAvalancheToBalance()
    {
        //Real-layout structural keys with FEW DISTINCT SUBJECTS: the frozen structural projection packs subject
        //into the low 32 bits of the low word, which ContentKey128 serializes little-endian into byte 0, and
        //ShardOf reads bytes 0..7 big-endian, so byte 0 — the subject's low byte — is the shard prefix. A
        //generation with few subjects (each carrying many triples) therefore concentrates Identity into only as
        //many shards as there are distinct subject low bytes, while Avalanche folds the full key entropy into the
        //leading bits and balances. This is the measured caveat — raw structural prefixes do not balance, only
        //mixed ones do.
        const int shardBits = 8;
        const int distinctSubjects = 4;
        const int count = 64_000;
        ItemKeyCorpus corpus = ItemKeyCorpus.StructuralShaped(count, distinctSubjects, KeyWidth);

        using PrefixShardPartition identity = new PrefixShardPolicy(shardBits, ShardKeyMixing.Identity).Partition(corpus.Keys, KeyWidth, Pool);
        int occupiedUnderIdentity = 0;
        int concentrated = 0;
        for(int s = 0; s < identity.ShardCount; s++)
        {
            if(identity.Occupancy(s) > 0)
            {
                occupiedUnderIdentity++;
                concentrated += identity.Occupancy(s);
            }
        }

        Assert.AreEqual(distinctSubjects, occupiedUnderIdentity, "Identity mixing shards on the subject low byte, so few distinct subjects must concentrate into that few shards.");
        Assert.AreEqual(count, concentrated, "Every key must land in one of the concentrated shards.");
        Assert.AreEqual(count / distinctSubjects, identity.Occupancy(0), "Each distinct subject's triples should pack evenly into its own shard under Identity.");

        using PrefixShardPartition avalanche = new PrefixShardPolicy(shardBits, ShardKeyMixing.Avalanche).Partition(corpus.Keys, KeyWidth, Pool);
        int mean = count / (1 << shardBits);
        int low = (mean * 3) / 4;
        int high = (mean * 5) / 4;
        for(int s = 0; s < avalanche.ShardCount; s++)
        {
            int occupancy = avalanche.Occupancy(s);
            Assert.IsTrue(occupancy >= low && occupancy <= high, $"Avalanche shard {s} held {occupancy} items, outside [{low}, {high}].");
        }
    }

    /// <summary>A partition reconstructs the input set exactly — every item in exactly one shard, every shard's items hashing back to it — and shard assignment is a pure function of the key.</summary>
    [TestMethod]
    public void EveryItemLandsInExactlyOneShardAndAssignmentIsDeterministic()
    {
        const int shardBits = 6;
        const int count = 40_000;
        ItemKeyCorpus corpus = ItemKeyCorpus.Uniform(count, seed: 0xABCD_1234UL, KeyWidth);

        PrefixShardPolicy policy = new(shardBits, ShardKeyMixing.Avalanche);
        using PrefixShardPartition partition = policy.Partition(corpus.Keys, KeyWidth, Pool);

        Assert.AreEqual(policy.ShardCount, partition.ShardCount);
        Assert.AreEqual(count, partition.ItemCount);

        //Union of all shards reconstructs the input set exactly, with no item in two shards.
        HashSet<string> union = [];
        int summed = 0;
        for(int s = 0; s < partition.ShardCount; s++)
        {
            IReadOnlyList<ReadOnlyMemory<byte>> shard = partition.Shard(s);
            Assert.AreEqual(shard.Count, partition.Occupancy(s));
            summed += shard.Count;
            for(int i = 0; i < shard.Count; i++)
            {
                //Membership is by shard prefix, so every item in shard s must hash back to s.
                Assert.AreEqual(s, policy.ShardOf(shard[i].Span));
                Assert.IsTrue(union.Add(Convert.ToHexString(shard[i].Span)), "An item appeared in more than one shard.");
            }
        }

        Assert.AreEqual(count, summed);
        Assert.HasCount(count, union);

        HashSet<string> input = [.. corpus.Keys.Select(k => Convert.ToHexString(k.Span))];
        Assert.IsTrue(union.SetEquals(input), "The shard union did not reconstruct the input set.");

        //Assignment is a pure function of the key: the same key hashes to the same shard every time.
        for(int i = 0; i < 1_000; i++)
        {
            Assert.AreEqual(policy.ShardOf(corpus.Keys[i].Span), policy.ShardOf(corpus.Keys[i].Span));
        }
    }

    /// <summary>Two replicas partitioned under one byte-identical policy recover an injected multi-item loss exactly: each shard's peel yields its slice of the symmetric difference, direction resolves against the local shard, and the composed peer-only set equals the loss.</summary>
    [TestMethod]
    public void PerShardRoundTripRecoversTheWholeGenerationLoss()
    {
        const int shardBits = 5;
        const int survivorCount = 20_000;
        const int lostCount = 400;

        //The peer holds the survivors plus the lost items; the local replica holds only the survivors. Recovering
        //the difference shard by shard must compose back to exactly the lost set.
        ItemKeyCorpus survivors = ItemKeyCorpus.Uniform(survivorCount, seed: 0x5EED_0001UL, KeyWidth);
        ItemKeyCorpus lost = ItemKeyCorpus.Uniform(lostCount, seed: 0x5EED_9999UL, KeyWidth);
        ItemKeyCorpus peerSet = survivors.With(lost);

        PrefixShardPolicy policy = new(shardBits, ShardKeyMixing.Avalanche);
        using PrefixShardPartition localPartition = policy.Partition(survivors.Keys, KeyWidth, Pool);
        using PrefixShardPartition peerPartition = policy.Partition(peerSet.Keys, KeyWidth, Pool);

        HashSet<string> recovered = [];
        for(int s = 0; s < policy.ShardCount; s++)
        {
            IReadOnlyList<ReadOnlyMemory<byte>> localShard = localPartition.Shard(s);
            IReadOnlyList<ReadOnlyMemory<byte>> peerShard = peerPartition.Shard(s);

            IReadOnlyList<ReadOnlyMemory<byte>> difference = ReconcileShard(localShard, peerShard);

            //Direction resolution: the difference item is peer-only when the local shard does not hold it, and
            //must hash into this shard.
            HashSet<string> local = [.. localShard.Select(k => Convert.ToHexString(k.Span))];
            foreach(ReadOnlyMemory<byte> item in difference)
            {
                Assert.AreEqual(s, policy.ShardOf(item.Span));
                string hex = Convert.ToHexString(item.Span);
                if(!local.Contains(hex))
                {
                    Assert.IsTrue(recovered.Add(hex), "An item was recovered by more than one shard.");
                }
            }
        }

        HashSet<string> expected = [.. lost.Keys.Select(k => Convert.ToHexString(k.Span))];
        Assert.IsTrue(recovered.SetEquals(expected), "The composed per-shard recovery did not equal the injected loss.");
    }

    /// <summary>Encodes both shard operands, subtracts the streams symbol-wise, and absorbs until the decoder completes or the cap trips; returns the recovered symmetric difference. This is the single-block rung's sketch peel run at shard scope, standing in for the wire session transport.</summary>
    /// <param name="localShard">The local shard operand.</param>
    /// <param name="peerShard">The peer shard operand.</param>
    /// <returns>The shard's decoded symmetric difference.</returns>
    private static IReadOnlyList<ReadOnlyMemory<byte>> ReconcileShard(IReadOnlyList<ReadOnlyMemory<byte>> localShard, IReadOnlyList<ReadOnlyMemory<byte>> peerShard)
    {
        using ReconciliationEncoder local = Encoder(localShard);
        using ReconciliationEncoder peer = Encoder(peerShard);
        using ReconciliationDecoder decoder = new(Contract, Pool);

        int cap = 64 + (8 * (localShard.Count + peerShard.Count));
        int absorbed = 0;
        while(!decoder.IsComplete && absorbed < cap)
        {
            decoder.Absorb(local.ProduceNext().Combine(peer.ProduceNext()));
            absorbed++;
        }

        Assert.IsTrue(decoder.IsComplete, "A shard decode did not complete within its symbol cap.");

        //DecodedItems copies to owned arrays; read it before the decoder disposes.
        return decoder.DecodedItems;
    }

    /// <summary>Builds an encoder folded over the given items.</summary>
    /// <param name="items">The shard operand items.</param>
    /// <returns>The folded encoder; the caller disposes it.</returns>
    private static ReconciliationEncoder Encoder(IReadOnlyList<ReadOnlyMemory<byte>> items)
    {
        ReconciliationEncoder encoder = new(Contract, ReconciliationInjectivityEnforcement.None, Pool, items.Count);
        for(int i = 0; i < items.Count; i++)
        {
            encoder.Add(items[i].Span);
        }

        return encoder;
    }

}

