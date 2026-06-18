using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// The SUB-3 extensions of the tuple-stream path cursor: the tuple-aware sort step (the reference's
/// <c>evaluateSortExpression</c> over a tuple stream and the <c>evaluateTupleStep</c> sort branch over a
/// non-tuple input) and the path-end group-by reduce over the tuple stream (the reference's
/// <c>evaluateGroupExpression</c> reduce branch plus <c>reduceTupleStream</c>). Both follow the resident
/// cursor pattern: every key / value sub-expression is scheduled on the shared work stack, so there is no
/// recursion and no nested <c>Evaluate(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Concern-partner of <c>JsonataEvaluator.PathStream.cs</c> (the seed / step / stage / projection cursor):
/// that file owns the flat-and-tuple step pipeline, this one owns the sort and group sub-cursors it dispatches
/// to. The <c>#</c> index stage re-numbering also lives in the step file, next to the other stages.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting / grouping reference</see>.</para>
/// </remarks>
public static partial class JsonataEvaluator
{
    /// <summary>
    /// Begins a tuple-stream sort step (the reference's <c>evaluateTupleStep</c> sort branch): a sort over an
    /// already-built tuple stream stably orders the tuples by the terms evaluated under each tuple's focus and
    /// frame; a sort that is the first tuple step (a non-tuple input) sorts the flat values then wraps each into
    /// one tuple. A term-less sort or a single element needs no comparison and the step finishes immediately
    /// onto its stages.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The sort step (its stages run after the sort completes).</param>
    /// <param name="sort">The sort expression carrying the order-by terms.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathSortStep(EvalFrame frame, PathStep step, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathTuples is not null)
        {
            BeginPathTupleSort(frame, step, sort, work, results);

            return;
        }

