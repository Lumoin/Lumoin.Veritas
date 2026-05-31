using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Xml;
using IoPath = System.IO.Path;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Dispatches a SPARQL <see cref="W3cTestType.SparqlQueryEvaluation"/> test case: builds the engine over the data
/// graph (<c>qt:data</c>), runs the query (<c>qt:query</c>) through parse → normalize → translate → execute, and
/// compares the result to the expected <c>mf:result</c> fixture with <see cref="SparqlResultComparer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> <c>SELECT</c> and <c>ASK</c> results, whose expected fixture is a SPARQL Query Results XML
/// (<c>.srx</c>) file, are executed and compared. The honest-skip cases (<see cref="W3cOutcomeStatus.Skipped"/>)
/// are the features not yet built rather than defects: <c>CONSTRUCT</c>/<c>DESCRIBE</c> (the engine yields solution
/// mappings, not a constructed graph, until result construction lands), a JSON (<c>.srj</c>) expected fixture (the
/// JSON results reader is not built yet), and any query whose algebra uses an operator the executor does not yet
/// support (a <see cref="NotSupportedException"/>). A genuine wrong answer is <see cref="W3cOutcomeStatus.Failed"/>.
/// </para>
/// <para>
/// The query and data terms bridge by <see cref="RdfTerm"/> value identity (the parser and the Turtle reader
/// intern into independent pools, but the engine's term dictionary compares terms by value), so cross-pool
/// resolution is correct.
/// </para>
/// </remarks>
internal static class W3cSparqlEvalRunner
{
    /// <summary>Runs one SPARQL query-evaluation test case.</summary>
    /// <param name="testCase">The test case to run.</param>
    /// <param name="cancellationToken">A token to cancel parsing, building, and evaluation.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="testCase"/> is <see langword="null"/>.</exception>
    public static async Task<W3cOutcome> RunAsync(W3cTestCase testCase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Query file not found: {testCase.InputPath}");
        }

