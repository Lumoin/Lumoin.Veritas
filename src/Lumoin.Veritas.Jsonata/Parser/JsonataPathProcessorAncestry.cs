using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Ast;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// The ancestry-resolution helpers and the intermediate <see cref="ProcessResult"/> / path-builder types of
/// the path-processing pass: the verbatim-faithful <c>seekParent</c> / <c>pushAncestry</c> /
/// <c>resolveAncestry</c> (rendered as bounded explicit-stack loops, never recursion), the step-normalisation
/// (string → name, number / value → S0213, cons / keep-array flagging), the path materialisation (a tuple
/// path becomes a <see cref="PathExpression"/>, a plain path stays the original chain), and the final
/// unresolved-parent (S0217) sweep.
/// </summary>
/// <remarks>
/// Third concern-partner of the path processor. See <see href="https://docs.jsonata.org/path-operators#navigate-to-the-parent">the JSONata parent-operator reference</see>.
/// </remarks>
internal sealed partial class JsonataPathProcessor
{
    /// <summary>The set of slots a <c>seekParent</c> walk attached to a capturing step, so the final sweep does not re-report an already-resolved (or already-errored) slot.</summary>
    private readonly HashSet<AncestorSlot> resolvedOrReported = [];

    /// <summary>The set of non-navigable (number / value) steps already reported as S0213, so <see cref="NormalizeSteps"/> — re-run on every map combine over the whole step list — reports each such step once rather than O(n) times.</summary>
    private readonly HashSet<PathStep> nonNavigableReported = [];

    /// <summary>The current depth of the mutually-recursive block / parenthesised-path descent (<see cref="SeekParentContainerStep"/> &lt;-&gt; <see cref="SeekParentStep"/>), bounded by <see cref="JsonataLimits.MaxParseDepth"/> so adversarially deep nested parens throw a catchable <see cref="JsonataParseException"/> rather than overflowing the C# stack.</summary>
    private int containerDescentDepth;

