namespace Lumoin.Veritas.Core.Xml;

/// <summary>The kind of a flat <see cref="XmlScanEvent"/> a byte-native XML scanner emits as it consumes a document.</summary>
public enum XmlScanEventKind
{
    /// <summary>An element's start tag (or, when <see cref="XmlScanEvent.IsEmpty"/>, its single self-closing tag): names the element and carries its attributes.</summary>
    StartElement,

    /// <summary>An element's end tag: names the element being closed (for the consumer's matching check) and carries the tag's offsets.</summary>
    EndElement,

    /// <summary>A run of character data — element content or a CDATA section — already entity-decoded and line-ending normalized.</summary>
    Text,

    /// <summary>The end of the document, emitted once after the final input has been consumed.</summary>
    EndDocument
}
