using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Sparql.Results;

/// <summary>
/// The two delimited SPARQL <c>SELECT</c> result serializations defined by the
/// <see href="https://www.w3.org/TR/sparql11-results-csv-tsv/">SPARQL 1.1 Query Results CSV and TSV Formats</see>.
/// They share one writer (<see cref="SparqlResultsDelimitedWriter"/>) and differ only by the
/// <see cref="DelimitedFormatSpec"/> each maps to.
/// </summary>
public enum SparqlDelimitedResultsFormat
{
    /// <summary>Comma-separated, bare header names, CRLF rows, lossy plain-text values with RFC 4180 quoting.</summary>
    Csv,

    /// <summary>Tab-separated, <c>?</c>-prefixed header names, LF rows, Turtle term-syntax values (round-trippable).</summary>
    Tsv
}

/// <summary>
/// Writes a <c>SELECT</c> <see cref="SparqlResultSet"/> as SPARQL Results CSV or TSV. CSV and TSV are the same
/// row-oriented format up to a <see cref="DelimitedFormatSpec"/> (delimiter, header prefix, line terminator, and
/// value rendering/escaping), so one implementation serves both. Output streams row-by-row over an
/// <see cref="IAsyncEnumerable{T}"/> of lines so a large result need never be buffered whole; materialized
/// convenience overloads build on the same line producer. <c>ASK</c> results have no tabular form and raise
/// <see cref="NotSupportedException"/>.
/// </summary>
public static class SparqlResultsDelimitedWriter
{
    /// <summary>
    /// Streams the serialized lines (the header line, then one line per solution; each line includes the format's
    /// terminator) over a possibly-async solution sequence, so neither the input solutions nor the output text is
    /// buffered whole.
    /// </summary>
    /// <param name="variables">The head variables, in column order.</param>
    /// <param name="solutions">The solution sequence to serialize.</param>
    /// <param name="format">The delimited format to emit.</param>
    /// <param name="cancellationToken">A token that aborts enumeration.</param>
    /// <returns>The serialized lines, in order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static async IAsyncEnumerable<string> WriteLinesAsync(
        IReadOnlyList<Utf8String> variables,
        IAsyncEnumerable<SparqlSolution> solutions,
        SparqlDelimitedResultsFormat format,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(solutions);

        DelimitedFormatSpec spec = DelimitedFormatSpec.For(format);
        yield return Header(variables, spec);

