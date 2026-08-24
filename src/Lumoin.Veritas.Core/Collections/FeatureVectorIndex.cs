using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// Which subsumption direction a <see cref="FeatureVectorIndex"/> retrieval walks, relative to
/// the query set.
/// </summary>
public enum SubsumptionDirection
{
    /// <summary>
    /// The stored sets that are supersets of the query — <c>query ⊆ stored</c>. Reading the set as a
    /// concept label, these are the more-specific ("subsumed") concepts; the monotone features
    /// descend on <c>≥</c>.
    /// </summary>
    Supersets,

    /// <summary>
    /// The stored sets that are subsets of the query — <c>stored ⊆ query</c>. Reading the set as a
    /// concept label, these are the more-general ("subsuming") concepts; the monotone features
    /// descend on <c>≤</c>.
    /// </summary>
    Subsets
}

/// <summary>
/// A feature-vector trie for subsumption retrieval over flat <see cref="ulong"/> bitsets: given a
/// query set, it returns the stored sets that contain it (<see cref="SubsumptionDirection.Supersets"/>)
/// or that it contains (<see cref="SubsumptionDirection.Subsets"/>) without an all-pairs scan — the
/// indexed replacement for the reasoner's pairwise double-blocking comparison, and the substrate the
/// anywhere-blocking and dependency-aware unsat caches retrieve over.
/// </summary>
/// <remarks>
/// <para>
/// <b>Monotone features.</b> The domain's bits are partitioned into word-aligned buckets; a set's
/// feature for a bucket is the population count of its bits there. Population count is monotone under
/// set inclusion — <c>A ⊆ B</c> implies <c>feature(A) ≤ feature(B)</c> for every bucket — so a
/// necessary condition for <c>query ⊆ stored</c> is that the stored set's every bucket feature is
/// <c>≥</c> the query's, and for <c>stored ⊆ query</c> that every stored feature is <c>≤</c> the
/// query's. The trie keys each level on one bucket's feature value, and a query descends only the
/// children satisfying its direction's inequality. Monotonicity is load-bearing: it is what makes the
/// pruning lose no true match. The inequality direction is asymmetric — pairing the wrong relation
/// with a direction silently drops real results.
/// </para>
/// <para>
/// <b>Candidates, then confirmation.</b> The feature condition is necessary but not sufficient, so a
/// leaf reached by the descent yields candidates, never answers. Each candidate is confirmed with an
/// exact <see cref="BitsetOps.IsSubsetOf"/> in the direction's orientation before it joins the result.
/// The naive all-pairs counterpart (<see cref="RetrieveNaive"/>) is the differential oracle and the
/// reference fallback for a store too small to index: the trie retrieval returns exactly the same set.
/// </para>
/// <para>
/// <b>Shape.</b> The trie is built once over a fixed corpus. Levels are ordered least-informative
/// first (by the value-span of each bucket feature across the corpus); this affects only the trie's
/// shape and the descent's work, never which sets are returned. The build and the query both walk with
/// an explicit stack, never call-stack recursion. After construction the index is immutable, so
/// concurrent retrievals — each with its own working stack — are safe.
/// </para>
/// </remarks>
public sealed class FeatureVectorIndex
{
    /// <summary>The default cap on the number of feature buckets when a caller does not specify one.</summary>
    private const int DefaultBucketCount = 16;

    /// <summary>The largest feature-vector width held on the stack during a query before renting from the pool.</summary>
    private const int StackBucketLimit = 64;

    /// <summary>The number of elements the domain spans; a valid element bit lies in <c>[0, DomainSize)</c>.</summary>
    public int DomainSize { get; }

    /// <summary>The number of stored sets the index holds.</summary>
    public int ElementCount { get; }

    /// <summary>The number of words a bitset over the domain occupies; every stored and queried set is this wide.</summary>
    private int WordCount { get; }

    /// <summary>The number of feature buckets — the feature-vector width and the trie depth.</summary>
    private int BucketCount { get; }

    /// <summary>
    /// The word offsets bounding each bucket: bucket <c>b</c> spans words
    /// <c>[BucketWordStart[b], BucketWordStart[b + 1])</c>. Length is <see cref="BucketCount"/> + 1.
    /// </summary>
    private int[] BucketWordStart { get; }

    /// <summary>The trie level order over the feature buckets: level <c>L</c> keys on feature <c>Order[L]</c>, least-informative first.</summary>
    private int[] Order { get; }

