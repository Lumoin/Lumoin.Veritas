using System;
using CsCheck;
using Lumoin.Veritas.Cbor;
using BclCbor = System.Formats.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// CsCheck-driven reader-side differential property tests pairing the
/// project's <see cref="CborReader"/> against the BCL
/// <see cref="System.Formats.Cbor.CborReader"/>. For randomly generated
/// CBOR-shaped values encoded via the BCL writer under canonical /
/// CTAP2-canonical modes, both readers must decode the bytes to the same
/// in-memory tree.
/// </summary>
[TestClass]
internal sealed class CborReaderDifferentialPropertyTests
{
    private const long Iterations = 10_000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BothReadersDecodeCanonicalBytesToEqualTrees()
    {
        DifferentialCborValueGenerator.ValueGenerator(maxDepth: 3).Sample(value =>
        {
            byte[] bytes = EncodeWithBcl(value, BclCbor.CborConformanceMode.Canonical);
            DifferentialCborValue mineDecoded = DecodeWithProjectReader(bytes);
            DifferentialCborValue bclDecoded = DecodeWithBclReader(bytes);
            Assert.IsTrue(DifferentialCborValueComparer.AreEqual(bclDecoded, mineDecoded));
        }, iter: Iterations);
    }

    [TestMethod]
    public void BothReadersDecodeCtap2CanonicalBytesToEqualTrees()
    {
        DifferentialCborValueGenerator.ValueGenerator(maxDepth: 3).Sample(value =>
        {
            byte[] bytes = EncodeWithBcl(value, BclCbor.CborConformanceMode.Ctap2Canonical);
            DifferentialCborValue mineDecoded = DecodeWithProjectReader(bytes);
            DifferentialCborValue bclDecoded = DecodeWithBclReader(bytes);
            Assert.IsTrue(DifferentialCborValueComparer.AreEqual(bclDecoded, mineDecoded));
        }, iter: Iterations);
    }

    private static byte[] EncodeWithBcl(DifferentialCborValue value, BclCbor.CborConformanceMode mode)
    {
        BclCbor.CborWriter writer = new(mode);
        WriteValueWithBcl(writer, value);
        return writer.Encode();
    }

    private static DifferentialCborValue DecodeWithProjectReader(byte[] bytes)
    {
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));
        return ReadValueWithProject(reader);
    }

    private static DifferentialCborValue ReadValueWithProject(CborReader reader)
    {
        return reader.PeekState() switch
        {
            CborReaderState.UnsignedInteger => new DifferentialCborInt(reader.ReadInt64()),
            CborReaderState.NegativeInteger => new DifferentialCborInt(reader.ReadInt64()),
            CborReaderState.ByteString => new DifferentialCborBytes(reader.ReadByteString()),
            CborReaderState.TextString => new DifferentialCborText(reader.ReadTextString()),
            CborReaderState.Boolean => new DifferentialCborBool(reader.ReadBoolean()),
            CborReaderState.Null => ReadNullWithProject(reader),
            CborReaderState.StartArray => ReadArrayWithProject(reader),
            CborReaderState.StartMap => ReadMapWithProject(reader),
            CborReaderState s => throw new InvalidOperationException($"Unexpected reader state in differential decode: {s}")
        };
    }

    private static DifferentialCborNull ReadNullWithProject(CborReader reader)
    {
        reader.ReadNull();
        return new DifferentialCborNull();
    }

    private static DifferentialCborArray ReadArrayWithProject(CborReader reader)
    {
        int? count = reader.ReadStartArray();
        if(count is null)
        {
            throw new InvalidOperationException("Differential decode does not handle indefinite-length arrays.");
        }
        DifferentialCborValue[] items = new DifferentialCborValue[count.Value];
        for(int i = 0; i < count.Value; i++)
        {
            items[i] = ReadValueWithProject(reader);
        }
        reader.ReadEndArray();
        return new DifferentialCborArray(items);
    }

    private static DifferentialCborStringMap ReadMapWithProject(CborReader reader)
    {
        int? count = reader.ReadStartMap();
        if(count is null)
        {
            throw new InvalidOperationException("Differential decode does not handle indefinite-length maps.");
        }
        (string Key, DifferentialCborValue Value)[] entries = new (string, DifferentialCborValue)[count.Value];
        for(int i = 0; i < count.Value; i++)
        {
            string key = reader.ReadTextString();
            DifferentialCborValue val = ReadValueWithProject(reader);
            entries[i] = (key, val);
        }
        reader.ReadEndMap();
        return new DifferentialCborStringMap(entries);
    }

    private static DifferentialCborValue DecodeWithBclReader(byte[] bytes)
    {
        BclCbor.CborReader reader = new(bytes);
        return ReadValueWithBcl(reader);
    }

    private static DifferentialCborValue ReadValueWithBcl(BclCbor.CborReader reader)
    {
        return reader.PeekState() switch
        {
            BclCbor.CborReaderState.UnsignedInteger => new DifferentialCborInt(reader.ReadInt64()),
            BclCbor.CborReaderState.NegativeInteger => new DifferentialCborInt(reader.ReadInt64()),
            BclCbor.CborReaderState.ByteString => new DifferentialCborBytes(reader.ReadByteString()),
            BclCbor.CborReaderState.TextString => new DifferentialCborText(reader.ReadTextString()),
            BclCbor.CborReaderState.Boolean => new DifferentialCborBool(reader.ReadBoolean()),
            BclCbor.CborReaderState.Null => ReadNullWithBcl(reader),
            BclCbor.CborReaderState.StartArray => ReadArrayWithBcl(reader),
            BclCbor.CborReaderState.StartMap => ReadMapWithBcl(reader),
            BclCbor.CborReaderState s => throw new InvalidOperationException($"Unexpected BCL reader state in differential decode: {s}")
        };
    }

    private static DifferentialCborNull ReadNullWithBcl(BclCbor.CborReader reader)
    {
        reader.ReadNull();
        return new DifferentialCborNull();
    }

    private static DifferentialCborArray ReadArrayWithBcl(BclCbor.CborReader reader)
    {
        int? count = reader.ReadStartArray();
        if(count is null)
        {
            throw new InvalidOperationException("Differential decode does not handle indefinite-length arrays.");
        }
        DifferentialCborValue[] items = new DifferentialCborValue[count.Value];
        for(int i = 0; i < count.Value; i++)
        {
            items[i] = ReadValueWithBcl(reader);
        }
        reader.ReadEndArray();
        return new DifferentialCborArray(items);
    }

    private static DifferentialCborStringMap ReadMapWithBcl(BclCbor.CborReader reader)
    {
        int? count = reader.ReadStartMap();
        if(count is null)
        {
            throw new InvalidOperationException("Differential decode does not handle indefinite-length maps.");
        }
        (string Key, DifferentialCborValue Value)[] entries = new (string, DifferentialCborValue)[count.Value];
        for(int i = 0; i < count.Value; i++)
        {
            string key = reader.ReadTextString();
            DifferentialCborValue val = ReadValueWithBcl(reader);
            entries[i] = (key, val);
        }
        reader.ReadEndMap();
        return new DifferentialCborStringMap(entries);
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
