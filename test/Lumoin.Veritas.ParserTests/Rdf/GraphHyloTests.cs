using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Rdf;

[TestClass]
internal sealed class GraphHyloTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task HyloReturnsEmptyOutcomeForEmptyExpansion()
    {
        HyloOutcome<int> outcome = await GraphHylo.HyloAsync<int, int>(
            seed: 0,
            coalgebra: _ => new GraphExpansion<int>(null, []),
            algebra: (_, _, _) => 1,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(outcome.HasResult);
    }

    [TestMethod]
    public async Task HyloFoldsLeafSeedToAlgebraResult()
    {
        //Coalgebra emits a single triple and no children. The algebra sees
        //outgoing=[] and childResults=[] because the triple IS this node's triple,
        //not a child edge.
        HyloOutcome<int> outcome = await GraphHylo.HyloAsync<int, int>(
            seed: 5,
            coalgebra: seed => new GraphExpansion<int>(EncodedTriple.FromEncoded((uint)seed, 100, (uint)(seed + 1)), []),
            algebra: (_, outgoing, childResults) => outgoing.Count + childResults.Count,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(outcome.HasResult);
        Assert.AreEqual(0, outcome.Result);
    }

    [TestMethod]
    public async Task HyloAlgebraSeesChildrenFolded()
    {
        //Seed 0 produces triple (0, p, 99) and two child seeds 1 and 2.
        //Each child produces a triple (s, p, s+10) and no further children.
        //Leaf algebra call sees outgoing=[] childResults=[] → returns 10.
        //Root algebra call sees outgoing=[(1,100,11),(2,100,12)] childResults=[10,10] → 2 + 20 = 22.
        HyloOutcome<int> outcome = await GraphHylo.HyloAsync<int, int>(
            seed: 0,
            coalgebra: seed =>
            {
                if(seed == 0)
                {
                    return new GraphExpansion<int>(EncodedTriple.FromEncoded(0, 100, 99), [1, 2]);
                }

                return new GraphExpansion<int>(EncodedTriple.FromEncoded((uint)seed, 100, (uint)(seed + 10)), []);
            },
            algebra: (_, outgoing, childResults) =>
            {
                if(outgoing.Count == 0 && childResults.Count == 0)
                {
                    return 10;
                }

                int sum = outgoing.Count;
                foreach(int child in childResults)
                {
                    sum += child;
                }

                return sum;
            },
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(outcome.HasResult);
        Assert.AreEqual(22, outcome.Result);
    }
}
