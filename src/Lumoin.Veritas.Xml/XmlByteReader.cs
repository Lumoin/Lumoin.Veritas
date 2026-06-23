using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// A byte-native, namespace-aware XML reader: it parses a UTF-8 XML document directly over its bytes into a
/// <see cref="XmlByteNode"/> element tree, with no <see cref="System.Xml"/> DOM and no UTF-16 round-trip. It is the
/// shared front-end the RDF/XML and SPARQL-results-XML readers walk instead of an <c>XDocument</c>.
/// </summary>
/// <remarks>
/// <para>
/// The reader builds the tree on an explicit open-element stack — start tags push, end tags pop, character data
/// accumulates on the open element — so it never recurses. Element and attribute names resolve through the
/// in-scope <c>xmlns</c>/<c>xmlns:prefix</c> declarations (a per-element scope stack); the implicit <c>xml</c>
/// prefix is always bound to the XML namespace. Predefined entities (<c>&amp;lt;</c>, <c>&amp;gt;</c>,
/// <c>&amp;amp;</c>, <c>&amp;quot;</c>, <c>&amp;apos;</c>) and numeric character references decode in text and
/// attribute values.
/// </para>
/// <para>
/// A DTD is rejected by default. With <see cref="Read(System.ReadOnlySpan{byte}, bool)"/> and
/// <c>parseInternalDtd: true</c> the DOCTYPE <b>internal subset</b> is parsed: its <c>&lt;!ENTITY name "value"&gt;</c>
/// general-entity declarations register, and <c>&amp;name;</c> references in text and attribute values expand to
/// them, bounded by an expansion budget (a billion-laughs guard). External resolution is never performed — no
/// external subset is fetched and no external entity is expanded — so this stays XXE-safe. (The DOCTYPE entity
/// machinery here duplicates <c>OwlXmlSyntaxIncrementalReader</c>'s; the two byte-native XML scanners are a planned
/// unification.)
/// </para>
/// <para>
/// Term and name values are sliced as owned <see cref="Utf8String"/> copies, so the tree is independent of the
/// input buffer. Malformedness — an unterminated construct, a stray <c>&lt;</c>, or a mismatched end tag — throws
/// <see cref="FormatException"/>, matching the well-formedness contract the previous <c>XmlReader</c>-based readers
/// presented.
/// </para>
/// </remarks>
public static class XmlByteReader
{
    /// <summary>The implicit <c>xml</c> prefix's namespace, always in scope.</summary>
    private static ReadOnlySpan<byte> XmlNamespace => "http://www.w3.org/XML/1998/namespace"u8;

    /// <summary>The cap on the bytes a document's <c>&amp;name;</c> general-entity references may expand to, guarding against entity-amplification.</summary>
    private const int MaxEntityExpansionBytes = 10_000_000;

    /// <summary>Reads a UTF-8 XML document into its element tree, rejecting any DTD.</summary>
    /// <param name="utf8">The document's UTF-8 bytes.</param>
    /// <returns>The document's single root element.</returns>
    /// <exception cref="FormatException">The document is not well-formed, declares a DTD, or has no single root element.</exception>
    public static XmlByteNode Read(ReadOnlySpan<byte> utf8)
    {
        return new Parser(parseInternalDtd: false).Parse(utf8);
    }

    /// <summary>Reads a UTF-8 XML document into its element tree, optionally parsing the DOCTYPE internal subset's general entities.</summary>
    /// <param name="utf8">The document's UTF-8 bytes.</param>
    /// <param name="parseInternalDtd">When <see langword="true"/>, parse the internal subset's <c>&lt;!ENTITY&gt;</c> declarations and expand <c>&amp;name;</c> references (external resolution stays off); when <see langword="false"/>, reject any DTD.</param>
    /// <returns>The document's single root element.</returns>
    /// <exception cref="FormatException">The document is not well-formed, has no single root element, or (with <paramref name="parseInternalDtd"/> <see langword="false"/>) declares a DTD.</exception>
    public static XmlByteNode Read(ReadOnlySpan<byte> utf8, bool parseInternalDtd)
    {
        return new Parser(parseInternalDtd).Parse(utf8);
    }

