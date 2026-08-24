using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Xml;
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
/// comparison against the engine's output. It is byte-native: it parses the UTF-8 document directly through the
/// shared <see cref="XmlByteReader"/> (no <see cref="System.Xml"/> DOM, no UTF-16 round-trip). A DTD is rejected, so
/// a fixture cannot pull in external entities.
/// </para>
/// <para>
/// Binding value forms: <c>&lt;uri&gt;</c> → a <see cref="NamedNode"/>; <c>&lt;bnode&gt;</c> → a
/// <see cref="BlankNode"/> (its label scoped to the document — blank-node identity across a result set is decided
/// structurally by the comparer, not by label); <c>&lt;literal&gt;</c> → a <see cref="Literal"/> typed
/// <c>xsd:string</c> by default, <c>rdf:langString</c> when it carries <c>xml:lang</c> (<c>rdf:dirLangString</c>
/// when it also carries <c>its:dir</c>), or its declared <c>datatype</c>; <c>&lt;triple&gt;</c> → a
/// <see cref="TripleTerm"/> built from nested <c>subject</c>/<c>predicate</c>/<c>object</c> terms.
/// </para>
/// </remarks>
public static class SparqlResultsXmlReader
{
    /// <summary>The SPARQL-results namespace IRI, the namespace of every result-structure element.</summary>
    private static ReadOnlySpan<byte> ResultsNamespace => "http://www.w3.org/2005/sparql-results#"u8;

    /// <summary>The Internationalization Tag Set namespace, carrying the <c>its:dir</c> base-direction attribute.</summary>
    private static ReadOnlySpan<byte> ItsNamespace => "http://www.w3.org/2005/11/its"u8;

    /// <summary>The XML namespace, carrying the <c>xml:lang</c> attribute.</summary>
    private static ReadOnlySpan<byte> XmlNamespace => "http://www.w3.org/XML/1998/namespace"u8;

    /// <summary>The <c>xsd:string</c> datatype of a plain literal.</summary>
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    /// <summary>The <c>rdf:langString</c> datatype of a language-tagged literal.</summary>
    private static NamedNode RdfLangString { get; } = new(Vocabulary.Rdf.LangString);

    /// <summary>The <c>rdf:dirLangString</c> datatype of a directional language-tagged literal.</summary>
    private static NamedNode RdfDirLangString { get; } = new(Vocabulary.Rdf.DirLangString);

    /// <summary>Reads a result set from the in-memory XML bytes.</summary>
    /// <param name="bytes">The <c>.srx</c> document bytes.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results XML.</exception>
    /// <exception cref="TripleTermDepthLimitException">A <c>triple</c> binding value is nested beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/>.</exception>
    public static SparqlResultSet Read(ReadOnlyMemory<byte> bytes)
    {
        XmlByteNode root = XmlByteReader.Read(bytes.Span);
        if(!root.Matches("sparql"u8, ResultsNamespace))
        {
            throw new FormatException("Expected a <sparql> root in the SPARQL-results namespace.");
        }

        List<Utf8String> variables = ReadHeadVariables(root);

        XmlByteNode? boolean = root.Element("boolean"u8, ResultsNamespace);
        if(boolean is not null)
        {
            return SparqlResultSet.ForAsk(ParseBoolean(boolean.Text));
        }

        XmlByteNode results = root.Element("results"u8, ResultsNamespace)
            ?? throw new FormatException("SPARQL SELECT results XML is missing its <results> element.");

        List<SparqlSolution> solutions = [];
        foreach(XmlByteNode result in results.Children)
        {
            if(result.Matches("result"u8, ResultsNamespace))
            {
                solutions.Add(ReadSolution(result));
            }
        }

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Reads a result set from an XML stream by draining it to bytes.</summary>
    /// <param name="stream">The stream over the <c>.srx</c> document.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results XML.</exception>
    /// <exception cref="TripleTermDepthLimitException">A <c>triple</c> binding value is nested beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/>.</exception>
    public static SparqlResultSet Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);

