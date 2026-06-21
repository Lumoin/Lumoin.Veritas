using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Collections;
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
    //leaves where the child slot is NodeHandle.None.
    private const ulong LeafChildMarker = 1UL;

    /// <summary>
    /// Builds a depth-3 hypertrie root from the given
    /// pre-deduplicated, lex-sorted triples and returns the
    /// canonical root handle.
    /// </summary>
    /// <param name="triples">The distinct, lex-sorted triples to index.</param>
    /// <param name="store">The intern table. The same store may be reused across many calls.</param>
    /// <param name="pools">The pools bundle used for EdgeMap promotion and grow.</param>
    /// <returns>The canonical depth-3 root's handle for the given triple set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    internal static NodeHandle BuildBottomUp(DistinctSortedTriples triples, NodeStore store, BuildPools pools)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(store);

        (NodeHandle root, NodeIdentifier _) = BuildBottomUpWithIdentifier(triples, store, pools);

        return root;
    }

    /// <summary>
    /// Builds a depth-3 hypertrie root from the given triples and
    /// returns the canonical root handle together with its
    /// content-addressed identifier. Equivalent to
    /// <see cref="BuildBottomUp"/> but exposes the root identifier
    /// the build already computes internally, so callers (notably
    /// <see cref="HypertrieSnapshot"/> construction) do not have to
    /// recompute it from a finished node tree.
    /// </summary>
    /// <param name="triples">The distinct, lex-sorted triples to index.</param>
    /// <param name="store">The intern table. The same store may be reused across many calls.</param>
    /// <param name="pools">The pools bundle used for EdgeMap promotion and grow.</param>
    /// <returns>A tuple of the canonical depth-3 root's handle and its content-addressed identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    internal static (NodeHandle Root, NodeIdentifier Id) BuildBottomUpWithIdentifier(DistinctSortedTriples triples, NodeStore store, BuildPools pools)
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
            Dictionary<uint, NodeHandle> sFirstDepth2 = BuildPathDepth2(spoView, ReadOnlySpan<uint>.Empty, store, PositionSubject, pools);
            Dictionary<uint, NodeHandle> pFirstDepth2 = BuildPathDepth2(spoView, psoPermutation, store, PositionPredicate, pools);
            Dictionary<uint, NodeHandle> oFirstDepth2 = BuildPathDepth2(spoView, ospPermutation, store, PositionObject, pools);

            //Build the depth-3 root: three edge maps, one per starting
            //position, each holding the pre-built depth-2 mapping for
            //that position.
            HypertrieNode root = HypertrieNode.Create(3);
            PopulateRootEdgeMap(ref root.EdgeMaps[PositionSubject], sFirstDepth2, pools);
            PopulateRootEdgeMap(ref root.EdgeMaps[PositionPredicate], pFirstDepth2, pools);
            PopulateRootEdgeMap(ref root.EdgeMaps[PositionObject], oFirstDepth2, pools);

            //Compute the root's identifier and intern it. Returning
            //both the canonical root's handle and its identifier saves
            //the caller a redundant identifier recomputation.
            NodeIdentifier rootIdentifier = ComputeIdentifier(root, store, store.Hash, knownChildIdentifiers: null);
            NodeHandle canonicalRoot = store.Intern(rootIdentifier, root);
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
    /// <param name="store">The store backing <paramref name="root"/>; used to resolve child handles during descent.</param>
    /// <param name="subject">The subject term identifier to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The predicate term identifier to match, or <see cref="TermId.None"/> for any predicate.</param>
    /// <param name="object">The object term identifier to match, or <see cref="TermId.None"/> for any object.</param>
    /// <returns>An enumerable of matching triples; each is yielded exactly once.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
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
        NodeStore store,
        TermId subject,
        TermId predicate,
        TermId @object)
    {
        ArgumentNullException.ThrowIfNull(store);
        if(root.Depth != 3)
        {
            throw new ArgumentException(
                $"Match must start at a depth-3 root; got depth={root.Depth}.",
                nameof(root));
        }

        return MatchCore(root, store, subject, predicate, @object);
    }

    /// <summary>
    /// Returns every triple in <paramref name="root"/> whose subject is a
    /// member of <paramref name="subjects"/>, whose predicate equals
    /// <paramref name="predicate"/>, and (when bound) whose object equals
    /// <paramref name="object"/>. Performs a single predicate-rooted
    /// descent and probes once per subject — not <c>|subjects| × </c> root
    /// descents.
    /// </summary>
    /// <param name="root">The depth-3 root node to query.</param>
    /// <param name="store">The store backing <paramref name="root"/>.</param>
    /// <param name="subjects">The subject set. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <param name="predicate">The predicate. Must be bound.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any.</param>
    /// <returns>Matching triples; output ordering is unspecified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is not depth-3; <paramref name="predicate"/> is <see cref="TermId.None"/>; or <paramref name="subjects"/> contains <see cref="TermId.None"/>.</exception>
    public static IEnumerable<EncodedTriple> MatchBySubjects(
        HypertrieNode root,
        NodeStore store,
        ReadOnlyMemory<TermId> subjects,
        TermId predicate,
        TermId @object)
    {
        ArgumentNullException.ThrowIfNull(store);
        if(root.Depth != 3)
        {
            throw new ArgumentException(
                $"MatchBySubjects must start at a depth-3 root; got depth={root.Depth}.",
                nameof(root));
        }

        if(predicate.IsNone)
        {
            throw new ArgumentException(
                "Predicate must be bound for MatchBySubjects.",
                nameof(predicate));
        }

        //Eager validation of the subject-set membership invariant; the
        //failure surfaces at the call site rather than mid-enumeration.
        ReadOnlySpan<TermId> validateSpan = subjects.Span;
        for(int i = 0; i < validateSpan.Length; i++)
        {
            if(validateSpan[i].IsNone)
            {
                throw new ArgumentException(
                    "Subject set must not contain TermId.None.",
                    nameof(subjects));
            }
        }

        return MatchBySubjectsCore(root, store, subjects, predicate, @object);
    }

    /// <summary>
    /// Mirror of <see cref="MatchBySubjects"/> across the object position:
    /// returns triples whose object is a member of <paramref name="objects"/>,
    /// whose predicate equals <paramref name="predicate"/>, and (when bound)
    /// whose subject equals <paramref name="subject"/>.
    /// </summary>
    /// <param name="root">The depth-3 root node to query.</param>
    /// <param name="store">The store backing <paramref name="root"/>.</param>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="predicate">The predicate. Must be bound.</param>
    /// <param name="objects">The object set. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <returns>Matching triples; output ordering is unspecified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is not depth-3; <paramref name="predicate"/> is <see cref="TermId.None"/>; or <paramref name="objects"/> contains <see cref="TermId.None"/>.</exception>
    public static IEnumerable<EncodedTriple> MatchByObjects(
        HypertrieNode root,
        NodeStore store,
        TermId subject,
        TermId predicate,
        ReadOnlyMemory<TermId> objects)
    {
        ArgumentNullException.ThrowIfNull(store);
        if(root.Depth != 3)
        {
            throw new ArgumentException(
                $"MatchByObjects must start at a depth-3 root; got depth={root.Depth}.",
                nameof(root));
        }

        if(predicate.IsNone)
        {
            throw new ArgumentException(
                "Predicate must be bound for MatchByObjects.",
                nameof(predicate));
        }

        ReadOnlySpan<TermId> validateSpan = objects.Span;
        for(int i = 0; i < validateSpan.Length; i++)
        {
            if(validateSpan[i].IsNone)
            {
                throw new ArgumentException(
                    "Object set must not contain TermId.None.",
                    nameof(objects));
            }
        }

        return MatchByObjectsCore(root, store, subject, predicate, objects);
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
    private static Dictionary<uint, NodeHandle> BuildPathDepth2(
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
        Dictionary<uint, NodeHandle> result = [];
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

            NodeHandle depth2 = BuildAndInternDepth2(entries, store, pools);
            result[outerKey] = depth2;
            index = runEnd;
        }

        return result;
    }

    //Struct comparer that sorts a permutation buffer of indices into
    //the canonical SPO array, ordered by ComparePsoOrdering. Captured
    //as a struct (not a class) so MemoryExtensions.Sort can specialise
    //its TComparer generic parameter and inline the comparator's
    //Compare call. Holds the array reference (not a span) because
    //IComparer<uint>-implementing types are not ref structs and
    //cannot hold ReadOnlySpan fields.
    private readonly struct PsoPermutationComparer(EncodedTriple[] source): IComparer<uint>
    {
        public int Compare(uint a, uint b) => ComparePsoOrdering(source[a], source[b]);
    }

    //Struct comparer that sorts a permutation buffer of indices into
    //the canonical SPO array, ordered by CompareOspOrdering. Same
    //specialisation contract as PsoPermutationComparer.
    private readonly struct OspPermutationComparer(EncodedTriple[] source): IComparer<uint>
    {
        public int Compare(uint a, uint b) => CompareOspOrdering(source[a], source[b]);
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
    //
    //The grouping is by stable radix sort, not by
    //Dictionary<uint, HashSet<uint>>: the input (Inner1, Inner2)
    //pairs are globally distinct (the build receives a
    //distinct-triple set, so for any fixed outer key the inner
    //pairs are distinct), which means no per-group dedup is
    //required. Materialising the pairs into pool-rented parallel
    //spans and sorting them lets the leaf-building walk run as a
    //linear scan over contiguous same-key runs — no per-group
    //dictionary lookup, no per-group HashSet resize storm. The
    //single-entry case fast-paths through to two SEN children
    //without touching the pool.
    private static NodeHandle BuildAndInternDepth2(
        List<(uint Inner1, uint Inner2)> entries,
        NodeStore store,
        BuildPools pools)
    {
        int entryCount = entries.Count;
        if(entryCount == 1)
        {
            //Single-entry fast path: the whole depth-2 subtree
            //collapses into one pair-arena slot addressed by an
            //SEN2 handle — no node, no edge maps, no intern entry.
            //The pair is (Inner1, Inner2): the two remaining
            //positions' keys in ascending original-position order,
            //the same invariant the materialised node's edge maps
            //would encode.
            return store.AllocateSingleEntryPair(entries[0].Inner1, entries[0].Inner2);
        }

        using IMemoryOwner<uint> by1KeysOwner = pools.PermutationPool.Rent(entryCount);
        using IMemoryOwner<uint> by1MembersOwner = pools.PermutationPool.Rent(entryCount);
        using IMemoryOwner<uint> by2KeysOwner = pools.PermutationPool.Rent(entryCount);
        using IMemoryOwner<uint> by2MembersOwner = pools.PermutationPool.Rent(entryCount);

        Span<uint> by1Keys = by1KeysOwner.Memory.Span[..entryCount];
        Span<uint> by1Members = by1MembersOwner.Memory.Span[..entryCount];
        Span<uint> by2Keys = by2KeysOwner.Memory.Span[..entryCount];
        Span<uint> by2Members = by2MembersOwner.Memory.Span[..entryCount];

        //Materialise both groupings in a single pass over entries.
        //by1 carries (Inner1, Inner2) to drive the Inner1-keyed
        //edge map; by2 carries (Inner2, Inner1) to drive the
        //Inner2-keyed edge map.
        for(int i = 0; i < entryCount; i++)
        {
            (uint inner1, uint inner2) = entries[i];
            by1Keys[i] = inner1;
            by1Members[i] = inner2;
            by2Keys[i] = inner2;
            by2Members[i] = inner1;
        }

        RadixSort.Sort(by1Keys, by1Members);
        RadixSort.Sort(by2Keys, by2Members);

        Dictionary<uint, NodeHandle> leavesByInner1 = BuildAndInternLeavesFromSortedGroups(by1Keys, by1Members, store, pools);
        Dictionary<uint, NodeHandle> leavesByInner2 = BuildAndInternLeavesFromSortedGroups(by2Keys, by2Members, store, pools);

        HypertrieNode depth2 = HypertrieNode.Create(2);
        foreach(KeyValuePair<uint, NodeHandle> entry in leavesByInner1)
        {
            EdgeMap.InsertOrReplace(ref depth2.EdgeMaps[0], entry.Key, entry.Value, pools, pools.InlineLookup);
        }

        foreach(KeyValuePair<uint, NodeHandle> entry in leavesByInner2)
        {
            EdgeMap.InsertOrReplace(ref depth2.EdgeMaps[1], entry.Key, entry.Value, pools, pools.InlineLookup);
        }

        Dictionary<NodeHandle, NodeIdentifier> childIdentifiers = CollectChildIdentifiers(depth2, store, store.Hash);
        NodeIdentifier identifier = ComputeIdentifier(depth2, store, store.Hash, childIdentifiers);
        return store.Intern(identifier, depth2);
    }

    //Walks parallel (sortedKeys, sortedMembers) spans, where
    //sortedKeys is in ascending order. Each contiguous run of
    //equal keys is one group; the run's members are the
    //corresponding sortedMembers slice. Groups of size 1 emit
    //as SEN-encoded handles directly in the parent's slot; no
    //HypertrieNode object is allocated and no intern-table entry
    //is created. Larger groups allocate a leaf node and intern it.
    private static Dictionary<uint, NodeHandle> BuildAndInternLeavesFromSortedGroups(
        ReadOnlySpan<uint> sortedKeys,
        ReadOnlySpan<uint> sortedMembers,
        NodeStore store,
        BuildPools pools)
    {
        Dictionary<uint, NodeHandle> result = [];
        int length = sortedKeys.Length;
        int start = 0;
        while(start < length)
        {
            uint key = sortedKeys[start];
            int end = start + 1;
            while(end < length && sortedKeys[end] == key)
            {
                end++;
            }

            int memberCount = end - start;
            if(memberCount == 1)
            {
                result[key] = NodeHandle.ForSingleEntry(sortedMembers[start]);
            }
            else
            {
                HypertrieNode leaf = HypertrieNode.Create(1);
                for(int i = start; i < end; i++)
                {
                    EdgeMap.InsertOrReplace(ref leaf.EdgeMaps[0], sortedMembers[i], NodeHandle.None, pools, pools.InlineLookup);
                }

                NodeIdentifier identifier = ComputeIdentifier(leaf, store, store.Hash, knownChildIdentifiers: null);
                result[key] = store.Intern(identifier, leaf);
            }

            start = end;
        }

        return result;
    }

    //Populates one root edge map from a depth-2 mapping.
    private static void PopulateRootEdgeMap(ref EdgeMap edgeMap, Dictionary<uint, NodeHandle> mapping, BuildPools pools)
    {
        foreach(KeyValuePair<uint, NodeHandle> entry in mapping)
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
    //of every distinct child handle. Used at depth-2 and depth-3
    //where children are themselves canonical interned nodes; the
    //identifier dictionary is consulted by `ComputeIdentifier` so
    //the parent's hash mixes in canonical child identifiers, not
    //object hashes. The hash is threaded through so the same mixer
    //is used end-to-end in a single build.
    private static Dictionary<NodeHandle, NodeIdentifier> CollectChildIdentifiers(HypertrieNode node, NodeStore store, VeritasHash hash)
    {
        Dictionary<NodeHandle, NodeIdentifier> result = [];
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                //SEN children carry their content inline in the slot
                //and SEN2 children live in the pair arena — neither
                //has a node arena entry to look up. The parent's
                //folded identifier mixes their identifiers in via
                //ChildIdentifierValue's SEN/SEN2 branches, computed
                //on the fly from the slot content.
                if(!entry.Value.IsArenaHandle)
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
                HypertrieNode childNode = store.GetByHandle(entry.Value);
                NodeIdentifier childIdentifier = ComputeIdentifier(childNode, store, hash, knownChildIdentifiers: null);
                result[entry.Value] = childIdentifier;
            }
        }
        return result;
    }

    //Returns the identifier-value to mix into a parent's folded
    //identifier for the given child slot. None slots contribute
    //the leaf-child marker; SEN slots contribute the synthetic
    //depth-1 identifier their content would produce as an FN leaf;
    //SEN2 slots contribute the depth-2 identifier the equivalent
    //single-entry node would produce; FN slots look up the
    //precomputed identifier in the table.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ChildIdentifierValue(NodeHandle child, NodeStore store, VeritasHash hash, Dictionary<NodeHandle, NodeIdentifier> table)
    {
        if(child.IsNone)
        {
            return LeafChildMarker;
        }

        if(child.IsSingleEntry)
        {
            return SenIdentifierValue(hash, child.SingleEntryKey);
        }

        if(child.IsSingleEntryPair)
        {
            (uint first, uint second) = store.GetPair(child);

            return Sen2IdentifierValue(hash, first, second);
        }

        return table[child].Value;
    }

    //The identifier value an SEN-encoded slot contributes to its
    //parent's identifier. An SEN slot represents the same content
    //as a depth-1 FN leaf with one entry; computing the same hash
    //both representations would produce keeps build-path dedup
    //consistent across SEN/FN representations of equal content.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SenIdentifierValue(VeritasHash hash, uint singleKey)
    {
        ulong entryHash = NodeEntryHashing.Default(hash, singleKey, LeafChildMarker);
        return entryHash & NodeIdentifier.ContentMask;
    }

    //The identifier value an SEN2-encoded slot contributes to its
    //parent's identifier. The pair represents the same content as
    //a depth-2 node with two SEN children — one entry per inner
    //edge map, each carrying the other key — so the fold below is
    //exactly the fold the equivalent node would produce, keeping
    //identifiers identical across SEN2/FN representations of equal
    //content.
    private static ulong Sen2IdentifierValue(VeritasHash hash, uint first, uint second)
    {
        NodeIdentifier id = NodeIdentifier.Empty;
        id = id.Add(NodeEntryHashing.Default(hash, first, SenIdentifierValue(hash, second)));
        id = id.Add(NodeEntryHashing.Default(hash, second, SenIdentifierValue(hash, first)));

        return id.Value;
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
        NodeStore store,
        VeritasHash hash,
        Dictionary<NodeHandle, NodeIdentifier>? knownChildIdentifiers)
    {
        //Fast path: depth-1 leaves never look up children.
        if(node.Depth == 1)
        {
            NodeIdentifier id = NodeIdentifier.Empty;
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[0]))
            {
                ulong childIdentifier = entry.Value.IsNone ? LeafChildMarker : LookupChildIdentifier(entry.Value, knownChildIdentifiers);
                id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
            }

            return id;
        }

        //Inner nodes: fold over every position, looking up children
        //in the supplied table or computing them on the fly via the
        //iterative post-order walk below.
        if(knownChildIdentifiers is not null)
        {
            return FoldInner(node, store, hash, knownChildIdentifiers);
        }

        //No children table supplied — compute identifiers for
        //every reachable child first via an explicit post-order
        //traversal, then fold. This keeps the whole routine
        //non-recursive even for depth-3 inputs.
        Dictionary<NodeHandle, NodeIdentifier> computed = ComputePostOrderIdentifiers(node, store, hash);
        return FoldInner(node, store, hash, computed);
    }

    //Folds a single inner node's identifier given a complete
    //child-identifier table for its FN children. SEN, SEN2, and
    //None children resolve via the same path.
    private static NodeIdentifier FoldInner(
        HypertrieNode node,
        NodeStore store,
        VeritasHash hash,
        Dictionary<NodeHandle, NodeIdentifier> childIdentifiers)
    {
        NodeIdentifier id = NodeIdentifier.Empty;
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                ulong childIdentifier = ChildIdentifierValue(entry.Value, store, hash, childIdentifiers);
                id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
            }
        }
        return id;
    }

    //Looks up the identifier of `child` in `table` if supplied. A
    //depth-1 leaf must never carry a non-None child, so reaching
    //this method without `table` populated for `child` is an
    //invariant violation; throw rather than silently substituting
    //a default mixer that would not be the one in use.
    private static ulong LookupChildIdentifier(
        NodeHandle child,
        Dictionary<NodeHandle, NodeIdentifier>? table)
    {
        if(table is not null && table.TryGetValue(child, out NodeIdentifier known))
        {
            return known.Value;
        }
        throw new InvalidOperationException("Depth-1 leaf carries a non-None child handle, which violates the leaf invariant.");
    }

    //Computes every reachable node's identifier in post-order so
    //that when each node is processed, its children's identifiers
    //are already in the result dictionary. The walk operates on
    //NodeHandle values; the adjacency dereferences each handle via
    //the store to enumerate its children.
    private static Dictionary<NodeHandle, NodeIdentifier> ComputePostOrderIdentifiers(
        HypertrieNode root,
        NodeStore store,
        VeritasHash hash)
    {
        //Seed the post-order walk from each FN child of the root
        //(SEN and None slots are not walk targets — SEN content is
        //inline, None means absent). The root itself has no handle
        //in the store before interning, so its identifier is folded
        //by the caller.
        List<NodeHandle> seeds = [];
        for(int position = 0; position < root.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(root.EdgeMaps[position]))
            {
                if(entry.Value.IsArenaHandle)
                {
                    seeds.Add(entry.Value);
                }
            }
        }

        Dictionary<NodeHandle, NodeIdentifier> known = [];
        foreach(NodeHandle handle in IterativeTraversal.PostOrder(
            seeds: seeds,
            adjacency: new ChildHandleAdjacency(store).Of))
        {
            HypertrieNode node = store.GetByHandle(handle);
            NodeIdentifier id = NodeIdentifier.Empty;
            for(int position = 0; position < node.Depth; position++)
            {
                foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
                {
                    ulong childIdentifier = ChildIdentifierValue(entry.Value, store, hash, known);
                    id = id.Add(NodeEntryHashing.Default(hash, entry.Key, childIdentifier));
                }
            }

            known[handle] = id;
        }

        return known;
    }

    //Adjacency for the post-order walk: yields every FN child
    //handle referenced from any of the node's edge maps. SEN
    //children carry their content inline and have no arena entry
    //to descend into. None slots are absence sentinels.
    private static IEnumerable<NodeHandle> EnumerateNonNullChildHandles(NodeHandle handle, NodeStore store)
    {
        HypertrieNode node = store.GetByHandle(handle);
        for(int position = 0; position < node.Depth; position++)
        {
            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
            {
                if(entry.Value.IsArenaHandle)
                {
                    yield return entry.Value;
                }
            }
        }
    }

    /// <summary>
    /// Carries the node store as explicit state so the post-order walk's adjacency is a bound
    /// method group, not a lambda closing over the enclosing store.
    /// </summary>
    /// <param name="store">The node store the child handles are resolved against.</param>
    private sealed class ChildHandleAdjacency(NodeStore store)
    {
        /// <summary>The node store the child handles are resolved against.</summary>
        private NodeStore Store { get; } = store;

        /// <summary>Yields every FN child handle referenced from the node's edge maps.</summary>
        /// <param name="handle">The node whose child handles to enumerate.</param>
        /// <returns>The node's non-null arena child handles.</returns>
        public IEnumerable<NodeHandle> Of(NodeHandle handle)
        {
            return EnumerateNonNullChildHandles(handle, Store);
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
        NodeStore store,
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

            int chosenPosition = SelectPosition(frame.PendingPositions, subject, predicate, @object, node);
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
                    if(EdgeMap.TryGetChild(in node.EdgeMaps[chosenEdgeMapIndex], chosenBound.Encoded, inlineLookup, out _))
                    {
                        yield return BuildTriple(frame, chosenPosition, chosenBound.Encoded);
                    }
                }
                else
                {
                    foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[chosenEdgeMapIndex]))
                    {
                        yield return BuildTriple(frame, chosenPosition, entry.Key);
                    }
                }
                continue;
            }

            byte childPending = WithResolved(frame.PendingPositions, chosenPosition);

            if(!chosenBound.IsNone)
            {
                if(EdgeMap.TryGetChild(in node.EdgeMaps[chosenEdgeMapIndex], chosenBound.Encoded, inlineLookup, out NodeHandle childHandle)
                    && !childHandle.IsNone)
                {
                    if(childHandle.IsSingleEntry)
                    {
                        ProcessSingleEntryChild(frame, chosenPosition, chosenBound.Encoded, childPending, childHandle.SingleEntryKey, subject, predicate, @object, out EncodedTriple? produced);
                        if(produced is EncodedTriple senTriple)
                        {
                            yield return senTriple;
                        }
                    }
                    else if(childHandle.IsSingleEntryPair)
                    {
                        (uint pairFirst, uint pairSecond) = store.GetPair(childHandle);
                        ProcessSingleEntryPairChild(chosenPosition, chosenBound.Encoded, childPending, pairFirst, pairSecond, subject, predicate, @object, out EncodedTriple? produced);
                        if(produced is EncodedTriple sen2Triple)
                        {
                            yield return sen2Triple;
                        }
                    }
                    else
                    {
                        HypertrieNode child = store.GetByHandle(childHandle);
                        stack.Push(NewMatchFrame(frame, chosenPosition, chosenBound.Encoded, child, childPending));
                    }
                }
            }
            else
            {
                foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[chosenEdgeMapIndex]))
                {
                    if(entry.Value.IsNone)
                    {
                        continue;
                    }

                    if(entry.Value.IsSingleEntry)
                    {
                        ProcessSingleEntryChild(frame, chosenPosition, entry.Key, childPending, entry.Value.SingleEntryKey, subject, predicate, @object, out EncodedTriple? produced);
                        if(produced is EncodedTriple senTriple)
                        {
                            yield return senTriple;
                        }
                    }
                    else if(entry.Value.IsSingleEntryPair)
                    {
                        (uint pairFirst, uint pairSecond) = store.GetPair(entry.Value);
                        ProcessSingleEntryPairChild(chosenPosition, entry.Key, childPending, pairFirst, pairSecond, subject, predicate, @object, out EncodedTriple? produced);
                        if(produced is EncodedTriple sen2Triple)
                        {
                            yield return sen2Triple;
                        }
                    }
                    else
                    {
                        HypertrieNode child = store.GetByHandle(entry.Value);
                        stack.Push(NewMatchFrame(frame, chosenPosition, entry.Key, child, childPending));
                    }
                }
            }
        }
    }

    //Descends one root edge: looks up `outerKey` in the root's edge map
    //for `outerPosition`. Returns false when the root has no entry for
    //that key or when the entry is None. On true, the mapping is
    //either a real depth-2 node (pairHandle is None) or a single-entry
    //pair (pairHandle is the SEN2 handle and depth2 is default). SEN
    //at the depth-3 → depth-2 transition is an invariant violation
    //(SEN appears only at depth-2 → depth-1).
    private static bool TryDescendRoot(
        HypertrieNode root,
        NodeStore store,
        int outerPosition,
        uint outerKey,
        InlineKeyLookup inlineLookup,
        out HypertrieNode depth2,
        out NodeHandle pairHandle)
    {
        if(!EdgeMap.TryGetChild(in root.EdgeMaps[outerPosition], outerKey, inlineLookup, out NodeHandle depth2Handle)
            || depth2Handle.IsNone)
        {
            depth2 = default;
            pairHandle = NodeHandle.None;

            return false;
        }

        Debug.Assert(!depth2Handle.IsSingleEntry,
            "Depth-2 mapping handles are never SEN; SEN appears only at depth-2 → depth-1.");

        if(depth2Handle.IsSingleEntryPair)
        {
            depth2 = default;
            pairHandle = depth2Handle;

            return true;
        }

        depth2 = store.GetByHandle(depth2Handle);
        pairHandle = NodeHandle.None;

        return true;
    }

    //Body of MatchBySubjects. Performs the single predicate-rooted
    //descent, then iterates the subject set and probes the per-subject
    //depth-1 leaf. SEN children are resolved inline; FN children fan
    //out into the depth-1 enumeration. The optional bound object
    //narrows the depth-1 walk to a single key lookup.
    private static IEnumerable<EncodedTriple> MatchBySubjectsCore(
        HypertrieNode root,
        NodeStore store,
        ReadOnlyMemory<TermId> subjects,
        TermId predicate,
        TermId @object)
    {
        InlineKeyLookup inlineLookup = InlineKeyLookups.Scalar;

        //One descent to the predicate's depth-2 mapping. The depth-2
        //node from the P-first descent has EdgeMaps[0] keyed by S and
        //EdgeMaps[1] keyed by O — that ordering is established by
        //BuildPathDepth2 for outerPosition=PositionPredicate, whose
        //inner positions resolve to (Subject, Object) in ascending
        //order.
        if(!TryDescendRoot(root, store, PositionPredicate, predicate.Encoded, inlineLookup, out HypertrieNode depth2, out NodeHandle sen2Handle))
        {
            yield break;
        }

        uint predicateKey = predicate.Encoded;

        if(sen2Handle.IsSingleEntryPair)
        {
            //The predicate's whole mapping is one (subject, object)
            //pair — the P-first inner positions in ascending order.
            (uint pairSubject, uint pairObject) = store.GetPair(sen2Handle);

            if(@object.IsNone || @object.Encoded == pairObject)
            {
                for(int i = 0; i < subjects.Length; i++)
                {
                    if(subjects.Span[i].Encoded == pairSubject)
                    {
                        yield return EncodedTriple.FromEncoded(pairSubject, predicateKey, pairObject);

                        break;
                    }
                }
            }

            yield break;
        }

        //Per subject: look up the depth-1 leaf in depth2.EdgeMaps[0]
        //and yield its contents filtered by the optional object bound.
        for(int i = 0; i < subjects.Length; i++)
        {
            uint subjectKey = subjects.Span[i].Encoded;
            if(!EdgeMap.TryGetChild(in depth2.EdgeMaps[0], subjectKey, inlineLookup, out NodeHandle leafHandle)
                || leafHandle.IsNone)
            {
                continue;
            }

            if(leafHandle.IsSingleEntry)
            {
                //SEN at depth-2 → depth-1 carries the single inner key
                //inline. For the P-first descent, EdgeMaps[0] is
                //subject-keyed and the SEN's inline key is therefore
                //the object value.
                uint senObject = leafHandle.SingleEntryKey;
                if(!@object.IsNone && @object.Encoded != senObject)
                {
                    continue;
                }

                yield return EncodedTriple.FromEncoded(subjectKey, predicateKey, senObject);

                continue;
            }

            HypertrieNode leaf = store.GetByHandle(leafHandle);
            if(!@object.IsNone)
            {
                if(EdgeMap.TryGetChild(in leaf.EdgeMaps[0], @object.Encoded, inlineLookup, out _))
                {
                    yield return EncodedTriple.FromEncoded(subjectKey, predicateKey, @object.Encoded);
                }

                continue;
            }

            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(leaf.EdgeMaps[0]))
            {
                yield return EncodedTriple.FromEncoded(subjectKey, predicateKey, entry.Key);
            }
        }
    }

    //Mirror of MatchBySubjectsCore across the object position. The
    //P-first depth-2 node has EdgeMaps[1] keyed by O and (when FN) its
    //depth-1 leaves are keyed by S; that ordering follows the same
    //BuildPathDepth2 invariant cited above.
    private static IEnumerable<EncodedTriple> MatchByObjectsCore(
        HypertrieNode root,
        NodeStore store,
        TermId subject,
        TermId predicate,
        ReadOnlyMemory<TermId> objects)
    {
        InlineKeyLookup inlineLookup = InlineKeyLookups.Scalar;

        if(!TryDescendRoot(root, store, PositionPredicate, predicate.Encoded, inlineLookup, out HypertrieNode depth2, out NodeHandle sen2Handle))
        {
            yield break;
        }

        uint predicateKey = predicate.Encoded;

        if(sen2Handle.IsSingleEntryPair)
        {
            //The predicate's whole mapping is one (subject, object)
            //pair — the P-first inner positions in ascending order.
            (uint pairSubject, uint pairObject) = store.GetPair(sen2Handle);

            if(subject.IsNone || subject.Encoded == pairSubject)
            {
                for(int i = 0; i < objects.Length; i++)
                {
                    if(objects.Span[i].Encoded == pairObject)
                    {
                        yield return EncodedTriple.FromEncoded(pairSubject, predicateKey, pairObject);

                        break;
                    }
                }
            }

            yield break;
        }

        for(int i = 0; i < objects.Length; i++)
        {
            uint objectKey = objects.Span[i].Encoded;
            if(!EdgeMap.TryGetChild(in depth2.EdgeMaps[1], objectKey, inlineLookup, out NodeHandle leafHandle)
                || leafHandle.IsNone)
            {
                continue;
            }

            if(leafHandle.IsSingleEntry)
            {
                //SEN inline key is the subject for the object-keyed
                //slot of the P-first depth-2 node.
                uint senSubject = leafHandle.SingleEntryKey;
                if(!subject.IsNone && subject.Encoded != senSubject)
                {
                    continue;
                }

                yield return EncodedTriple.FromEncoded(senSubject, predicateKey, objectKey);

                continue;
            }

            HypertrieNode leaf = store.GetByHandle(leafHandle);
            if(!subject.IsNone)
            {
                if(EdgeMap.TryGetChild(in leaf.EdgeMaps[0], subject.Encoded, inlineLookup, out _))
                {
                    yield return EncodedTriple.FromEncoded(subject.Encoded, predicateKey, objectKey);
                }

                continue;
            }

            foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(leaf.EdgeMaps[0]))
            {
                yield return EncodedTriple.FromEncoded(entry.Key, predicateKey, objectKey);
            }
        }
    }

    //Resolves a SEN child at the depth-2 → depth-1 transition. The
    //SEN carries the last position's value inline; this method
    //applies any bound on that position, and on a match emits a
    //fully-resolved triple via the out parameter. A null out
    //indicates the SEN's key did not match the bound.
    private static void ProcessSingleEntryChild(
        MatchFrame frame,
        int chosenPosition,
        uint chosenValue,
        byte childPending,
        uint senKey,
        TermId subject,
        TermId predicate,
        TermId @object,
        out EncodedTriple? produced)
    {
        Debug.Assert(BitOperations.PopCount((uint)childPending) == 1,
            "SEN child requires exactly one remaining position; SEN at non-leaf descent is an invariant violation.");

        int lastPosition = BitOperations.TrailingZeroCount((uint)childPending);
        TermId lastBound = BoundForPosition(lastPosition, subject, predicate, @object);

        if(!lastBound.IsNone && lastBound.Encoded != senKey)
        {
            produced = null;

            return;
        }

        produced = BuildTripleTwoPositions(frame, chosenPosition, chosenValue, lastPosition, senKey);
    }

    //Resolves an SEN2 child at the depth-3 → depth-2 transition.
    //The pair carries the two remaining positions' values in
    //ascending original-position order; this method applies any
    //bounds on those positions and, on a match, emits the fully-
    //resolved triple via the out parameter. A null out indicates a
    //bound mismatch.
    private static void ProcessSingleEntryPairChild(
        int chosenPosition,
        uint chosenValue,
        byte childPending,
        uint pairFirst,
        uint pairSecond,
        TermId subject,
        TermId predicate,
        TermId @object,
        out EncodedTriple? produced)
    {
        Debug.Assert(BitOperations.PopCount((uint)childPending) == 2,
            "SEN2 child requires exactly two remaining positions; SEN2 below the root descent is an invariant violation.");

        int lowPosition = BitOperations.TrailingZeroCount((uint)childPending);
        int highPosition = BitOperations.TrailingZeroCount((uint)(childPending & (childPending - 1)));

        TermId lowBound = BoundForPosition(lowPosition, subject, predicate, @object);
        TermId highBound = BoundForPosition(highPosition, subject, predicate, @object);

        if((!lowBound.IsNone && lowBound.Encoded != pairFirst)
            || (!highBound.IsNone && highBound.Encoded != pairSecond))
        {
            produced = null;

            return;
        }

        produced = BuildTripleThreePositions(chosenPosition, chosenValue, lowPosition, pairFirst, highPosition, pairSecond);
    }

    //Builds a triple from three (position, value) resolutions
    //covering all of subject, predicate, and object. Used at SEN2
    //descent where the root entry's key and both pair keys resolve
    //in one step.
    private static EncodedTriple BuildTripleThreePositions(
        int firstPosition,
        uint firstValue,
        int secondPosition,
        uint secondValue,
        int thirdPosition,
        uint thirdValue)
    {
        uint s =
            firstPosition == PositionSubject ? firstValue :
            secondPosition == PositionSubject ? secondValue :
            thirdValue;
        uint p =
            firstPosition == PositionPredicate ? firstValue :
            secondPosition == PositionPredicate ? secondValue :
            thirdValue;
        uint o =
            firstPosition == PositionObject ? firstValue :
            secondPosition == PositionObject ? secondValue :
            thirdValue;

        return EncodedTriple.FromEncoded(s, p, o);
    }

    //Returns the user-supplied bound for the given original position.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TermId BoundForPosition(int position, TermId subject, TermId predicate, TermId @object)
    {
        return position switch
        {
            PositionSubject => subject,
            PositionPredicate => predicate,
            PositionObject => @object,
            _ => throw new UnreachableException(),
        };
    }

    //Builds a triple from a match frame plus two newly-resolved
    //positions. Used at SEN descent where both the depth-2 entry's
    //key and the SEN's inline key are resolved in one step.
    private static EncodedTriple BuildTripleTwoPositions(MatchFrame frame, int firstPosition, uint firstValue, int secondPosition, uint secondValue)
    {
        uint s =
            firstPosition == PositionSubject ? firstValue :
            secondPosition == PositionSubject ? secondValue :
            frame.ResolvedSubject ?? throw new InvalidOperationException("Subject not resolved when constructing triple.");
        uint p =
            firstPosition == PositionPredicate ? firstValue :
            secondPosition == PositionPredicate ? secondValue :
            frame.ResolvedPredicate ?? throw new InvalidOperationException("Predicate not resolved when constructing triple.");
        uint o =
            firstPosition == PositionObject ? firstValue :
            secondPosition == PositionObject ? secondValue :
            frame.ResolvedObject ?? throw new InvalidOperationException("Object not resolved when constructing triple.");
        return EncodedTriple.FromEncoded(s, p, o);
    }

    //Picks the next original position to descend on. Preference is
    //"a pending position whose query value is bound" — that is the
    //position the user has constrained, so descending on it slices
    //the search space immediately. If no bound position is pending,
    //picks the pending position whose edge map has the smallest
    //cardinality (the `min_card_pos` heuristic) — descending on the
    //narrowest fan-out keeps the result-space exploration tightest.
    //Ties on cardinality go to the lower-numbered position. The
    //choice is by *original* position (0=S, 1=P, 2=O); the caller
    //maps it to the local edge map index via EdgeMapIndexOf.
    private static int SelectPosition(byte pending, TermId subject, TermId predicate, TermId @object, HypertrieNode node)
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

        //No bound position pending. Choose the pending position
        //whose edge map carries the fewest entries; ties go to the
        //lower-numbered position by the order this loop visits.
        int chosen = firstPending;
        int chosenCount = EdgeMap.Count(in node.EdgeMaps[EdgeMapIndexOf(pending, firstPending)]);
        for(int position = firstPending + 1; position < 3; position++)
        {
            if(!IsPending(pending, position))
            {
                continue;
            }

            int count = EdgeMap.Count(in node.EdgeMaps[EdgeMapIndexOf(pending, position)]);
            if(count < chosenCount)
            {
                chosen = position;
                chosenCount = count;
            }
        }

        return chosen;
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
