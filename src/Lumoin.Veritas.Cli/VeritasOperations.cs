using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Turtle;
using IoPath = System.IO.Path;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The shared engine operations the command-line, MCP, and HTTP surfaces all call directly.
/// Each surface is a thin transport over these methods; there is no interface between them and
/// the engine, and the library stays transport-free. Expected conditions (a file that cannot be
/// read, a query that does not parse, a query form the surface does not yet render) are returned
/// as an <see cref="OperationResult"/> rather than thrown.
/// </summary>
internal static class VeritasOperations
{
    /// <summary>Opens a Veritas database over the RDF documents at <paramref name="dataPaths"/> (default graph plus any named graphs the files carry), with the database's fully-wired default configuration.</summary>
    /// <param name="dataPaths">The data document paths (<c>.ttl</c>/<c>.nt</c>/<c>.trig</c>/<c>.nq</c>).</param>
    /// <param name="cancellationToken">A token that aborts loading and opening.</param>
    /// <returns>The opened database, or an error message describing the first document that could not be read.</returns>
    public static async Task<(VeritasEngine? Database, string? Error)> OpenDatabaseAsync(
        IReadOnlyList<string> dataPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);

        List<DataTriple> defaultGraph = [];
        Dictionary<RdfTerm, List<DataTriple>> namedGraphs = [];

        foreach(string path in dataPaths)
        {
            if(!File.Exists(path))
            {
                return (null, $"Data file not found: {path}");
            }

            (IReadOnlyList<Quad>? quads, string? error) = await ReadQuadsAsync(path, cancellationToken).ConfigureAwait(false);
            if(error is not null)
            {
                return (null, error);
            }

            foreach(Quad quad in quads!)
            {
                DataTriple triple = new(quad.Subject, quad.Predicate, quad.Object);
                if(quad.Graph is null)
                {
                    defaultGraph.Add(triple);
                }
                else
                {
                    if(!namedGraphs.TryGetValue(quad.Graph, out List<DataTriple>? bucket))
                    {
                        bucket = [];
                        namedGraphs[quad.Graph] = bucket;
                    }

                    bucket.Add(triple);
                }
            }
        }

        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = [];
        foreach(KeyValuePair<RdfTerm, List<DataTriple>> entry in namedGraphs)
        {
            named.Add((entry.Key, entry.Value));
        }

        VeritasEngine database = await VeritasEngine.OpenAsync(defaultGraph, named, cancellationToken: cancellationToken).ConfigureAwait(false);

        return (database, null);
    }

    /// <summary>Executes a SPARQL <c>SELECT</c>/<c>ASK</c> query against an open database and renders the result as CSV/TSV (SELECT) or a boolean (ASK).</summary>
    /// <param name="database">The database opened by <see cref="OpenDatabaseAsync"/>.</param>
    /// <param name="queryText">The query text.</param>
    /// <param name="baseIri">The base IRI relative references resolve against (a file URL for a query file, or the request base).</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The rendered results, or an error message for a malformed or not-yet-renderable query.</returns>
    public static async Task<OperationResult> ExecuteQueryAsync(
        VeritasEngine database,
        string queryText,
        string baseIri,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(queryText);

        VeritasQueryResult result;
        try
        {
            result = await database.QueryAsync(queryText, baseIri, cancellationToken).ConfigureAwait(false);
        }
        catch(ArgumentException ex)
        {
            return OperationResult.Failed(ex.Message);
        }
        catch(NotSupportedException ex)
        {
            return OperationResult.Failed(ex.Message);
        }

        return result.IsAsk
            ? OperationResult.Ok(result.Boolean!.Value ? "true" : "false")
            : OperationResult.Ok(SparqlResultsDelimitedWriter.WriteToString(result.Bindings!, format));
    }

    /// <summary>Reads a SPARQL query file and evaluates it against the documents at <paramref name="dataPaths"/>; the convenience the <c>query</c> command calls.</summary>
    /// <param name="queryPath">The query file path.</param>
    /// <param name="dataPaths">The data document paths forming the dataset.</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The rendered results, or an error message.</returns>
    public static async Task<OperationResult> RunQueryFileAsync(
        string queryPath,
        IReadOnlyList<string> dataPaths,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryPath);
        ArgumentNullException.ThrowIfNull(dataPaths);

        if(!File.Exists(queryPath))
        {
            return OperationResult.Failed($"Query file not found: {queryPath}");
        }

        string baseIri = new Uri(IoPath.GetFullPath(queryPath)).AbsoluteUri;
        string queryText = await File.ReadAllTextAsync(queryPath, cancellationToken).ConfigureAwait(false);

        return await RunQueryTextAsync(queryText, dataPaths, baseIri, format, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates a SPARQL query given as text against the documents at <paramref name="dataPaths"/>; the convenience the MCP and HTTP surfaces call.</summary>
    /// <param name="queryText">The query text.</param>
    /// <param name="dataPaths">The data document paths forming the dataset.</param>
    /// <param name="baseIri">The base IRI relative references resolve against.</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The rendered results, or an error message.</returns>
    public static async Task<OperationResult> RunQueryTextAsync(
        string queryText,
        IReadOnlyList<string> dataPaths,
        string baseIri,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(baseIri);

        (VeritasEngine? database, string? error) = await OpenDatabaseAsync(dataPaths, cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            return OperationResult.Failed(error);
        }

        await using var scope = database!.ConfigureAwait(false);

        return await ExecuteQueryAsync(database!, queryText, baseIri, format, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a document's quads, dispatching on the file extension; Turtle/N-Triples/TriG via the Turtle reader, N-Quads via the N-Quads reader.</summary>
    /// <param name="path">The document path.</param>
    /// <param name="cancellationToken">A token that aborts reading.</param>
    /// <returns>The quads, or an error message for an unreadable format or malformed document.</returns>
    private static async Task<(IReadOnlyList<Quad>? Quads, string? Error)> ReadQuadsAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string extension = IoPath.GetExtension(path);
        List<Quad> quads = [];

        if(string.Equals(extension, ".nq", StringComparison.OrdinalIgnoreCase))
        {
            await foreach(Quad quad in NQuadsReader.ReadAsync(bytes, pool: null, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            return (quads, null);
        }

        if(string.Equals(extension, ".ttl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".nt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase))
        {
            TurtleSyntax syntax = string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase) ? TurtleSyntax.TriG : TurtleSyntax.Turtle;
            string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
            DiagnosticBag diagnostics = new();
            await foreach(Quad quad in TurtleReader.ReadAsync(bytes, syntax, diagnostics, pool: null, baseIri: baseIri, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            return diagnostics.HasErrors
                ? (null, $"Document '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}")
                : (quads, null);
        }

        return (null, $"Unsupported data format '{extension}' for '{path}'; supported: .ttl, .nt, .trig, .nq.");
    }

    /// <summary>Renders the first error diagnostic (code and message) for an operation-result error string.</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <returns>A one-line description of the first error, or a generic message when none carry detail.</returns>
    private static string DescribeFirstError(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} {diagnostic.Message}";
            }
        }

        return "unspecified parse error.";
    }
}
