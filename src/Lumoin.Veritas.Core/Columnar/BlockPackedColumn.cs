using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A <c>uint</c> column stored as fixed-size blocks of
/// zigzag-delta, bit-packed lanes with patched exceptions — the
/// succinct layout's in-memory codec. Each block records its first
/// value raw (the ANCHOR), packs the zigzagged successive deltas at
/// one per-block bit width, and stores deltas wider than that as
/// (position, value) exception pairs. Block metadata lives in
/// parallel arrays — struct-of-arrays, never per-block objects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this shape.</b> The columnar index's value columns are
/// sorted within parent groups, so successive deltas are small —
/// except at group boundaries, where the value can drop and the
/// delta turns negative and large. Zigzag encoding makes negative
/// deltas cheap to represent; exception patching keeps one
/// boundary outlier from inflating the whole block's bit width.
/// Offset columns are strictly monotone (deltas = group sizes) and
/// ride the same codec.
/// </para>
/// <para>
/// <b>Delta arithmetic.</b> Deltas are computed in wrapping
/// 32-bit arithmetic and reinterpreted as signed for zigzag, so
/// the full <c>uint</c> range round-trips losslessly: decode adds
/// the un-zigzagged delta back with wrapping addition.
/// </para>
/// <para>
/// <b>Width selection.</b> Per block, the width minimising
/// <c>blockLength·width + exceptions(width)·48</c> bits (an
/// exception costs a 16-bit position plus a 32-bit value); ties
/// prefer the larger width, trading bits for fewer patches on the
/// decode path.
/// </para>
/// <para>
/// <b>Random access.</b> The column does not decode per element:
/// consumers decode whole blocks into a scratch buffer through
/// <see cref="DecodeBlock"/> and cache the last-decoded block —
/// descent locality makes consecutive touches hit the cache. The
/// raw <see cref="Anchors"/> are exposed for block-skipping seeks:
/// within a sorted range, the anchors of fully-interior blocks are
/// themselves sorted.
/// </para>
/// </remarks>
[DebuggerDisplay("BlockPackedColumn Length={Length} Blocks={BlockCount} PackedBytes={PackedByteCount}")]
public sealed class BlockPackedColumn
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

    private readonly ulong[] payload;

    private readonly int[] payloadStarts;

    private readonly uint[] anchors;

    private readonly byte[] widths;

    private readonly int[] exceptionStarts;

    private readonly ushort[] exceptionPositions;

    private readonly uint[] exceptionValues;

    /// <summary>The kernel bundle decoding this column's blocks.</summary>
    private ColumnarKernelBackend Backend { get; }

    /// <summary>The number of values in the column.</summary>
    public int Length { get; }

    /// <summary>The number of blocks; <c>ceil(Length / 1024)</c>.</summary>
    public int BlockCount => anchors.Length;

    /// <summary>The per-block anchor values — each block's first decoded value, raw. Within a sorted range, anchors of fully-interior blocks are sorted; seeks scan these to pick the one block to decode.</summary>
    public ReadOnlySpan<uint> Anchors => anchors;

    /// <summary>The packed size in bytes across payload, metadata, and exception arrays — the number the soak ladder tracks.</summary>
    public long PackedByteCount =>
        (payload.Length * sizeof(ulong))
        + (payloadStarts.Length * sizeof(int))
        + (anchors.Length * sizeof(uint))
        + widths.Length
        + (exceptionStarts.Length * sizeof(int))
        + (exceptionPositions.Length * sizeof(ushort))
        + (exceptionValues.Length * sizeof(uint));

    private BlockPackedColumn(
        ColumnarKernelBackend backend,
        int length,
        ulong[] payload,
        int[] payloadStarts,
        uint[] anchors,
        byte[] widths,
        int[] exceptionStarts,
        ushort[] exceptionPositions,
        uint[] exceptionValues)
    {
        Backend = backend;
        Length = length;
        this.payload = payload;
        this.payloadStarts = payloadStarts;
        this.anchors = anchors;
        this.widths = widths;
        this.exceptionStarts = exceptionStarts;
        this.exceptionPositions = exceptionPositions;
        this.exceptionValues = exceptionValues;
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
    /// Decodes one whole block into
    /// <paramref name="destination"/>: unpack the bit lanes, patch
    /// the exceptions, un-zigzag, and prefix-sum from the anchor.
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

        Span<uint> block = destination[..count];
        int width = widths[blockIndex];
        ReadOnlySpan<ulong> blockPayload = payload.AsSpan(payloadStarts[blockIndex], payloadStarts[blockIndex + 1] - payloadStarts[blockIndex]);
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
    /// Reads the value at <paramref name="index"/> through a
    /// caller-held scratch and cache slot, decoding the block on a
    /// cache miss. <see cref="BlockPackedColumnReader"/> wraps this
    /// with owned state; cold one-shot descents pass a stack
    /// scratch and a local cache slot.
    /// </summary>
    /// <param name="index">The column index.</param>
    /// <param name="scratch">The one-block decode scratch; at least <see cref="BlockLength"/> long.</param>
    /// <param name="cachedBlock">The caller's cache slot: the block currently decoded in <paramref name="scratch"/>, −1 for none. Updated on miss.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int index, Span<uint> scratch, ref int cachedBlock)
    {
        int block = index >> BlockShift;

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
    /// <param name="scratch">The one-block decode scratch; at least <see cref="BlockLength"/> long.</param>
    /// <param name="cachedBlock">The caller's cache slot, updated as blocks decode.</param>
    /// <returns>The lower-bound index.</returns>
    public int LowerBound(int lo, int hi, uint target, Span<uint> scratch, ref int cachedBlock)
    {
        if(lo >= hi)
        {
            return hi;
        }

        int blockLo = lo >> BlockShift;
        int blockHi = (hi - 1) >> BlockShift;

        if(blockLo == blockHi)
        {
            return ScanBlock(blockLo, lo, hi, target, scratch, ref cachedBlock);
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
            int found = ScanBlock(blockLo, lo, edgeEnd, target, scratch, ref cachedBlock);

            return found < edgeEnd ? found : edgeEnd;
        }

        int blockStart = candidate << BlockShift;
        int blockEnd = Math.Min(hi, (candidate + 1) << BlockShift);
        int inBlock = ScanBlock(candidate, blockStart, blockEnd, target, scratch, ref cachedBlock);

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

    /// <summary>Lower-bound scan within one block over the absolute index range <c>[lo, hi)</c>, both inside the block.</summary>
    /// <param name="block">The block to scan.</param>
    /// <param name="lo">The absolute inclusive start.</param>
    /// <param name="hi">The absolute exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <param name="scratch">The one-block decode scratch.</param>
    /// <param name="cachedBlock">The caller's cache slot.</param>
    /// <returns>The absolute lower-bound index, or <paramref name="hi"/>.</returns>
    private int ScanBlock(int block, int lo, int hi, uint target, Span<uint> scratch, ref int cachedBlock)
    {
        if(block != cachedBlock)
        {
            DecodeBlock(block, scratch);
            cachedBlock = block;
        }

        int blockStart = block << BlockShift;
        int relative = ColumnarSearch.LowerBound(scratch, lo - blockStart, hi - blockStart, target);

        return blockStart + relative;
    }

    /// <summary>
    /// Packs <paramref name="values"/> into a block-compressed
    /// column.
    /// </summary>
    /// <param name="values">The column values, in column order.</param>
    /// <param name="backendOption">The kernel bundle to pack with (and later decode with); <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The packed column.</returns>
    public static BlockPackedColumn Build(ReadOnlySpan<uint> values, ColumnarKernelBackend? backendOption = null)
    {
        ColumnarKernelBackend backend = backendOption ?? ColumnarKernelBackend.Default;

        int blockCount = (values.Length + BlockLength - 1) >> BlockShift;
        uint[] anchors = new uint[blockCount];
        byte[] widths = new byte[blockCount];
        int[] payloadStarts = new int[blockCount + 1];
        int[] exceptionStarts = new int[blockCount + 1];
        List<ulong> payload = [];
        List<ushort> exceptionPositions = [];
        List<uint> exceptionValues = [];

        Span<uint> zigzags = stackalloc uint[BlockLength];
        Span<int> bitLengthHistogram = stackalloc int[33];

        for(int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            int start = blockIndex << BlockShift;
            int count = Math.Min(BlockLength, values.Length - start);
            ReadOnlySpan<uint> block = values.Slice(start, count);

            anchors[blockIndex] = block[0];

            //Zigzag the wrapping successive deltas; lane 0 is the
            //anchor's zero delta.
            bitLengthHistogram.Clear();
            zigzags[0] = 0;
            bitLengthHistogram[0]++;
            for(int i = 1; i < count; i++)
            {
                int delta = unchecked((int)(block[i] - block[i - 1]));
                uint zigzag = (uint)((delta << 1) ^ (delta >> 31));
                zigzags[i] = zigzag;
                bitLengthHistogram[BitLengthOf(zigzag)]++;
            }

            //Choose the width minimising packed bits + exception
            //cost; ties prefer the larger width (fewer patches).
            int width = ChooseWidth(bitLengthHistogram, count);
            widths[blockIndex] = (byte)width;

            //Record exceptions: lanes whose zigzag value needs more
            //bits than the chosen width.
            for(int i = 0; i < count; i++)
            {
                if(BitLengthOf(zigzags[i]) > width)
                {
                    exceptionPositions.Add((ushort)i);
                    exceptionValues.Add(zigzags[i]);
                }
            }

            exceptionStarts[blockIndex + 1] = exceptionPositions.Count;

            //Pack the lanes into freshly zeroed words.
            int wordCount = (int)(((long)count * width + 63) >> 6);
            ulong[] blockWords = new ulong[wordCount];
            backend.Pack(zigzags[..count], width, blockWords);
            payload.AddRange(blockWords);
            payloadStarts[blockIndex + 1] = payload.Count;
        }

        return new BlockPackedColumn(
            backend,
            values.Length,
            [.. payload],
            payloadStarts,
            anchors,
            widths,
            exceptionStarts,
            [.. exceptionPositions],
            [.. exceptionValues]);
    }

    /// <summary>The number of bits needed to represent <paramref name="zigzag"/>; 0 for 0.</summary>
    /// <param name="zigzag">The zigzag value.</param>
    /// <returns>The bit length.</returns>
    private static int BitLengthOf(uint zigzag)
    {
        return zigzag == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(zigzag);
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
