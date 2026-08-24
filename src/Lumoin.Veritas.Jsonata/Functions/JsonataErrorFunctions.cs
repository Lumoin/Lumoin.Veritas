using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The error-raising built-in functions: <c>$error</c>, which always raises a user error, and
/// <c>$assert</c>, which raises a user error when its boolean condition is false and is otherwise the
/// undefined value. Both accept an optional message; an absent message falls back to a fixed default.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/other-functions">the JSONata other-functions reference</see>.</remarks>
internal static class JsonataErrorFunctions
{
    /// <summary>The message <c>$error</c> raises when none is supplied.</summary>
    private const string DefaultErrorMessage = "$error() function evaluated";

    /// <summary>The message a failed <c>$assert</c> raises when none is supplied.</summary>
    private const string DefaultAssertMessage = "$assert() statement failed";

    /// <summary>The error-raising built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("error"), InvokeError, JsonataSignature.Parse("<s?:x>")),
        new JsonataBuiltinFunction(Utf8Strings.From("assert"), InvokeAssert, JsonataSignature.Parse("<bs?:x>"))
    ];

    /// <summary>
    /// <c>$error([message])</c>: always raises the user error D3137 with the supplied message, or a fixed
    /// default when the message is undefined. The signature accepts only a string or the undefined value, so a
    /// non-string message (a number or null) is the T0410 argument error before this body runs.
    /// </summary>
    /// <param name="arguments">The argument list; the optional message is the first argument.</param>
    /// <returns>Never returns; always throws.</returns>
    /// <exception cref="JsonataErrorException">Always — the user error (code D3137).</exception>
    private static JsonataValue InvokeError(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue message = First(arguments);

        throw new JsonataErrorException(WellKnownJsonataErrors.UserError, null, message.Kind == JsonataValueKind.String ? message.AsString : DefaultErrorMessage);
    }

    /// <summary>
    /// <c>$assert(condition[, message])</c>: when the boolean condition is true the result is undefined;
    /// otherwise the user error D3141 is raised with the supplied message, or a fixed default when the message
    /// is undefined. The signature requires a boolean condition, so a non-boolean (a number or null) is the
    /// T0410 argument error before this body runs.
    /// </summary>
    /// <param name="arguments">The argument list; the boolean condition is the first argument, the optional message the second.</param>
    /// <returns>The undefined value when the condition holds.</returns>
    /// <exception cref="JsonataErrorException">The condition is not true — the assertion failed (code D3141).</exception>
    private static JsonataValue InvokeAssert(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue condition = First(arguments);
        if(condition.Kind == JsonataValueKind.Boolean && condition.AsBoolean)
        {
            return JsonataValue.Undefined;
        }

        JsonataValue message = arguments.Count > 1 ? arguments[1] : JsonataValue.Undefined;

        throw new JsonataErrorException(WellKnownJsonataErrors.AssertionFailed, null, message.Kind == JsonataValueKind.String ? message.AsString : DefaultAssertMessage);
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
