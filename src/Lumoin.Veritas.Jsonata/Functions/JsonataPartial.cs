using System.Collections.Generic;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// A partially-applied function value: the result of a call or chain whose argument list carried one or more
/// <c>?</c> placeholders (<c>$add(?, 2)</c>). The value is carried in the
/// <see cref="Lumoin.Veritas.Jsonata.Values.JsonataValue.Function(object)"/> slot beside the user-defined
/// lambda and the built-in, so a partial is usable everywhere a function value is — bound to a variable,
/// passed as an argument, chained through <c>~&gt;</c>, or applied. Applying it fills the placeholder slots
/// in order from the apply-call's arguments and applies the inner <see cref="Procedure"/> to the completed
/// argument list.
/// </summary>
/// <param name="Procedure">The inner function value to apply once the placeholders are filled — a lambda, a built-in, or itself another <see cref="JsonataPartial"/> (a partial over a partial). It has already been validated as a function when the partial was built.</param>
/// <param name="Slots">The evaluated argument list in source order, one entry per argument: a supplied argument carries its evaluated value, and a <c>?</c> placeholder carries <see langword="null"/>. Each <see langword="null"/> slot consumes the next argument supplied when the partial is applied, in order.</param>
/// <remarks>
/// <para>
/// The partial does not capture a C# closure: it is an explicit record carrying the inner procedure value and
/// the evaluated slots, so applying it reconstructs the completed argument list and dispatches through the
/// shared apply path — no enclosing-scope variable is captured by a delegate.
/// </para>
/// <para>JSONata partial function application. See <see href="https://docs.jsonata.org/programming#partial-function-application">the JSONata programming reference</see>.</para>
/// </remarks>
internal sealed record JsonataPartial(JsonataValue Procedure, IReadOnlyList<JsonataValue?> Slots);