        await foreach(SparqlSolution solution in solutions.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return Row(variables, solution, spec);
        }
    }

    /// <summary>Writes a materialized SELECT result set to a pipe (UTF-8, no BOM), one line at a time, completing the pipe at the end.</summary>
    /// <param name="results">The result set to serialize.</param>
    /// <param name="writer">The destination pipe writer.</param>
    /// <param name="format">The delimited format to emit.</param>
    /// <param name="cancellationToken">A token that aborts writing.</param>
    /// <returns>The asynchronous write operation.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="results"/> is an ASK (boolean) result.</exception>
    public static async Task WriteAsync(SparqlResultSet results, PipeWriter writer, SparqlDelimitedResultsFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(writer);

        await foreach(string line in WriteLinesAsync(results.Variables, ToAsync(SelectSolutions(results)), format, cancellationToken).ConfigureAwait(false))
        {
            writer.WriteUtf8(line);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await writer.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>Serializes a materialized SELECT result set to its delimited text.</summary>
    /// <param name="results">The result set to serialize.</param>
    /// <param name="format">The delimited format to emit.</param>
    /// <returns>The serialized text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="results"/> is an ASK (boolean) result.</exception>
    public static string WriteToString(SparqlResultSet results, SparqlDelimitedResultsFormat format)
    {
        ArgumentNullException.ThrowIfNull(results);

        DelimitedFormatSpec spec = DelimitedFormatSpec.For(format);
        StringBuilder builder = new();
        builder.Append(Header(results.Variables, spec));
        foreach(SparqlSolution solution in SelectSolutions(results))
        {
            builder.Append(Row(results.Variables, solution, spec));
        }

        return builder.ToString();
    }

    /// <summary>Returns the solutions of a SELECT result set, rejecting an ASK (boolean) result that has no tabular form.</summary>
    /// <param name="results">The result set.</param>
    /// <returns>The solution sequence.</returns>
    /// <exception cref="NotSupportedException"><paramref name="results"/> is an ASK result.</exception>
    private static IReadOnlyList<SparqlSolution> SelectSolutions(SparqlResultSet results)
    {
        if(results.IsBoolean)
        {
            throw new NotSupportedException("The SPARQL Results CSV/TSV formats represent SELECT results only; an ASK boolean has no delimited form.");
        }

        return results.Solutions;
    }

    /// <summary>Adapts a materialized solution list to an async sequence for the streaming line producer.</summary>
    /// <param name="solutions">The solutions.</param>
    /// <returns>The solutions as an async sequence.</returns>
    private static async IAsyncEnumerable<SparqlSolution> ToAsync(IReadOnlyList<SparqlSolution> solutions)
    {
        foreach(SparqlSolution solution in solutions)
        {
            yield return solution;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Builds the header line: each variable name (format-prefixed and -quoted), delimited, plus the terminator.</summary>
    /// <param name="variables">The head variables.</param>
    /// <param name="spec">The format spec.</param>
    /// <returns>The header line.</returns>
    private static string Header(IReadOnlyList<Utf8String> variables, DelimitedFormatSpec spec)
    {
        StringBuilder builder = new();
        for(int i = 0; i < variables.Count; i++)
        {
            if(i > 0)
            {
                builder.Append(spec.Delimiter);
            }

            string name = spec.PrefixVariables ? "?" + variables[i].ToString() : variables[i].ToString();
            builder.Append(spec.Escape(name));
        }

        return builder.Append(spec.LineTerminator).ToString();
    }

    /// <summary>Builds one solution's row: each variable's value (rendered and escaped for the format, empty when unbound), delimited, plus the terminator.</summary>
    /// <param name="variables">The head variables.</param>
    /// <param name="solution">The solution.</param>
    /// <param name="spec">The format spec.</param>
    /// <returns>The row line.</returns>
    private static string Row(IReadOnlyList<Utf8String> variables, SparqlSolution solution, DelimitedFormatSpec spec)
    {
        StringBuilder builder = new();
        for(int i = 0; i < variables.Count; i++)
        {
            if(i > 0)
            {
                builder.Append(spec.Delimiter);
            }

            if(solution.TryGetValue(new SparqlVariable(variables[i]), out RdfTerm value))
            {
                builder.Append(spec.RenderValue(value));
            }
        }

        return builder.Append(spec.LineTerminator).ToString();
    }
}

/// <summary>
/// The per-format differences between SPARQL Results CSV and TSV: the field delimiter, whether header variable
/// names carry a <c>?</c> prefix, the line terminator, and how a value and a header field are rendered/escaped.
/// </summary>
/// <param name="Delimiter">The field delimiter (<c>,</c> for CSV, tab for TSV).</param>
/// <param name="PrefixVariables">Whether header variable names are <c>?</c>-prefixed (TSV) or bare (CSV).</param>
/// <param name="LineTerminator">The row terminator (<c>\r\n</c> for CSV, <c>\n</c> for TSV).</param>
/// <param name="Lossy">Whether values use the lossy CSV plain-text rendering with RFC 4180 quoting (CSV) or Turtle term syntax (TSV).</param>
internal readonly record struct DelimitedFormatSpec(char Delimiter, bool PrefixVariables, string LineTerminator, bool Lossy)
{
    /// <summary>The CSV spec: comma-delimited, bare names, CRLF rows, lossy values with RFC 4180 quoting.</summary>
    private static DelimitedFormatSpec Csv { get; } = new(',', PrefixVariables: false, "\r\n", Lossy: true);

    /// <summary>The TSV spec: tab-delimited, <c>?</c>-prefixed names, LF rows, Turtle term-syntax values.</summary>
    private static DelimitedFormatSpec Tsv { get; } = new('\t', PrefixVariables: true, "\n", Lossy: false);

    /// <summary>Maps a format to its spec.</summary>
    /// <param name="format">The delimited format.</param>
    /// <returns>The matching spec.</returns>
    public static DelimitedFormatSpec For(SparqlDelimitedResultsFormat format)
    {
        return format switch
        {
            SparqlDelimitedResultsFormat.Csv => Csv,
            SparqlDelimitedResultsFormat.Tsv => Tsv,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown delimited results format.")
        };
    }

    /// <summary>Renders a binding value for this format: a quoted lossy plain-text form (CSV) or Turtle term syntax (TSV).</summary>
    /// <param name="value">The term to render.</param>
    /// <returns>The escaped field text.</returns>
    public string RenderValue(RdfTerm value)
    {
        return Lossy ? Rfc4180Quote(SparqlResultTermText.Csv(value)) : SparqlResultTermText.Turtle(value);
    }

    /// <summary>Escapes a header field: RFC 4180 quoting for CSV; Turtle TSV header names need no escaping.</summary>
    /// <param name="field">The header field text.</param>
    /// <returns>The escaped field.</returns>
    public string Escape(string field)
    {
        return Lossy ? Rfc4180Quote(field) : field;
    }

    /// <summary>Applies RFC 4180 quoting: a field containing a comma, quote, CR, or LF is wrapped in double quotes with internal quotes doubled.</summary>
    /// <param name="field">The field text.</param>
    /// <returns>The quoted field, or the field unchanged when no quoting is needed.</returns>
    private static string Rfc4180Quote(string field)
    {
        if(field.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
