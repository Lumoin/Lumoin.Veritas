using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using static Lumoin.Veritas.Tests.Replication.ReplicateProcessBatteryHelpers;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The consensus metadata plane's cross-process battery: real replicate-command processes over real loopback
/// TCP, coordinating replica identity and the lineage baseline on one chain through the production wire codec —
/// the production-wiring proof standing on the in-process plane rows and the socket transport rows. Three hosts
/// claim three distinct axes on one chain and each claim is an APPEND; a host the founder list does not name is
/// refused before it composes anything; a chain that loses one member of three still decides while a chain that
/// loses two answers by value and keeps serving data; the consensus host state lives beside the host identity
/// rather than inside the copied store and survives a restart; and a host seeding a SECOND independent lineage
/// against a confirmed baseline is refused at its open, loudly and across processes.
/// </summary>
/// <remarks>
/// Every replica runs under its OWN pre-written identity, because a chain's founder list must be known before
/// any founder starts — all of them mint the same chain identity from it. The endpoint map is bound after the
/// hosts report their ephemeral ports, which is the one-run-then-wire workflow an operator performs through its
/// own locator; a founder whose route is not bound yet is an unreachable member, never a composition failure.
/// </remarks>
[TestClass]
internal sealed class MetadataReplicateProcessTests
{
    /// <summary>How many times a row re-reads an answer-driven report before it gives up. It is a refusal bound and never a cadence: every read here ends on the transition it is about, and the count only turns a regression that would hang into a failed row.</summary>
    private const int SettleReads = 40;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The crown row: three replicate processes on ONE chain claim three DISTINCT identity axes, every claim lands at its own register version because the claim rule appends, the chain identity is byte-identical on all three (they were given the same founder SET, in different orders), and a re-issued claim answers idempotently at the version its first one took.</summary>
    [TestMethod]
    public async Task ThreeReplicateProcessesClaimDistinctIdentitiesOnOneChain()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-metadata-claims-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 20, shift: 0, startIndex: 0, count: 20);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string storeC = Path.Combine(root, "store-c");
            string hexA = WriteIdentityFile(IdentityDirectoryFor(storeA));
            string storeAIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeA));
            string hexB = WriteIdentityFile(IdentityDirectoryFor(storeB));
            string storeBIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeB));
            string hexC = WriteIdentityFile(IdentityDirectoryFor(storeC));
            string storeCIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeC));
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB, storeC], TestContext.CancellationToken).ConfigureAwait(false);

            //Three different listing orders of one founder SET: the command mints the chain in canonical order,
            //so all three hosts must land on one chain identity.
            string founderA = FounderToken(hexA, storeAIncarnation);
            string founderB = FounderToken(hexB, storeBIncarnation);
            string founderC = FounderToken(hexC, storeCIncarnation);
            string[] foundersA = FounderArguments(founderA, founderB, founderC);
            string[] foundersB = FounderArguments(founderB, founderC, founderA);
            string[] foundersC = FounderArguments(founderC, founderA, founderB);

            ReplicateProcess replicaA = ReplicateProcess.Start(["--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA), .. foundersA]);
            await using(replicaA.ConfigureAwait(false))
            {
                ReplicateProcess replicaB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB), .. foundersB]);
                await using(replicaB.ConfigureAwait(false))
                {
                    ReplicateProcess replicaC = ReplicateProcess.Start(["--store", storeC, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeC), .. foundersC]);
                    await using(replicaC.ConfigureAwait(false))
                    {
                        string startupLine = await AxisLineAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);

                        Assert.AreEqual(hexA, AxisOf(startupLine), "The axis line prints the identity the host actually runs under, which is what an operator copies into the founder list.");
                        Assert.AreEqual(storeAIncarnation, StoreOf(startupLine), "The same line prints the store that host holds, which is the other half of what the founder list names.");

                        int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                        int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                        int portC = await ListeningPortAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false);

                        string chainA = TokenAfter(await PlaneLineAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false), "chain=");
                        string chainB = TokenAfter(await PlaneLineAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false), "chain=");
                        string chainC = TokenAfter(await PlaneLineAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false), "chain=");
                        Assert.AreEqual(chainA, chainB, "Operators who agree on a founder SET mint one chain whatever order they listed it in.");
                        Assert.AreEqual(chainA, chainC, "Operators who agree on a founder SET mint one chain whatever order they listed it in.");

                        await BindFullMeshAsync(
                            [replicaA, replicaB, replicaC],
                            [hexA, hexB, hexC],
                            [portA, portB, portC],
                            TestContext.CancellationToken).ConfigureAwait(false);

                        string claimA = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        string claimB = await replicaB.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        string claimC = await replicaC.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("Claimed", TokenAfter(claimA, "claim="), $"The first host's axis was absent from the record, so consensus appended it: {claimA}");
                        Assert.AreEqual("Claimed", TokenAfter(claimB, "claim="), $"The second host's axis was absent from the record, so consensus appended it: {claimB}");
                        Assert.AreEqual("Claimed", TokenAfter(claimC, "claim="), $"The third host's axis was absent from the record, so consensus appended it: {claimC}");

                        ulong versionA = VersionOf(claimA);
                        ulong versionB = VersionOf(claimB);
                        ulong versionC = VersionOf(claimC);
                        Assert.AreNotEqual(versionA, versionB, "Claims APPEND, so two axes never settle at one register version.");
                        Assert.AreNotEqual(versionA, versionC, "Claims APPEND, so two axes never settle at one register version.");
                        Assert.AreNotEqual(versionB, versionC, "Claims APPEND, so two axes never settle at one register version.");

                        string repeat = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("AlreadyClaimedBySelf", TokenAfter(repeat, "claim="), $"A re-issued claim is the idempotent arm and never a second entry: {repeat}");
                        Assert.AreEqual(versionA, VersionOf(repeat), "The re-issued claim answers at the version its first one settled at, because the entry was never rewritten.");

                        await QuitAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false);
                        await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                        await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The membership refusal, across processes: a host whose own axis the founder list does not name is refused BEFORE it composes a plane, a store, or an engine — it decides nothing it would propose, so starting it would leave an operator with a host that looks coordinated while every obligation answers that it stands outside the configuration. The chain's live hosts are untouched.</summary>
    [TestMethod]
    public async Task AHostMissingFromTheFounderListIsRefusedBeforeAnyPlaneComposes()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-metadata-outsider-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 20, shift: 0, startIndex: 0, count: 20);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string storeD = Path.Combine(root, "store-d");
            string hexA = WriteIdentityFile(IdentityDirectoryFor(storeA));
            string storeAIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeA));
            string hexB = WriteIdentityFile(IdentityDirectoryFor(storeB));
            string storeBIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeB));
            string hexD = WriteIdentityFile(IdentityDirectoryFor(storeD));
            string storeDIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeD));
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB, storeD], TestContext.CancellationToken).ConfigureAwait(false);
            string[] founders = FounderArguments(FounderToken(hexA, storeAIncarnation), FounderToken(hexB, storeBIncarnation));

            ReplicateProcess replicaA = ReplicateProcess.Start(["--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA), .. founders]);
            await using(replicaA.ConfigureAwait(false))
            {
                ReplicateProcess replicaB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB), .. founders]);
                await using(replicaB.ConfigureAwait(false))
                {
                    int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaA, hexB, portB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaB, hexA, portA, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);

                    //The outsider: its own identity is real and its routes are right, but the chain's membership
                    //does not name it.
                    ReplicateProcess outsider = ReplicateProcess.Start([
                        "--store", storeD,
                        "--identity-dir", IdentityDirectoryFor(storeD),
                        .. founders,
                        "--metadata-route", $"{hexA}={LoopbackHost}:{portA}",
                        "--metadata-route", $"{hexB}={LoopbackHost}:{portB}"]);
                    await using(outsider.ConfigureAwait(false))
                    {
                        int exitCode = await RefusedExitCodeAsync(outsider, TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual(1, exitCode, $"The refused host exits non-zero; stderr: {string.Join(" | ", outsider.ErrorLines())}");
                        Assert.IsFalse(outsider.SawLine("plane chain="), "The refusal comes before the plane composes, so no chain was ever minted by the refused host.");
                        Assert.IsFalse(outsider.SawLine("identity "), "The refused host never opened an engine, so it printed no identity line.");
                        Assert.IsTrue(outsider.SawLine($"axis {hexD}"), "The axis line still prints, because it is what an operator reads to correct the founder list.");
                        Assert.Contains(hexD, string.Join(" | ", outsider.ErrorLines()), StringComparison.Ordinal);
                    }

                    string statusA = await replicaA.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("20", TokenAfter(statusA, "triples="), "The chain's live host is untouched by a refused newcomer.");
                    string planeStatusA = await replicaA.SendAndWaitAsync("metadata-status", "plane status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("2/2", TokenAfter(planeStatusA, "reachable="), $"Both members of the chain still answer their own version probes: {planeStatusA}");

                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The quorum row, across processes: a chain of three that loses ONE member still decides — its readiness reports two of three reachable with the quorum having learned the held version, and a claim still answers definitively — while a chain that loses TWO answers UNDECIDED by value, without the host dying, and keeps serving its data lane. The plane is never a liveness dependency.</summary>
    [TestMethod]
    public async Task AQuorumMinusOneStillDecidesAndAMajorityDownIsUndecided()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-metadata-quorum-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 40, shift: 0, startIndex: 0, count: 40);
            string ingestFile = WriteTriplesFile(Path.Combine(root, "more.nt"), universe: 40, shift: 7, startIndex: 0, count: 10);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string storeC = Path.Combine(root, "store-c");
            string hexA = WriteIdentityFile(IdentityDirectoryFor(storeA));
            string storeAIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeA));
            string hexB = WriteIdentityFile(IdentityDirectoryFor(storeB));
            string storeBIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeB));
            string hexC = WriteIdentityFile(IdentityDirectoryFor(storeC));
            string storeCIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeC));
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB, storeC], TestContext.CancellationToken).ConfigureAwait(false);
            string[] founders = FounderArguments(FounderToken(hexA, storeAIncarnation), FounderToken(hexB, storeBIncarnation), FounderToken(hexC, storeCIncarnation));

            ReplicateProcess replicaA = ReplicateProcess.Start(["--store", storeA, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeA), .. founders]);
            await using(replicaA.ConfigureAwait(false))
            {
                ReplicateProcess replicaB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB), .. founders]);
                await using(replicaB.ConfigureAwait(false))
                {
                    ReplicateProcess replicaC = ReplicateProcess.Start(["--store", storeC, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeC), .. founders]);
                    await using(replicaC.ConfigureAwait(false))
                    {
                        int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                        int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                        int portC = await ListeningPortAsync(replicaC, TestContext.CancellationToken).ConfigureAwait(false);
                        await BindFullMeshAsync(
                            [replicaA, replicaB, replicaC],
                            [hexA, hexB, hexC],
                            [portA, portB, portC],
                            TestContext.CancellationToken).ConfigureAwait(false);

                        string claimA = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        _ = await replicaB.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        _ = await replicaC.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("Claimed", TokenAfter(claimA, "claim="), $"The first host's claim settles before any member is taken down: {claimA}");
                        ulong claimedVersionA = VersionOf(claimA);

                        //One member down: a quorum of two of three still decides.
                        await replicaC.KillAsync().ConfigureAwait(false);
                        string twoOfThree = await SettledStatusAsync(replicaA, "2/3", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("True", TokenAfter(twoOfThree, "quorum="), $"Two members of three have learned the held version, which is a quorum: {twoOfThree}");

                        string stillDecides = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("AlreadyClaimedBySelf", TokenAfter(stillDecides, "claim="), $"A chain missing one of three still reaches a decision: {stillDecides}");
                        Assert.AreEqual(claimedVersionA, VersionOf(stillDecides), "The decision names the version the claim settled at, which nothing has moved.");

                        //Two members down: definite ignorance, by value, and the data lane keeps serving.
                        await replicaB.KillAsync().ConfigureAwait(false);
                        string oneOfThree = await SettledStatusAsync(replicaA, "1/3", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("False", TokenAfter(oneOfThree, "quorum="), $"One member of three is no quorum: {oneOfThree}");

                        string undecided = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("Undecided", TokenAfter(undecided, "claim="), $"An unreachable quorum is definite ignorance answered by value, never a refusal: {undecided}");
                        Assert.AreEqual("0", TokenAfter(undecided, "version="), $"An obligation that decided nothing names no version: {undecided}");

                        string ingested = await replicaA.SendAndWaitAsync($"ingest {ingestFile}", "ingest ", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("10", TokenAfter(ingested, "triples="), $"The data lane commits while the plane cannot decide: {ingested}");
                        string statusA = await replicaA.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual("50", TokenAfter(statusA, "triples="), "The host serves its own state with the plane quorumless.");
                        Assert.IsNotNull(await FingerprintAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false), "The host folds its committed set with the plane quorumless.");

                        await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The placement row: the consensus host state and this host's confirmed facts live beside the replica IDENTITY and never inside the store directory a deployment copies per replica — a node store inside the store would clone one member's consensus host into every copy. A restarted host comes back at the version it learned durably, and one that lost its node state but kept its store marker comes back fresh under the SAME admitted store and catches its own claim up over the wire rather than re-deciding it. A store wiped marker and all is the row beside this one.</summary>
    [TestMethod]
    public async Task TheMetadataNodeStoreLivesBesideTheIdentityAndSurvivesARestart()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-metadata-restart-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 20, shift: 0, startIndex: 0, count: 20);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string identityA = IdentityDirectoryFor(storeA);
            string identityB = IdentityDirectoryFor(storeB);
            string hexA = WriteIdentityFile(identityA);
            string hexB = WriteIdentityFile(identityB);
            string storeAIncarnation = WriteStoreMarkerFile(identityA);
            string storeBIncarnation = WriteStoreMarkerFile(identityB);
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB], TestContext.CancellationToken).ConfigureAwait(false);
            string[] founders = FounderArguments(FounderToken(hexA, storeAIncarnation), FounderToken(hexB, storeBIncarnation));
            string metadataB = Path.Combine(identityB, "metadata");

            ReplicateProcess replicaA = ReplicateProcess.Start(["--store", storeA, "--listen", "0", "--identity-dir", identityA, .. founders]);
            await using(replicaA.ConfigureAwait(false))
            {
                int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
                ulong claimedVersionB;
                ulong heldVersionB;

                ReplicateProcess replicaB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", identityB, .. founders]);
                await using(replicaB.ConfigureAwait(false))
                {
                    int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaA, hexB, portB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaB, hexA, portA, TestContext.CancellationToken).ConfigureAwait(false);

                    _ = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                    string claimB = await replicaB.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("Claimed", TokenAfter(claimB, "claim="), $"The second host's axis was absent from the record: {claimB}");
                    claimedVersionB = VersionOf(claimB);

                    string statusB = await replicaB.SendAndWaitAsync("metadata-status", "plane status ", TestContext.CancellationToken).ConfigureAwait(false);
                    heldVersionB = VersionOf(statusB);
                    Assert.IsGreaterThan(0UL, heldVersionB, $"The second host has learned a version durably before it is stopped: {statusB}");

                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                }

                //The placement, on the file system: HOST state beside the identity, never inside the store a
                //deployment copies per replica.
                Assert.IsFalse(File.Exists(Path.Combine(storeB, "metadata-node.state")), "The consensus host state is never written into the data store directory, which a deployment copies to seed peers.");
                Assert.IsFalse(File.Exists(Path.Combine(storeB, "metadata-facts.bin")), "The host's confirmed facts are never written into the data store directory either.");
                Assert.IsTrue(File.Exists(Path.Combine(metadataB, "metadata-node.state")), "The consensus host state lives beside the replica identity, which never travels with a copied store.");
                Assert.IsTrue(File.Exists(Path.Combine(metadataB, "metadata-facts.bin")), "The host's confirmed facts live beside the replica identity too.");

                ReplicateProcess revivedB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", identityB, .. founders]);
                await using(revivedB.ConfigureAwait(false))
                {
                    int revivedPortB = await ListeningPortAsync(revivedB, TestContext.CancellationToken).ConfigureAwait(false);
                    string planeB = await PlaneLineAsync(revivedB, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("True", TokenAfter(planeB, "restored="), $"The restarted host came back from its own node store: {planeB}");
                    Assert.AreEqual(heldVersionB, VersionOf(planeB), $"It came back at the version it had learned durably: {planeB}");

                    //A restarted member binds a fresh ephemeral port, so the deployment's routing is told where
                    //it went; the fellow's channel then redials on the call after the fault its next one meets.
                    _ = await MetadataRouteAsync(replicaA, hexB, revivedPortB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(revivedB, hexA, portA, TestContext.CancellationToken).ConfigureAwait(false);
                    string rejoined = await SettledStatusAsync(replicaA, "2/2", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("True", TokenAfter(rejoined, "quorum="), $"The rejoined member counts toward the quorum again: {rejoined}");

                    await QuitAsync(revivedB, TestContext.CancellationToken).ConfigureAwait(false);
                }

                //The node state and the confirmed facts deleted, the store MARKER kept: a host that lost its
                //record but not its identity. It comes back as a fresh consensus host under the store the
                //membership admitted, so it catches its own claim up over the wire rather than re-deciding it.
                //Deleting the marker too is a wiped store, which is the refusal row beside this one.
                File.Delete(Path.Combine(metadataB, "metadata-node.state"));
                File.Delete(Path.Combine(metadataB, "metadata-facts.bin"));

                ReplicateProcess freshB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", identityB, .. founders]);
                await using(freshB.ConfigureAwait(false))
                {
                    string freshStartup = await AxisLineAsync(freshB, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(storeBIncarnation, StoreOf(freshStartup), $"The kept marker is the store's identity, so the host answers under the incarnation the membership admitted: {freshStartup}");

                    int freshPortB = await ListeningPortAsync(freshB, TestContext.CancellationToken).ConfigureAwait(false);
                    string planeB = await PlaneLineAsync(freshB, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("False", TokenAfter(planeB, "restored="), $"A deleted node state is a fresh consensus host: {planeB}");
                    Assert.AreEqual("unwritten", TokenAfter(planeB, "version="), $"A fresh consensus host has learned nothing: {planeB}");

                    _ = await MetadataRouteAsync(replicaA, hexB, freshPortB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(freshB, hexA, portA, TestContext.CancellationToken).ConfigureAwait(false);

                    string caughtUp = await freshB.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("AlreadyClaimedBySelf", TokenAfter(caughtUp, "claim="), $"The chain already holds this host's claim, so a fresh host adopts it rather than appending a second one: {caughtUp}");
                    Assert.AreEqual(claimedVersionB, VersionOf(caughtUp), "The adopted claim names the version it first settled at, which is what proves it came over the wire rather than being decided afresh.");

                    await QuitAsync(freshB, TestContext.CancellationToken).ConfigureAwait(false);
                }

                await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The refusal row a membership over stores exists for: a store wiped marker and all mints a FRESH incarnation, the membership still admits the one it lost, and the host is refused rather than served — its startup line prints the new store and its claim answers undecided where the kept-marker row's answers already-claimed-by-self. Re-admitting the rebuilt store is retire-then-admit, a different row.</summary>
    [TestMethod]
    public async Task AWipedMetadataStoreIsRefusedRatherThanServed()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-metadata-wiped-").FullName;
        try
        {
            string seedFile = WriteTriplesFile(Path.Combine(root, "seed.nt"), universe: 20, shift: 0, startIndex: 0, count: 20);
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string identityA = IdentityDirectoryFor(storeA);
            string identityB = IdentityDirectoryFor(storeB);
            string hexA = WriteIdentityFile(identityA);
            string hexB = WriteIdentityFile(identityB);
            string storeAIncarnation = WriteStoreMarkerFile(identityA);
            string storeBIncarnation = WriteStoreMarkerFile(identityB);
            await SeedAndCopyAsync(Path.Combine(root, "store-seed"), seedFile, [storeA, storeB], TestContext.CancellationToken).ConfigureAwait(false);
            string[] founders = FounderArguments(FounderToken(hexA, storeAIncarnation), FounderToken(hexB, storeBIncarnation));
            string metadataB = Path.Combine(identityB, "metadata");

            ReplicateProcess replicaA = ReplicateProcess.Start(["--store", storeA, "--listen", "0", "--identity-dir", identityA, .. founders]);
            await using(replicaA.ConfigureAwait(false))
            {
                int portA = await ListeningPortAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);

                ReplicateProcess replicaB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", identityB, .. founders]);
                await using(replicaB.ConfigureAwait(false))
                {
                    int portB = await ListeningPortAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaA, hexB, portB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaB, hexA, portA, TestContext.CancellationToken).ConfigureAwait(false);

                    _ = await replicaA.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                    string claimB = await replicaB.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("Claimed", TokenAfter(claimB, "claim="), $"The admitted store claims while it still holds its marker: {claimB}");

                    await QuitAsync(replicaB, TestContext.CancellationToken).ConfigureAwait(false);
                }

                //The WIPE: marker and all. The membership admitted the incarnation this directory held, and
                //what restarts under it is a different store presenting the same replica.
                Directory.Delete(metadataB, recursive: true);

                ReplicateProcess wipedB = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", identityB, .. founders]);
                await using(wipedB.ConfigureAwait(false))
                {
                    string wipedStartup = await AxisLineAsync(wipedB, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(hexB, AxisOf(wipedStartup), $"The replica identity survives the wipe, which is exactly why it alone cannot vouch for the store: {wipedStartup}");
                    Assert.AreNotEqual(storeBIncarnation, StoreOf(wipedStartup), $"The wiped store mints a fresh incarnation, so the line prints a store the membership never admitted: {wipedStartup}");

                    int wipedPortB = await ListeningPortAsync(wipedB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(replicaA, hexB, wipedPortB, TestContext.CancellationToken).ConfigureAwait(false);
                    _ = await MetadataRouteAsync(wipedB, hexA, portA, TestContext.CancellationToken).ConfigureAwait(false);

                    string refused = await wipedB.SendAndWaitAsync("metadata-claim", "plane claim=", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("Undecided", TokenAfter(refused, "claim="), $"A store the membership never admitted is refused rather than served, so its obligation spends its budget and answers undecided — the chain holds this axis's claim, and a wiped store may not adopt it: {refused}");

                    await QuitAsync(wipedB, TestContext.CancellationToken).ConfigureAwait(false);
                }

                await QuitAsync(replicaA, TestContext.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The lineage row, across processes: one host's creation baseline is recorded and CONFIRMED on the coordinated record while a quorum is reachable, and a host that would seed a SECOND independent lineage against it is refused at its open — the two-phase intent closes the independent-baseline storm before the local durable commit, so the refused host never starts and the confirmed one is unaffected.</summary>
    [TestMethod]
    public async Task AConflictingLineageBaselineRefusesTheOpenAcrossProcesses()
    {
        RequireExecutable();
        string root = Directory.CreateTempSubdirectory("veritas-metadata-lineage-").FullName;
        try
        {
            string storeA = Path.Combine(root, "store-a");
            string storeB = Path.Combine(root, "store-b");
            string storeD = Path.Combine(root, "store-d");
            string hexA = WriteIdentityFile(IdentityDirectoryFor(storeA));
            string storeAIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeA));
            string hexB = WriteIdentityFile(IdentityDirectoryFor(storeB));
            string storeBIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeB));
            string hexD = WriteIdentityFile(IdentityDirectoryFor(storeD));
            string storeDIncarnation = WriteStoreMarkerFile(IdentityDirectoryFor(storeD));
            string[] founders = FounderArguments(FounderToken(hexA, storeAIncarnation), FounderToken(hexB, storeBIncarnation), FounderToken(hexD, storeDIncarnation));

            //Every store here is its OWN empty directory, so every host mints its own creation baseline: the
            //independent-lineage case the coordinated record exists to close.
            ReplicateProcess quorumMate = ReplicateProcess.Start(["--store", storeB, "--listen", "0", "--identity-dir", IdentityDirectoryFor(storeB), .. founders]);
            await using(quorumMate.ConfigureAwait(false))
            {
                int portB = await ListeningPortAsync(quorumMate, TestContext.CancellationToken).ConfigureAwait(false);

                //The quorum mate started with no routes bound, so its own baseline intent was undecided and
                //failed open: nothing of its lineage reached the record, and it serves as a recorder.
                string mateIdentity = await quorumMate.WaitForAnyLineAsync("identity ", TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual("Pending", TokenAfter(mateIdentity, "coordination="), $"An open whose fellows are unreachable fails OPEN and reports the standing by value: {mateIdentity}");

                ReplicateProcess confirmed = ReplicateProcess.Start([
                    "--store", storeA,
                    "--listen", "0",
                    "--identity-dir", IdentityDirectoryFor(storeA),
                    .. founders,
                    "--metadata-route", $"{hexB}={LoopbackHost}:{portB}"]);
                await using(confirmed.ConfigureAwait(false))
                {
                    int portA = await ListeningPortAsync(confirmed, TestContext.CancellationToken).ConfigureAwait(false);
                    string identityLine = await confirmed.WaitForAnyLineAsync("identity ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("Confirmed", TokenAfter(identityLine, "coordination="), $"The claim and both baseline phases decided in this host's favour: {identityLine}");

                    string planeStatus = await confirmed.SendAndWaitAsync("metadata-status", "plane status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.IsGreaterThan(0UL, VersionOf(planeStatus), $"The confirming write left the chain at a written version: {planeStatus}");

                    //The second lineage: its own empty store, so its own creation baseline, against a record
                    //that already carries a confirmed one.
                    ReplicateProcess conflicting = ReplicateProcess.Start([
                        "--store", storeD,
                        "--identity-dir", IdentityDirectoryFor(storeD),
                        .. founders,
                        "--metadata-route", $"{hexA}={LoopbackHost}:{portA}",
                        "--metadata-route", $"{hexB}={LoopbackHost}:{portB}"]);
                    await using(conflicting.ConfigureAwait(false))
                    {
                        int exitCode = await RefusedExitCodeAsync(conflicting, TestContext.CancellationToken).ConfigureAwait(false);
                        Assert.AreEqual(1, exitCode, $"The second lineage is refused at the open; stderr: {string.Join(" | ", conflicting.ErrorLines())}");
                        Assert.IsFalse(conflicting.SawLine("identity "), "The refusal precedes the engine, so the refused host printed no identity line.");
                        Assert.Contains("lineage baseline", string.Join(" | ", conflicting.ErrorLines()), StringComparison.Ordinal);
                        Assert.IsTrue(conflicting.SawLine($"axis {hexD}"), "The refused host still printed its own axis, which is what an operator reads to place it on the right lineage.");
                    }

                    string statusA = await confirmed.SendAndWaitAsync("status", "status ", TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.AreEqual("RemoveAware", TokenAfter(statusA, "causality="), $"The confirmed host is unaffected by the refusal: {statusA}");

                    await QuitAsync(confirmed, TestContext.CancellationToken).ConfigureAwait(false);
                }

                await QuitAsync(quorumMate, TestContext.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Binds every host's endpoint map to every other host of the cluster through the <c>metadata-route</c> verb, which is how a deployment tells its members where each other's ephemeral ports landed.</summary>
    /// <param name="replicas">The running replicas.</param>
    /// <param name="axes">Their identity axes as hex, in the same order.</param>
    /// <param name="ports">Their loopback ports, in the same order.</param>
    /// <param name="cancellationToken">Bounds the waits.</param>
    /// <returns>A task that completes when every route is bound.</returns>
    private static async Task BindFullMeshAsync(ReplicateProcess[] replicas, string[] axes, int[] ports, CancellationToken cancellationToken)
    {
        for(int i = 0; i < replicas.Length; i++)
        {
            for(int j = 0; j < replicas.Length; j++)
            {
                if(i != j)
                {
                    _ = await MetadataRouteAsync(replicas[i], axes[j], ports[j], cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Re-reads one host's readiness line until it reports the expected reachable count, bounded by a COUNT
    /// rather than by a wall. A readiness report is driven by the previous answer — a fellow's channel redials
    /// on the call after the fault its last one met — so the transition a row waits for is reached by asking
    /// again and never by waiting longer.
    /// </summary>
    /// <param name="replica">The host whose readiness is read.</param>
    /// <param name="expectedReachable">The <c>reachable=</c> token the row is waiting for.</param>
    /// <param name="cancellationToken">Bounds the waits.</param>
    /// <returns>The readiness line carrying the expected count.</returns>
    private static async Task<string> SettledStatusAsync(ReplicateProcess replica, string expectedReachable, CancellationToken cancellationToken)
    {
        string line = string.Empty;
        for(int read = 0; read < SettleReads; read++)
        {
            line = await replica.SendAndWaitAsync("metadata-status", "plane status ", cancellationToken).ConfigureAwait(false);
            if(string.Equals(TokenAfter(line, "reachable="), expectedReachable, StringComparison.Ordinal))
            {
                return line;
            }
        }

        Assert.Fail($"The readiness report never reached {expectedReachable} within {SettleReads} reads; the last line was: {line}");

        return line;
    }

    /// <summary>
    /// Waits for a host that was expected to REFUSE its own start and answers its exit code, bounded by the
    /// metadata batteries' shared in-flight bound. The bound is a refusal bound and never a cadence: a refused
    /// host exits at once, and the bound only turns a regression that would leave it running as a daemon into a
    /// failed row instead of a hung suite.
    /// </summary>
    /// <param name="replica">The host expected to refuse its start.</param>
    /// <param name="cancellationToken">The row's own token.</param>
    /// <returns>The refused host's exit code.</returns>
    private static async Task<int> RefusedExitCodeAsync(ReplicateProcess replica, CancellationToken cancellationToken)
    {
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(MetadataBatteryBackstops.InFlight);
        int? exitCode = null;
        try
        {
            exitCode = await replica.WaitForExitAsync(bound.Token).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        {
            //The host outlived the bound, which the assertion below names; asserting here would let a row pass
            //on the path where nothing was thrown at all.
        }

        Assert.IsNotNull(exitCode, $"The host was expected to refuse its start and exit; it was still running when the bound elapsed. Its error output was: {string.Join(" | ", replica.ErrorLines())}");

        return exitCode.Value;
    }

    /// <summary>Reads the <c>version=</c> token of a plane line as the register version it names; the unwritten version reads as zero.</summary>
    /// <param name="line">The plane output line.</param>
    /// <returns>The register version.</returns>
    private static ulong VersionOf(string line)
    {
        return ulong.Parse(TokenAfter(line, "version="), NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
