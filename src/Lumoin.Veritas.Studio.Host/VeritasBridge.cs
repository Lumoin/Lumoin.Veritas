using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lumoin.Veritas.Studio.Host;

/// <summary>
/// Bridges the page's <c>window.veritas</c> requests to the in-process engine: it runs queries and
/// forwards trace events over the <see cref="StudioWindow"/> message channel. The engine is supplied as
/// a delegate returning W3C SPARQL-results-JSON, so this type depends on a function, not the engine
/// assembly — the same shape the CLI server's planned SPARQL-results-JSON surface exposes.
/// </summary>
public sealed class VeritasBridge
{
    /// <summary>The window whose message channel carries requests and replies.</summary>
    private StudioWindow Window { get; }

    /// <summary>Runs a query and returns the SPARQL-results-JSON document; the in-process engine adapter.</summary>
    private Func<string, CancellationToken, Task<string>> RunSparqlJson { get; }

    /// <summary>Wires the bridge onto the window's inbound messages.</summary>
    /// <param name="window">The native window.</param>
    /// <param name="runSparqlJson">The engine adapter producing SPARQL-results-JSON.</param>
    public VeritasBridge(StudioWindow window, Func<string, CancellationToken, Task<string>> runSparqlJson)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(runSparqlJson);

        Window = window;
        RunSparqlJson = runSparqlJson;
        Window.MessageReceived += OnMessage;
    }

    /// <summary>Dispatches one inbound page message by its method.</summary>
    /// <param name="message">The JSON request.</param>
    private void OnMessage(string message)
    {
        BridgeRequest? request = JsonSerializer.Deserialize(message, BridgeJsonContext.Default.BridgeRequest);
        if(request is null)
        {
            return;
        }

        _ = request.Method switch
        {
            "runSparql" => RunAsync(request),
            "subscribeTrace" => SubscribeTraceAsync(),
            _ => Task.CompletedTask
        };
    }

    /// <summary>Runs the query and posts the reply; a failed query is reported as an error reply, never a thrown fault.</summary>
    /// <param name="request">The run request.</param>
    private async Task RunAsync(BridgeRequest request)
    {
        JsonObject reply = new() { ["id"] = request.Id };
        try
        {
            string resultsJson = await RunSparqlJson(request.Query ?? string.Empty, CancellationToken.None).ConfigureAwait(false);
            reply["ok"] = true;
            reply["results"] = JsonNode.Parse(resultsJson);
        }
        catch(Exception exception)
        {
            reply["ok"] = false;
            reply["error"] = new JsonObject { ["error"] = exception.Message, ["diagnostics"] = new JsonArray() };
        }

        Window.Send(reply.ToJsonString());
    }

    /// <summary>Starts forwarding engine trace events to the page as <c>{ "kind": "trace", "event": … }</c>.</summary>
    private Task SubscribeTraceAsync()
    {
        //Subscribe a TraceHandler<QueryTraceEvent> (and NetworkGovernanceTraceEvent) on the engine, project
        //each event to its wire shape keyed by CorrelationId, and post it. Wired with the engine adapter.
        return Task.CompletedTask;
    }
}

/// <summary>A page-to-host request: a correlation id, the method, and (for a query) its text.</summary>
public sealed class BridgeRequest
{
    /// <summary>The correlation id a reply echoes; absent for fire-and-forget requests.</summary>
    public string? Id { get; init; }

    /// <summary>The requested method, for example <c>runSparql</c> or <c>subscribeTrace</c>.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>The query text for a <c>runSparql</c> request.</summary>
    public string? Query { get; init; }
}

/// <summary>The source-generated JSON context for the bridge request DTO.</summary>
[JsonSerializable(typeof(BridgeRequest))]
internal sealed partial class BridgeJsonContext: JsonSerializerContext
{
}
