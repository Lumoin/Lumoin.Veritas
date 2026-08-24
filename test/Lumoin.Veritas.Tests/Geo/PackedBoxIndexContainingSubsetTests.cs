using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The subset-pop gate for <see cref="PackedBoxIndex"/>'s composed containing descent: a
/// test-side oracle walks the identical built dominance tree through the internal diagnostic
/// accessors, running only the half-space partition prune the production descent composes
/// with the union-box prune. The rows here pin three things the union rule's soundness
/// predicts: the composed descent's per-query visited-node count is a subset count of the
/// oracle's, the union rule strictly prunes a wide-union adversary the half-space rule alone
/// cannot touch, and the composed route stays exact and deterministic on the multi-container
/// extension fixture.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexContainingSubsetTests
{
    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>The composed descent's visited-node count never exceeds the half-space-only oracle's, and both agree with brute force, across the named fixture family.</summary>
    [TestMethod]
    public void ComposedDescentVisitsNeverExceedTheHalfSpaceOnlyOracle()
    {
        //The union prune only ever removes pops — every node the composed descent pops, the
        //half-space-only walk also pops — so the oracle's count is a per-query ceiling. The
        //union rule is also sound (a subtree whose union does not contain the query holds no
        //container of it), so the nodes it prunes away can never have held a real match: the
        //answer sets cannot differ between the composed descent, the oracle, and brute force.
        foreach((string name, BoundingBox[] items) in PackedBoxIndexFixtureFamily.NamedFixtures())
        {
            foreach(BoxIndexPacking packing in Packings)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

                Assert.IsTrue(index.TryBuild(items), $"The '{name}' fixture must build under {packing}.");

                BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

                foreach(BoundingBox probe in probes)
                {
                    string context = $"fixture '{name}', packing {packing}, probe ({probe.MinX}, {probe.MinY}, {probe.MaxX}, {probe.MaxY})";
                    List<int> realMatches = CollectRealContaining(index, probe, out int realVisitedNodeCount);
                    (int oraclePops, List<int> oracleMatches) = RunHalfSpaceOnlyOracle(index, items, probe);
                    List<int> bruteForceMatches = BruteForceContaining(items, probe);

                    Assert.IsLessThanOrEqualTo(oraclePops, realVisitedNodeCount,
                        $"The composed descent's visited-node count must be a subset count of the half-space-only oracle's; {context}.");

                    realMatches.Sort();
                    oracleMatches.Sort();

                    Assert.AreSequenceEqual(oracleMatches, realMatches, $"The composed descent's match set must equal the oracle's; {context}.");
                    Assert.AreSequenceEqual(bruteForceMatches, realMatches, $"The composed descent's match set must equal brute force; {context}.");
                }
            }
        }
    }

    /// <summary>The union prune strictly cuts visits below the half-space-only oracle's on a wide-envelope-over-thin-islands adversary the one-axis rule alone cannot separate.</summary>
    [TestMethod]
    public void ComposedDescentPrunesTheWideUnionShapesStrictly()
    {
        //A wide envelope over thin scattered islands is the shape the union prune is built
        //for: a one-axis half-space split alone cannot separate the islands apart (every split
        //still carries the wide envelope's own extremes on its split axis), while the union
        //prune kills a whole island subtree the instant its exact union misses the query on
        //any one of the four bounds.
        var items = new BoundingBox[901];
        items[0] = new BoundingBox(0, 0, 10_000, 10_000);

        for(int island = 0; island < 900; island++)
        {
            double x = (island % 30) * (10_000d / 30);
            double y = (island / 30) * (10_000d / 30);
            items[island + 1] = new BoundingBox(x, y, x + 8, y + 8);
        }

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items), "The archipelago fixture must build.");

        BoundingBox[] probes = [new BoundingBox(50, 50, 50, 50), new BoundingBox(40, 40, 460, 460)];

        foreach(BoundingBox probe in probes)
        {
            string context = $"probe ({probe.MinX}, {probe.MinY}, {probe.MaxX}, {probe.MaxY})";
            List<int> realMatches = CollectRealContaining(index, probe, out int realVisitedNodeCount);
            (int oraclePops, _) = RunHalfSpaceOnlyOracle(index, items, probe);
            List<int> bruteForceMatches = BruteForceContaining(items, probe);

            Assert.IsLessThan(oraclePops, realVisitedNodeCount,
                $"The composed route must genuinely prune where the one-axis rule alone cannot; {context}.");

            realMatches.Sort();

            Assert.AreSequenceEqual(bruteForceMatches, realMatches, $"The match set must agree with brute force; {context}.");
        }
    }

    /// <summary>The composed descent agrees with brute force and stays deterministic across two independent builds on the extension fixture's every probe.</summary>
    [TestMethod]
    public void ComposedDescentAgreesWithBruteForceOnTheExtensionFixtureEveryProbe()
    {
        //The extension fixture's overlapping and nested boxes make multi-result containment
        //possible, so this row also pins the composed route's determinism: two independently
        //built indexes over the identical sequence must emit the identical Containing order,
        //not merely the identical set.
        BoundingBox[] items = PackedBoxIndexFixtureFamily.ExtensionFixture();
        BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);
        int[] capacities = [4, 16];

        foreach(BoxIndexPacking packing in Packings)
        {
            foreach(int capacity in capacities)
            {
                var options = new PackedBoxIndexOptions(packing, capacity);
                using PackedBoxIndex first = PackedBoxIndex.Create(options);
                using PackedBoxIndex second = PackedBoxIndex.Create(options);

                Assert.IsTrue(first.TryBuild(items), $"The first build of the extension fixture must succeed under {packing}/capacity {capacity}.");
                Assert.IsTrue(second.TryBuild(items), $"The second build of the extension fixture must succeed under {packing}/capacity {capacity}.");

                foreach(BoundingBox probe in probes)
                {
                    string context = $"packing {packing}, capacity {capacity}, probe ({probe.MinX}, {probe.MinY}, {probe.MaxX}, {probe.MaxY})";
                    List<int> firstMatches = CollectRealContaining(first, probe, out _);
                    List<int> secondMatches = CollectRealContaining(second, probe, out _);
                    List<int> bruteForceMatches = BruteForceContaining(items, probe);

                    Assert.AreSequenceEqual(firstMatches, secondMatches, $"Two builds from one sequence must emit the identical Containing order; {context}.");

                    var sortedFirstMatches = new List<int>(firstMatches);
                    sortedFirstMatches.Sort();

                    Assert.AreSequenceEqual(bruteForceMatches, sortedFirstMatches, $"The match set must equal brute force; {context}.");
                }
            }
        }
    }

    /// <summary>Runs the real Containing enumeration through the manual using/MoveNext pattern, collecting matches in emission order alongside the composed descent's visited-node count.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="probe">The query box.</param>
    /// <param name="visitedNodeCount">The composed descent's visited-node count.</param>
    /// <returns>The matches in emission order.</returns>
    private static List<int> CollectRealContaining(PackedBoxIndex index, BoundingBox probe, out int visitedNodeCount)
    {
        var matches = new List<int>();
        using PackedBoxIndex.Enumerator containing = index.Containing(in probe).GetEnumerator();

        while(containing.MoveNext())
        {
            matches.Add(containing.Current);
        }

        visitedNodeCount = containing.VisitedNodeCount;

        return matches;
    }

    /// <summary>
    /// The half-space-only descent oracle: walks the identical built dominance tree through
    /// the internal diagnostic accessors, WITHOUT the union prune the production descent
    /// composes it with — the one-sided partition rule alone. Iterative, an explicit
    /// int-array stack (no recursion, a solution-wide rule test code holds to as well); 64
    /// entries is ample for the dominance tree's count-halving depth bound over every fixture
    /// this suite builds.
    /// </summary>
    /// <param name="index">The built index.</param>
    /// <param name="items">The registered items.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The oracle's pop count and its matches.</returns>
    private static (int Pops, List<int> Matches) RunHalfSpaceOnlyOracle(PackedBoxIndex index, BoundingBox[] items, BoundingBox probe)
    {
        var matches = new List<int>();
        int pops = 0;

        if(index.Count == 0)
        {
            return (pops, matches);
        }

        var stack = new int[64];
        int top = 0;
        stack[top] = 0;
        top++;

        while(top > 0)
        {
            top--;
            int node = stack[top];
            pops++;
            (int left, int itemStart, int itemSpan) = index.DominanceNodeRange(node);

            if(left < 0)
            {
                for(int position = itemStart; position < itemStart + itemSpan; position++)
                {
                    int slot = index.DominanceOrderSlot(position);
                    int registration = index.ItemSlotRegistration(slot);
                    BoundingBox box = items[registration];

                    if(box.MinX <= probe.MinX && box.MaxX >= probe.MaxX && box.MinY <= probe.MinY && box.MaxY >= probe.MaxY)
                    {
                        matches.Add(registration);
                    }
                }

                continue;
            }

            (int right, int axis, double split) = index.DominanceNodeSplitFacts(node);
            bool descendLeft;
            bool descendRight;

            if(axis % 2 == 0)
            {
                //MinX / MinY: a container needs its value at or below the probe's.
                double bound = axis == 0 ? probe.MinX : probe.MinY;
                descendLeft = true;
                descendRight = split <= bound;
            }
            else
            {
                //MaxX / MaxY: a container needs its value at or above the probe's.
                double bound = axis == 1 ? probe.MaxX : probe.MaxY;
                descendRight = true;
                descendLeft = split >= bound;
            }

            if(descendRight)
            {
                stack[top] = right;
                top++;
            }

            if(descendLeft)
            {
                stack[top] = left;
                top++;
            }
        }

        return (pops, matches);
    }

    /// <summary>The brute-force containment scan over the public box algebra: every registration whose stored box contains the probe, in ascending registration order.</summary>
    /// <param name="items">The registered items.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The ascending registration indices whose boxes contain the probe.</returns>
    private static List<int> BruteForceContaining(BoundingBox[] items, BoundingBox probe)
    {
        var matches = new List<int>();

        for(int registration = 0; registration < items.Length; registration++)
        {
            if(items[registration].Contains(probe))
            {
                matches.Add(registration);
            }
        }

        return matches;
    }
}
