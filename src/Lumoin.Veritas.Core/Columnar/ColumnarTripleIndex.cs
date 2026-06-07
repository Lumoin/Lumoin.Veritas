using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A columnar triple index: the triple set materialised in all six
/// position permutations, each as a three-level compressed-sparse
/// layout of contiguous sorted columns. One permutation answers
/// one descent shape — the bound positions form the permutation's
/// prefix and the variable positions follow — so a worst-case
/// optimal join always finds an order whose levels match its
/// variable elimination sequence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout per permutation.</b> Level 0 holds the distinct
/// first-position values in ascending order; a parallel offset
/// column marks where each value's group begins in level 1.
/// Level 1 holds, per level-0 group, the distinct second-position
/// values in ascending order, again with offsets into level 2.
/// Level 2 holds the third-position values, ascending within each
/// level-1 group. Every column is a contiguous <see cref="uint"/>
/// array, so a level's candidate set is one span and a seek is a
/// search over adjacent elements — no per-node objects and no
/// pointer chasing between levels, only offset arithmetic.
/// </para>
/// <para>
/// <b>Mutability model.</b> The base columns are immutable after
/// construction. Updates and deletes arrive as journal-shaped
/// deltas — sorted addition runs and a removal set — merged by the
/// cursors at read time; compaction folds an accumulated delta
/// into a fresh base. The base is the "main" half of that
/// delta-main pair.
/// </para>
/// <para>
/// <b>Build cost.</b> Construction sorts the triple set once per
/// permutation and emits each level in a single pass — no
/// per-triple intermediate structures.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarTripleIndex Triples={TripleCount}")]
public sealed class ColumnarTripleIndex
{
    /// <summary>The six position permutations, indexed by <see cref="PermutationIndex"/>: each entry lists the RDF positions (0 = subject, 1 = predicate, 2 = object) in descent order.</summary>
    private static readonly byte[][] Permutations =
    [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0],
    ];

    //One accumulated-delta fraction of the base size triggers
    //compaction: when added + removed reach a quarter of the base
    //triple count, Apply folds the delta into a fresh base instead
    //of growing the merge cost.
    private const int CompactionDenominator = 4;

    private static readonly EncodedTriple[] EmptyTriples = [];

    private readonly ColumnarOrder[] orders;

    //The accumulated delta over the immutable base: additions and
    //removals as canonical sets (for membership and Apply
    //normalisation) plus per-permutation sorted runs (for the merge
    //cursors). Invariants: added ∩ base = ∅, removed ⊆ base,
    //added ∩ removed = ∅.
    private readonly HashSet<EncodedTriple> addedSet;

    private readonly HashSet<EncodedTriple> removedSet;

    private readonly EncodedTriple[][] addedByOrder;

    private readonly EncodedTriple[][] removedByOrder;

    private readonly int baseTripleCount;

    /// <summary>The number of distinct triples the merged view contains — the base count net of the accumulated delta.</summary>
    public int TripleCount => baseTripleCount - removedSet.Count + addedSet.Count;

    private ColumnarTripleIndex(
        ColumnarOrder[] orders,
        int baseTripleCount,
        HashSet<EncodedTriple> addedSet,
        HashSet<EncodedTriple> removedSet,
        EncodedTriple[][] addedByOrder,
        EncodedTriple[][] removedByOrder)
    {
        this.orders = orders;
        this.baseTripleCount = baseTripleCount;
        this.addedSet = addedSet;
        this.removedSet = removedSet;
        this.addedByOrder = addedByOrder;
        this.removedByOrder = removedByOrder;
    }

    /// <summary>
    /// Builds the index from the given triples with an empty delta.
    /// Duplicates are absorbed; the realised
    /// <see cref="TripleCount"/> is the distinct count.
    /// </summary>
    /// <param name="triples">The triples to index.</param>
    /// <returns>The constructed index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    public static ColumnarTripleIndex Build(IEnumerable<EncodedTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(triples);

        HashSet<EncodedTriple> distinct = [.. triples];
        EncodedTriple[] working = [.. distinct];
        ColumnarOrder[] orders = new ColumnarOrder[Permutations.Length];

        for(int i = 0; i < Permutations.Length; i++)
        {
            orders[i] = ColumnarOrder.Build(working, Permutations[i]);
        }

        EncodedTriple[][] emptyRuns = [EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples];

        return new ColumnarTripleIndex(orders, working.Length, [], [], emptyRuns, emptyRuns);
    }

    /// <summary>
    /// Produces a new index whose merged view reflects
    /// <paramref name="additions"/> and <paramref name="removals"/>
    /// applied to this one. The base columns are shared; the delta
    /// accumulates — until it reaches a quarter of the base size,
    /// at which point the merged triple set is folded into a fresh
    /// base with an empty delta.
    /// </summary>
    /// <param name="additions">Triples to add. Triples already present in the merged view are ignored.</param>
    /// <param name="removals">Triples to remove. Triples absent from the merged view are ignored.</param>
    /// <returns>The new index; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <remarks>
    /// One application costs O(batch · log batch + accumulated
    /// delta): only the incoming batch is sorted, and each
    /// accumulated per-permutation run is evolved by a single
    /// linear merge pass. A sustained stream of small commits
    /// therefore stays linear in the accumulated delta between
    /// compactions — the write-rate contract insert-heavy graph
    /// workloads rely on.
    /// </remarks>
    public ColumnarTripleIndex Apply(IEnumerable<EncodedTriple> additions, IEnumerable<EncodedTriple> removals)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        HashSet<EncodedTriple> newAdded = [.. addedSet];
        HashSet<EncodedTriple> newRemoved = [.. removedSet];

        foreach(EncodedTriple triple in additions)
        {
            if(newAdded.Contains(triple))
            {
                continue;
            }

            //A base triple removed earlier is re-added by clearing
            //its tombstone; a genuinely new triple joins the added
            //set; a base triple still visible is a no-op.
            if(!newRemoved.Remove(triple) && !ContainsBase(triple))
            {
                newAdded.Add(triple);
            }
        }

        foreach(EncodedTriple triple in removals)
        {
            //An added triple is removed by dropping it from the
            //added set; a visible base triple gains a tombstone; a
            //triple absent from the merged view is a no-op.
            if(!newAdded.Remove(triple) && ContainsBase(triple))
            {
                newRemoved.Add(triple);
            }
        }

        if((newAdded.Count + newRemoved.Count) * CompactionDenominator >= baseTripleCount || baseTripleCount == 0)
        {
            HashSet<EncodedTriple> merged = [.. EnumerateBaseTriples()];
            merged.ExceptWith(newRemoved);
            merged.UnionWith(newAdded);

            return Build(merged);
        }

        //Evolve the per-order runs by linear merge: only this
        //batch's net effect is sorted (batch-sized), and each
        //accumulated run is walked once. This bounds one Apply at
        //O(batch log batch + accumulated delta), keeping sustained
        //small-commit streams linear — never quadratic — in the
        //accumulated delta between compactions.
        EncodedTriple[] addedInsertions = CollectMissingFrom(newAdded, addedSet);
        EncodedTriple[] addedDeletions = CollectMissingFrom(addedSet, newAdded);
        EncodedTriple[] removedInsertions = CollectMissingFrom(newRemoved, removedSet);
        EncodedTriple[] removedDeletions = CollectMissingFrom(removedSet, newRemoved);

        return new ColumnarTripleIndex(
            orders,
            baseTripleCount,
            newAdded,
            newRemoved,
            MergePerOrder(addedByOrder, addedInsertions, addedDeletions),
            MergePerOrder(removedByOrder, removedInsertions, removedDeletions));
    }

    /// <summary>
    /// Enumerates the merged view's triples — the base net of the
    /// accumulated delta. Ordering is unspecified.
    /// </summary>
    /// <returns>The merged triple sequence.</returns>
    public IEnumerable<EncodedTriple> EnumerateTriples()
    {
        foreach(EncodedTriple triple in EnumerateBaseTriples())
        {
            if(!removedSet.Contains(triple))
            {
                yield return triple;
            }
        }

        foreach(EncodedTriple triple in addedSet)
        {
            yield return triple;
        }
    }

    //Walks the subject-predicate-object base order reconstructing
    //its triples; the delta is not applied. The walk is sequential,
    //so the per-column readers decode each block exactly once.
    private IEnumerable<EncodedTriple> EnumerateBaseTriples()
    {
        ColumnarOrder spo = orders[0];
        BlockPackedColumnReader values0 = new(spo.ValuesColumnAt(0));
        BlockPackedColumnReader offsets0 = new(spo.OffsetsColumnAt(0));
        BlockPackedColumnReader values1 = new(spo.ValuesColumnAt(1));
        BlockPackedColumnReader offsets1 = new(spo.OffsetsColumnAt(1));
        BlockPackedColumnReader values2 = new(spo.ValuesColumnAt(2));

        for(int i = 0; i < values0.Length; i++)
        {
            uint subject = values0.ValueAt(i);
            int level1Start = (int)offsets0.ValueAt(i);
            int level1End = (int)offsets0.ValueAt(i + 1);

            for(int j = level1Start; j < level1End; j++)
            {
                uint predicate = values1.ValueAt(j);
                int level2Start = (int)offsets1.ValueAt(j);
                int level2End = (int)offsets1.ValueAt(j + 1);

                for(int k = level2Start; k < level2End; k++)
                {
                    yield return EncodedTriple.FromEncoded(subject, predicate, values2.ValueAt(k));
                }
            }
        }
    }

    //Collects the members of `source` absent from `reference` —
    //the net insertions (or deletions) one Apply contributed to a
    //delta set.
    private static EncodedTriple[] CollectMissingFrom(HashSet<EncodedTriple> source, HashSet<EncodedTriple> reference)
    {
        List<EncodedTriple> result = [];

        foreach(EncodedTriple triple in source)
        {
            if(!reference.Contains(triple))
            {
                result.Add(triple);
            }
        }

        return [.. result];
    }

    //Evolves the per-permutation sorted runs by one batch: the
    //insertions and deletions are sorted per permutation (they are
    //batch-sized), then each existing run is walked once in a
    //three-way merge.
    private static EncodedTriple[][] MergePerOrder(
        EncodedTriple[][] oldRuns,
        EncodedTriple[] insertions,
        EncodedTriple[] deletions)
    {
        EncodedTriple[][] runs = new EncodedTriple[Permutations.Length][];

        for(int i = 0; i < Permutations.Length; i++)
        {
            byte position0 = Permutations[i][0];
            byte position1 = Permutations[i][1];
            byte position2 = Permutations[i][2];

            //The shared batch arrays are re-sorted per permutation;
            //copies keep one permutation's sort from disturbing the
            //next.
            EncodedTriple[] sortedInsertions = [.. insertions];
            EncodedTriple[] sortedDeletions = [.. deletions];

            ColumnarSearch.SortByPermutation(sortedInsertions, position0, position1, position2);
            ColumnarSearch.SortByPermutation(sortedDeletions, position0, position1, position2);

            runs[i] = MergeRun(oldRuns[i], sortedInsertions, sortedDeletions, position0, position1, position2);
        }

        return runs;
    }

    //Produces old − deletions + insertions in one pass over three
    //runs sorted under the same permutation. A deletion always
    //names a triple present in the old run, and an insertion never
    //does, so the result length is exact. Packed keys cover all
    //three columns, so key equality is triple equality.
    private static EncodedTriple[] MergeRun(
        EncodedTriple[] oldRun,
        EncodedTriple[] insertions,
        EncodedTriple[] deletions,
        byte position0,
        byte position1,
        byte position2)
    {
        int resultLength = oldRun.Length - deletions.Length + insertions.Length;

        if(resultLength == 0)
        {
            return EmptyTriples;
        }

        EncodedTriple[] result = new EncodedTriple[resultLength];
        int oldIndex = 0;
        int insertIndex = 0;
        int deleteIndex = 0;
        int write = 0;

        while(oldIndex < oldRun.Length)
        {
            UInt128 oldKey = ColumnarSearch.PackKey(in oldRun[oldIndex], position0, position1, position2);

            if(deleteIndex < deletions.Length
                && ColumnarSearch.PackKey(in deletions[deleteIndex], position0, position1, position2) == oldKey)
            {
                oldIndex++;
                deleteIndex++;

                continue;
            }

            if(insertIndex < insertions.Length
                && ColumnarSearch.PackKey(in insertions[insertIndex], position0, position1, position2) < oldKey)
            {
                result[write++] = insertions[insertIndex++];

                continue;
            }

            result[write++] = oldRun[oldIndex++];
        }

        while(insertIndex < insertions.Length)
        {
            result[write++] = insertions[insertIndex++];
        }

        Debug.Assert(write == resultLength, "Merge must consume every insertion and deletion exactly once.");
        Debug.Assert(deleteIndex == deletions.Length, "Every deletion must match a triple in the old run.");

        return result;
    }

    /// <summary>
    /// Returns the added-triples run for the permutation at the
    /// given index, sorted under that permutation.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns>The sorted added run; empty when no delta has accumulated.</returns>
    internal ReadOnlySpan<EncodedTriple> AddedAt(int permutationIndex)
    {
        return addedByOrder[permutationIndex];
    }

    /// <summary>
    /// Returns the removed-triples run for the permutation at the
    /// given index, sorted under that permutation.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns>The sorted removed run; empty when no delta has accumulated.</returns>
    internal ReadOnlySpan<EncodedTriple> RemovedAt(int permutationIndex)
    {
        return removedByOrder[permutationIndex];
    }

    /// <summary>
    /// Returns the permutation index whose descent order starts
    /// with the given RDF positions, in the given sequence.
    /// </summary>
    /// <param name="positionSequence">The RDF positions (0 = subject, 1 = predicate, 2 = object) in descent order; one to three entries covering distinct positions.</param>
    /// <returns>The matching permutation index in [0, 6).</returns>
    /// <exception cref="ArgumentException">The sequence is empty, longer than three, or contains duplicates.</exception>
    public static int PermutationIndexFor(ReadOnlySpan<byte> positionSequence)
    {
        if(positionSequence.Length is < 1 or > 3)
        {
            throw new ArgumentException("A position sequence covers one to three positions.", nameof(positionSequence));
        }

        for(int i = 0; i < Permutations.Length; i++)
        {
            byte[] permutation = Permutations[i];
            bool matches = true;

            for(int j = 0; j < positionSequence.Length; j++)
            {
                if(permutation[j] != positionSequence[j])
                {
                    matches = false;

                    break;
                }
            }

            if(matches)
            {
                return i;
            }
        }

        throw new ArgumentException("The position sequence contains duplicate positions.", nameof(positionSequence));
    }

    /// <summary>
    /// Returns the columnar order at the given permutation index.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6), from <see cref="PermutationIndexFor"/>.</param>
    /// <returns>The order's column set.</returns>
    public ColumnarOrder OrderAt(int permutationIndex)
    {
        return orders[permutationIndex];
    }

    /// <summary>
    /// The RDF positions (0 = subject, 1 = predicate, 2 = object)
    /// of the permutation at the given index, in descent order.
    /// </summary>
    /// <param name="permutationIndex">A permutation index in [0, 6).</param>
    /// <returns>The three-position descent sequence.</returns>
    public static ReadOnlySpan<byte> PermutationAt(int permutationIndex)
    {
        return Permutations[permutationIndex];
    }

    /// <summary>
    /// Tests whether the merged view contains the given fully-bound
    /// triple: present in the base without a tombstone, or present
    /// in the accumulated additions.
    /// </summary>
    /// <param name="subject">The bound subject.</param>
    /// <param name="predicate">The bound predicate.</param>
    /// <param name="object">The bound object.</param>
    /// <returns><c>true</c> when the triple is present.</returns>
    public bool Contains(TermId subject, TermId predicate, TermId @object)
    {
        EncodedTriple triple = EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);

        if(addedSet.Count != 0 && addedSet.Contains(triple))
        {
            return true;
        }

        if(removedSet.Count != 0 && removedSet.Contains(triple))
        {
            return false;
        }

        return ContainsBase(triple);
    }

    //Tests the immutable base alone: three lower-bound searches
    //down the subject-predicate-object permutation, over one
    //stack-held block scratch — a cold one-shot descent that
    //allocates nothing.
    private bool ContainsBase(EncodedTriple triple)
    {
        ColumnarOrder spo = orders[0];
        Span<uint> keys = [triple.Subject.Encoded, triple.Predicate.Encoded, triple.Object.Encoded];
        Span<uint> scratch = stackalloc uint[BlockPackedColumn.BlockLength];

        int lo = 0;
        int hi = spo.ValuesLengthAt(0);

        for(int level = 0; level < 3; level++)
        {
            //The scratch is shared across columns, so the cache slot
            //resets whenever the column changes hands.
            BlockPackedColumn values = spo.ValuesColumnAt(level);
            int cachedBlock = -1;
            int found = values.LowerBound(lo, hi, keys[level], scratch, ref cachedBlock);

            if(found >= hi || values.ValueAt(found, scratch, ref cachedBlock) != keys[level])
            {
                return false;
            }

            if(level < 2)
            {
                BlockPackedColumn offsets = spo.OffsetsColumnAt(level);
                cachedBlock = -1;
                lo = (int)offsets.ValueAt(found, scratch, ref cachedBlock);
                hi = (int)offsets.ValueAt(found + 1, scratch, ref cachedBlock);
            }
        }

        return true;
    }
}
