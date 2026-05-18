using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Static operations over <see cref="HypertrieNode"/> trees:
/// bottom-up construction with content-addressed deduplication, and
/// pattern-matched enumeration.
/// </summary>
/// <remarks>
/// <para>
/// <b>BuildBottomUp.</b> The build is bottom-up so every node's
/// children are already canonical instances by the time the node
/// itself is interned. The descent is fully iterative — explicit
/// stacks and dictionaries, no method recursion. The build runs in
/// roughly six layers:
/// </para>
/// <list type="number">
///   <item><description>Materialise the input as a distinct triple array.</description></item>
///   <item><description>Group the triples three ways — by (s, p), by (p, o), and by (s, o) — to drive the three descent paths multi-position branching produces.</description></item>
///   <item><description>Build and intern the depth-1 leaves for every distinct entry-set in the three groupings.</description></item>
///   <item><description>Build and intern the depth-2 inner nodes whose edge maps reference those canonical leaves.</description></item>
///   <item><description>Build the depth-3 root, populating its three edge maps from the depth-2 mappings.</description></item>
///   <item><description>Intern the root.</description></item>
/// </list>
/// <para>
/// <b>Match.</b> <see cref="Match"/> picks one descent path per
/// call: at every node it slices on the first remaining position
/// whose value is bound, and falls back to the canonical first
/// edge map when no remaining position is bound. Because exactly
/// one branching position is chosen per node, each matching triple
/// is yielded exactly once regardless of how many access paths
/// through the hypertrie produce the same data — and this remains
/// true after dedup turns the tree into a DAG.
/// </para>
/// </remarks>
public static class HypertrieOps
{
    private const int PositionSubject = 0;
    private const int PositionPredicate = 1;
    private const int PositionObject = 2;

    //Non-zero presence marker fed into the entry hash for depth-1
    //leaves where the child reference is null.
    private const ulong LeafChildMarker = 1UL;

    /// <summary>
    /// Builds a depth-3 hypertrie root from the given triples,
    /// interning every node it produces through
    /// <paramref name="store"/>. Two calls with content-equal
    /// triple sets and the same store return the same root
    /// instance.
    /// </summary>
    /// <param name="triples">The triples to index. Duplicates are removed.</param>
    /// <param name="store">The intern table. The same store may be reused across many calls.</param>
    /// <returns>The canonical depth-3 root for the given triple set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    public static HypertrieNode BuildBottomUp(IEnumerable<EncodedTriple> triples, NodeStore store)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(store);

        DistinctSortedTriples distinct = DistinctSortedTriples.Create(triples);
        (HypertrieNode root, NodeIdentifier _) = BuildBottomUpWithIdentifier(distinct, store);

