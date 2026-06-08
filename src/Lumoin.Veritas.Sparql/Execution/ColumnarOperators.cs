using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The relational operators that stay on the columnar island: pure transforms of a columnar
/// <see cref="SolutionTable"/> (encoded-id columns over a <see cref="SparqlVariable"/> schema) into another, never
/// decoding a term. The engine owns the columnar-vs-row dispatch (an operator whose input is row-backed bridges to
/// the row form); this type is the columnar branch it calls, kept AST-free so each operation is a directly
/// testable table → table function with the row form as its differential oracle.
/// </summary>
/// <remarks>
/// These are the operations whose columnar form is exact: projection (column select/alias), duplicate elimination
/// (encoded-id-tuple dedup), the <c>OFFSET</c>/<c>LIMIT</c> window, union (schema merge), and the shared-variable
/// equi-join. The hot path is concrete — no per-row or per-batch virtual dispatch.
/// </remarks>
internal static class ColumnarOperators
{
    /// <summary>Projects a columnar table onto the given variables in projection order, aliasing each present variable's column and substituting an all-unbound column for a variable the input does not carry. No term is decoded.</summary>
    /// <param name="input">The columnar input table.</param>
    /// <param name="variables">The projected variables, in projection order.</param>
    /// <returns>The projected columnar table.</returns>
    public static SolutionTable Project(SolutionTable input, IReadOnlyList<SparqlVariable> variables)
    {
        IReadOnlyList<SparqlVariable> inputSchema = input.Schema;
        Dictionary<SparqlVariable, int> columnOf = new(inputSchema.Count);
        for(int i = 0; i < inputSchema.Count; i++)
        {
            columnOf[inputSchema[i]] = i;
        }

        uint[][] projectedColumns = new uint[variables.Count][];
        uint[]? unboundColumn = null;
        for(int i = 0; i < variables.Count; i++)
        {
            if(columnOf.TryGetValue(variables[i], out int source))
            {
                projectedColumns[i] = input.ColumnArray(source);

                continue;
            }

            //A projected variable the input never bound is unbound in every row; one shared zero column serves them all.
            unboundColumn ??= new uint[input.Count];
            projectedColumns[i] = unboundColumn;
        }

        return SolutionTable.Columnar(variables, projectedColumns, input.Count, input.Dictionary);
    }

    /// <summary>Eliminates duplicate rows of a columnar table, comparing the encoded-id tuple across the schema columns, in first-appearance order, without decoding any term.</summary>
    /// <param name="input">The columnar input table.</param>
    /// <returns>The distinct columnar table.</returns>
    public static SolutionTable Distinct(SolutionTable input)
    {
        int columnCount = input.Schema.Count;
        uint[][] columns = ColumnArraysOf(input);

        List<int> keptRows = new(input.Count);
        HashSet<int> seen = new(input.Count, new RowComparer(columns, columnCount));
        for(int row = 0; row < input.Count; row++)
        {
            if(seen.Add(row))
            {
                keptRows.Add(row);
            }
        }

        if(keptRows.Count == input.Count)
        {
            return input;
        }

        uint[][] distinctColumns = new uint[columnCount][];
        for(int column = 0; column < columnCount; column++)
        {
            uint[] source = columns[column];
            uint[] target = new uint[keptRows.Count];
            for(int i = 0; i < keptRows.Count; i++)
            {
                target[i] = source[keptRows[i]];
            }

            distinctColumns[column] = target;
        }

        return SolutionTable.Columnar(input.Schema, distinctColumns, keptRows.Count, input.Dictionary);
    }

