using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Pins the exact-answer contract of <see cref="BoxContainmentIndex"/>: for every query box the
/// index must yield exactly the registered boxes that enclose it — the same set a scan over every
/// box would find. Miss a container and a containment-shaped consumer's depth changes, so a
/// divergence here is a correctness bug, not an approximation. The bounds are non-strict
/// (touching and identical boxes contain each other), matching <see cref="BoundingBox.Contains"/>.
/// A second concern the tests pin is the reason the index exists: on the archipelago shape — one
/// lake box enclosing thousands of island boxes — the candidates yielded, and so the containment
/// tests a consumer then runs, stay linear in the box count rather than quadratic. The last row
/// pins the caller-provided pooling seam: every column returns to the caller's pool on dispose.
/// </summary>
[TestClass]
internal sealed class BoxContainmentIndexTests
{
    /// <summary>Disjoint boxes contain only themselves.</summary>
    [TestMethod]
    public void DisjointBoxesContainOnlyThemselves()
    {
        AssertParity(
            new BoundingBox(0, 0, 10, 10),
            new BoundingBox(20, 20, 30, 30),
            new BoundingBox(40, 0, 50, 10));
    }

    /// <summary>A nested ladder answers a full containment chain.</summary>
    [TestMethod]
    public void NestedBoxesFormAContainmentChain()
    {
        //Each larger box contains every smaller one and itself; the chain stresses multi-level results.
        AssertParity(
            new BoundingBox(0, 0, 100, 100),
            new BoundingBox(10, 10, 90, 90),
            new BoundingBox(20, 20, 80, 80),
            new BoundingBox(30, 30, 70, 70));
    }

    /// <summary>Equal boxes contain each other under the non-strict bounds.</summary>
    [TestMethod]
    public void IdenticalBoxesContainEachOther()
    {
        //Equal bounds: the non-strict test must report mutual containment, the equal-area degenerate case.
        AssertParity(
            new BoundingBox(5, 5, 15, 15),
            new BoundingBox(5, 5, 15, 15),
            new BoundingBox(5, 5, 15, 15));
    }

    /// <summary>Flush and edge-sharing boxes respect the non-strict bounds.</summary>
    [TestMethod]
    public void TouchingBoxesRespectNonStrictBounds()
    {
        //An inner box flush against the outer's edges is still enclosed; a box sharing only an outer edge is not.
        AssertParity(
            new BoundingBox(0, 0, 10, 10),
            new BoundingBox(0, 0, 10, 5),
            new BoundingBox(0, 5, 10, 10),
            new BoundingBox(10, 0, 20, 10));
    }

    /// <summary>One lake over a dense island field answers the brute-force set on every probe.</summary>
    [TestMethod]
    public void OneLakeEnclosingManyIslandsMatchesBruteForce()
    {
        var boxes = new List<BoundingBox> { new(0, 0, 10000, 10000) };
        AddIslandGrid(boxes, 30);

        AssertParity([.. boxes]);
    }

    /// <summary>A fixed-start pseudorandom field over integer coordinates answers the brute-force set.</summary>
    [TestMethod]
    public void RandomBoxesMatchBruteForce()
    {
        //Integer coordinates make exact ties — touching edges, equal boxes, zero-area boxes — common, the cases the bound test must get right.
        //The fixed mixing start keeps the sweep reproducible, and every expectation is the brute-force oracle, so no literal depends on the mixer.
        ulong state = 0x9E3779B97F4A7C15UL;
        var boxes = new List<BoundingBox>();

        for(int index = 0; index < 500; index++)
        {
            double x = DeterministicBitMixer.NextBelow(ref state, 80);
            double y = DeterministicBitMixer.NextBelow(ref state, 80);
            double width = DeterministicBitMixer.NextBelow(ref state, 40);
            double height = DeterministicBitMixer.NextBelow(ref state, 40);

            boxes.Add(new BoundingBox(x, y, x + width, y + height));
        }

        //A few deliberate extremes: a universe box, a duplicate of it, and a degenerate point.
        boxes.Add(new BoundingBox(0, 0, 120, 120));
        boxes.Add(new BoundingBox(0, 0, 120, 120));
        boxes.Add(new BoundingBox(60, 60, 60, 60));

        AssertParity([.. boxes]);
    }

