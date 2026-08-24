using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// A first-class context-aware built-in JSONata function value. It is carried in the
/// <see cref="Lumoin.Veritas.Jsonata.Values.JsonataValue.Function(object)"/> slot beside
/// <see cref="JsonataBuiltinFunction"/> and the user-defined lambda, so it is usable everywhere a function
/// value is — bound to a variable, passed as an argument, or chained through <c>~&gt;</c>. It differs from
/// <see cref="JsonataBuiltinFunction"/> only in that its delegate also receives the evaluation context, so a
/// built-in that needs the evaluation's captured instant resolves it without reaching a clock.
/// </summary>
/// <param name="Name">The bare function name without the leading <c>$</c>, matching how a named variable carries its name.</param>
/// <param name="Invoke">The named context-aware delegate that computes the function's result from its arguments and the evaluation context.</param>
/// <param name="Signature">The parsed argument signature the validator applies before the delegate runs, carrying the context-substitution, array singleton-wrapping, and type-checking rules.</param>
/// <remarks>See <see href="https://docs.jsonata.org/date-time-functions">the JSONata date/time-functions reference</see>.</remarks>
internal sealed record JsonataContextualBuiltinFunction(
    Utf8String Name,
    JsonataContextualBuiltinDelegate Invoke,
    JsonataSignature Signature);
