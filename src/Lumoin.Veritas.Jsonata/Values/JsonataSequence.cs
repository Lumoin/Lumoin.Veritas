using System.Collections.Generic;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// A JSONata result sequence: zero or more values plus the keep-as-array flag. A path step produces a
/// sequence; <see cref="Normalize"/> collapses it to a single <see cref="JsonataValue"/> for output.
/// </summary>
/// <param name="Items">The values in the sequence, in order.</param>
/// <param name="KeepArray">Whether a singleton or empty sequence stays a JSON array rather than auto-unwrapping.</param>
/// <remarks>
/// <para>
/// The sequence/value duality is the core of JSONata's model: an empty sequence is the "nothing"
/// value, a singleton is interchangeable with its bare value, and a multi-value sequence is a JSON
/// array. The flatten/auto-wrap/auto-unwrap rules live at operator boundaries in the evaluator; this
/// type only carries the items and the keep-as-array distinction.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</para>
/// </remarks>
public readonly record struct JsonataSequence(IReadOnlyList<JsonataValue> Items, bool KeepArray)
{
    /// <summary>Gets the empty sequence — the JSONata "nothing"/undefined value.</summary>
    public static JsonataSequence Nothing { get; } = new([], KeepArray: false);

    /// <summary>
    /// Normalizes the sequence to a single value for output: an empty non-keep-array sequence becomes
    /// <see cref="JsonataValue.Undefined"/>, a singleton non-keep-array sequence becomes its bare value,
    /// and every other case (any keep-array sequence, or two-or-more values) becomes a
    /// <see cref="JsonataValueKind.Array"/>.
    /// </summary>
    /// <returns>The normalized value.</returns>
    public JsonataValue Normalize()
    {
        return (Items.Count, KeepArray) switch
        {
            (0, false) => JsonataValue.Undefined,
            (1, false) => Items[0],
            _ => JsonataValue.Array(Items)
        };
    }
}
