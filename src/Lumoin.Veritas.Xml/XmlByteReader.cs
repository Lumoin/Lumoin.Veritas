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

    /// <summary>Feeds the whole document to the scanner in bounded chunks, draining between feeds, and folds the event stream into the element tree.</summary>
    /// <param name="scanner">The scanner to drive over the whole buffer.</param>
    /// <param name="utf8">The document bytes.</param>
    /// <returns>The document's single root element.</returns>
    /// <exception cref="FormatException">The document is not well-formed or has no single root element.</exception>
    /// <remarks>
    /// The whole input length is known up front, so the scanner's buffer is reserved once
    /// (no doubling ladder) and the event queue stays bounded at one chunk's events
    /// instead of holding the whole document's. The folded tree is identical to a
    /// whole-buffer feed: an in-element unit never emits at a chunk boundary (the scanner
    /// suspends it), and the only splittable unit — a top-level whitespace run — is
    /// discarded by the fold either way.
    /// </remarks>
    private static XmlByteNode Fold(XmlByteScanner scanner, ReadOnlySpan<byte> utf8)
    {
        scanner.Reserve(utf8.Length);
        Folder folder = new(scanner);
        int offset = 0;
        while(offset < utf8.Length)
        {
            int length = Math.Min(StreamingChunkSize, utf8.Length - offset);
            scanner.Feed(utf8.Slice(offset, length));
            folder.DrainInto();
            offset += length;
        }

        scanner.Complete();
        folder.DrainInto();

        return folder.Result();
    }

    /// <summary>The chunk size, in bytes, the streaming fold feeds the scanner so its event queue and the live tree stay bounded while the whole document is consumed.</summary>
    private const int StreamingChunkSize = 8192;

    /// <summary>
    /// Forward-streams a UTF-8 XML document: it folds the scanner's event stream with a bounded active path, handing
    /// each completed direct child subtree of the matched container element to <paramref name="onSubtree"/> and then
    /// discarding it, so the live <see cref="XmlByteNode"/> tree never exceeds one such subtree. The document is fed to
    /// the scanner in bounded chunks so its event queue stays bounded too, and a streaming scanner's consumed byte
    /// prefix is reclaimed (<see cref="XmlByteScanner.Compact"/>) between top-level subtrees — once no element is open
    /// below the container, every completed subtree has been handed over and its values copied, so the committed bytes
    /// (and any walked <c>rdf:parseType="Literal"</c> window) are no longer needed. Peak memory is then bounded by the
    /// largest single top-level subtree plus a chunk, not the document.
    /// </summary>
    /// <param name="scanner">The scanner to drive; the caller owns it so it can also slice literal windows from it.</param>
    /// <param name="utf8">The document bytes.</param>
    /// <param name="isContainer">The predicate (resolved element, zero-based depth) that marks the first container element whose direct children stream.</param>
    /// <param name="onContainerMatched">A callback invoked once with the container element when matched (before its children arrive) — e.g. to capture inherited context from its attributes — or <see langword="null"/>.</param>
    /// <param name="onSubtree">The callback invoked with each completed direct child subtree of the container, in document order.</param>
    /// <returns>The document's single root element (with the container's streamed children already detached).</returns>
    /// <exception cref="FormatException">The document is not well-formed — an unclosed element, or no single root element. The streaming fold enforces the same structural well-formedness the buffered <see cref="Read(System.ReadOnlySpan{byte})"/> does (the container's detached children leave the document root as the synthetic root's sole child, so the single-root and open-balance checks still apply).</exception>
    internal static XmlByteNode StreamContainer(XmlByteScanner scanner, ReadOnlyMemory<byte> utf8, Func<XmlByteNode, int, bool> isContainer, Action<XmlByteNode>? onContainerMatched, Action<XmlByteNode> onSubtree)
    {
        Folder folder = new(scanner, isContainer, onContainerMatched, onSubtree);
        int offset = 0;
        while(offset < utf8.Length)
        {
            int length = Math.Min(StreamingChunkSize, utf8.Length - offset);
            scanner.Feed(utf8.Span.Slice(offset, length));
            folder.DrainInto();
            offset += length;

            if(folder.IsBetweenSubtrees)
            {
                //No element is open below the container, so every completed subtree has been walked and detached and
                //its values copied; the scanned-and-committed bytes may be reclaimed.
                scanner.Compact();
            }
        }

        scanner.Complete();
        folder.DrainInto();

        return folder.Result();
    }

    /// <summary>The stateful fold of the scanner's event stream into an element tree: the open-element and namespace-scope stacks.</summary>
    /// <remarks>
    /// In its default (whole-buffer) shape the fold accumulates every element under <see cref="Root"/> and
    /// <see cref="Result"/> returns the single document element. In its streaming shape — a container predicate plus a
    /// per-subtree callback — each completed direct child of the matched container element is handed to the callback and
    /// then detached from the container, so the live tree never exceeds one such subtree (the memory bound the
    /// forward-streaming consumers rely on; the scanner's byte buffer itself is a separate concern and is not bounded
    /// here).
    /// </remarks>
    private sealed class Folder
    {
        /// <summary>The scanner the fold resolves byte offsets through into source spans.</summary>
        private XmlByteScanner Scanner { get; }

        /// <summary>The per-parse local-name intern table: one owned copy per DISTINCT element/attribute local name, span-probed per occurrence (names repeat massively in element markup).</summary>
        private Dictionary<Utf8String, Utf8String> NameTable { get; } = new(Utf8SpanComparer.Instance);

        /// <summary>The synthetic document root; its direct children are the document's top-level elements.</summary>
        private XmlByteNode Root { get; } = new();

        /// <summary>The open-element stack; <see cref="Root"/> sits at the bottom for the whole fold.</summary>
        private Stack<XmlByteNode> Open { get; } = new();

        /// <summary>The namespace-binding scope stack, one frame per open element.</summary>
        private Stack<Dictionary<Utf8String, Utf8String>> Scopes { get; } = new();

        /// <summary>The immutable namespace-declaration scope chain, one frame per open element, parallel to <see cref="Scopes"/>; a frame is shared with its parent when the element declares no namespaces. The flattened in-scope list is materialized from it lazily, only when an XML literal needs it.</summary>
        private Stack<XmlNamespaceScope?> ScopeChain { get; } = new();

        /// <summary>The predicate that, for the first matching element (by resolved node and zero-based depth), marks the element whose direct children stream, or <see langword="null"/> in the whole-buffer shape.</summary>
        private Func<XmlByteNode, int, bool>? ContainerPredicate { get; }

        /// <summary>The callback invoked once with the streaming container element when it is matched (before its children arrive), or <see langword="null"/>.</summary>
        private Action<XmlByteNode>? OnContainerMatched { get; }

        /// <summary>The callback invoked with each completed direct child subtree of the streaming container, or <see langword="null"/> in the whole-buffer shape.</summary>
        private Action<XmlByteNode>? OnSubtree { get; }

        /// <summary>The matched streaming container element whose children stream, once seen; <see langword="null"/> until then.</summary>
        private XmlByteNode? Container { get; set; }

        /// <summary>The open-element stack depth (<see cref="Open"/> count) with the matched container open but no child below it; the streaming driver compacts when the depth returns to this, i.e. between top-level subtrees. Zero until the container is matched and pushed.</summary>
        private int ContainerOpenCount { get; set; }

        /// <summary>Whether the container is matched and no element is open below it — every completed subtree has been handed over, so the scanner's committed bytes may be reclaimed.</summary>
        public bool IsBetweenSubtrees => Container is not null && Open.Count == ContainerOpenCount;

        /// <summary>Seeds the fold with the synthetic root and its empty namespace scope (the whole-buffer shape).</summary>
        /// <param name="scanner">The scanner whose offsets the fold resolves to spans.</param>
        public Folder(XmlByteScanner scanner)
            : this(scanner, null, null, null)
        {
        }

        /// <summary>Seeds the fold, optionally in its streaming shape.</summary>
        /// <param name="scanner">The scanner whose offsets the fold resolves to spans.</param>
        /// <param name="containerPredicate">The predicate marking the streaming container element, or <see langword="null"/> for the whole-buffer shape.</param>
        /// <param name="onContainerMatched">The callback invoked when the container is matched (before its children arrive), or <see langword="null"/>.</param>
        /// <param name="onSubtree">The callback invoked with each completed direct child subtree of the container, or <see langword="null"/> for the whole-buffer shape.</param>
        public Folder(XmlByteScanner scanner, Func<XmlByteNode, int, bool>? containerPredicate, Action<XmlByteNode>? onContainerMatched, Action<XmlByteNode>? onSubtree)
        {
            Scanner = scanner;
            ContainerPredicate = containerPredicate;
            OnContainerMatched = onContainerMatched;
            OnSubtree = onSubtree;
            Open.Push(Root);
            Scopes.Push(new Dictionary<Utf8String, Utf8String>(Utf8SpanComparer.Instance));
            ScopeChain.Push(null);
        }

        /// <summary>Folds every event the scanner has emitted and not yet been drained into the tree.</summary>
        public void DrainInto()
        {
            while(Scanner.TryDequeue(out XmlScanEvent scanEvent))
            {
                Apply(scanEvent);
            }
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
            int depth = Open.Count - 1;
            (Dictionary<Utf8String, Utf8String> scope, List<XmlNamespaceBinding>? local) = BindScope(scanEvent.Attributes);
            XmlNamespaceScope? namespaceScope = local is null ? ScopeChain.Peek() : new XmlNamespaceScope { Parent = ScopeChain.Peek(), Local = local };
            ReadOnlySpan<byte> qualified = scanEvent.Name.Span;

            XmlByteNode node = new()
            {
                LocalName = InternedLocalName(qualified),
                NamespaceIri = ResolveElementNamespace(qualified, scope),
                Scope = namespaceScope,
                ContentStart = scanEvent.IsEmpty ? 0 : scanEvent.Close + 1,
                SpanStart = scanEvent.Start,
                Span = scanEvent.IsEmpty ? Scanner.Span(scanEvent.Start, scanEvent.Close + 1) : default
            };

            List<XmlScanAttribute> scanAttributes = scanEvent.Attributes;
            int attributeCount = 0;
            for(int i = 0; i < scanAttributes.Count; i++)
            {
                if(!IsNamespaceDeclaration(scanAttributes[i]))
                {
                    attributeCount++;
                }
            }

            if(attributeCount > 0)
            {
                List<XmlByteAttribute> nodeAttributes = node.MaterializeAttributes(attributeCount);
                for(int i = 0; i < scanAttributes.Count; i++)
                {
                    XmlScanAttribute attribute = scanAttributes[i];
                    if(IsNamespaceDeclaration(attribute))
                    {
                        continue;
                    }

                    ReadOnlySpan<byte> name = attribute.Name.Span;
                    nodeAttributes.Add(new XmlByteAttribute(InternedLocalName(name), ResolveAttributeNamespace(name, scope), attribute.Value));
                }
            }

            XmlByteNode parent = Open.Peek();
            parent.MaterializeChildren().Add(node);

            if(Container is null && ContainerPredicate is not null && ContainerPredicate(node, depth))
            {
                Container = node;
                OnContainerMatched?.Invoke(node);
            }

            if(!scanEvent.IsEmpty)
            {
                Open.Push(node);
                Scopes.Push(scope);
                ScopeChain.Push(namespaceScope);
                if(ReferenceEquals(node, Container))
                {
                    //The container is now open with no child below it; the driver compacts whenever the depth returns here.
                    ContainerOpenCount = Open.Count;
                }
            }
            else if(OnSubtree is not null && ReferenceEquals(parent, Container))
            {
                //A self-closing child of the streaming container completes at its start tag (it has no end tag), so
                //it is handed over and detached here rather than in ApplyEndElement.
                YieldSubtree(parent, node);
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
            ScopeChain.Pop();

            if(OnSubtree is not null && ReferenceEquals(Open.Peek(), Container))
            {
                YieldSubtree(Container, node);
            }
        }

        /// <summary>Hands a completed direct child subtree of the streaming container to the callback, then detaches it from the container so the live tree never holds more than the current subtree.</summary>
        /// <param name="container">The streaming container.</param>
        /// <param name="subtree">The completed child subtree, the most recently added child of the container.</param>
        private void YieldSubtree(XmlByteNode container, XmlByteNode subtree)
        {
            OnSubtree!(subtree);
            container.Children.RemoveAt(container.Children.Count - 1);
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
            if(ReferenceEquals(node, Container))
            {
                //Streaming: character data directly under the long-lived container (inter-child whitespace and
                //comments) is discarded rather than accumulated, so the container node stays bounded. The walking
                //consumers never read the container element's own text.
                return;
            }

            node.Text = node.Text.IsEmpty ? content : Concat(node.Text, content);
        }

        /// <summary>Builds the namespace scope of an element: the parent scope extended with the element's own <c>xmlns</c> declarations, plus the element's own declarations in document order (for the in-scope binding list).</summary>
        /// <param name="attributes">The element's attributes.</param>
        /// <returns>The element's lookup scope (sharing the parent frame when the element declares no namespaces) and its own declarations in document order, or <see langword="null"/> when it declares none.</returns>
        private (Dictionary<Utf8String, Utf8String> Scope, List<XmlNamespaceBinding>? Local) BindScope(List<XmlScanAttribute> attributes)
        {
            Dictionary<Utf8String, Utf8String> parent = Scopes.Peek();
            Dictionary<Utf8String, Utf8String>? bound = null;
            List<XmlNamespaceBinding>? local = null;
            for(int i = 0; i < attributes.Count; i++)
            {
                XmlScanAttribute attribute = attributes[i];
                ReadOnlySpan<byte> name = attribute.Name.Span;
                if(name.SequenceEqual("xmlns"u8))
                {
                    bound ??= new Dictionary<Utf8String, Utf8String>(parent, Utf8SpanComparer.Instance);
                    bound[DefaultPrefix] = attribute.Value;
                    local ??= [];
                    local.Add(new XmlNamespaceBinding(default, attribute.Value));
                }
                else if(name.Length > 6 && name.StartsWith("xmlns:"u8))
                {
                    Utf8String prefix = new(attribute.Name.Memory.Slice(6));
                    bound ??= new Dictionary<Utf8String, Utf8String>(parent, Utf8SpanComparer.Instance);
                    bound[prefix] = attribute.Value;
                    local ??= [];
                    local.Add(new XmlNamespaceBinding(prefix, attribute.Value));
                }
            }

            return (bound ?? parent, local);
        }

        /// <summary>The local-name part of a qualified name, interned per parse: a span probe per occurrence, one owned copy per distinct name.</summary>
        /// <param name="qualified">The qualified name bytes.</param>
        /// <returns>The interned local name.</returns>
        private Utf8String InternedLocalName(ReadOnlySpan<byte> qualified)
        {
            ReadOnlySpan<byte> local = LocalNameSpan(qualified);
            Dictionary<Utf8String, Utf8String>.AlternateLookup<ReadOnlySpan<byte>> lookup = NameTable.GetAlternateLookup<ReadOnlySpan<byte>>();
            if(lookup.TryGetValue(local, out Utf8String interned))
            {
                return interned;
            }

            Utf8String owned = new(local.ToArray());
            NameTable[owned] = owned;

            return owned;
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

    /// <summary>Resolves a prefix to its namespace IRI; the implicit <c>xml</c> prefix is always the XML namespace. The scope probe runs by span, so no key materializes per lookup.</summary>
    /// <param name="prefix">The prefix bytes.</param>
    /// <param name="scope">The namespace scope in effect.</param>
    /// <returns>The namespace IRI, or an empty value when the prefix is unbound.</returns>
    private static Utf8String ResolvePrefix(ReadOnlySpan<byte> prefix, Dictionary<Utf8String, Utf8String> scope)
    {
        if(prefix.SequenceEqual("xml"u8))
        {
            return XmlNamespaceName;
        }

        Dictionary<Utf8String, Utf8String>.AlternateLookup<ReadOnlySpan<byte>> lookup = scope.GetAlternateLookup<ReadOnlySpan<byte>>();

        return lookup.TryGetValue(prefix, out Utf8String bound) ? bound : default;
    }

    /// <summary>The implicit <c>xml</c> prefix's namespace as one shared owned value.</summary>
    private static Utf8String XmlNamespaceName { get; } = new(XmlNamespace.ToArray());

    /// <summary>Whether an attribute is an <c>xmlns</c> or <c>xmlns:prefix</c> namespace declaration.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns><see langword="true"/> when the attribute declares a namespace.</returns>
    private static bool IsNamespaceDeclaration(XmlScanAttribute attribute)
    {
        ReadOnlySpan<byte> name = attribute.Name.Span;

        return name.SequenceEqual("xmlns"u8) || name.StartsWith("xmlns:"u8);
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
