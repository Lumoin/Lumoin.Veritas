using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Reentrancy, concurrency, and traversal-cost gates for
/// <see cref="PackedBoxIndex"/>: nested and interleaved enumerations over one
/// index are legal because every enumerator owns its own pooled rentals; a
/// parallel query sweep answers exactly the serial answers (best-effort
/// evidence for the concurrent-reader contract, not a proof — the pool lock
/// serializes the rentals); and the containing mode's own embedded four-axis
/// dominance descent is pinned as an executable canary — on
/// archipelago-shaped data its node visits stay far below the dominance
/// tree's node total while its results stay exact, on the cross adversary
/// they stay under the half-space-only descent's measured curve, and the
/// standalone containment index answers the same sets on every named
/// fixture. Every reentrancy, concurrency, and rental row gates both counted
/// rental classes: the traversal stacks every query mode rents, and the
/// containing route's own collect buffer.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexConcurrencyTests
{
    /// <summary>The worker count of the parallel sweeps.</summary>
    private const int ParallelWorkerCount = 4;

    /// <summary>The archipelago's only containing answer for a gap probe: the lake's registration index.</summary>
    private static readonly int[] LakeOnly = [0];

    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>An inner full enumeration inside every step of an outer one stays exact and repeatable.</summary>
    [TestMethod]
    public void NestedEnumerationsOverOneIndexAreLegalAndExact()
    {
        //An inner full enumeration runs inside every step of an outer one; both must answer
        //their brute-force sets, and the inner answer must be identical on every repetition —
        //each enumerator owns its own stack, so the traversals cannot disturb one another.
        BoundingBox[] items = BuildArchipelago(islandsPerAxis: 5);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

        Assert.IsTrue(index.TryBuild(items));

        var outerProbe = new BoundingBox(-1, -1, 10_001, 10_001);
        var innerProbe = new BoundingBox(50, 50, 50, 50);
        var outerSeen = new List<int>();
        List<int>? firstInnerAnswer = null;

        foreach(int outer in index.Intersecting(in outerProbe))
        {
            outerSeen.Add(outer);

            var innerSeen = new List<int>();

            foreach(int inner in index.Containing(in innerProbe))
            {
                innerSeen.Add(inner);
            }

            if(firstInnerAnswer is null)
            {
                firstInnerAnswer = innerSeen;
            }
            else
            {
                Assert.AreSequenceEqual(firstInnerAnswer, innerSeen, "The nested enumeration must answer identically on every repetition.");
            }
        }

        outerSeen.Sort();
        Assert.AreSequenceEqual(Enumerable.Range(0, items.Length).ToArray(), outerSeen, "The outer enumeration must cover every item.");
        Assert.IsNotNull(firstInnerAnswer);
        Assert.AreSequenceEqual(LakeOnly, firstInnerAnswer, "Only the lake contains the gap probe.");
    }

    /// <summary>Two manual enumerators advanced in strict alternation keep independent traversal state.</summary>
    [TestMethod]
    public void InterleavedManualEnumeratorsProgressIndependently()
    {
        BoundingBox[] items = BuildArchipelago(islandsPerAxis: 5);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 4));

        Assert.IsTrue(index.TryBuild(items));

        var everything = new BoundingBox(-1, -1, 10_001, 10_001);
        var islandRegion = new BoundingBox(0, 0, 250, 250);

        using PackedBoxIndex.Enumerator first = index.Intersecting(in everything).GetEnumerator();
        using PackedBoxIndex.Enumerator second = index.ContainedIn(in islandRegion).GetEnumerator();

        var firstSeen = new List<int>();
        var secondSeen = new List<int>();
        bool firstAlive = true;
        bool secondAlive = true;

        //Strict alternation: each MoveNext interleaves with the other enumerator's, so any
        //shared traversal state would corrupt one of the two sequences.
        while(firstAlive || secondAlive)
        {
            if(firstAlive && (firstAlive = first.MoveNext()))
            {
                firstSeen.Add(first.Current);
            }

            if(secondAlive && (secondAlive = second.MoveNext()))
            {
                secondSeen.Add(second.Current);
            }
        }

        firstSeen.Sort();
        secondSeen.Sort();

        Assert.AreSequenceEqual(Enumerable.Range(0, items.Length).ToArray(), firstSeen, "The intersecting sweep must cover every item.");

        var expectedContained = new List<int>();

        for(int registration = 0; registration < items.Length; registration++)
        {
            if(islandRegion.Contains(items[registration]))
            {
                expectedContained.Add(registration);
            }
        }

        Assert.AreSequenceEqual(expectedContained, secondSeen, "The contained-in sweep must answer its brute-force set.");
    }

    /// <summary>Four workers over ten thousand queries reproduce the serial answers exactly, both rental classes balanced.</summary>
    /// <returns>The task the sweep completes on.</returns>
    [TestMethod]
    public async Task ParallelQueriesEqualTheSerialAnswers()
    {
        //Best-effort evidence for the concurrent-reader contract, not a proof: after a build
        //the columns are read-only and each enumerator owns its rentals, so four workers over
        //ten thousand queries must reproduce the serial answers exactly. The pool lock
        //serializes the rentals themselves. Workers write into disjoint answer slots and every
        //comparison runs after the join, so the row's verdict never depends on scheduling.
        ulong state = 31_08_2026UL;
        var items = new BoundingBox[20_000];

        for(int registration = 0; registration < items.Length; registration++)
        {
            double x = DeterministicBitMixer.NextUnitDouble(ref state) * 10_000d;
            double y = DeterministicBitMixer.NextUnitDouble(ref state) * 10_000d;
            items[registration] = new BoundingBox(
                x,
                y,
                x + (DeterministicBitMixer.NextUnitDouble(ref state) * 20d),
                y + (DeterministicBitMixer.NextUnitDouble(ref state) * 20d));
        }

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items));

        var probes = new BoundingBox[10_000];

        for(int probe = 0; probe < probes.Length; probe++)
        {
            double x = DeterministicBitMixer.NextUnitDouble(ref state) * 10_000d;
            double y = DeterministicBitMixer.NextUnitDouble(ref state) * 10_000d;
            probes[probe] = new BoundingBox(x, y, x + 40d, y + 40d);
        }

        var serialAnswers = new int[probes.Length][];

        for(int probe = 0; probe < probes.Length; probe++)
        {
            serialAnswers[probe] = CollectMode(index, probe % 3, probes[probe]);
        }

        var parallelAnswers = new int[probes.Length][];
        var workers = new Task[ParallelWorkerCount];

        for(int worker = 0; worker < ParallelWorkerCount; worker++)
        {
            workers[worker] = Task.Factory.StartNew(
                RunProbeSlice,
                new ProbeSlice(index, probes, parallelAnswers, worker, ParallelWorkerCount),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        for(int probe = 0; probe < probes.Length; probe++)
        {
            Assert.AreSequenceEqual(serialAnswers[probe], parallelAnswers[probe],
                $"Probe {probe} answered differently under parallel enumeration.");
        }

        //Every third probe runs the containing route, so the sweep exercises both counted
        //rental classes: the traversal stacks every mode rents and the containing route's
        //collect buffer.
        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned,
            "Every traversal-stack rental must balance across the serial and parallel sweeps combined.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned,
            "Every collect-buffer rental must balance across the serial and parallel sweeps combined.");
    }

    /// <summary>The containing route stays exact on the archipelago while the intersecting descent keeps its pruning ceiling.</summary>
    [TestMethod]
    public void ContainingStaysExactOnTheArchipelagoAndIntersectingKeepsItsPruningCeiling()
    {
        //The containment canary, pinned to what each descent owns on its own terms: the
        //containing mode's embedded dominance descent stays exact on the archipelago shape,
        //and the intersecting mode's union-bound descent keeps pruning the gap probe to a
        //handful of spines. The two modes walk different structures, so each is gated on its
        //own observables — the dominance visit ceilings ride their own row.
        BoundingBox[] items = BuildArchipelago(islandsPerAxis: 100);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items));

        BoundingBox[] probes =
        [
            new BoundingBox(50, 50, 50, 50),
            new BoundingBox(40, 40, 460, 460),
            new BoundingBox(0, 0, 5_000, 5_000),
            items[1]
        ];

        foreach(BoundingBox probe in probes)
        {
            using PackedBoxIndex.Enumerator containing = index.Containing(in probe).GetEnumerator();
            var containingSeen = new List<int>();

            while(containing.MoveNext())
            {
                containingSeen.Add(containing.Current);
            }

            var expectedContaining = new List<int>();

            for(int registration = 0; registration < items.Length; registration++)
            {
                if(items[registration].Contains(probe))
                {
                    expectedContaining.Add(registration);
                }
            }

            containingSeen.Sort();
            Assert.AreSequenceEqual(expectedContaining, containingSeen, "Containing results stay exact on the archipelago.");
        }

        //The absolute pruning ceiling: ten thousand and one items at capacity sixteen build
        //626 + 40 + 3 + 1 = 670 node slots. A gap point probe survives only in unions that
        //cover its point — at most a handful per level where neighbouring unions overlap,
        //plus the lake's spine — so thirty-two visits is a generous bound that still sits
        //twenty-fold below the node total: an unpruned descent reddens this row immediately.
        var gapProbe = new BoundingBox(50, 50, 50, 50);

        using PackedBoxIndex.Enumerator gapIntersecting = index.Intersecting(in gapProbe).GetEnumerator();

        while(gapIntersecting.MoveNext())
        {
        }

        Assert.IsLessThan(32, gapIntersecting.VisitedNodeCount,
            "The intersecting descent must prune the gap probe to a few spines, far below the 670-node total.");
    }

    /// <summary>Every named archipelago probe's dominance visits stay under the derived absolute ceiling.</summary>
    [TestMethod]
    public void ContainingDominanceVisitsStayWithinAbsoluteCeilings()
    {
        //Visits are deterministic per (configuration, dataset, probe), so this is a stable
        //gate, not a benchmark. The ceiling derives from the composed descent's visit
        //model, not from the measurements: a query keeps two live spines — its own cone
        //and the full-field lake's ancestor chain — of roughly the count-halving tree
        //depth each (about twelve levels at ten thousand items), every surviving internal
        //node pushes one child unconditionally so an at-pop pruned frontier of the same
        //order rides along, and deep slab widths allow a small boundary multi-path
        //allowance: a central estimate of some forty to seventy visits, the ceiling at
        //roughly double the top of that band. Measured here the probes run eleven to
        //twenty-nine net of results; an unpruned descent visits all 4095 nodes and reddens
        //every row at once.
        BoundingBox[] items = BuildArchipelago(islandsPerAxis: 100);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items));
        Assert.AreEqual(4095, index.DominanceNodeCount,
            "The dominance tree's count-halving structure over ten thousand and one items must land on this node count.");

        (string Name, BoundingBox Probe)[] probes =
        [
            ("point probe at (50,50,50,50) — measured 30 visits for 1 result", new BoundingBox(50, 50, 50, 50)),
            ("wide probe at (40,40,460,460) — measured 27 visits for 1 result", new BoundingBox(40, 40, 460, 460)),
            ("half-field probe at (0,0,5000,5000) — measured 12 visits for 1 result", new BoundingBox(0, 0, 5_000, 5_000)),
            ("an island's own box — measured 30 visits for 2 results", items[1]),
            ("corner point probe at (9999,9999,9999,9999) — measured 18 visits for 1 result", new BoundingBox(9_999, 9_999, 9_999, 9_999)),
            ("three-percent probe at (300,300,600,600) — measured 25 visits for 1 result", new BoundingBox(300, 300, 600, 600)),
            ("fourteen-percent probe at (1000,1000,2400,2400) — measured 21 visits for 1 result", new BoundingBox(1_000, 1_000, 2_400, 2_400))
        ];

        foreach((string name, BoundingBox probe) in probes)
        {
            using PackedBoxIndex.Enumerator containing = index.Containing(in probe).GetEnumerator();
            int matchCount = 0;

            while(containing.MoveNext())
            {
                matchCount++;
            }

            int visits = containing.VisitedNodeCount;

            Assert.IsLessThanOrEqualTo(128, visits - matchCount,
                $"Probe '{name}' must stay within the dominance-visit ceiling net of its own results.");
        }
    }

    /// <summary>On the cross adversary the composed descent stays under the half-space-only descent's measured off-arm curve.</summary>
    [TestMethod]
    public void CrossOffArmContainingVisitsStayUnderTheHalfSpaceDescentCurve()
    {
        //The cross adversary: interleaved full-height and full-width slats whose every
        //packing key ties and whose child-run unions are the full field. The ceilings are
        //the half-space-only dominance descent's measured off-arm visit curve at these
        //sizes — the union rule tested at pop can only remove pops from that descent, so
        //its curve is a hard per-query ceiling — while the composed descent measures about
        //three net visits per probe here: an interstice probe fails a subtree union within
        //a handful of compares. The gate therefore also documents the recovery the
        //composition buys on the shape that defeats either prune rule alone.
        (int SlatCount, int Ceiling)[] tiers = [(1_000, 108), (10_000, 876)];

        foreach((int slatCount, int ceiling) in tiers)
        {
            var slats = new BoundingBox[slatCount];
            CrossSlatFixture.WriteSlats(slats, fieldExtent: 1_000d, thickness: 2d);

            var offArmProbes = new BoundingBox[48];
            CrossSlatFixture.WriteOffArmProbes(offArmProbes, 1_000d);

            foreach(BoxIndexPacking packing in Packings)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

                Assert.IsTrue(index.TryBuild(slats), $"The {slatCount}-slat cross fixture must build under {packing}.");

                foreach(BoundingBox probe in offArmProbes)
                {
                    using PackedBoxIndex.Enumerator containing = index.Containing(in probe).GetEnumerator();
                    int matchCount = 0;

                    while(containing.MoveNext())
                    {
                        matchCount++;
                    }

                    Assert.IsLessThanOrEqualTo(ceiling, containing.VisitedNodeCount - matchCount,
                        $"An off-arm probe at {slatCount} slats under {packing} must stay under the half-space-only descent's measured curve.");
                }
            }
        }
    }

    /// <summary>All three containment oracles answer identical sets on every named fixture.</summary>
    [TestMethod]
    public void ContainmentAnswersAgreeWithTheDominanceContainmentIndex()
    {
        //Both containment oracles exercise every named shape, the cross adversary included:
        //the packed index's embedded dominance descent, the standalone k-d dominance tree,
        //and a brute scan over the public box algebra must answer identical sets for every
        //probe of every named fixture.
        foreach((string name, BoundingBox[] items) in PackedBoxIndexFixtureFamily.NamedFixtures())
        {
            using PackedBoxIndex packed = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));
            using BoxContainmentIndex dominance = BoxContainmentIndex.Create();

            Assert.IsTrue(packed.TryBuild(items), $"Fixture '{name}' must build.");
            dominance.Build(items);

            BoundingBox[] probes = PackedBoxIndexFixtureFamily.ProbesFor(items);

            foreach(BoundingBox probe in probes)
            {
                var fromPacked = new List<int>();

                foreach(int candidate in packed.Containing(in probe))
                {
                    fromPacked.Add(candidate);
                }

                var fromDominance = new List<int>();

                foreach(int candidate in dominance.Containers(in probe))
                {
                    fromDominance.Add(candidate);
                }

                var fromBruteForce = new List<int>();

                for(int registration = 0; registration < items.Length; registration++)
                {
                    if(items[registration].Contains(probe))
                    {
                        fromBruteForce.Add(registration);
                    }
                }

                fromPacked.Sort();
                fromDominance.Sort();
                fromBruteForce.Sort();

                Assert.AreSequenceEqual(fromBruteForce, fromPacked, $"Fixture '{name}': the packed index's containing set must equal the brute-force set.");
                Assert.AreSequenceEqual(fromBruteForce, fromDominance, $"Fixture '{name}': the standalone containment index's set must equal the brute-force set.");
            }
        }
    }

    /// <summary>Nested containing enumerations stay exact and leak neither rental class.</summary>
    [TestMethod]
    public void NestedContainingEnumerationsCarryBothRentalClasses()
    {
        //The dominance descent's stack returns inside GetEnumerator, before the outer
        //manual enumerator's first MoveNext; the containing route's collect buffer instead
        //rides each enumerator until its own dispose — so an outer manual Containing
        //enumeration and an inner full Containing enumeration, run to exhaustion inside
        //every outer step, must neither disturb one another nor leak either rental class.
        BoundingBox[] items = PackedBoxIndexFixtureFamily.ExtensionFixture();

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

        Assert.IsTrue(index.TryBuild(items));

        var outerProbe = new BoundingBox(50, 50, 50, 50);
        var innerProbe = new BoundingBox(49, 49, 51, 51);
        List<int>? firstInnerAnswer = null;

        //The outer enumerator lives in its own scope: its collect buffer returns on its
        //dispose, which must happen before the balance below is read.
        using(PackedBoxIndex.Enumerator outer = index.Containing(in outerProbe).GetEnumerator())
        {
            while(outer.MoveNext())
            {
                var innerSeen = new List<int>();

                foreach(int inner in index.Containing(in innerProbe))
                {
                    innerSeen.Add(inner);
                }

                if(firstInnerAnswer is null)
                {
                    firstInnerAnswer = innerSeen;
                }
                else
                {
                    Assert.AreSequenceEqual(firstInnerAnswer, innerSeen, "The inner containing enumeration must answer identically on every repetition.");
                }
            }
        }

        Assert.IsNotNull(firstInnerAnswer, "The outer probe must contain at least one item for the nested sweep to run.");

        //Two rental classes, two return points: the dominance descent's stack returns
        //inside GetEnumerator before any match is emitted, while the collect buffer of
        //rank-sorted matches rides each enumerator until its own dispose.
        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned,
            "The traversal-stack rental class must balance across the nested sweep.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned,
            "The containing collect-buffer rental class must balance across the nested sweep.");
        Assert.IsGreaterThan(0L, index.StackRentalsIssued, "The sweep must actually have rented traversal stacks, or the balance proves nothing.");
        Assert.IsGreaterThan(0L, index.CollectRentalsIssued, "The sweep must actually have rented collect buffers, or the balance proves nothing.");
    }

    /// <summary>Four workers running the containing route reproduce the serial answers and balance both rental classes.</summary>
    /// <returns>The task the sweep completes on.</returns>
    [TestMethod]
    public async Task ConcurrentContainingQueriesBalanceBothRentalClasses()
    {
        //Best-effort evidence for the concurrent-reader contract over the containing route
        //specifically: four workers each running the eager dominance descent concurrently
        //must reproduce the serial answers exactly, and both counted rental classes must
        //balance once every worker has returned its rentals. Workers write into disjoint
        //answer slots and every comparison runs after the join, so the row's verdict never
        //depends on scheduling.
        BoundingBox[] items = BuildArchipelago(islandsPerAxis: 30);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items));

        var probes = new BoundingBox[2_000];

        for(int probe = 0; probe < probes.Length; probe++)
        {
            double x = ((probe % 100) * 100d) + 50d;
            double y = (((probe / 100) % 100) * 100d) + 50d;
            probes[probe] = new BoundingBox(x, y, x, y);
        }

        var serialAnswers = new int[100][];

        for(int probe = 0; probe < serialAnswers.Length; probe++)
        {
            serialAnswers[probe] = CollectMode(index, 2, probes[probe]);
        }

        var parallelAnswers = new int[probes.Length][];
        var workers = new Task[ParallelWorkerCount];

        for(int worker = 0; worker < ParallelWorkerCount; worker++)
        {
            workers[worker] = Task.Factory.StartNew(
                RunContainingProbeSlice,
                new ProbeSlice(index, probes, parallelAnswers, worker, ParallelWorkerCount),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        for(int probe = 0; probe < serialAnswers.Length; probe++)
        {
            Assert.AreSequenceEqual(serialAnswers[probe], parallelAnswers[probe],
                $"Probe {probe} answered differently under parallel containing enumeration.");
        }

        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned,
            "The traversal-stack rental class must balance across the concurrent sweep.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned,
            "The containing collect-buffer rental class must balance across the concurrent sweep.");
        Assert.IsGreaterThan(0L, index.StackRentalsIssued, "The sweep must actually have rented traversal stacks, or the balance proves nothing.");
        Assert.IsGreaterThan(0L, index.CollectRentalsIssued, "The sweep must actually have rented collect buffers, or the balance proves nothing.");
    }

    /// <summary>
    /// The archipelago: one lake spanning the whole field, then a square grid
    /// of small islands spaced far apart — the shape whose wide union defeats
    /// union-bound containment pruning.
    /// </summary>
    /// <param name="islandsPerAxis">The island count on each axis.</param>
    /// <returns>The fixture items, the lake first.</returns>
    private static BoundingBox[] BuildArchipelago(int islandsPerAxis)
    {
        var items = new BoundingBox[(islandsPerAxis * islandsPerAxis) + 1];
        items[0] = new BoundingBox(0, 0, 10_000, 10_000);

        for(int island = 0; island < islandsPerAxis * islandsPerAxis; island++)
        {
            double x = (island % islandsPerAxis) * (10_000d / islandsPerAxis);
            double y = (island / islandsPerAxis) * (10_000d / islandsPerAxis);
            items[island + 1] = new BoundingBox(x, y, x + 8, y + 8);
        }

        return items;
    }

    /// <summary>Enumerates one mode (0 intersecting, 1 contained-in, 2 containing) through the foreach pattern.</summary>
    /// <param name="index">The index to query.</param>
    /// <param name="mode">The query mode as an ordinal.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The answered registration indices in enumeration order.</returns>
    private static int[] CollectMode(PackedBoxIndex index, int mode, BoundingBox probe)
    {
        var results = new List<int>();
        PackedBoxIndex.Candidates candidates = mode switch
        {
            0 => index.Intersecting(in probe),
            1 => index.ContainedIn(in probe),
            _ => index.Containing(in probe)
        };

        foreach(int candidate in candidates)
        {
            results.Add(candidate);
        }

        return [.. results];
    }

    /// <summary>One worker's strided share of the mode-cycling sweep; bound as a static callback so nothing closes over the test body.</summary>
    /// <param name="state">The worker's <see cref="ProbeSlice"/>.</param>
    private static void RunProbeSlice(object? state)
    {
        var slice = (ProbeSlice)state!;

        for(int probe = slice.First; probe < slice.Probes.Length; probe += slice.Stride)
        {
            slice.Answers[probe] = CollectMode(slice.Index, probe % 3, slice.Probes[probe]);
        }
    }

    /// <summary>One worker's strided share of the containing-only sweep; bound as a static callback for the same reason.</summary>
    /// <param name="state">The worker's <see cref="ProbeSlice"/>.</param>
    private static void RunContainingProbeSlice(object? state)
    {
        var slice = (ProbeSlice)state!;

        for(int probe = slice.First; probe < slice.Probes.Length; probe += slice.Stride)
        {
            slice.Answers[probe] = CollectMode(slice.Index, 2, slice.Probes[probe]);
        }
    }

    /// <summary>
    /// The explicit state one parallel worker receives: the shared index and
    /// probe set, the shared answer array, and the strided slot range this
    /// worker alone writes — disjoint slots, so the sweep needs no
    /// synchronisation and no assertion runs off the test thread.
    /// </summary>
    private sealed class ProbeSlice
    {
        /// <summary>The index every worker queries concurrently.</summary>
        public PackedBoxIndex Index { get; }

        /// <summary>The shared probe set.</summary>
        public BoundingBox[] Probes { get; }

        /// <summary>The shared answer array; each worker writes only its own slots.</summary>
        public int[][] Answers { get; }

        /// <summary>This worker's first probe index.</summary>
        public int First { get; }

        /// <summary>The stride between this worker's probe indices.</summary>
        public int Stride { get; }

        /// <summary>Captures the worker's share of the sweep.</summary>
        /// <param name="index">The index every worker queries concurrently.</param>
        /// <param name="probes">The shared probe set.</param>
        /// <param name="answers">The shared answer array.</param>
        /// <param name="first">This worker's first probe index.</param>
        /// <param name="stride">The stride between this worker's probe indices.</param>
        public ProbeSlice(PackedBoxIndex index, BoundingBox[] probes, int[][] answers, int first, int stride)
        {
            Index = index;
            Probes = probes;
            Answers = answers;
            First = first;
            Stride = stride;
        }
    }
}
