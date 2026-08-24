using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata.Ast;

/// <summary>
/// One ancestor (<c>%</c> parent) slot resolved by the post-parse ancestry pass. A bare <c>%</c> in the
/// source becomes a <see cref="ParentExpression"/> owning a fresh slot; the ancestry pass walks the path
/// steps backward, decrements <see cref="Level"/> across each structural step, and — when the level reaches
/// zero — attaches the slot to the earlier <see cref="PathStep"/> that must capture the incoming focus under
/// the slot's <see cref="Label"/>. <see cref="Label"/> is the single rendezvous key: the capturing step
/// stores the parent value under the reserved tuple key derived from it, and the <see cref="ParentExpression"/>
/// reads it back through the same key.
/// </summary>
/// <remarks>
/// <para>
/// The type is a mutable reference class, not a record, because the ancestry pass mutates the level as the
/// seek walks (each contiguous <c>%</c> bumps it up, each structural step bumps it down) and may rewrite the
/// label when two slots land on the same capturing step (label reuse). Only <see cref="Label"/> is read at
/// evaluation time; <see cref="Level"/> and <see cref="Index"/> are compile-time bookkeeping.
/// </para>
/// <para>JSONata parent operator <c>%</c>. See <see href="https://docs.jsonata.org/path-operators#navigate-to-the-parent">the JSONata path-operators reference</see>.</para>
/// </remarks>
[DebuggerDisplay("slot !{Label} level={Level} index={Index}")]
internal sealed class AncestorSlot
{
    /// <summary>Gets or sets the slot's label: the small non-negative integer <c>N</c> whose reserved tuple key <c>"!"+N</c> the captured ancestor value is stored under. Rewritten to share a key when two slots land on the same capturing step.</summary>
    public int Label { get; set; }

    /// <summary>Gets or sets how many structural steps up the slot reaches; starts at one for a single <c>%</c>, rises by one per contiguous <c>%</c>, and falls by one per structural step the ancestry seek crosses, until it reaches zero at the capturing step.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the slot's index in the ancestry registry, used to rewrite a reused label so multiple <c>%</c> on the same step share a single tuple key.</summary>
    public int Index { get; set; }

