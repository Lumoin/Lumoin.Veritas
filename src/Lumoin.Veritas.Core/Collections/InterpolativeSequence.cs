using System;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A read-only non-decreasing <c>uint</c> sequence stored by binary
/// interpolative coding: the recursive-range midpoint scheme walked
/// with an explicit stack. The middle element of a range is written
/// in the minimal centered binary width its known bounds allow, then
/// the two halves are coded with bounds tightened by that midpoint;
/// decoding mirrors the walk. A coarse block directory records a
/// bit offset and the value bounds every <see cref="BlockLength"/>
/// elements so a window can begin its walk at the enclosing block
/// rather than the sequence start.
/// </summary>
/// <remarks>
/// <para>
/// <b>Centered minimal binary.</b> An element known to lie in the
/// inclusive value range <c>[low, high]</c> carries
/// <c>ceil(log2(high − low + 1))</c> bits at most. The centered
/// assignment spends the short codes on the values nearest the
/// range midpoint — the values interpolation makes most likely —
/// so the shorter codeword length falls to the
/// <c>2^ceil − (high − low + 1)</c> central values and the longer
/// length to the rest. A range that has collapsed to a single
/// admissible value costs zero bits. The monotone contract lets the
/// walk tighten each child range: the left half's values cannot
/// exceed the midpoint and the right half's cannot fall below it.
/// </para>
/// <para>
/// <b>Block directory.</b> Coding restarts at each block boundary
/// from the prior block's last value, and the directory stores that
/// value alongside the payload bit offset where the block begins.
/// <see cref="Decode(int, int, Span{uint})"/> seeks to the block
/// containing its window start and walks forward from there, so its
/// cost is the partial leading block plus the requested span — never
/// the whole sequence. The directory entries count toward
/// <see cref="BitCount"/>.
/// </para>
/// <para>
/// <b>Sequential decode only.</b> Interpolative coding is decoded by
/// walking ranges in order; there is no constant-time
/// <c>Access</c> or successor probe, and none is offered. A consumer
/// that wants bounded random reads chooses a block size and decodes
/// the enclosing block — that block-mode usage is the consumer's
/// affair, layered above this primitive rather than promised by it.
/// </para>
/// </remarks>
public sealed class InterpolativeSequence
{
    /// <summary>The block length as a shift: a directory entry covers 2^7 = 128 elements.</summary>
    public const int BlockShift = 7;

    /// <summary>The number of elements a directory entry covers (the last block may be shorter).</summary>
    public const int BlockLength = 1 << BlockShift;

    //The coded payload, bit-packed most-significant-bit first within
    //each 64-bit word so a codeword's leading bits read as its prefix.
    private readonly ulong[] payload;

    //The payload bit offset where each block's coding begins; one
    //entry per block.
    private readonly long[] blockBitOffsets;

    //The value the block before each block ended on — the lower
    //bound seeding the block's first range. The first block seeds
    //from zero.
    private readonly uint[] blockBaseValues;

    /// <summary>The number of elements in the sequence.</summary>
    public int Count { get; }

    /// <summary>The total bit count of the whole structure: the coded payload plus the block directory.</summary>
    public long BitCount =>
        TotalPayloadBits
        + ((long)blockBitOffsets.Length * (sizeof(long) * 8))
        + ((long)blockBaseValues.Length * (sizeof(uint) * 8));

    //The number of payload bits actually written, before word
    //rounding — the figure the directory offsets index into.
    private long TotalPayloadBits { get; }

    /// <summary>Wraps the coded payload and directory; callers reach instances through <see cref="Build(ReadOnlySpan{uint})"/>.</summary>
    /// <param name="count">The element count.</param>
    /// <param name="payload">The coded bit payload.</param>
    /// <param name="totalPayloadBits">The number of payload bits written.</param>
    /// <param name="blockBitOffsets">The per-block payload bit offsets.</param>
    /// <param name="blockBaseValues">The per-block seeding lower bounds.</param>
    private InterpolativeSequence(
        int count,
        ulong[] payload,
        long totalPayloadBits,
        long[] blockBitOffsets,
        uint[] blockBaseValues)
    {
        Count = count;
        this.payload = payload;
        TotalPayloadBits = totalPayloadBits;
        this.blockBitOffsets = blockBitOffsets;
        this.blockBaseValues = blockBaseValues;
    }

    /// <summary>
    /// Decodes the window <c>[start, start + count)</c> into
    /// <paramref name="destination"/>. The walk seeks to the block
    /// holding <paramref name="start"/> and decodes forward from
    /// there, discarding the leading block elements before the
    /// window; its cost is that partial leading block plus
    /// <paramref name="count"/> elements.
    /// </summary>
    /// <param name="start">The inclusive window start index.</param>
    /// <param name="count">The number of elements to decode.</param>
    /// <param name="destination">Receives the window; at least <paramref name="count"/> long, of which exactly that many entries are written.</param>
    /// <exception cref="ArgumentOutOfRangeException">The window falls outside <c>[0, Count]</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void Decode(int start, int count, Span<uint> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + (long)count, Count);

