using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Rdf;

[TestClass]
internal sealed class GraphFoldTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FoldReturnsAlgebraResultForNodeWithNoOutgoing()
    {
        //Root node 42 has no outgoing edges. The algebra is still invoked once with
        //an empty outgoing list, and its result is returned.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(1, 100, 2)]);

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(42),
            algebra: (_, outgoing, _) => outgoing.Count + 99,
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(99, result);
    }

    [TestMethod]
    public async Task FoldCountsOutgoingTriplesAtRoot()
    {
        //Root 0 has three outgoing triples.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(0, 200, 2),
            EncodedTriple.FromEncoded(0, 300, 3)
        ]);

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
            algebra: (_, outgoing, _) => outgoing.Count,
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3, result);
    }

    [TestMethod]
    public async Task FoldAggregatesTreeSize()
    {
        //Tree:
        //  0 -p-> 1 -p-> 2
        //  0 -p-> 3
        //Leaves 2 and 3 have no outgoing edges but are still reduced to 1 each
        //by the algebra (outgoing and childResults are both empty lists).
        //Node 1 has one child (2 -> 1). Result = 1 + 1 = 2.
        //Node 0 has two children (1 -> 2, 3 -> 1). Result = 1 + 2 + 1 = 4.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(0, 100, 3)
        ]);

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
            algebra: (_, _, childResults) =>
            {
                int sum = 1;
                foreach(int child in childResults)
                {
                    sum += child;
                }

                return sum;
            },
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task FoldTerminatesOnCycles()
    {
        //Cycle 0 -> 1 -> 2 -> 0. The visited set breaks the cycle and each node is
        //reduced exactly once.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(2, 100, 0)
        ]);

        int callCount = 0;

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
            algebra: (_, _, _) =>
            {
                callCount++;
                return 1;
            },
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3, callCount);
        Assert.AreEqual(1, result);
    }
}
