using System;
using System.Numerics;

namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// The slot-parade arithmetic of one <see cref="PackedBoxIndex"/> build, computed
/// over <see cref="long"/> counts so the near-<see cref="Array.MaxLength"/>
/// boundary is testable without building an index: the node total, the node
/// level count, the total slot parade (items + nodes), and the traversal-stack
/// bound a query rents.
/// </summary>
/// <param name="NodeCount">Node slots across all levels: Σ ceil(N / capacity^L) for L = 1…<paramref name="LevelCount"/>.</param>
/// <param name="LevelCount">Node levels — 0 for an empty build, 1 when the sole leaf node is the root.</param>
/// <param name="TotalSlots">The slot parade: item slots plus node slots. Every per-slot column must keep this addressable, so it is the build's binding ceiling — not the item count.</param>
/// <param name="TraversalStackBound">The per-query stack rental size: min((capacity − 1) · (LevelCount − 1) + 1, NodeCount) — the preorder pending-sibling bound, exact because only node slots enter the stack (leaf item runs scan in place), floored by the fact a node is visited at most once.</param>
internal readonly record struct PackedBoxIndexLayout(long NodeCount, int LevelCount, long TotalSlots, long TraversalStackBound);

/// <summary>
/// One sortable ordering element of a Hilbert packing pass: the Hilbert
/// distance of an entry's center and the entry's tie-break index — the
/// registration index on the leaf pass, the creation-order slot index on the
/// upper-level passes. Ordering compares distance first, index second, so
/// every element is unique and the sorted sequence never depends on the sort
/// algorithm's tie behaviour.
/// </summary>
/// <param name="Distance">The Hilbert distance of the entry's center cell.</param>
/// <param name="Index">The entry's unique tie-break index.</param>
internal readonly record struct HilbertBoxKey(ulong Distance, int Index): IComparable<HilbertBoxKey>
{
    /// <summary>Distance first; the unique tie-break index closes exact-key collisions (duplicates, shared grid cells).</summary>
    /// <param name="other">The element to compare against.</param>
    /// <returns>A negative value, zero, or a positive value as this element sorts before, with, or after <paramref name="other"/>.</returns>
    public int CompareTo(HilbertBoxKey other)
    {
        int byDistance = Distance.CompareTo(other.Distance);

        return byDistance != 0 ? byDistance : Index.CompareTo(other.Index);
    }
}

/// <summary>
/// One sortable ordering element of a Sort-Tile-Recursive packing pass: an
/// entry's center ordinate on the pass's axis and the entry's tie-break index
/// (registration on the leaf pass, creation-order slot on upper passes). The
/// element is re-keyed between the center-X and the within-slice center-Y
/// passes. Comparison uses <see cref="double.CompareTo(double)"/>, whose one
/// departure from the raw operators is giving NaN a total position; signed
/// zeros compare equal, so they — like every equal-key pair — close through
/// the tie-break index, deterministically.
/// </summary>
/// <param name="Center">The entry's center ordinate on the pass's axis.</param>
/// <param name="Index">The entry's unique tie-break index.</param>
internal readonly record struct StrBoxKey(double Center, int Index): IComparable<StrBoxKey>
{
    /// <summary>Center first via <see cref="double.CompareTo(double)"/>; the unique tie-break index closes equal centers, signed-zero pairs included.</summary>
    /// <param name="other">The element to compare against.</param>
    /// <returns>A negative value, zero, or a positive value as this element sorts before, with, or after <paramref name="other"/>.</returns>
    public int CompareTo(StrBoxKey other)
    {
        int byCenter = Center.CompareTo(other.Center);

        return byCenter != 0 ? byCenter : Index.CompareTo(other.Index);
    }
}

/// <summary>
/// One node record of the level under construction, formed in creation order
/// in the build's transient scratch column before the level's ordering pass
/// sorts it into parade order: the union bounds, the first-child parade slot,
/// and the child count.
/// </summary>
/// <param name="MinX">The union's minimum x extent.</param>
/// <param name="MinY">The union's minimum y extent.</param>
/// <param name="MaxX">The union's maximum x extent.</param>
/// <param name="MaxY">The union's maximum y extent.</param>
/// <param name="ChildStart">The first child's parade slot.</param>
/// <param name="ChildCount">The number of children in the contiguous run.</param>
internal readonly record struct PackedNodeRecord(double MinX, double MinY, double MaxX, double MaxY, int ChildStart, int ChildCount);

