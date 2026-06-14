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

    /// <summary>The maximum evaluation work-stack depth.</summary>
    public const int MaxEvaluationDepth = 128;

    /// <summary>The maximum number of evaluation steps.</summary>
    public const int MaxEvaluationSteps = 100_000;
}
