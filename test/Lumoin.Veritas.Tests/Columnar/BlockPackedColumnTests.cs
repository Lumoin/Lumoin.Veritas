using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The block-packed codec's contract: every distribution the
/// columnar index produces — sorted runs, grouped runs with
/// boundary drops, outlier-laden runs, full-range extremes —
/// round-trips losslessly through pack and decode, at every block
/// boundary, and the kernel seam dispatches per block.
/// </summary>
[TestClass]
internal sealed class BlockPackedColumnTests
{
    /// <summary>Decodes a packed column back to a full array, block by block.</summary>
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

    /// <summary>Asserts a value sequence round-trips through the codec, in BOTH encoding modes and through both whole-block decode and pointwise reads.</summary>
    /// <param name="values">The source values.</param>
    private static void AssertRoundTrips(uint[] values)
    {
        foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
        {
            BlockPackedColumn column = BlockPackedColumn.Build(values, mode);

            Assert.AreEqual(values.Length, column.Length);
            Assert.AreSequenceEqual(values, DecodeAll(column));

            BlockPackedColumnReader reader = new(column);
            for(int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(values[i], reader.ValueAt(i));
            }
        }
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness — entropy seams stay untouched in tests too.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    [TestMethod]
    public void EmptyColumnRoundTrips()
    {
        BlockPackedColumn column = BlockPackedColumn.Build([]);

        Assert.AreEqual(0, column.Length);
        Assert.AreEqual(0, column.BlockCount);
    }

    /// <summary>A native-backed column is byte-identical in footprint and decodes value-for-value with its managed twin, through both whole-block decode and pointwise reads, in both block-packed modes.</summary>
    [TestMethod]
    public void NativeBackingDecodesIdenticallyToManaged()
    {
        uint[] values = new uint[5000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)((i * 7) + (i & 15));
        }

        foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
        {
            BlockPackedColumn managed = BlockPackedColumn.Build(values, mode, backing: ColumnPayloadBacking.Managed);
            BlockPackedColumn native = BlockPackedColumn.Build(values, mode, backing: ColumnPayloadBacking.NativeAligned);

            Assert.AreEqual(managed.PackedByteCount, native.PackedByteCount, "Native backing must not change the footprint.");
            Assert.AreSequenceEqual(DecodeAll(managed), DecodeAll(native));

            BlockPackedColumnReader reader = new(native);
            for(int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(values[i], reader.ValueAt(i));
            }
        }
    }

    [TestMethod]
    public void SingleValueAndConstantRunsRoundTrip()
    {
        AssertRoundTrips([42U]);
        AssertRoundTrips([uint.MaxValue]);

        uint[] constant = new uint[3_000];
        Array.Fill(constant, 123_456_789U);
        AssertRoundTrips(constant);

        //A constant run's deltas are all zero: the packed payload
        //must collapse to (nearly) the metadata alone.
        BlockPackedColumn column = BlockPackedColumn.Build(constant);
        Assert.IsLessThan(constant.Length * sizeof(uint) / 16, (int)column.PackedByteCount);
    }

    [TestMethod]
    public void SortedUniformRunCompressesAndRoundTrips()
    {
        uint[] values = new uint[10_000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)(10 + (i * 3));
        }

        AssertRoundTrips(values);

