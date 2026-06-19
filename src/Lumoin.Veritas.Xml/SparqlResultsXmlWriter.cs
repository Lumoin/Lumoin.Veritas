using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Xml;

/// <summary>
/// Writes a <see cref="SparqlResultSet"/> in the
/// <see href="https://www.w3.org/TR/rdf-sparql-XMLres/">SPARQL Query Results XML Format</see> (the <c>.srx</c>
/// serialization): the <c>head</c> variables and <c>results</c> bindings of a <c>SELECT</c> result, or the
/// <c>boolean</c> of an <c>ASK</c> result. RDF 1.2 triple-term binding values are emitted as nested
/// <c>&lt;triple&gt;</c> elements. It is the inverse of <see cref="SparqlResultsXmlReader"/>.
/// </summary>
public static class SparqlResultsXmlWriter
{
    private const string ResultsNamespace = "http://www.w3.org/2005/sparql-results#";

    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    private const string ItsNamespace = "http://www.w3.org/2005/11/its";

    /// <summary>Serializes a result set to its <c>.srx</c> document as an owned UTF-8 string.</summary>
    /// <param name="results">The result set to serialize.</param>
    /// <returns>The <c>.srx</c> document as a <see cref="Utf8String"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    public static Utf8String WriteToUtf8String(SparqlResultSet results)
    {
        ArgumentNullException.ThrowIfNull(results);

        using MemoryStream stream = new();
        Write(results, stream);

        return new Utf8String(stream.ToArray());
    }

    /// <summary>
    /// Writes the SPARQL Results XML document to a stream: the internal sink behind <see cref="WriteToUtf8String"/>,
    /// kept private because <see cref="XmlWriter"/> serializes only to a <see cref="Stream"/> (not to an
    /// <see cref="System.Buffers.IBufferWriter{T}"/>), so the <see cref="Stream"/> is the localized framework boundary.
    /// </summary>
    /// <param name="results">The result set to serialize.</param>
    /// <param name="stream">The destination stream.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    private static void Write(SparqlResultSet results, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(stream);

        XmlWriterSettings settings = new()
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        using XmlWriter writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement(SparqlResultsXmlNames.Sparql, ResultsNamespace);

