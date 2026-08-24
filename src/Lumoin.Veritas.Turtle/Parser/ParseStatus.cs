namespace Lumoin.Veritas.Turtle.Parser;

/// <summary>
/// The outcome of <see cref="TurtleParser.TryParseStatement"/> when the parser is fed tokens
/// incrementally.
/// </summary>
internal enum ParseStatus
{
    /// <summary>A statement was parsed and is available to the caller.</summary>
    Produced,

    /// <summary>The current statement needs tokens that have not been fed yet; feed more and call again.</summary>
    NeedMore,

    /// <summary>The end-of-input token was reached at a statement boundary; no more statements remain.</summary>
    Completed
}
