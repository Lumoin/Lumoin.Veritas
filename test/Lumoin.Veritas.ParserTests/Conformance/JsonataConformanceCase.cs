using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One case from the vendored JSONata test suite: a resolved expression, the resolved input document, the
/// (raw) external bindings map, the expected outcome, and whether the result array's element order is
/// significant.
/// </summary>
/// <remarks>
/// All file references are resolved at load time: an <c>expr-file</c> is read into <see cref="Expression"/>,
/// and a <c>dataset</c> name is read into <see cref="Input"/>. <see cref="Input"/> is <see langword="null"/>
/// for the no-input case (a <c>dataset: null</c>); the runner evaluates that against a JSON <c>null</c> input.
/// <see cref="HasNonEmptyBindings"/> is precomputed so the runner can skip the (small) set of cases that
/// supply external variable bindings without re-walking the raw map. <see cref="LoadError"/> is set when a
/// case could not be resolved through the host JSON adapter (for example an expression string carrying a
/// lone UTF-16 surrogate the adapter cannot materialise); the runner skips such a case rather than running
/// a malformed one.
/// </remarks>
/// <param name="GroupName">The group directory the case came from (for example <c>fields</c>).</param>
/// <param name="CaseFile">The case file label, suffixed with an element index for the rare multi-case array file (for example <c>case000.json</c> or <c>case008.json[1]</c>).</param>
/// <param name="Expression">The resolved JSONata expression source.</param>
/// <param name="Input">The resolved input JSON node, or <see langword="null"/> for the no-input (<c>dataset: null</c>) case.</param>
/// <param name="HasNonEmptyBindings">Whether the case supplies a non-empty external <c>bindings</c> map.</param>
/// <param name="Expectation">The expected outcome (result value, undefined, or error code).</param>
/// <param name="Unordered">Whether the result array's element order is not significant (compare as a multiset).</param>
/// <param name="LoadError">A load-time resolution error message, or <see langword="null"/> when the case loaded cleanly.</param>
internal sealed record JsonataConformanceCase(
    string GroupName,
    string CaseFile,
    string Expression,
    JsonNode? Input,
    bool HasNonEmptyBindings,
    JsonataConformanceExpectation Expectation,
    bool Unordered,
    string? LoadError);
