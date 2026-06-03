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
    [McpServerTool(Name = "sparql_query"), Description("Run a SPARQL SELECT or ASK query over one or more RDF data files (.ttl/.nt/.trig/.nq) and return the results as CSV.")]
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
}
