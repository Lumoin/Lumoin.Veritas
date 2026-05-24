using System;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Indicates that otherwise well-formed Turtle or TriG input exceeded a configured resource
/// limit — for example, a single token longer than the reader's token-length cap.
/// </summary>
/// <remarks>
/// A limit breach is distinct from a syntax error: the input may be perfectly valid yet too
/// large to process within the configured bounds. The exception derives from
/// <see cref="TurtleParseException"/> so a single <c>catch</c> still covers every reader failure,
/// while callers that must tell resource exhaustion from malformed input — say, to answer with
/// "payload too large" rather than "bad request" — can catch this type directly.
/// </remarks>
public sealed class TurtleLimitExceededException: TurtleParseException
{
    /// <summary>
    /// Initializes a new <see cref="TurtleLimitExceededException"/> with a default message.
    /// </summary>
    public TurtleLimitExceededException()
        : base("A Turtle reader resource limit was exceeded.")
    {
    }

    /// <summary>
    /// Initializes a new <see cref="TurtleLimitExceededException"/> with the given message.
    /// </summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    public TurtleLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="TurtleLimitExceededException"/> with the given message and inner exception.
    /// </summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public TurtleLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="TurtleLimitExceededException"/> with the given message and source span.
    /// </summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    /// <param name="span">The source span identifying where the limit was reached.</param>
    public TurtleLimitExceededException(string message, SourceSpan span)
        : base(message, span)
    {
    }
}
