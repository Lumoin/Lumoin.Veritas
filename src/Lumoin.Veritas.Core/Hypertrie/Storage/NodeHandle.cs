using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// An opaque handle into a <see cref="NodeStore"/>'s indexed node
/// storage. Wraps a single <see cref="uint"/>; the storage layer
/// resolves handles to <see cref="HypertrieNode"/> values via
/// <see cref="NodeStore.GetByHandle(NodeHandle)"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sentinel.</b> <see cref="None"/> uses the encoded value <c>0</c>.
/// <c>default(NodeHandle)</c> equals <see cref="None"/>; uninitialised
/// handle slots in freshly-allocated arrays are safely absent by
/// construction. <see cref="NodeStore"/> assigns external handles
/// starting at <c>1</c>; <c>0</c> is reserved for this sentinel.
/// </para>
/// <para>
/// <b>Conversion semantics.</b> Conversions in both directions are
/// deliberate; there are no implicit operators. A raw
/// <see cref="uint"/> becomes a <see cref="NodeHandle"/> only via
/// <see cref="FromEncoded(uint)"/> or the explicit constructor;
/// the encoded value is read via <see cref="Encoded"/>.
/// </para>
/// <para>
/// <b>Depth-1 EdgeMap child slots.</b> At depth-1 leaves the
/// EdgeMap's child slot is structurally present but semantically
/// unused — the parent's depth alone determines that triples are
/// emitted directly from the edge map's keys, without descending.
/// Depth-1 slots therefore hold <see cref="None"/>; depth-2 and
/// depth-3 slots hold real handles.
/// </para>
/// </remarks>
/// <param name="Encoded">The raw encoded handle.</param>
[DebuggerDisplay("NodeHandle({Encoded})")]
public readonly record struct NodeHandle(uint Encoded): IComparable<NodeHandle>, IComparable
{
    /// <summary>
    /// A sentinel representing the absence of a node, encoded as <c>0</c>.
    /// Equal to <c>default(NodeHandle)</c>.
    /// </summary>
    public static NodeHandle None { get; } = new(0);

    /// <summary>
    /// Creates a <see cref="NodeHandle"/> from a raw encoded value.
    /// </summary>
    /// <param name="encoded">The raw encoded handle.</param>
    /// <returns>The wrapped handle.</returns>
    public static NodeHandle FromEncoded(uint encoded) => new(encoded);

    /// <summary>
    /// Returns <c>true</c> when this handle is the <see cref="None"/> sentinel.
    /// </summary>
    public bool IsNone => Encoded == 0;

    /// <summary>Orders handles by their encoded value.</summary>
    public int CompareTo(NodeHandle other) => Encoded.CompareTo(other.Encoded);

    /// <inheritdoc/>
    public int CompareTo(object? obj)
    {
        if(obj is null)
        {
            return 1;
        }

        if(obj is NodeHandle other)
        {
            return CompareTo(other);
        }

        throw new ArgumentException($"Object must be of type {nameof(NodeHandle)}.", nameof(obj));
    }

    /// <summary>Returns <c>true</c> if <paramref name="left"/> precedes <paramref name="right"/>.</summary>
    public static bool operator <(NodeHandle left, NodeHandle right) => left.Encoded < right.Encoded;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> precedes or equals <paramref name="right"/>.</summary>
    public static bool operator <=(NodeHandle left, NodeHandle right) => left.Encoded <= right.Encoded;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> follows <paramref name="right"/>.</summary>
    public static bool operator >(NodeHandle left, NodeHandle right) => left.Encoded > right.Encoded;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> follows or equals <paramref name="right"/>.</summary>
    public static bool operator >=(NodeHandle left, NodeHandle right) => left.Encoded >= right.Encoded;

    /// <inheritdoc/>
    public override string ToString() => Encoded.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
