namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// What a resident <see cref="EvalFrameKind.PathStream"/> cursor is doing on its current turn. The cursor
/// walks the path's steps in order; within one step it first evaluates the step expression once per input item
/// (<see cref="FlatStep"/>) or once per tuple (<see cref="TupleStep"/>), then runs the step's predicate stages
/// over the produced sequence / tuple stream (<see cref="FlatStage"/> / <see cref="TupleStage"/>), then
/// advances to the next step. The seed turn normalises the input.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
internal enum PathStreamPhase
{
    /// <summary>The seed turn: normalise the path input into the leading flat sequence (the reference's <c>inputSequence</c>) before the first step.</summary>
    Seed,

    /// <summary>An ordinary (non-tuple) step: the step expression is being evaluated once per item of the current flat sequence (the reference's <c>evaluateStep</c> per-item loop).</summary>
    FlatStep,

    /// <summary>An ordinary step's predicate stage: the stage's filter expression is being evaluated once per item of the post-step flat sequence (the reference's flat <c>evaluateFilter</c> per-item loop).</summary>
    FlatStage,

    /// <summary>A tuple step: the step expression is being evaluated once per input tuple (the reference's <c>evaluateTupleStep</c> per-tuple loop).</summary>
    TupleStep,

    /// <summary>A tuple step's predicate stage: the stage's filter expression is being evaluated once per tuple of the post-step tuple stream (the reference's tuple-aware <c>evaluateFilter</c> per-tuple loop).</summary>
    TupleStage,

    /// <summary>A tuple-aware sort step: each order-by term's key is being evaluated once per (tuple, term) under the tuple's focus and frame, then the tuples are stably sorted by the collected keys (the reference's <c>evaluateSortExpression</c> tuple branch).</summary>
    TupleSort,

    /// <summary>A flat-input sort step (the first tuple step over a non-tuple input): each order-by term's key is being evaluated once per (value, term) under the value's rebound focus, then the values are stably sorted and wrapped into one tuple each (the reference's <c>evaluateTupleStep</c> sort branch over a non-tuple input).</summary>
    FlatSort,

    /// <summary>The path-end group-by bucketing pass over the tuple stream: each member pair's key is being evaluated once per (tuple, pair) under the tuple's focus and frame, bucketing the tuple by the string key (the reference's <c>evaluateGroupExpression</c> reduce key phase).</summary>
    GroupBucketing,

    /// <summary>The path-end group-by valuing pass over the tuple stream: each group's value is being evaluated once per group under the group's merged-tuple focus and frame (the reference's <c>evaluateGroupExpression</c> reduce value phase over <c>reduceTupleStream</c>).</summary>
    GroupValuing
}
