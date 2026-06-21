using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// Reads an RDF/XML document (<see href="https://www.w3.org/TR/rdf-syntax-grammar/">RDF 1.1 / 1.2 XML syntax</see>)
/// into <see cref="Quad"/>s. Like the Turtle and N-Quads readers it is value-based — a malformed document is recorded
/// in the supplied <see cref="DiagnosticBag"/> and the read returns whatever it parsed, never throwing for bad input.
/// </summary>
/// <remarks>
/// <para>
/// RDF/XML reading is an I/O-boundary concern, not a per-value hot path, so this uses <see cref="XDocument"/> and
/// walks the element tree over an <b>explicit stack</b> (the no-recursion discipline holds: node-element subjects are
/// determined top-down from their own attributes, so no post-order returns are needed).
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

    private static XNamespace Rdf { get; } = RdfNamespace;

    /// <summary>The Internationalization Tag Set namespace, whose <c>its:dir</c> attribute carries the RDF 1.2 base direction.</summary>
    private static XNamespace Its { get; } = "http://www.w3.org/2005/11/its";

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
        XDocument document;
        try
        {
            using ReadOnlyMemoryStream stream = new(source);

            //RDF/XML documents in the wild abbreviate namespace IRIs through DOCTYPE
            //internal-subset entities (the W3C OWL corpus and 2000s-era ontologies do),
            //so the internal subset is parsed. External resolution stays off — a null
            //resolver blocks XXE and external-subset fetches — and the expansion budget
            //bounds entity amplification.
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Parse,
                XmlResolver = null,
                MaxCharactersFromEntities = 1_000_000
            };

            using XmlReader reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch(Exception exception) when(exception is XmlException or FormatException or InvalidOperationException)
        {
            Report(diagnostics, $"Malformed RDF/XML: {exception.Message}");

            return quads;
        }

        if(document.Root is not XElement root)
        {
            return quads;
        }

        try
        {
            new Walker(quads, diagnostics, canonicalizer).Walk(root, baseIri);
        }
        catch(Exception exception) when(exception is UriFormatException or FormatException or ArgumentException or InvalidOperationException)
        {
            Report(diagnostics, $"Malformed RDF/XML: {exception.Message}");
        }

        return quads;
    }

    /// <summary>Records a malformedness diagnostic.</summary>
    /// <param name="diagnostics">The bag.</param>
    /// <param name="message">The message.</param>
    private static void Report(DiagnosticBag diagnostics, string message)
    {
        diagnostics.Add(new Diagnostic(MalformedCode, DiagnosticSeverity.Error, default, Utf8String.FromString(message)));
    }

    /// <summary>One unit of pending work: expand a node's child properties, or process one property element.</summary>
    private readonly record struct WorkItem(bool ExpandNode, XElement Element, RdfTerm Subject, NamedNode? Predicate, string? Base, string? Lang);

    /// <summary>The explicit-stack walker that emits quads from the element tree.</summary>
    private sealed class Walker(List<Quad> quads, DiagnosticBag diagnostics, XmlLiteralCanonicalizer xmlLiteralCanonicalizer)
    {
        private List<Quad> Quads { get; } = quads;

        private DiagnosticBag Diagnostics { get; } = diagnostics;

        /// <summary>The strategy that canonicalizes <c>rdf:parseType="Literal"</c> content into an XML literal's lexical form.</summary>
        private XmlLiteralCanonicalizer CanonicalizeXmlLiteral { get; } = xmlLiteralCanonicalizer;

        /// <summary>The set of rdf:ID-derived IRIs already seen, used to reject a duplicate rdf:ID relative to one base.</summary>
        private HashSet<string> IssuedIds { get; } = new(StringComparer.Ordinal);

        private int blankCounter;

        /// <summary>Walks the document from its root, emitting quads.</summary>
        /// <param name="root">The document root (<c>rdf:RDF</c>, or a single node element).</param>
        /// <param name="baseIri">The document base IRI, or <see langword="null"/>.</param>
        public void Walk(XElement root, string? baseIri)
        {
            string? rootBase = AttributeBase(root, baseIri);
            string? rootLang = AttributeLang(root, null);

            Stack<WorkItem> stack = new();
            if(IsRdf(root.Name, "RDF"))
            {
                foreach(XElement child in root.Elements())
                {
                    PushNode(stack, child, rootBase, rootLang);
                }
            }
            else
            {
                PushNode(stack, root, rootBase, rootLang);
            }

            while(stack.Count > 0)
            {
                WorkItem item = stack.Pop();
                if(item.ExpandNode)
                {
                    ExpandNode(stack, item.Subject, item.Element, item.Base, item.Lang);
                }
                else
                {
                    ProcessProperty(stack, item.Subject, item.Element, item.Predicate!, item.Base, item.Lang);
                }
            }
        }

        /// <summary>Determines a node element's subject, emits its type and property-attribute statements, and schedules its child properties.</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="element">The node element.</param>
        /// <param name="parentBase">The inherited base IRI.</param>
        /// <param name="parentLang">The inherited language.</param>
        private void PushNode(Stack<WorkItem> stack, XElement element, string? parentBase, string? parentLang)
        {
            string? @base = AttributeBase(element, parentBase);
            string? lang = AttributeLang(element, parentLang);
            RdfTerm subject = SubjectOf(element, @base);
            EmitNodeHeader(subject, element, @base, lang);
            stack.Push(new WorkItem(ExpandNode: true, element, subject, Predicate: null, @base, lang));
        }

        /// <summary>Schedules each child property element of a node, resolving <c>rdf:li</c> to <c>rdf:_n</c> in document order.</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="subject">The node's subject.</param>
        /// <param name="element">The node element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        private static void ExpandNode(Stack<WorkItem> stack, RdfTerm subject, XElement element, string? @base, string? lang)
        {
            int li = 1;
            foreach(XElement property in element.Elements())
            {
                NamedNode predicate = IsRdf(property.Name, "li")
                    ? new NamedNode(Utf8String.FromString(RdfNamespace + "_" + li++.ToString(CultureInfo.InvariantCulture)))
                    : new NamedNode(Utf8String.FromString(property.Name.NamespaceName + property.Name.LocalName));
                stack.Push(new WorkItem(ExpandNode: false, property, subject, predicate, @base, lang));
            }
        }

        /// <summary>Emits a property element's statement (and any reification / nested-node expansion).</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="subject">The statement subject.</param>
        /// <param name="element">The property element.</param>
        /// <param name="predicate">The statement predicate.</param>
        /// <param name="parentBase">The inherited base IRI.</param>
        /// <param name="parentLang">The inherited language.</param>
        private void ProcessProperty(Stack<WorkItem> stack, RdfTerm subject, XElement element, NamedNode predicate, string? parentBase, string? parentLang)
        {
            string? @base = AttributeBase(element, parentBase);
            string? lang = AttributeLang(element, parentLang);
            ValidatePropertyElement(element, @base);
            RdfTerm? @object = ResolveObject(stack, element, @base, lang);

            //A null object is an RDF 1.2-only construct (a triple term outside RDF 1.2 mode): it asserts nothing.
            if(@object is null)
            {
                return;
            }

            Quads.Add(new Quad(subject, predicate, @object));
            EmitAnnotation(element, @base, subject, predicate, @object);

            //rdf:ID on a property element reifies the statement it makes.
            if(element.Attribute(Rdf + "ID") is { } reifyId)
            {
                EmitReification(new NamedNode(Resolve(@base, "#" + reifyId.Value)), subject, predicate, @object);
            }
        }

        /// <summary>Resolves a property element's object: a referenced/blank/typed/plain term, a nested node, or a parse-typed value.</summary>
        /// <param name="stack">The work stack (nested nodes push their own property expansion).</param>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        /// <returns>The object term.</returns>
        private RdfTerm? ResolveObject(Stack<WorkItem> stack, XElement element, string? @base, string? lang)
        {
            string? parseType = element.Attribute(Rdf + "parseType")?.Value;
            if(parseType == "Literal")
            {
                return new Literal(Utf8String.FromString(CanonicalizeXmlLiteral(element)), new NamedNode(Utf8String.FromString(RdfNamespace + "XMLLiteral")));
            }

            if(parseType == "Triple")
            {
                return BuildTripleTerm(stack, element, @base, lang);
            }

            if(parseType == "Resource")
            {
                RdfTerm bnode = FreshBlank();
                stack.Push(new WorkItem(ExpandNode: true, element, bnode, Predicate: null, @base, lang));

                return bnode;
            }

            if(parseType == "Collection")
            {
                return BuildCollection(stack, element, @base, lang);
            }

            if(element.Attribute(Rdf + "resource") is { } resource)
            {
                NamedNode @object = new(Resolve(@base, resource.Value));
                EmitPropertyAttributes(@object, element, @base, lang);

                return @object;
            }

            if(element.Attribute(Rdf + "nodeID") is { } nodeId)
            {
                BlankNode @object = new(Utf8String.FromString("b" + nodeId.Value));
                EmitPropertyAttributes(@object, element, @base, lang);

                return @object;
            }

            XElement? child = FirstChildElement(element);
            if(child is not null)
            {
                RdfTerm childSubject = SubjectOf(child, AttributeBase(child, @base));
                string? childLang = AttributeLang(child, lang);
                EmitNodeHeader(childSubject, child, AttributeBase(child, @base), childLang);
                stack.Push(new WorkItem(ExpandNode: true, child, childSubject, Predicate: null, AttributeBase(child, @base), childLang));

                return childSubject;
            }

            //Empty element carrying property attributes (and no rdf:resource/nodeID) denotes a fresh blank node.
            if(HasPropertyAttributes(element))
            {
                RdfTerm bnode = FreshBlank();
                EmitPropertyAttributes(bnode, element, @base, lang);

                return bnode;
            }

            //A leaf: a typed, language-tagged (optionally base-directional), or plain literal from the element's text.
            string text = element.Value;
            if(element.Attribute(Rdf + "datatype") is { } datatype)
            {
                return new Literal(Utf8String.FromString(text), new NamedNode(Resolve(@base, datatype.Value)));
            }

            return TextLiteral(text, element, lang);
        }

        /// <summary>Builds an RDF collection (<c>parseType="Collection"</c>) from the property element's child node elements, returning the list head (or <c>rdf:nil</c> when empty).</summary>
        /// <param name="stack">The work stack.</param>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        /// <returns>The list head term.</returns>
        private RdfTerm BuildCollection(Stack<WorkItem> stack, XElement element, string? @base, string? lang)
        {
            NamedNode first = new(Utf8String.FromString(RdfNamespace + "first"));
            NamedNode rest = new(Utf8String.FromString(RdfNamespace + "rest"));
            NamedNode nil = new(Utf8String.FromString(RdfNamespace + "nil"));

            List<RdfTerm> items = [];
            foreach(XElement child in element.Elements())
            {
                string? childBase = AttributeBase(child, @base);
                string? childLang = AttributeLang(child, lang);
                RdfTerm childSubject = SubjectOf(child, childBase);
                EmitNodeHeader(childSubject, child, childBase, childLang);
                stack.Push(new WorkItem(ExpandNode: true, child, childSubject, Predicate: null, childBase, childLang));
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

        /// <summary>Emits a node element's <c>rdf:type</c> (when it is a typed node, not <c>rdf:Description</c>) and its property-attribute statements.</summary>
        /// <param name="subject">The node subject.</param>
        /// <param name="element">The node element.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope.</param>
        private void EmitNodeHeader(RdfTerm subject, XElement element, string? @base, string? lang)
        {
            ValidateNodeElement(element, @base);

            if(!IsRdf(element.Name, "Description") && !IsRdf(element.Name, "RDF"))
            {
                Quads.Add(new Quad(subject, new NamedNode(Utf8String.FromString(RdfNamespace + "type")), new NamedNode(Utf8String.FromString(element.Name.NamespaceName + element.Name.LocalName))));
            }

            EmitPropertyAttributes(subject, element, @base, lang);
        }

        /// <summary>Emits the property-attribute shorthand: each non-RDF-structural attribute is a statement (a literal object — language-tagged when an <c>xml:lang</c> is in scope — or a resource for <c>rdf:type</c>).</summary>
        /// <param name="subject">The subject the attributes describe.</param>
        /// <param name="element">The element carrying the attributes.</param>
        /// <param name="base">The base IRI in scope.</param>
        /// <param name="lang">The language in scope, applied to literal-valued property attributes.</param>
        private void EmitPropertyAttributes(RdfTerm subject, XElement element, string? @base, string? lang)
        {
            foreach(XAttribute attribute in element.Attributes())
            {
                if(attribute.IsNamespaceDeclaration || IsStructuralAttribute(attribute.Name))
                {
                    continue;
                }

                if(IsRdf(attribute.Name, "type"))
                {
                    Quads.Add(new Quad(subject, new NamedNode(Utf8String.FromString(RdfNamespace + "type")), new NamedNode(Resolve(@base, attribute.Value))));

                    continue;
                }

                NamedNode predicate = new(Utf8String.FromString(attribute.Name.NamespaceName + attribute.Name.LocalName));
                Quads.Add(new Quad(subject, predicate, TextLiteral(attribute.Value, element, lang)));
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
        private void EmitAnnotation(XElement element, string? @base, RdfTerm subject, NamedNode predicate, RdfTerm @object)
        {
            RdfTerm? reifier = element switch
            {
                _ when element.Attribute(Rdf + "annotation") is { } annotation => new NamedNode(Resolve(@base, annotation.Value)),
                _ when element.Attribute(Rdf + "annotationNodeID") is { } annotationNodeId => new BlankNode(Utf8String.FromString("b" + annotationNodeId.Value)),
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
        private RdfTerm SubjectOf(XElement element, string? @base)
        {
            if(element.Attribute(Rdf + "about") is { } about)
            {
                return new NamedNode(Resolve(@base, about.Value));
            }

            if(element.Attribute(Rdf + "ID") is { } id)
            {
                return new NamedNode(Resolve(@base, "#" + id.Value));
            }

            if(element.Attribute(Rdf + "nodeID") is { } nodeId)
            {
                return new BlankNode(Utf8String.FromString("b" + nodeId.Value));
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
        /// <returns>The triple term, or <see langword="null"/>.</returns>
        private RdfTerm? BuildTripleTerm(Stack<WorkItem> stack, XElement tripleProperty, string? propertyBase, string? propertyLang)
        {
            //rdf:parseType="Triple" is an RDF 1.2 feature; outside RDF 1.2 mode it denotes nothing.
            if(!IsVersion12(tripleProperty))
            {
                return null;
            }

            //The object position is the only place a triple term nests, so the nesting is linear: descend the
            //object chain pushing each (subject, predicate), then assemble innermost-first.
            Stack<(RdfTerm Subject, NamedNode Predicate)> pending = new();
            XElement currentProperty = tripleProperty;
            string? currentBase = propertyBase;
            string? currentLang = propertyLang;

            while(true)
            {
                XElement? inner = FirstChildElement(currentProperty);
                if(inner is null)
                {
                    Report(Diagnostics, "rdf:parseType=\"Triple\" requires a single inner node element describing one triple.");

                    return null;
                }

                string? innerBase = AttributeBase(inner, currentBase);
                string? innerLang = AttributeLang(inner, currentLang);
                RdfTerm subject = SubjectOf(inner, innerBase);

                bool typed = !IsRdf(inner.Name, "Description") && !IsRdf(inner.Name, "RDF");
                int attributeProperties = 0;
                XAttribute? singleAttribute = null;
                foreach(XAttribute attribute in inner.Attributes())
                {
                    if(attribute.IsNamespaceDeclaration || IsStructuralAttribute(attribute.Name))
                    {
                        continue;
                    }

                    attributeProperties++;
                    singleAttribute = attribute;
                }

                int childProperties = 0;
                XElement? childProperty = null;
                foreach(XElement child in inner.Elements())
                {
                    childProperties++;
                    childProperty = child;
                }

                if((typed ? 1 : 0) + attributeProperties + childProperties != 1)
                {
                    Report(Diagnostics, "rdf:parseType=\"Triple\" requires its inner node element to describe exactly one triple.");

                    return null;
                }

                NamedNode rdfType = new(Utf8String.FromString(RdfNamespace + "type"));
                if(typed)
                {
                    return Assemble(pending, subject, rdfType, new NamedNode(Utf8String.FromString(inner.Name.NamespaceName + inner.Name.LocalName)));
                }

                if(singleAttribute is not null)
                {
                    return IsRdf(singleAttribute.Name, "type")
                        ? Assemble(pending, subject, rdfType, new NamedNode(Resolve(innerBase, singleAttribute.Value)))
                        : Assemble(pending, subject, new NamedNode(Utf8String.FromString(singleAttribute.Name.NamespaceName + singleAttribute.Name.LocalName)), TextLiteral(singleAttribute.Value, inner, innerLang));
                }

                XElement property = childProperty!;
                string? childBase = AttributeBase(property, innerBase);
                string? childLang = AttributeLang(property, innerLang);
                NamedNode predicate = IsRdf(property.Name, "li")
                    ? new NamedNode(Utf8String.FromString(RdfNamespace + "_1"))
                    : new NamedNode(Utf8String.FromString(property.Name.NamespaceName + property.Name.LocalName));

                if(property.Attribute(Rdf + "parseType")?.Value == "Triple")
                {
                    //The triple currently at depth pending.Count + 1 cannot nest a deeper one without exceeding
                    //the quoted-triple nesting cap; recording a diagnostic and stopping keeps the pending chain bounded.
                    if(pending.Count + 1 >= QuotedTripleLimits.MaxNestingDepth)
                    {
                        Report(Diagnostics, "An RDF 1.2 quoted triple (rdf:parseType=\"Triple\") is nested deeper than the maximum nesting depth.");

                        return null;
                    }

                    pending.Push((subject, predicate));
                    currentProperty = property;
                    currentBase = innerBase;
                    currentLang = innerLang;

                    continue;
                }

                RdfTerm? leafObject = ResolveObject(stack, property, childBase, childLang);
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
        private void ValidateNodeElement(XElement element, string? @base)
        {
            if(IsForbiddenNodeElementName(element.Name))
            {
                Report(Diagnostics, $"'{element.Name}' is not a permitted node element name.");
            }

            ValidateReservedAttributes(element);

            int identity = element.Attribute(Rdf + "about") is null ? 0 : 1;
            if(element.Attribute(Rdf + "ID") is { } id)
            {
                identity++;
                ValidateId(id.Value, @base);
            }

            if(element.Attribute(Rdf + "nodeID") is { } nodeId)
            {
                identity++;
                ValidateNcName(nodeId.Value, "rdf:nodeID");
            }

            if(identity > 1)
            {
                Report(Diagnostics, "A node element may carry at most one of rdf:about, rdf:ID, rdf:nodeID.");
            }
        }

        /// <summary>Validates a property element's name and attributes against the RDF/XML grammar, recording any violation.</summary>
        /// <param name="element">The property element.</param>
        /// <param name="base">The base IRI in scope (for the rdf:ID reifier's uniqueness).</param>
        private void ValidatePropertyElement(XElement element, string? @base)
        {
            if(IsForbiddenPropertyElementName(element.Name))
            {
                Report(Diagnostics, $"'{element.Name}' is not a permitted property element name.");
            }

            ValidateReservedAttributes(element);

            bool hasResource = element.Attribute(Rdf + "resource") is not null;
            if(element.Attribute(Rdf + "nodeID") is { } nodeId)
            {
                ValidateNcName(nodeId.Value, "rdf:nodeID");
                if(hasResource)
                {
                    Report(Diagnostics, "A property element may not carry both rdf:nodeID and rdf:resource.");
                }
            }

            if(element.Attribute(Rdf + "parseType") is not null && hasResource)
            {
                Report(Diagnostics, "A property element may not carry both rdf:parseType and rdf:resource.");
            }

            if(element.Attribute(Rdf + "ID") is { } reifyId)
            {
                ValidateId(reifyId.Value, @base);
            }
        }

        /// <summary>Rejects the withdrawn RDF terms (<c>rdf:aboutEach</c>, <c>rdf:aboutEachPrefix</c>, <c>rdf:bagID</c>) and <c>rdf:li</c> when used as attributes.</summary>
        /// <param name="element">The element whose attributes are checked.</param>
        private void ValidateReservedAttributes(XElement element)
        {
            foreach(XAttribute attribute in element.Attributes())
            {
                if(IsForbiddenAttributeName(attribute.Name))
                {
                    Report(Diagnostics, $"'{attribute.Name}' is not permitted as an attribute.");
                }
            }
        }

        /// <summary>Validates an <c>rdf:ID</c> value: it must be an XML NCName and unique relative to its base.</summary>
        /// <param name="value">The rdf:ID value.</param>
        /// <param name="base">The base IRI in scope.</param>
        private void ValidateId(string value, string? @base)
        {
            ValidateNcName(value, "rdf:ID");

            string resolved = Resolve(@base, "#" + value).ToString();
            if(!IssuedIds.Add(resolved))
            {
                Report(Diagnostics, $"rdf:ID '{value}' is defined more than once relative to the same base.");
            }
        }

        /// <summary>Records an error when a value is not a valid XML NCName (the production <c>rdf:ID</c> / <c>rdf:nodeID</c> values must match).</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="attribute">The attribute name, for the diagnostic message.</param>
        private void ValidateNcName(string value, string attribute)
        {
            if(!IsNcName(value))
            {
                Report(Diagnostics, $"The value of {attribute} ('{value}') is not a valid XML NCName.");
            }
        }
    }

    /// <summary>Whether an attribute is an RDF/XML structural attribute (not a property-attribute or namespace declaration).</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns><see langword="true"/> for an RDF structural attribute, an <c>xml:</c> attribute, or an attribute that does not form a property attribute.</returns>
    private static bool IsStructuralAttribute(XName name)
    {
        if(name.Namespace == XNamespace.Xml || name.Namespace == XNamespace.Xmlns)
        {
            return true;
        }

        //A property attribute must have a namespace; an attribute in no namespace — including the
        //XML-reserved unprefixed names beginning with "xml", which the spec excludes from RDF
        //processing — is not a property attribute.
        if(name.Namespace == XNamespace.None)
        {
            return true;
        }

        //its:dir / its:version carry the RDF 1.2 base direction and ITS version; they configure the
        //literal, they are not property statements.
        if(name.Namespace == Its && name.LocalName is "dir" or "version")
        {
            return true;
        }

        //rdf:version declares the RDF 1.2 mode and rdf:annotation / rdf:annotationNodeID name a reifier; all are
        //syntactic, not property statements.
        return name.Namespace == Rdf && name.LocalName is "about" or "ID" or "nodeID" or "resource" or "datatype" or "parseType" or "li" or "version" or "annotation" or "annotationNodeID";
    }

    /// <summary>Whether an element/attribute name is the RDF term with the given local name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="local">The RDF local name.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool IsRdf(XName name, string local)
    {
        return name.Namespace == Rdf && name.LocalName == local;
    }

    /// <summary>Whether a name is forbidden as a node element name (the core syntax terms, <c>rdf:li</c>, and the withdrawn old terms).</summary>
    /// <param name="name">The element name.</param>
    /// <returns><see langword="true"/> when the name may not be a node element.</returns>
    private static bool IsForbiddenNodeElementName(XName name)
    {
        return name.Namespace == Rdf
            && name.LocalName is "RDF" or "ID" or "about" or "parseType" or "resource" or "nodeID" or "datatype"
                or "li" or "aboutEach" or "aboutEachPrefix" or "bagID";
    }

    /// <summary>Whether a name is forbidden as a property element name (the core syntax terms, <c>rdf:Description</c>, and the withdrawn old terms; <c>rdf:li</c> is permitted).</summary>
    /// <param name="name">The element name.</param>
    /// <returns><see langword="true"/> when the name may not be a property element.</returns>
    private static bool IsForbiddenPropertyElementName(XName name)
    {
        return name.Namespace == Rdf
            && name.LocalName is "RDF" or "ID" or "about" or "parseType" or "resource" or "nodeID" or "datatype"
                or "Description" or "aboutEach" or "aboutEachPrefix" or "bagID";
    }

    /// <summary>Whether a name is forbidden as an attribute (<c>rdf:li</c> and the withdrawn old terms).</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns><see langword="true"/> when the name may not appear as an attribute.</returns>
    private static bool IsForbiddenAttributeName(XName name)
    {
        return name.Namespace == Rdf && name.LocalName is "li" or "aboutEach" or "aboutEachPrefix" or "bagID";
    }

    /// <summary>
    /// Builds a literal from an element's text or attribute value: a plain <c>xsd:string</c> when no language is in
    /// scope, an <c>rdf:langString</c> when a language is, or an <c>rdf:dirLangString</c> when a base direction is also
    /// in scope and the document is in RDF 1.2 mode (<c>rdf:version="1.2"</c> on the element or an ancestor).
    /// </summary>
    /// <param name="text">The lexical value.</param>
    /// <param name="element">The element the value comes from, for the in-scope <c>rdf:version</c> and <c>its:dir</c>.</param>
    /// <param name="lang">The in-scope language, or <see langword="null"/>.</param>
    /// <returns>The literal term.</returns>
    private static Literal TextLiteral(string text, XElement element, string? lang)
    {
        if(lang is null)
        {
            return new Literal(Utf8String.FromString(text), new NamedNode(Vocabulary.Xsd.String));
        }

        TextDirection? direction = IsVersion12(element) ? EffectiveDirection(element) : null;

        return direction is { } resolved
            ? new Literal(Utf8String.FromString(text), new NamedNode(Utf8String.FromString(RdfNamespace + "dirLangString")), Utf8String.FromString(lang), resolved)
            : new Literal(Utf8String.FromString(text), new NamedNode(Utf8String.FromString(RdfNamespace + "langString")), Utf8String.FromString(lang));
    }

    /// <summary>Whether the document is in RDF 1.2 mode for an element: <c>rdf:version="1.2"</c> on the element or any ancestor.</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when RDF 1.2 features are enabled in the element's scope.</returns>
    private static bool IsVersion12(XElement element)
    {
        foreach(XElement scope in element.AncestorsAndSelf())
        {
            if(scope.Attribute(Rdf + "version")?.Value == "1.2")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The in-scope base direction for an element: the nearest <c>its:dir</c> on the element or an ancestor.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The parsed base direction, or <see langword="null"/> when none is in scope or it is unrecognised.</returns>
    private static TextDirection? EffectiveDirection(XElement element)
    {
        foreach(XElement scope in element.AncestorsAndSelf())
        {
            if(scope.Attribute(Its + "dir") is { } dir)
            {
                return TextDirections.TryParse(dir.Value, out TextDirection direction) ? direction : null;
            }
        }

        return null;
    }

    /// <summary>Whether a string is a valid XML NCName (the production <c>rdf:ID</c> / <c>rdf:nodeID</c> values must satisfy).</summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when the value is a non-empty NCName.</returns>
    private static bool IsNcName(string value)
    {
        if(value.Length == 0 || !XmlConvert.IsStartNCNameChar(value[0]))
        {
            return false;
        }

        for(int i = 1; i < value.Length; i++)
        {
            if(!XmlConvert.IsNCNameChar(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The first child element of an element, or <see langword="null"/>.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The first child element, or <see langword="null"/>.</returns>
    private static XElement? FirstChildElement(XElement element)
    {
        foreach(XElement child in element.Elements())
        {
            return child;
        }

        return null;
    }

    /// <summary>Whether an element carries any property-attribute (a non-structural, non-namespace attribute).</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when a property attribute is present.</returns>
    private static bool HasPropertyAttributes(XElement element)
    {
        foreach(XAttribute attribute in element.Attributes())
        {
            if(!attribute.IsNamespaceDeclaration && !IsStructuralAttribute(attribute.Name))
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
    private static string? AttributeBase(XElement element, string? parentBase)
    {
        return element.Attribute(XNamespace.Xml + "base")?.Value ?? parentBase;
    }

    /// <summary>The language for an element: its <c>xml:lang</c> if present (empty clears it), else the inherited language.</summary>
    /// <param name="element">The element.</param>
    /// <param name="parentLang">The inherited language.</param>
    /// <returns>The language in scope, or <see langword="null"/>.</returns>
    private static string? AttributeLang(XElement element, string? parentLang)
    {
        if(element.Attribute(XNamespace.Xml + "lang") is not { } lang)
        {
            return parentLang;
        }

        return lang.Value.Length == 0 ? null : lang.Value;
    }

    /// <summary>Resolves a (possibly relative) IRI reference against the base IRI in scope.</summary>
    /// <param name="base">The base IRI, or <see langword="null"/>.</param>
    /// <param name="reference">The IRI reference.</param>
    /// <returns>The resolved IRI.</returns>
    private static Utf8String Resolve(string? @base, string reference)
    {
        return @base is null ? Utf8String.FromString(reference) : Utf8String.FromString(IriResolver.ResolveIri(@base, reference));
    }

}
