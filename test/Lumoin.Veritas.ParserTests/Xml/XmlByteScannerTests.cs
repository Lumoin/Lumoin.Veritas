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
    /// <param name="strictness">Whether a malformed token throws (strict) or is recovered from silently (lenient).</param>
    /// <returns>The events in document order.</returns>
    private static List<XmlScanEvent> Scan(string xml, int chunkSize, bool parseInternalDtd = false, XmlScanStrictness strictness = XmlScanStrictness.Strict)
    {
        XmlByteScanner scanner = new(strictness, parseInternalDtd);
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

    /// <summary>In streaming mode, compacting between top-level units keeps the scanner's retained-byte footprint bounded by roughly a chunk, far below the document size — whereas the default mode retains the whole document.</summary>
    [TestMethod]
    public void StreamingCompactionBoundsRetainedBytes()
    {
        StringBuilder builder = new("<r>");
        for(int i = 0; i < 50_000; i++)
        {
            builder.Append("<e>x</e>");
        }

        builder.Append("</r>");
        byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
        const int chunkSize = 8192;

        int streamingPeak = DriveAndPeak(bytes, chunkSize, streaming: true, compactBetweenChunks: true);
        int defaultPeak = DriveAndPeak(bytes, chunkSize, streaming: false, compactBetweenChunks: false);

        Assert.IsGreaterThan(bytes.Length / 2, defaultPeak, "the default mode retains the whole document, so its footprint tracks the document size");
        Assert.IsLessThan(4 * chunkSize, streamingPeak, $"streaming compaction must bound the retained bytes near a chunk (peak {streamingPeak} for a {bytes.Length}-byte document)");
    }

    /// <summary>An empty window is total: <see cref="XmlByteScanner.Window"/> returns an empty range without indexing the buffer even when the requested start predates the reclaimed base (the empty-element sentinel offset), so a self-closing literal does not abort streaming.</summary>
    [TestMethod]
    public void EmptyWindowDoesNotIndexTheBufferAfterCompaction()
    {
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false, streaming: true);
        scanner.Feed("<r><a>hello there</a>"u8);
        while(scanner.TryDequeue(out _))
        {
        }

        scanner.Compact();

        //After compaction the base has advanced; a zero-length window at the empty-element sentinel offset (0) would
        //form a negative index, but an empty range must resolve without touching the buffer.
        Assert.IsTrue(scanner.Window(0, 0).IsEmpty, "a zero-length window at the sentinel offset must be empty, not throw");
        Assert.IsTrue(scanner.Window(5, 0).IsEmpty, "a zero-length window below the reclaimed base must be empty, not throw");
    }

    /// <summary>A value emitted before a compaction is owned, so it still reads correctly after the buffer prefix is reclaimed and later feeds overwrite the buffer region.</summary>
    [TestMethod]
    public void StreamingEmittedValueSurvivesCompaction()
    {
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false, streaming: true);
        scanner.Feed("<r><a>hello</a>"u8);

        Utf8String captured = default;
        while(scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            if(scanEvent.Kind == XmlScanEventKind.Text)
            {
                captured = scanEvent.Text;
            }
        }

        Assert.AreEqual("hello", captured.ToString(), "the text event was emitted");

        //Reclaim the prefix that held "hello", then feed a large run that reuses the buffer region; an owned value is
        //unperturbed, a window into the reclaimed prefix would not be.
        scanner.Compact();
        scanner.Feed(Encoding.UTF8.GetBytes("<b>" + new string('z', 20_000) + "</b></r>"));
        while(scanner.TryDequeue(out _))
        {
        }

        scanner.Complete();

        Assert.AreEqual("hello", captured.ToString(), "a value emitted before compaction must survive the buffer prefix being reclaimed and overwritten");
    }

    /// <summary>Feeds bytes in chunks, draining (and discarding) events after each, optionally compacting between chunks, and returns the peak retained-byte count observed.</summary>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="chunkSize">The feed chunk size.</param>
    /// <param name="streaming">Whether the scanner runs in streaming mode.</param>
    /// <param name="compactBetweenChunks">Whether to compact after each chunk's drain.</param>
    /// <returns>The maximum <see cref="XmlByteScanner.RetainedByteCount"/> observed across the feed.</returns>
    private static int DriveAndPeak(byte[] bytes, int chunkSize, bool streaming, bool compactBetweenChunks)
    {
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false, streaming);
        int peak = 0;
        for(int i = 0; i < bytes.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, bytes.Length - i);
            scanner.Feed(bytes.AsSpan(i, length));
            while(scanner.TryDequeue(out _))
            {
            }

            if(compactBetweenChunks)
            {
                scanner.Compact();
            }

            peak = Math.Max(peak, scanner.RetainedByteCount);
        }

        scanner.Complete();
        while(scanner.TryDequeue(out _))
        {
        }

        return peak;
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
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false);
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

    /// <summary>Under strict mode, malformed tokens — a bad numeric reference, a duplicate attribute, an unterminated start tag at the final input, an impermissible DTD — throw.</summary>
    [TestMethod]
    public void RejectsMalformedTokens()
    {
        Assert.ThrowsExactly<FormatException>(() => Scan("<r>&#0;</r>", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<r a=\"1\" a=\"2\"/>", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<r", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<!DOCTYPE r><r/>", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<r>AT&T</r>", 64));
        Assert.ThrowsExactly<FormatException>(() => Scan("<r>a&nbsp;b</r>", 64));
    }

    /// <summary>Under lenient mode, the same malformed tokens are recovered from silently: a bare <c>&amp;</c> stays literal, an undefined entity drops, a duplicate attribute is kept, and an unterminated tail is abandoned.</summary>
    [TestMethod]
    public void LenientModeRecoversFromMalformedTokens()
    {
        Assert.AreEqual("AT&T", FirstText(Scan("<r>AT&T</r>", 1024, strictness: XmlScanStrictness.Lenient)), "A bare '&' stays literal under lenient mode.");
        Assert.AreEqual("ab", FirstText(Scan("<r>a&nbsp;b</r>", 1024, strictness: XmlScanStrictness.Lenient)), "An undefined entity drops under lenient mode.");

        List<XmlScanEvent> duplicate = Scan("<r a=\"1\" a=\"2\"/>", 1024, strictness: XmlScanStrictness.Lenient);
        Assert.HasCount(2, FirstStartElement(duplicate).Attributes, "A duplicate attribute is kept (not rejected) under lenient mode.");

        List<XmlScanEvent> truncated = Scan("<r>", 1024, strictness: XmlScanStrictness.Lenient);
        Assert.AreEqual("r", FirstStartElement(truncated).Name.ToString(), "An unterminated tail is abandoned silently, leaving the elements scanned before it.");
    }

    /// <summary>Under lenient mode a nested DOCTYPE entity (one referencing an earlier one) expands its backward reference at registration; under strict mode that nested reference is rejected.</summary>
    [TestMethod]
    public void LenientModeExpandsNestedDoctypeEntities()
    {
        string nested = "<!DOCTYPE r [ <!ENTITY base \"http://e/\"> <!ENTITY a \"&base;ns#\"> ]><r>&a;Thing</r>";

        Assert.AreEqual("http://e/ns#Thing", FirstText(Scan(nested, 1024, parseInternalDtd: true, strictness: XmlScanStrictness.Lenient)), "A nested entity expands its backward reference under lenient mode.");
        Assert.ThrowsExactly<FormatException>(() => Scan(nested, 1024, parseInternalDtd: true), "A nested entity's reference is rejected at registration under strict mode.");
    }

    /// <summary>Feeding a lenient document one byte at a time recovers identically to a whole-buffer feed.</summary>
    [TestMethod]
    public void LenientRecoveryIsStableAcrossChunkBoundaries()
    {
        string[] documents = ["<r>AT&T</r>", "<r>a&nbsp;b</r>", "<r a=\"1\" a=\"2\"><c/></r>"];
        foreach(string document in documents)
        {
            Assert.AreEqual(
                RenderAll(Scan(document, 4096, strictness: XmlScanStrictness.Lenient)),
                RenderAll(Scan(document, 1, strictness: XmlScanStrictness.Lenient)),
                $"Byte-by-byte lenient scan diverged from whole-buffer for: {document}");
        }
    }

    /// <summary>The first start-element event in an event sequence.</summary>
    /// <param name="events">The events.</param>
    /// <returns>The first start-element event.</returns>
    private static XmlScanEvent FirstStartElement(List<XmlScanEvent> events)
    {
        foreach(XmlScanEvent scanEvent in events)
        {
            if(scanEvent.Kind == XmlScanEventKind.StartElement)
            {
                return scanEvent;
            }
        }

        return default;
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
