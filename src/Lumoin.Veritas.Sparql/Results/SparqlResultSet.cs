using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Sparql.Results;

/// <summary>
/// The materialized result of a SPARQL query in the variable-binding form shared by the W3C SPARQL Query Results
/// serializations (XML, JSON, CSV/TSV): either a <c>SELECT</c> result (an ordered head of variables plus a sequence
/// of solution mappings) or an <c>ASK</c> result (a single boolean). <c>CONSTRUCT</c>/<c>DESCRIBE</c> produce an RDF
/// graph instead and are not represented here.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Variables"/> preserves the head order — the column order an <c>ASK</c> result leaves empty and a
/// serializer reproduces. A variable that no solution binds still appears in <see cref="Variables"/> (it is a
/// declared projection column); a solution simply omits the binding. <see cref="Boolean"/> is set only for an
/// <c>ASK</c> result, where <see cref="Solutions"/> is empty; for a <c>SELECT</c> result it is <see langword="null"/>.
/// </para>
/// <para>
/// See the <see href="https://www.w3.org/TR/rdf-sparql-XMLres/">SPARQL Query Results XML Format</see> and
/// <see href="https://www.w3.org/TR/sparql11-results-json/">SPARQL 1.1 Query Results JSON Format</see>.
/// </para>
/// </remarks>
[DebuggerDisplay("SparqlResultSet {Boolean == null ? \"SELECT\" : \"ASK\",nq} vars={Variables.Count} rows={Solutions.Count}")]
public sealed class SparqlResultSet
{
    /// <summary>The declared head variables, in column order; empty for an <c>ASK</c> result.</summary>
    public IReadOnlyList<Utf8String> Variables { get; }

    /// <summary>The solution sequence for a <c>SELECT</c> result; empty for an <c>ASK</c> result.</summary>
    public IReadOnlyList<SparqlSolution> Solutions { get; }

    /// <summary>The boolean answer for an <c>ASK</c> result; <see langword="null"/> for a <c>SELECT</c> result.</summary>
    public bool? Boolean { get; }

    /// <summary>Constructs a result set; use <see cref="ForSelect"/> / <see cref="ForAsk"/> rather than calling this directly.</summary>
    /// <param name="variables">The head variables.</param>
    /// <param name="solutions">The solution sequence.</param>
    /// <param name="boolean">The boolean answer, or <see langword="null"/> for a <c>SELECT</c> result.</param>
    private SparqlResultSet(IReadOnlyList<Utf8String> variables, IReadOnlyList<SparqlSolution> solutions, bool? boolean)
    {
        Variables = variables;
        Solutions = solutions;
        Boolean = boolean;
    }

    /// <summary>Whether this is an <c>ASK</c> result (carries a boolean) rather than a <c>SELECT</c> result.</summary>
    public bool IsBoolean => Boolean is not null;

    /// <summary>Builds a <c>SELECT</c> result from its head variables and solution sequence.</summary>
    /// <param name="variables">The declared head variables, in column order.</param>
    /// <param name="solutions">The solution sequence.</param>
    /// <returns>The result set.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static SparqlResultSet ForSelect(IReadOnlyList<Utf8String> variables, IReadOnlyList<SparqlSolution> solutions)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(solutions);

        return new SparqlResultSet(variables, solutions, boolean: null);
    }

    /// <summary>Builds an <c>ASK</c> result from its boolean answer.</summary>
    /// <param name="value">The boolean answer.</param>
    /// <returns>The result set.</returns>
    public static SparqlResultSet ForAsk(bool value)
    {
        return new SparqlResultSet([], [], value);
    }
}