    /// <summary>The archipelago's total candidate count stays linear in the box count.</summary>
    [TestMethod]
    public void ArchipelagoKeepsContainmentTestsLinear()
    {
        //The lever's point: with one lake box over a dense island field, the candidates yielded per query — the
        //containment tests a consumer then runs — total O(islands), not O(islands^2) as an all-pairs scan would.
        const int Side = 50;
        var boxes = new List<BoundingBox> { new(0, 0, 10000, 10000) };
        AddIslandGrid(boxes, Side);

        BoundingBox[] items = [.. boxes];

        using BoxContainmentIndex index = BoxContainmentIndex.Create();

        index.Build(items);

        long totalCandidates = 0;

        for(int query = 0; query < items.Length; query++)
        {
            int candidates = AssertContainersMatch(index, items, items[query]);
            totalCandidates += candidates;
        }

        //Each island is enclosed only by the lake and itself (two), the lake only by itself (one): ~2N. Quadratic would be ~N^2/2.
        long linearCeiling = (long)items.Length * 8;
        Assert.IsLessThan(linearCeiling, totalCandidates,
            $"Expected linear containment tests; got {totalCandidates} for {items.Length} boxes (quadratic would be ~{(long)items.Length * items.Length / 2}).");
    }

    /// <summary>A rebuild over one instance re-derives the whole working set, node count included.</summary>
    [TestMethod]
    public void ResetRebuildsCleanlyForReuse()
    {
        using BoxContainmentIndex index = BoxContainmentIndex.Create();

        var first = new List<BoundingBox> { new(0, 0, 10000, 10000) };
        AddIslandGrid(first, 12);
        BoundingBox[] firstBoxes = [.. first];

        index.Build(firstBoxes);
        AssertContainersMatch(index, firstBoxes, firstBoxes[1]);

        int firstNodeCount = index.NodeCount;
        Assert.IsGreaterThan(1, firstNodeCount, "A 145-box build must split into more than the root node.");

        //Reuse the same instance for a different, smaller set — buffers and node count must start fresh.
        BoundingBox[] secondBoxes = [new BoundingBox(0, 0, 5, 5)];

        index.Build(secondBoxes);
        AssertContainersMatch(index, secondBoxes, secondBoxes[0]);

        Assert.AreEqual(1, index.NodeCount, "A single-box rebuild must re-derive the node count, never accumulate the prior build's.");

        //And the empty rebuild collapses the tree without leaving the prior answers reachable.
        index.Build(ReadOnlySpan<BoundingBox>.Empty);

        Assert.AreEqual(0, index.NodeCount);
        Assert.HasCount(0, Collect(index, new BoundingBox(0, 0, 1, 1)), "An empty build must answer nothing.");
    }

    /// <summary>Every column rents from the caller's pool and returns to it on dispose.</summary>
    [TestMethod]
    public void CallerProvidedPoolsBackEveryColumnAndReturnOnDispose()
    {
        //The pooling seam this copy binds: the ordinate and integer columns are the caller's
        //rentals, not the shared pool's. A pool whose every rental has come back reclaims all of
        //its slabs, so the trim after dispose is the observable that the index leaks nothing.
        using var ordinatePool = new VeritasMemoryPool<double>();
        using var indexPool = new VeritasMemoryPool<int>();

        var boxes = new List<BoundingBox> { new(0, 0, 10000, 10000) };
        AddIslandGrid(boxes, 10);
        BoundingBox[] items = [.. boxes];

        using(BoxContainmentIndex index = BoxContainmentIndex.Create(ordinatePool, indexPool))
        {
            index.Build(items);
            AssertContainersMatch(index, items, items[1]);

            //Clear the size classes the growth pass already freed, so what remains is exactly the live rentals.
            _ = ordinatePool.TrimExcess();
            _ = indexPool.TrimExcess();
        }

        Assert.IsGreaterThan(0, ordinatePool.TrimExcess(), "Every ordinate column must have returned to the caller's pool.");
        Assert.IsGreaterThan(0, indexPool.TrimExcess(), "Every integer column must have returned to the caller's pool.");
    }

