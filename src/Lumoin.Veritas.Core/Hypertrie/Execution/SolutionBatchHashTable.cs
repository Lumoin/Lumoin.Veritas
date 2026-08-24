using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The build side of a batched hash join, materialised and indexed
/// by packed join key: an allocation-lean CHAINED hash table. The
/// build rows' columns are stored contiguously; one dictionary
/// entry per distinct key holds the most-recently-inserted row
/// carrying it, and a parallel chain links every earlier row with
/// the same key. Matching a probe key is one dictionary lookup
/// then a pointer walk — no per-key collection is ever allocated.
/// </summary>
/// <remarks>
/// <para>
/// Keys pack one or two join values into a <see cref="ulong"/>
/// (<see cref="PackKey"/>) — the case the batched join supports;
/// wider join-variable sets route to leapfrog instead of growing a
/// general tuple key here.
/// </para>
/// <para>
/// The table is built once and then read-only: <see cref="Build"/>
/// drains the build batches, and <see cref="FirstMatch"/> /
/// <see cref="NextMatch"/> / <see cref="ValueAt"/> serve the probe
/// side. Single-threaded by construction, like the batch streams
/// it indexes.
/// </para>
/// </remarks>
[DebuggerDisplay("SolutionBatchHashTable Rows={RowCount} Columns={ColumnCount} Keys={head.Count}")]
public sealed class SolutionBatchHashTable
{
    private const int NoMatch = -1;

    private readonly List<uint>[] columns;

    private readonly Dictionary<JoinKey, int> head;

    private readonly List<int> next;

    /// <summary>The number of build columns, positional against the build schema.</summary>
    public int ColumnCount => columns.Length;

    /// <summary>The number of materialised build rows.</summary>
    public int RowCount => next.Count;

    private SolutionBatchHashTable(List<uint>[] columns, Dictionary<JoinKey, int> head, List<int> next)
    {
        this.columns = columns;
        this.head = head;
        this.next = next;
    }

    /// <summary>
    /// Builds the table by draining <paramref name="buildBatches"/>:
    /// every row's columns append contiguously and its packed key
    /// joins the chain.
    /// </summary>
    /// <param name="buildBatches">The build side's batch stream; consumed eagerly.</param>
    /// <param name="columnCount">The build schema's column count.</param>
    /// <param name="keyColumn0">The first key column's index in the build schema.</param>
    /// <param name="keyColumn1">The second key column's index, or −1 for a single-variable key.</param>
    /// <returns>The built table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="buildBatches"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columnCount"/> is negative, or a key column is out of range.</exception>
    public static SolutionBatchHashTable Build(
        IEnumerable<SolutionBatch> buildBatches,
        int columnCount,
        int keyColumn0,
        int keyColumn1)
    {
        ArgumentNullException.ThrowIfNull(buildBatches);
        ArgumentOutOfRangeException.ThrowIfNegative(columnCount);

        List<uint>[] columns = new List<uint>[columnCount];
        for(int i = 0; i < columnCount; i++)
        {
            columns[i] = [];
        }

        Dictionary<JoinKey, int> head = [];
        List<int> next = [];

        foreach(SolutionBatch batch in buildBatches)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(keyColumn0);
            ReadOnlySpan<uint> key1Column = keyColumn1 >= 0 ? batch.ColumnOf(keyColumn1) : default;

            for(int row = 0; row < count; row++)
            {
                int rowId = next.Count;
                for(int column = 0; column < columnCount; column++)
                {
                    columns[column].Add(batch.ColumnOf(column)[row]);
                }

                JoinKey key = JoinKey.Pack(key0Column[row], keyColumn1 >= 0 ? key1Column[row] : 0);
                next.Add(head.TryGetValue(key, out int previous) ? previous : NoMatch);
                head[key] = rowId;
            }
        }

        return new SolutionBatchHashTable(columns, head, next);
    }

    /// <summary>Returns the first matching build row for a packed key, or −1 when none match.</summary>
    /// <param name="key">The packed probe key.</param>
    /// <returns>The first matching build row id, or −1.</returns>
    public int FirstMatch(JoinKey key)
    {
        return head.TryGetValue(key, out int rowId) ? rowId : NoMatch;
    }

    /// <summary>Returns the next build row sharing the current row's key, or −1 at the chain's end.</summary>
    /// <param name="rowId">A build row id from <see cref="FirstMatch"/> or a prior <see cref="NextMatch"/>.</param>
    /// <returns>The next matching build row id, or −1.</returns>
    public int NextMatch(int rowId)
    {
        return next[rowId];
    }

    /// <summary>Reads a materialised build value.</summary>
    /// <param name="column">The build column index.</param>
    /// <param name="rowId">The build row id.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int column, int rowId)
    {
        return columns[column][rowId];
    }
}