        return Read(new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    /// <summary>
    /// Reads a result set from the in-memory XML bytes by forward-streaming it: each <c>&lt;result&gt;</c> row is read
    /// and discarded as it completes, so the live element tree never exceeds one row (plus the small retained
    /// <c>&lt;head&gt;</c>). It produces the same result set the buffered <see cref="Read(ReadOnlyMemory{byte})"/> does.
    /// </summary>
    /// <param name="bytes">The <c>.srx</c> document bytes.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results XML.</exception>
    /// <exception cref="TripleTermDepthLimitException">A <c>triple</c> binding value is nested beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/>.</exception>
    public static SparqlResultSet ReadStreaming(ReadOnlyMemory<byte> bytes)
    {
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false, streaming: true);
        StreamingResults streamed = new();
        XmlByteNode root = XmlByteReader.StreamContainer(scanner, bytes, IsResultsContainer, onContainerMatched: null, streamed.OnResult);
        if(!root.Matches("sparql"u8, ResultsNamespace))
        {
            throw new FormatException("Expected a <sparql> root in the SPARQL-results namespace.");
        }

        List<Utf8String> variables = ReadHeadVariables(root);

        XmlByteNode? boolean = root.Element("boolean"u8, ResultsNamespace);
        if(boolean is not null)
        {
            return SparqlResultSet.ForAsk(ParseBoolean(boolean.Text));
        }

        //The <results> element is retained (only its <result> children stream and detach), so its absence still
        //distinguishes a SELECT document missing its body from a present-but-empty result set.
        if(root.Element("results"u8, ResultsNamespace) is null)
        {
            throw new FormatException("SPARQL SELECT results XML is missing its <results> element.");
        }

        return SparqlResultSet.ForSelect(variables, streamed.Solutions);
    }

    /// <summary>Reads a result set from an XML stream by draining it to bytes and forward-streaming them.</summary>
    /// <param name="stream">The stream over the <c>.srx</c> document.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results XML.</exception>
    /// <exception cref="TripleTermDepthLimitException">A <c>triple</c> binding value is nested beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/>.</exception>
    public static SparqlResultSet ReadStreaming(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);

