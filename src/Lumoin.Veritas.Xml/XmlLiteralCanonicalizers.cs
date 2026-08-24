using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography.Xml;
using System.Xml;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Xml;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// The well-known <see cref="XmlLiteralCanonicalizer"/> strategies for <c>rdf:parseType="Literal"</c> content: the
/// document-order form the W3C RDF/XML test corpus and mainstream RDF toolkits produce, and strict W3C Canonical
/// XML 1.0.
/// </summary>
/// <remarks>
/// <para>
/// Both hoist the namespaces an XML literal inherits from its ancestors onto the detached fragment's apex elements
/// and expand empty elements. <see cref="DocumentOrder"/> is fully byte-native: it re-scans the verbatim inner bytes
/// through the shared <see cref="XmlByteScanner"/> (no <see cref="System.Xml"/>) and hoists the namespaces in source
/// declaration order. <see cref="Canonical"/> emits strict W3C Canonical XML 1.0 (namespace declarations sorted
/// lexicographically by prefix), backed by <see cref="System.Security.Cryptography.Xml.XmlDsigC14NTransform"/> over the
/// verbatim bytes re-parsed with <see cref="System.Xml.XmlReader"/>. Selecting between them (or supplying a third) is
/// how the reader matches a peer's XML-literal form.
/// </para>
/// <para>
/// Because they use different engines, they treat the degenerate constructs differently: the shared scanner the
/// <see cref="DocumentOrder"/> path drives skips comments and processing instructions (so both are dropped) and decodes
/// references and CDATA in the source, whereas the <see cref="Canonical"/> path retains a processing instruction. An
/// internal-subset general entity referenced inside the literal cannot be resolved against the detached fragment under
/// either path, so it surfaces as a read failure the reader records as a diagnostic and recovers from. None of these
/// constructs appears inside an XML literal in the W3C RDF/XML conformance corpus.
/// </para>
/// </remarks>
public static class XmlLiteralCanonicalizers
{
    /// <summary>
    /// Gets the canonicalizer that hoists in-scope namespaces in <b>document declaration order</b> (the form the W3C
    /// RDF/XML test corpus carries). This is the reader's default.
    /// </summary>
    public static XmlLiteralCanonicalizer DocumentOrder { get; } = SerializeDocumentOrder;

    /// <summary>
    /// Gets the canonicalizer that emits strict W3C Canonical XML 1.0 (namespace declarations sorted lexicographically
    /// by prefix), the form used by XML Digital Signatures — the choice for interop with a signing pipeline.
    /// </summary>
    /// <remarks>Backed by <see cref="XmlDsigC14NTransform"/>, which is unavailable on the <c>browser</c> platform; the cross-platform default is <see cref="DocumentOrder"/>.</remarks>
    [UnsupportedOSPlatform("browser")]
    public static XmlLiteralCanonicalizer Canonical { get; } = SerializeCanonical;

    /// <summary>Serializes the literal content with in-scope namespaces hoisted in document order onto the apex content elements.</summary>
    /// <param name="innerContent">The literal element's verbatim inner UTF-8 bytes.</param>
    /// <param name="inScopeNamespaces">The namespaces in scope at the literal property element, in document declaration order.</param>
    /// <returns>The document-order canonical form.</returns>
    /// <exception cref="FormatException">The inner content is not well-formed, or references an internal-subset general entity that the detached fragment cannot resolve.</exception>
    private static Utf8String SerializeDocumentOrder(ReadOnlyMemory<byte> innerContent, IReadOnlyList<XmlNamespaceBinding> inScopeNamespaces)
    {
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false);
        scanner.Feed(innerContent.Span);
        scanner.Complete();