    /// <summary>
    /// Normalises a path's steps after a splice (the reference's per-<c>.</c> filter / forEach): converts a
    /// string-literal step to a name, reports a number / value step as a non-navigable step (S0213), flags the
    /// path keep-singleton when any step kept an array, and marks the first / last array-constructor steps cons.
    /// </summary>
    /// <param name="path">The path builder whose steps are normalised in place.</param>
    private void NormalizeSteps(PathBuilder path)
    {
        foreach(PathStep step in path.Steps)
        {
            switch(step.Step)
            {
                case(LiteralExpression { Kind: JsonataLiteralKind.String } literal):
                {
                    step.Step = new NameExpression(literal.Span, literal.Value);

                    break;
                }
                case(LiteralExpression { Kind: JsonataLiteralKind.Number or JsonataLiteralKind.Boolean or JsonataLiteralKind.Null }):
                {
                    //NormalizeSteps re-scans the whole step list on every map combine; report each non-navigable
                    //step once (it stays a LiteralExpression, so unlike the idempotent string->name conversion a
                    //naive re-scan would re-fire) rather than O(n) times.
                    if(nonNavigableReported.Add(step))
                    {
                        ReportError(WellKnownDiagnostics.Jsonata.PathStepNotNavigable, step.Step.Span, "A path step cannot be a number or a literal value.");
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }

            if(step.KeepArray)
            {
                path.KeepSingletonArray = true;
            }
        }

        if(path.Steps.Count > 0)
        {
            FlagConsArray(path.Steps[0]);
            FlagConsArray(path.Steps[^1]);
        }
    }

    /// <summary>Flags a step cons when it is an array-constructor step (the reference's first / last <c>'['</c> <c>consarray</c> flag), so the enclosing step keeps the constructed array whole.</summary>
    /// <param name="step">The step to flag.</param>
    private static void FlagConsArray(PathStep step)
    {
        if(step.Step is ArrayConstructorExpression)
        {
            step.ConsArray = true;
        }
    }

    /// <summary>
    /// Resolves a path's pending ancestry (the verbatim <c>resolveAncestry</c>): for each slot the last step is
    /// seeking (plus the last step's own slot when it is a bare parent), walks the earlier steps backward —
    /// skipping over contiguous focus-bound (<c>@</c>) steps — calling <see cref="SeekParentStep"/> until the
    /// slot's level reaches zero; a slot that runs off the front of the path is escalated to the path's own
    /// seeking list so an enclosing path can resolve it. The backward walk is a bounded loop, not recursion.
    /// </summary>
    /// <param name="path">The path whose pending ancestry is resolved.</param>
    private void ResolveAncestry(PathBuilder path)
    {
        if(path.Steps.Count == 0)
        {
            return;
        }

        PathStep lastStep = path.Steps[^1];
        List<AncestorSlot> slots = [.. lastStep.SeekingParent];
        if(lastStep.Step is ParentExpression lastParent)
        {
            slots.Add(lastParent.Slot);
        }

        foreach(AncestorSlot slot in slots)
        {
            int index = path.Steps.Count - 2;
            int guard = 0;
            while(slot.Level > 0)
            {
                if(++guard > JsonataLimits.MaxParseDepth)
                {
                    throw new JsonataParseException("The JSONata ancestry resolution exceeded the maximum path length.", path.Original.Span);
                }

                if(index < 0)
                {
                    path.SeekingParent.Add(slot);

                    break;
                }

                PathStep step = path.Steps[index--];

                //Multiple contiguous focus-bound (@) steps are skipped, so a % sees past them to the real
                //structural parent (the verbatim resolveAncestry focus-skip loop).
                while(index >= 0 && !step.Focus.IsEmpty && !path.Steps[index].Focus.IsEmpty)
                {
                    step = path.Steps[index--];
                }

                SeekParentStep(step, slot);
            }
        }
    }

    /// <summary>
    /// Seeks the ancestor a slot refers to against one path step (the verbatim <c>seekParent</c>, with its
    /// block / path descent rendered as a bounded explicit stack rather than recursion). A name / wildcard step
    /// decrements the slot's level and, at zero, captures the slot on the step (reusing an existing label when
    /// the step already captures one); a parent step increments the level; a block / path step descends into
    /// its last expression / step (and earlier steps while the level is positive); any other step kind cannot
    /// derive an ancestor and records an S0217.
    /// </summary>
    /// <param name="step">The step to seek against.</param>
    /// <param name="slot">The slot seeking an ancestor.</param>
    private void SeekParentStep(PathStep step, AncestorSlot slot)
    {
        switch(step.Step)
        {
            case(NameExpression or WildcardExpression):
            {
                slot.Level--;
                if(slot.Level == 0)
                {
                    if(step.Ancestor is null)
                    {
                        step.Ancestor = slot;
                    }
                    else
                    {
                        //Reuse the existing label so two % landing on one step share a single tuple key (the
                        //verbatim ancestry[slot.index].slot.label = node.ancestor.label rewrite).
                        slot.Label = step.Ancestor.Label;
                        step.Ancestor = slot;
                    }

                    step.Tuple = true;
                    resolvedOrReported.Add(slot);
                }

                break;
            }
            case(ParentExpression):
            {
                slot.Level++;

                break;
            }
            case(BlockExpression):
            {
                //The verbatim seekParent 'block'/'path' cases set node.tuple = true on the container they
                //descend into; mark the container step a tuple step so the cursor treats the block / nested
                //path as a tuple step. The descent promotes the block's inner path to a keep-tuples tuple
                //PathExpression and attaches the resolved ancestor to its captured inner step, so at eval the
                //inner path yields a tuple stream the enclosing step merges (the % then finds its ancestor).
                step.Tuple = true;
                SeekParentContainerStep(step, slot);

                break;
            }
            case(SortMarkerExpression):
            {
                //A sort step is not a structural ancestor; the slot cannot derive an ancestor from it.
                ReportUnresolved(slot, step.Step.Span);

                break;
            }
            default:
            {
                //Any other step kind (a variable such as $ / $$, an object constructor, a call, ...) cannot
                //derive an ancestor (the verbatim seekParent default S0217).
                ReportUnresolved(slot, step.Step.Span);

                break;
            }
        }
    }

    /// <summary>
    /// Descends a <c>seekParent</c> into a block / parenthesised-path container step (the verbatim
    /// <c>seekParent</c> block / path cases) and PROMOTES the container's inner content to a keep-tuples tuple
    /// <see cref="PathExpression"/>: it flattens the container's inner path into a mutable <see cref="PathStep"/>
    /// list (cached on the container step, so a later <c>%</c> / <c>%.%</c> resolving through the same container
    /// reuses it), walks those inner steps backward — decrementing the slot's level per structural step, bumping
    /// it per inner <c>%</c> — and, when the level reaches zero inside the container, attaches the slot to that
    /// inner step (the reference's inner capture). A slot whose level is still positive after the inner steps
    /// keeps descending into an EARLIER outer step on the caller's continuing walk (it is left seeking, the
    /// reference's <c>path</c> case consuming previous outer steps). The container step's expression is rebuilt
    /// once as a block wrapping the inner keep-tuples path, so at eval the inner path yields the tuple stream the
    /// enclosing step merges.
    /// </summary>
    /// <param name="containerStep">The outer path step whose block / parenthesised-path content is descended into.</param>
    /// <param name="slot">The slot seeking an ancestor.</param>
    /// <remarks>
    /// A nested-parens inner step (e.g. the inner <c>(Product)</c> of <c>(Order.(Product))</c>) descends through
    /// <see cref="SeekParentStep"/> back into this method, so the two mutually recurse with the paren-nesting
    /// depth. The shared <see cref="containerDescentDepth"/> counter bounds that nesting by
    /// <see cref="JsonataLimits.MaxParseDepth"/> so an adversarially deep <c>(((…)))</c> throws a catchable
    /// <see cref="JsonataParseException"/> rather than overflowing the C# stack — the same discipline the rest of
    /// the pass follows. (Sibling breadth — many inner steps — uses the bounded backward <c>while</c> loop below,
    /// which is not recursion.)
    /// </remarks>
    private void SeekParentContainerStep(PathStep containerStep, AncestorSlot slot)
    {
        if(++containerDescentDepth > JsonataLimits.MaxParseDepth)
        {
            containerDescentDepth--;

            throw new JsonataParseException("The JSONata ancestry block descent exceeded the maximum nesting depth.", containerStep.Step.Span);
        }

        try
        {
            List<PathStep>? innerSteps = EnsureContainerPromoted(containerStep);
            if(innerSteps is null)
            {
                //An empty / non-navigable container has nothing to descend into; the slot stays seeking (resolved
                //by the outer walk against an earlier step, else swept S0217). The container is not promoted.
                return;
            }

            //Walk the inner steps backward (the reference's path case: last inner step first, earlier while level
            //remains), decrementing per structural step and bumping per inner %, attaching the ancestor at zero.
            int index = innerSteps.Count - 1;
            while(slot.Level > 0 && index >= 0)
            {
                SeekParentStep(innerSteps[index--], slot);
            }

            //A level that did not reach zero inside the container is left seeking; the caller's resolveAncestry
            //walk continues it against earlier OUTER steps (the % reaches above this parenthesised sub-path).
        }
        finally
        {
            containerDescentDepth--;
        }
    }

    /// <summary>
    /// Flattens a container step's block / parenthesised-path content into a mutable tuple <see cref="PathStep"/>
    /// list and rebuilds the container step's expression as a block wrapping a keep-tuples inner
    /// <see cref="PathExpression"/> over that list — once, caching the list on <see cref="PathStep.InnerTupleSteps"/>
    /// so repeated descents reuse it. Returns the cached inner steps, or <see langword="null"/> when the content
    /// is an empty / non-navigable container that cannot host an ancestor capture.
    /// </summary>
    /// <param name="containerStep">The container step to promote.</param>
    /// <returns>The inner steps when the container was (or already is) promoted; otherwise <see langword="null"/>.</returns>
    private static List<PathStep>? EnsureContainerPromoted(PathStep containerStep)
    {
        if(containerStep.InnerTupleSteps is { } cached)
        {
            return cached;
        }

        List<PathStep>? innerSteps = FlattenContainerContent(containerStep.Step);
        if(innerSteps is null || innerSteps.Count == 0)
        {
            return null;
        }

        containerStep.InnerTupleSteps = innerSteps;

        //Rebuild the container's expression as a block wrapping the inner keep-tuples path, so the existing block
        //cursor runs the inner path (the reference keeps the block; its last statement is the tuple path) and the
        //inner path yields the tuple stream. The same innerSteps list backs this path, so an ancestor the walk
        //attaches to an inner step is seen here.
        PathExpression innerPath = new(containerStep.Step.Span, innerSteps, KeepSingletonArray: false, Group: null, CarriesAncestry: false, KeepTuples: true);
        containerStep.Step = new BlockExpression(containerStep.Step.Span, [innerPath]);

        return innerSteps;
    }

    /// <summary>
    /// Flattens a block / parenthesised-path container's inner content into an ordered tuple <see cref="PathStep"/>
    /// list (the steps a <c>%</c> resolves against), over a bounded explicit stack rather than recursion: a block
    /// descends into its last statement; a nested <see cref="MapExpression"/> / <see cref="PathExpression"/>
    /// chain flattens into its ordered steps; a single name / wildcard / parent leaf is one step. Returns
    /// <see langword="null"/> when the content is an empty block or a non-navigable leaf (no ancestor can be
    /// derived; the slot is left seeking for the outer walk or the S0217 sweep).
    /// </summary>
    /// <param name="content">The container step's inner expression.</param>
    /// <returns>The flattened inner steps in source order, or <see langword="null"/> when not navigable.</returns>
    private static List<PathStep>? FlattenContainerContent(JsonataExpression content)
    {
        //Unwrap a block to its last statement (the reference's block descent into the last expression).
        JsonataExpression inner = content;
        int unwrapGuard = 0;
        while(inner is BlockExpression block)
        {
            if(block.Statements.Count == 0)
            {
                return null;
            }

            if(++unwrapGuard > JsonataLimits.MaxParseDepth)
            {
                return null;
            }

            inner = block.Statements[^1];
        }

        //A nested path is already flattened: reuse its steps directly so an ancestor attaches to the live step.
        if(inner is PathExpression nestedPath)
        {
            return [.. nestedPath.Steps];
        }

        //Flatten a nested MapExpression chain into ordered steps (the reference's path-step flattening).
        List<JsonataExpression> ordered = [];
        Stack<JsonataExpression> work = new();
        work.Push(inner);
        int guard = 0;
        while(work.Count > 0)
        {
            if(++guard > JsonataLimits.MaxExpressionLength)
            {
                return null;
            }

            JsonataExpression node = work.Pop();
            if(node is MapExpression map)
            {
                //Push the step then the source so the source's (earlier) steps are popped before the step.
                work.Push(map.Step);
                work.Push(map.Source);

                continue;
            }

            ordered.Add(node);
        }

        List<PathStep> steps = new(ordered.Count);
        foreach(JsonataExpression node in ordered)
        {
            if(node is not (NameExpression or WildcardExpression or ParentExpression or BlockExpression))
            {
                //A non-navigable inner leaf (a variable, a call, an object constructor, ...) cannot host an
                //ancestor capture; do not promote the container.
                return null;
            }

            //A BlockExpression inner step is a NESTED parenthesised sub-path (e.g. the inner (Product) of
            //(Order.(Product))): it stays a block step that SeekParentStep recursively promotes when the walk
            //descends into it, so doubly-nested parens compose.
            steps.Add(new PathStep { Step = node });
        }

        return steps;
    }

    /// <summary>
    /// Threads a child's pending ancestry onto a step (the verbatim <c>pushAncestry(step, value)</c> as used by
    /// the predicate case): collects the child's seeking slots (and its own slot when it is a bare parent) onto
    /// the step's seeking list.
    /// </summary>
    /// <param name="step">The step to push the ancestry onto.</param>
    /// <param name="value">The processed child whose pending ancestry is pushed.</param>
    private static void PushAncestryToStep(PathStep step, ProcessResult value)
    {
        CollectSeeking(step.SeekingParent, value);
    }

    /// <summary>Threads a child's pending ancestry onto a path's seeking list (the verbatim <c>pushAncestry(path, value)</c> as used by the group / sort cases).</summary>
    /// <param name="path">The path to push the ancestry onto.</param>
    /// <param name="value">The processed child whose pending ancestry is pushed.</param>
    private static void PushAncestryToPath(PathBuilder path, ProcessResult value)
    {
        CollectSeeking(path.SeekingParent, value);
    }

    /// <summary>Collects a processed child's pending ancestry — its seeking slots plus its own slot when it is a bare parent — into a target slot list (the verbatim <c>pushAncestry</c> body).</summary>
    /// <param name="target">The list the slots are appended to.</param>
    /// <param name="value">The processed child.</param>
    private static void CollectSeeking(List<AncestorSlot> target, ProcessResult value)
    {
        foreach(AncestorSlot slot in value.SeekingParent)
        {
            target.Add(slot);
        }

        if(value.IsParent && value.ParentSlot is { } slotOfParent)
        {
            target.Add(slotOfParent);
        }
    }

    /// <summary>Returns the slots a processed result is seeking, for the predicate case's level threading.</summary>
    /// <param name="value">The processed result.</param>
    /// <returns>The seeking slots (plus the result's own slot when it is a bare parent).</returns>
    private static AncestorSlot[] SeekingSlots(ProcessResult value)
    {
        List<AncestorSlot> slots = [];
        CollectSeeking(slots, value);

        return [.. slots];
    }

    /// <summary>Records an S0217 for a slot that cannot derive an ancestor and marks it handled so the final sweep does not re-report it.</summary>
    /// <param name="slot">The slot that cannot be resolved.</param>
    /// <param name="span">The source extent the diagnostic is anchored at.</param>
    private void ReportUnresolved(AncestorSlot slot, SourceSpan span)
    {
        if(resolvedOrReported.Add(slot))
        {
            ReportError(WellKnownDiagnostics.Jsonata.CannotDeriveAncestor, span, "The parent operator '%' cannot derive an ancestor here.");
        }
    }

    /// <summary>The cap on the number of S0217 diagnostics the final unresolved-parent sweep records, so an adversarial wide-but-shallow <c>%.%.%…</c> within the source-length bound cannot emit an unbounded count of diagnostics; one comfortably above any real path's parent count.</summary>
    private const int MaxUnresolvedParentDiagnostics = 64;

    /// <summary>
    /// The final unresolved-parent sweep: any registered parent slot that no <c>seekParent</c> walk ever
    /// attached to a capturing step (and that was not already errored) cannot be bound to any ancestor — a bare
    /// <c>%</c>, a <c>(%)</c>, a <c>$.%</c>, a <c>library.loans.%.%.%</c> — so it records an S0217. This is the
    /// catch-all that preserves the suite's S0217 cases the leniency previously covered. The number of recorded
    /// diagnostics is capped by <see cref="MaxUnresolvedParentDiagnostics"/>, so a single Error-severity
    /// diagnostic (which the conformance S-code leniency keys on) is always present without flooding the bag.
    /// </summary>
    /// <param name="root">The top-level processed result (its escalated seeking slots also count as unresolved).</param>
    private void ReportUnresolvedParents(ProcessResult root)
    {
        int reported = 0;
        foreach(AncestorSlot slot in ancestry)
        {
            if(!resolvedOrReported.Add(slot))
            {
                continue;
            }

            if(reported >= MaxUnresolvedParentDiagnostics)
            {
                break;
            }

            ReportError(WellKnownDiagnostics.Jsonata.CannotDeriveAncestor, root.Node.Span, "The parent operator '%' cannot derive an ancestor.");
            reported++;
        }
    }

    /// <summary>
    /// Builds a path step from a processed step result: the result's materialised node as the step expression,
    /// marking the step keep-array / cons when the node carries those markers, and carrying the result's pending
    /// ancestry (its seeking slots, plus its own slot when it is a bare parent) onto the step's
    /// <see cref="PathStep.SeekingParent"/> so <see cref="ResolveAncestry"/> can thread a <c>%</c> bubbled up
    /// from inside this step (the verbatim <c>case '.'</c> pushes <c>rest</c> with <c>rest.seekingParent</c>
    /// riding along, which <c>resolveAncestry</c> reads from <c>laststep.seekingParent</c>).
    /// </summary>
    /// <param name="step">The processed step result.</param>
    /// <returns>The path step.</returns>
    private static PathStep StepFromResult(ProcessResult step)
    {
        JsonataExpression node = step.Node;
        PathStep pathStep = node switch
        {
            KeepArrayExpression keepArray => new PathStep { Step = keepArray.Source, KeepArray = true },
            _ => new PathStep { Step = node }
        };

        if(!step.PendingFocus.IsEmpty)
        {
            //A non-path context-focus (@) source folded into a path step as an enclosing path-forming operator
            //wraps the focus-annotated node (the reference's @ annotation becoming a tuple step on wrap).
            pathStep.Focus = step.PendingFocus;
            pathStep.Tuple = true;
        }

        //Carry only the step's BUBBLED ancestry (slots a predicate / sub-expression inside it raised), NOT a
        //bare parent step's own slot: that own slot is added separately by ResolveAncestry's lastParent check
        //(the verbatim 'if(laststep.type === parent) slots.push(laststep.slot)'). Adding it here too would put
        //the slot in the walk twice, and a slot that escalates past the path front (a '%.%' whose grandparent
        //sits above the path, e.g. inside a trailing constructor value) would then be level-incremented twice,
        //over-climbing by one structural step.
        pathStep.SeekingParent.AddRange(step.SeekingParent);

        return pathStep;
    }

    /// <summary>
    /// Materialises a path builder into the tree node it processes to: a <see cref="PathExpression"/> when the
    /// path became a tuple stream (a step bearing a focus / index / ancestor / stage), otherwise the original
    /// nested chain unchanged (the hard zero-regression invariant — a plain path is byte-for-byte preserved).
    /// </summary>
    /// <param name="path">The path builder.</param>
    /// <returns>The materialised node.</returns>
    private static JsonataExpression Materialize(PathBuilder path)
    {
        //Only the actual tuple bindings — a context focus (@), a positional index (#), or a resolved ancestor
        //(%) — latch the path into tuple-stream mode. A predicate stage alone does NOT: the existing evaluator
        //handles a plain predicated path (PredicateExpression) directly in flat mode, so a path whose only
        //"extra" is a migrated predicate stays the original chain (hard zero-regression invariant — the SUB-1
        //PathExpression eval is a stub, and converting every predicated path to it would regress the 1552
        //passing cases). The tuple-aware stage evaluation lands in SUB-2.
        bool isTuple = false;
        foreach(PathStep step in path.Steps)
        {
            if(step.Tuple || !step.Focus.IsEmpty || !step.Index.IsEmpty || step.Ancestor is not null)
            {
                isTuple = true;

                break;
            }
        }

        if(!isTuple)
        {
            //A plain path (no tuple binding) is returned as the original chain unchanged, whether or not it
            //carries a group: the led path{...} group form is already handled by the existing evaluator as an
            //ObjectConstructorExpression over the original source, and a plain predicated path by the existing
            //PredicateExpression path, so the original chain preserves both behaviours exactly.
            return path.Original;
        }

        return new PathExpression(path.Original.Span, path.Steps, path.KeepSingletonArray, path.Group, path.SeekingParent.Count > 0);
    }

    /// <summary>The mutable working form of a path during processing: the flat step list plus the path-level keep-singleton flag, pending ancestry, optional group, and the original chain to fall back to when the path is plain.</summary>
    private sealed class PathBuilder
    {
        /// <summary>Gets the flattened path steps, in source order.</summary>
        public List<PathStep> Steps { get; } = [];

        /// <summary>Gets or sets whether any step kept an array, so the whole path's singleton result stays an array.</summary>
        public bool KeepSingletonArray { get; set; }

        /// <summary>Gets the path's pending ancestry: slots that ran off the front of this path and must be resolved by an enclosing path.</summary>
        public List<AncestorSlot> SeekingParent { get; } = [];

        /// <summary>Gets or sets the trailing group-by constructor attached to the path, or <see langword="null"/> when none.</summary>
        public ObjectConstructorExpression? Group { get; set; }

        /// <summary>Gets or sets the original chain node the path was built from; returned unchanged when the path materialises as a plain (non-tuple) path.</summary>
        public required JsonataExpression Original { get; set; }

        /// <summary>
        /// Seeds a one-step path from a non-path source step (the reference's <c>result = {type:'path',
        /// steps:[lstep]}</c> wrap), carrying the source result's pending ancestry — its seeking slots and its
        /// own slot when it is a bare parent — onto the seeded step's <see cref="PathStep.SeekingParent"/> so a
        /// <c>%</c> bubbled up from inside the source is still resolvable against the enclosing path.
        /// </summary>
        /// <param name="source">The processed source result.</param>
        /// <returns>The seeded path builder.</returns>
        public static PathBuilder FromStep(ProcessResult source)
        {
            PathBuilder path = new() { Original = source.Node };
            path.Steps.Add(StepFromResult(source));

            return path;
        }
    }

    /// <summary>
    /// The processed result of one subtree: the materialised node, the path builder when the subtree is a
    /// path, the pending ancestry slots it is seeking, and whether it is a bare parent (with its slot).
    /// </summary>
    private readonly struct ProcessResult
    {
        /// <summary>Initialises a processed result.</summary>
        /// <param name="path">The path builder when the subtree is a path; otherwise <see langword="null"/>.</param>
        /// <param name="node">The non-path node when the subtree is not a path; ignored when <paramref name="path"/> is set.</param>
        /// <param name="seekingParent">The pending ancestry slots the subtree is seeking.</param>
        /// <param name="isParent">Whether the subtree is a bare parent operator.</param>
        /// <param name="parentSlot">The slot when <paramref name="isParent"/>; otherwise <see langword="null"/>.</param>
        /// <param name="pendingFocus">The pending context-focus (@) variable a non-path source carries; the empty <see cref="Utf8String"/> when none.</param>
        private ProcessResult(PathBuilder? path, JsonataExpression? node, AncestorSlot[] seekingParent, bool isParent, AncestorSlot? parentSlot, Utf8String pendingFocus = default)
        {
            PathInternal = path;
            NodeInternal = node;
            SeekingParent = seekingParent;
            IsParent = isParent;
            ParentSlot = parentSlot;
            PendingFocus = pendingFocus;
        }

        /// <summary>Gets the path builder when the subtree processed to a path; otherwise <see langword="null"/>.</summary>
        public PathBuilder? Path => PathInternal;

        /// <summary>Gets the pending ancestry slots the subtree is seeking.</summary>
        public AncestorSlot[] SeekingParent { get; }

        /// <summary>Gets whether the subtree is a bare parent operator.</summary>
        public bool IsParent { get; }

        /// <summary>Gets the parent slot when the subtree is a bare parent; otherwise <see langword="null"/>.</summary>
        public AncestorSlot? ParentSlot { get; }

        /// <summary>
        /// Gets the pending context-focus (<c>@</c>) variable a non-path source carries until a path-forming
        /// operator folds it onto the seeded step (the reference's <c>@</c> annotation on a bare node); the empty
        /// <see cref="Utf8String"/> when none. It is dropped — inert — when the result is materialised standalone,
        /// so a trailing <c>@</c> on a non-path node (e.g. <c>$@$i</c>) evaluates as the bare node.
        /// </summary>
        public Utf8String PendingFocus { get; }

        /// <summary>Gets the path builder backing field.</summary>
        private PathBuilder? PathInternal { get; }

        /// <summary>Gets the non-path node backing field.</summary>
        private JsonataExpression? NodeInternal { get; }

        /// <summary>Gets the materialised tree node: a path subtree materialises to a tuple <see cref="PathExpression"/> or the original chain; a non-path subtree is its node.</summary>
        public JsonataExpression Node => PathInternal is { } path ? Materialize(path) : NodeInternal!;

        /// <summary>Builds a plain (non-path, non-parent) result carrying no pending ancestry.</summary>
        /// <param name="node">The processed node.</param>
        /// <returns>The result.</returns>
        public static ProcessResult Plain(JsonataExpression node)
        {
            return new ProcessResult(path: null, node, [], isParent: false, parentSlot: null);
        }

        /// <summary>Builds a plain result carrying the given pending ancestry slots.</summary>
        /// <param name="node">The processed node.</param>
        /// <param name="seekingParent">The pending ancestry slots.</param>
        /// <returns>The result.</returns>
        public static ProcessResult PlainSeeking(JsonataExpression node, AncestorSlot[] seekingParent)
        {
            return new ProcessResult(path: null, node, seekingParent, isParent: false, parentSlot: null);
        }

        /// <summary>Builds a path result; its pending ancestry is the path's escalated seeking list (the slots that ran off the front of this path for an enclosing path to resolve).</summary>
        /// <param name="path">The path builder.</param>
        /// <returns>The result.</returns>
        public static ProcessResult FromPath(PathBuilder path)
        {
            return new ProcessResult(path, node: null, [.. path.SeekingParent], isParent: false, parentSlot: null);
        }

        /// <summary>Builds a bare-parent result carrying its slot.</summary>
        /// <param name="node">The parent node.</param>
        /// <param name="slot">The parent's slot.</param>
        /// <returns>The result.</returns>
        public static ProcessResult FromParent(JsonataExpression node, AncestorSlot slot)
        {
            return new ProcessResult(path: null, node, [], isParent: true, parentSlot: slot);
        }

        /// <summary>
        /// Builds a plain (non-path) result carrying a pending context-focus (<c>@</c>) bind, mirroring the
        /// reference's <c>@</c> annotation on a non-path node: the focus rides on the node until a path-forming
        /// operator folds it onto the seeded step, and is inert (dropped) when the node is materialised standalone.
        /// </summary>
        /// <param name="node">The processed source node the focus annotates.</param>
        /// <param name="focus">The context-focus variable's bare name.</param>
        /// <param name="seekingParent">The pending ancestry slots the node is seeking.</param>
        /// <returns>The result.</returns>
        public static ProcessResult PlainFocus(JsonataExpression node, Utf8String focus, AncestorSlot[] seekingParent)
        {
            return new ProcessResult(path: null, node, seekingParent, isParent: false, parentSlot: null, pendingFocus: focus);
        }
    }
}
