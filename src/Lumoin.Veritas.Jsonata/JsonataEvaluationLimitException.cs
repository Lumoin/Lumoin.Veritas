using System;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Indicates that evaluation of a well-formed JSONata expression exceeded a configured resource bound.
/// </summary>
/// <remarks>
/// The <see cref="Limit"/> property identifies which <see cref="JsonataLimits"/> bound was reached.
/// A limit breach is distinct from a parse error: the expression is valid yet too large or too deep
/// to evaluate within the configured bounds.
/// </remarks>
public sealed class JsonataEvaluationLimitException : Exception
{
    /// <summary>Initializes a new <see cref="JsonataEvaluationLimitException"/> with a default message.</summary>
    public JsonataEvaluationLimitException()
        : base("A JSONata evaluation resource limit was exceeded.")
    {
        Limit = JsonataLimit.EvaluationSteps;
    }

    /// <summary>Initializes a new <see cref="JsonataEvaluationLimitException"/> with the given message.</summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    public JsonataEvaluationLimitException(string message)
        : base(message)
    {
        Limit = JsonataLimit.EvaluationSteps;
    }

    /// <summary>Initializes a new <see cref="JsonataEvaluationLimitException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonataEvaluationLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
        Limit = JsonataLimit.EvaluationSteps;
    }

    /// <summary>Initializes a new <see cref="JsonataEvaluationLimitException"/> for a specific bound.</summary>
    /// <param name="limit">The bound that was exceeded.</param>
    /// <param name="message">A description of the limit that was exceeded.</param>
    public JsonataEvaluationLimitException(JsonataLimit limit, string message)
        : base(message)
    {
        Limit = limit;
    }

    /// <summary>Gets the bound that was exceeded.</summary>
    public JsonataLimit Limit { get; }
}