    /// <summary>The stateful parse of one document: the open-element and namespace-scope stacks, the entity table, and the expansion budget.</summary>
    /// <param name="parseInternalDtd">Whether to parse the DOCTYPE internal subset's general entities.</param>
    private sealed class Parser(bool parseInternalDtd)
    {
        /// <summary>Whether the DOCTYPE internal subset's general entities are parsed and expanded.</summary>
        private bool ParseInternalDtd { get; } = parseInternalDtd;

        /// <summary>The general entities declared in the internal subset, keyed by entity name, with decoded values.</summary>
        private Dictionary<Utf8String, Utf8String> Entities { get; } = [];

        /// <summary>The synthetic document root; its direct children are the document's top-level elements.</summary>
        private XmlByteNode Root { get; } = new();

        /// <summary>The open-element stack; <see cref="Root"/> sits at the bottom for the whole parse.</summary>
        private Stack<XmlByteNode> Open { get; } = new();

        /// <summary>The namespace-binding scope stack, one frame per open element.</summary>
        private Stack<Dictionary<Utf8String, Utf8String>> Scopes { get; } = new();

        /// <summary>The in-scope namespace bindings in document declaration order, one frame per open element, parallel to <see cref="Scopes"/>; a frame is shared with its parent when the element declares no namespaces.</summary>
        private Stack<IReadOnlyList<XmlNamespaceBinding>> OrderedScopes { get; } = new();

        /// <summary>The remaining budget for general-entity expansion, in bytes.</summary>
        private int entityBudget = MaxEntityExpansionBytes;

        /// <summary>Parses the document into its element tree.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <returns>The document's single root element.</returns>
        /// <exception cref="FormatException">The document is not well-formed or has no single root element.</exception>
        public XmlByteNode Parse(ReadOnlySpan<byte> utf8)
        {
            Open.Push(Root);
            Scopes.Push([]);
            OrderedScopes.Push(EmptyBindings);

            int i = 0;
            while(i < utf8.Length)
            {
                i = utf8[i] == (byte)'<' ? ScanMarkup(utf8, i) : ScanText(utf8, i);
            }

            if(Open.Count != 1)
            {
                throw new FormatException("XML document has an unclosed element.");
            }

            return SingleChild();
        }

        /// <summary>Returns the single element child of the synthetic document root.</summary>
        /// <returns>The document element.</returns>
        /// <exception cref="FormatException">The document has no element, or more than one top-level element.</exception>
        private XmlByteNode SingleChild()
        {
            XmlByteNode? found = null;
            foreach(XmlByteNode child in Root.Children)
            {
                if(found is not null)
                {
                    throw new FormatException("XML document has more than one root element.");
                }

                found = child;
            }

            return found ?? throw new FormatException("XML document has no root element.");
        }

        /// <summary>Scans a <c>&lt;…</c> markup unit (tag, comment, CDATA, processing instruction, or declaration) and returns the offset past it.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
        /// <returns>The offset just past the scanned unit.</returns>
        /// <exception cref="FormatException">The markup is unterminated or malformed.</exception>
        private int ScanMarkup(ReadOnlySpan<byte> utf8, int start)
        {
            if(start + 1 >= utf8.Length)
            {
                throw new FormatException("XML document ends with an unterminated '<'.");
            }

            return utf8[start + 1] switch
            {
                (byte)'/' => ScanEndTag(utf8, start),
                (byte)'?' => SkipDelimited(utf8, start, "?>"u8),
                (byte)'!' => ScanDeclaration(utf8, start),
                _ => ScanStartTag(utf8, start)
            };
        }

