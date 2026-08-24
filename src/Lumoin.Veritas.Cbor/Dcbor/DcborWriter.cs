using System;
using System.Buffers;

namespace Lumoin.Veritas.Cbor.Dcbor;

/// <summary>
/// Writes CBOR data items under the dCBOR profile
/// (<see href="https://datatracker.ietf.org/doc/draft-mcnally-deterministic-cbor/"/>):
/// deterministic encoding (shortest integer form, sorted map keys
/// bytewise), no indefinite-length items, and the dCBOR numeric-reduction
/// rules — integer-valued floats inside the range <c>[-2^63, 2^64-1]</c>
/// are emitted as integers, zero values (positive, negative, and integer)
/// converge on the integer 0, and NaN / infinity are emitted in their
/// canonical half-precision forms. Unlike DRISL, dCBOR allows
/// integer-keyed maps and arbitrary tags.
/// </summary>
public sealed class DcborWriter
{
    private readonly CborWriter inner;

    /// <summary>Initialises a new <see cref="DcborWriter"/> writing into <paramref name="destination"/>.</summary>
    /// <param name="destination">The destination buffer writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <c>null</c>.</exception>
    public DcborWriter(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        inner = new CborWriter(destination, DcborDefaults.CreateOptions());
    }

    /// <summary>Gets the total number of bytes emitted to the destination.</summary>
    public int BytesWritten => inner.BytesWritten;

    /// <summary>Resets the writer's internal state so it can be reused.</summary>
    public void Reset() => inner.Reset();

    /// <summary>Writes an unsigned 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt64(ulong value) => inner.WriteUInt64(value);

    /// <summary>Writes a signed 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt64(long value) => inner.WriteInt64(value);

    /// <summary>Writes a signed 32-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt32(int value) => inner.WriteInt32(value);

    /// <summary>Writes a byte string.</summary>
    /// <param name="value">The bytes to write.</param>
    public void WriteByteString(ReadOnlySpan<byte> value) => inner.WriteByteString(value);

    /// <summary>Writes a UTF-8 text string.</summary>
    /// <param name="value">The string to encode and write.</param>
    public void WriteTextString(string value) => inner.WriteTextString(value);

    /// <summary>Writes a UTF-8 text string.</summary>
    /// <param name="value">The characters to encode and write.</param>
    public void WriteTextString(ReadOnlySpan<char> value) => inner.WriteTextString(value);

    /// <summary>Writes a definite-length array introducer.</summary>
    /// <param name="length">The exact item count.</param>
    public void WriteStartArray(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        inner.WriteStartArray(length);
    }

    /// <summary>Closes the topmost array.</summary>
    public void WriteEndArray() => inner.WriteEndArray();

    /// <summary>Writes a definite-length map introducer.</summary>
    /// <param name="length">The exact key/value pair count.</param>
    public void WriteStartMap(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        inner.WriteStartMap(length);
    }

    /// <summary>Closes the topmost map.</summary>
    public void WriteEndMap() => inner.WriteEndMap();

    /// <summary>Writes a Boolean.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteBoolean(bool value) => inner.WriteBoolean(value);

    /// <summary>Writes the CBOR null value.</summary>
    public void WriteNull() => inner.WriteNull();

    /// <summary>Writes a CBOR tag.</summary>
    /// <param name="tag">The tag to write; the next data item is the tagged content.</param>
    public void WriteTag(CborTag tag) => inner.WriteTag(tag);

    /// <summary>
    /// Writes a floating-point value under the dCBOR numeric-reduction
    /// rules (draft-mcnally-deterministic-cbor §2.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NaN</b> is emitted as the canonical half-precision quiet NaN
    /// <c>0xF9 7E 00</c>.
    /// </para>
    /// <para>
    /// <b>+/- infinity</b> is emitted as canonical half-precision
    /// (<c>0xF9 7C 00</c> for <c>+Inf</c>, <c>0xF9 FC 00</c> for
    /// <c>-Inf</c>).
    /// </para>
    /// <para>
    /// <b>Finite integer-valued floats</b> whose value lies in
    /// <c>[-2^63, 2^64-1]</c> are emitted as the equivalent integer
    /// (major type 0 or 1). This collapses <c>0</c>, <c>0.0</c>, and
    /// <c>-0.0</c> onto the byte sequence <c>0x00</c>.
    /// </para>
    /// <para>
    /// <b>Other finite values</b> are emitted using the shortest IEEE 754
    /// form that round-trips losslessly (half / single / double), matching
    /// the deterministic-encoding rule in RFC 8949 §4.2.2.
    /// </para>
    /// </remarks>
    /// <param name="value">The value to write.</param>
    public void WriteDouble(double value)
    {
        if(double.IsNaN(value) || double.IsInfinity(value))
        {
            //CDE mode in the inner writer emits NaN as 0xF9 7E 00,
            //+inf as 0xF9 7C 00, and -inf as 0xF9 FC 00 — the dCBOR
            //canonical forms.
            inner.WriteDouble(value);
            return;
        }

        if(TryReduceToInteger(value, out long signedInt, out ulong unsignedInt, out bool isUnsigned))
        {
            if(isUnsigned)
            {
                inner.WriteUInt64(unsignedInt);
            }
            else
            {
                inner.WriteInt64(signedInt);
            }
            return;
        }

        inner.WriteDouble(value);
    }

    private static bool TryReduceToInteger(double value, out long signed, out ulong unsigned, out bool isUnsigned)
    {
        signed = 0;
        unsigned = 0;
        isUnsigned = false;

        //Non-integer-valued floats are kept as floats. Math.Floor(NaN) is
        //NaN, but NaN is filtered out by the caller, so this comparison is
        //safe here.
        if(Math.Floor(value) != value)
        {
            return false;
        }

        //Negative-zero compares equal to positive zero under ==, so it
        //naturally folds into the unsigned branch with value 0.
        if(value >= 0.0)
        {
            //(double)ulong.MaxValue rounds up to 2^64 in double form, so a
            //value equal to or above 2^64 cannot be represented as ulong.
            //Use the exact double-precision rendering of 2^64 as the
            //exclusive upper bound.
            const double TwoToTheSixtyFour = 18446744073709551616.0;
            if(value >= TwoToTheSixtyFour)
            {
                return false;
            }
            unsigned = (ulong)value;
            isUnsigned = true;
            return true;
        }

        //long.MinValue is exactly representable as double (-2^63).
        if(value < (double)long.MinValue)
        {
            return false;
        }
        signed = (long)value;
        return true;
    }
}
