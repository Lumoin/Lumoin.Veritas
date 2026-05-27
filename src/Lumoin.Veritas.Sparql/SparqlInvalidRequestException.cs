using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql;

/// <summary>
/// Indicates that a SPARQL request that did not parse cleanly was handed to execution.
/// </summary>
/// <remarks>
/// <para>
/// Parsing never throws on malformed input — it recovers and returns a
/// <see cref="ParseResult{TTree}"/> whose <see cref="ParseResult{TTree}.HasErrors"/> is set and whose
/// tree may carry error nodes. Executing such a request is a contract violation, so the query engine
/// (Milestone C) refuses it by throwing this, carrying the <see cref="Diagnostics"/> that made the
/// request invalid so a caller can surface them.
/// </para>
/// <para>
/// This is distinct from <see cref="SparqlParseException"/>, which the lexer and parser now raise only
/// for genuine internal invariants (a parser bug or a lexer-guaranteed shape), not for user syntax
/// errors — those flow through the diagnostic bag.
/// </para>
/// </remarks>
public class SparqlInvalidRequestException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="SparqlInvalidRequestException"/> with a default message.
    /// </summary>
    public SparqlInvalidRequestException()
        : base("The SPARQL request is not valid and cannot be executed.")
    {
        Diagnostics = [];
    }

    /// <summary>
    /// Initializes a new <see cref="SparqlInvalidRequestException"/> with the given message.
    /// </summary>
    /// <param name="message">A description of why the request is invalid.</param>
    public SparqlInvalidRequestException(string message)
        : base(message)
    {
        Diagnostics = [];
    }

    /// <summary>
    /// Initializes a new <see cref="SparqlInvalidRequestException"/> with the given message and inner exception.
    /// </summary>
    /// <param name="message">A description of why the request is invalid.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public SparqlInvalidRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
        Diagnostics = [];
    }

    /// <summary>
    /// Initializes a new <see cref="SparqlInvalidRequestException"/> carrying the diagnostics that made the request invalid.
    /// </summary>
    /// <param name="message">A description of why the request is invalid.</param>
    /// <param name="diagnostics">The diagnostics the parse recorded; the error-severity entries are the reason the request is rejected.</param>
    public SparqlInvalidRequestException(string message, IReadOnlyList<Diagnostic> diagnostics)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Diagnostics = diagnostics;
    }

    /// <summary>Gets the diagnostics the parse recorded, whose error-severity entries are the reason the request is invalid.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Throws a <see cref="SparqlInvalidRequestException"/> when a parse result carries error diagnostics,
    /// the guard an executing consumer uses to refuse a request that did not parse cleanly.
    /// </summary>
    /// <param name="result">The parse result to check.</param>
    /// <exception cref="SparqlInvalidRequestException"><paramref name="result"/> has error-severity diagnostics.</exception>
    public static void ThrowIfInvalid(ParseResult<SparqlRequest> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if(result.HasErrors)
        {
            throw new SparqlInvalidRequestException(DescribeErrors(result.Diagnostics), result.Diagnostics);
        }
    }

    /// <summary>Builds a concise message summarising the error diagnostics.</summary>
    /// <param name="diagnostics">The recorded diagnostics.</param>
    /// <returns>A human-readable summary of the error diagnostics.</returns>
    private static string DescribeErrors(IReadOnlyList<Diagnostic> diagnostics)
    {
        int errors = 0;
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                errors++;
            }
        }

        return $"The SPARQL request has {errors} error diagnostic(s) and cannot be executed.";
    }
}
