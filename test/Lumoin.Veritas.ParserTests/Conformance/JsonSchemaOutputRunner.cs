using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.JsonSchema;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Runs one JSON Schema output-tests case: produces the validation output in the requested format,
/// serializes it to JSON, and checks that it validates against the test's output-constraint schema.
/// </summary>
internal static class JsonSchemaOutputRunner
{
    /// <summary>Runs one output-test case to an outcome.</summary>
    /// <param name="testCase">The case.</param>
    /// <returns>The outcome.</returns>
    public static W3cOutcome Run(JsonSchemaOutputTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!TryMapFormat(testCase.Format, out OutputFormat format))
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Output format '{testCase.Format}' is not produced yet.");
        }

        OutputUnit output;
        try
        {
            output = JsonSchemaValidator.Evaluate(testCase.Schema, testCase.Data, format, JsonSchemaConformanceRunner.LoadRemote);
        }
        catch(Exception exception)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Producing {testCase.Format} output threw: {exception.Message}");
        }

        JsonNode outputNode = StjJsonAdapter.Parse(Serialize(output));
        ValidationResult conformance = JsonSchemaValidator.Validate(testCase.OutputConstraint, outputNode, JsonSchemaConformanceRunner.LoadRemote);

        return conformance.IsValid
            ? new W3cOutcome(W3cOutcomeStatus.Passed, $"{testCase.Format} output conforms.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"{testCase.Format} output does not satisfy the expected output schema ({conformance.Errors.Count} error(s)).");
    }

    /// <summary>Maps a suite format name to the produced <see cref="OutputFormat"/>.</summary>
    /// <param name="name">The format name.</param>
    /// <param name="format">On success, the format.</param>
    /// <returns><see langword="true"/> when the format is produced.</returns>
    private static bool TryMapFormat(string name, out OutputFormat format)
    {
        switch(name)
        {
            case "flag":
            {
                format = OutputFormat.Flag;

                return true;
            }
            case "basic":
            {
                format = OutputFormat.Basic;

                return true;
            }
            case "detailed":
            {
                format = OutputFormat.Detailed;

                return true;
            }
            case "verbose":
            {
                format = OutputFormat.Verbose;

                return true;
            }
            default:
            {
                format = OutputFormat.Flag;

                return false;
            }
        }
    }

    /// <summary>Serializes an output unit (and its nested units, for Detailed/Verbose) to canonical JSON bytes.</summary>
    /// <param name="output">The root output unit.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static Utf8String Serialize(OutputUnit output)
    {
        ArgumentNullException.ThrowIfNull(output);

        ArrayBufferWriter<byte> buffer = new();
        using(Utf8JsonWriter writer = new(buffer))
        {
            WriteUnit(writer, output);
        }

        return new Utf8String(buffer.WrittenSpan.ToArray());
    }

    /// <summary>Writes one output unit, recursing into its nested error and annotation units.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="unit">The unit.</param>
    private static void WriteUnit(Utf8JsonWriter writer, OutputUnit unit)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("valid", unit.Valid);

        if(unit.KeywordLocation is not null)
        {
            writer.WriteString("keywordLocation", unit.KeywordLocation);
        }

        if(unit.AbsoluteKeywordLocation is not null)
        {
            writer.WriteString("absoluteKeywordLocation", unit.AbsoluteKeywordLocation);
        }

        if(unit.InstanceLocation is not null)
        {
            writer.WriteString("instanceLocation", unit.InstanceLocation);
        }

        if(unit.Error is not null)
        {
            writer.WriteString("error", unit.Error);
        }

        if(unit.Annotation is { } annotation)
        {
            writer.WritePropertyName("annotation");
            WriteJsonNode(writer, annotation);
        }

        WriteUnitArray(writer, "errors", unit.Errors);
        WriteUnitArray(writer, "annotations", unit.Annotations);

        writer.WriteEndObject();
    }

    /// <summary>Writes a named array of child output units, if present.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The array property name.</param>
    /// <param name="units">The units, or <see langword="null"/>.</param>
    private static void WriteUnitArray(Utf8JsonWriter writer, string name, IReadOnlyList<OutputUnit>? units)
    {
        if(units is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach(OutputUnit unit in units)
        {
            WriteUnit(writer, unit);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes a <see cref="JsonNode"/> value (an annotation's value) to the JSON writer.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="node">The node.</param>
    private static void WriteJsonNode(Utf8JsonWriter writer, JsonNode node)
    {
        switch(node.Kind)
        {
            case JsonNodeKind.Null:
            {
                writer.WriteNullValue();
                break;
            }
            case JsonNodeKind.True:
            {
                writer.WriteBooleanValue(true);
                break;
            }
            case JsonNodeKind.False:
            {
                writer.WriteBooleanValue(false);
                break;
            }
            case JsonNodeKind.String:
            {
                writer.WriteStringValue(node.GetString());
                break;
            }
            case JsonNodeKind.Number:
            {
                writer.WriteRawValue(node.GetRawNumber());
                break;
            }
            case JsonNodeKind.Array:
            {
                writer.WriteStartArray();
                foreach(JsonNode element in node.EnumerateArray())
                {
                    WriteJsonNode(writer, element);
                }

                writer.WriteEndArray();
                break;
            }
            case JsonNodeKind.Object:
            {
                writer.WriteStartObject();
                foreach(KeyValuePair<string, JsonNode> member in node.EnumerateObject())
                {
                    writer.WritePropertyName(member.Key);
                    WriteJsonNode(writer, member.Value);
                }

                writer.WriteEndObject();
                break;
            }
            default:
            {
                writer.WriteNullValue();
                break;
            }
        }
    }
}
