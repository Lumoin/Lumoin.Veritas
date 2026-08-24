using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A blank-node term: <c>_:label</c> as written in the document or
/// <c>[]</c> when the parser allocated a fresh label for an anonymous
/// blank node.
/// </summary>
/// <remarks>
/// <see cref="Label"/> is the textual label without the <c>_:</c>
/// prefix. Labels parsed from source preserve their text exactly;
/// labels minted by the parser for anonymous blank nodes (the
/// <c>[]</c> and blank-node-property-list sugar) follow the
/// convention <c>b{N}</c> with <c>N</c> being a monotonic counter on
/// the document.
/// </remarks>
[DebuggerDisplay("_:{LabelText,nq} #{NodeId}")]
public sealed class BlankNodeTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="BlankNodeTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the blank-node label.</param>
    /// <param name="label">The label without the <c>_:</c> prefix.</param>
    public BlankNodeTerm(int nodeId, SourceSpan span, Utf8String label)
        : base(nodeId, span)
    {
        Label = label;
    }

    /// <summary>Gets the blank-node label without the <c>_:</c> prefix.</summary>
    public Utf8String Label { get; }

    private string LabelText => Label.ToString();
}