        BeginPathFlatSort(frame, step, sort, work, results);
    }

    /// <summary>
    /// Begins a sort over an already-built tuple stream (the reference's <c>evaluateSortExpression</c> tuple
    /// branch): a stream of zero or one tuple, or a term-less sort, is left as-is and the step proceeds straight
    /// to its stages; otherwise the (tuple, term) key cursor is initialised and the first term key scheduled
    /// under the first tuple's focus and frame.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The sort step.</param>
    /// <param name="sort">The sort expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathTupleSort(EvalFrame frame, PathStep step, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        List<PathTuple> tuples = frame.PathTuples!;
        if(tuples.Count <= 1 || sort.Terms.Count == 0)
        {
            //Nothing to order; the tuple stream is unchanged and the step's stages run over it.
            frame.PathStageIndex = 0;
            RunPathTupleStages(frame, step, work, results);

            return;
        }

        frame.PathSortTuples = tuples;
        frame.PathSortValues = null;
        frame.PathSortKeys = [];
        frame.PathSortElementIndex = 0;
        frame.PathSortTermIndex = 0;
        frame.PathPhase = PathStreamPhase.TupleSort;
        SchedulePathTupleSortKey(frame, sort, work, results);
    }

    /// <summary>
    /// Schedules the order-by term key for the current (tuple, term) cursor position under the tuple's focus and
    /// frame (the reference's tuple comparator <c>context = a['@']</c> / <c>createFrameFromTuple(a)</c>); when
    /// every key has been collected the tuples are stably reordered and the step proceeds to its stages.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="sort">The sort expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathTupleSortKey(EvalFrame frame, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathSortElementIndex < frame.PathSortTuples!.Count)
        {
            PathTuple tuple = frame.PathSortTuples[frame.PathSortElementIndex];
            JsonataExpression key = sort.Terms[frame.PathSortTermIndex].Key;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = key, Context = frame.Context.WithFocus(tuple.Focus).WithFrame(tuple.Frame) });

            return;
        }

        //Every (tuple, term) key is collected: stably reorder the tuples by the keys, then run the step stages.
        int[] order = OrderByKeyOrder(frame.PathSortTuples.Count, frame.PathSortKeys!, sort.Terms);
        List<PathTuple> sorted = new(order.Length);
        foreach(int index in order)
        {
            sorted.Add(frame.PathSortTuples[index]);
        }

        frame.PathTuples = sorted;
        PathStep step = ((PathExpression)frame.Node).Steps[frame.PathStepIndex];
        frame.PathStageIndex = 0;
        RunPathTupleStages(frame, step, work, results);
    }

    /// <summary>
    /// Collects the just-evaluated order-by key for the current (tuple, term) cursor position, advances the
    /// cursor (next term of the current tuple, else the first term of the next tuple), then schedules the next
    /// key or completes the sort.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathTupleSort(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        SortExpression sort = ((SortMarkerExpression)((PathExpression)frame.Node).Steps[frame.PathStepIndex].Step).Sort;
        frame.PathSortKeys!.Add(results.Pop());
        AdvancePathSortCursor(frame, sort);
        SchedulePathTupleSortKey(frame, sort, work, results);
    }

    /// <summary>
    /// Begins a sort that is the first tuple step over a non-tuple input (the reference's
    /// <c>evaluateTupleStep</c> sort branch with no <c>tupleBindings</c>): a single value or a term-less sort is
    /// wrapped into tuples directly (no comparison); otherwise the (value, term) key cursor is initialised and
    /// the first key scheduled under the value's rebound focus.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The sort step.</param>
    /// <param name="sort">The sort expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathFlatSort(EvalFrame frame, PathStep step, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        List<JsonataValue> values = frame.PathFlatSequence!;
        if(values.Count <= 1 || sort.Terms.Count == 0)
        {
            //Nothing to order; wrap each value into a tuple (assigning the sort index) and run the stages.
            WrapSortedValuesIntoTuples(frame, step, values);
            frame.PathStageIndex = 0;
            RunPathTupleStages(frame, step, work, results);

            return;
        }

        frame.PathSortValues = values;
        frame.PathSortTuples = null;
        frame.PathSortKeys = [];
        frame.PathSortElementIndex = 0;
        frame.PathSortTermIndex = 0;
        frame.PathPhase = PathStreamPhase.FlatSort;
        SchedulePathFlatSortKey(frame, sort, work, results);
    }

    /// <summary>
    /// Schedules the order-by term key for the current (value, term) cursor position under the value's rebound
    /// focus (the reference's non-tuple comparator <c>context = a</c>); when every key has been collected the
    /// values are stably reordered, each wrapped into one tuple (assigning the sort position to the step's index
    /// variable), and the step proceeds to its stages.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="sort">The sort expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathFlatSortKey(EvalFrame frame, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathSortElementIndex < frame.PathSortValues!.Count)
        {
            JsonataValue value = frame.PathSortValues[frame.PathSortElementIndex];
            JsonataExpression key = sort.Terms[frame.PathSortTermIndex].Key;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = key, Context = frame.Context.WithFocus(value) });

            return;
        }

        int[] order = OrderByKeyOrder(frame.PathSortValues.Count, frame.PathSortKeys!, sort.Terms);
        List<JsonataValue> sorted = new(order.Length);
        foreach(int index in order)
        {
            sorted.Add(frame.PathSortValues[index]);
        }

        PathStep step = ((PathExpression)frame.Node).Steps[frame.PathStepIndex];
        WrapSortedValuesIntoTuples(frame, step, sorted);
        frame.PathStageIndex = 0;
        RunPathTupleStages(frame, step, work, results);
    }

    /// <summary>
    /// Collects the just-evaluated order-by key for the current (value, term) cursor position, advances the
    /// cursor, then schedules the next key or completes the sort.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathFlatSort(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        SortExpression sort = ((SortMarkerExpression)((PathExpression)frame.Node).Steps[frame.PathStepIndex].Step).Sort;
        frame.PathSortKeys!.Add(results.Pop());
        AdvancePathSortCursor(frame, sort);
        SchedulePathFlatSortKey(frame, sort, work, results);
    }

    /// <summary>Advances the sort (element, term) cursor to the next term of the current element, else the first term of the next element.</summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="sort">The sort expression, consulted for its term count.</param>
    private static void AdvancePathSortCursor(EvalFrame frame, SortExpression sort)
    {
        frame.PathSortTermIndex++;
        if(frame.PathSortTermIndex >= sort.Terms.Count)
        {
            frame.PathSortTermIndex = 0;
            frame.PathSortElementIndex++;
        }
    }

    /// <summary>
    /// Wraps each sorted value into a one-focus tuple over the path's entry frame, assigning the post-sort
    /// position to the step's index variable when it has one (the reference's
    /// <c>tuple = {'@': sorted[ss]}; tuple[expr.index] = ss</c>). This bootstraps the tuple stream for the rest
    /// of the path when a sort is the first tuple step over a non-tuple input.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame (its entry frame is the tuple frame ancestor).</param>
    /// <param name="step">The sort step, consulted for its index variable.</param>
    /// <param name="sorted">The values in post-sort order.</param>
    private static void WrapSortedValuesIntoTuples(EvalFrame frame, PathStep step, List<JsonataValue> sorted)
    {
        bool hasIndex = !step.Index.IsEmpty;
        List<PathTuple> tuples = new(sorted.Count);
        for(int ss = 0; ss < sorted.Count; ss++)
        {
            JsonataBindingFrame tupleFrame = frame.PathEntryFrame!;
            if(hasIndex)
            {
                tupleFrame = tupleFrame.CreateChild();
                tupleFrame.Bind(step.Index, JsonataValue.Number(ss));
            }

            tuples.Add(new PathTuple(sorted[ss], tupleFrame));
        }

        frame.PathTuples = tuples;
    }

    /// <summary>
    /// Begins the path-end group-by reduce over the tuple stream (the reference's
    /// <c>evaluateGroupExpression</c> reduce branch): an empty stream is given one undefined tuple so a literal
    /// object still builds; the buckets and the (tuple, pair) bucketing cursor are initialised and the first key
    /// scheduled under its tuple's focus and frame.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="group">The group-by object constructor attached to the path.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathGroupBy(EvalFrame frame, ObjectConstructorExpression group, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        List<PathTuple> tuples = frame.PathTuples ?? [];
        if(tuples.Count == 0)
        {
            //The reference pushes one undefined entry so a literal-JSON object still builds over nothing; an
            //undefined-focus tuple over the entry frame keys the empty group the same way.
            tuples = [new PathTuple(JsonataValue.Undefined, frame.PathEntryFrame!)];
        }

        frame.PathTuples = tuples;
        frame.PathGroups = [];
        frame.PathGroupIndexByKey = new Dictionary<string, int>(System.StringComparer.Ordinal);
        frame.PathGroupEntries = [];
        frame.PathGroupItemIndex = 0;
        frame.PathGroupPairIndex = 0;
        frame.PathPhase = PathStreamPhase.GroupBucketing;
        SchedulePathGroupKey(frame, group, work, results);
    }

    /// <summary>
    /// Schedules the group key expression for the current (tuple, pair) bucketing cursor position under the
    /// tuple's focus and frame (the reference's key phase <c>context = item['@']</c> /
    /// <c>createFrameFromTuple(item)</c>); when the cursor is exhausted it transitions to the valuing phase and
    /// schedules the first group's value.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="group">The group-by object constructor.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathGroupKey(EvalFrame frame, ObjectConstructorExpression group, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(group.Members.Count > 0 && frame.PathGroupItemIndex < frame.PathTuples!.Count)
        {
            PathTuple tuple = frame.PathTuples[frame.PathGroupItemIndex];
            JsonataExpression key = group.Members[frame.PathGroupPairIndex].Key;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = key, Context = frame.Context.WithFocus(tuple.Focus).WithFrame(tuple.Frame) });

            return;
        }

        //Bucketing is complete: begin valuing from the first group.
        frame.PathPhase = PathStreamPhase.GroupValuing;
        frame.PathGroupIndex = 0;
        SchedulePathGroupValue(frame, group, work, results);
    }

    /// <summary>
    /// Collects the just-evaluated key for the current bucketing position, buckets the tuple under it (skipping
    /// an undefined key, throwing T1003 for a non-string key, throwing D1009 for a same-key collision from a
    /// different member pair, appending to the first-seen bucket otherwise), advances the cursor, then schedules
    /// the next key or transitions to valuing.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    /// <exception cref="JsonataErrorException">A key is a defined non-string (T1003) or collides across member pairs (D1009).</exception>
    private static void AdvancePathGroupBucketing(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        ObjectConstructorExpression group = ((PathExpression)frame.Node).Group!;
        JsonataValue key = results.Pop();
        BucketPathTuple(frame, frame.PathTuples![frame.PathGroupItemIndex], frame.PathGroupPairIndex, key);

        frame.PathGroupPairIndex++;
        if(frame.PathGroupPairIndex >= group.Members.Count)
        {
            frame.PathGroupPairIndex = 0;
            frame.PathGroupItemIndex++;
        }

        SchedulePathGroupKey(frame, group, work, results);
    }

    /// <summary>Records one tuple under its evaluated key per the group-by collision rules (the reference's key-phase bucketing).</summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="tuple">The tuple being bucketed.</param>
    /// <param name="pairIndex">The member pair whose key produced this key value.</param>
    /// <param name="key">The evaluated key value.</param>
    /// <exception cref="JsonataErrorException">The key is a defined non-string (T1003) or collides across member pairs (D1009).</exception>
    private static void BucketPathTuple(EvalFrame frame, PathTuple tuple, int pairIndex, JsonataValue key)
    {
        if(key.IsUndefined)
        {
            //An undefined key skips this pair for this tuple; no member is produced.
            return;
        }

        if(key.Kind != JsonataValueKind.String)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ObjectKeyNotString, null, "A key in an object constructor must evaluate to a string.");
        }

        string keyText = key.AsString;
        if(frame.PathGroupIndexByKey!.TryGetValue(keyText, out int existing))
        {
            PathGroupBucket bucket = frame.PathGroups![existing];
            if(bucket.PairIndex != pairIndex)
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.DuplicateGroupKey, null, "Multiple key definitions in an object constructor evaluate to the same key.");
            }

            //A same-pair collision appends the tuple to the existing group (the reduce input).
            bucket.Tuples.Add(tuple);

            return;
        }

        //A first-seen key opens a new bucket, preserving insertion order.
        PathGroupBucket created = new(keyText, pairIndex);
        created.Tuples.Add(tuple);
        frame.PathGroupIndexByKey[keyText] = frame.PathGroups!.Count;
        frame.PathGroups.Add(created);
    }

    /// <summary>
    /// Schedules the group value expression for the current group under its merged-tuple focus and frame (the
    /// reference's value phase over <c>reduceTupleStream</c>: the group's tuples are merged, the merged focus is
    /// the value context, and the merged bindings are re-materialised as variables); when the groups are
    /// exhausted it builds the object from the collected entries, pops the cursor, and pushes the object.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="group">The group-by object constructor.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathGroupValue(EvalFrame frame, ObjectConstructorExpression group, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathGroupIndex < frame.PathGroups!.Count)
        {
            PathGroupBucket bucket = frame.PathGroups[frame.PathGroupIndex];
            JsonataExpression value = group.Members[bucket.PairIndex].Value;
            PathTuple merged = ReducePathTupleStream(frame, bucket.Tuples);
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = value, Context = frame.Context.WithFocus(merged.Focus).WithFrame(merged.Frame) });

            return;
        }

        //Every group has been valued: build the object preserving first-seen key order, pop this resident
        //cursor off the work stack, and hand the object up on the results stack.
        work.Pop();
        results.Push(JsonataValue.Object(frame.PathGroupEntries!));
    }

    /// <summary>
    /// Collects the just-evaluated value for the current group, adds the member when the value is defined
    /// (omitting it otherwise), advances to the next group, then schedules the next group's value or builds and
    /// pushes the object.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathGroupValuing(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue value = results.Pop();
        PathGroupBucket bucket = frame.PathGroups![frame.PathGroupIndex];
        if(!value.IsUndefined)
        {
            //A defined value sets the member; an undefined value omits it.
            frame.PathGroupEntries!.Add(new KeyValuePair<string, JsonataValue>(bucket.Key, value));
        }

        frame.PathGroupIndex++;
        ObjectConstructorExpression group = ((PathExpression)frame.Node).Group!;
        SchedulePathGroupValue(frame, group, work, results);
    }

    /// <summary>
    /// Merges a group's tuples into one tuple for the value phase (the reference's <c>reduceTupleStream</c>): the
    /// merged focus is the <c>$append</c> of every tuple's focus, and each named binding the path can produce
    /// (its <c>@</c> / <c>#</c> focus / index variables, its <c>%</c> ancestor labels, and its index-stage
    /// variables) is the <c>$append</c> of that binding across the group's tuples. The merged bindings are
    /// re-materialised into a child of the path's entry frame so the value expression sees them as variables. A
    /// single-tuple group merges to that tuple unchanged.
    /// </summary>
    /// <remarks>
    /// INVARIANT: at the path end every tuple in the stream carries the same set of binding keys — a tuple step
    /// always layers its <c>@</c> / <c>#</c> / <c>%</c> binding (or, for a merged inner tuple stream, adopts the
    /// inner tuple's bindings) onto EVERY output tuple it emits, and a step that adds no binding leaves the set
    /// unchanged. So a key looked up via <see cref="PathBindingKeys"/> is bound in every group tuple or in none,
    /// and the per-key append is well-defined (a uniform absence appends nothing, matching the reference's
    /// <c>Object.keys(tuple)</c> skip). This is why looking each statically-known key up in each tuple's frame is
    /// sufficient and no per-tuple key enumeration is needed.
    /// </remarks>
    /// <param name="frame">The path-stream cursor frame (its node supplies the path's binding keys, its entry frame is the merged frame's ancestor).</param>
    /// <param name="groupTuples">The group's member tuples, in first-seen order; never empty.</param>
    /// <returns>The merged tuple whose focus and named bindings are the per-key append across the group.</returns>
    private static PathTuple ReducePathTupleStream(EvalFrame frame, List<PathTuple> groupTuples)
    {
        if(groupTuples.Count == 1)
        {
            return groupTuples[0];
        }

        JsonataValue mergedFocus = groupTuples[0].Focus;
        for(int ii = 1; ii < groupTuples.Count; ii++)
        {
            mergedFocus = AppendValues(mergedFocus, groupTuples[ii].Focus);
        }

        //The path's binding keys are statically known from its steps (the @ / # focus / index variables, the %
        //ancestor labels, and the index-stage variables); none can be an outer $variable, so looking each up in
        //each tuple's frame yields exactly that tuple's own binding (a miss appends nothing, matching the
        //reference's Object.keys(tuple) skip).
        JsonataBindingFrame mergedFrame = frame.PathEntryFrame!.CreateChild();
        foreach(Utf8String bindingKey in PathBindingKeys((PathExpression)frame.Node))
        {
            JsonataValue merged = JsonataValue.Undefined;
            foreach(PathTuple tuple in groupTuples)
            {
                if(tuple.Frame.TryLookup(bindingKey, out JsonataValue bound))
                {
                    merged = AppendValues(merged, bound);
                }
            }

            if(!merged.IsUndefined)
            {
                mergedFrame.Bind(bindingKey, merged);
            }
        }

        return new PathTuple(mergedFocus, mergedFrame);
    }

    /// <summary>
    /// Appends two values one level deep (the reference's <c>fn.append</c> as used by <c>reduceTupleStream</c>):
    /// an undefined operand yields the other operand, and a non-array operand is coerced to a one-element array
    /// before the flat join, so two scalars become a two-element array and a scalar appended to an array extends
    /// it. Single-sourced from the <c>$append</c> builtin so the group reduce and the builtin cannot drift.
    /// </summary>
    /// <param name="first">The accumulated value so far.</param>
    /// <param name="second">The value to append.</param>
    /// <returns>The appended value, or the defined operand when the other is undefined.</returns>
    private static JsonataValue AppendValues(JsonataValue first, JsonataValue second)
    {
        return Functions.JsonataArrayFunctions.AppendOneLevel(first, second);
    }

    /// <summary>
    /// Returns every named binding key a path's tuple stream can carry: each step's <c>@</c> focus and <c>#</c>
    /// index variable, each step's resolved <c>%</c> ancestor label (as the reserved <c>!N</c> key), and each
    /// step's index-stage variable. These are the keys <c>reduceTupleStream</c> merges across a group; none can
    /// collide with an outer <c>$variable</c>, so looking each up in a tuple's frame yields that tuple's own
    /// binding.
    /// </summary>
    /// <param name="path">The flattened tuple-stream path.</param>
    /// <returns>The named binding keys, de-duplicated, in step order.</returns>
    private static List<Utf8String> PathBindingKeys(PathExpression path)
    {
        List<Utf8String> keys = [];
        foreach(PathStep step in path.Steps)
        {
            AddBindingKey(keys, step.Focus);
            AddBindingKey(keys, step.Index);
            if(step.Ancestor is { } ancestor)
            {
                AddBindingKey(keys, AncestorSlot.ReservedKey(ancestor.Label));
            }

            foreach(PathStage stage in step.Stages)
            {
                if(stage.Kind == PathStageKind.Index)
                {
                    AddBindingKey(keys, stage.Index);
                }
            }
        }

        return keys;
    }

    /// <summary>Adds a non-empty binding key to the list when it is not already present, preserving first-seen order.</summary>
    /// <param name="keys">The accumulating key list.</param>
    /// <param name="key">The candidate key; the empty <see cref="Utf8String"/> is ignored.</param>
    private static void AddBindingKey(List<Utf8String> keys, Utf8String key)
    {
        if(key.IsEmpty)
        {
            return;
        }

        foreach(Utf8String existing in keys)
        {
            if(existing.Equals(key))
            {
                return;
            }
        }

        keys.Add(key);
    }
}
