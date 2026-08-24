using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Tests.MemoryPool;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The wire-honesty battery: the sketch channel stamps a domain and epoch on every request and response, so a
/// contract or epoch mismatch is a named refusal before any combine, and a completeness claim is gated on the
/// decoder's false-decode probability bound. A stale-epoch peer, a cross-domain peer in either direction, and a
/// content-hash peer advertising an epoch are each refused by name without touching the local index; an
/// implausibly-bounded peel is refused as insufficient evidence; the framing round-trips and refuses truncated or
/// unknown-domain frames. One test pins the honest boundary the structural stamp draws: the stamp is advisory, so a
/// same-epoch peer is trusted and the cross-organization defense is the content-hash session's per-triple re-hash.
/// </summary>
[TestClass]
internal sealed class AntiEntropyProtocolHonestyTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A line of triples with a shared predicate: subjects <c>[start, start + count)</c>, each linked to the next identifier.</summary>
    /// <param name="start">The first subject identifier.</param>
    /// <param name="count">The number of triples.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] Line(uint start, uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            uint subject = start + i;
            triples[i] = EncodedTriple.FromEncoded(subject, 10, subject + 1);
        }

        return triples;
    }

    /// <summary>A named node for an IRI string.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Encodes a triple of named nodes against a dictionary, assigning identifiers as needed.</summary>
    /// <param name="dictionary">The dictionary to encode against.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <param name="@object">The object.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermDictionary dictionary, NamedNode subject, NamedNode predicate, NamedNode @object)
    {
        return EncodedTriple.FromEncoded(dictionary.GetOrAdd((RdfTerm)subject).Encoded, dictionary.GetOrAdd((RdfTerm)predicate).Encoded, dictionary.GetOrAdd((RdfTerm)@object).Encoded);
    }

    /// <summary>Persists a peer replica's triples as a structural sketch image, stamped structural with the given epoch — the value a structural peer fetch hands back.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="symbolBudget">The number of symbols to persist.</param>
    /// <param name="dictionaryEpoch">The epoch to stamp the image with.</param>
    /// <param name="pool">The pool the persist and owned image are rented from.</param>
    /// <returns>The stamped structural sketch image.</returns>
    private static SketchFetchResult StructuralImage(EncodedTriple[] peerTriples, int symbolBudget, ulong dictionaryEpoch, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, dictionaryEpoch, writer.WrittenMemory, pool);
    }

    /// <summary>Persists a replica's content-hash sketch image, stamped content-hash with the given epoch — used to hand-build an out-of-contract content-hash response.</summary>
    /// <param name="index">The replica whose triples are projected.</param>
    /// <param name="projection">The replica's content-hash projection.</param>
    /// <param name="symbolBudget">The number of symbols to persist.</param>
    /// <param name="dictionaryEpoch">The epoch to stamp the image with.</param>
    /// <param name="pool">The pool the persist and owned image are rented from.</param>
    /// <returns>The stamped content-hash sketch image.</returns>
    private static SketchFetchResult ContentHashImage(ColumnarTripleIndex index, ContentHashReconciliationProjection projection, int symbolBudget, ulong dictionaryEpoch, MemoryPool<byte> pool)
    {
        List<ContentKey128> items = [];
        foreach(EncodedTriple triple in index.EnumerateTriples())
        {
            items.Add(projection.Project(triple));
        }

        ContentKey128[] itemArray = [.. items];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(itemArray, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchChannelStamps.OwnedImage(SketchChannelDomain.ContentHash, dictionaryEpoch, writer.WrittenMemory, pool);
    }

    /// <summary>A structural client whose epoch differs from the server's is refused by name (<see cref="AntiEntropyOutcome.PeerEpochMismatch"/>) before any combine: the server declines with its own epoch stamp, and the session refuses the mismatched stamp, leaving the local index unchanged with no symbols absorbed.</summary>
    [TestMethod]
    public async Task StaleEpochPeerIsRefusedByNameWithoutCombining()
    {
        using VeritasMemoryPool<byte> pool = new();
        const ulong localEpoch = 7;
        const ulong peerEpoch = 9;
        EncodedTriple[] triplesA = Line(0, 30);
        EncodedTriple[] triplesB = Line(20, 30);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ReplicationIndexFeed feedB = new(triplesB, default);
        using IncrementalSketchMaintainer maintainerB = new(feedB, pool, IncrementalSketchMaintainerOptions.Default, peerEpoch);
        SketchChannelServer server = new(maintainerB, pool, requestPipe.Reader, responsePipe.Writer, peerEpoch);
        SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, pool, SketchChannelDomain.Structural, localEpoch);

        Task serve = server.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, localEpoch, client.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await serve.ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerEpochMismatch, result.Outcome, "A stale-epoch peer is refused by name.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "A refused peer leaves the local index unchanged.");
        Assert.AreEqual(0, result.AbsorbedSymbols, "An epoch mismatch is refused before any combine, so no symbols were absorbed.");
    }

    /// <summary>A structural session against a content-hash peer is refused (<see cref="AntiEntropyOutcome.PeerContractMismatch"/>). This closes a real pre-existing hazard: both domains share the image geometry, so before the stamp a structural session would LOAD a content-hash image and mis-invert its garbage into triples; the domain stamp refuses it at the wire instead.</summary>
    [TestMethod]
    public async Task StructuralSessionRefusesAContentHashPeer()
    {
        using VeritasMemoryPool<byte> pool = new();
        const ulong localEpoch = 7;
        VeritasHash hash = VeritasHashing.Default;

        TermDictionary peerDictionary = new();
        EncodedTriple peerTriple = Encode(peerDictionary, Iri("http://example.org/s"), Iri("http://example.org/p"), Iri("http://example.org/o"));
        ColumnarTripleIndex peer = ColumnarTripleIndex.Build([peerTriple]);
        ContentHashReconciliationProjection peerProjection = new(peerDictionary, hash, pool);

        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(Line(0, 5));

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ContentHashSketchChannelServer server = new(peer, peerProjection, pool, requestPipe.Reader, responsePipe.Writer);
        SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, pool, SketchChannelDomain.Structural, localEpoch);

        Task serve = server.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, localEpoch, client.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await serve.ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerContractMismatch, result.Outcome, "A structural session refuses a content-hash peer at the stamp.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "A refused peer leaves the local index unchanged.");
    }

    /// <summary>The mirror direction: a content-hash session against a structural peer is refused (<see cref="AntiEntropyOutcome.PeerContractMismatch"/>) at the stamp, so the triple fetch is never consulted.</summary>
    [TestMethod]
    public async Task ContentHashSessionRefusesAStructuralPeer()
    {
        using VeritasMemoryPool<byte> pool = new();
        const ulong peerEpoch = 3;
        VeritasHash hash = VeritasHashing.Default;

        TermDictionary localDictionary = new();
        EncodedTriple localTriple = Encode(localDictionary, Iri("http://example.org/s"), Iri("http://example.org/p"), Iri("http://example.org/o"));
        ColumnarTripleIndex local = ColumnarTripleIndex.Build([localTriple]);

        ColumnarTripleIndex peer = ColumnarTripleIndex.Build(Line(0, 5));
        AsyncSketchFetchDelegate sketchFetch = async (budget, token) =>
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            ReplicationIndexFeed peerFeed = new(peer.EnumerateTriples(), default);
            using IncrementalSketchMaintainer peerMaintainer = new(peerFeed, pool, IncrementalSketchMaintainerOptions.Default, peerEpoch);
            SketchChannelServer server = new(peerMaintainer, pool, requestPipe.Reader, responsePipe.Writer, peerEpoch);
            Task serve = server.ServeAsync(token);
            SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, pool, SketchChannelDomain.ContentHash, 0);

            SketchFetchResult image = await client.FetchAsync(budget, token).ConfigureAwait(false);
            await serve.ConfigureAwait(false);

            return image;
        };
        bool triplesFetched = false;
        AsyncContentTripleFetchDelegate tripleFetch = (keys, onTriple, token) =>
        {
            triplesFetched = true;

            return ValueTask.CompletedTask;
        };

        AntiEntropySessionResult result = await ContentHashAntiEntropySession.ReconcileAsync(local, localDictionary, hash, ReplicationPolicy.Default, pool, TimeProvider.System, sketchFetch, tripleFetch, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerContractMismatch, result.Outcome, "A content-hash session refuses a structural peer at the stamp.");
        Assert.AreSame(local, result.ConvergedIndex, "A refused peer leaves the local index unchanged.");
        Assert.IsFalse(triplesFetched, "A stamp refusal precedes any triple fetch.");
    }

    /// <summary>A content-hash response stamped with a non-zero epoch is out of contract (<see cref="AntiEntropyOutcome.PeerContractMismatch"/>): the content-hash domain is epoch-independent and reserves the epoch at zero, so a stamped epoch is refused even though the domain matches.</summary>
    [TestMethod]
    public async Task ContentHashPeerAdvertisingAnEpochIsOutOfContract()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasHash hash = VeritasHashing.Default;

        TermDictionary localDictionary = new();
        EncodedTriple localTriple = Encode(localDictionary, Iri("http://example.org/s"), Iri("http://example.org/p"), Iri("http://example.org/o1"));
        ColumnarTripleIndex local = ColumnarTripleIndex.Build([localTriple]);

        TermDictionary peerDictionary = new();
        EncodedTriple peerTriple = Encode(peerDictionary, Iri("http://example.org/s"), Iri("http://example.org/p"), Iri("http://example.org/o2"));
        ColumnarTripleIndex peer = ColumnarTripleIndex.Build([peerTriple]);
        ContentHashReconciliationProjection peerProjection = new(peerDictionary, hash, pool);

        //A hand-built content-hash response stamped with a bogus non-zero epoch.
        AsyncSketchFetchDelegate sketchFetch = (budget, token) => new ValueTask<SketchFetchResult>(ContentHashImage(peer, peerProjection, budget, dictionaryEpoch: 5, pool));
        AsyncContentTripleFetchDelegate tripleFetch = (keys, onTriple, token) => ValueTask.CompletedTask;

        AntiEntropySessionResult result = await ContentHashAntiEntropySession.ReconcileAsync(local, localDictionary, hash, ReplicationPolicy.Default, pool, TimeProvider.System, sketchFetch, tripleFetch, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerContractMismatch, result.Outcome, "A content-hash peer advertising an epoch is out of contract.");
        Assert.AreSame(local, result.ConvergedIndex, "A refused peer leaves the local index unchanged.");
    }

    /// <summary>
    /// The structural trust boundary, pinned: a matching epoch stamp is the ONLY dictionary-identity evidence the
    /// structural session checks, so any peer whose stamp equals the local epoch is trusted and combined — the
    /// session cannot distinguish a peer that truly shares the dictionary from one that merely stamps the same
    /// epoch, because the stamp is advisory, not a cryptographic proof. This is by design: the structural domain is
    /// a same-trust-domain (intra-cluster) tool where a matching epoch is sufficient; the cross-organization
    /// defense, where a peer's identifiers cannot be trusted, is the content-hash session, whose per-triple re-hash
    /// verifies each fetched triple before it is applied. This is an honest-boundary pin, not a gap to close.
    /// </summary>
    [TestMethod]
    public async Task LyingEpochStampIsTheStructuralTrustBoundary()
    {
        using VeritasMemoryPool<byte> pool = new();
        const ulong sharedEpoch = 7;
        EncodedTriple[] triplesA = Line(0, 30);
        EncodedTriple[] triplesB = Line(20, 30);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        //The peer's stamp matches, and that alone admits its items to the combine: no check relates the image's
        //identifiers to the local dictionary beyond the advisory epoch equality.
        AsyncSketchFetchDelegate fetch = (budget, token) => new ValueTask<SketchFetchResult>(StructuralImage(triplesB, budget, sharedEpoch, pool));

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, sharedEpoch, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "A peer stamping the local epoch is trusted and combined — the stamp is advisory.");
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. result.ConvergedIndex.EnumerateTriples()]), "The trusted peer's items are applied to the union.");
    }

    /// <summary>A complete, non-empty peel whose false-decode probability bound exceeds the policy ceiling is refused (<see cref="AntiEntropyOutcome.FalseDecodeBoundExceeded"/>) with nothing applied: a zero ceiling makes any real peel's non-zero bound insufficient evidence to act on.</summary>
    [TestMethod]
    public async Task ImplausiblyBoundedPeelIsRefused()
    {
        using VeritasMemoryPool<byte> pool = new();
        const ulong epoch = 7;
        EncodedTriple[] triplesA = Line(0, 30);
        EncodedTriple[] triplesB = Line(20, 30);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        ReplicationPolicy zeroCeiling = ReplicationPolicy.Default with { MaxFalseDecodeProbability = 0 };
        AsyncSketchFetchDelegate fetch = (budget, token) => new ValueTask<SketchFetchResult>(StructuralImage(triplesB, budget, epoch, pool));

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, epoch, fetch, zeroCeiling, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.FalseDecodeBoundExceeded, result.Outcome, "A peel whose bound exceeds the zero ceiling is refused.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "A refused peel applies nothing.");
    }

    /// <summary>The stamped request frame round-trips through its writer and reader.</summary>
    [TestMethod]
    public void RequestStampRoundTrips()
    {
        SketchChannelRequest request = new(SketchChannelDomain.Structural, 42, 1000);
        ArrayBufferWriter<byte> writer = new();
        SketchChannelFraming.WriteRequest(request, writer);

        SketchChannelRequest read = SketchChannelFraming.ReadRequest(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.AreEqual(request, read, "A request round-trips through its stamped frame.");
    }

    /// <summary>The stamped response frame round-trips its domain, epoch, and image through the writer and owned reader.</summary>
    [TestMethod]
    public void ResponseStampRoundTrips()
    {
        using VeritasMemoryPool<byte> pool = new();
        byte[] image = [1, 2, 3, 4];

        using SketchFetchResult result = SketchChannelStamps.OwnedImage(SketchChannelDomain.ContentHash, 0, image, pool);

        Assert.AreEqual(SketchChannelDomain.ContentHash, result.Domain, "The response carries its domain.");
        Assert.AreEqual(0ul, result.DictionaryEpoch, "The response carries its epoch.");
        Assert.IsTrue(result.Image.Span.SequenceEqual(image), "The response carries its image.");
    }

    /// <summary>A request frame shorter than its domain-and-epoch stamp is refused as malformed input.</summary>
    [TestMethod]
    public void TruncatedRequestStampIsRefused()
    {
        byte[] truncated = new byte[5];

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchChannelFraming.ReadRequest(new ReadOnlySequence<byte>(truncated)); });
    }

    /// <summary>A response frame shorter than its domain-and-epoch stamp is refused as malformed input.</summary>
    [TestMethod]
    public void TruncatedResponseStampIsRefused()
    {
        using VeritasMemoryPool<byte> pool = new();
        byte[] truncated = new byte[4];

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchChannelFraming.ReadOwnedImage(new ReadOnlySequence<byte>(truncated), pool); });
    }

    /// <summary>A frame whose leading domain byte is not a known domain is refused as malformed input, in both the request and response readers.</summary>
    [TestMethod]
    public void UnknownDomainByteIsRefused()
    {
        using VeritasMemoryPool<byte> pool = new();
        byte[] request = new byte[13];
        request[0] = 99;
        byte[] response = new byte[9];
        response[0] = 99;

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchChannelFraming.ReadRequest(new ReadOnlySequence<byte>(request)); });
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchChannelFraming.ReadOwnedImage(new ReadOnlySequence<byte>(response), pool); });
    }
}
