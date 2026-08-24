using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A <c>uint</c> column stored as fixed-size blocks of bit-packed
/// lanes — the succinct layout's in-memory codec, with the
/// encoding chosen per column by access pattern
/// (<see cref="BlockPackedColumnMode"/>): prefixed zigzag deltas
/// with patched exceptions for pointwise-with-locality columns, or
/// frame-of-reference lanes for the value columns joins seek over.
/// Block metadata lives in parallel arrays — struct-of-arrays,
/// never per-block objects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Prefixed deltas.</b> The columnar index's value columns are
/// sorted within parent groups, so successive deltas are small —
/// except at group boundaries, where the value can drop and the
/// delta turns negative and large. Zigzag encoding makes negative
/// deltas cheap; exception patching keeps one boundary outlier from
/// inflating the block's width. Deltas are computed in wrapping
/// 32-bit arithmetic and reinterpreted as signed for zigzag, so the
/// full <c>uint</c> range round-trips losslessly. Per block, the
/// chosen width minimises
/// <c>blockLength·width + exceptions(width)·48</c> bits; ties
/// prefer the larger width (fewer patches). Reading a single value
/// requires decoding its whole block — consumers go through
/// <see cref="ValueAt"/>'s scratch-and-cache protocol.
/// </para>
/// <para>
/// <b>Frame of reference.</b> Lanes are <c>value − blockMinimum</c>
/// at the block's span width, no exceptions (the width covers the
/// span by construction). Wider than deltas on boundary-crossing
/// blocks, but every lane is independently readable:
/// <see cref="ValueAt"/> is one shift-or, and an in-block seek is a
/// handful of lane probes instead of a block decode — the measured
/// fix for seek-heavy worst-case-optimal descents.
/// </para>
/// <para>
/// <b>Seeks.</b> <see cref="Anchors"/> carries each block's FIRST
/// value in both modes: within a strictly-ascending query range,
/// the anchors of fully-interior blocks are themselves ascending,
/// so <see cref="LowerBound"/> binary-searches the anchors to pick
/// the single block that can contain the bound and finishes inside
/// it (decode-and-scan under prefixed deltas, lane probes under
/// frame of reference).
/// </para>
/// </remarks>
[DebuggerDisplay("BlockPackedColumn Mode={Mode} Length={Length} Blocks={BlockCount} PackedBytes={PackedByteCount}")]
public sealed partial class BlockPackedColumn
{
    /// <summary>The block length as a shift: blocks hold 2^10 = 1,024 values.</summary>
    public const int BlockShift = 10;

    /// <summary>The number of values per block (the last block may be shorter).</summary>
    public const int BlockLength = 1 << BlockShift;

    /// <summary>The mask extracting an in-block position from a column index.</summary>
    public const int BlockMask = BlockLength - 1;

    //An exception costs a 16-bit position plus a 32-bit zigzag
    //value in the side arrays.
    private const int ExceptionCostBits = 48;

    /// <summary>The whole-column payload view, resolved once from the byte source at construction and sliced per read.</summary>
    private ReadOnlyMemory<ulong> Payload { get; }

    private readonly int[] payloadStarts;

    private readonly uint[] anchors;

    private readonly uint[] frameBases;

    private readonly byte[] widths;

    private readonly int[] exceptionStarts;

    private readonly ushort[] exceptionPositions;

    private readonly uint[] exceptionValues;

    //The whole column as one monotone sequence under
    //BlockPackedColumnMode.EliasFano; null in the block-packed modes.
    private readonly EliasFanoSequence? eliasFano;

    //The within-group sequence (groups reset, boundaries borrowed from the
    //offset column) under BlockPackedColumnMode.PartitionedEliasFano; null
    //otherwise.
    private readonly PartitionedEliasFanoSequence? partitionedEliasFano;

    /// <summary>The kernel bundle decoding this column's blocks.</summary>
    private ColumnarKernelBackend Backend { get; }

    /// <summary>The encoding this column's blocks use.</summary>
    public BlockPackedColumnMode Mode { get; }

    /// <summary>The number of values in the column.</summary>
    public int Length { get; }

    /// <summary>The number of blocks; <c>ceil(Length / 1024)</c>. Derived from the length under Elias-Fano, which holds no block metadata.</summary>
    public int BlockCount => Mode is BlockPackedColumnMode.EliasFano or BlockPackedColumnMode.PartitionedEliasFano ? (Length + BlockMask) >> BlockShift : anchors.Length;

