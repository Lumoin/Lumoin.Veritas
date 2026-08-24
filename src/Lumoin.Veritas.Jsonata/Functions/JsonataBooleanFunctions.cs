using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The boolean built-in functions: <c>$boolean</c>, <c>$not</c>, and <c>$exists</c>. The coercion of
/// <c>$boolean</c> and <c>$not</c> is the engine's shared <see cref="JsonataTruthiness.IsTruthy"/>, so a
/// value is boolean-coerced identically whether through an operator or one of these functions.
/// </summary>
/// <remarks>
/// <para>See <see href="https://docs.jsonata.org/boolean-functions">the JSONata boolean-functions reference</see>.</para>
/// </remarks>
internal static class JsonataBooleanFunctions
{
    /// <summary>The boolean built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("boolean"), InvokeBoolean, JsonataSignature.Parse("<x-:b>")),
        new JsonataBuiltinFunction(Utf8Strings.From("not"), InvokeNot, JsonataSignature.Parse("<x-:b>")),
        new JsonataBuiltinFunction(Utf8Strings.From("exists"), InvokeExists, JsonataSignature.Parse("<x:b>"))
    ];

    /// <summary>
    /// <c>$boolean(arg)</c>: casts a value to its JSONata truthiness. An undefined argument yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the value to cast is the first argument.</param>
    /// <returns>The truthiness as a boolean, or undefined for an undefined argument.</returns>
    /// <remarks>
    /// The cast delegates to the engine's shared truthiness. The reference <c>$boolean</c> throws D1001 for a
    /// non-finite number through its numeric guard; this engine never materialises a non-finite number, so
    /// that case is unreachable here and the shared truthiness is faithful for every value the engine can
    /// produce. This is a fragment-relative note, not a behavioural divergence on any reachable value.
    /// </remarks>
    private static JsonataValue InvokeBoolean(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Boolean(JsonataTruthiness.IsTruthy(value));
    }

    /// <summary>
    /// <c>$not(arg)</c>: the logical negation of a value's JSONata truthiness. An undefined argument yields
    /// undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the value to negate is the first argument.</param>
    /// <returns>The negated truthiness as a boolean, or undefined for an undefined argument.</returns>
    private static JsonataValue InvokeNot(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Boolean(!JsonataTruthiness.IsTruthy(value));
    }

    /// <summary>
    /// <c>$exists(arg)</c>: whether a value is defined. This is the one boolean built-in that does not pass
    /// undefined through — an undefined argument yields <see langword="false"/>, and a defined value
    /// (including null and zero) yields <see langword="true"/>.
    /// </summary>
    /// <param name="arguments">The argument list; the value to test is the first argument.</param>
    /// <returns>Whether the argument is defined.</returns>
    private static JsonataValue InvokeExists(IReadOnlyList<JsonataValue> arguments)
    {
        return JsonataValue.Boolean(!First(arguments).IsUndefined);
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
