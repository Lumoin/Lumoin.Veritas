using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Core.Xml;

/// <summary>
/// A byte-native, resumable XML scanner: it consumes a UTF-8 XML document — fed whole or chunk by chunk — and emits a
/// flat <see cref="XmlScanEvent"/> stream (start tag, end tag, character data, end of document), with no
/// <see cref="System.Xml"/> DOM and no UTF-16 round-trip. It is the shared front end the RDF/XML, SPARQL-results-XML,
/// and OWL/XML readers drive, each folding the events into its own structure.
/// </summary>
/// <remarks>
/// <para>
/// The scanner commits one markup unit at a time — a tag, comment, processing instruction, CDATA section, doctype, or
/// text run. A unit whose terminator the buffer has not yet delivered suspends: the committed position holds at the
/// unit's first byte and the unit re-scans whole once more bytes arrive, so a chunk boundary never splits a token.
/// Element and attribute <b>names are emitted as written</b> (the scanner resolves no namespaces — that is the
/// consumer's policy); predefined and numeric character references decode in text and attribute values, internal-subset
/// general entities expand when permitted (bounded by an expansion budget against entity amplification), literal line
/// endings normalize to <c>LF</c> in text and CDATA (XML 1.0 §2.11), and attribute values are whitespace-normalized
/// (§3.3.3) while reference-introduced whitespace is preserved.
/// </para>
/// <para>
/// A malformed token — an unterminated construct once the input is final, a stray <c>&lt;</c>, a bad reference, a
/// duplicate attribute, an impermissible DTD — throws <see cref="FormatException"/> under
/// <see cref="XmlScanStrictness.Strict"/> (the well-formedness contract the RDF/XML and SPARQL-results readers present)
/// or is recovered from silently under <see cref="XmlScanStrictness.Lenient"/> (the value-based contract the OWL editor
/// surfaces present: a bare <c>&amp;</c> stays literal, an undefined entity drops, an unterminated tail is abandoned).
/// Structural well-formedness that spans tokens (a mismatched or orphaned end tag, an unclosed or absent root) is the
/// folding consumer's concern under either mode.
/// </para>
/// </remarks>
public sealed class XmlByteScanner
{
    /// <summary>The cap on the bytes a document's <c>&amp;name;</c> general-entity references may expand to, guarding against entity-amplification.</summary>
    private const int MaxEntityExpansionBytes = 10_000_000;

    /// <summary>The document bytes fed so far; grown by doubling and never compacted, so emitted <see cref="Utf8String"/> windows over it stay valid at stable offsets.</summary>
    private byte[] Buffer { get; set; } = new byte[256];

    /// <summary>The number of valid bytes in <see cref="Buffer"/>.</summary>
    private int Length { get; set; }

    /// <summary>The committed scan position: the offset of the next unscanned markup unit; advanced only past a fully terminated unit.</summary>
    private int LexPosition { get; set; }

    /// <summary>Whether the last drain suspended mid-unit waiting for more bytes.</summary>
    private bool LexSuspended { get; set; }

    /// <summary>Whether <see cref="Complete"/> has declared the input final.</summary>
    private bool Final { get; set; }

    /// <summary>The element nesting depth, tracked so an unterminated in-element text run suspends while insignificant top-level whitespace does not.</summary>
    private int Depth { get; set; }

    /// <summary>Resolves byte offsets to <see cref="SourceSpan"/> values; appended the same chunk per feed so already-seen offsets stay stable.</summary>
    private ByteSourceMap Map { get; } = new();

    /// <summary>The general entities declared in the internal subset, keyed by entity name, with decoded replacement values.</summary>
    private Dictionary<Utf8String, Utf8String> Entities { get; } = [];

    /// <summary>The remaining budget for general-entity expansion, in bytes.</summary>
    private int EntityBudget { get; set; } = MaxEntityExpansionBytes;

    /// <summary>Whether the DOCTYPE internal subset's general entities are parsed and expanded, as opposed to any DTD being rejected.</summary>
    private bool ParseInternalDtd { get; }

    /// <summary>How the scanner reacts to a malformed token: throwing (strict) or recovering silently (lenient).</summary>
    private XmlScanStrictness Strictness { get; }

    /// <summary>The events scanned and not yet dequeued by the consumer, in document order.</summary>
    private Queue<XmlScanEvent> Events { get; } = new();

    /// <summary>Initialises an empty scanner; feed source bytes through <see cref="Feed"/>.</summary>
    /// <param name="strictness">Whether a malformed token throws (<see cref="XmlScanStrictness.Strict"/>) or is recovered from silently (<see cref="XmlScanStrictness.Lenient"/>).</param>
    /// <param name="parseInternalDtd">When <see langword="true"/>, parse the internal subset's <c>&lt;!ENTITY&gt;</c> declarations and expand <c>&amp;name;</c> references (external resolution stays off); when <see langword="false"/>, reject any DTD.</param>
    public XmlByteScanner(XmlScanStrictness strictness, bool parseInternalDtd)
    {
        Strictness = strictness;
        ParseInternalDtd = parseInternalDtd;
    }

