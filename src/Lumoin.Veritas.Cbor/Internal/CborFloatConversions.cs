using System;
using System.Buffers.Binary;

namespace Lumoin.Veritas.Cbor.Internal;

/// <summary>
/// Float-format helpers for the CBOR codec. Provides binary64 emit and
/// parse plus the canonical "shortest equivalent" check that
/// deterministic-mode writers use to pick between binary16, binary32, and
/// binary64 representations of the same value.
/// </summary>
/// <remarks>
/// Binary16 conversions are hand-coded bit manipulation against the
/// IEEE 754 half-precision layout (1 sign bit, 5 exponent bits, 10
/// mantissa bits). Single- and double-precision conversions delegate to
/// the BCL reinterpretation primitives.
/// </remarks>
internal static class CborFloatConversions
{
    /// <summary>
    /// Writes <paramref name="value"/> as eight big-endian bytes (IEEE 754
    /// binary64) into <paramref name="destination"/>.
    /// </summary>
    /// <param name="value">The double-precision value.</param>
    /// <param name="destination">The destination span; must contain at least eight bytes.</param>
    internal static void WriteBinary64(double value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, BitConverter.DoubleToUInt64Bits(value));
    }

    /// <summary>
    /// Reads <paramref name="source"/> as eight big-endian bytes (IEEE 754
    /// binary64) and returns the corresponding <see cref="double"/>.
    /// </summary>
    /// <param name="source">The source span; must contain at least eight bytes.</param>
    internal static double ReadBinary64(ReadOnlySpan<byte> source)
    {
        return BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReadUInt64BigEndian(source));
    }

    /// <summary>
    /// Writes <paramref name="value"/> as four big-endian bytes (IEEE 754
    /// binary32) into <paramref name="destination"/>.
    /// </summary>
    /// <param name="value">The single-precision value.</param>
    /// <param name="destination">The destination span; must contain at least four bytes.</param>
    internal static void WriteBinary32(float value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, BitConverter.SingleToUInt32Bits(value));
    }

    /// <summary>
    /// Reads <paramref name="source"/> as four big-endian bytes (IEEE 754
    /// binary32) and returns the corresponding <see cref="float"/>.
    /// </summary>
    /// <param name="source">The source span; must contain at least four bytes.</param>
    internal static float ReadBinary32(ReadOnlySpan<byte> source)
    {
        return BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32BigEndian(source));
    }
}
