using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// One lexical scope in the binding-frame chain: a mutable name-to-value map plus a link to its enclosing
/// frame. A bind writes only this frame's own map; a lookup checks this frame, then walks the parent chain
/// to the root. A block pushes exactly one child frame at entry, so a binding made inside a block does not
/// leak to the enclosing scope and an inner binding shadows a same-named outer one for the block's duration.
/// </summary>
/// <remarks>
/// <para>
/// The dot/map step and the predicate filter do not push a frame — they rebind only the focus — so the
/// binding chain's depth tracks block nesting, not focus nesting. The depth is bounded by
/// <see cref="JsonataLimits.MaxEvaluationDepth"/> so adversarial block nesting throws a catchable
/// <see cref="JsonataEvaluationLimitException"/> rather than overflowing the stack, and the parent-chain
/// lookup is an explicit bounded loop with no recursion.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</para>
/// </remarks>
internal sealed class JsonataBindingFrame
{
    /// <summary>This frame's own bindings, allocated lazily on the first <see cref="Bind"/> so a no-bind block stays cheap; <see langword="null"/> until then.</summary>
    private Dictionary<Utf8String, JsonataValue>? bindings;

    /// <summary>Initializes a frame with the given parent and depth.</summary>
    /// <param name="parent">The enclosing frame, or <see langword="null"/> at the root.</param>
    /// <param name="depth">The frame's depth in the binding chain (the root is 0).</param>
    private JsonataBindingFrame(JsonataBindingFrame? parent, int depth)
    {
        Parent = parent;
        Depth = depth;
    }

    /// <summary>Gets the enclosing frame this one was derived from, or <see langword="null"/> at the root.</summary>
    public JsonataBindingFrame? Parent { get; }

    /// <summary>Gets this frame's depth in the binding chain; the root is 0 and each child is one deeper.</summary>
    public int Depth { get; }

    /// <summary>Creates an empty root frame at depth 0.</summary>
    /// <returns>The root binding frame.</returns>
    public static JsonataBindingFrame CreateRoot()
    {
        return new JsonataBindingFrame(parent: null, depth: 0);
    }

    /// <summary>
    /// Binds a name to a value in this frame only, overwriting any same-name binding already in this
    /// frame's own map (a re-bind in the same frame mutates that frame). The map is allocated on the first
    /// bind.
    /// </summary>
    /// <param name="name">The variable's bare name (without the leading <c>$</c>).</param>
    /// <param name="value">The value to bind.</param>
    public void Bind(Utf8String name, JsonataValue value)
    {
        bindings ??= [];
        bindings[name] = value;
    }

    /// <summary>
    /// Looks up a name, checking this frame's own map first and then walking the parent chain over an
    /// explicit bounded loop (no recursion); the first hit wins.
    /// </summary>
    /// <param name="name">The variable's bare name (without the leading <c>$</c>).</param>
    /// <param name="value">On a hit, the bound value; otherwise the undefined value.</param>
    /// <returns><see langword="true"/> when the name is bound in this frame or some ancestor.</returns>
    public bool TryLookup(Utf8String name, out JsonataValue value)
    {
        //The chain is at most JsonataLimits.MaxEvaluationDepth + 1 frames deep (CreateChild bounds it), so
        //a steps counter bounds the walk independently as a defensive backstop against an unexpected cycle.
        int steps = 0;
        for(JsonataBindingFrame? frame = this; frame is not null; frame = frame.Parent)
        {
            if(frame.bindings is not null && frame.bindings.TryGetValue(name, out value))
            {
                return true;
            }

            steps++;
            if(steps > JsonataLimits.MaxEvaluationDepth + 1)
            {
                throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "JSONata variable lookup walked a binding chain deeper than the maximum depth.");
            }
        }

        value = JsonataValue.Undefined;

        return false;
    }

    /// <summary>
    /// Creates a child frame whose parent is this frame and whose depth is one deeper, bounded by
    /// <see cref="JsonataLimits.MaxEvaluationDepth"/> so adversarial block nesting throws a catchable limit
    /// exception rather than overflowing the stack.
    /// </summary>
    /// <returns>The child frame.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The binding-chain depth bound was exceeded.</exception>
    public JsonataBindingFrame CreateChild()
    {
        int depth = Depth + 1;
        if(depth > JsonataLimits.MaxEvaluationDepth)
        {
            throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, WellKnownJsonataErrors.NonTerminatingRecursion, "JSONata block nesting exceeded the maximum binding-frame depth.");
        }

        return new JsonataBindingFrame(this, depth);
    }
}
