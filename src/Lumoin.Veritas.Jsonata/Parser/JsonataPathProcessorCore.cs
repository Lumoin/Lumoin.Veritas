using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Ast;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// The per-case processing logic and the verbatim-faithful ancestry helpers
/// (<c>seekParent</c> / <c>pushAncestry</c> / <c>resolveAncestry</c>) of the path-processing pass. This is the
/// concern-partner of the driver in <c>JsonataPathProcessor.cs</c>: that file owns the iterative post-order
/// walk and the node-to-children mapping; this file owns what each node does once its children are processed.
/// </summary>
/// <remarks>
/// <para>
/// The partial split is the project's partial-by-concern convention: the driver (traversal) and the
/// transform (per-case logic plus ancestry resolution) are two concerns of one type, sharing its private
/// counters / registry. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins
/// reference</see>.
/// </para>
/// </remarks>
internal sealed partial class JsonataPathProcessor
{
    /// <summary>
    /// Processes a map step <c>source.step</c> (the reference's binary <c>.</c> case): flattens into one path
    /// — reusing the source path's step list when the source is already a path, else seeding a path from the
    /// source step — appends the step (or splices the step path's steps), migrates a trailing predicate into a
    /// stage, converts string-literal steps to names and rejects number / value steps (S0213), flags the path
    /// keep-singleton when any step keeps an array, marks the first / last array-constructor steps cons, and
    /// resolves the path's pending ancestry.
    /// </summary>
    /// <param name="map">The map node.</param>
    /// <param name="source">The processed source result.</param>
    /// <param name="step">The processed step result.</param>
    /// <returns>The processed path result.</returns>
    private ProcessResult ProcessMap(MapExpression map, ProcessResult source, ProcessResult step)
    {
        PathBuilder path = source.Path is { } sourcePath
            ? sourcePath
            : PathBuilder.FromStep(source);

        path.Original = map;

        //A leading bare parent seeds the path's seeking list (the reference's 'if lstep.type === parent').
        if(source.IsParent && source.ParentSlot is { } leadingSlot)
        {
            path.SeekingParent.Add(leadingSlot);
        }

        if(step.Path is { } stepPath)
        {
            path.Steps.AddRange(stepPath.Steps);
            path.KeepSingletonArray |= stepPath.KeepSingletonArray;

            //A sub-path's escalated seeking slots (slots that ran off the front of the sub-path) ride on the
            //last spliced step so the enclosing path's resolveAncestry can still thread a % that reached above
            //the sub-path; without this the slots would be dropped and wrongly swept as unresolved (S0217).
            if(stepPath.SeekingParent.Count > 0 && path.Steps.Count > 0)
            {
                path.Steps[^1].SeekingParent.AddRange(stepPath.SeekingParent);
            }
        }
        else
        {
            path.Steps.Add(StepFromResult(step));
        }

        NormalizeSteps(path);
        ResolveAncestry(path);

        return ProcessResult.FromPath(path);
    }

    /// <summary>
    /// Processes a context bind <c>source@$v</c> (the reference's binary <c>@</c> case): takes the source
    /// path's last step (seeding a one-step path when the source is not a path), rejects an <c>@</c> after a
    /// predicate (S0215) or after a sort (S0216), sets that step's focus variable, and marks it a tuple step.
    /// </summary>
    /// <param name="contextBind">The context bind node.</param>
    /// <param name="source">The processed source result.</param>
    /// <returns>The processed path result.</returns>
    private ProcessResult ProcessContextBind(ContextBindExpression contextBind, ProcessResult source)
    {
        //Unlike a positional bind '#', the reference's '@' does NOT wrap a non-path source into a path: it
        //annotates the bare node with the focus, which is inert when the node is evaluated standalone (so '$@$i'
        //is just '$') and becomes a tuple step only once an enclosing path-forming operator wraps the
        //focus-annotated node (so 'Employee@$e.(Contact)' is a tuple stream). A non-path source therefore carries
        //a pending focus rather than seeding a one-step tuple path that would over-project at the path end.
        if(source.Path is not { } sourcePath)
        {
            List<AncestorSlot> seeking = [];
            CollectSeeking(seeking, source);

            return ProcessResult.PlainFocus(source.Node, contextBind.Variable, [.. seeking]);
        }

        sourcePath.Original = contextBind;
        PathStep step = sourcePath.Steps[^1];

        if(step.Stages.Count > 0)
        {
            ReportError(WellKnownDiagnostics.Jsonata.ContextBindAfterPredicate, contextBind.Span, "A context bind '@' cannot follow a predicate in a path step.");
        }
        else if(step.Step is SortMarkerExpression)
        {
            ReportError(WellKnownDiagnostics.Jsonata.BindAfterSort, contextBind.Span, "A context bind '@' cannot follow an order-by clause in a path step.");
        }

        step.Focus = contextBind.Variable;
        step.Tuple = true;

        return ProcessResult.FromPath(sourcePath);
    }

