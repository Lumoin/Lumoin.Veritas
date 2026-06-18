using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One case from the official JSON Schema Test Suite: a (schema, instance) pair with the expected
/// validity, drawn from one test inside one group of one suite file.
/// </summary>
/// <param name="File">The suite file the case came from, relative to the draft folder (for example <c>type.json</c>).</param>
/// <param name="GroupDescription">The enclosing group's description.</param>
/// <param name="TestDescription">The individual test's description.</param>
/// <param name="Schema">The schema node (an object schema or a boolean schema).</param>
/// <param name="Data">The instance node to validate.</param>
/// <param name="ExpectedValid">Whether the suite expects the instance to be valid against the schema.</param>
internal sealed record JsonSchemaTestCase(
    string File,
    string GroupDescription,
    string TestDescription,
    JsonNode Schema,
    JsonNode Data,
    bool ExpectedValid);
