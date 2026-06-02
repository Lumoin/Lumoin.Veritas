using System;
using System.Collections.Generic;
using System.Text.Json;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.Json.Stj;

/// <summary>
/// Implements the <see cref="JsonNodeNavigator"/> and
/// <see cref="ParseJsonDelegate"/> contracts on top of <see cref="System.Text.Json"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the bundled JSON-LD adapter. It plugs the JSON-LD pipeline into the
/// .NET base-class-library JSON parser. <see cref="System.Text.Json"/> types
/// are confined to this assembly and never appear in JSON-LD library signatures
/// or in consumer code that uses the JSON-LD pipeline through this adapter.
/// </para>
/// <para>
/// The adapter is purely a dispatch layer: every method takes the opaque handle
/// supplied by the JSON-LD library, casts it to <see cref="JsonElement"/>, and
/// reads the requested property. The cast is the JSON-LD library's promise that
/// it only hands back handles produced by this adapter; no defensive type check
/// is performed.
/// </para>
/// <para>
/// <see cref="Parse(Utf8String)"/> always returns a node detached from any
/// underlying <see cref="JsonDocument"/>. The parsed document is disposed before
/// the method returns and the cloned <see cref="JsonElement"/> carries its own
/// independently-allocated buffer. Callers therefore do not need to manage
/// document lifetime.
/// </para>
/// </remarks>
public static class StjJsonAdapter
{
    /// <summary>
    /// Gets the singleton navigator instance. Every <see cref="JsonNode"/>
    /// produced by this adapter shares this navigator. The instance is built
    /// from static method-group references and captures no state.
    /// </summary>
    public static JsonNodeNavigator Navigator { get; } = new JsonNodeNavigator
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

    /// <summary>
    /// Wraps an existing <see cref="JsonElement"/> as a <see cref="JsonNode"/>
    /// bound to this adapter's navigator.
    /// </summary>
    /// <remarks>
    /// The element's lifetime is the caller's responsibility. If the element
    /// belongs to a <see cref="JsonDocument"/> that may be disposed before the
    /// returned <see cref="JsonNode"/> is finished with, call
    /// <see cref="JsonElement.Clone"/> on the element first.
    /// </remarks>
    /// <param name="element">The element to wrap.</param>
    /// <returns>A <see cref="JsonNode"/> referring to <paramref name="element"/>.</returns>
    public static JsonNode From(JsonElement element)
    {
        return new JsonNode(element, Navigator);
    }

    /// <summary>
    /// Parses a UTF-8 JSON document into a <see cref="JsonNode"/> whose
    /// lifetime is independent of any owning <see cref="JsonDocument"/>.
    /// </summary>
    /// <remarks>
    /// The implementation parses the bytes, clones the root element, and
    /// disposes the parser document. The returned node carries its own buffer
    /// and remains valid for as long as the caller holds a reference to it.
    /// Suitable as a <see cref="ParseJsonDelegate"/>.
    /// </remarks>
    /// <param name="utf8Json">The UTF-8 encoded JSON bytes.</param>
    /// <returns>The parsed root node.</returns>
    /// <exception cref="JsonException">
    /// The input is not valid JSON.
    /// </exception>
    public static JsonNode Parse(Utf8String utf8Json)
    {
        // The document is disposed at the end of the using block; the cloned root remains valid.
        using JsonDocument document = JsonDocument.Parse(utf8Json.Memory);
        JsonElement detachedRoot = document.RootElement.Clone();

        return From(detachedRoot);
    }

    /// <summary>
    /// Maps the underlying <see cref="JsonElement.ValueKind"/> to a
    /// <see cref="JsonNodeKind"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonValueKind.Undefined"/> is reachable only when the caller
    /// passes a <see langword="default"/>(<see cref="JsonElement"/>), which is
    /// a contract violation; the method throws rather than introducing an
    /// undefined node-kind value into the JSON-LD model.
    /// </remarks>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>The corresponding <see cref="JsonNodeKind"/>.</returns>
    private static JsonNodeKind GetKind(object handle)
    {
        JsonElement element = (JsonElement)handle;
        return element.ValueKind switch
        {
            JsonValueKind.Null => JsonNodeKind.Null,
            JsonValueKind.String => JsonNodeKind.String,
            JsonValueKind.Number => JsonNodeKind.Number,
            JsonValueKind.True => JsonNodeKind.True,
            JsonValueKind.False => JsonNodeKind.False,
            JsonValueKind.Object => JsonNodeKind.Object,
            JsonValueKind.Array => JsonNodeKind.Array,
            JsonValueKind.Undefined => throw new InvalidOperationException(
                "An undefined JsonElement was supplied to the JSON-LD adapter. Default JsonElement instances cannot be used as handles."),
            _ => throw new InvalidOperationException(
                $"Unrecognised JsonValueKind '{element.ValueKind}'. The System.Text.Json contract has changed in a way the adapter does not handle.")
        };
    }

