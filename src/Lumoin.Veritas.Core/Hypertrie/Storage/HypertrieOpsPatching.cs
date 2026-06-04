using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Static operations that produce a new depth-3 hypertrie root by
/// patching an existing one with a net delta of additions and
/// removals. The operations rebuild only the nodes on the descent
/// paths touched by the delta; subtrees the delta does not reach
/// remain shared between the input snapshot and the produced one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where this fits.</b> <see cref="HypertrieOps.BuildBottomUp"/>
/// constructs a root from scratch given a triple set;
/// <see cref="ApplyDelta"/> constructs a root incrementally given
/// a base root and a delta. Both go through the same
/// <see cref="NodeStore.Intern"/> and converge on the same
/// canonical depth-3 root for any pair of inputs that resolve to
/// the same final triple set. The parity is exercised in
/// dedicated tests; here it is enforced by structure: every node
/// the patcher produces is built from scratch using
/// <see cref="EdgeMap.InsertOrReplace"/> and interned through the
/// same store, so the resulting <c>EdgeMap.Kind</c> mirrors what
/// the build path produces for the same entry set
/// (<c>Empty</c> for zero entries, <c>Inline</c> for one,
/// <c>SortedArray</c> for two or more).
/// </para>
/// <para>
/// <b>Three descent paths.</b> The depth-3 root has three edge
/// maps — one per starting position. The root's S-position edge
/// map's child at key <c>s</c> is a depth-2 node whose first edge
/// map is keyed by the predicate (with depth-1 leaves indexed by
/// object) and whose second edge map is keyed by the object
/// (with depth-1 leaves indexed by predicate). The P- and
/// O-position edge maps have analogous shapes; the precise
/// inner-key ordering is fixed by
/// <see cref="HypertrieOps.BuildBottomUp"/> and the patcher
/// matches it exactly so the canonical interned instances align.
/// </para>
/// <para>
/// <b>Per-edit cost.</b> A single triple add or remove touches
/// six depth-1 leaves — two leaves per descent path, one keyed by
/// the inner-1 position and one keyed by the inner-2 position —
/// plus three depth-2 nodes (one per descent path) plus the root.
/// All ten of those nodes are re-interned. The remainder of the
/// hypertrie is shared with the input.
/// </para>
/// <para>
/// <b>Batching.</b> Edits arrive as collections, not one at a
/// time. Within one <see cref="ApplyDelta"/> call the patcher
/// groups every edit by its outer key for each descent path,
/// processes the affected (path, outer-key) buckets once each,
/// and produces one new D2 node per bucket — not one D2 node per
/// edit. A session committing N edits all sharing the same outer
/// keys re-interns 3 + bucket_count D2s and one root, not 3N
/// D2s and N roots.
/// </para>
/// <para>
/// <b>Delta filtering.</b> The caller supplies the literal
/// session edits; the patcher filters them against the base
/// snapshot before applying. An add of a triple already present
/// is a no-op; a remove of a triple not present is a no-op. The
/// resulting effective additions and effective removals are
/// returned alongside the new root and identifier so the caller
/// can record them on the journal entry — the journal sees what
/// actually changed, not what the session asked for.
/// </para>
/// </remarks>
public static class HypertrieOpsPatching
{
    private const int PositionSubject = 0;
    private const int PositionPredicate = 1;
    private const int PositionObject = 2;

    /// <summary>
    /// Produces a new depth-3 hypertrie root from
    /// <paramref name="baseSnapshot"/> by applying the net delta
    /// of <paramref name="additions"/> and
    /// <paramref name="removals"/>. The base's root and every
    /// canonical node it shares with the new root remain
    /// untouched.
    /// </summary>
    /// <param name="baseSnapshot">The snapshot the delta applies to. Its root must be a depth-3 node.</param>
    /// <param name="additions">The session's literal additions. Triples already present in the base are filtered out before patching.</param>
    /// <param name="removals">The session's literal removals. Triples not present in the base are filtered out before patching.</param>
    /// <param name="store">The intern table to publish new nodes through.</param>
    /// <param name="pools">The pools bundle used for EdgeMap promotion and grow.</param>
    /// <returns>
    /// The new root and its identifier, plus the effective
    /// additions and removals (post-filter). When the effective
    /// delta is empty, the returned root and identifier are the
    /// base's; both effective lists are empty.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseSnapshot"/>'s root is not depth-3.</exception>
    public static ApplyDeltaResult ApplyDelta(
        HypertrieSnapshot baseSnapshot,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals,
        NodeStore store,
        BuildPools pools)
    {
        ArgumentNullException.ThrowIfNull(baseSnapshot);
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);
        ArgumentNullException.ThrowIfNull(store);

