namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// The grammar production a <see cref="ParseFrame"/> represents. The driver dispatches on the kind
/// together with the frame's stage counter to advance a production one step at a time, pushing child
/// frames for sub-productions that admit unbounded nesting — so the whole grammar is parsed iteratively
/// over an explicit stack, never by method recursion.
/// </summary>
/// <remarks>
/// JSONata is overwhelmingly an expression language, so the single multi-stage
/// <see cref="Expression"/> frame carries the bulk of the grammar via precedence climbing; the map
/// step, the predicate / index filter, the bind right operand, and the conditional branches each run as
/// resume stages inside that one frame rather than as separate frame kinds. The variadic
/// array-constructor, object-constructor, and block bodies are the exceptions: each runs as its own frame
/// kind (<see cref="ElementList"/> / <see cref="ObjectMemberList"/> / <see cref="BlockStatementList"/>)
/// because it accumulates an unbounded list of elements / member pairs / statements across
/// NeedMore-survivable stages, so the "every sub-production is a stage inside <see cref="Expression"/>"
/// description is not exhaustive.
/// </remarks>
internal enum ParseFrameKind
{
    /// <summary>The top-level program: a single expression spanning the whole input.</summary>
    Program,

    /// <summary>An expression (the Pratt precedence sub-driver).</summary>
    Expression,

    /// <summary>A comma-separated element list — the array constructor's variadic body between <c>[</c> and <c>]</c>.</summary>
    ElementList,

    /// <summary>A comma-separated key/value member list — the object constructor's variadic body between <c>{</c> and <c>}</c>.</summary>
    ObjectMemberList,

    /// <summary>A semicolon-separated statement list — the block's variadic body between <c>(</c> and <c>)</c>.</summary>
    BlockStatementList,

    /// <summary>A lambda definition <c>function ( params ) { body }</c> — collects the parameter names, then parses the body expression.</summary>
    LambdaDefinition,

    /// <summary>A comma-separated argument list — a function call's variadic argument expressions between <c>(</c> and <c>)</c>.</summary>
    ArgumentList,

    /// <summary>A comma-separated order-by term list — the <c>^</c> operator's variadic terms between <c>(</c> and <c>)</c>, each an optional direction prefix and a key expression.</summary>
    SortTermList
}