    /// <summary>The token-completeness of the scan: <see cref="IncrementalParseStatus.NeedMore"/> while a unit is suspended mid-token, otherwise <see cref="IncrementalParseStatus.Complete"/>. A consumer that builds a tree combines this with its own open-element balance.</summary>
    public IncrementalParseStatus Status => LexSuspended ? IncrementalParseStatus.NeedMore : IncrementalParseStatus.Complete;

    /// <summary>Feeds the next chunk of document bytes, scanning every newly complete markup unit into the event stream.</summary>
    /// <param name="chunk">The next UTF-8 bytes of the document.</param>
    /// <returns>The token-completeness after the chunk.</returns>
    /// <exception cref="InvalidOperationException">The input was already declared final by <see cref="Complete"/>.</exception>
    /// <exception cref="FormatException">A scanned token is malformed.</exception>
    public IncrementalParseStatus Feed(ReadOnlySpan<byte> chunk)
    {
        if(Final)
        {
            throw new InvalidOperationException("The XML input was already declared final.");
        }

        Append(chunk);
        Drain();

        return Status;
    }

    /// <summary>Declares the input final, scanning any remaining unit and emitting the end-of-document event.</summary>
    /// <returns>The token-completeness; <see cref="IncrementalParseStatus.NeedMore"/> indicates a non-throwing consumer should treat the tail as truncated.</returns>
    /// <exception cref="FormatException">An unterminated token remains at the now-final input.</exception>
    public IncrementalParseStatus Complete()
    {
        if(!Final)
        {
            Final = true;
            Drain();
            Events.Enqueue(XmlScanEvent.EndDocument());
        }

        return Status;
    }

    /// <summary>Dequeues the next scanned event.</summary>
    /// <param name="scanEvent">The dequeued event when one was available.</param>
    /// <returns><see langword="true"/> when an event was dequeued; <see langword="false"/> when none remain.</returns>
    public bool TryDequeue(out XmlScanEvent scanEvent)
    {
        return Events.TryDequeue(out scanEvent);
    }

    /// <summary>Builds the source span for a half-open byte range of the consumed document.</summary>
    /// <param name="startByte">The inclusive start byte offset.</param>
    /// <param name="endByte">The exclusive end byte offset.</param>
    /// <returns>The span in byte and line-column form.</returns>
    public SourceSpan Span(int startByte, int endByte)
    {
        return Map.Span(startByte, endByte);
    }

    /// <summary>The outcome of one resumable scanning step.</summary>
    private enum LexStep
    {
        /// <summary>A markup unit was scanned and the position committed past it.</summary>
        Unit,

        /// <summary>The buffer ended mid-unit; more bytes decide it, so the position holds at the unit start.</summary>
        NeedMore,

        /// <summary>The buffer is exhausted at a clean boundary; no unit remains.</summary>
        Exhausted
    }

    /// <summary>
    /// How a decoded value's <b>literal</b> whitespace is normalized. Whitespace introduced by a character or
    /// entity reference is never normalized under either mode — only whitespace present literally in the source is.
    /// </summary>
    private enum NormalizationMode
    {
        /// <summary>XML 1.0 §2.11 only: a literal <c>CR LF</c> pair or a lone literal <c>CR</c> collapses to a single <c>LF</c> (text and CDATA content).</summary>
        Text,

        /// <summary>XML 1.0 §2.11 then §3.3.3: after line-ending normalization, each remaining literal whitespace (<c>LF</c>, tab, space) becomes a single space (attribute values).</summary>
        Attribute
    }

    /// <summary>Appends a chunk to the buffer and extends the source map over it.</summary>
    /// <param name="chunk">The appended UTF-8 bytes.</param>
    private void Append(ReadOnlySpan<byte> chunk)
    {
        if(Length + chunk.Length > Buffer.Length)
        {
            byte[] grown = new byte[Math.Max(Buffer.Length * 2, Length + chunk.Length)];
            Buffer.AsSpan(0, Length).CopyTo(grown);
            Buffer = grown;
        }

        chunk.CopyTo(Buffer.AsSpan(Length));
        Length += chunk.Length;
        Map.Append(chunk);
    }

    /// <summary>Scans and emits markup units until the buffer is exhausted or a unit suspends.</summary>
    private void Drain()
    {
        LexSuspended = false;

        while(true)
        {
            LexStep step = TryScanUnit();

            if(step == LexStep.Exhausted)
            {
                return;
            }

            if(step == LexStep.NeedMore)
            {
                LexSuspended = true;

                return;
            }
        }
    }

