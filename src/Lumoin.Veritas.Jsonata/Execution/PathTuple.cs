using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// One tuple in a tuple-stream path (the reference's per-item map <c>{'@': focus, &lt;var&gt;: bound, '!N':
/// ancestor, …}</c>): the current focus the next step iterates over, plus the binding frame that re-materialises
/// the tuple's named <c>@</c> / <c>#</c> bindings and its <c>!N</c> ancestor captures as in-scope variables.
/// </summary>
/// <param name="Focus">The tuple's focus value (the reference's reserved <c>'@'</c> key): the value the next step's expression evaluates against.</param>
/// <param name="Frame">The tuple's binding frame: a descendant of the path's entry frame that carries this tuple's <c>@</c> / <c>#</c> / ancestor bindings (so a later step's expression, predicate, or constructor resolves <c>$o</c> / <c>$i</c> / <c>%</c>), with the entry frame as the ultimate ancestor so outer <c>$x</c> still resolve.</param>
/// <remarks>
/// <para>
/// This is the SUB-2 port of the reference's tuple object plus <c>createFrameFromTuple</c>: rather than copy
/// every key into a fresh frame per step, the bindings live directly on <see cref="Frame"/>, and "evaluate the
/// step under <c>createFrameFromTuple</c>" becomes "evaluate the step under
/// <c>context.WithFocus(Focus).WithFrame(Frame)</c>". A binding is added by opening a child frame
/// (<see cref="JsonataBindingFrame.CreateChild"/>) and <see cref="JsonataBindingFrame.Bind"/>-ing the new
/// key, so a tuple that adds no binding shares its parent's frame and a tuple that does keeps the chain
/// depth-bounded.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</para>
/// </remarks>
internal readonly record struct PathTuple(JsonataValue Focus, JsonataBindingFrame Frame);
