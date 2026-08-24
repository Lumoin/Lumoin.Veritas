using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The parsed <c>name=value</c> arguments for an analytics run, with typed, defaulted access. A surface collects
/// the raw assignments; the catalog descriptor reads the ones it understands.
/// </summary>
public sealed class AnalyticsParameters
{
    /// <summary>The case-insensitive name-to-value map.</summary>
    private Dictionary<string, string> Values { get; }

    /// <summary>Parses <c>name=value</c> assignments.</summary>
    /// <param name="assignments">The raw assignments, each <c>name=value</c>.</param>
    /// <exception cref="FormatException">An assignment is not <c>name=value</c> with a non-empty name.</exception>
    public AnalyticsParameters(IEnumerable<string> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach(string assignment in assignments)
        {
            int separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if(separator <= 0)
            {
                throw new FormatException($"Parameter '{assignment}' must be in name=value form.");
            }

            Values[assignment[..separator].Trim()] = assignment[(separator + 1)..].Trim();
        }
    }

    /// <summary>The integer value of <paramref name="name"/>, or <paramref name="fallback"/> when absent.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="fallback">The value to use when the parameter is absent.</param>
    /// <returns>The parsed integer.</returns>
    /// <exception cref="FormatException">The value is present but not an integer.</exception>
    public int GetInt(string name, int fallback)
    {
        return Values.TryGetValue(name, out string? value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : fallback;
    }

    /// <summary>The double value of <paramref name="name"/>, or <paramref name="fallback"/> when absent.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="fallback">The value to use when the parameter is absent.</param>
    /// <returns>The parsed double.</returns>
    /// <exception cref="FormatException">The value is present but not a double.</exception>
    public double GetDouble(string name, double fallback)
    {
        return Values.TryGetValue(name, out string? value)
            ? double.Parse(value, CultureInfo.InvariantCulture)
            : fallback;
    }

    /// <summary>The string value of <paramref name="name"/>, or <paramref name="fallback"/> when absent.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="fallback">The value to use when the parameter is absent.</param>
    /// <returns>The value.</returns>
    public string GetString(string name, string fallback)
    {
        return Values.TryGetValue(name, out string? value) ? value : fallback;
    }
}

/// <summary>The inputs a catalog descriptor reads to run one algorithm: the analytics view, the term dictionary for decoding ids, the parsed parameters, and a cancellation token.</summary>
/// <param name="Analytics">The analytics view over the loaded graph.</param>
/// <param name="Dictionary">The dictionary that decodes result term ids back to RDF terms.</param>
/// <param name="Parameters">The parsed algorithm parameters.</param>
/// <param name="CancellationToken">A token that aborts a long run.</param>
public sealed record AnalyticsContext(
    ColumnarGraphAnalytics Analytics,
    TermDictionary Dictionary,
    AnalyticsParameters Parameters,
    CancellationToken CancellationToken);

/// <summary>One registered graph-analytics algorithm: a name, a one-line summary, and the run that produces a SPARQL result set.</summary>
/// <param name="Name">The algorithm's invocation name.</param>
/// <param name="Summary">A one-line description, including any parameters it reads.</param>
/// <param name="Run">Runs the algorithm over an <see cref="AnalyticsContext"/>, producing a SELECT result set.</param>
public sealed record GraphAnalyticsDescriptor(string Name, string Summary, Func<AnalyticsContext, SparqlResultSet> Run);

/// <summary>
/// The registry of graph-analytics algorithms. Every surface (the CLI/MCP/HTTP operations and the in-process
/// analytics <c>SERVICE</c> transport) enumerates this catalog rather than hard-coding each algorithm, so a new
/// algorithm is added once — as a <see cref="GraphAnalyticsDescriptor"/> here — and appears everywhere. Each
/// descriptor reads its projection (predicates/direction parameters) and renders its result as a SPARQL SELECT
/// result set so the existing result serializers and the SERVICE join reuse it.
/// </summary>
public static class GraphAnalyticsCatalog
{
    /// <summary>The <c>xsd:integer</c> datatype node for count and index values.</summary>
    private static NamedNode XsdInteger { get; } = new(Utf8Strings.From(Vocabulary.Xsd.Namespace + "integer"));

    /// <summary>The <c>xsd:double</c> datatype node for rank and coefficient values.</summary>
    private static NamedNode XsdDouble { get; } = new(Utf8Strings.From(Vocabulary.Xsd.Namespace + "double"));

