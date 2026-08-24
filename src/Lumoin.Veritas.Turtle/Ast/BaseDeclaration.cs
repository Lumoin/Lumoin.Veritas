using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A <c>@base</c> or <c>BASE</c> declaration setting the base IRI
/// used to resolve subsequent relative IRIs.
/// </summary>
[DebuggerDisplay("@base <{IriText,nq}> #{NodeId}")]
public sealed class BaseDeclaration: Statement
{
    /// <summary>
    /// Initialises a new <see cref="BaseDeclaration"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the directive.</param>
    /// <param name="iri">The base IRI.</param>
    public BaseDeclaration(int nodeId, SourceSpan span, IriTerm iri)
        : base(nodeId, span)
    {
        Iri = iri;
    }

    /// <summary>Gets the base IRI.</summary>
    public IriTerm Iri { get; }

    private string IriText => Iri.Value.ToString();
}
