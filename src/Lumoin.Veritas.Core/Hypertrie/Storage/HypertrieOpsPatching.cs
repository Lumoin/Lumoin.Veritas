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
/// the same final triple set.
/// </para>
/// <para>
/// <b>Per-edit cost.</b> A single triple add or remove touches
/// six depth-1 leaves — two leaves per descent path, one keyed by
/// the inner-1 position and one keyed by the inner-2 position —
/// plus three depth-2 nodes (one per descent path) plus the root.
/// All ten of those nodes are re-interned.
/// </para>
/// <para>
/// <b>Delta filtering.</b> The caller supplies the literal
/// session edits; the patcher filters them against the base
/// snapshot before applying. An add of a triple already present
/// is a no-op; a remove of a triple not present is a no-op.
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

        HypertrieNode baseRoot = baseSnapshot.Root;
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
            if(!IsPresent(baseRoot, triple))
            {
                effectiveAdds.Add(triple);
            }
        }

        List<EncodedTriple> effectiveRemoves = new(removals.Count);
        foreach(EncodedTriple triple in removals)
        {
            if(IsPresent(baseRoot, triple))
            {
                effectiveRemoves.Add(triple);
            }
        }

        if(effectiveAdds.Count == 0 && effectiveRemoves.Count == 0)
        {
            return new ApplyDeltaResult(
                Root: baseRoot,
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
            in baseRoot.Edge0,
            effectiveAdds,
            effectiveRemoves,
            outer: PositionSubject,
            inner1: PositionPredicate,
            inner2: PositionObject,
            store,
            pools);

        EdgeMap newPredicateEdgeMap = PatchPath(
            in baseRoot.Edge1,
            effectiveAdds,
            effectiveRemoves,
            outer: PositionPredicate,
            inner1: PositionSubject,
            inner2: PositionObject,
            store,
            pools);

        EdgeMap newObjectEdgeMap = PatchPath(
            in baseRoot.Edge2,
            effectiveAdds,
            effectiveRemoves,
            outer: PositionObject,
            inner1: PositionSubject,
            inner2: PositionPredicate,
            store,
            pools);

        //Assemble the new root.
        HypertrieNode newRoot = new(3);
        newRoot.Edge0 = newSubjectEdgeMap;
        newRoot.Edge1 = newPredicateEdgeMap;
        newRoot.Edge2 = newObjectEdgeMap;

        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(newRoot, store.Hash);
        NodeIdentifier newRootId = ComputeIdentifier(newRoot, store.Hash, childIdentifiers);
        HypertrieNode canonicalRoot = store.Intern(newRootId, newRoot);

        return new ApplyDeltaResult(
            Root: canonicalRoot,
            Id: newRootId,
            EffectiveAdditions: effectiveAdds,
            EffectiveRemovals: effectiveRemoves);
    }

    //Patches one of the three descent paths off the root.
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
        //Group edits by their outer key.
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
        foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(baseOuterEdgeMap))
        {
            uint outerKey = entry.Key;
            HypertrieNode? oldD2 = entry.Value;

            if(editsByOuter.TryGetValue(outerKey, out List<EditEntry>? bucket))
            {
                HypertrieNode? newD2 = PatchD2(oldD2, bucket, store, pools);
                if(newD2 is not null)
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, outerKey, newD2, pools, pools.InlineLookup);
                }
                editsByOuter.Remove(outerKey);
            }
            else
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, outerKey, oldD2, pools, pools.InlineLookup);
            }
        }

        //Second pass: outer keys that survived the dictionary erase
        //above are pure-additions buckets.
        foreach(KeyValuePair<uint, List<EditEntry>> remaining in editsByOuter)
        {
            HypertrieNode? newD2 = PatchD2(oldD2: null, remaining.Value, store, pools);
            if(newD2 is not null)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, remaining.Key, newD2, pools, pools.InlineLookup);
            }
        }

        return newEdgeMap;
    }

    //Patches one depth-2 node for a single (outer-key) bucket.
    //Returns null when both edge maps end up empty — that is the
    //signal to the caller that the outer key should be dropped from
    //the root edge map entirely.
    private static HypertrieNode? PatchD2(HypertrieNode? oldD2, List<EditEntry> bucket, NodeStore store, BuildPools pools)
    {
        //Re-bucket edits by inner1 and inner2.
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

        //Build the two new edge maps.
        EdgeMap baseEdgeMap0 = oldD2 is null ? default : oldD2.Edge0;
        EdgeMap baseEdgeMap1 = oldD2 is null ? default : oldD2.Edge1;

        EdgeMap newEdgeMap0 = PatchD2EdgeMap(baseEdgeMap0, byInner1, store, pools);
        EdgeMap newEdgeMap1 = PatchD2EdgeMap(baseEdgeMap1, byInner2, store, pools);

        if(EdgeMap.Count(in newEdgeMap0) == 0 && EdgeMap.Count(in newEdgeMap1) == 0)
        {
            return null;
        }

        HypertrieNode newD2 = new(2);
        newD2.Edge0 = newEdgeMap0;
        newD2.Edge1 = newEdgeMap1;

        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(newD2, store.Hash);
        NodeIdentifier identifier = ComputeIdentifier(newD2, store.Hash, childIdentifiers);
        return store.Intern(identifier, newD2);
    }

    //Patches one of the two edge maps inside a D2 node.
    private static EdgeMap PatchD2EdgeMap(
        EdgeMap baseEdgeMap,
        Dictionary<uint, List<LeafEdit>> editsByLeafKey,
        NodeStore store,
        BuildPools pools)
    {
        EdgeMap newEdgeMap = default;

        foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(baseEdgeMap))
        {
            uint leafKey = entry.Key;
            HypertrieNode? oldLeaf = entry.Value;

            if(editsByLeafKey.TryGetValue(leafKey, out List<LeafEdit>? edits))
            {
                HypertrieNode? newLeaf = PatchLeaf(oldLeaf, edits, store, pools);
                if(newLeaf is not null)
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, leafKey, newLeaf, pools, pools.InlineLookup);
                }
                editsByLeafKey.Remove(leafKey);
            }
            else
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, leafKey, oldLeaf, pools, pools.InlineLookup);
            }
        }

        foreach(KeyValuePair<uint, List<LeafEdit>> remaining in editsByLeafKey)
        {
            HypertrieNode? newLeaf = PatchLeaf(oldLeaf: null, remaining.Value, store, pools);
            if(newLeaf is not null)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, remaining.Key, newLeaf, pools, pools.InlineLookup);
            }
        }

        return newEdgeMap;
    }

    //Patches one depth-1 leaf. Returns null when no entries survive.
    private static HypertrieNode? PatchLeaf(HypertrieNode? oldLeaf, List<LeafEdit> edits, NodeStore store, BuildPools pools)
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

        if(oldLeaf is not null)
        {
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(oldLeaf.Edge0))
            {
                if(!removedKeys.Contains(entry.Key))
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, entry.Key, child: null, pools, pools.InlineLookup);
                }
            }
        }

        foreach(LeafEdit edit in edits)
        {
            if(edit.IsAddition)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, edit.Key, child: null, pools, pools.InlineLookup);
            }
        }

        if(EdgeMap.Count(in newEdgeMap) == 0)
        {
            return null;
        }

        HypertrieNode newLeaf = new(1);
        newLeaf.Edge0 = newEdgeMap;

        NodeIdentifier identifier = ComputeIdentifier(newLeaf, store.Hash, knownChildIdentifiers: null);
        return store.Intern(identifier, newLeaf);
    }

    //Walks the depth-3 hypertrie rooted at `root` checking whether
    //`triple` is present.
    private static bool IsPresent(HypertrieNode root, EncodedTriple triple)
    {
        uint s = triple.Subject.Encoded;
        uint p = triple.Predicate.Encoded;
        uint o = triple.Object.Encoded;
        InlineKeyLookup inlineLookup = InlineKeyLookups.Scalar;

        //Step 1: subject edge map at the root → D2 node keyed by P, O.
        if(!EdgeMap.TryGetChild(in root.Edge0, s, inlineLookup, out HypertrieNode? d2) || d2 is null)
        {
            return false;
        }

        //Step 2: D2.Edge0 (keyed by P) → leaf indexed by O.
        if(!EdgeMap.TryGetChild(in d2.Edge0, p, inlineLookup, out HypertrieNode? leaf) || leaf is null)
        {
            return false;
        }

        //Step 3: leaf.Edge0 (keyed by O) → presence.
        return EdgeMap.TryGetChild(in leaf.Edge0, o, inlineLookup, out _);
    }

    //Reads the value at `position` from `triple` (0=S, 1=P, 2=O).
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
    //of every distinct non-null child via recomputation from content.
    private static Dictionary<HypertrieNode, NodeIdentifier> CollectChildIdentifiers(HypertrieNode node, VeritasHash hash)
    {
        Dictionary<HypertrieNode, NodeIdentifier> result = [];
        for(int position = 0; position < node.Depth; position++)
        {
            ref EdgeMap map = ref node.EdgeMapAt(position);
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
            {
                if(entry.Value is null)
                {
                    continue;
                }
                if(result.ContainsKey(entry.Value))
                {
                    continue;
                }
                NodeIdentifier childIdentifier = ComputeIdentifier(entry.Value, hash, knownChildIdentifiers: null);
                result[entry.Value] = childIdentifier;
            }
        }
        return result;
    }

    //Computes the identifier of a single node. Delegates to
    //HypertrieOps so the build and patch paths cannot drift.
    private static NodeIdentifier ComputeIdentifier(
        HypertrieNode node,
        VeritasHash hash,
        Dictionary<HypertrieNode, NodeIdentifier>? knownChildIdentifiers)
    {
        return HypertrieOps.ComputeIdentifier(node, hash, knownChildIdentifiers);
    }

    //An edit affecting one (outer, inner1, inner2) cell of one
    //descent path. IsAddition is the kind flag.
    private readonly record struct EditEntry(uint Inner1, uint Inner2, bool IsAddition);

    //An edit affecting one entry of one depth-1 leaf.
    private readonly record struct LeafEdit(uint Key, bool IsAddition);
}
