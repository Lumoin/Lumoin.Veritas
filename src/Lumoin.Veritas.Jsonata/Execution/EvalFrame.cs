using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Functions;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// One frame on the evaluator's explicit work stack: a single mutable layout discriminated by
/// <see cref="Kind"/>, so the driver is one switch with no frame-type hierarchy. The mutable
/// <see cref="NextIndex"/> cursor lets the dot/map and predicate frames re-enter the same step/filter
/// sub-tree once per source item; the group-by frame adds a second cursor and a bucket structure so it can
/// re-enter the key and value sub-trees once per (item, pair) and once per group.
/// </summary>
internal sealed class EvalFrame
{
    /// <summary>Gets or sets what this frame is doing.</summary>
    public EvalFrameKind Kind { get; set; }

    /// <summary>Gets or sets the node this frame evaluates or combines.</summary>
    public JsonataExpression Node { get; set; } = default!;

    /// <summary>Gets or sets the context (focus, root, binding frame) this frame's node evaluates under.</summary>
    public JsonataContext Context { get; set; } = default!;

    /// <summary>Gets or sets the source items a Map/Predicate/GroupBy frame iterates; <see langword="null"/> until the source is evaluated.</summary>
    public IReadOnlyList<JsonataValue>? Sequence { get; set; }

    /// <summary>Gets or sets the per-item cursor into <see cref="Sequence"/>; <c>-1</c> marks "source not yet consumed".</summary>
    public int NextIndex { get; set; }

    /// <summary>Gets the accumulator a Map flattens into / a Predicate filters into across items.</summary>
    public List<JsonataValue> Accumulator { get; } = [];

    /// <summary>Gets or sets whether a Map/Predicate frame must keep its result a JSON array (the JSONata <c>keepSingleton</c> marker propagated from a <c>[]</c>-marked source step); a singleton result then stays an array rather than auto-unwrapping.</summary>
    public bool KeepArrayResult { get; set; }

    /// <summary>Gets or sets the phase a GroupBy frame is in (seed, bucketing, or valuing).</summary>
    public GroupByPhase GroupByPhase { get; set; }

    /// <summary>Gets or sets the GroupBy frame's per-item member-pair cursor used during the bucketing phase.</summary>
    public int PairIndex { get; set; }

    /// <summary>Gets or sets the GroupBy frame's per-group cursor used during the valuing phase.</summary>
    public int GroupIndex { get; set; }

    /// <summary>Gets or sets the GroupBy frame's buckets in first-seen key order; <see langword="null"/> until the frame is seeded.</summary>
    public List<GroupByBucket>? Groups { get; set; }

    /// <summary>Gets or sets the GroupBy frame's key-to-bucket-index map for O(1) collision detection; <see langword="null"/> until the frame is seeded.</summary>
    public Dictionary<string, int>? GroupIndexByKey { get; set; }

    /// <summary>Gets or sets the GroupBy frame's result-object entry accumulator in first-seen key order; <see langword="null"/> until the frame is seeded.</summary>
    public List<KeyValuePair<string, JsonataValue>>? Entries { get; set; }

    /// <summary>Gets or sets which higher-order array function a <see cref="EvalFrameKind.HigherOrder"/> cursor applies.</summary>
    public HigherOrderKind HigherOrderKind { get; set; }

    /// <summary>Gets or sets the function value a <see cref="EvalFrameKind.HigherOrder"/> cursor applies per element; the undefined value until the cursor is seeded.</summary>
    public JsonataValue HigherOrderFunction { get; set; }

    /// <summary>Gets or sets the number of arguments the higher-order call site supplied, used to detect the absence of <c>$reduce</c>'s initial value.</summary>
    public int HigherOrderArgumentCount { get; set; }

    /// <summary>Gets or sets the running accumulator a <see cref="HigherOrderKind.Reduce"/> cursor folds into across the elements; the undefined value until seeded.</summary>
    public JsonataValue ReduceAccumulator { get; set; }

    /// <summary>Gets or sets whether a <see cref="HigherOrderKind.Single"/> cursor is already holding a matched element.</summary>
    public bool HasSingleMatch { get; set; }

    /// <summary>Gets or sets the element a <see cref="HigherOrderKind.Single"/> cursor is holding once it has matched; the undefined value until a match.</summary>
    public JsonataValue SingleMatch { get; set; }

    /// <summary>Gets or sets the source object's entries a <see cref="HigherOrderKind.Sift"/>/<see cref="HigherOrderKind.Each"/> cursor iterates in insertion order; <see langword="null"/> for an array-kind cursor.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonataValue>>? HigherOrderEntries { get; set; }

    /// <summary>Gets or sets the original object source value a <see cref="HigherOrderKind.Sift"/>/<see cref="HigherOrderKind.Each"/> cursor passes as the third argument of each application; the undefined value for an array-kind cursor.</summary>
    public JsonataValue HigherOrderObject { get; set; }

