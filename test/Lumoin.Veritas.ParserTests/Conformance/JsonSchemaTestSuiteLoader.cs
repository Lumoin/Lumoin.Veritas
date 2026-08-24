using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Loads the vendored JSON Schema Test Suite (draft 2020-12) into <see cref="JsonSchemaTestCase"/> rows.
/// Each suite file is a JSON array of groups; each group carries a <c>schema</c> and a list of
/// <c>tests</c>, and each test carries <c>data</c> and the expected <c>valid</c> flag.
/// </summary>
internal static class JsonSchemaTestSuiteLoader
{
    /// <summary>Loads the top-level draft 2020-12 suite files (excluding the <c>optional/</c> subset).</summary>
    /// <returns>One case per (group, test) pair across every suite file, in file then document order.</returns>
    public static IReadOnlyList<JsonSchemaTestCase> LoadRequired()
    {
        string suiteDirectory = Path.Combine(W3cCorpusPath.LibraryDirectory("JsonSchema"), "tests", "draft2020-12");
        List<JsonSchemaTestCase> cases = [];

        string[] files = Directory.GetFiles(suiteDirectory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        foreach(string file in files)
        {
            LoadFile(file, Path.GetFileName(file), cases);
        }

        return cases;
    }

    /// <summary>Parses one suite file and appends its cases.</summary>
    /// <param name="path">The absolute file path.</param>
    /// <param name="relativeName">The file name used to label cases.</param>
    /// <param name="cases">The list to populate.</param>
    private static void LoadFile(string path, string relativeName, List<JsonSchemaTestCase> cases)
    {
        JsonNode root = StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(path)));
        if(root.Kind != JsonNodeKind.Array)
        {
            return;
        }

        foreach(JsonNode group in root.EnumerateArray())
        {
            if(group.Kind != JsonNodeKind.Object || !group.TryGetProperty("schema", out JsonNode schema) || !group.TryGetProperty("tests", out JsonNode tests))
            {
                continue;
            }

            string groupDescription = group.TryGetProperty("description", out JsonNode groupName) && groupName.Kind == JsonNodeKind.String ? groupName.GetString() : string.Empty;
            foreach(JsonNode test in tests.EnumerateArray())
            {
                if(test.Kind != JsonNodeKind.Object || !test.TryGetProperty("data", out JsonNode data) || !test.TryGetProperty("valid", out JsonNode valid))
                {
                    continue;
                }

                string testDescription = test.TryGetProperty("description", out JsonNode testName) && testName.Kind == JsonNodeKind.String ? testName.GetString() : string.Empty;
                cases.Add(new JsonSchemaTestCase(relativeName, groupDescription, testDescription, schema, data, valid.Kind == JsonNodeKind.True));
            }
        }
    }
}