    /// <summary>Scans the next markup unit at the committed position and commits the position past it.</summary>
    /// <returns>Whether a unit was scanned, more bytes are needed, or the buffer is exhausted.</returns>
    private LexStep TryScanUnit()
    {
        ReadOnlySpan<byte> text = Buffer.AsSpan(0, Length);
        int i = LexPosition;

        if(i >= text.Length)
        {
            return LexStep.Exhausted;
        }

        if(text[i] == (byte)'<')
        {
            return TryScanMarkup(text, i);
        }

        return TryScanText(text, i);
    }

    /// <summary>Scans a <c>&lt;…</c> markup unit: a tag, comment, processing instruction, CDATA section, or doctype.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanMarkup(ReadOnlySpan<byte> text, int start)
    {
        if(start + 1 >= text.Length)
        {
            return Suspend("XML document ends with an unterminated '<'.");
        }

        return text[start + 1] switch
        {
            (byte)'/' => TryScanEndTag(text, start),
            (byte)'?' => TrySkipDelimited(text, start, "?>"u8),
            (byte)'!' => TryScanDeclaration(text, start),
            _ => TryScanStartTag(text, start)
        };
    }

    /// <summary>Scans a <c>&lt;!…</c> declaration: a comment, a CDATA section, or (when permitted) a doctype.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    /// <exception cref="FormatException">The declaration is an unsupported DTD.</exception>
    private LexStep TryScanDeclaration(ReadOnlySpan<byte> text, int start)
    {
        if(StartsWith(text, start, "<!--"u8))
        {
            return TrySkipDelimited(text, start, "-->"u8);
        }

        if(StartsWith(text, start, "<![CDATA["u8))
        {
            return TryScanCdata(text, start);
        }

        ReadOnlySpan<byte> here = text.Slice(start);
        if(here.Length < "<![CDATA["u8.Length && (IsPrefixOf(here, "<!--"u8) || IsPrefixOf(here, "<![CDATA["u8) || IsPrefixOf(here, "<!DOCTYPE"u8)))
        {
            return Suspend("XML document declares a DTD, which is not permitted.");
        }

        if(ParseInternalDtd && StartsWith(text, start, "<!DOCTYPE"u8))
        {
            return TryScanDoctype(text, start);
        }

        if(Strictness == XmlScanStrictness.Strict)
        {
            throw new FormatException("XML document declares a DTD, which is not permitted.");
        }

        //Lenient: an unrecognised or impermissible declaration carries no tree content and is skipped to its '>'.
        return TrySkipDelimited(text, start, ">"u8);
    }

    /// <summary>Scans a CDATA section, emitting its line-ending-normalized verbatim content as character data.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanCdata(ReadOnlySpan<byte> text, int start)
    {
        int open = start + "<![CDATA["u8.Length;
        int close = IndexOf(text, open, "]]>"u8);
        if(close < 0)
        {
            return Suspend("XML document has an unterminated CDATA section.");
        }

        EmitText(NormalizeText(open, close - open));

        return Commit(close + "]]>"u8.Length);
    }

    /// <summary>Scans a doctype declaration, registering the general entities its internal subset declares; external resolution is never performed.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c> of the doctype.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanDoctype(ReadOnlySpan<byte> text, int start)
    {
        int depth = 0;
        for(int i = start + 1; i < text.Length; i++)
        {
            byte b = text[i];
            if(b == (byte)'[')
            {
                depth++;
            }
            else if(b == (byte)']')
            {
                depth--;
            }
            else if(b == (byte)'>' && depth <= 0)
            {
                RegisterEntities(text, start, i);

                return Commit(i + 1);
            }
        }

        return Suspend("XML document has an unterminated DOCTYPE declaration.");
    }

    /// <summary>Parses the <c>&lt;!ENTITY name "value"&gt;</c> declarations in a doctype's internal subset into the entity table.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c> of the doctype.</param>
    /// <param name="end">The offset of the doctype's terminating <c>&gt;</c>.</param>
    private void RegisterEntities(ReadOnlySpan<byte> text, int start, int end)
    {
        int i = start;
        while(true)
        {
            int declaration = IndexOf(text.Slice(0, end), i, "<!ENTITY"u8);
            if(declaration < 0)
            {
                return;
            }

            int cursor = SkipWhitespace(text, declaration + "<!ENTITY"u8.Length, end);
            int nameStart = cursor;
            while(cursor < end && IsNameByte(text[cursor]))
            {
                cursor++;
            }

            ReadOnlySpan<byte> name = text.Slice(nameStart, cursor - nameStart);
            cursor = SkipWhitespace(text, cursor, end);
            if(cursor < end && (text[cursor] == (byte)'"' || text[cursor] == (byte)'\''))
            {
                byte quote = text[cursor];
                int valueStart = cursor + 1;
                int valueEnd = IndexOfByte(text, valueStart, end, quote);
                if(valueEnd >= 0 && name.Length > 0)
                {
                    //An entity's replacement text decodes predefined and numeric references once at registration. Under
                    //lenient mode a general-entity reference to an already-declared entity expands here too (so a nested
                    //declaration resolves), matching the OWL reader; strict mode registers it one-level (the replacement
                    //is emitted verbatim on reference), matching the RDF/XML reader.
                    Entities[new Utf8String(name.ToArray())] = Decode(valueStart, valueEnd - valueStart, expandGeneral: Strictness == XmlScanStrictness.Lenient, NormalizationMode.Text);
                    cursor = valueEnd + 1;
                }
            }

            i = cursor + 1;
        }
    }

