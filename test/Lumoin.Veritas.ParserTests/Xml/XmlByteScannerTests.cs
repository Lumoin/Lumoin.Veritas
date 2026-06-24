using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Core.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Verifies the shared byte-native <see cref="XmlByteScanner"/>: it emits a faithful event stream, splits no token
/// across a chunk boundary (a byte-by-byte feed produces the same events as a whole-buffer feed), resolves source
/// spans, normalizes literal line endings and attribute whitespace (XML 1.0 §2.11/§3.3.3), expands DOCTYPE
/// internal-subset entities, and rejects malformed tokens with <see cref="FormatException"/>.
/// </summary>
[TestClass]
internal sealed class XmlByteScannerTests
{
    /// <summary>Scans an XML string in fixed-size chunks, collecting every emitted event in order.</summary>
    /// <param name="xml">The XML document text.</param>
    /// <param name="chunkSize">The feed chunk size in bytes; a size at least the document length feeds it whole.</param>
    /// <param name="parseInternalDtd">Whether to parse the DOCTYPE internal subset.</param>
    /// <returns>The events in document order.</returns>
    private static List<XmlScanEvent> Scan(string xml, int chunkSize, bool parseInternalDtd = false)
    {
        XmlByteScanner scanner = new(parseInternalDtd);
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        List<XmlScanEvent> events = [];
        for(int i = 0; i < bytes.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, bytes.Length - i);
            scanner.Feed(bytes.AsSpan(i, length));
            while(scanner.TryDequeue(out XmlScanEvent scanEvent))
            {
                events.Add(scanEvent);
            }
        }

        scanner.Complete();
        while(scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            events.Add(scanEvent);
        }