    /// <summary>The per-block first values, raw, in every mode. Within a sorted range, anchors of fully-interior blocks are sorted; seeks scan these to pick the one block to finish in.</summary>
    public ReadOnlySpan<uint> Anchors => anchors;

    /// <summary>The packed size in bytes — the Elias-Fano bit-footprint under that mode, else across payload, metadata, and exception arrays. The number the soak ladder tracks.</summary>
    public long PackedByteCount => Mode switch
    {
        BlockPackedColumnMode.EliasFano => (eliasFano!.BitCount + 7) / 8,
        BlockPackedColumnMode.PartitionedEliasFano => (partitionedEliasFano!.BitCount + 7) / 8,
        _ => (Payload.Length * sizeof(ulong))
            + (payloadStarts.Length * sizeof(int))
            + (anchors.Length * sizeof(uint))
            + (frameBases.Length * sizeof(uint))
            + widths.Length
            + (exceptionStarts.Length * sizeof(int))
            + (exceptionPositions.Length * sizeof(ushort))
            + (exceptionValues.Length * sizeof(uint)),
    };

    /// <summary>Constructs a block-packed column over a payload byte source and its parallel block metadata.</summary>
    /// <param name="backend">The kernel bundle decoding this column's blocks.</param>
    /// <param name="mode">The block encoding.</param>
    /// <param name="length">The value count.</param>
    /// <param name="payloadSource">The byte source backing the packed lane words.</param>
    /// <param name="payloadStarts">The per-block payload word offsets, with one entry past the last block.</param>
    /// <param name="anchors">The per-block first values.</param>
    /// <param name="frameBases">The per-block frame minimums under frame of reference; empty otherwise.</param>
    /// <param name="widths">The per-block lane widths.</param>
    /// <param name="exceptionStarts">The per-block exception offsets, with one entry past the last block.</param>
    /// <param name="exceptionPositions">The packed exception in-block positions.</param>
    /// <param name="exceptionValues">The packed exception values.</param>
    private BlockPackedColumn(
        ColumnarKernelBackend backend,
        BlockPackedColumnMode mode,
        int length,
        ColumnSource payloadSource,
        int[] payloadStarts,
        uint[] anchors,
        uint[] frameBases,
        byte[] widths,
        int[] exceptionStarts,
        ushort[] exceptionPositions,
        uint[] exceptionValues)
    {
        Backend = backend;
        Mode = mode;
        Length = length;
        if(!payloadSource.TryGetMemory(out ReadOnlyMemory<ulong> resolvedPayload))
        {
            throw new ArgumentException("The payload source cannot hand out a contiguous column view.", nameof(payloadSource));
        }

        Payload = resolvedPayload;
        this.payloadStarts = payloadStarts;
        this.anchors = anchors;
        this.frameBases = frameBases;
        this.widths = widths;
        this.exceptionStarts = exceptionStarts;
        this.exceptionPositions = exceptionPositions;
        this.exceptionValues = exceptionValues;
    }

    /// <summary>Constructs an Elias-Fano-backed column — the whole column is one monotone sequence and the block-packed arrays are empty.</summary>
    /// <param name="backend">The kernel bundle (carried for the batch-decode SIMD seam; Elias-Fano access itself is scalar).</param>
    /// <param name="length">The value count.</param>
    /// <param name="eliasFano">The packed monotone sequence.</param>
    private BlockPackedColumn(ColumnarKernelBackend backend, int length, EliasFanoSequence eliasFano)
    {
        Backend = backend;
        Mode = BlockPackedColumnMode.EliasFano;
        Length = length;
        this.eliasFano = eliasFano;
        Payload = ReadOnlyMemory<ulong>.Empty;
        payloadStarts = [0];
        anchors = [];
        frameBases = [];
        widths = [];
        exceptionStarts = [0];
        exceptionPositions = [];
        exceptionValues = [];
    }

