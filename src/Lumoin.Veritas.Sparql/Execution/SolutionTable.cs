using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The engine's central result currency: a solution sequence held in one of two backings — a column-major
/// <c>TermId</c> table (the columnar island that starts at a basic-graph-pattern leaf and grows up through the
/// columnar operators) or a materialized <see cref="SparqlSolution"/> row list (the bridge the not-yet-columnar
/// operators consume). Decoding a <c>TermId</c> column to its <see cref="RdfTerm"/> happens ONCE, lazily, at the
/// boundary that needs rows — the final result, or the first operator still evaluating row-at-a-time — so a
/// fully-columnar pipeline never materializes an intermediate row.
/// </summary>
/// <remarks>
/// <para>
/// In the columnar backing each schema variable has one <c>uint</c> column of encoded term ids
/// (<see cref="TermId.Encoded"/>), all of length <see cref="Count"/>. A cell holding <c>0</c>
/// (<see cref="TermId.None"/>) is unbound — term ids are one-based, so zero never denotes a real term — which lets
/// the columnar form carry the partial solution mappings <c>UNION</c> and <c>OPTIONAL</c> produce. The columns are
/// positional against <see cref="Schema"/> and read-only by convention once the table is constructed.
/// </para>
/// <para>
/// The row backing simply wraps an existing <see cref="SparqlSolution"/> sequence. <see cref="AsRows"/> is the one
/// bridge between the two: it returns the row backing directly, or decodes the columnar backing once and caches the
/// result. The hot path is concrete — there is no per-row or per-batch virtual dispatch — matching the rest of the
/// engine's join machinery.
/// </para>
/// </remarks>
[DebuggerDisplay("SolutionTable Columnar={IsColumnar} Count={Count}")]
internal sealed class SolutionTable
{
    private readonly uint[][]? columns;

    private readonly IReadOnlyList<SparqlVariable>? schema;

    private readonly TermDictionary? dictionary;

    private readonly ComputedTermOverlay? overlay;

    private IReadOnlyList<SparqlSolution>? rows;

    /// <summary>Constructs the columnar backing over the given schema and encoded-id columns.</summary>
    /// <param name="schema">The variables the columns bind, positionally.</param>
    /// <param name="columns">One encoded-id column per schema variable, each of length <paramref name="count"/>; cell <c>0</c> is unbound.</param>
    /// <param name="count">The committed row count.</param>
    /// <param name="dictionary">The term dictionary the encoded ids decode through at the boundary.</param>
    /// <param name="overlay">The query-scoped overlay holding computed terms whose ids carry the reserved high bit, or <see langword="null"/> when the columns hold only data-dictionary ids.</param>
    private SolutionTable(IReadOnlyList<SparqlVariable> schema, uint[][] columns, int count, TermDictionary dictionary, ComputedTermOverlay? overlay)
    {
        this.schema = schema;
        this.columns = columns;
        this.dictionary = dictionary;
        this.overlay = overlay;
        Count = count;
    }

    /// <summary>Constructs the row backing over an already-materialized solution sequence.</summary>
    /// <param name="rows">The solutions held by reference.</param>
    private SolutionTable(IReadOnlyList<SparqlSolution> rows)
    {
        this.rows = rows;
        Count = rows.Count;
    }

    /// <summary>The empty solution sequence — no rows.</summary>
    public static SolutionTable Empty { get; } = new((IReadOnlyList<SparqlSolution>)[]);

    /// <summary>The number of solutions (rows) the table holds, in either backing.</summary>
    public int Count { get; }

    /// <summary>Whether this table is held in the columnar (encoded-id) backing rather than as materialized rows.</summary>
    public bool IsColumnar => columns is not null;

    /// <summary>The variables the columnar backing's columns bind, positionally; only valid when <see cref="IsColumnar"/>.</summary>
    public IReadOnlyList<SparqlVariable> Schema => schema ?? throw new InvalidOperationException("Schema is only available on a columnar SolutionTable.");

    /// <summary>The term dictionary the columnar backing decodes through; only valid when <see cref="IsColumnar"/>.</summary>
    public TermDictionary Dictionary => dictionary ?? throw new InvalidOperationException("Dictionary is only available on a columnar SolutionTable.");

    /// <summary>The query-scoped overlay holding any computed terms in the columns (ids with the reserved high bit), or <see langword="null"/> when the columns hold only data-dictionary ids. Carried so derived columnar tables decode their computed cells; propagate it onto any columnar table built from this one.</summary>
    public ComputedTermOverlay? Overlay => overlay;

    /// <summary>The encoded-id column for a schema position; only valid when <see cref="IsColumnar"/>. Cell <c>0</c> is unbound.</summary>
    /// <param name="schemaIndex">The variable's position in <see cref="Schema"/>.</param>
    /// <returns>The column's <see cref="Count"/> encoded ids.</returns>
    public ReadOnlySpan<uint> ColumnOf(int schemaIndex)
    {
        return columns is null
            ? throw new InvalidOperationException("Columns are only available on a columnar SolutionTable.")
            : columns[schemaIndex].AsSpan(0, Count);
    }

