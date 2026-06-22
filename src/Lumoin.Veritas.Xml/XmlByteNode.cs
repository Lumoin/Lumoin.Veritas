using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Xml;

/// <summary>One attribute of an <see cref="XmlByteNode"/>: its namespace-resolved name and its entity-decoded value.</summary>
/// <param name="LocalName">The attribute's local name (the part after any prefix).</param>
/// <param name="NamespaceIri">The attribute's namespace IRI, or an empty value for an unprefixed attribute (which is in no namespace).</param>
/// <param name="Value">The attribute value with XML references resolved.</param>
public readonly record struct XmlByteAttribute(Utf8String LocalName, Utf8String NamespaceIri, Utf8String Value);

/// <summary>
/// One element of the tree an <see cref="XmlByteReader"/> builds: its namespace-resolved name, its attributes, its
/// element children, and any directly-contained character data. Lookups compare names as UTF-8 byte spans, so a
/// consumer never materialises a <see cref="string"/> to navigate the tree.
/// </summary>
public sealed class XmlByteNode
{
    /// <summary>The element's local name (the part after any prefix).</summary>
    public Utf8String LocalName { get; init; }

    /// <summary>The element's resolved namespace IRI, or an empty value when no namespace is in scope.</summary>
    public Utf8String NamespaceIri { get; init; }

    /// <summary>The element's attributes, in document order; namespace declarations are not included.</summary>
    public List<XmlByteAttribute> Attributes { get; } = [];

    /// <summary>The element's child elements, in document order.</summary>
    public List<XmlByteNode> Children { get; } = [];

    /// <summary>The element's directly-contained character data with references resolved; empty for an element that carries only children.</summary>
    public Utf8String Text { get; set; }

    /// <summary>Whether this element has the given local name and namespace IRI.</summary>
    /// <param name="localName">The local name to match.</param>
    /// <param name="namespaceIri">The namespace IRI to match.</param>
    /// <returns><see langword="true"/> when both match.</returns>
    public bool Matches(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceIri)
    {
        return LocalName.Span.SequenceEqual(localName) && NamespaceIri.Span.SequenceEqual(namespaceIri);
    }

    /// <summary>The value of an attribute by local name and namespace IRI.</summary>
    /// <param name="localName">The attribute local name.</param>
    /// <param name="namespaceIri">The attribute namespace IRI.</param>
    /// <returns>The attribute value, or <see langword="null"/> when the element has no such attribute.</returns>
    public Utf8String? Attribute(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceIri)
    {
        foreach(XmlByteAttribute attribute in Attributes)
        {
            if(attribute.LocalName.Span.SequenceEqual(localName) && attribute.NamespaceIri.Span.SequenceEqual(namespaceIri))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>The value of an unprefixed (no-namespace) attribute by local name.</summary>
    /// <param name="localName">The attribute local name.</param>
    /// <returns>The attribute value, or <see langword="null"/> when the element has no such attribute.</returns>
    public Utf8String? Attribute(ReadOnlySpan<byte> localName)
    {
        return Attribute(localName, default);
    }

    /// <summary>The first child element with the given local name and namespace IRI.</summary>
    /// <param name="localName">The child local name.</param>
    /// <param name="namespaceIri">The child namespace IRI.</param>
    /// <returns>The first matching child, or <see langword="null"/> when there is none.</returns>
    public XmlByteNode? Element(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceIri)
    {
        foreach(XmlByteNode child in Children)
        {
            if(child.Matches(localName, namespaceIri))
            {
                return child;
            }
        }

        return null;
    }
}
