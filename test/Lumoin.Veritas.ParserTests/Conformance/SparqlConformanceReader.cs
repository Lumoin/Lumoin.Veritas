using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Shared SPARQL query reader for the W3C conformance harness: parses the query bytes and yields no quads,
/// surfacing a parse or static-scope error as the exception the runner observes.
/// </summary>
/// <remarks>
/// The recovery-mode parser never throws on malformed input — it records diagnostics into a bag and returns
/// a (possibly error-node-carrying) request. <see cref="ParseQuery"/> re-raises a
/// <see cref="Lumoin.Veritas.Sparql.SparqlParseException"/> when the bag holds any error-severity diagnostic
/// (including a SPARQL §18.2.1 static-scope violation), mirroring <see cref="TurtleConformanceReader"/>, so a
/// positive-syntax test passes when the query parses cleanly and a negative-syntax test passes when it raises.
/// </remarks>
internal static class SparqlConformanceReader
{
    /// <summary>
    /// Parses the query bytes from the pipe and yields no quads; a lexical, grammar, or static-scope error
    /// surfaces as the <see cref="Lumoin.Veritas.Sparql.SparqlParseException"/> the runner observes.
    /// </summary>
    /// <param name="input">The pipe over the query file's bytes.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>An empty quad stream (a query produces no quads).</returns>
    public static async IAsyncEnumerable<Quad> ParseQuery(PipeReader input, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        ReadOnlyMemory<byte> source = await ReadToEndAsync(input, cancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        SparqlLexer lexer = new(source, pool);
        SparqlParser parser = new(lexer.Tokenize(), pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        ParseResult<Lumoin.Veritas.Sparql.Ast.SparqlRequest> result = parser.ParseToResult();
        BridgeLexerDiagnostics(lexer, diagnostics);

        //A grammatically well-formed query can still violate the SPARQL §18.2.1 static scope constraints (an
        //in-scope AS target, an ungrouped projection, a nested aggregate); those make it a negative-syntax test.
        if(result.Tree is not null)
        {
            Lumoin.Veritas.Sparql.Analysis.SparqlScopeAnalyzer.Analyze(result.Tree, diagnostics, Lumoin.Veritas.Sparql.Execution.SparqlFunctionRegistry.Empty.AggregateIris);
        }

        if(result.HasErrors || diagnostics.HasErrors)
        {
            throw new Lumoin.Veritas.Sparql.SparqlParseException(TurtleConformanceReader.DescribeFirstError(diagnostics));
        }

        yield break;
    }

    /// <summary>Bridges the lexer's internal diagnostics into the shared parse-level bag.</summary>
    /// <param name="lexer">The lexer whose diagnostics are drained.</param>
    /// <param name="diagnostics">The bag to append the bridged diagnostics to.</param>
    private static void BridgeLexerDiagnostics(SparqlLexer lexer, DiagnosticBag diagnostics)
    {
        foreach(SparqlLexDiagnostic lexDiagnostic in lexer.Diagnostics)
        {
            diagnostics.Add(SparqlLexDiagnosticBridge.ToDiagnostic(lexDiagnostic));
        }
    }

    /// <summary>Drains a <see cref="PipeReader"/> into a contiguous byte buffer.</summary>
    /// <param name="input">The pipe to drain.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The full input bytes.</returns>
    private static async Task<ReadOnlyMemory<byte>> ReadToEndAsync(PipeReader input, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();

        while(true)
        {
            ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);

            foreach(ReadOnlyMemory<byte> segment in result.Buffer)
            {
                buffer.Write(segment.Span);
            }

            input.AdvanceTo(result.Buffer.End);

            if(result.IsCompleted)
            {
                break;
            }
        }

        return buffer.ToArray();
    }
}
