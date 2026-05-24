using System;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Indicates that Turtle or TriG input could not be lexed or parsed.
/// </summary>
/// <remarks>
/// <para>
/// Thrown for syntactic errors: malformed IRIs, unterminated strings,
/// invalid UTF-8 sequences, missing statement terminators, invalid
/// escape forms, and similar structural problems. Semantic issues
/// (an unresolved prefix, a relative IRI without a base, an invalid
/// language tag) are also surfaced through this exception so callers
/// see a single failure type from the reader.
/// </para>
/// <para>
/// The <see cref="Span"/> property carries the byte and line/column
/// range of the offending input when one is available; programmatic
/// construction without a parsed source uses <see cref="SourceSpan.None"/>.
/// </para>
/// </remarks>
public class TurtleParseException: Exception
{
    /// <summary>
    /// Initializes a new <see cref="TurtleParseException"/> with a default message.
    /// </summary>
    public TurtleParseException()
        : base("Turtle input could not be parsed.")
    {
        Span = SourceSpan.None;
    }

    /// <summary>
    /// Initializes a new <see cref="TurtleParseException"/> with the given message.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    public TurtleParseException(string message)
        : base(message)
    {
        Span = SourceSpan.None;
    }

    /// <summary>
    /// Initializes a new <see cref="TurtleParseException"/> with the given message and inner exception.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public TurtleParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Span = SourceSpan.None;
    }

    /// <summary>
    /// Initializes a new <see cref="TurtleParseException"/> with the given message and source span.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="span">The source span identifying the offending input.</param>
    public TurtleParseException(string message, SourceSpan span)
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
