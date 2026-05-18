using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class HypertrieSnapshotTests
{
    public TestContext TestContext { get; set; } = null!;

    //A non-empty synthetic identifier used wherever the test does
    //not care about the specific content addressing — it just
    //needs an id to thread through the constructor.
    private static NodeIdentifier SyntheticId { get; } = new(0x123456789ABCDEFUL);

    [TestMethod]
    public void NewSnapshotHasReferenceCountOne()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);

        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        Assert.AreEqual(1, snapshot.RefCount);
    }

    [TestMethod]
    public void NewSnapshotIsRegisteredWithStore()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);

        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        Assert.IsTrue(store.IsSnapshotAcquired(snapshot));
        Assert.AreEqual(1, store.AcquiredSnapshotCount);
    }

    [TestMethod]
    public void IdIsExposedFromConstructor()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);

        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        Assert.AreEqual(SyntheticId, snapshot.Id);
    }

    [TestMethod]
    public void EmptyIdentifierIsAllowed()
    {
        //An empty graph is a legitimate snapshot whose root has
        //the empty identifier; the constructor must accept it.
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);

        using HypertrieSnapshot snapshot = new(store, root, NodeIdentifier.Empty);

        Assert.AreEqual(NodeIdentifier.Empty, snapshot.Id);
    }

    [TestMethod]
    public void AcquireIncrementsReferenceCount()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        snapshot.Acquire();

        Assert.AreEqual(2, snapshot.RefCount);

        //Restore balance so the using-Dispose returns count to
        //zero cleanly.
        snapshot.Release();
    }

    [TestMethod]
    public void AcquireReturnsTheSameSnapshot()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        HypertrieSnapshot returned = snapshot.Acquire();

        Assert.AreSame(snapshot, returned);

        snapshot.Release();
    }

    [TestMethod]
    public void ReleaseDecrementsReferenceCount()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);
        snapshot.Acquire();

        snapshot.Release();

        Assert.AreEqual(1, snapshot.RefCount);
    }

    [TestMethod]
    public void ReleaseToZeroDeregistersFromStore()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        snapshot.Release();

        Assert.AreEqual(0, snapshot.RefCount);
        Assert.IsFalse(store.IsSnapshotAcquired(snapshot));
        Assert.AreEqual(0, store.AcquiredSnapshotCount);
    }

    [TestMethod]
    public void AcquireAfterFullReleaseThrows()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);
        snapshot.Release();

        Assert.Throws<ObjectDisposedException>(() => snapshot.Acquire());
    }

    [TestMethod]
    public void DisposeReleasesOnce()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        snapshot.Dispose();

        Assert.AreEqual(0, snapshot.RefCount);
        Assert.IsFalse(store.IsSnapshotAcquired(snapshot));
    }

    [TestMethod]
    public void DoubleDisposeIsNoOp()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        snapshot.Dispose();
        snapshot.Dispose();

        Assert.AreEqual(0, snapshot.RefCount);
    }

    [TestMethod]
    public void AcquireFollowedByReleaseLeavesOriginalCount()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        snapshot.Acquire();
        snapshot.Release();

        Assert.AreEqual(1, snapshot.RefCount);
        Assert.IsTrue(store.IsSnapshotAcquired(snapshot));
    }

    [TestMethod]
    public void MultipleAcquiresAndReleasesBalance()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root = InternFreshRoot(store);
        using HypertrieSnapshot snapshot = new(store, root, SyntheticId);

        for(int i = 0; i < 5; i++)
        {
            snapshot.Acquire();
        }

        Assert.AreEqual(6, snapshot.RefCount);

        for(int i = 0; i < 5; i++)
        {
            snapshot.Release();
        }

        Assert.AreEqual(1, snapshot.RefCount);
        Assert.IsTrue(store.IsSnapshotAcquired(snapshot));
    }

    [TestMethod]
    public void ConstructorRejectsNullStore()
    {
        Assert.Throws<ArgumentNullException>(() => new HypertrieSnapshot(null!, NodeHandle.None, SyntheticId));
    }

    [TestMethod]
    public void TwoIndependentSnapshotsRegisterIndependently()
    {
        using NodeStore store = new(VeritasHashing.Default);
        NodeHandle root1 = InternFreshRoot(store);
        NodeHandle root2 = InternFreshRoot(store, distinguisher: 7U);
        NodeIdentifier id1 = new(0x1111);
        NodeIdentifier id2 = new(0x2222);

        using HypertrieSnapshot s1 = new(store, root1, id1);
        using HypertrieSnapshot s2 = new(store, root2, id2);

        Assert.AreEqual(2, store.AcquiredSnapshotCount);

        s1.Release();

        Assert.AreEqual(1, store.AcquiredSnapshotCount);
        Assert.IsFalse(store.IsSnapshotAcquired(s1));
        Assert.IsTrue(store.IsSnapshotAcquired(s2));
    }

    //Builds a minimal depth-3 root, interns it via the store, and
    //returns the handle. Used by tests that just need a real
    //handle to thread through the snapshot constructor without
    //caring about content.
    private static NodeHandle InternFreshRoot(NodeStore store, uint distinguisher = 1U)
    {
        HypertrieNode root = HypertrieNode.Create(3);
        NodeIdentifier id = new(distinguisher);
        return store.Intern(id, root);
    }
}
