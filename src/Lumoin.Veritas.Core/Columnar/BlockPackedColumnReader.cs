using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A single-threaded cursor over a <see cref="BlockPackedColumn"/>:
/// owns a one-block decode scratch and the last-decoded-block cache
/// slot, so the descent locality of trie iteration turns most
/// touches into two array reads. One reader per (column, iterator)
/// pair; readers share nothing. The algorithms live on
/// <see cref="BlockPackedColumn"/> — the reader supplies the state.
/// </summary>
[DebuggerDisplay("BlockPackedColumnReader CachedBlock={cachedBlock}")]
public sealed class BlockPackedColumnReader
{
    private readonly BlockPackedColumn column;

    private readonly uint[] scratch;

    //Naked field: passed by ref to the column's scratch-parameterised
    //access methods.
    private int cachedBlock = -1;

    /// <summary>The number of values in the underlying column.</summary>
    public int Length => column.Length;

    /// <summary>Constructs a reader over <paramref name="column"/> with its own scratch buffer.</summary>
    /// <param name="column">The column to read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <c>null</c>.</exception>
    public BlockPackedColumnReader(BlockPackedColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        this.column = column;
        scratch = new uint[BlockPackedColumn.BlockLength];
    }

    /// <summary>Reads the value at <paramref name="index"/>, decoding its block on a cache miss.</summary>
    /// <param name="index">The column index.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int index)
    {
        return column.ValueAt(index, scratch, ref cachedBlock);
    }

    /// <summary>
    /// Returns the smallest index in <c>[lo, hi)</c> whose value is
    /// greater than or equal to <paramref name="target"/>, or
    /// <paramref name="hi"/> when no such index exists. The range
    /// must be strictly ascending — see
    /// <see cref="BlockPackedColumn.LowerBound"/>.
    /// </summary>
    /// <param name="lo">The range's inclusive start.</param>
    /// <param name="hi">The range's exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    public int LowerBound(int lo, int hi, uint target)
    {
        return column.LowerBound(lo, hi, target, scratch, ref cachedBlock);
    }
}
