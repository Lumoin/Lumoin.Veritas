using System.Collections.Generic;

namespace Lumoin.Veritas.Workbench;

/// <summary>
/// Aggregate counts produced by <see cref="EdgeMapDistribution.Survey"/>.
/// Carries per-tier totals and a histogram of SortedArray-tier
/// entry counts bucketed by power-of-two ranges.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bucket keys.</b> The histogram's keys are the inclusive
/// lower bound of each bucket (9, 16, 32, 64, 128, 256, 512,
/// 1024). The Inline tier (1..8 entries) is captured by
/// <see cref="InlineCount"/>; the SortedArray buckets cover
/// everything from 9 entries upward.
/// </para>
/// </remarks>
internal readonly record struct EdgeMapDistributionResult
{
    /// <summary>The total number of <c>EdgeMap</c> instances surveyed across every reachable node.</summary>
    public int TotalEdgeMaps { get; init; }

    /// <summary>The number of edge maps in the <c>Empty</c> tier.</summary>
    public int EmptyCount { get; init; }

    /// <summary>The number of edge maps in the <c>Inline</c> tier (1..8 entries).</summary>
    public int InlineCount { get; init; }

    /// <summary>The number of edge maps in the <c>SortedArray</c> tier (9+ entries).</summary>
    public int SortedArrayCount { get; init; }

    /// <summary>Histogram of SortedArray-tier entry counts bucketed by power-of-two ranges. Keys are the inclusive lower bound of each bucket.</summary>
    public IReadOnlyDictionary<int, int> SortedArrayCountHistogram { get; init; }

    /// <summary>Histogram of Inline-tier entry counts bucketed to mirror the pre-Batch-4 survey shape (1, 2, 3-4, 5-8). Keys are the inclusive lower bound of each bucket.</summary>
    public IReadOnlyDictionary<int, int> InlineCountHistogram { get; init; }

    /// <summary>Per-depth tier counts. Outer key is the node depth (the trie root has the highest depth, leaves depth 1); inner array indices map: 0 = Empty, 1 = Inline, 2 = SortedArray.</summary>
    public IReadOnlyDictionary<int, int[]> PerDepthTierCounts { get; init; }

    /// <summary>The number of distinct <c>HypertrieNode</c> instances visited during the walk.</summary>
    public int DistinctNodeCount { get; init; }

    /// <summary>The number of SEN-encoded depth-1 leaves referenced from the visited nodes. SEN leaves carry their single key inline in the parent slot and have no separate <c>HypertrieNode</c> instance, so they are not part of <see cref="DistinctNodeCount"/>.</summary>
    public int SingleEntryNodeCount { get; init; }
}
