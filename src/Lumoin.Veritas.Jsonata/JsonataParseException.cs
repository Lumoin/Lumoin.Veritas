using System;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Indicates that JSONata expression text could not be lexed or parsed.
/// </summary>
/// <remarks>
/// The <see cref="Span"/> property carries the byte and line/column range of the offending input
/// when one is available; programmatic construction without a parsed source uses
/// <see cref="SourceSpan.None"/>.
/// </remarks>
public class JsonataParseException : Exception
{
    /// <summary>Initializes a new <see cref="JsonataParseException"/> with a default message.</summary>
    public JsonataParseException()
        : base("JSONata input could not be parsed.")
    {
        Span = SourceSpan.None;
    }

    /// <summary>Initializes a new <see cref="JsonataParseException"/> with the given message.</summary>
    /// <param name="message">A description of the parse error.</param>
    public JsonataParseException(string message)
        : base(message)
    {
        Span = SourceSpan.None;
    }

    /// <summary>Initializes a new <see cref="JsonataParseException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonataParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Span = SourceSpan.None;
    }

    /// <summary>Initializes a new <see cref="JsonataParseException"/> with the given message and source span.</summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="span">The source span identifying the offending input.</param>
    public JsonataParseException(string message, SourceSpan span)
        : base(message)
    {
        Span = span;
    }

    /// <summary>
    /// Gets the source span identifying the offending input, or <see cref="SourceSpan.None"/> when
    /// no span is available.
    /// </summary>
    public SourceSpan Span { get; }
}
