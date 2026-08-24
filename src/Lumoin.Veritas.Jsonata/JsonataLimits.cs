namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// The deterministic resource bounds enforced while parsing and evaluating a JSONata expression.
/// </summary>
/// <remarks>
/// The bounds keep parsing and evaluation terminating within predictable limits regardless of
/// input. Each bound has a corresponding <see cref="JsonataLimit"/> value reported when it is
/// exceeded.
/// </remarks>
public static class JsonataLimits
{
    /// <summary>The maximum length of the expression source, in bytes.</summary>
    public const int MaxExpressionLength = 64 * 1024;

    /// <summary>The maximum parser frame-stack depth.</summary>
    public const int MaxParseDepth = 64;

    /// <summary>
    /// The maximum evaluation work-stack depth — the bound that stops a non-terminating or pathologically deep
    /// recursion. One nested function application adds roughly 1.25 work frames here: the iterative driver
    /// schedules a tail call in place and retains only the pending non-tail frames. The reference engine, which
    /// recurses through its host call stack, adds about 3 stack frames per level (the body's ternary, the
    /// arithmetic, and the recursive call are each a nested evaluate), so its depth metric is about 2.4x ours —
    /// a recursion that reaches ~300 reference frames reaches only ~127 here. A reference depth bound of 302
    /// (the suite's <c>$factorial(100)</c> case) thus corresponds to ~126 here (302 * 1.25 / 3); 128 sits just
    /// above that, so a <c>$factorial(100)</c> the reference rejects as a stack overflow completes here.
    /// Lowering this below ~127 to reproduce that rejection would also reject legitimately deep non-recursive
    /// nesting, so the two engines disagree on deep-but-finite recursion by design.
    /// </summary>
    public const int MaxEvaluationDepth = 128;

    /// <summary>
    /// The default ceiling on evaluation steps — one step per work frame the driver runs — bounding a runaway
    /// FINITE iteration. It is the conservative production default; the budget is supplied per evaluation, so a
    /// host running a legitimately large but finite computation raises it. A non-terminating recursion is
    /// bounded independently and far sooner by <see cref="MaxEvaluationDepth"/>, so this ceiling is never the
    /// recursion guard. The iterative driver optimises a tail call in place, so a deep tail recursion is a
    /// finite loop here and completes once its step count is under the ceiling — where the reference, recursing
    /// through its host stack, would overflow; the two engines disagree on deep-but-finite tail recursion by
    /// design.
    /// </summary>
    public const int MaxEvaluationSteps = 100_000;

    /// <summary>The maximum number of elements the range operator <c>..</c> may produce; an inclusive element count.</summary>
    public const int MaxRangeSize = 10_000_000;
}
