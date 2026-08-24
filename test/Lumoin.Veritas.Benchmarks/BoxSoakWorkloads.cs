using System;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The shared workload substrate of the box-index stands: deterministic
/// synthetic shapes, probe builders for the intersection-mix and
/// containment-shaped protocols, the median reading, and the shared-pool
/// trims between combinations. Both the packing matrix
/// (<c>--profile-box-index-matrix</c>) and the containment head-to-head
/// (<c>--profile-box-containment</c>) consume it, so the two stands measure
/// identical datasets.
/// </summary>
internal static class BoxSoakWorkloads
{
    /// <summary>Deterministic synthetic shapes over an xorshift generator seeded by the golden ratio — identical datasets on every machine and round.</summary>
    /// <param name="shape">The shape name.</param>
    /// <param name="itemCount">The item count.</param>
    /// <returns>The generated items.</returns>
    public static BoundingBox[] BuildShape(string shape, long itemCount)
    {
        var random = new XorShiftGenerator(0x9E3779B97F4A7C15UL);
        var items = new BoundingBox[itemCount];

        for(long item = 0; item < itemCount; item++)
        {
            switch(shape)
            {
                case "uniform":
                {
                    double x = random.NextDouble() * 10_000d;
                    double y = random.NextDouble() * 10_000d;
                    items[item] = new BoundingBox(x, y, x + (random.NextDouble() * 20d), y + (random.NextDouble() * 20d));
                    break;
                }

                case "clustered":
                {
                    double clusterX = Math.Floor(random.NextDouble() * 30d) * 333d;
                    double clusterY = Math.Floor(random.NextDouble() * 30d) * 333d;
                    double x = clusterX + (random.NextDouble() * 60d);
                    double y = clusterY + (random.NextDouble() * 60d);
                    items[item] = new BoundingBox(x, y, x + (random.NextDouble() * 10d), y + (random.NextDouble() * 10d));
                    break;
                }

                case "archipelago":
                {
                    if(item == 0)
                    {
                        items[item] = new BoundingBox(0, 0, 10_000, 10_000);
                    }
                    else
                    {
                        double x = random.NextDouble() * 9_990d;
                        double y = random.NextDouble() * 9_990d;
                        items[item] = new BoundingBox(x, y, x + 8d, y + 8d);
                    }

                    break;
                }

                case "nested":
                {
                    //Sixteen-deep nested chains: each chain shrinks by 0.8 per level with the
                    //child placed at a random interior offset. Containment answers come from
                    //chain prefixes AND cross-chain overlap (at large counts the 400-unit
                    //roots tile the field many times over) - the answer-rich regime. Probe
                    //reach: the point and 0.1%-extent tiers fit every level; the 1% tier
                    //(about 100 units) is enclosed only by the outer six chain levels.
                    long positionInChain = item % 16;

                    if(positionInChain == 0)
                    {
                        double x = random.NextDouble() * 9_600d;
                        double y = random.NextDouble() * 9_600d;
                        items[item] = new BoundingBox(x, y, x + 400d, y + 400d);
                    }
                    else
                    {
                        BoundingBox parent = items[item - 1];
                        double width = (parent.MaxX - parent.MinX) * 0.8d;
                        double height = (parent.MaxY - parent.MinY) * 0.8d;
                        double x = parent.MinX + (random.NextDouble() * ((parent.MaxX - parent.MinX) - width));
                        double y = parent.MinY + (random.NextDouble() * ((parent.MaxY - parent.MinY) - height));
                        items[item] = new BoundingBox(x, y, x + width, y + height);
                    }

                    break;
                }

                case "blanket":
                {
                    //One in a hundred items is a field-scale blanket; the rest are small. The
                    //blankets scatter across leaves and inflate node bounds everywhere, so most
                    //subtrees survive a containment descent while few items answer - the
                    //visit-heavy adversarial regime for a bounds-pruned traversal.
                    if(item % 100 == 0)
                    {
                        double x = random.NextDouble() * 5_000d;
                        double y = random.NextDouble() * 5_000d;
                        items[item] = new BoundingBox(x, y, x + 5_000d, y + 5_000d);
                    }
                    else
                    {
                        double x = random.NextDouble() * 9_990d;
                        double y = random.NextDouble() * 9_990d;
                        items[item] = new BoundingBox(x, y, x + (random.NextDouble() * 10d), y + (random.NextDouble() * 10d));
                    }

                    break;
                }

                default:
                {
                    double x = random.NextDouble() * 10_000d;
                    double y = random.NextDouble() * 10_000d;
                    items[item] = new BoundingBox(x, y, x, y);
                    break;
                }
            }
        }

        return items;
    }

