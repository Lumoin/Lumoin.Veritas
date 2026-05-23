using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Writer knobs for <see cref="TurtleWriter"/>. All optional; sensible
/// defaults apply when callers do not supply a value.
/// </summary>
/// <remarks>
/// <para>
/// Custom prefix mappings let callers control the prefixes the writer
/// emits at the top of the document. The writer will declare any
/// supplied prefix whose namespace is referenced by any quad it
/// emits; prefixes whose namespace is never referenced are omitted.
/// </para>
/// <para>
/// Indentation controls the per-level indent string used when the
/// writer chooses to break a predicate-object list across lines.
/// </para>
/// </remarks>
public sealed record TurtleWriterOptions
{
    /// <summary>Gets the explicit prefix mappings the writer should consider when emitting prefixed names.</summary>
    public IReadOnlyDictionary<Utf8String, Utf8String>? Prefixes { get; init; }

    /// <summary>Gets the base IRI written as a <c>@base</c> directive at the top of the document.</summary>
    public Utf8String? BaseIri { get; init; }

    /// <summary>Gets the indent string used for nested constructs. Defaults to two spaces.</summary>
    public string Indent { get; init; } = "  ";

    /// <summary>Gets a value indicating whether the writer should auto-declare common namespace prefixes (rdf, rdfs, xsd, owl, sh, skos).</summary>
    public bool AutoDeclareCommonPrefixes { get; init; } = true;
}
