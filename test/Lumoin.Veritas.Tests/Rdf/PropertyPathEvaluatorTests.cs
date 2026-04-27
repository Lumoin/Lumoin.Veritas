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

    //Local helper: wraps a raw long predicate id as an IriId. SHACL/SPARQL
    //predicates are always IRIs so an unchecked wrap is appropriate in tests.
    private static IriId PredIri(long encoded) => IriId.FromUnchecked(TermId.FromEncoded(encoded));

    [TestMethod]
    public async Task PredicatePathReachesDirectNeighbours()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(0, 100, 2),
            EncodedTriple.FromEncoded(0, 200, 3)
        ]);

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(0), new PredicatePath(PredIri(100)), store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(1), TermId.FromEncoded(2) },
            reached.ToList());
    }

    [TestMethod]
    public async Task SequencePathThreadsResultsBetweenSteps()
    {
        //0 -p1-> 1 -p2-> 2; sequence p1/p2 from 0 should reach {2}.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 200, 2),
            EncodedTriple.FromEncoded(1, 200, 3)
        ]);

        PropertyPath path = new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(0), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(2), TermId.FromEncoded(3) },
            reached.ToList());
    }

    [TestMethod]
    public async Task AlternativePathUnionsBranches()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(0, 200, 2)
        ]);

        PropertyPath path = new AlternativePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(0), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(1), TermId.FromEncoded(2) },
            reached.ToList());
    }

    [TestMethod]
    public async Task InversePathFlipsDirection()
    {
        //0 -p-> 1; inverse of p from 1 should reach 0.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(0, 100, 1)]);

        PropertyPath path = new InversePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(1), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(0) },
            reached.ToList());
    }

    [TestMethod]
    public async Task ZeroOrMoreIncludesStartAndAllReachable()
    {
        //Chain 0 -> 1 -> 2 -> 3.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(2, 100, 3)
        ]);

        PropertyPath path = new ZeroOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(0), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(0), TermId.FromEncoded(1), TermId.FromEncoded(2), TermId.FromEncoded(3) },
            reached.ToList());
    }

    [TestMethod]
    public async Task OneOrMoreExcludesStartUnlessReachableByCycle()
    {
        //Chain 0 -> 1 -> 2; start not reachable from itself.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2)
        ]);

        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(0), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(1), TermId.FromEncoded(2) },
            reached.ToList());
    }

    [TestMethod]
    public async Task ZeroOrOneIncludesStartAndOneStep()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2)
        ]);

        PropertyPath path = new ZeroOrOnePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            TermId.FromEncoded(0), path, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        CollectionAssert.AreEquivalent(
            new TermId[] { TermId.FromEncoded(0), TermId.FromEncoded(1) },
            reached.ToList());
    }
}
