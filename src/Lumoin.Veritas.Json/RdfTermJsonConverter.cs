using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Json;

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
    public override void Write(Utf8JsonWriter writer, RdfTerm value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        writer.WriteStartObject();

        switch(value)
        {
            case NamedNode(var iri):
            {
                writer.WriteString(TypeProperty, TypeUri);
                writer.WriteString(ValueProperty, iri.ToString());
                break;
            }
            case BlankNode(var label):
            {
                writer.WriteString(TypeProperty, TypeBnode);
                writer.WriteString(ValueProperty, label.ToString());
                break;
            }
            case Literal(var lexical, var dt, var lang, var dir):
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
            case TripleTerm(var s, var p, var o):
            {
                writer.WriteString(TypeProperty, TypeTriple);
                writer.WritePropertyName(SubjectProperty);
                Write(writer, s, options);
                writer.WritePropertyName(PredicateProperty);
                Write(writer, p, options);
                writer.WritePropertyName(ObjectProperty);
                Write(writer, o, options);
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
}
