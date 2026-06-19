using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// Converts <see cref="RdfTerm"/> and its subtypes to and from JSON.
/// </summary>
/// <remarks>
/// <para>
/// Serializes the RDF term hierarchy using a type discriminator following the
/// conventions of the SPARQL JSON results format: <c>"type"</c> is one of
/// <c>"uri"</c>, <c>"bnode"</c>, <c>"literal"</c>, or <c>"triple"</c>.
/// </para>
/// <para>
/// This converter handles the polymorphic dispatch that <see cref="RdfTerm"/>
/// requires. Domain types remain clean POCOs; all JSON structure decisions
/// are encapsulated here.
/// </para>
/// </remarks>
public sealed class RdfTermJsonConverter: JsonConverter<RdfTerm>
{
    private const string TypeProperty = "type";
    private const string ValueProperty = "value";
    private const string DatatypeProperty = "datatype";
    private const string LanguageProperty = "language";
    private const string DirectionProperty = "direction";
    private const string SubjectProperty = "subject";
    private const string PredicateProperty = "predicate";
    private const string ObjectProperty = "object";

    private const string TypeUri = "uri";
    private const string TypeBnode = "bnode";
    private const string TypeLiteral = "literal";
    private const string TypeTriple = "triple";

    /// <inheritdoc/>
    public override RdfTerm? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        ArgumentNullException.ThrowIfNull(options);

        if(reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for RdfTerm.");
        }

        string? type = null;
        string? value = null;
        string? datatype = null;
        string? language = null;
        string? direction = null;
        RdfTerm? subject = null;
        RdfTerm? predicate = null;
        RdfTerm? @object = null;

