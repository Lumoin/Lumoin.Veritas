using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class NodeStoreSweepTests
{
    public TestContext TestContext { get; set; } = null!;

    //A small graph spanning multiple subjects, predicates, and
    //objects so that sweep walks a non-trivial node tree under
    //the registered snapshot.
    private static EncodedTriple[] SampleTriples { get; } =
    [
        EncodedTriple.FromEncoded(1, 10, 100),
        EncodedTriple.FromEncoded(1, 11, 200),
        EncodedTriple.FromEncoded(2, 10, 100),
        EncodedTriple.FromEncoded(2, 12, 300),
    ];

    [TestMethod]
    public async Task SweepOnEmptyStoreReturnsZeroResult()
    {
        using NodeStore store = new(VeritasHashing.Default);

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.NodesEvicted);
        Assert.AreEqual(0, result.NodesRetained);
        Assert.AreEqual(0, result.ChainsTouched);
        Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public async Task SweepWithLiveSnapshotRetainsAllNodes()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        int countBefore = store.Count;
        Assert.IsGreaterThan(0, countBefore);

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.NodesEvicted);
        Assert.AreEqual(countBefore, result.NodesRetained);
        Assert.AreEqual(countBefore, store.Count);

        //Bind the graph store to a use after the assertions so that
        //the JIT cannot have proven it dead earlier and dropped its
        //snapshot reference, which would falsify the test.
        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public async Task SweepWithNoLiveSnapshotsEvictsAllNodes()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        int countBefore = store.Count;
        Assert.IsGreaterThan(0, countBefore);

        //Drop the only snapshot, removing it from the registry.
        graphStore.Snapshot.Dispose();
        Assert.AreEqual(0, store.AcquiredSnapshotCount);

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(countBefore, result.NodesEvicted);
        Assert.AreEqual(0, result.NodesRetained);
        Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public async Task SweepRetainsNodesReachableFromAnyLiveSnapshotWhenContentDiffers()
    {
        using NodeStore store = new(VeritasHashing.Default);

        EncodedTriple[] triplesA = [EncodedTriple.FromEncoded(1, 10, 100)];
        EncodedTriple[] triplesB = [EncodedTriple.FromEncoded(2, 20, 200)];

        HypertrieGraphStore first = await HypertrieGraphStore.BuildAsync(triplesA, store, TestContext.CancellationToken).ConfigureAwait(false);
        HypertrieGraphStore second = await HypertrieGraphStore.BuildAsync(triplesB, store, TestContext.CancellationToken).ConfigureAwait(false);

        int totalCount = store.Count;
        Assert.IsGreaterThan(0, totalCount);
        Assert.AreEqual(2, store.AcquiredSnapshotCount);

        first.Snapshot.Dispose();
        Assert.AreEqual(1, store.AcquiredSnapshotCount);

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        //First's nodes are no longer reachable; second's still are.
        //Both build subtrees over disjoint subject/predicate/object
        //terms, so first's nodes are wholly unreachable from second.
        Assert.IsGreaterThan(0, result.NodesEvicted);
        Assert.IsGreaterThan(0, result.NodesRetained);
        Assert.AreEqual(totalCount, result.NodesEvicted + result.NodesRetained);
        Assert.AreEqual(result.NodesRetained, store.Count);

        //second's snapshot must remain usable after the sweep.
        Assert.IsNotNull(second.Snapshot);
    }

    [TestMethod]
    public async Task SweepRetainsAllNodesWhenSnapshotsBuildIdenticalContent()
    {
        using NodeStore store = new(VeritasHashing.Default);

        HypertrieGraphStore first = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);
        HypertrieGraphStore second = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        int countAfterBoth = store.Count;
        Assert.AreEqual(2, store.AcquiredSnapshotCount);

        first.Snapshot.Dispose();
        SweepResult afterFirst = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        //Identical content means every node is canonical-shared
        //between the two builds; second's snapshot still pins them
        //all, so nothing should be evicted.
        Assert.AreEqual(0, afterFirst.NodesEvicted);
        Assert.AreEqual(countAfterBoth, store.Count);

        second.Snapshot.Dispose();
        SweepResult afterBoth = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(countAfterBoth, afterBoth.NodesEvicted);
        Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public async Task SweepCountIncrementsAcrossSweeps()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.AreEqual(0, store.SweepCount);

        await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, store.SweepCount);

        await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(2, store.SweepCount);

        await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(3, store.SweepCount);
    }

    [TestMethod]
    public async Task SweepIsCancellable()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.SweepAsync(cts.Token).ConfigureAwait(false))
            .ConfigureAwait(false);

        //The store should be unchanged: a cancelled sweep neither
        //evicts nodes nor advances the sweep counter.
        Assert.IsGreaterThan(0, store.Count);
        Assert.AreEqual(0, store.SweepCount);
        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public async Task SweepEvictsSnapshotDroppedWithoutRelease()
    {
        using NodeStore store = new(VeritasHashing.Default);

        //A logical store superseded by simply dropping the reference
        //(no explicit release) — the shared-arena supersede pattern.
        //The weak snapshot registry must let the collector reclaim
        //it, after which the sweep evicts its nodes.
        int countBefore = await BuildAndDropAsync(store, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsGreaterThan(0, countBefore);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(countBefore, result.NodesEvicted);
        Assert.AreEqual(0, store.Count);
        Assert.AreEqual(0, store.AcquiredSnapshotCount);
    }

    /// <summary>
    /// Builds a graph store in the arena and drops the reference on
    /// return. A separate non-inlined method so the test frame holds
    /// no live reference to the store or its snapshot afterwards.
    /// </summary>
    /// <param name="store">The shared arena.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The arena's node count with the build live.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<int> BuildAndDropAsync(NodeStore store, CancellationToken cancellationToken)
    {
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(graphStore.Snapshot);

        return store.Count;
    }

    [TestMethod]
    public async Task RootSetPinKeepsRootsAcrossSweepWithoutSnapshots()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);
        int countBefore = store.Count;

        //The pin is the dataset-level snapshot: it alone keeps the
        //root reachable after the per-store snapshot is released.
        HypertrieRootSetPin pin = store.PinRoots([graphStore.Snapshot.Root]);
        graphStore.Snapshot.Dispose();

        SweepResult pinned = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, pinned.NodesEvicted);
        Assert.AreEqual(countBefore, store.Count);

        pin.Dispose();
        SweepResult released = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(countBefore, released.NodesEvicted);
        Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public async Task SweepEvictsPinDroppedWithoutDispose()
    {
        using NodeStore store = new(VeritasHashing.Default);

        int countBefore = await BuildPinAndDropAsync(store, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsGreaterThan(0, countBefore);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(countBefore, result.NodesEvicted);
        Assert.AreEqual(0, store.Count);
    }

    /// <summary>
    /// Builds a graph, pins its root, releases the snapshot, and
    /// drops the pin on return. A separate non-inlined method so the
    /// test frame holds no live reference to the pin afterwards.
    /// </summary>
    /// <param name="store">The shared arena.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The arena's node count with the pin live.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<int> BuildPinAndDropAsync(NodeStore store, CancellationToken cancellationToken)
    {
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, cancellationToken).ConfigureAwait(false);
        HypertrieRootSetPin pin = store.PinRoots([graphStore.Snapshot.Root]);
        graphStore.Snapshot.Dispose();
        Assert.IsNotNull(pin.Roots);

        return store.Count;
    }

    [TestMethod]
    public void DisposingStoreIsIdempotent()
    {
        NodeStore store = new(VeritasHashing.Default);
        store.Dispose();
        store.Dispose();
        store.Dispose();
    }

    [TestMethod]
    public async Task SweepReportsAccurateStatistics()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        int countBefore = store.Count;
        graphStore.Snapshot.Dispose();

        SweepResult result = await store.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(countBefore, result.NodesEvicted + result.NodesRetained);
        Assert.AreEqual(store.Count, result.NodesRetained);

        //ChainsTouched is bounded by NodesEvicted in absence of hash
        //collisions: every eviction in the common single-entry
        //bucket case touches exactly one chain. With collisions a
        //single chain may evict multiple nodes, so the bound is
        //ChainsTouched <= NodesEvicted.
        Assert.IsLessThanOrEqualTo(result.NodesEvicted, result.ChainsTouched);
    }
}
