using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak testing whether exploiting graph self-similarity through identifier
/// assignment shrinks the columnar footprint. A community-structured graph is
/// encoded twice: once with SCATTERED ids (a random permutation, no locality)
/// and once with CLUSTERED ids (community members given consecutive ids). For
/// each it reports the within-group level-2 value column (objects per subject)
/// under frame of reference and partitioned Elias-Fano, plus the whole-index
/// bits per triple under frame of reference and Elias-Fano. The intra-cluster
/// edge fraction is swept, because a single cross-cluster neighbour stretches a
/// group's value span and erodes the clustering benefit for both encodings.
/// </summary>
/// <remarks>Line-oriented output, the same shape as the other <c>--profile-*</c> soaks.</remarks>
internal static class IdReassignmentSoak
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Maps a clustered node index to the identifier it is stored under.</summary>
    /// <param name="node">The clustered node index, in <c>[0, nodeCount)</c>.</param>
    /// <returns>The stored identifier.</returns>
    private delegate uint NodeLabelDelegate(int node);

    /// <summary>Runs the id-assignment comparison over an intra-cluster fraction sweep.</summary>
    public static void RunIdReassignmentSoak()
    {
        foreach(int intraPercent in (ReadOnlySpan<int>)[100, 95, 80])
        {
            RunConfiguration(clusters: 400, nodesPerCluster: 500, fanOut: 16, intraClusterPercent: intraPercent);
        }
    }

    /// <summary>Builds the graph once, then measures it under clustered and scattered identifiers.</summary>
    /// <param name="clusters">The community count.</param>
    /// <param name="nodesPerCluster">The nodes per community.</param>
    /// <param name="fanOut">The out-edges per node.</param>
    /// <param name="intraClusterPercent">The percentage of edges that stay within the source's community.</param>
    private static void RunConfiguration(int clusters, int nodesPerCluster, int fanOut, int intraClusterPercent)
    {
        int nodeCount = clusters * nodesPerCluster;
        (int Source, int Target)[] edges = BuildEdges(clusters, nodesPerCluster, fanOut, intraClusterPercent);
        uint[] scatter = BuildScatterPermutation(nodeCount);

        Measurement clustered = Measure(edges, node => (uint)node);
        Measurement scattered = Measure(edges, node => scatter[node]);

        Console.WriteLine(
            $"[idorder] clusters={clusters} nodes/cluster={nodesPerCluster} fanout={fanOut} intra={intraClusterPercent}% nodes={nodeCount:N0} triples~{clustered.Triples:N0}\n"
            + $"[idorder]   {"labeling",-10} {"L2 FoR b/val",13} {"L2 PEF b/val",13} {"index FoR b/trip",17} {"index EF b/trip",16}");
        Console.WriteLine($"[idorder]   {"clustered",-10} {clustered.Level2Frame,13:F2} {clustered.Level2Partitioned,13:F2} {clustered.IndexFrame,17:F2} {clustered.IndexEliasFano,16:F2}");
        Console.WriteLine($"[idorder]   {"scattered",-10} {scattered.Level2Frame,13:F2} {scattered.Level2Partitioned,13:F2} {scattered.IndexFrame,17:F2} {scattered.IndexEliasFano,16:F2}");
    }

    /// <summary>The footprint figures for one labeling.</summary>
    /// <param name="Level2Frame">Level-2 value column, frame of reference, bits/value.</param>
    /// <param name="Level2Partitioned">Level-2 value column, partitioned Elias-Fano, bits/value.</param>
    /// <param name="IndexFrame">Whole index, frame of reference, bits/triple.</param>
    /// <param name="IndexEliasFano">Whole index, Elias-Fano on monotone value columns, bits/triple.</param>
    /// <param name="Triples">The distinct triple count.</param>
    private readonly record struct Measurement(double Level2Frame, double Level2Partitioned, double IndexFrame, double IndexEliasFano, long Triples);

    /// <summary>Builds the index both ways under one labeling and computes the footprint figures.</summary>
    /// <param name="edges">The graph edges, by clustered node index.</param>
    /// <param name="label">The identifier assignment.</param>
    /// <returns>The measurement.</returns>
    private static Measurement Measure((int Source, int Target)[] edges, NodeLabelDelegate label)
    {
        List<EncodedTriple> corpus = new(edges.Length);
        foreach((int source, int target) in edges)
        {
            corpus.Add(EncodedTriple.FromEncoded(label(source), Predicate, label(target)));
        }

        ColumnarTripleIndex frame = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations);
        ColumnarTripleIndex eliasFano = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations, ColumnarValueColumnEncoding.EliasFanoWhenMonotone);

        ColumnarOrder order = frame.OrderAt(0);
        BlockPackedColumn level2 = order.ValuesColumnAt(2);
        long triples = level2.Length;
        double level2Frame = level2.PackedByteCount * 8.0 / Math.Max(level2.Length, 1);

        uint[] values = DecodeColumn(level2);
        uint[] offsets = DecodeColumn(order.OffsetsColumnAt(1));
        int[] boundaries = new int[offsets.Length];
        for(int i = 0; i < offsets.Length; i++)
        {
            boundaries[i] = (int)offsets[i];
        }

        PartitionedEliasFanoSequence partitioned = PartitionedEliasFanoSequence.Build(values, boundaries);
        double level2Partitioned = (double)partitioned.BitCount / Math.Max(values.Length, 1);

        double indexFrame = IndexBytes(frame) * 8.0 / triples;
        double indexEliasFano = IndexBytes(eliasFano) * 8.0 / triples;

        return new Measurement(level2Frame, level2Partitioned, indexFrame, indexEliasFano, triples);
    }

    /// <summary>Sums the packed byte footprint across an index's materialised orders.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The packed bytes.</returns>
    private static long IndexBytes(ColumnarTripleIndex index)
    {
        long bytes = 0;
        for(int permutation = 0; permutation < 6; permutation++)
        {
            if(index.IsPermutationAvailable(permutation))
            {
                bytes += index.OrderAt(permutation).PackedByteCount;
            }
        }

        return bytes;
    }

    /// <summary>Decodes a whole block-packed column back to its values.</summary>
    /// <param name="column">The column to decode.</param>
    /// <returns>The column's values in column order.</returns>
    private static uint[] DecodeColumn(BlockPackedColumn column)
    {
        uint[] values = new uint[column.Length];
        for(int block = 0; block < column.BlockCount; block++)
        {
            int start = block << BlockPackedColumn.BlockShift;
            column.DecodeBlock(block, values.AsSpan(start, column.BlockLengthOf(block)));
        }

        return values;
    }

    /// <summary>
    /// Builds a community-structured graph: each node emits <paramref name="fanOut"/>
    /// edges, <paramref name="intraClusterPercent"/>% of them to a random node in
    /// its own community and the rest to a random node anywhere.
    /// </summary>
    /// <param name="clusters">The community count.</param>
    /// <param name="nodesPerCluster">The nodes per community.</param>
    /// <param name="fanOut">The out-edges per node.</param>
    /// <param name="intraClusterPercent">The intra-community edge percentage.</param>
    /// <returns>The edges, addressed by clustered node index.</returns>
    private static (int Source, int Target)[] BuildEdges(int clusters, int nodesPerCluster, int fanOut, int intraClusterPercent)
    {
        int nodeCount = clusters * nodesPerCluster;
        (int Source, int Target)[] edges = new (int, int)[(long)nodeCount * fanOut <= int.MaxValue ? nodeCount * fanOut : 0];
        ulong state = 0x1234_5678_9ABC_DEF0UL;
        int index = 0;
        for(int node = 0; node < nodeCount; node++)
        {
            int cluster = node / nodesPerCluster;
            int clusterStart = cluster * nodesPerCluster;
            for(int k = 0; k < fanOut; k++)
            {
                state = Mix(state);
                bool intra = (int)(state % 100) < intraClusterPercent;
                state = Mix(state);
                int target = intra
                    ? clusterStart + (int)(state % (ulong)nodesPerCluster)
                    : (int)(state % (ulong)nodeCount);
                edges[index++] = (node, target);
            }
        }

        return edges;
    }

    /// <summary>A deterministic permutation of <c>[0, count)</c> — the scattered identifier assignment.</summary>
    /// <param name="count">The node count.</param>
    /// <returns>The permutation; entry <c>i</c> is the scattered id of clustered node <c>i</c>.</returns>
    private static uint[] BuildScatterPermutation(int count)
    {
        uint[] permutation = new uint[count];
        for(int i = 0; i < count; i++)
        {
            permutation[i] = (uint)i;
        }

        ulong state = 0xDEAD_BEEF_CAFE_F00DUL;
        for(int i = count - 1; i > 0; i--)
        {
            state = Mix(state);
            int j = (int)(state % (ulong)(i + 1));
            (permutation[i], permutation[j]) = (permutation[j], permutation[i]);
        }

        return permutation;
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }
}
