using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// Reads the <see href="https://www.w3.org/TR/rdf-sparql-XMLres/">SPARQL Query Results XML Format</see> (the
/// <c>.srx</c> serialization, namespace <c>http://www.w3.org/2005/sparql-results#</c>) into a
/// <see cref="SparqlResultSet"/>: a <c>SELECT</c> result's head variables and solution rows, or an <c>ASK</c>
/// result's boolean. RDF 1.2 triple-term binding values (<c>&lt;triple&gt;</c>) are supported.
/// </summary>
/// <remarks>
/// <para>
/// This is the reader the conformance harness uses to load a SPARQL evaluation test's expected results for
/// comparison against the engine's output. DTD processing is prohibited and external resolution is disabled, so a
/// fixture cannot pull in external entities.
/// </para>
/// <para>
/// Binding value forms: <c>&lt;uri&gt;</c> → a <see cref="NamedNode"/>; <c>&lt;bnode&gt;</c> → a
/// <see cref="BlankNode"/> (its label scoped to the document — blank-node identity across a result set is decided
/// structurally by the comparer, not by label); <c>&lt;literal&gt;</c> → a <see cref="Literal"/> typed
/// <c>xsd:string</c> by default, <c>rdf:langString</c> when it carries <c>xml:lang</c>, or its declared
/// <c>datatype</c>; <c>&lt;triple&gt;</c> → a <see cref="TripleTerm"/> built from nested <c>subject</c>/
/// <c>predicate</c>/<c>object</c> terms.
/// </para>
/// </remarks>
public static class SparqlResultsXmlReader
{
    private static XNamespace ResultsNamespace { get; } = "http://www.w3.org/2005/sparql-results#";

    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    private static NamedNode RdfLangString { get; } = new(Vocabulary.Rdf.LangString);

    /// <summary>Reads a result set from the in-memory XML bytes.</summary>
    /// <param name="bytes">The <c>.srx</c> document bytes.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results XML.</exception>
    public static SparqlResultSet Read(ReadOnlyMemory<byte> bytes)
    {
        using MemoryStream stream = new(bytes.ToArray(), writable: false);

        return Read(stream);
    }

