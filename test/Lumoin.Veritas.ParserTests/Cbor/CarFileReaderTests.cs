using System;
using System.IO;
using Lumoin.Veritas.Cbor.Car;
using Lumoin.Veritas.Cid;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Unit coverage for <see cref="CarFileReader"/>'s section walk over a deterministic, in-test CARv1 buffer
/// (no optional real-data fixture required). Focuses on the zero-allocation raw-slice overload of
/// <c>TryReadSection</c> and its agreement with the Cid-materialising overload.
/// </summary>
[TestClass]
internal sealed class CarFileReaderTests
{
    [TestMethod]
    public void RawSectionOverloadAgreesWithCidOverloadAndTryParseDigest()
    {
        (CidValue Cid, byte[] Block)[] sections =
        [
            (MakeCid(CidCodec.Raw, 0x11), [0x01, 0x02, 0x03, 0x04]),
            (MakeCid(CidCodec.Drisl, 0xAB), [0xAA, 0xBB])
        ];
        byte[] car = BuildCar(sections);

        CarFileReader cidReader = new(car);
        cidReader.ReadHeader();
        CarFileReader rawReader = new(car);
        rawReader.ReadHeader();

        foreach((CidValue expectedCid, byte[] expectedBlock) in sections)
        {
            Assert.IsTrue(cidReader.TryReadSection(out CidValue cid, out ReadOnlyMemory<byte> cidBlock));
            Assert.IsTrue(rawReader.TryReadSection(out ReadOnlyMemory<byte> cidBytes, out ReadOnlyMemory<byte> rawBlock));

            //Both overloads return the same block, equal to the source.
            Assert.IsTrue(cidBlock.Span.SequenceEqual(expectedBlock));
            Assert.IsTrue(rawBlock.Span.SequenceEqual(expectedBlock));

            //The raw CID slice parses to the same codec + digest the Cid overload materialised, with no Cid
            //allocation needed to compare digests.
            Assert.IsTrue(CidParser.TryParseDigest(cidBytes.Span, out CidCodec rawCodec, out Digest32 rawDigest));
            Assert.AreEqual(expectedCid.Codec, rawCodec);
            Assert.AreEqual(expectedCid.Digest, rawDigest);
            Assert.AreEqual(cid.Codec, rawCodec);
            Assert.AreEqual(cid.Digest, rawDigest);
        }

        //Both overloads agree on end-of-stream.
        Assert.IsFalse(cidReader.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> _));
        Assert.IsFalse(rawReader.TryReadSection(out ReadOnlyMemory<byte> _, out ReadOnlyMemory<byte> _));
    }

    [TestMethod]
    public void CodecDigestOverloadYieldsCidCodecAndDigestWithoutMaterialisingCid()
    {
        (CidValue Cid, byte[] Block)[] sections =
        [
            (MakeCid(CidCodec.Raw, 0x11), [0x01, 0x02, 0x03, 0x04]),
            (MakeCid(CidCodec.Drisl, 0xAB), [0xAA, 0xBB])
        ];
        byte[] car = BuildCar(sections);

        CarFileReader reader = new(car);
        reader.ReadHeader();

        foreach((CidValue expectedCid, byte[] expectedBlock) in sections)
        {
            Assert.IsTrue(reader.TryReadSection(out CidCodec codec, out Digest32 digest, out ReadOnlyMemory<byte> block));

            //The codec+digest overload yields exactly what the Cid overload would have materialised, and the
            //same block — the shape BlueSky's firehose walk consumes to compare/index by digest.
            Assert.AreEqual(expectedCid.Codec, codec);
            Assert.AreEqual(expectedCid.Digest, digest);
            Assert.IsTrue(block.Span.SequenceEqual(expectedBlock));
        }

        Assert.IsFalse(reader.TryReadSection(out CidCodec _, out Digest32 _, out ReadOnlyMemory<byte> _));
    }

    [TestMethod]
    public void RawSectionOverloadThrowsBeforeReadHeader()
    {
        byte[] car = BuildCar([(MakeCid(CidCodec.Raw, 0x01), [0x01])]);
        CarFileReader reader = new(car);

        Assert.Throws<InvalidOperationException>(() => reader.TryReadSection(out ReadOnlyMemory<byte> _, out ReadOnlyMemory<byte> _));
    }

    /// <summary>
    /// Builds a minimal CARv1 byte buffer: a DAG-CBOR header (<c>{ roots: [], version: 1 }</c>) followed by one
    /// section per pair. Section digests are not verified against blocks (the reader slices, it does not hash),
    /// so arbitrary block bytes are fine.
    /// </summary>
    /// <param name="sections">The CID + block pairs to frame, in order.</param>
    /// <returns>The CARv1 bytes.</returns>
    private static byte[] BuildCar((CidValue Cid, byte[] Block)[] sections)
    {
        //DAG-CBOR map(2) { "roots": [], "version": 1 } in canonical key order (roots before version). 17 bytes.
        byte[] header =
        [
            0xA2,
            0x65, 0x72, 0x6F, 0x6F, 0x74, 0x73,             //"roots"
            0x80,                                           //[]
            0x67, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E, //"version"
            0x01                                            //1
        ];

        using MemoryStream stream = new();
        WriteVarint(stream, (ulong)header.Length);
        stream.Write(header);
        foreach((CidValue cid, byte[] block) in sections)
        {
            byte[] cidBytes = CidFormatter.ToBytes(cid);
            WriteVarint(stream, (ulong)(cidBytes.Length + block.Length));
            stream.Write(cidBytes);
            stream.Write(block);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Writes an unsigned LEB128 varint, the length prefix CARv1 uses for the header and each section.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteVarint(Stream stream, ulong value)
    {
        while(value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    /// <summary>
    /// Builds a CID with the given codec and a 32-byte digest filled with <paramref name="fill"/>.
    /// </summary>
    /// <param name="codec">The CID codec.</param>
    /// <param name="fill">The byte to fill the digest with.</param>
    /// <returns>The CID.</returns>
    private static CidValue MakeCid(CidCodec codec, byte fill)
    {
        Span<byte> digest = stackalloc byte[Digest32.Size];
        digest.Fill(fill);

        return new CidValue { Codec = codec, Digest = Digest32.FromSpan(digest) };
    }
}
