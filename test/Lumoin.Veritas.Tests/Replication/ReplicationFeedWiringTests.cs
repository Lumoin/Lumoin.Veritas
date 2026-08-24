using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The bridge wiring: a committed default-graph delta on a <see cref="MutableSparqlDataset"/> advances a subscribed
/// <see cref="ReplicationIndexFeed"/> by the same delta, so the reconciliation index tracks the committed default
/// graph. The adapter's forward binds as a method group (no closure), confirming the observer signature aligns.
/// </summary>
[TestClass]
internal sealed class ReplicationFeedWiringTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Adapts a feed's advance to the delta observer seam, dropping the causality annotation a feed does not track.</summary>
    private sealed class FeedAdvanceObserver
    {
        /// <summary>The feed advanced per committed delta.</summary>
        private ReplicationIndexFeed Feed { get; }

        /// <summary>Creates the adapter over a feed.</summary>
        /// <param name="feed">The feed to advance.</param>
        public FeedAdvanceObserver(ReplicationIndexFeed feed)
        {
            Feed = feed;
        }

        /// <summary>Forwards a committed delta to the feed; binds as a method group.</summary>
        /// <param name="additions">The triples the commit added.</param>
        /// <param name="removals">The triples the commit removed.</param>
        /// <param name="stateId">The dataset StateId the commit produced.</param>
        /// <param name="causality">The commit's causality annotation; a feed tracks only the triple set, so it is ignored.</param>
        public void OnDefaultGraphDelta(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId, CommitCausality? causality)
        {
            Feed.Advance(additions, removals, stateId);
        }
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

    /// <summary>A commit fires the default-graph delta observer, advancing the subscribed feed to the committed default graph and recording the committed StateId.</summary>
    [TestMethod]
    public async Task CommitAdvancesASubscribedReplicationFeed()
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
        FeedAdvanceObserver adapter = new(feed);
        dataset.ObserveDefaultGraphDelta(adapter.OnDefaultGraphDelta);

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

        ReplicationGeneration current = feed.Current();
        HashSet<EncodedTriple> triples = [.. current.Index.EnumerateTriples()];
        Assert.IsTrue(triples.SetEquals([added]), "The feed tracks the committed default graph: o1 removed, o2 added.");
        Assert.AreNotEqual(default(NodeIdentifier), current.StateId, "The feed records the committed StateId delivered by the observer.");
    }
}
