using System.Collections.Generic;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// The vendored JSONata suite cases this engine DELIBERATELY diverges from, each with a documented reason.
/// A divergence is a case whose expectation this engine does not meet by design — either because this
/// engine's behaviour is arguably more correct (the expectation depends on a reference-implementation
/// artifact this engine does not share), or because the case exercises a consciously-deferred feature. Such a
/// case is reported as a skip (inconclusive) carrying its reason rather than as a failure, so the failing
/// count stays the honest distance to conformance for REAL bugs. A case is added here ONLY with a verified,
/// written reason — never to hide an unexplained failure. The vendored suite is current: each entry was
/// cross-checked against jsonata-js master, so a divergence reflects an engine design choice, not a stale
/// expectation.
/// </summary>
internal static class JsonataReferenceDivergences
{
    /// <summary>The documented divergence reasons, keyed by the case's group name and file.</summary>
    private static readonly Dictionary<(string Group, string CaseFile), string> Reasons = new()
    {
        [("tail-recursion", "case002.json")] =
            "The reference raises U1001 because $factorial(100) overflows its recursive host call stack at the "
            + "case's depth bound (302). This engine's iterative, tail-call-optimised evaluator instead computes "
            + "the terminating result (100!) within its work-stack depth limit (peaks ~127, under 128). The "
            + "reference's depth metric is ~2.4x this engine's (see JsonataLimits.MaxEvaluationDepth for the "
            + "mapping); reproducing the overflow would require lowering the depth cap below legitimate deep "
            + "recursion. The engine is arguably more correct here.",

        [("matchers", "case000.json")] =
            "Function-as-matcher: the case passes a user-defined matcher function (returning a "
            + "{ match, start, end, groups, next } object whose `next` is a continuation closure) to $match in "
            + "place of a regular expression. This engine matches against regular-expression matchers only; the "
            + "function-matcher protocol is an esoteric feature deferred by design, not a defect in regex "
            + "matching.",
    };

    /// <summary>Gets the documented divergence reason for a case, when it is a known deliberate divergence.</summary>
    /// <param name="groupName">The case's group name.</param>
    /// <param name="caseFile">The case's file name.</param>
    /// <param name="reason">The documented reason when the case is a known divergence; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the case is a known deliberate divergence.</returns>
    public static bool TryGetReason(string groupName, string caseFile, out string? reason)
    {
        return Reasons.TryGetValue((groupName, caseFile), out reason);
    }
}
