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

    private IReadOnlyList<SparqlSolution>? rows;

    /// <summary>Constructs the columnar backing over the given schema and encoded-id columns.</summary>
    /// <param name="schema">The variables the columns bind, positionally.</param>
    /// <param name="columns">One encoded-id column per schema variable, each of length <paramref name="count"/>; cell <c>0</c> is unbound.</param>
    /// <param name="count">The committed row count.</param>
    /// <param name="dictionary">The term dictionary the encoded ids decode through at the boundary.</param>
    private SolutionTable(IReadOnlyList<SparqlVariable> schema, uint[][] columns, int count, TermDictionary dictionary)
    {
        this.schema = schema;
        this.columns = columns;
        this.dictionary = dictionary;
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

    /// <summary>The encoded-id column for a schema position; only valid when <see cref="IsColumnar"/>. Cell <c>0</c> is unbound.</summary>
    /// <param name="schemaIndex">The variable's position in <see cref="Schema"/>.</param>
    /// <returns>The column's <see cref="Count"/> encoded ids.</returns>
    public ReadOnlySpan<uint> ColumnOf(int schemaIndex)
    {
        return columns is null
            ? throw new InvalidOperationException("Columns are only available on a columnar SolutionTable.")
            : columns[schemaIndex].AsSpan(0, Count);
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
    /// <returns>The columnar table.</returns>
    public static SolutionTable Columnar(IReadOnlyList<SparqlVariable> schema, uint[][] columns, int count, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(dictionary);

        return new SolutionTable(schema, columns, count, dictionary);
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
            List<SparqlBinding> bindings = new(schema!.Count);
            for(int column = 0; column < schema.Count; column++)
            {
                uint encoded = columns![column][row];
                if(encoded != 0)
                {
                    bindings.Add(new SparqlBinding(schema[column], dictionary!.Resolve(TermId.FromEncoded(encoded))));
                }
            }

            decoded.Add(new SparqlSolution(bindings));
        }

        rows = decoded;

        return decoded;
    }
}