        /// <summary>Scans a <c>&lt;!…</c> declaration: a comment, a CDATA section, or (when permitted) a DOCTYPE.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
        /// <returns>The offset just past the declaration.</returns>
        /// <exception cref="FormatException">The declaration is an unsupported DTD or is malformed.</exception>
        private int ScanDeclaration(ReadOnlySpan<byte> utf8, int start)
        {
            if(StartsWith(utf8, start, "<!--"u8))
            {
                return SkipDelimited(utf8, start, "-->"u8);
            }

            if(StartsWith(utf8, start, "<![CDATA["u8))
            {
                int open = start + "<![CDATA["u8.Length;
                int close = IndexOf(utf8, open, "]]>"u8);
                if(close < 0)
                {
                    throw new FormatException("XML document has an unterminated CDATA section.");
                }

                AppendText(Utf8String.WithoutPrecomputedHash(utf8.Slice(open, close - open).ToArray()));

                return close + "]]>"u8.Length;
            }

            if(ParseInternalDtd && StartsWith(utf8, start, "<!DOCTYPE"u8))
            {
                return ScanDoctype(utf8, start);
            }

            throw new FormatException("XML document declares a DTD, which is not permitted.");
        }

        /// <summary>Scans a DOCTYPE declaration, registering the general entities its internal subset declares; external resolution is never performed.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the opening <c>&lt;</c> of the doctype.</param>
        /// <returns>The offset just past the doctype's terminating <c>&gt;</c>.</returns>
        /// <exception cref="FormatException">The doctype is unterminated.</exception>
        private int ScanDoctype(ReadOnlySpan<byte> utf8, int start)
        {
            int depth = 0;
            for(int i = start + 1; i < utf8.Length; i++)
            {
                byte b = utf8[i];
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
                    RegisterEntities(utf8, start, i);

                    return i + 1;
                }
            }

            throw new FormatException("XML document has an unterminated DOCTYPE declaration.");
        }

        /// <summary>Parses the <c>&lt;!ENTITY name "value"&gt;</c> declarations in a doctype's internal subset into the entity table.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the opening <c>&lt;</c> of the doctype.</param>
        /// <param name="end">The offset of the doctype's terminating <c>&gt;</c>.</param>
        private void RegisterEntities(ReadOnlySpan<byte> utf8, int start, int end)
        {
            int i = start;
            while(true)
            {
                int declaration = IndexOf(utf8.Slice(0, end), i, "<!ENTITY"u8);
                if(declaration < 0)
                {
                    return;
                }

                int cursor = SkipWhitespace(utf8, declaration + "<!ENTITY"u8.Length, end);
                int nameStart = cursor;
                while(cursor < end && IsNameByte(utf8[cursor]))
                {
                    cursor++;
                }

                ReadOnlySpan<byte> name = utf8.Slice(nameStart, cursor - nameStart);
                cursor = SkipWhitespace(utf8, cursor, end);
                if(cursor < end && (utf8[cursor] == (byte)'"' || utf8[cursor] == (byte)'\''))
                {
                    byte quote = utf8[cursor];
                    int valueStart = cursor + 1;
                    int valueEnd = IndexOfByte(utf8, valueStart, end, quote);
                    if(valueEnd >= 0 && name.Length > 0)
                    {
                        //An entity's replacement text decodes predefined and numeric references once at registration;
                        //it is then emitted verbatim on reference (general entities do not nest in this reader).
                        Entities[new Utf8String(name.ToArray())] = Decode(utf8.Slice(valueStart, valueEnd - valueStart), expandGeneral: false);
                        cursor = valueEnd + 1;
                    }
                }

                i = cursor + 1;
            }
        }

