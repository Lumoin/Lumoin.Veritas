using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Workbench;

/// <summary>
/// Walks the reachable nodes of a built hypertrie and tallies the
/// <c>EdgeMap</c>-tier distribution. Used by the workbench
/// <c>--profile-edgemap-distribution</c> scenario to inform
/// future tuning of the Inline-tier capacity and SortedArray-tier
/// initial-rental size.
/// </summary>
/// <remarks>
/// <para>
/// The walk is iterative: a work stack of <see cref="NodeHandle"/>
/// values plus a <see cref="HashSet{T}"/> for visited-handle dedup.
/// Each visited node contributes one edge map per remaining
/// position (depth = 1 → 1 edge map, depth = 3 → 3 edge maps).
/// </para>
/// </remarks>
internal static class EdgeMapDistribution
{
    private static readonly int[] SortedArrayBucketLowerBounds =
    [
        9, 16, 32, 64, 128, 256, 512, 1024,
    ];

    private static readonly int[] InlineBucketLowerBounds =
    [
        1, 2, 3, 5,
    ];

    /// <summary>
    /// Surveys the EdgeMap distribution across every reachable
    /// node in <paramref name="store"/> from
    /// <paramref name="rootHandle"/>.
    /// </summary>
    /// <param name="store">The intern table holding the hypertrie nodes.</param>
    /// <param name="rootHandle">The root handle to start the walk from.</param>
    /// <returns>An <see cref="EdgeMapDistributionResult"/> with per-tier counts and the SortedArray histogram.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    public static EdgeMapDistributionResult Survey(NodeStore store, NodeHandle rootHandle)
    {
        ArgumentNullException.ThrowIfNull(store);

        int total = 0;
        int empty = 0;
        int inline = 0;
        int sortedArray = 0;

        Dictionary<int, int> sortedHistogram = [];
        foreach(int bucket in SortedArrayBucketLowerBounds)
        {
            sortedHistogram[bucket] = 0;
        }

        Dictionary<int, int> inlineHistogram = [];
        foreach(int bucket in InlineBucketLowerBounds)
        {
            inlineHistogram[bucket] = 0;
        }

        Dictionary<int, int[]> perDepth = [];

        if(rootHandle.IsNone)
        {
            return new EdgeMapDistributionResult
            {
                TotalEdgeMaps = 0,
                EmptyCount = 0,
                InlineCount = 0,
                SortedArrayCount = 0,
                SortedArrayCountHistogram = sortedHistogram,
                InlineCountHistogram = inlineHistogram,
                PerDepthTierCounts = perDepth,
                DistinctNodeCount = 0,
                SingleEntryNodeCount = 0,
            };
        }

        HashSet<NodeHandle> visited = [];
        Stack<NodeHandle> work = new();
        int senCount = 0;
        work.Push(rootHandle);

        while(work.Count > 0)
        {
            NodeHandle handle = work.Pop();

            //SEN handles carry their leaf inline and SEN2 handles
            //live in the pair arena; neither has a node arena entry
            //to walk. Count them separately and move on.
            if(handle.IsSingleEntry || handle.IsSingleEntryPair)
            {
                senCount++;

                continue;
            }

            if(handle.IsNone || !visited.Add(handle))
            {
                continue;
            }

            HypertrieNode node = store.GetByHandle(handle);
            if(!perDepth.TryGetValue(node.Depth, out int[]? depthCounts))
            {
                depthCounts = new int[3];
                perDepth[node.Depth] = depthCounts;
            }

            for(int position = 0; position < node.Depth; position++)
            {
                ref readonly EdgeMap map = ref node.EdgeMaps[position];
                total++;
                switch(map.Kind)
                {
                    case EdgeMapKind.Empty:
                    {
                        empty++;
                        depthCounts[0]++;

                        break;
                    }
                    case EdgeMapKind.Inline:
                    {
                        inline++;
                        depthCounts[1]++;
                        int inlineCount = EdgeMap.Count(in map);
                        int inlineBucket = BucketFor(inlineCount, InlineBucketLowerBounds);
                        inlineHistogram[inlineBucket]++;

                        break;
                    }
                    case EdgeMapKind.SortedArray:
                    {
                        sortedArray++;
                        depthCounts[2]++;
                        int sortedCount = EdgeMap.Count(in map);
                        int sortedBucket = BucketFor(sortedCount, SortedArrayBucketLowerBounds);
                        sortedHistogram[sortedBucket]++;

                        break;
                    }
                    default:
                    {
                        break;
                    }
                }

                //Push every non-None child onto the work stack. SEN
                //children are recognised at pop time and counted
                //without an arena lookup.
                foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(node.EdgeMaps[position]))
                {
                    if(!entry.Value.IsNone)
                    {
                        work.Push(entry.Value);
                    }
                }
            }
        }

        return new EdgeMapDistributionResult
        {
            TotalEdgeMaps = total,
            EmptyCount = empty,
            InlineCount = inline,
            SortedArrayCount = sortedArray,
            SortedArrayCountHistogram = sortedHistogram,
            InlineCountHistogram = inlineHistogram,
            PerDepthTierCounts = perDepth,
            DistinctNodeCount = visited.Count,
            SingleEntryNodeCount = senCount,
        };
    }

    //Returns the inclusive lower bound of the bucket containing
    //the given entry count from a sorted ascending list of bucket
    //lower bounds.
    private static int BucketFor(int count, int[] bucketLowerBounds)
    {
        int chosen = bucketLowerBounds[0];
        foreach(int lower in bucketLowerBounds)
        {
            if(count >= lower)
            {
                chosen = lower;
            }
            else
            {
                break;
            }
        }

        return chosen;
    }
}