    /// <summary>Scans a start tag or empty-element tag, emitting the start-element event it names.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanStartTag(ReadOnlySpan<byte> text, int start)
    {
        int close = IndexOfTagClose(text, start);
        if(close < 0)
        {
            return Suspend("XML document has an unterminated start tag.");
        }

        bool empty = text[close - 1] == (byte)'/';
        int nameStart = start + 1;
        int nameEnd = nameStart;
        while(nameEnd < close && !IsXmlWhitespace(text[nameEnd]) && text[nameEnd] != (byte)'/' && text[nameEnd] != (byte)'>')
        {
            nameEnd++;
        }

        List<XmlScanAttribute> attributes = ParseAttributes(text, nameEnd, empty ? close - 1 : close);
        if(Strictness == XmlScanStrictness.Strict)
        {
            EnsureNoDuplicateAttributes(attributes);
        }

        Utf8String name = new(Buffer.AsMemory(nameStart, nameEnd - nameStart));
        Events.Enqueue(XmlScanEvent.StartElement(name, attributes, empty, start, close));
        if(!empty)
        {
            Depth++;
        }

        return Commit(close + 1);
    }

    /// <summary>Scans an end tag, emitting the end-element event it names.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanEndTag(ReadOnlySpan<byte> text, int start)
    {
        int close = IndexOfByte(text, start, text.Length, (byte)'>');
        if(close < 0)
        {
            return Suspend("XML document has an unterminated end tag.");
        }

        int nameStart = start + 2;
        int nameEnd = nameStart;
        while(nameEnd < close && !IsXmlWhitespace(text[nameEnd]))
        {
            nameEnd++;
        }

        Utf8String name = new(Buffer.AsMemory(nameStart, nameEnd - nameStart));
        Events.Enqueue(XmlScanEvent.EndElement(name, start, close));
        if(Depth > 0)
        {
            Depth--;
        }

        return Commit(close + 1);
    }

    /// <summary>Scans a text run up to the next markup, emitting its entity-decoded content.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the first text byte.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanText(ReadOnlySpan<byte> text, int start)
    {
        int next = IndexOfByte(text, start, text.Length, (byte)'<');
        if(next < 0)
        {
            //An in-element run, or any run ending in an entity reference the buffer has not yet terminated, must wait
            //for the bytes that complete it: splitting a reference across the feed boundary would corrupt the decoded
            //value (and the strict decode would reject the half). Top-level whitespace carries no reference, so it
            //never suspends and a whitespace tail reads as a document boundary rather than an unfinished unit.
            if(!Final && (Depth > 0 || EndsWithPartialReference(text.Slice(start))))
            {
                return Suspend("XML document has an unterminated text run.");
            }

            next = text.Length;
        }

        EmitText(Decode(start, next - start, expandGeneral: true, NormalizationMode.Text));

        return Commit(next);
    }

    /// <summary>Skips a delimited unit (comment, processing instruction) that carries no event content.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <param name="closing">The closing delimiter bytes.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TrySkipDelimited(ReadOnlySpan<byte> text, int start, ReadOnlySpan<byte> closing)
    {
        //The closing delimiter is sought from two bytes past the opening '<', tolerating an opening/closing overlap: an
        //empty '<!-->' comment closes on the '-->' that overlaps its own dashes, the overlap the whole-buffer RDF/XML
        //reader has always permitted. (A consumer that wants a stricter comment shape rejects it after the fact.)
        int close = IndexOf(text, start + 2, closing);
        if(close < 0)
        {
            return Suspend("XML document has an unterminated comment or processing instruction.");
        }

        return Commit(close + closing.Length);
    }

