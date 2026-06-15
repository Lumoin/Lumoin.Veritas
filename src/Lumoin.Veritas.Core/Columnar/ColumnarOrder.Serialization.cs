namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Warm-start reassembly of a <see cref="ColumnarOrder"/> from its five already-reloaded
/// columns. The persistence container reads each column back through the per-column codec
/// (<see cref="BlockPackedColumn.ReadFrom"/>) and hands the five here in canonical slot order;
/// nothing is re-sorted or re-packed at the order level — every length is recovered from the
/// columns themselves.
/// </summary>
public sealed partial class ColumnarOrder
{
    /// <summary>Reassembles an order from its five reloaded columns, in the canonical slot order the build pipeline and private constructor use.</summary>
    /// <param name="level0Values">The level-0 value column.</param>
    /// <param name="level0Offsets">The level-0 to level-1 exclusive-end offset column.</param>
    /// <param name="level1Values">The level-1 value column.</param>
    /// <param name="level1Offsets">The level-1 to level-2 exclusive-end offset column.</param>
    /// <param name="level2Values">The level-2 value column.</param>
    /// <returns>The reassembled order.</returns>
    internal static ColumnarOrder FromColumns(
        BlockPackedColumn level0Values,
        BlockPackedColumn level0Offsets,
        BlockPackedColumn level1Values,
        BlockPackedColumn level1Offsets,
        BlockPackedColumn level2Values)
    {
        return new ColumnarOrder(level0Values, level0Offsets, level1Values, level1Offsets, level2Values);
    }
}
