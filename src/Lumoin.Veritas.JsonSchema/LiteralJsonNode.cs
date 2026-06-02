using System;
using System.Collections.Generic;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// Synthesises <see cref="JsonNode"/> values that are not drawn from a parsed document. The validator
/// needs these for keywords that assert over a value the instance does not directly carry — most notably
/// <c>propertyNames</c>, which validates each object member name as if it were a string instance.
/// </summary>
internal static class LiteralJsonNode
{
    /// <summary>The navigator that reads a boxed <see cref="string"/> handle as a string-kind node.</summary>
    private static JsonNodeNavigator StringNavigator { get; } = new()
    {
        GetKind = static _ => JsonNodeKind.String,
        GetString = static handle => (string)handle,
        GetBoolean = static _ => throw new InvalidOperationException("A literal string node has no boolean value."),
        GetRawNumber = static _ => throw new InvalidOperationException("A literal string node has no numeric value."),
        TryGetProperty = static (object handle, string name, out JsonNode value) =>
        {
            _ = handle;
            _ = name;
            value = default;

            return false;
        },
        EnumerateArray = static _ => [],
        EnumerateObject = static _ => [],
        Clone = static handle => String((string)handle)
    };

    /// <summary>Creates a string-kind <see cref="JsonNode"/> wrapping the given value.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>A node whose <see cref="JsonNodeOperations"/> kind is <see cref="JsonNodeKind.String"/>.</returns>
    public static JsonNode String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new JsonNode(value, StringNavigator);
    }
}
