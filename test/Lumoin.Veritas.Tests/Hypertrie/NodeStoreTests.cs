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
    public void FirstInternRecordsAndReturnsHandle()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode node = MakeDepth1Leaf([10, 20]);
        NodeIdentifier id = ComputeIdentifier(store, node);

        NodeHandle handle = store.Intern(id, node);

        Assert.IsFalse(handle.IsNone);
        Assert.AreEqual(1, store.Count);
        Assert.IsTrue(store.Contains(id));

        HypertrieNode resolved = store.GetByHandle(handle);
        Assert.AreEqual(node.Depth, resolved.Depth);
    }

    [TestMethod]
    public void GetByHandleReturnsStructurallyEqualNode()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode node = MakeDepth1Leaf([5, 10, 15]);
        NodeIdentifier id = ComputeIdentifier(store, node);

        NodeHandle handle = store.Intern(id, node);
        HypertrieNode resolved = store.GetByHandle(handle);

        Assert.AreEqual(node.Depth, resolved.Depth);
        Assert.HasCount(resolved.EdgeMaps.Length, node.EdgeMaps);
        Assert.AreEqual(EdgeMap.Count(in node.EdgeMaps[0]), EdgeMap.Count(in resolved.EdgeMaps[0]));
    }

    [TestMethod]
    public void GetByHandleOnNoneThrows()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetByHandle(NodeHandle.None));
    }

    [TestMethod]
    public void RepeatedInternOfContentEqualNodeReturnsSameHandle()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode first = MakeDepth1Leaf([10, 20]);
        HypertrieNode second = MakeDepth1Leaf([10, 20]);
        NodeIdentifier id = ComputeIdentifier(store, first);

        NodeHandle canonical = store.Intern(id, first);
        NodeHandle rediscovered = store.Intern(id, second);

        Assert.AreEqual(canonical, rediscovered);
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void InsertionOrderDoesNotAffectCanonicalIdentity()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode forwardOrder = MakeDepth1Leaf([1, 2, 3, 4, 5]);
        HypertrieNode reverseOrder = MakeDepth1Leaf([5, 4, 3, 2, 1]);
        NodeIdentifier idForward = ComputeIdentifier(store, forwardOrder);
        NodeIdentifier idReverse = ComputeIdentifier(store, reverseOrder);

        Assert.AreEqual(idForward, idReverse);

        NodeHandle canonical = store.Intern(idForward, forwardOrder);
        NodeHandle rediscovered = store.Intern(idReverse, reverseOrder);

        Assert.AreEqual(canonical, rediscovered);
    }

    [TestMethod]
    public void DifferentDepthsAreCanonicalisedSeparately()
    {
        //Two empty nodes of different depths share the empty
        //identifier (no entries to hash) but differ in depth, so
        //the store must treat them as a collision and chain them.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode emptyDepth1 = HypertrieNode.Create(1);
        HypertrieNode emptyDepth2 = HypertrieNode.Create(2);

        NodeHandle firstCanonical = store.Intern(NodeIdentifier.Empty, emptyDepth1);
        NodeHandle secondCanonical = store.Intern(NodeIdentifier.Empty, emptyDepth2);

        Assert.AreNotEqual(firstCanonical, secondCanonical);
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

        NodeIdentifier idFirst = ComputeIdentifier(store, first);
        NodeIdentifier idSecond = ComputeIdentifier(store, second);
        Assert.AreEqual(idFirst, idSecond);

        NodeHandle canonicalFirst = store.Intern(idFirst, first);
        NodeHandle canonicalSecond = store.Intern(idSecond, second);

        Assert.AreNotEqual(canonicalFirst, canonicalSecond);
        Assert.AreEqual(2, store.Count);

        //Re-presenting either with a content-equal node still walks
        //the chain and returns the right canonical handle.
        HypertrieNode firstAgain = MakeDepth1Leaf([1, 2]);
        HypertrieNode secondAgain = MakeDepth1Leaf([3, 4]);
        Assert.AreEqual(canonicalFirst, store.Intern(idFirst, firstAgain));
        Assert.AreEqual(canonicalSecond, store.Intern(idSecond, secondAgain));
        Assert.AreEqual(2, store.Count);
    }

    [TestMethod]
    public void ContentEqualityAcrossRepresentationsIsRespected()
    {
        //One node holds two entries built in one order; another
        //holds the same two entries built in reverse. The store
        //must recognise them as content-equal regardless of the
        //underlying EdgeMap representation details.
        using NodeStore store = new(VeritasHashing.Default);

        HypertrieNode viaForward = MakeDepth1Leaf([7, 13]);
        HypertrieNode viaReverse = MakeDepth1Leaf([13, 7]);

        NodeIdentifier id = ComputeIdentifier(store, viaForward);
        Assert.AreEqual(id, ComputeIdentifier(store, viaReverse));

        NodeHandle canonical = store.Intern(id, viaForward);
        NodeHandle rediscovered = store.Intern(id, viaReverse);

        Assert.AreEqual(canonical, rediscovered);
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void InnerNodeEqualityChecksHandleEqualChildren()
    {
        //Two depth-2 nodes with the same edge map structure pointing
        //to the same canonical depth-1 child handle must dedup.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode leaf = MakeDepth1Leaf([100, 200]);
        NodeIdentifier leafId = ComputeIdentifier(store, leaf);
        NodeHandle canonicalLeafHandle = store.Intern(leafId, leaf);

        HypertrieNode parentSharing = MakeDepth2Inner([(50, canonicalLeafHandle)]);
        HypertrieNode parentSharingAgain = MakeDepth2Inner([(50, canonicalLeafHandle)]);
        NodeIdentifier parentId = ComputeInnerIdentifier(store, parentSharing, leafId.Value);

        NodeHandle canonicalParent = store.Intern(parentId, parentSharing);
        NodeHandle rediscovered = store.Intern(parentId, parentSharingAgain);

        Assert.AreEqual(canonicalParent, rediscovered);
    }

    [TestMethod]
    public void DifferentContentDoesNotDedupEvenAtSameIdentifierProbabilistically()
    {
        //Without collision verification the store would conflate
        //these because they have the same number of entries; the
        //test confirms verification distinguishes them.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode left = MakeDepth1Leaf([1, 2]);
        HypertrieNode right = MakeDepth1Leaf([1, 3]);

        NodeIdentifier idLeft = ComputeIdentifier(store, left);
        NodeIdentifier idRight = ComputeIdentifier(store, right);

        NodeHandle leftHandle = store.Intern(idLeft, left);
        NodeHandle rightHandle = store.Intern(idRight, right);

        Assert.AreNotEqual(leftHandle, rightHandle);
        Assert.AreEqual(2, store.Count);
    }

    [TestMethod]
    public void DefaultCandidateIsRejected()
    {
        using NodeStore store = new(VeritasHashing.Default);

        //default(HypertrieNode) has a null EdgeMaps array; the store
        //rejects it as a malformed candidate.
        Assert.Throws<ArgumentException>(() => store.Intern(NodeIdentifier.Empty, default));
    }

    [TestMethod]
    public void NullHashIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NodeStore(null!));
    }

    private static HypertrieNode MakeDepth1Leaf(ReadOnlySpan<uint> keys)
    {
        HypertrieNode leaf = HypertrieNode.Create(1);
        BuildPools pools = BuildPools.CreateDefault();
        foreach(uint key in keys)
        {
            EdgeMap.InsertOrReplace(ref leaf.EdgeMaps[0], key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        return leaf;
    }

    private static HypertrieNode MakeDepth2Inner(ReadOnlySpan<(uint Key, NodeHandle Child)> entries)
    {
        //Build a depth-2 node where the first edge map carries the
        //given entries; tests only exercise the first position.
        HypertrieNode inner = HypertrieNode.Create(2);
        BuildPools pools = BuildPools.CreateDefault();
        foreach((uint key, NodeHandle child) in entries)
        {
            EdgeMap.InsertOrReplace(ref inner.EdgeMaps[0], key, child, pools, InlineKeyLookups.Scalar);
        }

        return inner;
    }

    //Compute identifier for a depth-1 leaf where every entry is a
    //presence marker (child handle is None, hashed as 1UL).
    private static NodeIdentifier ComputeIdentifier(NodeStore store, HypertrieNode root)
    {
        NodeIdentifier id = NodeIdentifier.Empty;
        for(int position = 0; position < root.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(root.EdgeMaps[position]))
            {
                //At depth-1 leaves the child handle is None — the
                //historical algorithm mixed the key with a non-zero
                //presence marker (1UL).
                ulong childIdentifier = entry.Value.IsNone ? 1UL : entry.Value.Encoded;
                ulong entryHash = NodeEntryHashing.Default(store.Hash, entry.Key, childIdentifier);
                id = id.Add(entryHash);
            }
        }

        return id;
    }

    //Compute identifier for a depth-2 inner whose entries all
    //point at a single previously-computed child identifier. Used
    //by tests that build a one-level-deep parent over a single
    //already-interned leaf.
    private static NodeIdentifier ComputeInnerIdentifier(NodeStore store, HypertrieNode inner, ulong childIdentifier)
    {
        NodeIdentifier id = NodeIdentifier.Empty;
        for(int position = 0; position < inner.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(inner.EdgeMaps[position]))
            {
                //Every entry points at the same child here.
                ulong useChild = entry.Value.IsNone ? 1UL : childIdentifier;
                ulong entryHash = NodeEntryHashing.Default(store.Hash, entry.Key, useChild);
                id = id.Add(entryHash);
            }
        }

        return id;
    }

    //A constant hash used to force every node to collide on
    //identifier, exercising the collision-chain path explicitly.
    //Production code never uses such a hash; this is for testing
    //only.
    private static ulong StaticHash(ReadOnlySpan<byte> bytes) => 0xDEADBEEFCAFEBABEUL;
}
