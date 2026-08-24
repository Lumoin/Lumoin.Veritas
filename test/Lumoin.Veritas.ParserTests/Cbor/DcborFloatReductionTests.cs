using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.Dcbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Pins the dCBOR float-encoding behaviour of <see cref="DcborWriter"/>
/// against draft-mcnally-deterministic-cbor §2.5 (Numeric Reduction). The
/// rules: NaN canonicalises to half-precision <c>0xF9 7E 00</c>; +/- inf
/// canonicalise to half-precision; integer-valued finite floats whose
/// numeric value lies in <c>[-2^63, 2^64-1]</c> emit as integers; other
/// finite values emit in the shortest IEEE 754 form (RFC 8949 §4.2.2).
/// </summary>
[TestClass]
internal sealed class DcborFloatReductionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NaNEncodesAsCanonicalHalfPrecision()
    {
        byte[] bytes = WriteDouble(double.NaN);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0x7E, 0x00 }, bytes);
    }

    [TestMethod]
    public void PositiveInfinityEncodesAsCanonicalHalfPrecision()
    {
        byte[] bytes = WriteDouble(double.PositiveInfinity);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0x7C, 0x00 }, bytes);
    }

    [TestMethod]
    public void NegativeInfinityEncodesAsCanonicalHalfPrecision()
    {
        byte[] bytes = WriteDouble(double.NegativeInfinity);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0xFC, 0x00 }, bytes);
    }

    [TestMethod]
    public void PositiveZeroEncodesAsIntegerZero()
    {
        byte[] bytes = WriteDouble(0.0);

        Assert.AreSequenceEqual(new byte[] { 0x00 }, bytes);
    }

    [TestMethod]
    public void NegativeZeroEncodesAsIntegerZero()
    {
        byte[] bytes = WriteDouble(-0.0);

        Assert.AreSequenceEqual(new byte[] { 0x00 }, bytes);
    }

    [TestMethod]
    public void SmallPositiveIntegerEncodesAsImmediate()
    {
        byte[] bytes = WriteDouble(5.0);

        Assert.AreSequenceEqual(new byte[] { 0x05 }, bytes);
    }

    [TestMethod]
    public void NegativeIntegerEncodesAsMajorTypeOne()
    {
        //-10 → 0x29 (major type 1, additional info 9, encodes -1 - 9 = -10).
        byte[] bytes = WriteDouble(-10.0);

        Assert.AreSequenceEqual(new byte[] { 0x29 }, bytes);
    }

    [TestMethod]
    public void LargeUnsignedIntegerValuedDoubleEncodesAsUnsignedInteger()
    {
        //2^53 is representable as both double and ulong. Should emit as
        //major type 0 with eight-byte argument.
        const double Value = 9007199254740992.0; //2^53.
        byte[] bytes = WriteDouble(Value);

        //1b = major type 0 with eight-byte argument; the eight bytes are
        //the big-endian unsigned encoding of 2^53.
        Assert.AreEqual((byte)0x1B, bytes[0]);
        Assert.HasCount(9, bytes);
    }

    [TestMethod]
    public void NonIntegerValuedFiniteFloatStaysAsFloat()
    {
        //1.5 is finite, exactly representable in binary16, not integer-valued.
        //Should be emitted as half-precision per the shortest-form rule.
        byte[] bytes = WriteDouble(1.5);

        Assert.AreEqual((byte)0xF9, bytes[0]);
        Assert.HasCount(3, bytes);
    }

    [TestMethod]
    public void NonReducibleFiniteFloatStaysAsBinary64()
    {
        //1.1 cannot be exactly represented in any IEEE 754 width smaller
        //than binary64 and is not integer-valued.
        byte[] bytes = WriteDouble(1.1);

        Assert.AreEqual((byte)0xFB, bytes[0]);
        Assert.HasCount(9, bytes);
    }

    [TestMethod]
    public void IntegerOutsideUnsignedRangeStaysAsFloat()
    {
        //2^64 itself is outside [-2^63, 2^64-1]. It is integer-valued as
        //a double but does not fit in ulong, so dCBOR keeps it as a
        //float.
        const double Value = 18446744073709551616.0; //2^64.
        byte[] bytes = WriteDouble(Value);

        //Should NOT be a major-type-0 / major-type-1 byte.
        Assert.IsTrue(bytes[0] is 0xF9 or 0xFA or 0xFB, $"Expected float marker; got 0x{bytes[0]:X2}.");
    }

    [TestMethod]
    public void RoundTripIntegerReducedDoubleReadsBackAsIntegerOrDouble()
    {
        //The reader returns the integer form for reduced values; callers
        //that need a double back can convert. This test pins the wire
        //shape so callers can decide.
        byte[] bytes = WriteDouble(42.0);

        DcborReader reader = new(bytes);
        Assert.AreEqual(CborReaderState.UnsignedInteger, reader.PeekState());
        Assert.AreEqual(42UL, reader.ReadUInt64());
    }

    [TestMethod]
    public void RoundTripCanonicalNaNReadsBackAsHalfPrecision()
    {
        byte[] bytes = WriteDouble(double.NaN);

        DcborReader reader = new(bytes);
        Assert.AreEqual(CborReaderState.HalfPrecisionFloat, reader.PeekState());
        Half value = reader.ReadHalf();
        Assert.IsTrue(Half.IsNaN(value));
    }

    [TestMethod]
    public void RoundTripCanonicalInfinityReadsBackAsHalfPrecision()
    {
        byte[] bytes = WriteDouble(double.PositiveInfinity);

        DcborReader reader = new(bytes);
        Assert.AreEqual(CborReaderState.HalfPrecisionFloat, reader.PeekState());
        Half value = reader.ReadHalf();
        Assert.IsTrue(Half.IsPositiveInfinity(value));
    }

    private static byte[] WriteDouble(double value)
    {
        ArrayBufferWriter<byte> buffer = new();
        DcborWriter writer = new(buffer);
        writer.WriteDouble(value);
        return buffer.WrittenSpan.ToArray();
    }
}