    /// <summary>Reads a result set from an XML stream.</summary>
    /// <param name="stream">The stream over the <c>.srx</c> document.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results XML.</exception>
    public static SparqlResultSet Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        XDocument document;
        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using XmlReader reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader);
        }
        catch(XmlException ex)
        {
            throw new FormatException($"Malformed SPARQL Results XML: {ex.Message}", ex);
        }

        XElement root = document.Root ?? throw new FormatException("SPARQL Results XML has no root element.");
        if(root.Name != ResultsNamespace + "sparql")
        {
            throw new FormatException($"Expected a <sparql> root in the SPARQL-results namespace; found <{root.Name}>.");
        }

        List<Utf8String> variables = ReadHeadVariables(root);

        XElement? booleanElement = root.Element(ResultsNamespace + "boolean");
        if(booleanElement is not null)
        {
            return SparqlResultSet.ForAsk(ParseBoolean(booleanElement.Value));
        }

        XElement results = root.Element(ResultsNamespace + "results")
            ?? throw new FormatException("SPARQL SELECT results XML is missing its <results> element.");

        List<SparqlSolution> solutions = [];
        foreach(XElement result in results.Elements(ResultsNamespace + "result"))
        {
            solutions.Add(ReadSolution(result));
        }

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Reads the head's declared variables in document order.</summary>
    /// <param name="root">The <c>&lt;sparql&gt;</c> root element.</param>
    /// <returns>The variable names.</returns>
    private static List<Utf8String> ReadHeadVariables(XElement root)
    {
        List<Utf8String> variables = [];
        XElement? head = root.Element(ResultsNamespace + "head");
        if(head is null)
        {
            return variables;
        }

        foreach(XElement variable in head.Elements(ResultsNamespace + "variable"))
        {
            string? name = variable.Attribute("name")?.Value;
            if(name is not null)
            {
                variables.Add(Utf8String.FromString(name));
            }
        }

        return variables;
    }

    /// <summary>Reads one <c>&lt;result&gt;</c> row into a solution, binding each named binding to its parsed term.</summary>
    /// <param name="result">The <c>&lt;result&gt;</c> element.</param>
    /// <returns>The solution mapping.</returns>
    private static SparqlSolution ReadSolution(XElement result)
    {
        List<SparqlBinding> bindings = [];
        foreach(XElement binding in result.Elements(ResultsNamespace + "binding"))
        {
            string name = binding.Attribute("name")?.Value
                ?? throw new FormatException("A <binding> element is missing its 'name' attribute.");
            XElement value = OnlyChildElement(binding)
                ?? throw new FormatException($"The <binding name=\"{name}\"> has no value element.");

            bindings.Add(new SparqlBinding(new SparqlVariable(Utf8String.FromString(name)), ParseTerm(value)));
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>
    /// Parses a binding value element (<c>uri</c>/<c>literal</c>/<c>bnode</c>/<c>triple</c>) to an RDF term over an
    /// explicit post-order work stack, so a deeply-nested triple term cannot overflow the call stack.
    /// </summary>
    /// <param name="value">The binding value element.</param>
    /// <returns>The parsed term.</returns>
    private static RdfTerm ParseTerm(XElement value)
    {
        Dictionary<XElement, RdfTerm> results = new(ReferenceEqualityComparer.Instance);
        Stack<(XElement Node, bool Combine)> work = new();
        work.Push((value, Combine: false));

        while(work.Count > 0)
        {
            (XElement node, bool combine) = work.Pop();
            if(combine)
            {
                results[node] = CombineTriple(node, results);

                continue;
            }

            if(node.Name == ResultsNamespace + "triple")
            {
                (XElement subject, XElement predicate, XElement @object) = TripleChildren(node);
                work.Push((node, Combine: true));
                work.Push((@object, Combine: false));
                work.Push((predicate, Combine: false));
                work.Push((subject, Combine: false));
            }
            else
            {
                results[node] = ParseLeaf(node);
            }
        }

        return results[value];
    }

    /// <summary>Combines a <c>&lt;triple&gt;</c> from its already-parsed subject/predicate/object terms.</summary>
    /// <param name="triple">The <c>&lt;triple&gt;</c> element.</param>
    /// <param name="results">The map of already-parsed term elements.</param>
    /// <returns>The triple term.</returns>
    private static TripleTerm CombineTriple(XElement triple, Dictionary<XElement, RdfTerm> results)
    {
        (XElement subject, XElement predicate, XElement @object) = TripleChildren(triple);
        if(results[predicate] is not NamedNode predicateNode)
        {
            throw new FormatException("A <triple> binding value has a non-IRI predicate term.");
        }

        return new TripleTerm(results[subject], predicateNode, results[@object]);
    }

    /// <summary>Extracts the subject/predicate/object inner value elements of a <c>&lt;triple&gt;</c> element.</summary>
    /// <param name="triple">The <c>&lt;triple&gt;</c> element.</param>
    /// <returns>The three inner term elements.</returns>
    private static (XElement Subject, XElement Predicate, XElement Object) TripleChildren(XElement triple)
    {
        XElement subject = InnerTerm(triple, "subject");
        XElement predicate = InnerTerm(triple, "predicate");
        XElement @object = InnerTerm(triple, "object");

        return (subject, predicate, @object);
    }

    /// <summary>Returns the single value element nested in a triple component wrapper (<c>subject</c>/<c>predicate</c>/<c>object</c>).</summary>
    /// <param name="triple">The <c>&lt;triple&gt;</c> element.</param>
    /// <param name="component">The component wrapper local name.</param>
    /// <returns>The inner value element.</returns>
    private static XElement InnerTerm(XElement triple, string component)
    {
        XElement wrapper = triple.Element(ResultsNamespace + component)
            ?? throw new FormatException($"A <triple> binding value is missing its <{component}>.");

        return OnlyChildElement(wrapper)
            ?? throw new FormatException($"A <triple> binding value's <{component}> has no term element.");
    }

    /// <summary>Parses a leaf value element (<c>uri</c>/<c>literal</c>/<c>bnode</c>) to its RDF term.</summary>
    /// <param name="value">The value element.</param>
    /// <returns>The parsed term.</returns>
    private static RdfTerm ParseLeaf(XElement value)
    {
        string local = value.Name.LocalName;

        return local switch
        {
            "uri" => new NamedNode(Utf8String.FromString(value.Value)),
            "bnode" => new BlankNode(Utf8String.FromString(value.Value)),
            "literal" => ParseLiteral(value),
            _ => throw new FormatException($"Unsupported SPARQL-results binding value element <{value.Name}>.")
        };
    }

    /// <summary>Parses a <c>&lt;literal&gt;</c> value: <c>rdf:langString</c> when it carries <c>xml:lang</c>, its declared <c>datatype</c>, or <c>xsd:string</c> otherwise.</summary>
    /// <param name="literal">The <c>&lt;literal&gt;</c> element.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseLiteral(XElement literal)
    {
        Utf8String lexical = Utf8String.FromString(literal.Value);
        string? language = literal.Attribute(XNamespace.Xml + "lang")?.Value;
        if(language is not null)
        {
            return new Literal(lexical, RdfLangString, Utf8String.FromString(language));
        }

        string? datatype = literal.Attribute("datatype")?.Value;
        if(datatype is not null)
        {
            return new Literal(lexical, new NamedNode(Utf8String.FromString(datatype)));
        }

        return new Literal(lexical, XsdString);
    }

    /// <summary>Returns the single child element of an element, or <see langword="null"/> when there is none.</summary>
    /// <param name="parent">The parent element.</param>
    /// <returns>The single child element, or <see langword="null"/>.</returns>
    private static XElement? OnlyChildElement(XElement parent)
    {
        XElement? found = null;
        foreach(XElement child in parent.Elements())
        {
            if(found is not null)
            {
                throw new FormatException($"Expected a single child element under <{parent.Name}> but found more than one.");
            }

            found = child;
        }

        return found;
    }

    /// <summary>Parses the <c>&lt;boolean&gt;</c> text of an <c>ASK</c> result.</summary>
    /// <param name="text">The element text.</param>
    /// <returns>The boolean answer.</returns>
    /// <exception cref="FormatException">The text is neither <c>true</c> nor <c>false</c>.</exception>
    private static bool ParseBoolean(string text)
    {
        return text.Trim() switch
        {
            "true" => true,
            "false" => false,
            _ => throw new FormatException($"A SPARQL ASK result's <boolean> must be 'true' or 'false'; found '{text}'.")
        };
    }
}