    /// <summary>Constructs a partitioned-Elias-Fano-backed column — within-group sequence, block-packed arrays empty.</summary>
    /// <param name="backend">The kernel bundle (carried for parity; partitioned access is scalar).</param>
    /// <param name="length">The value count.</param>
    /// <param name="partitionedEliasFano">The packed within-group sequence.</param>
    private BlockPackedColumn(ColumnarKernelBackend backend, int length, PartitionedEliasFanoSequence partitionedEliasFano)
    {
        Backend = backend;
        Mode = BlockPackedColumnMode.PartitionedEliasFano;
        Length = length;
        this.partitionedEliasFano = partitionedEliasFano;
        Payload = ReadOnlyMemory<ulong>.Empty;
        payloadStarts = [0];
        anchors = [];
        frameBases = [];
        widths = [];
        exceptionStarts = [0];
        exceptionPositions = [];
        exceptionValues = [];
    }

    /// <summary>
    /// Packs a within-group-monotone column as partitioned Elias-Fano, the
    /// group boundaries supplied by the parent offset column. The boundaries
    /// are borrowed (not copied into the footprint); the matching offset column
    /// stores them once.
    /// </summary>
    /// <param name="values">The column values, non-decreasing within each group.</param>
    /// <param name="boundaries">The exclusive-end group boundaries (the offset column).</param>
    /// <param name="backendOption">The kernel bundle; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The packed column.</returns>
    public static BlockPackedColumn BuildPartitioned(ReadOnlySpan<uint> values, int[] boundaries, ColumnarKernelBackend? backendOption = null)
    {
        ColumnarKernelBackend backend = backendOption ?? ColumnarKernelBackend.Default;

        return new BlockPackedColumn(backend, values.Length, PartitionedEliasFanoSequence.Build(values, boundaries));
    }

    /// <summary>The number of values in the given block — <see cref="BlockLength"/> for every block but possibly the last.</summary>
    /// <param name="blockIndex">The block index.</param>
    /// <returns>The block's value count.</returns>
    public int BlockLengthOf(int blockIndex)
    {
        int start = blockIndex << BlockShift;

        return Math.Min(BlockLength, Length - start);
    }

    /// <summary>
    /// Decodes one whole block into <paramref name="destination"/>.
    /// </summary>
    /// <param name="blockIndex">The block to decode.</param>
    /// <param name="destination">Receives the block's values; at least <see cref="BlockLengthOf"/> long, of which exactly that many entries are written.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockIndex"/> is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void DecodeBlock(int blockIndex, Span<uint> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(blockIndex, BlockCount);

        int count = BlockLengthOf(blockIndex);
        if(destination.Length < count)
        {
            throw new ArgumentException("The destination is shorter than the block.", nameof(destination));
        }

        if(Mode == BlockPackedColumnMode.EliasFano)
        {
            eliasFano!.Decode(blockIndex << BlockShift, count, destination[..count]);

            return;
        }

        if(Mode == BlockPackedColumnMode.PartitionedEliasFano)
        {
            int partitionedStart = blockIndex << BlockShift;
            for(int i = 0; i < count; i++)
            {
                destination[i] = partitionedEliasFano!.Access(partitionedStart + i);
            }

            return;
        }

        Span<uint> block = destination[..count];
        int width = widths[blockIndex];
        ReadOnlySpan<ulong> blockPayload = Payload.Span.Slice(payloadStarts[blockIndex], payloadStarts[blockIndex + 1] - payloadStarts[blockIndex]);

        if(Mode == BlockPackedColumnMode.FrameOfReference)
        {
            Backend.DecodeFrame(blockPayload, width, frameBases[blockIndex], block);

            return;
        }

        int exceptionStart = exceptionStarts[blockIndex];
        int exceptionCount = exceptionStarts[blockIndex + 1] - exceptionStart;