    /// <summary>
    /// Returns the reserved binding-frame key a slot's captured ancestor value is stored under: <c>"!"+label</c>.
    /// This is the single rendezvous key between the capturing step (which binds the parent value at this key)
    /// and the reading <see cref="ParentExpression"/> (which looks it up). A user <c>$name</c> can never begin
    /// with <c>!</c>, so the key cannot collide with a user variable.
    /// </summary>
    /// <param name="label">The slot's label.</param>
    /// <returns>The reserved frame key.</returns>
    public static Utf8String ReservedKey(int label)
    {
        return Utf8Strings.From(string.Concat("!", label.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}

/// <summary>
/// The raw context bind <c>source@$variable</c> the parser emits in led position before the ancestry pass
/// rewrites it. The <c>@</c> binds the source step's focus under the named variable while leaving the path's
/// running focus on the source value, latching the rest of the path into tuple-stream mode. The processing
/// pass folds this into the source path's last <see cref="PathStep"/> (setting its
/// <see cref="PathStep.Focus"/> and marking it a tuple step); it never reaches the evaluator.
/// </summary>
/// <param name="Span">The source extent from the source through the bound variable.</param>
/// <param name="Source">The expression whose last step the focus binds against.</param>
/// <param name="Variable">The bound variable's bare name (without the leading <c>$</c>).</param>
/// <remarks>JSONata context-variable binding <c>@</c>. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
[DebuggerDisplay("(@${Variable})")]
internal sealed record ContextBindExpression(SourceSpan Span, JsonataExpression Source, Utf8String Variable) : JsonataExpression(Span);

/// <summary>
/// The raw positional bind <c>source#$variable</c> the parser emits in led position before the ancestry pass
/// rewrites it. The <c>#</c> binds the source step's per-item index under the named variable, latching the
/// rest of the path into tuple-stream mode. The processing pass folds this into the source path's last
/// <see cref="PathStep"/> (setting its <see cref="PathStep.Index"/> — or pushing an index stage after any
/// predicate stages — and marking it a tuple step); it never reaches the evaluator.
/// </summary>
/// <param name="Span">The source extent from the source through the bound variable.</param>
/// <param name="Source">The expression whose last step the index binds against.</param>
/// <param name="Variable">The bound variable's bare name (without the leading <c>$</c>).</param>
/// <remarks>JSONata positional-variable binding <c>#</c>. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
[DebuggerDisplay("(#${Variable})")]
internal sealed record IndexBindExpression(SourceSpan Span, JsonataExpression Source, Utf8String Variable) : JsonataExpression(Span);

/// <summary>
/// The parent operator <c>%</c>: a leaf that, in a tuple-stream path, evaluates to the structural ancestor of
/// the current step. The parser emits it in nud (operand) position carrying a fresh <see cref="AncestorSlot"/>;
/// the ancestry pass resolves the slot against the enclosing path's earlier steps, attaching it to the step
/// that captures the ancestor. At evaluation the node reads the captured value from the current binding frame
/// under the slot's reserved key, or the undefined value when the slot was never bound.
/// </summary>
/// <param name="Span">The source extent covering the <c>%</c>.</param>
/// <param name="Slot">The ancestor slot, resolved by the post-parse ancestry pass.</param>
/// <remarks>JSONata parent operator <c>%</c>. See <see href="https://docs.jsonata.org/path-operators#navigate-to-the-parent">the JSONata path-operators reference</see>.</remarks>
[DebuggerDisplay("(% !{Slot.Label})")]
internal sealed record ParentExpression(SourceSpan Span, AncestorSlot Slot) : JsonataExpression(Span);

/// <summary>The kind of post-step stage a tuple-stream <see cref="PathStep"/> runs after its step expression.</summary>
/// <remarks>JSONata tuple-stream stages. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
internal enum PathStageKind
{
    /// <summary>A predicate filter <c>[ ... ]</c> applied to the post-step tuple stream.</summary>
    Filter,

    /// <summary>A positional index re-binding <c>#$i</c> applied after predicate stages, re-numbering the bound index to the post-filter position.</summary>
    Index
}

/// <summary>
/// One post-step stage of a tuple-stream <see cref="PathStep"/>: a predicate filter or a positional index
/// re-binding. A predicate carries its filter expression in <see cref="Filter"/>; an index stage carries the
/// bound variable name in <see cref="Index"/>. Stages run in order after the step's expression, mirroring the
/// reference parser's migration of trailing predicates and <c>#</c> binds into a step's <c>stages</c> array.
/// </summary>
/// <remarks>
/// The type is a mutable reference class, not a record, to match the reference's mutable stage objects and to
/// keep it uniform with the mutable <see cref="PathStep"/> the ancestry pass threads slots through.
/// </remarks>
[DebuggerDisplay("{Kind}")]
internal sealed class PathStage
{
    /// <summary>Gets or sets the stage kind: a predicate filter or a positional index re-binding.</summary>
    public PathStageKind Kind { get; set; }

    /// <summary>Gets or sets the predicate filter expression for a <see cref="PathStageKind.Filter"/> stage; <see langword="null"/> for an index stage.</summary>
    public JsonataExpression? Filter { get; set; }

    /// <summary>Gets or sets the bound variable's bare name for a <see cref="PathStageKind.Index"/> stage; the empty <see cref="Utf8String"/> for a filter stage.</summary>
    public Utf8String Index { get; set; }
}

/// <summary>
/// One step of a flattened tuple-stream <see cref="PathExpression"/>: the step's expression together with the
/// tuple-stream markers the ancestry pass attached to it. A step latches the path into tuple-stream mode when
/// it carries a context focus (<see cref="Focus"/>), a positional index (<see cref="Index"/>), or a resolved
/// ancestor (<see cref="Ancestor"/>), or when it carries a stage. The flags <see cref="ConsArray"/> /
/// <see cref="KeepArray"/> mirror the reference's per-step <c>consarray</c> / <c>keepArray</c> flags.
/// </summary>
/// <remarks>
/// <para>
/// The type is a mutable reference class, not a record, because the processing pass mutates a step in place as
/// it folds <c>@</c> / <c>#</c> binds and predicate / index stages into it and as the ancestry seek attaches
/// an ancestor slot — exactly as the reference parser mutates its step objects.
/// </para>
/// <para>JSONata tuple-stream path step. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</para>
/// </remarks>
[DebuggerDisplay("step Tuple={Tuple} Focus={Focus} Index={Index}")]
internal sealed class PathStep
{
    /// <summary>Gets or sets the step's expression, evaluated against each incoming focus.</summary>
    public required JsonataExpression Step { get; set; }

    /// <summary>Gets or sets the context-focus variable name a <c>@</c> bound on this step (the reference's <c>step.focus</c>); the empty <see cref="Utf8String"/> when none.</summary>
    public Utf8String Focus { get; set; }

    /// <summary>Gets or sets the positional-index variable name a <c>#</c> bound on this step (the reference's <c>step.index</c>); the empty <see cref="Utf8String"/> when none, or when the index was migrated to an <see cref="PathStageKind.Index"/> stage.</summary>
    public Utf8String Index { get; set; }

    /// <summary>Gets or sets the resolved ancestor slot a <c>%</c> attached to this step (the reference's <c>step.ancestor</c>); <see langword="null"/> when none.</summary>
    public AncestorSlot? Ancestor { get; set; }

    /// <summary>Gets the step's post-step stages, in order — predicate filters and positional index re-bindings migrated onto the step.</summary>
    public List<PathStage> Stages { get; } = [];

    /// <summary>
    /// Gets the parent slots this step is still seeking an ancestor for (the reference's per-step
    /// <c>seekingParent</c>): slots bubbled up from a predicate / sort term so the enclosing path's ancestry
    /// resolution can thread them. Processing-time-only state; it is empty on a fully-resolved step.
    /// </summary>
    public List<AncestorSlot> SeekingParent { get; } = [];

    /// <summary>Gets or sets whether the step is a tuple step (the reference's <c>step.tuple</c>): set by <c>@</c> / <c>#</c> / an attached ancestor / a containing-block seek.</summary>
    public bool Tuple { get; set; }

    /// <summary>
    /// Gets or sets the flattened inner tuple steps a parent seek promoted this block / parenthesised-path step's
    /// content into (the reference's block / path descent making the inner path a tuple <c>PathExpression</c>):
    /// <see langword="null"/> until a <c>%</c> resolves through this container, then the inner path's mutable
    /// step list — the same list the rebuilt keep-tuples inner <see cref="PathExpression"/> holds — so a later
    /// <c>%</c> / <c>%.%</c> resolving through the same container reuses it and attaches its ancestor to the same
    /// inner step. Processing-time-only state.
    /// </summary>
    public List<PathStep>? InnerTupleSteps { get; set; }

    /// <summary>Gets or sets whether the step is an array-constructor step kept whole (the reference's <c>consarray</c>).</summary>
    public bool ConsArray { get; set; }

    /// <summary>Gets or sets whether the step carries a keep-array marker (the reference's per-step <c>keepArray</c>).</summary>
    public bool KeepArray { get; set; }
}

/// <summary>
/// A flattened tuple-stream path: the post-parse ancestry pass's replacement for a nested
/// <see cref="MapExpression"/> chain that contains a <c>@</c>, <c>#</c>, or <c>%</c>. The steps are a flat list
/// (nested <c>.</c> collapsed into one <see cref="Steps"/> array); a step bearing a focus / index / ancestor /
/// stage latches the path into tuple-stream evaluation. A path with none of these markers is NOT emitted as a
/// <see cref="PathExpression"/> — it stays the original nested <see cref="MapExpression"/> chain — so a plain
/// path is byte-for-byte unchanged.
/// </summary>
/// <param name="Span">The source extent of the whole path.</param>
/// <param name="Steps">The flattened path steps, in source order.</param>
/// <param name="KeepSingletonArray">Whether any step carried a keep-array marker, so the whole path's singleton result stays a JSON array (the reference's path-level <c>keepSingletonArray</c>).</param>
/// <param name="Group">The trailing group-by object constructor attached to the path (the led <c>path{ ... }</c> form), or <see langword="null"/> when the path has no group.</param>
/// <param name="CarriesAncestry">Whether the path still carries unresolved ancestry that an enclosing path must resolve (the reference's <c>path.seekingParent</c> escalation), so the tuple stream is kept rather than projected at the path end.</param>
/// <param name="KeepTuples">Whether the path keeps its raw tuple stream at the path end instead of projecting each tuple to its focus (the reference's <c>expr.tuple</c>, set by the parent seek's path / block descent): set ONLY on a NESTED path an enclosing tuple step consumes (e.g. the inner path of a parenthesised <c>(Order.Product)</c> a trailing <c>%</c> resolves through). The outermost path always has this <see langword="false"/> and projects to focuses, so the internal tuple-stream carrier never escapes.</param>
/// <remarks>
/// <para>
/// The tuple-stream evaluation cursor evaluates this node. A path with <see cref="KeepTuples"/> set yields the
/// internal <see cref="Values.JsonataValueKind.TupleStream"/> carrier instead of projecting to focuses, so the
/// enclosing tuple step can adopt each inner tuple's focus and ancestor bindings (the reference's
/// <c>if(res.tupleStream) Object.assign(tuple, res[bb])</c>).
/// </para>
/// <para>JSONata tuple-stream path. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</para>
/// </remarks>
[DebuggerDisplay("(path {Steps.Count})")]
internal sealed record PathExpression(SourceSpan Span, IReadOnlyList<PathStep> Steps, bool KeepSingletonArray, ObjectConstructorExpression? Group, bool CarriesAncestry, bool KeepTuples = false) : JsonataExpression(Span);

/// <summary>
/// A marker wrapping a tuple-stream sort step's <see cref="SortExpression"/> inside a flattened path. The
/// ancestry pass appends one of these as a <see cref="PathStep.Step"/> when an order-by <c>^</c> lands in a
/// path, so the later <c>@</c> / <c>#</c> processing can detect "a bind after a sort" (S0216) by pattern-matching
/// the step's expression. The wrapped <see cref="Sort"/> carries the rewritten order-by terms.
/// </summary>
/// <param name="Span">The source extent of the order-by clause.</param>
/// <param name="Sort">The wrapped order-by expression whose terms were ancestry-processed.</param>
/// <remarks>
/// In SUB-1 a sort step inside a tuple path is a compile-time marker only; the tuple-aware sort evaluation
/// lands in SUB-3. JSONata order-by <c>^</c>. See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting reference</see>.
/// </remarks>
[DebuggerDisplay("(sort-step)")]
internal sealed record SortMarkerExpression(SourceSpan Span, SortExpression Sort) : JsonataExpression(Span);
