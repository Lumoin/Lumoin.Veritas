using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The incremental sketch maintainer's Veritas-side contract: a delta-driven maintained encoder serves the same
/// sketch bytes a whole-set re-projection would, survives a re-seed and an epoch rebuild with byte-identical serves
/// (the history-erasure law), and pins each serve to one coherent generation. The maintained image flows through the
/// shipped <see cref="SketchPersistence.PersistSketch"/> framing, so equality against a fresh-encoder image is the
/// byte-compatibility assertion the drop-in requires. Verisync proves the encoder against its own law suites; these
/// tests assert only the maintainer's wiring and equivalence.
/// </summary>
[TestClass]
internal sealed class IncrementalSketchMaintainerTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The governed pool the maintainer, the fresh reference encoder, and the serve buffers rent from — the tracked allocation path production uses.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>The number of symbols every serve in the suite produces; comfortably above the small symmetric differences under test.</summary>
    private const int SymbolBudget = 128;

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

    /// <summary>Creates a maintainer over a feed seeded with <paramref name="seed"/>, with the given re-seed operation budget and no time-based cadence.</summary>
    /// <param name="seed">The committed triples to seed the feed and encoder from.</param>
    /// <param name="reseedOperationBudget">The operation budget that trips an inline re-seed.</param>
    /// <returns>The maintainer.</returns>
    private static IncrementalSketchMaintainer NewMaintainer(IEnumerable<EncodedTriple> seed, long reseedOperationBudget)
    {
        ReplicationIndexFeed feed = new(seed, default);

        //The suite constructs the maintainer under Strict enforcement so an accidental non-net delta (a double add or
        //an unmatched remove) surfaces as a throw the test catches, rather than silently corrupting the sketch.
        IncrementalSketchMaintainerOptions options = new(TimeProvider.System, reseedOperationBudget, Timeout.InfiniteTimeSpan, 0, ReconciliationInjectivityEnforcement.Strict);

        return new IncrementalSketchMaintainer(feed, Pool, options, dictionaryEpoch: 1);
    }

    /// <summary>Serves the maintained encoder's sketch image and returns its bytes.</summary>
    /// <param name="maintainer">The maintainer to serve from.</param>
    /// <returns>The sketch image bytes.</returns>
    private static byte[] MaintainedImage(IncrementalSketchMaintainer maintainer)
    {
        ArrayBufferWriter<byte> writer = new();
        maintainer.WriteSketchImage(SymbolBudget, Pool, writer);

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Builds the reference sketch image a fresh whole-set encoder produces over <paramref name="netSet"/>, through the same framing the maintainer uses.</summary>
    /// <param name="netSet">The net triple set to project and fold.</param>
    /// <returns>The reference sketch image bytes.</returns>
    private static byte[] FreshImage(IReadOnlyCollection<EncodedTriple> netSet)
    {
        ContentKey128[] items = [.. netSet.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, SymbolBudget, ChecksumAlgorithm.XxHash3, Pool, new RatelessSketchCodec(Pool).Encode, writer);

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>A maintainer fed a commit delta serves the exact bytes a fresh whole-set encoder over the resulting net set produces.</summary>
    [TestMethod]
    public void DeltaApplyMatchesFreshProjection()
    {
        EncodedTriple[] seed = Line(0, 100);
        IncrementalSketchMaintainer maintainer = NewMaintainer(seed, reseedOperationBudget: long.MaxValue);
        try
        {
            EncodedTriple[] additions = Line(100, 50);
            EncodedTriple[] removals = Line(0, 20);
            maintainer.OnDefaultGraphDelta(additions, removals, default, causality: null);

            HashSet<EncodedTriple> net = [.. seed];
            net.ExceptWith(removals);
            net.UnionWith(additions);

            Assert.AreSequenceEqual(FreshImage(net), MaintainedImage(maintainer), "The maintained delta-driven serve must byte-match a fresh whole-set projection of the net set.");
            Assert.AreEqual(1L, maintainer.Generation, "One committed delta advances the generation by one.");
        }
        finally
        {
            maintainer.Dispose();
        }
    }

    /// <summary>A re-seed disposes and rebuilds the encoder from the current committed set, and the serve bytes are unchanged — the history-erasure law across a re-seed.</summary>
    [TestMethod]
    public void ReseedPreservesTheServedStream()
    {
        EncodedTriple[] seed = Line(0, 100);
        IncrementalSketchMaintainer maintainer = NewMaintainer(seed, reseedOperationBudget: long.MaxValue);
        try
        {
            maintainer.OnDefaultGraphDelta(Line(100, 40), Line(0, 30), default, causality: null);
            maintainer.OnDefaultGraphDelta(Line(140, 10), Line(30, 10), default, causality: null);
            byte[] before = MaintainedImage(maintainer);

            maintainer.Reseed();
            byte[] after = MaintainedImage(maintainer);

            Assert.AreSequenceEqual(before, after, "A re-seed must serve byte-identical symbols: the rebuilt encoder equals the churned one over the same net set.");
            Assert.AreEqual(0L, maintainer.OperationsSinceReseed, "A re-seed resets the churn counter.");
        }
        finally
        {
            maintainer.Dispose();
        }
    }

    /// <summary>A mid-run epoch change throws — the feed holds old-epoch term ids, so rebuilding under a new epoch would serve a garbage difference — while a rebuild for the fixed current epoch re-seeds from the current committed set.</summary>
    [TestMethod]
    public void MidRunEpochChangeThrowsAndSameEpochRebuildResetsToTheCurrentSet()
    {
        EncodedTriple[] seed = Line(0, 100);
        IncrementalSketchMaintainer maintainer = NewMaintainer(seed, reseedOperationBudget: long.MaxValue);
        try
        {
            EncodedTriple[] additions = Line(100, 25);
            maintainer.OnDefaultGraphDelta(additions, [], default, causality: null);

            Assert.ThrowsExactly<InvalidOperationException>(() => maintainer.RebuildForEpoch(2), "A mid-run epoch change must throw: the feed still holds old-epoch term ids, so rebuilding under a new epoch would serve a peer a garbage difference. Epoch changes require an engine reopen.");
            Assert.AreEqual(1UL, maintainer.DictionaryEpoch, "A rejected epoch change leaves the fixed epoch unchanged.");

            maintainer.RebuildForEpoch(1);

            HashSet<EncodedTriple> net = [.. seed];
            net.UnionWith(additions);
            Assert.AreSequenceEqual(FreshImage(net), MaintainedImage(maintainer), "A same-epoch rebuild re-seeds from the current committed set and serves byte-identical symbols.");
        }
        finally
        {
            maintainer.Dispose();
        }
    }

    /// <summary>Crossing the operation budget only raises the re-seed hint on the commit path — it does not rebuild — so a serve meanwhile is byte-identical off the current encoder; the host cadence's <c>Reseed</c> then clears the hint and resets the churn counter.</summary>
    [TestMethod]
    public void OperationBudgetSignalsReseedWithoutRebuildingOnTheCommitPath()
    {
        EncodedTriple[] seed = Line(0, 50);
        IncrementalSketchMaintainer maintainer = NewMaintainer(seed, reseedOperationBudget: 10);
        try
        {
            EncodedTriple[] additions = Line(50, 40);
            maintainer.OnDefaultGraphDelta(additions, [], default, causality: null);

            Assert.IsTrue(maintainer.NeedsReseed, "A delta crossing the operation budget must signal NeedsReseed for the host cadence, not rebuild on the commit path.");
            Assert.AreEqual(40L, maintainer.OperationsSinceReseed, "The commit-path observer does not reset the churn counter; only the host cadence's Reseed does.");

            HashSet<EncodedTriple> net = [.. seed];
            net.UnionWith(additions);
            Assert.AreSequenceEqual(FreshImage(net), MaintainedImage(maintainer), "A serve arriving while NeedsReseed is set serves the current, valid encoder byte-identically.");

            maintainer.Reseed();

            Assert.IsFalse(maintainer.NeedsReseed, "The host cadence's Reseed clears the NeedsReseed hint.");
            Assert.AreEqual(0L, maintainer.OperationsSinceReseed, "The host cadence's Reseed resets the churn counter.");
            Assert.AreSequenceEqual(FreshImage(net), MaintainedImage(maintainer), "The cadence re-seed leaves the serve byte-identical to a fresh projection.");
        }
        finally
        {
            maintainer.Dispose();
        }
    }

    /// <summary>Wired to a real dataset as the delta observer, the maintainer serves a generation-pinned prefix reflecting exactly the committed set, tagged with the committed StateId.</summary>
    [TestMethod]
    public async Task ServeIsPinnedToTheCommittedGeneration()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        EncodedTriple original = Triple(dictionary, "s", "p", "o1");
        MutableSparqlDataset dataset = await MutableSparqlDataset.CreateAsync(
            dictionary,
            [original],
            namedGraphs: null,
            journalAppend: journal.AppendDelegate,
            journalRead: journal.ReadDelegate,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        ReplicationIndexFeed feed = new([original], default);
        IncrementalSketchMaintainerOptions options = new(TimeProvider.System, long.MaxValue, Timeout.InfiniteTimeSpan, 0, ReconciliationInjectivityEnforcement.Strict);
        using IncrementalSketchMaintainer maintainer = new(feed, Pool, options, dictionaryEpoch: 1);
        dataset.ObserveDefaultGraphDelta(maintainer.OnDefaultGraphDelta);

        EncodedTriple added = Triple(dictionary, "s", "p", "o2");
        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await session.ApplyDeltaAsync(TermId.None, [added], [original], TestContext.CancellationToken).ConfigureAwait(false);
            await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        ArrayBufferWriter<byte> writer = new();
        SketchServeReceipt receipt = maintainer.WriteSketchImage(SymbolBudget, Pool, writer);

        Assert.AreEqual(1L, receipt.Generation, "The commit advanced the maintainer to generation one, which the serve pins.");
        Assert.AreNotEqual(default(NodeIdentifier), receipt.StateId, "The serve carries the committed StateId the observer delivered.");
        Assert.AreSequenceEqual(FreshImage([added]), writer.WrittenSpan.ToArray(), "The serve must reflect exactly the committed set: o1 removed, o2 added.");
    }

    /// <summary>Mints an IRI term in the test namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/ds/" + local)));
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
}