    /// <summary>Parses the attribute list of a tag, between the element name and the tag's closing delimiter.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="from">The offset just past the element name.</param>
    /// <param name="to">The offset of the tag's closing delimiter.</param>
    /// <returns>The parsed attributes, in document order (truncated at the first malformed attribute under lenient mode).</returns>
    /// <exception cref="FormatException">An attribute is malformed (no '=' or unterminated value) under strict mode.</exception>
    private List<XmlScanAttribute> ParseAttributes(ReadOnlySpan<byte> text, int from, int to)
    {
        List<XmlScanAttribute> attributes = [];
        int i = SkipWhitespace(text, from, to);
        while(i < to)
        {
            int nameStart = i;
            while(i < to && text[i] != (byte)'=' && !IsXmlWhitespace(text[i]))
            {
                i++;
            }

            int nameEnd = i;
            i = SkipWhitespace(text, i, to);
            if(i >= to || text[i] != (byte)'=')
            {
                return StopAttributes(attributes, "XML attribute is missing its '='.");
            }

            i = SkipWhitespace(text, i + 1, to);
            if(i >= to || (text[i] != (byte)'"' && text[i] != (byte)'\''))
            {
                return StopAttributes(attributes, "XML attribute value is not quoted.");
            }

            byte quote = text[i];
            int valueStart = i + 1;
            int valueEnd = IndexOfByte(text, valueStart, to, quote);
            if(valueEnd < 0)
            {
                return StopAttributes(attributes, "XML attribute value is unterminated.");
            }

            Utf8String name = new(Buffer.AsMemory(nameStart, nameEnd - nameStart));
            Utf8String value = Decode(valueStart, valueEnd - valueStart, expandGeneral: true, NormalizationMode.Attribute);
            attributes.Add(new XmlScanAttribute(name, value, nameStart, valueEnd + 1));
            i = SkipWhitespace(text, valueEnd + 1, to);
        }

        return attributes;
    }

    /// <summary>Handles a malformed attribute: throws under strict mode, or returns the attributes parsed so far under lenient mode (stopping at the malformed one).</summary>
    /// <param name="attributes">The attributes parsed before the malformed one.</param>
    /// <param name="message">The well-formedness message for strict mode.</param>
    /// <returns>The attributes parsed so far, under lenient mode.</returns>
    /// <exception cref="FormatException">The scanner is strict.</exception>
    private List<XmlScanAttribute> StopAttributes(List<XmlScanAttribute> attributes, string message)
    {
        if(Strictness == XmlScanStrictness.Strict)
        {
            throw new FormatException(message);
        }

        return attributes;
    }

    /// <summary>Rejects a start tag that names the same attribute twice (the XML 1.0 well-formedness constraint <i>Unique Att Spec</i>), comparing qualified names.</summary>
    /// <param name="attributes">The tag's attributes.</param>
    /// <exception cref="FormatException">Two attributes share a qualified name.</exception>
    private static void EnsureNoDuplicateAttributes(List<XmlScanAttribute> attributes)
    {
        for(int first = 0; first < attributes.Count; first++)
        {
            for(int second = first + 1; second < attributes.Count; second++)
            {
                if(attributes[first].Name.Span.SequenceEqual(attributes[second].Name.Span))
                {
                    throw new FormatException("XML start tag has a duplicate attribute name.");
                }
            }
        }
    }

    /// <summary>Emits a character-data event, unless the decoded run is empty.</summary>
    /// <param name="content">The decoded character data.</param>
    private void EmitText(Utf8String content)
    {
        if(!content.IsEmpty)
        {
            Events.Enqueue(XmlScanEvent.TextRun(content));
        }
    }

    /// <summary>Commits the scan position past a fully scanned unit.</summary>
    /// <param name="end">The offset just past the unit.</param>
    /// <returns><see cref="LexStep.Unit"/>.</returns>
    private LexStep Commit(int end)
    {
        LexPosition = end;

        return LexStep.Unit;
    }

    /// <summary>Decides whether an unfinished unit suspends for more bytes or, at a final input, is rejected (strict) or abandoned (lenient).</summary>
    /// <param name="unterminatedMessage">The well-formedness message thrown when the input is final and strict.</param>
    /// <returns><see cref="LexStep.NeedMore"/> when more input may arrive, or <see cref="LexStep.Exhausted"/> when a final input's tail is abandoned.</returns>
    /// <exception cref="FormatException">The input is final and strict, so the unit is permanently unterminated.</exception>
    private LexStep Suspend(string unterminatedMessage)
    {
        if(!Final)
        {
            return LexStep.NeedMore;
        }

        if(Strictness == XmlScanStrictness.Strict)
        {
            throw new FormatException(unterminatedMessage);
        }

        //Lenient: a permanently unterminated tail is abandoned silently; the consumer closes any element left open.
        LexPosition = Length;

        return LexStep.Exhausted;
    }