        while(reader.Read())
        {
            if(reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if(reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name.");
            }

            string propertyName = reader.GetString()!;
            reader.Read();

            switch(propertyName)
            {
                case TypeProperty:
                {
                    type = reader.GetString();
                    break;
                }
                case ValueProperty:
                {
                    value = reader.GetString();
                    break;
                }
                case DatatypeProperty:
                {
                    datatype = reader.GetString();
                    break;
                }
                case LanguageProperty:
                {
                    language = reader.GetString();
                    break;
                }
                case DirectionProperty:
                {
                    direction = reader.GetString();
                    break;
                }
                case SubjectProperty:
                {
                    subject = Read(ref reader, typeToConvert, options);
                    break;
                }
                case PredicateProperty:
                {
                    predicate = Read(ref reader, typeToConvert, options);
                    break;
                }
                case ObjectProperty:
                {
                    @object = Read(ref reader, typeToConvert, options);
                    break;
                }
                default:
                {
                    reader.Skip();
                    break;
                }
            }
        }

        return type switch
        {
            TypeUri => new NamedNode(Utf8String.FromString(value!)),
            TypeBnode => new BlankNode(Utf8String.FromString(value!)),
            TypeLiteral => CreateLiteral(value!, datatype, language, direction),
            TypeTriple => new TripleTerm(subject!, (NamedNode)predicate!, @object!),
            _ => throw new JsonException($"Unknown RDF term type: '{type}'.")
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A quoted triple is walked over an explicit work-stack (no recursion); a leaf is written directly on the
    /// fast path. Nesting beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/> raises
    /// <see cref="TripleTermDepthLimitException"/> rather than overflowing the call stack or the JSON writer.
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, RdfTerm value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        if(value is not TripleTerm root)
        {
            WriteLeaf(writer, value);

            return;
        }

        Stack<TermStep> work = new();
        work.Push(new TermStep(JsonStepKind.Term, root));
        int depth = 0;

        while(work.Count > 0)
        {
            TermStep step = work.Pop();
            switch(step.Kind)
            {
                case(JsonStepKind.Term):
                {
                    if(step.Term is TripleTerm triple)
                    {
                        depth++;
                        if(depth > QuotedTripleLimits.MaxNestingDepth)
                        {
                            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                        }

                        //Push in reverse so they pop in emission order: { type, subject, predicate, object }.
                        work.Push(new TermStep(JsonStepKind.EndObject, null));
                        work.Push(new TermStep(JsonStepKind.Term, triple.Object));
                        work.Push(new TermStep(JsonStepKind.ObjectName, null));
                        work.Push(new TermStep(JsonStepKind.Term, triple.Predicate));
                        work.Push(new TermStep(JsonStepKind.PredicateName, null));
                        work.Push(new TermStep(JsonStepKind.Term, triple.Subject));
                        work.Push(new TermStep(JsonStepKind.SubjectName, null));
                        work.Push(new TermStep(JsonStepKind.StartTripleObject, null));
                    }
                    else
                    {
                        WriteLeaf(writer, step.Term!);
                    }

                    break;
                }
                case(JsonStepKind.StartTripleObject):
                {
                    writer.WriteStartObject();
                    writer.WriteString(TypeProperty, TypeTriple);
                    break;
                }
                case(JsonStepKind.SubjectName):
                {
                    writer.WritePropertyName(SubjectProperty);
                    break;
                }
                case(JsonStepKind.PredicateName):
                {
                    writer.WritePropertyName(PredicateProperty);
                    break;
                }
                case(JsonStepKind.ObjectName):
                {
                    writer.WritePropertyName(ObjectProperty);
                    break;
                }
                default:
                {
                    //JsonStepKind.EndObject: close the triple object and unwind one nesting level.
                    writer.WriteEndObject();
                    depth--;
                    break;
                }
            }
        }
    }

    /// <summary>Writes a leaf term (named node, blank node, or literal) as its complete JSON object.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="term">The leaf term.</param>
    private static void WriteLeaf(Utf8JsonWriter writer, RdfTerm term)
    {
        writer.WriteStartObject();

        switch(term)
        {
            case(NamedNode(var iri)):
            {
                writer.WriteString(TypeProperty, TypeUri);
                writer.WriteString(ValueProperty, iri.ToString());
                break;
            }
            case(BlankNode(var label)):
            {
                writer.WriteString(TypeProperty, TypeBnode);
                writer.WriteString(ValueProperty, label.ToString());
                break;
            }
            case(Literal(var lexical, var dt, var lang, var dir)):
            {
                writer.WriteString(TypeProperty, TypeLiteral);
                writer.WriteString(ValueProperty, lexical.ToString());
                writer.WriteString(DatatypeProperty, dt.Iri.ToString());
                if(lang is { } l)
                {
                    writer.WriteString(LanguageProperty, l.ToString());
                }

                if(dir is { } d)
                {
                    writer.WriteString(DirectionProperty, TextDirections.ToText(d));
                }

                break;
            }
            default:
            {
                //An unknown term kind writes a bare object, preserving the original converter's behaviour.
                break;
            }
        }

        writer.WriteEndObject();
    }

    private static Literal CreateLiteral(string value, string? datatype, string? language, string? direction)
    {
        TextDirection? dir = TextDirections.TryParse(direction, out TextDirection parsed) ? parsed : null;

        Utf8String lexical = Utf8String.FromString(value);
        NamedNode dt = new(Utf8String.FromString(datatype ?? "http://www.w3.org/2001/XMLSchema#string"));

        Utf8String? lang = language is not null
            ? Utf8String.FromString(language)
            : null;

        return new Literal(lexical, dt, lang, dir);
    }

    /// <summary>The kind of step on the quoted-triple serialization work-stack.</summary>
    private enum JsonStepKind
    {
        /// <summary>Write a term: a leaf object directly, or expand a quoted triple into its component steps.</summary>
        Term,

        /// <summary>Open a quoted triple's object and write its <c>type</c> discriminator.</summary>
        StartTripleObject,

        /// <summary>Write the <c>subject</c> property name.</summary>
        SubjectName,

        /// <summary>Write the <c>predicate</c> property name.</summary>
        PredicateName,

        /// <summary>Write the <c>object</c> property name.</summary>
        ObjectName,

        /// <summary>Close a quoted triple's object and unwind one nesting level.</summary>
        EndObject
    }

    /// <summary>One step on the quoted-triple serialization work-stack.</summary>
    /// <param name="Kind">The step kind.</param>
    /// <param name="Term">The term to write; set only for a <c>Term</c> step.</param>
    private readonly record struct TermStep(JsonStepKind Kind, RdfTerm? Term);
}
