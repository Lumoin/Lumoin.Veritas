using System;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql;

/// <summary>
/// Indicates that SPARQL query text could not be lexed or parsed.
/// </summary>
/// <remarks>
/// <para>
/// Thrown for lexical errors (malformed IRIs, unterminated strings, invalid
/// escapes, invalid UTF-8) and for grammar errors (an unexpected token, an
/// unbound prefix, a relative IRI without a base, an unsupported SPARQL Update
/// keyword in this build). A single failure type surfaces from both the lexer
/// and the parser.
/// </para>
/// <para>
/// The <see cref="Span"/> property carries the byte and line/column range of the
/// offending input when one is available; programmatic construction without a
/// parsed source uses <see cref="SourceSpan.None"/>.
/// </para>
/// </remarks>
public class SparqlParseException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="SparqlParseException"/> with a default message.
    /// </summary>
    public SparqlParseException()
        : base("SPARQL input could not be parsed.")
    {
        Span = SourceSpan.None;
    }

    /// <summary>
    /// Initializes a new <see cref="SparqlParseException"/> with the given message.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    public SparqlParseException(string message)
        : base(message)
    {
        Span = SourceSpan.None;
    }

    /// <summary>
    /// Initializes a new <see cref="SparqlParseException"/> with the given message and inner exception.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public SparqlParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Span = SourceSpan.None;
    }

    /// <summary>
    /// Initializes a new <see cref="SparqlParseException"/> with the given message and source span.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="span">The source span identifying the offending input.</param>
    public SparqlParseException(string message, SourceSpan span)
        : base(message)
    {
        Span = span;
    }

    /// <summary>
    /// Gets the source span identifying the offending input, or
    /// <see cref="SourceSpan.None"/> when no span is available.
    /// </summary>
    public SourceSpan Span { get; }
}
