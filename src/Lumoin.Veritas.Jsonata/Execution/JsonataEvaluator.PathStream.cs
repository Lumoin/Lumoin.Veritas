using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// The tuple-stream path evaluation cursor (the SUB-2 port of the reference's <c>evaluatePath</c> /
/// <c>evaluateStep</c> / <c>evaluateTupleStep</c> / tuple-aware <c>evaluateFilter</c>): a resident
/// <see cref="EvalFrameKind.PathStream"/> frame that walks a flattened <see cref="PathExpression"/>'s steps,
/// scheduling every step expression and predicate on the shared work stack so there is no recursion and no
/// nested <c>Evaluate(...)</c> subroutine call. A path runs flat (a value sequence) until it hits a step
/// marked a tuple step, after which it latches into tuple-stream mode for the rest of the path; at the end each
/// tuple is projected back to its focus (unless the path carries ancestry, which keeps the tuples).
/// </summary>
/// <remarks>
/// <para>
/// SUB-2 scope: <c>@</c> focus joins, <c>#</c> positional index binds, the <c>%</c> ancestor capture, the
/// predicate <c>Filter</c> stages (flat and tuple-aware), and the final projection / keep-singleton. NOT in
/// SUB-2 (left to SUB-3, so a case needing them stays failing, never regresses): a tuple-aware sort step
/// (<see cref="SortMarkerExpression"/>), the path-level group-by (<see cref="PathExpression.Group"/>), the
/// <see cref="PathStageKind.Index"/> stage re-numbering, and numeric-literal positional select refinements over
/// the tuple stream.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</para>
/// </remarks>
public static partial class JsonataEvaluator
{
    /// <summary>
    /// Drives one turn of a tuple-stream path cursor, which stays resident across its turns (the frame is
    /// peeked, not popped, until the whole path is evaluated). The turn is dispatched on the cursor's phase: the
    /// seed turn normalises the input; an ordinary step turn collects the previous item's step result then
    /// schedules the next item (or moves to the step's stages); a tuple step turn does the same over tuples; a
    /// stage turn collects the previous predicate result then schedules the next (or advances to the next stage
    /// / step). When the last step is done the path is projected and pushed.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepPathStreamFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        switch(frame.PathPhase)
        {
            case PathStreamPhase.Seed:
            {
                SeedPathStream(frame, work, results);

                break;
            }
            case PathStreamPhase.FlatStep:
            {
                AdvancePathFlatStep(frame, work, results);

                break;
            }
            case PathStreamPhase.FlatStage:
            {
                AdvancePathFlatStage(frame, work, results);

                break;
            }
            case PathStreamPhase.TupleStep:
            {
                AdvancePathTupleStep(frame, work, results);

                break;
            }
            case PathStreamPhase.TupleStage:
            {
                AdvancePathTupleStage(frame, work, results);

                break;
            }
            case PathStreamPhase.TupleSort:
            {
                AdvancePathTupleSort(frame, work, results);

                break;
            }
            case PathStreamPhase.FlatSort:
            {
                AdvancePathFlatSort(frame, work, results);

                break;
            }
            case PathStreamPhase.GroupBucketing:
            {
                AdvancePathGroupBucketing(frame, work, results);

                break;
            }
            case PathStreamPhase.GroupValuing:
            {
                AdvancePathGroupValuing(frame, work, results);

                break;
            }
            default:
            {
                throw new InvalidOperationException("The JSONata path-stream cursor reached an undefined phase.");
            }
        }
    }

    /// <summary>
    /// Seeds the path cursor (the reference's <c>evaluatePath</c> input normalisation): an array input whose
    /// first step is NOT a variable reference iterates the array's elements; otherwise the input is wrapped as a
    /// single item (the reference's <c>createSequence(input)</c>). The cursor's entry frame is captured so every
    /// tuple's frame chain reaches the outer bindings, then the first step is begun.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SeedPathStream(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        JsonataValue input = frame.Context.Focus;

        bool firstStepIsVariable = path.Steps.Count > 0 && path.Steps[0].Step is VariableExpression;
        if(input.Kind == JsonataValueKind.Array && !firstStepIsVariable)
        {
            frame.PathFlatSequence = [.. input.AsArray];
        }
        else
        {
            //createSequence(input): the input is one item the first step iterates over (an absolute path whose
            //first step is a $variable, or a non-array input).
            frame.PathFlatSequence = [input];
        }

        frame.PathEntryFrame = frame.Context.Frame;
        frame.PathStepIndex = 0;
        frame.PathIsTupleStream = false;
        frame.PathTuples = null;
        BeginPathStep(frame, work, results);
    }

    /// <summary>
    /// Begins the step at the cursor's current step index (the body of the reference's <c>evaluatePath</c>
    /// per-step loop): latches tuple mode when the step is a tuple step, then dispatches to the flat or tuple
    /// step driver. When the steps are exhausted the path is projected and pushed.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathStep(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        if(frame.PathStepIndex >= path.Steps.Count)
        {
            FinishPathStream(frame, work, results);

            return;
        }

        PathStep step = path.Steps[frame.PathStepIndex];
        if(step.Tuple)
        {
            //isTupleStream latches on the first tuple step and stays set for the rest of the path.
            frame.PathIsTupleStream = true;
        }

        if(frame.PathIsTupleStream)
        {
            BeginPathTupleStep(frame, step, work, results);

            return;
        }

        BeginPathFlatStep(frame, step, work, results);
    }

    /// <summary>
    /// Begins an ordinary (non-tuple) step (the reference's <c>evaluateStep</c>): initialises the per-item
    /// accumulator and schedules the step expression against the first input item, or finishes the step
    /// immediately when the input sequence is empty.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The step being evaluated.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathFlatStep(EvalFrame frame, PathStep step, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        frame.PathPhase = PathStreamPhase.FlatStep;
        frame.PathStepResults = [];
        frame.PathItemIndex = 0;
        SchedulePathFlatItem(frame, step, work, results);
    }

    /// <summary>
    /// Schedules the ordinary step expression against the current input item under the item's rebound focus, or
    /// finalises the step's flat result (the last-step single-array unwrap and one-level flatten) when the items
    /// are exhausted, then proceeds to the step's predicate stages.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The step being evaluated.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathFlatItem(EvalFrame frame, PathStep step, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathItemIndex < frame.PathFlatSequence!.Count)
        {
            JsonataValue item = frame.PathFlatSequence[frame.PathItemIndex];
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = step.Step, Context = frame.Context.WithFocus(item) });

            return;
        }

        //Every input item produced a step result; assemble the flat step result (the reference's last-step
        //single-array unwrap, else one-level flatten), then run the step's predicate stages over it.
        PathExpression path = (PathExpression)frame.Node;
        bool lastStep = frame.PathStepIndex == path.Steps.Count - 1;
        frame.PathFlatSequence = FlattenFlatStep(frame.PathStepResults!, lastStep);
        frame.PathStageIndex = 0;
        RunPathFlatStages(frame, step, work, results);
    }

    /// <summary>
    /// Collects the previous input item's ordinary-step result (a cons array stays whole, a normal array
    /// flattens one level into the accumulator, undefined contributes nothing — the reference's per-item
    /// <c>if(typeof res !== 'undefined') result.push(res)</c> followed by the path-level flatten) then schedules
    /// the next item.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathFlatStep(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        PathStep step = path.Steps[frame.PathStepIndex];
        JsonataValue itemResult = results.Pop();
        if(!itemResult.IsUndefined)
        {
            frame.PathStepResults!.Add(itemResult);
        }

        frame.PathItemIndex++;
        SchedulePathFlatItem(frame, step, work, results);
    }

    /// <summary>
    /// Runs the ordinary step's predicate stages over its flat result, one stage at a time (the reference's
    /// flat <c>evaluateStep</c> stage loop). A <see cref="PathStageKind.Filter"/> stage is applied via a
    /// per-item predicate sub-pass; a <see cref="PathStageKind.Index"/> stage is a SUB-3 concern with no flat
    /// effect, so it is skipped here. When the stages are exhausted the cursor advances to the next step.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The step whose stages are being run.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void RunPathFlatStages(EvalFrame frame, PathStep step, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        while(frame.PathStageIndex < step.Stages.Count)
        {
            PathStage stage = step.Stages[frame.PathStageIndex];
            if(stage.Kind == PathStageKind.Filter && stage.Filter is { } filter)
            {
                BeginPathFlatStageFilter(frame, filter, work, results);

                return;
            }

            //A non-filter (index) stage has no effect on a flat sequence (the index re-numbering is a tuple-mode
            //SUB-3 concern); skip it.
            frame.PathStageIndex++;
        }

        AdvanceToNextPathStep(frame, work, results);
    }

    /// <summary>
    /// Begins a flat predicate stage (the reference's flat <c>evaluateFilter</c>): a literal numeric index is a
    /// single positional select with no per-item iteration; every other filter is evaluated once per item under
    /// the item's rebound focus.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="filter">The stage's filter expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathFlatStageFilter(EvalFrame frame, JsonataExpression filter, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(filter is LiteralExpression { Kind: JsonataLiteralKind.Number } literal)
        {
            JsonataValue selected = SelectLiteralIndex(frame.PathFlatSequence!, literal);
            frame.PathFlatSequence = [.. ToSequenceItems(selected)];
            frame.PathStageIndex++;
            PathExpression path = (PathExpression)frame.Node;
            RunPathFlatStages(frame, path.Steps[frame.PathStepIndex], work, results);

            return;
        }

        frame.PathPhase = PathStreamPhase.FlatStage;
        frame.PathStepResults = [];
        frame.PathItemIndex = 0;
        SchedulePathFlatStageItem(frame, filter, work, results);
    }

    /// <summary>
    /// Schedules a flat stage's filter against the current item under its rebound focus, or finalises the stage
    /// (the kept items become the new flat sequence) and resumes the step's remaining stages when the items are
    /// exhausted.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="filter">The stage's filter expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathFlatStageItem(EvalFrame frame, JsonataExpression filter, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathItemIndex < frame.PathFlatSequence!.Count)
        {
            JsonataValue item = frame.PathFlatSequence[frame.PathItemIndex];
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = filter, Context = frame.Context.WithFocus(item) });

            return;
        }

        frame.PathFlatSequence = frame.PathStepResults!;
        frame.PathStageIndex++;
        frame.PathPhase = PathStreamPhase.FlatStep;
        PathExpression path = (PathExpression)frame.Node;
        RunPathFlatStages(frame, path.Steps[frame.PathStepIndex], work, results);
    }

    /// <summary>
    /// Collects the previous flat stage item's predicate result (keeping the item when its result selects the
    /// item's position or is otherwise truthy — the reference's flat <c>evaluateFilter</c> per-item rule) then
    /// schedules the next item.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathFlatStage(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        PathStep step = path.Steps[frame.PathStepIndex];
        JsonataExpression filter = step.Stages[frame.PathStageIndex].Filter!;
        JsonataValue filterResult = results.Pop();
        int position = frame.PathItemIndex;
        if(KeepsItem(filterResult, position, frame.PathFlatSequence!.Count))
        {
            frame.PathStepResults!.Add(frame.PathFlatSequence[position]);
        }

        frame.PathItemIndex++;
        SchedulePathFlatStageItem(frame, filter, work, results);
    }

    /// <summary>
    /// Begins a tuple step (the reference's <c>evaluateTupleStep</c>): bootstraps the tuple stream from the
    /// current flat sequence on the first tuple step (each item becomes a one-binding tuple over the entry
    /// frame), initialises the output-tuple accumulator, and schedules the step expression against the first
    /// tuple. A sort-marker step (the reference's evaluateTupleStep 'sort' branch) hands off to the sort
    /// sub-cursor instead of seeding / scheduling an ordinary step expression.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The step being evaluated.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathTupleStep(EvalFrame frame, PathStep step, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(step.Step is SortMarkerExpression sortMarker)
        {
            //A sort step: a tuple-stream input is stably sorted by the terms under each tuple's frame; a
            //non-tuple input (this is the first tuple step) is sorted then each value wrapped into one tuple.
            BeginPathSortStep(frame, step, sortMarker.Sort, work, results);

            return;
        }

        if(frame.PathTuples is null)
        {
            //First tuple step: seed one tuple per current flat item (the reference's
            //tupleBindings = input.map(item => {'@': item})), each over the path's entry frame so outer
            //$variables resolve.
            List<PathTuple> seeded = new(frame.PathFlatSequence!.Count);
            foreach(JsonataValue item in frame.PathFlatSequence)
            {
                seeded.Add(new PathTuple(item, frame.PathEntryFrame!));
            }

            frame.PathTuples = seeded;
        }

        frame.PathPhase = PathStreamPhase.TupleStep;
        frame.PathStepTuples = [];
        frame.PathItemIndex = 0;
        SchedulePathTupleItem(frame, step, work, results);
    }

    /// <summary>
    /// Schedules the tuple step expression against the current tuple's focus under that tuple's frame (the
    /// reference's <c>evaluate(expr, tuple['@'], createFrameFromTuple(...))</c>), or moves to the step's
    /// predicate stages when the tuples are exhausted. A sort-marker step is handled by the sort sub-cursor in
    /// <see cref="BeginPathTupleStep"/> and never reaches here.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The step being evaluated.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathTupleItem(EvalFrame frame, PathStep step, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathItemIndex < frame.PathTuples!.Count)
        {
            PathTuple tuple = frame.PathTuples[frame.PathItemIndex];
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = step.Step, Context = frame.Context.WithFocus(tuple.Focus).WithFrame(tuple.Frame) });

            return;
        }

        //Every input tuple produced its output tuples; the produced stream becomes the running tuple stream,
        //then the step's predicate stages run over it.
        frame.PathTuples = frame.PathStepTuples;
        frame.PathStageIndex = 0;
        RunPathTupleStages(frame, step, work, results);
    }

    /// <summary>
    /// Collects the previous tuple's step result into output tuples (the reference's <c>evaluateTupleStep</c>
    /// per-result-item mapping: one output tuple per result item, binding the focus / index / ancestor as the
    /// step prescribes) then schedules the next input tuple.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathTupleStep(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        PathStep step = path.Steps[frame.PathStepIndex];
        JsonataValue stepResult = results.Pop();
        PathTuple inputTuple = frame.PathTuples![frame.PathItemIndex];
        AppendTupleStepOutput(frame.PathStepTuples!, step, inputTuple, stepResult);
        frame.PathItemIndex++;
        SchedulePathTupleItem(frame, step, work, results);
    }

    /// <summary>
    /// Builds the output tuples for one input tuple's step result (the reference's <c>for(bb)</c> loop): an
    /// undefined result drops the input tuple (no output); otherwise one output tuple is emitted per result
    /// item. A focus bind (<c>@</c>) keeps the focus on the input tuple's focus and binds the variable to the
    /// result item; an index bind (<c>#</c>) advances the focus and binds the variable to the item's position;
    /// an ancestor (<c>%</c>) binds the input tuple's focus under the slot's reserved key. A child frame is
    /// opened only when a binding is added, so a plain navigation tuple shares its parent's frame.
    /// </summary>
    /// <param name="output">The output tuple accumulator.</param>
    /// <param name="step">The tuple step prescribing the focus / index / ancestor binds.</param>
    /// <param name="inputTuple">The input tuple whose result is being mapped.</param>
    /// <param name="stepResult">The step expression's result for this input tuple.</param>
    private static void AppendTupleStepOutput(List<PathTuple> output, PathStep step, PathTuple inputTuple, JsonataValue stepResult)
    {
        if(stepResult.IsUndefined)
        {
            return;
        }

        if(stepResult.IsTupleStream)
        {
            //The step expression was itself a nested keep-tuples path (e.g. the inner (Order.Product) of a
            //parenthesised path a trailing % resolves through), so it produced the internal tuple-stream carrier
            //rather than a value. The reference's 'if(res.tupleStream) Object.assign(tuple, res[bb])' merges each
            //inner tuple's bindings into the outgoing tuple: because the inner path was evaluated under the
            //input tuple's frame, each inner tuple's frame already chains to the outer frame, so adopting the
            //inner tuple's (focus, frame) IS the merge — the outer % reads the inner step's captured ancestor
            //from the adopted frame. No focus / index / ancestor of THIS step is layered (the reference skips
            //the else branch entirely in the tupleStream case).
            List<PathTuple> innerTuples = (List<PathTuple>)stepResult.AsTupleStream;
            foreach(PathTuple innerTuple in innerTuples)
            {
                output.Add(innerTuple);
            }

            return;
        }

        IReadOnlyList<JsonataValue> items = stepResult.Kind == JsonataValueKind.Array ? stepResult.AsArray : [stepResult];
        for(int bb = 0; bb < items.Count; bb++)
        {
            JsonataValue resultItem = items[bb];
            bool hasFocus = !step.Focus.IsEmpty;
            bool hasIndex = !step.Index.IsEmpty;
            bool hasAncestor = step.Ancestor is not null;

            JsonataValue outputFocus = hasFocus ? inputTuple.Focus : resultItem;
            JsonataBindingFrame outputFrame = inputTuple.Frame;
            if(hasFocus || hasIndex || hasAncestor)
            {
                //A binding is added, so open a child frame layered on the input tuple's frame (the reference's
                //per-output-tuple key writes seen through createFrameFromTuple on the next step).
                outputFrame = inputTuple.Frame.CreateChild();
                if(hasFocus)
                {
                    outputFrame.Bind(step.Focus, resultItem);
                }

                if(hasIndex)
                {
                    outputFrame.Bind(step.Index, JsonataValue.Number(bb));
                }

                if(hasAncestor)
                {
                    outputFrame.Bind(AncestorSlot.ReservedKey(step.Ancestor!.Label), inputTuple.Focus);
                }
            }

            output.Add(new PathTuple(outputFocus, outputFrame));
        }
    }

    /// <summary>
    /// Runs a tuple step's predicate stages over its output tuple stream, one stage at a time (the reference's
    /// <c>evaluateStages</c>). A <see cref="PathStageKind.Filter"/> stage is applied via a tuple-aware per-tuple
    /// predicate sub-pass; a <see cref="PathStageKind.Index"/> stage re-binds the index variable to each tuple's
    /// current (post-filter) position in the stream. When the stages are exhausted the cursor advances to the
    /// next step.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="step">The step whose stages are being run.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void RunPathTupleStages(EvalFrame frame, PathStep step, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        while(frame.PathStageIndex < step.Stages.Count)
        {
            PathStage stage = step.Stages[frame.PathStageIndex];
            if(stage.Kind == PathStageKind.Filter && stage.Filter is { } filter)
            {
                BeginPathTupleStageFilter(frame, filter, work, results);

                return;
            }

            if(stage.Kind == PathStageKind.Index)
            {
                //The reference's evaluateStages 'index' case: re-bind the index variable to each tuple's CURRENT
                //(post-filter) position in the stream (tuple[stage.value] = ee). Each tuple is replaced by one
                //whose child frame binds the index var to its position; the re-numbering is synchronous, so no
                //sub-evaluation is scheduled.
                ApplyPathIndexStage(frame, stage.Index);
            }

            frame.PathStageIndex++;
        }

        AdvanceToNextPathStep(frame, work, results);
    }

    /// <summary>
    /// Applies a <see cref="PathStageKind.Index"/> stage (the reference's <c>evaluateStages</c> 'index' case):
    /// re-binds the index variable to each tuple's current position in the running tuple stream by replacing the
    /// tuple with one whose child frame binds the index variable to that position. The tuple's focus is
    /// unchanged; only the index binding is (re)written, mirroring the reference's in-place
    /// <c>tuple[stage.value] = ee</c> over the post-filter stream.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="indexVariable">The index variable's bare name the stage re-binds to each tuple's position.</param>
    private static void ApplyPathIndexStage(EvalFrame frame, Utf8String indexVariable)
    {
        List<PathTuple> tuples = frame.PathTuples!;
        List<PathTuple> renumbered = new(tuples.Count);
        for(int ee = 0; ee < tuples.Count; ee++)
        {
            JsonataBindingFrame indexed = tuples[ee].Frame.CreateChild();
            indexed.Bind(indexVariable, JsonataValue.Number(ee));
            renumbered.Add(new PathTuple(tuples[ee].Focus, indexed));
        }

        frame.PathTuples = renumbered;
    }

    /// <summary>
    /// Begins a tuple-aware predicate stage (the reference's tuple <c>evaluateFilter</c>): a literal numeric
    /// index is a single positional select on the tuple stream with no per-tuple iteration; every other filter
    /// is evaluated once per tuple under the tuple's focus and frame.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="filter">The stage's filter expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void BeginPathTupleStageFilter(EvalFrame frame, JsonataExpression filter, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(filter is LiteralExpression { Kind: JsonataLiteralKind.Number } literal)
        {
            frame.PathTuples = SelectLiteralTupleIndex(frame.PathTuples!, literal);
            frame.PathStageIndex++;
            PathExpression path = (PathExpression)frame.Node;
            RunPathTupleStages(frame, path.Steps[frame.PathStepIndex], work, results);

            return;
        }

        frame.PathPhase = PathStreamPhase.TupleStage;
        frame.PathStepTuples = [];
        frame.PathItemIndex = 0;
        SchedulePathTupleStageItem(frame, filter, work, results);
    }

    /// <summary>
    /// Schedules a tuple stage's filter against the current tuple under its focus and frame, or finalises the
    /// stage (the kept tuples become the new tuple stream) and resumes the step's remaining stages when the
    /// tuples are exhausted.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="filter">The stage's filter expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SchedulePathTupleStageItem(EvalFrame frame, JsonataExpression filter, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.PathItemIndex < frame.PathTuples!.Count)
        {
            PathTuple tuple = frame.PathTuples[frame.PathItemIndex];
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = filter, Context = frame.Context.WithFocus(tuple.Focus).WithFrame(tuple.Frame) });

            return;
        }

        frame.PathTuples = frame.PathStepTuples;
        frame.PathStageIndex++;
        frame.PathPhase = PathStreamPhase.TupleStep;
        PathExpression path = (PathExpression)frame.Node;
        RunPathTupleStages(frame, path.Steps[frame.PathStepIndex], work, results);
    }

    /// <summary>
    /// Collects the previous tuple stage's predicate result (keeping the tuple when its result selects the
    /// tuple's position or is otherwise truthy — the reference's tuple-aware <c>evaluateFilter</c> per-tuple
    /// rule) then schedules the next tuple.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvancePathTupleStage(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        PathStep step = path.Steps[frame.PathStepIndex];
        JsonataExpression filter = step.Stages[frame.PathStageIndex].Filter!;
        JsonataValue filterResult = results.Pop();
        int position = frame.PathItemIndex;
        if(KeepsItem(filterResult, position, frame.PathTuples!.Count))
        {
            frame.PathStepTuples!.Add(frame.PathTuples[position]);
        }

        frame.PathItemIndex++;
        SchedulePathTupleStageItem(frame, filter, work, results);
    }

    /// <summary>
    /// Advances the cursor to the next path step (the reference's per-step loop tail): in flat mode an empty
    /// result breaks the loop early (so the path is undefined); the step index increments and the next step is
    /// begun. The reference only re-seeds the input cursor for a non-focus step; in tuple mode the running
    /// tuple stream already carries the state, so this advance simply re-enters the loop.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void AdvanceToNextPathStep(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(!frame.PathIsTupleStream && frame.PathFlatSequence!.Count == 0)
        {
            //A flat step that produced nothing breaks the loop (the reference's
            //if(!isTupleStream && resultSequence.length === 0) break); the path projects to undefined.
            frame.PathStepIndex = ((PathExpression)frame.Node).Steps.Count;
            FinishPathStream(frame, work, results);

            return;
        }

        frame.PathStepIndex++;
        BeginPathStep(frame, work, results);
    }

    /// <summary>
    /// Finishes the path (the reference's <c>evaluatePath</c> tail): a path bearing a group-by hands its tuple
    /// stream to the group-by reduce sub-cursor; otherwise a tuple stream is projected to each tuple's focus
    /// (unless the path carries ancestry for an enclosing path, which keeps the tuples — represented here as the
    /// projected focuses, since there is no enclosing-path tuple consumer) and a flat path's sequence is its
    /// result. The keep-singleton-array marker is applied. The resident cursor is popped and the result pushed.
    /// </summary>
    /// <param name="frame">The path-stream cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void FinishPathStream(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PathExpression path = (PathExpression)frame.Node;
        if(path.Group is { } group && frame.PathIsTupleStream)
        {
            //The led path{...} group form over a tuple stream (the reference's evaluatePath final
            //evaluateGroupExpression with isTupleStream ? tupleBindings : resultSequence): group the WHOLE tuple
            //stream (bindings intact) so a $i / $e in a key / value resolves against the merged tuple's frame.
            //A PathExpression carrying a group is always a tuple path (a plain path{...} stays the original
            //ObjectConstructorExpression chain), so only the reduce branch is needed here.
            BeginPathGroupBy(frame, group, work, results);

            return;
        }

        if(frame.PathIsTupleStream && path.KeepTuples)
        {
            //The reference's evaluatePath 'if(expr.tuple) resultSequence = tupleBindings': this nested path
            //carries ancestry for an enclosing tuple step, so keep the raw tuple stream (focus + ancestor
            //bindings intact) rather than projecting to focuses. The enclosing step's AppendTupleStepOutput
            //merges each inner tuple (the reference's 'if(res.tupleStream) Object.assign(tuple, res[bb])'). The
            //carrier is internal-only and is consumed immediately by that merge, so it never escapes.
            work.Pop();
            results.Push(JsonataValue.TupleStream(frame.PathTuples ?? new List<PathTuple>()));

            return;
        }

        List<JsonataValue> resultItems;
        if(frame.PathIsTupleStream)
        {
            List<PathTuple> tuples = frame.PathTuples ?? [];
            resultItems = new List<JsonataValue>(tuples.Count);
            foreach(PathTuple tuple in tuples)
            {
                //Project each tuple to its focus (the reference's else branch of evaluatePath's expr.tuple
                //check): the ancestor bindings have already been read by any % within the path, so the focus is
                //the result.
                resultItems.Add(tuple.Focus);
            }
        }
        else
        {
            resultItems = frame.PathFlatSequence ?? [];
        }

        work.Pop();
        results.Push(NormalizeStepResult(resultItems, path.KeepSingletonArray));
    }

    /// <summary>
    /// Flattens an ordinary step's per-item results into its flat result sequence (the reference's
    /// <c>evaluateStep</c> tail): the last step with a single non-sequence array result yields that array
    /// unwrapped; otherwise each result is flattened one level (a cons array stays whole, a normal array spreads
    /// its elements, a scalar is one element).
    /// </summary>
    /// <param name="stepResults">The per-item step results, undefined results already dropped.</param>
    /// <param name="lastStep">Whether this is the path's last step.</param>
    /// <returns>The flattened flat result sequence.</returns>
    private static List<JsonataValue> FlattenFlatStep(List<JsonataValue> stepResults, bool lastStep)
    {
        if(lastStep && stepResults.Count == 1 && stepResults[0].Kind == JsonataValueKind.Array && !stepResults[0].IsConsArray)
        {
            //The reference's last-step unwrap: a single plain-array result is taken as-is rather than flattened
            //(so a final navigation that yields one array keeps that array's shape).
            return [.. stepResults[0].AsArray];
        }

        List<JsonataValue> flattened = [];
        foreach(JsonataValue result in stepResults)
        {
            AppendStepResult(flattened, result);
        }

        return flattened;
    }

    /// <summary>
    /// Selects one positional tuple for a literal numeric index over a tuple stream (the reference's tuple
    /// <c>evaluateFilter</c> number branch): the index is floored and taken from the end when negative; an
    /// out-of-range index yields the empty stream, an in-range index the single selected tuple.
    /// </summary>
    /// <param name="tuples">The tuple stream.</param>
    /// <param name="literal">The literal numeric index node.</param>
    /// <returns>The single-tuple stream, or the empty stream when out of range.</returns>
    private static List<PathTuple> SelectLiteralTupleIndex(List<PathTuple> tuples, LiteralExpression literal)
    {
        double index = Math.Floor(double.Parse(literal.Value.Span, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));
        if(index < 0)
        {
            index += tuples.Count;
        }

        if(index < 0 || index >= tuples.Count)
        {
            return [];
        }

        return [tuples[(int)index]];
    }
}
