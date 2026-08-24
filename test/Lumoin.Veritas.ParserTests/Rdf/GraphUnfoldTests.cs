using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

[TestClass]
internal sealed class GraphUnfoldTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task UnfoldEmitsSingleTripleForLeafSeed()
    {
        //Coalgebra turns an int seed into a triple (seed, 100, seed+1) with no further seeds.
        GraphExpansion<int> Coalgebra(int seed)
        {
            return new GraphExpansion<int>(
                EncodedTriple.FromEncoded((uint)seed, 100, (uint)(seed + 1)),
                []);
        }

        List<EncodedTriple> emitted = [];
        await foreach(EncodedTriple triple in GraphUnfold.UnfoldAsync<int>(
            seed: 0,
            coalgebra: Coalgebra,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            emitted.Add(triple);
        }

        Assert.HasCount(1, emitted);
        Assert.AreEqual(EncodedTriple.FromEncoded(0, 100, 1), emitted[0]);
    }

    [TestMethod]
    public async Task UnfoldExpandsFiniteChain()
    {
        //Seed is a counter. Coalgebra emits (seed, 100, seed+1) and seeds the next step
        //while seed < 3, stopping by returning no triple and no seeds.
        GraphExpansion<int> Coalgebra(int seed)
        {
            if(seed >= 3)
            {
                return new GraphExpansion<int>(null, []);
            }

            return new GraphExpansion<int>(
                EncodedTriple.FromEncoded((uint)seed, 100, (uint)(seed + 1)),
                [seed + 1]);
        }

        List<EncodedTriple> emitted = [];
        await foreach(EncodedTriple triple in GraphUnfold.UnfoldAsync<int>(
            seed: 0,
            coalgebra: Coalgebra,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            emitted.Add(triple);
        }

        Assert.HasCount(3, emitted);
        Assert.AreEqual(EncodedTriple.FromEncoded(0, 100, 1), emitted[0]);
        Assert.AreEqual(EncodedTriple.FromEncoded(1, 100, 2), emitted[1]);
        Assert.AreEqual(EncodedTriple.FromEncoded(2, 100, 3), emitted[2]);
    }

    [TestMethod]
    public async Task UnfoldBranchesOnMultipleSeeds()
    {
        //Seed 0 expands into (0, 100, 1) and produces two further seeds: 10 and 20.
        //Seeds 10 and 20 each produce one leaf triple and no further seeds.
        GraphExpansion<int> Coalgebra(int seed)
        {
            if(seed == 0)
            {
                return new GraphExpansion<int>(
                    EncodedTriple.FromEncoded(0, 100, 1),
                    [10, 20]);
            }

            return new GraphExpansion<int>(
                EncodedTriple.FromEncoded((uint)seed, 100, (uint)(seed + 1)),
                []);
        }

        List<EncodedTriple> emitted = [];
        await foreach(EncodedTriple triple in GraphUnfold.UnfoldAsync<int>(
            seed: 0,
            coalgebra: Coalgebra,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            emitted.Add(triple);
        }

        Assert.HasCount(3, emitted);
        Assert.Contains(EncodedTriple.FromEncoded(0, 100, 1), emitted);
        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 11), emitted);
        Assert.Contains(EncodedTriple.FromEncoded(20, 100, 21), emitted);
    }
}