    /// <summary>
    /// Processes a positional bind <c>source#$v</c> (the reference's binary <c>#</c> case): takes the source
    /// path's last step (seeding a one-step path when the source is not a path), sets the step's index variable
    /// when it has no stages, else pushes an index stage, and marks the step a tuple step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference's <c>case '#'</c> migrates a trailing predicate into a stage when wrapping a non-path
    /// source; in this build a trailing predicate is already a separate <see cref="PredicateExpression"/> node
    /// processed into the source step's stages before the <c>#</c> is combined, so no migration is needed here —
    /// the source step already carries any predicate stages.
    /// </para>
    /// <para>
    /// Unlike a context bind <c>@</c>, a positional bind <c>#</c> after a sort step is NOT an error (the
    /// reference's <c>case '#'</c> has no <c>type === 'sort'</c> guard, only <c>case '@'</c> does): it sets the
    /// sort step's index variable so the sort branch numbers each tuple by its post-sort position (the
    /// <c>$^($)#$pos</c> form).
    /// </para>
    /// </remarks>
    /// <param name="indexBind">The positional bind node.</param>
    /// <param name="source">The processed source result.</param>
    /// <returns>The processed path result.</returns>
    private static ProcessResult ProcessIndexBind(IndexBindExpression indexBind, ProcessResult source)
    {
        PathBuilder path = source.Path is { } sourcePath ? sourcePath : PathBuilder.FromStep(source);
        path.Original = indexBind;
        PathStep step = path.Steps[^1];

        if(step.Stages.Count == 0)
        {
            step.Index = indexBind.Variable;
        }
        else
        {
            step.Stages.Add(new PathStage { Kind = PathStageKind.Index, Filter = null, Index = indexBind.Variable });
        }

        step.Tuple = true;

        return ProcessResult.FromPath(path);
    }

    /// <summary>
    /// Processes a keep-array marker <c>source[]</c> (the reference's empty-bracket <c>[</c> infix that sets
    /// <c>keepArray</c> on the preceding step / node, then the path case's <c>keepSingletonArray</c>
    /// propagation): when the source processed to a path, folds the marker into that path — flagging its last
    /// step keep-array and the whole path keep-singleton — so a tuple path stays ONE tuple
    /// <see cref="PathExpression"/> whose singleton result is kept an array, rather than wrapping the
    /// materialised path in a marker node (which would break a following predicate / sort / index stage). A
    /// plain (non-tuple) path materialises to the original marker-wrapped chain unchanged, and a non-path source
    /// keeps the marker wrapper so the standalone keep-array evaluation — and <see cref="StepFromResult"/>, when
    /// the node later becomes a path step — applies it.
    /// </summary>
    /// <param name="keepArray">The keep-array marker node.</param>
    /// <param name="source">The processed source result.</param>
    /// <returns>The processed result.</returns>
    private static ProcessResult ProcessKeepArray(KeepArrayExpression keepArray, ProcessResult source)
    {
        if(source.Path is { } sourcePath)
        {
            sourcePath.Original = keepArray;
            sourcePath.Steps[^1].KeepArray = true;
            sourcePath.KeepSingletonArray = true;

            return ProcessResult.FromPath(sourcePath);
        }

        List<AncestorSlot> seeking = [];
        CollectSeeking(seeking, source);
        JsonataExpression rebuilt = ReferenceEquals(source.Node, keepArray.Source)
            ? keepArray
            : new KeepArrayExpression(keepArray.Span, source.Node);

        return ProcessResult.PlainSeeking(rebuilt, [.. seeking]);
    }

