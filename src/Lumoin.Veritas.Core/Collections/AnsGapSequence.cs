using System;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A read-only non-decreasing <c>uint</c> sequence stored as
/// entropy-coded gaps. Each element's gap from its predecessor (the
/// first element's gap is measured from zero) is split into a
/// nibble-length symbol and the gap's nibbles, and both symbol streams
/// are coded by a range-variant asymmetric-numeral-systems coder
/// against a frequency model fitted to this build. A coarse block
/// directory records each block's coder state and seeding value so a
/// window can begin decoding at the enclosing block rather than the
/// sequence start.
/// </summary>
/// <remarks>
/// <para>
/// <b>Gap decomposition.</b> The gap of an element is its difference
/// from the previous element, the first measured from zero; the
/// monotone contract makes every gap non-negative. A gap is split into
/// two symbol kinds: a <i>length</i> symbol — the number of nibbles
/// (4-bit groups) its value occupies, 0..8, where 0 is a zero gap —
/// and that many <i>nibble</i> symbols, 0..15, emitted low-nibble
/// first. The length stream and the nibble stream each carry their own
/// per-build frequency model; both models count toward
/// <see cref="BitCount"/>.
/// </para>
/// <para>
/// <b>Range-variant numeral-systems coder.</b> Each model is a
/// cumulative frequency table normalised to a fixed total of
/// 2^<see cref="ModelPrecisionBits"/>. The coder holds a 32-bit state
/// and renormalises one byte at a time: on encode the state's low byte
/// is emitted whenever it would overflow the symbol's range; on decode
/// a byte is folded back in whenever the state falls below the
/// renormalisation floor. A model that fits a single symbol (every gap
/// identical, so one length symbol and possibly no nibbles) spends no
/// state bits on that stream — the symbol is implied. An empty stream
/// (no nibbles at all, as when every gap is zero) codes nothing.
/// </para>
/// <para>
/// <b>Interleaved streams.</b> Throughput comes from four coder lanes
/// run in lockstep. Within a block the symbols of each stream are
/// assigned to lanes round-robin by their position in that stream:
/// stream symbol <c>k</c> belongs to lane <c>k mod 4</c>. The length
/// stream and the nibble stream are coded independently, each across
/// the same four lanes, into one shared byte buffer. Because a
/// numeral-systems coder emits in reverse, a block is encoded by
/// walking its symbols backward; the four lane states are flushed at
/// the block's end and the directory records all four seeding states.
/// On decode the four lanes are seeded from the directory and read
/// forward, each consuming the renormalisation bytes the matching
/// encode lane produced.
/// </para>
/// <para>
/// <b>Block directory.</b> Coding restarts at each block boundary from
/// the prior block's last value; the directory stores that value, the
/// byte offset where the block's renormalisation bytes begin, and the
/// four flushed lane states for each stream.
/// <see cref="Decode(int, int, Span{uint})"/> seeks to the block
/// containing its window start and decodes forward from there,
/// discarding the leading block elements before the window, so its
/// cost is the partial leading block plus the requested span — never
/// the whole sequence. The directory entries count toward
/// <see cref="BitCount"/>.
/// </para>
/// <para>
/// <b>Sequential decode only.</b> A numeral-systems coder is decoded
/// by replaying its symbols in order; there is no constant-time
/// <c>Access</c> or successor probe, and none is offered. A consumer
/// that wants bounded random reads chooses a block size and decodes
/// the enclosing block — that block-mode usage is the consumer's
/// affair, layered above this primitive rather than promised by it.
/// </para>
/// </remarks>
public sealed class AnsGapSequence
{
    /// <summary>The block length as a shift: a directory entry covers 2^9 = 512 elements.</summary>
    public const int BlockShift = 9;

    /// <summary>The number of elements a directory entry covers (the last block may be shorter).</summary>
    public const int BlockLength = 1 << BlockShift;

    /// <summary>The number of interleaved coder lanes decoded in lockstep.</summary>
    public const int LaneCount = 4;

