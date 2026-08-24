using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// An absolute or relative IRI parsed in angle-bracket form: <c>&lt;http://example.org/&gt;</c>.
/// </summary>
/// <remarks>
/// The lexer strips the angle brackets; <see cref="Value"/> carries the
/// raw IRI bytes. Relative IRIs are resolved against the document's
/// <c>@base</c> by the quad emitter, not by the parser, so the AST
/// preserves the original form.
/// </remarks>
[DebuggerDisplay("<{ValueText,nq}> #{NodeId}")]
public sealed class IriTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="IriTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the IRI including its delimiters.</param>
    /// <param name="value">The IRI bytes with the angle brackets stripped.</param>
    public IriTerm(int nodeId, SourceSpan span, Utf8String value)
        : base(nodeId, span)
    {
        Value = value;
    }

    /// <summary>Gets the IRI value without the angle-bracket delimiters.</summary>
    public Utf8String Value { get; }

    private string ValueText => Value.ToString();
}