    /// <summary>Applies the <c>OFFSET</c>/<c>LIMIT</c> window to a columnar table, copying the windowed row range out of each column so only the surviving rows are ever decoded downstream. No term is decoded.</summary>
    /// <param name="input">The columnar input table.</param>
    /// <param name="offset">The number of leading rows to skip (clamped to the row count); non-positive skips none.</param>
    /// <param name="limit">The maximum rows to keep, or <see langword="null"/> for no upper bound.</param>
    /// <returns>The windowed columnar table.</returns>
    public static SolutionTable Slice(SolutionTable input, int offset, int? limit)
    {
        int start = Math.Min(offset > 0 ? offset : 0, input.Count);
        int end = limit is int upper ? Math.Min(start + Math.Max(upper, 0), input.Count) : input.Count;
        int windowCount = end - start;
        if(start == 0 && windowCount == input.Count)
        {
            return input;
        }

        int columnCount = input.Schema.Count;
        uint[][] windowColumns = new uint[columnCount][];
        for(int column = 0; column < columnCount; column++)
        {
            uint[] target = new uint[windowCount];
            Array.Copy(input.ColumnArray(column), start, target, 0, windowCount);
            windowColumns[column] = target;
        }

        return SolutionTable.Columnar(input.Schema, windowColumns, windowCount, input.Dictionary);
    }

    /// <summary>Unions two columnar tables (§18.6 Union): the schema is the left schema followed by the right-only variables, and each output column is filled from the left rows then the right rows, storing <c>0</c> (unbound) where a side lacks the variable. No term is decoded.</summary>
    /// <param name="left">The columnar left table.</param>
    /// <param name="right">The columnar right table.</param>
    /// <returns>The unioned columnar table.</returns>
    public static SolutionTable Union(SolutionTable left, SolutionTable right)
    {
        IReadOnlyList<SparqlVariable> leftSchema = left.Schema;
        IReadOnlyList<SparqlVariable> rightSchema = right.Schema;

        List<SparqlVariable> schema = new(leftSchema.Count + rightSchema.Count);
        Dictionary<SparqlVariable, int> columnOf = new(leftSchema.Count + rightSchema.Count);
        foreach(SparqlVariable variable in leftSchema)
        {
            columnOf[variable] = schema.Count;
            schema.Add(variable);
        }

        foreach(SparqlVariable variable in rightSchema)
        {
            if(!columnOf.ContainsKey(variable))
            {
                columnOf[variable] = schema.Count;
                schema.Add(variable);
            }
        }

        int total = left.Count + right.Count;
        uint[][] columns = new uint[schema.Count][];
        for(int i = 0; i < schema.Count; i++)
        {
            columns[i] = new uint[total];
        }

        CopySideInto(left, columnOf, columns, 0);
        CopySideInto(right, columnOf, columns, left.Count);

        return SolutionTable.Columnar(schema, columns, total, left.Dictionary);
    }

