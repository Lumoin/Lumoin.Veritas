using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.JsonPointer;

/// <summary>
/// A node encountered during document traversal, pairing its <see cref="JsonPointer"/> path with its
/// value. Captures the context at each point of a depth-first walk so callers can filter, transform,
/// or collect nodes by path.
/// </summary>
/// <typeparam name="TValue">The node value type.</typeparam>
public readonly struct TraversalNode<TValue>: IEquatable<TraversalNode<TValue>>
{
    /// <summary>Gets the JSON Pointer path to this node from the document root.</summary>
    public JsonPointer Path { get; }

    /// <summary>Gets the value at this node.</summary>
    public TValue Value { get; }

    /// <summary>Gets the depth of this node (0 = root).</summary>
    public int Depth => Path.Depth;

    /// <summary>Gets a value indicating whether this node is the document root.</summary>
    public bool IsRoot => Path.IsRoot;

    /// <summary>Creates a traversal node.</summary>
    /// <param name="path">The path to this node.</param>
    /// <param name="value">The value at this node.</param>
    public TraversalNode(JsonPointer path, TValue value)
    {
        Path = path;
        Value = value;
    }

    /// <summary>Deconstructs this node into its path and value.</summary>
    /// <param name="path">Receives the path.</param>
    /// <param name="value">Receives the value.</param>
    public void Deconstruct(out JsonPointer path, out TValue value)
    {
        path = Path;
        value = Value;
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool Equals(TraversalNode<TValue> other)
    {
        return Path.Equals(other.Path) && EqualityComparer<TValue>.Default.Equals(Value, other.Value);
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TraversalNode<TValue> other && Equals(other);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => HashCode.Combine(Path, Value);

    /// <summary>Determines whether two traversal nodes are equal.</summary>
    /// <param name="left">The first node.</param>
    /// <param name="right">The second node.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(TraversalNode<TValue> left, TraversalNode<TValue> right) => left.Equals(right);

    /// <summary>Determines whether two traversal nodes are not equal.</summary>
    /// <param name="left">The first node.</param>
    /// <param name="right">The second node.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(TraversalNode<TValue> left, TraversalNode<TValue> right) => !left.Equals(right);
}
