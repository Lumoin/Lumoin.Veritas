using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The <see cref="ColumnSource"/> byte-source seam: the in-memory
/// backing hands out the whole-column view with no copy, reports its
/// byte length and raw bytes faithfully, the empty source is a
/// zero-length view in every accessor, and a column built over the
/// source decodes value-for-value — the seam is transparent to the
/// codec.
/// </summary>
[TestClass]
internal sealed class ColumnSourceTests
{
    /// <summary>The in-memory source hands back exactly the words it wraps, as one contiguous view.</summary>
    [TestMethod]
    public void InMemorySourceHandsOutWholeColumn()
    {
        ulong[] words = [0, 1, 0xFFFF_FFFF_FFFF_FFFFUL, 42, 7];
        InMemoryColumnSource source = new(words);

        Assert.IsTrue(source.TryGetMemory(out ReadOnlyMemory<ulong> memory));
        Assert.AreSequenceEqual(words, memory.ToArray());
    }

    /// <summary>The byte length and raw byte view match the wrapped words.</summary>
    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void InMemorySourceReportsBytes()
    {
        ulong[] words = [1, 2, 3, 4];
        InMemoryColumnSource source = new(words);

        Assert.AreEqual(words.Length * sizeof(ulong), source.LengthInBytes);

        ReadOnlySpan<byte> bytes = source.Bytes;
        Assert.AreEqual(words.Length * sizeof(ulong), bytes.Length);
        Assert.AreSequenceEqual(words, MemoryMarshal.Cast<byte, ulong>(bytes).ToArray());
    }

    /// <summary>The shared empty source is a zero-length view everywhere.</summary>
    [TestMethod]
    public void EmptySourceIsZeroLength()
    {
        Assert.AreEqual(0, ColumnSource.Empty.LengthInBytes);
        Assert.IsTrue(ColumnSource.Empty.Bytes.IsEmpty);
        Assert.IsTrue(ColumnSource.Empty.TryGetMemory(out ReadOnlyMemory<ulong> memory));
        Assert.IsTrue(memory.IsEmpty);
    }

    /// <summary>A block-packed column built over the in-memory source decodes every value identically in both block-packed modes.</summary>
    [TestMethod]
    public void BlockPackedColumnOverSourceRoundTrips()
    {
        uint[] values = new uint[3000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)((i * 3) + (i & 7));
        }

        foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
        {
            BlockPackedColumn column = BlockPackedColumn.Build(values, mode);
            BlockPackedColumnReader reader = new(column);

            for(int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(values[i], reader.ValueAt(i));
            }
        }
    }

    /// <summary>A native-backed source hands back exactly the words it was built from, off the managed heap.</summary>
    [TestMethod]
    public void NativeBackedSourceRoundTrips()
    {
        ulong[] data = [0, 1, 0xFFFF_FFFF_FFFF_FFFFUL, 42, 7, 100, 1UL << 63];
        InMemoryColumnSource source = InMemoryColumnSource.CreateNative(data);

        Assert.IsTrue(source.TryGetMemory(out ReadOnlyMemory<ulong> memory));
        Assert.AreSequenceEqual(data, memory.ToArray());
        Assert.AreEqual(data.Length * sizeof(ulong), source.LengthInBytes);
        Assert.AreSequenceEqual(data, MemoryMarshal.Cast<byte, ulong>(source.Bytes).ToArray());
    }

    /// <summary>A native-backed empty source is a zero-length view.</summary>
    [TestMethod]
    public void NativeBackedEmptySourceIsZeroLength()
    {
        InMemoryColumnSource source = InMemoryColumnSource.CreateNative([]);

        Assert.IsTrue(source.TryGetMemory(out ReadOnlyMemory<ulong> memory));
        Assert.IsTrue(memory.IsEmpty);
        Assert.AreEqual(0, source.LengthInBytes);
    }
}