    /// <summary>Decodes a buffer range, resolving XML predefined and numeric references and (when permitted) general entities, and normalizing the range's literal whitespace per <paramref name="mode"/>; a range with neither a reference nor normalizable whitespace is a zero-copy window.</summary>
    /// <param name="start">The inclusive start offset.</param>
    /// <param name="length">The range length in bytes.</param>
    /// <param name="expandGeneral">Whether <c>&amp;name;</c> general-entity references expand (text/attribute values), as opposed to an entity replacement text being registered.</param>
    /// <param name="mode">Whether the range is text/CDATA (§2.11 line endings only) or an attribute value (§2.11 then §3.3.3 whitespace-to-space).</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="FormatException">A reference is unterminated, undefined, or the expansion budget is exhausted.</exception>
    private Utf8String Decode(int start, int length, bool expandGeneral, NormalizationMode mode)
    {
        ReadOnlySpan<byte> raw = Buffer.AsSpan(start, length);
        if(!NeedsDecoding(raw, mode))
        {
            return Utf8String.WithoutPrecomputedHash(Buffer.AsMemory(start, length));
        }

        ArrayBufferWriter<byte> output = new();
        int i = 0;
        while(i < raw.Length)
        {
            if(raw[i] == (byte)'&')
            {
                int semicolon = raw.Slice(i).IndexOf((byte)';');
                if(semicolon < 0)
                {
                    if(Strictness == XmlScanStrictness.Strict)
                    {
                        throw new FormatException("XML value has an unterminated entity reference.");
                    }

                    //Lenient: an unterminated reference stays literal — emit the rest of the run verbatim and stop.
                    output.Write(raw.Slice(i));

                    break;
                }

                ExpandReference(raw.Slice(i + 1, semicolon - 1), output, expandGeneral);
                i += semicolon + 1;

                continue;
            }

            int amp = raw.Slice(i).IndexOf((byte)'&');
            int literalEnd = amp < 0 ? raw.Length : i + amp;
            WriteNormalizedLiteral(raw.Slice(i, literalEnd - i), mode, output);
            i = literalEnd;
        }

        return Utf8String.WithoutPrecomputedHash(output.WrittenSpan.ToArray());
    }

    /// <summary>Expands one predefined, numeric, or general-entity reference into the decode buffer.</summary>
    /// <param name="reference">The reference name or numeric code, the bytes between <c>&amp;</c> and <c>;</c>.</param>
    /// <param name="output">The decode buffer.</param>
    /// <param name="expandGeneral">Whether a general-entity name resolves through the entity table.</param>
    /// <exception cref="FormatException">The reference is undefined or the expansion budget is exhausted, under strict mode.</exception>
    private void ExpandReference(ReadOnlySpan<byte> reference, ArrayBufferWriter<byte> output, bool expandGeneral)
    {
        if(reference.SequenceEqual("amp"u8))
        {
            output.Write("&"u8);
        }
        else if(reference.SequenceEqual("lt"u8))
        {
            output.Write("<"u8);
        }
        else if(reference.SequenceEqual("gt"u8))
        {
            output.Write(">"u8);
        }
        else if(reference.SequenceEqual("quot"u8))
        {
            output.Write("\""u8);
        }
        else if(reference.SequenceEqual("apos"u8))
        {
            output.Write("'"u8);
        }
        else if(reference.Length > 1 && reference[0] == (byte)'#')
        {
            ExpandNumericReference(reference, output);
        }
        else if(expandGeneral && Entities.TryGetValue(new Utf8String(reference.ToArray()), out Utf8String entity))
        {
            if(Strictness == XmlScanStrictness.Strict)
            {
                EntityBudget -= entity.Length;
                if(EntityBudget < 0)
                {
                    throw new FormatException("XML general-entity expansion exceeds the permitted budget.");
                }

                output.Write(entity.Span);
            }
            else if(EntityBudget >= entity.Length)
            {
                //Lenient: expand while the budget lasts, then leave further references unexpanded rather than throwing.
                EntityBudget -= entity.Length;
                output.Write(entity.Span);
            }
        }
        else if(Strictness == XmlScanStrictness.Strict)
        {
            throw new FormatException("XML value references an undefined general entity.");
        }
    }

    /// <summary>Expands a numeric character reference (<c>#1234</c> or <c>#x04D2</c>) into the decode buffer; a malformed or non-XML reference throws under strict mode and drops (emits nothing) under lenient mode.</summary>
    /// <param name="reference">The reference bytes, beginning with <c>#</c>.</param>
    /// <param name="output">The decode buffer.</param>
    /// <exception cref="FormatException">The numeric reference is malformed, out of range, or not a valid XML character, under strict mode.</exception>
    private void ExpandNumericReference(ReadOnlySpan<byte> reference, ArrayBufferWriter<byte> output)
    {
        bool strict = Strictness == XmlScanStrictness.Strict;
        bool hex = reference.Length > 1 && (reference[1] == (byte)'x' || reference[1] == (byte)'X');
        ReadOnlySpan<byte> digits = reference.Slice(hex ? 2 : 1);
        if(digits.IsEmpty && strict)
        {
            throw new FormatException("XML numeric character reference has no digits.");
        }

        int value = 0;
        foreach(byte digit in digits)
        {
            int place = HexValue(digit);
            if(place < 0 || (!hex && place > 9))
            {
                if(strict)
                {
                    throw new FormatException("XML numeric character reference has an invalid digit.");
                }

                return;
            }

            value = (hex ? value * 16 : value * 10) + place;
            if(value > 0x10FFFF && strict)
            {
                throw new FormatException("XML numeric character reference is out of range.");
            }
        }

        bool valid = Rune.TryCreate(value, out Rune rune);
        if(strict && (!valid || !IsXmlChar(value)))
        {
            throw new FormatException("XML numeric character reference is not a valid XML character.");
        }

        if(valid)
        {
            Span<byte> encoded = stackalloc byte[4];

            output.Write(encoded.Slice(0, rune.EncodeToUtf8(encoded)));
        }
    }

