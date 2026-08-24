using System.Globalization;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Lexer;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Context handed to a <see cref="TurtleReaderLimits.TokenGrowthGuard"/> as a token's decoded
/// length grows.
/// </summary>
/// <remarks>
/// <see cref="ProposedByteLength"/> is the total decoded length, in bytes, the token would reach
/// if the current growth proceeds. The start position locates the offending token in the source.
/// </remarks>
/// <param name="Kind">The kind of token being decoded.</param>
/// <param name="ProposedByteLength">The decoded byte length the token would reach.</param>
/// <param name="StartByte">Zero-based byte offset where the token began.</param>
/// <param name="StartLine">Zero-based line index where the token began.</param>
/// <param name="StartColumn">Zero-based column index where the token began.</param>
public readonly record struct TokenGrowthContext(
    TurtleTokenKind Kind,
    long ProposedByteLength,
    int StartByte,
    int StartLine,
    int StartColumn);

/// <summary>
/// Configurable resource limits for the Turtle and TriG reader, guarding against adversarial or
/// pathological input that would otherwise force unbounded memory use.
/// </summary>
/// <remarks>
/// <para>
/// Each limit is a delegate with a protective default, so a host can keep the default value or
/// swap the policy entirely — per token kind, a dynamic budget, telemetry — without the reader
/// hard-coding a single rule.
/// </para>
/// <para>
/// The token-length guard fires when a token's decode buffer must grow, so it bounds the
/// contiguous memory any one token — an IRI or string literal whose decoded content is built up
/// before interning — can force. It is the placement that stops a single multi-gigabyte literal
/// from being assembled in memory.
/// </para>
/// </remarks>
public sealed class TurtleReaderLimits
{
    /// <summary>
    /// The default maximum decoded length, in bytes, of a single token: 64 MiB. Comfortably above
    /// legitimate large literals such as base64-embedded media, while stopping a runaway token.
    /// </summary>
    public const long DefaultMaxTokenByteLength = 64L * 1024 * 1024;

    /// <summary>
    /// Inspects a token's proposed decoded length and rejects it — typically by throwing
    /// <see cref="TurtleLimitExceededException"/> — when a policy bound is exceeded.
    /// </summary>
    /// <param name="context">The growth context for the token being decoded.</param>
    public delegate void TokenGrowthGuard(in TokenGrowthContext context);

    /// <summary>
    /// Gets the shared default limits: the default token-length cap and no other restriction.
    /// </summary>
    public static TurtleReaderLimits Default { get; } = new();

    /// <summary>
    /// Gets the guard invoked as a token's decode buffer grows. Defaults to enforcing
    /// <see cref="DefaultMaxTokenByteLength"/>.
    /// </summary>
    public TokenGrowthGuard OnTokenGrowth { get; init; } = EnforceDefaultMaxTokenByteLength;

    private static void EnforceDefaultMaxTokenByteLength(in TokenGrowthContext context)
    {
        if(context.ProposedByteLength > DefaultMaxTokenByteLength)
        {
            throw new TurtleLimitExceededException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Token of kind {context.Kind} starting at line {context.StartLine + 1} column {context.StartColumn + 1} exceeds the maximum length of {DefaultMaxTokenByteLength} bytes."),
                SourceSpan.SingleLine(context.StartByte, context.StartByte, context.StartLine, context.StartColumn, context.StartColumn));
        }
    }
}
