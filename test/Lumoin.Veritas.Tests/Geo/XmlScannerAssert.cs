using System;
using System.Text;

using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Xml;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The shared assertion machinery of the scanner family: drain a document to
/// clean exhaustion, drain it to a refusal asserting kind AND byte offset
/// with the terminal repeat, and compute byte offsets from markers so no row
/// hand-counts one.
/// </summary>
internal static class XmlScannerAssert
{
    /// <summary>
    /// The UTF-8 byte offset of a marker's first occurrence in a document —
    /// computed over the encoded bytes, so non-ASCII prefixes count at
    /// their encoded width.
    /// </summary>
    public static int ByteOffsetOf(string document, string marker)
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(document);
        byte[] markerBytes = Encoding.UTF8.GetBytes(marker);
        int offset = documentBytes.AsSpan().IndexOf(markerBytes);
        Assert.AreNotEqual(-1, offset, $"the marker '{marker}' must occur in '{document}'");

        return offset;
    }

    /// <summary>Drains a document to clean exhaustion and asserts the exhaustion is sticky.</summary>
    public static void Accepts(string document)
    {
        AcceptsBytes(Encoding.UTF8.GetBytes(document), document);
    }

    /// <summary>Drains a byte document to clean exhaustion and asserts the exhaustion is sticky.</summary>
    public static void AcceptsBytes(byte[] document, string label)
    {
        using XmlFragmentScanner scanner = new(document);
        GeometryCodecRefusal refusal = GeometryCodecRefusal.None;
        while(scanner.TryReadNext(out _, out refusal))
        {
        }

        Assert.AreEqual(GeometryCodecRefusal.None, refusal, $"'{label}' must scan to clean exhaustion");
        bool repeated = scanner.TryReadNext(out _, out GeometryCodecRefusal repeat);
        Assert.IsFalse(repeated, "exhaustion must stay exhausted");
        Assert.AreEqual(GeometryCodecRefusal.None, repeat, "repeated exhaustion must stay the no-offense sentinel");
    }

    /// <summary>
    /// Drains a document to its refusal, asserting the kind, the byte
    /// offset, and that the refusal is terminal — a second call repeats
    /// both.
    /// </summary>
    public static void Refuses(string document, GeometryCodecRefusalKind kind, int expectedOffset)
    {
        RefusesBytes(Encoding.UTF8.GetBytes(document), kind, expectedOffset, document);
    }

    /// <summary>Drains a byte document to its refusal, asserting kind, offset, and the terminal repeat.</summary>
    public static void RefusesBytes(byte[] document, GeometryCodecRefusalKind kind, int expectedOffset, string label)
    {
        using XmlFragmentScanner scanner = new(document);
        GeometryCodecRefusal refusal = GeometryCodecRefusal.None;
        while(scanner.TryReadNext(out _, out refusal))
        {
        }

        Assert.AreEqual(kind, refusal.Kind, $"'{label}' must refuse with the expected kind");
        Assert.AreEqual(expectedOffset, refusal.ByteOffset, $"'{label}' must anchor the refusal at the expected byte");
        bool repeated = scanner.TryReadNext(out _, out GeometryCodecRefusal repeat);
        Assert.IsFalse(repeated, "a terminal refusal must not deliver another token");
        Assert.AreEqual(refusal, repeat, "the repeated refusal must carry the identical kind and byte offset");
    }

    /// <summary>
    /// Drains a document expecting exactly one text token, returning its
    /// decoded bytes as a string and the token's anchor offset; the drain
    /// must end in clean exhaustion.
    /// </summary>
    public static string ReadSingleText(string document, out int tokenStartOffset)
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(document);
        using XmlFragmentScanner scanner = new(documentBytes);
        string? text = null;
        tokenStartOffset = -1;
        GeometryCodecRefusal refusal = GeometryCodecRefusal.None;
        while(scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
        {
            if(kind != XmlFragmentTokenKind.Text)
            {
                continue;
            }

            Assert.IsNull(text, $"'{document}' must deliver exactly one text token");
            text = Encoding.UTF8.GetString(scanner.Text);
            tokenStartOffset = scanner.TokenStartOffset;
        }

        Assert.AreEqual(GeometryCodecRefusal.None, refusal, $"'{document}' must scan to clean exhaustion");
        Assert.IsNotNull(text, $"'{document}' must deliver a text token");

        return text;
    }

    /// <summary>
    /// Drains a document counting its text tokens, asserting clean
    /// exhaustion — the row shape for content that must deliver NO text
    /// token at all.
    /// </summary>
    public static int CountTextTokens(string document)
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(document);
        using XmlFragmentScanner scanner = new(documentBytes);
        int count = 0;
        GeometryCodecRefusal refusal = GeometryCodecRefusal.None;
        while(scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
        {
            if(kind == XmlFragmentTokenKind.Text)
            {
                count++;
            }
        }

        Assert.AreEqual(GeometryCodecRefusal.None, refusal, $"'{document}' must scan to clean exhaustion");

        return count;
    }

    /// <summary>
    /// Drains a document expecting exactly one start tag and returns the
    /// decoded value of the named attribute on it; the drain must end in
    /// clean exhaustion. An empty namespace means an attribute in no
    /// namespace.
    /// </summary>
    public static string ReadRootAttributeValue(string document, string namespaceUri, string localName)
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(document);
        byte[] namespaceBytes = Encoding.UTF8.GetBytes(namespaceUri);
        byte[] localBytes = Encoding.UTF8.GetBytes(localName);
        using XmlFragmentScanner scanner = new(documentBytes);
        string? value = null;
        GeometryCodecRefusal refusal = GeometryCodecRefusal.None;
        while(scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
        {
            if(kind != XmlFragmentTokenKind.ElementOpen || value is not null)
            {
                continue;
            }

            bool found = scanner.TryFindAttribute(namespaceBytes, localBytes, out int index);
            Assert.IsTrue(found, $"'{document}' must carry the attribute '{localName}'");
            value = Encoding.UTF8.GetString(scanner.AttributeValue(index));
        }

        Assert.AreEqual(GeometryCodecRefusal.None, refusal, $"'{document}' must scan to clean exhaustion");
        Assert.IsNotNull(value, $"'{document}' must deliver a start tag");

        return value;
    }
}