    /// <summary>
    /// Processes a function-application / chain <c>left ~&gt; right</c> (the reference's <c>~&gt;</c> case, whose
    /// <c>result.keepArray = lhs.keepArray || rhs.keepArray</c> propagates an operand's keep-array marker onto
    /// the application): when the right operand carries a keep-array marker (<c>left ~&gt; right[]</c>), hoists
    /// the marker to wrap the whole application (<c>(left ~&gt; right)[]</c>), so the inner right operand is the
    /// bare call / function again — restoring the call-prepend shape the evaluator dispatches on — and the
    /// application's singleton result is kept an array. Otherwise the node is rebuilt from its processed
    /// operands. Each operand's pending ancestry bubbles up so a <c>%</c> nested in either side resolves against
    /// an enclosing path.
    /// </summary>
    /// <param name="apply">The apply node.</param>
    /// <param name="left">The processed left operand.</param>
    /// <param name="right">The processed right operand.</param>
    /// <returns>The processed result.</returns>
    private static ProcessResult ProcessApply(ApplyExpression apply, ProcessResult left, ProcessResult right)
    {
        List<AncestorSlot> seeking = [];
        CollectSeeking(seeking, left);
        CollectSeeking(seeking, right);
        JsonataExpression leftNode = left.Node;
        JsonataExpression rightNode = right.Node;

        if(rightNode is KeepArrayExpression { Source: { } inner })
        {
            ApplyExpression innerApply = new(apply.Span, leftNode, inner);
            JsonataExpression hoisted = new KeepArrayExpression(apply.Span, innerApply);

            return ProcessResult.PlainSeeking(hoisted, [.. seeking]);
        }

        bool changed = !ReferenceEquals(leftNode, apply.Left) || !ReferenceEquals(rightNode, apply.Right);
        JsonataExpression node = changed ? new ApplyExpression(apply.Span, leftNode, rightNode) : apply;

        return ProcessResult.PlainSeeking(node, [.. seeking]);
    }

    /// <summary>
    /// Processes a predicate <c>source[filter]</c> (the reference's <c>[</c> case): takes the source path's
    /// last step (seeding a one-step path when the source is not a path), threads any parent slots the filter
    /// is seeking through that step (a level-one slot resolves against the step via <c>seekParent</c>, a deeper
    /// slot decrements its level), bubbles the filter's remaining ancestry onto the step, and pushes the filter
    /// as a stage on the step.
    /// </summary>
    /// <param name="predicate">The predicate node.</param>
    /// <param name="source">The processed source result.</param>
    /// <param name="filter">The processed filter result.</param>
    /// <returns>The processed path result.</returns>
    private ProcessResult ProcessPredicate(PredicateExpression predicate, ProcessResult source, ProcessResult filter)
    {
        PathBuilder path = source.Path is { } sourcePath ? sourcePath : PathBuilder.FromStep(source);
        path.Original = predicate;
        PathStep step = path.Steps[^1];

        AncestorSlot[] filterSeeking = SeekingSlots(filter);
        if(filterSeeking.Length > 0)
        {
            foreach(AncestorSlot slot in filterSeeking)
            {
                if(slot.Level == 1)
                {
                    SeekParentStep(step, slot);
                }
                else
                {
                    slot.Level--;
                }
            }

            PushAncestryToStep(step, filter);
        }

        step.Stages.Add(new PathStage { Kind = PathStageKind.Filter, Filter = filter.Node, Index = default });

        return ProcessResult.FromPath(path);
    }

    /// <summary>
    /// Processes an order-by <c>source^(terms)</c> (the reference's <c>^</c> case): builds a sort step over the
    /// source path (seeding a one-step path when the source is not a path), bubbles each sort term's pending
    /// ancestry onto the sort step, appends it, and resolves the path's pending ancestry. The sort step's
    /// expression is the original <see cref="SortExpression"/> wrapped in a <see cref="SortMarkerExpression"/>
    /// so the later @ / # cases can detect "after sort" (S0216); the tuple-aware sort evaluation itself is
    /// SUB-3.
    /// </summary>
    /// <param name="sort">The sort node.</param>
    /// <param name="source">The processed source result.</param>
    /// <param name="termResults">The processed term-key results, in term order.</param>
    /// <returns>The processed path result.</returns>
    private ProcessResult ProcessSort(SortExpression sort, ProcessResult source, ProcessResult[] termResults)
    {
        PathBuilder path = source.Path is { } sourcePath ? sourcePath : PathBuilder.FromStep(source);
        path.Original = sort;

        List<SortTerm> rewrittenTerms = [];
        for(int i = 0; i < sort.Terms.Count; i++)
        {
            rewrittenTerms.Add(new SortTerm(sort.Terms[i].Direction, termResults[i].Node));
        }

        SortExpression rewritten = new(sort.Span, sort.Source, rewrittenTerms);
        PathStep sortStep = new() { Step = new SortMarkerExpression(sort.Span, rewritten) };
        foreach(ProcessResult termResult in termResults)
        {
            PushAncestryToStep(sortStep, termResult);
        }

        path.Steps.Add(sortStep);
        ResolveAncestry(path);

        return ProcessResult.FromPath(path);
    }

