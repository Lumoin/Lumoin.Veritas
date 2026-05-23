using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A prefixed-name term: <c>foaf:name</c>, <c>:local</c>, or any other
/// <c>prefix:local</c> reference resolved through the document's
/// <c>@prefix</c> mappings.
/// </summary>
/// <remarks>
/// The parser does not expand prefixed names against the prefix
/// dictionary; expansion is the emitter's responsibility so the AST
/// preserves the surface form and editor consumers can show the
/// original token. A prefix with no namespace declaration in scope
/// triggers a <see cref="TurtleParseException"/> when the emitter
/// attempts to expand it.
/// </remarks>
[DebuggerDisplay("{PrefixText,nq}:{LocalText,nq} #{NodeId}")]
public sealed class PrefixedNameTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="PrefixedNameTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the prefixed name.</param>
    /// <param name="prefix">The prefix portion, empty for <c>:local</c>.</param>
    /// <param name="local">The local-name portion.</param>
    public PrefixedNameTerm(int nodeId, SourceSpan span, Utf8String prefix, Utf8String local)
        : base(nodeId, span)
    {
        Prefix = prefix;
        Local = local;
    }

    /// <summary>Gets the prefix portion of the name. May be empty for the default prefix.</summary>
    public Utf8String Prefix { get; }

    /// <summary>Gets the local portion of the name.</summary>
    public Utf8String Local { get; }

    private string PrefixText => Prefix.ToString();

    private string LocalText => Local.ToString();
}
