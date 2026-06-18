using System;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Indicates that otherwise well-formed JSONata expression text exceeded a configured lex- or
/// parse-time resource limit — for example, a single token longer than the reader's token-length cap.
/// </summary>
/// <remarks>
/// A limit breach is distinct from a syntax error: the input may be perfectly valid yet too large to
/// process within the configured bounds. The exception derives from <see cref="JsonataParseException"/>
/// so a single <c>catch</c> still covers every lex/parse failure, while callers that must tell
/// resource exhaustion from malformed input can catch this type directly. It is distinct from
/// <see cref="JsonataEvaluationLimitException"/>, which reports bounds reached during evaluation.
/// </remarks>
public sealed class JsonataLimitExceededException : JsonataParseException
{
    /// <summary>Initializes a new <see cref="JsonataLimitExceededException"/> with a default message.</summary>
    public JsonataLimitExceededException()
        : base("A JSONata reader resource limit was exceeded.")
    {
    }

    /// <summary>Initializes a new <see cref="JsonataLimitExceededException"/> with the given message.</summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    public JsonataLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="JsonataLimitExceededException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonataLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new <see cref="JsonataLimitExceededException"/> with the given message and source span.</summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    /// <param name="span">The source span identifying where the limit was reached.</param>
    public JsonataLimitExceededException(string message, SourceSpan span)
        : base(message, span)
    {
    }
}