/// <summary>
/// One pending subtree of the iterative dominance-tree build: the range of the
/// dominance order it owns, its depth (the split axis is depth modulo four),
/// and which parent link to fill once the node is created — the write a
/// recursive build would have made through its return value.
/// </summary>
/// <param name="ItemStart">The first position of the dominance-order range this subtree owns.</param>
/// <param name="ItemCount">The number of positions in the range.</param>
/// <param name="Depth">The subtree's depth from the root; the split axis is the depth modulo four.</param>
/// <param name="ParentNode">The parent node whose child link this subtree fills once created; −1 for the root.</param>
/// <param name="IsLeftChild">Whether this subtree fills the parent's left link.</param>
internal readonly record struct DominanceBuildWorkItem(int ItemStart, int ItemCount, int Depth, int ParentNode, bool IsLeftChild);

/// <summary>
/// The pure computations of <see cref="PackedBoxIndex"/>'s build, factored as
/// internal statics so each is unit-testable directly — the layout arithmetic
/// against its ceilings, the half-form center, the totality of the grid
/// normalization, and the Hilbert curve, whose tests oracle it against an
/// independently written inverse and a pinned vector rather than against this
/// code's own output.
/// </summary>
internal static class PackedBoxIndexPrimitives
{
    /// <summary>
    /// Computes the level, node, parade, and stack arithmetic for a build of
    /// <paramref name="itemCount"/> items at <paramref name="nodeCapacity"/>.
    /// All internal arithmetic is <see cref="long"/>, so callers can probe the
    /// <see cref="Array.MaxLength"/> parade boundary directly.
    /// </summary>
    /// <param name="itemCount">The item count of the build; a non-positive count answers the empty layout.</param>
    /// <param name="nodeCapacity">The children-per-node capacity.</param>
    /// <returns>The layout of the build.</returns>
    internal static PackedBoxIndexLayout ComputeLayout(long itemCount, int nodeCapacity)
    {
        if(itemCount <= 0L)
        {
            return new PackedBoxIndexLayout(0L, 0, 0L, 0L);
        }

        long nodeCount = 0L;
        int levelCount = 0;
        long currentLevelCount = itemCount;

        do
        {
            currentLevelCount = (currentLevelCount + nodeCapacity - 1L) / nodeCapacity;
            nodeCount += currentLevelCount;
            levelCount++;
        }
        while(currentLevelCount > 1L);

        long formulaBound = ((long)(nodeCapacity - 1) * (levelCount - 1)) + 1L;

        return new PackedBoxIndexLayout(
            nodeCount,
            levelCount,
            itemCount + nodeCount,
            Math.Min(formulaBound, nodeCount));
    }

    /// <summary>
    /// The dominance tree's node-count bound for a build of
    /// <paramref name="itemCount"/> items: 2 · ⌈N / 4⌉ − 1. A median split of
    /// any range above the eight-item leaf ceiling produces two parts of at
    /// least four items each, so every leaf holds at least four items (the
    /// sole leaf of a small build being the one exception, and one leaf is
    /// within the bound), the leaf count is at most ⌈N / 4⌉, and a full binary
    /// tree over that many leaves has at most twice-minus-one nodes. The
    /// dominance node columns are sized by this formula; its tightness is
    /// asserted by test, not assumed.
    /// </summary>
    /// <param name="itemCount">The item count of the build; a non-positive count answers zero.</param>
    /// <returns>The dominance node-count bound.</returns>
    internal static long ComputeDominanceNodeBound(long itemCount)
    {
        if(itemCount <= 0L)
        {
            return 0L;
        }

        long leafBound = (itemCount + 3L) / 4L;

        return (2L * leafBound) - 1L;
    }

    /// <summary>
    /// The dominance descent's stack bound for a build of
    /// <paramref name="itemCount"/> items: ⌈log₂(max(1, N / 4))⌉ + 2. The
    /// median split halves the range each level down to eight-item leaves, so
    /// the depth is at most ⌈log₂(N / 8)⌉ and a descent holds at most one
    /// pending sibling per level plus the node in hand; the N / 4 form with
    /// the constant covers that exactly with one spare slot. The same bound
    /// sizes the build's own work stack — it pushes two children and pops one
    /// per split, peaking at the identical one-pending-sibling-per-level
    /// shape.
    /// </summary>
    /// <param name="itemCount">The item count of the build; a non-positive count answers zero.</param>
    /// <returns>The dominance traversal-stack bound.</returns>
    internal static long ComputeDominanceTraversalStackBound(long itemCount)
    {
        if(itemCount <= 0L)
        {
            return 0L;
        }

        long quarters = Math.Max(1L, itemCount / 4L);
        long ceilingLog = quarters == 1L ? 0L : BitOperations.Log2((ulong)(quarters - 1L)) + 1L;

        return ceilingLog + 2L;
    }