    /// <summary>Whether a byte range needs the decoding slow path: it carries a reference, or whitespace the <paramref name="mode"/> normalizes (a literal <c>CR</c> under either mode; also a literal <c>LF</c> or tab in an attribute value).</summary>
    /// <param name="raw">The bytes to test.</param>
    /// <param name="mode">The normalization mode.</param>
    /// <returns><see langword="true"/> when the range must be decoded; <see langword="false"/> when it is an unchanged window.</returns>
    private static bool NeedsDecoding(ReadOnlySpan<byte> raw, NormalizationMode mode)
    {
        return raw.IndexOfAny(mode == NormalizationMode.Attribute ? "&\r\n\t"u8 : "&\r"u8) >= 0;
    }

    /// <summary>Normalizes a CDATA range's line endings (XML 1.0 §2.11) with no reference expansion; a range with no <c>CR</c> is a zero-copy window.</summary>
    /// <param name="start">The inclusive start offset.</param>
    /// <param name="length">The range length in bytes.</param>
    /// <returns>The line-ending-normalized value.</returns>
    private Utf8String NormalizeText(int start, int length)
    {
        ReadOnlySpan<byte> raw = Buffer.AsSpan(start, length);
        if(raw.IndexOf((byte)'\r') < 0)
        {
            return Utf8String.WithoutPrecomputedHash(Buffer.AsMemory(start, length));
        }

        ArrayBufferWriter<byte> output = new();
        WriteNormalizedLiteral(raw, NormalizationMode.Text, output);

        return Utf8String.WithoutPrecomputedHash(output.WrittenSpan.ToArray());
    }

    /// <summary>Writes a reference-free literal run to the decode buffer, normalizing its whitespace per <paramref name="mode"/>: §2.11 collapses a literal <c>CR LF</c> or lone <c>CR</c> to one <c>LF</c>, and in an attribute value §3.3.3 then maps every literal <c>LF</c>, tab, and space to one space.</summary>
    /// <param name="run">The literal bytes (no entity reference).</param>
    /// <param name="mode">The normalization mode.</param>
    /// <param name="output">The decode buffer.</param>
    private static void WriteNormalizedLiteral(ReadOnlySpan<byte> run, NormalizationMode mode, ArrayBufferWriter<byte> output)
    {
        ReadOnlySpan<byte> normalizable = mode == NormalizationMode.Attribute ? "\r\n\t"u8 : "\r"u8;
        int i = 0;
        while(i < run.Length)
        {
            int next = run.Slice(i).IndexOfAny(normalizable);
            if(next < 0)
            {
                output.Write(run.Slice(i));

                return;
            }

            if(next > 0)
            {
                output.Write(run.Slice(i, next));
            }

            i += next;
            if(run[i] == (byte)'\r')
            {
                output.Write(mode == NormalizationMode.Attribute ? " "u8 : "\n"u8);
                i += i + 1 < run.Length && run[i + 1] == (byte)'\n' ? 2 : 1;
            }
            else
            {
                output.Write(" "u8);
                i++;
            }
        }
    }

    /// <summary>Whether a codepoint is a valid XML 1.0 character (the <c>Char</c> production): tab, line feed, carriage return, or a non-control scalar excluding the surrogate block, U+FFFE, and U+FFFF.</summary>
    /// <param name="value">The codepoint.</param>
    /// <returns><see langword="true"/> when the codepoint is a permitted XML character.</returns>
    private static bool IsXmlChar(int value)
    {
        return value switch
        {
            0x9 or 0xA or 0xD => true,
            >= 0x20 and <= 0xD7FF => true,
            >= 0xE000 and <= 0xFFFD => true,
            >= 0x10000 and <= 0x10FFFF => true,
            _ => false
        };
    }

