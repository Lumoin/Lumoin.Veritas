using System.Collections.Generic;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The signature of a built-in JSONata function: a synchronous, pure mapping from an ordered argument list
/// to a result value. The named delegate is the only function-typed member in the built-in seam, so the
/// implementations stay <see langword="static"/> method groups with no captured state.
/// </summary>
/// <param name="arguments">The validated argument values in positional order, with context substitution, array singleton-wrapping, and type checking already applied by the signature validator.</param>
/// <returns>The function's result value (the undefined value when the function produces nothing).</returns>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal delegate JsonataValue JsonataBuiltinDelegate(IReadOnlyList<JsonataValue> arguments);