    //The frequency-model total is 2^this; cumulative frequencies and the
    //state's renormalisation floor are sized against it.
    private const int ModelPrecisionBits = 12;

    //The renormalisation total a model's frequencies sum to.
    private const uint ModelTotal = 1u << ModelPrecisionBits;

    //A lane's state holds at most this many bits; renormalisation keeps
    //it in [RenormFloor, RenormCeiling).
    private const int StateBits = 32;

    //The lower bound a decode lane's state is topped back up to by
    //folding in bytes; the encode lane emits a byte whenever a symbol
    //would push the state at or above the matching ceiling.
    private const uint RenormFloor = 1u << (StateBits - 8);

    //The number of length symbols: a uint gap occupies at most eight
    //nibbles, and zero nibbles marks a zero gap.
    private const int LengthSymbolCount = 9;

    //The number of nibble symbols.
    private const int NibbleSymbolCount = 16;

    //The renormalisation bytes for both streams across all lanes,
    //laid out per block as the lanes emit them.
    private readonly byte[] payload;

    //The cumulative frequency table for the length stream; one extra
    //entry holds the total. Empty when the length stream is single-symbol.
    private readonly uint[] lengthCumulative;

    //The per-symbol frequency table for the length stream, for encode
    //and for the decode symbol lookup.
    private readonly uint[] lengthFrequency;

    //The single length symbol when the length model is degenerate, else -1.
    private readonly int lengthSingleSymbol;

    //The cumulative frequency table for the nibble stream.
    private readonly uint[] nibbleCumulative;

    //The per-symbol frequency table for the nibble stream.
    private readonly uint[] nibbleFrequency;

    //The single nibble symbol when the nibble model is degenerate, else -1.
    private readonly int nibbleSingleSymbol;

    //One directory entry per block, holding the block's seeding value,
    //its payload byte offset, and the flushed lane states of both streams.
    private readonly BlockEntry[] blocks;

    /// <summary>The number of elements in the sequence.</summary>
    public int Count { get; }

    /// <summary>The total bit count of the whole structure: the renormalisation payload, both frequency models, and the block directory.</summary>
    public long BitCount =>
        ((long)payload.Length * 8)
        + ((long)(lengthCumulative.Length + lengthFrequency.Length) * (sizeof(uint) * 8))
        + ((long)(nibbleCumulative.Length + nibbleFrequency.Length) * (sizeof(uint) * 8))
        + ((long)blocks.Length * BlockEntry.BitSize);

    /// <summary>Wraps the coded payload, frequency models, and directory; callers reach instances through <see cref="Build(ReadOnlySpan{uint})"/>.</summary>
    /// <param name="count">The element count.</param>
    /// <param name="payload">The renormalisation byte payload.</param>
    /// <param name="lengthCumulative">The length stream's cumulative frequency table.</param>
    /// <param name="lengthFrequency">The length stream's per-symbol frequency table.</param>
    /// <param name="lengthSingleSymbol">The sole length symbol when degenerate, else -1.</param>
    /// <param name="nibbleCumulative">The nibble stream's cumulative frequency table.</param>
    /// <param name="nibbleFrequency">The nibble stream's per-symbol frequency table.</param>
    /// <param name="nibbleSingleSymbol">The sole nibble symbol when degenerate, else -1.</param>
    /// <param name="blocks">The per-block directory entries.</param>
    private AnsGapSequence(
        int count,
        byte[] payload,
        uint[] lengthCumulative,
        uint[] lengthFrequency,
        int lengthSingleSymbol,
        uint[] nibbleCumulative,
        uint[] nibbleFrequency,
        int nibbleSingleSymbol,
        BlockEntry[] blocks)
    {
        Count = count;
        this.payload = payload;
        this.lengthCumulative = lengthCumulative;
        this.lengthFrequency = lengthFrequency;
        this.lengthSingleSymbol = lengthSingleSymbol;
        this.nibbleCumulative = nibbleCumulative;
        this.nibbleFrequency = nibbleFrequency;
        this.nibbleSingleSymbol = nibbleSingleSymbol;
        this.blocks = blocks;
    }

