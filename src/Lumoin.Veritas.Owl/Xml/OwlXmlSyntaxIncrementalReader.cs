using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Owl.Syntax;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>Whether incrementally fed OWL/XML ends at a document boundary or inside an unfinished construct.</summary>
public enum OwlXmlParseStatus
{
    /// <summary>The input so far ends at a document boundary: no element is open and no markup is half-scanned.</summary>
    Complete = 0,

    /// <summary>The input so far ends inside an open element or a half-scanned markup unit; more bytes are needed.</summary>
    NeedMore = 1
}

/// <summary>
/// Reads an OWL 2 XML serialization document fed incrementally, chunk by chunk,
/// over UTF-8 bytes, into the same structural model the other OWL front-ends
/// produce.
/// </summary>
/// <remarks>
/// <para>
/// A byte-native XML scanner builds the element tree on an explicit open-element
/// stack: start tags push, end tags pop, character data accumulates on the open
/// element. The scanner commits one markup unit — a tag, a comment, a processing
/// instruction, a CDATA section, a doctype, or a text run — at a time, and a unit
/// whose end the buffer has not yet delivered suspends and re-scans once more
/// bytes arrive, so a chunk boundary never splits a token. When the document is
/// declared final the open <c>Ontology</c> element converts to structural axioms.
/// </para>
/// <para>
/// The reader is value-based: malformedness is recorded in the document's
/// diagnostics and reading continues where structure permits. Incompleteness is a
/// <see cref="Status"/>, never a diagnostic; truncation becomes an error only when
/// <see cref="Complete"/> declares the input final.
/// </para>
/// </remarks>
public sealed class OwlXmlSyntaxIncrementalReader
{
    /// <summary>The document bytes fed so far.</summary>
    private byte[] Buffer { get; set; } = new byte[256];

    /// <summary>The number of valid bytes in <see cref="Buffer"/>.</summary>
    private int Length { get; set; }

    /// <summary>The committed scan position: the start of the next unscanned markup.</summary>
    private int LexPosition { get; set; }

    /// <summary>Whether the last drain suspended mid-markup waiting for more bytes.</summary>
    private bool LexSuspended { get; set; }

    /// <summary>Whether <see cref="Complete"/> has declared the input final.</summary>
    private bool Final { get; set; }

    /// <summary>The completed document, built once by <see cref="Complete"/>.</summary>
    private OwlOntologyDocument? Document { get; set; }

    /// <summary>Maps byte offsets to line-column source spans.</summary>
    private OwlByteSourceMap Map { get; } = new();

    /// <summary>The converter the completed ontology element folds into structural axioms.</summary>
    private OwlXmlSyntaxConverter Converter { get; } = new();

    /// <summary>The tree root standing for the document; its direct children are the top-level elements.</summary>
    private OwlXmlNode Root { get; } = new();

    /// <summary>The open-element stack; <see cref="Root"/> sits at the bottom for the whole parse.</summary>
    private Stack<OwlXmlNode> Open { get; } = new();

    /// <summary>The namespace-binding scope stack, one frame per open element; the bottom frame carries the implicit <c>xml</c> binding.</summary>
    private Stack<Dictionary<Utf8String, Utf8String>> Scopes { get; } = new();

    /// <summary>The general entities declared in the document's internal subset, keyed by entity name.</summary>
    private Dictionary<Utf8String, Utf8String> Entities { get; } = [];

    /// <summary>Initialises an empty reader; feed source bytes through <see cref="Feed"/>.</summary>
    public OwlXmlSyntaxIncrementalReader()
    {
        Open.Push(Root);
        Scopes.Push([]);
    }

    /// <summary>Gets the diagnostics recorded so far; an unfinished tail is reported through <see cref="Status"/>, never here.</summary>
    public DiagnosticBag Diagnostics => Converter.Diagnostics;