    /// <summary>
    /// Returns the decoded .NET string of a string-kind element. STJ guarantees
    /// a non-null value for <see cref="JsonElement.GetString"/> on a
    /// <see cref="JsonValueKind.String"/> element, so the result is asserted
    /// non-null with a clear failure message.
    /// </summary>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>The decoded string.</returns>
    private static string GetString(object handle)
    {
        JsonElement element = (JsonElement)handle;
        return element.GetString()
            ?? throw new InvalidOperationException(
                "A string-kind JsonElement returned a null .NET string from GetString.");
    }

    /// <summary>
    /// Returns the boolean value of a true-kind or false-kind element.
    /// </summary>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>The boolean value.</returns>
    private static bool GetBoolean(object handle)
    {
        JsonElement element = (JsonElement)handle;

        return element.GetBoolean();
    }

    /// <summary>
    /// Returns the raw lexical form of a number-kind element exactly as it
    /// appears in the source document. The caller decides how to interpret
    /// the form; preserving it lets JSON-LD distinguish integer from
    /// floating-point literals when producing typed RDF values.
    /// </summary>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>The raw JSON number lexical form.</returns>
    private static string GetRawNumber(object handle)
    {
        JsonElement element = (JsonElement)handle;

        return element.GetRawText();
    }

    /// <summary>
    /// Locates a property by name in an object-kind element. Wraps STJ's
    /// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> with
    /// <see cref="JsonNode"/>-shaped output so the dispatch surface is
    /// uniform across adapters.
    /// </summary>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">
    /// On success, a <see cref="JsonNode"/> referring to the property value;
    /// on failure, a <see langword="default"/> <see cref="JsonNode"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the property exists; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryGetProperty(object handle, string name, out JsonNode value)
    {
        JsonElement element = (JsonElement)handle;
        if(element.TryGetProperty(name, out JsonElement propertyValue))
        {
            value = From(propertyValue);
            return true;
        }

        value = default;

        return false;
    }

    /// <summary>
    /// Yields every element of an array-kind element in document order, each
    /// wrapped in a <see cref="JsonNode"/>.
    /// </summary>
    /// <remarks>
    /// The <c>yield return</c> form allocates one iterator state machine per
    /// call. For documents with many array-typed values this is a minor but
    /// measurable allocation cost. A future struct-enumerator implementation
    /// could remove the allocation without changing the public contract.
    /// </remarks>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>The array elements as JSON-LD nodes.</returns>
    private static IEnumerable<JsonNode> EnumerateArray(object handle)
    {
        JsonElement element = (JsonElement)handle;
        foreach(JsonElement child in element.EnumerateArray())
        {
            yield return From(child);
        }
    }

    /// <summary>
    /// Yields every property of an object-kind element in document order, each
    /// wrapped in a <see cref="KeyValuePair{TKey, TValue}"/> of property name
    /// and value.
    /// </summary>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>The object properties as name-and-node pairs.</returns>
    private static IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObject(object handle)
    {
        JsonElement element = (JsonElement)handle;
        foreach(JsonProperty property in element.EnumerateObject())
        {
            yield return new KeyValuePair<string, JsonNode>(property.Name, From(property.Value));
        }
    }

    /// <summary>
    /// Produces a copy of the given element with a lifetime independent of
    /// any owning <see cref="JsonDocument"/>. Forwards to
    /// <see cref="JsonElement.Clone"/>, which allocates a fresh self-contained
    /// buffer.
    /// </summary>
    /// <param name="handle">The boxed <see cref="JsonElement"/>.</param>
    /// <returns>A detached clone wrapped as a <see cref="JsonNode"/>.</returns>
    private static JsonNode Clone(object handle)
    {
        JsonElement element = (JsonElement)handle;
        JsonElement detached = element.Clone();

        return From(detached);
    }
}
