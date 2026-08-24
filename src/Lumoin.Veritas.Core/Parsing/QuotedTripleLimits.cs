namespace Lumoin.Veritas.Core.Parsing;

/// <summary>
/// Shared limits for RDF-star quoted-triple (<c>TripleTerm</c>) nesting. The iterative term walkers
/// enforce <see cref="MaxNestingDepth"/> so a pathologically deep quoted triple raises a catchable
/// <see cref="TripleTermDepthLimitException"/> instead of exhausting the call stack (or, after the walks
/// became iterative, growing an unbounded heap work-stack).
/// </summary>
public static class QuotedTripleLimits
{
    /// <summary>The maximum quoted-triple nesting depth a term may have, mirroring the CBOR and JSONata nesting caps.</summary>
    public const int MaxNestingDepth = 64;
}