    /// <summary>Finds the closing <c>&gt;</c> of a start or empty-element tag, skipping over quoted attribute values.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The offset of the closing <c>&gt;</c>, or <c>-1</c> when the buffer ends first.</returns>
    private static int IndexOfTagClose(ReadOnlySpan<byte> text, int start)
    {
        int i = start + 1;
        while(i < text.Length)
        {
            byte b = text[i];
            if(b == (byte)'"' || b == (byte)'\'')
            {
                int closeQuote = IndexOfByte(text, i + 1, text.Length, b);
                if(closeQuote < 0)
                {
                    return -1;
                }

                i = closeQuote + 1;
            }
            else if(b == (byte)'>')
            {
                return i;
            }
            else
            {
                i++;
            }
        }

        return -1;
    }

    /// <summary>Whether the bytes at an offset begin with a sequence.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset to test at.</param>
    /// <param name="prefix">The sequence to match.</param>
    /// <returns><see langword="true"/> when the bytes at the offset begin with the sequence.</returns>
    private static bool StartsWith(ReadOnlySpan<byte> text, int start, ReadOnlySpan<byte> prefix)
    {
        return start + prefix.Length <= text.Length && text.Slice(start, prefix.Length).SequenceEqual(prefix);
    }

    /// <summary>Whether a candidate run is a prefix of a keyword (and so might still grow into it).</summary>
    /// <param name="candidate">The bytes seen so far.</param>
    /// <param name="keyword">The keyword to test against.</param>
    /// <returns><see langword="true"/> when the candidate is a (possibly partial) prefix of the keyword.</returns>
    private static bool IsPrefixOf(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> keyword)
    {
        return candidate.Length <= keyword.Length && keyword.Slice(0, candidate.Length).SequenceEqual(candidate);
    }

    /// <summary>Whether a text run ends with an entity reference the buffer has not yet terminated (a final <c>&amp;</c> with no following <c>;</c>), which more bytes would complete.</summary>
    /// <param name="run">The text run bytes.</param>
    /// <returns><see langword="true"/> when the run's last reference is unterminated.</returns>
    private static bool EndsWithPartialReference(ReadOnlySpan<byte> run)
    {
        int ampersand = run.LastIndexOf((byte)'&');

        return ampersand >= 0 && run.Slice(ampersand).IndexOf((byte)';') < 0;
    }

    /// <summary>Finds a byte sequence at or after a start offset.</summary>
    /// <param name="text">The bytes to search.</param>
    /// <param name="start">The offset to search from.</param>
    /// <param name="sequence">The sequence to find.</param>
    /// <returns>The offset of the sequence, or <c>-1</c>.</returns>
    private static int IndexOf(ReadOnlySpan<byte> text, int start, ReadOnlySpan<byte> sequence)
    {
        if(start < 0 || start > text.Length)
        {
            return -1;
        }

        int found = text.Slice(start).IndexOf(sequence);

        return found < 0 ? -1 : start + found;
    }

    /// <summary>Finds a byte within a bounded range.</summary>
    /// <param name="text">The bytes to search.</param>
    /// <param name="start">The inclusive start offset.</param>
    /// <param name="end">The exclusive end offset.</param>
    /// <param name="value">The byte to find.</param>
    /// <returns>The offset of the byte, or <c>-1</c>.</returns>
    private static int IndexOfByte(ReadOnlySpan<byte> text, int start, int end, byte value)
    {
        for(int i = start; i < end && i < text.Length; i++)
        {
            if(text[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Advances past XML whitespace within a bounded range.</summary>
    /// <param name="text">The bytes to scan.</param>
    /// <param name="start">The offset to scan from.</param>
    /// <param name="end">The exclusive end offset.</param>
    /// <returns>The offset of the first non-whitespace byte at or after the start.</returns>
    private static int SkipWhitespace(ReadOnlySpan<byte> text, int start, int end)
    {
        int i = start;
        while(i < end && i < text.Length && IsXmlWhitespace(text[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>The hexadecimal value of an ASCII digit, or <c>-1</c>.</summary>
    /// <param name="b">The byte to evaluate.</param>
    /// <returns>The value in <c>0..15</c>, or <c>-1</c>.</returns>
    private static int HexValue(byte b)
    {
        return b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
            >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
            _ => -1
        };
    }

    /// <summary>Whether a byte is XML whitespace.</summary>
    /// <param name="b">The byte to test.</param>
    /// <returns><see langword="true"/> for space, tab, carriage return, or line feed.</returns>
    private static bool IsXmlWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Whether a byte may appear in an entity name: an ASCII letter, digit, underscore, hyphen, period, or a non-ASCII byte.</summary>
    /// <param name="b">The byte to test.</param>
    /// <returns><see langword="true"/> for an entity-name byte.</returns>
    private static bool IsNameByte(byte b)
    {
        return (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'0' && b <= (byte)'9')
            || b is (byte)'_' or (byte)'-' or (byte)'.' || b >= 0x80;
    }
}
