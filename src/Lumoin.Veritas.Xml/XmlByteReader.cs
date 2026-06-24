using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Xml;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// A byte-native, namespace-aware XML reader: it parses a UTF-8 XML document into a <see cref="XmlByteNode"/> element
/// tree, with no <see cref="System.Xml"/> DOM and no UTF-16 round-trip. It is the shared front end the RDF/XML and
/// SPARQL-results-XML readers walk instead of an <c>XDocument</c>.
/// </summary>
/// <remarks>
/// <para>
/// The reader drives the shared, byte-native <see cref="XmlByteScanner"/> over the whole buffer and folds its flat
/// event stream — start tag, end tag, character data — onto an explicit open-element stack: start tags push, end tags
/// pop, character data accumulates on the open element, so it never recurses. The scanner resolves entity and numeric
/// references, normalizes literal line endings to <c>LF</c> in text and CDATA (XML 1.0 §2.11) and attribute whitespace
/// to spaces (§3.3.3), expands a DOCTYPE internal subset's general entities (bounded against amplification, external
/// resolution never performed so this stays XXE-safe), and reports malformed tokens. The reader resolves element and
/// attribute names through the in-scope <c>xmlns</c>/<c>xmlns:prefix</c> declarations (a per-element scope stack); the
/// implicit <c>xml</c> prefix is always bound to the XML namespace.
/// </para>
/// <para>
/// A DTD is rejected by default. With <see cref="Read(System.ReadOnlySpan{byte}, bool)"/> and
/// <c>parseInternalDtd: true</c> the DOCTYPE internal subset's general entities are parsed and expanded.
/// </para>
/// <para>
/// Term and name values are owned <see cref="Utf8String"/> windows over the scanner's buffer, so the tree is
/// independent of the input span. Malformedness — an unterminated construct, a stray <c>&lt;</c>, a mismatched end
/// tag, or no single root — throws <see cref="FormatException"/>: the scanner raises token-level violations, the reader
/// raises the structural ones (an orphaned or mismatched end tag, an unclosed or absent root).
/// </para>
/// </remarks>
public static class XmlByteReader
{
    /// <summary>The implicit <c>xml</c> prefix's namespace, always in scope.</summary>
    private static ReadOnlySpan<byte> XmlNamespace => "http://www.w3.org/XML/1998/namespace"u8;

    /// <summary>Reads a UTF-8 XML document into its element tree, rejecting any DTD.</summary>
    /// <param name="utf8">The document's UTF-8 bytes.</param>
    /// <returns>The document's single root element.</returns>
    /// <exception cref="FormatException">The document is not well-formed, declares a DTD, or has no single root element.</exception>
    public static XmlByteNode Read(ReadOnlySpan<byte> utf8)
    {
        return Fold(new XmlByteScanner(XmlScanStrictness.Strict, parseInternalDtd: false), utf8);
    }

    /// <summary>Reads a UTF-8 XML document into its element tree, optionally parsing the DOCTYPE internal subset's general entities.</summary>
    /// <param name="utf8">The document's UTF-8 bytes.</param>
    /// <param name="parseInternalDtd">When <see langword="true"/>, parse the internal subset's <c>&lt;!ENTITY&gt;</c> declarations and expand <c>&amp;name;</c> references (external resolution stays off); when <see langword="false"/>, reject any DTD.</param>
    /// <returns>The document's single root element.</returns>
    /// <exception cref="FormatException">The document is not well-formed, has no single root element, or (with <paramref name="parseInternalDtd"/> <see langword="false"/>) declares a DTD.</exception>
    public static XmlByteNode Read(ReadOnlySpan<byte> utf8, bool parseInternalDtd)
    {
        return Fold(new XmlByteScanner(XmlScanStrictness.Strict, parseInternalDtd), utf8);
    }

    /// <summary>Feeds the whole document to the scanner and folds its event stream into the element tree.</summary>
    /// <param name="scanner">The scanner to drive over the whole buffer.</param>
    /// <param name="utf8">The document bytes.</param>
    /// <returns>The document's single root element.</returns>
    /// <exception cref="FormatException">The document is not well-formed or has no single root element.</exception>
    private static XmlByteNode Fold(XmlByteScanner scanner, ReadOnlySpan<byte> utf8)
    {
        scanner.Feed(utf8);
        scanner.Complete();

        Folder folder = new(scanner);
        while(scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            folder.Apply(scanEvent);
        }

        return folder.Result();
    }

    /// <summary>The stateful fold of the scanner's event stream into an element tree: the open-element and namespace-scope stacks.</summary>
    private sealed class Folder
    {
        /// <summary>The scanner the fold resolves byte offsets through into source spans.</summary>
        private XmlByteScanner Scanner { get; }

        /// <summary>The synthetic document root; its direct children are the document's top-level elements.</summary>
        private XmlByteNode Root { get; } = new();

        /// <summary>The open-element stack; <see cref="Root"/> sits at the bottom for the whole fold.</summary>
        private Stack<XmlByteNode> Open { get; } = new();

        /// <summary>The namespace-binding scope stack, one frame per open element.</summary>
        private Stack<Dictionary<Utf8String, Utf8String>> Scopes { get; } = new();

        /// <summary>The in-scope namespace bindings in document declaration order, one frame per open element, parallel to <see cref="Scopes"/>; a frame is shared with its parent when the element declares no namespaces.</summary>
        private Stack<IReadOnlyList<XmlNamespaceBinding>> OrderedScopes { get; } = new();