    /// <summary>A grid of small, well-separated island boxes inside the [0,10000] lake; none encloses another, so each is contained only by the lake.</summary>
    /// <param name="boxesToAppendTo">The fixture the islands are appended to.</param>
    /// <param name="side">The island count on each axis.</param>
    private static void AddIslandGrid(List<BoundingBox> boxesToAppendTo, int side)
    {
        for(int row = 0; row < side; row++)
        {
            for(int column = 0; column < side; column++)
            {
                double x = 100 + (column * 190);
                double y = 100 + (row * 190);

                boxesToAppendTo.Add(new BoundingBox(x, y, x + 20, y + 20));
            }
        }
    }

    /// <summary>
    /// Builds the index over the boxes and asserts the index's container set matches a
    /// brute-force scan for every registered box plus a grid of point and small probe queries that overspill the bounds.
    /// </summary>
    /// <param name="boxes">The fixture to register.</param>
    private static void AssertParity(params BoundingBox[] boxes)
    {
        using BoxContainmentIndex index = BoxContainmentIndex.Create();

        index.Build(boxes);

        for(int query = 0; query < boxes.Length; query++)
        {
            AssertContainersMatch(index, boxes, boxes[query]);
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        for(int box = 0; box < boxes.Length; box++)
        {
            minX = Math.Min(minX, boxes[box].MinX);
            minY = Math.Min(minY, boxes[box].MinY);
            maxX = Math.Max(maxX, boxes[box].MaxX);
            maxY = Math.Max(maxY, boxes[box].MaxY);
        }

        const int Steps = 12;
        double stepX = Math.Max((maxX - minX) / Steps, 1.0);
        double stepY = Math.Max((maxY - minY) / Steps, 1.0);

        for(int row = -1; row <= Steps + 1; row++)
        {
            for(int column = -1; column <= Steps + 1; column++)
            {
                double x = minX + (column * stepX);
                double y = minY + (row * stepY);

                //A zero-area point query and a small box query at the same spot.
                AssertContainersMatch(index, boxes, new BoundingBox(x, y, x, y));
                AssertContainersMatch(index, boxes, new BoundingBox(x, y, x + (stepX * 0.5), y + (stepY * 0.5)));
            }
        }
    }

    /// <summary>Asserts the index yields exactly the brute-force container set for one query; returns the candidate count.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="boxes">The registered fixture the oracle scans.</param>
    /// <param name="query">The query box.</param>
    /// <returns>The number of containers the index yielded.</returns>
    private static int AssertContainersMatch(BoxContainmentIndex index, ReadOnlySpan<BoundingBox> boxes, BoundingBox query)
    {
        var brute = new List<int>();

        for(int box = 0; box < boxes.Length; box++)
        {
            if(boxes[box].Contains(query))
            {
                brute.Add(box);
            }
        }

        List<int> viaIndex = Collect(index, query);
        viaIndex.Sort();

        Assert.HasCount(brute.Count, viaIndex,
            $"Container count mismatch for query ({query.MinX}, {query.MinY}, {query.MaxX}, {query.MaxY}).");
        Assert.AreSequenceEqual(brute, viaIndex,
            $"Container set mismatch for query ({query.MinX}, {query.MinY}, {query.MaxX}, {query.MaxY}).");

        return viaIndex.Count;
    }

    /// <summary>Enumerates one query through the public foreach pattern into a list, order preserved.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="query">The query box.</param>
    /// <returns>The answered item ids in enumeration order.</returns>
    private static List<int> Collect(BoxContainmentIndex index, BoundingBox query)
    {
        var results = new List<int>();

        foreach(int item in index.Containers(in query))
        {
            results.Add(item);
        }

        return results;
    }
}
