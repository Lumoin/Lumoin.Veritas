using System;
using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The explicit causality baseline step and the replication-status ladder: a resumed pre-causality store becomes
/// remove-aware only through the operator-requested baseline at open — never ambiently — and the baseline lands
/// as a causality-only annotated journal entry a later open recovers through; the step changes nothing on a
/// store that is already remove-aware, requires an identity, and refuses a store whose causality trace it cannot
/// safely extend; and the status surface reports the add-only / awaiting-baseline / remove-aware standing with
/// the ledger's fold generation.
/// </summary>
[TestClass]
internal sealed class ReplicationBaselineTests
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

    /// <summary>The first dot of a triple's ledger entry, or the default dot when the entry is absent.</summary>
    /// <param name="snapshot">The ledger snapshot.</param>
    /// <param name="triple">The triple whose dot to find.</param>
    /// <returns>The dot, or default.</returns>
    private static CausalDot FirstDotOf(DottedLedgerSnapshot snapshot, EncodedTriple triple)
    {
        foreach(DottedTripleAssignment entry in snapshot.Entries)
        {
            if(entry.Triple == triple)
            {
                return entry.Dots[0];
            }
        }

        return default;
    }

    /// <summary>The explicit baseline upgrades a resumed pre-causality durable-journal store, and the upgrade is durable through the annotated journal alone: the baseline dots every present triple on the supplied axis, a post-baseline retraction becomes protected observed-remove knowledge, and a later identity-only reopen recovers remove-awareness through the baseline-annotated entry with the tombstone intact.</summary>
    [TestMethod]
    public async Task ExplicitBaselineUpgradesAResumedJournalStoreDurably()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x0B);
            string journalPath = Path.Combine(directory, "dataset.journal");
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            EncodedTriple retracted;
            EncodedTriple kept;
            CausalDot retractedDot;

            VeritasEngineOptions addOnly = new() { DatasetJournalPath = journalPath };
            VeritasEngine created = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b"), Data("a", "p", "c")], addOnly, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var createdScope = created.ConfigureAwait(false))
            {
                await created
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}d> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                Assert.AreEqual(ReplicationCausalityState.AddOnly, created.ReadReplicationStatus().CausalityState, "No identity, no causality standing.");
            }

            VeritasEngineOptions baseline = new() { DatasetJournalPath = journalPath, ReplicaIdentity = identity, BaselineReplicationCausality = true };
            VeritasEngine baselined = await VeritasEngine
                .OpenMutableAsync(store, baseline, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var baselinedScope = baselined.ConfigureAwait(false))
            {
                Assert.IsNotNull(baselined.CommitLedger, "The explicit baseline step makes the resumed store remove-aware.");
                Assert.AreEqual(ReplicationBaselineOutcome.Baselined, baselined.ReplicationBaseline, "The step ran: the outcome value says so.");
                DottedLedgerSnapshot after = baselined.CommitLedger!.Snapshot();
                Assert.HasCount(3, after.Entries);
                Assert.AreEqual(3UL, after.Context.MaxOn(identity), "One fresh dot per present committed triple, counters one through the triple count.");
                VeritasReplicationStatus status = baselined.ReadReplicationStatus();
                Assert.AreEqual(ReplicationCausalityState.RemoveAware, status.CausalityState);
                Assert.AreEqual(1L, status.LedgerGeneration, "The baseline commit itself is the one publish folded since open.");

                retracted = Encode(baselined.Dictionary, "a", "p", "c");
                kept = Encode(baselined.Dictionary, "a", "p", "b");
                retractedDot = FirstDotOf(after, retracted);
                Assert.AreNotEqual(default(CausalDot), retractedDot, "The baselined triple carries its dot before the retract.");

                await baselined
                    .UpdateAsync(Utf8Strings.From($"DELETE DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
            }

            VeritasEngineOptions identityOnly = new() { DatasetJournalPath = journalPath, ReplicaIdentity = identity };
            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, identityOnly, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.CommitLedger, "The baseline-annotated journal entry is the causality source a later open claims remove-awareness through.");
            DottedLedgerSnapshot recovered = reopened.CommitLedger!.Snapshot();
            Assert.IsFalse(HasEntry(recovered, retracted), "The post-baseline retraction stays out of the recovered entry table.");
            Assert.IsTrue(recovered.Context.Covers(retractedDot), "The context still covers the dropped dot — the tombstone is durable through the journal alone.");
            Assert.IsTrue(HasEntry(recovered, kept), "The kept triple's baseline entry survives the reopen.");
            Assert.AreEqual(ReplicationCausalityState.RemoveAware, reopened.ReadReplicationStatus().CausalityState);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The explicit baseline on a warm journal-less store persists through the causality artifact: the baselined open is remove-aware in memory, a persist writes the paired artifact, and a later identity-only reopen recovers remove-awareness from it.</summary>
    [TestMethod]
    public async Task ExplicitBaselineOnAWarmStorePersistsThroughTheArtifact()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x0C);
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            VeritasEngine created = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var createdScope = created.ConfigureAwait(false))
            {
                created.Persist(store);
            }

            VeritasEngineOptions baseline = new() { ReplicaIdentity = identity, BaselineReplicationCausality = true };
            VeritasEngine baselined = await VeritasEngine
                .OpenMutableAsync(store, baseline, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var baselinedScope = baselined.ConfigureAwait(false))
            {
                Assert.IsNotNull(baselined.CommitLedger, "The explicit baseline step makes the warm store remove-aware.");
                Assert.AreEqual(ReplicationBaselineOutcome.Baselined, baselined.ReplicationBaseline);
                Assert.AreEqual(1UL, baselined.CommitLedger!.Snapshot().Context.MaxOn(identity));
                baselined.Persist(store);
            }

            //The persisted causality artifact now guards the lineage: an identity-less MUTABLE reopen refuses
            //rather than committing unannotated history and persisting artifact-less generations over it.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await VeritasEngine.OpenMutableAsync(store, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            VeritasEngineOptions identityOnly = new() { ReplicaIdentity = identity };
            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, identityOnly, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.CommitLedger, "The persisted causality artifact carries the baseline across the restart.");
            DottedLedgerSnapshot recovered = reopened.CommitLedger!.Snapshot();
            Assert.HasCount(1, recovered.Entries);
            Assert.AreEqual(1UL, recovered.Context.MaxOn(identity));
            Assert.AreEqual(ReplicationCausalityState.RemoveAware, reopened.ReadReplicationStatus().CausalityState);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The explicit baseline step without a replica identity is an argument error on both mutable open entrances: the baseline mints on the identity's axis, so there is nothing coherent to request. An IMMUTABLE open refuses the step even with an identity — a query-only database cannot commit the baseline entry, and dropping the command silently is not an answer.</summary>
    [TestMethod]
    public async Task BaselineStepRequiresAnIdentityAndAMutableOpen()
    {
        VeritasEngineOptions flagOnly = new() { BaselineReplicationCausality = true };

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await VeritasEngine.OpenMutableAsync([Data("a", "p", "b")], flagOnly, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        VeritasEngineOptions flagged = new() { ReplicaIdentity = Axis(0x1B), BaselineReplicationCausality = true };
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await VeritasEngine.OpenAsync([Data("a", "p", "b")], flagged, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await VeritasEngine.OpenMutableAsync(store, flagOnly, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await VeritasEngine.OpenAsync(store, flagged, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The baseline flag on a fresh CREATE with an identity is exactly the creation baseline and nothing more: the Initial entry is the baseline, no separate baseline commit lands (the fold generation stays zero), and the seed triples carry the creation counters.</summary>
    [TestMethod]
    public async Task BaselineFlagOnACreateIsTheCreationBaselineOnly()
    {
        ReplicaAxis identity = Axis(0x1C);
        VeritasEngineOptions options = new() { ReplicaIdentity = identity, BaselineReplicationCausality = true };
        VeritasEngine created = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b"), Data("a", "p", "c")], options, TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = created.ConfigureAwait(false);

        Assert.IsNotNull(created.CommitLedger, "Created with identity: remove-aware from birth, flag or no flag.");
        DottedLedgerSnapshot snapshot = created.CommitLedger!.Snapshot();
        Assert.HasCount(2, snapshot.Entries);
        Assert.AreEqual(2UL, snapshot.Context.MaxOn(identity), "The creation baseline's counters — nothing extra minted for the flag.");
        Assert.AreEqual(ReplicationBaselineOutcome.AlreadyRemoveAware, created.ReplicationBaseline, "The creation baseline covers the request; the explicit step had nothing to do.");
        VeritasReplicationStatus status = created.ReadReplicationStatus();
        Assert.AreEqual(ReplicationCausalityState.RemoveAware, status.CausalityState);
        Assert.AreEqual(0L, status.LedgerGeneration, "No separate baseline commit landed: the Initial entry IS the baseline.");
    }

    /// <summary>The baseline flag on a store that is already remove-aware mints nothing new: the recovered ledger stands, no second baseline commit lands (the fold generation stays at zero — recovery folds do not count), and the identity axis's counter maximum is exactly the creation baseline's.</summary>
    [TestMethod]
    public async Task BaselineFlagOnARemoveAwareStoreMintsNothingNew()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x0D);
            string journalPath = Path.Combine(directory, "dataset.journal");
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            VeritasEngineOptions withIdentity = new() { DatasetJournalPath = journalPath, ReplicaIdentity = identity };
            VeritasEngine created = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b"), Data("a", "p", "c")], withIdentity, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var createdScope = created.ConfigureAwait(false))
            {
                Assert.IsNotNull(created.CommitLedger, "Created with identity: remove-aware from birth.");
            }

            VeritasEngineOptions withFlag = new() { DatasetJournalPath = journalPath, ReplicaIdentity = identity, BaselineReplicationCausality = true };
            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, withFlag, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.CommitLedger, "The recovered ledger stands.");
            DottedLedgerSnapshot snapshot = reopened.CommitLedger!.Snapshot();
            Assert.HasCount(2, snapshot.Entries);
            Assert.AreEqual(2UL, snapshot.Context.MaxOn(identity), "The creation baseline's counters are the axis maximum — nothing re-minted.");
            foreach(DottedTripleAssignment entry in snapshot.Entries)
            {
                Assert.HasCount(1, entry.Dots, "Each entry keeps its single creation-baseline dot.");
            }

            Assert.AreEqual(ReplicationBaselineOutcome.AlreadyRemoveAware, reopened.ReplicationBaseline, "The request is answered by value: the store already is remove-aware.");
            VeritasReplicationStatus status = reopened.ReadReplicationStatus();
            Assert.AreEqual(ReplicationCausalityState.RemoveAware, status.CausalityState);
            Assert.AreEqual(0L, status.LedgerGeneration, "No second baseline commit landed: recovery folds do not count and no publish has run since open.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The explicit baseline step refuses a store whose causality artifact fails verification — BY VALUE: the open serves the store in its awaiting-baseline standing and reports <see cref="ReplicationBaselineOutcome.RefusedCausalityTrace"/>, because a fresh baseline over surviving causal history could re-issue dots recorded history already names. An identity-less MUTABLE reopen of the same store refuses outright — that open would fork the causal lineage, and no safe degraded service exists to return.</summary>
    [TestMethod]
    public async Task BaselineOverACorruptCausalityArtifactRefusesByValue()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x0E);
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            VeritasEngineOptions withIdentity = new() { ReplicaIdentity = identity };
            VeritasEngine created = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], withIdentity, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var createdScope = created.ConfigureAwait(false))
            {
                created.Persist(store);
            }

            string causalityPath = string.Empty;
            foreach(string path in Directory.EnumerateFiles(directory, "rcl-*"))
            {
                causalityPath = path;
            }

            Assert.AreNotEqual(string.Empty, causalityPath, "The remove-aware persist wrote a causality artifact to corrupt.");
            byte[] artifact = await File.ReadAllBytesAsync(causalityPath, TestContext.CancellationToken).ConfigureAwait(false);
            artifact[^1] ^= 0xFF;
            await File.WriteAllBytesAsync(causalityPath, artifact, TestContext.CancellationToken).ConfigureAwait(false);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await VeritasEngine.OpenMutableAsync(store, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            VeritasEngineOptions withFlag = new() { ReplicaIdentity = identity, BaselineReplicationCausality = true };
            VeritasEngine refused = await VeritasEngine
                .OpenMutableAsync(store, withFlag, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var refusedScope = refused.ConfigureAwait(false))
            {
                Assert.AreEqual(ReplicationBaselineOutcome.RefusedCausalityTrace, refused.ReplicationBaseline, "The refusal is a value on the engine, never an exception.");
                Assert.IsNull(refused.CommitLedger, "A refused artifact is not a causality pair: the store is not remove-aware.");
                Assert.AreEqual(ReplicationCausalityState.AwaitingBaseline, refused.ReadReplicationStatus().CausalityState);
            }

            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, withIdentity, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNull(reopened.CommitLedger, "Without the flag the store likewise serves awaiting-baseline.");
            Assert.AreEqual(ReplicationBaselineOutcome.NotRequested, reopened.ReplicationBaseline);
            Assert.AreEqual(ReplicationCausalityState.AwaitingBaseline, reopened.ReadReplicationStatus().CausalityState);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>An add-only interlude on a never-persisted remove-aware journal store — an identity-less mutable reopen that commits — breaks the causal lineage, and the break is caught LOUDLY at the next identity-supplied open: the store is not remove-aware (the recorded knowledge no longer describes the committed set), and the explicit baseline step refuses by value over the broken-lineage trace instead of re-minting dots the surviving history already names.</summary>
    [TestMethod]
    public async Task AnAddOnlyInterludeBreaksTheCausalLineageLoudly()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x1D);
            string journalPath = Path.Combine(directory, "dataset.journal");
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            VeritasEngineOptions withIdentity = new() { DatasetJournalPath = journalPath, ReplicaIdentity = identity };
            VeritasEngine created = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], withIdentity, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var createdScope = created.ConfigureAwait(false))
            {
                Assert.IsNotNull(created.CommitLedger);
            }

            //The interlude: no persisted generation exists, so the identity-less reopen has no artifact to
            //refuse over — it serves add-only and its commit lands unannotated in the annotated journal.
            VeritasEngineOptions identityless = new() { DatasetJournalPath = journalPath };
            VeritasEngine interlude = await VeritasEngine
                .OpenMutableAsync(store, identityless, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var interludeScope = interlude.ConfigureAwait(false))
            {
                await interlude
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
            }

            VeritasEngineOptions withFlag = new() { DatasetJournalPath = journalPath, ReplicaIdentity = identity, BaselineReplicationCausality = true };
            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, withFlag, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNull(reopened.CommitLedger, "The unannotated interlude commit broke the causal lineage: the recovered knowledge would not describe the committed set, so the store is not remove-aware.");
            Assert.AreEqual(ReplicationBaselineOutcome.RefusedCausalityTrace, reopened.ReplicationBaseline, "The broken lineage is a causality trace: re-baselining under the same identity could re-issue dots the surviving history already names.");
            Assert.AreEqual(ReplicationCausalityState.AwaitingBaseline, reopened.ReadReplicationStatus().CausalityState);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The status surface reports the causality ladder: add-only without an identity, awaiting-baseline for an identity-supplied resume with no causality pair, and remove-aware with the ledger's fold generation advancing one per committed publish.</summary>
    [TestMethod]
    public async Task StatusReportsTheCausalityLadder()
    {
        VeritasEngine addOnly = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(var addOnlyScope = addOnly.ConfigureAwait(false))
        {
            VeritasReplicationStatus status = addOnly.ReadReplicationStatus();
            Assert.AreEqual(ReplicationCausalityState.AddOnly, status.CausalityState);
            Assert.AreEqual(0L, status.LedgerGeneration);
        }

        VeritasEngineOptions withIdentity = new() { ReplicaIdentity = Axis(0x0F) };
        VeritasEngine removeAware = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b")], withIdentity, TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(var removeAwareScope = removeAware.ConfigureAwait(false))
        {
            Assert.AreEqual(0L, removeAware.ReadReplicationStatus().LedgerGeneration, "The creation baseline seeds the ledger through the constructor, not a publish.");
            await removeAware
                .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await removeAware
                .UpdateAsync(Utf8Strings.From($"DELETE DATA {{ <{Ex}a> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            VeritasReplicationStatus status = removeAware.ReadReplicationStatus();
            Assert.AreEqual(ReplicationCausalityState.RemoveAware, status.CausalityState);
            Assert.AreEqual(2L, status.LedgerGeneration, "Each committed default-graph publish folds once.");
        }

        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            VeritasEngine seeded = await VeritasEngine
                .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var seededScope = seeded.ConfigureAwait(false))
            {
                seeded.Persist(store);
            }

            VeritasEngine awaiting = await VeritasEngine
                .OpenMutableAsync(store, withIdentity, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var awaitingScope = awaiting.ConfigureAwait(false);

            VeritasReplicationStatus status = awaiting.ReadReplicationStatus();
            Assert.AreEqual(ReplicationCausalityState.AwaitingBaseline, status.CausalityState, "Identity supplied over a pre-causality generation: add-only until the explicit baseline step.");
            Assert.AreEqual(0L, status.LedgerGeneration);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>An explicit baseline over an empty pre-causality store claims no knowledge: zero entries, an empty context, and the first post-baseline insert mints counter one on the identity axis.</summary>
    [TestMethod]
    public async Task ExplicitBaselineOnAnEmptyStoreClaimsNoKnowledge()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-baseline-").FullName;
        try
        {
            ReplicaAxis identity = Axis(0x1A);
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            VeritasEngine created = await VeritasEngine
                .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(var createdScope = created.ConfigureAwait(false))
            {
                created.Persist(store);
            }

            VeritasEngineOptions baseline = new() { ReplicaIdentity = identity, BaselineReplicationCausality = true };
            VeritasEngine baselined = await VeritasEngine
                .OpenMutableAsync(store, baseline, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var baselinedScope = baselined.ConfigureAwait(false);

            Assert.IsNotNull(baselined.CommitLedger, "An empty store baselines to remove-aware with no knowledge claimed.");
            Assert.AreEqual(ReplicationBaselineOutcome.Baselined, baselined.ReplicationBaseline);
            DottedLedgerSnapshot empty = baselined.CommitLedger!.Snapshot();
            Assert.IsEmpty(empty.Entries);
            Assert.AreEqual(0UL, empty.Context.MaxOn(identity), "The baseline claims exactly the present dots: none.");

            await baselined
                .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}b> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            DottedLedgerSnapshot after = baselined.CommitLedger!.Snapshot();
            Assert.HasCount(1, after.Entries);
            Assert.AreEqual(1UL, after.Context.MaxOn(identity), "The first post-baseline mint continues from the empty context: counter one.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