        /// <summary>Seeds the fold with the synthetic root and its empty namespace scope.</summary>
        /// <param name="scanner">The scanner whose offsets the fold resolves to spans.</param>
        public Folder(XmlByteScanner scanner)
        {
            Scanner = scanner;
            Open.Push(Root);
            Scopes.Push([]);
            OrderedScopes.Push(EmptyBindings);
        }

        /// <summary>Applies one scan event to the tree; the end-of-document event needs no folding.</summary>
        /// <param name="scanEvent">The event to fold.</param>
        /// <exception cref="FormatException">An end tag is orphaned or mismatched.</exception>
        public void Apply(XmlScanEvent scanEvent)
        {
            if(scanEvent.Kind == XmlScanEventKind.StartElement)
            {
                ApplyStartElement(scanEvent);
            }
            else if(scanEvent.Kind == XmlScanEventKind.EndElement)
            {
                ApplyEndElement(scanEvent);
            }
            else if(scanEvent.Kind == XmlScanEventKind.Text)
            {
                AppendText(scanEvent.Text);
            }
        }

        /// <summary>Folds a start-element event: resolves the element's namespace, adds it to the tree, and pushes it when it is not self-closing.</summary>
        /// <param name="scanEvent">The start-element event.</param>
        private void ApplyStartElement(XmlScanEvent scanEvent)
        {
            (Dictionary<Utf8String, Utf8String> scope, List<XmlNamespaceBinding>? local) = BindScope(scanEvent.Attributes);
            IReadOnlyList<XmlNamespaceBinding> ordered = local is null ? OrderedScopes.Peek() : MergeOrdered(OrderedScopes.Peek(), local);
            ReadOnlySpan<byte> qualified = scanEvent.Name.Span;

            XmlByteNode node = new()
            {
                LocalName = LocalName(qualified),
                NamespaceIri = ResolveElementNamespace(qualified, scope),
                InScopeNamespaces = ordered,
                ContentStart = scanEvent.IsEmpty ? 0 : scanEvent.Close + 1,
                SpanStart = scanEvent.Start,
                Span = scanEvent.IsEmpty ? Scanner.Span(scanEvent.Start, scanEvent.Close + 1) : default
            };

            foreach(XmlScanAttribute attribute in scanEvent.Attributes)
            {
                if(IsNamespaceDeclaration(attribute))
                {
                    continue;
                }

                ReadOnlySpan<byte> name = attribute.Name.Span;
                node.Attributes.Add(new XmlByteAttribute(LocalName(name), ResolveAttributeNamespace(name, scope), attribute.Value));
            }

            Open.Peek().Children.Add(node);
            if(!scanEvent.IsEmpty)
            {
                Open.Push(node);
                Scopes.Push(scope);
                OrderedScopes.Push(ordered);
            }
        }

        /// <summary>Folds an end-element event: matches the open element, fixes its content end and span, and pops it.</summary>
        /// <param name="scanEvent">The end-element event.</param>
        /// <exception cref="FormatException">The end tag has no matching start tag, or does not match the open element.</exception>
        private void ApplyEndElement(XmlScanEvent scanEvent)
        {
            if(Open.Count <= 1)
            {
                throw new FormatException("XML document has an end tag with no matching start tag.");
            }

            XmlByteNode node = Open.Peek();
            if(!node.LocalName.Span.SequenceEqual(LocalNameSpan(scanEvent.Name.Span)))
            {
                throw new FormatException("XML document has a mismatched end tag.");
            }

            node.ContentEnd = scanEvent.Start;
            node.Span = Scanner.Span(node.SpanStart, scanEvent.Close + 1);
            Open.Pop();
            Scopes.Pop();
            OrderedScopes.Pop();
        }

        /// <summary>Returns the document element once the whole event stream is folded.</summary>
        /// <returns>The document's single root element.</returns>
        /// <exception cref="FormatException">An element is left open, or the document has no single root element.</exception>
        public XmlByteNode Result()
        {
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
        /// <param name="attributes">The element's attributes.</param>
        /// <returns>The element's lookup scope (sharing the parent frame when the element declares no namespaces) and its own declarations in document order, or <see langword="null"/> when it declares none.</returns>
        private (Dictionary<Utf8String, Utf8String> Scope, List<XmlNamespaceBinding>? Local) BindScope(IReadOnlyList<XmlScanAttribute> attributes)
        {
            Dictionary<Utf8String, Utf8String> parent = Scopes.Peek();
            Dictionary<Utf8String, Utf8String>? bound = null;
            List<XmlNamespaceBinding>? local = null;
            foreach(XmlScanAttribute attribute in attributes)
            {
                ReadOnlySpan<byte> name = attribute.Name.Span;
                if(name.SequenceEqual("xmlns"u8))
                {
                    bound ??= new(parent);
                    bound[DefaultPrefix] = attribute.Value;
                    local ??= [];
                    local.Add(new XmlNamespaceBinding(default, attribute.Value));
                }
                else if(name.Length > 6 && name.StartsWith("xmlns:"u8))
                {
                    Utf8String prefix = new(attribute.Name.Memory.Slice(6));
                    bound ??= new(parent);
                    bound[prefix] = attribute.Value;
                    local ??= [];
                    local.Add(new XmlNamespaceBinding(prefix, attribute.Value));
                }
            }

            return (bound ?? parent, local);
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

    /// <summary>Whether an attribute is an <c>xmlns</c> or <c>xmlns:prefix</c> namespace declaration.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns><see langword="true"/> when the attribute declares a namespace.</returns>
    private static bool IsNamespaceDeclaration(XmlScanAttribute attribute)
    {
        ReadOnlySpan<byte> name = attribute.Name.Span;

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
}