    /// <summary>
    /// The columnar shared-variable hash join (§18.6 Join): when the two tables share one or two variables that
    /// every row binds, equal join key ⟺ compatible (the shared bound variables are the whole schema intersection,
    /// so no residual compatibility check remains). Builds the smaller side's encoded-id hash table, probes the
    /// larger, and emits a columnar table over the left schema followed by the right-only variables (Merge(left,
    /// right) order). Returns <see langword="false"/> when the shared set is not one or two all-bound variables —
    /// the caller then bridges to the row form (cartesian, wider, or partially-bound joins).
    /// </summary>
    /// <param name="left">The left columnar table.</param>
    /// <param name="right">The right columnar table.</param>
    /// <param name="result">Receives the joined columnar table when the fast path applies.</param>
    /// <returns><see langword="true"/> when the columnar fast path produced the join; otherwise <see langword="false"/>.</returns>
    public static bool TryJoin(SolutionTable left, SolutionTable right, out SolutionTable result)
    {
        IReadOnlyList<SparqlVariable> leftSchema = left.Schema;
        IReadOnlyList<SparqlVariable> rightSchema = right.Schema;

        Dictionary<SparqlVariable, int> rightColumnOf = new(rightSchema.Count);
        for(int i = 0; i < rightSchema.Count; i++)
        {
            rightColumnOf[rightSchema[i]] = i;
        }

        //The shared variables (in left-schema order) and each side's column index for them.
        List<int> leftSharedColumns = [];
        List<int> rightSharedColumns = [];
        for(int i = 0; i < leftSchema.Count; i++)
        {
            if(rightColumnOf.TryGetValue(leftSchema[i], out int rightColumn))
            {
                leftSharedColumns.Add(i);
                rightSharedColumns.Add(rightColumn);
            }
        }

        int sharedCount = leftSharedColumns.Count;
        if(sharedCount is < 1 or > 2 || !AllRowsBound(left, leftSharedColumns) || !AllRowsBound(right, rightSharedColumns))
        {
            result = SolutionTable.Empty;

            return false;
        }

        //The output schema: the whole left schema, then the right-only variables — Merge(left, right) order.
        List<SparqlVariable> schema = new(leftSchema.Count + rightSchema.Count);
        schema.AddRange(leftSchema);
        List<int> rightOnlyColumns = [];
        for(int i = 0; i < rightSchema.Count; i++)
        {
            if(!leftSchema.Contains(rightSchema[i]))
            {
                schema.Add(rightSchema[i]);
                rightOnlyColumns.Add(i);
            }
        }

        //Build the smaller side's hash table on the packed shared-variable key, probe the larger; the (left row,
        //right row) pair is recovered regardless of which side was physically built.
        bool buildLeft = left.Count <= right.Count;
        SolutionTable build = buildLeft ? left : right;
        SolutionTable probe = buildLeft ? right : left;
        List<int> buildSharedColumns = buildLeft ? leftSharedColumns : rightSharedColumns;
        List<int> probeSharedColumns = buildLeft ? rightSharedColumns : leftSharedColumns;

        Dictionary<ulong, List<int>> index = new(build.Count);
        for(int row = 0; row < build.Count; row++)
        {
            ulong key = PackJoinKey(build, buildSharedColumns, row);
            if(!index.TryGetValue(key, out List<int>? bucket))
            {
                bucket = [];
                index[key] = bucket;
            }

            bucket.Add(row);
        }

        uint[][] leftColumns = ColumnArraysOf(left);
        uint[][] rightColumns = ColumnArraysOf(right);

        List<uint>[] outputColumns = new List<uint>[schema.Count];
        for(int i = 0; i < outputColumns.Length; i++)
        {
            outputColumns[i] = [];
        }

        int count = 0;
        for(int probeRow = 0; probeRow < probe.Count; probeRow++)
        {
            ulong key = PackJoinKey(probe, probeSharedColumns, probeRow);
            if(!index.TryGetValue(key, out List<int>? bucket))
            {
                continue;
            }

            foreach(int buildRow in bucket)
            {
                int leftRow = buildLeft ? buildRow : probeRow;
                int rightRow = buildLeft ? probeRow : buildRow;
                for(int c = 0; c < leftSchema.Count; c++)
                {
                    outputColumns[c].Add(leftColumns[c][leftRow]);
                }

                for(int c = 0; c < rightOnlyColumns.Count; c++)
                {
                    outputColumns[leftSchema.Count + c].Add(rightColumns[rightOnlyColumns[c]][rightRow]);
                }

                count++;
            }
        }

        uint[][] frozen = new uint[schema.Count][];
        for(int c = 0; c < schema.Count; c++)
        {
            frozen[c] = [.. outputColumns[c]];
        }

        result = SolutionTable.Columnar(schema, frozen, count, left.Dictionary);

        return true;
    }

