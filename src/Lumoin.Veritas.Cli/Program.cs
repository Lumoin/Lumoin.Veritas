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
        builder.Logging.AddConsole(static options => options.LogToStandardErrorThreshold = LogLevel.Trace);

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

        queryCommand.SetAction(new QueryCommandHandler(queryFileArgument, queryDataOption, formatOption).InvokeAsync);

        Option<string[]> serveDataOption = new("--data", "-d") { Description = "RDF data file (.ttl/.nt/.trig/.nq) to serve; repeat for several.", AllowMultipleArgumentsPerToken = true };
        Option<int> portOption = new("--port", "-p") { Description = "Loopback port to listen on (default 3030)." };
        Option<bool> serveUiOption = new("--ui") { Description = "Also serve the in-browser Studio UI (from --ui-dir) at the root and open a browser; its tab queries this engine." };
        Option<string> serveUiDirOption = new("--ui-dir") { Description = "Directory of the built Studio web app (its 'dist') to serve with --ui; defaults to a 'ui' directory beside the executable." };
        Option<bool> serveNoOpenOption = new("--no-open") { Description = "With --ui, do not open a browser automatically." };
        Option<string[]> serveCorsOriginOption = new("--cors-origin") { Description = "Origin allowed cross-origin (CORS) access to the endpoints, e.g. a remotely hosted Studio page; repeat for several, or pass * to allow any origin. Absent, no cross-origin access is granted.", AllowMultipleArgumentsPerToken = true };

        Command serveCommand = new("serve", "Run a SPARQL 1.1 Protocol HTTP endpoint over RDF data files, optionally hosting the Studio UI (--ui).")
        {
            serveDataOption,
            portOption,
            serveUiOption,
            serveUiDirOption,
            serveNoOpenOption,
            serveCorsOriginOption
        };
        root.Subcommands.Add(serveCommand);

        serveCommand.SetAction(new ServeCommandHandler(serveDataOption, portOption, serveUiOption, serveUiDirOption, serveNoOpenOption, serveCorsOriginOption).InvokeAsync);

        Option<string> algorithmOption = new("--algo", "-a") { Description = "Graph-analytics algorithm name (run with --list to see them)." };
        Option<string[]> analyticsDataOption = new("--data", "-d") { Description = "RDF data file (.ttl/.nt/.trig/.nq/.rdf/.owl) forming the graph; repeat for several.", AllowMultipleArgumentsPerToken = true };
        Option<string[]> parameterOption = new("--param") { Description = "Algorithm parameter as name=value (for example size=4, connectivity=mutual, damping=0.85); repeat.", AllowMultipleArgumentsPerToken = true };
        Option<string> analyticsFormatOption = new("--format", "-f") { Description = "Result format: csv (default) or tsv." };
        Option<bool> listOption = new("--list") { Description = "List the available graph-analytics algorithms." };

        Command analyticsCommand = new("analytics", "Run a graph-analytics algorithm (degree, triangles, clustering, PageRank, components, cliques) over RDF data files.")
        {
            algorithmOption,
            analyticsDataOption,
            parameterOption,
            analyticsFormatOption,
            listOption
        };
        root.Subcommands.Add(analyticsCommand);

        analyticsCommand.SetAction(new AnalyticsCommandHandler(algorithmOption, analyticsDataOption, parameterOption, analyticsFormatOption, listOption).InvokeAsync);

        Option<string> storeOption = new("--store") { Description = "Store directory the replica opens (created if missing).", Required = true };
        Option<string[]> replicateDataOption = new("--data", "-d") { Description = "RDF data file seeding an EMPTY store as the lineage seed; repeat for several.", AllowMultipleArgumentsPerToken = true };
        Option<int?> listenOption = new("--listen") { Description = "Loopback TCP port serving replication to peers; 0 picks an ephemeral port, printed as 'listening <port>'." };
        Option<string> replicatePeerOption = new("--peer") { Description = "Peer replication endpoint as host:port; also bindable later with the 'peer' verb." };
        Option<int?> reconcileIntervalOption = new("--reconcile-interval") { Description = "Seconds between automatic reconcile pulls from the peer; absent pulls only on the 'reconcile' verb." };
        Option<bool> selfHealOption = new("--self-heal") { Description = "Run the background storage self-heal loop, with both peer-repair seams over the bound peer." };
        Option<int?> healIntervalOption = new("--heal-interval") { Description = "Seconds between self-heal rounds (a fixed cadence, no jitter); absent uses the reliability-model cadence." };
        Option<string> identityDirOption = new("--identity-dir") { Description = "Directory the HOST's replica identity is persisted in (minted on first use); absent uses the per-user configuration location. Distinct replicas on one machine each need their own directory." };
        Option<bool> baselineOption = new("--baseline") { Description = "Perform the explicit causality baseline step on a resumed store that is not already remove-aware; the outcome is reported by name on the identity line." };
        Option<string[]> metadataFounderOption = new("--metadata-founder") { Description = "Founding member of the consensus metadata chain as <64hex axis>:<32hex store>, which is what the 'axis ... store ...' startup line prints; repeat for every founder. Its presence turns the plane on, and this host's own axis must be among them. Both halves are named because the membership admits the store answering for a replica, so every founder's store must exist and have printed its incarnation before the list can be written.", AllowMultipleArgumentsPerToken = true };
        Option<string[]> metadataRouteOption = new("--metadata-route") { Description = "Metadata endpoint of one founder as <64hex>=<host:port>; repeat for several, or bind later with the 'metadata-route' verb. A founder with no route is treated as an unreachable member.", AllowMultipleArgumentsPerToken = true };
        Option<string> metadataStoreOption = new("--metadata-store") { Description = "Directory the consensus host state and this host's confirmed facts are persisted in; absent uses a 'metadata' directory beside the replica identity, never the data store directory a deployment copies per replica." };
        Option<int?> metadataAttemptsOption = new("--metadata-attempts") { Description = "Consensus attempts one coordination obligation may spend before it answers undecided; absent uses the command's own budget." };

        Command replicateCommand = new("replicate", "Run a store-backed replica: serve replication on loopback, reconcile with a peer (remove-aware by default), self-heal over the wire, and optionally coordinate identity and lineage on a consensus metadata plane.")
        {
            storeOption,
            replicateDataOption,
            listenOption,
            replicatePeerOption,
            reconcileIntervalOption,
            selfHealOption,
            healIntervalOption,
            identityDirOption,
            baselineOption,
            metadataFounderOption,
            metadataRouteOption,
            metadataStoreOption,
            metadataAttemptsOption
        };
        root.Subcommands.Add(replicateCommand);

        replicateCommand.SetAction(new ReplicateCommandHandler(storeOption, replicateDataOption, listenOption, replicatePeerOption, reconcileIntervalOption, selfHealOption, healIntervalOption, identityDirOption, baselineOption, metadataFounderOption, metadataRouteOption, metadataStoreOption, metadataAttemptsOption).InvokeAsync);

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

    /// <summary>
    /// Runs the <c>query</c> command, carrying its argument and options as explicit state so the action passed
    /// to <see cref="Command.SetAction(System.Func{ParseResult, CancellationToken, Task{int}})"/> is a bound
    /// method group rather than a lambda closing over the enclosing symbols.
    /// </summary>
    /// <param name="queryFileArgument">The query-file argument.</param>
    /// <param name="queryDataOption">The data-files option.</param>
    /// <param name="formatOption">The result-format option.</param>
    private sealed class QueryCommandHandler(Argument<string> queryFileArgument, Option<string[]> queryDataOption, Option<string> formatOption)
    {
        /// <summary>The query-file argument.</summary>
        private Argument<string> QueryFileArgument { get; } = queryFileArgument;

        /// <summary>The data-files option.</summary>
        private Option<string[]> QueryDataOption { get; } = queryDataOption;

        /// <summary>The result-format option.</summary>
        private Option<string> FormatOption { get; } = formatOption;

        /// <summary>Runs the query file and emits its result.</summary>
        /// <param name="parseResult">The parsed command line.</param>
        /// <param name="cancellationToken">A token that cancels the run.</param>
        /// <returns>The process exit code.</returns>
        public async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken)
        {
            string queryFile = parseResult.GetValue(QueryFileArgument)!;
            string[] dataPaths = parseResult.GetValue(QueryDataOption) ?? [];
            SparqlDelimitedResultsFormat format = ResolveFormat(parseResult.GetValue(FormatOption));

            OperationResult result = await VeritasOperations.RunQueryFileAsync(queryFile, dataPaths, format, cancellationToken).ConfigureAwait(false);

            return await EmitAsync(result).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the <c>serve</c> command, carrying its options as explicit state so the action passed to
    /// <see cref="Command.SetAction(System.Func{ParseResult, CancellationToken, Task{int}})"/> is a bound
    /// method group rather than a lambda closing over the enclosing symbols.
    /// </summary>
    /// <param name="serveDataOption">The data-files option.</param>
    /// <param name="portOption">The listen-port option.</param>
    /// <param name="uiOption">The flag that also serves the Studio UI.</param>
    /// <param name="uiDirOption">The Studio UI directory option.</param>
    /// <param name="noOpenOption">The flag that suppresses opening a browser with <c>--ui</c>.</param>
    /// <param name="corsOriginOption">The allowed cross-origin (CORS) origins option.</param>
    private sealed class ServeCommandHandler(
        Option<string[]> serveDataOption,
        Option<int> portOption,
        Option<bool> uiOption,
        Option<string> uiDirOption,
        Option<bool> noOpenOption,
        Option<string[]> corsOriginOption)
    {
        /// <summary>The data-files option.</summary>
        private Option<string[]> ServeDataOption { get; } = serveDataOption;

        /// <summary>The listen-port option.</summary>
        private Option<int> PortOption { get; } = portOption;

        /// <summary>The flag that also serves the Studio UI and opens a browser.</summary>
        private Option<bool> UiOption { get; } = uiOption;

        /// <summary>The Studio UI directory option.</summary>
        private Option<string> UiDirOption { get; } = uiDirOption;

        /// <summary>The flag that suppresses opening a browser with --ui.</summary>
        private Option<bool> NoOpenOption { get; } = noOpenOption;

        /// <summary>The allowed cross-origin (CORS) origins option; empty grants no cross-origin access.</summary>
        private Option<string[]> CorsOriginOption { get; } = corsOriginOption;

        /// <summary>Starts the SPARQL HTTP endpoint over the served data files, optionally hosting the Studio UI.</summary>
        /// <param name="parseResult">The parsed command line.</param>
        /// <param name="cancellationToken">A token that stops the server.</param>
        /// <returns>The process exit code.</returns>
        public async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken)
        {
            string[] dataPaths = parseResult.GetValue(ServeDataOption) ?? [];
            int port = parseResult.GetValue(PortOption);

            bool ui = parseResult.GetValue(UiOption);
            string? uiDirectory = ui
                ? parseResult.GetValue(UiDirOption) is { Length: > 0 } directory ? directory : System.IO.Path.Combine(AppContext.BaseDirectory, "ui")
                : null;
            bool openBrowser = ui && !parseResult.GetValue(NoOpenOption);
            string[] corsOrigins = parseResult.GetValue(CorsOriginOption) ?? [];

            return await VeritasSparqlServer.RunAsync(dataPaths, port == 0 ? 3030 : port, uiDirectory, openBrowser, corsOrigins, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the <c>replicate</c> command, carrying its options as explicit state so the action passed to
    /// <see cref="Command.SetAction(System.Func{ParseResult, CancellationToken, Task{int}})"/> is a bound method
    /// group rather than a lambda closing over the enclosing symbols.
    /// </summary>
    /// <param name="storeOption">The store-directory option.</param>
    /// <param name="dataOption">The seed-data option.</param>
    /// <param name="listenOption">The loopback listen-port option.</param>
    /// <param name="peerOption">The peer-endpoint option.</param>
    /// <param name="reconcileIntervalOption">The automatic reconcile interval option.</param>
    /// <param name="selfHealOption">The self-heal flag.</param>
    /// <param name="healIntervalOption">The fixed heal-interval option.</param>
    /// <param name="identityDirOption">The host replica-identity directory option.</param>
    /// <param name="baselineOption">The explicit causality-baseline flag.</param>
    /// <param name="metadataFounderOption">The repeated metadata-chain founder option.</param>
    /// <param name="metadataRouteOption">The repeated metadata endpoint-map option.</param>
    /// <param name="metadataStoreOption">The metadata node-store directory option.</param>
    /// <param name="metadataAttemptsOption">The coordination attempt-budget option.</param>
    private sealed class ReplicateCommandHandler(
        Option<string> storeOption,
        Option<string[]> dataOption,
        Option<int?> listenOption,
        Option<string> peerOption,
        Option<int?> reconcileIntervalOption,
        Option<bool> selfHealOption,
        Option<int?> healIntervalOption,
        Option<string> identityDirOption,
        Option<bool> baselineOption,
        Option<string[]> metadataFounderOption,
        Option<string[]> metadataRouteOption,
        Option<string> metadataStoreOption,
        Option<int?> metadataAttemptsOption)
    {
        /// <summary>The store-directory option.</summary>
        private Option<string> StoreOption { get; } = storeOption;

        /// <summary>The seed-data option.</summary>
        private Option<string[]> DataOption { get; } = dataOption;

        /// <summary>The loopback listen-port option.</summary>
        private Option<int?> ListenOption { get; } = listenOption;

        /// <summary>The peer-endpoint option.</summary>
        private Option<string> PeerOption { get; } = peerOption;

        /// <summary>The automatic reconcile interval option.</summary>
        private Option<int?> ReconcileIntervalOption { get; } = reconcileIntervalOption;

        /// <summary>The self-heal flag.</summary>
        private Option<bool> SelfHealOption { get; } = selfHealOption;

        /// <summary>The fixed heal-interval option.</summary>
        private Option<int?> HealIntervalOption { get; } = healIntervalOption;

        /// <summary>The host replica-identity directory option.</summary>
        private Option<string> IdentityDirOption { get; } = identityDirOption;

        /// <summary>The explicit causality-baseline flag.</summary>
        private Option<bool> BaselineOption { get; } = baselineOption;

        /// <summary>The repeated metadata-chain founder option, whose presence turns the consensus metadata plane on.</summary>
        private Option<string[]> MetadataFounderOption { get; } = metadataFounderOption;

        /// <summary>The repeated metadata endpoint-map option.</summary>
        private Option<string[]> MetadataRouteOption { get; } = metadataRouteOption;

        /// <summary>The metadata node-store directory option.</summary>
        private Option<string> MetadataStoreOption { get; } = metadataStoreOption;

        /// <summary>The coordination attempt-budget option.</summary>
        private Option<int?> MetadataAttemptsOption { get; } = metadataAttemptsOption;

        /// <summary>Runs the replica host until it quits.</summary>
        /// <param name="parseResult">The parsed command line.</param>
        /// <param name="cancellationToken">A token that stops the host.</param>
        /// <returns>The process exit code.</returns>
        public async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken)
        {
            ReplicateSettings settings = new(
                parseResult.GetValue(StoreOption)!,
                parseResult.GetValue(DataOption) ?? [],
                parseResult.GetValue(ListenOption),
                parseResult.GetValue(PeerOption),
                parseResult.GetValue(ReconcileIntervalOption),
                parseResult.GetValue(SelfHealOption),
                parseResult.GetValue(HealIntervalOption),
                parseResult.GetValue(IdentityDirOption),
                parseResult.GetValue(BaselineOption),
                parseResult.GetValue(MetadataFounderOption) ?? [],
                parseResult.GetValue(MetadataRouteOption) ?? [],
                parseResult.GetValue(MetadataStoreOption),
                parseResult.GetValue(MetadataAttemptsOption));

            using ReplicateHost host = new(settings);

            return await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the <c>analytics</c> command, carrying its options as explicit state so the action passed to
    /// <see cref="Command.SetAction(System.Func{ParseResult, CancellationToken, Task{int}})"/> is a bound method
    /// group rather than a lambda closing over the enclosing symbols.
    /// </summary>
    /// <param name="algorithmOption">The algorithm-name option.</param>
    /// <param name="dataOption">The data-files option.</param>
    /// <param name="parameterOption">The repeated algorithm-parameter option.</param>
    /// <param name="formatOption">The result-format option.</param>
    /// <param name="listOption">The list-algorithms flag.</param>
    private sealed class AnalyticsCommandHandler(
        Option<string> algorithmOption,
        Option<string[]> dataOption,
        Option<string[]> parameterOption,
        Option<string> formatOption,
        Option<bool> listOption)
    {
        /// <summary>The algorithm-name option.</summary>
        private Option<string> AlgorithmOption { get; } = algorithmOption;

        /// <summary>The data-files option.</summary>
        private Option<string[]> DataOption { get; } = dataOption;

        /// <summary>The repeated algorithm-parameter option.</summary>
        private Option<string[]> ParameterOption { get; } = parameterOption;

        /// <summary>The result-format option.</summary>
        private Option<string> FormatOption { get; } = formatOption;

        /// <summary>The list-algorithms flag.</summary>
        private Option<bool> ListOption { get; } = listOption;

        /// <summary>Lists the algorithms, or runs the named one and emits its result.</summary>
        /// <param name="parseResult">The parsed command line.</param>
        /// <param name="cancellationToken">A token that cancels the run.</param>
        /// <returns>The process exit code.</returns>
        public async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken)
        {
            if(parseResult.GetValue(ListOption))
            {
                return await EmitAsync(VeritasOperations.DescribeAnalytics()).ConfigureAwait(false);
            }

            string? algorithm = parseResult.GetValue(AlgorithmOption);
            if(string.IsNullOrEmpty(algorithm))
            {
                return await EmitAsync(OperationResult.Failed("Specify an algorithm with --algo <name>, or --list to see the available algorithms.")).ConfigureAwait(false);
            }

            string[] dataPaths = parseResult.GetValue(DataOption) ?? [];
            string[] parameters = parseResult.GetValue(ParameterOption) ?? [];
            SparqlDelimitedResultsFormat format = ResolveFormat(parseResult.GetValue(FormatOption));

            OperationResult result = await VeritasOperations.RunGraphAnalyticsAsync(algorithm, dataPaths, parameters, format, cancellationToken).ConfigureAwait(false);

            return await EmitAsync(result).ConfigureAwait(false);
        }
    }
}
