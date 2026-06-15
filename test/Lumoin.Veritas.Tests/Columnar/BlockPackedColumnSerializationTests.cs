using System;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The block-packed column byte codec: a column round-trips through its byte image — footprint,
/// mode, and every value identical — under both managed and native reconstruction and both
/// block-packed modes; the empty column round-trips; and the Elias-Fano modes report their
/// not-yet-serialized status loudly rather than silently mis-writing.
/// </summary>
[TestClass]
internal sealed class BlockPackedColumnSerializationTests
{
    /// <summary>Decodes a packed column to a flat array, block by block.</summary>
    /// <param name="column">The packed column.</param>
    /// <returns>The decoded values.</returns>
    private static uint[] DecodeAll(BlockPackedColumn column)
    {
        uint[] decoded = new uint[column.Length];
        Span<uint> scratch = new uint[BlockPackedColumn.BlockLength];
        for(int block = 0; block < column.BlockCount; block++)
        {
            int count = column.BlockLengthOf(block);
            column.DecodeBlock(block, scratch);
            scratch[..count].CopyTo(decoded.AsSpan(block << BlockPackedColumn.BlockShift, count));
        }

        return decoded;
    }

    /// <summary>A block-packed column reloads from its byte image identically — footprint, decode, and pointwise reads — in both modes and under both managed and native reconstruction.</summary>
    [TestMethod]
    public void BlockPackedColumnRoundTripsThroughBytes()
    {
        uint[] values = new uint[5000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)((i * 11) + (i & 31));
        }

        foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
        {
            BlockPackedColumn column = BlockPackedColumn.Build(values, mode);
            byte[] image = new byte[column.SerializedSize];
            column.WriteTo(image);

            foreach(ColumnPayloadBacking backing in (ColumnPayloadBacking[])[ColumnPayloadBacking.Managed, ColumnPayloadBacking.NativeAligned])
            {
                BlockPackedColumn restored = BlockPackedColumn.ReadFrom(image, backing);

                Assert.AreEqual(column.Mode, restored.Mode);
                Assert.AreEqual(column.Length, restored.Length);
                Assert.AreEqual(column.PackedByteCount, restored.PackedByteCount);
                CollectionAssert.AreEqual(DecodeAll(column), DecodeAll(restored));

                BlockPackedColumnReader reader = new(restored);
                for(int i = 0; i < values.Length; i++)
                {
                    Assert.AreEqual(values[i], reader.ValueAt(i));
                }
            }
        }
    }

    /// <summary>The empty column round-trips to an empty column with the same footprint.</summary>
    [TestMethod]
    public void EmptyBlockPackedColumnRoundTrips()
    {
        BlockPackedColumn column = BlockPackedColumn.Build([], BlockPackedColumnMode.FrameOfReference);
        byte[] image = new byte[column.SerializedSize];
        column.WriteTo(image);

        BlockPackedColumn restored = BlockPackedColumn.ReadFrom(image);

        Assert.AreEqual(0, restored.Length);
        Assert.AreEqual(column.PackedByteCount, restored.PackedByteCount);
    }

    /// <summary>An Elias-Fano column reports its serialization as not-yet-supported rather than writing a wrong image.</summary>
    [TestMethod]
    public void EliasFanoColumnSerializationIsNotYetSupported()
    {
        uint[] monotone = new uint[2000];
        for(int i = 0; i < monotone.Length; i++)
        {
            monotone[i] = (uint)(i * 5);
        }

        BlockPackedColumn eliasFano = BlockPackedColumn.Build(monotone, BlockPackedColumnMode.EliasFano);

        Assert.AreEqual(BlockPackedColumnMode.EliasFano, eliasFano.Mode);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = eliasFano.SerializedSize);
    }
}