        BlockPackedColumn column = BlockPackedColumn.Build(values);
        Assert.IsLessThan(values.Length * sizeof(uint) / 4, (int)column.PackedByteCount);
    }

    [TestMethod]
    public void GroupedRunsWithBoundaryDropsRoundTrip()
    {
        //The L1/L2 shape: ascending runs that reset at every group
        //boundary — the negative-delta case zigzag exists for.
        const int GroupCount = 1_500;
        const int MaximumGroupSize = 7;
        const uint GroupStartRange = 1_000_000;
        const uint InGroupStride = 17;
        const ulong MixerSeed = 7;

        List<uint> values = [];
        ulong state = MixerSeed;
        for(int group = 0; group < GroupCount; group++)
        {
            state = Mix(state);
            int size = 1 + (int)(state % MaximumGroupSize);
            uint start = (uint)(Mix(state) % GroupStartRange);
            for(int i = 0; i < size; i++)
            {
                values.Add(start + ((uint)i * InGroupStride));
            }
        }

        AssertRoundTrips([.. values]);
    }

    [TestMethod]
    public void OutlierLadenRunsExerciseTheExceptionPath()
    {
        //Mostly tiny deltas with rare huge jumps: the patched-
        //exception case — one outlier must not inflate the block.
        const int ValueCount = 8_192;
        const ulong MixerSeed = 99;
        const ulong OutlierEvery = 97;
        const uint OutlierJump = 50_000_000;
        const ulong SmallDeltaRange = 5;

        uint[] values = new uint[ValueCount];
        uint current = 0;
        ulong state = MixerSeed;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            current = unchecked(current + ((state % OutlierEvery) == 0 ? OutlierJump : (uint)(state % SmallDeltaRange)));
            values[i] = current;
        }

        AssertRoundTrips(values);

        BlockPackedColumn column = BlockPackedColumn.Build(values);
        Assert.IsLessThan(values.Length * sizeof(uint) / 3, (int)column.PackedByteCount);
    }

    [TestMethod]
    public void FullRangeExtremesRoundTripThroughWrappingDeltas()
    {
        AssertRoundTrips([0U, uint.MaxValue, 0U, uint.MaxValue, 1U, uint.MaxValue - 1, 2_147_483_648U, 0U]);

        //A descending column: every delta is negative.
        uint[] descending = new uint[4_000];
        for(int i = 0; i < descending.Length; i++)
        {
            descending[i] = (uint)(4_000_000 - (i * 13));
        }

        AssertRoundTrips(descending);
    }

    [TestMethod]
    public void BlockBoundaryLengthsRoundTrip()
    {
        foreach(int length in (int[])[1, BlockPackedColumn.BlockLength - 1, BlockPackedColumn.BlockLength, BlockPackedColumn.BlockLength + 1, (2 * BlockPackedColumn.BlockLength) + 1])
        {
            uint[] values = new uint[length];
            ulong state = (ulong)length;
            for(int i = 0; i < length; i++)
            {
                state = Mix(state);
                values[i] = (uint)state;
            }

            AssertRoundTrips(values);
        }
    }

    [TestMethod]
    public void AnchorsAreTheBlockFirstValues()
    {
        const int FullBlockCount = 3;
        const int TailLength = 5;
        const uint Stride = 7;

        uint[] values = new uint[(FullBlockCount * BlockPackedColumn.BlockLength) + TailLength];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)i * Stride;
        }

        BlockPackedColumn column = BlockPackedColumn.Build(values);

        Assert.AreEqual(FullBlockCount + 1, column.BlockCount);
        for(int block = 0; block < column.BlockCount; block++)
        {
            Assert.AreEqual(values[block << BlockPackedColumn.BlockShift], column.Anchors[block]);
        }
    }

    [TestMethod]
    public void KernelSeamDispatchesPerBlock()
    {
        int packCalls = 0;
        int decodeCalls = 0;
        ColumnarKernelBackend portable = ColumnarPortableBackend.Backend;
        ColumnarKernelBackend counting = new(
            (values, width, payload) =>
            {
                packCalls++;
                portable.Pack(values, width, payload);
            },
            (payload, width, anchor, exceptionPositions, exceptionValues, destination) =>
            {
                decodeCalls++;
                portable.Decode(payload, width, anchor, exceptionPositions, exceptionValues, destination);
            },
            portable.DecodeFrame);

        const int FullBlockCount = 2;
        const int TailLength = 100;
        const uint Stride = 11;
        const int ExpectedBlocks = FullBlockCount + 1;

        uint[] values = new uint[(FullBlockCount * BlockPackedColumn.BlockLength) + TailLength];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)i * Stride;
        }

        BlockPackedColumn column = BlockPackedColumn.Build(values, BlockPackedColumnMode.PrefixedDeltas, counting);
        Assert.AreEqual(ExpectedBlocks, packCalls);

        Assert.AreSequenceEqual(values, DecodeAll(column));
        Assert.AreEqual(ExpectedBlocks, decodeCalls);
    }

    [TestMethod]
    public void EveryAvailableBackendDecodesIdentically()
    {
        //The bundle ladder: the portable backend is the correctness
        //reference; every accelerated backend available on this
        //machine must agree with it on every distribution, including
        //the exception-laden and wrapping cases.
        List<ColumnarKernelBackend> backends = [ColumnarPortableBackend.Backend];
        if(ColumnarVector128Backend.IsSupported)
        {
            backends.Add(ColumnarVector128Backend.Backend);
        }

        if(ColumnarVector256Backend.IsSupported)
        {
            backends.Add(ColumnarVector256Backend.Backend);
        }

        if(ColumnarWasmPackedSimdBackend.IsSupported)
        {
            backends.Add(ColumnarWasmPackedSimdBackend.Backend);
        }

        //A partial tail block, rare wrapping-scale jumps (the
        //exception path), and small deltas otherwise.
        const int FullBlockCount = 3;
        const int TailLength = 77;
        const ulong MixerSeed = 1_234;
        const ulong OutlierEvery = 89;
        const uint OutlierJump = 3_000_000_000;
        const ulong SmallDeltaRange = 9;

        uint[] values = new uint[(FullBlockCount * BlockPackedColumn.BlockLength) + TailLength];
        uint current = 0;
        ulong state = MixerSeed;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            current = unchecked(current + ((state % OutlierEvery) == 0 ? OutlierJump : (uint)(state % SmallDeltaRange)));
            values[i] = current;
        }

        foreach(ColumnarKernelBackend backend in backends)
        {
            foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
            {
                BlockPackedColumn column = BlockPackedColumn.Build(values, mode, backend);

                Assert.AreEqual(values.Length, column.Length);
                Assert.AreSequenceEqual(values, DecodeAll(column));
            }
        }
    }

    [TestMethod]
    public void LowerBoundAgreesWithTheLinearReferenceInBothModes()
    {
        //Grouped ascending runs spanning many blocks, searched per
        //group range — the iterator's exact usage. Every block-
        //boundary alignment of [lo, hi) and every in/under/over
        //target lands exactly where a linear scan lands.
        const int GroupCount = 200;
        const int GroupSize = 37;
        const uint InGroupStride = 5;
        const ulong MixerSeed = 4_321;

        uint[] values = new uint[GroupCount * GroupSize];
        ulong state = MixerSeed;
        for(int group = 0; group < GroupCount; group++)
        {
            state = Mix(state);
            uint start = (uint)(state % 100_000);
            for(int i = 0; i < GroupSize; i++)
            {
                values[(group * GroupSize) + i] = start + ((uint)i * InGroupStride);
            }
        }

        foreach(BlockPackedColumnMode mode in (BlockPackedColumnMode[])[BlockPackedColumnMode.PrefixedDeltas, BlockPackedColumnMode.FrameOfReference])
        {
            BlockPackedColumn column = BlockPackedColumn.Build(values, mode);
            BlockPackedColumnReader reader = new(column);

            for(int group = 0; group < GroupCount; group++)
            {
                int lo = group * GroupSize;
                int hi = lo + GroupSize;

                foreach(uint target in (uint[])[values[lo], values[hi - 1], values[lo + (GroupSize / 2)] + 1, values[lo] - 1, values[hi - 1] + 1])
                {
                    int expected = lo;
                    while(expected < hi && values[expected] < target)
                    {
                        expected++;
                    }

                    Assert.AreEqual(expected, reader.LowerBound(lo, hi, target));
                }
            }
        }
    }
}
