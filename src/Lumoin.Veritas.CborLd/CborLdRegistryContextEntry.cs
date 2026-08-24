using System.Collections.Generic;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// One processed entry in the conversion algorithm's <c>contextMap</c>:
/// the resolved active context plus its term map. Built up as the
/// algorithm walks <c>@context</c> entries (W3C CBOR-LD 1.0 §5.4.2 step
/// 1).
/// </summary>
internal sealed class CborLdRegistryContextEntry
{
    /// <summary>Gets or sets the resolved active context.</summary>
    public LinkedDataContext Context { get; set; } = LinkedDataContext.Empty;

    /// <summary>Gets the term-name -> term-definition map.</summary>
    public Dictionary<string, TermDefinition> TermMap { get; init; } = [];
}
