namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// The kind of expected outcome a JSONata conformance case declares: a concrete result value, the
/// undefined "nothing" value, or a runtime/parse error identified by its code.
/// </summary>
internal enum JsonataConformanceOutcomeKind
{
    /// <summary>The case declares a concrete expected <c>result</c> value to compare structurally.</summary>
    Result,

    /// <summary>The case declares <c>undefinedResult</c> — the evaluation must produce the undefined value.</summary>
    Undefined,

    /// <summary>The case declares an expected error (via <c>code</c> or an <c>error</c> object carrying a <c>code</c>).</summary>
    Error
}
