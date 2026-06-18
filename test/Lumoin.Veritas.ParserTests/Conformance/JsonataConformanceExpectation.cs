using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// The expected outcome of one JSONata conformance case: a small closed discriminator over a concrete
/// result value, the undefined value, or an error code.
/// </summary>
/// <remarks>
/// Exactly one of the three shapes is meaningful per <see cref="Kind"/>: <see cref="ResultNode"/> carries
/// the parsed expected-result JSON for <see cref="JsonataConformanceOutcomeKind.Result"/>;
/// <see cref="ErrorCode"/> and <see cref="ErrorToken"/> carry the error identity for
/// <see cref="JsonataConformanceOutcomeKind.Error"/>; <see cref="JsonataConformanceOutcomeKind.Undefined"/>
/// carries no payload.
/// </remarks>
/// <param name="Kind">The discriminating outcome kind.</param>
/// <param name="ResultNode">The expected-result JSON node for a <see cref="JsonataConformanceOutcomeKind.Result"/> case; otherwise a default node.</param>
/// <param name="ErrorCode">The expected error code for a <see cref="JsonataConformanceOutcomeKind.Error"/> case; otherwise <see langword="null"/>.</param>
/// <param name="ErrorToken">The optional expected error token for a <see cref="JsonataConformanceOutcomeKind.Error"/> case; otherwise <see langword="null"/>.</param>
internal readonly record struct JsonataConformanceExpectation(
    JsonataConformanceOutcomeKind Kind,
    JsonNode ResultNode,
    string? ErrorCode,
    string? ErrorToken)
{
    /// <summary>Builds an expectation for a concrete result value.</summary>
    /// <param name="resultNode">The expected-result JSON node.</param>
    /// <returns>The result expectation.</returns>
    public static JsonataConformanceExpectation Result(JsonNode resultNode)
    {
        return new JsonataConformanceExpectation(JsonataConformanceOutcomeKind.Result, resultNode, null, null);
    }

    /// <summary>Builds an expectation for the undefined "nothing" value.</summary>
    /// <returns>The undefined expectation.</returns>
    public static JsonataConformanceExpectation Undefined()
    {
        return new JsonataConformanceExpectation(JsonataConformanceOutcomeKind.Undefined, default, null, null);
    }

    /// <summary>Builds an expectation for an error identified by its code and optional token.</summary>
    /// <param name="code">The expected JSONata error code (for example <c>T2001</c> or <c>S0201</c>).</param>
    /// <param name="token">The optional expected error token, or <see langword="null"/> when none is named.</param>
    /// <returns>The error expectation.</returns>
    public static JsonataConformanceExpectation Error(string code, string? token)
    {
        return new JsonataConformanceExpectation(JsonataConformanceOutcomeKind.Error, default, code, token);
    }
}
