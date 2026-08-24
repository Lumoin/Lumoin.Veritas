using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;
using static Lumoin.Veritas.Tests.Replication.ReplicateProcessBatteryHelpers;
using Path = System.IO.Path;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The dotted two-process battery: real replicate-command processes over real loopback TCP driving the
/// remove-aware lane — the production-wiring proof standing on the in-process dotted mechanism rows. A
/// retraction on one live host propagates to its peer through the dotted reconcile and STAYS retracted through
/// a wire-backed self-heal repair cycle and further reconciles; an unknown service selector is answered with
/// the explicit one-byte refusal, measurably distinct from network death; and THREE processes carry a
/// retraction transitively through pairwise exchanges — the protocol is identical whatever the topology, and a
/// geo-distributed deployment differs only in setup (peer routing and cadence), which moves convergence
/// latency, never the outcome. Every replica descends from one seeded store directory copied per replica under
/// its OWN host identity, and writes stay dictionary-stable over the seed's terms.
/// </summary>
[TestClass]
internal sealed class DottedReplicateProcessTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The crown row: a retraction on one live host propagates to its peer over real TCP through the dotted reconcile, and STANDS on both processes through a wire-backed self-heal repair cycle and a further reconcile — the live dotted lane converges histories while repair only restores a replica's own recorded truth.</summary>
    [TestMethod]
    public async Task ARetractionStandsAcrossProcessesThroughDottedReconcileAndRepair()
    {
        RequireExecutable();
        string root = System.IO.Directory.CreateTempSubdirectory("veritas-dotted-crown-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 40, shift: 0, startIndex: 0, count: 40);
            string retractFile = WriteRetractionFile(Path.Combine(root, "retract.rq"), universe: 40, shift: 0, index: 3);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB], TestContext.CancellationToken).ConfigureAwait(false);

            //B's copy takes at-rest damage before it starts: its self-heal loop must restore the generation
            //over the wire from A while the dotted lane runs beside it.
            GarbageSegmentBlockInFile(LatestSystemOfRecordFile(storeB), block: 0);

            ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA));
            await using(replicaA.ConfigureAwait(false))
            {
                int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);

                ReplicateProcess replicaB = ReplicateProcess.Start("--store", storeB, "--listen", "0", "--peer", $"{LoopbackHost}:{portA}", "--self-heal", "--heal-interval", "1", "--identity-dir", IdentityDirectoryFor(storeB));
                await using(replicaB.ConfigureAwait(false))
                {
                    int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaA.SendAndWaitAsync($"peer {LoopbackHost}:{portB}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);

                    //Both hosts run remove-aware: the seed carried the creation baseline, the copies carried
                    //the causal history, and each host opened under its own identity.
                    string statusB = await replicaB.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("RemoveAware", TokenAfter(statusB, "causality="), "The cloned replica recovers remove-aware under its own identity.");

                    //The wire-backed heal restores B's damaged generation from A.
                    _ = await replicaB.WaitForAnyLineAsync("heal kind=GenerationHealed", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsFalse(replicaB.SawLine("heal kind=NamedLoss"), "The wire-backed heal restored the loss, so no loss is named.");

                    //The retraction on A, then B's dotted reconcile: the drop propagates over real TCP.
                    _ = await replicaA.SendAndWaitAsync($"update {retractFile}", "update ok", TestContext.CancellationToken).ConfigureAwait(false);
                    string firstReconcile = await replicaB.SendAndWaitAsync("reconcile", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("Converged", TokenAfter(firstReconcile, "kind="), "The dotted reconcile converges across processes.");

                    string statusAfterDrop = await replicaB.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("39", TokenAfter(statusAfterDrop, "triples="), "The retraction landed on the peer as a drop, never a resurrection.");

                    //The self-heal loop keeps running its one-second rounds across the exchanges; a further
                    //reconcile after the heal cycle moves nothing — repair restored at-rest truth, and the
                    //dotted lane holds the live histories converged.
                    string secondReconcile = await replicaB.SendAndWaitAsync("reconcile", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("AlreadyConsistent", TokenAfter(secondReconcile, "kind="), "A further reconcile after the repair cycle moves nothing.");

                    string finalStatusA = await replicaA.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("39", TokenAfter(finalStatusA, "triples="), "The retraction stands on the retracting host.");
                    Assert.AreEqual(await FingerprintAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false), await FingerprintAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false), "Converged replicas fingerprint identically with the retraction standing.");

                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The unknown-selector split, on the real wire: a live host answers an unknown service selector with EXACTLY the one named refusal byte before closing — so a dialing peer distinguishes service-unknown from network death — while a reconcile against a dead endpoint reports peer-unavailable, never unsupported.</summary>
    [TestMethod]
    public async Task AnUnknownSelectorIsRefusedByTheNamedByteWhileDeathReportsUnavailable()
    {
        RequireExecutable();
        string root = System.IO.Directory.CreateTempSubdirectory("veritas-dotted-selector-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 10, shift: 0, startIndex: 0, count: 10);
            string storeA = Path.Combine(root, "store-a");
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA], TestContext.CancellationToken).ConfigureAwait(false);

            ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA));
            await using(replicaA.ConfigureAwait(false))
            {
                int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);

                //The refusal-byte arm: an unknown selector is answered with exactly one zero byte, then the
                //close — the explicit evidence, never inferred from silence.
                using(TcpClient probe = new())
                {
                    await probe.ConnectAsync(LoopbackHost, portA, TestContext.CancellationToken).ConfigureAwait(false);
                    NetworkStream stream = probe.GetStream();
                    await stream.WriteAsync(new byte[] { 9 }, TestContext.CancellationToken).ConfigureAwait(false);
                    byte[] verdict = new byte[2];
                    int first = await stream.ReadAsync(verdict.AsMemory(0, 1), TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(1, first, "The host answers the unknown selector with a verdict byte.");
                    Assert.AreEqual(0, verdict[0], "The verdict is the named unknown-service refusal byte.");
                    int second;
                    try
                    {
                        second = await stream.ReadAsync(verdict.AsMemory(1, 1), TestContext.CancellationToken).ConfigureAwait(false);
                    }
                    catch(System.IO.IOException)
                    {
                        //A reset on the closing socket is the same evidence as a clean end: nothing followed.
                        second = 0;
                    }

                    Assert.AreEqual(0, second, "Nothing follows the refusal byte; the connection closes.");
                }

                //The death arm: a peer endpoint that no longer listens reports peer-unavailable by name.
                int deadPort;
                using(TcpListener ephemeral = new(System.Net.IPAddress.Loopback, 0))
                {
                    ephemeral.Start();
                    deadPort = ((System.Net.IPEndPoint)ephemeral.LocalEndpoint).Port;
                    ephemeral.Stop();
                }

                _ = await replicaA.SendAndWaitAsync($"peer {LoopbackHost}:{deadPort}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);
                string reconcile = await replicaA.SendAndWaitAsync("reconcile", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual("PeerUnavailable", TokenAfter(reconcile, "kind="), "An absent reply is peer death, never inferred as unsupported.");

                await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The three-process row: a retraction on the first host reaches the third TRANSITIVELY through pairwise dotted reconciles over real TCP, all three fingerprints agree, and a further reconcile moves nothing — the pairwise protocol is topology-agnostic, so a geo-distributed deployment differs only in peer routing and cadence, never in outcome.</summary>
    [TestMethod]
    public async Task ThreeProcessesCarryARetractionTransitivelyThroughPairwiseReconciles()
    {
        RequireExecutable();
        string root = System.IO.Directory.CreateTempSubdirectory("veritas-dotted-threeproc-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 40, shift: 0, startIndex: 0, count: 40);
            string retractFile = WriteRetractionFile(Path.Combine(root, "retract.rq"), universe: 40, shift: 0, index: 7);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string storeC = Path.Combine(root, "store-c");
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB, storeC], TestContext.CancellationToken).ConfigureAwait(false);

            ReplicateProcess replicaA = ReplicateProcess.Start("--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA));
            await using(replicaA.ConfigureAwait(false))
            {
                ReplicateProcess replicaB = ReplicateProcess.Start("--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB));
                await using(replicaB.ConfigureAwait(false))
                {
                    ReplicateProcess replicaC = ReplicateProcess.Start("--store", storeC, "--identity-dir", IdentityDirectoryFor(storeC));
                    await using(replicaC.ConfigureAwait(false))
                    {
                        int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                        int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);

                        //The chain routing: B pulls from A, C pulls from B — one of many topologies the same
                        //pairwise protocol serves; the setup, not the protocol, decides the geometry.
                        _ = await replicaB.SendAndWaitAsync($"peer {LoopbackHost}:{portA}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);
                        _ = await replicaC.SendAndWaitAsync($"peer {LoopbackHost}:{portB}", "peer ok", TestContext.CancellationToken).ConfigureAwait(false);

                        _ = await replicaA.SendAndWaitAsync($"update {retractFile}", "update ok", TestContext.CancellationToken).ConfigureAwait(false);

                        string hopOne = await replicaB.SendAndWaitAsync("reconcile", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("Converged", TokenAfter(hopOne, "kind="), "The first hop converges and carries the drop.");
                        string hopTwo = await replicaC.SendAndWaitAsync("reconcile", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("Converged", TokenAfter(hopTwo, "kind="), "The second hop converges transitively.");

                        string statusC = await replicaC.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("39", TokenAfter(statusC, "triples="), "The retraction reached the third process transitively and nothing resurrected it.");

                        string settled = await replicaC.SendAndWaitAsync("reconcile", "reconcile ", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("AlreadyConsistent", TokenAfter(settled, "kind="), "A further reconcile moves nothing — the chain converged.");

                        string fingerprintA = await FingerprintAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual(fingerprintA, await FingerprintAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false), "The first and second replicas agree.");
                        Assert.AreEqual(fingerprintA, await FingerprintAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false), "The first and third replicas agree.");

                        await QuitAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false);
                        await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                        await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }
}