        return root;
    }

    /// <summary>
    /// Builds a depth-3 hypertrie root from the given triples and
    /// returns it together with its content-addressed identifier.
    /// Equivalent to <see cref="BuildBottomUp"/> but exposes the
    /// root identifier the build already computes internally, so
    /// callers (notably <see cref="HypertrieSnapshot"/>
    /// construction) do not have to recompute it from a finished
    /// node tree.
    /// </summary>
    /// <param name="triples">The distinct, lex-sorted triples to index.</param>
    /// <param name="store">The intern table. The same store may be reused across many calls.</param>
    /// <returns>A tuple of the canonical depth-3 root and its content-addressed identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    internal static (HypertrieNode Root, NodeIdentifier Id) BuildBottomUpWithIdentifier(DistinctSortedTriples triples, NodeStore store)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(store);

        //The wrapper hands us the canonical SPO ordering for free.
        //Materialise the two other orderings (PSO and OSP) by
        //copying the SPO span and re-sorting under the
        //corresponding comparer. Per-position grouping in
        //BuildPathDepth2 then collapses to a linear walk over a
        //sorted span — no dictionary, no per-group List allocation
        //beyond the outer-key bucket.
        ReadOnlySpan<EncodedTriple> spoView = triples.AsSpan();
        EncodedTriple[] psoView = spoView.ToArray();
        Array.Sort(psoView, ComparePsoOrdering);
        EncodedTriple[] ospView = spoView.ToArray();
        Array.Sort(ospView, CompareOspOrdering);

        //Three descent paths off the root: one starting at S, one
        //at P, one at O. Each path's depth-2 node is keyed by that
        //starting position and indexes the remaining two by them
        //in a fixed pair order. The pair order is (predicate,
        //object) for the S-first path, (subject, object) for the
        //P-first path, and (subject, predicate) for the O-first
        //path.
        Dictionary<uint, HypertrieNode> sFirstDepth2 = BuildPathDepth2(spoView, store, PositionSubject);
        Dictionary<uint, HypertrieNode> pFirstDepth2 = BuildPathDepth2(psoView, store, PositionPredicate);
        Dictionary<uint, HypertrieNode> oFirstDepth2 = BuildPathDepth2(ospView, store, PositionObject);

        //Build the depth-3 root: three edge maps, one per starting
        //position, each holding the pre-built depth-2 mapping for
        //that position.
        HypertrieNode root = new(3);
        PopulateRootEdgeMap(ref root.EdgeMaps[PositionSubject], sFirstDepth2);
        PopulateRootEdgeMap(ref root.EdgeMaps[PositionPredicate], pFirstDepth2);
        PopulateRootEdgeMap(ref root.EdgeMaps[PositionObject], oFirstDepth2);

        //Compute the root's identifier and intern it. Returning
        //both the canonical root and its identifier saves the
        //caller a redundant identifier recomputation.
        NodeIdentifier rootIdentifier = ComputeIdentifier(root, store.Hash, knownChildIdentifiers: null);
        HypertrieNode canonicalRoot = store.Intern(rootIdentifier, root);
        return (canonicalRoot, rootIdentifier);
    }

    /// <summary>
    /// Enumerates every triple in <paramref name="root"/> that
    /// matches the given pattern. <see cref="TermId.None"/> on a
    /// position means "any value at this position."
    /// </summary>
    /// <param name="root">The depth-3 root node to query.</param>
    /// <param name="subject">The subject term identifier to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The predicate term identifier to match, or <see cref="TermId.None"/> for any predicate.</param>
    /// <param name="object">The object term identifier to match, or <see cref="TermId.None"/> for any object.</param>
    /// <returns>An enumerable of matching triples; each is yielded exactly once.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is not a depth-3 node.</exception>
    /// <remarks>
    /// <para>
    /// <b>Unbound positions.</b> A position parameter of
    /// <see cref="TermId.None"/> means "match any value at this
    /// position" — the pattern is unbound at that position. A
    /// concrete non-<see cref="TermId.None"/> value means "match
    /// this exact encoded term." Since <see cref="TermId.None"/>
    /// equals <c>default(TermId)</c>, position parameters that
    /// default to <c>default</c> are unbound by construction.
    /// </para>
    /// </remarks>
    public static IEnumerable<EncodedTriple> Match(
        HypertrieNode root,
        TermId subject,
        TermId predicate,
        TermId @object)
    {
        ArgumentNullException.ThrowIfNull(root);
        if(root.Depth != 3)
        {
            throw new ArgumentException(
                $"Match must start at a depth-3 root; got depth={root.Depth}.",
                nameof(root));
        }

        return MatchCore(root, subject, predicate, @object);
    }

    //Builds the depth-2 mapping for one of the three root descent
    //paths. <paramref name="outerPosition"/> picks which position the
    //depth-2 node is keyed by; the two remaining positions are
    //derived in ascending order and used as the inner edge map
    //indices. The ascending-order derivation is the protocol
    //contract that <c>MatchCore</c>'s bitset-to-edge-map-index
    //formula relies on. Computing it here, not asking callers to
    //pass it, makes the contract impossible to violate by call-site
    //convention.
    //
    //The input span is sorted by <paramref name="outerPosition"/>
    //first (then by the two inner positions in some order), so all
    //triples sharing an outer key are contiguous. The walk slides a
    //[start, end) window across each run, pre-sizes the entries
    //list to the run length, then hands the run to
    //BuildAndInternDepth2 — no dictionary, no per-bucket
    //rehashing, no list growth.
    private static Dictionary<uint, HypertrieNode> BuildPathDepth2(
        ReadOnlySpan<EncodedTriple> sortedByOuter,
        NodeStore store,
        int outerPosition)
    {
        (int innerPosition1, int innerPosition2) = outerPosition switch
        {
            PositionSubject => (PositionPredicate, PositionObject),
            PositionPredicate => (PositionSubject, PositionObject),
            PositionObject => (PositionSubject, PositionPredicate),
            _ => throw new UnreachableException(),
        };

        Dictionary<uint, HypertrieNode> result = [];
        int index = 0;
        int length = sortedByOuter.Length;
        while(index < length)
        {
            uint outerKey = ValueAt(sortedByOuter[index], outerPosition);

            //Slide the right edge of the run to the first triple
            //whose outer value differs.
            int runEnd = index + 1;
            while(runEnd < length && ValueAt(sortedByOuter[runEnd], outerPosition) == outerKey)
            {
                runEnd++;
            }

            int runLength = runEnd - index;
            List<(uint Inner1, uint Inner2)> entries = new(capacity: runLength);
            for(int cursor = index; cursor < runEnd; cursor++)
            {
                EncodedTriple triple = sortedByOuter[cursor];
                entries.Add((ValueAt(triple, innerPosition1), ValueAt(triple, innerPosition2)));
            }

            HypertrieNode depth2 = BuildAndInternDepth2(entries, store);
            result[outerKey] = depth2;
            index = runEnd;
        }

        return result;
    }

    //Comparator for PSO ordering: predicate, then subject, then
    //object. Used to materialise the predicate-first view from the
    //canonical SPO ordering.
    private static int ComparePsoOrdering(EncodedTriple left, EncodedTriple right)
    {
        int predicateComparison = left.Predicate.CompareTo(right.Predicate);
        if(predicateComparison != 0)
        {
            return predicateComparison;
        }

        int subjectComparison = left.Subject.CompareTo(right.Subject);
        if(subjectComparison != 0)
        {
            return subjectComparison;
        }

        return left.Object.CompareTo(right.Object);
    }

    //Comparator for OSP ordering: object, then subject, then
    //predicate. Used to materialise the object-first view from the
    //canonical SPO ordering.
    private static int CompareOspOrdering(EncodedTriple left, EncodedTriple right)
    {
        int objectComparison = left.Object.CompareTo(right.Object);
        if(objectComparison != 0)
        {
            return objectComparison;
        }

        int subjectComparison = left.Subject.CompareTo(right.Subject);
        if(subjectComparison != 0)
        {
            return subjectComparison;
        }

        return left.Predicate.CompareTo(right.Predicate);
    }

    //Builds a depth-2 node whose first edge map is keyed by Inner1
    //and whose second edge map is keyed by Inner2, with both edge
    //maps populated and consistent. Each first-position child is
    //the canonical depth-1 leaf holding all Inner2 values seen for
    //that Inner1; each second-position child is the canonical
    //depth-1 leaf holding all Inner1 values seen for that Inner2.
    //Children are interned through the store before the depth-2
    //node is built, so the depth-2 node's identifier sees
    //canonical child identifiers.
    private static HypertrieNode BuildAndInternDepth2(
        List<(uint Inner1, uint Inner2)> entries,
        NodeStore store)
    {
        //Group by Inner1 -> set of Inner2; group by Inner2 -> set of Inner1.
        Dictionary<uint, HashSet<uint>> byInner1 = [];
        Dictionary<uint, HashSet<uint>> byInner2 = [];
        foreach((uint inner1, uint inner2) in entries)
        {
            if(!byInner1.TryGetValue(inner1, out HashSet<uint>? bucket1))
            {
                bucket1 = [];
                byInner1[inner1] = bucket1;
            }
            bucket1.Add(inner2);

            if(!byInner2.TryGetValue(inner2, out HashSet<uint>? bucket2))
            {
                bucket2 = [];
                byInner2[inner2] = bucket2;
            }
            bucket2.Add(inner1);
        }

        //Build & intern depth-1 leaves for each grouping; reuse
        //already-interned leaves when two groupings produce the
        //same set (common when the data has symmetry).
        Dictionary<uint, HypertrieNode> leavesByInner1 = BuildAndInternLeaves(byInner1, store);
        Dictionary<uint, HypertrieNode> leavesByInner2 = BuildAndInternLeaves(byInner2, store);

        //Assemble the depth-2 node with both edge maps populated.
        HypertrieNode depth2 = new(2);
        foreach(KeyValuePair<uint, HypertrieNode> entry in leavesByInner1)
        {
            EdgeMap.InsertOrReplace(ref depth2.EdgeMaps[0], entry.Key, entry.Value);
        }
        foreach(KeyValuePair<uint, HypertrieNode> entry in leavesByInner2)
        {
            EdgeMap.InsertOrReplace(ref depth2.EdgeMaps[1], entry.Key, entry.Value);
        }

        //Compute the depth-2 node's identifier using the canonical
        //children's identifiers, then intern.
        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(depth2, store.Hash);
        NodeIdentifier identifier = ComputeIdentifier(depth2, store.Hash, childIdentifiers);
        return store.Intern(identifier, depth2);
    }

    //Builds a depth-1 leaf for every distinct key set in `groups`,
    //interning each through `store` and returning a map from group
    //key to canonical leaf.
    private static Dictionary<uint, HypertrieNode> BuildAndInternLeaves(
        Dictionary<uint, HashSet<uint>> groups,
        NodeStore store)
    {
        Dictionary<uint, HypertrieNode> result = new(groups.Count);
        foreach(KeyValuePair<uint, HashSet<uint>> group in groups)
        {
            HypertrieNode leaf = new(1);
            foreach(uint member in group.Value)
            {
                EdgeMap.InsertOrReplace(ref leaf.EdgeMaps[0], member, null);
            }
            NodeIdentifier identifier = ComputeIdentifier(leaf, store.Hash, knownChildIdentifiers: null);
            result[group.Key] = store.Intern(identifier, leaf);
        }
        return result;
    }

    //Populates one root edge map from a depth-2 mapping.
    private static void PopulateRootEdgeMap(ref EdgeMap edgeMap, Dictionary<uint, HypertrieNode> mapping)
    {
        foreach(KeyValuePair<uint, HypertrieNode> entry in mapping)
        {
            EdgeMap.InsertOrReplace(ref edgeMap, entry.Key, entry.Value);
        }
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
    //of every distinct non-null child. Used at depth-2 and depth-3
    //where children are themselves canonical interned nodes; the
    //identifier dictionary is consulted by `ComputeIdentifier` so
    //the parent's hash mixes in canonical child identifiers, not
    //object hashes. The hash is threaded through so the same mixer
    //is used end-to-end in a single build.
    private static Dictionary<HypertrieNode, NodeIdentifier> CollectChildIdentifiers(HypertrieNode node, VeritasHash hash)
    {
        Dictionary<HypertrieNode, NodeIdentifier> result = new(ReferenceEqualityComparer.Instance);
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                if(entry.Value is null)
                {
                    continue;
                }
                if(result.ContainsKey(entry.Value))
                {
                    continue;
                }
                //The child is canonical (interned) by the time we
                //reach here, but its identifier is not stored on
                //the node itself. Recompute it from the child's
                //content using the same hash function the store
                //uses; this is O(child entries) per distinct child
                //and fires once per build, not per query.
                NodeIdentifier childIdentifier = ComputeIdentifier(entry.Value, hash, knownChildIdentifiers: null);
                result[entry.Value] = childIdentifier;
            }
        }
        return result;
    }

    //Computes the identifier of a single node by XOR-folding a
    //per-entry hash for every entry in every edge map. For depth-1
    //leaves the per-entry hash mixes the key with the non-zero
    //presence marker. For inner nodes the per-entry hash mixes the
    //key with the child's identifier — looked up in
    //`knownChildIdentifiers` if supplied, otherwise computed
    //iteratively in this same method via an explicit stack so the
    //routine handles arbitrary depth-3 inputs without recursion.
    //Internal (not private) so `HypertrieOpsPatching.ApplyDelta`
    //can compute identifiers for nodes it produces using the same
    //leaf-child marker the build path uses; otherwise build and
    //patch could disagree on per-node identifiers.
    internal static NodeIdentifier ComputeIdentifier(
        HypertrieNode node,
        VeritasHash hash,
        Dictionary<HypertrieNode, NodeIdentifier>? knownChildIdentifiers)
    {
        //Fast path: depth-1 leaves never look up children.
        if(node.Depth == 1)
        {
            NodeIdentifier id = NodeIdentifier.Empty;
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[0]))
            {
                ulong childIdentifier = entry.Value is null ? LeafChildMarker : LookupChildIdentifier(entry.Value, knownChildIdentifiers);
                id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
            }
            return id;
        }

        //Inner nodes: fold over every position, looking up children
        //in the supplied table or computing them on the fly via the
        //iterative post-order walk below.
        if(knownChildIdentifiers is not null)
        {
            return FoldInner(node, hash, knownChildIdentifiers);
        }

        //No children table supplied — compute identifiers for
        //every reachable child first via an explicit post-order
        //traversal, then fold. This keeps the whole routine
        //non-recursive even for depth-3 inputs.
        Dictionary<HypertrieNode, NodeIdentifier> computed = ComputePostOrderIdentifiers(node, hash);
        return computed[node];
    }

    //Folds a single inner node's identifier given a complete
    //child-identifier table.
    private static NodeIdentifier FoldInner(
        HypertrieNode node,
        VeritasHash hash,
        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers)
    {
        NodeIdentifier id = NodeIdentifier.Empty;
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                ulong childIdentifier = entry.Value is null ? LeafChildMarker : childIdentifiers[entry.Value].Value;
                id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
            }
        }
        return id;
    }

    //Looks up the identifier of `child` in `table` if supplied. A
    //depth-1 leaf must never carry a non-null child, so reaching
    //this method without `table` populated for `child` is an
    //invariant violation; throw rather than silently substituting
    //a default mixer that would not be the one in use.
    private static ulong LookupChildIdentifier(
        HypertrieNode child,
        Dictionary<HypertrieNode, NodeIdentifier>? table)
    {
        if(table is not null && table.TryGetValue(child, out NodeIdentifier known))
        {
            return known.Value;
        }
        throw new InvalidOperationException("Depth-1 leaf carries a non-null child, which violates the leaf invariant.");
    }

    //Computes every reachable node's identifier in post-order so
    //that when each node is processed, its children's identifiers
    //are already in the result dictionary. Delegates the post-order
    //walk itself to IterativeTraversal.PostOrder, leaving only the
    //per-node folding step in this method.
    private static Dictionary<HypertrieNode, NodeIdentifier> ComputePostOrderIdentifiers(
        HypertrieNode root,
        VeritasHash hash)
    {
        Dictionary<HypertrieNode, NodeIdentifier> known = new(ReferenceEqualityComparer.Instance);
        foreach(HypertrieNode node in IterativeTraversal.PostOrder(
            seeds: [root],
            keyOf: static node => node,
            adjacency: EnumerateNonNullChildren))
        {
            NodeIdentifier id = NodeIdentifier.Empty;
            for(int position = 0; position < node.Depth; position++)
            {
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
                {
                    ulong childIdentifier = entry.Value is null ? LeafChildMarker : known[entry.Value].Value;
                    id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
                }
            }
            known[node] = id;
        }
        return known;
    }

    //Adjacency for the post-order walk: yields every non-null child
    //referenced from any of `node`'s edge maps. Static and free of
    //captures so the delegate group can be passed without
    //allocation.
    private static IEnumerable<HypertrieNode> EnumerateNonNullChildren(HypertrieNode node)
    {
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                if(entry.Value is not null)
                {
                    yield return entry.Value;
                }
            }
        }
    }

    //Bitset encoding for "positions still pending" on a MatchFrame.
    //Bit N set means original position N (0=S, 1=P, 2=O) has not
    //yet been resolved by the descent. The initial frame's bitset
    //is 0b111. Resolving a position clears its bit.
    //
    //At every depth, a node's edge maps are indexed by original
    //position number in ascending order of the positions still
    //remaining at that depth — an invariant BuildPathDepth2
    //establishes by computing inner positions in ascending order
    //rather than accepting them from the caller. Given that
    //invariant, the edge map index for original position P at a
    //frame whose pending bitset is B equals the count of lower-
    //numbered pending bits — implemented in EdgeMapIndexOf below.
    private const byte InitialPendingPositions = 0b111;

    //True when `position` is still pending (its bit set in `pending`).
    private static bool IsPending(byte pending, int position)
    {
        return (pending & (1 << position)) != 0;
    }

    //Returns `pending` with `position`'s bit cleared, indicating
    //the position has been resolved at this descent step.
    private static byte WithResolved(byte pending, int position)
    {
        return (byte)(pending & ~(1 << position));
    }

    //Maps an (original) position number to its index in the
    //current node's edge maps. Equals the count of pending
    //positions strictly lower than `position` — exactly what the
    //ascending-order build invariant guarantees.
    private static int EdgeMapIndexOf(byte pending, int position)
    {
        int lowerMask = (1 << position) - 1;
        return BitOperations.PopCount((uint)(pending & lowerMask));
    }

    private static IEnumerable<EncodedTriple> MatchCore(
        HypertrieNode root,
        TermId subject,
        TermId predicate,
        TermId @object)
    {
        Stack<MatchFrame> stack = new();
        stack.Push(new MatchFrame
        {
            Node = root,
            PendingPositions = InitialPendingPositions,
            ResolvedSubject = null,
            ResolvedPredicate = null,
            ResolvedObject = null,
        });

        while(stack.Count > 0)
        {
            MatchFrame frame = stack.Pop();
            HypertrieNode node = frame.Node;

            int chosenPosition = SelectPosition(frame.PendingPositions, subject, predicate, @object);
            int chosenEdgeMapIndex = EdgeMapIndexOf(frame.PendingPositions, chosenPosition);
            TermId chosenBound = chosenPosition switch
            {
                PositionSubject => subject,
                PositionPredicate => predicate,
                PositionObject => @object,
                _ => throw new UnreachableException(),
            };

            if(node.Depth == 1)
            {
                if(!chosenBound.IsNone)
                {
                    if(EdgeMap.TryGetChild(node.EdgeMaps[chosenEdgeMapIndex], chosenBound.Encoded, out _))
                    {
                        yield return BuildTriple(frame, chosenPosition, chosenBound.Encoded);
                    }
                }
                else
                {
                    foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[chosenEdgeMapIndex]))
                    {
                        yield return BuildTriple(frame, chosenPosition, entry.Key);
                    }
                }
                continue;
            }

            byte childPending = WithResolved(frame.PendingPositions, chosenPosition);

            if(!chosenBound.IsNone)
            {
                if(EdgeMap.TryGetChild(node.EdgeMaps[chosenEdgeMapIndex], chosenBound.Encoded, out HypertrieNode? child)
                    && child is not null)
                {
                    stack.Push(NewMatchFrame(frame, chosenPosition, chosenBound.Encoded, child, childPending));
                }
            }
            else
            {
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.EdgeMaps[chosenEdgeMapIndex]))
                {
                    if(entry.Value is not null)
                    {
                        stack.Push(NewMatchFrame(frame, chosenPosition, entry.Key, entry.Value, childPending));
                    }
                }
            }
        }
    }

    //Picks the next original position to descend on. Preference is
    //"a pending position whose query value is bound" — that is the
    //position the user has constrained, so descending on it slices
    //the search space immediately. If no bound position is pending,
    //falls back to the lowest-numbered pending position. The choice
    //is by *original* position (0=S, 1=P, 2=O); the caller maps it
    //to the local edge map index via EdgeMapIndexOf.
    //
    //Iteration walks positions 0..2 in ascending order so the
    //fall-through case naturally returns the lowest pending bit
    //without a separate code path.
    private static int SelectPosition(byte pending, TermId subject, TermId predicate, TermId @object)
    {
        int firstPending = -1;
        for(int position = 0; position < 3; position++)
        {
            if(!IsPending(pending, position))
            {
                continue;
            }

            if(firstPending < 0)
            {
                firstPending = position;
            }

            bool isBound = position switch
            {
                PositionSubject => !subject.IsNone,
                PositionPredicate => !predicate.IsNone,
                PositionObject => !@object.IsNone,
                _ => throw new UnreachableException(),
            };

            if(isBound)
            {
                return position;
            }
        }

        //No bound position pending — return the first pending one.
        //The loop above always finds at least one pending position
        //because Match never reaches a frame whose bitset is zero
        //(a frame with no pending positions cannot be descended).
        Debug.Assert(firstPending >= 0, "MatchFrame must have at least one pending position.");
        return firstPending;
    }

    private static EncodedTriple BuildTriple(MatchFrame frame, int newPosition, uint newValue)
    {
        uint s = newPosition == PositionSubject
            ? newValue
            : frame.ResolvedSubject ?? throw new InvalidOperationException("Subject not resolved when constructing triple.");
        uint p = newPosition == PositionPredicate
            ? newValue
            : frame.ResolvedPredicate ?? throw new InvalidOperationException("Predicate not resolved when constructing triple.");
        uint o = newPosition == PositionObject
            ? newValue
            : frame.ResolvedObject ?? throw new InvalidOperationException("Object not resolved when constructing triple.");
        return EncodedTriple.FromEncoded(s, p, o);
    }

    private static MatchFrame NewMatchFrame(
        MatchFrame parent,
        int newPosition,
        uint newValue,
        HypertrieNode child,
        byte childPending)
    {
        return new MatchFrame
        {
            Node = child,
            PendingPositions = childPending,
            ResolvedSubject = newPosition == PositionSubject ? newValue : parent.ResolvedSubject,
            ResolvedPredicate = newPosition == PositionPredicate ? newValue : parent.ResolvedPredicate,
            ResolvedObject = newPosition == PositionObject ? newValue : parent.ResolvedObject,
        };
    }

    //Internal frame for the iterative Match traversal. Carries
    //the already-resolved bindings so the leaf step can assemble a
    //complete triple without a second pass, plus the bitset of
    //positions still pending. See InitialPendingPositions for the
    //bitset encoding.
    private readonly struct MatchFrame
    {
        public required HypertrieNode Node { get; init; }
        public required byte PendingPositions { get; init; }
        public required uint? ResolvedSubject { get; init; }
        public required uint? ResolvedPredicate { get; init; }
        public required uint? ResolvedObject { get; init; }
    }
}
