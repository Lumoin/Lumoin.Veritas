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
}
