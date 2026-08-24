using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Structure, parity, determinism, sanitation, options, and lifecycle gates
/// for <see cref="PackedBoxIndex"/>. The structural rows pin the level/node
/// arithmetic and the run-of-capacity fill through the internal observables
/// (an even-fill would change node counts outright on some shapes and child
/// count sequences on others); the parity harness holds all three query
/// modes to brute force over the public box algebra across packings,
/// capacities, and the internal Hilbert grid widths, over a fixture family
/// shared with the containment-parity suite and including the cross
/// adversary that defeats union-bound pruning; the determinism rows
/// pin enumeration order as a contract of (packing, capacity, item
/// sequence); the sanitation and lifecycle rows pin destructive refusal, the
/// disposed-state table, and the build-version guard. Reentrancy,
/// concurrency, and the canary and envelope gates live in their own suite.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexTests
{
    /// <summary>The node capacities the parity harness sweeps.</summary>
    private static readonly int[] ParityCapacities = [2, 3, 4, 16];

    /// <summary>The internal Hilbert grid widths the parity harness sweeps.</summary>
    private static readonly int[] HilbertGridWidths = [16, 31];

    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>The transcribed Sort-Tile-Recursive preorder over the whole golden fixture.</summary>
    private static readonly int[] GoldenSortTileRecursiveFullTraversal = [0, 5, 11, 10, 1, 12, 8, 13, 2, 7, 9, 3, 4, 6];

    /// <summary>The transcribed Hilbert preorder over the whole golden fixture; the two packings deliberately disagree.</summary>
    private static readonly int[] GoldenHilbertFullTraversal = [0, 11, 5, 10, 2, 7, 3, 4, 9, 6, 13, 12, 8, 1];

    /// <summary>The golden fixture's mid-region intersecting answer, identical under both packings.</summary>
    private static readonly int[] GoldenMidRegionCandidates = [10, 2, 7, 4];

    /// <summary>The golden fixture's point-probe containing answer.</summary>
    private static readonly int[] GoldenPointProbeContainers = [2];

    /// <summary>The ascending registration pair the signed-zero tie must close to.</summary>
    private static readonly int[] AscendingRegistrationPair = [0, 1];

    /// <summary>The ascending registration run the one-cell key tie must close to.</summary>
    private static readonly int[] AscendingRegistrationCluster = [0, 1, 2, 3, 4];

    /// <summary>The extension fixture's probes for the multi-result containing rows, in the order the pinned emission tables index.</summary>
    private static readonly BoundingBox[] ExtensionContainingProbes =
    [
        new BoundingBox(49, 49, 51, 51),
        new BoundingBox(25, 45, 28, 55),
        new BoundingBox(73, 73, 76, 76),
        new BoundingBox(50, 50, 50, 50)
    ];

    /// <summary>The transcribed Sort-Tile-Recursive containing emissions over the extension fixture, one row per probe.</summary>
    private static readonly int[][] GoldenExtensionSortTileRecursiveContaining =
    [
        [9, 0, 1, 2, 3, 4, 10, 16],
        [0, 1, 11, 10, 6],
        [0, 1, 10, 12],
        [9, 0, 1, 2, 3, 4, 10, 16]
    ];

    /// <summary>The transcribed Hilbert containing emissions over the extension fixture; the two packings deliberately disagree.</summary>
    private static readonly int[][] GoldenExtensionHilbertContaining =
    [
        [2, 3, 4, 9, 10, 16, 0, 1],
        [11, 6, 10, 0, 1],
        [10, 0, 1, 12],
        [2, 3, 4, 9, 10, 16, 0, 1]
    ];

    /// <summary>The level and node counts follow the ceil chain under both packings.</summary>
    /// <param name="capacity">The node capacity.</param>
    /// <param name="itemCount">The item count of the build.</param>
    /// <param name="expectedLevels">The expected node level count.</param>
    /// <param name="expectedNodes">The expected node slot count.</param>
    [TestMethod]
    [DataRow(2, 1, 1, 1, DisplayName = "capacity 2, one item: the sole leaf node is the root")]
    [DataRow(2, 2, 1, 1, DisplayName = "capacity 2, two items: one full leaf node is the root")]
    [DataRow(2, 3, 2, 3, DisplayName = "capacity 2, three items: two leaves (2+1) under a root")]
    [DataRow(2, 4, 2, 3, DisplayName = "capacity 2, four items: two full leaves under a root")]
    [DataRow(2, 5, 3, 6, DisplayName = "capacity 2, five items: leaves 3, then 2, then the root")]
    [DataRow(2, 8, 3, 7, DisplayName = "capacity 2, eight items: leaves 4, then 2, then the root")]
    [DataRow(3, 1, 1, 1, DisplayName = "capacity 3, one item: the sole leaf node is the root")]
    [DataRow(3, 2, 1, 1, DisplayName = "capacity 3, two items: one leaf node is the root")]
    [DataRow(3, 3, 1, 1, DisplayName = "capacity 3, three items: one full leaf node is the root")]
    [DataRow(3, 4, 2, 3, DisplayName = "capacity 3, four items: two leaves (3+1) under a root")]
    [DataRow(3, 9, 2, 4, DisplayName = "capacity 3, nine items: three full leaves under a root")]
    [DataRow(3, 10, 3, 7, DisplayName = "capacity 3, ten items: leaves 4, then 2, then the root")]
    [DataRow(16, 1, 1, 1, DisplayName = "capacity 16, one item: the sole leaf node is the root")]
    [DataRow(16, 15, 1, 1, DisplayName = "capacity 16, fifteen items: one leaf node is the root")]
    [DataRow(16, 16, 1, 1, DisplayName = "capacity 16, sixteen items: one full leaf node is the root")]
    [DataRow(16, 17, 2, 3, DisplayName = "capacity 16, seventeen items: two leaves under a root")]
    [DataRow(16, 256, 2, 17, DisplayName = "capacity 16, 256 items: sixteen full leaves under a root")]
    [DataRow(16, 257, 3, 20, DisplayName = "capacity 16, 257 items: leaves 17, then 2, then the root")]
    public void LevelAndNodeCountsFollowTheCeilChain(int capacity, int itemCount, int expectedLevels, int expectedNodes)
    {
        //Each expectation is the ceil-chain by hand: level k holds ceil(previous / capacity)
        //nodes until one remains. The shape must be identical under both packings — the
        //ordering pass permutes entries, never regroups them.
        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

            Assert.IsTrue(index.TryBuild(DisjointGrid(itemCount)));
            Assert.AreEqual(expectedLevels, index.LevelCount, $"Level count diverged from the ceil chain under {packing}.");
            Assert.AreEqual(expectedNodes, index.NodeCount, $"Node count diverged from the ceil chain under {packing}.");
        }
    }

    /// <summary>The never-built and empty-built states share one empty shape.</summary>
    [TestMethod]
    public void EmptyAndNeverBuiltIndexesHaveNoLevelsAndNoNodes()
    {
        using PackedBoxIndex neverBuilt = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.AreEqual(0, neverBuilt.Count);
        Assert.AreEqual(0, neverBuilt.LevelCount);
        Assert.AreEqual(0, neverBuilt.NodeCount);

        using PackedBoxIndex emptyBuilt = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(emptyBuilt.TryBuild(ReadOnlySpan<BoundingBox>.Empty), "An empty span is not malformed; it builds successfully.");
        Assert.AreEqual(0, emptyBuilt.Count);
        Assert.AreEqual(0, emptyBuilt.LevelCount);
        Assert.AreEqual(0, emptyBuilt.NodeCount);
    }

    /// <summary>The leaf level packs full runs of the capacity with at most one partial node.</summary>
    /// <param name="capacity">The node capacity.</param>
    /// <param name="itemCount">The item count of the build.</param>
    /// <param name="expectedOrderedChildCounts">The expected start-ordered leaf child counts.</param>
    [TestMethod]
    [DataRow(3, 8, new[] { 3, 3, 2 }, DisplayName = "eight items at capacity 3 pack 3+3+2 — an even two-slice fill would form four leaves")]
    [DataRow(3, 10, new[] { 3, 3, 3, 1 }, DisplayName = "ten items at capacity 3 pack 3+3+3+1 — an even fill would spread 3+2+3+2")]
    public void LeafFillPacksFullRunsWithAtMostOnePartialNode(int capacity, int itemCount, int[] expectedOrderedChildCounts)
    {
        //The leaf level's child runs, ordered by their start slot, must tile the item region
        //in full runs of the capacity with at most one short run at the end. The two rows are
        //the shapes on which the rejected even-fill alternative differs observably: once in
        //the node count itself, once only in the child-count sequence.
        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

            Assert.IsTrue(index.TryBuild(DisjointGrid(itemCount)));

            (int start, int end) = index.LevelSlots(0);
            var childRuns = new List<(int Start, int Count)>();

            for(int slot = start; slot < end; slot++)
            {
                (int childStart, int childCount) = index.NodeChildRun(slot);
                childRuns.Add((childStart, childCount));
            }

            childRuns.Sort();

            Assert.HasCount(expectedOrderedChildCounts.Length, childRuns, $"Leaf node count diverged under {packing}.");
            Assert.AreSequenceEqual(expectedOrderedChildCounts, childRuns.Select(run => run.Count).ToArray(),
                $"The start-ordered child counts must be full runs with at most one partial, under {packing}.");
        }
    }

    /// <summary>Every level's child runs tile the previous region exactly.</summary>
    [TestMethod]
    public void EveryLevelsChildRunsTileThePreviousRegionExactly()
    {
        //For every node level: the level's child runs, ordered by start, must cover the
        //previous region (items for the leaf level, the previous level's slots above) exactly
        //once with no gap and no overlap. This is the run encoding's structural invariant —
        //the level ordering permutes where a node is stored, never which run it owns.
        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 3));

            Assert.IsTrue(index.TryBuild(ClusteredBoxes(100, startState: 20260803UL)));

            int previousRegionStart = 0;
            int previousRegionEnd = index.Count;

            for(int level = 0; level < index.LevelCount; level++)
            {
                (int start, int end) = index.LevelSlots(level);
                var childRuns = new List<(int Start, int Count)>();

                for(int slot = start; slot < end; slot++)
                {
                    childRuns.Add(index.NodeChildRun(slot));
                }

                childRuns.Sort();

                int cursor = previousRegionStart;

                foreach((int runStart, int runCount) in childRuns)
                {
                    Assert.AreEqual(cursor, runStart, $"Level {level} child runs must tile without gap or overlap under {packing}.");
                    cursor += runCount;
                }

                Assert.AreEqual(previousRegionEnd, cursor, $"Level {level} child runs must cover the previous region exactly under {packing}.");

                previousRegionStart = start;
                previousRegionEnd = end;
            }
        }
    }

    /// <summary>Every mode answers the brute-force set on every fixture, packing, capacity, and grid width.</summary>
    [TestMethod]
    public void AllThreeModesMatchBruteForceAcrossConfigurationsAndFixtures()
    {
        //The parity harness: every candidate set equals a brute-force scan with the public box
        //algebra, for every named fixture, every probe, every packing, every capacity, and
        //both internal Hilbert grid widths — candidate sets are invariant across all of them.
        foreach((string name, BoundingBox[] items) in PackedBoxIndexFixtureFamily.NamedFixtures())
        {
            BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

            List<int>[][]? referenceSets = null;

            foreach(BoxIndexPacking packing in Packings)
            {
                foreach(int capacity in ParityCapacities)
                {
                    foreach(int width in HilbertGridWidths)
                    {
                        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity), width);

                        Assert.IsTrue(index.TryBuild(items), $"Fixture '{name}' must build.");

                        List<int>[][] observed = CollectAllModes(index, probes);

                        for(int mode = 0; mode < 3; mode++)
                        {
                            for(int probe = 0; probe < probes.Length; probe++)
                            {
                                List<int> expected = BruteForce(items, (PackedBoxIndex.QueryMode)mode, probes[probe]);
                                List<int> actual = [.. observed[mode][probe]];
                                actual.Sort();

                                Assert.AreSequenceEqual(expected, actual,
                                    $"Fixture '{name}', probe {probe}, mode {(PackedBoxIndex.QueryMode)mode}, {packing}/capacity {capacity}/width {width}: candidate set diverged from brute force.");
                            }
                        }

                        //Cross-configuration set equality: every configuration answers the same sets.
                        if(referenceSets is null)
                        {
                            referenceSets = observed;

                            for(int mode = 0; mode < 3; mode++)
                            {
                                foreach(List<int> sequence in referenceSets[mode])
                                {
                                    sequence.Sort();
                                }
                            }
                        }
                        else
                        {
                            for(int mode = 0; mode < 3; mode++)
                            {
                                for(int probe = 0; probe < probes.Length; probe++)
                                {
                                    List<int> actual = [.. observed[mode][probe]];
                                    actual.Sort();
                                    Assert.AreSequenceEqual(referenceSets[mode][probe], actual,
                                        $"Fixture '{name}': candidate sets must be identical across configurations.");
                                }
                            }
                        }
                    }
                }
            }

            referenceSets = null;
        }
    }

    /// <summary>The size ladder answers brute force at every rung under both packings.</summary>
    [TestMethod]
    public void FuzzSweepMatchesBruteForceAtEverySize()
    {
        //Fixed-start pseudorandom boxes across the size ladder, one probe grid per size; the
        //fixed mixing start makes the sweep reproducible rather than a coverage lottery.
        foreach(int size in new[] { 0, 1, 2, 15, 16, 17, 100, 1000 })
        {
            BoundingBox[] items = ClusteredBoxes(size, startState: 62_000UL + (ulong)size);
            BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

            foreach(BoxIndexPacking packing in Packings)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 4));

                Assert.IsTrue(index.TryBuild(items));

                for(int mode = 0; mode < 3; mode++)
                {
                    foreach(BoundingBox probe in probes)
                    {
                        List<int> expected = BruteForce(items, (PackedBoxIndex.QueryMode)mode, probe);
                        List<int> actual = Collect(index, (PackedBoxIndex.QueryMode)mode, probe);
                        actual.Sort();

                        Assert.AreSequenceEqual(expected, actual,
                            $"Size {size}, mode {(PackedBoxIndex.QueryMode)mode}, {packing}: candidate set diverged from brute force.");
                    }
                }
            }
        }
    }

    /// <summary>Two builds from one item sequence enumerate identically, order included.</summary>
    [TestMethod]
    public void RepeatBuildsEnumerateIdenticallyOrderIncluded()
    {
        //Enumeration order is contract within (packing, capacity) over the same item
        //sequence: build twice from one sequence and every probe must enumerate the identical
        //list, order included. Sorting before comparison would gate nothing here.
        BoundingBox[] items = ClusteredBoxes(200, startState: 42_4242UL);
        BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

        foreach(BoxIndexPacking packing in Packings)
        {
            foreach(int capacity in new[] { 3, 16 })
            {
                var options = new PackedBoxIndexOptions(packing, capacity);
                using PackedBoxIndex first = PackedBoxIndex.Create(options);
                using PackedBoxIndex second = PackedBoxIndex.Create(options);

                Assert.IsTrue(first.TryBuild(items));
                Assert.IsTrue(second.TryBuild(items));

                for(int mode = 0; mode < 3; mode++)
                {
                    foreach(BoundingBox probe in probes)
                    {
                        Assert.AreSequenceEqual(
                            Collect(first, (PackedBoxIndex.QueryMode)mode, probe),
                            Collect(second, (PackedBoxIndex.QueryMode)mode, probe),
                            $"Two builds from one sequence must enumerate identically under {packing}/capacity {capacity}.");
                    }
                }
            }
        }
    }

    /// <summary>The transcribed preorder sequences of a named fixture hold per packing.</summary>
    [TestMethod]
    public void GoldenEnumerationSequencesHoldPerPacking()
    {
        //The committed evidence of machine- and process-independent enumeration order: a
        //named fourteen-box fixture whose full preorder sequences were transcribed from the
        //first verified build and are pinned as literals. A copy of this engine fed the same
        //sequence must reproduce them exactly; any platform or code change that moves a
        //packing key moves one of these rows. The two packings deliberately disagree on the
        //full traversal — that disagreement is itself part of the pin.
        BoundingBox[] items = GoldenFixture();
        var everything = new BoundingBox(0, 0, 100, 100);
        var midRegion = new BoundingBox(20, 20, 60, 60);
        var pointProbe = new BoundingBox(44, 44, 46, 46);

        using PackedBoxIndex sortTileRecursive = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

        Assert.IsTrue(sortTileRecursive.TryBuild(items));
        Assert.AreSequenceEqual(GoldenSortTileRecursiveFullTraversal,
            Collect(sortTileRecursive, PackedBoxIndex.QueryMode.Intersecting, everything));
        Assert.AreSequenceEqual(GoldenMidRegionCandidates,
            Collect(sortTileRecursive, PackedBoxIndex.QueryMode.Intersecting, midRegion));
        Assert.AreSequenceEqual(GoldenPointProbeContainers,
            Collect(sortTileRecursive, PackedBoxIndex.QueryMode.Containing, pointProbe));

        using PackedBoxIndex hilbert = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 4));

        Assert.IsTrue(hilbert.TryBuild(items));
        Assert.AreSequenceEqual(GoldenHilbertFullTraversal,
            Collect(hilbert, PackedBoxIndex.QueryMode.Intersecting, everything));
        Assert.AreSequenceEqual(GoldenMidRegionCandidates,
            Collect(hilbert, PackedBoxIndex.QueryMode.Intersecting, midRegion));
        Assert.AreSequenceEqual(GoldenPointProbeContainers,
            Collect(hilbert, PackedBoxIndex.QueryMode.Containing, pointProbe));
    }

    /// <summary>
    /// Multi-result containing rows are the order evidence a single-result golden cannot
    /// give: the extension fixture's overlapping and nested boxes put up to eight
    /// containers under one probe, so the emission carries a real sequence to pin. The two
    /// packings deliberately disagree on that order; the restriction assertion is the
    /// mechanical order contract — every query's emission is the restriction of one fixed
    /// build-time preorder to its own candidate set — and the literals are the committed
    /// cross-machine evidence.
    /// </summary>
    [TestMethod]
    public void ExtensionFixtureContainingSequencesRestrictThePreorderAndHoldPerPacking()
    {
        BoundingBox[] items = PackedBoxIndexFixtureFamily.ExtensionFixture();
        var everything = new BoundingBox(-1000, -1000, 1000, 1000);

        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 4));

            Assert.IsTrue(index.TryBuild(items));
            Assert.AreEqual(3, index.LevelCount, $"The extension fixture must carry two node levels above the leaf level under {packing}.");

            List<int> fullOrder = Collect(index, PackedBoxIndex.QueryMode.Intersecting, everything);

            for(int probeIndex = 0; probeIndex < ExtensionContainingProbes.Length; probeIndex++)
            {
                BoundingBox probe = ExtensionContainingProbes[probeIndex];
                var containingEmission = new List<int>();

                foreach(int candidate in index.Containing(in probe))
                {
                    containingEmission.Add(candidate);
                }

                List<int> sortedEmission = [.. containingEmission];
                sortedEmission.Sort();

                List<int> bruteForceContaining = BruteForce(items, PackedBoxIndex.QueryMode.Containing, probe);

                Assert.AreSequenceEqual(bruteForceContaining, sortedEmission,
                    $"Probe {probeIndex} under {packing}: the containing candidate set diverged from brute force.");

                var containingRegistrations = new HashSet<int>(bruteForceContaining);
                var restriction = new List<int>();

                foreach(int registration in fullOrder)
                {
                    if(containingRegistrations.Contains(registration))
                    {
                        restriction.Add(registration);
                    }
                }

                Assert.AreSequenceEqual(restriction, containingEmission,
                    $"Probe {probeIndex} under {packing}: the emission of every query is the restriction of one fixed build-time preorder.");

                int[] literalExpected = packing == BoxIndexPacking.SortTileRecursive
                    ? GoldenExtensionSortTileRecursiveContaining[probeIndex]
                    : GoldenExtensionHilbertContaining[probeIndex];

                Assert.AreSequenceEqual(literalExpected, containingEmission,
                    $"Probe {probeIndex} under {packing}: the pinned containing emission sequence diverged.");
            }
        }
    }

    /// <summary>All-identical input enumerates in ascending registration order under both packings.</summary>
    [TestMethod]
    public void AllDuplicateItemsEnumerateInAscendingRegistrationOrder()
    {
        //One thousand identical boxes: every packing key ties at every level, so the
        //tie-break index alone determines the tree — the leaf order is ascending
        //registration, every level's creation order is ascending, and preorder over
        //ascending runs emits 0..N−1. Derivable by hand for both packings, so it is pinned
        //literally.
        var items = new BoundingBox[1000];
        Array.Fill(items, new BoundingBox(5, 5, 6, 6));
        int[] expected = [.. Enumerable.Range(0, items.Length)];

        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

            Assert.IsTrue(index.TryBuild(items));

            Assert.AreSequenceEqual(expected, Collect(index, PackedBoxIndex.QueryMode.Intersecting, new BoundingBox(5, 5, 6, 6)),
                $"All-duplicate input must enumerate in ascending registration order under {packing}.");
        }
    }

    /// <summary>Signed-zero center ties close through the registration tie-break, not a zero-sign distinction.</summary>
    [TestMethod]
    public void SignedZeroCentersTieAndCloseByRegistrationOrder()
    {
        //Two point-boxes whose centers differ only as −0.0 versus +0.0 on Y, registered with
        //the +0.0 box first. Two entries at capacity two form a single slice, so the deciding
        //sort is the within-slice center-Y pass — and double.CompareTo equates the signed
        //zeros (its only departure from the raw operators is giving NaN a total position, and
        //NaN centers cannot survive sanitation), so the pair ties and the registration
        //tie-break closes it. The determinism contract rides that tie-break, never a
        //zero-sign distinction; this row pins the tie closing ascending, deterministically.
        BoundingBox[] items =
        [
            new BoundingBox(1, 0.0, 1, 0.0),
            new BoundingBox(1, -0.0, 1, -0.0)
        ];

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 2));

        Assert.IsTrue(index.TryBuild(items));

        Assert.AreSequenceEqual(AscendingRegistrationPair,
            Collect(index, PackedBoxIndex.QueryMode.Intersecting, new BoundingBox(0, -1, 2, 1)),
            "Signed-zero center ties must close in ascending registration order.");
    }

    /// <summary>Exact key collisions inside one grid cell close by registration order.</summary>
    [TestMethod]
    public void KeyTiesInsideOneGridCellCloseByRegistrationOrder()
    {
        //A far-away box stretches the Hilbert normalization extent so a tight cluster of
        //distinct boxes lands in one grid cell: their distances tie exactly, and the
        //tie-break must close by registration order — the cluster enumerates ascending even
        //though its coordinates are all distinct.
        var items = new BoundingBox[6];

        for(int index = 0; index < 5; index++)
        {
            double offset = index * 1e-13;
            items[index] = new BoundingBox(offset, offset, 1e-12 + offset, 1e-12 + offset);
        }

        items[5] = new BoundingBox(1e6, 1e6, 1e6 + 1, 1e6 + 1);

        using PackedBoxIndex hilbert = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16));

        Assert.IsTrue(hilbert.TryBuild(items));

        Assert.AreSequenceEqual(AscendingRegistrationCluster,
            Collect(hilbert, PackedBoxIndex.QueryMode.Intersecting, new BoundingBox(-1, -1, 1, 1)),
            "Exact key ties must close in ascending registration order.");
    }

    /// <summary>Permuting the item sequence maps the answered sets through the permutation and changes nothing else.</summary>
    [TestMethod]
    public void PermutedInputAnswersTheSameSetsThroughThePermutation()
    {
        //Permuting the item sequence permutes registration identities; the answered sets map
        //through the permutation and are otherwise unchanged.
        BoundingBox[] items = ClusteredBoxes(120, startState: 77_0803UL);
        int[] permutation = [.. Enumerable.Range(0, items.Length)];
        ulong state = 99_0803UL;

        for(int position = permutation.Length - 1; position > 0; position--)
        {
            int swap = DeterministicBitMixer.NextBelow(ref state, position + 1);
            (permutation[position], permutation[swap]) = (permutation[swap], permutation[position]);
        }

        BoundingBox[] permuted = [.. permutation.Select(source => items[source])];
        BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

        using PackedBoxIndex original = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));
        using PackedBoxIndex shuffled = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

        Assert.IsTrue(original.TryBuild(items));
        Assert.IsTrue(shuffled.TryBuild(permuted));

        for(int mode = 0; mode < 3; mode++)
        {
            foreach(BoundingBox probe in probes)
            {
                //Map the shuffled answers back to original registrations before comparing sets.
                List<int> fromOriginal = Collect(original, (PackedBoxIndex.QueryMode)mode, probe);
                List<int> fromShuffled = [.. Collect(shuffled, (PackedBoxIndex.QueryMode)mode, probe).Select(shuffledIndex => permutation[shuffledIndex])];
                fromOriginal.Sort();
                fromShuffled.Sort();

                Assert.AreSequenceEqual(fromOriginal, fromShuffled, "Sets must be equal through the permutation map.");
            }
        }
    }

    /// <summary>A malformed item refuses the build wherever it sits in the sequence.</summary>
    /// <param name="minX">The malformed item's minimum x.</param>
    /// <param name="minY">The malformed item's minimum y.</param>
    /// <param name="maxX">The malformed item's maximum x.</param>
    /// <param name="maxY">The malformed item's maximum y.</param>
    [TestMethod]
    [DataRow(double.NaN, 0d, 10d, 10d, DisplayName = "NaN MinX refuses the build")]
    [DataRow(0d, double.NaN, 10d, 10d, DisplayName = "NaN MinY refuses the build")]
    [DataRow(0d, 0d, double.NaN, 10d, DisplayName = "NaN MaxX refuses the build")]
    [DataRow(0d, 0d, 10d, double.NaN, DisplayName = "NaN MaxY refuses the build")]
    [DataRow(double.NegativeInfinity, 0d, 10d, 10d, DisplayName = "negative infinity refuses the build")]
    [DataRow(0d, 0d, double.PositiveInfinity, 10d, DisplayName = "positive infinity refuses the build")]
    [DataRow(10d, 0d, 5d, 10d, DisplayName = "an inverted X axis refuses the build")]
    [DataRow(0d, 10d, 10d, 5d, DisplayName = "an inverted Y axis refuses the build")]
    public void MalformedItemsRefuseAtEveryPosition(double minX, double minY, double maxX, double maxY)
    {
        var malformed = new BoundingBox(minX, minY, maxX, maxY);

        foreach(int position in new[] { 0, 5, 10 })
        {
            BoundingBox[] items = DisjointGrid(11);
            items[position] = malformed;

            using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

            Assert.IsFalse(index.TryBuild(items), $"A malformed item at position {position} must refuse the build.");
            Assert.AreEqual(0, index.Count, "Refusal leaves the index empty.");
            Assert.HasCount(0, Collect(index, PackedBoxIndex.QueryMode.Intersecting, new BoundingBox(-1e9, -1e9, 1e9, 1e9)),
                "A refused index enumerates nothing.");
        }
    }

    /// <summary>Refusal discards the prior working set and a later rebuild recovers fully.</summary>
    [TestMethod]
    public void RefusalIsDestructiveAndRebuildRecovers()
    {
        //A successful build followed by a refused one must not leave the old answers
        //reachable: a false return that silently retained the prior working set would let a
        //caller read stale candidates as if they were current. Rebuilding afterwards is legal
        //and fully working.
        BoundingBox[] first = DisjointGrid(20);
        BoundingBox[] malformed = DisjointGrid(5);
        malformed[2] = new BoundingBox(double.NaN, 0, 1, 1);
        BoundingBox[] third = ClusteredBoxes(30, startState: 31_0803UL);

        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(first));
        Assert.AreEqual(20, index.Count);

        Assert.IsFalse(index.TryBuild(malformed));
        Assert.AreEqual(0, index.Count);

        var everything = new BoundingBox(-1e9, -1e9, 1e9, 1e9);

        for(int mode = 0; mode < 3; mode++)
        {
            Assert.HasCount(0, Collect(index, (PackedBoxIndex.QueryMode)mode, everything),
                "No item of the refused-over build may ever enumerate.");
        }

        Assert.IsTrue(index.TryBuild(third), "Rebuild after refusal must succeed.");

        List<int> expected = BruteForce(third, PackedBoxIndex.QueryMode.Intersecting, everything);
        List<int> actual = Collect(index, PackedBoxIndex.QueryMode.Intersecting, everything);
        actual.Sort();

        Assert.AreSequenceEqual(expected, actual, "The rebuild must answer the parity oracle over the new items.");
    }

    /// <summary>A rebuild overwrites the prior working set rather than merging with it.</summary>
    [TestMethod]
    public void RebuildOverwritesThePriorWorkingSetEntirely()
    {
        //Build a larger set, then a smaller one somewhere else: a query over the old region
        //must find nothing — the catcher for a rebuild that silently merges instead of
        //overwriting.
        BoundingBox[] larger = DisjointGrid(50);
        var smaller = new BoundingBox[3];

        for(int index = 0; index < smaller.Length; index++)
        {
            double offset = 1e6 + (index * 10d);
            smaller[index] = new BoundingBox(offset, offset, offset + 5, offset + 5);
        }

        using PackedBoxIndex packedIndex = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(packedIndex.TryBuild(larger));
        Assert.IsTrue(packedIndex.TryBuild(smaller));
        Assert.AreEqual(3, packedIndex.Count);

        Assert.HasCount(0, Collect(packedIndex, PackedBoxIndex.QueryMode.Intersecting, new BoundingBox(-10, -10, 1000, 1000)),
            "No item of the overwritten build may enumerate.");
        Assert.HasCount(3, Collect(packedIndex, PackedBoxIndex.QueryMode.Intersecting, new BoundingBox(1e6 - 1, 1e6 - 1, 1e6 + 30, 1e6 + 30)));
    }

    /// <summary>A malformed query box enumerates nothing in every mode.</summary>
    [TestMethod]
    public void MalformedQueriesEnumerateNothingInEveryMode()
    {
        BoundingBox[] items = DisjointGrid(25);

        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(items));

        BoundingBox[] malformedQueries =
        [
            new BoundingBox(double.NaN, 0, 10, 10),
            new BoundingBox(0, double.NaN, 10, 10),
            new BoundingBox(0, 0, double.PositiveInfinity, 10),
            new BoundingBox(double.NegativeInfinity, 0, 10, 10),
            new BoundingBox(10, 0, 5, 10),
            new BoundingBox(0, 10, 10, 5)
        ];

        foreach(BoundingBox query in malformedQueries)
        {
            for(int mode = 0; mode < 3; mode++)
            {
                Assert.HasCount(0, Collect(index, (PackedBoxIndex.QueryMode)mode, query),
                    $"A malformed query must enumerate nothing in mode {(PackedBoxIndex.QueryMode)mode}.");
            }
        }
    }

    /// <summary>Queries over rootless indexes enumerate nothing without renting.</summary>
    [TestMethod]
    public void QueriesOnEmptyAndNeverBuiltIndexesEnumerateNothing()
    {
        using PackedBoxIndex neverBuilt = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);
        using PackedBoxIndex emptyBuilt = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(emptyBuilt.TryBuild(ReadOnlySpan<BoundingBox>.Empty));

        var probe = new BoundingBox(0, 0, 100, 100);

        for(int mode = 0; mode < 3; mode++)
        {
            Assert.HasCount(0, Collect(neverBuilt, (PackedBoxIndex.QueryMode)mode, probe));
            Assert.HasCount(0, Collect(emptyBuilt, (PackedBoxIndex.QueryMode)mode, probe));
        }
    }

    /// <summary>A node capacity outside the sanctioned range throws at creation.</summary>
    /// <param name="capacity">The rejected capacity.</param>
    [TestMethod]
    [DataRow(-1, DisplayName = "a negative capacity is rejected")]
    [DataRow(0, DisplayName = "zero capacity — the record-struct default trap — is rejected")]
    [DataRow(1, DisplayName = "capacity one cannot form a tree and is rejected")]
    [DataRow(65537, DisplayName = "capacity above the sanctioned range is rejected")]
    public void OutOfRangeNodeCapacityThrows(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, capacity)).Dispose());
    }

    /// <summary>An undefined packing and the record-struct default both throw at creation.</summary>
    [TestMethod]
    public void UndefinedPackingAndDefaultOptionsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PackedBoxIndex.Create(new PackedBoxIndexOptions((BoxIndexPacking)7, 16)).Dispose());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PackedBoxIndex.Create(default).Dispose());
    }

    /// <summary>The sanctioned default carries the measured configuration.</summary>
    [TestMethod]
    public void DefaultOptionsCarryTheShippedConfiguration()
    {
        //The sole reader of the sanctioned default — the measured pick: the best query
        //aggregate over the primary dataset-scale cells, ties broken by build cost.
        Assert.AreEqual(BoxIndexPacking.HilbertCurve, PackedBoxIndexOptions.Default.Packing);
        Assert.AreEqual(32, PackedBoxIndexOptions.Default.NodeCapacity);
    }

    /// <summary>Rebuilding to a larger and a smaller working set keeps full traversals correct.</summary>
    [TestMethod]
    public void GrowAndShrinkRebuildsAnswerFullTraversalsCorrectly()
    {
        //The rebuild must re-derive every build-scoped bound: after a grow, a stale cached
        //traversal bound would under-provision the exact-size stack rental and a deep query
        //would overrun it. Full-extent traversals after each rebuild exercise the deepest
        //stack the new tree can demand.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 2));

        var everything = new BoundingBox(double.MinValue / 4, double.MinValue / 4, double.MaxValue / 4, double.MaxValue / 4);

        foreach(int size in new[] { 500, 50, 800 })
        {
            BoundingBox[] items = ClusteredBoxes(size, startState: 55_000UL + (ulong)size);

            Assert.IsTrue(index.TryBuild(items));

            List<int> expected = BruteForce(items, PackedBoxIndex.QueryMode.Intersecting, everything);
            List<int> actual = Collect(index, PackedBoxIndex.QueryMode.Intersecting, everything);
            actual.Sort();

            Assert.AreSequenceEqual(expected, actual, $"The full traversal after rebuilding to {size} items diverged.");
        }
    }

    /// <summary>Every guarded member of a disposed index throws.</summary>
    [TestMethod]
    public void DisposedIndexThrowsFromEveryGuardedMember()
    {
        PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(DisjointGrid(5)));
        index.Dispose();
        index.Dispose();

        var probe = new BoundingBox(0, 0, 1, 1);

        Assert.Throws<ObjectDisposedException>(() => _ = index.Count);
        Assert.Throws<ObjectDisposedException>(() => _ = index.Options);
        Assert.Throws<ObjectDisposedException>(() => index.TryBuild(DisjointGrid(3)));
        Assert.Throws<ObjectDisposedException>(() => _ = index.Intersecting(in probe));
        Assert.Throws<ObjectDisposedException>(() => _ = index.ContainedIn(in probe));
        Assert.Throws<ObjectDisposedException>(() => _ = index.Containing(in probe));
        Assert.Throws<ObjectDisposedException>(() => _ = index.LevelCount);
        Assert.Throws<ObjectDisposedException>(() => _ = index.NodeCount);
    }

    /// <summary>A disposed enumerator throws from both of its readers.</summary>
    [TestMethod]
    public void DisposedEnumeratorThrowsFromMoveNextAndCurrent()
    {
        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(DisjointGrid(5)));

        var probe = new BoundingBox(-1e9, -1e9, 1e9, 1e9);
        PackedBoxIndex.Enumerator enumerator = index.Intersecting(in probe).GetEnumerator();

        Assert.IsTrue(enumerator.MoveNext());
        enumerator.Dispose();
        enumerator.Dispose();

        //Ref-struct enumerators cannot enter a lambda, so the throw assertions are manual.
        bool moveNextThrew = false;

        try
        {
            _ = enumerator.MoveNext();
        }
        catch(ObjectDisposedException)
        {
            moveNextThrew = true;
        }

        bool currentThrew = false;

        try
        {
            _ = enumerator.Current;
        }
        catch(ObjectDisposedException)
        {
            currentThrew = true;
        }

        Assert.IsTrue(moveNextThrew, "MoveNext after the enumerator's own dispose must throw.");
        Assert.IsTrue(currentThrew, "Current after the enumerator's own dispose must throw.");
    }

    /// <summary>A rebuild under a live enumerator makes that enumerator fail loud.</summary>
    [TestMethod]
    public void RebuildUnderALiveEnumeratorFailsLoudOnItsNextMoveNext()
    {
        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(DisjointGrid(40)));

        var probe = new BoundingBox(-1e9, -1e9, 1e9, 1e9);
        using PackedBoxIndex.Enumerator enumerator = index.Intersecting(in probe).GetEnumerator();

        Assert.IsTrue(enumerator.MoveNext());
        Assert.IsTrue(index.TryBuild(DisjointGrid(40)), "The rebuild itself is legal; only the stale enumerator must fail.");

        bool threw = false;

        try
        {
            _ = enumerator.MoveNext();
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A live enumerator over a rebuilt index reads stale views and must fail loud.");
    }

    /// <summary>Disposing under a live enumerator makes that enumerator fail loud.</summary>
    [TestMethod]
    public void DisposeUnderALiveEnumeratorFailsLoudOnItsNextMoveNext()
    {
        PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(DisjointGrid(40)));

        var probe = new BoundingBox(-1e9, -1e9, 1e9, 1e9);
        using PackedBoxIndex.Enumerator enumerator = index.Intersecting(in probe).GetEnumerator();

        Assert.IsTrue(enumerator.MoveNext());
        index.Dispose();

        bool threw = false;

        try
        {
            _ = enumerator.MoveNext();
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A live enumerator over a disposed index must fail loud, not read returned rentals.");
    }

    /// <summary>A rebuild under a live containing enumerator makes that enumerator fail loud.</summary>
    [TestMethod]
    public void RebuildUnderALiveContainingEnumeratorFailsLoudOnItsNextMoveNext()
    {
        //The containing route's enumerator is the eager variant: its matches are already
        //collected when GetEnumerator answers, but its emission still reads the pooled
        //child-start column, so the staleness guard must fail it as loudly as the lazy
        //modes — an eagerly-collected enumerator never outlives a rebuild.
        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(DisjointGrid(40)));

        BoundingBox probe = DisjointGrid(40)[3];
        using PackedBoxIndex.Enumerator enumerator = index.Containing(in probe).GetEnumerator();

        Assert.IsTrue(index.TryBuild(DisjointGrid(40)), "The rebuild itself is legal; only the stale enumerator must fail.");

        bool threw = false;

        try
        {
            _ = enumerator.MoveNext();
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A live containing enumerator over a rebuilt index reads stale views and must fail loud.");
    }

    /// <summary>Disposing under a live containing enumerator makes that enumerator fail loud.</summary>
    [TestMethod]
    public void DisposeUnderALiveContainingEnumeratorFailsLoudOnItsNextMoveNext()
    {
        PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(DisjointGrid(40)));

        BoundingBox probe = DisjointGrid(40)[3];
        using PackedBoxIndex.Enumerator enumerator = index.Containing(in probe).GetEnumerator();

        index.Dispose();

        bool threw = false;

        try
        {
            _ = enumerator.MoveNext();
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A live containing enumerator over a disposed index must fail loud, not read returned rentals.");
    }

    /// <summary>Every rental of both counted classes comes back across a mixed foreach and manual sweep.</summary>
    [TestMethod]
    public void StackRentalsBalanceAcrossAQuerySweep()
    {
        //Every rented traversal stack must come back: the foreach pattern disposes by the
        //language, the manual pattern by its using — the counters prove neither path leaks.
        //The manual half runs the containing route, so the sweep also gates the second
        //counted class, the collect buffer that rides each containing enumerator to its
        //own dispose.
        BoundingBox[] items = ClusteredBoxes(300, startState: 88_0803UL);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 4));

        Assert.IsTrue(index.TryBuild(items));

        BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

        foreach(BoundingBox probe in probes)
        {
            foreach(int candidate in index.Intersecting(in probe))
            {
                _ = candidate;
            }

            using PackedBoxIndex.Enumerator enumerator = index.Containing(in probe).GetEnumerator();

            while(enumerator.MoveNext())
            {
                _ = enumerator.Current;
            }
        }

        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned,
            "Every traversal-stack rental must be returned across the sweep.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned,
            "Every containing collect-buffer rental must be returned across the sweep.");
        Assert.IsGreaterThan(0L, index.StackRentalsIssued, "The sweep must actually have rented, or the balance proves nothing.");
        Assert.IsGreaterThan(0L, index.CollectRentalsIssued, "The sweep must actually have rented collect buffers, or the balance proves nothing.");
    }

    /// <summary>The golden fixture: fourteen scattered boxes over a 100×100 field, sizes and positions chosen so the two packings order the full traversal differently.</summary>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] GoldenFixture()
    {
        return
        [
            new BoundingBox(2, 3, 6, 7),
            new BoundingBox(90, 4, 95, 9),
            new BoundingBox(41, 42, 49, 48),
            new BoundingBox(5, 88, 12, 95),
            new BoundingBox(60, 60, 75, 75),
            new BoundingBox(30, 8, 34, 12),
            new BoundingBox(82, 78, 88, 84),
            new BoundingBox(14, 55, 20, 63),
            new BoundingBox(70, 22, 78, 28),
            new BoundingBox(48, 70, 55, 80),
            new BoundingBox(25, 30, 31, 36),
            new BoundingBox(8, 20, 12, 26),
            new BoundingBox(55, 5, 60, 11),
            new BoundingBox(92, 50, 98, 58)
        ];
    }

    /// <summary>A disjoint row of unit boxes spaced two apart: predictable structure, no overlap, every probe answer derivable.</summary>
    /// <param name="count">The number of boxes.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] DisjointGrid(int count)
    {
        var items = new BoundingBox[count];

        for(int index = 0; index < count; index++)
        {
            double offset = index * 2d;
            items[index] = new BoundingBox(offset, 0, offset + 1, 1);
        }

        return items;
    }

    /// <summary>Fixed-start clustered boxes: several dense clusters plus scattered singles, sizes varied, all well-formed.</summary>
    /// <param name="count">The number of boxes.</param>
    /// <param name="startState">The mixing start value that makes the fixture reproducible.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] ClusteredBoxes(int count, ulong startState)
    {
        ulong state = startState;
        var items = new BoundingBox[count];

        for(int index = 0; index < count; index++)
        {
            double clusterX = DeterministicBitMixer.NextBelow(ref state, 5) * 250d;
            double clusterY = DeterministicBitMixer.NextBelow(ref state, 5) * 250d;
            double x = clusterX + (DeterministicBitMixer.NextUnitDouble(ref state) * 50d);
            double y = clusterY + (DeterministicBitMixer.NextUnitDouble(ref state) * 50d);
            double width = DeterministicBitMixer.NextUnitDouble(ref state) * 8d;
            double height = DeterministicBitMixer.NextUnitDouble(ref state) * 8d;
            items[index] = new BoundingBox(x, y, x + width, y + height);
        }

        return items;
    }

    /// <summary>Enumerates one mode through the public foreach pattern into a list, order preserved.</summary>
    /// <param name="index">The index to query.</param>
    /// <param name="mode">The query mode.</param>
    /// <param name="query">The query box.</param>
    /// <returns>The answered registration indices in enumeration order.</returns>
    private static List<int> Collect(PackedBoxIndex index, PackedBoxIndex.QueryMode mode, BoundingBox query)
    {
        var results = new List<int>();
        PackedBoxIndex.Candidates candidates = mode switch
        {
            PackedBoxIndex.QueryMode.Intersecting => index.Intersecting(in query),
            PackedBoxIndex.QueryMode.ContainedIn => index.ContainedIn(in query),
            _ => index.Containing(in query),
        };

        foreach(int candidate in candidates)
        {
            results.Add(candidate);
        }

        return results;
    }

    /// <summary>All three modes over every probe, order preserved per enumeration.</summary>
    /// <param name="index">The index to query.</param>
    /// <param name="probes">The probe boxes.</param>
    /// <returns>The answers indexed by mode, then by probe.</returns>
    private static List<int>[][] CollectAllModes(PackedBoxIndex index, BoundingBox[] probes)
    {
        var observed = new List<int>[3][];

        for(int mode = 0; mode < 3; mode++)
        {
            observed[mode] = new List<int>[probes.Length];

            for(int probe = 0; probe < probes.Length; probe++)
            {
                observed[mode][probe] = Collect(index, (PackedBoxIndex.QueryMode)mode, probes[probe]);
            }
        }

        return observed;
    }

    /// <summary>The oracle: a scan with the public box algebra — the agreement gate between the predicate spelling and the index's column spelling.</summary>
    /// <param name="items">The registered items.</param>
    /// <param name="mode">The query mode.</param>
    /// <param name="query">The query box.</param>
    /// <returns>The ascending registration indices the mode's predicate affirms.</returns>
    private static List<int> BruteForce(ReadOnlySpan<BoundingBox> items, PackedBoxIndex.QueryMode mode, BoundingBox query)
    {
        var results = new List<int>();
        bool queryWellFormed =
            double.IsFinite(query.MinX) && double.IsFinite(query.MinY)
            && double.IsFinite(query.MaxX) && double.IsFinite(query.MaxY)
            && query.MinX <= query.MaxX && query.MinY <= query.MaxY;

        if(!queryWellFormed)
        {
            return results;
        }

        for(int index = 0; index < items.Length; index++)
        {
            bool matches = mode switch
            {
                PackedBoxIndex.QueryMode.Intersecting => query.Intersects(items[index]),
                PackedBoxIndex.QueryMode.ContainedIn => query.Contains(items[index]),
                _ => items[index].Contains(query),
            };

            if(matches)
            {
                results.Add(index);
            }
        }

        return results;
    }
}
