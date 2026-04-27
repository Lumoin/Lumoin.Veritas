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
        NodeStore store)
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
            in baseRoot.EdgeMaps[PositionSubject],
            effectiveAdds,
            effectiveRemoves,
            outer: PositionSubject,
            inner1: PositionPredicate,
            inner2: PositionObject,
            store);

        EdgeMap newPredicateEdgeMap = PatchPath(
            in baseRoot.EdgeMaps[PositionPredicate],
            effectiveAdds,
            effectiveRemoves,
            outer: PositionPredicate,
            inner1: PositionSubject,
            inner2: PositionObject,
            store);

        EdgeMap newObjectEdgeMap = PatchPath(
            in baseRoot.EdgeMaps[PositionObject],
            effectiveAdds,
            effectiveRemoves,
            outer: PositionObject,
            inner1: PositionSubject,
            inner2: PositionPredicate,
            store);

        //Assemble the new root. EdgeMap is a struct; assigning
        //into the array slots is a copy of the value type, so the
        //heap arrays produced by PatchPath flow into the new root
        //without aliasing the path-local locals.
        HypertrieNode newRoot = new(3);
        newRoot.EdgeMaps[PositionSubject] = newSubjectEdgeMap;
        newRoot.EdgeMaps[PositionPredicate] = newPredicateEdgeMap;
        newRoot.EdgeMaps[PositionObject] = newObjectEdgeMap;

        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(newRoot, store.Hash);
        NodeIdentifier newRootId = ComputeIdentifier(newRoot, store.Hash, childIdentifiers);
        HypertrieNode canonicalRoot = store.Intern(newRootId, newRoot);

        return new ApplyDeltaResult(
            Root: canonicalRoot,
            Id: newRootId,
            EffectiveAdditions: effectiveAdds,
            EffectiveRemovals: effectiveRemoves);
    }

    //Patches one of the three descent paths off the root. Walks
    //the base path's outer edge map, building a new edge map by:
    //  - groupings of edits affecting outer keys present in the base
    //    drive PatchD2 to produce updated D2 nodes (or null when the
    //    bucket becomes empty);
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
        NodeStore store)
    {
        //Group edits by their outer key. Each bucket carries the
        //(inner1, inner2) projections of the affected triples plus
        //a kind flag indicating whether the edit is an add or a
        //remove. The dictionary's value is keyed by inner1, then by
        //inner2, and the bool says "true=add, false=remove" because
        //a given (outer, inner1, inner2) triple is added xor removed
        //inside one ApplyDelta call (the EditBuffer layer above
        //already collapsed the two on a same-triple basis).
        Dictionary<long, List<EditEntry>> editsByOuter = [];
        foreach(EncodedTriple triple in effectiveAdds)
        {
            long outerKey = ValueAt(triple, outer);
            long innerKey1 = ValueAt(triple, inner1);
            long innerKey2 = ValueAt(triple, inner2);

            if(!editsByOuter.TryGetValue(outerKey, out List<EditEntry>? bucket))
            {
                bucket = [];
                editsByOuter[outerKey] = bucket;
            }
            bucket.Add(new EditEntry(innerKey1, innerKey2, IsAddition: true));
        }

        foreach(EncodedTriple triple in effectiveRemoves)
        {
            long outerKey = ValueAt(triple, outer);
            long innerKey1 = ValueAt(triple, inner1);
            long innerKey2 = ValueAt(triple, inner2);

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
        foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(baseOuterEdgeMap))
        {
            long outerKey = entry.Key;
            HypertrieNode? oldD2 = entry.Value;

            if(editsByOuter.TryGetValue(outerKey, out List<EditEntry>? bucket))
            {
                HypertrieNode? newD2 = PatchD2(oldD2, bucket, store);
                if(newD2 is not null)
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, outerKey, newD2);
                }
                editsByOuter.Remove(outerKey);
            }
            else
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, outerKey, oldD2);
            }
        }

        //Second pass: outer keys that survived the dictionary erase
        //above are pure-additions buckets — the base did not have
        //the outer key, so every edit in the bucket must be an add
        //(removes against absent triples were filtered earlier).
        //Build a fresh D2 for each.
        foreach(KeyValuePair<long, List<EditEntry>> remaining in editsByOuter)
        {
            HypertrieNode? newD2 = PatchD2(oldD2: null, remaining.Value, store);
            if(newD2 is not null)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, remaining.Key, newD2);
            }
        }

        return newEdgeMap;
    }

    //Patches one depth-2 node for a single (outer-key) bucket.
    //Builds a fresh D2 from scratch using EdgeMap.InsertOrReplace,
    //which means the resulting EdgeMap kinds follow the canonical
    //promotion ladder for the post-edit entry counts. Returns null
    //when both edge maps end up empty — that is the signal to the
    //caller that the outer key should be dropped from the root
    //edge map entirely.
    private static HypertrieNode? PatchD2(HypertrieNode? oldD2, List<EditEntry> bucket, NodeStore store)
    {
        //Re-bucket edits by inner1 (drives the first edge map's
        //leaves) and by inner2 (drives the second edge map's
        //leaves). Same edits, two views; mirrors the symmetry the
        //build path uses.
        Dictionary<long, List<LeafEdit>> byInner1 = [];
        Dictionary<long, List<LeafEdit>> byInner2 = [];

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
        EdgeMap newEdgeMap0 = PatchD2EdgeMap(oldD2?.EdgeMaps[0] ?? default, byInner1, store);
        EdgeMap newEdgeMap1 = PatchD2EdgeMap(oldD2?.EdgeMaps[1] ?? default, byInner2, store);

        if(EdgeMap.Count(in newEdgeMap0) == 0 && EdgeMap.Count(in newEdgeMap1) == 0)
        {
            return null;
        }

        HypertrieNode newD2 = new(2);
        newD2.EdgeMaps[0] = newEdgeMap0;
        newD2.EdgeMaps[1] = newEdgeMap1;

        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(newD2, store.Hash);
        NodeIdentifier identifier = ComputeIdentifier(newD2, store.Hash, childIdentifiers);
        return store.Intern(identifier, newD2);
    }

    //Patches one of the two edge maps inside a D2 node. The
    //structure mirrors PatchPath at one level shallower: walk the
    //base edge map, rebuild a leaf for every key with edits, copy
    //through every key without edits, then process pure-addition
    //leaves not present in the base.
    private static EdgeMap PatchD2EdgeMap(
        EdgeMap baseEdgeMap,
        Dictionary<long, List<LeafEdit>> editsByLeafKey,
        NodeStore store)
    {
        EdgeMap newEdgeMap = default;

        //Snapshot pending leaf-edit keys so we can drop entries as
        //we consume them — the dictionary itself is mutated inside
        //the loop. Iteration order over the base edge map is
        //independent of dictionary mutation.
        foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(baseEdgeMap))
        {
            long leafKey = entry.Key;
            HypertrieNode? oldLeaf = entry.Value;

            if(editsByLeafKey.TryGetValue(leafKey, out List<LeafEdit>? edits))
            {
                HypertrieNode? newLeaf = PatchLeaf(oldLeaf, edits, store);
                if(newLeaf is not null)
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, leafKey, newLeaf);
                }
                editsByLeafKey.Remove(leafKey);
            }
            else
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, leafKey, oldLeaf);
            }
        }

        foreach(KeyValuePair<long, List<LeafEdit>> remaining in editsByLeafKey)
        {
            HypertrieNode? newLeaf = PatchLeaf(oldLeaf: null, remaining.Value, store);
            if(newLeaf is not null)
            {
                EdgeMap.InsertOrReplace(ref newEdgeMap, remaining.Key, newLeaf);
            }
        }

        return newEdgeMap;
    }

    //Patches one depth-1 leaf. Rebuilds the edge map from scratch:
    //every old key not in the removes list is reinserted, then
    //every new addition is inserted. The InsertOrReplace path
    //chooses the canonical EdgeMapKind for the resulting entry
    //count. Returns null when no entries survive — the leaf is
    //dead and the caller drops the corresponding bucket key.
    private static HypertrieNode? PatchLeaf(HypertrieNode? oldLeaf, List<LeafEdit> edits, NodeStore store)
    {
        //Materialise the removed keys for O(1) membership in the
        //old-key copy loop.
        HashSet<long> removedKeys = [];
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
            foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(oldLeaf.EdgeMaps[0]))
            {
                if(!removedKeys.Contains(entry.Key))
                {
                    EdgeMap.InsertOrReplace(ref newEdgeMap, entry.Key, child: null);
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
                EdgeMap.InsertOrReplace(ref newEdgeMap, edit.Key, child: null);
            }
        }

        if(EdgeMap.Count(in newEdgeMap) == 0)
        {
            return null;
        }

        HypertrieNode newLeaf = new(1);
        newLeaf.EdgeMaps[0] = newEdgeMap;

        NodeIdentifier identifier = ComputeIdentifier(newLeaf, store.Hash, knownChildIdentifiers: null);
        return store.Intern(identifier, newLeaf);
    }

    //Walks the depth-3 hypertrie rooted at `root` checking whether
    //`triple` is present. Reuses HypertrieOps.Match's S-first
    //descent contract: descend the subject edge map, then the
    //object edge map of the resulting D2, then check the leaf for
    //the predicate.
    private static bool IsPresent(HypertrieNode root, EncodedTriple triple)
    {
        long s = triple.Subject.Encoded;
        long p = triple.Predicate.Encoded;
        long o = triple.Object.Encoded;

        //Step 1: subject edge map at the root → D2 node keyed by P, O.
        if(!EdgeMap.TryGetChild(in root.EdgeMaps[PositionSubject], s, out HypertrieNode? d2) || d2 is null)
        {
            return false;
        }

        //Step 2: D2.EdgeMaps[0] (keyed by P) → leaf indexed by O.
        if(!EdgeMap.TryGetChild(in d2.EdgeMaps[0], p, out HypertrieNode? leaf) || leaf is null)
        {
            return false;
        }

        //Step 3: leaf.EdgeMaps[0] (keyed by O) → presence.
        return EdgeMap.TryGetChild(in leaf.EdgeMaps[0], o, out _);
    }

    //Reads the value at `position` from `triple` (0=S, 1=P, 2=O).
    //Mirrors HypertrieOps.ValueAt; declared locally because that
    //one is private to HypertrieOps.
    private static long ValueAt(EncodedTriple triple, int position)
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
    //of every distinct non-null child via post-order traversal.
    //Children produced by Patch* are interned through the store, so
    //their identifiers are recoverable by recomputing from content.
    //This mirrors HypertrieOps.CollectChildIdentifiers exactly so
    //the two routines agree on per-node identifiers.
    private static Dictionary<HypertrieNode, NodeIdentifier> CollectChildIdentifiers(HypertrieNode node, VeritasHash hash)
    {
        Dictionary<HypertrieNode, NodeIdentifier> result = new(ReferenceEqualityComparer.Instance);
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
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

    //Computes the identifier of a single node by XOR-folding a
    //per-entry hash over every entry in every edge map. Delegates
    //to HypertrieOps so the build and patch paths cannot drift —
    //both produce the same identifier for the same content using
    //the same per-entry mixer and the same leaf-child marker.
    private static NodeIdentifier ComputeIdentifier(
        HypertrieNode node,
        VeritasHash hash,
        Dictionary<HypertrieNode, NodeIdentifier>? knownChildIdentifiers)
    {
        return HypertrieOps.ComputeIdentifier(node, hash, knownChildIdentifiers);
    }

    //An edit affecting one (outer, inner1, inner2) cell of one
    //descent path. IsAddition is the kind flag.
    private readonly record struct EditEntry(long Inner1, long Inner2, bool IsAddition);

    //An edit affecting one entry of one depth-1 leaf. The leaf's
    //single edge map is keyed by the original triple's third
    //position; Key carries that position's value. IsAddition is
    //the kind flag.
    private readonly record struct LeafEdit(long Key, bool IsAddition);
}
