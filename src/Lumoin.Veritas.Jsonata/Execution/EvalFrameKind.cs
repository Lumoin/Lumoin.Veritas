namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>What an <see cref="EvalFrame"/> is doing on the evaluator's work stack.</summary>
internal enum EvalFrameKind
{
    /// <summary>Expand phase: resolve a leaf, schedule an operator's children, or open a cursor.</summary>
    Expand,

    /// <summary>Combine phase: fold an operator's already-computed children (binary, unary, conditional).</summary>
    Combine,

    /// <summary>The dot/map operator: per-item re-evaluation of the step under a rebound focus.</summary>
    Map,

    /// <summary>The predicate/index operator: per-item evaluation of the filter under a rebound focus.</summary>
    Predicate,

    /// <summary>The object constructor's group-by: two-phase per-item key bucketing then per-group value evaluation, each under a rebound focus.</summary>
    GroupBy,

    /// <summary>The block <c>( ... )</c>: in-order per-statement evaluation under one child binding frame, yielding the last statement's value.</summary>
    Block,

    /// <summary>A higher-order array function (<c>$map</c>/<c>$filter</c>/<c>$single</c>/<c>$reduce</c>): per-element application of a supplied function through the shared apply path.</summary>
    HigherOrder,

    /// <summary>The transform <c>| location | update [, delete] |</c>: the location pattern is evaluated once over the cloned input, then the update and (optional) delete clauses once per matched node, each under the match's rebound focus.</summary>
    Transform,

    /// <summary>The order-by <c>source ^ ( term, ... )</c>: each term's key is evaluated once per element under the element's rebound focus, then the elements are stably sorted by the collected keys.</summary>
    OrderBy,

    /// <summary>The regular-expression function-replacement of <c>$replace(str, /re/, fn[, limit])</c>: the user function is applied once per match (its body scheduled on the work stack), and each non-string result is a D3012 error.</summary>
    RegexReplace,

    /// <summary>The flattened tuple-stream path (<c>@</c> / <c>#</c> / <c>%</c>): each step's expression is evaluated once per input item (flat mode) or per tuple (tuple mode) under a rebound focus / frame, output tuples carry the focus / index / ancestor bindings, trailing predicate stages filter the stream, and the path end projects each tuple back to its focus.</summary>
    PathStream,

    /// <summary>Boolize: replace the top result with its truthiness — the resumption that finishes a short-circuiting <c>and</c>/<c>or</c> once its right operand has been evaluated.</summary>
    Boolize
}