    /// <summary>Gets or sets the mutable working array a custom-comparator <see cref="HigherOrderKind.Sort"/> cursor insertion-sorts in place; <see langword="null"/> until the cursor is seeded.</summary>
    public List<JsonataValue>? SortWorking { get; set; }

    /// <summary>Gets or sets the outer index of a custom-comparator <see cref="HigherOrderKind.Sort"/> cursor — the element being inserted into the sorted prefix.</summary>
    public int SortOuterIndex { get; set; }

    /// <summary>Gets or sets the inner scan index of a custom-comparator <see cref="HigherOrderKind.Sort"/> cursor — the position in the sorted prefix being compared against the held element.</summary>
    public int SortInnerIndex { get; set; }

    /// <summary>Gets or sets the held element a custom-comparator <see cref="HigherOrderKind.Sort"/> cursor is inserting into the sorted prefix; the undefined value until the cursor is seeded.</summary>
    public JsonataValue SortHeld { get; set; }

    /// <summary>Gets or sets the transformer a resident <see cref="EvalFrameKind.Transform"/> cursor applies — its pattern, update, and optional delete clause expressions; <see langword="null"/> for a non-transform frame.</summary>
    public JsonataTransformer? Transformer { get; set; }

    /// <summary>Gets or sets the deep-cloned input a <see cref="EvalFrameKind.Transform"/> cursor navigates and mutates in place, and returns as the transform's result; the undefined value until the cursor is seeded.</summary>
    public JsonataValue TransformResult { get; set; }

    /// <summary>Gets or sets which clause a <see cref="EvalFrameKind.Transform"/> cursor is evaluating on its current turn (the pattern once, then the update and delete clauses per matched node).</summary>
    public TransformPhase TransformPhase { get; set; }

    /// <summary>Gets or sets the per-element sort keys an <see cref="EvalFrameKind.OrderBy"/> cursor accumulates in element-major, term-minor order (key for element <c>i</c>, term <c>t</c> at index <c>i * termCount + t</c>); <see langword="null"/> until the cursor is seeded.</summary>
    public List<JsonataValue>? SortKeyValues { get; set; }

    /// <summary>Gets or sets the input string a <see cref="EvalFrameKind.RegexReplace"/> cursor replaces matches within; <see langword="null"/> for a non-replace frame.</summary>
    public string? RegexReplaceInput { get; set; }

    /// <summary>Gets or sets the pre-computed matches a <see cref="EvalFrameKind.RegexReplace"/> cursor applies the replacement function to, in left-to-right order; <see langword="null"/> for a non-replace frame.</summary>
    public IReadOnlyList<RegexReplaceMatch>? RegexReplaceMatches { get; set; }

    /// <summary>Gets or sets the replacement function a <see cref="EvalFrameKind.RegexReplace"/> cursor applies once per match; the undefined value for a non-replace frame.</summary>
    public JsonataValue RegexReplaceFunction { get; set; }

    /// <summary>Gets or sets the output buffer a <see cref="EvalFrameKind.RegexReplace"/> cursor builds the replaced string in; <see langword="null"/> until the cursor is seeded.</summary>
    public StringBuilder? RegexReplaceBuilder { get; set; }

    /// <summary>Gets or sets the index into the original input a <see cref="EvalFrameKind.RegexReplace"/> cursor has copied through (the end of the previous match); the unreplaced tail follows the last match.</summary>
    public int RegexReplacePosition { get; set; }

    /// <summary>Gets or sets which sub-task a resident <see cref="EvalFrameKind.PathStream"/> cursor is on its current turn (seeding, an ordinary / tuple step's expression, or an ordinary / tuple step's predicate stage).</summary>
    public PathStreamPhase PathPhase { get; set; }

    /// <summary>Gets or sets the path's entry binding frame a <see cref="EvalFrameKind.PathStream"/> cursor seeds every tuple's frame chain from, so outer <c>$x</c> still resolve inside the tuple stream; <see langword="null"/> for a non-path frame.</summary>
    public JsonataBindingFrame? PathEntryFrame { get; set; }

    /// <summary>Gets or sets the step index a <see cref="EvalFrameKind.PathStream"/> cursor is on (the reference's <c>ii</c> over <c>expr.steps</c>).</summary>
    public int PathStepIndex { get; set; }

    /// <summary>Gets or sets whether a <see cref="EvalFrameKind.PathStream"/> cursor has latched into tuple-stream mode (the reference's <c>isTupleStream</c>): once a step bears a tuple marker it stays set for the rest of the path.</summary>
    public bool PathIsTupleStream { get; set; }

    /// <summary>Gets or sets the current flat input sequence a <see cref="EvalFrameKind.PathStream"/> cursor's ordinary steps iterate (the reference's <c>inputSequence</c> while not a tuple stream); <see langword="null"/> for a non-path frame.</summary>
    public List<JsonataValue>? PathFlatSequence { get; set; }