        /// <summary>Scans a start tag or empty-element tag, pushing or adding the element it names.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
        /// <returns>The offset just past the tag.</returns>
        /// <exception cref="FormatException">The tag is unterminated.</exception>
        private int ScanStartTag(ReadOnlySpan<byte> utf8, int start)
        {
            int close = IndexOfTagClose(utf8, start);
            if(close < 0)
            {
                throw new FormatException("XML document has an unterminated start tag.");
            }

            bool empty = utf8[close - 1] == (byte)'/';
            int nameStart = start + 1;
            int nameEnd = nameStart;
            while(nameEnd < close && !IsWhitespace(utf8[nameEnd]) && utf8[nameEnd] != (byte)'/' && utf8[nameEnd] != (byte)'>')
            {
                nameEnd++;
            }

            List<RawAttribute> raw = ParseAttributes(utf8, nameEnd, empty ? close - 1 : close);
            (Dictionary<Utf8String, Utf8String> scope, List<XmlNamespaceBinding>? local) = BindScope(utf8, raw);
            IReadOnlyList<XmlNamespaceBinding> ordered = local is null ? OrderedScopes.Peek() : MergeOrdered(OrderedScopes.Peek(), local);

            XmlByteNode node = new()
            {
                LocalName = LocalName(utf8.Slice(nameStart, nameEnd - nameStart)),
                NamespaceIri = ResolveElementNamespace(utf8.Slice(nameStart, nameEnd - nameStart), scope),
                InScopeNamespaces = ordered,
                ContentStart = empty ? 0 : close + 1
            };

            foreach(RawAttribute attribute in raw)
            {
                if(IsNamespaceDeclaration(utf8, attribute))
                {
                    continue;
                }

                node.Attributes.Add(new XmlByteAttribute(
                    LocalName(utf8.Slice(attribute.NameStart, attribute.NameEnd - attribute.NameStart)),
                    ResolveAttributeNamespace(utf8.Slice(attribute.NameStart, attribute.NameEnd - attribute.NameStart), scope),
                    Decode(utf8.Slice(attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart), expandGeneral: true)));
            }

            Open.Peek().Children.Add(node);
            if(!empty)
            {
                Open.Push(node);
                Scopes.Push(scope);
                OrderedScopes.Push(ordered);
            }

            return close + 1;
        }

        /// <summary>Scans an end tag, popping the matching open element.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
        /// <returns>The offset just past the end tag.</returns>
        /// <exception cref="FormatException">The end tag is unterminated, or does not match the open element.</exception>
        private int ScanEndTag(ReadOnlySpan<byte> utf8, int start)
        {
            int close = IndexOf(utf8, start, ">"u8);
            if(close < 0)
            {
                throw new FormatException("XML document has an unterminated end tag.");
            }

            int nameStart = start + 2;
            int nameEnd = nameStart;
            while(nameEnd < close && !IsWhitespace(utf8[nameEnd]))
            {
                nameEnd++;
            }

            if(Open.Count <= 1)
            {
                throw new FormatException("XML document has an end tag with no matching start tag.");
            }

            XmlByteNode node = Open.Peek();
            if(!node.LocalName.Span.SequenceEqual(LocalNameSpan(utf8.Slice(nameStart, nameEnd - nameStart))))
            {
                throw new FormatException("XML document has a mismatched end tag.");
            }

            node.ContentEnd = start;
            Open.Pop();
            Scopes.Pop();
            OrderedScopes.Pop();

            return close + 1;
        }

        /// <summary>Scans a text run up to the next markup, appending its entity-decoded content to the open element.</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="start">The offset of the first text byte.</param>
        /// <returns>The offset of the next <c>&lt;</c> (or the document end).</returns>
        private int ScanText(ReadOnlySpan<byte> utf8, int start)
        {
            int next = IndexOf(utf8, start, "<"u8);
            if(next < 0)
            {
                next = utf8.Length;
            }

            AppendText(Decode(utf8.Slice(start, next - start), expandGeneral: true));

            return next;
        }

        /// <summary>Appends character data to the open element, coalescing with any already accumulated; text outside any element is discarded.</summary>
        /// <param name="content">The decoded character data.</param>
        private void AppendText(Utf8String content)
        {
            if(content.IsEmpty || Open.Count <= 1)
            {
                return;
            }

            XmlByteNode node = Open.Peek();
            node.Text = node.Text.IsEmpty ? content : Concat(node.Text, content);
        }