    /// <summary>The intersection-mix probe set: point probes plus two region selectivities (roughly a thousandth and a fiftieth of the field), cycled deterministically.</summary>
    /// <param name="items">The items the field size is read from.</param>
    /// <param name="probeCount">The probe count.</param>
    /// <returns>The generated probes.</returns>
    public static BoundingBox[] BuildProbes(BoundingBox[] items, int probeCount)
    {
        var random = new XorShiftGenerator(0xC2B2AE3D27D4EB4FUL);
        var probes = new BoundingBox[probeCount];
        double fieldSize = FieldSize(items);

        for(int probe = 0; probe < probeCount; probe++)
        {
            double x = random.NextDouble() * fieldSize;
            double y = random.NextDouble() * fieldSize;
            double extent = (probe % 3) switch
            {
                0 => 0d,
                1 => fieldSize * 0.03d,
                _ => fieldSize * 0.14d
            };

            probes[probe] = new BoundingBox(x, y, x + extent, y + extent);
        }

        return probes;
    }

    /// <summary>
    /// The containment-shaped probe set: point probes plus two SMALL region
    /// selectivities (a thousandth and a hundredth of the field), cycled
    /// deterministically — containers of a large probe are degenerate, so the
    /// intersection mix's mid and large extents do not transfer to the
    /// containment stand.
    /// </summary>
    /// <param name="items">The items the field size is read from.</param>
    /// <param name="probeCount">The probe count.</param>
    /// <returns>The generated probes.</returns>
    public static BoundingBox[] BuildContainmentProbes(BoundingBox[] items, int probeCount)
    {
        var random = new XorShiftGenerator(0xC2B2AE3D27D4EB4FUL);
        var probes = new BoundingBox[probeCount];
        double fieldSize = FieldSize(items);

        for(int probe = 0; probe < probeCount; probe++)
        {
            double x = random.NextDouble() * fieldSize;
            double y = random.NextDouble() * fieldSize;
            double extent = (probe % 3) switch
            {
                0 => 0d,
                1 => fieldSize * 0.001d,
                _ => fieldSize * 0.01d
            };

            probes[probe] = new BoundingBox(x, y, x + extent, y + extent);
        }

        return probes;
    }

    /// <summary>The median of an ascending sample.</summary>
    /// <param name="sorted">The ascending sample.</param>
    /// <returns>The median.</returns>
    public static double Median(double[] sorted)
    {
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2d;
    }

    /// <summary>
    /// Trims every publicly reachable pool the box structures rent from, so a
    /// combination never inherits the previous one's slabs. The containment
    /// tree's private build-stack element pool is unreachable from here; its
    /// column holds a logarithmic handful of entries, so the residue is
    /// negligible by construction.
    /// </summary>
    public static void TrimSharedPools()
    {
        _ = VeritasMemoryPool<int>.Shared.TrimExcess();
        _ = VeritasMemoryPool<double>.Shared.TrimExcess();
        _ = VeritasMemoryPool<long>.Shared.TrimExcess();
        _ = VeritasMemoryPool<byte>.Shared.TrimExcess();
        _ = VeritasMemoryPool<HilbertBoxKey>.Shared.TrimExcess();
        _ = VeritasMemoryPool<StrBoxKey>.Shared.TrimExcess();
        _ = VeritasMemoryPool<PackedNodeRecord>.Shared.TrimExcess();
        _ = VeritasMemoryPool<DominanceBuildWorkItem>.Shared.TrimExcess();
    }

    /// <summary>The field size the probe builders scale extents by: the maximum x extent of the items, floored at one.</summary>
    /// <param name="items">The items the field size is read from.</param>
    /// <returns>The field size.</returns>
    private static double FieldSize(BoundingBox[] items)
    {
        double fieldSize = 10_000d;

        if(items.Length > 0)
        {
            double maxX = double.NegativeInfinity;

            foreach(BoundingBox item in items)
            {
                maxX = Math.Max(maxX, item.MaxX);
            }

            fieldSize = Math.Max(1d, maxX);
        }

        return fieldSize;
    }

    /// <summary>A 64-bit xorshift-star generator: deterministic across runtimes and machines, which the digest gates and cross-round dataset identity require.</summary>
    /// <param name="seed">The generator seed.</param>
    public struct XorShiftGenerator(ulong seed)
    {
        /// <summary>The generator state.</summary>
        private ulong state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;

        /// <summary>The next value in [0, 1): the top 53 bits of the scrambled state over two to the fifty-third.</summary>
        /// <returns>The drawn value.</returns>
        public double NextDouble()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;

            ulong scrambled = state * 0x2545F4914F6CDD1DUL;

            return (scrambled >> 11) * (1.0 / 9_007_199_254_740_992d);
        }
    }
}
