namespace Lumoin.Veritas.Core.Xml;

/// <summary>
/// One attribute a byte-native XML scanner parsed from a start tag: its qualified name as written and its
/// entity-decoded, whitespace-normalized value, with the source offsets that bound it. Namespace declarations
/// (<c>xmlns</c> / <c>xmlns:prefix</c>) are included verbatim — the scanner does not resolve namespaces, so a
/// consumer folds the declarations into its own scope and resolves the qualified name itself.
/// </summary>
/// <param name="Name">The attribute's qualified name (prefix and local part) exactly as written.</param>
/// <param name="Value">The attribute value with XML references resolved and attribute whitespace normalized (XML 1.0 §3.3.3).</param>
/// <param name="NameStart">The byte offset of the attribute name's first byte.</param>
/// <param name="End">The byte offset just past the attribute's closing quote.</param>
public readonly record struct XmlScanAttribute(Utf8String Name, Utf8String Value, int NameStart, int End);
