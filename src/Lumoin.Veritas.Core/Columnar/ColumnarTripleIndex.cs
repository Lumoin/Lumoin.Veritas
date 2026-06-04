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

    private readonly ColumnarOrder[] orders;

    /// <summary>The number of distinct triples indexed.</summary>
    public int TripleCount { get; }

    private ColumnarTripleIndex(ColumnarOrder[] orders, int tripleCount)
    {
        this.orders = orders;
        TripleCount = tripleCount;
    }

    /// <summary>
    /// Builds the index from the given triples. Duplicates are
    /// absorbed; the realised <see cref="TripleCount"/> is the
    /// distinct count.
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

        return new ColumnarTripleIndex(orders, working.Length);
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
}
