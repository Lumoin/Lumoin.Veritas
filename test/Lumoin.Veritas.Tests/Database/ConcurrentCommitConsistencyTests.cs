using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// A concurrent-commit consistency check — the first piece of the Jepsen-in-process harness. Many writers commit
/// overlapping INSERT/DELETE updates on one mutable database at once; afterwards the replication feed and the query
/// store must agree on every triple in the universe. This is the invariant the out-of-order-observer bug violated
/// (the feed evolves by delta fold, the query store by absolute snapshot, so a reordered observer diverged them),
/// so it is the regression for that fix: with the atomic publish it holds for every interleaving, and it would fail
/// were the observer moved back outside the publish lock. The final state is nondeterministic; the invariant is not.
/// </summary>
[TestClass]
internal sealed class ConcurrentCommitConsistencyTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A named node in the example namespace for a local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From(Ex + local));
    }

    /// <summary>Drives one writer: a deterministic sequence of single-triple INSERT/DELETE updates over the shared object set, interleaved with the other writers by the scheduler.</summary>
    /// <param name="database">The shared mutable database.</param>
    /// <param name="writerIndex">The writer's index, which seeds its deterministic op sequence.</param>
    /// <param name="objectCount">The size of the shared object universe.</param>
    /// <param name="opsPerWriter">The number of updates the writer issues.</param>
    /// <returns>The writer's completion.</returns>
    private async Task RunWriterAsync(VeritasEngine database, int writerIndex, int objectCount, int opsPerWriter)
    {
        for(int op = 0; op < opsPerWriter; op++)
        {
            int target = (writerIndex + op) % objectCount;
            bool insert = ((writerIndex + op) & 1) == 0;
            string verb = insert ? "INSERT" : "DELETE";
            await database
                .UpdateAsync(Utf8Strings.From($"{verb} DATA {{ <{Ex}s> <{Ex}p> <{Ex}o{target}> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>After many concurrent overlapping commits, the replication feed and the query store agree on every triple in the universe.</summary>
    [TestMethod]
    public async Task ConcurrentCommitsKeepTheFeedAndQueryStoreConsistent()
    {
        const int Writers = 8;
        const int Objects = 5;
        const int OpsPerWriter = 12;

        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Task[] writers = new Task[Writers];
        for(int writer = 0; writer < Writers; writer++)
        {
            writers[writer] = RunWriterAsync(database, writer, Objects, OpsPerWriter);
        }

        await Task.WhenAll(writers).ConfigureAwait(false);

        //Extract the feed's triples through the public serve surface: an empty replica reconciled against the
        //database's served sketch converges to exactly the feed's current contents.
        using VeritasMemoryPool<byte> pool = new();
        ReplicaReconcileResult feedView = await ReplicaReconcileLoop
            .RunUntilConvergedAsync(ColumnarTripleIndex.Build([]), database.Dictionary.Epoch, database.CreateSketchFetch(), ReplicationPolicy.Default, pool, TimeProvider.System, 4, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(feedView.Converged, "The feed's contents are recoverable through the serve surface.");
        HashSet<EncodedTriple> feedTriples = [.. feedView.Index.EnumerateTriples()];

        //For every triple in the universe the feed and the query store must agree on its presence.
        TermDictionary dictionary = database.Dictionary;
        for(int target = 0; target < Objects; target++)
        {
            EncodedTriple encoded = EncodedTriple.FromEncoded(dictionary.GetOrAdd(Iri("s")).Encoded, dictionary.GetOrAdd(Iri("p")).Encoded, dictionary.GetOrAdd(Iri("o" + target)).Encoded);
            bool inStore = await database
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o{target}> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            bool inFeed = feedTriples.Contains(encoded);

            Assert.AreEqual(inStore, inFeed, $"The feed and query store must agree on <{Ex}o{target}> after concurrent commits.");
        }
    }

    /// <summary>Drives one writer over its OWN disjoint object, asserting read-your-writes after each commit: the writer always observes the effect of the update it just committed, since no other writer touches its object.</summary>
    /// <param name="database">The shared mutable database.</param>
    /// <param name="writerIndex">The writer's index; it owns object <c>o{writerIndex}</c>.</param>
    /// <param name="opsPerWriter">The number of updates the writer issues.</param>
    /// <returns>The writer's completion.</returns>
    private async Task RunReadYourWritesWriterAsync(VeritasEngine database, int writerIndex, int opsPerWriter)
    {
        for(int op = 0; op < opsPerWriter; op++)
        {
            bool insert = (op & 1) == 0;
            string verb = insert ? "INSERT" : "DELETE";
            await database
                .UpdateAsync(Utf8Strings.From($"{verb} DATA {{ <{Ex}s> <{Ex}p> <{Ex}o{writerIndex}> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            bool observed = await database
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o{writerIndex}> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.AreEqual(insert, observed, $"Writer {writerIndex} must read its own write at op {op} ({verb}).");
        }
    }

    /// <summary>Under many writers committing concurrently, each over its own disjoint object, every writer reads its own writes: the update it just committed is immediately visible to it (read-your-writes), uncorrupted by the others' concurrent commits and their journal-head contention.</summary>
    [TestMethod]
    public async Task ConcurrentWritersEachReadTheirOwnWrites()
    {
        const int Writers = 8;
        const int OpsPerWriter = 12;

        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Task[] writers = new Task[Writers];
        for(int writer = 0; writer < Writers; writer++)
        {
            writers[writer] = RunReadYourWritesWriterAsync(database, writer, OpsPerWriter);
        }

        await Task.WhenAll(writers).ConfigureAwait(false);
    }
}
