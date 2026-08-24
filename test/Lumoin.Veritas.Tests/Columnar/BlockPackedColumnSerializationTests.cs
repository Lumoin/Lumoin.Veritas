using System;
using System.Buffers;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The column byte codec: a column round-trips through its byte image — footprint, mode, and
/// every value identical — across every encoding. Block-packed columns round-trip under both
/// managed and native reconstruction and both block-packed modes; the empty column round-trips;
/// and the Elias-Fano and partitioned-Elias-Fano columns round-trip through the succinct
/// sequence codecs, the select samples recomputed on read. The image buffer is pool-rented, not
/// heap-allocated.
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

    /// <summary>Writes a column to a pool-rented image and reads it back; the rented buffer is released before returning, the column having copied its state out.</summary>
    /// <param name="pool">The buffer pool to rent the image from.</param>
    /// <param name="column">The column to round-trip.</param>
    /// <param name="backing">Where a reconstructed block-packed payload lives.</param>
    /// <returns>The reconstructed column.</returns>
    private static BlockPackedColumn RoundTrip(MemoryPool<byte> pool, BlockPackedColumn column, ColumnPayloadBacking backing)
    {
        int size = column.SerializedSize;
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        column.WriteTo(image);

        return BlockPackedColumn.ReadFrom(image, backing);
    }

    /// <summary>A block-packed column reloads from its byte image identically — footprint, decode, and pointwise reads — in both modes and under both managed and native reconstruction.</summary>
    [TestMethod]
    public void BlockPackedColumnRoundTripsThroughBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        uint[] values = new uint[5000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)((i * 11) + (i & 31));
        }

        foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
        {
            BlockPackedColumn column = BlockPackedColumn.Build(values, mode);

            foreach(ColumnPayloadBacking backing in (ColumnPayloadBacking[])[ColumnPayloadBacking.Managed, ColumnPayloadBacking.NativeAligned])
            {
                BlockPackedColumn restored = RoundTrip(pool, column, backing);

                Assert.AreEqual(column.Mode, restored.Mode);
                Assert.AreEqual(column.Length, restored.Length);
                Assert.AreEqual(column.PackedByteCount, restored.PackedByteCount);
                Assert.AreSequenceEqual(DecodeAll(column), DecodeAll(restored));

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
        using VeritasMemoryPool<byte> pool = new();
        BlockPackedColumn column = BlockPackedColumn.Build([], BlockPackedColumnMode.FrameOfReference);

        BlockPackedColumn restored = RoundTrip(pool, column, ColumnPayloadBacking.Managed);

        Assert.AreEqual(0, restored.Length);
        Assert.AreEqual(column.PackedByteCount, restored.PackedByteCount);
    }

    /// <summary>A globally-monotone Elias-Fano column reloads from its byte image identically — footprint, decode, and pointwise reads — the select samples recomputed on read.</summary>
    [TestMethod]
    public void EliasFanoColumnRoundTripsThroughBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        uint[] monotone = new uint[3000];
        for(int i = 0; i < monotone.Length; i++)
        {
            monotone[i] = (uint)((i * 7) + (i & 3));
        }

        BlockPackedColumn column = BlockPackedColumn.Build(monotone, BlockPackedColumnMode.EliasFano);
        Assert.AreEqual(BlockPackedColumnMode.EliasFano, column.Mode);

        BlockPackedColumn restored = RoundTrip(pool, column, ColumnPayloadBacking.Managed);

        Assert.AreEqual(BlockPackedColumnMode.EliasFano, restored.Mode);
        Assert.AreEqual(column.Length, restored.Length);
        Assert.AreEqual(column.PackedByteCount, restored.PackedByteCount);
        Assert.AreSequenceEqual(DecodeAll(column), DecodeAll(restored));

        BlockPackedColumnReader reader = new(restored);
        for(int i = 0; i < monotone.Length; i++)
        {
            Assert.AreEqual(monotone[i], reader.ValueAt(i));
        }
    }

    /// <summary>A within-group-monotone partitioned-Elias-Fano column reloads from its byte image identically — footprint, every value, and the borrowed boundaries preserved.</summary>
    [TestMethod]
    public void PartitionedEliasFanoColumnRoundTripsThroughBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int groups = 300;
        const int groupSize = 8;
        uint[] values = new uint[groups * groupSize];
        int[] boundaries = new int[groups + 1];
        for(int g = 0; g < groups; g++)
        {
            boundaries[g] = g * groupSize;
            for(int k = 0; k < groupSize; k++)
            {
                values[(g * groupSize) + k] = (uint)(k * 100);
            }
        }

        boundaries[groups] = groups * groupSize;

        BlockPackedColumn column = BlockPackedColumn.BuildPartitioned(values, boundaries);
        Assert.AreEqual(BlockPackedColumnMode.PartitionedEliasFano, column.Mode);

        BlockPackedColumn restored = RoundTrip(pool, column, ColumnPayloadBacking.Managed);

        Assert.AreEqual(BlockPackedColumnMode.PartitionedEliasFano, restored.Mode);
        Assert.AreEqual(column.Length, restored.Length);
        Assert.AreEqual(column.PackedByteCount, restored.PackedByteCount);
        Assert.AreSequenceEqual(DecodeAll(column), DecodeAll(restored));

        BlockPackedColumnReader reader = new(restored);
        for(int i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(values[i], reader.ValueAt(i));
        }
    }
}
