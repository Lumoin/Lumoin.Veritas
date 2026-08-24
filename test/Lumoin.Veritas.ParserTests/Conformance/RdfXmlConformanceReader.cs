using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Adapts the never-throwing <see cref="RdfXmlReader"/> to the catch-based contract of
/// <see cref="W3cTestRunner"/>, supplying the per-test base IRI the shared
/// <see cref="W3cTestRunner.InputReader"/> delegate cannot carry.
/// </summary>
/// <remarks>
/// The RDF/XML reader resolves relative references and <c>rdf:ID</c> reification against a document
/// base IRI; the W3C suites supply that base via the manifest's <c>mf:assumedTestBase</c> composed with
/// each fixture's location, not via the fixture file's own <c>file://</c> URL. The suite class therefore
/// closes over the computed base and passes it here. Like the Turtle adapter, this drains the input,
/// streams the quads, and — once the document has been read — re-raises any error-severity diagnostic as
/// a <see cref="FormatException"/> so the runner's negative-syntax tests observe a rejection.
/// </remarks>
internal static class RdfXmlConformanceReader
{
    /// <summary>
    /// Reads an RDF/XML pipe into quads, throwing <see cref="FormatException"/> at the end if the reader
    /// reported any error-severity diagnostic.
    /// </summary>
    /// <param name="input">The UTF-8 source pipe.</param>
    /// <param name="baseIri">The document base IRI relative references resolve against.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The parsed quads.</returns>
    public static async IAsyncEnumerable<Quad> ReadAsync(
        PipeReader input,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> source = await ReadToEndAsync(input, cancellationToken).ConfigureAwait(false);

        DiagnosticBag diagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(source, diagnostics, baseIri is null ? default : Utf8Strings.From(baseIri));

        foreach(Quad quad in quads)
        {
            yield return quad;
        }

        if(diagnostics.HasErrors)
        {
            throw new FormatException(TurtleConformanceReader.DescribeFirstError(diagnostics));
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
