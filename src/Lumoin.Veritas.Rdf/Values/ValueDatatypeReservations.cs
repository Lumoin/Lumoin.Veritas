using System;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// The reservation gate over value-datatype registration: the union of the whole XSD namespace, the whole
/// RDF namespace, and every datatype IRI the engine's own <see cref="ValueSpaceClassifier"/> models. A
/// reserved IRI is never registrable, so no XSD-typed and no language-tagged literal can ever reach a
/// registered definition — the engine's built-in semantics stay authoritative structurally, not merely by
/// convention. The classifier leg consults <see cref="ValueSpaceClassifier.Classify"/> directly, so a
/// future classifier addition is reserved the moment it exists and the gate cannot drift from the built-in
/// set.
/// </summary>
public static class ValueDatatypeReservations
{
    /// <summary>The XSD namespace prefix bytes (<c>http://www.w3.org/2001/XMLSchema#</c>).</summary>
    private static ReadOnlyMemory<byte> XsdNamespacePrefix { get; } = "http://www.w3.org/2001/XMLSchema#"u8.ToArray();

    /// <summary>The RDF namespace prefix bytes (<c>http://www.w3.org/1999/02/22-rdf-syntax-ns#</c>).</summary>
    private static ReadOnlyMemory<byte> RdfNamespacePrefix { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"u8.ToArray();

    /// <summary>Whether the datatype IRI is reserved against value-datatype registration.</summary>
    /// <param name="datatypeIri">The candidate datatype IRI.</param>
    /// <returns><see langword="true"/> when the IRI is in the XSD or RDF namespace or is modelled by the value-space classifier.</returns>
    public static bool IsReserved(Utf8String datatypeIri)
    {
        ReadOnlySpan<byte> iri = datatypeIri.Span;

        return iri.StartsWith(XsdNamespacePrefix.Span)
            || iri.StartsWith(RdfNamespacePrefix.Span)
            || ValueSpaceClassifier.Classify(datatypeIri) != ValueSpace.Unknown;
    }
}
