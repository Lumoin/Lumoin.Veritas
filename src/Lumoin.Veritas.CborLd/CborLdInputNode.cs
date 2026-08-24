using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// The format-neutral document tree shape that the CBOR-LD encoder accepts
/// and the decoder produces. Consumers convert their format-specific tree
/// (a JSON-LD <c>JsonNode</c> from <c>Lumoin.Veritas.JsonLd</c>, or any
/// other source) to this shape at the CBOR-LD boundary.
/// </summary>
/// <remarks>
/// <para>
/// The CBOR-LD project deliberately does not take a dependency on any
/// specific format's document-node type. This sum type is the minimum
/// surface the encoder needs to walk a Linked Data document and emit it as
/// CBOR primitives: nulls, booleans, integers, doubles, strings, arrays,
/// and string-keyed objects. Round-tripping a tree through the encoder
/// and decoder reproduces the same tree shape.
/// </para>
/// <para>
/// Variants are exposed as sealed subclasses with pattern-matchable
/// fields. Use pattern matching at consumption sites: <c>switch(node) {
/// case CborLdInputInt i: ...; case CborLdInputArray a: ...; }</c>.
/// </para>
/// </remarks>
public abstract class CborLdInputNode
{
    private protected CborLdInputNode()
    {
    }
}

/// <summary>The null leaf.</summary>
public sealed class CborLdInputNull: CborLdInputNode
{
    /// <summary>Gets the singleton null instance.</summary>
    public static CborLdInputNull Instance { get; } = new();
}

/// <summary>A Boolean leaf.</summary>
public sealed class CborLdInputBool: CborLdInputNode
{
    /// <summary>Initialises a new Boolean leaf.</summary>
    /// <param name="value">The Boolean value.</param>
    public CborLdInputBool(bool value)
    {
        Value = value;
    }

    /// <summary>Gets the Boolean value.</summary>
    public bool Value { get; }
}

/// <summary>A 64-bit integer leaf.</summary>
public sealed class CborLdInputInt: CborLdInputNode
{
    /// <summary>Initialises a new integer leaf.</summary>
    /// <param name="value">The integer value.</param>
    public CborLdInputInt(long value)
    {
        Value = value;
    }

    /// <summary>Gets the integer value.</summary>
    public long Value { get; }
}

/// <summary>A double-precision floating-point leaf.</summary>
public sealed class CborLdInputDouble: CborLdInputNode
{
    /// <summary>Initialises a new floating-point leaf.</summary>
    /// <param name="value">The value.</param>
    public CborLdInputDouble(double value)
    {
        Value = value;
    }

    /// <summary>Gets the floating-point value.</summary>
    public double Value { get; }
}

/// <summary>A UTF-8 text string leaf.</summary>
public sealed class CborLdInputString: CborLdInputNode
{
    /// <summary>Initialises a new string leaf.</summary>
    /// <param name="value">The string value. Must not be <c>null</c>.</param>
    public CborLdInputString(string value)
    {
        Value = value ?? throw new System.ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the string value.</summary>
    public string Value { get; }
}

/// <summary>An ordered array of nodes.</summary>
public sealed class CborLdInputArray: CborLdInputNode
{
    /// <summary>Initialises a new array node.</summary>
    /// <param name="items">The array items.</param>
    public CborLdInputArray(IReadOnlyList<CborLdInputNode> items)
    {
        Items = items ?? throw new System.ArgumentNullException(nameof(items));
    }

    /// <summary>Gets the array items.</summary>
    public IReadOnlyList<CborLdInputNode> Items { get; }
}

/// <summary>A byte-string leaf. The bytes are held as
/// <see cref="ReadOnlyMemory{T}"/> so that decoded values can alias the
/// source memory without copying; callers that need to retain bytes past
/// the decoder's lifetime must copy explicitly.</summary>
public sealed class CborLdInputBytes: CborLdInputNode
{
    /// <summary>Initialises a new byte-string leaf.</summary>
    /// <param name="value">The byte content. <see cref="ReadOnlyMemory{T}.Empty"/> represents an empty byte string.</param>
    public CborLdInputBytes(ReadOnlyMemory<byte> value)
    {
        Value = value;
    }

    /// <summary>Gets the byte content.</summary>
    public ReadOnlyMemory<byte> Value { get; }
}

/// <summary>A string-keyed object with ordered entries.</summary>
public sealed class CborLdInputMap: CborLdInputNode
{
    /// <summary>Initialises a new map node.</summary>
    /// <param name="entries">The key/value entries in author order.</param>
    public CborLdInputMap(IReadOnlyList<KeyValuePair<string, CborLdInputNode>> entries)
    {
        Entries = entries ?? throw new System.ArgumentNullException(nameof(entries));
    }

    /// <summary>Gets the key/value entries.</summary>
    public IReadOnlyList<KeyValuePair<string, CborLdInputNode>> Entries { get; }
}
