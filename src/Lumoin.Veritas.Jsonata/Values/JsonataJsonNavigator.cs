using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// Exposes a <see cref="JsonataValue"/> tree through the read-only <see cref="JsonNodeNavigator"/>
/// seam: the handle for every node is the <see cref="JsonataValue"/> itself (boxed once), so a
/// constructed JSONata result flows back into any <see cref="JsonNode"/> consumer with no JSON-bytes
/// intermediate. This mirrors the CBOR-LD navigator pattern.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonataValueKind.Undefined"/> and <see cref="JsonataValueKind.Function"/> have no JSON
/// representation; <see cref="GetKind"/> throws for them. Output paths drop undefined before it
/// reaches the navigator, and a function value is not JSON-serializable.
/// </para>
/// </remarks>
internal static class JsonataJsonNavigator
{
    /// <summary>Gets the shared navigator instance, built from static method groups over the value union.</summary>
    public static JsonNodeNavigator Instance { get; } = new JsonNodeNavigator
    {
        GetKind = GetKind,
        GetString = GetString,
        GetBoolean = GetBoolean,
        GetRawNumber = GetRawNumber,
        TryGetProperty = TryGetProperty,
        EnumerateArray = EnumerateArray,
        EnumerateObject = EnumerateObject,
        Clone = Clone
    };

    /// <summary>Maps a JSONata value's kind to its <see cref="JsonNodeKind"/>; throws for undefined/function.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>The JSON node kind.</returns>
    private static JsonNodeKind GetKind(object handle)
    {
        JsonataValue value = (JsonataValue)handle;

        return value.Kind switch
        {
            JsonataValueKind.Null => JsonNodeKind.Null,
            JsonataValueKind.Boolean => value.AsBoolean ? JsonNodeKind.True : JsonNodeKind.False,
            JsonataValueKind.Number => JsonNodeKind.Number,
            JsonataValueKind.String => JsonNodeKind.String,
            JsonataValueKind.Array => JsonNodeKind.Array,
            JsonataValueKind.Object => JsonNodeKind.Object,
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"A JSONata {value.Kind} value has no JSON node kind."))
        };
    }

    /// <summary>Returns the decoded string of a string-kind value.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>The string value.</returns>
    private static string GetString(object handle)
    {
        return ((JsonataValue)handle).AsString;
    }

    /// <summary>Returns the boolean of a boolean-kind value.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>The boolean value.</returns>
    private static bool GetBoolean(object handle)
    {
        return ((JsonataValue)handle).AsBoolean;
    }

    /// <summary>Returns the shortest round-trip lexical form of a number-kind value, with a lowercase exponent marker to match JSON/JS output.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>The number's raw lexical form.</returns>
    private static string GetRawNumber(object handle)
    {
        return ((JsonataValue)handle).AsNumber.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    /// <summary>Locates a named property in an object-kind value (ordinal, case-sensitive).</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">On success, the property's value; otherwise a default node.</param>
    /// <returns><see langword="true"/> when the property exists.</returns>
    private static bool TryGetProperty(object handle, string name, out JsonNode value)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in ((JsonataValue)handle).AsObject)
        {
            if(string.Equals(entry.Key, name, StringComparison.Ordinal))
            {
                value = new JsonNode(entry.Value, Instance);

                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>Yields the elements of an array-kind value, each wrapped through this navigator.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>The array elements.</returns>
    private static IEnumerable<JsonNode> EnumerateArray(object handle)
    {
        return EnumerateArrayCore(((JsonataValue)handle).AsArray);
    }

    /// <summary>Lazily yields array elements as nodes.</summary>
    /// <param name="items">The array items.</param>
    /// <returns>The array elements as nodes.</returns>
    private static IEnumerable<JsonNode> EnumerateArrayCore(IReadOnlyList<JsonataValue> items)
    {
        foreach(JsonataValue item in items)
        {
            yield return new JsonNode(item, Instance);
        }
    }

    /// <summary>Yields the properties of an object-kind value in insertion order.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>The object properties.</returns>
    private static IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObject(object handle)
    {
        return EnumerateObjectCore(((JsonataValue)handle).AsObject);
    }

    /// <summary>Lazily yields object entries as keyed nodes.</summary>
    /// <param name="entries">The object entries.</param>
    /// <returns>The object properties as keyed nodes.</returns>
    private static IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObjectCore(IReadOnlyList<KeyValuePair<string, JsonataValue>> entries)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in entries)
        {
            yield return new KeyValuePair<string, JsonNode>(entry.Key, new JsonNode(entry.Value, Instance));
        }
    }

    /// <summary>Clones a node; a JSONata value is an independent value carrier, so cloning is identity.</summary>
    /// <param name="handle">The boxed <see cref="JsonataValue"/>.</param>
    /// <returns>An equivalent node.</returns>
    private static JsonNode Clone(object handle)
    {
        return new JsonNode(handle, Instance);
    }
}