    /// <summary>
    /// Decodes the window <c>[start, start + count)</c> into
    /// <paramref name="destination"/>. The walk seeks to the block
    /// holding <paramref name="start"/> and decodes forward from there,
    /// discarding the leading block elements before the window; its cost
    /// is that partial leading block plus <paramref name="count"/>
    /// elements.
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

        Span<uint> blockBuffer = stackalloc uint[BlockLength];
        int written = 0;
        int decodeStart = blockStart;
        int decodeBlock = block;

        //Each block decodes whole from its directory seed; only the
        //window slice is kept, the run-up elements are discarded as they
        //emerge. The walk advances block by block until the window fills.
        while(written < count)
        {
            int decodeEnd = Math.Min(Count, decodeStart + BlockLength);
            int blockCount = decodeEnd - decodeStart;
            DecodeBlock(decodeBlock, blockBuffer[..blockCount]);

            for(int i = 0; i < blockCount && written < count; i++)
            {
                if(decodeStart + i >= start)
                {
                    destination[written] = blockBuffer[i];
                    written++;
                }
            }

            decodeStart = decodeEnd;
            decodeBlock++;
        }
    }

    /// <summary>
    /// Decodes one block's values into <paramref name="output"/>: the
    /// four lanes are seeded from the directory, the length stream is
    /// replayed to recover each gap's nibble count, then the nibble
    /// stream is replayed to recover the gaps, and the gaps accumulate
    /// onto the block's seeding value.
    /// </summary>
    /// <param name="block">The block index.</param>
    /// <param name="output">Receives the block's values; its length is the block's element count.</param>
    private void DecodeBlock(int block, Span<uint> output)
    {
        if(output.Length == 0)
        {
            return;
        }

        BlockEntry entry = blocks[block];
        var reader = new ByteReader(payload, entry.PayloadByteOffset);

        //The length stream: one symbol per element gives the nibble
        //count, and the running total of nibble counts sizes the nibble
        //stream's replay.
        Span<int> nibbleCounts = stackalloc int[output.Length];
        int totalNibbles = DecodeStream(
            ref reader,
            entry.LengthStates,
            lengthCumulative,
            lengthFrequency,
            lengthSingleSymbol,
            nibbleCounts,
            output.Length,
            sumOfSymbols: true);

        //The nibble stream: totalNibbles symbols, consumed back into the
        //per-element gaps low-nibble first.
        Span<int> nibbleSymbols = stackalloc int[BlockLength * 8];
        DecodeStream(
            ref reader,
            entry.NibbleStates,
            nibbleCumulative,
            nibbleFrequency,
            nibbleSingleSymbol,
            nibbleSymbols[..totalNibbles],
            totalNibbles,
            sumOfSymbols: false);

        uint previous = entry.BaseValue;
        int nibbleCursor = 0;
        for(int i = 0; i < output.Length; i++)
        {
            int nibbleCount = nibbleCounts[i];
            uint gap = 0;
            for(int n = 0; n < nibbleCount; n++)
            {
                gap |= (uint)nibbleSymbols[nibbleCursor + n] << (n * 4);
            }

            nibbleCursor += nibbleCount;
            previous = unchecked(previous + gap);
            output[i] = previous;
        }
    }

    /// <summary>
    /// Decodes <paramref name="symbolCount"/> symbols of one stream
    /// across the four lanes into <paramref name="symbols"/>, reading
    /// renormalisation bytes forward from <paramref name="reader"/>. A
    /// single-symbol model fills directly without touching the coder
    /// state.
    /// </summary>
    /// <param name="reader">The renormalisation byte reader, advanced past the stream.</param>
    /// <param name="seedStates">The four lane states the directory recorded for this stream in this block.</param>
    /// <param name="cumulative">The stream's cumulative frequency table, or empty when single-symbol.</param>
    /// <param name="frequency">The stream's per-symbol frequency table, or empty when single-symbol.</param>
    /// <param name="singleSymbol">The sole symbol when the model is degenerate, else -1.</param>
    /// <param name="symbols">Receives the decoded symbols.</param>
    /// <param name="symbolCount">The number of symbols to decode.</param>
    /// <param name="sumOfSymbols">When set, returns the sum of the decoded symbols, else zero.</param>
    /// <returns>The sum of the decoded symbols when <paramref name="sumOfSymbols"/> is set, else zero.</returns>
    private static int DecodeStream(
        ref ByteReader reader,
        LaneStates seedStates,
        uint[] cumulative,
        uint[] frequency,
        int singleSymbol,
        Span<int> symbols,
        int symbolCount,
        bool sumOfSymbols)
    {
        if(symbolCount == 0)
        {
            return 0;
        }

        //A degenerate single-symbol stream carries no coder state; every
        //symbol is the implied one.
        if(singleSymbol >= 0)
        {
            int sum = 0;
            for(int i = 0; i < symbolCount; i++)
            {
                symbols[i] = singleSymbol;
                sum += singleSymbol;
            }

            return sumOfSymbols ? sum : 0;
        }

        Span<uint> states = stackalloc uint[LaneCount];
        seedStates.CopyTo(states);

        int total = 0;
        for(int i = 0; i < symbolCount; i++)
        {
            int lane = i & (LaneCount - 1);
            uint state = states[lane];

            //The slot in the model total the state currently selects.
            uint slot = state & (ModelTotal - 1);
            int symbol = FindSymbol(cumulative, slot);
            uint start = cumulative[symbol];
            uint freq = frequency[symbol];

            //Fold the model out of the state, then top the state back up
            //from the renormalisation bytes while it sits below the floor.
            state = (freq * (state >> ModelPrecisionBits)) + slot - start;
            while(state < RenormFloor)
            {
                state = (state << 8) | reader.ReadByte();
            }

            states[lane] = state;
            symbols[i] = symbol;
            if(sumOfSymbols)
            {
                total += symbol;
            }
        }

        return total;
    }

    /// <summary>
    /// Finds the symbol whose cumulative interval contains
    /// <paramref name="slot"/> by a branchless-bounded linear scan over
    /// the cumulative table; the alphabets here are tiny so a scan beats
    /// a search structure.
    /// </summary>
    /// <param name="cumulative">The cumulative frequency table, ascending, terminated by the total.</param>
    /// <param name="slot">The slot in <c>[0, total)</c> to place.</param>
    /// <returns>The containing symbol index.</returns>
    private static int FindSymbol(uint[] cumulative, uint slot)
    {
        int symbol = 0;
        while(symbol + 1 < cumulative.Length - 1 && cumulative[symbol + 1] <= slot)
        {
            symbol++;
        }

        return symbol;
    }

    /// <summary>
    /// Packs <paramref name="values"/> into an entropy-coded gap
    /// sequence. The values must be non-decreasing.
    /// </summary>
    /// <param name="values">The sequence values, non-decreasing.</param>
    /// <returns>The coded sequence.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is not non-decreasing.</exception>
    public static AnsGapSequence Build(ReadOnlySpan<uint> values)
    {
        for(int i = 1; i < values.Length; i++)
        {
            if(values[i] < values[i - 1])
            {
                throw new ArgumentException("The values must be non-decreasing.", nameof(values));
            }
        }

        //First pass: tally the length and nibble symbol frequencies over
        //every gap so the per-build models reflect the whole sequence.
        Span<uint> lengthTally = stackalloc uint[LengthSymbolCount];
        Span<uint> nibbleTally = stackalloc uint[NibbleSymbolCount];
        uint previous = 0;
        for(int i = 0; i < values.Length; i++)
        {
            uint gap = unchecked(values[i] - previous);
            previous = values[i];
            int nibbleCount = NibbleCount(gap);
            lengthTally[nibbleCount]++;
            uint remaining = gap;
            for(int n = 0; n < nibbleCount; n++)
            {
                nibbleTally[(int)(remaining & 0xF)]++;
                remaining >>= 4;
            }
        }

        BuildModel(lengthTally, out uint[] lengthCumulative, out uint[] lengthFrequency, out int lengthSingle);
        BuildModel(nibbleTally, out uint[] nibbleCumulative, out uint[] nibbleFrequency, out int nibbleSingle);

        int blockCount = (values.Length + BlockLength - 1) >> BlockShift;
        BlockEntry[] blocks = new BlockEntry[blockCount];
        var payload = new ForwardByteBuffer();

        uint baseValue = 0;
        for(int block = 0; block < blockCount; block++)
        {
            int start = block << BlockShift;
            int count = Math.Min(BlockLength, values.Length - start);

            //A block is coded into its own reverse scratch, then its
            //finished bytes are appended forward to the payload, so blocks
            //lie in block order and each recorded offset stays valid as
            //later blocks are encoded.
            var scratch = new ByteWriter();
            EncodeBlock(
                ref scratch,
                values.Slice(start, count),
                baseValue,
                lengthCumulative,
                lengthFrequency,
                lengthSingle,
                nibbleCumulative,
                nibbleFrequency,
                nibbleSingle,
                out LaneStates lengthStates,
                out LaneStates nibbleStates);

            int payloadOffset = payload.Length;
            payload.Append(scratch.WrittenSpan);
            blocks[block] = new BlockEntry(baseValue, payloadOffset, lengthStates, nibbleStates);
            baseValue = values[start + count - 1];
        }

        return new AnsGapSequence(
            values.Length,
            payload.ToPayload(),
            lengthCumulative,
            lengthFrequency,
            lengthSingle,
            nibbleCumulative,
            nibbleFrequency,
            nibbleSingle,
            blocks);
    }

    /// <summary>
    /// Encodes one block backward across the four lanes: both streams
    /// are gathered into symbol lists, then replayed in reverse so the
    /// numeral-systems coder emits its renormalisation bytes ahead of
    /// the forward decode that consumes them. The nibble stream is
    /// emitted before the length stream so the forward decode reads the
    /// length stream first.
    /// </summary>
    /// <param name="writer">The renormalisation byte sink.</param>
    /// <param name="block">The block's values, non-decreasing.</param>
    /// <param name="baseValue">The value the block seeds its first gap from.</param>
    /// <param name="lengthCumulative">The length stream's cumulative frequency table.</param>
    /// <param name="lengthFrequency">The length stream's per-symbol frequency table.</param>
    /// <param name="lengthSingle">The sole length symbol when degenerate, else -1.</param>
    /// <param name="nibbleCumulative">The nibble stream's cumulative frequency table.</param>
    /// <param name="nibbleFrequency">The nibble stream's per-symbol frequency table.</param>
    /// <param name="nibbleSingle">The sole nibble symbol when degenerate, else -1.</param>
    /// <param name="lengthStates">Receives the flushed lane states of the length stream.</param>
    /// <param name="nibbleStates">Receives the flushed lane states of the nibble stream.</param>
    private static void EncodeBlock(
        ref ByteWriter writer,
        ReadOnlySpan<uint> block,
        uint baseValue,
        uint[] lengthCumulative,
        uint[] lengthFrequency,
        int lengthSingle,
        uint[] nibbleCumulative,
        uint[] nibbleFrequency,
        int nibbleSingle,
        out LaneStates lengthStates,
        out LaneStates nibbleStates)
    {
        //Gather both streams in forward order: the length symbol per
        //element and the nibbles of each gap low-nibble first.
        Span<int> lengthSymbols = stackalloc int[BlockLength];
        Span<int> nibbleSymbols = stackalloc int[BlockLength * 8];
        int nibbleTotal = 0;
        uint previous = baseValue;
        for(int i = 0; i < block.Length; i++)
        {
            uint gap = unchecked(block[i] - previous);
            previous = block[i];
            int nibbleCount = NibbleCount(gap);
            lengthSymbols[i] = nibbleCount;
            uint remaining = gap;
            for(int n = 0; n < nibbleCount; n++)
            {
                nibbleSymbols[nibbleTotal++] = (int)(remaining & 0xF);
                remaining >>= 4;
            }
        }

        //Encode backward so the bytes line up with the forward decode.
        //The nibble stream is encoded first, leaving its bytes deeper in
        //the buffer; the length stream's bytes land in front of them,
        //matching the decode that reads the length stream first.
        nibbleStates = EncodeStream(ref writer, nibbleSymbols[..nibbleTotal], nibbleCumulative, nibbleFrequency, nibbleSingle);
        lengthStates = EncodeStream(ref writer, lengthSymbols[..block.Length], lengthCumulative, lengthFrequency, lengthSingle);
    }

    /// <summary>
    /// Encodes one stream's symbols backward across the four lanes,
    /// emitting renormalisation bytes in reverse, and returns the four
    /// flushed lane states. A single-symbol model emits nothing and
    /// returns zeroed states.
    /// </summary>
    /// <param name="writer">The renormalisation byte sink, written in reverse.</param>
    /// <param name="symbols">The stream's symbols, in forward order.</param>
    /// <param name="cumulative">The stream's cumulative frequency table.</param>
    /// <param name="frequency">The stream's per-symbol frequency table.</param>
    /// <param name="singleSymbol">The sole symbol when the model is degenerate, else -1.</param>
    /// <returns>The four flushed lane states.</returns>
    private static LaneStates EncodeStream(
        ref ByteWriter writer,
        ReadOnlySpan<int> symbols,
        uint[] cumulative,
        uint[] frequency,
        int singleSymbol)
    {
        if(singleSymbol >= 0 || symbols.Length == 0)
        {
            return default;
        }

        Span<uint> states = stackalloc uint[LaneCount];
        for(int lane = 0; lane < LaneCount; lane++)
        {
            states[lane] = RenormFloor;
        }

        //Walk symbols in reverse, each to its lane; the lane the forward
        //decode consumes first is the one this reverse walk flushes last,
        //so the bytes interleave into the same lane order on replay.
        for(int i = symbols.Length - 1; i >= 0; i--)
        {
            int lane = i & (LaneCount - 1);
            uint state = states[lane];
            int symbol = symbols[i];
            uint freq = frequency[symbol];
            uint start = cumulative[symbol];

            //Renormalise: emit the low byte while the state would
            //overflow the symbol's slot range, lowering it into the
            //window the encode step keeps it in.
            uint ceiling = (RenormFloor >> ModelPrecisionBits) << 8;
            while(state >= freq * ceiling)
            {
                writer.WriteReverse((byte)(state & 0xFF));
                state >>= 8;
            }

            //Fold the symbol's interval into the state.
            state = ((state / freq) << ModelPrecisionBits) + (state % freq) + start;
            states[lane] = state;
        }

        return new LaneStates(states[0], states[1], states[2], states[3]);
    }

    /// <summary>
    /// Normalises a symbol tally into a cumulative and a per-symbol
    /// frequency table summing to the model total, never assigning a
    /// used symbol zero frequency. A tally with a single used symbol
    /// reports that symbol and leaves the tables empty.
    /// </summary>
    /// <param name="tally">The raw symbol counts.</param>
    /// <param name="cumulative">Receives the cumulative table, or empty when single-symbol.</param>
    /// <param name="frequency">Receives the per-symbol table, or empty when single-symbol.</param>
    /// <param name="singleSymbol">Receives the sole used symbol when degenerate, else -1.</param>
    private static void BuildModel(ReadOnlySpan<uint> tally, out uint[] cumulative, out uint[] frequency, out int singleSymbol)
    {
        int symbolCount = tally.Length;
        int usedCount = 0;
        int lastUsed = -1;
        uint grandTotal = 0;
        for(int s = 0; s < symbolCount; s++)
        {
            if(tally[s] > 0)
            {
                usedCount++;
                lastUsed = s;
                grandTotal += tally[s];
            }
        }

        //No symbols at all (an empty stream) and a single used symbol
        //both code without coder state.
        if(usedCount <= 1)
        {
            cumulative = [];
            frequency = [];
            singleSymbol = lastUsed;

            return;
        }

        singleSymbol = -1;
        frequency = new uint[symbolCount];

        //Scale every used symbol to at least one slot, then settle the
        //rounding drift onto the most frequent symbol so the table sums
        //to exactly the model total.
        uint assigned = 0;
        int richest = 0;
        uint richestTally = 0;
        for(int s = 0; s < symbolCount; s++)
        {
            if(tally[s] == 0)
            {
                continue;
            }

            uint scaled = (uint)(((ulong)tally[s] * ModelTotal) / grandTotal);
            if(scaled == 0)
            {
                scaled = 1;
            }

            frequency[s] = scaled;
            assigned += scaled;
            if(tally[s] > richestTally)
            {
                richestTally = tally[s];
                richest = s;
            }
        }

        //Settle the difference onto the richest symbol; the per-symbol
        //floor of one keeps it positive even when the drift is negative.
        frequency[richest] = (uint)((long)frequency[richest] + ((long)ModelTotal - assigned));

        cumulative = new uint[symbolCount + 1];
        uint running = 0;
        for(int s = 0; s < symbolCount; s++)
        {
            cumulative[s] = running;
            running += frequency[s];
        }

        cumulative[symbolCount] = running;
    }

    /// <summary>Counts the nibbles a gap value occupies, 0..8, where 0 marks a zero gap.</summary>
    /// <param name="gap">The gap value.</param>
    /// <returns>The nibble count.</returns>
    private static int NibbleCount(uint gap)
    {
        return gap == 0 ? 0 : ((35 - BitOperations.LeadingZeroCount(gap)) >> 2);
    }

    /// <summary>The four interleaved lane states a stream flushes at a block's end.</summary>
    /// <param name="Lane0">The first lane's flushed state.</param>
    /// <param name="Lane1">The second lane's flushed state.</param>
    /// <param name="Lane2">The third lane's flushed state.</param>
    /// <param name="Lane3">The fourth lane's flushed state.</param>
    private readonly record struct LaneStates(uint Lane0, uint Lane1, uint Lane2, uint Lane3)
    {
        /// <summary>Copies the four lane states into <paramref name="destination"/>.</summary>
        /// <param name="destination">Receives the four states; at least four long.</param>
        public void CopyTo(Span<uint> destination)
        {
            destination[0] = Lane0;
            destination[1] = Lane1;
            destination[2] = Lane2;
            destination[3] = Lane3;
        }
    }

    /// <summary>One block's directory entry: its seeding value, payload byte offset, and both streams' flushed lane states.</summary>
    /// <param name="BaseValue">The value the block seeds its first gap from.</param>
    /// <param name="PayloadByteOffset">The byte offset where the block's renormalisation bytes begin.</param>
    /// <param name="LengthStates">The length stream's flushed lane states.</param>
    /// <param name="NibbleStates">The nibble stream's flushed lane states.</param>
    private readonly record struct BlockEntry(uint BaseValue, int PayloadByteOffset, LaneStates LengthStates, LaneStates NibbleStates)
    {
        /// <summary>The directory entry's footprint in bits: the base value, the offset, and eight lane states.</summary>
        public const int BitSize = (sizeof(uint) + sizeof(int) + (8 * sizeof(uint))) * 8;
    }

    /// <summary>
    /// A byte sink the numeral-systems coder writes into in reverse:
    /// bytes are emitted from the buffer's high end downward so the
    /// finished payload reads forward in the order the decode consumes.
    /// </summary>
    private struct ByteWriter
    {
        //The backing buffer; bytes fill from the high end downward.
        private byte[] buffer;

        //The index just past the lowest byte written so far.
        private int head;

        /// <summary>Initialises an empty writer with a small backing buffer.</summary>
        public ByteWriter()
        {
            buffer = new byte[64];
            head = buffer.Length;
        }

        /// <summary>The number of bytes written so far.</summary>
        public readonly int Length => buffer.Length - head;

        /// <summary>Writes one byte ahead of the bytes already written, growing the buffer from its low end as needed.</summary>
        /// <param name="value">The byte to write.</param>
        public void WriteReverse(byte value)
        {
            if(head == 0)
            {
                Grow();
            }

            head--;
            buffer[head] = value;
        }

        /// <summary>The written bytes in forward order — the order the decode consumes them.</summary>
        public readonly ReadOnlySpan<byte> WrittenSpan => buffer.AsSpan(head);

        /// <summary>Doubles the backing buffer, keeping the written bytes at its high end.</summary>
        private void Grow()
        {
            int written = Length;
            byte[] grown = new byte[buffer.Length * 2];
            buffer.AsSpan(head).CopyTo(grown.AsSpan(grown.Length - written));
            buffer = grown;
            head = grown.Length - written;
        }
    }

    /// <summary>A forward-growing byte sink the per-block reverse scratches are appended into in block order.</summary>
    private struct ForwardByteBuffer
    {
        //The backing buffer; bytes fill from the low end upward.
        private byte[] buffer;

        //The number of bytes appended so far.
        private int length;

        /// <summary>Initialises an empty buffer with a small backing array.</summary>
        public ForwardByteBuffer()
        {
            buffer = new byte[64];
            length = 0;
        }

        /// <summary>The number of bytes appended so far — the offset the next append will land at.</summary>
        public readonly int Length => length;

        /// <summary>Appends <paramref name="bytes"/> after the bytes already held, growing the buffer as needed.</summary>
        /// <param name="bytes">The bytes to append.</param>
        public void Append(ReadOnlySpan<byte> bytes)
        {
            EnsureCapacity(length + bytes.Length);
            bytes.CopyTo(buffer.AsSpan(length));
            length += bytes.Length;
        }

        /// <summary>Returns the appended bytes trimmed to length.</summary>
        /// <returns>The packed payload bytes.</returns>
        public readonly byte[] ToPayload()
        {
            return buffer.AsSpan(0, length).ToArray();
        }

        /// <summary>Grows the backing buffer to hold at least <paramref name="required"/> bytes.</summary>
        /// <param name="required">The required capacity.</param>
        private void EnsureCapacity(int required)
        {
            if(required <= buffer.Length)
            {
                return;
            }

            int capacity = buffer.Length;
            while(capacity < required)
            {
                capacity <<= 1;
            }

            Array.Resize(ref buffer, capacity);
        }
    }

    /// <summary>A forward byte reader the numeral-systems decode folds renormalisation bytes back from.</summary>
    private struct ByteReader
    {
        //The payload being read.
        private readonly byte[] bytes;

        //The next byte offset to read from.
        private int offset;

        /// <summary>Positions a reader at <paramref name="startOffset"/> in <paramref name="bytes"/>.</summary>
        /// <param name="bytes">The payload bytes.</param>
        /// <param name="startOffset">The starting byte offset.</param>
        public ByteReader(byte[] bytes, int startOffset)
        {
            this.bytes = bytes;
            offset = startOffset;
        }

        /// <summary>Reads one byte and advances; a read past the payload end yields zero, which the renormalisation window never relies on.</summary>
        /// <returns>The next byte, or zero past the end.</returns>
        public byte ReadByte()
        {
            byte value = offset < bytes.Length ? bytes[offset] : (byte)0;
            offset++;

            return value;
        }
    }
}
