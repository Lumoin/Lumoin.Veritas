using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// Converts <see cref="Quad"/> instances to and from JSON.
/// </summary>
/// <remarks>
/// Serializes quads as JSON objects with <c>subject</c>, <c>predicate</c>,
/// <c>object</c>, and optional <c>graph</c> properties. Each term is serialized
/// using <see cref="RdfTermJsonConverter"/>.
/// </remarks>
public sealed class QuadJsonConverter: JsonConverter<Quad>
{
    private const string SubjectProperty = "subject";
    private const string PredicateProperty = "predicate";
    private const string ObjectProperty = "object";
    private const string GraphProperty = "graph";

    /// <inheritdoc/>
    public override Quad? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        ArgumentNullException.ThrowIfNull(options);

        if(reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for Quad.");
        }

        RdfTerm? subject = null;
        RdfTerm? predicate = null;
        RdfTerm? @object = null;
        RdfTerm? graph = null;

        RdfTermJsonConverter termConverter = GetTermConverter(options);

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
                case SubjectProperty:
                {
                    subject = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                case PredicateProperty:
                {
                    predicate = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                case ObjectProperty:
                {
                    @object = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                case GraphProperty:
                {
                    graph = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                default:
                {
                    reader.Skip();
                    break;
                }
            }
        }

        if(subject is null || predicate is null || @object is null)
        {
            throw new JsonException("Quad JSON must have subject, predicate, and object.");
        }

        return new Quad(subject, (NamedNode)predicate, @object, graph);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Quad value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        RdfTermJsonConverter termConverter = GetTermConverter(options);

        writer.WriteStartObject();

        writer.WritePropertyName(SubjectProperty);
        termConverter.Write(writer, value.Subject, options);

        writer.WritePropertyName(PredicateProperty);
        termConverter.Write(writer, value.Predicate, options);

        writer.WritePropertyName(ObjectProperty);
        termConverter.Write(writer, value.Object, options);

        if(value.Graph is { } graph)
        {
            writer.WritePropertyName(GraphProperty);
            termConverter.Write(writer, graph, options);
        }

        writer.WriteEndObject();
    }

    private static RdfTermJsonConverter GetTermConverter(JsonSerializerOptions options)
    {
        foreach(JsonConverter converter in options.Converters)
        {
            if(converter is RdfTermJsonConverter termConverter)
            {
                return termConverter;
            }
        }

        return new RdfTermJsonConverter();
    }
}
