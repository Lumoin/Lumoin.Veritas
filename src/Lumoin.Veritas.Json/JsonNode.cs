using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Json;

/// <summary>
/// A handle to a single node in a JSON document, decoupled from any particular
/// JSON library. The handle carries an opaque payload supplied by an adapter
/// together with the <see cref="JsonNodeNavigator"/> that knows how to read
/// the payload.
/// </summary>
/// <remarks>
/// <para>
/// This type is a consumer's only contact with parsed JSON content. Consumers
/// read values, properties, and array elements through the navigator's
/// delegates, never by referencing types from a specific JSON library. A
/// back end (the <c>System.Text.Json</c>-backed adapter, or an alternative)
/// supplies a navigator together with the handles it produces.
/// </para>
/// <para>
/// Instances are lightweight value-type wrappers. The <see cref="Handle"/> is
/// a reference to whatever the adapter wants to carry — typically a parsed
/// element, but the JSON-LD code never inspects it directly. The
/// <see cref="Navigator"/> is the dispatch table; the same singleton instance
/// is shared across every node produced by a given adapter.
/// </para>
/// <para>
/// A <see langword="default"/> instance has a <see langword="null"/>
/// <see cref="Handle"/> and <see cref="Navigator"/>; it is the value an
/// out-parameter takes when a Try* operation fails. Using a default instance
/// for navigation will throw a <see cref="NullReferenceException"/>.
/// </para>
/// <para>
/// Equality is reference identity on both <see cref="Handle"/> and
/// <see cref="Navigator"/>. Two instances are equal when they refer to the
/// same handle object produced by the same navigator. The library does not
/// attempt deep value comparison of JSON content, and never calls
/// <see cref="object.GetHashCode"/> on the handle directly — some adapters'
/// underlying types (notably <c>System.Text.Json.JsonElement</c>) throw from
/// their <see cref="object.GetHashCode"/> override.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly struct JsonNode: IEquatable<JsonNode>
{
    /// <summary>
    /// Initialises a new node bound to the given handle and navigator.
    /// </summary>
    /// <param name="handle">
    /// The opaque payload understood by <paramref name="navigator"/>.
    /// May not be <see langword="null"/>.
    /// </param>
    /// <param name="navigator">
    /// The dispatch table that reads values from <paramref name="handle"/>.
    /// May not be <see langword="null"/>.
    /// </param>
    public JsonNode(object handle, JsonNodeNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(navigator);

        Handle = handle;
        Navigator = navigator;
    }

    /// <summary>
    /// Gets the opaque payload that the navigator interprets. JSON-LD library
    /// code never inspects this directly; it passes the handle into navigator
    /// delegates which know its concrete type.
    /// </summary>
    public object? Handle { get; }

    /// <summary>
    /// Gets the dispatch table that reads values from <see cref="Handle"/>.
    /// </summary>
    public JsonNodeNavigator? Navigator { get; }

    /// <summary>
    /// Gets the debugger label describing this node. Used by the type's
    /// <see cref="DebuggerDisplayAttribute"/>.
    /// </summary>
    private string DebuggerLabel
        => Handle is null || Navigator is null
            ? "JsonNode (default)"
            : $"JsonNode {{Kind={Navigator.GetKind(Handle)}, Handle={Handle.GetType().Name}}}";

    /// <summary>
    /// Determines whether this node refers to the same handle and navigator
    /// as another node. Equality is reference identity; no deep comparison
    /// of JSON content is performed.
    /// </summary>
    /// <param name="other">The other node.</param>
    /// <returns>
    /// <see langword="true"/> when both nodes share the same handle reference
    /// and the same navigator reference; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(JsonNode other)
    {
        return ReferenceEquals(Handle, other.Handle)
            && ReferenceEquals(Navigator, other.Navigator);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is JsonNode other && Equals(other);
    }

    /// <summary>
    /// Returns a hash code derived from the runtime identities of
    /// <see cref="Handle"/> and <see cref="Navigator"/>. The handle's own
    /// <see cref="object.GetHashCode"/> is bypassed via
    /// <see cref="RuntimeHelpers.GetHashCode(object)"/>, since some adapter
    /// payload types throw from that method.
    /// </summary>
    /// <returns>The combined identity hash.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            RuntimeHelpers.GetHashCode(Handle!),
            RuntimeHelpers.GetHashCode(Navigator!));
    }

    /// <summary>
    /// Determines whether two <see cref="JsonNode"/> instances refer to the
    /// same handle and navigator.
    /// </summary>
    public static bool operator ==(JsonNode left, JsonNode right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="JsonNode"/> instances do not refer
    /// to the same handle and navigator.
    /// </summary>
    public static bool operator !=(JsonNode left, JsonNode right)
    {
        return !left.Equals(right);
    }
}