    /// <summary>Copies one union side's columns into the merged columns at a row offset, leaving the merged columns the side does not bind at their default <c>0</c> (unbound).</summary>
    /// <param name="side">The side table (columnar).</param>
    /// <param name="columnOf">The merged-schema variable-to-column index.</param>
    /// <param name="mergedColumns">The merged columns being filled.</param>
    /// <param name="rowOffset">The first merged-column row this side's rows occupy.</param>
    private static void CopySideInto(SolutionTable side, Dictionary<SparqlVariable, int> columnOf, uint[][] mergedColumns, int rowOffset)
    {
        IReadOnlyList<SparqlVariable> sideSchema = side.Schema;
        for(int sourceColumn = 0; sourceColumn < sideSchema.Count; sourceColumn++)
        {
            int targetColumn = columnOf[sideSchema[sourceColumn]];
            Array.Copy(side.ColumnArray(sourceColumn), 0, mergedColumns[targetColumn], rowOffset, side.Count);
        }
    }

    /// <summary>Whether every row of a columnar table binds each of the given columns (no unbound <c>0</c> cell) — the all-bound precondition that makes equal join key equivalent to compatibility.</summary>
    /// <param name="table">The columnar table.</param>
    /// <param name="columns">The schema column indices to test.</param>
    /// <returns><see langword="true"/> when no listed column has an unbound cell.</returns>
    private static bool AllRowsBound(SolutionTable table, List<int> columns)
    {
        foreach(int column in columns)
        {
            ReadOnlySpan<uint> values = table.ColumnOf(column);
            for(int row = 0; row < values.Length; row++)
            {
                if(values[row] == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Packs a row's one or two shared-variable encoded ids into a join key (the high word the first, the low word the second; a single variable leaves the low word zero, unambiguous because the shared columns are all bound).</summary>
    /// <param name="table">The columnar table.</param>
    /// <param name="sharedColumns">The one or two shared-variable column indices.</param>
    /// <param name="row">The row index.</param>
    /// <returns>The packed key.</returns>
    private static ulong PackJoinKey(SolutionTable table, List<int> sharedColumns, int row)
    {
        ulong first = table.ColumnArray(sharedColumns[0])[row];

        return sharedColumns.Count == 1 ? first << 32 : (first << 32) | table.ColumnArray(sharedColumns[1])[row];
    }

    /// <summary>Gathers a columnar table's backing column arrays into one array, indexed by schema position.</summary>
    /// <param name="table">The columnar table.</param>
    /// <returns>The backing arrays, one per schema column.</returns>
    private static uint[][] ColumnArraysOf(SolutionTable table)
    {
        uint[][] columns = new uint[table.Schema.Count][];
        for(int i = 0; i < columns.Length; i++)
        {
            columns[i] = table.ColumnArray(i);
        }

        return columns;
    }

    /// <summary>Row identity over a columnar table's columns: two row indices are equal when every column holds the same encoded id, hashing the row's encoded-id tuple. The comparison key is the row index into the shared columns.</summary>
    private sealed class RowComparer : IEqualityComparer<int>
    {
        private readonly uint[][] columns;

        private readonly int columnCount;

        /// <summary>Constructs the comparer over the table's columns.</summary>
        /// <param name="columns">The encoded-id columns, indexed by schema position then row.</param>
        /// <param name="columnCount">The number of columns (schema width).</param>
        public RowComparer(uint[][] columns, int columnCount)
        {
            this.columns = columns;
            this.columnCount = columnCount;
        }

        /// <summary>Returns whether two rows hold the same encoded id in every column.</summary>
        /// <param name="x">The first row index.</param>
        /// <param name="y">The second row index.</param>
        /// <returns><see langword="true"/> when the rows are equal across all columns.</returns>
        public bool Equals(int x, int y)
        {
            for(int column = 0; column < columnCount; column++)
            {
                if(columns[column][x] != columns[column][y])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Hashes a row's encoded-id tuple across the columns.</summary>
        /// <param name="row">The row index.</param>
        /// <returns>The row's hash code.</returns>
        public int GetHashCode(int row)
        {
            HashCode hash = new();
            for(int column = 0; column < columnCount; column++)
            {
                hash.Add(columns[column][row]);
            }

            return hash.ToHashCode();
        }
    }
}
