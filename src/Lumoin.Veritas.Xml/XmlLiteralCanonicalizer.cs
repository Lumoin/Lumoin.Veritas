using System.Xml.Linq;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// Canonicalizes the content of an <c>rdf:parseType="Literal"</c> property element into the lexical value of the
/// resulting <c>rdf:XMLLiteral</c>.
/// </summary>
/// <remarks>
/// <para>
/// An XML literal must be reduced to one canonical byte sequence so that two literals carrying the same XML compare
/// equal and hash/canonicalize consistently (RDFC-1.0, signing, dataset isomorphism). The fragment is detached from
/// its document, so the in-scope namespaces it inherits from ancestors have to be materialised onto it.
/// </para>
/// <para>
/// There is no single universally-agreed serialisation: the W3C RDF/XML test corpus (and mainstream toolkits such as
/// raptor / rdflib) hoist the in-scope namespaces in <b>document declaration order</b>, whereas W3C Canonical XML 1.0
/// sorts namespace declarations lexicographically by prefix. This delegate is the seam that selects between them —
/// <see cref="XmlLiteralCanonicalizers.DocumentOrder"/> for RDF-toolkit interop and conformance,
/// <see cref="XmlLiteralCanonicalizers.Canonical"/> for strict Canonical XML 1.0 — and is the compatibility knob for
/// matching any other implementation's XML-literal form.
/// </para>
/// </remarks>
/// <param name="literalProperty">The <c>rdf:parseType="Literal"</c> property element whose child content is the literal.</param>
/// <returns>The canonical lexical form of the XML literal.</returns>
public delegate string XmlLiteralCanonicalizer(XElement literalProperty);
