namespace Lumoin.Veritas.Shacl.Diagnostics;

/// <summary>
/// The outcome status of a single constraint evaluation, as reported in
/// <see cref="ShaclTraceEvent"/> completion events.
/// </summary>
public enum ConstraintEvaluationStatus
{
    /// <summary>The constraint was satisfied; no violation was produced.</summary>
    Passed = 0,

    /// <summary>The constraint was violated; at least one validation result was produced.</summary>
    Failed = 1,

    /// <summary>
    /// Evaluation stopped early because enough information was available.
    /// Typical for boolean combinators: <c>sh:or</c> stops at the first
    /// passing member, <c>sh:maxCount</c> stops once <c>max + 1</c>
    /// values have been seen.
    /// </summary>
    ShortCircuited = 2
}
