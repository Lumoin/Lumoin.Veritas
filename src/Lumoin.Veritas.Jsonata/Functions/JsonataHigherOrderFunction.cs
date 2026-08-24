using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// A first-class higher-order JSONata function value — <c>$map</c>, <c>$filter</c>, <c>$single</c>, or
/// <c>$reduce</c>. Like a built-in or a lambda it is carried in the
/// <see cref="Lumoin.Veritas.Jsonata.Values.JsonataValue.Function(object)"/> slot, so it is usable everywhere
/// a function value is — bound to a variable, passed as an argument, or chained through <c>~&gt;</c>. Unlike a
/// built-in it has no synchronous delegate: it applies a user function once per element, and our function
/// application schedules a lambda body onto the work stack (the result lands a turn later), so the evaluator
/// drives it through a resident per-element cursor instead. The <see cref="Kind"/> selects which cursor logic
/// runs.
/// </summary>
/// <param name="Name">The bare function name without the leading <c>$</c>, matching how a named variable carries its name.</param>
/// <param name="Kind">Which higher-order array function this value applies.</param>
/// <remarks>See <see href="https://docs.jsonata.org/higher-order-functions">the JSONata higher-order-functions reference</see>.</remarks>
internal sealed record JsonataHigherOrderFunction(
    Utf8String Name,
    HigherOrderKind Kind);
