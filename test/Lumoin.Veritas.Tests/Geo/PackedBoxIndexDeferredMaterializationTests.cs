using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The deferred-materialization canon for <see cref="PackedBoxIndex"/>'s
/// embedded dominance structure: a successful non-empty build leaves the
/// structure pending, the containing route's first use of the epoch — or any
/// forcing diagnostic accessor — runs the pass exactly once, and the
/// non-forcing pins make the whole lifecycle observable. The suite covers the
/// deferral and forcing pins under both packings, trigger-order identity
/// between the query-first and accessor-first routes, the rebuild, refusal,
/// empty, and never-built epoch transitions, disposal of a pending epoch,
/// cold-start concurrent first callers, a live enumerator crossing the
/// materialization, the warm-epoch allocation pin, and the staleness guard
/// across materialization epochs, plus the carriage-option rows: the
/// <see cref="DominanceMaterializationMode.EagerAtBuild"/> option drives the
/// identical gate at the build tail, exactly once per epoch, emitting the
/// identical structure and sequences; the defaults all carry the deferred
/// carriage; an undefined mode is refused at creation. The dominance tree's
/// own structural canon lives in <see cref="PackedBoxIndexDominanceTests"/>.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexDeferredMaterializationTests
{
    /// <summary>The worker count of the cold-start materialization race.</summary>
    private const int ColdStartWorkerCount = 8;

    /// <summary>The freshly built epochs the cold-start race runs against, one race each.</summary>
    private const int ColdStartEpochCount = 5;

    /// <summary>The worker count of the held-enumerator materialization race.</summary>
    private const int HeldEnumeratorWorkerCount = 4;

    /// <summary>The steps every held enumerator advances before the rendezvous; the overlap-rich probe has more hits than this.</summary>
    private const int PreRendezvousSteps = 5;

    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>A non-empty build stays pending until the first containing use, which materializes it exactly once.</summary>
    [TestMethod]
    public void BuildDefersDominanceMaterializationUntilTheFirstContainingUse()
    {
        //The deferral pin under both packings: a successful non-empty build leaves the
        //dominance structure pending with the completion count untouched, the first
        //containing enumeration materializes it exactly once while answering the
        //brute-force set, and neither a second query nor a forcing accessor runs the pass
        //again within the epoch.
        BoundingBox[] items = OverlappingGrid(20, 10);
        var probe = new BoundingBox(52, 33, 52, 33);

        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

            long baseline = index.DominanceMaterializationCount;

            Assert.IsTrue(index.TryBuild(items), $"The overlapping grid must build under {packing}.");
            Assert.IsFalse(index.DominanceMaterialized, $"A non-empty build must leave the dominance structure pending under {packing}.");
            Assert.AreEqual(baseline, index.DominanceMaterializationCount, $"The build itself must not run the deferred pass under {packing}.");

            List<int> firstSeen = CollectContaining(index, probe);
            firstSeen.Sort();

            Assert.AreSequenceEqual(BruteForceContaining(items, probe), firstSeen, $"The first containing enumeration must answer the brute-force set under {packing}.");
            Assert.IsTrue(index.DominanceMaterialized, $"The first containing enumeration must materialize the structure under {packing}.");
            Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, $"Exactly one materialization must have completed under {packing}.");

            //A second query and a forcing accessor both ride the already-materialized
            //epoch: the completion count must not move again.
            List<int> secondSeen = CollectContaining(index, probe);
            secondSeen.Sort();

            Assert.AreSequenceEqual(BruteForceContaining(items, probe), secondSeen, $"The second containing enumeration must answer identically under {packing}.");
            Assert.IsGreaterThan(0, index.DominanceNodeCount, $"The materialized epoch must report its dominance tree under {packing}.");
            Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, $"No repeat materialization may run within one epoch under {packing}.");
        }
    }

    /// <summary>A forcing diagnostic accessor runs the pass without any query having been asked.</summary>
    [TestMethod]
    public void ADominanceAccessorForcesMaterializationWithoutAnyQuery()
    {
        //The forcing accessor route: a diagnostic reader is as valid a first use of the
        //epoch as a containing query, and the pin surfaces flip identically.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(OverlappingGrid(20, 10)), "The overlapping grid must build.");
        Assert.IsFalse(index.DominanceMaterialized, "The build must leave the dominance structure pending.");
        Assert.IsGreaterThan(0, index.DominanceNodeCount, "The node-count accessor must force the pass and report the built tree.");
        Assert.IsTrue(index.DominanceMaterialized, "The forcing accessor must have flipped the pin.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "Exactly one materialization must have completed.");
    }

    /// <summary>The malformed-query short-circuit answers before the trigger, so no pass runs.</summary>
    [TestMethod]
    public void AMalformedProbeAnswersEmptyWithoutTriggeringMaterialization()
    {
        //The query well-formedness short-circuit precedes the materialization trigger: an
        //inverted probe box enumerates nothing in the containing mode, and because the
        //guard answers before the route reaches its first-use gate, the deferred pass must
        //stay un-run.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(OverlappingGrid(20, 10)), "The overlapping grid must build.");
        Assert.IsFalse(index.DominanceMaterialized, "The build must leave the dominance structure pending.");

        var inverted = new BoundingBox(60, 10, 40, 20);
        List<int> seen = CollectContaining(index, inverted);

        Assert.HasCount(0, seen, "An inverted probe must enumerate nothing.");
        Assert.IsFalse(index.DominanceMaterialized, "The malformed-probe short-circuit must precede the materialization trigger.");
        Assert.AreEqual(baseline, index.DominanceMaterializationCount, "No pass may have run for the malformed probe.");
    }

    /// <summary>Which surface triggered the pass cannot appear in the structure or the emission sequence.</summary>
    [TestMethod]
    public void QueryTriggeredAndAccessorTriggeredEpochsAgreeExactly()
    {
        //The trigger-order identity: WHICH surface runs the deferred pass first cannot
        //appear in the structure. One instance materializes through a containing query;
        //the other materializes through the forcing accessors before its first query. The
        //two full emission sequences and every per-node and per-position reading must
        //coincide exactly — sequence order is the claim, so nothing here is sorted.
        BoundingBox[] items = ConcentricBoxes(64);
        var probe = new BoundingBox(500, 500, 500, 500);

        foreach(BoxIndexPacking packing in Packings)
        {
            var options = new PackedBoxIndexOptions(packing, 16);
            using PackedBoxIndex queryFirst = PackedBoxIndex.Create(options);
            using PackedBoxIndex accessorFirst = PackedBoxIndex.Create(options);

            Assert.IsTrue(queryFirst.TryBuild(items), $"The concentric fixture must build for the query-first instance under {packing}.");
            Assert.IsTrue(accessorFirst.TryBuild(items), $"The concentric fixture must build for the accessor-first instance under {packing}.");

            //Every concentric box contains the centre point, so the emission is the
            //complete rank order over all sixty-four items.
            List<int> queryFirstEmission = CollectContaining(queryFirst, probe);

            Assert.HasCount(items.Length, queryFirstEmission, $"Every concentric box must contain the centre probe under {packing}.");

            //The accessor-first instance forces through every accessor class before its
            //first query, comparing each reading against the already-materialized
            //query-first instance as it goes.
            Assert.IsFalse(accessorFirst.DominanceMaterialized, $"The accessor-first instance must still be pending before its forcing sweep under {packing}.");

            int nodeCount = accessorFirst.DominanceNodeCount;

            Assert.IsTrue(accessorFirst.DominanceMaterialized, $"The node-count read must have forced the accessor-first instance under {packing}.");
            Assert.AreEqual(queryFirst.DominanceNodeCount, nodeCount, $"Both trigger orders must build the same node count under {packing}.");

            for(int node = 0; node < nodeCount; node++)
            {
                (int Left, int ItemStart, int ItemSpan) range = accessorFirst.DominanceNodeRange(node);
                Assert.AreEqual(queryFirst.DominanceNodeRange(node), range, $"Node {node} must carry identical range facts under both trigger orders under {packing}.");
                Assert.AreEqual(queryFirst.DominanceNodeUnion(node), accessorFirst.DominanceNodeUnion(node), $"Node {node} must carry the identical union box under both trigger orders under {packing}.");

                if(range.Left >= 0)
                {
                    Assert.AreEqual(queryFirst.DominanceNodeSplitFacts(node), accessorFirst.DominanceNodeSplitFacts(node), $"Internal node {node} must carry identical split facts under both trigger orders under {packing}.");
                }
            }

            for(int position = 0; position < items.Length; position++)
            {
                Assert.AreEqual(queryFirst.DominanceOrderSlot(position), accessorFirst.DominanceOrderSlot(position), $"Dominance order position {position} must agree under both trigger orders under {packing}.");
            }

            //Mutual agreement alone cannot see a drift the relocated pass itself carried —
            //both instances ran the same code — so both trigger orders also anchor against
            //the independent reference construction on this deep concentric shape.
            PackedBoxIndexDominanceBuildIdentityTests.AssertTreeMatchesReference(queryFirst, items, $"query-first concentric, {packing}");
            PackedBoxIndexDominanceBuildIdentityTests.AssertTreeMatchesReference(accessorFirst, items, $"accessor-first concentric, {packing}");

            List<int> accessorFirstEmission = CollectContaining(accessorFirst, probe);

            Assert.AreSequenceEqual(queryFirstEmission, accessorFirstEmission,
                $"The full containing emission sequence must be identical whichever surface materialized first under {packing}.");
        }
    }

    /// <summary>A rebuild returns the structure to pending and the next use answers the new items.</summary>
    [TestMethod]
    public void RebuildReturnsTheEpochToPendingAndAnswersTheNewItems()
    {
        //The rebuild lifecycle: materializing one epoch buys nothing for the next — a
        //rebuild returns the structure to pending, and the next containing use
        //materializes the new epoch and answers the new items' brute-force set.
        BoundingBox[] firstItems = OverlappingGrid(20, 10);
        BoundingBox[] secondItems = ConcentricBoxes(48);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(firstItems), "The first build must succeed.");

        var firstProbe = new BoundingBox(52, 33, 52, 33);
        List<int> firstSeen = CollectContaining(index, firstProbe);
        firstSeen.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(firstItems, firstProbe), firstSeen, "The first epoch must answer its brute-force set.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "The first epoch must have materialized once.");

        Assert.IsTrue(index.TryBuild(secondItems), "The rebuild must succeed.");
        Assert.IsFalse(index.DominanceMaterialized, "A rebuild must return the dominance structure to pending.");

        var secondProbe = new BoundingBox(500, 500, 500, 500);
        List<int> secondSeen = CollectContaining(index, secondProbe);
        secondSeen.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(secondItems, secondProbe), secondSeen, "The rebuilt epoch must answer the new items' brute-force set.");
        Assert.AreEqual(baseline + 2, index.DominanceMaterializationCount, "The rebuilt epoch must have run its own pass.");
    }

    /// <summary>The forcing accessors judge the current epoch, so a stale node index fails their guard.</summary>
    [TestMethod]
    public void StaleDominanceNodeIndicesFailAgainstTheRebuiltSmallerTree()
    {
        //The forcing accessors judge the CURRENT epoch, never a stale one: a node index
        //valid in a large epoch must fail the range guard once a rebuild has produced a
        //smaller tree, because the guard runs after the accessor's own materialization.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16));

        Assert.IsTrue(index.TryBuild(OverlappingGrid(20, 20)), "The four-hundred-item build must succeed.");

        int largeCount = index.DominanceNodeCount;

        Assert.IsGreaterThan(20, largeCount, "The four-hundred-item epoch must carry many dominance nodes.");
        Assert.IsTrue(index.TryBuild(OverlappingGrid(5, 4)), "The twenty-item rebuild must succeed.");
        Assert.IsFalse(index.DominanceMaterialized, "The rebuild must leave the new epoch pending.");

        int smallCount = index.DominanceNodeCount;

        Assert.IsTrue(index.DominanceMaterialized, "The node-count read must have materialized the new epoch.");
        Assert.IsGreaterThan(0, smallCount, "The rebuilt epoch must report its own tree.");
        Assert.IsLessThan(largeCount, smallCount, "The rebuilt epoch's tree must be the smaller one.");

        //A node index from the discarded large epoch must fail the current epoch's guard.
        bool threw = false;

        try
        {
            _ = index.DominanceNodeRange(largeCount - 1);
        }
        catch(ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A stale node index above the rebuilt tree's count must throw from the range guard.");
    }

    /// <summary>An epoch rebuilt away before any containing use never runs its pass at all.</summary>
    [TestMethod]
    public void ARebuildOverAPendingEpochRunsOnlyTheNewEpochsPass()
    {
        //The pending-skip: an epoch that is rebuilt away before any containing use never
        //runs its pass at all — across build A (never queried), build B, and B's first
        //query, the completion count advances by exactly one.
        BoundingBox[] firstItems = OverlappingGrid(20, 10);
        BoundingBox[] secondItems = ConcentricBoxes(32);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(firstItems), "The first build must succeed.");
        Assert.IsTrue(index.TryBuild(secondItems), "The rebuild over the pending epoch must succeed.");
        Assert.IsFalse(index.DominanceMaterialized, "The rebuilt epoch must be pending.");

        var probe = new BoundingBox(500, 500, 500, 500);
        List<int> seen = CollectContaining(index, probe);
        seen.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(secondItems, probe), seen, "The containing enumeration must answer the second build's brute-force set.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "Only the second epoch's pass may have run; the skipped pending epoch never materializes.");
    }

    /// <summary>Refusal discards a materialized epoch into the rootless shape and the next build recovers fully.</summary>
    [TestMethod]
    public void RefusalDiscardsAMaterializedEpochAndTheNextBuildRecovers()
    {
        //Refusal is destructive: a materialized working set is discarded, the index lands
        //in the rootless shape that is trivially materialized, and a subsequent valid
        //rebuild starts a fully working pending epoch of its own.
        BoundingBox[] validItems = OverlappingGrid(20, 10);
        var probe = new BoundingBox(52, 33, 52, 33);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(validItems), "The valid build must succeed.");

        List<int> beforeRefusal = CollectContaining(index, probe);
        beforeRefusal.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(validItems, probe), beforeRefusal, "The first epoch must answer its brute-force set.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "The first epoch must have materialized once.");

        //One non-finite ordinate refuses the whole build and discards the prior epoch.
        BoundingBox[] malformed = [new BoundingBox(0, 0, 1, 1), new BoundingBox(double.NaN, 0, 1, 1)];

        Assert.IsFalse(index.TryBuild(malformed), "A build with a non-finite ordinate must refuse.");
        Assert.AreEqual(0, index.Count, "Refusal must discard the prior working set.");

        List<int> afterRefusal = CollectContaining(index, probe);

        Assert.HasCount(0, afterRefusal, "A refused index must enumerate nothing.");
        Assert.IsTrue(index.DominanceMaterialized, "The refused rootless state is trivially materialized.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "The refused state must not have run any pass.");

        //Rebuilding after a refusal is legal and fully working, deferral included.
        Assert.IsTrue(index.TryBuild(validItems), "The rebuild after the refusal must succeed.");
        Assert.IsFalse(index.DominanceMaterialized, "The rebuilt epoch must be pending again.");

        List<int> recovered = CollectContaining(index, probe);
        recovered.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(validItems, probe), recovered, "The rebuilt epoch must answer its brute-force set.");
        Assert.AreEqual(baseline + 2, index.DominanceMaterializationCount, "The rebuilt epoch must have run its own pass.");
    }

    /// <summary>An empty build has nothing to defer: the pin reads materialized and no pass runs.</summary>
    [TestMethod]
    public void AnEmptyBuildIsTriviallyMaterialized()
    {
        //An empty span builds successfully into the rootless shape: there is nothing to
        //defer, so the pin reads materialized and no pass ever runs.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(ReadOnlySpan<BoundingBox>.Empty), "An empty span is a successful build.");
        Assert.IsTrue(index.DominanceMaterialized, "The empty build's rootless state is trivially materialized.");

        var probe = new BoundingBox(0, 0, 1, 1);
        List<int> seen = CollectContaining(index, probe);

        Assert.HasCount(0, seen, "An empty build must enumerate nothing.");
        Assert.AreEqual(baseline, index.DominanceMaterializationCount, "No pass may run for the empty build.");
    }

    /// <summary>A never-built index is materialized, node-free, and rejects every accessor argument.</summary>
    [TestMethod]
    public void ANeverBuiltIndexIsTriviallyMaterialized()
    {
        //A freshly created index is in the rootless shape from construction: materialized,
        //zero nodes, zero completed passes, and the node-range guard rejects every node index.
        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.DominanceMaterialized, "The never-built state is trivially materialized.");
        Assert.AreEqual(0, index.DominanceNodeCount, "The never-built state carries no dominance nodes.");
        Assert.AreEqual(0L, index.DominanceMaterializationCount, "No pass may have run on a never-built index.");

        //Every arg-taking dominance accessor must reject every argument on the
        //never-built index — the guards judge a zero-node, zero-item structure.
        bool rangeThrew = false;

        try
        {
            _ = index.DominanceNodeRange(0);
        }
        catch(ArgumentOutOfRangeException)
        {
            rangeThrew = true;
        }

        Assert.IsTrue(rangeThrew, "The node-range guard must reject every node index on the never-built tree.");

        bool splitThrew = false;

        try
        {
            _ = index.DominanceNodeSplitFacts(0);
        }
        catch(ArgumentOutOfRangeException)
        {
            splitThrew = true;
        }

        Assert.IsTrue(splitThrew, "The split-facts guard must reject every node index on the never-built tree.");

        bool orderThrew = false;

        try
        {
            _ = index.DominanceOrderSlot(0);
        }
        catch(ArgumentOutOfRangeException)
        {
            orderThrew = true;
        }

        Assert.IsTrue(orderThrew, "The order-slot guard must reject every position on the never-built tree.");

        bool unionThrew = false;

        try
        {
            _ = index.DominanceNodeUnion(0);
        }
        catch(ArgumentOutOfRangeException)
        {
            unionThrew = true;
        }

        Assert.IsTrue(unionThrew, "The union guard must reject every node index on the never-built tree.");
        Assert.AreEqual(0L, index.DominanceMaterializationCount, "The accessor probes must not have run a pass on the never-built index.");
    }

    /// <summary>Disposing a pending epoch keeps the pins readable, fails every forcing surface, and balances both rental classes.</summary>
    [TestMethod]
    public void DisposingAPendingEpochBalancesRentalsAndFailsEveryForcingSurface()
    {
        //Disposal of an epoch whose pass never ran: the pins keep reading without
        //throwing, every forcing surface fails loud, and both counted rental classes
        //balance — the intersecting sweep beforehand makes the stack class nonzero, so the
        //balance assertion can actually fail.
        PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(OverlappingGrid(20, 10)), "The overlapping grid must build.");

        var everything = new BoundingBox(-1, -1, 1_000, 1_000);

        foreach(int candidate in index.Intersecting(in everything))
        {
            _ = candidate;
        }

        Assert.IsFalse(index.DominanceMaterialized, "The intersect-based modes must never trigger the deferred pass.");

        index.Dispose();

        Assert.AreEqual(0L, index.DominanceMaterializationCount, "The disposed pending epoch must never have run its pass; the pin reads without throwing.");

        //The forcing accessor must fail loud on the disposed index, before any pass runs.
        bool nodeCountThrew = false;

        try
        {
            _ = index.DominanceNodeCount;
        }
        catch(ObjectDisposedException)
        {
            nodeCountThrew = true;
        }

        Assert.IsTrue(nodeCountThrew, "Reading the node count on a disposed index must throw.");

        //The containing route must fail loud at the query method itself.
        bool containingThrew = false;

        try
        {
            _ = index.Containing(in everything);
        }
        catch(ObjectDisposedException)
        {
            containingThrew = true;
        }

        Assert.IsTrue(containingThrew, "Starting a containing query on a disposed index must throw.");
        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned, "The traversal-stack rental class must balance across the disposed lifetime.");
        Assert.IsGreaterThan(0L, index.StackRentalsIssued, "The sweep must actually have rented a traversal stack, or the balance proves nothing.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned, "The containing collect-buffer rental class must balance across the disposed lifetime.");
    }

    /// <summary>Eight barrier-released first callers per epoch all answer the oracle and drive exactly one pass per epoch.</summary>
    /// <returns>The task the race completes on.</returns>
    [TestMethod]
    public async Task ConcurrentFirstContainingCallersMaterializeExactlyOncePerEpoch()
    {
        //Best-effort evidence for the cold-start concurrency contract, not a proof: eight
        //workers released through a barrier race into the FIRST containing use of each
        //freshly built epoch, so every worker either wins the materialization lock and
        //runs the pass or blocks until the winner publishes. Every worker must answer the
        //serial oracle, and after five such epochs the completion count must have advanced
        //by exactly five — one pass per epoch no matter how many first callers raced.
        //Workers write into disjoint answer slots and every comparison runs after the
        //join, so the row's verdict never depends on scheduling.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        long baseline = index.DominanceMaterializationCount;

        for(int epoch = 0; epoch < ColdStartEpochCount; epoch++)
        {
            //The single-writer rule holds: every build runs on this thread and the workers
            //only ever query. Varying the grid width per epoch makes each epoch's
            //structure its own.
            BoundingBox[] items = OverlappingGrid(12 + epoch, 10);

            Assert.IsTrue(index.TryBuild(items), $"The epoch-{epoch} build must succeed.");
            Assert.IsFalse(index.DominanceMaterialized, $"The epoch-{epoch} build must leave the dominance structure pending.");

            var probe = new BoundingBox(52, 33, 52, 33);
            List<int> oracle = BruteForceContaining(items, probe);
            var answers = new List<int>[ColdStartWorkerCount];

            using var release = new Barrier(ColdStartWorkerCount);
            var workers = new Task[ColdStartWorkerCount];

            for(int worker = 0; worker < ColdStartWorkerCount; worker++)
            {
                //Dedicated threads rather than pool threads: eight workers blocking on one
                //barrier must never wait on thread-pool injection to release each other.
                workers[worker] = Task.Factory.StartNew(
                    RunColdStartContainingQuery,
                    new ColdStartSlice(index, release, probe, answers, worker),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);

            for(int worker = 0; worker < ColdStartWorkerCount; worker++)
            {
                List<int> seen = answers[worker];
                seen.Sort();

                Assert.AreSequenceEqual(oracle, seen, $"Worker {worker} of epoch {epoch} must answer the serial oracle.");
            }
        }

        Assert.AreEqual(baseline + ColdStartEpochCount, index.DominanceMaterializationCount, "Exactly one materialization must have completed per epoch across the whole run.");
        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned, "The traversal-stack rental class must balance across the concurrent epochs.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned, "The containing collect-buffer rental class must balance across the concurrent epochs.");
    }

    /// <summary>A materialization mid-enumeration leaves a live intersecting enumerator walking to its exact set.</summary>
    [TestMethod]
    public void MaterializationUnderALiveIntersectingEnumeratorLeavesItUndisturbed()
    {
        //The deterministic single-threaded interleaving pin: the deferred pass writes only
        //columns the intersect-based modes never read and moves no span, so a containing
        //enumeration materializing mid-enumeration must leave a live intersecting
        //enumerator walking to its exact brute-force set.
        BoundingBox[] items = OverlappingGrid(20, 10);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 4));

        Assert.IsTrue(index.TryBuild(items), "The overlapping grid must build.");
        Assert.IsFalse(index.DominanceMaterialized, "The build must leave the dominance structure pending.");

        int versionBefore = index.BuildVersion;
        var intersectProbe = new BoundingBox(0, 0, 55, 55);
        var collected = new List<int>();

        using PackedBoxIndex.Enumerator enumerator = index.Intersecting(in intersectProbe).GetEnumerator();

        //Advance partway so the enumerator is mid-traversal when the pass runs.
        for(int step = 0; step < PreRendezvousSteps; step++)
        {
            Assert.IsTrue(enumerator.MoveNext(), "The overlap-rich probe must have more than five hits.");
            collected.Add(enumerator.Current);
        }

        var containingProbe = new BoundingBox(52, 33, 52, 33);
        List<int> containingSeen = CollectContaining(index, containingProbe);
        containingSeen.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(items, containingProbe), containingSeen, "The containing enumeration must answer its brute-force set mid-enumeration.");
        Assert.IsTrue(index.DominanceMaterialized, "The containing enumeration must have materialized under the live enumerator.");
        Assert.AreEqual(versionBefore, index.BuildVersion, "The deferred pass must publish without touching the build version.");

        //The live enumerator resumes across the materialization without any exception and
        //completes its exact answer.
        while(enumerator.MoveNext())
        {
            collected.Add(enumerator.Current);
        }

        collected.Sort();

        Assert.AreSequenceEqual(BruteForceIntersecting(items, intersectProbe), collected, "The interrupted intersecting enumeration must still answer its full brute-force set.");
        Assert.AreEqual(versionBefore, index.BuildVersion, "The completed interleaving must leave the build version unmoved.");
    }

    /// <summary>Held intersecting enumerators on worker threads survive one racing materialization and complete exactly.</summary>
    /// <returns>The task the race completes on.</returns>
    [TestMethod]
    public async Task LiveIntersectingEnumeratorsOnWorkerThreadsSurviveAConcurrentMaterialization()
    {
        //The threaded sibling of the deterministic pin — best-effort evidence in the
        //concurrent-reader tradition, not a proof: worker threads each hold an
        //intersecting enumerator advanced partway, rendezvous, race their first
        //containing query against one another, then resume their held enumerators to
        //completion. The pass writes only columns the intersect-based modes never read
        //and moves no span, so every answer must be exact and the pass must have run
        //exactly once. Workers write into disjoint slots and every comparison runs after
        //the join, so the row's verdict never depends on scheduling.
        BoundingBox[] items = OverlappingGrid(20, 10);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items), "The overlapping grid must build.");
        Assert.IsFalse(index.DominanceMaterialized, "The build must leave the dominance structure pending.");

        int versionBefore = index.BuildVersion;
        long countBefore = index.DominanceMaterializationCount;
        var intersectProbe = new BoundingBox(0, 0, 55, 55);
        var containingProbe = new BoundingBox(52, 33, 52, 33);
        List<int> expectedIntersecting = BruteForceIntersecting(items, intersectProbe);
        List<int> expectedContaining = BruteForceContaining(items, containingProbe);
        var intersectingAnswers = new List<int>[HeldEnumeratorWorkerCount];
        var containingAnswers = new List<int>[HeldEnumeratorWorkerCount];
        var advancedSteps = new int[HeldEnumeratorWorkerCount];

        using var rendezvous = new Barrier(HeldEnumeratorWorkerCount);
        var workers = new Task[HeldEnumeratorWorkerCount];

        for(int worker = 0; worker < HeldEnumeratorWorkerCount; worker++)
        {
            //Dedicated threads, not pool workers: every participant must reach the
            //barrier regardless of thread-pool injection pacing.
            workers[worker] = Task.Factory.StartNew(
                RunHeldEnumeratorSlice,
                new HeldEnumeratorSlice(index, rendezvous, intersectProbe, containingProbe, intersectingAnswers, containingAnswers, advancedSteps, worker),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        for(int worker = 0; worker < HeldEnumeratorWorkerCount; worker++)
        {
            Assert.AreEqual(PreRendezvousSteps, advancedSteps[worker], $"Worker {worker} must reach the rendezvous holding a mid-traversal enumerator: the overlap-rich probe has more than five hits.");

            List<int> containingSeen = containingAnswers[worker];
            containingSeen.Sort();

            Assert.AreSequenceEqual(expectedContaining, containingSeen, $"Worker {worker}'s racing containing query must answer the brute-force set.");

            List<int> intersectingSeen = intersectingAnswers[worker];
            intersectingSeen.Sort();

            Assert.AreSequenceEqual(expectedIntersecting, intersectingSeen, $"Worker {worker}'s held intersecting enumerator must complete its exact brute-force set across the materialization.");
        }

        Assert.IsTrue(index.DominanceMaterialized, "The racing containing queries must have materialized the epoch.");
        Assert.AreEqual(countBefore + 1L, index.DominanceMaterializationCount, "The racing first uses must have run the pass exactly once.");
        Assert.AreEqual(versionBefore, index.BuildVersion, "The concurrent materialization must leave the build version unmoved.");
        Assert.AreEqual(index.StackRentalsIssued, index.StackRentalsReturned, "The traversal-stack rental class must balance across the threaded interleaving.");
        Assert.AreEqual(index.CollectRentalsIssued, index.CollectRentalsReturned, "The containing collect-buffer rental class must balance across the threaded interleaving.");
    }

    /// <summary>At steady state the deferred pass allocates exactly zero managed bytes.</summary>
    [TestMethod]
    public void AWarmEpochsDeferredPassAllocatesNothing()
    {
        //The deferred pass writes through columns the build already sized, so at steady
        //state it must allocate zero managed bytes. Three build-and-materialize cycles
        //settle the column capacities and tiered compilation; the measured cycle then pins
        //the pass at exactly zero.
        BoundingBox[] items = OverlappingGrid(50, 40);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        for(int warm = 0; warm < 3; warm++)
        {
            Assert.IsTrue(index.TryBuild(items), "The warm-up build must succeed.");
            index.EnsureDominanceMaterialized();
        }

        Assert.IsTrue(index.TryBuild(items), "The measured build must succeed.");

        long before = GC.GetAllocatedBytesForCurrentThread();
        index.EnsureDominanceMaterialized();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0L, delta, "The warm-epoch deferred pass must not allocate; any byte is a regression in the rank walk, the axis sorts, the split loop, or the union sweep.");
    }

    /// <summary>A containing enumerator held across a rebuild fails loud on its next advance.</summary>
    [TestMethod]
    public void AContainingEnumeratorHeldAcrossARebuildFailsLoudOnItsNextMoveNext()
    {
        //The staleness guard spans materialization epochs: a containing enumerator's
        //matches were collected against one epoch's structure, so once a rebuild starts
        //the next epoch its next MoveNext must fail loud, exactly like the lazy modes'
        //enumerators.
        BoundingBox[] items = OverlappingGrid(20, 10);

        using PackedBoxIndex index = PackedBoxIndex.Create(PackedBoxIndexOptions.Default);

        Assert.IsTrue(index.TryBuild(items), "The first build must succeed.");

        var probe = new BoundingBox(52, 33, 52, 33);
        using PackedBoxIndex.Enumerator enumerator = index.Containing(in probe).GetEnumerator();

        Assert.IsTrue(index.TryBuild(items), "The rebuild itself is legal; only the stale enumerator must fail.");

        //Ref-struct enumerators cannot enter a lambda, so the throw assertion is manual.
        bool threw = false;

        try
        {
            _ = enumerator.MoveNext();
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A containing enumerator collected in one materialization epoch must fail loud after the next build.");
    }

    /// <summary>The eager carriage materializes at the build tail, exactly once per epoch.</summary>
    [TestMethod]
    public void AnEagerBuildMaterializesAtTheBuildTailExactlyOnce()
    {
        //The eager carriage under both packings: the build tail drives the same gate the
        //deferred carriage's first use drives, so the flag reads materialized and the
        //completion count reads exactly one the moment TryBuild returns — and neither the
        //first containing query nor a forcing accessor runs the pass again.
        BoundingBox[] items = OverlappingGrid(20, 10);
        var probe = new BoundingBox(52, 33, 52, 33);

        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16, DominanceMaterializationMode.EagerAtBuild));

            long baseline = index.DominanceMaterializationCount;

            Assert.IsTrue(index.TryBuild(items), $"The overlapping grid must build under {packing}.");
            Assert.IsTrue(index.DominanceMaterialized, $"An eager build must return with the dominance structure materialized under {packing}.");
            Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, $"The eager build must have run the pass exactly once under {packing}.");

            List<int> seen = CollectContaining(index, probe);
            seen.Sort();

            Assert.AreSequenceEqual(BruteForceContaining(items, probe), seen, $"The eager epoch must answer the brute-force set under {packing}.");
            Assert.IsGreaterThan(0, index.DominanceNodeCount, $"The materialized epoch must report its dominance tree under {packing}.");
            Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, $"Neither the query nor the accessor may re-run the pass within the epoch under {packing}.");
        }
    }

    /// <summary>When the pass runs cannot appear in what it builds: both carriages emit identical sequences and structures.</summary>
    [TestMethod]
    public void EagerAndDeferredCarriagesEmitIdenticalSequencesAndStructures()
    {
        //The carriage-identity row: WHEN the pass runs cannot appear in WHAT it builds.
        //One instance materializes eagerly at the build tail, the other through its first
        //containing query; the two full emission sequences and every per-node and
        //per-position reading must coincide exactly — sequence order is the claim, so
        //nothing here is sorted — and both anchor against the independent reference
        //construction, because mutual agreement alone cannot see a drift both carriages
        //share.
        BoundingBox[] items = ConcentricBoxes(64);
        var probe = new BoundingBox(500, 500, 500, 500);

        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex eager = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16, DominanceMaterializationMode.EagerAtBuild));
            using PackedBoxIndex deferred = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

            Assert.IsTrue(eager.TryBuild(items), $"The concentric fixture must build for the eager instance under {packing}.");
            Assert.IsTrue(deferred.TryBuild(items), $"The concentric fixture must build for the deferred instance under {packing}.");
            Assert.IsTrue(eager.DominanceMaterialized, $"The eager instance must be materialized at build return under {packing}.");
            Assert.IsFalse(deferred.DominanceMaterialized, $"The deferred instance must still be pending before its first query under {packing}.");

            List<int> deferredEmission = CollectContaining(deferred, probe);
            List<int> eagerEmission = CollectContaining(eager, probe);

            Assert.HasCount(items.Length, eagerEmission, $"Every concentric box must contain the centre probe under {packing}.");
            Assert.AreSequenceEqual(deferredEmission, eagerEmission,
                $"The full containing emission sequence must be carriage-invariant under {packing}.");

            int nodeCount = eager.DominanceNodeCount;

            Assert.AreEqual(deferred.DominanceNodeCount, nodeCount, $"Both carriages must build the same node count under {packing}.");

            for(int node = 0; node < nodeCount; node++)
            {
                (int Left, int ItemStart, int ItemSpan) range = eager.DominanceNodeRange(node);
                Assert.AreEqual(deferred.DominanceNodeRange(node), range, $"Node {node} must carry identical range facts under both carriages under {packing}.");
                Assert.AreEqual(deferred.DominanceNodeUnion(node), eager.DominanceNodeUnion(node), $"Node {node} must carry the identical union box under both carriages under {packing}.");

                if(range.Left >= 0)
                {
                    Assert.AreEqual(deferred.DominanceNodeSplitFacts(node), eager.DominanceNodeSplitFacts(node), $"Internal node {node} must carry identical split facts under both carriages under {packing}.");
                }
            }

            for(int position = 0; position < items.Length; position++)
            {
                Assert.AreEqual(deferred.DominanceOrderSlot(position), eager.DominanceOrderSlot(position), $"Dominance order position {position} must agree under both carriages under {packing}.");
            }

            PackedBoxIndexDominanceBuildIdentityTests.AssertTreeMatchesReference(eager, items, $"eager concentric, {packing}");
            PackedBoxIndexDominanceBuildIdentityTests.AssertTreeMatchesReference(deferred, items, $"deferred concentric, {packing}");
        }
    }

    /// <summary>Every eager build pays its own pass at the tail and answers its own items.</summary>
    [TestMethod]
    public void AnEagerRebuildRunsThePassOncePerEpoch()
    {
        //The eager rebuild lifecycle: every successful non-empty build pays its own pass
        //at the tail — no pending state is ever observable — and each epoch answers its
        //own items' brute-force set.
        BoundingBox[] firstItems = OverlappingGrid(20, 10);
        BoundingBox[] secondItems = ConcentricBoxes(48);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16, DominanceMaterializationMode.EagerAtBuild));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(firstItems), "The first eager build must succeed.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "The first eager epoch must have run its pass at the tail.");

        var firstProbe = new BoundingBox(52, 33, 52, 33);
        List<int> firstSeen = CollectContaining(index, firstProbe);
        firstSeen.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(firstItems, firstProbe), firstSeen, "The first eager epoch must answer its brute-force set.");

        Assert.IsTrue(index.TryBuild(secondItems), "The eager rebuild must succeed.");
        Assert.IsTrue(index.DominanceMaterialized, "An eager rebuild must return materialized, never pending.");
        Assert.AreEqual(baseline + 2, index.DominanceMaterializationCount, "The rebuilt eager epoch must have run its own pass.");

        var secondProbe = new BoundingBox(500, 500, 500, 500);
        List<int> secondSeen = CollectContaining(index, secondProbe);
        secondSeen.Sort();

        Assert.AreSequenceEqual(BruteForceContaining(secondItems, secondProbe), secondSeen, "The rebuilt eager epoch must answer the new items' brute-force set.");
        Assert.AreEqual(baseline + 2, index.DominanceMaterializationCount, "The queries must ride the already-materialized epochs.");
    }

    /// <summary>The rootless states are carriage-independent: an eager empty build and an eager refusal run no pass.</summary>
    [TestMethod]
    public void AnEagerEmptyBuildAndAnEagerRefusalStayTriviallyMaterialized()
    {
        //The rootless states are carriage-independent: after a real materialized eager
        //epoch — seeded first so the count and flag pins have travel — an empty eager
        //build and an eager refusal both land trivially materialized with the completion
        //count untouched, because the build tail's eager drive belongs to the non-empty
        //success path alone and the rootless short-circuit counts nothing.
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16, DominanceMaterializationMode.EagerAtBuild));

        long baseline = index.DominanceMaterializationCount;

        Assert.IsTrue(index.TryBuild(OverlappingGrid(20, 10)), "The seeding eager build must succeed.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "The seeding eager build must have run its pass.");

        Assert.IsTrue(index.TryBuild(ReadOnlySpan<BoundingBox>.Empty), "An empty span is a successful build under the eager carriage too.");
        Assert.IsTrue(index.DominanceMaterialized, "The empty eager build's rootless state is trivially materialized.");
        Assert.AreEqual(baseline + 1, index.DominanceMaterializationCount, "No pass may run for the empty eager build.");

        Assert.IsTrue(index.TryBuild(OverlappingGrid(5, 4)), "The re-seeding eager build must succeed.");
        Assert.AreEqual(baseline + 2, index.DominanceMaterializationCount, "The re-seeding eager build must have run its pass.");

        BoundingBox[] malformed = [new BoundingBox(0, 0, 1, 1), new BoundingBox(double.NaN, 0, 1, 1)];

        Assert.IsFalse(index.TryBuild(malformed), "A build with a non-finite ordinate must refuse under the eager carriage too.");
        Assert.IsTrue(index.DominanceMaterialized, "The refused rootless state is trivially materialized.");
        Assert.AreEqual(baseline + 2, index.DominanceMaterializationCount, "No pass may run for the eager refusal.");
    }

    /// <summary>The sanctioned, record-struct, and two-argument defaults all carry the deferred carriage.</summary>
    [TestMethod]
    public void TheDefaultCarriageIsDeferredToFirstUse()
    {
        //The default trap stays inert: the sanctioned default, the record-struct default,
        //and the two-argument construction all carry the deferred carriage — the validated
        //default — so no existing consumer's behavior moves.
        Assert.AreEqual(DominanceMaterializationMode.DeferredToFirstUse, PackedBoxIndexOptions.Default.DominanceMaterialization, "The sanctioned default must carry the deferred carriage.");
        Assert.AreEqual(DominanceMaterializationMode.DeferredToFirstUse, default(PackedBoxIndexOptions).DominanceMaterialization, "The record-struct default must carry the deferred carriage.");
        Assert.AreEqual(DominanceMaterializationMode.DeferredToFirstUse, new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16).DominanceMaterialization, "The two-argument construction must carry the deferred carriage.");
    }

    /// <summary>An undefined materialization mode is refused at creation, never carried into a build.</summary>
    [TestMethod]
    public void AnUndefinedDominanceMaterializationModeIsRefusedAtCreate()
    {
        //The mode joins the packing and capacity validations: an undefined member is
        //refused at creation, never carried into a build.
        Assert.Throws<ArgumentOutOfRangeException>(
            static () => PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 16, (DominanceMaterializationMode)7)).Dispose());
    }

    /// <summary>
    /// An overlapping grid: boxes on a ten-unit lattice, each fifteen wide and
    /// tall, so interior point probes have several containers and intersecting
    /// probes have rich candidate sets.
    /// </summary>
    /// <param name="columns">The lattice column count.</param>
    /// <param name="rows">The lattice row count.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] OverlappingGrid(int columns, int rows)
    {
        var items = new BoundingBox[columns * rows];

        for(int index = 0; index < items.Length; index++)
        {
            double x = (index % columns) * 10d;
            double y = (index / columns) * 10d;
            items[index] = new BoundingBox(x, y, x + 15, y + 15);
        }

        return items;
    }

    /// <summary>Concentric nested boxes insetting one unit per registration, every one containing the centre point (500, 500).</summary>
    /// <param name="count">The number of boxes.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] ConcentricBoxes(int count)
    {
        var items = new BoundingBox[count];

        for(int index = 0; index < count; index++)
        {
            items[index] = new BoundingBox(index, index, 1_000 - index, 1_000 - index);
        }

        return items;
    }

    /// <summary>The brute-force containment oracle: every registration whose item contains the probe, ascending.</summary>
    /// <param name="items">The registered items.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The ascending registration indices of the probe's containers.</returns>
    private static List<int> BruteForceContaining(BoundingBox[] items, BoundingBox probe)
    {
        var expected = new List<int>();

        for(int registration = 0; registration < items.Length; registration++)
        {
            if(items[registration].Contains(probe))
            {
                expected.Add(registration);
            }
        }

        return expected;
    }

    /// <summary>The brute-force intersection oracle: every registration whose item meets the probe, ascending.</summary>
    /// <param name="items">The registered items.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The ascending registration indices the probe meets.</returns>
    private static List<int> BruteForceIntersecting(BoundingBox[] items, BoundingBox probe)
    {
        var expected = new List<int>();

        for(int registration = 0; registration < items.Length; registration++)
        {
            if(items[registration].Intersects(probe))
            {
                expected.Add(registration);
            }
        }

        return expected;
    }

    /// <summary>Enumerates the containing mode through the public foreach pattern into a list, emission order preserved.</summary>
    /// <param name="index">The index to query.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The answered registration indices in emission order.</returns>
    private static List<int> CollectContaining(PackedBoxIndex index, BoundingBox probe)
    {
        var results = new List<int>();

        foreach(int candidate in index.Containing(in probe))
        {
            results.Add(candidate);
        }

        return results;
    }

    /// <summary>One barrier-released cold-start worker: rendezvous, then the epoch's first containing enumeration into this worker's own answer slot; bound as a static callback so nothing closes over the test body.</summary>
    /// <param name="state">The worker's <see cref="ColdStartSlice"/>.</param>
    private static void RunColdStartContainingQuery(object? state)
    {
        var slice = (ColdStartSlice)state!;
        slice.Release.SignalAndWait();

        slice.Answers[slice.Worker] = CollectContaining(slice.Index, slice.Probe);
    }

    /// <summary>
    /// One held-enumerator worker: advance the intersecting enumerator
    /// partway, rendezvous, race the containing query, then finish the held
    /// enumeration — every reading lands in this worker's own slots, and the
    /// pre-rendezvous advance never throws, so a short probe can never strand
    /// the other workers on the barrier.
    /// </summary>
    /// <param name="state">The worker's <see cref="HeldEnumeratorSlice"/>.</param>
    private static void RunHeldEnumeratorSlice(object? state)
    {
        var slice = (HeldEnumeratorSlice)state!;
        BoundingBox intersectProbe = slice.IntersectProbe;
        var collected = new List<int>();
        int advanced = 0;

        using PackedBoxIndex.Enumerator enumerator = slice.Index.Intersecting(in intersectProbe).GetEnumerator();

        for(int step = 0; step < PreRendezvousSteps; step++)
        {
            if(!enumerator.MoveNext())
            {
                break;
            }

            collected.Add(enumerator.Current);
            advanced++;
        }

        slice.AdvancedSteps[slice.Worker] = advanced;
        slice.Rendezvous.SignalAndWait();

        slice.ContainingAnswers[slice.Worker] = CollectContaining(slice.Index, slice.ContainingProbe);

        while(enumerator.MoveNext())
        {
            collected.Add(enumerator.Current);
        }

        slice.IntersectingAnswers[slice.Worker] = collected;
    }

    /// <summary>
    /// The explicit state one cold-start worker receives: the shared index,
    /// the release barrier every worker signals, the probe, the shared answer
    /// array, and the single slot this worker alone writes — disjoint slots,
    /// so the race needs no synchronisation and no assertion runs off the test
    /// thread.
    /// </summary>
    private sealed class ColdStartSlice
    {
        /// <summary>The index every worker races into its first containing use of.</summary>
        public PackedBoxIndex Index { get; }

        /// <summary>The barrier that releases all workers into the race together.</summary>
        public Barrier Release { get; }

        /// <summary>The containing probe every worker asks.</summary>
        public BoundingBox Probe { get; }

        /// <summary>The shared answer array; each worker writes only its own slot.</summary>
        public List<int>[] Answers { get; }

        /// <summary>This worker's answer slot.</summary>
        public int Worker { get; }

        /// <summary>Captures the worker's share of the race.</summary>
        /// <param name="index">The index every worker races into.</param>
        /// <param name="release">The release barrier.</param>
        /// <param name="probe">The containing probe.</param>
        /// <param name="answers">The shared answer array.</param>
        /// <param name="worker">This worker's answer slot.</param>
        public ColdStartSlice(PackedBoxIndex index, Barrier release, BoundingBox probe, List<int>[] answers, int worker)
        {
            Index = index;
            Release = release;
            Probe = probe;
            Answers = answers;
            Worker = worker;
        }
    }

    /// <summary>
    /// The explicit state one held-enumerator worker receives: the shared
    /// index, the rendezvous barrier, both probes, the shared answer arrays
    /// for the held intersecting enumeration and the racing containing query,
    /// the advance-count array, and the single slot this worker alone writes.
    /// </summary>
    private sealed class HeldEnumeratorSlice
    {
        /// <summary>The index every worker queries concurrently.</summary>
        public PackedBoxIndex Index { get; }

        /// <summary>The barrier every worker signals once its enumerator is mid-traversal.</summary>
        public Barrier Rendezvous { get; }

        /// <summary>The probe of the enumeration held across the materialization.</summary>
        public BoundingBox IntersectProbe { get; }

        /// <summary>The probe of the query racing into the materialization.</summary>
        public BoundingBox ContainingProbe { get; }

        /// <summary>The shared answers of the held intersecting enumerations; each worker writes only its own slot.</summary>
        public List<int>[] IntersectingAnswers { get; }

        /// <summary>The shared answers of the racing containing queries; each worker writes only its own slot.</summary>
        public List<int>[] ContainingAnswers { get; }

        /// <summary>The steps each worker advanced before signalling the rendezvous.</summary>
        public int[] AdvancedSteps { get; }

        /// <summary>This worker's answer slot.</summary>
        public int Worker { get; }

        /// <summary>Captures the worker's share of the race.</summary>
        /// <param name="index">The index every worker queries concurrently.</param>
        /// <param name="rendezvous">The rendezvous barrier.</param>
        /// <param name="intersectProbe">The held enumeration's probe.</param>
        /// <param name="containingProbe">The racing query's probe.</param>
        /// <param name="intersectingAnswers">The shared intersecting-answer array.</param>
        /// <param name="containingAnswers">The shared containing-answer array.</param>
        /// <param name="advancedSteps">The shared pre-rendezvous advance counts.</param>
        /// <param name="worker">This worker's answer slot.</param>
        public HeldEnumeratorSlice(
            PackedBoxIndex index,
            Barrier rendezvous,
            BoundingBox intersectProbe,
            BoundingBox containingProbe,
            List<int>[] intersectingAnswers,
            List<int>[] containingAnswers,
            int[] advancedSteps,
            int worker)
        {
            Index = index;
            Rendezvous = rendezvous;
            IntersectProbe = intersectProbe;
            ContainingProbe = containingProbe;
            IntersectingAnswers = intersectingAnswers;
            ContainingAnswers = containingAnswers;
            AdvancedSteps = advancedSteps;
            Worker = worker;
        }
    }
}