        NodeHandle baseRootHandle = baseSnapshot.Root;
        HypertrieNode baseRoot = store.GetByHandle(baseRootHandle);
        if(baseRoot.Depth != 3)
        {
            throw new ArgumentException(
                $"ApplyDelta must start at a depth-3 root; got depth={baseRoot.Depth}.",
                nameof(baseSnapshot));
        }

        //Filter the literal edits against the base.
        List<EncodedTriple> effectiveAdds = new(additions.Count);
        foreach(EncodedTriple triple in additions)
        {
            if(!IsPresent(baseRoot, store, triple))
            {
                effectiveAdds.Add(triple);
            }
        }

        List<EncodedTriple> effectiveRemoves = new(removals.Count);
        foreach(EncodedTriple triple in removals)
        {
            if(IsPresent(baseRoot, store, triple))
            {
                effectiveRemoves.Add(triple);
            }
        }

        if(effectiveAdds.Count == 0 && effectiveRemoves.Count == 0)
        {
            return new ApplyDeltaResult(
                Root: baseRootHandle,
                Id: baseSnapshot.Id,
                EffectiveAdditions: [],
                EffectiveRemovals: []);
        }

        //Patch each of the three descent paths. The descent path
        //shape is fixed by HypertrieOps.BuildBottomUp:
        //  S-first: outer=S, inner1=P, inner2=O
        //  P-first: outer=P, inner1=S, inner2=O
        //  O-first: outer=O, inner1=S, inner2=P
        //Each path yields a fresh root edge map.
        EdgeMap newSubjectEdgeMap = PatchPath(
            in baseRoot.EdgeMaps[PositionSubject],
            effectiveAdds,
            effectiveRemoves,
            outer: PositionSubject,
            inner1: PositionPredicate,
            inner2: PositionObject,
            store,
            pools);

        EdgeMap newPredicateEdgeMap = PatchPath(
            in baseRoot.EdgeMaps[PositionPredicate],
            effectiveAdds,
            effectiveRemoves,
            outer: PositionPredicate,
            inner1: PositionSubject,
            inner2: PositionObject,
            store,
            pools);

        EdgeMap newObjectEdgeMap = PatchPath(
            in baseRoot.EdgeMaps[PositionObject],
            effectiveAdds,
            effectiveRemoves,
            outer: PositionObject,
            inner1: PositionSubject,
            inner2: PositionPredicate,
            store,
            pools);

        //Assemble the new root. EdgeMap is a struct; assigning
        //into the array slots is a copy of the value type, so the
        //heap arrays produced by PatchPath flow into the new root
        //without aliasing the path-local locals.
        HypertrieNode newRoot = HypertrieNode.Create(3);
        newRoot.EdgeMaps[PositionSubject] = newSubjectEdgeMap;
        newRoot.EdgeMaps[PositionPredicate] = newPredicateEdgeMap;
        newRoot.EdgeMaps[PositionObject] = newObjectEdgeMap;

        Dictionary<NodeHandle, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(newRoot, store, store.Hash);
        NodeIdentifier newRootId = ComputeIdentifier(newRoot, store, store.Hash, childIdentifiers);
        NodeHandle canonicalRootHandle = store.Intern(newRootId, newRoot);

