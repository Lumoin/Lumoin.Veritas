using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Rdf.Indexing;

namespace Lumoin.Veritas.Tests.Indexing;

/// <summary>
/// The rendezvous-level value-index maintenance pins: a probe serves from the index built against the
/// pinned generation, a commit invalidates and the NEXT probe rebuilds from the post-commit store (the
/// commit-then-probe freshness rule — the drop-and-rebuild lifecycle's live counterpart of the
/// stale-answer mutation row), and every decline path (empty registry, unregistered predicate,
/// non-literal objects) falls through without serving a stale or partial answer.
/// </summary>
[TestClass]
internal sealed class ValueIndexMaintenanceTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The declared point-axis predicate.</summary>
    private static Utf8String At => Utf8Strings.From("http://example.org/at");

    /// <summary>A probe serves the built generation; after Advance the next probe rebuilds from the post-commit store and sees the committed entry.</summary>
    [TestMethod]
    public async Task ProbeRebuildsAcrossAdvance()
    {
        TermDictionary dictionary = new();
        TermId at = dictionary.GetOrAdd(new NamedNode(At));
        TermId s1 = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s1")));
        TermId v1 = dictionary.GetOrAdd(DateTimeLiteral("2020-01-01T00:00:00Z"));
        EncodedTriple first = new(s1, at, v1);

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([first], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEngineRendezvous rendezvous = new(store, QueryEnginePolicy.Default, valueIndexes: TemporalRegistry());

        Assert.HasCount(1, Probe(rendezvous, dictionary, store));

        //A commit adds a second observation; Advance invalidates, and the NEXT probe rebuilds from the
        //post-commit store — the freshness half of the drop-and-rebuild lifecycle.
        TermId s2 = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s2")));
        TermId v2 = dictionary.GetOrAdd(DateTimeLiteral("2020-02-01T00:00:00Z"));
        EncodedTriple second = new(s2, at, v2);
        HypertrieGraphStore advanced = await HypertrieGraphStore.BuildAsync([first, second], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        rendezvous.Advance(advanced, [second], []);

        Assert.HasCount(2, Probe(rendezvous, dictionary, advanced));

        //H8: a caller still holding the PRE-commit pinned store declines — the probe never serves a
        //generation newer than the snapshot the caller is evaluating (the torn-generation kill).
        ValueProbeRequest stale = ValueProbeRequest.Range(null, false, null, false);
        Assert.IsFalse(rendezvous.TryOpenValueProbe(At, in stale, dictionary, store, out _));
    }

    /// <summary>The decline paths: an empty registry declines on one branch, an unregistered predicate declines, and a non-literal object never enters the axis.</summary>
    [TestMethod]
    public async Task DeclinePathsFallThroughToTheScan()
    {
        TermDictionary dictionary = new();
        TermId at = dictionary.GetOrAdd(new NamedNode(At));
        TermId s1 = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s1")));
        TermId iriObject = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/notAValue")));
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([new EncodedTriple(s1, at, iriObject)], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        QueryEngineRendezvous bare = new(store, QueryEnginePolicy.Default);
        ValueProbeRequest request = ValueProbeRequest.Range(null, false, null, false);
        Assert.IsFalse(bare.TryOpenValueProbe(At, in request, dictionary, store, out _));

        QueryEngineRendezvous registered = new(store, QueryEnginePolicy.Default, valueIndexes: TemporalRegistry());
        Assert.IsFalse(registered.TryOpenValueProbe(Utf8Strings.From("http://example.org/other"), in request, dictionary, store, out _));

        //The declared predicate's sole object is an IRI: the source yields literals only, so the built
        //axis is empty rather than corrupt.
        Assert.IsEmpty(Probe(registered, dictionary, store));
    }

    /// <summary>Drains a full-range probe's subjects.</summary>
    /// <param name="rendezvous">The rendezvous under test.</param>
    /// <param name="dictionary">The shared dictionary.</param>
    /// <param name="callerStore">The store the caller is evaluating over — the pinning the probe validates.</param>
    /// <returns>The hit subjects' encoded ids.</returns>
    private static List<long> Probe(QueryEngineRendezvous rendezvous, TermDictionary dictionary, HypertrieGraphStore callerStore)
    {
        ValueProbeRequest request = ValueProbeRequest.Range(null, false, null, false);
        List<long> subjects = [];
        Assert.IsTrue(rendezvous.TryOpenValueProbe(At, in request, dictionary, callerStore, out ValueProbeCursor? cursor));
        using(cursor)
        {
            while(cursor!.TryAdvance(out ValueProbeHit hit))
            {
                subjects.Add(hit.Subject.Encoded);
            }
        }

        return subjects;
    }

    /// <summary>A registry holding one temporal point-axis registration over <see cref="At"/> (empty sample corpus — the C.1 battery certifies the method's semantics).</summary>
    /// <returns>The registry.</returns>
    private static ValueIndexRegistry TemporalRegistry()
    {
        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.Zero),
                ValueAxisDeclaration.PointAxis(At),
                new EmptySource(),
                selfTestCases: []))
            .Build();
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTimeLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>An empty sample corpus for registrations whose semantics are certified elsewhere.</summary>
    private sealed class EmptySource: ValueSegmentSource
    {
        /// <summary>Enumerates nothing.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>No entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return [];
        }
    }
}