        return ReadStreaming(new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    /// <summary>The streaming container predicate: the <c>&lt;results&gt;</c> element directly under the root (depth 1), whose <c>&lt;result&gt;</c> children stream.</summary>
    /// <param name="node">The candidate container element.</param>
    /// <param name="depth">The element's zero-based depth.</param>
    /// <returns><see langword="true"/> when the element is the depth-1 <c>&lt;results&gt;</c> element.</returns>
    private static bool IsResultsContainer(XmlByteNode node, int depth)
    {
        return depth == 1 && node.Matches("results"u8, ResultsNamespace);
    }

    /// <summary>Accumulates the solution rows a forward stream yields, one completed <c>&lt;result&gt;</c> subtree at a time.</summary>
    private sealed class StreamingResults
    {
        /// <summary>The solution rows read so far, in document order.</summary>
        public List<SparqlSolution> Solutions { get; } = [];

        /// <summary>Reads one completed direct child of <c>&lt;results&gt;</c> into a solution; a non-<c>result</c> child is skipped, mirroring the buffered read.</summary>
        /// <param name="result">The completed direct child of <c>&lt;results&gt;</c>.</param>
        public void OnResult(XmlByteNode result)
        {
            if(result.Matches("result"u8, ResultsNamespace))
            {
                Solutions.Add(ReadSolution(result));
            }
        }
    }

    /// <summary>Reads the head's declared variables in document order.</summary>
    /// <param name="root">The <c>&lt;sparql&gt;</c> root element.</param>
    /// <returns>The variable names.</returns>
    private static List<Utf8String> ReadHeadVariables(XmlByteNode root)
    {
        List<Utf8String> variables = [];
        XmlByteNode? head = root.Element("head"u8, ResultsNamespace);
        if(head is null)
        {
            return variables;
        }

        foreach(XmlByteNode variable in head.Children)
        {
            if(variable.Matches("variable"u8, ResultsNamespace) && variable.Attribute("name"u8) is Utf8String name)
            {
                variables.Add(name);
            }
        }

        return variables;
    }

    /// <summary>Reads one <c>&lt;result&gt;</c> row into a solution, binding each named binding to its parsed term.</summary>
    /// <param name="result">The <c>&lt;result&gt;</c> element.</param>
    /// <returns>The solution mapping.</returns>
    /// <exception cref="FormatException">A <c>binding</c> is missing its name or value.</exception>
    private static SparqlSolution ReadSolution(XmlByteNode result)
    {
        List<SparqlBinding> bindings = [];
        foreach(XmlByteNode binding in result.Children)
        {
            if(!binding.Matches("binding"u8, ResultsNamespace))
            {
                continue;
            }

            Utf8String name = binding.Attribute("name"u8)
                ?? throw new FormatException("A <binding> element is missing its 'name' attribute.");
            XmlByteNode value = OnlyChildElement(binding)
                ?? throw new FormatException("A <binding> element has no value element.");

            bindings.Add(new SparqlBinding(new SparqlVariable(name), ParseTerm(value)));
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>
    /// Parses a binding value element (<c>uri</c>/<c>literal</c>/<c>bnode</c>/<c>triple</c>) to an RDF term over an
    /// explicit work stack carrying each frame's quoted-triple nesting depth, so a deeply-nested triple cannot
    /// overflow the call stack and a term nested beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/> raises a
    /// catchable <see cref="TripleTermDepthLimitException"/> (mirroring <see cref="SparqlResultsXmlWriter"/>).
    /// </summary>
    /// <param name="value">The binding value element.</param>
    /// <returns>The parsed term.</returns>
    /// <exception cref="TripleTermDepthLimitException">A <c>triple</c> binding value is nested too deep.</exception>
    private static RdfTerm ParseTerm(XmlByteNode value)
    {
        Dictionary<XmlByteNode, RdfTerm> results = new(ReferenceEqualityComparer.Instance);
        Stack<(XmlByteNode Node, bool Combine, int Depth)> work = new();
        work.Push((value, Combine: false, Depth: 1));

        while(work.Count > 0)
        {
            (XmlByteNode node, bool combine, int depth) = work.Pop();
            if(combine)
            {
                results[node] = CombineTriple(node, results);

                continue;
            }

            if(node.Matches("triple"u8, ResultsNamespace))
            {
                if(depth > QuotedTripleLimits.MaxNestingDepth)
                {
                    throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                }

                (XmlByteNode subject, XmlByteNode predicate, XmlByteNode @object) = TripleChildren(node);
                work.Push((node, Combine: true, depth));
                work.Push((@object, Combine: false, depth + 1));
                work.Push((predicate, Combine: false, depth + 1));
                work.Push((subject, Combine: false, depth + 1));
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
    /// <exception cref="FormatException">The predicate term is not an IRI.</exception>
    private static TripleTerm CombineTriple(XmlByteNode triple, Dictionary<XmlByteNode, RdfTerm> results)
    {
        (XmlByteNode subject, XmlByteNode predicate, XmlByteNode @object) = TripleChildren(triple);
        if(results[predicate] is not NamedNode predicateNode)
        {
            throw new FormatException("A <triple> binding value has a non-IRI predicate term.");
        }

        return new TripleTerm(results[subject], predicateNode, results[@object]);
    }

    /// <summary>Extracts the subject/predicate/object inner value elements of a <c>&lt;triple&gt;</c> element.</summary>
    /// <param name="triple">The <c>&lt;triple&gt;</c> element.</param>
    /// <returns>The three inner term elements.</returns>
    private static (XmlByteNode Subject, XmlByteNode Predicate, XmlByteNode Object) TripleChildren(XmlByteNode triple)
    {
        return (InnerTerm(triple, "subject"u8), InnerTerm(triple, "predicate"u8), InnerTerm(triple, "object"u8));
    }

    /// <summary>Returns the single value element nested in a triple component wrapper (<c>subject</c>/<c>predicate</c>/<c>object</c>).</summary>
    /// <param name="triple">The <c>&lt;triple&gt;</c> element.</param>
    /// <param name="component">The component wrapper local name.</param>
    /// <returns>The inner value element.</returns>
    /// <exception cref="FormatException">The component wrapper is missing or empty.</exception>
    private static XmlByteNode InnerTerm(XmlByteNode triple, ReadOnlySpan<byte> component)
    {
        XmlByteNode wrapper = triple.Element(component, ResultsNamespace)
            ?? throw new FormatException("A <triple> binding value is missing a component.");

        return OnlyChildElement(wrapper)
            ?? throw new FormatException("A <triple> binding value's component has no term element.");
    }

    /// <summary>Parses a leaf value element (<c>uri</c>/<c>literal</c>/<c>bnode</c>) to its RDF term.</summary>
    /// <param name="value">The value element.</param>
    /// <returns>The parsed term.</returns>
    /// <exception cref="FormatException">The element is not a known leaf value form.</exception>
    private static RdfTerm ParseLeaf(XmlByteNode value)
    {
        ReadOnlySpan<byte> local = value.LocalName.Span;
        if(local.SequenceEqual("uri"u8))
        {
            return new NamedNode(value.Text);
        }

        if(local.SequenceEqual("bnode"u8))
        {
            return new BlankNode(value.Text);
        }

        if(local.SequenceEqual("literal"u8))
        {
            return ParseLiteral(value);
        }

        throw new FormatException("Unsupported SPARQL-results binding value element.");
    }

    /// <summary>Parses a <c>&lt;literal&gt;</c> value: <c>rdf:dirLangString</c> with both <c>xml:lang</c> and <c>its:dir</c>, <c>rdf:langString</c> for <c>xml:lang</c> alone, its declared <c>datatype</c>, or <c>xsd:string</c>.</summary>
    /// <param name="literal">The <c>&lt;literal&gt;</c> element.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseLiteral(XmlByteNode literal)
    {
        Utf8String lexical = literal.Text;
        if(literal.Attribute("lang"u8, XmlNamespace) is Utf8String tag)
        {
            if(literal.Attribute("dir"u8, ItsNamespace) is Utf8String dir && TextDirections.TryParse(dir.Span, out TextDirection direction))
            {
                return new Literal(lexical, RdfDirLangString, tag, direction);
            }

            return new Literal(lexical, RdfLangString, tag);
        }

        if(literal.Attribute("datatype"u8) is Utf8String datatype)
        {
            return new Literal(lexical, new NamedNode(datatype));
        }

        return new Literal(lexical, XsdString);
    }

    /// <summary>Returns the single child element of an element, or <see langword="null"/> when there is none.</summary>
    /// <param name="parent">The parent element.</param>
    /// <returns>The single child element, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">The element has more than one child element.</exception>
    private static XmlByteNode? OnlyChildElement(XmlByteNode parent)
    {
        XmlByteNode? found = null;
        foreach(XmlByteNode child in parent.Children)
        {
            if(found is not null)
            {
                throw new FormatException("Expected a single child element but found more than one.");
            }

            found = child;
        }

        return found;
    }

    /// <summary>Parses the <c>&lt;boolean&gt;</c> text of an <c>ASK</c> result.</summary>
    /// <param name="text">The element text.</param>
    /// <returns>The boolean answer.</returns>
    /// <exception cref="FormatException">The text is neither <c>true</c> nor <c>false</c>.</exception>
    private static bool ParseBoolean(Utf8String text)
    {
        ReadOnlySpan<byte> trimmed = TrimWhitespace(text.Span);
        if(trimmed.SequenceEqual("true"u8))
        {
            return true;
        }

        if(trimmed.SequenceEqual("false"u8))
        {
            return false;
        }

        throw new FormatException("A SPARQL ASK result's <boolean> must be 'true' or 'false'.");
    }

    /// <summary>Trims leading and trailing XML whitespace from a byte span.</summary>
    /// <param name="span">The bytes to trim.</param>
    /// <returns>The trimmed span.</returns>
    private static ReadOnlySpan<byte> TrimWhitespace(ReadOnlySpan<byte> span)
    {
        int start = 0;
        int end = span.Length;
        while(start < end && span[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        while(end > start && span[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        return span.Slice(start, end - start);
    }
}
