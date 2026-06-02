using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// Writes a <see cref="SparqlResultSet"/> in the
/// <see href="https://www.w3.org/TR/sparql11-results-json/">SPARQL 1.1 Query Results JSON Format</see> (the
/// <c>.srj</c> serialization): <c>head.vars</c> + <c>results.bindings</c> of a <c>SELECT</c> result, or the
/// <c>boolean</c> of an <c>ASK</c> result. RDF 1.2 triple-term binding values are emitted as nested
/// <c>"type":"triple"</c> objects. It is the inverse of <see cref="SparqlResultsJsonReader"/>.
/// </summary>
/// <remarks>
/// Values are written as UTF-8 directly from each term's <see cref="Utf8String.Span"/> (no string round-trip);
/// <see cref="Utf8JsonWriter"/> escapes them. The writer is reflection-free and AOT-compatible.
/// </remarks>
public static class SparqlResultsJsonWriter
{
    /// <summary>Serializes a result set to its <c>.srj</c> document as an owned UTF-8 string.</summary>
    /// <param name="results">The result set to serialize.</param>
    /// <param name="indented">Whether to pretty-print the JSON.</param>
    /// <returns>The <c>.srj</c> document as a <see cref="Utf8String"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    public static Utf8String WriteToUtf8String(SparqlResultSet results, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(results);

        using MemoryStream stream = new();
        Write(results, stream, indented);