        /// <summary>Builds the namespace scope of an element: the parent scope extended with the element's own <c>xmlns</c> declarations, plus the element's own declarations in document order (for the in-scope binding list).</summary>
        /// <param name="utf8">The document bytes.</param>
        /// <param name="attributes">The element's raw attributes.</param>
        /// <returns>The element's lookup scope (sharing the parent frame when the element declares no namespaces) and its own declarations in document order, or <see langword="null"/> when it declares none.</returns>
        private (Dictionary<Utf8String, Utf8String> Scope, List<XmlNamespaceBinding>? Local) BindScope(ReadOnlySpan<byte> utf8, List<RawAttribute> attributes)
        {
            Dictionary<Utf8String, Utf8String> parent = Scopes.Peek();
            Dictionary<Utf8String, Utf8String>? bound = null;
            List<XmlNamespaceBinding>? local = null;
            foreach(RawAttribute attribute in attributes)
            {
                ReadOnlySpan<byte> name = utf8.Slice(attribute.NameStart, attribute.NameEnd - attribute.NameStart);
                if(name.SequenceEqual("xmlns"u8))
                {
                    Utf8String iri = Decode(utf8.Slice(attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart), expandGeneral: true);
                    bound ??= new(parent);
                    bound[DefaultPrefix] = iri;
                    local ??= [];
                    local.Add(new XmlNamespaceBinding(default, iri));
                }
                else if(name.Length > 6 && name.StartsWith("xmlns:"u8))
                {
                    Utf8String prefix = new(name.Slice(6).ToArray());
                    Utf8String iri = Decode(utf8.Slice(attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart), expandGeneral: true);
                    bound ??= new(parent);
                    bound[prefix] = iri;
                    local ??= [];
                    local.Add(new XmlNamespaceBinding(prefix, iri));
                }
            }

            return (bound ?? parent, local);
        }

        /// <summary>Decodes a byte range, resolving XML predefined and numeric references and (when permitted) general entities; a reference-free range is an owned slice.</summary>
        /// <param name="raw">The bytes to decode.</param>
        /// <param name="expandGeneral">Whether <c>&amp;name;</c> general-entity references expand (text/attribute values), as opposed to an entity replacement text being registered.</param>
        /// <returns>The decoded, owned value.</returns>
        /// <exception cref="FormatException">A reference is unterminated, undefined, or the expansion budget is exhausted.</exception>
        private Utf8String Decode(ReadOnlySpan<byte> raw, bool expandGeneral)
        {
            if(raw.IndexOf((byte)'&') < 0)
            {
                return Utf8String.WithoutPrecomputedHash(raw.ToArray());
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
                    throw new FormatException("XML value has an unterminated entity reference.");
                }

                ExpandReference(raw.Slice(i + 1, semicolon - 1), output, expandGeneral);
                i += semicolon + 1;
            }

