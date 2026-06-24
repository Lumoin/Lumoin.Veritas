using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Core.Xml;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// Reads an RDF/XML document (<see href="https://www.w3.org/TR/rdf-syntax-grammar/">RDF 1.1 / 1.2 XML syntax</see>)
/// into <see cref="Quad"/>s. Like the Turtle and N-Quads readers it is value-based — a malformed document is recorded
/// in the supplied <see cref="DiagnosticBag"/> and the read returns whatever it parsed, never throwing for bad input.
/// </summary>
/// <remarks>
/// <para>
/// The document is parsed byte-native by the shared <see cref="XmlByteReader"/> into an <see cref="XmlByteNode"/> tree
/// (no <c>XDocument</c> DOM, no per-term <see cref="string"/> round-trip), which this walks over an <b>explicit
/// stack</b> (the no-recursion discipline holds: node-element subjects are determined top-down from their own
/// attributes, so no post-order returns are needed). Because a byte node has no parent pointer, the scope an ancestor
/// walk would recover — <c>xml:base</c>, <c>xml:lang</c>, <c>rdf:version</c>, and <c>its:dir</c> — is threaded down the
/// work stack instead.
/// </para>
/// <para>
/// An <c>rdf:parseType="Literal"</c> value is the canonical XML of the element's content. The reader hands the
/// element's verbatim inner bytes plus its in-scope namespaces to the <see cref="XmlLiteralCanonicalizer"/>, which
/// re-scans the bytes byte-natively (the default <see cref="XmlLiteralCanonicalizers.DocumentOrder"/> uses no
/// <c>System.Xml</c>; only the strict <see cref="XmlLiteralCanonicalizers.Canonical"/> variant does, for XML Digital
/// Signature interop).
/// </para>
/// <para>
/// Covered grammar: node elements (<c>rdf:Description</c> and typed nodes; <c>rdf:about</c>/<c>rdf:ID</c>/
/// <c>rdf:nodeID</c>/fresh blank node), property elements (<c>rdf:resource</c>, <c>rdf:nodeID</c>, <c>rdf:datatype</c>,
/// <c>xml:lang</c>, <c>rdf:parseType</c> = <c>Resource</c>/<c>Literal</c>/<c>Collection</c>, nested node, text literal,
/// empty-with-attributes), property attributes (shorthand), <c>rdf:li</c> → <c>rdf:_n</c>, <c>rdf:ID</c> reification,
/// and <c>xml:base</c>/<c>xml:lang</c> inheritance.
/// </para>
/// </remarks>
public static class RdfXmlReader
{
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>The diagnostic code for a malformed RDF/XML document.</summary>
    private static Utf8String MalformedCode { get; } = new("RX0001"u8.ToArray());

    /// <summary>The RDF namespace IRI as UTF-8 bytes, for byte-native name comparisons.</summary>
    private static ReadOnlySpan<byte> RdfNs => "http://www.w3.org/1999/02/22-rdf-syntax-ns#"u8;

    /// <summary>The Internationalization Tag Set namespace, whose <c>its:dir</c> attribute carries the RDF 1.2 base direction.</summary>
    private static ReadOnlySpan<byte> ItsNs => "http://www.w3.org/2005/11/its"u8;

    /// <summary>The XML namespace, the home of <c>xml:base</c> and <c>xml:lang</c>.</summary>
    private static ReadOnlySpan<byte> XmlNs => "http://www.w3.org/XML/1998/namespace"u8;

    /// <summary>
    /// Reads the RDF/XML document into quads, recording any malformedness in <paramref name="diagnostics"/>.
    /// </summary>
    /// <param name="source">The document bytes (UTF-8 / per the XML declaration).</param>
    /// <param name="diagnostics">The bag malformedness is recorded in.</param>
    /// <param name="baseIri">The base IRI relative references resolve against, or <see langword="null"/>.</param>
    /// <param name="xmlLiteralCanonicalizer">
    /// The strategy that canonicalizes <c>rdf:parseType="Literal"</c> content into the lexical form of an
    /// <c>rdf:XMLLiteral</c>; defaults to <see cref="XmlLiteralCanonicalizers.DocumentOrder"/> (the RDF-toolkit
    /// interop form). Supply <see cref="XmlLiteralCanonicalizers.Canonical"/> for strict Canonical XML 1.0.
    /// </param>
    /// <returns>The parsed quads (the default graph; RDF/XML has no named graphs).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<Quad> Read(ReadOnlyMemory<byte> source, DiagnosticBag diagnostics, string? baseIri = null, XmlLiteralCanonicalizer? xmlLiteralCanonicalizer = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        XmlLiteralCanonicalizer canonicalizer = xmlLiteralCanonicalizer ?? XmlLiteralCanonicalizers.DocumentOrder;

        List<Quad> quads = [];
        XmlByteNode root;
        try
        {
            //RDF/XML documents in the wild abbreviate namespace IRIs through DOCTYPE internal-subset entities (the
            //W3C OWL corpus and 2000s-era ontologies do), so the internal subset is parsed; external resolution
            //stays off and an expansion budget bounds amplification (both enforced by XmlByteReader, XXE-safe).
            root = XmlByteReader.Read(source.Span, parseInternalDtd: true);
        }
        catch(Exception exception) when(exception is FormatException or InvalidOperationException)
        {
            Report(diagnostics, $"Malformed RDF/XML: {exception.Message}");

            return quads;
        }

        try
        {
            new Walker(quads, diagnostics, canonicalizer, source, literalScanner: null).Walk(root, baseIri);
        }
        catch(Exception exception) when(exception is UriFormatException or FormatException or ArgumentException or InvalidOperationException or XmlException)
        {
            Report(diagnostics, $"Malformed RDF/XML: {exception.Message}");
        }

