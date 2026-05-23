using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A literal term. Carries the decoded lexical value plus exactly one
/// of: a datatype IRI reference, a language tag (with optional base
/// direction), or neither (implicit <c>xsd:string</c>).
/// </summary>
/// <remarks>
/// <para>
/// The lexer decodes string-literal escape sequences during scanning
/// so <see cref="Value"/> carries the resolved bytes. Numeric and
/// boolean literals appear here too: a numeric literal carries the
/// canonical text in <see cref="Value"/> and the relevant XSD
/// datatype in <see cref="Datatype"/>; a boolean literal carries
/// <c>true</c> or <c>false</c> as <see cref="Value"/> with the XSD
/// boolean datatype.
/// </para>
/// </remarks>
[DebuggerDisplay("\"{ValueText,nq}\" #{NodeId}")]
public sealed class LiteralTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="LiteralTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the literal lexeme.</param>
    /// <param name="value">The decoded lexical value.</param>
    /// <param name="datatype">The datatype IRI reference, when present.</param>
    /// <param name="language">The language tag without the leading <c>@</c>, when present.</param>
    /// <param name="direction">The base direction for a directional language-tagged string (RDF 1.2).</param>
    public LiteralTerm(
        int nodeId,
        SourceSpan span,
        Utf8String value,
        Term? datatype,
        Utf8String? language,
        TextDirection? direction)
        : base(nodeId, span)
    {
        Value = value;
        Datatype = datatype;
        Language = language;
        Direction = direction;
    }

    /// <summary>Gets the decoded lexical value.</summary>
    public Utf8String Value { get; }

    /// <summary>
    /// Gets the datatype reference, when explicitly written with
    /// <c>^^</c>. Either an <see cref="IriTerm"/> for absolute or
    /// relative IRIs or a <see cref="PrefixedNameTerm"/> for
    /// prefixed-name forms.
    /// </summary>
    public Term? Datatype { get; }

    /// <summary>Gets the language tag without the leading <c>@</c>, when present.</summary>
    public Utf8String? Language { get; }

    /// <summary>Gets the base text direction for directional language-tagged strings (RDF 1.2).</summary>
    public TextDirection? Direction { get; }

    private string ValueText => Value.ToString();
}