    /// <summary>
    /// A box's center ordinate in the overflow-immune half form
    /// <c>min / 2 + max / 2</c>: each half is at most half of
    /// <see cref="double.MaxValue"/>, where the naive <c>(min + max) / 2</c>
    /// overflows at the rim, and the result is the correctly rounded midpoint
    /// absent subnormal halves. This expression form is contract: an
    /// algebraic rewrite may round differently and silently change the
    /// packing order the determinism clause pins.
    /// </summary>
    /// <param name="minimum">The box's minimum ordinate on this axis.</param>
    /// <param name="maximum">The box's maximum ordinate on this axis.</param>
    /// <returns>The center ordinate.</returns>
    internal static double BoxCenter(double minimum, double maximum)
    {
        return (minimum / 2d) + (maximum / 2d);
    }

    /// <summary>
    /// Normalizes one center ordinate to the packing grid: the ratio of the
    /// half-form offset to the half-form extent, scaled to
    /// <paramref name="gridMaximum"/>. Total over every finite center pair:
    /// the halves keep both the offset and the extent finite where the direct
    /// subtraction overflows to infinity for rim-magnitude centers, and the
    /// ratio-before-scale order keeps the quotient in [0, 1] even for
    /// subnormal extents where scale-first overflows. A zero extent pins the
    /// coordinate to 0, and any non-finite intermediate pins to 0 as a stated
    /// belt — unreachable by the argument above, kept as defense.
    /// </summary>
    /// <param name="center">The entry's center ordinate on this axis.</param>
    /// <param name="minimumCenter">The smallest center ordinate on this axis across the entries being ordered.</param>
    /// <param name="extentHalf">The precomputed half-form extent: <c>maximumCenter / 2 − minimumCenter / 2</c>.</param>
    /// <param name="gridMaximum">The largest grid coordinate: 2^bits − 1.</param>
    /// <returns>The grid coordinate in [0, <paramref name="gridMaximum"/>].</returns>
    internal static uint GridCoordinate(double center, double minimumCenter, double extentHalf, uint gridMaximum)
    {
        if(extentHalf == 0d)
        {
            return 0u;
        }

        double offsetHalf = (center / 2d) - (minimumCenter / 2d);
        double scaled = (offsetHalf / extentHalf) * gridMaximum;

        if(!double.IsFinite(scaled))
        {
            return 0u;
        }

        return (uint)scaled;
    }

    /// <summary>
    /// The Hilbert distance of grid cell (<paramref name="x"/>, <paramref name="y"/>)
    /// on a <paramref name="gridSide"/> × <paramref name="gridSide"/> grid —
    /// the canonical cell-halving reflect/swap traversal. Distances reach 62
    /// bits at the 2³¹ grid.
    /// </summary>
    /// <remarks>
    /// Two properties are load-bearing and deliberate. The distance term
    /// widens through <c>(ulong)cell * cell</c> — at a 2³¹ grid the squared
    /// cell reaches 2⁶⁰ and an unwidened product wraps on most iterations.
    /// And the reflection <c>cursor = cell − 1 − cursor</c> wraps on
    /// <see cref="uint"/> when the cursor exceeds the cell: the wrap preserves
    /// the low bits (power-of-two cell, borrow-free) and the high bits are
    /// never read below that cell size — a clamped or widened "fix" produces
    /// a different, still-round-tripping curve that only a pinned vector
    /// would catch.
    /// </remarks>
    /// <param name="gridSide">The grid side length; a power of two.</param>
    /// <param name="x">The cell's x coordinate.</param>
    /// <param name="y">The cell's y coordinate.</param>
    /// <returns>The Hilbert distance of the cell.</returns>
    internal static ulong HilbertDistance(uint gridSide, uint x, uint y)
    {
        ulong distance = 0UL;
        uint cursorX = x;
        uint cursorY = y;

        for(uint cell = gridSide / 2u; cell > 0u; cell /= 2u)
        {
            uint quadrantX = (cursorX & cell) > 0u ? 1u : 0u;
            uint quadrantY = (cursorY & cell) > 0u ? 1u : 0u;

            distance += (ulong)cell * cell * ((3u * quadrantX) ^ quadrantY);

            if(quadrantY == 0u)
            {
                if(quadrantX != 0u)
                {
                    cursorX = cell - 1u - cursorX;
                    cursorY = cell - 1u - cursorY;
                }

                (cursorX, cursorY) = (cursorY, cursorX);
            }
        }

        return distance;
    }
}
