using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Core.Xml;

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

    /// <summary>The lazily materialized attribute list; <see langword="null"/> until the reader lands the first attribute, so an attribute-less element allocates no list.</summary>
    private List<XmlByteAttribute>? attributes;

    /// <summary>The lazily materialized child list; <see langword="null"/> until the reader lands the first child, so a leaf element allocates no list.</summary>
    private List<XmlByteNode>? children;

    /// <summary>The shared empty attribute list an attribute-less element exposes; never mutated — the reader appends only through <see cref="MaterializeAttributes"/>.</summary>
    private static List<XmlByteAttribute> EmptyAttributes { get; } = [];

    /// <summary>The shared empty child list a leaf element exposes; never mutated — the reader appends only through <see cref="MaterializeChildren"/>.</summary>
    private static List<XmlByteNode> EmptyChildren { get; } = [];

    /// <summary>The element's attributes, in document order; namespace declarations are not included. An attribute-less element returns a shared empty instance, which callers only read.</summary>
    public List<XmlByteAttribute> Attributes => attributes ?? EmptyAttributes;

    /// <summary>The element's child elements, in document order. A leaf element returns a shared empty instance, which callers only read.</summary>
    public List<XmlByteNode> Children => children ?? EmptyChildren;

    /// <summary>The attribute list for the reader to fill, materialized at exact capacity on first need.</summary>
    /// <param name="capacity">The number of attributes the tag carries (namespace declarations excluded).</param>
    /// <returns>The materialized list.</returns>
    internal List<XmlByteAttribute> MaterializeAttributes(int capacity)
    {
        return attributes ??= new List<XmlByteAttribute>(capacity);
    }

    /// <summary>The child list for the reader to append to, materialized on first need.</summary>
    /// <returns>The materialized list.</returns>
    internal List<XmlByteNode> MaterializeChildren()
    {
        return children ??= [];
    }

    /// <summary>The element's directly-contained character data with references resolved; empty for an element that carries only children.</summary>
    public Utf8String Text { get; set; }

    /// <summary>The immutable upward chain of namespace declarations enclosing this element; <see langword="null"/> when no ancestor (or this element) declares a namespace. The flattened in-scope list is materialized from it lazily by <see cref="InScopeNamespaces"/>.</summary>
    internal XmlNamespaceScope? Scope { get; init; }

    /// <summary>The flattened in-scope list, materialized from <see cref="Scope"/> on first read and cached.</summary>
    private IReadOnlyList<XmlNamespaceBinding>? MaterializedScope { get; set; }

    /// <summary>
    /// The namespace bindings in scope at this element — the inherited bindings extended with this element's own
    /// declarations — in document declaration order (outermost first, the nearest declaration of a prefix supplying its
    /// IRI). Empty for an element under no namespace declarations. Used to hoist the in-scope namespaces onto a detached
    /// <c>rdf:parseType="Literal"</c> fragment, the one place canonicalization needs the prefixes the reader otherwise
    /// resolves away. Materialized lazily from <see cref="Scope"/> (the reader folds no in-scope list for the
    /// overwhelming majority of elements, which are never literals), then cached for re-reads.
    /// </summary>
    public IReadOnlyList<XmlNamespaceBinding> InScopeNamespaces
    {
        get
        {
            MaterializedScope ??= Scope is { } scope ? scope.Materialize() : EmptyBindings;

            return MaterializedScope;
        }
    }

    /// <summary>The shared empty in-scope binding list for an element under no namespace declarations (never mutated).</summary>
    private static IReadOnlyList<XmlNamespaceBinding> EmptyBindings { get; } = [];

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

/// <summary>
/// One frame of an element's immutable, upward namespace-declaration chain: the element's own declarations and a link
/// to the enclosing frame. An element references the chain rather than a flattened in-scope list, so the reader folds
/// no list for the overwhelming majority of elements (which are never <c>rdf:parseType="Literal"</c> captures); the
/// flattened document-order list is materialized on demand by <see cref="XmlByteNode.InScopeNamespaces"/>. The chain is
/// shared and never mutated, so detaching a streamed subtree leaves every node's chain intact.
/// </summary>
internal sealed class XmlNamespaceScope
{
    /// <summary>The enclosing scope frame, or <see langword="null"/> at the outermost declaring element.</summary>
    internal XmlNamespaceScope? Parent { get; init; }

    /// <summary>This element's own namespace declarations, in document declaration order.</summary>
    internal IReadOnlyList<XmlNamespaceBinding> Local { get; init; } = [];

    /// <summary>
    /// Materializes the in-scope bindings in document declaration order by replaying the chain outermost-first — the
    /// same left fold the reader once built eagerly: a redeclared prefix keeps its outermost position with the nearest
    /// (innermost) IRI, a new prefix appends.
    /// </summary>
    /// <returns>The in-scope bindings in document declaration order.</returns>
    internal IReadOnlyList<XmlNamespaceBinding> Materialize()
    {
        Stack<IReadOnlyList<XmlNamespaceBinding>> frames = new();
        for(XmlNamespaceScope? frame = this; frame is not null; frame = frame.Parent)
        {
            frames.Push(frame.Local);
        }

        List<XmlNamespaceBinding> merged = [];
        while(frames.Count > 0)
        {
            foreach(XmlNamespaceBinding binding in frames.Pop())
            {
                int index = IndexOfPrefix(merged, binding.Prefix.Span);
                if(index >= 0)
                {
                    merged[index] = binding;
                }
                else
                {
                    merged.Add(binding);
                }
            }
        }

        return merged;
    }

    /// <summary>The index of the binding with a given prefix, or <c>-1</c>.</summary>
    /// <param name="bindings">The bindings to search.</param>
    /// <param name="prefix">The prefix to find.</param>
    /// <returns>The index, or <c>-1</c>.</returns>
    private static int IndexOfPrefix(List<XmlNamespaceBinding> bindings, ReadOnlySpan<byte> prefix)
    {
        for(int i = 0; i < bindings.Count; i++)
        {
            if(bindings[i].Prefix.Span.SequenceEqual(prefix))
            {
                return i;
            }
        }

        return -1;
    }
}
