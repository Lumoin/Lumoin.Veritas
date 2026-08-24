namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Identifies which deterministic bound a <see cref="JsonataEvaluationLimitException"/> reports.
/// </summary>
public enum JsonataLimit
{
    /// <summary>The maximum length of the expression source, in bytes, was exceeded.</summary>
    ExpressionLength = 0,

    /// <summary>The maximum parser frame-stack depth was exceeded.</summary>
    ParseDepth,

    /// <summary>The maximum evaluation work-stack depth was exceeded.</summary>
    EvaluationDepth,

    /// <summary>The maximum number of evaluation steps was exceeded.</summary>
    EvaluationSteps
}
