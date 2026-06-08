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

        return SolutionTable.Columnar(variables, projectedColumns, input.Count, input.Dictionary, input.Overlay);
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

        return Gather(input, keptRows);
    }

    /// <summary>
    /// Keeps the rows whose value in one column equals (or, when <paramref name="keepEqual"/> is
    /// <see langword="false"/>, differs from) a term id, without decoding — the columnar form of a
    /// <c>FILTER(?v = &lt;iri&gt;)</c> / <c>FILTER(?v != &lt;iri&gt;)</c>. Sound only for an IRI constant: an IRI is
    /// never value-equal to a different term, so term-id equality coincides with SPARQL <c>=</c>/<c>!=</c> there
    /// (including the IRI-vs-literal "known different" case and the unbound type error). An unbound cell
    /// (<c>0</c>) is dropped by both forms (the comparison is a type error); an absent constant
    /// (<paramref name="termId"/> <c>0</c>) matches no bound term, so the equality form keeps nothing.
    /// </summary>
    /// <param name="input">The columnar input table.</param>
    /// <param name="columnIndex">The schema position of the compared variable.</param>
    /// <param name="termId">The constant IRI's encoded term id, or <c>0</c> when the IRI is absent from the dictionary.</param>
    /// <param name="keepEqual"><see langword="true"/> to keep rows equal to the term (the <c>=</c> form); <see langword="false"/> to keep bound rows that differ (the <c>!=</c> form).</param>
    /// <returns>The surviving columnar table.</returns>
    public static SolutionTable FilterByTerm(SolutionTable input, int columnIndex, uint termId, bool keepEqual)
    {
        ReadOnlySpan<uint> column = input.ColumnOf(columnIndex);
        List<int> keptRows = new(input.Count);
        if(keepEqual)
        {
            //An absent constant (termId 0) equals no bound term; an unbound cell (0) never equals a real id either.
            if(termId != 0)
            {
                for(int row = 0; row < column.Length; row++)
                {
                    if(column[row] == termId)
                    {
                        keptRows.Add(row);
                    }
                }
            }
        }
        else
        {
            //Bound and different: an unbound cell (0) is a type error, dropped; a real id different from the term passes.
            for(int row = 0; row < column.Length; row++)
            {
                uint value = column[row];
                if(value != 0 && value != termId)
                {
                    keptRows.Add(row);
                }
            }
        }

        return Gather(input, keptRows);
    }

    /// <summary>Builds a columnar table of the given rows of <paramref name="input"/>, gathered in the listed order; returns <paramref name="input"/> unchanged when every row is kept (the rows are an ascending subset, so a full count is the identity).</summary>
    /// <param name="input">The columnar source table.</param>
    /// <param name="keptRows">The retained row indices, ascending.</param>
    /// <returns>The gathered columnar table.</returns>
    private static SolutionTable Gather(SolutionTable input, List<int> keptRows)
    {
        if(keptRows.Count == input.Count)
        {
            return input;
        }

        int columnCount = input.Schema.Count;
        uint[][] gathered = new uint[columnCount][];
        for(int column = 0; column < columnCount; column++)
        {
            uint[] source = input.ColumnArray(column);
            uint[] target = new uint[keptRows.Count];
            for(int i = 0; i < keptRows.Count; i++)
            {
                target[i] = source[keptRows[i]];
            }

            gathered[column] = target;
        }

        return SolutionTable.Columnar(input.Schema, gathered, keptRows.Count, input.Dictionary, input.Overlay);
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

        return SolutionTable.Columnar(input.Schema, windowColumns, windowCount, input.Dictionary, input.Overlay);
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

        return SolutionTable.Columnar(schema, columns, total, left.Dictionary, left.Overlay ?? right.Overlay);
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

        result = SolutionTable.Columnar(schema, frozen, count, left.Dictionary, left.Overlay ?? right.Overlay);

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

    /// <summary>
    /// The columnar form of a condition-free LEFT JOIN (§18.6 LeftJoin, the <c>OPTIONAL</c> with no lifted inner
    /// FILTER): every left row appears at least once, extended by each compatible right row (matched on the shared
    /// one or two all-bound variables) and otherwise carried with the right-only columns unbound. The output schema
    /// is the left schema then the right-only variables. Returns <see langword="false"/> for a shared set that is
    /// not one or two all-bound variables (including a cartesian no-shared OPTIONAL), leaving the caller to bridge
    /// to the row form; the caller also keeps OPTIONALs that carry a condition on the row path.
    /// </summary>
    /// <param name="left">The required (left) columnar table.</param>
    /// <param name="right">The optional (right) columnar table.</param>
    /// <param name="result">Receives the left-joined columnar table when the columnar path applies.</param>
    /// <returns><see langword="true"/> when the left join was evaluated columnar; otherwise <see langword="false"/>.</returns>
    public static bool TryLeftJoin(SolutionTable left, SolutionTable right, out SolutionTable result)
    {
        IReadOnlyList<SparqlVariable> leftSchema = left.Schema;
        IReadOnlyList<SparqlVariable> rightSchema = right.Schema;

        Dictionary<SparqlVariable, int> rightColumnOf = new(rightSchema.Count);
        for(int i = 0; i < rightSchema.Count; i++)
        {
            rightColumnOf[rightSchema[i]] = i;
        }

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

        if(leftSharedColumns.Count is < 1 or > 2 || !AllRowsBound(left, leftSharedColumns) || !AllRowsBound(right, rightSharedColumns))
        {
            result = SolutionTable.Empty;

            return false;
        }

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

        //Index the right side on the shared key; every left row appears at least once.
        Dictionary<ulong, List<int>> index = new(right.Count);
        for(int row = 0; row < right.Count; row++)
        {
            ulong key = PackJoinKey(right, rightSharedColumns, row);
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
        for(int leftRow = 0; leftRow < left.Count; leftRow++)
        {
            if(index.TryGetValue(PackJoinKey(left, leftSharedColumns, leftRow), out List<int>? matches))
            {
                foreach(int rightRow in matches)
                {
                    AppendLeftJoinRow(outputColumns, leftColumns, leftSchema.Count, rightColumns, rightOnlyColumns, leftRow, rightRow);
                    count++;
                }

                continue;
            }

            //No compatible right row: the left row is carried with the right-only columns unbound.
            AppendLeftJoinRow(outputColumns, leftColumns, leftSchema.Count, rightColumns, rightOnlyColumns, leftRow, rightRow: -1);
            count++;
        }

        uint[][] frozen = new uint[schema.Count][];
        for(int c = 0; c < schema.Count; c++)
        {
            frozen[c] = [.. outputColumns[c]];
        }

        result = SolutionTable.Columnar(schema, frozen, count, left.Dictionary, left.Overlay ?? right.Overlay);

        return true;
    }

    /// <summary>Appends one left-join output row: the left columns from <paramref name="leftRow"/>, then the right-only columns from <paramref name="rightRow"/> (or <c>0</c>/unbound when <paramref name="rightRow"/> is <c>-1</c>, the unmatched left case).</summary>
    /// <param name="outputColumns">The output column builders.</param>
    /// <param name="leftColumns">The left table's column arrays.</param>
    /// <param name="leftColumnCount">The number of left schema columns (the output's left prefix width).</param>
    /// <param name="rightColumns">The right table's column arrays.</param>
    /// <param name="rightOnlyColumns">The right column indices that are not in the left schema, in output order.</param>
    /// <param name="leftRow">The left row index.</param>
    /// <param name="rightRow">The right row index, or <c>-1</c> for the unmatched (right-unbound) case.</param>
    private static void AppendLeftJoinRow(List<uint>[] outputColumns, uint[][] leftColumns, int leftColumnCount, uint[][] rightColumns, List<int> rightOnlyColumns, int leftRow, int rightRow)
    {
        for(int c = 0; c < leftColumnCount; c++)
        {
            outputColumns[c].Add(leftColumns[c][leftRow]);
        }

        for(int c = 0; c < rightOnlyColumns.Count; c++)
        {
            outputColumns[leftColumnCount + c].Add(rightRow < 0 ? 0u : rightColumns[rightOnlyColumns[c]][rightRow]);
        }
    }

    /// <summary>
    /// The columnar form of MINUS (§18.6): keeps the left rows whose shared-variable key matches no right row.
    /// When the schemas share no variable, the disjoint-domain exception keeps every left row (returned as the
    /// left table). When they share one or two variables that every row binds, equal key ⟺ compatible (and the
    /// shared variables are exactly the common set, so compatible ⟺ "compatible and shares a variable", the MINUS
    /// condition), so a left row is removed precisely when its key is present among the right keys. Returns
    /// <see langword="false"/> for a wider or partially-bound shared set, leaving the caller to bridge to the row
    /// form.
    /// </summary>
    /// <param name="left">The left (kept) columnar table.</param>
    /// <param name="right">The right (subtracting) columnar table.</param>
    /// <param name="result">Receives the surviving columnar table when the columnar path applies.</param>
    /// <returns><see langword="true"/> when MINUS was evaluated columnar; otherwise <see langword="false"/>.</returns>
    public static bool TryMinus(SolutionTable left, SolutionTable right, out SolutionTable result)
    {
        IReadOnlyList<SparqlVariable> leftSchema = left.Schema;
        IReadOnlyList<SparqlVariable> rightSchema = right.Schema;

        Dictionary<SparqlVariable, int> rightColumnOf = new(rightSchema.Count);
        for(int i = 0; i < rightSchema.Count; i++)
        {
            rightColumnOf[rightSchema[i]] = i;
        }

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

        //No shared variable: the disjoint-domain exception keeps every left row unchanged.
        if(leftSharedColumns.Count == 0)
        {
            result = left;

            return true;
        }

        if(leftSharedColumns.Count > 2 || !AllRowsBound(left, leftSharedColumns) || !AllRowsBound(right, rightSharedColumns))
        {
            result = SolutionTable.Empty;

            return false;
        }

        HashSet<ulong> rightKeys = new(right.Count);
        for(int row = 0; row < right.Count; row++)
        {
            rightKeys.Add(PackJoinKey(right, rightSharedColumns, row));
        }

        List<int> keptRows = new(left.Count);
        for(int row = 0; row < left.Count; row++)
        {
            if(!rightKeys.Contains(PackJoinKey(left, leftSharedColumns, row)))
            {
                keptRows.Add(row);
            }
        }

        result = Gather(left, keptRows);

        return true;
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
