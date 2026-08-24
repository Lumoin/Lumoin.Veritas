using System;
using System.Globalization;

namespace Lumoin.Veritas.Core.Parsing;

/// <summary>
/// Thrown when an RDF-star quoted-triple (<c>TripleTerm</c>) is nested deeper than
/// <see cref="QuotedTripleLimits.MaxNestingDepth"/>. The iterative term walkers raise this catchable
/// exception rather than letting a pathologically deep quoted triple exhaust the call stack or grow an
/// unbounded heap work-stack. The exception names the depth reached and the cap so callers can diagnose
/// precisely what to lift if the input is legitimate.
/// </summary>
public sealed class TripleTermDepthLimitException: InvalidOperationException
{
    /// <summary>Initialises a new <see cref="TripleTermDepthLimitException"/> with default values.</summary>
    public TripleTermDepthLimitException()
        : this(0, 0)
    {
    }

    /// <summary>Initialises a new <see cref="TripleTermDepthLimitException"/> with a message.</summary>
    /// <param name="message">The exception message.</param>
    public TripleTermDepthLimitException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new <see cref="TripleTermDepthLimitException"/> with a message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TripleTermDepthLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new <see cref="TripleTermDepthLimitException"/>.</summary>
    /// <param name="depth">The quoted-triple nesting depth reached.</param>
    /// <param name="limit">The configured maximum nesting depth.</param>
    public TripleTermDepthLimitException(int depth, int limit)
        : base(string.Create(CultureInfo.InvariantCulture, $"Quoted-triple nesting depth {depth} exceeds the maximum of {limit}."))
    {
        Depth = depth;
        Limit = limit;
    }

    /// <summary>Gets the quoted-triple nesting depth reached.</summary>
    public int Depth { get; }

    /// <summary>Gets the configured maximum nesting depth.</summary>
    public int Limit { get; }
}
