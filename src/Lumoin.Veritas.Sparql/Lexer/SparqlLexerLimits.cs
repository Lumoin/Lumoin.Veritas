using System.Globalization;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// Context handed to a <see cref="SparqlLexerLimits.TokenGrowthGuard"/> as a
/// token's decoded length grows.
/// </summary>
/// <remarks>
/// <see cref="ProposedByteLength"/> is the total decoded length, in bytes, the
/// token would reach if the current growth proceeds. The start position locates
/// the offending token in the source.
/// </remarks>
/// <param name="Kind">The kind of token being decoded.</param>
/// <param name="ProposedByteLength">The decoded byte length the token would reach.</param>
/// <param name="StartByte">Zero-based byte offset where the token began.</param>
/// <param name="StartLine">Zero-based line index where the token began.</param>
/// <param name="StartColumn">Zero-based column index where the token began.</param>
public readonly record struct SparqlTokenGrowthContext(
    SparqlTokenKind Kind,
    long ProposedByteLength,
    int StartByte,
    int StartLine,
    int StartColumn);

/// <summary>
/// Configurable resource limits for <see cref="SparqlLexer"/>, guarding against
/// adversarial or pathological query text that would otherwise force unbounded
/// memory use.
/// </summary>
/// <remarks>
/// <para>
/// The token-length guard fires when a token's decode buffer must grow, so it
/// bounds the contiguous memory any one token — an IRI or string literal whose
/// decoded content is assembled before interning — can force. Defaults are sized
/// to admit any reasonable hand-written query while stopping a runaway token.
/// </para>
/// </remarks>
public sealed class SparqlLexerLimits
{
    /// <summary>
    /// The default maximum decoded length, in bytes, of a single token: 1 MiB.
    /// Comfortably above any reasonable IRI or string literal in a query, while
    /// stopping a runaway token.
    /// </summary>
    public const long DefaultMaxTokenByteLength = 1L * 1024 * 1024;

    /// <summary>
    /// Inspects a token's proposed decoded length and rejects it — by throwing
    /// <see cref="SparqlParseException"/> — when a policy bound is exceeded.
    /// </summary>
    /// <param name="context">The growth context for the token being decoded.</param>
    public delegate void TokenGrowthGuard(in SparqlTokenGrowthContext context);

    /// <summary>
    /// Gets the shared default limits: the default token-length cap and no other restriction.
    /// </summary>
    public static SparqlLexerLimits Default { get; } = new();

    /// <summary>
    /// Gets the guard invoked as a token's decode buffer grows. Defaults to
    /// enforcing <see cref="DefaultMaxTokenByteLength"/>.
    /// </summary>
    public TokenGrowthGuard OnTokenGrowth { get; init; } = EnforceDefaultMaxTokenByteLength;

    private static void EnforceDefaultMaxTokenByteLength(in SparqlTokenGrowthContext context)
    {
        if(context.ProposedByteLength > DefaultMaxTokenByteLength)
        {
            throw new SparqlParseException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Token of kind {context.Kind} starting at line {context.StartLine + 1} column {context.StartColumn + 1} exceeds the maximum length of {DefaultMaxTokenByteLength} bytes."),
                SourceSpan.SingleLine(context.StartByte, context.StartByte, context.StartLine, context.StartColumn, context.StartColumn));
        }
    }
}
