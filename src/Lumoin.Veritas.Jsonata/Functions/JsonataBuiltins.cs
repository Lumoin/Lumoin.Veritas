using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The registry of the engine's built-in functions, assembled once from the category classes and looked up
/// by bare name. This is the static root frame of the reference resolver: a named-variable read consults
/// the binding chain first and falls back here on a miss, so a user binding of the same name shadows a
/// built-in. The registry holds pre-wrapped <see cref="JsonataValue"/> function values, so the synchronous
/// built-ins and the higher-order functions (and any future function-value kind) resolve through one map.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/string-functions">the JSONata function reference</see>.</remarks>
internal static class JsonataBuiltins
{
    /// <summary>The bare-name to pre-wrapped function-value map, assembled once from every category's <c>All</c> list.</summary>
    private static Dictionary<Utf8String, JsonataValue> Registry { get; } = BuildRegistry();

    /// <summary>
    /// Resolves a built-in by its bare name (without the leading <c>$</c>) to its pre-wrapped function value.
    /// </summary>
    /// <param name="name">The bare function name to resolve.</param>
    /// <param name="function">On a hit, the wrapped built-in function value; otherwise the undefined value.</param>
    /// <returns><see langword="true"/> when a built-in of that name exists.</returns>
    public static bool TryResolve(Utf8String name, [MaybeNullWhen(false)] out JsonataValue function)
    {
        return Registry.TryGetValue(name, out function);
    }

    /// <summary>Assembles the registry from the category classes' <c>All</c> lists, keyed by bare name, each value pre-wrapped as a function value.</summary>
    /// <returns>The assembled registry.</returns>
    private static Dictionary<Utf8String, JsonataValue> BuildRegistry()
    {
        Dictionary<Utf8String, JsonataValue> registry = [];
        AddBuiltins(registry, JsonataStringFunctions.All);
        AddBuiltins(registry, JsonataNumericFunctions.All);
        AddBuiltins(registry, JsonataAggregationFunctions.All);
        AddBuiltins(registry, JsonataBooleanFunctions.All);
        AddBuiltins(registry, JsonataArrayFunctions.All);
        AddBuiltins(registry, JsonataObjectFunctions.All);
        AddBuiltins(registry, JsonataDateFunctions.All);
        AddBuiltins(registry, JsonataEncodingFunctions.All);
        AddBuiltins(registry, JsonataErrorFunctions.All);
        AddHigherOrder(registry, JsonataHigherOrderFunctions.All);
        AddContextual(registry, JsonataDateFunctions.ContextualAll);
        AddContextual(registry, JsonataEvalFunctions.ContextualAll);
        AddContextual(registry, JsonataArrayFunctions.ContextualAll);

        return registry;
    }

    /// <summary>Adds every synchronous built-in of a category to the registry under its bare name, pre-wrapped as a function value.</summary>
    /// <param name="registry">The registry being assembled.</param>
    /// <param name="functions">The category's built-in functions.</param>
    private static void AddBuiltins(Dictionary<Utf8String, JsonataValue> registry, IReadOnlyList<JsonataBuiltinFunction> functions)
    {
        foreach(JsonataBuiltinFunction function in functions)
        {
            registry[function.Name] = JsonataValue.Function(function);
        }
    }

    /// <summary>Adds every higher-order function to the registry under its bare name, pre-wrapped as a function value.</summary>
    /// <param name="registry">The registry being assembled.</param>
    /// <param name="functions">The higher-order functions.</param>
    private static void AddHigherOrder(Dictionary<Utf8String, JsonataValue> registry, IReadOnlyList<JsonataHigherOrderFunction> functions)
    {
        foreach(JsonataHigherOrderFunction function in functions)
        {
            registry[function.Name] = JsonataValue.Function(function);
        }
    }

    /// <summary>Adds every context-aware built-in to the registry under its bare name, pre-wrapped as a function value.</summary>
    /// <param name="registry">The registry being assembled.</param>
    /// <param name="functions">The context-aware built-in functions.</param>
    private static void AddContextual(Dictionary<Utf8String, JsonataValue> registry, IReadOnlyList<JsonataContextualBuiltinFunction> functions)
    {
        foreach(JsonataContextualBuiltinFunction function in functions)
        {
            registry[function.Name] = JsonataValue.Function(function);
        }
    }
}