        return new Utf8String(stream.ToArray());
    }

    /// <summary>Writes a result set as SPARQL Results JSON to a stream.</summary>
    /// <param name="results">The result set to serialize.</param>
    /// <param name="stream">The destination stream.</param>
    /// <param name="indented">Whether to pretty-print the JSON.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void Write(SparqlResultSet results, Stream stream, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(stream);

        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = indented });
        writer.WriteStartObject();

        writer.WritePropertyName(SparqlResultsJsonNames.Head);
        writer.WriteStartObject();
        if(!results.IsBoolean)
        {
            writer.WritePropertyName(SparqlResultsJsonNames.Vars);
            writer.WriteStartArray();
            foreach(Utf8String variable in results.Variables)
            {
                writer.WriteStringValue(variable.Span);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();

        if(results.IsBoolean)
        {
            writer.WriteBoolean(SparqlResultsJsonNames.Boolean, results.Boolean!.Value);
        }
        else
        {
            WriteResults(writer, results.Solutions);
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Writes the <c>results.bindings</c> array with one binding-set object per solution.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="solutions">The solution sequence.</param>
    private static void WriteResults(Utf8JsonWriter writer, IReadOnlyList<SparqlSolution> solutions)
    {
        writer.WritePropertyName(SparqlResultsJsonNames.Results);
        writer.WriteStartObject();
        writer.WritePropertyName(SparqlResultsJsonNames.Bindings);
        writer.WriteStartArray();
        foreach(SparqlSolution solution in solutions)
        {
            writer.WriteStartObject();
            foreach(SparqlBinding binding in solution.Bindings)
            {
                writer.WritePropertyName(binding.Variable.Name.Span);
                WriteTerm(writer, binding.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a binding value object, descending into triple terms over an explicit op stack (no recursion). The
    /// forward-only writer needs ordered calls, so the stack carries explicit end-object and property-name ops
    /// around each nested component.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="root">The term to write.</param>
    private static void WriteTerm(Utf8JsonWriter writer, RdfTerm root)
    {
        Stack<JsonOp> work = new();
        work.Push(new JsonOp(JsonOpKind.Term, root, null));

        while(work.Count > 0)
        {
            JsonOp op = work.Pop();
            switch(op.Kind)
            {
                case JsonOpKind.EndObject:
                {
                    writer.WriteEndObject();
                    break;
                }
                case JsonOpKind.PropertyName:
                {
                    writer.WritePropertyName(op.Property!);
                    break;
                }
                default:
                {
                    if(op.Term is TripleTerm triple)
                    {
                        writer.WriteStartObject();
                        writer.WriteString(SparqlResultsJsonNames.TypeUtf8, SparqlResultsJsonNames.TripleUtf8);
                        writer.WritePropertyName(SparqlResultsJsonNames.ValueUtf8);
                        writer.WriteStartObject();
                        work.Push(new JsonOp(JsonOpKind.EndObject, null, null));
                        work.Push(new JsonOp(JsonOpKind.EndObject, null, null));
                        PushComponent(work, SparqlResultsJsonNames.Object, triple.Object);
                        PushComponent(work, SparqlResultsJsonNames.Predicate, triple.Predicate);
                        PushComponent(work, SparqlResultsJsonNames.Subject, triple.Subject);
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

    /// <summary>Pushes the ops that write a triple component (its property name then its term), so they pop in name/term order.</summary>
    /// <param name="work">The op stack.</param>
    /// <param name="property">The component property name (<c>subject</c>/<c>predicate</c>/<c>object</c>).</param>
    /// <param name="term">The component term.</param>
    private static void PushComponent(Stack<JsonOp> work, string property, RdfTerm term)
    {
        work.Push(new JsonOp(JsonOpKind.Term, term, null));
        work.Push(new JsonOp(JsonOpKind.PropertyName, null, property));
    }

    /// <summary>Writes a leaf term (an IRI, blank node, or literal) as its complete value object.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="term">The leaf term.</param>
    /// <exception cref="NotSupportedException">The term is not a writable binding value.</exception>
    private static void WriteLeaf(Utf8JsonWriter writer, RdfTerm term)
    {
        writer.WriteStartObject();
        switch(term)
        {
            case NamedNode named:
            {
                writer.WriteString(SparqlResultsJsonNames.TypeUtf8, SparqlResultsJsonNames.UriUtf8);
                writer.WriteString(SparqlResultsJsonNames.ValueUtf8, named.Iri.Span);
                break;
            }
            case BlankNode blank:
            {
                writer.WriteString(SparqlResultsJsonNames.TypeUtf8, SparqlResultsJsonNames.BnodeUtf8);
                writer.WriteString(SparqlResultsJsonNames.ValueUtf8, blank.Label.Span);
                break;
            }
            case Literal literal:
            {
                writer.WriteString(SparqlResultsJsonNames.TypeUtf8, SparqlResultsJsonNames.LiteralUtf8);
                writer.WriteString(SparqlResultsJsonNames.ValueUtf8, literal.Value.Span);
                if(literal.Language is { } language)
                {
                    writer.WriteString(SparqlResultsJsonNames.LanguageUtf8, language.Span);

                    if(literal.BaseDirection is { } direction)
                    {
                        writer.WriteString(SparqlResultsJsonNames.DirectionUtf8, TextDirections.ToToken(direction).Span);
                    }
                }
                else if(!literal.Datatype.Iri.Equals(Vocabulary.Xsd.String))
                {
                    writer.WriteString(SparqlResultsJsonNames.DatatypeUtf8, literal.Datatype.Iri.Span);
                }

                break;
            }
            default:
            {
                throw new NotSupportedException($"Cannot serialize term '{term.GetType().Name}' as a SPARQL Results JSON binding value.");
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>The kind of a <see cref="JsonOp"/> on the term-writing stack.</summary>
    private enum JsonOpKind
    {
        /// <summary>Write a term (a leaf object, or open a triple and schedule its components).</summary>
        Term,

        /// <summary>Write a property name.</summary>
        PropertyName,

        /// <summary>Close the current object.</summary>
        EndObject
    }

    /// <summary>One operation on the explicit term-writing stack.</summary>
    /// <param name="Kind">Whether to write a term, a property name, or close an object.</param>
    /// <param name="Term">The term to write (for <see cref="JsonOpKind.Term"/>).</param>
    /// <param name="Property">The property name to write (for <see cref="JsonOpKind.PropertyName"/>).</param>
    private readonly record struct JsonOp(JsonOpKind Kind, RdfTerm? Term, string? Property);
}
