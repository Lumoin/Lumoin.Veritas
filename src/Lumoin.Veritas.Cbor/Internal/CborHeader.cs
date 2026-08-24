using System;
using System.Buffers.Binary;

namespace Lumoin.Veritas.Cbor.Internal;

/// <summary>
/// Helpers for emitting and parsing the CBOR initial-byte plus argument
/// encoding defined by RFC 8949 §3. The initial byte packs the major type
/// in its top three bits and an additional information value in its low
/// five bits; the argument follows in 0, 1, 2, 4, or 8 bytes depending on
/// the additional-information value.
/// </summary>
internal static class CborHeader
{
    /// <summary>Argument: 0..23 immediate. Values 24..27 select 1, 2, 4, 8 follow-on bytes.</summary>
    internal const byte ImmediateMax = 23;

    internal const byte AdditionalInfoOneByte = 24;
    internal const byte AdditionalInfoTwoByte = 25;
    internal const byte AdditionalInfoFourByte = 26;
    internal const byte AdditionalInfoEightByte = 27;
    internal const byte AdditionalInfoIndefinite = 31;
    internal const byte BreakStop = 0xFF;

    /// <summary>
    /// Computes the total number of bytes the header for
    /// <paramref name="argument"/> will occupy: the initial byte plus the
    /// argument's follow-on bytes (0, 1, 2, 4, or 8).
    /// </summary>
    /// <param name="argument">The unsigned argument value.</param>
    /// <returns>The header length in bytes.</returns>
    internal static int LengthFor(ulong argument)
    {
        if(argument <= ImmediateMax)
        {
            return 1;
        }
        if(argument <= byte.MaxValue)
        {
            return 2;
        }
        if(argument <= ushort.MaxValue)
        {
            return 3;
        }
        if(argument <= uint.MaxValue)
        {
            return 5;
        }
        return 9;
    }

    /// <summary>
    /// Writes the canonical (length-minimised) header for
    /// <paramref name="major"/> and <paramref name="argument"/> into
    /// <paramref name="destination"/>. <paramref name="destination"/> must
    /// be at least <see cref="LengthFor"/> bytes long.
    /// </summary>
    /// <param name="major">The major type, packed into the top three bits of the initial byte.</param>
    /// <param name="argument">The unsigned argument value.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    internal static int Write(CborMajorType major, ulong argument, Span<byte> destination)
    {
        byte majorBits = (byte)((byte)major << 5);

        if(argument <= ImmediateMax)
        {
            destination[0] = (byte)(majorBits | (byte)argument);
            return 1;
        }
        if(argument <= byte.MaxValue)
        {
            destination[0] = (byte)(majorBits | AdditionalInfoOneByte);
            destination[1] = (byte)argument;
            return 2;
        }
        if(argument <= ushort.MaxValue)
        {
            destination[0] = (byte)(majorBits | AdditionalInfoTwoByte);
            BinaryPrimitives.WriteUInt16BigEndian(destination[1..], (ushort)argument);
            return 3;
        }
        if(argument <= uint.MaxValue)
        {
            destination[0] = (byte)(majorBits | AdditionalInfoFourByte);
            BinaryPrimitives.WriteUInt32BigEndian(destination[1..], (uint)argument);
            return 5;
        }
        destination[0] = (byte)(majorBits | AdditionalInfoEightByte);
        BinaryPrimitives.WriteUInt64BigEndian(destination[1..], argument);
        return 9;
    }

    /// <summary>
    /// Writes the indefinite-length introducer byte for
    /// <paramref name="major"/> into <paramref name="destination"/>. The
    /// initial byte combines the major type's top three bits with
    /// additional information value 31. The caller is responsible for
    /// emitting child items and a terminating <see cref="BreakStop"/>.
    /// </summary>
    /// <param name="major">The major type to introduce. Must be ByteString, TextString, Array, or Map.</param>
    /// <param name="destination">The destination span; must contain at least one byte.</param>
    internal static void WriteIndefiniteIntroducer(CborMajorType major, Span<byte> destination)
    {
        destination[0] = (byte)(((byte)major << 5) | AdditionalInfoIndefinite);
    }
}
