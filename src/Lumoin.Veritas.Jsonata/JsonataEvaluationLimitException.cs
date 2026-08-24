using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Indicates that evaluation of a well-formed JSONata expression exceeded a configured resource bound.
/// </summary>
/// <remarks>
/// The <see cref="Limit"/> property identifies which <see cref="JsonataLimits"/> bound was reached.
/// A limit breach is distinct from a parse error: the expression is valid yet too large or too deep
/// to evaluate within the configured bounds. When the bound corresponds to a JSONata specification error —
/// a non-terminating or too-deeply recursive evaluation — the <see cref="Code"/> property carries that code
/// (<see cref="WellKnownJsonataErrors.NonTerminatingRecursion"/>, <c>U1001</c>); otherwise it is the empty
/// <see cref="Utf8String"/>, because the bound is an engine-internal safety guard with no spec code.
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

    /// <summary>Initializes a new <see cref="JsonataEvaluationLimitException"/> for a specific bound, carrying the JSONata error code the bound corresponds to.</summary>
    /// <param name="limit">The bound that was exceeded.</param>
    /// <param name="code">The JSONata error code for the condition (a <see cref="WellKnownJsonataErrors"/> member), or the empty <see cref="Utf8String"/> when the bound carries no spec code.</param>
    /// <param name="message">A description of the limit that was exceeded.</param>
    public JsonataEvaluationLimitException(JsonataLimit limit, Utf8String code, string message)
        : base(message)
    {
        Limit = limit;
        Code = code;
    }

    /// <summary>Gets the bound that was exceeded.</summary>
    public JsonataLimit Limit { get; }

    /// <summary>Gets the JSONata error code this bound corresponds to (a <see cref="WellKnownJsonataErrors"/> member such as <see cref="WellKnownJsonataErrors.NonTerminatingRecursion"/>), or the empty <see cref="Utf8String"/> when the bound is an engine-internal guard with no spec code.</summary>
    public Utf8String Code { get; }
}
