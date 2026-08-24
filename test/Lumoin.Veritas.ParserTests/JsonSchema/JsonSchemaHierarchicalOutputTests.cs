using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.JsonSchema;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.JsonSchema;

/// <summary>
/// Exercises the hierarchical JSON Schema output formats (<see cref="OutputFormat.Detailed"/> and
/// <see cref="OutputFormat.Verbose"/>). The official output-tests corpus carries only <c>basic</c>
/// cases, so the oracle here is the canonical output meta-schema
/// (<c>output-tests/output-schema.json</c>): every produced structure must validate against it, plus a
/// few structural checks on the tree shape and the Detailed-vs-Verbose pruning.
/// </summary>
[TestClass]
internal sealed class JsonSchemaHierarchicalOutputTests
{
    private static JsonNode OutputMetaSchema { get; } = StjJsonAdapter.Parse(
        new Utf8String(File.ReadAllBytes(Path.Combine(W3cCorpusPath.LibraryDirectory("JsonSchema"), "output-tests", "output-schema.json"))));

    private const string ObjectSchema = """
        {
          "type": "object",
          "title": "person",
          "properties": {
            "name": { "type": "string", "minLength": 2 },
            "age": { "type": "integer", "minimum": 0 }
          },
          "required": ["name"]
        }
        """;

    /// <summary>A passing instance produces a Verbose structure that conforms to the output meta-schema and is reported valid.</summary>
    [TestMethod]
    public void VerboseOutputForValidInstanceConformsToMetaSchema()
    {
        OutputUnit output = Evaluate(ObjectSchema, """{ "name": "Ada", "age": 36 }""", OutputFormat.Verbose);

        Assert.IsTrue(output.Valid);
        Assert.AreEqual(string.Empty, output.KeywordLocation);
        Assert.AreEqual(string.Empty, output.InstanceLocation);
        AssertConformsToMetaSchema(output);
    }

    /// <summary>A failing instance produces a Verbose structure that conforms to the output meta-schema, is reported invalid, and carries nested errors.</summary>
    [TestMethod]
    public void VerboseOutputForInvalidInstanceConformsToMetaSchema()
    {
        OutputUnit output = Evaluate(ObjectSchema, """{ "name": "A", "age": -1 }""", OutputFormat.Verbose);

        Assert.IsFalse(output.Valid);
        Assert.IsNotNull(output.Errors);
        AssertConformsToMetaSchema(output);
    }

    /// <summary>A failing instance produces a Detailed structure that conforms to the output meta-schema and is reported invalid.</summary>
    [TestMethod]
    public void DetailedOutputForInvalidInstanceConformsToMetaSchema()
    {
        OutputUnit output = Evaluate(ObjectSchema, """{ "age": -1 }""", OutputFormat.Detailed);

        Assert.IsFalse(output.Valid);
        Assert.IsNotNull(output.Errors);
        AssertConformsToMetaSchema(output);
    }

    /// <summary>For a valid instance, Detailed prunes the valid, information-free nodes that Verbose retains, so Detailed is no larger than Verbose.</summary>
    [TestMethod]
    public void DetailedPrunesValidNodesVerboseRetains()
    {
        OutputUnit verbose = Evaluate(ObjectSchema, """{ "name": "Ada", "age": 36 }""", OutputFormat.Verbose);
        OutputUnit detailed = Evaluate(ObjectSchema, """{ "name": "Ada", "age": 36 }""", OutputFormat.Detailed);

        Assert.IsTrue(verbose.Valid);
        Assert.IsTrue(detailed.Valid);
        Assert.IsLessThanOrEqualTo(CountUnits(verbose), CountUnits(detailed));
        AssertConformsToMetaSchema(detailed);
    }

    /// <summary>Validates a produced output structure against the canonical output meta-schema.</summary>
    /// <param name="output">The output unit.</param>
    private static void AssertConformsToMetaSchema(OutputUnit output)
    {
        JsonNode outputNode = StjJsonAdapter.Parse(JsonSchemaOutputRunner.Serialize(output));
        ValidationResult result = JsonSchemaValidator.Validate(OutputMetaSchema, outputNode, JsonSchemaConformanceRunner.LoadRemote);

        Assert.IsTrue(result.IsValid, $"Output did not satisfy the meta-schema: {(result.Errors.Count > 0 ? result.Errors[0].Message : string.Empty)}");
    }

    /// <summary>Evaluates an instance against a schema (both given as JSON text) into the requested output format.</summary>
    /// <param name="schemaJson">The schema JSON.</param>
    /// <param name="instanceJson">The instance JSON.</param>
    /// <param name="format">The output format.</param>
    /// <returns>The root output unit.</returns>
    private static OutputUnit Evaluate(string schemaJson, string instanceJson, OutputFormat format)
    {
        JsonNode schema = StjJsonAdapter.Parse(Utf8Strings.From(schemaJson));
        JsonNode instance = StjJsonAdapter.Parse(Utf8Strings.From(instanceJson));

        return JsonSchemaValidator.Evaluate(schema, instance, format);
    }

    /// <summary>Counts the units in an output tree (the node plus its nested error and annotation units).</summary>
    /// <param name="unit">The root unit.</param>
    /// <returns>The total node count.</returns>
    private static int CountUnits(OutputUnit unit)
    {
        int count = 1;
        if(unit.Errors is not null)
        {
            foreach(OutputUnit child in unit.Errors)
            {
                count += CountUnits(child);
            }
        }

        if(unit.Annotations is not null)
        {
            foreach(OutputUnit child in unit.Annotations)
            {
                count += CountUnits(child);
            }
        }

        return count;
    }
}
