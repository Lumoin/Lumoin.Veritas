using System;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// One operand's local contribution at a node: which operand, which part, and
/// which segment within the part carries the node. The fan derives rays from
/// the segment's original endpoints.
/// </summary>
/// <param name="IsFirst">True for the first operand's section.</param>
/// <param name="PartIndex">The part carrying the segment.</param>
/// <param name="SegmentIndex">The segment within the part: vertices <c>Start + SegmentIndex</c> to <c>Start + SegmentIndex + 1</c>.</param>
internal readonly record struct NodeSection(bool IsFirst, int PartIndex, int SegmentIndex);

/// <summary>
/// The intersection-matrix accumulator: nine cells under the raise-only
/// discipline — a cell moves only upward, never regressing from a higher
/// intersection dimension to a lower one, which is what makes incremental
/// evidence from seeds, probes, and fans composable in any order.
/// </summary>
internal sealed class RelateTopology
{
    /// <summary>The nine cells, row-major, initialized to the empty dimension.</summary>
    private int[] Cells { get; } = [-1, -1, -1, -1, -1, -1, -1, -1, -1];

    /// <summary>
    /// Raises the cell for the first operand's <paramref name="first"/> part
    /// against the second operand's <paramref name="second"/> part to at
    /// least <paramref name="dimension"/>.
    /// </summary>
    public void Raise(PointPlacement first, PointPlacement second, int dimension)
    {
        int index = ((int)first * 3) + (int)second;

        if(dimension > Cells[index])
        {
            Cells[index] = dimension;
        }
    }

    /// <summary>The accumulated matrix.</summary>
    public IntersectionMatrix ToMatrix()
    {
        return new IntersectionMatrix(
            Cells[0], Cells[1], Cells[2],
            Cells[3], Cells[4], Cells[5],
            Cells[6], Cells[7], Cells[8]);
    }
}
