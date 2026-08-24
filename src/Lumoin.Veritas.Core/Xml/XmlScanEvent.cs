using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Xml;

/// <summary>
/// A single event in the flat stream a byte-native XML scanner emits: a start tag, an end tag, a run of character
/// data, or the end of the document. Source offsets index into the document's UTF-8 bytes; a consumer resolves them
/// to a <see cref="Sourcing.SourceSpan"/> through the scanner, and folds the events into its own structure.
/// </summary>
/// <param name="Kind">Which kind of event this is; it selects which of the remaining members carry meaning.</param>
/// <param name="Name">For <see cref="XmlScanEventKind.StartElement"/>/<see cref="XmlScanEventKind.EndElement"/>, the element's qualified name as written; empty otherwise.</param>
/// <param name="Text">For <see cref="XmlScanEventKind.Text"/>, the entity-decoded, line-ending-normalized character data; empty otherwise.</param>
/// <param name="Attributes">For <see cref="XmlScanEventKind.StartElement"/>, the start tag's attributes in document order (namespace declarations included); empty otherwise. The concrete list type keeps every consumer's enumeration unboxed; an event with no attributes carries the shared <see cref="EmptyAttributes"/> instance, which is never mutated.</param>
/// <param name="IsEmpty">For <see cref="XmlScanEventKind.StartElement"/>, whether the element was written self-closing (<c>&lt;a/&gt;</c>) and therefore has no matching end tag.</param>
/// <param name="Start">For an element event, the byte offset of the tag's opening <c>&lt;</c>; <c>0</c> otherwise.</param>
/// <param name="Close">For an element event, the byte offset of the tag's closing <c>&gt;</c>; <c>0</c> otherwise.</param>
public readonly record struct XmlScanEvent(
    XmlScanEventKind Kind,
    Utf8String Name,
    Utf8String Text,
    List<XmlScanAttribute> Attributes,
    bool IsEmpty,
    int Start,
    int Close)
{
    /// <summary>The shared empty attribute list carried by every event that names no attributes; it is never mutated — the scanner appends only to lists it allocates per tag.</summary>
    internal static List<XmlScanAttribute> EmptyAttributes { get; } = [];

    /// <summary>Builds a start-element event.</summary>
    /// <param name="name">The element's qualified name as written.</param>
    /// <param name="attributes">The start tag's attributes in document order.</param>
    /// <param name="isEmpty">Whether the element was written self-closing.</param>
    /// <param name="start">The byte offset of the start tag's <c>&lt;</c>.</param>
    /// <param name="close">The byte offset of the start tag's <c>&gt;</c>.</param>
    /// <returns>The start-element event.</returns>
    public static XmlScanEvent StartElement(Utf8String name, List<XmlScanAttribute> attributes, bool isEmpty, int start, int close)
    {
        return new XmlScanEvent(XmlScanEventKind.StartElement, name, default, attributes, isEmpty, start, close);
    }

    /// <summary>Builds an end-element event.</summary>
    /// <param name="name">The element's qualified name as written in the end tag.</param>
    /// <param name="start">The byte offset of the end tag's <c>&lt;</c>.</param>
    /// <param name="close">The byte offset of the end tag's <c>&gt;</c>.</param>
    /// <returns>The end-element event.</returns>
    public static XmlScanEvent EndElement(Utf8String name, int start, int close)
    {
        return new XmlScanEvent(XmlScanEventKind.EndElement, name, default, EmptyAttributes, false, start, close);
    }

    /// <summary>Builds a character-data event.</summary>
    /// <param name="text">The entity-decoded, line-ending-normalized character data.</param>
    /// <returns>The text event.</returns>
    public static XmlScanEvent TextRun(Utf8String text)
    {
        return new XmlScanEvent(XmlScanEventKind.Text, default, text, EmptyAttributes, false, 0, 0);
    }

    /// <summary>Builds the end-of-document event.</summary>
    /// <returns>The end-of-document event.</returns>
    public static XmlScanEvent EndDocument()
    {
        return new XmlScanEvent(XmlScanEventKind.EndDocument, default, default, EmptyAttributes, false, 0, 0);
    }
}
