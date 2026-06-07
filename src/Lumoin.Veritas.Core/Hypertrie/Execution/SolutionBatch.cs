using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// A column-major batch of solutions: one <c>uint</c> column of
/// bound term ids per schema variable, up to
/// <see cref="BatchLength"/> rows. The batched execution spine —
/// one batch object stands in for up to a thousand per-row
/// <see cref="Solution"/> allocations, and conversion to rows
/// happens once at the consumer boundary instead of per element
/// inside the join.
/// </summary>
/// <remarks>
/// The schema is fixed at construction and shared by every batch
/// of a stream; columns are positional against it. Producers write
/// through <see cref="ColumnSpan"/> and commit rows with
/// <see cref="SetCount"/>; consumers read <see cref="ColumnOf"/>
/// or flatten through <see cref="CopyRowTo"/>. A batch is
/// single-writer, then read-only by convention.
/// </remarks>
[DebuggerDisplay("SolutionBatch Variables={Schema.Count} Count={Count}")]
public sealed class SolutionBatch
{
    /// <summary>The maximum rows per batch — matched to the columnar block length so a scan's decode granularity and the batch granularity coincide.</summary>
    public const int BatchLength = 1024;

    private readonly uint[][] columns;

    /// <summary>The variables this batch's columns bind, positionally.</summary>
    public IReadOnlyList<Variable> Schema { get; }

    /// <summary>The number of committed rows.</summary>
    public int Count { get; private set; }

    /// <summary>Constructs an empty batch over <paramref name="schema"/>, allocating one full-length column per variable.</summary>
    /// <param name="schema">The variables the columns bind, positionally; shared across the stream's batches.</param>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c>.</exception>
    public SolutionBatch(IReadOnlyList<Variable> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        Schema = schema;
        columns = new uint[schema.Count][];
        for(int i = 0; i < schema.Count; i++)
        {
            columns[i] = new uint[BatchLength];
        }
    }

    /// <summary>The committed rows of one column.</summary>
    /// <param name="schemaIndex">The variable's position in <see cref="Schema"/>.</param>
    /// <returns>The column's committed values.</returns>
    public ReadOnlySpan<uint> ColumnOf(int schemaIndex)
    {
        return columns[schemaIndex].AsSpan(0, Count);
    }

    /// <summary>The full writable column, for producers filling rows before <see cref="SetCount"/>.</summary>
    /// <param name="schemaIndex">The variable's position in <see cref="Schema"/>.</param>
    /// <returns>The column's full span.</returns>
    public Span<uint> ColumnSpan(int schemaIndex)
    {
        return columns[schemaIndex];
    }

    /// <summary>Commits the batch's row count after the producer filled the columns.</summary>
    /// <param name="count">The committed row count; at most <see cref="BatchLength"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative or exceeds <see cref="BatchLength"/>.</exception>
    public void SetCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, BatchLength);

        Count = count;
    }

    /// <summary>Materialises one row as bindings appended to <paramref name="bindings"/> — the per-row boundary conversion.</summary>
    /// <param name="row">The row index; below <see cref="Count"/>.</param>
    /// <param name="bindings">Receives one binding per schema variable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is <c>null</c>.</exception>
    public void CopyRowTo(int row, List<VariableBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        for(int i = 0; i < Schema.Count; i++)
        {
            bindings.Add(new VariableBinding(Schema[i], TermId.FromEncoded(columns[i][row])));
        }
    }

    /// <summary>
    /// The boundary adapter: flattens a batch stream into the
    /// per-row <see cref="Solution"/> sequence the row-oriented
    /// consumers expect. The batched pipeline's wins live BEFORE
    /// this point; the conversion happens exactly once.
    /// </summary>
    /// <param name="batches">The batch stream.</param>
    /// <param name="cancellationToken">A token that aborts the flattening.</param>
    /// <returns>The solutions, row by row.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "CS1998:Async method lacks await operators",
        Justification = "Yield-only async iterator over a synchronous batch stream; the async shape matches the engine surfaces that consume it.")]
    public static async IAsyncEnumerable<Solution> FlattenAsync(
        IEnumerable<SolutionBatch> batches,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batches);

        foreach(SolutionBatch batch in batches)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<VariableBinding> bindings = new(batch.Schema.Count);
                batch.CopyRowTo(row, bindings);

                yield return new Solution(bindings);
            }
        }
    }
}