        if(destination.Length < count)
        {
            throw new ArgumentException("The destination is shorter than the window.", nameof(destination));
        }

        if(count == 0)
        {
            return;
        }

        int block = start >> BlockShift;
        int blockStart = block << BlockShift;
        var cursor = new BitCursor(payload, blockBitOffsets[block]);
        uint lowerBound = blockBaseValues[block];

        //The leading block decodes whole; only the window slice is
        //kept, the run-up elements are discarded as they emerge. The
        //cursor walks forward across contiguous block codings, so the
        //lower bound carries over from each block's last value.
        Span<uint> blockBuffer = stackalloc uint[BlockLength];
        int written = 0;
        int decodeStart = blockStart;

        while(written < count)
        {
            int decodeEnd = Math.Min(Count, decodeStart + BlockLength);
            int blockCount = decodeEnd - decodeStart;
            DecodeRange(ref cursor, blockBuffer[..blockCount], lowerBound, uint.MaxValue);

            for(int i = 0; i < blockCount && written < count; i++)
            {
                if(decodeStart + i >= start)
                {
                    destination[written] = blockBuffer[i];
                    written++;
                }
            }

            lowerBound = blockBuffer[blockCount - 1];
            decodeStart = decodeEnd;
        }
    }

    /// <summary>
    /// Decodes one block's elements into <paramref name="output"/>
    /// from <paramref name="cursor"/>: the iterative midpoint walk
    /// over the index range, with the value bounds tightened by each
    /// resolved midpoint. The first element's lower bound is
    /// <paramref name="seedLow"/>; the last block's upper bound is
    /// <paramref name="seedHigh"/>.
    /// </summary>
    /// <param name="cursor">The payload bit cursor, advanced past the block.</param>
    /// <param name="output">Receives the block's values; its length is the block's element count.</param>
    /// <param name="seedLow">The lower bound seeding the block's first range.</param>
    /// <param name="seedHigh">The upper bound closing the block's last range.</param>
    private static void DecodeRange(ref BitCursor cursor, Span<uint> output, uint seedLow, uint seedHigh)
    {
        if(output.Length == 0)
        {
            return;
        }

        Span<RangeFrame> stack = output.Length <= MaxStackDepth ? stackalloc RangeFrame[output.Length] : new RangeFrame[output.Length];
        int top = 0;
        stack[top++] = new RangeFrame(0, output.Length - 1, seedLow, seedHigh);

        while(top > 0)
        {
            RangeFrame frame = stack[--top];
            int first = frame.First;
            int last = frame.Last;
            uint low = frame.Low;
            uint high = frame.High;
            int mid = first + ((last - first) >> 1);

            uint value = DecodeCentered(ref cursor, low, high);
            output[mid] = value;

            //The right half is pushed first so the left half is
            //popped and decoded first — the encoder's order.
            if(mid + 1 <= last)
            {
                stack[top++] = new RangeFrame(mid + 1, last, value, high);
            }

            if(first <= mid - 1)
            {
                stack[top++] = new RangeFrame(first, mid - 1, low, value);
            }
        }
    }

    /// <summary>
    /// Packs <paramref name="values"/> into an interpolatively-coded
    /// sequence. The values must be non-decreasing.
    /// </summary>
    /// <param name="values">The sequence values, non-decreasing.</param>
    /// <returns>The coded sequence.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is not non-decreasing.</exception>
    public static InterpolativeSequence Build(ReadOnlySpan<uint> values)
    {
        for(int i = 1; i < values.Length; i++)
        {
            if(values[i] < values[i - 1])
            {
                throw new ArgumentException("The values must be non-decreasing.", nameof(values));
            }
        }

        int blockCount = (values.Length + BlockLength - 1) >> BlockShift;
        long[] blockBitOffsets = new long[blockCount];
        uint[] blockBaseValues = new uint[blockCount];
        var writer = new BitWriter();

        uint seedLow = 0;
        for(int block = 0; block < blockCount; block++)
        {
            int start = block << BlockShift;
            int count = Math.Min(BlockLength, values.Length - start);
            blockBitOffsets[block] = writer.BitLength;
            blockBaseValues[block] = seedLow;

            EncodeRange(ref writer, values.Slice(start, count), seedLow, uint.MaxValue);
            seedLow = values[start + count - 1];
        }

        return new InterpolativeSequence(
            values.Length,
            writer.ToPayload(out long totalPayloadBits),
            totalPayloadBits,
            blockBitOffsets,
            blockBaseValues);
    }

    /// <summary>
    /// Codes one block's elements into <paramref name="writer"/>:
    /// the iterative midpoint walk over the index range, writing each
    /// midpoint in the centered minimal width its tightened bounds
    /// allow.
    /// </summary>
    /// <param name="writer">The payload bit writer.</param>
    /// <param name="block">The block's values, non-decreasing.</param>
    /// <param name="seedLow">The lower bound seeding the block's first range.</param>
    /// <param name="seedHigh">The upper bound closing the block's last range.</param>
    private static void EncodeRange(ref BitWriter writer, ReadOnlySpan<uint> block, uint seedLow, uint seedHigh)
    {
        if(block.Length == 0)
        {
            return;
        }

        Span<RangeFrame> stack = block.Length <= MaxStackDepth ? stackalloc RangeFrame[block.Length] : new RangeFrame[block.Length];
        int top = 0;
        stack[top++] = new RangeFrame(0, block.Length - 1, seedLow, seedHigh);

        while(top > 0)
        {
            RangeFrame frame = stack[--top];
            int first = frame.First;
            int last = frame.Last;
            uint low = frame.Low;
            uint high = frame.High;
            int mid = first + ((last - first) >> 1);

            uint value = block[mid];
            EncodeCentered(ref writer, value, low, high);

            if(mid + 1 <= last)
            {
                stack[top++] = new RangeFrame(mid + 1, last, value, high);
            }

            if(first <= mid - 1)
            {
                stack[top++] = new RangeFrame(first, mid - 1, low, value);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> in the centered minimal
    /// binary code for the inclusive range <c>[low, high]</c>. A
    /// range of one admissible value writes nothing; otherwise the
    /// central values take the shorter codeword length and the rest
    /// the longer, by a truncated binary code whose short region is
    /// rotated to the range centre.
    /// </summary>
    /// <param name="writer">The payload bit writer.</param>
    /// <param name="value">The value to code, within <c>[low, high]</c>.</param>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    private static void EncodeCentered(ref BitWriter writer, uint value, uint low, uint high)
    {
        ulong span = (ulong)high - low + 1;
        if(span <= 1)
        {
            return;
        }

        int width = 64 - BitOperations.LeadingZeroCount(span - 1);
        ulong shortCount = (1UL << width) - span;
        ulong offset = (ulong)(value - low);

        //Rotate so the centre of the range holds symbol 0: the
        //short-coded symbols 0..shortCount−1 then cluster around the
        //range middle, where interpolation makes values likeliest.
        ulong pivot = (span - shortCount) >> 1;
        ulong symbol = offset >= pivot ? offset - pivot : offset + span - pivot;

        if(symbol < shortCount)
        {
            writer.Write(symbol, width - 1);

            return;
        }

        writer.Write(symbol + shortCount, width);
    }

    /// <summary>
    /// Reads the centered minimal binary code for the inclusive
    /// range <c>[low, high]</c>, the mirror of
    /// <see cref="EncodeCentered"/>. A range of one admissible value
    /// reads nothing and returns it.
    /// </summary>
    /// <param name="cursor">The payload bit cursor.</param>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <returns>The decoded value.</returns>
    private static uint DecodeCentered(ref BitCursor cursor, uint low, uint high)
    {
        ulong span = (ulong)high - low + 1;
        if(span <= 1)
        {
            return low;
        }

        int width = 64 - BitOperations.LeadingZeroCount(span - 1);
        ulong shortCount = (1UL << width) - span;
        ulong pivot = (span - shortCount) >> 1;

        //Peek the short prefix: a short symbol reads (width − 1) bits,
        //a long symbol the full width — the short prefixes 0..shortCount−1
        //never collide with a long codeword's leading bits.
        ulong prefix = cursor.Peek(width - 1);
        ulong symbol;
        if(prefix < shortCount)
        {
            cursor.Advance(width - 1);
            symbol = prefix;
        }
        else
        {
            symbol = cursor.Read(width) - shortCount;
        }

        //Undo the centring rotation.
        ulong offset = symbol + pivot;
        if(offset >= span)
        {
            offset -= span;
        }

        return (uint)(low + offset);
    }

    //The midpoint walk's stack never exceeds the block length, and a
    //block is bounded; a small extra margin keeps the stack on the
    //thread stack for every block.
    private const int MaxStackDepth = BlockLength + 8;

    /// <summary>One pending index range and its value bounds in the midpoint walk.</summary>
    /// <param name="First">The inclusive first index.</param>
    /// <param name="Last">The inclusive last index.</param>
    /// <param name="Low">The inclusive lower value bound.</param>
    /// <param name="High">The inclusive upper value bound.</param>
    private readonly record struct RangeFrame(int First, int Last, uint Low, uint High);

    /// <summary>A growable most-significant-bit-first bit sink over 64-bit words.</summary>
    private struct BitWriter
    {
        //The accumulated words; each field fills from the high end of
        //the remaining room, words low-to-high across the list.
        private ulong[] words;

        //The number of bits written so far.
        private long bitLength;

        /// <summary>The number of bits written so far.</summary>
        public readonly long BitLength => bitLength;

        /// <summary>Initialises an empty writer with a small backing buffer.</summary>
        public BitWriter()
        {
            words = new ulong[16];
            bitLength = 0;
        }

        /// <summary>
        /// Writes the low <paramref name="width"/> bits of
        /// <paramref name="value"/>, most-significant bit first: the
        /// field's high bit lands at the lowest free codeword
        /// position, so a later peek of the leading bits recovers a
        /// codeword's prefix.
        /// </summary>
        /// <param name="value">The value whose low bits are written.</param>
        /// <param name="width">The number of bits to write; 0..64.</param>
        public void Write(ulong value, int width)
        {
            if(width == 0)
            {
                return;
            }

            int word = (int)(bitLength >> 6);
            int shift = (int)(bitLength & 63);

            //A straddling field touches the next word too, so capacity
            //covers both.
            EnsureWord(word + 2);

            ulong masked = width == 64 ? value : value & ((1UL << width) - 1);

            //Place the field high-bit-first: its top bit sits at the
            //high end of the remaining room in the word.
            int room = 64 - shift;
            if(width <= room)
            {
                words[word] |= masked << (room - width);
            }
            else
            {
                words[word] |= masked >> (width - room);
                words[word + 1] |= masked << (64 - (width - room));
            }

            bitLength += width;
        }

        /// <summary>Returns the packed words trimmed to the written length, and reports the exact bit length.</summary>
        /// <param name="totalBits">Receives the exact number of bits written.</param>
        /// <returns>The packed payload words.</returns>
        public readonly ulong[] ToPayload(out long totalBits)
        {
            totalBits = bitLength;
            int wordCount = (int)((bitLength + 63) >> 6);

            return words.AsSpan(0, wordCount).ToArray();
        }

        /// <summary>Grows the backing buffer to hold at least <paramref name="wordCount"/> words.</summary>
        /// <param name="wordCount">The required word capacity.</param>
        private void EnsureWord(int wordCount)
        {
            if(wordCount <= words.Length)
            {
                return;
            }

            int capacity = words.Length;
            while(capacity < wordCount)
            {
                capacity <<= 1;
            }

            Array.Resize(ref words, capacity);
        }
    }

    /// <summary>A most-significant-bit-first reader over packed 64-bit words, positioned by an absolute bit offset.</summary>
    private struct BitCursor
    {
        //The packed words being read.
        private readonly ulong[] words;

        //The next bit offset to read from.
        private long bitOffset;

        /// <summary>Positions a cursor at <paramref name="startBitOffset"/> in <paramref name="words"/>.</summary>
        /// <param name="words">The packed words.</param>
        /// <param name="startBitOffset">The absolute starting bit offset.</param>
        public BitCursor(ulong[] words, long startBitOffset)
        {
            this.words = words;
            bitOffset = startBitOffset;
        }

        /// <summary>Reads <paramref name="width"/> bits at the cursor without advancing it, the leading bit most significant.</summary>
        /// <param name="width">The number of bits to read; 0..64.</param>
        /// <returns>The bits, right-aligned.</returns>
        public readonly ulong Peek(int width)
        {
            if(width == 0)
            {
                return 0;
            }

            int word = (int)(bitOffset >> 6);
            int shift = (int)(bitOffset & 63);
            int room = 64 - shift;

            //The leading bits occupy the high end of the remaining room
            //in the current word; a field wider than that room spills
            //into the next word's high bits.
            if(width <= room)
            {
                ulong bits = words[word] >> (room - width);

                return width == 64 ? bits : bits & ((1UL << width) - 1);
            }

            int spill = width - room;
            ulong high = (words[word] & ((1UL << room) - 1)) << spill;

            //Near the payload end a short peek can ask for more bits
            //than were written, so a missing spill word reads as zero —
            //the surplus bits are never consumed.
            ulong low = word + 1 < words.Length ? words[word + 1] >> (64 - spill) : 0;

            return high | low;
        }

        /// <summary>Advances the cursor by <paramref name="width"/> bits.</summary>
        /// <param name="width">The number of bits to advance.</param>
        public void Advance(int width)
        {
            bitOffset += width;
        }

        /// <summary>Reads <paramref name="width"/> bits and advances the cursor past them.</summary>
        /// <param name="width">The number of bits to read; 0..64.</param>
        /// <returns>The bits, right-aligned.</returns>
        public ulong Read(int width)
        {
            ulong bits = Peek(width);
            bitOffset += width;

            return bits;
        }
    }
}
