using System.Diagnostics;

namespace Lumoin.Veritas.Json;

/// <summary>
/// A dispatch table of static method groups supplied by an adapter to read JSON
/// values from opaque handles. A single navigator instance is shared across every
/// <see cref="JsonNode"/> the adapter produces.
/// </summary>
/// <remarks>
/// <para>
/// The navigator separates JSON-LD library code from any specific JSON parser.
/// The library calls navigator delegates to inspect handles; the adapter unboxes
/// the handle to its concrete type and returns a value the library understands.
/// Adapters typically construct one navigator instance, populated from static
/// method groups, and reuse it across every node they produce.
/// </para>
/// <para>
/// Every property is <see langword="required"/>: an adapter must supply all eight
/// delegates, with no implicit defaults. Missing a delegate is a compile-time
/// error rather than a runtime null-reference at the first call site that needs it.
/// </para>
/// </remarks>
[DebuggerDisplay("JsonNodeNavigator")]
public sealed class JsonNodeNavigator
{
    /// <summary>
    /// Gets the delegate that returns a node's <see cref="JsonNodeKind"/>.
    /// </summary>
    public required GetNodeKindDelegate GetKind { get; init; }

    /// <summary>
    /// Gets the delegate that returns the decoded string value of a string-kind node.
    /// </summary>
    public required GetStringValueDelegate GetString { get; init; }

    /// <summary>
    /// Gets the delegate that returns the boolean value of a true-kind or false-kind node.
    /// </summary>
    public required GetBooleanValueDelegate GetBoolean { get; init; }

    /// <summary>
    /// Gets the delegate that returns the raw lexical form of a number-kind node.
    /// </summary>
    public required GetRawNumberDelegate GetRawNumber { get; init; }

    /// <summary>
    /// Gets the delegate that locates a named property in an object-kind node.
    /// </summary>
    public required TryGetPropertyDelegate TryGetProperty { get; init; }

    /// <summary>
    /// Gets the delegate that yields the elements of an array-kind node.
    /// </summary>
    public required EnumerateArrayDelegate EnumerateArray { get; init; }

    /// <summary>
    /// Gets the delegate that yields the properties of an object-kind node.
    /// </summary>
    public required EnumerateObjectDelegate EnumerateObject { get; init; }

    /// <summary>
    /// Gets the delegate that produces a lifetime-independent copy of a node.
    /// </summary>
    public required CloneNodeDelegate Clone { get; init; }
}