    /// <summary>
    /// Processes a led group-by <c>source{ members }</c> (the reference's path <c>{</c> case): processes the
    /// source into a path, bubbles each member key and value's pending ancestry onto the path so a <c>%</c> in
    /// a key / value resolves against the path, attaches the group constructor, and resolves the path's pending
    /// ancestry.
    /// </summary>
    /// <param name="group">The led group-by object constructor.</param>
    /// <param name="source">The processed source result.</param>
    /// <param name="memberResults">The processed member key / value results, interleaved.</param>
    /// <returns>The processed path result.</returns>
    private ProcessResult ProcessObjectGroup(ObjectConstructorExpression group, ProcessResult source, ProcessResult[] memberResults)
    {
        PathBuilder path = source.Path is { } sourcePath ? sourcePath : PathBuilder.FromStep(source);
        path.Original = group;

        List<(JsonataExpression Key, JsonataExpression Value)> rewrittenMembers = [];
        for(int i = 0; i < group.Members.Count; i++)
        {
            ProcessResult keyResult = memberResults[2 * i];
            ProcessResult valueResult = memberResults[(2 * i) + 1];
            PushAncestryToPath(path, keyResult);
            PushAncestryToPath(path, valueResult);
            rewrittenMembers.Add((keyResult.Node, valueResult.Node));
        }

        path.Group = new ObjectConstructorExpression(group.Span, rewrittenMembers, Source: null);
        ResolveAncestry(path);

        return ProcessResult.FromPath(path);
    }

    /// <summary>
    /// Processes a block <c>( ... )</c> (the reference's <c>block</c> case): rebuilds the block from its
    /// processed statements and bubbles each statement's pending ancestry up so a <c>%</c> inside the block's
    /// last statement can be resolved by an enclosing path (the <c>seekParent</c> block descent into the last
    /// statement is handled at resolve time).
    /// </summary>
    /// <param name="block">The block node.</param>
    /// <param name="statementResults">The processed statement results, in order.</param>
    /// <returns>The processed result.</returns>
    private static ProcessResult ProcessBlock(BlockExpression block, ProcessResult[] statementResults)
    {
        List<JsonataExpression> statements = [];
        List<AncestorSlot> seeking = [];
        bool changed = false;
        for(int i = 0; i < statementResults.Length; i++)
        {
            statements.Add(statementResults[i].Node);
            if(!ReferenceEquals(statementResults[i].Node, block.Statements[i]))
            {
                changed = true;
            }

            CollectSeeking(seeking, statementResults[i]);
        }

        JsonataExpression rebuilt = changed ? new BlockExpression(block.Span, statements) : block;

        return ProcessResult.PlainSeeking(rebuilt, [.. seeking]);
    }

    /// <summary>
    /// Processes any other node (the reference's default case): rebuilds the node from its processed children
    /// when one changed and bubbles each child's pending ancestry (and a bare-parent child's own slot) up onto
    /// the node, so a <c>%</c> nested in a binary / call / constructor / etc. resolves against an enclosing
    /// path.
    /// </summary>
    /// <param name="node">The node being processed.</param>
    /// <param name="childResults">The processed child results, in source order.</param>
    /// <returns>The processed result.</returns>
    private ProcessResult ProcessDefault(JsonataExpression node, ProcessResult[] childResults)
    {
        List<AncestorSlot> seeking = [];
        JsonataExpression[] children = new JsonataExpression[childResults.Length];
        bool changed = false;
        IReadOnlyList<JsonataExpression> original = ImmediateChildren(node);
        for(int i = 0; i < childResults.Length; i++)
        {
            children[i] = childResults[i].Node;
            if(!ReferenceEquals(children[i], original[i]))
            {
                changed = true;
            }

            CollectSeeking(seeking, childResults[i]);
        }

        //A parent operator that is the immediate procedure of a call ('%()' / '%(1)') is NOT an unresolvable
        //ancestor: it evaluates to undefined and is then invoked, which the existing call path rejects as a
        //non-function -> T1006 (the reference's eval-time behaviour). Excluding its slot from the final
        //unresolved-parent sweep keeps it out of S0217 so the case reaches eval, matching the suite.
        //
        //INTENT (narrow): this only fires for a call whose procedure is a TOP-LEVEL bare '%' with no enclosing
        //path — exactly the standalone '%()' / '%(1)' cases — because such a '%' has no path to resolve against
        //and would otherwise be swept as S0217. It is broader than strictly needed: it would also suppress
        //S0217 for a hypothetical '...%()' where the '%' could resolve against an enclosing path. No suite case
        //exercises that, and SUB-1 has no tuple-stream eval to capture the ancestor, so the behaviour matches
        //every case today. SUB-2 REVISIT: once the tuple cursor binds ancestors, a resolvable '%' used as a
        //callee should capture its ancestor (be seekable) AND still reach the eval-time T1006, rather than be
        //blanket-excluded here.
        if(node is CallExpression && childResults.Length > 0 && childResults[0].IsParent && childResults[0].ParentSlot is { } callableSlot)
        {
            resolvedOrReported.Add(callableSlot);
        }

        JsonataExpression rebuilt = changed ? RebuildImmediate(node, children) : node;

        return ProcessResult.PlainSeeking(rebuilt, [.. seeking]);
    }

