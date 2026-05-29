using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// The severity of a validation result, identified by its IRI.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.6 severity is an <em>IRI</em>, not a closed
/// enumeration: the specification defines three standard levels
/// (<see cref="Violation"/>, <see cref="Warning"/>, <see cref="Info"/>)
/// but explicitly permits a shape to carry any IRI as its
/// <c>sh:severity</c>, which is then echoed verbatim as the
/// <c>sh:resultSeverity</c> of every result the shape produces. This type
/// therefore wraps the severity IRI directly rather than enumerating the
/// three standard ones; the well-known levels are exposed as static
/// members for convenient construction and comparison.
/// </para>
/// <para>
/// Severity does not affect conformance: a report conforms iff it has no
/// results at all (§3.6), regardless of their severities. The severity is
/// purely advisory metadata carried through to the result.
/// </para>
/// <para>
/// The default severity when a shape declares none is
/// <see cref="Violation"/>.
/// </para>
/// </remarks>
/// <param name="Iri">The severity IRI (for example <c>sh:Violation</c> or a user-defined IRI).</param>
public readonly record struct Severity(Utf8String Iri)
{
    /// <summary><c>sh:Violation</c> — a hard constraint failure. The default.</summary>
    public static Severity Violation { get; } = new(ShaclSeverityVocabulary.Violation);

    /// <summary><c>sh:Warning</c> — a soft failure that does not invalidate the data.</summary>
    public static Severity Warning { get; } = new(ShaclSeverityVocabulary.Warning);

    /// <summary><c>sh:Info</c> — informational finding that does not represent a constraint failure.</summary>
    public static Severity Info { get; } = new(ShaclSeverityVocabulary.Info);
}