    /// <summary>The registered algorithms, in presentation order.</summary>
    private static IReadOnlyList<GraphAnalyticsDescriptor> Descriptors { get; } =
    [
        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.TriangleCount,
            "The number of undirected triangles. Parameters: predicates (comma-separated edge IRIs; default all), graph (union|default|<iri>). Columns: count.",
            static context => Scalar("count", Integer(context.Analytics.TriangleCount(ProjectionFor(context, GraphEdgeDirection.Undirected))))),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.GlobalClustering,
            "The global clustering coefficient (transitivity). Parameters: predicates, graph. Columns: coefficient.",
            static context => Scalar("coefficient", Double(context.Analytics.GlobalClusteringCoefficient(ProjectionFor(context, GraphEdgeDirection.Undirected))))),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.Degree,
            "Each node's degree. Parameters: predicates, direction (forward|reverse|undirected; default forward), graph. Columns: node, degree.",
            RunDegree),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.PageRank,
            "PageRank by power iteration. Parameters: predicates, direction (default forward), graph, damping (0.85), iterations (30), top (0=all). Columns: node, rank.",
            RunPageRank),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.ConnectedComponents,
            "Weakly connected components; each node with its component index. Parameters: predicates, graph. Columns: node, component.",
            RunConnectedComponents),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.StronglyConnectedComponents,
            "Strongly connected components (directed); each node with its component index. Parameters: predicates, direction (default forward), graph. Columns: node, component.",
            RunStronglyConnectedComponents),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.KCore,
            "The k-core number of each node (the degeneracy is the maximum). Parameters: predicates, graph. Columns: node, core.",
            RunCoreNumbers),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.ShortestPaths,
            "Unweighted single-source shortest-path hop distances. Parameters: source (required node IRI), predicates, direction (default forward), graph. Columns: node, distance.",
            RunShortestPaths),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.Cliques,
            "Fixed-size cliques. Parameters: size (3, >=2), connectivity (undirected|mutual), predicates, graph, limit (1000, 0=all). Columns: v0..v{size-1}.",
            RunCliques),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.Closeness,
            "Closeness centrality (unweighted; near one is central in its component). Parameters: predicates, graph, top (0=all). Columns: node, centrality.",
            RunCloseness),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.Betweenness,
            "Betweenness centrality (unweighted, raw shortest-path counts). Parameters: predicates, graph, top (0=all). Columns: node, centrality.",
            RunBetweenness),

        new GraphAnalyticsDescriptor(
            GraphAnalyticsServices.Eigenvector,
            "Eigenvector centrality by power iteration (L2-normalised). Parameters: predicates, graph, iterations (100), top (0=all). Columns: node, centrality.",
            RunEigenvector),
    ];

    /// <summary>The registered algorithms.</summary>
    public static IReadOnlyList<GraphAnalyticsDescriptor> All => Descriptors;

    /// <summary>Looks up an algorithm by name, case-insensitively.</summary>
    /// <param name="name">The algorithm name.</param>
    /// <param name="descriptor">Receives the descriptor on success.</param>
    /// <returns><see langword="true"/> when an algorithm of that name is registered.</returns>
    public static bool TryGet(string name, out GraphAnalyticsDescriptor descriptor)
    {
        foreach(GraphAnalyticsDescriptor candidate in Descriptors)
        {
            if(string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                descriptor = candidate;

                return true;
            }
        }

        descriptor = null!;

        return false;
    }

    /// <summary>Runs the out-degree algorithm, one row per node.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/degree result set.</returns>
    private static SparqlResultSet RunDegree(AnalyticsContext context)
    {
        List<SparqlSolution> rows = [];
        foreach((TermId node, long degree) in context.Analytics.Degrees(ProjectionFor(context, GraphEdgeDirection.Forward)))
        {
            rows.Add(Row(Bind("node", context.Dictionary.Resolve(node)), Bind("degree", Integer(degree))));
        }

        return SparqlResultSet.ForSelect(Head("node", "degree"), rows);
    }

    /// <summary>Runs PageRank, ranked descending, optionally truncated to the top rows.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/rank result set.</returns>
    private static SparqlResultSet RunPageRank(AnalyticsContext context)
    {
        double damping = context.Parameters.GetDouble("damping", 0.85);
        int iterations = context.Parameters.GetInt("iterations", 30);
        int top = context.Parameters.GetInt("top", 0);

        IEnumerable<KeyValuePair<TermId, double>> ordered = context.Analytics
            .PageRank(ProjectionFor(context, GraphEdgeDirection.Forward), damping, iterations)
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key.Encoded);
        if(top > 0)
        {
            ordered = ordered.Take(top);
        }

        List<SparqlSolution> rows = [];
        foreach(KeyValuePair<TermId, double> pair in ordered)
        {
            rows.Add(Row(Bind("node", context.Dictionary.Resolve(pair.Key)), Bind("rank", Double(pair.Value))));
        }

        return SparqlResultSet.ForSelect(Head("node", "rank"), rows);
    }

    /// <summary>Runs weakly-connected-components, one row per node carrying its component index.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/component result set.</returns>
    private static SparqlResultSet RunConnectedComponents(AnalyticsContext context)
    {
        IReadOnlyList<IReadOnlyList<TermId>> components = context.Analytics.ConnectedComponents(ProjectionFor(context, GraphEdgeDirection.Undirected));

        List<SparqlSolution> rows = [];
        for(int component = 0; component < components.Count; component++)
        {
            foreach(TermId node in components[component])
            {
                rows.Add(Row(Bind("node", context.Dictionary.Resolve(node)), Bind("component", Integer(component))));
            }
        }

        return SparqlResultSet.ForSelect(Head("node", "component"), rows);
    }

    /// <summary>Runs strongly-connected-components (directed), one row per node carrying its component index.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/component result set.</returns>
    private static SparqlResultSet RunStronglyConnectedComponents(AnalyticsContext context)
    {
        IReadOnlyList<IReadOnlyList<TermId>> components = context.Analytics.StronglyConnectedComponents(ProjectionFor(context, GraphEdgeDirection.Forward));

        List<SparqlSolution> rows = [];
        for(int component = 0; component < components.Count; component++)
        {
            foreach(TermId node in components[component])
            {
                rows.Add(Row(Bind("node", context.Dictionary.Resolve(node)), Bind("component", Integer(component))));
            }
        }

        return SparqlResultSet.ForSelect(Head("node", "component"), rows);
    }

    /// <summary>Runs the k-core decomposition, one row per node carrying its core number, ascending by node.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/core result set.</returns>
    private static SparqlResultSet RunCoreNumbers(AnalyticsContext context)
    {
        IEnumerable<KeyValuePair<TermId, long>> ordered = context.Analytics
            .CoreNumbers(ProjectionFor(context, GraphEdgeDirection.Undirected))
            .OrderBy(static pair => pair.Key.Encoded);

        List<SparqlSolution> rows = [];
        foreach(KeyValuePair<TermId, long> pair in ordered)
        {
            rows.Add(Row(Bind("node", context.Dictionary.Resolve(pair.Key)), Bind("core", Integer(pair.Value))));
        }

        return SparqlResultSet.ForSelect(Head("node", "core"), rows);
    }

    /// <summary>Runs unweighted single-source shortest paths from the required <c>source</c> node IRI, one row per reachable node, ascending by distance then node.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/distance result set.</returns>
    /// <exception cref="ArgumentException">The <c>source</c> parameter is absent or empty.</exception>
    private static SparqlResultSet RunShortestPaths(AnalyticsContext context)
    {
        string sourceIri = context.Parameters.GetString("source", string.Empty);
        if(string.IsNullOrWhiteSpace(sourceIri))
        {
            throw new ArgumentException("The shortest-paths algorithm requires a 'source' parameter naming the source node IRI.");
        }

        NamedNode sourceNode = new(Utf8Strings.From(sourceIri));

        List<SparqlSolution> rows = [];
        if(context.Dictionary.Contains(sourceNode))
        {
            IEnumerable<KeyValuePair<TermId, long>> ordered = context.Analytics
                .ShortestPathLengths(context.Dictionary.GetOrAdd(sourceNode), ProjectionFor(context, GraphEdgeDirection.Forward))
                .OrderBy(static pair => pair.Value)
                .ThenBy(static pair => pair.Key.Encoded);
            foreach(KeyValuePair<TermId, long> pair in ordered)
            {
                rows.Add(Row(Bind("node", context.Dictionary.Resolve(pair.Key)), Bind("distance", Integer(pair.Value))));
            }
        }
        else
        {
            //A source absent from the graph reaches only itself; surface the single zero-distance row over it.
            rows.Add(Row(Bind("node", sourceNode), Bind("distance", Integer(0))));
        }

        return SparqlResultSet.ForSelect(Head("node", "distance"), rows);
    }

    /// <summary>Runs fixed-size clique enumeration, one row per clique, bounded by the limit parameter.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The clique result set, columns v0..v{size-1}.</returns>
    private static SparqlResultSet RunCliques(AnalyticsContext context)
    {
        int size = context.Parameters.GetInt("size", 3);
        CliqueConnectivity connectivity = ParseConnectivity(context.Parameters.GetString("connectivity", "undirected"));
        int limit = context.Parameters.GetInt("limit", 1000);

        IReadOnlyList<Utf8String> head = Head([.. Enumerable.Range(0, size).Select(static i => "v" + i.ToString(CultureInfo.InvariantCulture))]);

        List<SparqlSolution> rows = [];
        foreach(IReadOnlyList<TermId> clique in context.Analytics.Cliques(ProjectionFor(context, GraphEdgeDirection.Undirected), size, connectivity, context.CancellationToken))
        {
            SparqlBinding[] bindings = new SparqlBinding[clique.Count];
            for(int i = 0; i < clique.Count; i++)
            {
                bindings[i] = Bind("v" + i.ToString(CultureInfo.InvariantCulture), context.Dictionary.Resolve(clique[i]));
            }

            rows.Add(new SparqlSolution(bindings));
            if(limit > 0 && rows.Count >= limit)
            {
                break;
            }
        }

        return SparqlResultSet.ForSelect(head, rows);
    }

    /// <summary>Runs closeness centrality, one row per node, ranked descending and optionally truncated to the top rows.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/centrality result set.</returns>
    private static SparqlResultSet RunCloseness(AnalyticsContext context)
    {
        return CentralityRows(context, context.Analytics.ClosenessCentrality(ProjectionFor(context, GraphEdgeDirection.Undirected), context.CancellationToken));
    }

    /// <summary>Runs betweenness centrality, one row per node, ranked descending and optionally truncated to the top rows.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/centrality result set.</returns>
    private static SparqlResultSet RunBetweenness(AnalyticsContext context)
    {
        return CentralityRows(context, context.Analytics.BetweennessCentrality(ProjectionFor(context, GraphEdgeDirection.Undirected), context.CancellationToken));
    }

    /// <summary>Runs eigenvector centrality, one row per node, ranked descending and optionally truncated to the top rows.</summary>
    /// <param name="context">The analytics context.</param>
    /// <returns>The node/centrality result set.</returns>
    private static SparqlResultSet RunEigenvector(AnalyticsContext context)
    {
        int iterations = context.Parameters.GetInt("iterations", 100);

        return CentralityRows(context, context.Analytics.EigenvectorCentrality(ProjectionFor(context, GraphEdgeDirection.Undirected), iterations));
    }

    /// <summary>Renders a per-node centrality map as node/centrality rows, descending by value then by node, optionally truncated to the top rows.</summary>
    /// <param name="context">The analytics context carrying the parameters and the dictionary.</param>
    /// <param name="centrality">The per-node centrality.</param>
    /// <returns>The node/centrality result set.</returns>
    private static SparqlResultSet CentralityRows(AnalyticsContext context, IReadOnlyDictionary<TermId, double> centrality)
    {
        int top = context.Parameters.GetInt("top", 0);

        IEnumerable<KeyValuePair<TermId, double>> ordered = centrality
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key.Encoded);
        if(top > 0)
        {
            ordered = ordered.Take(top);
        }

        List<SparqlSolution> rows = [];
        foreach(KeyValuePair<TermId, double> pair in ordered)
        {
            rows.Add(Row(Bind("node", context.Dictionary.Resolve(pair.Key)), Bind("centrality", Double(pair.Value))));
        }

        return SparqlResultSet.ForSelect(Head("node", "centrality"), rows);
    }

    /// <summary>A single-column, single-row result set carrying one scalar value.</summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The scalar value as an RDF term.</param>
    /// <returns>The result set.</returns>
    private static SparqlResultSet Scalar(string column, RdfTerm value)
    {
        return SparqlResultSet.ForSelect(Head(column), [Row(Bind(column, value))]);
    }

    /// <summary>Maps a connectivity token to its enum value.</summary>
    /// <param name="token">The token (<c>undirected</c> or <c>mutual</c>).</param>
    /// <returns>The connectivity.</returns>
    /// <exception cref="ArgumentException">The token is neither.</exception>
    private static CliqueConnectivity ParseConnectivity(string token)
    {
        return token switch
        {
            _ when string.Equals(token, "undirected", StringComparison.OrdinalIgnoreCase) => CliqueConnectivity.Undirected,
            _ when string.Equals(token, "mutual", StringComparison.OrdinalIgnoreCase) => CliqueConnectivity.Mutual,
            _ => throw new ArgumentException($"Unknown connectivity '{token}'; use undirected or mutual."),
        };
    }

    /// <summary>
    /// Builds the graph projection an algorithm reads from the <c>predicates</c> and <c>direction</c> parameters:
    /// the predicate filter (a comma-separated list of edge IRIs, or every predicate when absent) and the edge
    /// direction (defaulting to <paramref name="defaultDirection"/>; ignored by the undirected metrics).
    /// </summary>
    /// <param name="context">The analytics context carrying the parameters and the dictionary.</param>
    /// <param name="defaultDirection">The direction to use when the <c>direction</c> parameter is absent.</param>
    /// <returns>The projection.</returns>
    /// <exception cref="ArgumentException">The <c>direction</c> parameter is not a defined direction.</exception>
    private static GraphProjection ProjectionFor(AnalyticsContext context, GraphEdgeDirection defaultDirection)
    {
        GraphEdgeDirection direction = ParseDirection(context.Parameters.GetString("direction", DirectionToken(defaultDirection)));
        string predicates = context.Parameters.GetString("predicates", string.Empty);
        if(string.IsNullOrWhiteSpace(predicates))
        {
            return GraphProjection.AllPredicates(direction);
        }

        List<TermId> ids = [];
        foreach(string iri in predicates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ids.Add(context.Dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri))));
        }

        return GraphProjection.ForPredicates(ids, direction);
    }

    /// <summary>Maps a direction token to its enum value.</summary>
    /// <param name="token">The token (<c>forward</c>, <c>reverse</c>, or <c>undirected</c>).</param>
    /// <returns>The direction.</returns>
    /// <exception cref="ArgumentException">The token is none of those.</exception>
    private static GraphEdgeDirection ParseDirection(string token)
    {
        return token switch
        {
            _ when string.Equals(token, "forward", StringComparison.OrdinalIgnoreCase) => GraphEdgeDirection.Forward,
            _ when string.Equals(token, "reverse", StringComparison.OrdinalIgnoreCase) => GraphEdgeDirection.Reverse,
            _ when string.Equals(token, "undirected", StringComparison.OrdinalIgnoreCase) => GraphEdgeDirection.Undirected,
            _ => throw new ArgumentException($"Unknown direction '{token}'; use forward, reverse, or undirected."),
        };
    }

    /// <summary>The canonical token for a direction, used as the default when the <c>direction</c> parameter is absent.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The token.</returns>
    private static string DirectionToken(GraphEdgeDirection direction)
    {
        return direction switch
        {
            GraphEdgeDirection.Forward => "forward",
            GraphEdgeDirection.Reverse => "reverse",
            _ => "undirected",
        };
    }

    /// <summary>The head variables for the given column names, in order.</summary>
    /// <param name="names">The column names.</param>
    /// <returns>The head variables.</returns>
    private static IReadOnlyList<Utf8String> Head(params string[] names)
    {
        return [.. names.Select(Utf8Strings.From)];
    }

    /// <summary>A binding of a named column to a term.</summary>
    /// <param name="name">The column (variable) name.</param>
    /// <param name="value">The bound term.</param>
    /// <returns>The binding.</returns>
    private static SparqlBinding Bind(string name, RdfTerm value)
    {
        return new SparqlBinding(new SparqlVariable(Utf8Strings.From(name)), value);
    }

    /// <summary>A solution row over the given bindings.</summary>
    /// <param name="bindings">The row's bindings.</param>
    /// <returns>The solution.</returns>
    private static SparqlSolution Row(params SparqlBinding[] bindings)
    {
        return new SparqlSolution(bindings);
    }

    /// <summary>An <c>xsd:integer</c> literal of a value.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal term.</returns>
    private static Literal Integer(long value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), XsdInteger);
    }

    /// <summary>An <c>xsd:double</c> literal of a value, in round-trippable form.</summary>
    /// <param name="value">The double value.</param>
    /// <returns>The literal term.</returns>
    private static Literal Double(double value)
    {
        return new Literal(Utf8Strings.From(value.ToString("R", CultureInfo.InvariantCulture)), XsdDouble);
    }
}
