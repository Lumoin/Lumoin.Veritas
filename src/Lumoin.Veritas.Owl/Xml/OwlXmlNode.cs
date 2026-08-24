using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>One attribute of an OWL/XML element: its raw qualified name and its entity-decoded value.</summary>
/// <param name="Name">The attribute's qualified name as written, including any prefix (for example <c>xml:lang</c>).</param>
/// <param name="Value">The attribute value with XML entity and character references resolved. A zero-copy window over the reader's buffer when the value carries no reference, otherwise its own decoded bytes.</param>
/// <param name="Start">The inclusive start byte offset of the attribute in the source.</param>
/// <param name="End">The exclusive end byte offset of the attribute in the source.</param>
internal readonly record struct OwlXmlAttribute(Utf8String Name, Utf8String Value, int Start, int End);

/// <summary>
/// One element of the OWL/XML element tree: its resolved OWL element discriminant,
/// its local name and namespace, its attributes, its element children, and any
/// character data it directly contains.
/// </summary>
internal sealed class OwlXmlNode
{
    /// <summary>The element's OWL/XML discriminant, or <see cref="OwlXmlElement.Unknown"/> when it is not an OWL element or sits outside the OWL namespace.</summary>
    public OwlXmlElement Element { get; init; }

    /// <summary>The element's local name (the part after any prefix). A zero-copy window over the reader's buffer.</summary>
    public Utf8String LocalName { get; init; }

    /// <summary>The element's resolved namespace IRI, or an empty value when no namespace is in scope.</summary>
    public Utf8String NamespaceIri { get; init; }

    /// <summary>The element's attributes, in document order. The XML namespace declarations and reserved attributes are kept alongside the OWL attributes.</summary>
    public List<OwlXmlAttribute> Attributes { get; } = [];

    /// <summary>The element's child elements, in document order.</summary>
    public List<OwlXmlNode> Children { get; } = [];

    /// <summary>The element's directly-contained character data with references resolved; empty for the element-content elements that carry only children.</summary>
    public Utf8String Text { get; set; }

    /// <summary>The element's source extent: the run from its opening delimiter to its closing delimiter.</summary>
    public SourceSpan Span { get; set; }

    /// <summary>The byte offset the element's span starts at, held until the closing tag fixes the end.</summary>
    public int SpanStart { get; init; }

    /// <summary>Finds an attribute by its exact qualified name.</summary>
    /// <param name="name">The attribute name to match.</param>
    /// <returns>The attribute value, or <see langword="null"/> when the element has no such attribute.</returns>
    public Utf8String? Attribute(System.ReadOnlySpan<byte> name)
    {
        foreach(OwlXmlAttribute attribute in Attributes)
        {
            if(attribute.Name.SequenceEqual(name))
            {
                return attribute.Value;
            }
        }

        return null;
    }
}
