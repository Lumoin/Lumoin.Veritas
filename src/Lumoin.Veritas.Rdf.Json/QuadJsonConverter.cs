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
    /// <summary>The <c>subject</c> property name.</summary>
    private static ReadOnlySpan<byte> SubjectProperty => "subject"u8;

    /// <summary>The <c>predicate</c> property name.</summary>
    private static ReadOnlySpan<byte> PredicateProperty => "predicate"u8;

    /// <summary>The <c>object</c> property name.</summary>
    private static ReadOnlySpan<byte> ObjectProperty => "object"u8;

    /// <summary>The <c>graph</c> property name.</summary>
    private static ReadOnlySpan<byte> GraphProperty => "graph"u8;

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

            QuadProperty property = ClassifyProperty(ref reader);
            reader.Read();

            switch(property)
            {
                case(QuadProperty.Subject):
                {
                    subject = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                case(QuadProperty.Predicate):
                {
                    predicate = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                case(QuadProperty.Object):
                {
                    @object = termConverter.Read(ref reader, typeof(RdfTerm), options);
                    break;
                }
                case(QuadProperty.Graph):
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

    /// <summary>Classifies the current property-name token against the known names without materialising a string.</summary>
    /// <param name="reader">The reader positioned on a property-name token.</param>
    /// <returns>The matched property, or <see cref="QuadProperty.Unknown"/>.</returns>
    private static QuadProperty ClassifyProperty(ref Utf8JsonReader reader)
    {
        if(reader.ValueTextEquals(SubjectProperty))
        {
            return QuadProperty.Subject;
        }

        if(reader.ValueTextEquals(PredicateProperty))
        {
            return QuadProperty.Predicate;
        }

        if(reader.ValueTextEquals(ObjectProperty))
        {
            return QuadProperty.Object;
        }

        if(reader.ValueTextEquals(GraphProperty))
        {
            return QuadProperty.Graph;
        }

        return QuadProperty.Unknown;
    }

    /// <summary>A quad object's property, classified from its UTF-8 name.</summary>
    private enum QuadProperty
    {
        /// <summary>An unrecognised property, skipped.</summary>
        Unknown,

        /// <summary>The <c>subject</c> term.</summary>
        Subject,

        /// <summary>The <c>predicate</c> term.</summary>
        Predicate,

        /// <summary>The <c>object</c> term.</summary>
        Object,

        /// <summary>The <c>graph</c> term.</summary>
        Graph
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
