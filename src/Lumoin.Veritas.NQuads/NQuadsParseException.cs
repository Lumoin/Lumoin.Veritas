using System;

namespace Lumoin.Veritas.NQuads;

/// <summary>
/// Indicates that N-Quads input could not be parsed.
/// </summary>
/// <remarks>
/// This exception is thrown for syntactically invalid input — malformed IRIs,
/// unterminated strings, missing statement terminators, and similar structural errors.
/// It is not thrown for semantic issues such as an invalid IRI scheme.
/// </remarks>
public sealed class NQuadsParseException: Exception
{
    /// <summary>
    /// Initializes a new <see cref="NQuadsParseException"/> with a default message.
    /// </summary>
    public NQuadsParseException()
        : base("N-Quads input could not be parsed.")
    {
    }

    /// <summary>
    /// Initializes a new <see cref="NQuadsParseException"/> with the given message.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    public NQuadsParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="NQuadsParseException"/> with the given message and inner exception.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public NQuadsParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="NQuadsParseException"/> with the given message and line number.
    /// </summary>
    /// <param name="message">A description of the parse error.</param>
    /// <param name="lineNumber">The one-based line number where the error occurred.</param>
    public NQuadsParseException(string message, int lineNumber)
        : base(message)
    {
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Gets the one-based line number where the parse error occurred.
    /// </summary>
    public int LineNumber { get; }
}