using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// A first-class built-in JSONata function value. The value is carried in the
/// <see cref="Lumoin.Veritas.Jsonata.Values.JsonataValue.Function(object)"/> slot beside the user-defined
/// lambda, so a built-in is usable everywhere a function value is — bound to a variable, passed as an
/// argument, or chained through <c>~&gt;</c>.
/// </summary>
/// <param name="Name">The bare function name without the leading <c>$</c>, matching how a named variable carries its name.</param>
/// <param name="Invoke">The named delegate that computes the function's result from its arguments.</param>
/// <param name="Signature">The parsed argument signature the validator applies before the delegate runs, carrying the context-substitution, array singleton-wrapping, and type-checking rules.</param>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal sealed record JsonataBuiltinFunction(
    Utf8String Name,
    JsonataBuiltinDelegate Invoke,
    JsonataSignature Signature);