        ArrayBufferWriter<byte> output = new();
        Stack<Frame> open = new();
        while(scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            if(scanEvent.Kind == XmlScanEventKind.StartElement)
            {
                WriteStartElement(output, scanEvent, open, inScopeNamespaces);
            }
            else if(scanEvent.Kind == XmlScanEventKind.EndElement)
            {
                output.WriteEndTag(scanEvent.Name.Span);
                open.Pop();
            }
            else if(scanEvent.Kind == XmlScanEventKind.Text)
            {
                output.WriteEscaped(scanEvent.Text.Span, attribute: false);
            }
        }

        return Utf8String.WithoutPrecomputedHash(output.WrittenSpan.ToArray());
    }

    /// <summary>Serializes the literal content as strict Canonical XML 1.0 via <see cref="XmlDsigC14NTransform"/>.</summary>
    /// <param name="innerContent">The literal element's verbatim inner UTF-8 bytes.</param>
    /// <param name="inScopeNamespaces">The namespaces in scope at the literal property element, in document declaration order.</param>
    /// <returns>The Canonical XML 1.0 form.</returns>
    /// <exception cref="FormatException">The inner content is not well-formed, or references an internal-subset general entity that the detached fragment cannot resolve.</exception>
    [UnsupportedOSPlatform("browser")]
    private static Utf8String SerializeCanonical(ReadOnlyMemory<byte> innerContent, IReadOnlyList<XmlNamespaceBinding> inScopeNamespaces)
    {
        //Wrap the verbatim inner bytes in a synthetic element that re-declares every in-scope namespace, so the
        //transform sees the full set and hoists it onto the apex elements (sorted per Canonical XML 1.0). The wrapper
        //has no DOCTYPE, so an internal-subset general entity inside the literal cannot resolve and surfaces as a
        //read failure (DtdProcessing.Prohibit keeps it XXE-safe).
        ArrayBufferWriter<byte> wrapper = new();
        wrapper.Write("<rdf-xml-literal"u8);
        HashSet<Utf8String> declared = [];
        foreach(XmlNamespaceBinding binding in inScopeNamespaces)
        {
            if(binding.Prefix.Span.SequenceEqual(XmlCanonicalWriting.XmlPrefix.Span) || !declared.Add(binding.Prefix))
            {
                continue;
            }

            wrapper.WriteDeclaration(binding.Prefix.Span, binding.NamespaceIri.Span);
        }

        wrapper.Write(">"u8);
        wrapper.Write(innerContent.Span);
        wrapper.Write("</rdf-xml-literal>"u8);

        XmlDocument document = new() { PreserveWhitespace = true };
        try
        {
            using MemoryStream stream = new(wrapper.WrittenSpan.ToArray());
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            document.Load(reader);
        }
        catch(XmlException exception)
        {
            throw new FormatException(exception.Message, exception);
        }

        //The C14N node-set is the wrapper's full content subtree (every descendant node with its attributes and
        //namespace nodes), NOT just the direct children: a node-set of only the direct children would canonicalize
        //those elements but drop their descendants and text. Excluding the wrapper element itself makes the apex
        //content elements the top of the set, so the transform hoists the inherited namespaces onto them.
        XmlNodeList content = document.DocumentElement!.SelectNodes("descendant::node() | descendant::*/@* | descendant::*/namespace::*")!;
        XmlDsigC14NTransform transform = new();
        transform.LoadInput(content);
        using Stream output = (Stream)transform.GetOutput(typeof(Stream));
        using StreamReader streamReader = new(output);

        return Utf8Strings.From(streamReader.ReadToEnd());
    }

    /// <summary>Writes an element's start tag (namespace declarations then sorted attributes) and, for a self-closing element, its end tag; otherwise pushes its scope.</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="start">The start-element event (its <see cref="XmlScanEvent.Name"/> is the qualified name as written and its <see cref="XmlScanEvent.Attributes"/> include namespace declarations).</param>
    /// <param name="open">The open-element scope stack.</param>
    /// <param name="inScopeNamespaces">The namespaces in scope at the literal property element, in document order.</param>
    private static void WriteStartElement(ArrayBufferWriter<byte> output, XmlScanEvent start, Stack<Frame> open, IReadOnlyList<XmlNamespaceBinding> inScopeNamespaces)
    {
        ReadOnlySpan<byte> name = start.Name.Span;
        List<XmlNamespaceBinding> local = LocalDeclarations(start.Attributes);
        bool apex = open.Count == 0;

        output.Write("<"u8);
        output.Write(name);

        Dictionary<Utf8String, Utf8String> scope;
        Dictionary<Utf8String, Utf8String> rendered;
        if(apex)
        {
            //An apex content element declares every namespace in scope at it (the literal's inherited scope extended
            //with the element's own declarations), in document order.
            List<XmlNamespaceBinding> ordered = MergeOrdered(inScopeNamespaces, local);
            scope = ToScope(ordered);
            rendered = [];
            foreach(XmlNamespaceBinding binding in ordered)
            {
                EmitDeclaration(output, binding, rendered);
            }
        }
        else
        {
            //A descendant declares only the namespaces it adds that an output ancestor has not already rendered.
            Frame parent = open.Peek();
            scope = new(parent.Scope);
            rendered = new(parent.Rendered);
            foreach(XmlNamespaceBinding binding in local)
            {
                scope[binding.Prefix] = binding.NamespaceIri;
                EmitDeclaration(output, binding, rendered);
            }
        }

        WriteAttributeAxis(output, start.Attributes, scope);
        output.Write(">"u8);

        if(start.IsEmpty)
        {
            output.WriteEndTag(name);
        }
        else
        {
            open.Push(new Frame(scope, rendered));
        }
    }

    /// <summary>Writes the non-namespace attributes of a tag, sorted by namespace IRI then local name (Canonical XML 1.0 attribute order).</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="attributes">The tag's attributes (namespace declarations included; they are filtered out).</param>
    /// <param name="scope">The element's namespace scope, to resolve each attribute's sort key.</param>
    private static void WriteAttributeAxis(ArrayBufferWriter<byte> output, IReadOnlyList<XmlScanAttribute> attributes, Dictionary<Utf8String, Utf8String> scope)
    {
        List<XmlSortedAttribute> sorted = [];
        foreach(XmlScanAttribute attribute in attributes)
        {
            if(XmlCanonicalWriting.IsNamespaceDeclaration(attribute.Name.Span))
            {
                continue;
            }

            sorted.Add(new XmlSortedAttribute(attribute, AttributeNamespace(attribute.Name.Span, scope), XmlCanonicalWriting.LocalNameOf(attribute.Name)));
        }

        output.WriteSortedAttributes(sorted);
    }

    /// <summary>
    /// Declares a namespace binding on the tag being opened, unless it is superfluous: the implicit <c>xml</c> prefix
    /// is never declared; a prefix already declared with the same IRI by an output ancestor is suppressed; and an
    /// empty default declaration (<c>xmlns=""</c>) is rendered only to undeclare an inherited <b>non-empty</b> default,
    /// never otherwise (Canonical XML 1.0 §2.3). A declaration that is written is recorded in <paramref name="rendered"/>.
    /// </summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="binding">The candidate namespace binding.</param>
    /// <param name="rendered">The bindings already declared in the output (prefix to IRI), updated when one is written.</param>
    private static void EmitDeclaration(ArrayBufferWriter<byte> output, XmlNamespaceBinding binding, Dictionary<Utf8String, Utf8String> rendered)
    {
        if(binding.Prefix.Span.SequenceEqual(XmlCanonicalWriting.XmlPrefix.Span))
        {
            return;
        }

        if(rendered.TryGetValue(binding.Prefix, out Utf8String existing) && existing.Span.SequenceEqual(binding.NamespaceIri.Span))
        {
            return;
        }

        if(binding.Prefix.IsEmpty && binding.NamespaceIri.IsEmpty && !(rendered.TryGetValue(default, out Utf8String current) && !current.IsEmpty))
        {
            //An xmlns="" with no inherited non-empty default to cancel carries no information.
            return;
        }

        output.WriteDeclaration(binding.Prefix.Span, binding.NamespaceIri.Span);
        rendered[binding.Prefix] = binding.NamespaceIri;
    }

    /// <summary>The namespace declarations made directly on an element, in document order; the default-namespace prefix is the empty value.</summary>
    /// <param name="attributes">The element's attributes.</param>
    /// <returns>The element's own <c>(prefix, IRI)</c> bindings.</returns>
    private static List<XmlNamespaceBinding> LocalDeclarations(IReadOnlyList<XmlScanAttribute> attributes)
    {
        List<XmlNamespaceBinding> local = [];
        foreach(XmlScanAttribute attribute in attributes)
        {
            if(XmlCanonicalWriting.TryReadDeclaration(attribute, out XmlNamespaceBinding binding))
            {
                local.Add(binding);
            }
        }

        return local;
    }

    /// <summary>Extends a parent element's in-scope bindings with a child's own declarations, in document order: a redeclared prefix keeps its outermost position with the child's IRI; a new prefix appends.</summary>
    /// <param name="parent">The parent's in-scope bindings, in document order.</param>
    /// <param name="local">The element's own declarations, in document order.</param>
    /// <returns>The element's in-scope bindings, in document order.</returns>
    private static List<XmlNamespaceBinding> MergeOrdered(IReadOnlyList<XmlNamespaceBinding> parent, List<XmlNamespaceBinding> local)
    {
        List<XmlNamespaceBinding> merged = [.. parent];
        foreach(XmlNamespaceBinding binding in local)
        {
            int index = -1;
            for(int i = 0; i < merged.Count; i++)
            {
                if(merged[i].Prefix.Span.SequenceEqual(binding.Prefix.Span))
                {
                    index = i;

                    break;
                }
            }

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

    /// <summary>Builds a prefix-to-IRI lookup from a binding list, with the implicit <c>xml</c> prefix bound to the XML namespace.</summary>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The prefix-to-IRI scope.</returns>
    private static Dictionary<Utf8String, Utf8String> ToScope(List<XmlNamespaceBinding> bindings)
    {
        Dictionary<Utf8String, Utf8String> scope = [];
        foreach(XmlNamespaceBinding binding in bindings)
        {
            scope[binding.Prefix] = binding.NamespaceIri;
        }

        scope[XmlCanonicalWriting.XmlPrefix] = XmlCanonicalWriting.XmlNamespaceIri;

        return scope;
    }

    /// <summary>The namespace IRI used as an attribute's sort key: empty for an unprefixed attribute (which is in no namespace), the XML namespace for an <c>xml:</c> attribute, otherwise the prefix's bound IRI.</summary>
    /// <param name="qualified">The attribute's qualified name as written.</param>
    /// <param name="scope">The element's namespace scope.</param>
    /// <returns>The attribute's namespace IRI.</returns>
    private static Utf8String AttributeNamespace(ReadOnlySpan<byte> qualified, Dictionary<Utf8String, Utf8String> scope)
    {
        int colon = qualified.IndexOf((byte)':');
        if(colon < 0)
        {
            return default;
        }

        return scope.GetValueOrDefault(new Utf8String(qualified.Slice(0, colon).ToArray()));
    }

    /// <summary>One open element's namespace state during serialization: the prefix-to-IRI scope inherited by its content, and the bindings already declared in the output by it or an output ancestor.</summary>
    /// <param name="Scope">The prefix-to-IRI bindings in effect for the element and its descendants.</param>
    /// <param name="Rendered">The bindings already declared in the output (prefix to IRI), used to suppress a redundant redeclaration on a descendant.</param>
    private readonly record struct Frame(Dictionary<Utf8String, Utf8String> Scope, Dictionary<Utf8String, Utf8String> Rendered);
}
