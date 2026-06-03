using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The Veritas application entry point. One executable hosts three peer surfaces over the shared
/// <see cref="VeritasOperations"/>: the command-line (default), the Model Context Protocol server
/// (<c>-mcp</c>, stdio), and the SPARQL HTTP endpoint (the <c>serve</c> command). The surfaces do
/// not layer on each other — each is a thin transport over the same engine operations.
/// </summary>
internal static class Program
{
    /// <summary>Dispatches to the MCP stdio server (<c>-mcp</c>) or the command-line parser.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if(args.Length == 1 && string.Equals(args[0], "-mcp", StringComparison.Ordinal))
        {
            return await RunMcpServerAsync(args).ConfigureAwait(false);
        }

        return await RunCliAsync(args).ConfigureAwait(false);
    }

    /// <summary>Runs the Model Context Protocol server over stdio, exposing <see cref="VeritasMcpServer"/>'s tools.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The process exit code.</returns>
    private static async Task<int> RunMcpServerAsync(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<VeritasMcpServer>();

        //MCP speaks JSON-RPC on stdout, so all logging must go to stderr to keep the protocol stream clean.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        await builder.Build().RunAsync().ConfigureAwait(false);

        return 0;
    }

    /// <summary>Builds the command tree (<c>query</c>, <c>serve</c>) and invokes it.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The process exit code.</returns>
    private static async Task<int> RunCliAsync(string[] args)
    {
        RootCommand root = new("Veritas — RDF / SPARQL / SHACL command-line, MCP server (-mcp), and SPARQL HTTP endpoint.");

        Argument<string> queryFileArgument = new("query-file") { Description = "Path to a file containing the SPARQL query." };
        Option<string[]> queryDataOption = new("--data", "-d") { Description = "RDF data file (.ttl/.nt/.trig/.nq) forming the dataset; repeat for several.", AllowMultipleArgumentsPerToken = true };
        Option<string> formatOption = new("--format", "-f") { Description = "Result format: csv (default) or tsv." };

        Command queryCommand = new("query", "Run a SPARQL SELECT/ASK query over RDF data files.")
        {
            queryFileArgument,
            queryDataOption,
            formatOption
        };
        root.Subcommands.Add(queryCommand);

        queryCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            string queryFile = parseResult.GetValue(queryFileArgument)!;
            string[] dataPaths = parseResult.GetValue(queryDataOption) ?? [];
            SparqlDelimitedResultsFormat format = ResolveFormat(parseResult.GetValue(formatOption));

            OperationResult result = await VeritasOperations.RunQueryFileAsync(queryFile, dataPaths, format, cancellationToken).ConfigureAwait(false);

            return await EmitAsync(result).ConfigureAwait(false);
        });

        Option<string[]> serveDataOption = new("--data", "-d") { Description = "RDF data file (.ttl/.nt/.trig/.nq) to serve; repeat for several.", AllowMultipleArgumentsPerToken = true };
        Option<int> portOption = new("--port", "-p") { Description = "Loopback port to listen on (default 3030)." };

        Command serveCommand = new("serve", "Run a SPARQL 1.1 Protocol HTTP endpoint over RDF data files.")
        {
            serveDataOption,
            portOption
        };
        root.Subcommands.Add(serveCommand);

        serveCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            string[] dataPaths = parseResult.GetValue(serveDataOption) ?? [];
            int port = parseResult.GetValue(portOption);

            return await VeritasSparqlServer.RunAsync(dataPaths, port == 0 ? 3030 : port, cancellationToken).ConfigureAwait(false);
        });

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>Maps the <c>--format</c> option value to a delimited format, defaulting to CSV.</summary>
    /// <param name="format">The option value (or <see langword="null"/>).</param>
    /// <returns>The resolved format.</returns>
    private static SparqlDelimitedResultsFormat ResolveFormat(string? format)
    {
        return format switch
        {
            "tsv" => SparqlDelimitedResultsFormat.Tsv,
            _ => SparqlDelimitedResultsFormat.Csv
        };
    }

    /// <summary>Writes a successful result to standard output (or the error to standard error) and returns the exit code.</summary>
    /// <param name="result">The operation result.</param>
    /// <returns>0 on success, 1 on failure.</returns>
    private static async Task<int> EmitAsync(OperationResult result)
    {
        if(result.Succeeded)
        {
            await Console.Out.WriteLineAsync(result.Output).ConfigureAwait(false);

            return 0;
        }

        await Console.Error.WriteLineAsync(result.ErrorMessage).ConfigureAwait(false);

        return 1;
    }
}
