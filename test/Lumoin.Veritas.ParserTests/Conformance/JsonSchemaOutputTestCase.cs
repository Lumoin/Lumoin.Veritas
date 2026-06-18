using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One case from the JSON Schema output-tests suite: validating <see cref="Data"/> against
/// <see cref="Schema"/> in the <see cref="Format"/> output format must produce output that itself
/// validates against <see cref="OutputConstraint"/>.
/// </summary>
/// <param name="File">The suite file (for example <c>type.json</c>).</param>
/// <param name="GroupDescription">The enclosing group's description.</param>
/// <param name="TestDescription">The individual test's description.</param>
/// <param name="Format">The output format name (<c>flag</c>/<c>basic</c>).</param>
/// <param name="Schema">The schema to validate the data against.</param>
/// <param name="Data">The instance.</param>
/// <param name="OutputConstraint">The schema the produced output must satisfy.</param>
internal sealed record JsonSchemaOutputTestCase(
    string File,
    string GroupDescription,
    string TestDescription,
    string Format,
    JsonNode Schema,
    JsonNode Data,
    JsonNode OutputConstraint);
