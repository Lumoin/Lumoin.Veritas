using Lumoin.Veritas.Jsonata.Ast;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// A transformer function value: the closure produced when a <see cref="TransformExpression"/> is evaluated.
/// The three clause expressions and the captured binding frame ARE the closure — the frame is snapshotted at
/// definition time and stored here rather than captured by a C# delegate, so the clauses are later evaluated
/// against exactly the environment the transform was defined in. The value is carried in the
/// <see cref="Values.JsonataValue.Function(object)"/> slot, and applying it (typically through the chain
/// operator <c>~&gt;</c>) deep-clones the argument, navigates <see cref="Pattern"/> over the clone, merges
/// <see cref="Update"/> into each matched object, and removes the keys <see cref="Delete"/> names.
/// </summary>
/// <param name="Pattern">The location expression navigated over the cloned input to the nodes to transform.</param>
/// <param name="Update">The expression, evaluated under each matched node, whose object value is merged into the match.</param>
/// <param name="Delete">The optional expression, evaluated under each matched node, whose string / string-array value names the keys to remove; <see langword="null"/> when the transform has no delete clause.</param>
/// <param name="CapturedFrame">The binding frame snapshotted at definition; the clause expressions evaluate in a fresh child of this frame, so they resolve the variables the transform was defined among.</param>
/// <remarks>See <see href="https://docs.jsonata.org/other-operators#-------transform">the JSONata transform-operator reference</see>.</remarks>
internal sealed record JsonataTransformer(
    JsonataExpression Pattern,
    JsonataExpression Update,
    JsonataExpression? Delete,
    JsonataBindingFrame CapturedFrame);
