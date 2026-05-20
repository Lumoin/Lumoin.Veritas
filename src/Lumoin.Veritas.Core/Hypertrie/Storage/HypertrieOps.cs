using System;
using System.Buffers;
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
    //leaves where the child slot is null (no descent).
    private const ulong LeafChildMarker = 1UL;

    /// <summary>
    /// Builds a depth-3 hypertrie root from the given
    /// pre-deduplicated, lex-sorted triples and returns the
    /// canonical root reference.
    /// </summary>
    /// <param name="triples">The distinct, lex-sorted triples to index.</param>
    /// <param name="store">The intern table. The same store may be reused across many calls.</param>
    /// <param name="pools">The pools bundle used for EdgeMap promotion and grow.</param>
    /// <returns>The canonical depth-3 root for the given triple set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    internal static HypertrieNode BuildBottomUp(DistinctSortedTriples triples, NodeStore store, BuildPools pools)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(store);

        (HypertrieNode root, NodeIdentifier _) = BuildBottomUpWithIdentifier(triples, store, pools);

        return root;
    }

    /// <summary>
    /// Builds a depth-3 hypertrie root from the given triples and
    /// returns the canonical root reference together with its
    /// content-addressed identifier. Equivalent to
    /// <see cref="BuildBottomUp"/> but exposes the root identifier
    /// the build already computes internally, so callers (notably
    /// <see cref="HypertrieSnapshot"/> construction) do not have to
    /// recompute it from a finished node tree.
    /// </summary>
    /// <param name="triples">The distinct, lex-sorted triples to index.</param>
    /// <param name="store">The intern table. The same store may be reused across many calls.</param>
    /// <param name="pools">The pools bundle used for EdgeMap promotion and grow.</param>
    /// <returns>A tuple of the canonical depth-3 root and its content-addressed identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    internal static (HypertrieNode Root, NodeIdentifier Id) BuildBottomUpWithIdentifier(DistinctSortedTriples triples, NodeStore store, BuildPools pools)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(store);

        //The wrapper hands us the canonical SPO ordering for free.
        //Materialise the two other orderings (PSO and OSP) as uint
        //index permutations over the SPO array — 4 bytes per triple
        //rented from BuildPools.PermutationPool, in place of 12
        //bytes per triple for a full EncodedTriple copy. Each
        //permutation is sorted via a struct comparer whose
        //Compare(uint, uint) reads two triples out of the SPO array
        //and dispatches to the corresponding ordering comparator.
        EncodedTriple[] spoArray = triples.AsArray();
        ReadOnlySpan<EncodedTriple> spoView = spoArray;
        int tripleCount = spoArray.Length;

        //Rent only when there is something to permute. The pool's
        //exact-size policy rejects zero-sized rentals, and the
        //downstream BuildPathDepth2 path treats an empty permutation
        //span as "walk source directly" — so the empty-triple case
        //flows through unchanged with both permutations as empty
        //spans.
        IMemoryOwner<uint>? psoPermutationOwner = null;
        IMemoryOwner<uint>? ospPermutationOwner = null;
        try
        {
            ReadOnlySpan<uint> psoPermutation = ReadOnlySpan<uint>.Empty;
            ReadOnlySpan<uint> ospPermutation = ReadOnlySpan<uint>.Empty;

            if(tripleCount > 0)
            {
                psoPermutationOwner = pools.PermutationPool.Rent(tripleCount);
                ospPermutationOwner = pools.PermutationPool.Rent(tripleCount);

                Span<uint> psoSpan = psoPermutationOwner.Memory.Span[..tripleCount];
                Span<uint> ospSpan = ospPermutationOwner.Memory.Span[..tripleCount];

                //Initialise both permutations to identity, then sort
                //under their respective triple-comparators. The sorts
                //are specialised by TComparer (struct) so the
                //comparator's Compare call inlines through JIT
                //specialisation.
                for(int i = 0; i < tripleCount; i++)
                {
                    psoSpan[i] = (uint)i;
                    ospSpan[i] = (uint)i;
                }

                psoSpan.Sort(new PsoPermutationComparer(spoArray));
                ospSpan.Sort(new OspPermutationComparer(spoArray));

                psoPermutation = psoSpan;
                ospPermutation = ospSpan;
            }

            //Three descent paths off the root: one starting at S, one
            //at P, one at O. Each path's depth-2 node is keyed by that
            //starting position and indexes the remaining two by them
            //in a fixed pair order. The pair order is (predicate,
            //object) for the S-first path, (subject, object) for the
            //P-first path, and (subject, predicate) for the O-first
            //path. The S-first path walks spoView directly; the
            //P-first and O-first paths walk spoView through their
            //permutation indirection. An empty permutation parameter
            //to BuildPathDepth2 means "use the source span as-is."
            Dictionary<uint, HypertrieNode> sFirstDepth2 = BuildPathDepth2(spoView, ReadOnlySpan<uint>.Empty, store, PositionSubject, pools);
            Dictionary<uint, HypertrieNode> pFirstDepth2 = BuildPathDepth2(spoView, psoPermutation, store, PositionPredicate, pools);
            Dictionary<uint, HypertrieNode> oFirstDepth2 = BuildPathDepth2(spoView, ospPermutation, store, PositionObject, pools);

            //Build the depth-3 root: three edge maps, one per starting
            //position, each holding the pre-built depth-2 mapping for
            //that position.
            HypertrieNode root = new(3);
            PopulateRootEdgeMap(ref root.Edge0, sFirstDepth2, pools);
            PopulateRootEdgeMap(ref root.Edge1, pFirstDepth2, pools);
            PopulateRootEdgeMap(ref root.Edge2, oFirstDepth2, pools);

            //Compute the root's identifier and intern it. Returning
            //both the canonical root reference and its identifier
            //saves the caller a redundant identifier recomputation.
            NodeIdentifier rootIdentifier = ComputeIdentifier(root, store.Hash, knownChildIdentifiers: null);
            HypertrieNode canonicalRoot = store.Intern(rootIdentifier, root);
            return (canonicalRoot, rootIdentifier);
        }
        finally
        {
            psoPermutationOwner?.Dispose();
            ospPermutationOwner?.Dispose();
        }
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
    //indices.
    private static Dictionary<uint, HypertrieNode> BuildPathDepth2(
        ReadOnlySpan<EncodedTriple> source,
        ReadOnlySpan<uint> permutation,
        NodeStore store,
        int outerPosition,
        BuildPools pools)
    {
        (int innerPosition1, int innerPosition2) = outerPosition switch
        {
            PositionSubject => (PositionPredicate, PositionObject),
            PositionPredicate => (PositionSubject, PositionObject),
            PositionObject => (PositionSubject, PositionPredicate),
            _ => throw new UnreachableException(),
        };

        //An empty permutation means "walk source directly" — the
        //canonical SPO descent. A non-empty permutation means "walk
        //source through this index indirection" — the PSO and OSP
        //descents. The branch is hoistable by JIT because permutation
        //is a parameter that does not change inside the loop.
        bool usePermutation = !permutation.IsEmpty;
        Dictionary<uint, HypertrieNode> result = [];
        int index = 0;
        int length = usePermutation ? permutation.Length : source.Length;
        while(index < length)
        {
            EncodedTriple firstTriple = usePermutation ? source[(int)permutation[index]] : source[index];
            uint outerKey = ValueAt(firstTriple, outerPosition);

            //Slide the right edge of the run to the first triple
            //whose outer value differs.
            int runEnd = index + 1;
            while(runEnd < length)
            {
                EncodedTriple candidate = usePermutation ? source[(int)permutation[runEnd]] : source[runEnd];
                if(ValueAt(candidate, outerPosition) != outerKey)
                {
                    break;
                }

                runEnd++;
            }

            int runLength = runEnd - index;
            List<(uint Inner1, uint Inner2)> entries = new(capacity: runLength);
            for(int cursor = index; cursor < runEnd; cursor++)
            {
                EncodedTriple triple = usePermutation ? source[(int)permutation[cursor]] : source[cursor];
                entries.Add((ValueAt(triple, innerPosition1), ValueAt(triple, innerPosition2)));
            }

            HypertrieNode depth2 = BuildAndInternDepth2(entries, store, pools);
            result[outerKey] = depth2;
            index = runEnd;
        }

        return result;
    }

    //Struct comparer that sorts a permutation buffer of indices into
    //the canonical SPO array, ordered by ComparePsoOrdering.
    private readonly struct PsoPermutationComparer(EncodedTriple[] source): IComparer<uint>
    {
        public int Compare(uint a, uint b) => ComparePsoOrdering(source[a], source[b]);
    }

    //Struct comparer that sorts a permutation buffer of indices into
    //the canonical SPO array, ordered by CompareOspOrdering.
    private readonly struct OspPermutationComparer(EncodedTriple[] source): IComparer<uint>
    {
        public int Compare(uint a, uint b) => CompareOspOrdering(source[a], source[b]);
    }

    //Comparator for PSO ordering: predicate, then subject, then object.
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

    //Comparator for OSP ordering: object, then subject, then predicate.
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

    //Builds a depth-2 node and interns it. Children of the depth-2
    //node are the canonical depth-1 leaves holding the inner-key
    //sets; children are interned through the store before the
    //depth-2 node is built, so the depth-2 node's identifier sees
    //canonical child identifiers.
    private static HypertrieNode BuildAndInternDepth2(
        List<(uint Inner1, uint Inner2)> entries,
        NodeStore store,
        BuildPools pools)
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
        Dictionary<uint, HypertrieNode> leavesByInner1 = BuildAndInternLeaves(byInner1, store, pools);
        Dictionary<uint, HypertrieNode> leavesByInner2 = BuildAndInternLeaves(byInner2, store, pools);

        //Assemble the depth-2 node with both edge maps populated.
        HypertrieNode depth2 = new(2);
        foreach(KeyValuePair<uint, HypertrieNode> entry in leavesByInner1)
        {
            EdgeMap.InsertOrReplace(ref depth2.Edge0, entry.Key, entry.Value, pools, pools.InlineLookup);
        }
        foreach(KeyValuePair<uint, HypertrieNode> entry in leavesByInner2)
        {
            EdgeMap.InsertOrReplace(ref depth2.Edge1, entry.Key, entry.Value, pools, pools.InlineLookup);
        }

        //Compute the depth-2 node's identifier using the canonical
        //children's identifiers, then intern.
        Dictionary<HypertrieNode, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(depth2, store.Hash);
        NodeIdentifier identifier = ComputeIdentifier(depth2, store.Hash, childIdentifiers);
        return store.Intern(identifier, depth2);
    }

    //Builds a depth-1 leaf for every distinct key set in `groups`,
    //interning each through `store` and returning a map from group
    //key to canonical leaf reference.
    private static Dictionary<uint, HypertrieNode> BuildAndInternLeaves(
        Dictionary<uint, HashSet<uint>> groups,
        NodeStore store,
        BuildPools pools)
    {
        Dictionary<uint, HypertrieNode> result = new(groups.Count);
        foreach(KeyValuePair<uint, HashSet<uint>> group in groups)
        {
            HypertrieNode leaf = new(1);
            foreach(uint member in group.Value)
            {
                EdgeMap.InsertOrReplace(ref leaf.Edge0, member, child: null, pools, pools.InlineLookup);
            }
            NodeIdentifier identifier = ComputeIdentifier(leaf, store.Hash, knownChildIdentifiers: null);
            result[group.Key] = store.Intern(identifier, leaf);
        }
        return result;
    }

    //Populates one root edge map from a depth-2 mapping.
    private static void PopulateRootEdgeMap(ref EdgeMap edgeMap, Dictionary<uint, HypertrieNode> mapping, BuildPools pools)
    {
        foreach(KeyValuePair<uint, HypertrieNode> entry in mapping)
        {
            EdgeMap.InsertOrReplace(ref edgeMap, entry.Key, entry.Value, pools, pools.InlineLookup);
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
    //of every distinct child reference. Used at depth-2 and depth-3
    //where children are themselves canonical interned nodes.
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
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(node.Edge0))
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
        return FoldInner(node, hash, computed);
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
            ref EdgeMap map = ref node.EdgeMapAt(position);
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
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
        throw new InvalidOperationException("Depth-1 leaf carries a non-null child reference, which violates the leaf invariant.");
    }

    //Computes every reachable node's identifier in post-order so
    //that when each node is processed, its children's identifiers
    //are already in the result dictionary. The walk operates on
    //HypertrieNode references; the adjacency dereferences each
    //node's three embedded edge maps to find its non-null children.
    private static Dictionary<HypertrieNode, NodeIdentifier> ComputePostOrderIdentifiers(
        HypertrieNode root,
        VeritasHash hash)
    {
        //Seed the post-order walk from each non-null child of the
        //root (the root itself is not yet interned, so its
        //identifier is folded by the caller).
        List<HypertrieNode> seeds = [];
        for(int position = 0; position < root.Depth; position++)
        {
            ref EdgeMap map = ref root.EdgeMapAt(position);
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
            {
                if(entry.Value is not null)
                {
                    seeds.Add(entry.Value);
                }
            }
        }

        Dictionary<HypertrieNode, NodeIdentifier> known = [];
        //Pass keyOf: n => n to use the explicit-key overload; the
        //2-arg overload constrains TNode : IEquatable<TNode>, which
        //a class type with reference equality does not satisfy
        //structurally. The HashSet<HypertrieNode> the traversal uses
        //internally for dedup still works via reference equality
        //because HypertrieNode is a class.
        foreach(HypertrieNode node in IterativeTraversal.PostOrder(
            seeds: seeds,
            keyOf: n => n,
            adjacency: EnumerateNonNullChildren))
        {
            NodeIdentifier id = NodeIdentifier.Empty;
            for(int position = 0; position < node.Depth; position++)
            {
                ref EdgeMap map = ref node.EdgeMapAt(position);
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
                {
                    ulong childIdentifier = entry.Value is null ? LeafChildMarker : known[entry.Value].Value;
                    id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
                }
            }
            known[node] = id;
        }
        return known;
    }

    //Adjacency for the post-order walk: yields every non-null
    //child reference from any of the node's edge maps.
    private static IEnumerable<HypertrieNode> EnumerateNonNullChildren(HypertrieNode node)
    {
        for(int position = 0; position < node.Depth; position++)
        {
            //EdgeMap.EdgeMapAt returns ref EdgeMap. Capture via a
            //local to avoid re-reading the field three times per
            //iteration. Cannot use ref local across yield, so copy
            //by value into the local.
            EdgeMap map = node.EdgeMapAt(position);
            foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
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
        InlineKeyLookup inlineLookup = InlineKeyLookups.Scalar;
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
                EdgeMap leafMap = node.EdgeMapAt(chosenEdgeMapIndex);
                if(!chosenBound.IsNone)
                {
                    if(EdgeMap.TryGetChild(in leafMap, chosenBound.Encoded, inlineLookup, out _))
                    {
                        yield return BuildTriple(frame, chosenPosition, chosenBound.Encoded);
                    }
                }
                else
                {
                    foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(leafMap))
                    {
                        yield return BuildTriple(frame, chosenPosition, entry.Key);
                    }
                }
                continue;
            }

            byte childPending = WithResolved(frame.PendingPositions, chosenPosition);
            EdgeMap innerMap = node.EdgeMapAt(chosenEdgeMapIndex);

            if(!chosenBound.IsNone)
            {
                if(EdgeMap.TryGetChild(in innerMap, chosenBound.Encoded, inlineLookup, out HypertrieNode? child)
                    && child is not null)
                {
                    stack.Push(NewMatchFrame(frame, chosenPosition, chosenBound.Encoded, child, childPending));
                }
            }
            else
            {
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(innerMap))
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
    //falls back to the lowest-numbered pending position.
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

    //Internal frame for the iterative Match traversal.
    private readonly struct MatchFrame
    {
        public required HypertrieNode Node { get; init; }
        public required byte PendingPositions { get; init; }
        public required uint? ResolvedSubject { get; init; }
        public required uint? ResolvedPredicate { get; init; }
        public required uint? ResolvedObject { get; init; }
    }
}
