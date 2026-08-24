using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;
using ModelContextProtocol.Server;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The Model Context Protocol tool surface, exposed to MCP clients when the program runs with
/// <c>-mcp</c> (stdio transport). Each tool is a thin call into <see cref="VeritasOperations"/>,
/// the same operations the command-line and HTTP surfaces use.
/// </summary>
[McpServerToolType]
internal sealed class VeritasMcpServer
{
    /// <summary>Runs a SPARQL <c>SELECT</c>/<c>ASK</c> query over RDF data files and returns the results.</summary>
    /// <param name="query">The SPARQL query text.</param>
    /// <param name="dataPaths">The RDF data file paths forming the dataset.</param>
    /// <param name="baseIri">The base IRI for resolving relative references; pass an empty string when the query uses only absolute IRIs.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The CSV-rendered results, or an error description.</returns>
    [McpServerTool(Name = "sparql_query"), Description("Run a SPARQL query (SELECT, ASK, CONSTRUCT, or DESCRIBE) over one or more RDF data files (.ttl/.nt/.trig/.nq); SELECT/ASK results return as CSV and CONSTRUCT/DESCRIBE graphs as N-Triples.")]
    public static async Task<string> SparqlQueryAsync(
        [Description("The SPARQL query text.")] string query,
        [Description("Paths to the RDF data files (.ttl/.nt/.trig/.nq) forming the dataset.")] string[] dataPaths,
        [Description("Base IRI for relative references; empty when the query uses only absolute IRIs.")] string baseIri,
        CancellationToken cancellationToken)
    {
        OperationResult result = await VeritasOperations.RunQueryTextAsync(
            query,
            dataPaths,
            baseIri,
            SparqlDelimitedResultsFormat.Csv,
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded ? result.Output : result.ErrorMessage!;
    }

    /// <summary>Runs a graph-analytics algorithm over RDF data files and returns the result as CSV.</summary>
    /// <param name="algorithm">The algorithm name (use <see cref="GraphAnalyticsListAsync"/> to discover them).</param>
    /// <param name="dataPaths">The RDF data file paths forming the graph.</param>
    /// <param name="parameters">The algorithm parameters as <c>name=value</c> strings.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The CSV-rendered result, or an error description.</returns>
    [McpServerTool(Name = "graph_analytics"), Description("Run a graph-analytics algorithm (triangle-count, global-clustering, degree, pagerank, connected-components, cliques) over one or more RDF data files and return the result as CSV. Use graph_analytics_list to discover algorithms and their parameters.")]
    public static async Task<string> GraphAnalyticsAsync(
        [Description("The algorithm name, for example pagerank or cliques.")] string algorithm,
        [Description("Paths to the RDF data files (.ttl/.nt/.trig/.nq/.rdf/.owl) forming the graph.")] string[] dataPaths,
        [Description("Algorithm parameters as name=value strings, for example size=4, connectivity=mutual, damping=0.85.")] string[] parameters,
        CancellationToken cancellationToken)
    {
        OperationResult result = await VeritasOperations.RunGraphAnalyticsAsync(
            algorithm,
            dataPaths,
            parameters ?? [],
            SparqlDelimitedResultsFormat.Csv,
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded ? result.Output : result.ErrorMessage!;
    }

    /// <summary>Lists the available graph-analytics algorithms and their parameters.</summary>
    /// <param name="cancellationToken">A token that aborts the call.</param>
    /// <returns>The algorithm list, one per line.</returns>
    [McpServerTool(Name = "graph_analytics_list"), Description("List the available graph-analytics algorithms and the parameters each accepts.")]
    public static Task<string> GraphAnalyticsListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(VeritasOperations.DescribeAnalytics().Output);
    }
}
