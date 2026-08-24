using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A <c>@prefix</c> or <c>PREFIX</c> declaration binding a textual
/// prefix to an IRI namespace for subsequent prefixed-name expansion.
/// </summary>
/// <remarks>
/// Both the Turtle <c>@prefix foaf: &lt;...&gt; .</c> form and the
/// SPARQL-style <c>PREFIX foaf: &lt;...&gt;</c> form (no trailing
/// period) lower to this node. The parser captures the original
/// surface form in <see cref="Span"/> so editor consumers can preserve
/// it on round-trip in a future Level 2 writer.
/// </remarks>
[DebuggerDisplay("@prefix {PrefixText,nq}: <{IriText,nq}> #{NodeId}")]
public sealed class PrefixDeclaration: Statement
{
    /// <summary>
    /// Initialises a new <see cref="PrefixDeclaration"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the directive.</param>
    /// <param name="prefix">The prefix portion, empty for the default prefix.</param>
    /// <param name="iri">The namespace IRI.</param>
    public PrefixDeclaration(int nodeId, SourceSpan span, Utf8String prefix, IriTerm iri)
        : base(nodeId, span)
    {
        Prefix = prefix;
        Iri = iri;
    }

    /// <summary>Gets the prefix portion of the declaration.</summary>
    public Utf8String Prefix { get; }

    /// <summary>Gets the namespace IRI bound to the prefix.</summary>
    public IriTerm Iri { get; }

    private string PrefixText => Prefix.ToString();

    private string IriText => Iri.Value.ToString();
}
