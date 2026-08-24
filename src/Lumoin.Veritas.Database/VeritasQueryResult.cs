using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The result of a query against a <see cref="VeritasEngine"/>: a SELECT's solution bindings, an ASK's
/// boolean, or a CONSTRUCT/DESCRIBE's RDF graph. Exactly one is present, per the query's form, so a
/// transport renders it to the requested SPARQL result format without parsing the query itself to learn
/// the form. A consumer dispatches on the shape FIRST — <see cref="IsAsk"/> / <see cref="IsGraph"/> —
/// before touching the shape-specific member; the tabular result writers reject a <c>null</c> result
/// set, so a graph result routed at a bindings renderer is a caller defect.
/// </summary>
public sealed record VeritasQueryResult
{
    /// <summary>Constructs a result holding exactly one of the bindings, the boolean, or the graph.</summary>
    /// <param name="bindings">The SELECT bindings, or <c>null</c> for the other forms.</param>
    /// <param name="boolean">The ASK boolean, or <c>null</c> for the other forms.</param>
    /// <param name="graph">The CONSTRUCT/DESCRIBE graph, or <c>null</c> for the other forms.</param>
    private VeritasQueryResult(SparqlResultSet? bindings, bool? boolean, IReadOnlyList<Quad>? graph)
    {
        Bindings = bindings;
        Boolean = boolean;
        Graph = graph;
    }

    /// <summary>The SELECT solution bindings, or <c>null</c> when the query was not a SELECT.</summary>
    public SparqlResultSet? Bindings { get; }

    /// <summary>The ASK boolean, or <c>null</c> when the query was not an ASK.</summary>
    public bool? Boolean { get; }

    /// <summary>The CONSTRUCT/DESCRIBE result graph as default-graph quads, or <c>null</c> when the query was not a graph form.</summary>
    public IReadOnlyList<Quad>? Graph { get; }

    /// <summary>Whether the result is an ASK boolean.</summary>
    public bool IsAsk => Boolean is not null;

    /// <summary>Whether the result is a CONSTRUCT/DESCRIBE graph.</summary>
    public bool IsGraph => Graph is not null;

    /// <summary>Builds a SELECT result over its bindings.</summary>
    /// <param name="bindings">The solution bindings.</param>
    /// <returns>The result.</returns>
    public static VeritasQueryResult ForSelect(SparqlResultSet bindings)
    {
        return new VeritasQueryResult(bindings, boolean: null, graph: null);
    }

    /// <summary>Builds an ASK result over its boolean.</summary>
    /// <param name="boolean">The ASK answer.</param>
    /// <returns>The result.</returns>
    public static VeritasQueryResult ForAsk(bool boolean)
    {
        return new VeritasQueryResult(bindings: null, boolean, graph: null);
    }

    /// <summary>Builds a CONSTRUCT/DESCRIBE result over its graph.</summary>
    /// <param name="graph">The result graph, as default-graph quads.</param>
    /// <returns>The result.</returns>
    public static VeritasQueryResult ForGraph(IReadOnlyList<Quad> graph)
    {
        return new VeritasQueryResult(bindings: null, boolean: null, graph);
    }
}
