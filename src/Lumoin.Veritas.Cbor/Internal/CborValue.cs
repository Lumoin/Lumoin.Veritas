using System.Collections.Generic;

namespace Lumoin.Veritas.Cbor.Internal;

/// <summary>
/// Minimal in-memory representation of a CBOR data item used by
/// <see cref="CborCanonicalizer"/>. The hierarchy is internal because it
/// is not a general-purpose CBOR DOM — its only role is to hold a parsed
/// CBOR tree long enough to re-emit it through a canonicalising writer.
/// </summary>
internal abstract class CborValue
{
    /// <summary>
    /// Emits this value via <paramref name="writer"/>. The walker is
    /// iterative; this method delegates the actual stack management to
    /// <see cref="CborCanonicalizer"/>.
    /// </summary>
    public abstract void Accept(ICborValueVisitor visitor);
}

internal interface ICborValueVisitor
{
    void Visit(CborNullValue value);
    void Visit(CborUndefinedValue value);
    void Visit(CborBoolValue value);
    void Visit(CborSignedIntValue value);
    void Visit(CborUnsignedIntValue value);
    void Visit(CborLargeNegativeIntValue value);
    void Visit(CborByteStringValue value);
    void Visit(CborTextStringValue value);
    void Visit(CborDoubleValue value);
    void Visit(CborTaggedValue value);
    void Visit(CborArrayValue value);
    void Visit(CborMapValue value);
}

internal sealed class CborNullValue: CborValue
{
    public static CborNullValue Instance { get; } = new();
    private CborNullValue() { }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborUndefinedValue: CborValue
{
    public static CborUndefinedValue Instance { get; } = new();
    private CborUndefinedValue() { }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborBoolValue: CborValue
{
    public CborBoolValue(bool value) => Value = value;
    public bool Value { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

/// <summary>An integer value that fits in <see cref="long"/>; covers
/// both unsigned (major type 0) and negative (major type 1) up to the
/// Int64 range.</summary>
internal sealed class CborSignedIntValue: CborValue
{
    public CborSignedIntValue(long value) => Value = value;
    public long Value { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

/// <summary>An unsigned integer (major type 0) whose value exceeds
/// <see cref="long.MaxValue"/>. Used for the high half of the
/// <see cref="ulong"/> range that signed Int64 cannot represent.</summary>
internal sealed class CborUnsignedIntValue: CborValue
{
    public CborUnsignedIntValue(ulong value) => Value = value;
    public ulong Value { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

/// <summary>A negative integer (major type 1) whose magnitude exceeds
/// what <see cref="long"/> can represent — i.e., values in the range
/// <c>-(2^64) .. -(2^63 + 1)</c>. Stored as the raw CBOR argument; the
/// represented numeric value is <c>-(1 + <see cref="RawArgument"/>)</c>.
/// Used for round-trip canonicalisation of the lowest negative range.</summary>
internal sealed class CborLargeNegativeIntValue: CborValue
{
    public CborLargeNegativeIntValue(ulong rawArgument) => RawArgument = rawArgument;
    public ulong RawArgument { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborByteStringValue: CborValue
{
    public CborByteStringValue(byte[] value) => Value = value;
    public byte[] Value { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborTextStringValue: CborValue
{
    public CborTextStringValue(string value) => Value = value;
    public string Value { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

/// <summary>A floating-point value normalised to <see cref="double"/>.
/// Half- and single-precision values are widened on read; the canonical
/// writer reduces back to the shortest form that preserves the value
/// per RFC 8949 §4.2.2.</summary>
internal sealed class CborDoubleValue: CborValue
{
    public CborDoubleValue(double value) => Value = value;
    public double Value { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborTaggedValue: CborValue
{
    public CborTaggedValue(ulong tag, CborValue inner)
    {
        Tag = tag;
        Inner = inner;
    }
    public ulong Tag { get; }
    public CborValue Inner { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborArrayValue: CborValue
{
    public CborArrayValue(List<CborValue> items) => Items = items;
    public List<CborValue> Items { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}

internal sealed class CborMapValue: CborValue
{
    public CborMapValue(List<KeyValuePair<CborValue, CborValue>> entries) => Entries = entries;
    public List<KeyValuePair<CborValue, CborValue>> Entries { get; }
    public override void Accept(ICborValueVisitor visitor) => visitor.Visit(this);
}