    /// <summary>Gets or sets the current tuple stream a <see cref="EvalFrameKind.PathStream"/> cursor's tuple steps iterate (the reference's <c>tupleBindings</c>); <see langword="null"/> until the first tuple step bootstraps it.</summary>
    public List<PathTuple>? PathTuples { get; set; }

    /// <summary>Gets or sets the within-step item / tuple cursor a <see cref="EvalFrameKind.PathStream"/> cursor advances as it schedules the step expression once per input item / tuple.</summary>
    public int PathItemIndex { get; set; }

    /// <summary>Gets or sets the flat accumulator a <see cref="EvalFrameKind.PathStream"/> cursor's ordinary step collects its per-item results into; <see langword="null"/> for a non-path frame.</summary>
    public List<JsonataValue>? PathStepResults { get; set; }

    /// <summary>Gets or sets the tuple accumulator a <see cref="EvalFrameKind.PathStream"/> cursor's tuple step collects its per-tuple output tuples into; <see langword="null"/> for a non-path frame.</summary>
    public List<PathTuple>? PathStepTuples { get; set; }

    /// <summary>Gets or sets the stage index a <see cref="EvalFrameKind.PathStream"/> cursor is on within the current step's predicate stages.</summary>
    public int PathStageIndex { get; set; }

    /// <summary>Gets or sets the tuples a <see cref="EvalFrameKind.PathStream"/> cursor's sort step is ordering (the sort's element list); <see langword="null"/> for a non-sort turn.</summary>
    public List<PathTuple>? PathSortTuples { get; set; }

    /// <summary>Gets or sets the flat values a <see cref="EvalFrameKind.PathStream"/> cursor's first-tuple-step sort over a non-tuple input is ordering; <see langword="null"/> for a non-flat-sort turn.</summary>
    public List<JsonataValue>? PathSortValues { get; set; }

    /// <summary>Gets or sets the per-(element, term) sort keys a <see cref="EvalFrameKind.PathStream"/> cursor's sort step accumulates, element-major and term-minor (key for element <c>e</c>, term <c>t</c> at index <c>e * termCount + t</c>); <see langword="null"/> for a non-sort turn.</summary>
    public List<JsonataValue>? PathSortKeys { get; set; }

    /// <summary>Gets or sets the element index a <see cref="EvalFrameKind.PathStream"/> cursor's sort step is scheduling a term key for.</summary>
    public int PathSortElementIndex { get; set; }

    /// <summary>Gets or sets the term index within the current element a <see cref="EvalFrameKind.PathStream"/> cursor's sort step is scheduling a key for.</summary>
    public int PathSortTermIndex { get; set; }

    /// <summary>Gets or sets the path-end group-by buckets a <see cref="EvalFrameKind.PathStream"/> cursor accumulates over the tuple stream, in first-seen key order; each bucket holds the group's member tuples; <see langword="null"/> for a non-group turn.</summary>
    public List<PathGroupBucket>? PathGroups { get; set; }

    /// <summary>Gets or sets the path-end group-by key-to-bucket-index map a <see cref="EvalFrameKind.PathStream"/> cursor uses for O(1) collision detection; <see langword="null"/> for a non-group turn.</summary>
    public Dictionary<string, int>? PathGroupIndexByKey { get; set; }

    /// <summary>Gets or sets the path-end group-by result-object entry accumulator a <see cref="EvalFrameKind.PathStream"/> cursor builds in first-seen key order; <see langword="null"/> for a non-group turn.</summary>
    public List<KeyValuePair<string, JsonataValue>>? PathGroupEntries { get; set; }

    /// <summary>Gets or sets the tuple index a <see cref="EvalFrameKind.PathStream"/> cursor's group-by bucketing pass is on.</summary>
    public int PathGroupItemIndex { get; set; }

    /// <summary>Gets or sets the member-pair index a <see cref="EvalFrameKind.PathStream"/> cursor's group-by bucketing pass is on for the current tuple.</summary>
    public int PathGroupPairIndex { get; set; }

    /// <summary>Gets or sets the group index a <see cref="EvalFrameKind.PathStream"/> cursor's group-by valuing pass is on.</summary>
    public int PathGroupIndex { get; set; }
}

/// <summary>
/// One pre-computed match a <see cref="EvalFrameKind.RegexReplace"/> cursor applies its replacement function
/// to: the half-open span of the match in the input and the match object passed as the function's single
/// argument. The matches are computed up front (matching is synchronous and the zero-length D1004 guard fires
/// then), so the cursor only schedules the per-match function application across turns.
/// </summary>
/// <param name="Start">The zero-based start index of the match in the input.</param>
/// <param name="End">The index one past the end of the match.</param>
/// <param name="MatchObject">The match object <c>{ match, index, groups }</c> passed as the replacement function's argument.</param>
internal readonly record struct RegexReplaceMatch(int Start, int End, JsonataValue MatchObject);
