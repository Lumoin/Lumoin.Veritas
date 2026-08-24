using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;
using BclCbor = System.Formats.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Pins the float-encoding behaviour of this project's canonical modes
/// against the RFC 8949 §4.1 / §4.2.2 spec, and documents the deliberate
/// divergence from the BCL <see cref="System.Formats.Cbor.CborWriter"/>
/// under its <c>Canonical</c> mode. Per RFC 8949 §4.1, deterministic
/// encoding "MUST use the shortest form that preserves the value", so
/// any value that round-trips losslessly through binary32 must be emitted
/// in five bytes (or three for binary16). BCL's <c>Canonical</c> emits
/// some such values as binary64; this project chooses the spec.
/// </summary>
[TestClass]
internal sealed class CborCanonicalFloatDivergenceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void IntegerValuedDoubleRepresentableAsFloatEncodesAsFiveBytesUnderCanonical()
    {
        //57010512 is exactly representable in IEEE 754 binary32 — the
        //round-trip check ((double)(float)x == x) succeeds. Per RFC 8949
        //§4.1 a deterministic encoder MUST emit it in the shorter form.
        byte[] bytes = WriteWithCanonical(57010512.0);

        //fa = major type 7, additional info 26 (4-byte float follows).
        Assert.AreEqual((byte)0xFA, bytes[0]);
        Assert.HasCount(5, bytes);
    }

    [TestMethod]
    public void IntegerValuedDoubleRepresentableAsFloatEncodesAsFiveBytesUnderCde()
    {
        byte[] bytes = WriteWith(CborConformanceMode.Cde, 57010512.0);

        Assert.AreEqual((byte)0xFA, bytes[0]);
        Assert.HasCount(5, bytes);
    }

    [TestMethod]
    public void OneAndAHalfEncodesAsHalfPrecisionUnderCanonical()
    {
        //1.5 is exactly representable in binary16; canonical encoding
        //emits the three-byte half-precision form (RFC 8949 §A test
        //vector "1.5_1 = f9 3e 00").
        byte[] bytes = WriteWithCanonical(1.5);

        Assert.AreEqual((byte)0xF9, bytes[0]);
        Assert.AreEqual((byte)0x3E, bytes[1]);
        Assert.AreEqual((byte)0x00, bytes[2]);
        Assert.HasCount(3, bytes);
    }

    [TestMethod]
    public void NaNEncodesAsHalfPrecisionUnderCanonical()
    {
        //RFC 8949 §4.1: NaN is represented as 0xF9 7E 00 — the canonical
        //half-precision quiet NaN.
        byte[] bytes = WriteWithCanonical(double.NaN);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0x7E, 0x00 }, bytes);
    }

    [TestMethod]
    public void PositiveInfinityEncodesAsHalfPrecisionUnderCanonical()
    {
        byte[] bytes = WriteWithCanonical(double.PositiveInfinity);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0x7C, 0x00 }, bytes);
    }

    [TestMethod]
    public void NegativeInfinityEncodesAsHalfPrecisionUnderCanonical()
    {
        byte[] bytes = WriteWithCanonical(double.NegativeInfinity);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0xFC, 0x00 }, bytes);
    }

    [TestMethod]
    public void EncodingMatchesBclOnStandaloneIntegerValuedFloat()
    {
        //Empirical pin: for the simple standalone case, this project and
        //BCL agree on float reduction. Earlier writer-differential
        //property failures suggested context-dependent BCL divergence for
        //some values inside larger trees; the standalone case is
        //byte-identical and is captured here as a baseline. If a future
        //BCL change reintroduces a standalone divergence, this test will
        //fail and the float canonicalization investigation should be
        //reopened.
        const double Value = 57010512.0;

        byte[] mine = WriteWithCanonical(Value);
        byte[] bcl = WriteWithBcl(Value, BclCbor.CborConformanceMode.Canonical);

        Assert.AreSequenceEqual(bcl, mine);
    }

    [TestMethod]
    public void NonReducibleDoubleStaysAsBinary64UnderCanonical()
    {
        //1.1 is not exactly representable in binary32 — the round-trip
        //((double)(float)1.1 == 1.1) is false. The canonical writer must
        //therefore keep it as binary64.
        byte[] bytes = WriteWithCanonical(1.1);

        Assert.AreEqual((byte)0xFB, bytes[0]);
        Assert.HasCount(9, bytes);
    }

    private static byte[] WriteWithCanonical(double value)
    {
        return WriteWith(CborConformanceMode.RfcCanonical, value);
    }

    private static byte[] WriteWith(CborConformanceMode mode, double value)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(mode));
        writer.WriteDouble(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteWithBcl(double value, BclCbor.CborConformanceMode mode)
    {
        BclCbor.CborWriter writer = new(mode);
        writer.WriteDouble(value);
        return writer.Encode();
    }
}
