using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The in-place radix <see cref="ColumnarSearch.SortByPermutation"/> is differentially equivalent to the
/// comparison sort it replaced: across every permutation and a range of key distributions (distinct, heavy
/// duplicates, monotone, reverse, single-value columns, threshold-crossing sizes, and the insertion-sort tail),
/// it produces the exact ordering an <c>Array.Sort</c> over the packed composite key produces. Because distinct
/// triples always carry distinct composite keys, that ordering is unique, so an element-wise match is the proof.
/// </summary>
[TestClass]
internal sealed class ColumnarSearchSortTests
{
    /// <summary>The six position permutations, each as the descent order of RDF positions (0 = subject, 1 = predicate, 2 = object).</summary>
    private static byte[][] Permutations { get; } =
    [
        [0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0],
    ];

    /// <summary>The radix sort matches the comparison sort by the packed composite key, across every permutation and distribution.</summary>
    [TestMethod]
    public void RadixSortMatchesComparisonSortAcrossPermutationsAndDistributions()
    {
        foreach(EncodedTriple[] dataset in Datasets())
        {
            foreach(byte[] permutation in Permutations)
            {
                EncodedTriple[] radix = (EncodedTriple[])dataset.Clone();
                ColumnarSearch.SortByPermutation(radix, permutation[0], permutation[1], permutation[2]);

                EncodedTriple[] reference = (EncodedTriple[])dataset.Clone();
                UInt128[] keys = new UInt128[reference.Length];
                for(int i = 0; i < reference.Length; i++)
                {
                    keys[i] = ColumnarSearch.PackKey(in reference[i], permutation[0], permutation[1], permutation[2]);
                }

                Array.Sort(keys, reference);

                Assert.IsTrue(
                    radix.AsSpan().SequenceEqual(reference),
                    $"Radix order diverged from the comparison sort (permutation {permutation[0]}{permutation[1]}{permutation[2]}, {dataset.Length} triples).");
            }
        }
    }

    /// <summary>The key distributions the differential check sweeps.</summary>
    /// <returns>Each dataset of triples.</returns>
    private static IEnumerable<EncodedTriple[]> Datasets()
    {
        yield return [];
        yield return [Triple(5, 9, 2)];
        yield return Generate(1000, i => Triple((uint)((i * 2654435761u) % 997), (uint)(i % 13), (uint)((i * 7) % 101)));
        yield return Generate(500, i => Triple((uint)(i % 3), (uint)(i % 5), (uint)(i % 2)));
        yield return Generate(300, i => Triple((uint)i, (uint)i, (uint)i));
        yield return Generate(300, i => Triple((uint)(300 - i), (uint)(300 - i), (uint)(300 - i)));
        yield return Generate(64, i => Triple(7, (uint)i, 3));
        yield return Generate(40, i => Triple((uint)(i % 7), (uint)(i % 4), (uint)(i % 9)));
        yield return Generate(33, i => Triple((uint)(i * 65521u), (uint)(i * 257u), (uint)i));
    }

    /// <summary>Builds a dataset of triples from a per-index generator.</summary>
    /// <param name="count">The number of triples.</param>
    /// <param name="make">Builds the triple at an index.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] Generate(int count, Func<int, EncodedTriple> make)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(int i = 0; i < count; i++)
        {
            triples[i] = make(i);
        }

        return triples;
    }

    /// <summary>Builds an encoded triple from its three identifiers.</summary>
    /// <param name="subject">The subject identifier.</param>
    /// <param name="predicate">The predicate identifier.</param>
    /// <param name="object">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Triple(uint subject, uint predicate, uint @object)
    {
        return EncodedTriple.FromEncoded(subject, predicate, @object);
    }
}
