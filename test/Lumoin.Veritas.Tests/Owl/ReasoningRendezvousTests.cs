using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Tests for <see cref="ReasoningRendezvous"/>: the expressiveness-floor
/// detection, the strategy rungs and their announcements on the trace bus,
/// the journal-committed materializations, and the description-logic seam's
/// report-or-delegate behaviour for beyond-ceiling modules.
/// </summary>
[TestClass]
internal sealed class ReasoningRendezvousTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    /// <summary>An RDFS-shaped TBox takes the streaming pass and derives the subclass typing.</summary>
    [TestMethod]
    public async Task RdfsShapedTakesTheStreamingPass()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId car = Mint(dictionary, Example + "Car");
        TermId vehicle = Mint(dictionary, Example + "Vehicle");
        TermId myCar = Mint(dictionary, Example + "myCar");

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(car, type, owlClass),
                Triple(vehicle, type, owlClass),
                Triple(car, subClassOf, vehicle),
                Triple(myCar, type, car),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.Rdfs, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.RdfsSufficient, result.Reason);
        Assert.IsTrue(result.IsConsistent);
        Assert.IsGreaterThan(0, result.DerivedCount, "The streaming pass derives the subclass typing.");
        Assert.IsTrue(ContainsTriple(result.Store, Triple(myCar, type, vehicle)), "myCar types as Vehicle in the post-commit store.");
        Assert.HasCount(1, events);
        Assert.AreEqual(ReasoningStrategy.Rdfs, events[0].Strategy);
    }

    /// <summary>A TBox using an RL-but-not-RDFS construct takes the RL closure.</summary>
    [TestMethod]
    public async Task TransitivePropertyTakesTheRlClosure()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId transitive = Mint(dictionary, "http://www.w3.org/2002/07/owl#TransitiveProperty");
        TermId objectProperty = Mint(dictionary, "http://www.w3.org/2002/07/owl#ObjectProperty");
        TermId partOf = Mint(dictionary, Example + "partOf");
        TermId wheel = Mint(dictionary, Example + "wheel");
        TermId car = Mint(dictionary, Example + "car");
        TermId fleet = Mint(dictionary, Example + "fleet");

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(partOf, type, objectProperty),
                Triple(partOf, type, transitive),
                Triple(wheel, partOf, car),
                Triple(car, partOf, fleet),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.Rl, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.RlSufficient, result.Reason);
        Assert.IsTrue(ContainsTriple(result.Store, Triple(wheel, partOf, fleet)), "Transitivity closes wheel partOf fleet.");
        Assert.IsTrue(result.DetectedProfiles.HasFlag(OwlProfiles.Rl));
    }

    /// <summary>A TBox beyond RL is reported when no description-logic delegate is wired — never silently dropped.</summary>
    [TestMethod]
    public async Task BeyondRlIsReportedWithoutADelegate()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.Rl, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlReported, result.Reason);
        Assert.IsNotNull(result.Module);
        Assert.IsNotEmpty(result.Module.Violations, "The module carries the grammar findings.");
        Assert.IsNull(result.ModuleVerdict);
        Assert.IsFalse(result.DetectedProfiles.HasFlag(OwlProfiles.Rl));
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlReported, events[0].Reason);
    }

    /// <summary>A wired delegate receives the beyond-ceiling module and its verdict folds into the result.</summary>
    [TestMethod]
    public async Task BeyondRlDelegatesWhenWired()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningModule? received = null;
        ReasoningRendezvous rendezvous = new(
            ReasoningPolicy.Default,
            (module, _) =>
            {
                received = module;

                return ValueTask.FromResult(new ModuleVerdict(IsConsistent: true, Subsumptions: []));
            });

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.DescriptionLogicDelegate, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, result.Reason);
        Assert.IsNotNull(received);
        Assert.IsNotNull(result.ModuleVerdict);
        Assert.IsTrue(result.IsConsistent);
    }

    //A TBox with a union on the superclass side — outside the RL grammar:
    //c1 ⊑ (a ∪ b).
    private async Task<(HypertrieGraphStore Store, TermId C1)> BuildBeyondRlStoreAsync(TermDictionary dictionary)
    {
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId unionOf = Mint(dictionary, "http://www.w3.org/2002/07/owl#unionOf");
        TermId first = Mint(dictionary, RdfVocabulary.Rdf.First.ToString());
        TermId rest = Mint(dictionary, RdfVocabulary.Rdf.Rest.ToString());
        TermId nil = Mint(dictionary, RdfVocabulary.Rdf.Nil.ToString());
        TermId c1 = Mint(dictionary, Example + "c1");
        TermId a = Mint(dictionary, Example + "a");
        TermId b = Mint(dictionary, Example + "b");
        TermId union = dictionary.GetOrAdd(new BlankNode(Utf8String.FromString("union")));
        TermId list1 = dictionary.GetOrAdd(new BlankNode(Utf8String.FromString("list1")));
        TermId list2 = dictionary.GetOrAdd(new BlankNode(Utf8String.FromString("list2")));

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(c1, type, owlClass),
                Triple(a, type, owlClass),
                Triple(b, type, owlClass),
                Triple(c1, subClassOf, union),
                Triple(union, unionOf, list1),
                Triple(list1, first, a),
                Triple(list1, rest, list2),
                Triple(list2, first, b),
                Triple(list2, rest, nil),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        return (store, c1);
    }

    private static TermId Mint(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8String.FromString(iri)));
    }

    private static bool ContainsTriple(HypertrieGraphStore store, EncodedTriple triple)
    {
        foreach(EncodedTriple _ in store.Match(triple.Subject, triple.Predicate, triple.Object))
        {
            return true;
        }

        return false;
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
