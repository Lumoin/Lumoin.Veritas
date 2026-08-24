using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Lexer;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A term (in subject, predicate, or object position) the parser could not parse. Recovery emits this
/// in place of a well-formed <see cref="Term"/>, spanning the tokens it skipped to resynchronise.
/// </summary>
/// <remarks>
/// The contributing diagnostics are held in the <see cref="Lumoin.Veritas.Core.Diagnostics.ParseResult{TTree}"/>
/// bag; <see cref="DiagnosticCodes"/> lists the codes that fired while this node was built, and
/// <see cref="SkippedTokens"/> records the consumed tokens. An error term contributes nothing to the
/// document's emitted quads.
/// </remarks>
[DebuggerDisplay("ErrorTerm {ExpectedProductionText,nq} ({SkippedTokens.Length} skipped) #{NodeId}")]
public sealed class ErrorTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="ErrorTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the skipped tokens and the point of first failure.</param>
    /// <param name="expectedProduction">The canonical name of the grammar production the parser expected here.</param>
    /// <param name="diagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
    /// <param name="skippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
    public ErrorTerm(
        int nodeId,
        SourceSpan span,
        Utf8String expectedProduction,
        ImmutableArray<Utf8String> diagnosticCodes,
        ImmutableArray<TurtleToken> skippedTokens)
        : base(nodeId, span)
    {
        ExpectedProduction = expectedProduction;
        DiagnosticCodes = diagnosticCodes;
        SkippedTokens = skippedTokens;
    }

    /// <summary>Gets the canonical name of the grammar production the parser expected here.</summary>
    public Utf8String ExpectedProduction { get; }

    /// <summary>Gets the diagnostic codes that fired while constructing this node.</summary>
    public ImmutableArray<Utf8String> DiagnosticCodes { get; }

    /// <summary>Gets the tokens the resync logic consumed to settle on this node's span.</summary>
    public ImmutableArray<TurtleToken> SkippedTokens { get; }

    /// <summary>Gets the expected-production name as text for the debugger display.</summary>
    private string ExpectedProductionText => ExpectedProduction.ToString();
}
