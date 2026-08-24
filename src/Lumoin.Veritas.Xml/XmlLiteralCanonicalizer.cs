using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Xml;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// Canonicalizes the content of an <c>rdf:parseType="Literal"</c> property element into the lexical value of the
/// resulting <c>rdf:XMLLiteral</c>.
/// </summary>
/// <remarks>
/// <para>
/// An XML literal must be reduced to one canonical byte sequence so that two literals carrying the same XML compare
/// equal and hash/canonicalize consistently (RDFC-1.0, signing, dataset isomorphism). The strategy serializes the
/// literal's verbatim inner UTF-8 bytes, hoisting the namespaces it inherits from ancestors onto the apex content
/// elements (the fragment is detached from its document, so the inherited namespaces have to be materialised onto it).
/// </para>
/// <para>
/// There is no single universally-agreed serialisation: <see cref="XmlLiteralCanonicalizers.DocumentOrder"/> hoists
/// the in-scope namespaces in <b>document declaration order</b> (the W3C RDF/XML test-corpus form), whereas
/// <see cref="XmlLiteralCanonicalizers.Canonical"/> sorts namespace declarations lexicographically by prefix per
/// W3C Canonical XML 1.0. This delegate is the seam that selects between them and is the compatibility knob for
/// matching a peer's XML-literal form.
/// </para>
/// </remarks>
/// <param name="innerContent">The verbatim UTF-8 bytes of the literal element's inner content (the markup between its start and end tags), undecoded.</param>
/// <param name="inScopeNamespaces">The namespaces in scope at the literal property element, in document declaration order, to hoist onto the apex content elements.</param>
/// <returns>The canonical lexical form of the XML literal.</returns>
public delegate Utf8String XmlLiteralCanonicalizer(ReadOnlyMemory<byte> innerContent, IReadOnlyList<XmlNamespaceBinding> inScopeNamespaces);
