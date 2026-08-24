using System;
using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The remove-aware persistence spine: a database created with a host replica identity is remove-aware from
/// birth (the Initial entry is its baseline); a persist writes the replication causality artifact paired with
/// the system of record by StateId, under racing commits included; a reopen recovers the ledger — from the
/// artifact, or from the annotated durable journal alone — with the tombstone knowledge intact; and a store
/// without a causality pair stays add-only, never ambiently upgraded.
/// </summary>
[TestClass]
internal sealed class RemoveAwarePersistenceTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
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

    /// <summary>A data triple of example-namespace named nodes.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="object">The object local name.</param>
    /// <returns>The data triple.</returns>
    private static DataTriple Data(string subject, string predicate, string @object)
    {
        return new DataTriple(new NamedNode(Utf8Strings.From(Ex + subject)), new NamedNode(Utf8Strings.From(Ex + predicate)), new NamedNode(Utf8Strings.From(Ex + @object)));
    }

    /// <summary>Encodes an example-namespace triple against a dictionary, registering any new terms.</summary>
    /// <param name="dictionary">The dictionary to encode against.</param>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="object">The object local name.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermDictionary dictionary, string subject, string predicate, string @object)
    {
        return EncodedTriple.FromEncoded(
            dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Ex + subject))).Encoded,
            dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Ex + predicate))).Encoded,
            dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Ex + @object))).Encoded);
    }

    /// <summary>Lands twelve insert commits, hopping to the thread pool first so they genuinely race the caller's persists.</summary>
    /// <param name="database">The database the commits land into.</param>
    /// <param name="cancellationToken">A token that aborts the commits.</param>
    private static async Task InsertRacingCommitsAsync(VeritasEngine database, CancellationToken cancellationToken)
    {
        await Task.Yield();
        for(int i = 0; i < 12; i++)
        {
            await database
                .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}o{i}> }}"), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Whether a ledger snapshot holds an entry for a triple.</summary>
    /// <param name="snapshot">The ledger snapshot.</param>
    /// <param name="triple">The triple to probe.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasEntry(DottedLedgerSnapshot snapshot, EncodedTriple triple)
    {
        foreach(DottedTripleAssignment entry in snapshot.Entries)
        {
            if(entry.Triple == triple)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A database created with a host replica identity is remove-aware from birth: the ledger exists, every seed triple carries one baseline dot on the supplied axis, and the stamp is the actual committed StateId.</summary>
    [TestMethod]
    public async Task CreatedWithIdentityIsRemoveAwareFromBirth()
    {
        VeritasEngineOptions options = new() { ReplicaIdentity = Axis(0x0A) };
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b"), Data("a", "p", "c")], options, TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNotNull(database.CommitLedger, "A host identity at open makes the database remove-aware from birth.");
        DottedLedgerSnapshot snapshot = database.CommitLedger!.Snapshot();
        Assert.HasCount(2, snapshot.Entries);
        Assert.AreEqual(2UL, snapshot.Context.PrefixMaxOn(Axis(0x0A)), "The Initial entry IS the baseline: one dot per seed triple on the supplied axis.");
    }

    /// <summary>A database opened without an identity keeps today's add-only shape exactly: no ledger exists.</summary>
    [TestMethod]
    public async Task OpenedWithoutIdentityStaysAddOnly()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNull(database.CommitLedger, "No identity, no ledger — byte-identical add-only behaviour.");
    }

    /// <summary>A persist writes the replication causality artifact from the same captured instant as the system of record: the artifact loads back digest-verified and its pairing StateId equals the generation's provenance epoch.</summary>
    [TestMethod]
    public async Task PersistWritesTheCausalityArtifactPairedByStateId()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rcl-").FullName;
        try
        {
            VeritasEngineOptions options = new() { ReplicaIdentity = Axis(0x0A) };
            VeritasEngine database = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = database.ConfigureAwait(false);

            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            database.Persist(store);

            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = new DurableSystemOfRecordStore(store, pool).TryLoad(termPool, triplePool);
            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome);
            Assert.IsFalse(load.CausalityRefused, "The artifact verifies at rest.");
            Assert.IsNotNull(load.CausalityImage, "A remove-aware persist stages the causality artifact.");
            DottedLedgerSnapshot snapshot = DottedLedgerSnapshot.ReadFrom(load.CausalityImage!.Value.Span);
            Assert.AreEqual(unchecked((ulong)load.ProvenanceEpoch), snapshot.StateId.Value, "The artifact is paired with the system of record by StateId.");
            load.Triples!.Dispose();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A reopen recovers remove-awareness from the persisted causality artifact, with the tombstone intact: a triple retracted before the persist stays absent from the entry table while its dot stays covered — observed-remove knowledge survives the restart.</summary>
    [TestMethod]
    public async Task ReopenRecoversTheLedgerAndTheTombstone()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rcl-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x0A);
            VeritasEngineOptions options = new() { ReplicaIdentity = identity };
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            CausalDot retractedDot;
            EncodedTriple retracted;
            EncodedTriple kept;

            VeritasEngine database = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b"), Data("a", "p", "c")], options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var scope = database.ConfigureAwait(false))
            {
                retracted = Encode(database.Dictionary, "a", "p", "c");
                kept = Encode(database.Dictionary, "a", "p", "b");
                DottedLedgerSnapshot before = database.CommitLedger!.Snapshot();
                CausalDot found = default;
                foreach(DottedTripleAssignment entry in before.Entries)
                {
                    if(entry.Triple == retracted)
                    {
                        found = entry.Dots[0];
                    }
                }

                retractedDot = found;
                Assert.AreNotEqual(default(CausalDot), retractedDot, "The seed triple carries its baseline dot before the retract.");

                await database
                    .UpdateAsync(Utf8Strings.From($"DELETE DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                database.Persist(store);
            }

            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.CommitLedger, "The persisted causality pair makes the reopened database remove-aware.");
            DottedLedgerSnapshot after = reopened.CommitLedger!.Snapshot();
            Assert.IsFalse(HasEntry(after, retracted), "The retracted triple stays out of the entry table across the restart.");
            Assert.IsTrue(after.Context.Covers(retractedDot), "The context still covers the dropped dot — the tombstone is durable.");
            Assert.IsTrue(HasEntry(after, kept), "The kept triple's entry survives the restart.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A generation persisted by an add-only database gives a later identity-supplied reopen no causality pair: the reopened database stays add-only — becoming remove-aware is an explicit baseline step, never an ambient upgrade.</summary>
    [TestMethod]
    public async Task ReopenWithoutACausalityPairStaysAddOnly()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rcl-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            VeritasEngine addOnly = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var scope = addOnly.ConfigureAwait(false))
            {
                addOnly.Persist(store);
            }

            VeritasEngineOptions withIdentity = new() { ReplicaIdentity = Axis(0x0A) };
            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, withIdentity, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNull(reopened.CommitLedger, "No causality pair, no remove-awareness: the upgrade is an explicit baseline step.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A durable-journal database recovers its ledger from the annotated log alone — no persist ever ran: the baseline rides the Initial entry, every commit's annotation folds in sequence order, and the tombstone survives the restart.</summary>
    [TestMethod]
    public async Task DurableJournalTailRestoresTheLedgerWithoutAPersist()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rcl-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x0A);
            string journalPath = Path.Combine(directory, "dataset.journal");
            VeritasEngineOptions options = new() { ReplicaIdentity = identity, DatasetJournalPath = journalPath };
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            EncodedTriple retracted;

            VeritasEngine database = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var scope = database.ConfigureAwait(false))
            {
                await database
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                retracted = Encode(database.Dictionary, "a", "p", "c");
                await database
                    .UpdateAsync(Utf8Strings.From($"DELETE DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
            }

            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            //A non-null ledger IS the cross-check's verdict: recovery refuses (and hands back no ledger) unless
            //the folded stamp equals the committed state served.
            Assert.IsNotNull(reopened.CommitLedger, "The annotated self-contained log alone is a causality source: the baseline rides the Initial entry.");
            DottedLedgerSnapshot after = reopened.CommitLedger!.Snapshot();
            Assert.IsFalse(HasEntry(after, retracted), "The retracted triple stays out of the recovered entry table.");
            Assert.AreEqual(2UL, after.Context.MaxOn(identity), "Both mints — the baseline's and the insert's — are covered after the tail fold.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The persist pairing holds under racing commits: EVERY generation persisted while commits were landing concurrently — not only the post-quiescent one — carries a causality artifact whose pairing StateId equals that generation's own provenance epoch. Each persisted manifest is read back individually.</summary>
    [TestMethod]
    public async Task PersistPairingHoldsUnderRacingCommits()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rcl-").FullName;
        try
        {
            VeritasEngineOptions options = new() { ReplicaIdentity = Axis(0x0A) };
            VeritasEngine database = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = database.ConfigureAwait(false);

            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Task commits = InsertRacingCommitsAsync(database, TestContext.CancellationToken);

            List<long> generations = [];
            for(int round = 0; round < 3; round++)
            {
                generations.Add(database.Persist(store).Generation);
            }

            await commits.ConfigureAwait(false);
            generations.Add(database.Persist(store).Generation);

            //Every persisted generation — the three that raced the commits and the final quiescent one — is
            //read back through its OWN manifest: the causality artifact must exist and its pairing stamp must
            //equal that manifest's provenance epoch, or the re-capture loop tore under contention.
            foreach(long generation in generations)
            {
                byte[] manifestImage = await File.ReadAllBytesAsync(Path.Combine(directory, ManifestNaming.ManifestName(generation)), TestContext.CancellationToken).ConfigureAwait(false);
                Manifest manifest = Manifest.ReadFrom(manifestImage);
                string? causalityName = null;
                foreach(ManifestEntry entry in manifest.Entries)
                {
                    if(entry.FileName.StartsWith("rcl-", StringComparison.Ordinal))
                    {
                        causalityName = entry.FileName;
                    }
                }

                Assert.IsNotNull(causalityName, $"Generation {generation} names a causality artifact even when persisted under racing commits.");
                byte[] causalityImage = await File.ReadAllBytesAsync(Path.Combine(directory, causalityName!), TestContext.CancellationToken).ConfigureAwait(false);
                DottedLedgerSnapshot snapshot = DottedLedgerSnapshot.ReadFrom(causalityImage);
                Assert.AreEqual(unchecked((ulong)manifest.ProvenanceEpoch), snapshot.StateId.Value, $"Generation {generation}'s pairing stamp matches its own provenance epoch — the re-capture loop held under contention.");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