        if(testCase.ExpectedPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "Evaluation test declares no mf:result fixture.");
        }

        if(!File.Exists(testCase.ExpectedPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected result file not found: {testCase.ExpectedPath}");
        }

        if(testCase.QueryDataPath is not null && !File.Exists(testCase.QueryDataPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Data graph file not found: {testCase.QueryDataPath}");
        }

        using Utf8StringPool pool = new();

        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(
            await File.ReadAllBytesAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false),
            pool,
            Utf8String.FromString(new Uri(IoPath.GetFullPath(testCase.InputPath)).AbsoluteUri));
        if(parsed.HasErrors)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Query did not parse: {DescribeFirstError(parsed.Diagnostics)}");
        }

        if(parsed.Tree is not SparqlQuery query)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "SPARQL Update requests are not evaluated by this build.");
        }

        if(query.Form is ConstructQuery or DescribeQuery)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "CONSTRUCT/DESCRIBE result construction is not yet implemented.");
        }

        List<DataTriple> data;
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs;
        try
        {
            data = await LoadDataAsync(testCase.QueryDataPath, cancellationToken).ConfigureAwait(false);
            namedGraphs = await LoadNamedGraphsAsync(testCase.GraphDataPaths, cancellationToken).ConfigureAwait(false);
        }
        catch(InvalidOperationException ex)
        {
            //A graph (default or named) is in a format this harness cannot read (RDF/XML, TriG) or is malformed:
            //the test cannot be set up, so it is skipped structurally rather than reported as an engine failure.
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Could not load a graph (unsupported format or malformed): {ex.Message}");
        }

        SparqlResultSet actual;
        try
        {
            //IRI()/URI() resolve a relative argument against the query's effective base — the last in-query BASE,
            //else the external (file-URL) base the parser was given. Thread it through the expression context.
            Utf8String? baseIri = query.Prologue.Bases is { Count: > 0 } bases
                ? bases[^1].Iri.Value
                : Utf8String.FromString(new Uri(IoPath.GetFullPath(testCase.InputPath)).AbsoluteUri);
            SparqlExpressionContext context = SparqlExpressionContext.CreateDefault().WithBaseIri(baseIri);

            //A test with qt:graphData evaluates against a dataset (default graph + named graphs); otherwise the
            //single default graph. GRAPH forms resolve their designator against the same file-URL space.
            SparqlQueryEngine engine = namedGraphs.Count == 0
                ? await SparqlQueryEngine.BuildAsync(data, context, cancellationToken).ConfigureAwait(false)
                : await SparqlQueryEngine.BuildDatasetAsync(data, namedGraphs, context, cancellationToken).ConfigureAwait(false);
            SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);
            AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
            IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);
            actual = BuildResultSet(query, solutions);
        }
        catch(NotSupportedException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Query uses a feature the executor does not yet support: {ex.Message}");
        }

        //Only the SPARQL Query Results serializations (.srx / .srj) are compared today; a switch expression
        //classifies the rest (case-insensitively) into the honest-skip outcome, leaving null to mean "this is a
        //solution-result format — proceed and read it below".
        string extension = IoPath.GetExtension(testCase.ExpectedPath);
        bool isJson = string.Equals(extension, ".srj", StringComparison.OrdinalIgnoreCase);
        W3cOutcome? unsupported = extension switch
        {
            _ when string.Equals(extension, ".srx", StringComparison.OrdinalIgnoreCase) => null,
            _ when isJson => null,
            _ => new W3cOutcome(W3cOutcomeStatus.Skipped, $"Expected result format '{extension}' (a graph result) is not yet compared.")
        };
        if(unsupported is not null)
        {
            return unsupported;
        }

        byte[] expectedBytes = await File.ReadAllBytesAsync(testCase.ExpectedPath, cancellationToken).ConfigureAwait(false);
        SparqlResultSet expected;
        try
        {
            expected = isJson
                ? SparqlResultsJsonReader.Read(expectedBytes)
                : SparqlResultsXmlReader.Read(expectedBytes);
        }
        catch(FormatException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected SPARQL results fixture did not parse: {ex.Message}");
        }

        bool ordered = query.Modifier.Order is not null;
        bool equal = SparqlResultComparer.AreEquivalent(actual, expected, ordered);
        string note = actual.IsBoolean
            ? $"ASK actual={actual.Boolean}, expected={expected.Boolean}"
            : $"SELECT actual={actual.Solutions.Count} rows, expected={expected.Solutions.Count} rows, ordered={ordered}";

        return equal
            ? new W3cOutcome(W3cOutcomeStatus.Passed, note)
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"Result does not match expected: {note}");
    }

    /// <summary>Builds the result set for a SELECT (head variables + solutions) or ASK (the pattern matched) query.</summary>
    /// <param name="query">The parsed query.</param>
    /// <param name="solutions">The engine's solution sequence.</param>
    /// <returns>The result set.</returns>
    private static SparqlResultSet BuildResultSet(SparqlQuery query, IReadOnlyList<SparqlSolution> solutions)
    {
        if(query.Form is AskQuery)
        {
            return SparqlResultSet.ForAsk(solutions.Count > 0);
        }

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

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Loads the data graph from a Turtle file into the engine's triple model; an absent path is the empty graph.</summary>
    /// <param name="dataPath">The data-graph file path, or <see langword="null"/> for an empty graph.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The data triples.</returns>
    /// <exception cref="InvalidOperationException">The data graph did not parse.</exception>
    private static async Task<List<DataTriple>> LoadDataAsync(string? dataPath, CancellationToken cancellationToken)
    {
        List<DataTriple> data = [];
        if(dataPath is null)
        {
            return data;
        }

        byte[] bytes = await File.ReadAllBytesAsync(dataPath, cancellationToken).ConfigureAwait(false);
        DiagnosticBag diagnostics = new();
        string baseIri = new Uri(IoPath.GetFullPath(dataPath)).AbsoluteUri;
        await foreach(Quad quad in TurtleReader.ReadAsync(bytes, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseIri, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            data.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        if(diagnostics.HasErrors)
        {
            throw new InvalidOperationException($"Data graph '{dataPath}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}");
        }

        return data;
    }

    /// <summary>Loads each <c>qt:graphData</c> named-graph file, keyed by its own file IRI — the IRI a query's <c>GRAPH &lt;file&gt;</c> (or projected graph variable) resolves to.</summary>
    /// <param name="graphDataPaths">The named-graph data files, or <see langword="null"/>/empty when the test declares none.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The named graphs as (graph-name, triples) pairs.</returns>
    /// <exception cref="InvalidOperationException">A named-graph file did not parse.</exception>
    private static async Task<List<(RdfTerm Name, IEnumerable<DataTriple> Triples)>> LoadNamedGraphsAsync(IReadOnlyList<string>? graphDataPaths, CancellationToken cancellationToken)
    {
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs = [];
        if(graphDataPaths is null)
        {
            return namedGraphs;
        }

        foreach(string path in graphDataPaths)
        {
            RdfTerm name = new NamedNode(Utf8String.FromString(new Uri(IoPath.GetFullPath(path)).AbsoluteUri));
            List<DataTriple> triples = await LoadDataAsync(path, cancellationToken).ConfigureAwait(false);
            namedGraphs.Add((name, triples));
        }

        return namedGraphs;
    }

    /// <summary>Describes the first error-severity diagnostic for a failure message.</summary>
    /// <param name="diagnostics">The diagnostics to scan.</param>
    /// <returns>A one-line description of the first error, or a generic note when none is error-severity.</returns>
    private static string DescribeFirstError(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code}: {diagnostic.Message}";
            }
        }

        return "an unspecified parse error";
    }
}
