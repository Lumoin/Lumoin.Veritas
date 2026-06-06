using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// The well-known <see cref="XmlLiteralCanonicalizer"/> strategies for <c>rdf:parseType="Literal"</c> content: the
/// document-order form the W3C RDF/XML test corpus and mainstream RDF toolkits produce, and strict W3C Canonical
/// XML 1.0.
/// </summary>
/// <remarks>
/// Both hoist the in-scope namespaces an XML literal inherits from its ancestors onto the detached fragment and
/// expand empty elements; they differ only in the order of the hoisted namespace declarations —
/// <see cref="DocumentOrder"/> keeps source declaration order, <see cref="Canonical"/> sorts lexicographically by
/// prefix per Canonical XML 1.0. Selecting between them (or supplying a third) is how the reader matches a peer's
/// XML-literal form.
/// </remarks>
public static class XmlLiteralCanonicalizers
{
    private const string XmlPrefix = "xml";

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

    /// <summary>Serializes the literal content with in-scope namespaces hoisted in document order onto the apex elements.</summary>
    /// <param name="literalProperty">The <c>rdf:parseType="Literal"</c> property element.</param>
    /// <returns>The document-order canonical form.</returns>
    private static string SerializeDocumentOrder(XElement literalProperty)
    {
        StringBuilder builder = new();

        //The top-level content nodes are the apex: their parent (the property element) is not in the output, so an
        //apex element must render every namespace in scope at it. Walk the subtree over an explicit stack; a Close
        //frame carries the deferred end tag so no call-stack recursion is needed.
        Stack<Frame> work = new();
        PushChildrenReversed(work, literalProperty, EmptyRendered, apex: true);

        while(work.Count > 0)
        {
            Frame frame = work.Pop();
            if(frame.CloseName is { } closeName)
            {
                builder.Append("</").Append(closeName).Append('>');

                continue;
            }

            switch(frame.Node)
            {
                case XElement element:
                {
                    string qualifiedName = WriteElementOpen(builder, element, frame.Rendered, frame.Apex, out Dictionary<string, string> childRendered);
                    work.Push(new Frame(Node: null, CloseName: qualifiedName, Rendered: frame.Rendered, Apex: false));
                    PushChildrenReversed(work, element, childRendered, apex: false);

                    break;
                }

                case XText text:
                {
                    AppendEscaped(builder, text.Value, attribute: false);

                    break;
                }

                case XProcessingInstruction instruction:
                {
                    builder.Append("<?").Append(instruction.Target);
                    if(instruction.Data.Length > 0)
                    {
                        builder.Append(' ').Append(instruction.Data);
                    }

                    builder.Append("?>");

                    break;
                }

                default:
                {
                    //Comments are dropped (Canonical XML without comments).
                    break;
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Serializes the literal content as strict Canonical XML 1.0 via <see cref="XmlDsigC14NTransform"/>.</summary>
    /// <param name="literalProperty">The <c>rdf:parseType="Literal"</c> property element.</param>
    /// <returns>The Canonical XML 1.0 form.</returns>
    [UnsupportedOSPlatform("browser")]
    private static string SerializeCanonical(XElement literalProperty)
    {
        //A reader over the element alone drops the inherited-but-unused namespaces (the very ones the literal must
        //hoist), so materialize them onto a detached copy first; the transform then sees the full in-scope set and
        //hoists it onto the apex elements (sorted per Canonical XML 1.0).
        XElement contextual = new(literalProperty);
        foreach((string prefix, string iri) in InScopeDocumentOrder(literalProperty))
        {
            XName declaration = prefix.Length == 0 ? "xmlns" : XNamespace.Xmlns + prefix;
            if(contextual.Attribute(declaration) is null)
            {
                contextual.SetAttributeValue(declaration, iri);
            }
        }

        XmlDocument document = new() { PreserveWhitespace = true };
        using(XmlReader reader = contextual.CreateReader())
        {
            document.Load(reader);
        }

        XmlDsigC14NTransform transform = new();
        transform.LoadInput(document.DocumentElement!.ChildNodes);
        using Stream output = (Stream)transform.GetOutput(typeof(Stream));
        using StreamReader streamReader = new(output);

        return streamReader.ReadToEnd();
    }

    /// <summary>Writes an element's start tag (namespace declarations then sorted attributes) and returns its qualified name for the matching end tag.</summary>
    /// <param name="builder">The output buffer.</param>
    /// <param name="element">The element.</param>
    /// <param name="rendered">The namespaces already declared by output ancestors (prefix to IRI).</param>
    /// <param name="apex">Whether the element is a fragment apex (it then declares all in-scope namespaces).</param>
    /// <param name="childRendered">Receives the namespaces in scope for the element's children.</param>
    /// <returns>The element's qualified name.</returns>
    private static string WriteElementOpen(StringBuilder builder, XElement element, Dictionary<string, string> rendered, bool apex, out Dictionary<string, string> childRendered)
    {
        string qualifiedName = QualifiedName(element.Name, element);
        builder.Append('<').Append(qualifiedName);

        childRendered = new Dictionary<string, string>(rendered, System.StringComparer.Ordinal);

        //An apex element declares every namespace in scope (in document order); a descendant declares only the
        //namespaces it adds that an output ancestor has not already rendered.
        IEnumerable<(string Prefix, string Iri)> candidates = apex ? InScopeDocumentOrder(element) : LocalNamespaces(element);
        foreach((string prefix, string iri) in candidates)
        {
            if(prefix == XmlPrefix || (childRendered.TryGetValue(prefix, out string? existing) && existing == iri))
            {
                continue;
            }

            childRendered[prefix] = iri;
            builder.Append(prefix.Length == 0 ? " xmlns=\"" : " xmlns:" + prefix + "=\"");
            AppendEscaped(builder, iri, attribute: true);
            builder.Append('"');
        }

        List<XAttribute> attributes = [];
        foreach(XAttribute attribute in element.Attributes())
        {
            if(!attribute.IsNamespaceDeclaration)
            {
                attributes.Add(attribute);
            }
        }

        attributes.Sort(static (left, right) =>
        {
            int byNamespace = string.CompareOrdinal(left.Name.NamespaceName, right.Name.NamespaceName);
            return byNamespace != 0 ? byNamespace : string.CompareOrdinal(left.Name.LocalName, right.Name.LocalName);
        });

        foreach(XAttribute attribute in attributes)
        {
            builder.Append(' ').Append(QualifiedName(attribute.Name, element)).Append("=\"");
            AppendEscaped(builder, attribute.Value, attribute: true);
            builder.Append('"');
        }

        builder.Append('>');

        return qualifiedName;
    }

    /// <summary>Pushes an element's child nodes onto the work stack in reverse, so they pop in document order.</summary>
    /// <param name="work">The work stack.</param>
    /// <param name="element">The parent element.</param>
    /// <param name="rendered">The namespaces in scope for the children.</param>
    /// <param name="apex">Whether the children are fragment apexes.</param>
    private static void PushChildrenReversed(Stack<Frame> work, XElement element, Dictionary<string, string> rendered, bool apex)
    {
        List<XNode> nodes = [];
        foreach(XNode node in element.Nodes())
        {
            nodes.Add(node);
        }

        for(int i = nodes.Count - 1; i >= 0; i--)
        {
            work.Push(new Frame(nodes[i], CloseName: null, rendered, apex));
        }
    }

    /// <summary>The namespaces in scope at an element, in document declaration order (outermost first; the nearest declaration supplies a prefix's IRI).</summary>
    /// <param name="element">The element.</param>
    /// <returns>The (prefix, IRI) pairs in document order.</returns>
    private static IEnumerable<(string Prefix, string Iri)> InScopeDocumentOrder(XElement element)
    {
        List<XElement> chain = [];
        foreach(XElement ancestor in element.AncestorsAndSelf())
        {
            chain.Add(ancestor);
        }

        List<string> order = [];
        Dictionary<string, string> iris = new(System.StringComparer.Ordinal);

        //chain is innermost-first; walk it outermost-first so a prefix's first-seen position is its outermost
        //declaration while the innermost declaration supplies the effective IRI.
        for(int i = chain.Count - 1; i >= 0; i--)
        {
            foreach((string prefix, string iri) in LocalNamespaces(chain[i]))
            {
                if(!iris.ContainsKey(prefix))
                {
                    order.Add(prefix);
                }

                iris[prefix] = iri;
            }
        }

        foreach(string prefix in order)
        {
            yield return (prefix, iris[prefix]);
        }
    }

    /// <summary>The namespace declarations made directly on an element.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The (prefix, IRI) pairs declared on the element; the default-namespace prefix is the empty string.</returns>
    private static IEnumerable<(string Prefix, string Iri)> LocalNamespaces(XElement element)
    {
        foreach(XAttribute attribute in element.Attributes())
        {
            if(attribute.IsNamespaceDeclaration)
            {
                string prefix = attribute.Name.Namespace == XNamespace.Xmlns ? attribute.Name.LocalName : string.Empty;
                yield return (prefix, attribute.Value);
            }
        }
    }

    /// <summary>The qualified name of an element/attribute relative to the scope element's namespace bindings.</summary>
    /// <param name="name">The name.</param>
    /// <param name="scope">The element whose prefix bindings resolve the name.</param>
    /// <returns>The <c>prefix:local</c> form, or <c>local</c> when the name is in no namespace or the default namespace.</returns>
    private static string QualifiedName(XName name, XElement scope)
    {
        if(name.Namespace == XNamespace.None)
        {
            return name.LocalName;
        }

        string? prefix = scope.GetPrefixOfNamespace(name.Namespace);

        return string.IsNullOrEmpty(prefix) ? name.LocalName : prefix + ":" + name.LocalName;
    }

    /// <summary>Appends a string with Canonical XML character escaping for text content or for an attribute value.</summary>
    /// <param name="builder">The output buffer.</param>
    /// <param name="value">The raw value.</param>
    /// <param name="attribute">Whether the value is an attribute value (escapes <c>"</c> and the whitespace controls) rather than text (escapes <c>&gt;</c>).</param>
    private static void AppendEscaped(StringBuilder builder, string value, bool attribute)
    {
        foreach(char character in value)
        {
            string? replacement = (character, attribute) switch
            {
                ('&', _) => "&amp;",
                ('<', _) => "&lt;",
                ('>', false) => "&gt;",
                ('"', true) => "&quot;",
                ('\t', true) => "&#x9;",
                ('\n', true) => "&#xA;",
                ('\r', _) => "&#xD;",
                _ => null
            };

            if(replacement is null)
            {
                builder.Append(character);
            }
            else
            {
                builder.Append(replacement);
            }
        }
    }

    /// <summary>The shared empty rendered-namespace map for the fragment apexes (never mutated).</summary>
    private static Dictionary<string, string> EmptyRendered { get; } = new(System.StringComparer.Ordinal);

    /// <summary>One unit of serialization work: a node to emit, or a deferred end tag to close.</summary>
    /// <param name="Node">The node to serialize, or <see langword="null"/> for a close-tag frame.</param>
    /// <param name="CloseName">The qualified name of a deferred end tag, or <see langword="null"/> for a node frame.</param>
    /// <param name="Rendered">The namespaces already in scope from output ancestors.</param>
    /// <param name="Apex">Whether a node frame's element is a fragment apex.</param>
    private readonly record struct Frame(XNode? Node, string? CloseName, Dictionary<string, string> Rendered, bool Apex);
}
