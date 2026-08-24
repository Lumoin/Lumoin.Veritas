using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

[TestClass]
internal sealed class GraphFoldTests
{
    public TestContext TestContext { get; set; } = null!;

    //Encoded node ids start at 10 (well clear of the TermId.None
    //sentinel at 0) so bound-to-X queries do not alias with
    //unbound queries inside the fold's match calls.

    [TestMethod]
    public async Task FoldReturnsAlgebraResultForNodeWithNoOutgoing()
    {
        //Root node 42 has no outgoing edges. The algebra is still invoked once with
        //an empty outgoing list, and its result is returned.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(11, 100, 12)]);

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
        //Root 10 has three outgoing triples.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 200, 12),
            EncodedTriple.FromEncoded(10, 300, 13)
        ]);

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(10),
            algebra: (_, outgoing, _) => outgoing.Count,
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3, result);
    }

    [TestMethod]
    public async Task FoldAggregatesTreeSize()
    {
        //Tree:
        //  10 -p-> 11 -p-> 12
        //  10 -p-> 13
        //Leaves 12 and 13 have no outgoing edges but are still reduced to 1 each
        //by the algebra (outgoing and childResults are both empty lists).
        //Node 11 has one child (12 -> 1). Result = 1 + 1 = 2.
        //Node 10 has two children (11 -> 2, 13 -> 1). Result = 1 + 2 + 1 = 4.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(10, 100, 13)
        ]);

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(10),
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
        //Cycle 10 -> 11 -> 12 -> 10. The visited set breaks the cycle and each node
        //is reduced exactly once.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(12, 100, 10)
        ]);

        int callCount = 0;

        int result = await GraphFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(10),
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
