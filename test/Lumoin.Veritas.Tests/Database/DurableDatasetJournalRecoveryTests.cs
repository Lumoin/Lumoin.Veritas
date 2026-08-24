using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Journal;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The durable dataset-journal recovery lifecycle on the engine facade: a mutable database wired to an
/// append-only dataset journal records every commit durably (flush-before-ack), and a reopen over the store
/// recovers the acked commits — the persisted generation folded forward through the journal to the head state,
/// verified content-addressed against the journal head. Divergence (the store and journal from different
/// histories), an unborn journal over an existing generation, and a create-over-existing-history are loud
/// refusals; a torn tail is named and its intact prefix serves.
/// </summary>
[TestClass]
internal sealed class DurableDatasetJournalRecoveryTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A directory durability barrier that does nothing, so the store side does not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Lays out a fresh store directory and a sibling durable-journal path under one temp root, so the store's retention name-sweep never enumerates the journal.</summary>
    /// <returns>The temp root, the store directory, and the durable-journal file path.</returns>
    private static (string Root, string StoreDirectory, string JournalPath) NewLayout()
    {
        string root = Directory.CreateTempSubdirectory("veritas-dsrecovery-").FullName;
        string storeDirectory = Path.Combine(root, "store");
        Directory.CreateDirectory(storeDirectory);
        string journalPath = Path.Combine(root, "journal", "dataset.journal");

        return (root, storeDirectory, journalPath);
    }

    /// <summary>The engine options wiring a database to a durable dataset journal at the given path.</summary>
    /// <param name="journalPath">The durable-journal file path.</param>
    /// <returns>The options.</returns>
    private static VeritasEngineOptions JournalOptions(string journalPath)
    {
        return new VeritasEngineOptions { DatasetJournalPath = journalPath };
    }

    /// <summary>Asks a boolean over the database.</summary>
    /// <param name="database">The database.</param>
    /// <param name="ask">The ASK query text.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The boolean answer.</returns>
    private static async Task<bool> AskAsync(VeritasEngine database, string ask, CancellationToken cancellationToken)
    {
        return await database.AskAsync(Utf8Strings.From(ask), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserts a triple through a SPARQL Update, minting any new IRIs into the database's dictionary.</summary>
    /// <param name="database">The mutable database.</param>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="obj">The object local name.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The asynchronous update.</returns>
    private static async Task InsertAsync(VeritasEngine database, string subject, string predicate, string obj, CancellationToken cancellationToken)
    {
        await database
            .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}{subject}> <{Ex}{predicate}> <{Ex}{obj}> }}"), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AckedCommitsSurviveACrashBetweenAckAndPersist()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);

                //Pre-persist commits, each minting brand-new IRIs.
                await InsertAsync(mutable, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(mutable, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);
                mutable.Persist(store);

                //Post-persist commits, minting further IRIs — the term-durability proof: their terms live only in
                //the journal until this reopen restores them.
                await InsertAsync(mutable, "e", "p", "f", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(mutable, "g", "p", "h", TestContext.CancellationToken).ConfigureAwait(false);

                //Dispose WITHOUT persisting the post-persist commits: they survive only through the durable journal.
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.IsGreaterThan(0L, reopened.DatasetJournalRecovery!.EntriesReplayed, "The post-persist commits are replayed from the durable journal.");

            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false));
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false));
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}e> <{Ex}p> <{Ex}f> }}", TestContext.CancellationToken).ConfigureAwait(false), "A post-persist commit's triple survives with its minted terms.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}g> <{Ex}p> <{Ex}h> }}", TestContext.CancellationToken).ConfigureAwait(false), "The last post-persist commit survives.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task FullReplayFromEmptyWhenNoGenerationWasEverPersisted()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);

                await InsertAsync(mutable, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(mutable, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);

                //No Persist: the store never received a generation, so the whole self-contained log replays from empty.
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false), "The content replays from empty and its terms resolve.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task NamedGraphCreateAndDropReplay()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);

                //Generation: three named graphs, each with a triple.
                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ GRAPH <{Ex}g1> {{ <{Ex}s> <{Ex}p> <{Ex}o1> }} GRAPH <{Ex}g2> {{ <{Ex}s> <{Ex}p> <{Ex}o2> }} GRAPH <{Ex}g3> {{ <{Ex}s> <{Ex}p> <{Ex}o3> }} }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                mutable.Persist(store);

                //Post-persist directory reshaping: create a fourth graph, drop the second, empty the third (existence
                //retained through CLEAR).
                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ GRAPH <{Ex}g4> {{ <{Ex}s> <{Ex}p> <{Ex}o4> }} }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await mutable.UpdateAsync(Utf8Strings.From($"DROP GRAPH <{Ex}g2>"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await mutable.UpdateAsync(Utf8Strings.From($"CLEAR GRAPH <{Ex}g3>"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            }

            //The reopen only succeeds when the rebuilt directory shape (g1 present, g2 gone, g3 present-but-empty, g4
            //present) reproduces the content-addressed journal head exactly — an empty-but-present graph contributes
            //to the state id, so mistaking it for absent would fail the head check. The reopen succeeding is itself
            //the existence-vs-emptiness proof.
            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ GRAPH <{Ex}g1> {{ <{Ex}s> <{Ex}p> <{Ex}o1> }} }}", TestContext.CancellationToken).ConfigureAwait(false), "The untouched persisted graph survives.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ GRAPH <{Ex}g4> {{ <{Ex}s> <{Ex}p> <{Ex}o4> }} }}", TestContext.CancellationToken).ConfigureAwait(false), "The post-persist created graph is present.");
            Assert.IsFalse(await AskAsync(reopened, $"ASK {{ GRAPH <{Ex}g2> {{ <{Ex}s> <{Ex}p> <{Ex}o2> }} }}", TestContext.CancellationToken).ConfigureAwait(false), "The dropped graph is gone.");
            Assert.IsFalse(await AskAsync(reopened, $"ASK {{ GRAPH <{Ex}g3> {{ <{Ex}s> <{Ex}p> <{Ex}o3> }} }}", TestContext.CancellationToken).ConfigureAwait(false), "The emptied graph holds no triples.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task GenerationAheadOfJournalIsRefused()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            //Persist a non-empty generation into the store through an in-memory-journal engine.
            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
                mutable.Persist(store);
            }

            //Independently create a fresh durable journal at the reopen path holding only an empty-dataset Initial —
            //its head state is nowhere near the persisted generation's state.
            {
                VeritasEngine other = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
            }

            //The persisted generation's state does not appear in the journal — different histories.
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A bulk build persisted with no journal gains a durable journal on reopen: the reopen ATTACHES a v2 log anchored at the persisted generation (folding nothing), commits ack durably onward, and after a kill every acked commit survives a second reopen — the anchored-attach acceptance story.</summary>
    [TestMethod]
    public async Task AttachAtThePersistedAnchorResumesAndAcksDurably()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            //Bulk build with NO journal, then persist: the generation is stamped with its dataset state, the anchor
            //a later attach continues from. There is no path to onboard this generation as a giant Initial record —
            //the attach exists exactly to make it durable-acked.
            {
                VeritasEngine bulk = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = bulk.ConfigureAwait(false);
                await InsertAsync(bulk, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(bulk, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);
                bulk.Persist(store);
            }

            //Reopen with a FRESH journal path: the engine attaches a v2 log anchored at the persisted state and folds
            //nothing, then acks commits durably onward.
            {
                VeritasEngine attached = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = attached.ConfigureAwait(false);

                Assert.IsNotNull(attached.DatasetJournalRecovery);
                Assert.AreEqual(0L, attached.DatasetJournalRecovery!.EntriesReplayed, "The empty attached log folds nothing over the anchored generation.");
                Assert.IsTrue(await AskAsync(attached, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false), "The anchored generation is served.");

                //Durable post-attach commits, minting further IRIs — their terms live only in the journal until the
                //next reopen restores them. Drop the engine WITHOUT persisting.
                await InsertAsync(attached, "e", "p", "f", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(attached, "g", "p", "h", TestContext.CancellationToken).ConfigureAwait(false);
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.IsGreaterThan(0L, reopened.DatasetJournalRecovery!.EntriesReplayed, "The post-attach commits replay over the anchored generation.");

            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false), "A generation triple survives the attach.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false));
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}e> <{Ex}p> <{Ex}f> }}", TestContext.CancellationToken).ConfigureAwait(false), "A post-attach commit's triple survives with its journal-only minted terms.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}g> <{Ex}p> <{Ex}h> }}", TestContext.CancellationToken).ConfigureAwait(false), "The last post-attach commit survives.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>After an attach and post-attach commits, persisting a NEWER generation and reopening resumes through the newer anchor by the last-index-of-child path (the newer state appears as a record's child), not the header-anchor pivot.</summary>
    [TestMethod]
    public async Task AttachThenNewerGenerationReopenResumesFromTheNewerAnchor()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine bulk = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = bulk.ConfigureAwait(false);
                await InsertAsync(bulk, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                bulk.Persist(store);
            }

            {
                VeritasEngine attached = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = attached.ConfigureAwait(false);
                await InsertAsync(attached, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);

                //Persist a NEWER generation whose state is the current post-attach head — a record's child.
                attached.Persist(store);
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.AreEqual(0L, reopened.DatasetJournalRecovery!.EntriesReplayed, "The newer generation's state is the journal head, so the last-index-of-child path folds nothing after it.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false));
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false), "The commit folded into the newer generation survives.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>An attached journal reopened against a DIFFERENT store's generation is refused loudly: the journal continues from one store's state and terms, so reopening it against an unrelated generation serves nothing.</summary>
    [TestMethod]
    public async Task AttachedJournalReopenedAgainstAnotherStoreIsRefused()
    {
        (string root, string storeADirectory, string journalPath) = NewLayout();
        try
        {
            string storeBDirectory = Path.Combine(root, "storeB");
            Directory.CreateDirectory(storeBDirectory);
            FileSystemPersistenceStore storeA = new(storeADirectory, NoOpBarrier);
            FileSystemPersistenceStore storeB = new(storeBDirectory, NoOpBarrier);

            //Store A's generation, attached to a journal that then acks a commit.
            {
                VeritasEngine bulkA = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = bulkA.ConfigureAwait(false);
                await InsertAsync(bulkA, "a1", "p", "o1", TestContext.CancellationToken).ConfigureAwait(false);
                bulkA.Persist(storeA);
            }

            {
                VeritasEngine attached = await VeritasEngine.OpenMutableAsync(storeA, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = attached.ConfigureAwait(false);
                await InsertAsync(attached, "x", "p", "y", TestContext.CancellationToken).ConfigureAwait(false);
            }

            //Store B holds a structurally DIFFERENT generation (a distinct number of terms and triples), so its state
            //and dictionary genuinely differ from A's (state identifiers are content-addressed over encoded triples,
            //so a merely-relabelled store would share A's state). Reopening the A-anchored journal against B refuses.
            {
                VeritasEngine bulkB = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = bulkB.ConfigureAwait(false);
                await InsertAsync(bulkB, "b1", "p1", "o1", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(bulkB, "b2", "p2", "o2", TestContext.CancellationToken).ConfigureAwait(false);
                bulkB.Persist(storeB);
            }

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(storeB, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Two independently built stores with IDENTICAL encoded content share the content-addressed anchor AND every term, so neither the anchor nor the term-identity check can tell their histories apart; the header's dictionary replication epoch is the discriminator, and reopening an A-attached journal against B's generation is refused loudly instead of silently churning the node's replication identity.</summary>
    [TestMethod]
    public async Task AttachedJournalAgainstAnIdenticalContentStoreIsRefusedByTheEpochCrossCheck()
    {
        (string root, string storeADirectory, string journalPath) = NewLayout();
        try
        {
            string storeBDirectory = Path.Combine(root, "storeB");
            Directory.CreateDirectory(storeBDirectory);
            FileSystemPersistenceStore storeA = new(storeADirectory, NoOpBarrier);
            FileSystemPersistenceStore storeB = new(storeBDirectory, NoOpBarrier);

            //Two engines built independently over the SAME content: identical encoded triples give the identical
            //content-addressed state (the same provenance-epoch anchor) and identical dictionaries — except each
            //minted its own random replication epoch.
            {
                VeritasEngine one = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = one.ConfigureAwait(false);
                await InsertAsync(one, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
                one.Persist(storeA);
            }

            {
                VeritasEngine two = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = two.ConfigureAwait(false);
                await InsertAsync(two, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
                two.Persist(storeB);
            }

            //Attach a journal to store A: the header records A's dictionary replication epoch.
            {
                VeritasEngine attached = await VeritasEngine.OpenMutableAsync(storeA, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await attached.DisposeAsync().ConfigureAwait(false);
            }

            //Reopening the A-attached journal against store B: the anchor matches, every term matches — only the
            //epoch tells the histories apart, and it refuses.
            InvalidDataException refusal = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(storeB, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Contains("different histories", refusal.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>The recovery pivot refuses an attached log whose first record does not continue the anchor: the header anchors the log to the generation, but the first record's parent is a different state, so the histories diverge.</summary>
    [TestMethod]
    public async Task AttachedLogWhoseFirstRecordDoesNotContinueTheAnchorDiverges()
    {
        NodeIdentifier anchor = new(0x0000_0000_0000_1111UL);
        NodeIdentifier wrongParent = new(0x0000_0000_0000_2222UL);
        NodeIdentifier child = new(0x0000_0000_0000_3333UL);

        //A single post-attach record whose parent is NOT the anchor.
        DatasetJournalEntry entry = new(
            ParentId: wrongParent,
            ChildId: child,
            EntryKind: EditSessionEntryKind.Committed,
            SessionId: null,
            EditCommitment: null,
            Transitions: [],
            Timestamp: default,
            SequenceNumber: 0);

        DatasetJournalReplayResult result = await DatasetJournalRecovery
            .ReplayAsync(ReadOf(entry), child, anchor, headerAnchor: anchor, static _ => null, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(DatasetJournalReplayOutcome.Diverged, result.Outcome);
    }

    /// <summary>The recovery pivot refuses when the generation's state is neither a record's child nor the log's header anchor: the header anchors the log to a different state than the loaded generation, so the histories diverge.</summary>
    [TestMethod]
    public async Task RecoveryDivergesWhenTheGenerationIsNeitherAChildNorTheHeaderAnchor()
    {
        NodeIdentifier generationState = new(0x0000_0000_0000_AAAAUL);
        NodeIdentifier headerAnchor = new(0x0000_0000_0000_BBBBUL);
        NodeIdentifier recordChild = new(0x0000_0000_0000_CCCCUL);

        //A post-attach record over the header anchor, but the loaded generation names a different state.
        DatasetJournalEntry entry = new(
            ParentId: headerAnchor,
            ChildId: recordChild,
            EntryKind: EditSessionEntryKind.Committed,
            SessionId: null,
            EditCommitment: null,
            Transitions: [],
            Timestamp: default,
            SequenceNumber: 0);

        DatasetJournalReplayResult result = await DatasetJournalRecovery
            .ReplayAsync(ReadOf(entry), recordChild, generationState, headerAnchor, static _ => null, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(DatasetJournalReplayOutcome.Diverged, result.Outcome);
    }

    /// <summary>A generation-less reopen of a v2 log restores the dictionary replication epoch from the header, so the epoch survives a crash restart; a headerless v1 log keeps the documented caveat — a fresh epoch is minted on every reopen.</summary>
    [TestMethod]
    public async Task JournalOnlyV2ReopenRestoresTheReplicationEpochWhileV1MintsFresh()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            ulong createdEpoch;
            {
                VeritasEngine created = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = created.ConfigureAwait(false);
                await InsertAsync(created, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
                createdEpoch = created.Dictionary.Epoch;

                //No persist: the store never receives a generation, so the reopen is journal-only.
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);
            Assert.AreEqual(createdEpoch, reopened.Dictionary.Epoch, "A v2 journal-only reopen restores the replication epoch from the header.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o> }}", TestContext.CancellationToken).ConfigureAwait(false));

            //A headerless v1 log keeps the caveat: with no stored epoch, every journal-only reopen mints a fresh one.
            (string v1Root, string v1StoreDirectory, string v1JournalPath) = NewLayout();
            try
            {
                FileSystemPersistenceStore v1Store = new(v1StoreDirectory, NoOpBarrier);
                await CreateHeaderlessV1LogAsync(v1JournalPath).ConfigureAwait(false);

                VeritasEngine v1First = await VeritasEngine.OpenMutableAsync(v1Store, JournalOptions(v1JournalPath), TestContext.CancellationToken).ConfigureAwait(false);
                ulong firstEpoch = v1First.Dictionary.Epoch;
                await v1First.DisposeAsync().ConfigureAwait(false);

                VeritasEngine v1Second = await VeritasEngine.OpenMutableAsync(v1Store, JournalOptions(v1JournalPath), TestContext.CancellationToken).ConfigureAwait(false);
                ulong secondEpoch = v1Second.Dictionary.Epoch;
                await v1Second.DisposeAsync().ConfigureAwait(false);

                Assert.AreNotEqual(0UL, firstEpoch, "A v1 journal-only reopen mints a non-zero replication epoch.");
                Assert.AreNotEqual(firstEpoch, secondEpoch, "A v1 log carries no epoch, so each reopen mints a fresh one (the retained caveat).");
            }
            finally
            {
                Directory.Delete(v1Root, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A crash between an attach (header written) and the first ack reopens cleanly: the empty attached log folds nothing over the generation (UpToDate, zero entries), and the dictionary carries the persisted generation's replication epoch.</summary>
    [TestMethod]
    public async Task CrashBetweenAttachAndFirstAckReopensUpToDate()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            ulong generationEpoch;
            {
                VeritasEngine bulk = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = bulk.ConfigureAwait(false);
                await InsertAsync(bulk, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                bulk.Persist(store);
                generationEpoch = bulk.Dictionary.Epoch;
            }

            //Attach but ack NOTHING (the header is written, no records) — the crash-after-attach state.
            {
                VeritasEngine attached = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await attached.DisposeAsync().ConfigureAwait(false);
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.AreEqual(0L, reopened.DatasetJournalRecovery!.EntriesReplayed, "The header-only attached log folds nothing.");
            Assert.AreEqual(generationEpoch, reopened.Dictionary.Epoch, "The attach carries the persisted generation's replication epoch.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false), "The generation is served after the crash-after-attach reopen.");

            //The attach state is consistent: a fresh commit acks and survives another reopen. Dispose this engine
            //before reopening so the second open sees the on-disk log, not a live handle.
            await InsertAsync(reopened, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);
            await reopened.DisposeAsync().ConfigureAwait(false);

            VeritasEngine second = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var secondScope = second.ConfigureAwait(false);
            Assert.IsTrue(await AskAsync(second, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false), "A commit made after the crash-after-attach reopen round-trips.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A generation with no dataset state binding (provenance epoch zero) is refused on the attach path too: a fresh journal never stamps an attach header over an unanchorable generation.</summary>
    [TestMethod]
    public async Task EpochZeroGenerationIsRefusedOnTheAttachPath()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            //A store-level persist stamps no dataset state (the provenance-epoch default), unlike the engine's Persist.
            using(VeritasMemoryPool<byte> pool = new())
            {
                _ = new DurableSystemOfRecordStore(store, pool).Persist(new TermDictionary(), ReadOnlyMemory<EncodedTriple>.Empty);
            }

            //The journal path is FRESH — the attach path — and the epoch-0 refusal fires before any header is stamped.
            InvalidDataException refusal = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Contains("state binding", refusal.Message);
            Assert.IsFalse(File.Exists(journalPath), "The refusal fires before the fresh journal file is created.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A loaded generation whose manifest carries no dataset state binding (provenance epoch zero — a direct store-level persist, never the engine's) is refused against a born journal: zero is also the no-generation sentinel, and accepting it would silently full-replay the log and discard the generation's content instead of anchoring or refusing.</summary>
    [TestMethod]
    public async Task GenerationWithoutAStateBindingIsRefusedAgainstABornJournal()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
            }

            //A store-level persist stamps no dataset state (the provenance-epoch default), unlike the engine's
            //Persist, which always binds the captured state identifier.
            using(VeritasMemoryPool<byte> pool = new())
            {
                _ = new DurableSystemOfRecordStore(store, pool).Persist(new TermDictionary(), ReadOnlyMemory<EncodedTriple>.Empty);
            }

            InvalidDataException refusal = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Contains("state binding", refusal.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ForeignJournalIsRefused()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            //The store's generation comes from one dataset's content (terms d1s/p/d1o).
            {
                VeritasEngine one = await VeritasEngine.OpenMutableAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = one.ConfigureAwait(false);
                await InsertAsync(one, "d1s", "d1p", "d1o", TestContext.CancellationToken).ConfigureAwait(false);
                one.Persist(store);
            }

            //The durable journal at the reopen path is a DIFFERENT dataset's history (terms d2s/d2p/d2o) — its term
            //section denotes different terms at the same identifiers than the store's dictionary binds.
            {
                VeritasEngine two = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = two.ConfigureAwait(false);
                await InsertAsync(two, "d2s", "d2p", "d2o", TestContext.CancellationToken).ConfigureAwait(false);
            }

            //A foreign journal over this store's generation is refused — a term-identity clash on construction or an
            //anchor divergence; either loud refusal is fine.
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task TornTailLossIsNamedAndTheIntactPrefixServes()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);

                await InsertAsync(mutable, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                mutable.Persist(store);

                //Two post-persist commits: the torn tail will eat the last one and keep the first.
                await InsertAsync(mutable, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);
                await InsertAsync(mutable, "e", "p", "f", TestContext.CancellationToken).ConfigureAwait(false);
            }

            //Corrupt the tail: dropping the final byte makes the last record fail its framing, so replay recovers
            //through the record before it and truncates the torn tail.
            using(FileStream fs = new(journalPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(fs.Length - 1);
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.IsNotNull(reopened.DatasetJournalRecovery!.TornTailLoss, "The discarded torn tail is named.");
            Assert.AreEqual(UnrecoverableItemReportKind.OperationRange, reopened.DatasetJournalRecovery.TornTailLoss!.Kind);
            Assert.IsGreaterThan(0L, reopened.DatasetJournalRecovery.EntriesReplayed, "The surviving post-persist suffix still replays after the anchor.");

            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false), "The persisted generation serves.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false), "The intact post-persist commit serves.");
            Assert.IsFalse(await AskAsync(reopened, $"ASK {{ <{Ex}e> <{Ex}p> <{Ex}f> }}", TestContext.CancellationToken).ConfigureAwait(false), "The commit the torn tail ate is not served.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ReopenedEngineContinuesCommittingAndPersistsAndReopensAgain()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);
                mutable.Persist(store);
                await InsertAsync(mutable, "c", "p", "d", TestContext.CancellationToken).ConfigureAwait(false);
            }

            {
                VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = reopened.ConfigureAwait(false);

                //Commit against the recovered head, then persist a new generation.
                await InsertAsync(reopened, "e", "p", "f", TestContext.CancellationToken).ConfigureAwait(false);
                reopened.Persist(store);
            }

            VeritasEngine second = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var secondScope = second.ConfigureAwait(false);

            Assert.IsTrue(await AskAsync(second, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false));
            Assert.IsTrue(await AskAsync(second, $"ASK {{ <{Ex}c> <{Ex}p> <{Ex}d> }}", TestContext.CancellationToken).ConfigureAwait(false));
            Assert.IsTrue(await AskAsync(second, $"ASK {{ <{Ex}e> <{Ex}p> <{Ex}f> }}", TestContext.CancellationToken).ConfigureAwait(false), "The commit made on the reopened engine round-trips through a second reopen.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task UpToDateReopenReplaysNothing()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
                mutable.Persist(store);

                //Clean dispose right after persist: the journal head already names the persisted state.
            }

            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsNotNull(reopened.DatasetJournalRecovery);
            Assert.AreEqual(0L, reopened.DatasetJournalRecovery!.EntriesReplayed, "The reopen is up to date and folds nothing.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o> }}", TestContext.CancellationToken).ConfigureAwait(false));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task CreateOverloadRefusesAnExistingDurableHistory()
    {
        (string root, string storeDirectory, string journalPath) = NewLayout();
        try
        {
            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "s", "p", "o", TestContext.CancellationToken).ConfigureAwait(false);
            }

            //The create overload creates a dataset; a journal path whose log already holds entries is refused.
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await VeritasEngine.OpenMutableAsync([], JournalOptions(journalPath), TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ForkInteropPins()
    {
        (string root, _, string journalPath) = NewLayout();
        try
        {
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<byte> bufferPool = new();
            using FileBackedDatasetJournal durable = new(journalPath, new TermDictionary(), termPool, VeritasHashing.Default, ChecksumAlgorithm.XxHash3, TimeProvider.System, bufferPool, NoOpBarrier);

            TermDictionary dictionary = new();
            MutableSparqlDataset dataset = await MutableSparqlDataset
                .CreateAsync(dictionary, [], namedGraphs: null, durable.AppendDelegate, durable.ReadDelegate, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await CommitDefaultDeltaAsync(dataset, [Triple(dictionary, "d", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);

            //Forking with a fresh in-memory journal works and puts the fork edge in the in-memory journal, not the
            //durable log.
            InMemoryDatasetJournal forkJournal = new();
            MutableSparqlDataset fork = await dataset.ForkAsync(forkJournal.AppendDelegate, forkJournal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(dataset.StateId, fork.StateId);

            await foreach(DatasetJournalEntry entry in durable.ReadDelegate(0, TestContext.CancellationToken).ConfigureAwait(false))
            {
                Assert.AreNotEqual(EditSessionEntryKind.Forked, entry.EntryKind, "The durable dataset log carries no fork edge.");
            }

            //Passing the born durable journal's delegates to a fork fails loudly: the fork edge expects the empty head.
            await Assert.ThrowsExactlyAsync<EditSessionConcurrencyException>(
                async () => await dataset.ForkAsync(durable.AppendDelegate, durable.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Builds a read seam over a fixed set of entries, for a direct recovery-pivot test.</summary>
    /// <param name="entries">The entries the seam yields in order.</param>
    /// <returns>The read delegate.</returns>
    private static DatasetJournalDelegates.ReadDatasetJournalEntriesAsync ReadOf(params DatasetJournalEntry[] entries)
    {
        return (fromSequenceNumber, cancellationToken) => Enumerate(entries, fromSequenceNumber, cancellationToken);

        static async IAsyncEnumerable<DatasetJournalEntry> Enumerate(
            DatasetJournalEntry[] entries,
            long fromSequenceNumber,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            foreach(DatasetJournalEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(entry.SequenceNumber >= fromSequenceNumber)
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>Writes a valid, self-contained HEADERLESS v1 dataset-journal log (an Initial build plus one committed delta, records at offset 0) through the v1 constructor, so a journal-only engine reopen exercises the v1 mint-epoch caveat.</summary>
    /// <param name="journalPath">The log file path.</param>
    /// <returns>The asynchronous write.</returns>
    private async Task CreateHeaderlessV1LogAsync(string journalPath)
    {
        using Utf8StringPool termPool = new();
        using VeritasMemoryPool<byte> bufferPool = new();
        TermDictionary dictionary = new();
        using FileBackedDatasetJournal v1 = new(journalPath, dictionary, termPool, VeritasHashing.Default, ChecksumAlgorithm.XxHash3, TimeProvider.System, bufferPool);
        Assert.IsFalse(v1.Header.IsV2, "The v1 constructor writes no header.");

        MutableSparqlDataset dataset = await MutableSparqlDataset
            .CreateAsync(dictionary, [], namedGraphs: null, v1.AppendDelegate, v1.ReadDelegate, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await CommitDefaultDeltaAsync(dataset, [Triple(dictionary, "v1s", "v1p", "v1o")], [], TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Mints an IRI term in the example namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Ex + local)));
    }

    /// <summary>Encodes an (s, p, o) triple of local names.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="s">The subject local name.</param>
    /// <param name="p">The predicate local name.</param>
    /// <param name="o">The object local name.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Triple(TermDictionary dictionary, string s, string p, string o)
    {
        return EncodedTriple.FromEncoded(Mint(dictionary, s).Encoded, Mint(dictionary, p).Encoded, Mint(dictionary, o).Encoded);
    }

    /// <summary>Commits one delta against a dataset's default graph.</summary>
    /// <param name="dataset">The dataset.</param>
    /// <param name="additions">The triples to add.</param>
    /// <param name="removals">The triples to remove.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The asynchronous commit.</returns>
    private static async Task CommitDefaultDeltaAsync(
        MutableSparqlDataset dataset,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals,
        CancellationToken cancellationToken)
    {
        DatasetEditSession session = await dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await session.ApplyDeltaAsync(TermId.None, additions, removals, cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
