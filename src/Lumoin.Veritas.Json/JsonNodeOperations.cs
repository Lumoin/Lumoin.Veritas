using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Json;

/// <summary>
/// Adds ergonomic dispatch methods on <see cref="JsonNode"/> that delegate to its
/// <see cref="JsonNode.Navigator"/>.
/// </summary>
/// <remarks>
/// <para>
/// These extensions allow JSON-LD library code to read <c>node.Kind</c>,
/// <c>node.TryGetProperty(name, out var value)</c>, and the rest as if they
/// were instance members of <see cref="JsonNode"/>, while keeping the struct
/// itself a pure data carrier. The dispatch is one delegate invocation, the
/// same shape as a virtual call but supplied by the adapter rather than the
/// type system.
/// </para>
/// <para>
/// Every operation here forwards directly to the navigator without inspecting
/// or modifying the handle. The handle's concrete type is the adapter's secret;
/// JSON-LD library code never depends on it.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "The C# 14 extension(JsonNode) block compiles to a synthetic nested type. The nesting is a compiler implementation detail and is not part of the user-facing surface of the containing class.")]
public static class JsonNodeOperations
{
    extension(JsonNode node)
    {
        /// <summary>
        /// Gets the <see cref="JsonNodeKind"/> of this node.
        /// </summary>
        public JsonNodeKind Kind
            => node.Navigator!.GetKind(node.Handle!);

        /// <summary>
        /// Returns the decoded string value of this node.
        /// </summary>
        /// <returns>The decoded string.</returns>
        public string GetString()
            => node.Navigator!.GetString(node.Handle!);

        /// <summary>
        /// Returns the boolean value of this node.
        /// </summary>
        /// <returns>The boolean value.</returns>
        public bool GetBoolean()
            => node.Navigator!.GetBoolean(node.Handle!);

        /// <summary>
        /// Returns the raw lexical form of this number-kind node.
        /// </summary>
        /// <returns>The raw lexical form.</returns>
        public string GetRawNumber()
            => node.Navigator!.GetRawNumber(node.Handle!);

        /// <summary>
        /// Attempts to locate a property by name in this object-kind node.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">
        /// On success, the property's value; on failure, a default
        /// <see cref="JsonNode"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the property exists; otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryGetProperty(string name, out JsonNode value)
            => node.Navigator!.TryGetProperty(node.Handle!, name, out value);

        /// <summary>
        /// Yields the elements of this array-kind node in document order.
        /// </summary>
        /// <returns>The array elements.</returns>
        public IEnumerable<JsonNode> EnumerateArray()
            => node.Navigator!.EnumerateArray(node.Handle!);

        /// <summary>
        /// Yields the properties of this object-kind node in document order.
        /// </summary>
        /// <returns>The object properties.</returns>
        public IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObject()
            => node.Navigator!.EnumerateObject(node.Handle!);

        /// <summary>
        /// Produces a lifetime-independent copy of this node, suitable for
        /// long-term storage outside the original document's scope.
        /// </summary>
        /// <returns>The detached copy.</returns>
        public JsonNode Clone()
            => node.Navigator!.Clone(node.Handle!);
    }
}
