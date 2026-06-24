using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Xml;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>
/// Reads an OWL 2 XML serialization document fed incrementally, chunk by chunk,
/// over UTF-8 bytes, into the same structural model the other OWL front-ends
/// produce.
/// </summary>
/// <remarks>
/// <para>
/// The reader drives the shared, byte-native <see cref="XmlByteScanner"/> in its
/// lenient mode and folds the scanner's flat event stream — start tag, end tag,
/// character data — onto an explicit open-element stack: start tags push, end tags
/// pop, character data accumulates on the open element. The scanner commits one
/// markup unit at a time and a unit whose end the buffer has not yet delivered
/// suspends and re-scans once more bytes arrive, so a chunk boundary never splits a
/// token. When the document is declared final the open <c>Ontology</c> element
/// converts to structural axioms.
/// </para>
/// <para>
/// The reader is value-based: the lenient scanner recovers from a malformed token
/// silently and malformedness in the converted structure is recorded in the
/// document's diagnostics; reading continues where structure permits. Incompleteness
/// is a <see cref="Status"/>, never a diagnostic; truncation is abandoned, never an
/// error, once <see cref="Complete"/> declares the input final.
/// </para>
/// </remarks>
public sealed class OwlXmlSyntaxIncrementalReader
{
    /// <summary>Whether <see cref="Complete"/> has declared the input final.</summary>
    private bool Final { get; set; }

    /// <summary>The number of bytes fed so far; the end-of-document offset, used to fix the span of an element left open at a final input.</summary>
    private int Length { get; set; }

    /// <summary>The completed document, built once by <see cref="Complete"/>.</summary>
    private OwlOntologyDocument? Document { get; set; }

    /// <summary>The shared byte-native scanner the reader folds; lenient so a malformed token is recovered from rather than thrown, with DOCTYPE internal-subset entities parsed.</summary>
    private XmlByteScanner Scanner { get; } = new(XmlScanStrictness.Lenient, parseInternalDtd: true);

    /// <summary>The converter the completed ontology element folds into structural axioms.</summary>
    private OwlXmlSyntaxConverter Converter { get; } = new();

    /// <summary>The tree root standing for the document; its direct children are the top-level elements.</summary>
    private OwlXmlNode Root { get; } = new();

    /// <summary>The open-element stack; <see cref="Root"/> sits at the bottom for the whole parse.</summary>
    private Stack<OwlXmlNode> Open { get; } = new();

    /// <summary>The namespace-binding scope stack, one frame per open element.</summary>
    private Stack<Dictionary<Utf8String, Utf8String>> Scopes { get; } = new();

    /// <summary>Initialises an empty reader; feed source bytes through <see cref="Feed"/>.</summary>
    public OwlXmlSyntaxIncrementalReader()
    {
        Open.Push(Root);
        Scopes.Push([]);
    }

    /// <summary>Gets the diagnostics recorded so far; an unfinished tail is reported through <see cref="Status"/>, never here.</summary>
    public DiagnosticBag Diagnostics => Converter.Diagnostics;

    /// <summary>Gets whether the input fed so far ends at a document boundary or inside an unfinished construct.</summary>
    public IncrementalParseStatus Status =>
        Scanner.Status == IncrementalParseStatus.NeedMore || Open.Count > 1
            ? IncrementalParseStatus.NeedMore
            : IncrementalParseStatus.Complete;

    /// <summary>Appends source bytes and scans as far as the input now permits.</summary>
    /// <param name="chunk">The next run of document bytes, of any length.</param>
    /// <returns>The <see cref="Status"/> after the chunk is consumed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has already been called.</exception>
    public IncrementalParseStatus Feed(ReadOnlySpan<byte> chunk)
    {
        if(Final)
        {
            throw new InvalidOperationException("The reader has been completed; no more input can be fed.");
        }

        Length += chunk.Length;
        Scanner.Feed(chunk);
        Drain();

        return Status;
    }

    /// <summary>Declares the input final and returns the structural document; truncation is abandoned from here on.</summary>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    public OwlOntologyDocument Complete()
    {
        if(Document is OwlOntologyDocument document)
        {
            return document;
        }

        Final = true;
        Scanner.Complete();
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

    /// <summary>Folds every event the scanner has emitted onto the element tree.</summary>
    private void Drain()
    {
        while(Scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            Apply(scanEvent);
        }
    }

    /// <summary>Applies one scan event to the tree; the end-of-document event needs no folding.</summary>
    /// <param name="scanEvent">The event to fold.</param>
    private void Apply(XmlScanEvent scanEvent)
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

    /// <summary>Folds a start-element event: resolves the element's name, builds its node, and pushes it when it is not self-closing.</summary>
    /// <param name="scanEvent">The start-element event.</param>
    private void ApplyStartElement(XmlScanEvent scanEvent)
    {
        List<OwlXmlAttribute> attributes = MapAttributes(scanEvent.Attributes);
        Dictionary<Utf8String, Utf8String> scope = BindScope(attributes);
        (Utf8String namespaceIri, Utf8String localName) = ResolveQName(scanEvent.Name, scope);

        OwlXmlNode node = new()
        {
            Element = OwlXmlNames.Resolve(localName),
            LocalName = localName,
            NamespaceIri = namespaceIri,
            SpanStart = scanEvent.Start,
        };
        node.Attributes.AddRange(attributes);

        Open.Peek().Children.Add(node);

        if(scanEvent.IsEmpty)
        {
            node.Span = Scanner.Span(scanEvent.Start, scanEvent.Close + 1);
        }
        else
        {
            Open.Push(node);
            Scopes.Push(scope);
        }
    }

    /// <summary>Folds an end-element event: closes the open element and fixes its span. The end-tag name is not matched, mirroring the lenient scanner's structural tolerance.</summary>
    /// <param name="scanEvent">The end-element event.</param>
    private void ApplyEndElement(XmlScanEvent scanEvent)
    {
        if(Open.Count > 1)
        {
            OwlXmlNode node = Open.Pop();
            Scopes.Pop();
            node.Span = Scanner.Span(node.SpanStart, scanEvent.Close + 1);
        }
    }

    /// <summary>Maps the scanner's attributes onto the OWL attribute record, keeping every attribute verbatim (including <c>xmlns</c>/<c>xmlns:*</c> and <c>xml:*</c>, which the OWL converter reads by raw qualified name).</summary>
    /// <param name="attributes">The scanner's attributes.</param>
    /// <returns>The OWL attributes, in document order.</returns>
    private static List<OwlXmlAttribute> MapAttributes(IReadOnlyList<XmlScanAttribute> attributes)
    {
        List<OwlXmlAttribute> mapped = [];
        foreach(XmlScanAttribute attribute in attributes)
        {
            mapped.Add(new OwlXmlAttribute(attribute.Name, attribute.Value, attribute.NameStart, attribute.End));
        }

        return mapped;
    }

    /// <summary>Appends character data to the open element, coalescing with any already accumulated; text outside any element is discarded.</summary>
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

    /// <summary>Closes every element still open at the end of a final input, fixing each span to the input end.</summary>
    private void CloseUnterminatedElements()
    {
        while(Open.Count > 1)
        {
            OwlXmlNode node = Open.Pop();
            Scopes.Pop();
            node.Span = Scanner.Span(node.SpanStart, Length);
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
}
