using System;
using System.Buffers;
using CsCheck;
using Lumoin.Veritas.Cbor;
using BclCbor = System.Formats.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// CsCheck-driven writer-side differential property tests pairing the
/// project's <see cref="CborWriter"/> against the BCL
/// <see cref="System.Formats.Cbor.CborWriter"/>. For randomly generated
/// CBOR-shaped values, both writers must produce byte-identical output
/// under the canonical and CTAP2-canonical conformance modes.
/// </summary>
/// <remarks>
/// The grammar lives in <see cref="DifferentialCborValueGenerator"/>; this
/// file holds only the encode-via-each-writer dispatch.
/// </remarks>
[TestClass]
internal sealed class CborDifferentialPropertyTests
{
    private const long Iterations = 10_000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void CanonicalEncodingMatchesBclByteForByte()
    {
        DifferentialCborValueGenerator.ValueGenerator(maxDepth: 3).Sample(value =>
        {
            byte[] mine = EncodeWithProject(value, CborConformanceMode.RfcCanonical);
            byte[] bcl = EncodeWithBcl(value, BclCbor.CborConformanceMode.Canonical);
            Assert.AreSequenceEqual(bcl, mine);
        }, iter: Iterations);
    }

    [TestMethod]
    public void Ctap2CanonicalEncodingMatchesBclByteForByte()
    {
        DifferentialCborValueGenerator.ValueGenerator(maxDepth: 3).Sample(value =>
        {
            byte[] mine = EncodeWithProject(value, CborConformanceMode.Ctap2Canonical);
            byte[] bcl = EncodeWithBcl(value, BclCbor.CborConformanceMode.Ctap2Canonical);
            Assert.AreSequenceEqual(bcl, mine);
        }, iter: Iterations);
    }

    private static byte[] EncodeWithProject(DifferentialCborValue value, CborConformanceMode mode)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(mode));
        WriteValueWithProject(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeWithBcl(DifferentialCborValue value, BclCbor.CborConformanceMode mode)
    {
        BclCbor.CborWriter writer = new(mode);
        WriteValueWithBcl(writer, value);
        return writer.Encode();
    }

    private static void WriteValueWithProject(CborWriter writer, DifferentialCborValue value)
    {
        switch(value)
        {
            case DifferentialCborInt v:
            {
                writer.WriteInt64(v.Value);
                break;
            }
            case DifferentialCborText v:
            {
                writer.WriteTextString(v.Value);
                break;
            }
            case DifferentialCborBytes v:
            {
                writer.WriteByteString(v.Value);
                break;
            }
            case DifferentialCborBool v:
            {
                writer.WriteBoolean(v.Value);
                break;
            }
            case DifferentialCborNull:
            {
                writer.WriteNull();
                break;
            }
            case DifferentialCborArray v:
            {
                writer.WriteStartArray(v.Items.Length);
                foreach(DifferentialCborValue item in v.Items)
                {
                    WriteValueWithProject(writer, item);
                }
                writer.WriteEndArray();
                break;
            }
            case DifferentialCborStringMap v:
            {
                writer.WriteStartMap(v.Entries.Length);
                foreach((string key, DifferentialCborValue val) in v.Entries)
                {
                    writer.WriteTextString(key);
                    WriteValueWithProject(writer, val);
                }
                writer.WriteEndMap();
                break;
            }
            default:
            {
                throw new InvalidOperationException($"Unhandled DifferentialCborValue subtype: {value.GetType().Name}");
            }
        }
    }

    private static void WriteValueWithBcl(BclCbor.CborWriter writer, DifferentialCborValue value)
    {
        switch(value)
        {
            case DifferentialCborInt v:
            {
                writer.WriteInt64(v.Value);
                break;
            }
            case DifferentialCborText v:
            {
                writer.WriteTextString(v.Value);
                break;
            }
            case DifferentialCborBytes v:
            {
                writer.WriteByteString(v.Value);
                break;
            }
            case DifferentialCborBool v:
            {
                writer.WriteBoolean(v.Value);
                break;
            }
            case DifferentialCborNull:
            {
                writer.WriteNull();
                break;
            }
            case DifferentialCborArray v:
            {
                writer.WriteStartArray(v.Items.Length);
                foreach(DifferentialCborValue item in v.Items)
                {
                    WriteValueWithBcl(writer, item);
                }
                writer.WriteEndArray();
                break;
            }
            case DifferentialCborStringMap v:
            {
                writer.WriteStartMap(v.Entries.Length);
                foreach((string key, DifferentialCborValue val) in v.Entries)
                {
                    writer.WriteTextString(key);
                    WriteValueWithBcl(writer, val);
                }
                writer.WriteEndMap();
                break;
            }
            default:
            {
                throw new InvalidOperationException($"Unhandled DifferentialCborValue subtype: {value.GetType().Name}");
            }
        }
    }
}
