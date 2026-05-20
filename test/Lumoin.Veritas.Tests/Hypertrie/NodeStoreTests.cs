using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class NodeStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NewStoreIsEmpty()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public void HashIsRetainedFromConstruction()
    {
        //The store carries the hash function its consumers must
        //share. Reference equality on a static method group is
        //not guaranteed across delegate creations, so verify the
        //store's delegate targets the same static method as the
        //one passed in, and that it produces the same output for
        //a sample input — that is the substantive property
        //consumers rely on.
        using NodeStore store = new(VeritasHashing.Default);

        Assert.AreEqual(((VeritasHash)VeritasHashing.Default).Method, store.Hash.Method);

        Span<byte> sample = stackalloc byte[8];
        sample[0] = 0x42;
        Assert.AreEqual(VeritasHashing.Default(sample), store.Hash(sample));
    }

    [TestMethod]
    public void FirstInternRecordsAndReturnsReference()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode node = MakeDepth1Leaf([10, 20]);
        NodeIdentifier id = HypertrieOps.ComputeIdentifier(node, store.Hash, knownChildIdentifiers: null);

        HypertrieNode canonical = store.Intern(id, node);

        Assert.IsNotNull(canonical);
        Assert.AreSame(node, canonical);
        Assert.AreEqual(1, store.Count);
        Assert.IsTrue(store.Contains(id));
    }

    [TestMethod]
    public void RepeatedInternOfContentEqualNodeReturnsSameReference()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode first = MakeDepth1Leaf([10, 20]);
        HypertrieNode second = MakeDepth1Leaf([10, 20]);
        NodeIdentifier id = HypertrieOps.ComputeIdentifier(first, store.Hash, knownChildIdentifiers: null);

        HypertrieNode canonical = store.Intern(id, first);
        HypertrieNode rediscovered = store.Intern(id, second);

        Assert.AreSame(canonical, rediscovered);
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void InsertionOrderDoesNotAffectCanonicalIdentity()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode forwardOrder = MakeDepth1Leaf([1, 2, 3, 4, 5]);
        HypertrieNode reverseOrder = MakeDepth1Leaf([5, 4, 3, 2, 1]);
        NodeIdentifier idForward = HypertrieOps.ComputeIdentifier(forwardOrder, store.Hash, knownChildIdentifiers: null);
        NodeIdentifier idReverse = HypertrieOps.ComputeIdentifier(reverseOrder, store.Hash, knownChildIdentifiers: null);

        Assert.AreEqual(idForward, idReverse);

        HypertrieNode canonical = store.Intern(idForward, forwardOrder);
        HypertrieNode rediscovered = store.Intern(idReverse, reverseOrder);

        Assert.AreSame(canonical, rediscovered);
    }

    [TestMethod]
    public void DifferentDepthsAreCanonicalisedSeparately()
    {
        //Two empty nodes of different depths share the empty
        //identifier (no entries to hash) but differ in depth, so
        //the store must treat them as a collision and chain them.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode emptyDepth1 = new(1);
        HypertrieNode emptyDepth2 = new(2);

        HypertrieNode firstCanonical = store.Intern(NodeIdentifier.Empty, emptyDepth1);
        HypertrieNode secondCanonical = store.Intern(NodeIdentifier.Empty, emptyDepth2);

        Assert.AreNotSame(firstCanonical, secondCanonical);
        Assert.AreEqual(2, store.Count);
    }

    [TestMethod]
    public void ForcedHashCollisionPreservesBothNodes()
    {
        //Use a custom hash that always returns the same value to
        //force every node to collide on identifier. Two
        //content-different nodes must each remain reachable as the
        //canonical instance for their own content.
        using NodeStore store = new(StaticHash);
        HypertrieNode first = MakeDepth1Leaf([1, 2]);
        HypertrieNode second = MakeDepth1Leaf([3, 4]);

        NodeIdentifier idFirst = HypertrieOps.ComputeIdentifier(first, store.Hash, knownChildIdentifiers: null);
        NodeIdentifier idSecond = HypertrieOps.ComputeIdentifier(second, store.Hash, knownChildIdentifiers: null);
        Assert.AreEqual(idFirst, idSecond);

        HypertrieNode canonicalFirst = store.Intern(idFirst, first);
        HypertrieNode canonicalSecond = store.Intern(idSecond, second);

        Assert.AreNotSame(canonicalFirst, canonicalSecond);
        Assert.AreEqual(2, store.Count);

        //Re-presenting either with a content-equal node still walks
        //the chain and returns the right canonical reference.
        HypertrieNode firstAgain = MakeDepth1Leaf([1, 2]);
        HypertrieNode secondAgain = MakeDepth1Leaf([3, 4]);
        Assert.AreSame(canonicalFirst, store.Intern(idFirst, firstAgain));
        Assert.AreSame(canonicalSecond, store.Intern(idSecond, secondAgain));
        Assert.AreEqual(2, store.Count);
    }

    [TestMethod]
    public void ContentEqualityAcrossRepresentationsIsRespected()
    {
        using NodeStore store = new(VeritasHashing.Default);

        HypertrieNode viaForward = MakeDepth1Leaf([7, 13]);
        HypertrieNode viaReverse = MakeDepth1Leaf([13, 7]);

        NodeIdentifier id = HypertrieOps.ComputeIdentifier(viaForward, store.Hash, knownChildIdentifiers: null);
        Assert.AreEqual(id, HypertrieOps.ComputeIdentifier(viaReverse, store.Hash, knownChildIdentifiers: null));

        HypertrieNode canonical = store.Intern(id, viaForward);
        HypertrieNode rediscovered = store.Intern(id, viaReverse);

        Assert.AreSame(canonical, rediscovered);
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void InnerNodeEqualityChecksReferenceEqualChildren()
    {
        //Two depth-2 nodes with the same edge map structure pointing
        //to the same canonical depth-1 child must dedup.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode leaf = MakeDepth1Leaf([100, 200]);
        NodeIdentifier leafId = HypertrieOps.ComputeIdentifier(leaf, store.Hash, knownChildIdentifiers: null);
        HypertrieNode canonicalLeaf = store.Intern(leafId, leaf);

        Dictionary<HypertrieNode, NodeIdentifier> childIds = new() { [canonicalLeaf] = leafId };

        HypertrieNode parentSharing = MakeDepth2Inner([(50, canonicalLeaf)]);
        HypertrieNode parentSharingAgain = MakeDepth2Inner([(50, canonicalLeaf)]);
        NodeIdentifier parentId = HypertrieOps.ComputeIdentifier(parentSharing, store.Hash, childIds);

        HypertrieNode canonicalParent = store.Intern(parentId, parentSharing);
        HypertrieNode rediscovered = store.Intern(parentId, parentSharingAgain);

        Assert.AreSame(canonicalParent, rediscovered);
    }

    [TestMethod]
    public void DifferentContentDoesNotDedupAtSameIdentifierProbabilistically()
    {
        //Without collision verification the store would conflate
        //these because they have the same number of entries; the
        //test confirms verification distinguishes them.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode left = MakeDepth1Leaf([1, 2]);
        HypertrieNode right = MakeDepth1Leaf([1, 3]);

        NodeIdentifier idLeft = HypertrieOps.ComputeIdentifier(left, store.Hash, knownChildIdentifiers: null);
        NodeIdentifier idRight = HypertrieOps.ComputeIdentifier(right, store.Hash, knownChildIdentifiers: null);

        HypertrieNode leftCanonical = store.Intern(idLeft, left);
        HypertrieNode rightCanonical = store.Intern(idRight, right);

        Assert.AreNotSame(leftCanonical, rightCanonical);
        Assert.AreEqual(2, store.Count);
    }

    [TestMethod]
    public void NullCandidateIsRejected()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.Throws<ArgumentNullException>(() => store.Intern(NodeIdentifier.Empty, null!));
    }

    [TestMethod]
    public void NullHashIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NodeStore(null!));
    }

    private static HypertrieNode MakeDepth1Leaf(ReadOnlySpan<uint> keys)
    {
        HypertrieNode leaf = new(1);
        BuildPools pools = BuildPools.CreateDefault();
        foreach(uint key in keys)
        {
            EdgeMap.InsertOrReplace(ref leaf.Edge0, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        return leaf;
    }

    private static HypertrieNode MakeDepth2Inner(ReadOnlySpan<(uint Key, HypertrieNode Child)> entries)
    {
        //Build a depth-2 node where the first edge map carries the
        //given entries; tests only exercise the first position.
        HypertrieNode inner = new(2);
        BuildPools pools = BuildPools.CreateDefault();
        foreach((uint key, HypertrieNode child) in entries)
        {
            EdgeMap.InsertOrReplace(ref inner.Edge0, key, child, pools, InlineKeyLookups.Scalar);
        }

        return inner;
    }

    //A constant hash used to force every node to collide on
    //identifier, exercising the collision-chain path explicitly.
    //Production code never uses such a hash; this is for testing
    //only.
    private static ulong StaticHash(ReadOnlySpan<byte> bytes) => 0xDEADBEEFCAFEBABEUL;
}