    /// <summary>
    /// The backing encoded-id array for a schema position; only valid when <see cref="IsColumnar"/>. Its length is
    /// exactly <see cref="Count"/> (every producer freezes columns to size), so the columnar operators may alias it
    /// into a derived table without copying. Read-only by convention, like the rest of the table.
    /// </summary>
    /// <param name="schemaIndex">The variable's position in <see cref="Schema"/>.</param>
    /// <returns>The column's backing array.</returns>
    public uint[] ColumnArray(int schemaIndex)
    {
        return columns is null
            ? throw new InvalidOperationException("Columns are only available on a columnar SolutionTable.")
            : columns[schemaIndex];
    }

    /// <summary>Wraps an already-materialized solution sequence as a row-backed table.</summary>
    /// <param name="rows">The solutions held by reference.</param>
    /// <returns>The row-backed table.</returns>
    public static SolutionTable FromRows(IReadOnlyList<SparqlSolution> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows.Count == 0 ? Empty : new SolutionTable(rows);
    }

    /// <summary>Builds a columnar table over the given schema and encoded-id columns.</summary>
    /// <param name="schema">The variables the columns bind, positionally.</param>
    /// <param name="columns">One encoded-id column per schema variable, each of length <paramref name="count"/>; cell <c>0</c> is unbound.</param>
    /// <param name="count">The committed row count.</param>
    /// <param name="dictionary">The term dictionary the encoded ids decode through at the boundary.</param>
    /// <param name="overlay">The query-scoped overlay holding any computed terms (ids with the reserved high bit), or <see langword="null"/> when the columns hold only data-dictionary ids.</param>
    /// <returns>The columnar table.</returns>
    public static SolutionTable Columnar(IReadOnlyList<SparqlVariable> schema, uint[][] columns, int count, TermDictionary dictionary, ComputedTermOverlay? overlay = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(dictionary);

        return new SolutionTable(schema, columns, count, dictionary, overlay);
    }

    /// <summary>
    /// The bridge to the row-oriented operators: returns the materialized solution sequence, decoding the columnar
    /// backing once (and caching it) when this table is columnar. An unbound cell (<c>0</c>) is dropped from its
    /// solution, so a decoded solution binds only its bound variables — the same shape the row operators produce.
    /// </summary>
    /// <returns>The solutions, row by row.</returns>
    public IReadOnlyList<SparqlSolution> AsRows()
    {
        if(rows is not null)
        {
            return rows;
        }

        List<SparqlSolution> decoded = new(Count);
        for(int row = 0; row < Count; row++)
        {
            decoded.Add(DecodeRow(row));
        }

        rows = decoded;

        return decoded;
    }

    /// <summary>
    /// Decodes one columnar row to a solution, skipping its unbound cells — the per-row form <see cref="AsRows"/>
    /// builds on, exposed so an operator that must evaluate an expression over a row (a columnar <c>Extend</c>) can
    /// do so without materializing and caching the whole decoded sequence. Only valid when <see cref="IsColumnar"/>.
    /// </summary>
    /// <param name="row">The row index; below <see cref="Count"/>.</param>
    /// <returns>The decoded solution.</returns>
    public SparqlSolution DecodeRow(int row)
    {
        List<SparqlBinding> bindings = new(schema!.Count);
        for(int column = 0; column < schema.Count; column++)
        {
            if(DecodeCell(column, row) is RdfTerm term)
            {
                bindings.Add(new SparqlBinding(schema[column], term));
            }
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>
    /// Decodes one cell to its RDF term, or <see langword="null"/> when the cell is unbound (<c>0</c>). A computed
    /// term (the overlay's reserved id bit set) resolves through the overlay, a data id through the dictionary; the
    /// two id ranges never overlap. Only valid when <see cref="IsColumnar"/>. Lets an operator decode just the
    /// columns it needs (an <c>ORDER BY</c> key) rather than the whole row.
    /// </summary>
    /// <param name="schemaIndex">The variable's position in <see cref="Schema"/>.</param>
    /// <param name="row">The row index; below <see cref="Count"/>.</param>
    /// <returns>The cell's term, or <see langword="null"/> when unbound.</returns>
    public RdfTerm? DecodeCell(int schemaIndex, int row)
    {
        uint encoded = columns![schemaIndex][row];
        if(encoded == 0)
        {
            return null;
        }

        return overlay is not null && ComputedTermOverlay.IsOverlay(encoded)
            ? overlay.Resolve(encoded)
            : dictionary!.Resolve(TermId.FromEncoded(encoded));
    }
}