    /// <summary>Gets whether the input fed so far ends at a document boundary or inside an unfinished construct.</summary>
    public OwlXmlParseStatus Status =>
        LexSuspended || Open.Count > 1
            ? OwlXmlParseStatus.NeedMore
            : OwlXmlParseStatus.Complete;

    /// <summary>Appends source bytes and scans as far as the input now permits.</summary>
    /// <param name="chunk">The next run of document bytes, of any length.</param>
    /// <returns>The <see cref="Status"/> after the chunk is consumed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has already been called.</exception>
    public OwlXmlParseStatus Feed(ReadOnlySpan<byte> chunk)
    {
        if(Final)
        {
            throw new InvalidOperationException("The reader has been completed; no more input can be fed.");
        }

        Append(chunk);
        Drain();

        return Status;
    }

    /// <summary>Declares the input final and returns the structural document; truncation is an error from here on.</summary>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    public OwlOntologyDocument Complete()
    {
        if(Document is OwlOntologyDocument document)
        {
            return document;
        }

        Final = true;
        Drain();
        CloseUnterminatedElements();
        Converter.Convert(FindOntology());

        Document = new OwlOntologyDocument(
            Converter.Axioms.ToImmutable(),
            Converter.OntologyIri,
            Converter.Diagnostics,
            Converter.DeclaredClasses,
            Converter.DeclaredObjectProperties,
            Converter.DeclaredDataProperties,
            Converter.DeclaredAnnotationProperties,
            Converter.DeclaredDatatypes);

        return Document;
    }

    /// <summary>Appends a chunk to the byte buffer and extends the source map over it.</summary>
    /// <param name="chunk">The bytes to append.</param>
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

    /// <summary>Scans and applies markup units until the buffer is exhausted or a unit suspends.</summary>
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

    /// <summary>
    /// Scans the next markup unit at the committed position, applies it to the
    /// element tree, and commits the position past it.
    /// </summary>
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
        //A lone '<' at the very end is undecided until the next byte arrives.
        if(start + 1 >= text.Length)
        {
            return Suspend(text, start);
        }

        byte second = text[start + 1];

