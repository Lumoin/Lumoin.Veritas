using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Xml;

/// <summary>One attribute of an <see cref="XmlByteNode"/>: its namespace-resolved name and its entity-decoded value.</summary>
/// <param name="LocalName">The attribute's local name (the part after any prefix).</param>
/// <param name="NamespaceIri">The attribute's namespace IRI, or an empty value for an unprefixed attribute (which is in no namespace).</param>
/// <param name="Value">The attribute value with XML references resolved.</param>
public readonly record struct XmlByteAttribute(Utf8String LocalName, Utf8String NamespaceIri, Utf8String Value);

/// <summary>One namespace binding in scope at an <see cref="XmlByteNode"/>: a prefix and the namespace IRI it is bound to.</summary>
/// <param name="Prefix">The prefix, or an empty value for the default-namespace binding.</param>
/// <param name="NamespaceIri">The namespace IRI the prefix is bound to.</param>
public readonly record struct XmlNamespaceBinding(Utf8String Prefix, Utf8String NamespaceIri);

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

    /// <summary>
    /// The namespace bindings in scope at this element — the inherited bindings extended with this element's own
    /// declarations — in document declaration order (outermost first, the nearest declaration of a prefix supplying its
    /// IRI). Empty for an element under no namespace declarations. Used to hoist the in-scope namespaces onto a detached
    /// <c>rdf:parseType="Literal"</c> fragment, the one place canonicalization needs the prefixes the reader otherwise resolves away.
    /// </summary>
    public IReadOnlyList<XmlNamespaceBinding> InScopeNamespaces { get; init; } = [];

    /// <summary>
    /// The source span covering this element — from its start tag's <c>&lt;</c> through just past its end tag's
    /// <c>&gt;</c>, or the single tag of an empty element. <see cref="SourceSpan.None"/> on the synthetic document
    /// root (which a consumer never sees); on every element the reader returns, the span locates that element.
    /// </summary>
    public SourceSpan Span { get; internal set; }

    /// <summary>The byte offset of this element's start tag's <c>&lt;</c>; the reader pairs it with the end-tag offset to fix <see cref="Span"/> when the element closes.</summary>
    internal int SpanStart { get; init; }

    /// <summary>The start offset of this element's verbatim inner content (just past the start tag's <c>&gt;</c>) in the source bytes; <c>0</c> for an empty element.</summary>
    /// <remarks>Paired with <see cref="ContentEnd"/> to recover the raw, undecoded inner markup an XML literal canonicalizes; meaningful only against the same buffer the reader parsed.</remarks>
    internal int ContentStart { get; init; }

    /// <summary>The end offset of this element's verbatim inner content (the <c>&lt;</c> of the end tag) in the source bytes; <c>0</c> for an empty element.</summary>
    internal int ContentEnd { get; set; }

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
