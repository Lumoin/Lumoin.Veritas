using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Rdf;

[TestClass]
internal sealed class PropertyPathEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    //Encoded node ids start at 10 (well clear of the TermId.None
    //sentinel at 0) so bound-to-X match queries do not alias with
    //unbound queries inside the path evaluator.

    //Local helper: wraps a raw predicate id as an IriId. SHACL/SPARQL
    //predicates are always IRIs so an unchecked wrap is appropriate in tests.
    private static IriId PredIri(uint encoded) => IriId.FromUnchecked(TermId.FromEncoded(encoded));

    [TestMethod]
    public async Task PredicatePathReachesDirectNeighbours()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(10, 200, 13)
        ]);

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(10), new PredicatePath(PredIri(100)), store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            reached.ToList());
    }

    [TestMethod]
    public async Task SequencePathThreadsResultsBetweenSteps()
    {
        //10 -p1-> 11 -p2-> 12; sequence p1/p2 from 10 should reach {12}.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12),
            EncodedTriple.FromEncoded(11, 200, 13)
        ]);

        PropertyPath path = new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(10), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(12), TermId.FromEncoded(13) },
            reached.ToList());
    }

    [TestMethod]
    public async Task AlternativePathUnionsBranches()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 200, 12)
        ]);

        PropertyPath path = new AlternativePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(10), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            reached.ToList());
    }

    [TestMethod]
    public async Task InversePathFlipsDirection()
    {
        //10 -p-> 11; inverse of p from 11 should reach 10.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);

        PropertyPath path = new InversePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(11), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(10) },
            reached.ToList());
    }

    [TestMethod]
    public async Task ZeroOrMoreIncludesStartAndAllReachable()
    {
        //Chain 10 -> 11 -> 12 -> 13.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(12, 100, 13)
        ]);

        PropertyPath path = new ZeroOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(10), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12), TermId.FromEncoded(13) },
            reached.ToList());
    }

    [TestMethod]
    public async Task OneOrMoreExcludesStartUnlessReachableByCycle()
    {
        //Chain 10 -> 11 -> 12; start not reachable from itself.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12)
        ]);

        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(10), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            reached.ToList());
    }

    [TestMethod]
    public async Task ZeroOrOneIncludesStartAndOneStep()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12)
        ]);

        PropertyPath path = new ZeroOrOnePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(10), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) },
            reached.ToList());
    }
}
