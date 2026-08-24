using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

[TestClass]
internal sealed class GraphParaTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ParaInvokesAlgebraWithOriginalTriples()
    {
        //0 -p-> 1 -p-> 2. The root's algebra call should see (0,100,1) as its edge,
        //and node 1's call should see (1,100,2) as its edge.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2)
        ]);

        Dictionary<TermId, List<EncodedTriple>> seenByNode = [];

        int result = await GraphPara.ParaAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
            algebra: (nodeId, edges) =>
            {
                List<EncodedTriple> edgeTriples = [];
                foreach(var (triple, _) in edges)
                {
                    edgeTriples.Add(triple);
                }

                seenByNode[nodeId] = edgeTriples;
                return 1;
            },
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, result);
        Assert.Contains(EncodedTriple.FromEncoded(0, 100, 1), seenByNode[TermId.FromEncoded(0)]);
        Assert.Contains(EncodedTriple.FromEncoded(1, 100, 2), seenByNode[TermId.FromEncoded(1)]);
    }

    [TestMethod]
    public async Task ParaSumsUsingChildResults()
    {
        //Fan out from 0 to three leaves. Each leaf reduces to 1. Root sums 1 + 3 = 4.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(0, 200, 2),
            EncodedTriple.FromEncoded(0, 300, 3)
        ]);

        int result = await GraphPara.ParaAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
            algebra: (_, edges) =>
            {
                int sum = 1;
                foreach(var (_, child) in edges)
                {
                    sum += child;
                }

                return sum;
            },
            match: store.AsMatchDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Leaves 1, 2, 3 reduce to 1 each. Root: 1 + 1 + 1 + 1 = 4.
        Assert.AreEqual(4, result);
    }
}
