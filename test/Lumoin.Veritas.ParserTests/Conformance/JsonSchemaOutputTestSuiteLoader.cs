using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Loads the JSON Schema output-tests suite (draft 2020-12) into <see cref="JsonSchemaOutputTestCase"/>
/// rows — one per (test, output-format) pair across the vendored <c>output-tests/content/</c> files.
/// </summary>
internal static class JsonSchemaOutputTestSuiteLoader
{
    /// <summary>Loads every output-test case.</summary>
    /// <returns>One case per (group, test, format) the suite declares.</returns>
    public static IReadOnlyList<JsonSchemaOutputTestCase> Load()
    {
        string directory = Path.Combine(W3cCorpusPath.LibraryDirectory("JsonSchema"), "output-tests", "content");
        List<JsonSchemaOutputTestCase> cases = [];

        string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        foreach(string file in files)
        {
            JsonNode root = StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(file)));
            if(root.Kind != JsonNodeKind.Array)
            {
                continue;
            }

            string fileName = Path.GetFileName(file);
            foreach(JsonNode group in root.EnumerateArray())
            {
                LoadGroup(group, fileName, cases);
            }
        }

        return cases;
    }

    /// <summary>Appends the cases of one group.</summary>
    /// <param name="group">The group node.</param>
    /// <param name="fileName">The suite file name.</param>
    /// <param name="cases">The list to populate.</param>
    private static void LoadGroup(JsonNode group, string fileName, List<JsonSchemaOutputTestCase> cases)
    {
        if(group.Kind != JsonNodeKind.Object || !group.TryGetProperty("schema", out JsonNode schema) || !group.TryGetProperty("tests", out JsonNode tests))
        {
            return;
        }

        string groupDescription = group.TryGetProperty("description", out JsonNode groupName) && groupName.Kind == JsonNodeKind.String ? groupName.GetString() : string.Empty;
        foreach(JsonNode test in tests.EnumerateArray())
        {
            if(test.Kind != JsonNodeKind.Object || !test.TryGetProperty("data", out JsonNode data) || !test.TryGetProperty("output", out JsonNode output) || output.Kind != JsonNodeKind.Object)
            {
                continue;
            }

            string testDescription = test.TryGetProperty("description", out JsonNode testName) && testName.Kind == JsonNodeKind.String ? testName.GetString() : string.Empty;
            foreach(KeyValuePair<string, JsonNode> format in output.EnumerateObject())
            {
                cases.Add(new JsonSchemaOutputTestCase(fileName, groupDescription, testDescription, format.Key, schema, data, format.Value));
            }
        }
    }
}