            return Utf8String.WithoutPrecomputedHash(output.WrittenSpan.ToArray());
        }

        /// <summary>Expands one predefined, numeric, or general-entity reference into the decode buffer.</summary>
        /// <param name="reference">The reference name or numeric code, the bytes between <c>&amp;</c> and <c>;</c>.</param>
        /// <param name="output">The decode buffer.</param>
        /// <param name="expandGeneral">Whether a general-entity name resolves through the entity table.</param>
        /// <exception cref="FormatException">The reference is undefined or the expansion budget is exhausted.</exception>
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
                entityBudget -= entity.Length;
                if(entityBudget < 0)
                {
                    throw new FormatException("XML general-entity expansion exceeds the permitted budget.");
                }

                output.Write(entity.Span);
            }
            else
            {
                throw new FormatException("XML value references an undefined general entity.");
            }
        }

        /// <summary>Resolves an element's qualified name to its namespace IRI (the default namespace when unprefixed).</summary>
        /// <param name="qualified">The element's qualified name bytes.</param>
        /// <param name="scope">The namespace scope in effect.</param>
        /// <returns>The resolved namespace IRI, or an empty value when none is in scope.</returns>
        private static Utf8String ResolveElementNamespace(ReadOnlySpan<byte> qualified, Dictionary<Utf8String, Utf8String> scope)
        {
            int colon = qualified.IndexOf((byte)':');

            return colon < 0 ? scope.GetValueOrDefault(DefaultPrefix) : ResolvePrefix(qualified.Slice(0, colon), scope);
        }

        /// <summary>Resolves an attribute's qualified name to its namespace IRI (no namespace when unprefixed).</summary>
        /// <param name="qualified">The attribute's qualified name bytes.</param>
        /// <param name="scope">The namespace scope in effect.</param>
        /// <returns>The resolved namespace IRI, or an empty value when the attribute is unprefixed.</returns>
        private static Utf8String ResolveAttributeNamespace(ReadOnlySpan<byte> qualified, Dictionary<Utf8String, Utf8String> scope)
        {
            int colon = qualified.IndexOf((byte)':');

            return colon < 0 ? default : ResolvePrefix(qualified.Slice(0, colon), scope);
        }

        /// <summary>Resolves a prefix to its namespace IRI; the implicit <c>xml</c> prefix is always the XML namespace.</summary>
        /// <param name="prefix">The prefix bytes.</param>
        /// <param name="scope">The namespace scope in effect.</param>
        /// <returns>The namespace IRI, or an empty value when the prefix is unbound.</returns>
        private static Utf8String ResolvePrefix(ReadOnlySpan<byte> prefix, Dictionary<Utf8String, Utf8String> scope)
        {
            return prefix.SequenceEqual("xml"u8) ? new Utf8String(XmlNamespace.ToArray()) : scope.GetValueOrDefault(new Utf8String(prefix.ToArray()));
        }
    }

    /// <summary>The raw byte ranges of one parsed attribute, before namespace resolution.</summary>
    /// <param name="NameStart">The inclusive start of the qualified name.</param>
    /// <param name="NameEnd">The exclusive end of the qualified name.</param>
    /// <param name="ValueStart">The inclusive start of the value (inside the quotes).</param>
    /// <param name="ValueEnd">The exclusive end of the value.</param>
    private readonly record struct RawAttribute(int NameStart, int NameEnd, int ValueStart, int ValueEnd);

    /// <summary>Parses the attribute list of a tag, between the element name and the tag's closing delimiter.</summary>
    /// <param name="utf8">The document bytes.</param>
    /// <param name="from">The offset just past the element name.</param>
    /// <param name="to">The offset of the tag's closing delimiter.</param>
    /// <returns>The raw attributes, in document order.</returns>
    /// <exception cref="FormatException">An attribute is malformed (no '=' or unterminated value).</exception>
    private static List<RawAttribute> ParseAttributes(ReadOnlySpan<byte> utf8, int from, int to)
    {
        List<RawAttribute> attributes = [];
        int i = SkipWhitespace(utf8, from, to);
        while(i < to)
        {
            int nameStart = i;
            while(i < to && utf8[i] != (byte)'=' && !IsWhitespace(utf8[i]))
            {
                i++;
            }

            int nameEnd = i;
            i = SkipWhitespace(utf8, i, to);
            if(i >= to || utf8[i] != (byte)'=')
            {
                throw new FormatException("XML attribute is missing its '='.");
            }

            i = SkipWhitespace(utf8, i + 1, to);
            if(i >= to || (utf8[i] != (byte)'"' && utf8[i] != (byte)'\''))
            {
                throw new FormatException("XML attribute value is not quoted.");
            }

            byte quote = utf8[i];
            int valueStart = i + 1;
            int valueEnd = IndexOfByte(utf8, valueStart, to, quote);
            if(valueEnd < 0)
            {
                throw new FormatException("XML attribute value is unterminated.");
            }

            attributes.Add(new RawAttribute(nameStart, nameEnd, valueStart, valueEnd));
            i = SkipWhitespace(utf8, valueEnd + 1, to);
        }

        return attributes;
    }

    /// <summary>Whether a raw attribute is an <c>xmlns</c> or <c>xmlns:prefix</c> namespace declaration.</summary>
    /// <param name="utf8">The document bytes.</param>
    /// <param name="attribute">The raw attribute.</param>
    /// <returns><see langword="true"/> when the attribute declares a namespace.</returns>
    private static bool IsNamespaceDeclaration(ReadOnlySpan<byte> utf8, RawAttribute attribute)
    {
        ReadOnlySpan<byte> name = utf8.Slice(attribute.NameStart, attribute.NameEnd - attribute.NameStart);

        return name.SequenceEqual("xmlns"u8) || name.StartsWith("xmlns:"u8);
    }

    /// <summary>The local name of a qualified name, as an owned <see cref="Utf8String"/>.</summary>
    /// <param name="qualified">The qualified name bytes.</param>
    /// <returns>The local-name part.</returns>
    private static Utf8String LocalName(ReadOnlySpan<byte> qualified)
    {
        return new Utf8String(LocalNameSpan(qualified).ToArray());
    }

    /// <summary>The local-name byte span of a qualified name (the part after any prefix).</summary>
    /// <param name="qualified">The qualified name bytes.</param>
    /// <returns>The local-name span.</returns>
    private static ReadOnlySpan<byte> LocalNameSpan(ReadOnlySpan<byte> qualified)
    {
        int colon = qualified.IndexOf((byte)':');

        return colon < 0 ? qualified : qualified.Slice(colon + 1);
    }

    /// <summary>Expands a numeric character reference (<c>#1234</c> or <c>#x04D2</c>) into the decode buffer.</summary>
    /// <param name="reference">The reference bytes, beginning with <c>#</c>.</param>
    /// <param name="output">The decode buffer.</param>
    /// <exception cref="FormatException">The numeric reference is malformed or out of range.</exception>
    private static void ExpandNumericReference(ReadOnlySpan<byte> reference, ArrayBufferWriter<byte> output)
    {
        bool hex = reference.Length > 1 && (reference[1] == (byte)'x' || reference[1] == (byte)'X');
        ReadOnlySpan<byte> digits = reference.Slice(hex ? 2 : 1);
        if(digits.IsEmpty)
        {
            throw new FormatException("XML numeric character reference has no digits.");
        }

        int value = 0;
        foreach(byte digit in digits)
        {
            int place = HexValue(digit);
            if(place < 0 || (!hex && place > 9))
            {
                throw new FormatException("XML numeric character reference has an invalid digit.");
            }

            value = (hex ? value * 16 : value * 10) + place;
        }

        if(!Rune.TryCreate(value, out Rune rune))
        {
            throw new FormatException("XML numeric character reference is not a valid Unicode scalar value.");
        }

        Span<byte> encoded = stackalloc byte[4];

        output.Write(encoded.Slice(0, rune.EncodeToUtf8(encoded)));
    }

    /// <summary>The scope key standing for the default (unprefixed) namespace binding.</summary>
    private static Utf8String DefaultPrefix { get; } = new("\0default"u8.ToArray());

    /// <summary>The shared empty in-scope binding list for the document root (never mutated).</summary>
    private static IReadOnlyList<XmlNamespaceBinding> EmptyBindings { get; } = [];

    /// <summary>Extends a parent element's in-scope bindings with a child's own declarations, in document order: a redeclared prefix keeps its outermost position with the child's IRI; a new prefix appends.</summary>
    /// <param name="parent">The parent's in-scope bindings, in document order.</param>
    /// <param name="local">The element's own declarations, in document order.</param>
    /// <returns>The element's in-scope bindings, in document order.</returns>
    private static List<XmlNamespaceBinding> MergeOrdered(IReadOnlyList<XmlNamespaceBinding> parent, List<XmlNamespaceBinding> local)
    {
        List<XmlNamespaceBinding> merged = [.. parent];
        foreach(XmlNamespaceBinding binding in local)
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

    /// <summary>Concatenates two decoded text fragments into one owned buffer.</summary>
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

    /// <summary>Finds the closing <c>&gt;</c> of a start or empty-element tag, skipping over quoted attribute values.</summary>
    /// <param name="utf8">The document bytes.</param>
    /// <param name="start">The offset of the opening <c>&lt;</c>.</param>
    /// <returns>The offset of the closing <c>&gt;</c>, or <c>-1</c> when none is found.</returns>
    private static int IndexOfTagClose(ReadOnlySpan<byte> utf8, int start)
    {
        int i = start + 1;
        while(i < utf8.Length)
        {
            byte b = utf8[i];
            if(b == (byte)'"' || b == (byte)'\'')
            {
                int closeQuote = IndexOfByte(utf8, i + 1, utf8.Length, b);
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

    /// <summary>Skips a delimited unit (comment, processing instruction) and returns the offset past its closing delimiter.</summary>
    /// <param name="utf8">The document bytes.</param>
    /// <param name="start">The offset of the opening delimiter.</param>
    /// <param name="closing">The closing delimiter bytes.</param>
    /// <returns>The offset just past the closing delimiter.</returns>
    /// <exception cref="FormatException">The unit is unterminated.</exception>
    private static int SkipDelimited(ReadOnlySpan<byte> utf8, int start, ReadOnlySpan<byte> closing)
    {
        int close = IndexOf(utf8, start + 2, closing);
        if(close < 0)
        {
            throw new FormatException("XML document has an unterminated comment or processing instruction.");
        }

        return close + closing.Length;
    }

    /// <summary>Whether the bytes at an offset begin with a sequence.</summary>
    /// <param name="utf8">The document bytes.</param>
    /// <param name="start">The offset to test at.</param>
    /// <param name="prefix">The sequence to match.</param>
    /// <returns><see langword="true"/> when the bytes at the offset begin with the sequence.</returns>
    private static bool StartsWith(ReadOnlySpan<byte> utf8, int start, ReadOnlySpan<byte> prefix)
    {
        return start + prefix.Length <= utf8.Length && utf8.Slice(start, prefix.Length).SequenceEqual(prefix);
    }

    /// <summary>Finds a byte sequence at or after a start offset.</summary>
    /// <param name="utf8">The bytes to search.</param>
    /// <param name="start">The offset to search from.</param>
    /// <param name="sequence">The sequence to find.</param>
    /// <returns>The offset of the sequence, or <c>-1</c>.</returns>
    private static int IndexOf(ReadOnlySpan<byte> utf8, int start, ReadOnlySpan<byte> sequence)
    {
        if(start < 0 || start > utf8.Length)
        {
            return -1;
        }

        int found = utf8.Slice(start).IndexOf(sequence);

        return found < 0 ? -1 : start + found;
    }

    /// <summary>Finds a byte within a bounded range.</summary>
    /// <param name="utf8">The bytes to search.</param>
    /// <param name="start">The inclusive start offset.</param>
    /// <param name="end">The exclusive end offset.</param>
    /// <param name="value">The byte to find.</param>
    /// <returns>The offset of the byte, or <c>-1</c>.</returns>
    private static int IndexOfByte(ReadOnlySpan<byte> utf8, int start, int end, byte value)
    {
        for(int i = start; i < end && i < utf8.Length; i++)
        {
            if(utf8[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Advances past XML whitespace within a bounded range.</summary>
    /// <param name="utf8">The bytes to scan.</param>
    /// <param name="start">The offset to scan from.</param>
    /// <param name="end">The exclusive end offset.</param>
    /// <returns>The offset of the first non-whitespace byte at or after the start.</returns>
    private static int SkipWhitespace(ReadOnlySpan<byte> utf8, int start, int end)
    {
        int i = start;
        while(i < end && i < utf8.Length && IsWhitespace(utf8[i]))
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
    private static bool IsWhitespace(byte b)
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