        return new ApplyDeltaResult(
            Root: canonicalRootHandle,
            Id: newRootId,
            EffectiveAdditions: effectiveAdds,
            EffectiveRemovals: effectiveRemoves);
    }

    //Patches one of the three descent paths off the root. Walks
    //the base path's outer edge map, building a new edge map by:
    //  - groupings of edits affecting outer keys present in the base
    //    drive PatchD2 to produce updated D2 nodes (or NodeHandle.None
    //    when the bucket becomes empty);
    //  - groupings of edits affecting outer keys NOT present in the
    //    base produce fresh D2 nodes from scratch;
    //  - outer keys present in the base with no edits affecting them
    //    are copied through as references to the existing canonical
    //    D2 nodes — those subtrees are shared.
    private static EdgeMap PatchPath(
        in EdgeMap baseOuterEdgeMap,
        List<EncodedTriple> effectiveAdds,
        List<EncodedTriple> effectiveRemoves,
        int outer,
        int inner1,
        int inner2,
        NodeStore store,
        BuildPools pools)
    {
        //Group edits by their outer key. Each bucket carries the
        //(inner1, inner2) projections of the affected triples plus
        //a kind flag indicating whether the edit is an add or a
        //remove. The dictionary's value is keyed by inner1, then by
        //inner2, and the bool says "true=add, false=remove" because
        //a given (outer, inner1, inner2) triple is added xor removed
        //inside one ApplyDelta call (the EditBuffer layer above
        //already collapsed the two on a same-triple basis).
        Dictionary<uint, List<EditEntry>> editsByOuter = [];
        foreach(EncodedTriple triple in effectiveAdds)
        {
            uint outerKey = ValueAt(triple, outer);
            uint innerKey1 = ValueAt(triple, inner1);
            uint innerKey2 = ValueAt(triple, inner2);

            if(!editsByOuter.TryGetValue(outerKey, out List<EditEntry>? bucket))
            {
                bucket = [];
                editsByOuter[outerKey] = bucket;
            }
            bucket.Add(new EditEntry(innerKey1, innerKey2, IsAddition: true));
        }

        foreach(EncodedTriple triple in effectiveRemoves)
        {
            uint outerKey = ValueAt(triple, outer);
            uint innerKey1 = ValueAt(triple, inner1);
            uint innerKey2 = ValueAt(triple, inner2);

            if(!editsByOuter.TryGetValue(outerKey, out List<EditEntry>? bucket))
            {
                bucket = [];
                editsByOuter[outerKey] = bucket;
            }
            bucket.Add(new EditEntry(innerKey1, innerKey2, IsAddition: false));
        }

        EdgeMap newEdgeMap = default;

        //First pass: walk the base outer edge map. Every existing
        //outer key either has edits (rebuild its D2) or has none
        //(carry its D2 over unchanged).
        foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(baseOuterEdgeMap))
        {
            uint outerKey = entry.Key;
            NodeHandle oldD2Handle = entry.Value;

            if(editsByOuter.TryGetValue(outerKey, out List<EditEntry>? bucket))
            {
                NodeHandle newD2Handle = PatchD2(oldD2Handle, bucket, store, pools);
                if(!newD2Handle.IsNone)
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, outerKey, newD2Handle, pools, pools.InlineLookup);
                }
                editsByOuter.Remove(outerKey);
            }
            else
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, outerKey, oldD2Handle, pools, pools.InlineLookup);
            }
        }

        //Second pass: outer keys that survived the dictionary erase
        //above are pure-additions buckets — the base did not have
        //the outer key, so every edit in the bucket must be an add
        //(removes against absent triples were filtered earlier).
        //Build a fresh D2 for each.
        foreach(KeyValuePair<uint, List<EditEntry>> remaining in editsByOuter)
        {
            NodeHandle newD2Handle = PatchD2(oldD2Handle: NodeHandle.None, remaining.Value, store, pools);
            if(!newD2Handle.IsNone)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, remaining.Key, newD2Handle, pools, pools.InlineLookup);
            }
        }

        return newEdgeMap;
    }

    //Patches one depth-2 node for a single (outer-key) bucket.
    //Builds a fresh D2 from scratch using EdgeMap.InsertOrReplace,
    //which means the resulting EdgeMap kinds follow the canonical
    //promotion ladder for the post-edit entry counts. Returns
    //NodeHandle.None when both edge maps end up empty — that is the
    //signal to the caller that the outer key should be dropped from
    //the root edge map entirely.
    private static NodeHandle PatchD2(NodeHandle oldD2Handle, List<EditEntry> bucket, NodeStore store, BuildPools pools)
    {
        //Re-bucket edits by inner1 (drives the first edge map's
        //leaves) and by inner2 (drives the second edge map's
        //leaves). Same edits, two views; mirrors the symmetry the
        //build path uses.
        Dictionary<uint, List<LeafEdit>> byInner1 = [];
        Dictionary<uint, List<LeafEdit>> byInner2 = [];

        foreach(EditEntry edit in bucket)
        {
            if(!byInner1.TryGetValue(edit.Inner1, out List<LeafEdit>? leaf1))
            {
                leaf1 = [];
                byInner1[edit.Inner1] = leaf1;
            }
            leaf1.Add(new LeafEdit(edit.Inner2, edit.IsAddition));

            if(!byInner2.TryGetValue(edit.Inner2, out List<LeafEdit>? leaf2))
            {
                leaf2 = [];
                byInner2[edit.Inner2] = leaf2;
            }
            leaf2.Add(new LeafEdit(edit.Inner1, edit.IsAddition));
        }

        //Build the two new edge maps. An SEN2 base materialises as
        //the equivalent pair of single-entry maps with SEN
        //children — the exact shape the build path would have
        //produced for the same content before collapsing it.
        EdgeMap baseEdgeMap0 = default;
        EdgeMap baseEdgeMap1 = default;

        if(oldD2Handle.IsSingleEntryPair)
        {
            (uint pairFirst, uint pairSecond) = store.GetPair(oldD2Handle);
            EdgeMap.InsertOrReplace(ref baseEdgeMap0, pairFirst, NodeHandle.ForSingleEntry(pairSecond), pools, pools.InlineLookup);
            EdgeMap.InsertOrReplace(ref baseEdgeMap1, pairSecond, NodeHandle.ForSingleEntry(pairFirst), pools, pools.InlineLookup);
        }
        else if(!oldD2Handle.IsNone)
        {
            HypertrieNode oldD2 = store.GetByHandle(oldD2Handle);
            baseEdgeMap0 = oldD2.EdgeMaps[0];
            baseEdgeMap1 = oldD2.EdgeMaps[1];
        }

        EdgeMap newEdgeMap0 = PatchD2EdgeMap(baseEdgeMap0, byInner1, store, pools);
        EdgeMap newEdgeMap1 = PatchD2EdgeMap(baseEdgeMap1, byInner2, store, pools);

        if(EdgeMap.Count(in newEdgeMap0) == 0 && EdgeMap.Count(in newEdgeMap1) == 0)
        {
            return NodeHandle.None;
        }

        //Single-entry result: collapse into the pair arena, exactly
        //as the build path does, so build and patch converge on the
        //same representation (and therefore the same canonical
        //parents) for the same content. Both maps holding exactly
        //one entry is equivalent to the D2 holding one
        //(inner1, inner2) pair; the 1-entry maps are Inline-tier
        //and own no rentals.
        if(EdgeMap.Count(in newEdgeMap0) == 1 && EdgeMap.Count(in newEdgeMap1) == 1)
        {
            uint soleInner1 = EdgeMap.InlineKeysSpan(in newEdgeMap0)[0];
            uint soleInner2 = EdgeMap.InlineKeysSpan(in newEdgeMap1)[0];

            return store.AllocateSingleEntryPair(soleInner1, soleInner2);
        }

        HypertrieNode newD2 = HypertrieNode.Create(2);
        newD2.EdgeMaps[0] = newEdgeMap0;
        newD2.EdgeMaps[1] = newEdgeMap1;

        Dictionary<NodeHandle, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(newD2, store, store.Hash);
        NodeIdentifier identifier = ComputeIdentifier(newD2, store, store.Hash, childIdentifiers);
        return store.Intern(identifier, newD2);
    }

    //Patches one of the two edge maps inside a D2 node. The
    //structure mirrors PatchPath at one level shallower: walk the
    //base edge map, rebuild a leaf for every key with edits, copy
    //through every key without edits, then process pure-addition
    //leaves not present in the base.
    private static EdgeMap PatchD2EdgeMap(
        EdgeMap baseEdgeMap,
        Dictionary<uint, List<LeafEdit>> editsByLeafKey,
        NodeStore store,
        BuildPools pools)
    {
        EdgeMap newEdgeMap = default;

        //Snapshot pending leaf-edit keys so we can drop entries as
        //we consume them — the dictionary itself is mutated inside
        //the loop. Iteration order over the base edge map is
        //independent of dictionary mutation.
        foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(baseEdgeMap))
        {
            uint leafKey = entry.Key;
            NodeHandle oldLeafHandle = entry.Value;

            if(editsByLeafKey.TryGetValue(leafKey, out List<LeafEdit>? edits))
            {
                NodeHandle newLeafHandle = PatchLeaf(oldLeafHandle, edits, store, pools);
                if(!newLeafHandle.IsNone)
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, leafKey, newLeafHandle, pools, pools.InlineLookup);
                }
                editsByLeafKey.Remove(leafKey);
            }
            else
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, leafKey, oldLeafHandle, pools, pools.InlineLookup);
            }
        }

        foreach(KeyValuePair<uint, List<LeafEdit>> remaining in editsByLeafKey)
        {
            NodeHandle newLeafHandle = PatchLeaf(oldLeafHandle: NodeHandle.None, remaining.Value, store, pools);
            if(!newLeafHandle.IsNone)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, remaining.Key, newLeafHandle, pools, pools.InlineLookup);
            }
        }

        return newEdgeMap;
    }

    //Patches one depth-1 leaf. Returns None when no entries survive,
    //a SEN-encoded handle when exactly one entry survives, or an
    //interned FN leaf handle when two or more entries survive. The
    //old leaf may itself be None, SEN, or FN.
    private static NodeHandle PatchLeaf(NodeHandle oldLeafHandle, List<LeafEdit> edits, NodeStore store, BuildPools pools)
    {
        //Materialise the removed keys for O(1) membership in the
        //old-key copy loop.
        HashSet<uint> removedKeys = [];
        foreach(LeafEdit edit in edits)
        {
            if(!edit.IsAddition)
            {
                removedKeys.Add(edit.Key);
            }
        }

        EdgeMap newEdgeMap = default;

        if(oldLeafHandle.IsSingleEntry)
        {
            uint oldKey = oldLeafHandle.SingleEntryKey;
            if(!removedKeys.Contains(oldKey))
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, oldKey, NodeHandle.None, pools, pools.InlineLookup);
            }
        }
        else if(!oldLeafHandle.IsNone)
        {
            HypertrieNode oldLeaf = store.GetByHandle(oldLeafHandle);
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(oldLeaf.EdgeMaps[0]))
            {
                if(!removedKeys.Contains(entry.Key))
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, entry.Key, NodeHandle.None, pools, pools.InlineLookup);
                }
            }
        }

        foreach(LeafEdit edit in edits)
        {
            if(edit.IsAddition)
            {
                //InsertOrReplace is idempotent on duplicate keys, so
                //adding a key that the base also carried (which the
                //pre-filter against IsPresent should have excluded
                //anyway) is harmless.
                EdgeMap.InsertOrReplace(ref newEdgeMap, edit.Key, NodeHandle.None, pools, pools.InlineLookup);
            }
        }

        int finalCount = EdgeMap.Count(in newEdgeMap);

        if(finalCount == 0)
        {
            return NodeHandle.None;
        }

        if(finalCount == 1)
        {
            //SEN encoding: the surviving key lives inline in the
            //parent's slot. The newEdgeMap we built holds the
            //surviving key in the Inline tier (no pool rentals at
            //count == 1), so leaving it to GC frees no rentals.
            uint soleKey = EdgeMap.InlineKeysSpan(in newEdgeMap)[0];

            return NodeHandle.ForSingleEntry(soleKey);
        }

        HypertrieNode newLeaf = HypertrieNode.Create(1);
        newLeaf.EdgeMaps[0] = newEdgeMap;

        NodeIdentifier identifier = ComputeIdentifier(newLeaf, store, store.Hash, knownChildIdentifiers: null);

        return store.Intern(identifier, newLeaf);
    }

    //Walks the depth-3 hypertrie rooted at `root` checking whether
    //`triple` is present. Reuses HypertrieOps.Match's S-first
    //descent contract: descend the subject edge map, then the
    //predicate edge map of the resulting D2, then check the leaf
    //for the object. The leaf may be FN (real arena node) or SEN
    //(inline single key in the parent's slot).
    private static bool IsPresent(HypertrieNode root, NodeStore store, EncodedTriple triple)
    {
        uint s = triple.Subject.Encoded;
        uint p = triple.Predicate.Encoded;
        uint o = triple.Object.Encoded;
        InlineKeyLookup inlineLookup = InlineKeyLookups.Scalar;

        //Step 1: subject edge map at the root → D2 node keyed by P, O.
        if(!EdgeMap.TryGetChild(in root.EdgeMaps[PositionSubject], s, inlineLookup, out NodeHandle d2Handle) || d2Handle.IsNone)
        {
            return false;
        }

        //SEN2 mapping: the subject's whole subtree is one (P, O)
        //pair — the S-first inner positions in ascending order.
        if(d2Handle.IsSingleEntryPair)
        {
            (uint pairPredicate, uint pairObject) = store.GetPair(d2Handle);

            return pairPredicate == p && pairObject == o;
        }

        HypertrieNode d2 = store.GetByHandle(d2Handle);

        //Step 2: D2.EdgeMaps[0] (keyed by P) → leaf indexed by O.
        if(!EdgeMap.TryGetChild(in d2.EdgeMaps[0], p, inlineLookup, out NodeHandle leafHandle) || leafHandle.IsNone)
        {
            return false;
        }

        //Step 3a: SEN leaf — the single key is encoded inline.
        if(leafHandle.IsSingleEntry)
        {
            return leafHandle.SingleEntryKey == o;
        }

        //Step 3b: FN leaf → standard arena lookup for the object.
        HypertrieNode leaf = store.GetByHandle(leafHandle);

        return EdgeMap.TryGetChild(in leaf.EdgeMaps[0], o, inlineLookup, out _);
    }

    //Reads the value at `position` from `triple` (0=S, 1=P, 2=O).
    //Mirrors HypertrieOps.ValueAt; declared locally because that
    //one is private to HypertrieOps.
    private static uint ValueAt(EncodedTriple triple, int position)
    {
        return position switch
        {
            PositionSubject => triple.Subject.Encoded,
            PositionPredicate => triple.Predicate.Encoded,
            PositionObject => triple.Object.Encoded,
            _ => throw new UnreachableException(),
        };
    }

    //Walks `node`'s edge maps and collects the canonical identifier
    //of every distinct non-None child via post-order traversal.
    //Children produced by Patch* are interned through the store, so
    //their identifiers are recoverable by recomputing from content.
    //This mirrors HypertrieOps.CollectChildIdentifiers exactly so
    //the two routines agree on per-node identifiers.
    private static Dictionary<NodeHandle, NodeIdentifier> CollectChildIdentifiers(HypertrieNode node, NodeStore store, VeritasHash hash)
    {
        Dictionary<NodeHandle, NodeIdentifier> result = [];
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                //SEN children carry their content inline and SEN2
                //children live in the pair arena — neither needs an
                //arena lookup; ComputeIdentifier delegates to
                //HypertrieOps, which resolves their identifiers on
                //the fly from the slot content.
                if(!entry.Value.IsArenaHandle)
                {
                    continue;
                }

                if(result.ContainsKey(entry.Value))
                {
                    continue;
                }

                HypertrieNode childNode = store.GetByHandle(entry.Value);
                NodeIdentifier childIdentifier = ComputeIdentifier(childNode, store, hash, knownChildIdentifiers: null);
                result[entry.Value] = childIdentifier;
            }
        }

        return result;
    }

    //Computes the identifier of a single node by XOR-folding a
    //per-entry hash over every entry in every edge map. Delegates
    //to HypertrieOps so the build and patch paths cannot drift —
    //both produce the same identifier for the same content using
    //the same per-entry mixer and the same leaf-child marker.
    private static NodeIdentifier ComputeIdentifier(
        HypertrieNode node,
        NodeStore store,
        VeritasHash hash,
        Dictionary<NodeHandle, NodeIdentifier>? knownChildIdentifiers)
    {
        return HypertrieOps.ComputeIdentifier(node, store, hash, knownChildIdentifiers);
    }

    //An edit affecting one (outer, inner1, inner2) cell of one
    //descent path. IsAddition is the kind flag.
    private readonly record struct EditEntry(uint Inner1, uint Inner2, bool IsAddition);

    //An edit affecting one entry of one depth-1 leaf. The leaf's
    //single edge map is keyed by the original triple's third
    //position; Key carries that position's value. IsAddition is
    //the kind flag.
    private readonly record struct LeafEdit(uint Key, bool IsAddition);
}
