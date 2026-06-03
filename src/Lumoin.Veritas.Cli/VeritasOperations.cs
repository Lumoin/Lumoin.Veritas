using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
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
    /// <summary>Builds a query engine over the RDF documents at <paramref name="dataPaths"/> (default graph plus any named graphs the files carry).</summary>
    /// <param name="dataPaths">The data document paths (<c>.ttl</c>/<c>.nt</c>/<c>.trig</c>/<c>.nq</c>).</param>
    /// <param name="context">The expression context (carries the effective base IRI).</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The built engine, or an error message describing the first document that could not be read.</returns>
    public static async Task<(SparqlQueryEngine? Engine, string? Error)> BuildEngineAsync(
        IReadOnlyList<string> dataPaths,
        SparqlExpressionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(context);

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

        if(namedGraphs.Count == 0)
        {
            SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(defaultGraph, context, cancellationToken: cancellationToken).ConfigureAwait(false);
            return (engine, null);
        }

        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = [];
        foreach(KeyValuePair<RdfTerm, List<DataTriple>> entry in namedGraphs)
        {
            named.Add((entry.Key, entry.Value));
        }

        SparqlQueryEngine datasetEngine = await SparqlQueryEngine.BuildDatasetAsync(defaultGraph, named, context, cancellationToken: cancellationToken).ConfigureAwait(false);
        return (datasetEngine, null);
    }

    /// <summary>Executes a SPARQL <c>SELECT</c>/<c>ASK</c> query against a built engine and renders the result set as CSV or TSV.</summary>
    /// <param name="engine">The engine built by <see cref="BuildEngineAsync"/>.</param>
    /// <param name="pool">The string pool the request parsed into.</param>
    /// <param name="queryText">The query text.</param>
    /// <param name="baseIri">The base IRI relative references resolve against (a file URL for a query file, or the request base).</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The rendered results, or an error message for a malformed or not-yet-renderable query.</returns>
    public static async Task<OperationResult> ExecuteQueryAsync(
        SparqlQueryEngine engine,
        Utf8StringPool pool,
        string queryText,
        string baseIri,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(queryText);

        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(
            Utf8String.FromString(queryText).Memory,
            pool,
            Utf8String.FromString(baseIri));
        if(parsed.HasErrors)
        {
            return OperationResult.Failed($"Query did not parse: {DescribeFirstError(parsed.Diagnostics)}");
        }

        if(parsed.Tree is not SparqlQuery query)
        {
            return OperationResult.Failed("The request is a SPARQL Update, not a query; use the update surface.");
        }

        if(query.Form is ConstructQuery or DescribeQuery)
        {
            return OperationResult.Failed("CONSTRUCT / DESCRIBE graph results are not yet rendered by this surface; SELECT and ASK are supported.");
        }

        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);
        AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        if(query.Form is AskQuery)
        {
            return OperationResult.Ok(solutions.Count > 0 ? "true" : "false");
        }

        SparqlResultSet results = SparqlResultSet.ForSelect(SelectVariables(query), solutions);
        return OperationResult.Ok(SparqlResultsDelimitedWriter.WriteToString(results, format));
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

        using Utf8StringPool pool = new();
        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault().WithBaseIri(Utf8String.FromString(baseIri));

        (SparqlQueryEngine? engine, string? error) = await BuildEngineAsync(dataPaths, context, cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            return OperationResult.Failed(error);
        }

        return await ExecuteQueryAsync(engine!, pool, queryText, baseIri, format, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Collects a <c>SELECT</c> query's projected variable names in head order (the result columns).</summary>
    /// <param name="query">The query.</param>
    /// <returns>The projected variable names.</returns>
    private static List<Utf8String> SelectVariables(SparqlQuery query)
    {
        List<Utf8String> variables = [];
        if(query.Form is SelectQuery select)
        {
            foreach(SelectProjection projection in select.Projections)
            {
                switch(projection)
                {
                    case SelectVariable variable:
                    {
                        variables.Add(variable.Variable.Name);

                        break;
                    }

                    case SelectExpressionAs expressionAs:
                    {
                        variables.Add(expressionAs.AsVariable.Name);

                        break;
                    }

                    default:
                    {
                        break;
                    }
                }
            }
        }

        return variables;
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
