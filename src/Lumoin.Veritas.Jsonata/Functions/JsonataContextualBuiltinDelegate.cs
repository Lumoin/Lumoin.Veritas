using System.Collections.Generic;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The signature of a context-aware built-in JSONata function: a synchronous mapping from an ordered
/// argument list and the evaluation context to a result value. It mirrors <see cref="JsonataBuiltinDelegate"/>
/// but receives the context, so a built-in that needs the evaluation's captured instant — the date built-ins
/// <c>$now</c> and <c>$millis</c> — reads it from <see cref="JsonataContext.EvaluationMillis"/> rather than
/// from a clock. The named delegate is the only function-typed member in this seam, so the implementations
/// stay <see langword="static"/> method groups with no captured state.
/// </summary>
/// <param name="arguments">The validated argument values in positional order, with context substitution, array singleton-wrapping, and type checking already applied by the signature validator.</param>
/// <param name="context">The evaluation context the built-in reads the captured instant from.</param>
/// <returns>The function's result value (the undefined value when the function produces nothing).</returns>
/// <remarks>See <see href="https://docs.jsonata.org/date-time-functions">the JSONata date/time-functions reference</see>.</remarks>
internal delegate JsonataValue JsonataContextualBuiltinDelegate(IReadOnlyList<JsonataValue> arguments, JsonataContext context);
