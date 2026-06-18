using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Adapts the never-throwing <see cref="TurtleReader"/> to the catch-based contract of
/// <see cref="W3cTestRunner"/>.
/// </summary>
/// <remarks>
/// The shared, syntax-agnostic runner detects a negative-syntax test by catching a parse exception, a
/// contract still honoured by the unchanged NQuads and SPARQL readers. The Turtle reader instead
/// recovers and reports diagnostics into a caller-owned <see cref="DiagnosticBag"/>, so this adapter
/// owns a bag, streams the quads, and — once enumeration completes — re-raises any error-severity
/// diagnostic as a <see cref="TurtleParseException"/>. The runner thus keeps working over the Turtle and
/// TriG suites without the recovery rework leaking into the shared runner.
/// </remarks>
internal static class TurtleConformanceReader
{
    /// <summary>
    /// Reads a Turtle/TriG pipe into quads, throwing <see cref="TurtleParseException"/> at the end if the
    /// reader reported any error-severity diagnostic.
    /// </summary>
    /// <param name="input">The UTF-8 source pipe.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The parsed quads.</returns>
    public static async IAsyncEnumerable<Quad> ReadAsync(
        PipeReader input,
        TurtleSyntax syntax,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DiagnosticBag diagnostics = new();
        await foreach(Quad quad in TurtleReader.ReadAsync(input, syntax, diagnostics, pool: null, baseIri: null, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return quad;
        }

        if(diagnostics.HasErrors)
        {
            throw new TurtleParseException(DescribeFirstError(diagnostics));
        }
    }

    /// <summary>Renders the first error-severity diagnostic for a parse-failure message.</summary>
    /// <param name="diagnostics">The bag to inspect.</param>
    /// <returns>A human-readable description of the first error.</returns>
    internal static string DescribeFirstError(DiagnosticBag diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics.Diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} at {diagnostic.Span}: {diagnostic.Message}";
            }
        }

        return "The reader reported error diagnostics.";
    }
}