        return events;
    }

    /// <summary>Renders one event to a canonical string for value comparison (record-struct equality compares the attribute list by reference).</summary>
    /// <param name="scanEvent">The event to render.</param>
    /// <returns>The canonical rendering.</returns>
    private static string Render(XmlScanEvent scanEvent)
    {
        return scanEvent.Kind switch
        {
            XmlScanEventKind.StartElement => FormattableString.Invariant($"Start {scanEvent.Name} empty={scanEvent.IsEmpty} [{scanEvent.Start}..{scanEvent.Close}] {RenderAttributes(scanEvent.Attributes)}"),
            XmlScanEventKind.EndElement => FormattableString.Invariant($"End {scanEvent.Name} [{scanEvent.Start}..{scanEvent.Close}]"),
            XmlScanEventKind.Text => $"Text '{scanEvent.Text}'",
            _ => "EndDocument"
        };
    }

    /// <summary>Renders an attribute list to a canonical string.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The canonical rendering.</returns>
    private static string RenderAttributes(IReadOnlyList<XmlScanAttribute> attributes)
    {
        StringBuilder builder = new("(");
        foreach(XmlScanAttribute attribute in attributes)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{attribute.Name}='{attribute.Value}'@{attribute.NameStart}..{attribute.End} ");
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Renders an event sequence to one comparable string.</summary>
    /// <param name="events">The events.</param>
    /// <returns>The newline-joined rendering.</returns>
    private static string RenderAll(List<XmlScanEvent> events)
    {
        return string.Join("\n", events.ConvertAll(Render));
    }

    /// <summary>A whole-buffer scan emits a start tag, its attributes, character data, and an end tag, ending with the document event.</summary>
    [TestMethod]
    public void EmitsEventsForADocument()
    {
        List<XmlScanEvent> events = Scan("<r a=\"1\"><c>text</c></r>", 1024);

        Assert.AreEqual(
            "Start r empty=False [0..8] (a='1'@3..8 )\n" +
            "Start c empty=False [9..11] ()\n" +
            "Text 'text'\n" +
            "End c [16..19]\n" +
            "End r [20..23]\n" +
            "EndDocument",
            RenderAll(events));
    }

    /// <summary>A self-closing element emits a single empty start-element event and no end-element event.</summary>
    [TestMethod]
    public void EmitsAnEmptyElementAsOneEvent()
    {
        List<XmlScanEvent> events = Scan("<r><a/></r>", 1024);

        Assert.AreEqual(
            "Start r empty=False [0..2] ()\n" +
            "Start a empty=True [3..6] ()\n" +
            "End r [7..10]\n" +
            "EndDocument",
            RenderAll(events));
    }

    /// <summary>Feeding a document one byte at a time produces exactly the events a whole-buffer feed does — no token is split across a chunk boundary.</summary>
    [TestMethod]
    public void ChunkBoundaryNeverSplitsAToken()
    {
        string[] documents =
        [
            "<r a=\"1\" b=\"2\"><c>text</c><d/></r>",
            "<r>a<!--comment--><![CDATA[<x>&amp;]]>b</r>",
            "<?xml version=\"1.0\"?><r xmlns:p=\"urn:p\"><p:c>x&#65;y</p:c></r>",
            "<r>line1\r\nline2</r>",
            "<r a=\"x&amp;y\" c=\"a\tb\"/>",
            "<r/>&#65;",
            "&#65;<r/>",
            "<r></a>&#65;</r>",
            "<r><!--></r>",
            "<r>a<!-->b</r>"
        ];

        foreach(string document in documents)
        {
            Assert.AreEqual(RenderAll(Scan(document, 4096)), RenderAll(Scan(document, 1)), $"Byte-by-byte scan diverged from whole-buffer for: {document}");
        }
    }

    /// <summary>A DOCTYPE internal subset feeds correctly across chunk boundaries and its entities expand the same as a whole-buffer feed.</summary>
    [TestMethod]
    public void ChunkBoundaryNeverSplitsADoctype()
    {
        string document = "<!DOCTYPE r [ <!ENTITY ex \"urn:x:\"> ]><r a=\"&ex;v\">&ex;t</r>";

        Assert.AreEqual(RenderAll(Scan(document, 4096, parseInternalDtd: true)), RenderAll(Scan(document, 1, parseInternalDtd: true)), "Byte-by-byte DOCTYPE scan diverged from whole-buffer.");
    }

    /// <summary>An element event's offsets resolve to zero-based line and column positions across a multi-line document.</summary>
    [TestMethod]
    public void ResolvesSpanEndpoints()
    {
        XmlByteScanner scanner = new(parseInternalDtd: false);
        scanner.Feed(Encoding.UTF8.GetBytes("<r>\n<c/>\n</r>"));
        scanner.Complete();

        XmlScanEvent child = default;
        while(scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            if(scanEvent.Kind == XmlScanEventKind.StartElement && scanEvent.Name.ToString() == "c")
            {
                child = scanEvent;
            }
        }

        SourceSpan span = scanner.Span(child.Start, child.Close + 1);
        Assert.AreEqual(1, span.StartLine, "The child begins on the second line (zero-based line 1).");
        Assert.AreEqual(0, span.StartColumn, "The child begins at the line's first column.");
        Assert.AreEqual(4, span.EndColumn, "The child ends four columns in, just past '<c/>'.");
    }

    /// <summary>Text and CDATA line endings normalize to LF (§2.11) and attribute whitespace to spaces (§3.3.3), while reference-introduced whitespace is preserved.</summary>
    [TestMethod]
    public void NormalizesWhitespacePerXmlRules()
    {
        Assert.AreEqual("a\nb", FirstText(Scan("<r>a\r\nb</r>", 1024)), "A literal CRLF in text becomes a single LF.");
        Assert.AreEqual("x\ny", FirstText(Scan("<r><![CDATA[x\r\ny]]></r>", 1024)), "A CRLF in CDATA becomes a single LF.");
        Assert.AreEqual("a b", FirstAttributeValue(Scan("<r v=\"a\tb\"/>", 1024)), "A literal tab in an attribute becomes a space.");
        Assert.AreEqual("a\rb", FirstText(Scan("<r>a&#xD;b</r>", 1024)), "A CR character reference in text is preserved, not normalized.");
    }

    /// <summary>DOCTYPE internal-subset general entities expand in text and attribute values.</summary>
    [TestMethod]
    public void ExpandsDoctypeEntities()
    {
        List<XmlScanEvent> events = Scan("<!DOCTYPE r [ <!ENTITY ex \"urn:x:\"> ]><r a=\"&ex;v\">&ex;t</r>", 1024, parseInternalDtd: true);

        Assert.AreEqual("urn:x:v", FirstAttributeValue(events), "An entity reference expands in an attribute value.");
        Assert.AreEqual("urn:x:t", FirstText(events), "An entity reference expands in text content.");
    }

    /// <summary>Malformed tokens — a bad numeric reference, a duplicate attribute, an unterminated start tag at the final input — throw.</summary>
    [TestMethod]
    public void RejectsMalformedTokens()
    {
        Assert.ThrowsExactly<FormatException>(() => Scan("<r>&#0;</r>", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<r a=\"1\" a=\"2\"/>", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<r", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<!DOCTYPE r><r/>", 64));
    }

    /// <summary>The value of the first start-element attribute in an event sequence.</summary>
    /// <param name="events">The events.</param>
    /// <returns>The attribute value as a string.</returns>
    private static string FirstAttributeValue(List<XmlScanEvent> events)
    {
        foreach(XmlScanEvent scanEvent in events)
        {
            if(scanEvent.Kind == XmlScanEventKind.StartElement && scanEvent.Attributes.Count > 0)
            {
                return scanEvent.Attributes[0].Value.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>The content of the first text event in an event sequence.</summary>
    /// <param name="events">The events.</param>
    /// <returns>The text as a string.</returns>
    private static string FirstText(List<XmlScanEvent> events)
    {
        foreach(XmlScanEvent scanEvent in events)
        {
            if(scanEvent.Kind == XmlScanEventKind.Text)
            {
                return scanEvent.Text.ToString();
            }
        }

        return string.Empty;
    }
}
