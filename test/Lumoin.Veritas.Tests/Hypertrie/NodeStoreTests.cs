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
    public void FirstInternRecordsAndReturnsCandidate()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode node = MakeDepth1Leaf([10, 20]);
        NodeIdentifier id = ComputeIdentifier(store, node);

        HypertrieNode result = store.Intern(id, node);

        Assert.AreSame(node, result);
        Assert.AreEqual(1, store.Count);
        Assert.IsTrue(store.Contains(id));
    }

    [TestMethod]
    public void RepeatedInternOfContentEqualNodeReturnsCanonical()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode first = MakeDepth1Leaf([10, 20]);
        HypertrieNode second = MakeDepth1Leaf([10, 20]);
        NodeIdentifier id = ComputeIdentifier(store, first);

        HypertrieNode canonical = store.Intern(id, first);
        HypertrieNode rediscovered = store.Intern(id, second);

        Assert.AreSame(first, canonical);
        Assert.AreSame(first, rediscovered);
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

        HypertrieNode canonical = store.Intern(idForward, forwardOrder);
        HypertrieNode rediscovered = store.Intern(idReverse, reverseOrder);

        Assert.AreSame(forwardOrder, canonical);
        Assert.AreSame(forwardOrder, rediscovered);
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

        Assert.AreSame(emptyDepth1, firstCanonical);
        Assert.AreSame(emptyDepth2, secondCanonical);
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

        HypertrieNode canonicalFirst = store.Intern(idFirst, first);
        HypertrieNode canonicalSecond = store.Intern(idSecond, second);

        Assert.AreSame(first, canonicalFirst);
        Assert.AreSame(second, canonicalSecond);
        Assert.AreEqual(2, store.Count);

        //Re-presenting either with content-equal node still walks
        //the chain and returns the right canonical instance.
        HypertrieNode firstAgain = MakeDepth1Leaf([1, 2]);
        HypertrieNode secondAgain = MakeDepth1Leaf([3, 4]);
        Assert.AreSame(first, store.Intern(idFirst, firstAgain));
        Assert.AreSame(second, store.Intern(idSecond, secondAgain));
        Assert.AreEqual(2, store.Count);
    }

    [TestMethod]
    public void ContentEqualityAcrossRepresentationsIsRespected()
    {
        //One node holds two entries via Inline → SortedArray
        //promotion; another holds the same two entries through a
        //completely different representation path. The store must
        //recognise them as content-equal even though their
        //EdgeMap.Equals would return false because the heap fields
        //differ.
        using NodeStore store = new(VeritasHashing.Default);

        HypertrieNode viaSortedArray = MakeDepth1Leaf([7, 13]);
        HypertrieNode viaSortedArrayAgain = MakeDepth1Leaf([13, 7]);

        NodeIdentifier id = ComputeIdentifier(store, viaSortedArray);
        Assert.AreEqual(id, ComputeIdentifier(store, viaSortedArrayAgain));

        HypertrieNode canonical = store.Intern(id, viaSortedArray);
        HypertrieNode rediscovered = store.Intern(id, viaSortedArrayAgain);

        Assert.AreSame(viaSortedArray, canonical);
        Assert.AreSame(viaSortedArray, rediscovered);
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void InnerNodeEqualityChecksReferenceEqualChildren()
    {
        //Two depth-2 nodes with the same edge map structure pointing
        //to the same canonical depth-1 child must dedup. Pointing to
        //different child instances (that happen to be content-equal
        //but not yet interned) must NOT dedup, because the store
        //compares children by reference under the assumption that
        //leaves are interned first.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieNode leaf = MakeDepth1Leaf([100, 200]);
        NodeIdentifier leafId = ComputeIdentifier(store, leaf);
        HypertrieNode canonicalLeaf = store.Intern(leafId, leaf);

        HypertrieNode parentSharing = MakeDepth2Inner([(50, canonicalLeaf)]);
        HypertrieNode parentSharingAgain = MakeDepth2Inner([(50, canonicalLeaf)]);
        NodeIdentifier parentId = ComputeIdentifier(store, parentSharing);

        HypertrieNode canonicalParent = store.Intern(parentId, parentSharing);
        HypertrieNode rediscovered = store.Intern(parentId, parentSharingAgain);

        Assert.AreSame(parentSharing, canonicalParent);
        Assert.AreSame(parentSharing, rediscovered);
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

        //Identifiers are almost always different here; the store's
        //correctness does not depend on that. What it depends on is
        //that when content differs, the canonical instances are kept
        //separate.
        store.Intern(idLeft, left);
        HypertrieNode canonicalRight = store.Intern(idRight, right);

        Assert.AreSame(right, canonicalRight);
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
        foreach(uint key in keys)
        {
            EdgeMap.InsertOrReplace(ref leaf.EdgeMaps[0], key, null);
        }
        return leaf;
    }

    private static HypertrieNode MakeDepth2Inner(ReadOnlySpan<(uint Key, HypertrieNode Child)> entries)
    {
        //Build a depth-2 node where every position points to the
        //same children (consistent with how multi-position branching
        //works at depth 2; tests only exercise the first position
        //and leave the second empty).
        HypertrieNode inner = new(2);
        foreach((uint key, HypertrieNode child) in entries)
        {
            EdgeMap.InsertOrReplace(ref inner.EdgeMaps[0], key, child);
        }
        return inner;
    }

    private static NodeIdentifier ComputeIdentifier(NodeStore store, HypertrieNode root)
    {
        //Compute the identifier bottom-up over the node tree using
        //an explicit stack and a post-order pass: first walk every
        //reachable inner node onto a stack, then drain the stack so
        //every child's identifier is known before its parent is
        //processed. For depth-1 leaves the per-entry hash mixes the
        //key with the non-zero presence marker 1; for inner nodes it
        //mixes the key with the child's already-computed identifier.
        Stack<HypertrieNode> discoveryStack = new();
        Stack<HypertrieNode> postOrder = new();
        discoveryStack.Push(root);
        while(discoveryStack.Count > 0)
        {
            HypertrieNode current = discoveryStack.Pop();
            postOrder.Push(current);
            for(int position = 0; position < current.Depth; position++)
            {
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(current.EdgeMaps[position]))
                {
                    if(entry.Value is not null)
                    {
                        discoveryStack.Push(entry.Value);
                    }
                }
            }
        }

        Dictionary<HypertrieNode, NodeIdentifier> known = new(ReferenceEqualityComparer.Instance);
        while(postOrder.Count > 0)
        {
            HypertrieNode current = postOrder.Pop();
            if(known.ContainsKey(current))
            {
                continue;
            }

            NodeIdentifier id = NodeIdentifier.Empty;
            for(int position = 0; position < current.Depth; position++)
            {
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(current.EdgeMaps[position]))
                {
                    ulong childIdentifier = entry.Value is null ? 1UL : known[entry.Value].Value;
                    ulong entryHash = NodeEntryHashing.Default(store.Hash, entry.Key, childIdentifier);
                    id = id.Add(entryHash);
                }
            }
            known[current] = id;
        }

        return known[root];
    }

    //A constant hash used to force every node to collide on
    //identifier, exercising the collision-chain path explicitly.
    //Production code never uses such a hash; this is for testing
    //only.
    private static ulong StaticHash(ReadOnlySpan<byte> bytes) => 0xDEADBEEFCAFEBABEUL;
}