        return quads;
    }

    /// <summary>
    /// Reads the RDF/XML document into quads by forward-streaming it: each top-level node element (a direct child of
    /// <c>rdf:RDF</c>, or the single document element when the root is a node element) is walked and discarded as it
    /// completes, so the live element tree never exceeds one such subtree. The quads it produces are the same RDF graph
    /// the buffered <see cref="Read"/> produces.
    /// </summary>
    /// <remarks>
    /// The element tree and the scanner's event queue are bounded to one top-level subtree; the scanner's byte buffer
    /// is not compacted, so the document's bytes stay resident (the buffer bound is a separate, deferred concern). An
    /// <c>rdf:parseType="Literal"</c> value is recovered by slicing the scanner's buffer at the literal element's
    /// absolute offsets, which stay valid because the buffer never moves.
    /// </remarks>
    /// <param name="source">The document bytes (UTF-8 / per the XML declaration).</param>
    /// <param name="diagnostics">The bag malformedness is recorded in.</param>
    /// <param name="baseIri">The base IRI relative references resolve against, or <see langword="null"/>.</param>
    /// <param name="xmlLiteralCanonicalizer">The strategy that canonicalizes <c>rdf:parseType="Literal"</c> content; defaults to <see cref="XmlLiteralCanonicalizers.DocumentOrder"/>.</param>
    /// <returns>The parsed quads (the default graph; RDF/XML has no named graphs).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<Quad> ReadStreaming(ReadOnlyMemory<byte> source, DiagnosticBag diagnostics, string? baseIri = null, XmlLiteralCanonicalizer? xmlLiteralCanonicalizer = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        XmlLiteralCanonicalizer canonicalizer = xmlLiteralCanonicalizer ?? XmlLiteralCanonicalizers.DocumentOrder;

        List<Quad> quads = [];

        //The internal subset is parsed (XXE-safe: external resolution stays off, amplification is bounded), matching
        //the buffered Read; the scanner is owned here so the walker can slice an XML literal's bytes from it.
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: true);
        Walker walker = new(quads, diagnostics, canonicalizer, source: default, literalScanner: scanner);
        walker.BeginStreaming(baseIri);
        try
        {
            XmlByteNode root = XmlByteReader.StreamContainer(scanner, source, Walker.IsRdfContainer, walker.OnContainerMatched, walker.OnStreamedSubtree);
            walker.FinishStreaming(root, baseIri);
        }
        catch(Exception exception) when(exception is UriFormatException or FormatException or ArgumentException or InvalidOperationException or XmlException)
        {
            Report(diagnostics, $"Malformed RDF/XML: {exception.Message}");
        }

        return quads;
    }

    /// <summary>Records a malformedness diagnostic with no source location (for a document-level failure, where no element is in hand).</summary>
    /// <param name="diagnostics">The bag.</param>
    /// <param name="message">The message.</param>
    private static void Report(DiagnosticBag diagnostics, string message)
    {
        Report(diagnostics, message, default);
    }

    /// <summary>Records a malformedness diagnostic located at the span of the offending element.</summary>
    /// <param name="diagnostics">The bag.</param>
    /// <param name="message">The message.</param>
    /// <param name="span">The source span of the element the violation is reported against.</param>
    private static void Report(DiagnosticBag diagnostics, string message, SourceSpan span)
    {
        diagnostics.Add(new Diagnostic(MalformedCode, DiagnosticSeverity.Error, span, Utf8String.FromString(message)));
    }

    /// <summary>One unit of pending work: expand a node's child properties, or process one property element. <see cref="Version12"/> and <see cref="Direction"/> carry the inherited RDF 1.2 mode and base direction a parent walk would otherwise recover.</summary>
    private readonly record struct WorkItem(bool ExpandNode, XmlByteNode Element, RdfTerm Subject, NamedNode? Predicate, string? Base, string? Lang, bool Version12, TextDirection? Direction);

    /// <summary>The document-root scope a top-level node element inherits — the base IRI, language, RDF 1.2 mode, and base direction in effect at the document element (or <c>rdf:RDF</c>) — computed once and threaded into each top-level node element by both the buffered and streaming walks.</summary>
    private readonly record struct RootContext(string? Base, string? Lang, bool Version12, TextDirection? Direction);

    /// <summary>The explicit-stack walker that emits quads from the element tree.</summary>
    private sealed class Walker(List<Quad> quads, DiagnosticBag diagnostics, XmlLiteralCanonicalizer xmlLiteralCanonicalizer, ReadOnlyMemory<byte> source, XmlByteScanner? literalScanner)
    {
        private List<Quad> Quads { get; } = quads;

        private DiagnosticBag Diagnostics { get; } = diagnostics;

        /// <summary>The strategy that canonicalizes <c>rdf:parseType="Literal"</c> content into an XML literal's lexical form.</summary>
        private XmlLiteralCanonicalizer Canonicalizer { get; } = xmlLiteralCanonicalizer;

        /// <summary>The document bytes the buffered walk slices verbatim to recover an XML literal's inner markup; unused (default) on the streaming walk, which slices <see cref="LiteralScanner"/> instead.</summary>
        private ReadOnlyMemory<byte> Source { get; } = source;

        /// <summary>The scanner the streaming walk slices an XML literal's verbatim inner bytes from, by absolute offset over its uncompacted buffer; <see langword="null"/> on the buffered walk.</summary>
        private XmlByteScanner? LiteralScanner { get; } = literalScanner;

        /// <summary>The base IRI the streaming walk resolves references against, captured before streaming begins.</summary>
        private string? StreamingBaseIri { get; set; }

        /// <summary>The root scope the streaming walk threads into each streamed top-level node element, captured from the <c>rdf:RDF</c> element.</summary>
        private RootContext StreamingContext { get; set; }

        /// <summary>Whether the streaming walk matched an <c>rdf:RDF</c> container (so its children streamed); when it did not, the single document element is walked at the end instead.</summary>
        private bool MatchedRdf { get; set; }

        /// <summary>The set of rdf:ID-derived IRIs already seen, used to reject a duplicate rdf:ID relative to one base.</summary>
        private HashSet<string> IssuedIds { get; } = new(StringComparer.Ordinal);

        private int blankCounter;

        /// <summary>Walks the document from its root, emitting quads.</summary>
        /// <param name="root">The document root (<c>rdf:RDF</c>, or a single node element).</param>
        /// <param name="baseIri">The document base IRI, or <see langword="null"/>.</param>
        public void Walk(XmlByteNode root, string? baseIri)
        {
            RootContext context = ContextFrom(root, baseIri);
            if(root.Matches("RDF"u8, RdfNs))
            {
                foreach(XmlByteNode child in root.Children)
                {
                    WalkNode(child, context);
                }
            }
            else
            {
                WalkNode(root, context);
            }
        }

        /// <summary>Walks one top-level node element to completion over its own work stack, emitting its quads. Shared by the buffered walk (called per top-level child in document order) and the streaming walk (called per child as it completes), so both produce the identical quad sequence; the cross-subtree state (<see cref="IssuedIds"/>, <c>blankCounter</c>) lives on this instance and is threaded across calls.</summary>
        /// <param name="node">The top-level node element.</param>
        /// <param name="context">The root scope the node inherits.</param>
        private void WalkNode(XmlByteNode node, RootContext context)
        {
            Stack<WorkItem> stack = new();
            PushNode(stack, node, context.Base, context.Lang, context.Version12, context.Direction);
            while(stack.Count > 0)
            {
                WorkItem item = stack.Pop();
                if(item.ExpandNode)
                {
                    ExpandNode(stack, item.Subject, item.Element, item.Base, item.Lang, item.Version12, item.Direction);
                }
                else
                {
                    ProcessProperty(stack, item.Subject, item.Element, item.Predicate!, item.Base, item.Lang, item.Version12, item.Direction);
                }
            }
        }

        /// <summary>Records the base IRI the streaming walk resolves references against.</summary>
        /// <param name="baseIri">The document base IRI, or <see langword="null"/>.</param>
        public void BeginStreaming(string? baseIri)
        {
            StreamingBaseIri = baseIri;
        }

        /// <summary>The streaming container predicate: the document element (depth 0) when it is <c>rdf:RDF</c>, whose direct children are the streamed top-level node elements.</summary>
        /// <param name="node">The candidate container element.</param>
        /// <param name="depth">The element's zero-based depth.</param>
        /// <returns><see langword="true"/> when the element is the <c>rdf:RDF</c> document element.</returns>
        public static bool IsRdfContainer(XmlByteNode node, int depth)
        {
            return depth == 0 && node.Matches("RDF"u8, RdfNs);
        }

        /// <summary>Captures the root scope from the matched <c>rdf:RDF</c> element so each streamed child inherits it.</summary>
        /// <param name="container">The <c>rdf:RDF</c> element.</param>
        public void OnContainerMatched(XmlByteNode container)
        {
            MatchedRdf = true;
            StreamingContext = ContextFrom(container, StreamingBaseIri);
        }

        /// <summary>Walks one streamed top-level node element (a direct child of <c>rdf:RDF</c>).</summary>
        /// <param name="node">The completed top-level node element.</param>
        public void OnStreamedSubtree(XmlByteNode node)
        {
            WalkNode(node, StreamingContext);
        }

        /// <summary>Finishes the streaming walk: a document whose root is a single node element (not <c>rdf:RDF</c>) is walked once here, since its content did not stream as the children of an <c>rdf:RDF</c> container. The single-root and open-balance well-formedness was already enforced by <see cref="XmlByteReader.StreamContainer"/>.</summary>
        /// <param name="root">The document's single root element.</param>
        /// <param name="baseIri">The document base IRI, or <see langword="null"/>.</param>
        public void FinishStreaming(XmlByteNode root, string? baseIri)
        {
            if(!MatchedRdf)
            {
                WalkNode(root, ContextFrom(root, baseIri));
            }
        }

        /// <summary>Builds the root scope inherited by a top-level node element from the document element's own <c>xml:base</c>/<c>xml:lang</c>/<c>rdf:version</c>/<c>its:dir</c>.</summary>
        /// <param name="element">The document element (or <c>rdf:RDF</c>).</param>
        /// <param name="baseIri">The document base IRI, or <see langword="null"/>.</param>
        /// <returns>The root scope.</returns>
        private static RootContext ContextFrom(XmlByteNode element, string? baseIri)
        {
            return new RootContext(AttributeBase(element, baseIri), AttributeLang(element, null), ScopeVersion12(false, element), ScopeDirection(null, element));
        }

        /// <summary>Determines a node element's subject, emits its type and property-attribute statements, and schedules its child properties.</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="element">The node element.</param>
        /// <param name="parentBase">The inherited base IRI.</param>
        /// <param name="parentLang">The inherited language.</param>
        /// <param name="parentVersion12">Whether RDF 1.2 mode is inherited.</param>
        /// <param name="parentDirection">The inherited base direction.</param>
        private void PushNode(Stack<WorkItem> stack, XmlByteNode element, string? parentBase, string? parentLang, bool parentVersion12, TextDirection? parentDirection)
        {
            string? @base = AttributeBase(element, parentBase);
            string? lang = AttributeLang(element, parentLang);
            bool version12 = ScopeVersion12(parentVersion12, element);
            TextDirection? direction = ScopeDirection(parentDirection, element);
            RdfTerm subject = SubjectOf(element, @base);
            EmitNodeHeader(subject, element, @base, lang, version12, direction);
            stack.Push(new WorkItem(ExpandNode: true, element, subject, Predicate: null, @base, lang, version12, direction));
        }

        /// <summary>Schedules each child property element of a node, resolving <c>rdf:li</c> to <c>rdf:_n</c> in document order.</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="subject">The node's subject.</param>
        /// <param name="element">The node element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        /// <param name="version12">Whether RDF 1.2 mode is in scope.</param>
        /// <param name="direction">The base direction in scope.</param>
        private static void ExpandNode(Stack<WorkItem> stack, RdfTerm subject, XmlByteNode element, string? @base, string? lang, bool version12, TextDirection? direction)
        {
            int li = 1;
            foreach(XmlByteNode property in element.Children)
            {
                NamedNode predicate = property.Matches("li"u8, RdfNs)
                    ? new NamedNode(Utf8String.FromString(RdfNamespace + "_" + li++.ToString(CultureInfo.InvariantCulture)))
                    : new NamedNode(ConcatUtf8(property.NamespaceIri.Span, property.LocalName.Span));
                stack.Push(new WorkItem(ExpandNode: false, property, subject, predicate, @base, lang, version12, direction));
            }
        }

        /// <summary>Emits a property element's statement (and any reification / nested-node expansion).</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="subject">The statement subject.</param>
        /// <param name="element">The property element.</param>
        /// <param name="predicate">The statement predicate.</param>
        /// <param name="parentBase">The inherited base IRI.</param>
        /// <param name="parentLang">The inherited language.</param>
        /// <param name="parentVersion12">Whether RDF 1.2 mode is inherited.</param>
        /// <param name="parentDirection">The inherited base direction.</param>
        private void ProcessProperty(Stack<WorkItem> stack, RdfTerm subject, XmlByteNode element, NamedNode predicate, string? parentBase, string? parentLang, bool parentVersion12, TextDirection? parentDirection)
        {
            string? @base = AttributeBase(element, parentBase);
            string? lang = AttributeLang(element, parentLang);
            bool version12 = ScopeVersion12(parentVersion12, element);
            TextDirection? direction = ScopeDirection(parentDirection, element);
            ValidatePropertyElement(element, @base);
            RdfTerm? @object = ResolveObject(stack, element, @base, lang, version12, direction);

            //A null object is an RDF 1.2-only construct (a triple term outside RDF 1.2 mode): it asserts nothing.
            if(@object is null)
            {
                return;
            }

            Quads.Add(new Quad(subject, predicate, @object));
            EmitAnnotation(element, @base, subject, predicate, @object);

            //rdf:ID on a property element reifies the statement it makes.
            if(element.Attribute("ID"u8, RdfNs) is { } reifyId)
            {
                EmitReification(new NamedNode(Resolve(@base, ConcatUtf8("#"u8, reifyId.Span))), subject, predicate, @object);
            }
        }

        /// <summary>Resolves a property element's object: a referenced/blank/typed/plain term, a nested node, or a parse-typed value.</summary>
        /// <param name="stack">The work stack (nested nodes push their own property expansion).</param>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        /// <param name="version12">Whether RDF 1.2 mode is in scope.</param>
        /// <param name="direction">The base direction in scope.</param>
        /// <returns>The object term.</returns>
        private RdfTerm? ResolveObject(Stack<WorkItem> stack, XmlByteNode element, string? @base, string? lang, bool version12, TextDirection? direction)
        {
            if(element.Attribute("parseType"u8, RdfNs) is { } parseType)
            {
                if(parseType.Span.SequenceEqual("Literal"u8))
                {
                    return new Literal(CanonicalizeXmlLiteral(element), new NamedNode(Utf8String.FromString(RdfNamespace + "XMLLiteral")));
                }

                if(parseType.Span.SequenceEqual("Triple"u8))
                {
                    return BuildTripleTerm(stack, element, @base, lang, version12, direction);
                }

                if(parseType.Span.SequenceEqual("Resource"u8))
                {
                    RdfTerm bnode = FreshBlank();
                    stack.Push(new WorkItem(ExpandNode: true, element, bnode, Predicate: null, @base, lang, version12, direction));

                    return bnode;
                }

                if(parseType.Span.SequenceEqual("Collection"u8))
                {
                    return BuildCollection(stack, element, @base, lang, version12, direction);
                }
            }

            if(element.Attribute("resource"u8, RdfNs) is { } resource)
            {
                NamedNode @object = new(Resolve(@base, resource));
                EmitPropertyAttributes(@object, element, @base, lang, version12, direction);

                return @object;
            }

            if(element.Attribute("nodeID"u8, RdfNs) is { } nodeId)
            {
                BlankNode @object = new(ConcatUtf8("b"u8, nodeId.Span));
                EmitPropertyAttributes(@object, element, @base, lang, version12, direction);

                return @object;
            }

            XmlByteNode? child = FirstChildElement(element);
            if(child is not null)
            {
                string? childBase = AttributeBase(child, @base);
                string? childLang = AttributeLang(child, lang);
                bool childVersion12 = ScopeVersion12(version12, child);
                TextDirection? childDirection = ScopeDirection(direction, child);
                RdfTerm childSubject = SubjectOf(child, childBase);
                EmitNodeHeader(childSubject, child, childBase, childLang, childVersion12, childDirection);
                stack.Push(new WorkItem(ExpandNode: true, child, childSubject, Predicate: null, childBase, childLang, childVersion12, childDirection));

                return childSubject;
            }

            //Empty element carrying property attributes (and no rdf:resource/nodeID) denotes a fresh blank node.
            if(HasPropertyAttributes(element))
            {
                RdfTerm bnode = FreshBlank();
                EmitPropertyAttributes(bnode, element, @base, lang, version12, direction);

                return bnode;
            }

            //A leaf: a typed, language-tagged (optionally base-directional), or plain literal from the element's text.
            Utf8String text = element.Text;
            if(element.Attribute("datatype"u8, RdfNs) is { } datatype)
            {
                return new Literal(text, new NamedNode(Resolve(@base, datatype)));
            }

            return TextLiteral(text, version12, direction, lang);
        }

        /// <summary>Builds an RDF collection (<c>parseType="Collection"</c>) from the property element's child node elements, returning the list head (or <c>rdf:nil</c> when empty).</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        /// <param name="version12">Whether RDF 1.2 mode is in scope.</param>
        /// <param name="direction">The base direction in scope.</param>
        /// <returns>The list head term.</returns>
        private RdfTerm BuildCollection(Stack<WorkItem> stack, XmlByteNode element, string? @base, string? lang, bool version12, TextDirection? direction)
        {
            NamedNode first = new(Utf8String.FromString(RdfNamespace + "first"));
            NamedNode rest = new(Utf8String.FromString(RdfNamespace + "rest"));
            NamedNode nil = new(Utf8String.FromString(RdfNamespace + "nil"));

            List<RdfTerm> items = [];
            foreach(XmlByteNode child in element.Children)
            {
                string? childBase = AttributeBase(child, @base);
                string? childLang = AttributeLang(child, lang);
                bool childVersion12 = ScopeVersion12(version12, child);
                TextDirection? childDirection = ScopeDirection(direction, child);
                RdfTerm childSubject = SubjectOf(child, childBase);
                EmitNodeHeader(childSubject, child, childBase, childLang, childVersion12, childDirection);
                stack.Push(new WorkItem(ExpandNode: true, child, childSubject, Predicate: null, childBase, childLang, childVersion12, childDirection));
                items.Add(childSubject);
            }

            if(items.Count == 0)
            {
                return nil;
            }

            RdfTerm head = FreshBlank();
            RdfTerm node = head;
            for(int i = 0; i < items.Count; i++)
            {
                Quads.Add(new Quad(node, first, items[i]));
                RdfTerm next = i == items.Count - 1 ? nil : FreshBlank();
                Quads.Add(new Quad(node, rest, next));
                node = next;
            }

            return head;
        }

        /// <summary>The verbatim inner content bytes of an XML-literal element: sliced from the streaming scanner's buffer by absolute offset when streaming (the buffer never moves, so the offsets stay valid), or from the document <see cref="Source"/> when buffered.</summary>
        /// <param name="element">The literal property element.</param>
        /// <returns>the element's raw inner content bytes (entity references and CDATA undecoded).</returns>
        private ReadOnlyMemory<byte> LiteralBytes(XmlByteNode element)
        {
            int length = element.ContentEnd - element.ContentStart;

            return LiteralScanner is { } scanner
                ? scanner.Window(element.ContentStart, length)
                : Source.Slice(element.ContentStart, length);
        }

        /// <summary>Canonicalizes an <c>rdf:parseType="Literal"</c> element's content into the lexical form of an <c>rdf:XMLLiteral</c>.</summary>
        /// <param name="element">The literal property element.</param>
        /// <returns>The canonical XML form of the element's content.</returns>
        /// <remarks>
        /// The element's verbatim inner bytes and its in-scope namespaces (which the apex content elements must hoist,
        /// since the fragment is detached from its ancestors) are handed to the <see cref="XmlLiteralCanonicalizer"/>,
        /// which re-scans the bytes byte-natively. An internal-subset general entity referenced inside the literal
        /// content cannot be resolved against the detached fragment and fails to re-scan; rather than discard every
        /// quad in the document, that narrow case degrades to a recorded diagnostic and the verbatim content (the
        /// W3C RDF/XML suite does not exercise it).
        /// </remarks>
        private Utf8String CanonicalizeXmlLiteral(XmlByteNode element)
        {
            ReadOnlyMemory<byte> inner = LiteralBytes(element);
            try
            {
                return Canonicalizer(inner, element.InScopeNamespaces);
            }
            catch(Exception exception) when(exception is FormatException or XmlException or InvalidOperationException)
            {
                Report(Diagnostics, $"rdf:parseType=\"Literal\" content could not be canonicalized (a DOCTYPE general-entity reference inside an XML literal is not expanded): {exception.Message}", element.Span);

                return new Utf8String(inner);
            }
        }

        /// <summary>Emits a node element's <c>rdf:type</c> (when it is a typed node, not <c>rdf:Description</c>) and its property-attribute statements.</summary>
        /// <param name="subject">The node subject.</param>
        /// <param name="element">The node element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        /// <param name="version12">Whether RDF 1.2 mode is in scope.</param>
        /// <param name="direction">The base direction in scope.</param>
        private void EmitNodeHeader(RdfTerm subject, XmlByteNode element, string? @base, string? lang, bool version12, TextDirection? direction)
        {
            ValidateNodeElement(element, @base);

            if(!element.Matches("Description"u8, RdfNs) && !element.Matches("RDF"u8, RdfNs))
            {
                Quads.Add(new Quad(subject, new NamedNode(Utf8String.FromString(RdfNamespace + "type")), new NamedNode(ConcatUtf8(element.NamespaceIri.Span, element.LocalName.Span))));
            }

            EmitPropertyAttributes(subject, element, @base, lang, version12, direction);
        }

        /// <summary>Emits the property-attribute shorthand: each non-RDF-structural attribute is a statement (a literal object — language-tagged when an <c>xml:lang</c> is in scope — or a resource for <c>rdf:type</c>).</summary>
        /// <param name="subject">The subject the attributes describe.</param>
        /// <param name="element">The element carrying the attributes.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope, applied to literal-valued property attributes.</param>
        /// <param name="version12">Whether RDF 1.2 mode is in scope.</param>
        /// <param name="direction">The base direction in scope.</param>
        private void EmitPropertyAttributes(RdfTerm subject, XmlByteNode element, string? @base, string? lang, bool version12, TextDirection? direction)
        {
            foreach(XmlByteAttribute attribute in element.Attributes)
            {
                if(IsStructuralAttribute(attribute))
                {
                    continue;
                }

                if(IsRdfAttribute(attribute, "type"u8))
                {
                    Quads.Add(new Quad(subject, new NamedNode(Utf8String.FromString(RdfNamespace + "type")), new NamedNode(Resolve(@base, attribute.Value))));

                    continue;
                }

                NamedNode predicate = new(ConcatUtf8(attribute.NamespaceIri.Span, attribute.LocalName.Span));
                Quads.Add(new Quad(subject, predicate, TextLiteral(attribute.Value, version12, direction, lang)));
            }
        }

        /// <summary>
        /// Emits the RDF 1.2 annotation statement for a property element carrying <c>rdf:annotation</c> (an IRI
        /// reifier) or <c>rdf:annotationNodeID</c> (a blank-node reifier): <c>reifier rdf:reifies &lt;&lt;(s p o)&gt;&gt;</c>,
        /// where <c>(s p o)</c> is the base triple the property element asserts.
        /// </summary>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="subject">The base triple's subject.</param>
        /// <param name="predicate">The base triple's predicate.</param>
        /// <param name="object">The base triple's object.</param>
        private void EmitAnnotation(XmlByteNode element, string? @base, RdfTerm subject, NamedNode predicate, RdfTerm @object)
        {
            RdfTerm? reifier = element switch
            {
                _ when element.Attribute("annotation"u8, RdfNs) is { } annotation => new NamedNode(Resolve(@base, annotation)),
                _ when element.Attribute("annotationNodeID"u8, RdfNs) is { } annotationNodeId => new BlankNode(ConcatUtf8("b"u8, annotationNodeId.Span)),
                _ => null
            };

            if(reifier is null)
            {
                return;
            }

            Quads.Add(new Quad(reifier, new NamedNode(Utf8String.FromString(RdfNamespace + "reifies")), new TripleTerm(subject, predicate, @object)));
        }

        /// <summary>Emits the four reification triples for a statement reified by an <c>rdf:ID</c>.</summary>
        /// <param name="statement">The reifying IRI.</param>
        /// <param name="subject">The reified statement's subject.</param>
        /// <param name="predicate">The reified statement's predicate.</param>
        /// <param name="object">The reified statement's object.</param>
        private void EmitReification(NamedNode statement, RdfTerm subject, NamedNode predicate, RdfTerm @object)
        {
            Quads.Add(new Quad(statement, new NamedNode(Utf8String.FromString(RdfNamespace + "type")), new NamedNode(Utf8String.FromString(RdfNamespace + "Statement"))));
            Quads.Add(new Quad(statement, new NamedNode(Utf8String.FromString(RdfNamespace + "subject")), subject));
            Quads.Add(new Quad(statement, new NamedNode(Utf8String.FromString(RdfNamespace + "predicate")), predicate));
            Quads.Add(new Quad(statement, new NamedNode(Utf8String.FromString(RdfNamespace + "object")), @object));
        }

        /// <summary>Determines a node element's subject from its <c>rdf:about</c> / <c>rdf:ID</c> / <c>rdf:nodeID</c>, or mints a fresh blank node.</summary>
        /// <param name="element">The node element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <returns>The subject term.</returns>
        private RdfTerm SubjectOf(XmlByteNode element, string? @base)
        {
            if(element.Attribute("about"u8, RdfNs) is { } about)
            {
                return new NamedNode(Resolve(@base, about));
            }

            if(element.Attribute("ID"u8, RdfNs) is { } id)
            {
                return new NamedNode(Resolve(@base, ConcatUtf8("#"u8, id.Span)));
            }

            if(element.Attribute("nodeID"u8, RdfNs) is { } nodeId)
            {
                return new BlankNode(ConcatUtf8("b"u8, nodeId.Span));
            }

            return FreshBlank();
        }

        /// <summary>Mints a fresh blank node.</summary>
        /// <returns>The blank node.</returns>
        private BlankNode FreshBlank()
        {
            return new BlankNode(Utf8String.FromString("rx" + blankCounter++.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Builds the triple term denoted by an <c>rdf:parseType="Triple"</c> property element (RDF 1.2): its single
        /// inner node element must describe exactly one triple, whose object may itself be a triple term (nested
        /// <c>parseType="Triple"</c>). Returns <see langword="null"/> outside RDF 1.2 mode (the construct asserts
        /// nothing) or when the inner node does not describe exactly one triple (a recorded error).
        /// </summary>
        /// <param name="stack">The work stack (a leaf nested-node object expands through it).</param>
        /// <param name="tripleProperty">The <c>parseType="Triple"</c> property element.</param>
        /// <param name="propertyBase">The base IRI in scope.</param>
        /// <param name="propertyLang">The language in scope.</param>
        /// <param name="version12">Whether RDF 1.2 mode is in scope at the property element.</param>
        /// <param name="direction">The base direction in scope at the property element.</param>
        /// <returns>The triple term, or <see langword="null"/>.</returns>
        private RdfTerm? BuildTripleTerm(Stack<WorkItem> stack, XmlByteNode tripleProperty, string? propertyBase, string? propertyLang, bool version12, TextDirection? direction)
        {
            //rdf:parseType="Triple" is an RDF 1.2 feature; outside RDF 1.2 mode it denotes nothing.
            if(!version12)
            {
                return null;
            }

            //The object position is the only place a triple term nests, so the nesting is linear: descend the
            //object chain pushing each (subject, predicate), then assemble innermost-first.
            Stack<(RdfTerm Subject, NamedNode Predicate)> pending = new();
            XmlByteNode currentProperty = tripleProperty;
            string? currentBase = propertyBase;
            string? currentLang = propertyLang;
            bool currentVersion12 = version12;
            TextDirection? currentDirection = direction;

            while(true)
            {
                XmlByteNode? inner = FirstChildElement(currentProperty);
                if(inner is null)
                {
                    Report(Diagnostics, "rdf:parseType=\"Triple\" requires a single inner node element describing one triple.", currentProperty.Span);

                    return null;
                }

                string? innerBase = AttributeBase(inner, currentBase);
                string? innerLang = AttributeLang(inner, currentLang);
                bool innerVersion12 = ScopeVersion12(currentVersion12, inner);
                TextDirection? innerDirection = ScopeDirection(currentDirection, inner);
                RdfTerm subject = SubjectOf(inner, innerBase);

                bool typed = !inner.Matches("Description"u8, RdfNs) && !inner.Matches("RDF"u8, RdfNs);
                int attributeProperties = 0;
                XmlByteAttribute singleAttribute = default;
                bool hasSingleAttribute = false;
                foreach(XmlByteAttribute attribute in inner.Attributes)
                {
                    if(IsStructuralAttribute(attribute))
                    {
                        continue;
                    }

                    attributeProperties++;
                    singleAttribute = attribute;
                    hasSingleAttribute = true;
                }

                int childProperties = 0;
                XmlByteNode? childProperty = null;
                foreach(XmlByteNode child in inner.Children)
                {
                    childProperties++;
                    childProperty = child;
                }

                if((typed ? 1 : 0) + attributeProperties + childProperties != 1)
                {
                    Report(Diagnostics, "rdf:parseType=\"Triple\" requires its inner node element to describe exactly one triple.", inner.Span);

                    return null;
                }

                NamedNode rdfType = new(Utf8String.FromString(RdfNamespace + "type"));
                if(typed)
                {
                    return Assemble(pending, subject, rdfType, new NamedNode(ConcatUtf8(inner.NamespaceIri.Span, inner.LocalName.Span)));
                }

                if(hasSingleAttribute)
                {
                    return IsRdfAttribute(singleAttribute, "type"u8)
                        ? Assemble(pending, subject, rdfType, new NamedNode(Resolve(innerBase, singleAttribute.Value)))
                        : Assemble(pending, subject, new NamedNode(ConcatUtf8(singleAttribute.NamespaceIri.Span, singleAttribute.LocalName.Span)), TextLiteral(singleAttribute.Value, innerVersion12, innerDirection, innerLang));
                }

                XmlByteNode property = childProperty!;
                string? childBase = AttributeBase(property, innerBase);
                string? childLang = AttributeLang(property, innerLang);
                bool childVersion12 = ScopeVersion12(innerVersion12, property);
                TextDirection? childDirection = ScopeDirection(innerDirection, property);
                NamedNode predicate = property.Matches("li"u8, RdfNs)
                    ? new NamedNode(Utf8String.FromString(RdfNamespace + "_1"))
                    : new NamedNode(ConcatUtf8(property.NamespaceIri.Span, property.LocalName.Span));

                if(property.Attribute("parseType"u8, RdfNs) is { } innerParseType && innerParseType.Span.SequenceEqual("Triple"u8))
                {
                    //The triple currently at depth pending.Count + 1 cannot nest a deeper one without exceeding
                    //the quoted-triple nesting cap; recording a diagnostic and stopping keeps the pending chain bounded.
                    if(pending.Count + 1 >= QuotedTripleLimits.MaxNestingDepth)
                    {
                        Report(Diagnostics, "An RDF 1.2 quoted triple (rdf:parseType=\"Triple\") is nested deeper than the maximum nesting depth.", property.Span);

                        return null;
                    }

                    pending.Push((subject, predicate));
                    currentProperty = property;
                    currentBase = innerBase;
                    currentLang = innerLang;

                    //version/direction fold in the property element's own rdf:version/its:dir (childVersion12/
                    //childDirection): a deeper leaf's ancestor scope includes this property, which the base/lang
                    //threading (innerBase/innerLang) deliberately does not — matching the original ancestor walk.
                    currentVersion12 = childVersion12;
                    currentDirection = childDirection;

                    continue;
                }

                RdfTerm? leafObject = ResolveObject(stack, property, childBase, childLang, childVersion12, childDirection);
                if(leafObject is null)
                {
                    return null;
                }

                return Assemble(pending, subject, predicate, leafObject);
            }
        }

        /// <summary>Folds a descended chain of (subject, predicate) frames around an innermost triple into the nested triple term.</summary>
        /// <param name="pending">The outer (subject, predicate) frames, innermost last.</param>
        /// <param name="subject">The innermost triple's subject.</param>
        /// <param name="predicate">The innermost triple's predicate.</param>
        /// <param name="object">The innermost triple's object.</param>
        /// <returns>The assembled triple term.</returns>
        private static RdfTerm Assemble(Stack<(RdfTerm Subject, NamedNode Predicate)> pending, RdfTerm subject, NamedNode predicate, RdfTerm @object)
        {
            RdfTerm term = new TripleTerm(subject, predicate, @object);
            while(pending.Count > 0)
            {
                (RdfTerm outerSubject, NamedNode outerPredicate) = pending.Pop();
                term = new TripleTerm(outerSubject, outerPredicate, term);
            }

            return term;
        }

        /// <summary>Validates a node element's name and attributes against the RDF/XML grammar, recording any violation.</summary>
        /// <param name="element">The node element.</param>
        /// <param name="base">The base IRI in scope (for rdf:ID uniqueness).</param>
        private void ValidateNodeElement(XmlByteNode element, string? @base)
        {
            if(IsForbiddenNodeElementName(element))
            {
                Report(Diagnostics, $"'{element.LocalName}' is not a permitted node element name.", element.Span);
            }

            ValidateReservedAttributes(element);

            int identity = element.Attribute("about"u8, RdfNs) is null ? 0 : 1;
            if(element.Attribute("ID"u8, RdfNs) is { } id)
            {
                identity++;
                ValidateId(id, @base, element.Span);
            }

            if(element.Attribute("nodeID"u8, RdfNs) is { } nodeId)
            {
                identity++;
                ValidateNcName(nodeId, "rdf:nodeID", element.Span);
            }

            if(identity > 1)
            {
                Report(Diagnostics, "A node element may carry at most one of rdf:about, rdf:ID, rdf:nodeID.", element.Span);
            }
        }

        /// <summary>Validates a property element's name and attributes against the RDF/XML grammar, recording any violation.</summary>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope (for the rdf:ID reifier's uniqueness).</param>
        private void ValidatePropertyElement(XmlByteNode element, string? @base)
        {
            if(IsForbiddenPropertyElementName(element))
            {
                Report(Diagnostics, $"'{element.LocalName}' is not a permitted property element name.", element.Span);
            }

            ValidateReservedAttributes(element);

            bool hasResource = element.Attribute("resource"u8, RdfNs) is not null;
            if(element.Attribute("nodeID"u8, RdfNs) is { } nodeId)
            {
                ValidateNcName(nodeId, "rdf:nodeID", element.Span);
                if(hasResource)
                {
                    Report(Diagnostics, "A property element may not carry both rdf:nodeID and rdf:resource.", element.Span);
                }
            }

            if(element.Attribute("parseType"u8, RdfNs) is not null && hasResource)
            {
                Report(Diagnostics, "A property element may not carry both rdf:parseType and rdf:resource.", element.Span);
            }

            if(element.Attribute("ID"u8, RdfNs) is { } reifyId)
            {
                ValidateId(reifyId, @base, element.Span);
            }
        }

        /// <summary>Rejects the withdrawn RDF terms (<c>rdf:aboutEach</c>, <c>rdf:aboutEachPrefix</c>, <c>rdf:bagID</c>) and <c>rdf:li</c> when used as attributes.</summary>
        /// <param name="element">The element whose attributes are checked.</param>
        private void ValidateReservedAttributes(XmlByteNode element)
        {
            foreach(XmlByteAttribute attribute in element.Attributes)
            {
                if(IsForbiddenAttributeName(attribute))
                {
                    Report(Diagnostics, $"'{attribute.LocalName}' is not permitted as an attribute.", element.Span);
                }
            }
        }

        /// <summary>Validates an <c>rdf:ID</c> value: it must be an XML NCName and unique relative to its base.</summary>
        /// <param name="value">The rdf:ID value.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="span">The source span of the element carrying the rdf:ID.</param>
        private void ValidateId(Utf8String value, string? @base, SourceSpan span)
        {
            ValidateNcName(value, "rdf:ID", span);

            string resolved = Resolve(@base, ConcatUtf8("#"u8, value.Span)).ToString();
            if(!IssuedIds.Add(resolved))
            {
                Report(Diagnostics, $"rdf:ID '{value}' is defined more than once relative to the same base.", span);
            }
        }

        /// <summary>Records an error when a value is not a valid XML NCName (the production <c>rdf:ID</c> / <c>rdf:nodeID</c> values must match).</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="attribute">The attribute name, for the diagnostic message.</param>
        /// <param name="span">The source span of the element carrying the value.</param>
        private void ValidateNcName(Utf8String value, string attribute, SourceSpan span)
        {
            if(!IsNcName(value.Span))
            {
                Report(Diagnostics, $"The value of {attribute} ('{value}') is not a valid XML NCName.", span);
            }
        }
    }

    /// <summary>Whether RDF 1.2 mode is in scope at an element: inherited, or declared by <c>rdf:version="1.2"</c> on the element.</summary>
    /// <param name="parentVersion12">Whether RDF 1.2 mode is inherited from an ancestor.</param>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when RDF 1.2 features are enabled in the element's scope.</returns>
    private static bool ScopeVersion12(bool parentVersion12, XmlByteNode element)
    {
        return parentVersion12 || (element.Attribute("version"u8, RdfNs) is { } version && version.Span.SequenceEqual("1.2"u8));
    }

    /// <summary>The base direction in scope at an element: its own <c>its:dir</c> (parsed, or none when unrecognised), else the inherited direction.</summary>
    /// <param name="parentDirection">The base direction inherited from an ancestor.</param>
    /// <param name="element">The element.</param>
    /// <returns>The base direction in scope, or <see langword="null"/>.</returns>
    private static TextDirection? ScopeDirection(TextDirection? parentDirection, XmlByteNode element)
    {
        if(element.Attribute("dir"u8, ItsNs) is { } dir)
        {
            return TextDirections.TryParse(dir.Span, out TextDirection direction) ? direction : null;
        }

        return parentDirection;
    }

    /// <summary>Whether an attribute is an RDF/XML structural attribute (not a property attribute), comparing its namespace and local name as bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns><see langword="true"/> for an RDF structural attribute, an <c>xml:</c> attribute, an <c>its:dir</c>/<c>its:version</c>, or an attribute in no namespace.</returns>
    private static bool IsStructuralAttribute(XmlByteAttribute attribute)
    {
        //xml:base / xml:lang configure the element; they are not property statements.
        if(attribute.NamespaceIri.Span.SequenceEqual(XmlNs))
        {
            return true;
        }

        //A property attribute must have a namespace; an attribute in no namespace — including the XML-reserved
        //unprefixed names beginning with "xml", which the spec excludes from RDF processing — is not one.
        if(attribute.NamespaceIri.IsEmpty)
        {
            return true;
        }

        //its:dir / its:version carry the RDF 1.2 base direction and ITS version; they configure the literal.
        if(attribute.NamespaceIri.Span.SequenceEqual(ItsNs) && (attribute.LocalName.Span.SequenceEqual("dir"u8) || attribute.LocalName.Span.SequenceEqual("version"u8)))
        {
            return true;
        }

        //rdf:version declares the RDF 1.2 mode and rdf:annotation / rdf:annotationNodeID name a reifier; all are syntactic.
        return attribute.NamespaceIri.Span.SequenceEqual(RdfNs) && IsRdfSyntacticLocal(attribute.LocalName.Span);
    }

    /// <summary>Whether a local name is one of the RDF syntactic attribute names (not a property statement).</summary>
    /// <param name="local">The local-name bytes.</param>
    /// <returns><see langword="true"/> for an RDF syntactic attribute local name.</returns>
    private static bool IsRdfSyntacticLocal(ReadOnlySpan<byte> local)
    {
        return local.SequenceEqual("about"u8) || local.SequenceEqual("ID"u8) || local.SequenceEqual("nodeID"u8)
            || local.SequenceEqual("resource"u8) || local.SequenceEqual("datatype"u8) || local.SequenceEqual("parseType"u8)
            || local.SequenceEqual("li"u8) || local.SequenceEqual("version"u8) || local.SequenceEqual("annotation"u8)
            || local.SequenceEqual("annotationNodeID"u8);
    }

    /// <summary>Whether an attribute is the RDF term with the given local name.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="local">The RDF local name.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool IsRdfAttribute(XmlByteAttribute attribute, ReadOnlySpan<byte> local)
    {
        return attribute.NamespaceIri.Span.SequenceEqual(RdfNs) && attribute.LocalName.Span.SequenceEqual(local);
    }

    /// <summary>Whether an element name is forbidden as a node element name (the core syntax terms, <c>rdf:li</c>, and the withdrawn old terms).</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when the name may not be a node element.</returns>
    private static bool IsForbiddenNodeElementName(XmlByteNode element)
    {
        if(!element.NamespaceIri.Span.SequenceEqual(RdfNs))
        {
            return false;
        }

        ReadOnlySpan<byte> local = element.LocalName.Span;

        return local.SequenceEqual("RDF"u8) || local.SequenceEqual("ID"u8) || local.SequenceEqual("about"u8)
            || local.SequenceEqual("parseType"u8) || local.SequenceEqual("resource"u8) || local.SequenceEqual("nodeID"u8)
            || local.SequenceEqual("datatype"u8) || local.SequenceEqual("li"u8) || local.SequenceEqual("aboutEach"u8)
            || local.SequenceEqual("aboutEachPrefix"u8) || local.SequenceEqual("bagID"u8);
    }

    /// <summary>Whether an element name is forbidden as a property element name (the core syntax terms, <c>rdf:Description</c>, and the withdrawn old terms; <c>rdf:li</c> is permitted).</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when the name may not be a property element.</returns>
    private static bool IsForbiddenPropertyElementName(XmlByteNode element)
    {
        if(!element.NamespaceIri.Span.SequenceEqual(RdfNs))
        {
            return false;
        }

        ReadOnlySpan<byte> local = element.LocalName.Span;

        return local.SequenceEqual("RDF"u8) || local.SequenceEqual("ID"u8) || local.SequenceEqual("about"u8)
            || local.SequenceEqual("parseType"u8) || local.SequenceEqual("resource"u8) || local.SequenceEqual("nodeID"u8)
            || local.SequenceEqual("datatype"u8) || local.SequenceEqual("Description"u8) || local.SequenceEqual("aboutEach"u8)
            || local.SequenceEqual("aboutEachPrefix"u8) || local.SequenceEqual("bagID"u8);
    }

    /// <summary>Whether an attribute name is forbidden (<c>rdf:li</c> and the withdrawn old terms).</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns><see langword="true"/> when the name may not appear as an attribute.</returns>
    private static bool IsForbiddenAttributeName(XmlByteAttribute attribute)
    {
        if(!attribute.NamespaceIri.Span.SequenceEqual(RdfNs))
        {
            return false;
        }

        ReadOnlySpan<byte> local = attribute.LocalName.Span;

        return local.SequenceEqual("li"u8) || local.SequenceEqual("aboutEach"u8) || local.SequenceEqual("aboutEachPrefix"u8) || local.SequenceEqual("bagID"u8);
    }

    /// <summary>
    /// Builds a literal from an element's text or attribute value: a plain <c>xsd:string</c> when no language is in
    /// scope, an <c>rdf:langString</c> when a language is, or an <c>rdf:dirLangString</c> when a base direction is also
    /// in scope and the document is in RDF 1.2 mode.
    /// </summary>
    /// <param name="text">The lexical value.</param>
    /// <param name="version12">Whether RDF 1.2 mode is in scope (a base direction applies only then).</param>
    /// <param name="direction">The in-scope base direction, or <see langword="null"/>.</param>
    /// <param name="lang">The in-scope language, or <see langword="null"/>.</param>
    /// <returns>The literal term.</returns>
    private static Literal TextLiteral(Utf8String text, bool version12, TextDirection? direction, string? lang)
    {
        if(lang is null)
        {
            return new Literal(text, new NamedNode(Vocabulary.Xsd.String));
        }

        TextDirection? effective = version12 ? direction : null;

        return effective is { } resolved
            ? new Literal(text, new NamedNode(Utf8String.FromString(RdfNamespace + "dirLangString")), Utf8String.FromString(lang), resolved)
            : new Literal(text, new NamedNode(Utf8String.FromString(RdfNamespace + "langString")), Utf8String.FromString(lang));
    }

    /// <summary>Whether a UTF-8 value is a valid XML NCName (the production <c>rdf:ID</c> / <c>rdf:nodeID</c> values must satisfy).</summary>
    /// <param name="value">The candidate value's UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when the value is a non-empty NCName.</returns>
    private static bool IsNcName(ReadOnlySpan<byte> value)
    {
        if(value.IsEmpty)
        {
            return false;
        }

        if(Rune.DecodeFromUtf8(value, out Rune first, out int consumed) != OperationStatus.Done || !IsNcNameStart(first))
        {
            return false;
        }

        int i = consumed;
        while(i < value.Length)
        {
            if(Rune.DecodeFromUtf8(value.Slice(i), out Rune rune, out int width) != OperationStatus.Done || !IsNcNameChar(rune))
            {
                return false;
            }

            i += width;
        }

        return true;
    }

    /// <summary>Whether a codepoint may start an NCName (the XML 1.0 <c>NameStartChar</c> production minus the colon).</summary>
    /// <param name="rune">The codepoint.</param>
    /// <returns><see langword="true"/> when it is an NCName start character.</returns>
    private static bool IsNcNameStart(Rune rune)
    {
        return rune.Value switch
        {
            >= 'A' and <= 'Z' => true,
            '_' => true,
            >= 'a' and <= 'z' => true,
            >= 0xC0 and <= 0xD6 => true,
            >= 0xD8 and <= 0xF6 => true,
            >= 0xF8 and <= 0x2FF => true,
            >= 0x370 and <= 0x37D => true,
            >= 0x37F and <= 0x1FFF => true,
            >= 0x200C and <= 0x200D => true,
            >= 0x2070 and <= 0x218F => true,
            >= 0x2C00 and <= 0x2FEF => true,
            >= 0x3001 and <= 0xD7FF => true,
            >= 0xF900 and <= 0xFDCF => true,
            >= 0xFDF0 and <= 0xFFFD => true,
            >= 0x10000 and <= 0xEFFFF => true,
            _ => false
        };
    }

    /// <summary>Whether a codepoint may continue an NCName (the XML 1.0 <c>NameChar</c> production minus the colon).</summary>
    /// <param name="rune">The codepoint.</param>
    /// <returns><see langword="true"/> when it is an NCName character.</returns>
    private static bool IsNcNameChar(Rune rune)
    {
        return rune.Value switch
        {
            '-' => true,
            '.' => true,
            >= '0' and <= '9' => true,
            0xB7 => true,
            >= 0x0300 and <= 0x036F => true,
            >= 0x203F and <= 0x2040 => true,
            _ => IsNcNameStart(rune)
        };
    }

    /// <summary>The first child element of an element, or <see langword="null"/>.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The first child element, or <see langword="null"/>.</returns>
    private static XmlByteNode? FirstChildElement(XmlByteNode element)
    {
        return element.Children.Count > 0 ? element.Children[0] : null;
    }

    /// <summary>Whether an element carries any property-attribute (a non-structural attribute).</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when a property attribute is present.</returns>
    private static bool HasPropertyAttributes(XmlByteNode element)
    {
        foreach(XmlByteAttribute attribute in element.Attributes)
        {
            if(!IsStructuralAttribute(attribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The base IRI for an element: its <c>xml:base</c> if present, else the inherited base.</summary>
    /// <param name="element">The element.</param>
    /// <param name="parentBase">The inherited base.</param>
    /// <returns>The base IRI in scope, or <see langword="null"/>.</returns>
    private static string? AttributeBase(XmlByteNode element, string? parentBase)
    {
        return element.Attribute("base"u8, XmlNs) is { } @base ? @base.ToString() : parentBase;
    }

    /// <summary>The language for an element: its <c>xml:lang</c> if present (empty clears it), else the inherited language.</summary>
    /// <param name="element">The element.</param>
    /// <param name="parentLang">The inherited language.</param>
    /// <returns>The language in scope, or <see langword="null"/>.</returns>
    private static string? AttributeLang(XmlByteNode element, string? parentLang)
    {
        if(element.Attribute("lang"u8, XmlNs) is not { } lang)
        {
            return parentLang;
        }

        return lang.IsEmpty ? null : lang.ToString();
    }

    /// <summary>Resolves a (possibly relative) IRI reference against the base IRI in scope.</summary>
    /// <param name="base">The base IRI, or <see langword="null"/>.</param>
    /// <param name="reference">The IRI reference.</param>
    /// <returns>The resolved IRI.</returns>
    private static Utf8String Resolve(string? @base, Utf8String reference)
    {
        return @base is null ? reference : Utf8String.FromString(IriResolver.ResolveIri(@base, reference.ToString()));
    }

    /// <summary>Concatenates two byte spans into one owned <see cref="Utf8String"/> (a namespace IRI with a local name, or a label prefix with a value).</summary>
    /// <param name="left">The leading bytes.</param>
    /// <param name="right">The trailing bytes.</param>
    /// <returns>The concatenation.</returns>
    private static Utf8String ConcatUtf8(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        byte[] joined = new byte[left.Length + right.Length];
        left.CopyTo(joined);
        right.CopyTo(joined.AsSpan(left.Length));

        return new Utf8String(joined);
    }
}
