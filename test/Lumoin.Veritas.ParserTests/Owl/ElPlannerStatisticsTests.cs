using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for the Strategy E planner feed: <see cref="Lumoin.Veritas.Owl.El.ElPlannerStatistics"/>
/// computes subclass closure × per-class extent counts into sound upper
/// bounds, <see cref="ReasoningRendezvous.PlannerStatistics"/> caches them
/// per store generation, and the bounds reach a planner through
/// <see cref="PlannerContext.Cardinalities"/> on both join drivers.
/// </summary>
[TestClass]
internal sealed class ElPlannerStatisticsTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    /// <summary>A superclass's bound sums its subsumees' asserted extents; a classified class with no instances carries an explicit zero.</summary>
    [TestMethod]
    public async Task ClosureTimesCountsBoundsTheSuperclass()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        HypertrieGraphStore store = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        AprioriCardinalities statistics = rendezvous.PlannerStatistics(store, dictionary, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(terms.Type, statistics.TypePredicate);
        Assert.IsTrue(statistics.TryGetUpperBound(terms.Vehicle, out long vehicleBound));
        Assert.AreEqual(3, vehicleBound, "Vehicle bounds at its own extent plus Car's: 1 + 2.");
        Assert.IsTrue(statistics.TryGetUpperBound(terms.Car, out long carBound));
        Assert.AreEqual(2, carBound, "Car has no subsumees beyond itself.");
        Assert.IsTrue(statistics.TryGetUpperBound(terms.Bicycle, out long bicycleBound));
        Assert.AreEqual(0, bicycleBound, "A classified class with no instances anywhere below it is known to match nothing.");
    }

    /// <summary>A class asserted in the data the TBox never mentions carries its asserted extent — exact, since no subclass structure feeds it.</summary>
    [TestMethod]
    public async Task DataOnlyClassCarriesItsAssertedCount()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        HypertrieGraphStore store = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        AprioriCardinalities statistics = rendezvous.PlannerStatistics(store, dictionary, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(statistics.TryGetUpperBound(terms.Boat, out long boatBound));
        Assert.AreEqual(1, boatBound);
    }

    /// <summary>A class IRI the dictionary has never seen carries no entry — no information, not a zero.</summary>
    [TestMethod]
    public async Task UnknownClassHasNoEntry()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        HypertrieGraphStore store = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        AprioriCardinalities statistics = rendezvous.PlannerStatistics(store, dictionary, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(statistics.TryGetUpperBound(TermId.FromEncoded(uint.MaxValue), out _));
    }

    /// <summary>The same store generation reuses the statistics instance; the classification builds once underneath.</summary>
    [TestMethod]
    public async Task RendezvousCachesStatisticsPerGeneration()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        HypertrieGraphStore store = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        AprioriCardinalities first = rendezvous.PlannerStatistics(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);
        AprioriCardinalities second = rendezvous.PlannerStatistics(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        Assert.AreSame(first, second, "The same store generation reuses the statistics.");
        Assert.HasCount(1, events, "Only the first request classifies; the cached statistics skip the classification entirely.");
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationBuilt, events[0].Reason);
    }

    /// <summary>The statistics reach the planner on every consultation of the hypertrie driver; without them the context carries <c>null</c>.</summary>
    [TestMethod]
    public async Task StatisticsReachThePlannerOnTheHypertrieDriver()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        HypertrieGraphStore store = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        AprioriCardinalities statistics = rendezvous.PlannerStatistics(store, dictionary, cancellationToken: TestContext.CancellationToken);

        (BasicGraphPattern bgp, _) = VehicleQuery(terms);
        List<AprioriCardinalities?> seen = [];
        Planner recording = RecordingPlanner(bgp, seen);

        await foreach(Solution _ in store.QueryAsync(bgp, TimeProvider.System, recording, statistics, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
        }

        Assert.IsNotEmpty(seen);
        Assert.IsTrue(seen.TrueForAll(c => ReferenceEquals(c, statistics)), "Every consultation sees the caller's statistics instance.");

        seen.Clear();
        await foreach(Solution _ in store.QueryAsync(bgp, TimeProvider.System, recording, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
        }

        Assert.IsNotEmpty(seen);
        Assert.IsTrue(seen.TrueForAll(c => c is null), "Without statistics the context carries null — no information.");
    }

    /// <summary>The statistics reach every HyperCube cell's planner on the columnar driver.</summary>
    [TestMethod]
    public async Task StatisticsReachThePlannerOnTheColumnarDriver()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        HypertrieGraphStore store = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        AprioriCardinalities statistics = rendezvous.PlannerStatistics(store, dictionary, cancellationToken: TestContext.CancellationToken);

        ColumnarTripleIndex index = ColumnarTripleIndex.Build(store.Match(TermId.None, TermId.None, TermId.None));
        (BasicGraphPattern bgp, _) = VehicleQuery(terms);
        List<AprioriCardinalities?> seen = [];
        Planner recording = RecordingPlanner(bgp, seen);

        await foreach(Solution _ in ColumnarHyperCube.QueryAsync(
            index, bgp, degreeOfParallelism: 2, TimeProvider.System, recording, statistics,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
        }

        Assert.IsNotEmpty(seen);

        lock(seen)
        {
            Assert.IsTrue(seen.TrueForAll(c => ReferenceEquals(c, statistics)), "Every cell's consultation sees the caller's statistics instance.");
        }
    }

    /// <summary>An assertion-only advance rebuilds the statistics with the new extents from the carried classification — no re-classification.</summary>
    [TestMethod]
    public async Task StatisticsRebuildAcrossAssertionOnlyAdvance()
    {
        TermDictionary dictionary = new();
        Terms terms = new(dictionary);
        TermId car3 = Mint(dictionary, Example + "car3");
        EncodedTriple addition = Triple(car3, terms.Type, terms.Car);

        HypertrieGraphStore first = await BuildVehicleStoreAsync(dictionary, terms).ConfigureAwait(false);
        HypertrieGraphStore second = await BuildVehicleStoreAsync(dictionary, terms, addition).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        AprioriCardinalities before = rendezvous.PlannerStatistics(
            first, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        rendezvous.Advance(second, [addition], [], dictionary);

        AprioriCardinalities after = rendezvous.PlannerStatistics(
            second, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        Assert.AreNotSame(before, after, "Assertions change the extents the bounds sum; the statistics never carry.");
        Assert.IsTrue(after.TryGetUpperBound(terms.Vehicle, out long vehicleBound));
        Assert.AreEqual(4, vehicleBound, "The new Car instance raises Vehicle's bound: 1 + 3.");
        Assert.HasCount(2, events);
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationBuilt, events[0].Reason);
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationReused, events[1].Reason, "The rebuild reuses the carried classification.");
    }

    /// <summary>
    /// Builds the shared fixture: Car ⊑ Vehicle, Bicycle ⊑ Vehicle; two
    /// Cars, one Vehicle, one Boat (a class the TBox never mentions), no
    /// Bicycles — plus whatever assertion-delta triples a test appends.
    /// </summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="terms">The shared identifiers.</param>
    /// <param name="extras">Assertion-delta triples appended to the fixture.</param>
    /// <returns>The built store.</returns>
    private async Task<HypertrieGraphStore> BuildVehicleStoreAsync(TermDictionary dictionary, Terms terms, params EncodedTriple[] extras)
    {
        TermId car1 = Mint(dictionary, Example + "car1");
        TermId car2 = Mint(dictionary, Example + "car2");
        TermId van = Mint(dictionary, Example + "van");
        TermId dinghy = Mint(dictionary, Example + "dinghy");

        return await HypertrieGraphStore.BuildAsync(
            [
                Triple(terms.Car, terms.Type, terms.OwlClass),
                Triple(terms.Vehicle, terms.Type, terms.OwlClass),
                Triple(terms.Bicycle, terms.Type, terms.OwlClass),
                Triple(terms.Car, terms.SubClassOf, terms.Vehicle),
                Triple(terms.Bicycle, terms.SubClassOf, terms.Vehicle),
                Triple(car1, terms.Type, terms.Car),
                Triple(car2, terms.Type, terms.Car),
                Triple(van, terms.Type, terms.Vehicle),
                Triple(dinghy, terms.Type, terms.Boat),
                .. extras,
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A one-pattern query <c>?x rdf:type Vehicle</c>, enough to drive
    /// planner consultations through either driver.
    /// </summary>
    /// <param name="terms">The shared identifiers.</param>
    /// <returns>The pattern and its variable.</returns>
    private static (BasicGraphPattern Query, Variable X) VehicleQuery(Terms terms)
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(terms.Type),
            PatternPosition.Bound(terms.Vehicle));

        return (new BasicGraphPattern([pattern], registry), x);
    }

    /// <summary>
    /// Wraps the default first-occurrence decisions, recording the
    /// cardinalities each consultation carried. The sink is locked because
    /// the columnar driver consults from concurrent cells.
    /// </summary>
    /// <param name="bgp">The query the inner planner walks.</param>
    /// <param name="seen">The sink receiving each consultation's cardinalities.</param>
    /// <returns>The recording planner.</returns>
    private static Planner RecordingPlanner(BasicGraphPattern bgp, List<AprioriCardinalities?> seen)
    {
        Planner inner = Planners.FirstOccurrence(bgp);

        return (context, cancellationToken) =>
        {
            lock(seen)
            {
                seen.Add(context.Cardinalities);
            }

            return inner(context, cancellationToken);
        };
    }

    //The IRIs every test shares, minted once per dictionary.
    private sealed class Terms
    {
        /// <summary>The <c>rdf:type</c> identifier.</summary>
        public TermId Type { get; }

        /// <summary>The <c>rdfs:subClassOf</c> identifier.</summary>
        public TermId SubClassOf { get; }

        /// <summary>The <c>owl:Class</c> identifier.</summary>
        public TermId OwlClass { get; }

        /// <summary>The Vehicle class identifier.</summary>
        public TermId Vehicle { get; }

        /// <summary>The Car class identifier.</summary>
        public TermId Car { get; }

        /// <summary>The Bicycle class identifier.</summary>
        public TermId Bicycle { get; }

        /// <summary>The Boat class identifier — asserted in data, never in the TBox.</summary>
        public TermId Boat { get; }

        /// <summary>Mints the shared identifiers into <paramref name="dictionary"/>.</summary>
        /// <param name="dictionary">The dictionary the store's triples encode with.</param>
        public Terms(TermDictionary dictionary)
        {
            Type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
            SubClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
            OwlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
            Vehicle = Mint(dictionary, Example + "Vehicle");
            Car = Mint(dictionary, Example + "Car");
            Bicycle = Mint(dictionary, Example + "Bicycle");
            Boat = Mint(dictionary, Example + "Boat");
        }
    }

    private static TermId Mint(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    private static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }

    private static TraceHandler<ReasoningTraceEvent> Collect(List<ReasoningTraceEvent> sink)
    {
        return (in ReasoningTraceEvent evt) => sink.Add(evt);
    }
}