    /// <summary>
    /// The stored sets' words, row-major: element <c>id</c> occupies
    /// <c>[id · WordCount, (id + 1) · WordCount)</c>. Durable index state, the source the candidate
    /// confirmation reads.
    /// </summary>
    private ulong[] Store { get; }

    /// <summary>The trie root; the descent starts here.</summary>
    private Node Root { get; }

    /// <summary>
    /// Builds the index over <paramref name="elements"/>, a fixed corpus of bitsets over a domain of
    /// <paramref name="domainSize"/> elements.
    /// </summary>
    /// <param name="elements">The sets to index, each a bitset of exactly the domain's word width with a clean tail.</param>
    /// <param name="domainSize">The number of elements the domain spans; must be non-negative.</param>
    /// <param name="bucketCount">The number of feature buckets; <see langword="null"/> picks a default. Capped at the domain's word count, since buckets are word-aligned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="domainSize"/> is negative, or <paramref name="bucketCount"/> is non-positive.</exception>
    /// <exception cref="ArgumentException">An element's word width does not match the domain.</exception>
    public FeatureVectorIndex(IReadOnlyList<ReadOnlyMemory<ulong>> elements, int domainSize, int? bucketCount = null)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentOutOfRangeException.ThrowIfNegative(domainSize);
        if(bucketCount is int requestedBuckets)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedBuckets);
        }

        DomainSize = domainSize;
        WordCount = BitsetOps.WordCount(domainSize);
        ElementCount = elements.Count;
        BucketCount = WordCount == 0
            ? 0
            : Math.Clamp(bucketCount ?? Math.Min(WordCount, DefaultBucketCount), 1, WordCount);

        BucketWordStart = BuildBucketBounds(WordCount, BucketCount);
        Store = new ulong[ElementCount * WordCount];

        for(int id = 0; id < ElementCount; id++)
        {
            ReadOnlySpan<ulong> source = elements[id].Span;
            if(source.Length != WordCount)
            {
                throw new ArgumentException($"Element {id} has {source.Length} words; the domain needs {WordCount}.", nameof(elements));
            }

            Span<ulong> row = ElementRow(id);
            source.CopyTo(row);
            BitsetOps.MaskTail(row, DomainSize);
        }

        //Compute every element's feature vector once, then order the trie levels by each
        //feature's value-span across the corpus and insert the elements by that order.
        int featureCells = Math.Max(1, ElementCount * BucketCount);
        using IMemoryOwner<int> featuresOwner = VeritasMemoryPool<int>.Shared.Rent(featureCells);
        Span<int> featureMatrix = featuresOwner.Memory.Span[..(ElementCount * BucketCount)];
        for(int id = 0; id < ElementCount; id++)
        {
            ComputeFeatures(ElementRow(id), featureMatrix.Slice(id * BucketCount, BucketCount));
        }

        Order = BuildLevelOrder(featureMatrix, ElementCount, BucketCount);
        Root = new Node();
        for(int id = 0; id < ElementCount; id++)
        {
            Insert(id, featureMatrix.Slice(id * BucketCount, BucketCount));
        }
    }

    /// <summary>
    /// Appends to <paramref name="idsToAppendTo"/> the ids of the stored sets standing in the
    /// <paramref name="direction"/> subsumption relation to <paramref name="query"/>, found through
    /// the feature trie and confirmed exactly. The order of appended ids is unspecified.
    /// </summary>
    /// <param name="query">The query set, a bitset of the domain's word width with a clean tail.</param>
    /// <param name="direction">Which subsumption relation to retrieve.</param>
    /// <param name="idsToAppendTo">The list the matching element ids are appended to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="idsToAppendTo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="query"/>'s word width does not match the domain.</exception>
    public void Retrieve(ReadOnlySpan<ulong> query, SubsumptionDirection direction, List<int> idsToAppendTo)
    {
        ArgumentNullException.ThrowIfNull(idsToAppendTo);
        CheckQueryLength(query.Length);

        Span<int> queryFeatures = BucketCount <= StackBucketLimit ? stackalloc int[BucketCount] : new int[BucketCount];
        ComputeFeatures(query, queryFeatures);

        //Iterative dual descent: follow only the children whose bucket feature satisfies the
        //direction's inequality, and confirm every leaf candidate exactly.
        Stack<NodeWalk> stack = new();
        stack.Push(new NodeWalk(Root, 0));
        while(stack.Count > 0)
        {
            NodeWalk walk = stack.Pop();
            if(walk.Level == BucketCount)
            {
                ConfirmLeaf(walk.Node, query, direction, idsToAppendTo);

                continue;
            }

            if(walk.Node.Children is null)
            {
                continue;
            }

            int threshold = queryFeatures[Order[walk.Level]];
            foreach(KeyValuePair<int, Node> child in walk.Node.Children)
            {
                bool follow = direction switch
                {
                    SubsumptionDirection.Supersets => child.Key >= threshold,
                    SubsumptionDirection.Subsets => child.Key <= threshold,
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown subsumption direction.")
                };

                if(follow)
                {
                    stack.Push(new NodeWalk(child.Value, walk.Level + 1));
                }
            }
        }
    }

    /// <summary>
    /// Appends to <paramref name="idsToAppendTo"/> the ids of the stored sets standing in the
    /// <paramref name="direction"/> subsumption relation to <paramref name="query"/> by scanning every
    /// stored set — the differential oracle for <see cref="Retrieve"/> and the reference fallback for a
    /// store too small to index. Appended ids come out in ascending order.
    /// </summary>
    /// <param name="query">The query set, a bitset of the domain's word width with a clean tail.</param>
    /// <param name="direction">Which subsumption relation to retrieve.</param>
    /// <param name="idsToAppendTo">The list the matching element ids are appended to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="idsToAppendTo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="query"/>'s word width does not match the domain.</exception>
    public void RetrieveNaive(ReadOnlySpan<ulong> query, SubsumptionDirection direction, List<int> idsToAppendTo)
    {
        ArgumentNullException.ThrowIfNull(idsToAppendTo);
        CheckQueryLength(query.Length);

        for(int id = 0; id < ElementCount; id++)
        {
            if(Confirm(ElementRow(id), query, direction))
            {
                idsToAppendTo.Add(id);
            }
        }
    }

    /// <summary>Computes the bucket population counts of <paramref name="words"/> into <paramref name="features"/>.</summary>
    /// <param name="words">The set whose features are computed.</param>
    /// <param name="features">Receives one population count per bucket; length is <see cref="BucketCount"/>.</param>
    private void ComputeFeatures(ReadOnlySpan<ulong> words, Span<int> features)
    {
        for(int b = 0; b < BucketCount; b++)
        {
            int start = BucketWordStart[b];
            features[b] = BitsetOps.PopCount(words.Slice(start, BucketWordStart[b + 1] - start));
        }
    }

    /// <summary>Inserts element <paramref name="id"/> into the trie along its feature vector, in level order.</summary>
    /// <param name="id">The element id.</param>
    /// <param name="features">The element's feature vector, indexed by bucket.</param>
    private void Insert(int id, ReadOnlySpan<int> features)
    {
        Node node = Root;
        for(int level = 0; level < BucketCount; level++)
        {
            int key = features[Order[level]];
            node.Children ??= new SortedDictionary<int, Node>();
            if(!node.Children.TryGetValue(key, out Node? child))
            {
                child = new Node();
                node.Children[key] = child;
            }

            node = child;
        }

        node.ElementIds ??= [];
        node.ElementIds.Add(id);
    }

    /// <summary>Confirms each candidate id at a leaf with an exact subset test and appends the survivors.</summary>
    /// <param name="leaf">The leaf reached by the descent.</param>
    /// <param name="query">The query set.</param>
    /// <param name="direction">The subsumption relation being retrieved.</param>
    /// <param name="idsToAppendTo">The list confirmed ids are appended to.</param>
    private void ConfirmLeaf(Node leaf, ReadOnlySpan<ulong> query, SubsumptionDirection direction, List<int> idsToAppendTo)
    {
        if(leaf.ElementIds is null)
        {
            return;
        }

        foreach(int id in leaf.ElementIds)
        {
            if(Confirm(ElementRow(id), query, direction))
            {
                idsToAppendTo.Add(id);
            }
        }
    }

    /// <summary>The exact subset test deciding whether a stored set stands in the <paramref name="direction"/> relation to the query.</summary>
    /// <param name="stored">The stored set's words.</param>
    /// <param name="query">The query set's words.</param>
    /// <param name="direction">The subsumption relation: supersets confirm <c>query ⊆ stored</c>, subsets confirm <c>stored ⊆ query</c>.</param>
    /// <returns><see langword="true"/> when the relation holds.</returns>
    private static bool Confirm(ReadOnlySpan<ulong> stored, ReadOnlySpan<ulong> query, SubsumptionDirection direction)
    {
        return direction switch
        {
            SubsumptionDirection.Supersets => BitsetOps.IsSubsetOf(query, stored),
            SubsumptionDirection.Subsets => BitsetOps.IsSubsetOf(stored, query),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown subsumption direction.")
        };
    }

    /// <summary>The words of stored element <paramref name="id"/>.</summary>
    /// <param name="id">The element id.</param>
    /// <returns>The element's row in the store.</returns>
    private Span<ulong> ElementRow(int id)
    {
        return Store.AsSpan(id * WordCount, WordCount);
    }

    /// <summary>Throws when a query's word width does not match the domain.</summary>
    /// <param name="queryLength">The query's word count.</param>
    /// <exception cref="ArgumentException">The length does not equal <see cref="WordCount"/>.</exception>
    private void CheckQueryLength(int queryLength)
    {
        if(queryLength != WordCount)
        {
            throw new ArgumentException($"The query has {queryLength} words; the domain needs {WordCount}.", nameof(queryLength));
        }
    }

    /// <summary>Distributes <paramref name="wordCount"/> words across <paramref name="bucketCount"/> word-aligned buckets as evenly as possible.</summary>
    /// <param name="wordCount">The total number of words.</param>
    /// <param name="bucketCount">The number of buckets.</param>
    /// <returns>The bucket bounds: bucket <c>b</c> spans words <c>[result[b], result[b + 1])</c>.</returns>
    private static int[] BuildBucketBounds(int wordCount, int bucketCount)
    {
        int[] bounds = new int[bucketCount + 1];
        if(bucketCount == 0)
        {
            return bounds;
        }

        int baseWords = wordCount / bucketCount;
        int remainder = wordCount % bucketCount;
        int offset = 0;
        for(int b = 0; b < bucketCount; b++)
        {
            offset += baseWords + (b < remainder ? 1 : 0);
            bounds[b + 1] = offset;
        }

        return bounds;
    }

    /// <summary>
    /// Orders the trie levels over the feature buckets least-informative first — by the ascending
    /// value-span of each bucket feature across the corpus, ties broken by bucket index — so the trie
    /// shares prefixes among the features that vary least. The order changes only the trie's shape, not
    /// which sets a query retrieves.
    /// </summary>
    /// <param name="featureMatrix">The corpus feature vectors, row-major by element.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <param name="bucketCount">The number of feature buckets.</param>
    /// <returns>The bucket indices in level order.</returns>
    private static int[] BuildLevelOrder(ReadOnlySpan<int> featureMatrix, int elementCount, int bucketCount)
    {
        int[] order = new int[bucketCount];
        for(int b = 0; b < bucketCount; b++)
        {
            order[b] = b;
        }

        int[] span = new int[bucketCount];
        for(int b = 0; b < bucketCount; b++)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            for(int id = 0; id < elementCount; id++)
            {
                int feature = featureMatrix[(id * bucketCount) + b];
                min = Math.Min(min, feature);
                max = Math.Max(max, feature);
            }

            span[b] = elementCount == 0 ? 0 : max - min;
        }

        Array.Sort(order, (left, right) => span[left] != span[right] ? span[left].CompareTo(span[right]) : left.CompareTo(right));

        return order;
    }

    /// <summary>A node of the feature trie: an internal node keys its children on one bucket feature, a leaf holds the element ids reaching it.</summary>
    private sealed class Node
    {
        /// <summary>The children keyed by this level's bucket feature value, in ascending key order; <see langword="null"/> at a leaf.</summary>
        public SortedDictionary<int, Node>? Children { get; set; }

        /// <summary>The ids of the elements whose feature vector reaches this leaf; <see langword="null"/> at an internal node.</summary>
        public List<int>? ElementIds { get; set; }
    }

    /// <summary>A pending trie node paired with the level it sits at, the unit of the iterative descent's work stack.</summary>
    /// <param name="Node">The node to visit.</param>
    /// <param name="Level">The node's level: the number of features already descended.</param>
    private readonly record struct NodeWalk(Node Node, int Level);
}