    /// <summary>
    /// Processes a bare parent <c>%</c> (the reference's <c>parent</c> case): assigns the slot a fresh label
    /// and registry index, registers it in the ancestry list, and returns it as a parent result so an
    /// enclosing path / node resolves it.
    /// </summary>
    /// <param name="parent">The parent node carrying the slot to assign.</param>
    /// <returns>The processed parent result.</returns>
    private ProcessResult ProcessParent(ParentExpression parent)
    {
        parent.Slot.Label = ancestorLabel++;
        parent.Slot.Level = 1;
        parent.Slot.Index = ancestorIndex++;
        ancestry.Add(parent.Slot);

        return ProcessResult.FromParent(parent, parent.Slot);
    }

    /// <summary>Rebuilds an immediate-children node from its processed children, preserving every non-expression field; matches <see cref="ImmediateChildren"/>'s order.</summary>
    /// <param name="node">The original node.</param>
    /// <param name="children">The processed children in source order.</param>
    /// <returns>The rebuilt node.</returns>
    private static JsonataExpression RebuildImmediate(JsonataExpression node, JsonataExpression[] children)
    {
        return node switch
        {
            BinaryExpression binary => new BinaryExpression(binary.Span, children[0], binary.Operator, children[1]),
            UnaryExpression unary => new UnaryExpression(unary.Span, unary.Operator, children[0]),
            DefaultExpression def => new DefaultExpression(def.Span, children[0], def.Operator, children[1]),
            ConditionalExpression { WhenFalse: null } conditional => new ConditionalExpression(conditional.Span, children[0], children[1], WhenFalse: null),
            ConditionalExpression conditional => new ConditionalExpression(conditional.Span, children[0], children[1], children[2]),
            BindExpression bind => new BindExpression(bind.Span, bind.VariableName, children[0]),
            RangeExpression range => new RangeExpression(range.Span, children[0], children[1]),
            ApplyExpression apply => new ApplyExpression(apply.Span, children[0], children[1]),
            KeepArrayExpression keepArray => new KeepArrayExpression(keepArray.Span, children[0]),
            LambdaExpression lambda => new LambdaExpression(lambda.Span, lambda.Parameters, children[0], lambda.Signature),
            CallExpression call => new CallExpression(call.Span, children[0], children[1..]),
            ArrayConstructorExpression array => new ArrayConstructorExpression(array.Span, children, array.ConsArray),
            ObjectConstructorExpression obj => RebuildObject(obj, children),
            TransformExpression transform => new TransformExpression(transform.Span, children[0], children[1], transform.Delete is null ? null : children[2]),
            _ => node
        };
    }

    /// <summary>Rebuilds a prefix or led object constructor from its processed children, re-pairing the interleaved member tail and preserving the leading source child when present.</summary>
    /// <param name="obj">The original object constructor.</param>
    /// <param name="children">The processed children: an optional leading source, then interleaved key / value pairs.</param>
    /// <returns>The rebuilt object constructor.</returns>
    private static ObjectConstructorExpression RebuildObject(ObjectConstructorExpression obj, JsonataExpression[] children)
    {
        int sourceCount = obj.Source is null ? 0 : 1;
        List<(JsonataExpression Key, JsonataExpression Value)> members = [];
        for(int i = 0; i < obj.Members.Count; i++)
        {
            members.Add((children[sourceCount + (2 * i)], children[sourceCount + (2 * i) + 1]));
        }

        return new ObjectConstructorExpression(obj.Span, members, sourceCount == 0 ? null : children[0]);
    }

    /// <summary>Records an error-severity diagnostic in the pass's sink.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    /// <param name="message">A human-readable explanation.</param>
    private void ReportError(Utf8String code, SourceSpan span, string message)
    {
        diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, Utf8Strings.From(message)));
    }
}
