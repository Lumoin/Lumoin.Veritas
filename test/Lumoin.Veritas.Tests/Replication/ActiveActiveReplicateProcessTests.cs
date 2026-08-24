using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;
using static Lumoin.Veritas.Tests.Replication.ReplicateProcessBatteryHelpers;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The two-process active-active battery: real replicate-command processes over real loopback TCP, each owning
/// its own store — the production-wiring proof standing on the in-process mechanism rows. Two live engines each
/// accepting writes converge to the union; writes landing between pull rounds still converge; a damaged store
/// generation heals ACROSS PROCESSES through the sharded repair wire while writes quiesce; and an independently
/// seeded replica is refused by the wire's dictionary-epoch stamp, named as itself. Every replica descends from
/// one seeded store directory copied per replica (the same-lineage posture), writes stay dictionary-stable over
/// the seed's terms, and every ingest asserts the term count unmoved — the posture's runtime check.
/// </summary>
[TestClass]
internal sealed class ActiveActiveReplicateProcessTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Two live engines, each owning its store and accepting its own writes, converge to the union over the wire: disjoint dictionary-stable writes land on both, each pulls from the other, and the fingerprints and triple counts agree — with the converged side's next serve carrying the union to the other process.</summary>
    [TestMethod]
    public async Task TwoLiveEnginesEachOwningItsStoreConvergeAcrossProcesses()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-replicate-converge-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 40, shift: 0, startIndex: 0, count: 40);
            string ingestA = WriteTriplesFile(Path.Combine(root, "a.nt"), universe: 40, shift: 7, startIndex: 0, count: 10);
            string ingestB = WriteTriplesFile(Path.Combine(root, "b.nt"), universe: 40, shift: 13, startIndex: 0, count: 10);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB], TestContext.CancellationToken).ConfigureAwait(false);

            ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA));
            await using(replicaA.ConfigureAwait(false))
            {
                ReplicateProcess replicaB = ReplicateProcess.Start("--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB));
                await using(replicaB.ConfigureAwait(false))
                {
                    int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaA.SendAndWaitAsync($"peer {LoopbackHost}:{portB}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaB.SendAndWaitAsync($"peer {LoopbackHost}:{portA}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);

                    string statusA = await replicaA.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    string seedTerms = TokenAfter(statusA, "terms=");

                    string ingestLineA = await replicaA.SendAndWaitAsync($"ingest {ingestA}", "ingest ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(seedTerms, TokenAfter(ingestLineA, "terms="), "A dictionary-stable ingest must leave the term count unmoved.");
                    string ingestLineB = await replicaB.SendAndWaitAsync($"ingest {ingestB}", "ingest ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(seedTerms, TokenAfter(ingestLineB, "terms="), "A dictionary-stable ingest must leave the term count unmoved.");

                    string pullA = await replicaA.SendAndWaitAsync("reconcile-addonly", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains("converged=True", pullA, StringComparison.Ordinal);
                    string pullB = await replicaB.SendAndWaitAsync("reconcile-addonly", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains("converged=True", pullB, StringComparison.Ordinal);

                    string finalStatusA = await replicaA.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    string finalStatusB = await replicaB.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("60", TokenAfter(finalStatusA, "triples="), "A must hold the union of the seed and both ingests.");
                    Assert.AreEqual("60", TokenAfter(finalStatusB, "triples="), "B must hold the union of the seed and both ingests.");
                    Assert.AreEqual(await FingerprintAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false), await FingerprintAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false), "Converged replicas must fingerprint identically.");

                    await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Writes landing BETWEEN pull rounds — on the puller and on the served side, phase-interleaved across three ingests — still converge both replicas to the full union within the bounded explicit rounds.</summary>
    [TestMethod]
    public async Task WritesLandingBetweenPullRoundsStillConvergeAcrossProcesses()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-replicate-racing-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 40, shift: 0, startIndex: 0, count: 40);
            string first = WriteTriplesFile(Path.Combine(root, "f1.nt"), universe: 40, shift: 3, startIndex: 0, count: 10);
            string second = WriteTriplesFile(Path.Combine(root, "f2.nt"), universe: 40, shift: 9, startIndex: 10, count: 10);
            string third = WriteTriplesFile(Path.Combine(root, "f3.nt"), universe: 40, shift: 17, startIndex: 20, count: 10);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB], TestContext.CancellationToken).ConfigureAwait(false);

            ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA));
            await using(replicaA.ConfigureAwait(false))
            {
                ReplicateProcess replicaB = ReplicateProcess.Start("--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB));
                await using(replicaB.ConfigureAwait(false))
                {
                    int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaA.SendAndWaitAsync($"peer {LoopbackHost}:{portB}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaB.SendAndWaitAsync($"peer {LoopbackHost}:{portA}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);

                    //The interleave: a write lands on A, B pulls it, a NEW write lands on A between B's pull and
                    //A's own pull, and a write lands on B between rounds too — every pull runs against a set that
                    //moved since the previous round's serve.
                    _ = await replicaA.SendAndWaitAsync($"ingest {first}", "ingest ", TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaB.SendAndWaitAsync($"ingest {second}", "ingest ", TestContext.CancellationToken).ConfigureAwait(false);
                    string pullB1 = await replicaB.SendAndWaitAsync("reconcile-addonly", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains("converged=True", pullB1, StringComparison.Ordinal);
                    _ = await replicaA.SendAndWaitAsync($"ingest {third}", "ingest ", TestContext.CancellationToken).ConfigureAwait(false);
                    string pullA1 = await replicaA.SendAndWaitAsync("reconcile-addonly", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains("converged=True", pullA1, StringComparison.Ordinal);
                    string pullB2 = await replicaB.SendAndWaitAsync("reconcile-addonly", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains("converged=True", pullB2, StringComparison.Ordinal);

                    string finalStatusA = await replicaA.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    string finalStatusB = await replicaB.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("70", TokenAfter(finalStatusA, "triples="), "A must hold the union including every racing write.");
                    Assert.AreEqual("70", TokenAfter(finalStatusB, "triples="), "B must hold the union including every racing write.");
                    Assert.AreEqual(await FingerprintAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false), await FingerprintAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false), "Converged replicas must fingerprint identically.");

                    await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The crown row: a two-block loss in one replica's persisted store heals ACROSS PROCESSES through the sharded repair wire — the damaged replica restarts with self-heal against its live peer, the round recovers the loss through per-shard sessions over real TCP, publishes the healed generation, and names no loss and no refusal; writes quiesce throughout.</summary>
    [TestMethod]
    public async Task ACrossProcessShardedRepairHealsTheDamagedStoreOverTheWire()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-replicate-heal-").FullName;
        try
        {
            //The seed spans three system-of-record blocks, so garbaging two of them is a multi-block loss only
            //the sharded peer body can restore: no parity artifact exists and the single-block body caps at one.
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 8300, shift: 0, startIndex: 0, count: 8300);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB], TestContext.CancellationToken).ConfigureAwait(false);

            string damagedSegment = LatestSystemOfRecordFile(storeA);
            GarbageSegmentBlockInFile(damagedSegment, block: 0);
            GarbageSegmentBlockInFile(damagedSegment, block: 2);

            ReplicateProcess replicaB = ReplicateProcess.Start("--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB));
            await using(replicaB.ConfigureAwait(false))
            {
                int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);

                ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--peer", $"{LoopbackHost}:{portB}", "--self-heal", "--heal-interval", "1", "--identity-dir", IdentityDirectoryFor(storeA));
                await using(replicaA.ConfigureAwait(false))
                {
                    string reingested = await replicaA.WaitForAnyLineAsync("heal kind=Reingested", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains(" role=1 ", reingested, StringComparison.Ordinal);
                    Assert.Contains(" items=8300", reingested, StringComparison.Ordinal);
                    _ = await replicaA.WaitForAnyLineAsync("heal kind=GenerationHealed", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsFalse(replicaA.SawLine("heal kind=NamedLoss"), "The wire-backed sharded body healed the loss, so no loss is named.");
                    Assert.IsFalse(replicaA.SawLine("heal kind=ShardedRepairRefused"), "A clean heal refuses nothing.");
                    Assert.IsFalse(replicaA.SawLine("shardfault"), "No shard fetch declined on the clean heal.");

                    Assert.AreEqual(await FingerprintAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false), await FingerprintAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false), "The healed replica must agree with its peer.");

                    await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>An INDEPENDENTLY seeded replica dialing a live peer is refused by the wire's dictionary-epoch stamp: the caller passes its own epoch, so the front gate never fires and the session's per-round stamp check names the cross-lineage peer as the epoch mismatch it is — never as an unavailable peer or corruption.</summary>
    [TestMethod]
    public async Task AnIndependentlySeededReplicaIsRefusedByTheWireEpochStamp()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-replicate-epoch-").FullName;
        try
        {
            //Both replicas seed the SAME content, but independently: each seed mints its own dictionary epoch,
            //so the identical bytes still form two lineages the structural wire must keep apart.
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 10, shift: 0, startIndex: 0, count: 10);
            string storeA = Path.Combine(root, "store-a");
            string storeC = Path.Combine(root, "store-c");
            await SeedStoreAsync(storeA, seedFile, TestContext.CancellationToken).ConfigureAwait(false);
            await SeedStoreAsync(storeC, seedFile, TestContext.CancellationToken).ConfigureAwait(false);

            ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA));
            await using(replicaA.ConfigureAwait(false))
            {
                int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);

                ReplicateProcess replicaC = ReplicateProcess.Start("--store", storeC, "--identity-dir", IdentityDirectoryFor(storeC));
                await using(replicaC.ConfigureAwait(false))
                {
                    _ = await replicaC.SendAndWaitAsync($"peer {LoopbackHost}:{portA}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);
                    string pull = await replicaC.SendAndWaitAsync("reconcile-addonly", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.Contains("converged=False", pull, StringComparison.Ordinal);
                    Assert.Contains("outcome=PeerEpochMismatch", pull, StringComparison.Ordinal);

                    await QuitAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false);
                    await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
