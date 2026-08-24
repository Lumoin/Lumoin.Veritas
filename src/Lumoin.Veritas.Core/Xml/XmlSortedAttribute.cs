namespace Lumoin.Veritas.Core.Xml;

/// <summary>
/// An attribute paired with its computed Canonical XML sort key: the namespace IRI its prefix
/// resolves to, then its local name. The serializer resolves the prefix against its own scope, so
/// the key travels with the attribute rather than being recomputed during the sort.
/// </summary>
/// <param name="Attribute">The attribute.</param>
/// <param name="NamespaceIri">The attribute's namespace IRI sort key; empty for an unprefixed attribute, which is in no namespace.</param>
/// <param name="LocalName">The attribute's local-name sort key.</param>
public readonly record struct XmlSortedAttribute(XmlScanAttribute Attribute, Utf8String NamespaceIri, Utf8String LocalName);
