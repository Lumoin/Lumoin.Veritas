using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Serve-from-disk: a database opened over a durable persistence store recovers the committed generation — the
/// term dictionary and the system-of-record triples — and answers queries over it, the engine reaching the
/// hardened persistence tier end to end; an empty store is refused rather than served as a silent empty database.
/// </summary>
[TestClass]
internal sealed class VeritasEngineServeFromDiskTests
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

    /// <summary>Persisting a generation then opening a database over the store serves the recovered triple, and a triple absent from the generation is not served.</summary>
    [TestMethod]
    public async Task OpensOverAStoreAndServesTheRecoveredGeneration()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            using(VeritasMemoryPool<byte> pool = new())
            {
                FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
                DurableSystemOfRecordStore records = new(persistence, pool);

                TermDictionary dictionary = new(0xD15C0DE);
                TermId subject = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}s")));
                TermId predicate = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}p")));
                TermId @object = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}o")));
                records.Persist(dictionary, new EncodedTriple[] { new(subject, predicate, @object) });
            }

            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            VeritasEngine database = await VeritasEngine
                .OpenAsync(store, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = database.ConfigureAwait(false);

            bool served = await database
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(served, "The database serves the triple recovered from disk.");

            bool absent = await database
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}absent> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(absent, "A triple absent from the recovered generation is not served.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Opening over a store that holds no committed generation is refused rather than serving an empty database silently.</summary>
    [TestMethod]
    public async Task OpenOverAnEmptyStoreIsRefused()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            bool refused = false;
            try
            {
                VeritasEngine database = await VeritasEngine
                    .OpenAsync(store, cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);

                //Only reached if the empty store unexpectedly opened; dispose it so the failing assert leaks nothing.
                await database.DisposeAsync().ConfigureAwait(false);
            }
            catch(InvalidDataException)
            {
                refused = true;
            }

            Assert.IsTrue(refused, "Opening over an empty store must be refused.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The full durable lifecycle on the facade: a mutable database commits an update, persists its state, and is reopened over the store — the reopened database serves the persisted mutation.</summary>
    [TestMethod]
    public async Task MutableDatabasePersistsAndReopensServingItsState()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            DurableSystemOfRecordCommit commit;
            {
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}s> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                commit = mutable.Persist(store);
            }

            Assert.AreEqual(0L, commit.Generation, "The first persisted generation is zero.");
            Assert.AreEqual(1, commit.TripleCount, "The persisted generation holds the inserted triple.");

            VeritasEngine reopened = await VeritasEngine
                .OpenAsync(store, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = reopened.ConfigureAwait(false);

            bool served = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(served, "The reopened database serves the persisted mutation.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A reopened database serves a multi-pattern join — which routes through the columnar view — off the warm-loaded Elias-Fano sidecar, so the persisted index is consistent with the recovered store and routable.</summary>
    [TestMethod]
    public async Task ReopenedDatabaseServesAMultiPatternJoinFromTheWarmColumnarView()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}b> . <{Ex}b> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                mutable.Persist(store);
            }

            VeritasEngine reopened = await VeritasEngine
                .OpenAsync(store, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = reopened.ConfigureAwait(false);

            //A two-pattern join qualifies for the columnar view; it runs over the warm-loaded sidecar.
            bool twoHop = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ ?x <{Ex}p> ?y . ?y <{Ex}p> ?z }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(twoHop, "The reopened database serves the a->b->c two-hop join from the warm columnar view.");

            bool threeHop = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ ?x <{Ex}p> ?y . ?y <{Ex}p> ?z . ?z <{Ex}p> ?w }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(threeHop, "There is no three-hop path, so the warm view answers the join as empty.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Reopening under deferred residency serves columnar-capable shapes (a single pattern, a multi-pattern join)
    /// from the warm-loaded sidecar without building the trie, and materialises the trie on demand for a shape that
    /// needs it (a property path) — the answers identical to an eager reopen of the same store.
    /// </summary>
    [TestMethod]
    public async Task DeferredResidencyServesFromTheWarmViewAndMaterialisesTheTrieOnDemand()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}b> . <{Ex}b> <{Ex}p> <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                mutable.Persist(store);
            }

            VeritasEngineOptions deferredOptions = new() { HypertrieResidency = HypertrieResidency.Deferred };
            VeritasEngine deferred = await VeritasEngine
                .OpenAsync(store, deferredOptions, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = deferred.ConfigureAwait(false);

            //A two-pattern join qualifies for the columnar view; it serves from the warm sidecar, no trie.
            bool twoHop = await deferred
                .AskAsync(Utf8Strings.From($"ASK {{ ?x <{Ex}p> ?y . ?y <{Ex}p> ?z }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(twoHop, "The deferred database serves the two-hop join from the warm view.");

            //A single pattern also serves from the warm view under deferred residency.
            bool single = await deferred
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(single, "The deferred database serves the single pattern from the warm view.");

            //A property path needs the trie's Match ops; the deferred database materialises it on demand and answers.
            bool transitive = await deferred
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}a> <{Ex}p>+ <{Ex}c> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(transitive, "The deferred database materialises the trie on demand to answer a transitive path a->b->c.");

            bool noPath = await deferred
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}c> <{Ex}p>+ <{Ex}a> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(noPath, "There is no path c->a, so the materialised trie answers the path as absent.");

            //A SPARQL self-join lowers to a fresh variable plus a post-join equality, so it serves from the warm
            //view (as ?x p ?fresh) under deferred residency; with no self-loop in a->b->c it answers empty.
            bool selfLoop = await deferred
                .AskAsync(Utf8Strings.From($"ASK {{ ?x <{Ex}p> ?x }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(selfLoop, "No node links to itself, so the self-join answers empty.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A database holding a named graph persists and reopens with BOTH the default graph and the named graph served — per-graph system-of-record segments recovered into a multi-graph dataset, the named-graph triple kept out of the default graph.</summary>
    [TestMethod]
    public async Task MultiGraphDatabasePersistsAndReopensServingEveryGraph()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}s> <{Ex}p> <{Ex}o> . GRAPH <{Ex}g> {{ <{Ex}a> <{Ex}p> <{Ex}b> }} }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);

                DurableSystemOfRecordCommit commit = mutable.Persist(store);
                Assert.AreEqual(2, commit.TripleCount, "The generation holds the default-graph triple and the named-graph triple.");
            }

            VeritasEngine reopened = await VeritasEngine
                .OpenAsync(store, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = reopened.ConfigureAwait(false);

            bool defaultServed = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(defaultServed, "The reopened database serves the default-graph triple.");

            bool namedServed = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g> {{ <{Ex}a> <{Ex}p> <{Ex}b> }} }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(namedServed, "The reopened database serves the named-graph triple.");

            bool namedTripleInDefault = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(namedTripleInDefault, "The named-graph triple is isolated from the default graph.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A multi-graph database reopens under deferred residency: the default graph defers its trie while the named graphs build eagerly, and every graph is served correctly.</summary>
    [TestMethod]
    public async Task MultiGraphDatabaseReopensUnderDeferredResidency()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-serve-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            {
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{Ex}p> <{Ex}b> . <{Ex}b> <{Ex}p> <{Ex}c> . GRAPH <{Ex}g> {{ <{Ex}x> <{Ex}p> <{Ex}y> }} }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                mutable.Persist(store);
            }

            VeritasEngineOptions deferredOptions = new() { HypertrieResidency = HypertrieResidency.Deferred };
            VeritasEngine reopened = await VeritasEngine
                .OpenAsync(store, deferredOptions, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = reopened.ConfigureAwait(false);

            //The default-graph join serves from the warm view without materialising the deferred trie.
            bool twoHop = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ ?x <{Ex}p> ?y . ?y <{Ex}p> ?z }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(twoHop, "The deferred default graph serves the two-hop join from the warm view.");

            //The named graph (built eagerly) is served.
            bool namedServed = await reopened
                .AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g> {{ <{Ex}x> <{Ex}p> <{Ex}y> }} }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(namedServed, "The eagerly-built named graph is served under deferred default residency.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
