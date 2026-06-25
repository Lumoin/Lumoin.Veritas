using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// End-to-end bridge: a mutable database reconciles a shared-dictionary peer's extra triple, writes it back through
/// the dataset journal, and a subsequent query sees it — from "a node reconciles from a peer" to "the convergence
/// persists and is queryable". The peer is intra-cluster (shares the engine's dictionary epoch), which the
/// structural reconcile requires; the cross-organisation case is the content-hash domain.
/// </summary>
[TestClass]
internal sealed class MutableDatabaseReconcileTests
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
        return new NamedNode(Utf8String.FromString(Ex + local));
    }

    /// <summary>A data triple of example-namespace named nodes.</summary>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="object">The object local name.</param>
    /// <returns>The data triple.</returns>
    private static DataTriple Data(string subject, string predicate, string @object)
    {
        return new DataTriple(Iri(subject), Iri(predicate), Iri(@object));
    }

    /// <summary>Encodes an example-namespace triple against a dictionary, registering any new terms.</summary>
    /// <param name="dictionary">The dictionary to encode against.</param>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="object">The object local name.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermDictionary dictionary, string subject, string predicate, string @object)
    {
        return EncodedTriple.FromEncoded(dictionary.GetOrAdd(Iri(subject)).Encoded, dictionary.GetOrAdd(Iri(predicate)).Encoded, dictionary.GetOrAdd(Iri(@object)).Encoded);
    }

    /// <summary>Persists a peer replica's triples as a structural sketch image at the requested budget — the bytes the peer fetch hands back.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="symbolBudget">The number of symbols to persist.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from.</param>
    /// <returns>The peer's persisted sketch image.</returns>
    private static ReadOnlyMemory<byte> PersistPeerImage(EncodedTriple[] peerTriples, int symbolBudget, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, RatelessSketchCodec.Encode, writer);

        return writer.WrittenMemory;
    }

    /// <summary>A mutable database reconciles a shared-dictionary peer's extra triple, writes it back through the journal, and a subsequent query sees it.</summary>
    [TestMethod]
    public async Task ReconcilesAPeersTripleAndMakesItQueryable()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The peer is a shared-dictionary cluster node: its triples encode against the engine's dictionary, so the
        //reconcile's recovered term ids resolve on the engine. It holds (a,p,b) plus a peer-only (a,p,c).
        TermDictionary dictionary = database.Dictionary;
        EncodedTriple[] peerTriples = [Encode(dictionary, "a", "p", "b"), Encode(dictionary, "a", "p", "c")];
        AsyncSketchFetchDelegate peerFetch = (budget, token) => new ValueTask<ReadOnlyMemory<byte>>(PersistPeerImage(peerTriples, budget, pool));

        //The peer shares this database's dictionary epoch (it built its sketch against database.Dictionary).
        PeerReconcileOutcome outcome = await database
            .ReconcileFromPeerAsync(peerFetch, dictionary.Epoch, ReplicationPolicy.Default, 4, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(outcome.Converged, "The reconcile converges.");
        Assert.AreEqual(WriteBackOutcome.Committed, outcome.WriteBack, "The recovered peer triple is written back through the journal.");

        bool hasPeerTriple = await database
            .AskAsync($"ASK {{ <{Ex}a> <{Ex}p> <{Ex}c> }}", cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(hasPeerTriple, "The reconciled peer-only triple is queryable on the engine.");

        bool keepsOriginal = await database
            .AskAsync($"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(keepsOriginal, "The original triple is unchanged.");
    }

    /// <summary>A mutable database serves its own structural sketch (CreateSketchFetch), and an intra-cluster peer sharing its dictionary reconciles against it and converges to the database's set — the database is a peer others reconcile FROM.</summary>
    [TestMethod]
    public async Task ServesItsSketchSoAPeerConvergesToIt()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b"), Data("a", "p", "c")], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //An intra-cluster peer sharing the database's dictionary epoch, holding a subset (only (a,p,b)).
        TermDictionary dictionary = database.Dictionary;
        ColumnarTripleIndex peer = ColumnarTripleIndex.Build([Encode(dictionary, "a", "p", "b")]);

        //The database serves; the peer reconciles against the database's serve delegate and converges to its set.
        AsyncSketchFetchDelegate serve = database.CreateSketchFetch();
        using VeritasMemoryPool<byte> pool = new();
        ReplicaReconcileResult result = await ReplicaReconcileLoop
            .RunUntilConvergedAsync(peer, serve, ReplicationPolicy.Default, pool, TimeProvider.System, 4, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.Converged, "The peer converges against the database's served sketch.");
        HashSet<EncodedTriple> converged = [.. result.Index.EnumerateTriples()];
        HashSet<EncodedTriple> expected = [Encode(dictionary, "a", "p", "b"), Encode(dictionary, "a", "p", "c")];
        Assert.IsTrue(expected.SetEquals(converged), "The peer converges to the database's full triple set via its served sketch.");
    }

    /// <summary>A peer advertising a different dictionary epoch is refused before the reconcile begins (PeerEpochMismatch): its sketch is never fetched and the database is unchanged — the structural reconcile is intra-cluster only.</summary>
    [TestMethod]
    public async Task RefusesAPeerWithAMismatchedDictionaryEpoch()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([Data("a", "p", "b")], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //A foreign peer whose advertised epoch differs from this database's; its fetch must never be consulted.
        bool fetched = false;
        AsyncSketchFetchDelegate foreignPeer = (budget, token) =>
        {
            fetched = true;
            return new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
        };
        ulong foreignEpoch = unchecked(database.Dictionary.Epoch + 1);

        PeerReconcileOutcome outcome = await database
            .ReconcileFromPeerAsync(foreignPeer, foreignEpoch, ReplicationPolicy.Default, 4, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(outcome.PeerEpochMismatch, "A mismatched-epoch peer is refused.");
        Assert.IsFalse(outcome.Converged, "A refused reconcile does not converge.");
        Assert.IsFalse(fetched, "A mismatched-epoch peer's sketch is never fetched — the reconcile is refused before it begins.");

        bool keepsOriginal = await database
            .AskAsync($"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(keepsOriginal, "The database is unchanged by a refused reconcile.");
    }
}