        Backend.Decode(
            blockPayload,
            width,
            anchors[blockIndex],
            exceptionPositions.AsSpan(exceptionStart, exceptionCount),
            exceptionValues.AsSpan(exceptionStart, exceptionCount),
            block);
    }

    /// <summary>
    /// Reads the value at <paramref name="index"/>. Under frame of
    /// reference this is one lane probe and the scratch is
    /// untouched; under prefixed deltas the value's block decodes
    /// into the caller-held scratch on a cache miss.
    /// <see cref="BlockPackedColumnReader"/> wraps this with owned
    /// state; cold one-shot descents pass a stack scratch and a
    /// local cache slot.
    /// </summary>
    /// <param name="index">The column index.</param>
    /// <param name="scratch">The one-block decode scratch; at least <see cref="BlockLength"/> long.</param>
    /// <param name="cachedBlock">The caller's cache slot: the block currently decoded in <paramref name="scratch"/>, −1 for none. Updated on miss.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int index, Span<uint> scratch, ref int cachedBlock)
    {
        if(Mode == BlockPackedColumnMode.EliasFano)
        {
            return eliasFano!.Access(index);
        }

        if(Mode == BlockPackedColumnMode.PartitionedEliasFano)
        {
            return partitionedEliasFano!.Access(index);
        }

        int block = index >> BlockShift;

        if(Mode == BlockPackedColumnMode.FrameOfReference)
        {
            return frameBases[block] + LaneAt(Payload.Span, block, index & BlockMask);
        }

        if(block != cachedBlock)
        {
            DecodeBlock(block, scratch);
            cachedBlock = block;
        }

        return scratch[index & BlockMask];
    }

    /// <summary>
    /// Returns the smallest index in <c>[lo, hi)</c> whose value is
    /// greater than or equal to <paramref name="target"/>, or
    /// <paramref name="hi"/> when no such index exists. The range
    /// must be STRICTLY ascending (the columnar index's group
    /// contract — distinct level values within a parent group):
    /// blocks fully interior to the range start inside it, so their
    /// anchors are ascending range values, and a binary search over
    /// the anchors picks the single block that can contain the
    /// bound.
    /// </summary>
    /// <param name="lo">The range's inclusive start.</param>
    /// <param name="hi">The range's exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <param name="scratch">The one-block decode scratch; at least <see cref="BlockLength"/> long. Untouched under frame of reference.</param>
    /// <param name="cachedBlock">The caller's cache slot, updated as blocks decode.</param>
    /// <returns>The lower-bound index.</returns>
    public int LowerBound(int lo, int hi, uint target, Span<uint> scratch, ref int cachedBlock)
    {
        if(lo >= hi)
        {
            return hi;
        }

        if(Mode == BlockPackedColumnMode.EliasFano)
        {
            //The column is globally non-decreasing, so the range successor is
            //the global successor clamped into [lo, hi): a global lower bound
            //below lo means every in-range value already clears the target.
            int found = eliasFano!.NextGEQ(target);
            if(found < lo)
            {
                return lo;
            }

            return found >= hi ? hi : found;
        }

        if(Mode == BlockPackedColumnMode.PartitionedEliasFano)
        {
            return partitionedEliasFano!.LowerBound(lo, hi, target);
        }

        int blockLo = lo >> BlockShift;
        int blockHi = (hi - 1) >> BlockShift;

        if(blockLo == blockHi)
        {
            return FinishInBlock(blockLo, lo, hi, target, scratch, ref cachedBlock);
        }

        //Interior blocks (blockLo+1 .. blockHi) start inside the
        //range, so their anchors are ascending range values: find
        //the LAST interior block whose anchor is at or below the
        //target — the bound can only live there or earlier.
        ReadOnlySpan<uint> anchorSpan = anchors;
        int candidateLow = blockLo + 1;
        int candidateHigh = blockHi;
        int candidate = -1;

        while(candidateLow <= candidateHigh)
        {
            int mid = candidateLow + ((candidateHigh - candidateLow) >> 1);

            if(anchorSpan[mid] <= target)
            {
                candidate = mid;
                candidateLow = mid + 1;
            }
            else
            {
                candidateHigh = mid - 1;
            }
        }

        if(candidate < 0)
        {
            //Every interior anchor exceeds the target: the bound is
            //inside the edge block's range portion, or exactly at
            //the first interior block's start.
            int edgeEnd = Math.Min(hi, (blockLo + 1) << BlockShift);
            int found = FinishInBlock(blockLo, lo, edgeEnd, target, scratch, ref cachedBlock);

            return found < edgeEnd ? found : edgeEnd;
        }

        int blockStart = candidate << BlockShift;
        int blockEnd = Math.Min(hi, (candidate + 1) << BlockShift);
        int inBlock = FinishInBlock(candidate, blockStart, blockEnd, target, scratch, ref cachedBlock);

        if(inBlock < blockEnd)
        {
            return inBlock;
        }

        //Every value in the candidate block is below the target;
        //the next range value is the following block's anchor,
        //which exceeds the target by the search above — or the
        //range simply ends.
        return blockEnd;
    }

    /// <summary>Finishes a lower-bound inside one block over the absolute index range <c>[lo, hi)</c>: decode-and-scan under prefixed deltas, lane probes under frame of reference.</summary>
    /// <param name="block">The block to finish in.</param>
    /// <param name="lo">The absolute inclusive start.</param>
    /// <param name="hi">The absolute exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <param name="scratch">The one-block decode scratch.</param>
    /// <param name="cachedBlock">The caller's cache slot.</param>
    /// <returns>The absolute lower-bound index, or <paramref name="hi"/>.</returns>
    private int FinishInBlock(int block, int lo, int hi, uint target, Span<uint> scratch, ref int cachedBlock)
    {
        if(Mode == BlockPackedColumnMode.FrameOfReference)
        {
            //The frame base bounds every lane from below: when even
            //base + lane ceiling cannot reach the target the probes
            //collapse immediately, and the binary search costs
            //log2(range) single-lane reads.
            uint frameBase = frameBases[block];
            ReadOnlySpan<ulong> payloadSpan = Payload.Span;
            int low = lo;
            int high = hi;

            while(low < high)
            {
                int mid = low + ((high - low) >> 1);

                if(frameBase + LaneAt(payloadSpan, block, mid & BlockMask) < target)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        if(block != cachedBlock)
        {
            DecodeBlock(block, scratch);
            cachedBlock = block;
        }

        int blockStart = block << BlockShift;
        int relative = ColumnarSearch.LowerBound(scratch, lo - blockStart, hi - blockStart, target);

        return blockStart + relative;
    }

    /// <summary>Reads one bit lane raw from a resolved payload span — layout arithmetic, not a kernel: a lane spans at most two payload words.</summary>
    /// <param name="payload">The whole-column payload span, resolved once by the caller.</param>
    /// <param name="blockIndex">The block.</param>
    /// <param name="lane">The in-block lane position.</param>
    /// <returns>The lane's packed value.</returns>
    private uint LaneAt(ReadOnlySpan<ulong> payload, int blockIndex, int lane)
    {
        int width = widths[blockIndex];
        if(width == 0)
        {
            return 0;
        }

        ulong mask = width == 32 ? uint.MaxValue : (1UL << width) - 1;
        long bitOffset = (long)lane * width;
        int word = payloadStarts[blockIndex] + (int)(bitOffset >> 6);
        int shift = (int)(bitOffset & 63);

        ulong laneBits = payload[word] >> shift;
        if(shift + width > 64)
        {
            laneBits |= payload[word + 1] << (64 - shift);
        }

        return (uint)(laneBits & mask);
    }

    /// <summary>
    /// Packs <paramref name="values"/> into a block-compressed
    /// column.
    /// </summary>
    /// <param name="values">The column values, in column order.</param>
    /// <param name="mode">The encoding to use; see <see cref="BlockPackedColumnMode"/> for the trade.</param>
    /// <param name="backendOption">The kernel bundle to pack with (and later decode with); <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <param name="backing">Where the block-packed payload words live; default managed. Ignored by the Elias-Fano modes, which hold no block payload.</param>
    /// <returns>The packed column.</returns>
    public static BlockPackedColumn Build(
        ReadOnlySpan<uint> values,
        BlockPackedColumnMode mode = BlockPackedColumnMode.PrefixedDeltas,
        ColumnarKernelBackend? backendOption = null,
        ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        ColumnarKernelBackend backend = backendOption ?? ColumnarKernelBackend.Default;

        if(mode == BlockPackedColumnMode.EliasFano)
        {
            //One monotone sequence for the whole column; the caller is
            //responsible for only choosing this mode where the values are
            //globally non-decreasing (EliasFanoSequence.Build enforces it).
            //The backend's lane kernels are injected here — the sequence's
            //lower payload is exactly their lane layout — so the succinct
            //layer stays free of this layer.
            return new BlockPackedColumn(
                backend,
                values.Length,
                EliasFanoSequence.Build(values, lanePacker: backend.Pack.Invoke, laneUnpacker: backend.DecodeFrame.Invoke));
        }

        int blockCount = (values.Length + BlockLength - 1) >> BlockShift;
        uint[] anchors = new uint[blockCount];
        uint[] frameBases = mode == BlockPackedColumnMode.FrameOfReference ? new uint[blockCount] : [];
        byte[] widths = new byte[blockCount];
        int[] payloadStarts = new int[blockCount + 1];
        int[] exceptionStarts = new int[blockCount + 1];
        List<ulong> payload = [];
        List<ushort> exceptionPositions = [];
        List<uint> exceptionValues = [];

        Span<uint> lanes = stackalloc uint[BlockLength];
        Span<int> bitLengthHistogram = stackalloc int[33];

        for(int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            int start = blockIndex << BlockShift;
            int count = Math.Min(BlockLength, values.Length - start);
            ReadOnlySpan<uint> block = values.Slice(start, count);

            anchors[blockIndex] = block[0];

            int width;
            if(mode == BlockPackedColumnMode.FrameOfReference)
            {
                //Frame of reference: lanes are value − min at the
                //span's width; no exceptions by construction.
                uint minimum = block[0];
                uint maximum = block[0];
                for(int i = 1; i < count; i++)
                {
                    minimum = Math.Min(minimum, block[i]);
                    maximum = Math.Max(maximum, block[i]);
                }

                frameBases[blockIndex] = minimum;
                width = BitLengthOf(maximum - minimum);
                for(int i = 0; i < count; i++)
                {
                    lanes[i] = block[i] - minimum;
                }
            }
            else
            {
                //Prefixed deltas: zigzag the wrapping successive
                //deltas; lane 0 is the anchor's zero delta.
                bitLengthHistogram.Clear();
                lanes[0] = 0;
                bitLengthHistogram[0]++;
                for(int i = 1; i < count; i++)
                {
                    int delta = unchecked((int)(block[i] - block[i - 1]));
                    uint zigzag = (uint)((delta << 1) ^ (delta >> 31));
                    lanes[i] = zigzag;
                    bitLengthHistogram[BitLengthOf(zigzag)]++;
                }

                width = ChooseWidth(bitLengthHistogram, count);

                //Record exceptions: lanes whose zigzag value needs
                //more bits than the chosen width.
                for(int i = 0; i < count; i++)
                {
                    if(BitLengthOf(lanes[i]) > width)
                    {
                        exceptionPositions.Add((ushort)i);
                        exceptionValues.Add(lanes[i]);
                    }
                }
            }

            widths[blockIndex] = (byte)width;
            exceptionStarts[blockIndex + 1] = exceptionPositions.Count;

            //Pack the lanes into freshly zeroed words.
            int wordCount = (int)(((long)count * width + 63) >> 6);
            ulong[] blockWords = new ulong[wordCount];
            backend.Pack(lanes[..count], width, blockWords);
            payload.AddRange(blockWords);
            payloadStarts[blockIndex + 1] = payload.Count;
        }

        ColumnSource payloadSource = backing == ColumnPayloadBacking.NativeAligned
            ? InMemoryColumnSource.CreateNative(CollectionsMarshal.AsSpan(payload))
            : new InMemoryColumnSource([.. payload]);

        return new BlockPackedColumn(
            backend,
            mode,
            values.Length,
            payloadSource,
            payloadStarts,
            anchors,
            frameBases,
            widths,
            exceptionStarts,
            [.. exceptionPositions],
            [.. exceptionValues]);
    }

    /// <summary>The number of bits needed to represent <paramref name="value"/>; 0 for 0.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The bit length.</returns>
    private static int BitLengthOf(uint value)
    {
        return value == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(value);
    }

    /// <summary>
    /// Chooses the per-block lane width from the bit-length
    /// histogram: minimise <c>count·width + exceptions·48</c>,
    /// ties to the larger width.
    /// </summary>
    /// <param name="bitLengthHistogram">Counts of lanes per bit length, indices 0..32.</param>
    /// <param name="count">The block's lane count.</param>
    /// <returns>The chosen width.</returns>
    private static int ChooseWidth(ReadOnlySpan<int> bitLengthHistogram, int count)
    {
        int best = 32;
        long bestCost = (long)count * 32;

        //exceptions(w) = lanes whose bit length exceeds w — a
        //suffix sum walked from wide to narrow.
        int exceptions = 0;
        for(int width = 31; width >= 0; width--)
        {
            exceptions += bitLengthHistogram[width + 1];
            long cost = ((long)count * width) + ((long)exceptions * ExceptionCostBits);

            if(cost < bestCost)
            {
                bestCost = cost;
                best = width;
            }
        }

        return best;
    }
}