        writer.WriteStartElement(SparqlResultsXmlNames.Head, ResultsNamespace);
        foreach(Utf8String variable in results.Variables)
        {
            writer.WriteStartElement(SparqlResultsXmlNames.Variable, ResultsNamespace);
            writer.WriteAttributeString(SparqlResultsXmlNames.Name, variable.ToString());
            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        if(results.IsBoolean)
        {
            writer.WriteStartElement(SparqlResultsXmlNames.Boolean, ResultsNamespace);
            writer.WriteString(results.Boolean!.Value ? "true" : "false");
            writer.WriteEndElement();
        }
        else
        {
            WriteResults(writer, results.Solutions);
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>Writes the <c>results</c> element with one <c>result</c> per solution.</summary>
    /// <param name="writer">The XML writer.</param>
    /// <param name="solutions">The solution sequence.</param>
    private static void WriteResults(XmlWriter writer, IReadOnlyList<SparqlSolution> solutions)
    {
        writer.WriteStartElement(SparqlResultsXmlNames.Results, ResultsNamespace);
        foreach(SparqlSolution solution in solutions)
        {
            writer.WriteStartElement(SparqlResultsXmlNames.Result, ResultsNamespace);
            foreach(SparqlBinding binding in solution.Bindings)
            {
                writer.WriteStartElement(SparqlResultsXmlNames.Binding, ResultsNamespace);
                writer.WriteAttributeString(SparqlResultsXmlNames.Name, binding.Variable.Name.ToString());
                WriteTerm(writer, binding.Value);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// Writes a binding value, descending into triple terms over an explicit op stack (no recursion). A forward-only
    /// <see cref="XmlWriter"/> needs start/end calls in order, so the stack carries explicit start/end-element ops
    /// around each nested component.
    /// </summary>
    /// <param name="writer">The XML writer.</param>
    /// <param name="root">The term to write.</param>
    private static void WriteTerm(XmlWriter writer, RdfTerm root)
    {
        Stack<XmlOp> work = new();
        work.Push(new XmlOp(XmlOpKind.Term, root, null));
        int depth = 0;

        while(work.Count > 0)
        {
            XmlOp op = work.Pop();
            switch(op.Kind)
            {
                case XmlOpKind.CloseTriple:
                {
                    writer.WriteEndElement();
                    depth--;
                    break;
                }
                case XmlOpKind.End:
                {
                    writer.WriteEndElement();
                    break;
                }
                case XmlOpKind.Start:
                {
                    writer.WriteStartElement(op.Element!, ResultsNamespace);
                    break;
                }
                default:
                {
                    if(op.Term is TripleTerm triple)
                    {
                        depth++;
                        if(depth > QuotedTripleLimits.MaxNestingDepth)
                        {
                            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                        }

                        writer.WriteStartElement(SparqlResultsXmlNames.Triple, ResultsNamespace);

                        //CloseTriple pops last (closes the triple element) and unwinds the depth.
                        work.Push(XmlOp.CloseTriple);
                        PushComponent(work, SparqlResultsXmlNames.Object, triple.Object);
                        PushComponent(work, SparqlResultsXmlNames.Predicate, triple.Predicate);
                        PushComponent(work, SparqlResultsXmlNames.Subject, triple.Subject);
                    }
                    else
                    {
                        WriteLeaf(writer, op.Term!);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>Pushes the ops that write a triple component wrapper (<c>&lt;subject&gt;</c>/<c>&lt;predicate&gt;</c>/<c>&lt;object&gt;</c>) around its term, so they pop in start/term/end order.</summary>
    /// <param name="work">The op stack.</param>
    /// <param name="element">The wrapper element name.</param>
    /// <param name="term">The component term.</param>
    private static void PushComponent(Stack<XmlOp> work, string element, RdfTerm term)
    {
        work.Push(XmlOp.End);
        work.Push(new XmlOp(XmlOpKind.Term, term, null));
        work.Push(new XmlOp(XmlOpKind.Start, null, element));
    }

    /// <summary>Writes a leaf term (an IRI, blank node, or literal) as its complete value element.</summary>
    /// <param name="writer">The XML writer.</param>
    /// <param name="term">The leaf term.</param>
    /// <exception cref="NotSupportedException">The term is not a writable binding value.</exception>
    private static void WriteLeaf(XmlWriter writer, RdfTerm term)
    {
        switch(term)
        {
            case NamedNode named:
            {
                writer.WriteStartElement(SparqlResultsXmlNames.Uri, ResultsNamespace);
                writer.WriteString(named.Iri.ToString());
                writer.WriteEndElement();
                break;
            }
            case BlankNode blank:
            {
                writer.WriteStartElement(SparqlResultsXmlNames.Bnode, ResultsNamespace);
                writer.WriteString(blank.Label.ToString());
                writer.WriteEndElement();
                break;
            }
            case Literal literal:
            {
                writer.WriteStartElement(SparqlResultsXmlNames.Literal, ResultsNamespace);
                if(literal.Language is { } language)
                {
                    writer.WriteAttributeString("xml", "lang", XmlNamespace, language.ToString());

                    //A directional language-tagged string (RDF 1.2) carries its base direction as its:dir (SPARQL 1.2 Results XML).
                    if(literal.BaseDirection is { } direction)
                    {
                        writer.WriteAttributeString("its", "dir", ItsNamespace, TextDirections.ToText(direction));
                    }
                }
                else if(!literal.Datatype.Iri.Equals(Vocabulary.Xsd.String))
                {
                    writer.WriteAttributeString(SparqlResultsXmlNames.Datatype, literal.Datatype.Iri.ToString());
                }

                writer.WriteString(literal.Value.ToString());
                writer.WriteEndElement();
                break;
            }
            default:
            {
                throw new NotSupportedException($"Cannot serialize term '{term.GetType().Name}' as a SPARQL Results XML binding value.");
            }
        }
    }

    /// <summary>The kind of an <see cref="XmlOp"/> on the term-writing stack.</summary>
    private enum XmlOpKind
    {
        /// <summary>Write a term (a leaf element, or open a triple and schedule its components).</summary>
        Term,

        /// <summary>Open a named element.</summary>
        Start,

        /// <summary>Close the current element.</summary>
        End,

        /// <summary>Close a quoted triple's element and unwind one nesting level.</summary>
        CloseTriple
    }

    /// <summary>One operation on the explicit term-writing stack.</summary>
    /// <param name="Kind">Whether to write a term, open an element, or close one.</param>
    /// <param name="Term">The term to write (for <see cref="XmlOpKind.Term"/>).</param>
    /// <param name="Element">The element name to open (for <see cref="XmlOpKind.Start"/>).</param>
    private readonly record struct XmlOp(XmlOpKind Kind, RdfTerm? Term, string? Element)
    {
        /// <summary>The payload-less op that closes the current element.</summary>
        public static XmlOp End { get; } = new(XmlOpKind.End, null, null);

        /// <summary>The payload-less op that closes a quoted triple's element and unwinds one nesting level.</summary>
        public static XmlOp CloseTriple { get; } = new(XmlOpKind.CloseTriple, null, null);
    }
}