        switch(second)
        {
            case((byte)'/'):
            {
                return TryScanEndTag(text, start);
            }
            case((byte)'?'):
            {
                return TrySkipDelimited(text, start, "<?"u8, "?>"u8);
            }
            case((byte)'!'):
            {
                return TryScanDeclaration(text, start);
            }
            default:
            {
                return TryScanStartTag(text, start);
            }
        }
    }

    /// <summary>Scans a <c>&lt;!…</c> declaration: a comment, a CDATA section, or a doctype.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanDeclaration(ReadOnlySpan<byte> text, int start)
    {
        if(StartsWith(text, start, "<!--"u8))
        {
            return TrySkipDelimited(text, start, "<!--"u8, "-->"u8);
        }

        if(StartsWith(text, start, "<![CDATA["u8))
        {
            return TryScanCdata(text, start);
        }

        if(StartsWith(text, start, "<!DOCTYPE"u8) || text.Slice(start).Length < "<!DOCTYPE"u8.Length)
        {
            return TryScanDoctype(text, start);
        }

        //An unrecognised declaration is skipped to its terminating '>'.
        return TrySkipDelimited(text, start, "<!"u8, ">"u8);
    }

    /// <summary>Scans a CDATA section, appending its verbatim content to the open element.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanCdata(ReadOnlySpan<byte> text, int start)
    {
        int open = start + "<![CDATA["u8.Length;
        int close = IndexOf(text, open, "]]>"u8);
        if(close < 0)
        {
            return Suspend(text, start);
        }

        AppendText(new Utf8String(Buffer.AsMemory(open, close - open)));

        return Commit(close + "]]>"u8.Length);
    }

    /// <summary>Scans a doctype declaration, registering any general entities its internal subset declares.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
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

        return Suspend(text, start);
    }

    /// <summary>Parses <c>&lt;!ENTITY name "value"&gt;</c> declarations in a doctype's internal subset into the entity table.</summary>
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

            int cursor = declaration + "<!ENTITY"u8.Length;
            cursor = SkipWhitespace(text, cursor, end);
            int nameStart = cursor;
            while(cursor < end && IsNameByte(text[cursor]))
            {
                cursor++;
            }

            Utf8String name = new(Buffer.AsMemory(nameStart, cursor - nameStart));
            cursor = SkipWhitespace(text, cursor, end);
            if(cursor < end && (text[cursor] == (byte)'"' || text[cursor] == (byte)'\''))
            {
                byte quote = text[cursor];
                int valueStart = cursor + 1;
                int valueEnd = IndexOfByte(text, valueStart, end, quote);
                if(valueEnd >= 0 && name.Length > 0)
                {
                    Entities[name] = Decode(valueStart, valueEnd - valueStart);
                    cursor = valueEnd + 1;
                }
            }

            i = cursor + 1;
        }
    }

    /// <summary>Scans a start tag or empty-element tag, pushing or adding the element it names.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanStartTag(ReadOnlySpan<byte> text, int start)
    {
        int close = IndexOfTagClose(text, start);
        if(close < 0)
        {
            return Suspend(text, start);
        }

        bool empty = text[close - 1] == (byte)'/';
        int nameStart = start + 1;
        int nameEnd = nameStart;
        while(nameEnd < close && !IsXmlWhitespace(text[nameEnd]) && text[nameEnd] != (byte)'/' && text[nameEnd] != (byte)'>')
        {
            nameEnd++;
        }

        List<OwlXmlAttribute> attributes = ParseAttributes(text, nameEnd, empty ? close - 1 : close);
        Dictionary<Utf8String, Utf8String> scope = BindScope(attributes);
        Utf8String rawName = new(Buffer.AsMemory(nameStart, nameEnd - nameStart));
        (Utf8String namespaceIri, Utf8String localName) = ResolveQName(rawName, scope);

        OwlXmlNode node = new()
        {
            Element = OwlXmlNames.Resolve(localName),
            LocalName = localName,
            NamespaceIri = namespaceIri,
            SpanStart = start,
        };
        node.Attributes.AddRange(attributes);

        Open.Peek().Children.Add(node);

        if(empty)
        {
            node.Span = Map.Span(start, close + 1);
        }
        else
        {
            Open.Push(node);
            Scopes.Push(scope);
        }

        return Commit(close + 1);
    }

    /// <summary>Scans an end tag, closing the matching open element.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanEndTag(ReadOnlySpan<byte> text, int start)
    {
        int close = IndexOfByte(text, start, text.Length, (byte)'>');
        if(close < 0)
        {
            return Suspend(text, start);
        }

        if(Open.Count > 1)
        {
            OwlXmlNode node = Open.Pop();
            Scopes.Pop();
            node.Span = Map.Span(node.SpanStart, close + 1);
        }

        return Commit(close + 1);
    }

    /// <summary>Scans a text run up to the next markup, appending its entity-decoded content to the open element.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the first text byte.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TryScanText(ReadOnlySpan<byte> text, int start)
    {
        int next = IndexOfByte(text, start, text.Length, (byte)'<');
        if(next < 0)
        {
            //Character data inside an open element is significant and is decoded
            //as one unit, so an unterminated in-element run waits for the '<'
            //that ends it: decoding a reference or a code point split across the
            //boundary in halves would corrupt it. Top-level character data is the
            //insignificant whitespace between and after the document's elements;
            //it is discarded, so it never suspends and a whitespace tail after
            //the closed root reads as a document boundary, not an unfinished unit.
            if(!Final && Open.Count > 1)
            {
                return Suspend(text, start);
            }

            next = text.Length;
        }

        AppendText(Decode(start, next - start));

        return Commit(next);
    }

    /// <summary>Skips a delimited unit (comment, processing instruction, or unrecognised declaration) that carries no tree content.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the opening delimiter.</param>
    /// <param name="opening">The opening delimiter bytes.</param>
    /// <param name="closing">The closing delimiter bytes.</param>
    /// <returns>The scan outcome.</returns>
    private LexStep TrySkipDelimited(ReadOnlySpan<byte> text, int start, ReadOnlySpan<byte> opening, ReadOnlySpan<byte> closing)
    {
        int close = IndexOf(text, start + opening.Length, closing);
        if(close < 0)
        {
            return Suspend(text, start);
        }

        return Commit(close + closing.Length);
    }

    /// <summary>Parses the attribute list of a tag, between the element name and the tag's closing delimiter.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="from">The offset just past the element name.</param>
    /// <param name="to">The offset of the tag's closing delimiter.</param>
    /// <returns>The parsed attributes, in document order.</returns>
    private List<OwlXmlAttribute> ParseAttributes(ReadOnlySpan<byte> text, int from, int to)
    {
        List<OwlXmlAttribute> attributes = [];
        int i = from;
        while(true)
        {
            i = SkipWhitespace(text, i, to);
            if(i >= to || !IsNameStartByte(text[i]))
            {
                return attributes;
            }

            int nameStart = i;
            while(i < to && IsNameByte(text[i]))
            {
                i++;
            }

            int nameEnd = i;
            i = SkipWhitespace(text, i, to);
            if(i >= to || text[i] != (byte)'=')
            {
                return attributes;
            }

            i = SkipWhitespace(text, i + 1, to);
            if(i >= to || (text[i] != (byte)'"' && text[i] != (byte)'\''))
            {
                return attributes;
            }

            byte quote = text[i];
            int valueStart = i + 1;
            int valueEnd = IndexOfByte(text, valueStart, to, quote);
            if(valueEnd < 0)
            {
                return attributes;
            }

            Utf8String name = new(Buffer.AsMemory(nameStart, nameEnd - nameStart));
            attributes.Add(new OwlXmlAttribute(name, Decode(valueStart, valueEnd - valueStart), nameStart, valueEnd + 1));
            i = valueEnd + 1;
        }
    }

    /// <summary>Builds the namespace scope of an element: the open scope extended with the element's own <c>xmlns</c> declarations.</summary>
    /// <param name="attributes">The element's attributes.</param>
    /// <returns>The element's scope, sharing the parent frame when the element declares no namespaces.</returns>
    private Dictionary<Utf8String, Utf8String> BindScope(List<OwlXmlAttribute> attributes)
    {
        Dictionary<Utf8String, Utf8String>? bound = null;
        foreach(OwlXmlAttribute attribute in attributes)
        {
            ReadOnlySpan<byte> name = attribute.Name.Span;
            if(name.SequenceEqual(OwlXmlNames.XmlnsAttribute))
            {
                bound ??= new(Scopes.Peek());
                bound[DefaultPrefix] = attribute.Value;
            }
            else if(name.Length > 6 && name.StartsWith("xmlns:"u8))
            {
                bound ??= new(Scopes.Peek());
                bound[new Utf8String(attribute.Name.Memory.Slice(6))] = attribute.Value;
            }
        }

        return bound ?? Scopes.Peek();
    }

    /// <summary>Resolves an element's qualified name to its namespace IRI and local name.</summary>
    /// <param name="rawName">The element's qualified name as written.</param>
    /// <param name="scope">The namespace scope in effect for the element.</param>
    /// <returns>The resolved namespace IRI and local name.</returns>
    private static (Utf8String NamespaceIri, Utf8String LocalName) ResolveQName(Utf8String rawName, Dictionary<Utf8String, Utf8String> scope)
    {
        int colon = rawName.Span.IndexOf((byte)':');
        if(colon < 0)
        {
            return (scope.GetValueOrDefault(DefaultPrefix), rawName);
        }

        Utf8String prefix = new(rawName.Memory.Slice(0, colon));
        Utf8String local = new(rawName.Memory.Slice(colon + 1));

        return (scope.GetValueOrDefault(prefix), local);
    }

    /// <summary>Appends character data to the open element, coalescing with any already accumulated.</summary>
    /// <param name="content">The decoded character data.</param>
    private void AppendText(Utf8String content)
    {
        if(content.IsEmpty || Open.Count <= 1)
        {
            return;
        }

        OwlXmlNode node = Open.Peek();
        node.Text = node.Text.IsEmpty ? content : Concat(node.Text, content);
    }

    /// <summary>Closes every element still open at the end of a final input, fixing each span to the input end.</summary>
    private void CloseUnterminatedElements()
    {
        while(Open.Count > 1)
        {
            OwlXmlNode node = Open.Pop();
            Scopes.Pop();
            node.Span = Map.Span(node.SpanStart, Length);
        }
    }

    /// <summary>Finds the document's <c>Ontology</c> element among the top-level elements.</summary>
    /// <returns>The ontology element, or <see langword="null"/> when the document has none.</returns>
    private OwlXmlNode? FindOntology()
    {
        foreach(OwlXmlNode child in Root.Children)
        {
            if(child.Element == OwlXmlElement.Ontology)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Commits the scan position past a fully scanned unit.</summary>
    /// <param name="end">The offset just past the unit.</param>
    /// <returns><see cref="LexStep.Unit"/>.</returns>
    private LexStep Commit(int end)
    {
        LexPosition = end;

        return LexStep.Unit;
    }

    /// <summary>Decides whether an unfinished unit suspends for more bytes or, at a final tail, is abandoned.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset of the unfinished unit.</param>
    /// <returns><see cref="LexStep.NeedMore"/> when more input may arrive, otherwise <see cref="LexStep.Exhausted"/>.</returns>
    private LexStep Suspend(ReadOnlySpan<byte> text, int start)
    {
        if(Final)
        {
            LexPosition = text.Length;

            return LexStep.Exhausted;
        }

        return LexStep.NeedMore;
    }

    /// <summary>Decodes a buffer range, resolving XML entity and character references; a range with no reference is a zero-copy window.</summary>
    /// <param name="start">The inclusive start offset.</param>
    /// <param name="length">The range length in bytes.</param>
    /// <returns>The decoded text.</returns>
    private Utf8String Decode(int start, int length)
    {
        ReadOnlySpan<byte> raw = Buffer.AsSpan(start, length);
        if(raw.IndexOf((byte)'&') < 0)
        {
            return Utf8String.WithoutPrecomputedHash(Buffer.AsMemory(start, length));
        }

        ArrayBufferWriter<byte> output = new();
        int i = 0;
        while(i < raw.Length)
        {
            if(raw[i] != (byte)'&')
            {
                output.Write(raw.Slice(i, 1));
                i++;

                continue;
            }

            int semicolon = raw.Slice(i).IndexOf((byte)';');
            if(semicolon < 0)
            {
                output.Write(raw.Slice(i));

                break;
            }

            ReadOnlySpan<byte> reference = raw.Slice(i + 1, semicolon - 1);
            ExpandReference(reference, output);
            i += semicolon + 1;
        }

        return Utf8String.WithoutPrecomputedHash(output.WrittenMemory.ToArray());
    }

    /// <summary>Expands one entity or character reference into the decode buffer.</summary>
    /// <param name="reference">The reference name or numeric code, the bytes between <c>&amp;</c> and <c>;</c>.</param>
    /// <param name="output">The decode buffer.</param>
    private void ExpandReference(ReadOnlySpan<byte> reference, ArrayBufferWriter<byte> output)
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
        else if(Entities.TryGetValue(new Utf8String(reference.ToArray()), out Utf8String entity))
        {
            output.Write(entity.Span);
        }
    }

    /// <summary>Expands a numeric character reference (<c>#1234</c> or <c>#x04D2</c>) into the decode buffer.</summary>
    /// <param name="reference">The reference bytes, beginning with <c>#</c>.</param>
    /// <param name="output">The decode buffer.</param>
    private static void ExpandNumericReference(ReadOnlySpan<byte> reference, ArrayBufferWriter<byte> output)
    {
        bool hex = reference.Length > 1 && (reference[1] == (byte)'x' || reference[1] == (byte)'X');
        ReadOnlySpan<byte> digits = reference.Slice(hex ? 2 : 1);
        int value = 0;
        foreach(byte digit in digits)
        {
            int place = HexValue(digit);
            if(place < 0 || (!hex && place > 9))
            {
                return;
            }

            value = (hex ? value * 16 : value * 10) + place;
        }

        if(Rune.TryCreate(value, out Rune rune))
        {
            Span<byte> encoded = stackalloc byte[4];
            int written = rune.EncodeToUtf8(encoded);
            output.Write(encoded.Slice(0, written));
        }
    }

    /// <summary>Concatenates two decoded text fragments into one buffer.</summary>
    /// <param name="left">The leading fragment.</param>
    /// <param name="right">The trailing fragment.</param>
    /// <returns>The concatenation.</returns>
    private static Utf8String Concat(Utf8String left, Utf8String right)
    {
        byte[] joined = new byte[left.Length + right.Length];
        left.Span.CopyTo(joined);
        right.Span.CopyTo(joined.AsSpan(left.Length));

        return Utf8String.WithoutPrecomputedHash(joined);
    }

    /// <summary>The scope key standing for the default (unprefixed) namespace binding.</summary>
    private static Utf8String DefaultPrefix { get; } = new("\0default"u8.ToArray());

    /// <summary>Finds the offset of the closing <c>&gt;</c> of a start or empty-element tag, skipping over quoted attribute values.</summary>
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

    /// <summary>Whether the buffer at an offset begins with a byte sequence.</summary>
    /// <param name="text">The buffer bytes fed so far.</param>
    /// <param name="start">The offset to test at.</param>
    /// <param name="prefix">The byte sequence to match.</param>
    /// <returns><see langword="true"/> when the bytes at the offset begin with the sequence.</returns>
    private static bool StartsWith(ReadOnlySpan<byte> text, int start, ReadOnlySpan<byte> prefix)
    {
        return start + prefix.Length <= text.Length && text.Slice(start, prefix.Length).SequenceEqual(prefix);
    }

    /// <summary>Finds the offset of a byte sequence at or after a start offset.</summary>
    /// <param name="text">The bytes to search.</param>
    /// <param name="start">The offset to search from.</param>
    /// <param name="sequence">The byte sequence to find.</param>
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

    /// <summary>Finds the offset of a byte within a bounded range.</summary>
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

    /// <summary>The hexadecimal value of an ASCII digit, or <c>-1</c> when the byte is not a hexadecimal digit.</summary>
    /// <param name="b">The byte to evaluate.</param>
    /// <returns>The value in <c>0..15</c>, or <c>-1</c>.</returns>
    private static int HexValue(byte b)
    {
        return b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
            >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
            _ => -1,
        };
    }

    /// <summary>Whether a byte is XML whitespace: space, tab, carriage return, or line feed.</summary>
    /// <param name="b">The byte to test.</param>
    /// <returns><see langword="true"/> for XML whitespace.</returns>
    private static bool IsXmlWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Whether a byte may start an XML name: an ASCII letter, an underscore, a colon, or any non-ASCII byte.</summary>
    /// <param name="b">The byte to test.</param>
    /// <returns><see langword="true"/> for a name-start byte.</returns>
    private static bool IsNameStartByte(byte b)
    {
        return b is (byte)'_' or (byte)':' || (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z') || b >= 0x80;
    }

    /// <summary>Whether a byte may continue an XML name: a name-start byte, an ASCII digit, a hyphen, or a period.</summary>
    /// <param name="b">The byte to test.</param>
    /// <returns><see langword="true"/> for a name byte.</returns>
    private static bool IsNameByte(byte b)
    {
        return IsNameStartByte(b) || (b >= (byte)'0' && b <= (byte)'9') || b is (byte)'-' or (byte)'.';
    }
}
