using System;
using System.Diagnostics;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A read-only bit-vector that stores fixed-width blocks as a popcount
/// <c>class</c> plus an enumerative <c>offset</c> — the block's rank within the
/// lexicographic enumeration of all patterns of its class — so a dense-but-skewed
/// fill approaches its order-zero entropy bound instead of paying one stored bit
/// per input bit. It answers the same <c>Access</c>, <c>Rank1</c>/<c>Rank0</c>,
/// and <c>Select1</c>/<c>Select0</c> counting primitives the succinct sequences
/// build on. Single-writer at build, then read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> The valid bits split into fixed <see cref="BlockBits"/>-bit
/// blocks. Each block's class — its set-bit count, in <c>[0, BlockBits]</c> — is
/// stored in <see cref="ClassBits"/> bits in a packed class array. Each block's
/// offset is its pattern's index within the C(<see cref="BlockBits"/>, class)
/// lexicographically ordered patterns of that class, stored in
/// <c>ceil(log2(C(BlockBits, class)))</c> bits in a bit-packed offset stream;
/// the all-zero and all-set classes need zero offset bits. A superblock directory
/// records, at every <see cref="SuperblockBlocks"/>-th block boundary, the
/// cumulative set-bit count and the offset-stream bit position, so an operation
/// seeks O(1) to a superblock and then walks at most <see cref="SuperblockBlocks"/>
/// blocks, reading their classes to advance the offset cursor.
/// </para>
/// </remarks>
[DebuggerDisplay("BlockCompressedBitVector Length={Length} OneCount={OneCount}")]
public sealed class BlockCompressedBitVector
{
    /// <summary>The number of input bits per block; the class enumerates the C(BlockBits, class) patterns of each popcount.</summary>
    private const int BlockBits = 15;

    /// <summary>The number of bits a block class occupies in the packed class array; it covers the class range <c>[0, BlockBits]</c>.</summary>
    private const int ClassBits = 4;

    /// <summary>The number of blocks per superblock; the directory stores one cumulative-count and one offset-position entry per superblock.</summary>
    private const int SuperblockBlocks = 32;

    /// <summary>The stride of one row in the flattened <see cref="Binomial"/> table.</summary>
    private const int BinomialStride = BlockBits + 1;

    /// <summary>The binomial coefficients C(n, k) for n, k in <c>[0, BlockBits]</c>, flattened row-major as <c>C(n, k) = Binomial[n * BinomialStride + k]</c> — the enumerative-coding table walked high-to-low to map a pattern to its class and offset and back.</summary>
    private static readonly uint[] Binomial = BuildBinomial();

    /// <summary>The offset-field bit width per class — <c>ceil(log2(C(BlockBits, class)))</c> — indexed by class in <c>[0, BlockBits]</c>; zero for the all-zero and all-set classes.</summary>
    private static readonly int[] OffsetWidth = BuildOffsetWidth();

    //Each block's class (set-bit count), ClassBits per block, little-endian
    //within each 64-bit word.
    private readonly ulong[] classWords;

    //Each block's enumerative offset, OffsetWidth[class] bits per block packed
    //back to back, little-endian within each 64-bit word; classes with a single
    //pattern contribute nothing.
    private readonly ulong[] offsetWords;

    //Set bits before each superblock boundary — the cumulative tier walked by
    //rank and binary-searched by select; one extra entry covers the vector's end.
    private readonly int[] superblockOnes;

    //The offset-stream bit position at each superblock boundary, so a walk
    //resumes its offset cursor there without rescanning prior superblocks.
    private readonly long[] superblockOffsetBit;

    /// <summary>The number of bits in the vector.</summary>
    public int Length { get; }

    /// <summary>The number of set bits in the vector.</summary>
    public int OneCount { get; }

    /// <summary>The number of unset bits in the vector.</summary>
    public int ZeroCount => Length - OneCount;

    /// <summary>The total footprint in bits — the packed class array, the bit-packed offset stream, and the two superblock directories.</summary>
    public long BitCount =>
        ((long)classWords.Length * 64)
        + ((long)offsetWords.Length * 64)
        + ((long)superblockOnes.Length * 32)
        + ((long)superblockOffsetBit.Length * 64);

    /// <summary>Constructs the vector from its built components.</summary>
    /// <param name="classWords">The packed class array.</param>
    /// <param name="offsetWords">The bit-packed offset stream.</param>
    /// <param name="superblockOnes">The cumulative set-bit directory.</param>
    /// <param name="superblockOffsetBit">The offset-stream bit-position directory.</param>
    /// <param name="length">The valid bit length.</param>
    /// <param name="oneCount">The set-bit count.</param>
    private BlockCompressedBitVector(
        ulong[] classWords,
        ulong[] offsetWords,
        int[] superblockOnes,
        long[] superblockOffsetBit,
        int length,
        int oneCount)
    {
        this.classWords = classWords;
        this.offsetWords = offsetWords;
        this.superblockOnes = superblockOnes;
        this.superblockOffsetBit = superblockOffsetBit;
        Length = length;
        OneCount = oneCount;
    }

    /// <summary>Builds the vector over a packed word payload, re-encoding its bits into the class-plus-offset form; the payload is read, not retained.</summary>
    /// <param name="payload">The packed bits, little-endian within each word; padding bits beyond <paramref name="bitLength"/> are ignored. The array is read during the build and not retained.</param>
    /// <param name="bitLength">The number of valid bits.</param>
    /// <returns>The built vector.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The payload length does not match <paramref name="bitLength"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitLength"/> is negative.</exception>
    public static BlockCompressedBitVector Build(ulong[] payload, int bitLength)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfNegative(bitLength);

        int wordCount = (bitLength + 63) >> 6;
        if(payload.Length != wordCount)
        {
            throw new ArgumentException("The payload length does not match the bit length.", nameof(payload));
        }

        int blockCount = (bitLength + BlockBits - 1) / BlockBits;
        int superblockCount = (blockCount + SuperblockBlocks - 1) / SuperblockBlocks;

        ulong[] classWords = new ulong[(((long)blockCount * ClassBits) + 63) >> 6];
        int[] superblockOnes = new int[superblockCount + 1];
        long[] superblockOffsetBit = new long[superblockCount + 1];

        //First pass: classify every block, pack its class, and total the offset
        //bits so the offset stream can be sized exactly.
        long totalOffsetBits = 0;
        int ones = 0;
        for(int block = 0; block < blockCount; block++)
        {
            if((block % SuperblockBlocks) == 0)
            {
                int superblock = block / SuperblockBlocks;
                superblockOnes[superblock] = ones;
                superblockOffsetBit[superblock] = totalOffsetBits;
            }

            int pattern = ReadBlock(payload, block, bitLength);
            int blockClass = BitOperations.PopCount((uint)pattern);
            WriteField(classWords, (long)block * ClassBits, (uint)blockClass, ClassBits);
            totalOffsetBits += OffsetWidth[blockClass];
            ones += blockClass;
        }

        superblockOnes[superblockCount] = ones;
        superblockOffsetBit[superblockCount] = totalOffsetBits;

        ulong[] offsetWords = new ulong[(totalOffsetBits + 63) >> 6];

        //Second pass: encode each block's pattern to its enumerative offset and
        //pack it where the cumulated width landed it.
        long offsetBit = 0;
        for(int block = 0; block < blockCount; block++)
        {
            int pattern = ReadBlock(payload, block, bitLength);
            int blockClass = BitOperations.PopCount((uint)pattern);
            int width = OffsetWidth[blockClass];
            if(width > 0)
            {
                WriteField(offsetWords, offsetBit, Encode(pattern, blockClass), width);
                offsetBit += width;
            }
        }

        return new BlockCompressedBitVector(classWords, offsetWords, superblockOnes, superblockOffsetBit, bitLength, ones);
    }

    /// <summary>The valid bits of a block read from the payload, low bit first; bits at or beyond <paramref name="bitLength"/> read as zero.</summary>
    /// <param name="payload">The payload words.</param>
    /// <param name="block">The block index.</param>
    /// <param name="bitLength">The valid bit length.</param>
    /// <returns>The block's pattern in its low <see cref="BlockBits"/> bits.</returns>
    private static int ReadBlock(ulong[] payload, int block, int bitLength)
    {
        int start = block * BlockBits;
        int pattern = 0;
        for(int offset = 0; offset < BlockBits; offset++)
        {
            int position = start + offset;
            if(position < bitLength && (payload[position >> 6] & (1UL << (position & 63))) != 0)
            {
                pattern |= 1 << offset;
            }
        }

        return pattern;
    }

    /// <summary>The enumerative offset of a pattern within the lexicographic order of all patterns of its class — a single high-to-low bit walk accumulating binomials.</summary>
    /// <param name="pattern">The block pattern in its low <see cref="BlockBits"/> bits.</param>
    /// <param name="blockClass">The pattern's set-bit count.</param>
    /// <returns>The offset, in <c>[0, C(BlockBits, blockClass))</c>.</returns>
    private static uint Encode(int pattern, int blockClass)
    {
        uint offset = 0;
        int remaining = blockClass;
        for(int position = BlockBits - 1; position >= 0; position--)
        {
            if((pattern & (1 << position)) != 0)
            {
                //Every pattern with the same higher bits but a 0 here sorts
                //first; there are C(position, remaining) of them.
                offset += Binomial[(position * BinomialStride) + remaining];
                remaining--;
            }
        }

        return offset;
    }

    /// <summary>The pattern an enumerative offset names within the patterns of a class — the inverse high-to-low walk subtracting binomials.</summary>
    /// <param name="offset">The enumerative offset.</param>
    /// <param name="blockClass">The class whose patterns are enumerated.</param>
    /// <returns>The block pattern in its low <see cref="BlockBits"/> bits.</returns>
    private static int Decode(uint offset, int blockClass)
    {
        int pattern = 0;
        int remaining = blockClass;
        for(int position = BlockBits - 1; position >= 0; position--)
        {
            uint skip = Binomial[(position * BinomialStride) + remaining];
            if(offset >= skip)
            {
                offset -= skip;
                pattern |= 1 << position;
                remaining--;
            }
        }

        return pattern;
    }

    /// <summary>The full <see cref="BlockBits"/>-bit pattern of a block — its class read from the class array, then its offset decoded.</summary>
    /// <param name="block">The block index.</param>
    /// <param name="offsetBit">The offset-stream bit position of this block.</param>
    /// <returns>The block's pattern in its low <see cref="BlockBits"/> bits.</returns>
    private int BlockPattern(int block, long offsetBit)
    {
        int blockClass = (int)ReadField(classWords, (long)block * ClassBits, ClassBits);
        int width = OffsetWidth[blockClass];
        uint offset = width > 0 ? ReadField(offsetWords, offsetBit, width) : 0;

        return Decode(offset, blockClass);
    }

    /// <summary>The offset-stream bit position of a block — the superblock boundary plus the widths of the blocks before it within the superblock.</summary>
    /// <param name="block">The block index.</param>
    /// <returns>The offset-stream bit position where the block's offset begins.</returns>
    private long OffsetBitOf(int block)
    {
        int superblock = block / SuperblockBlocks;
        int boundary = superblock * SuperblockBlocks;
        long offsetBit = superblockOffsetBit[superblock];
        for(int walk = boundary; walk < block; walk++)
        {
            int walkClass = (int)ReadField(classWords, (long)walk * ClassBits, ClassBits);
            offsetBit += OffsetWidth[walkClass];
        }

        return offsetBit;
    }

    /// <summary>Whether the bit at a position is set — one block decode.</summary>
    /// <param name="position">The bit position, in <c>[0, Length)</c>.</param>
    /// <returns><see langword="true"/> when set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public bool Access(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Length);

        int block = position / BlockBits;
        int pattern = BlockPattern(block, OffsetBitOf(block));

        return (pattern & (1 << (position - (block * BlockBits)))) != 0;
    }

    /// <summary>The number of set bits strictly before a position — a superblock seek plus a walk of at most <see cref="SuperblockBlocks"/> blocks, the last partly counted.</summary>
    /// <param name="position">The exclusive end position, in <c>[0, Length]</c>.</param>
    /// <returns>The set-bit count in <c>[0, position)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public int Rank1(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);

        int block = position / BlockBits;
        int superblock = block / SuperblockBlocks;
        int boundary = superblock * SuperblockBlocks;
        int ones = superblockOnes[superblock];
        long offsetBit = superblockOffsetBit[superblock];

        //Whole blocks below the target add their class outright; the target
        //block adds only its set bits below the position.
        for(int walk = boundary; walk < block; walk++)
        {
            int walkClass = (int)ReadField(classWords, (long)walk * ClassBits, ClassBits);
            ones += walkClass;
            offsetBit += OffsetWidth[walkClass];
        }

        int bitInBlock = position - (block * BlockBits);
        if(bitInBlock > 0 && block < (Length + BlockBits - 1) / BlockBits)
        {
            int pattern = BlockPattern(block, offsetBit);
            ones += BitOperations.PopCount((uint)(pattern & ((1 << bitInBlock) - 1)));
        }

        return ones;
    }

    /// <summary>The number of unset bits strictly before a position.</summary>
    /// <param name="position">The exclusive end position, in <c>[0, Length]</c>.</param>
    /// <returns>The unset-bit count in <c>[0, position)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public int Rank0(int position)
    {
        return position - Rank1(position);
    }

    /// <summary>The position of the <paramref name="rank"/>-th set bit (0-based) — a binary search over the superblock cumulative counts, then a block walk, then a single in-block decode.</summary>
    /// <param name="rank">The zero-based rank, in <c>[0, OneCount)</c>.</param>
    /// <returns>The bit position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is out of range.</exception>
    public int Select1(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rank, OneCount);

        int blockCount = (Length + BlockBits - 1) / BlockBits;
        int superblock = SuperblockOf(rank, ones: true);
        int boundary = superblock * SuperblockBlocks;
        int ceiling = Math.Min(boundary + SuperblockBlocks, blockCount);
        int ones = superblockOnes[superblock];
        long offsetBit = superblockOffsetBit[superblock];

        for(int block = boundary; block < ceiling; block++)
        {
            int blockClass = (int)ReadField(classWords, (long)block * ClassBits, ClassBits);
            if(ones + blockClass > rank)
            {
                int pattern = BlockPattern(block, offsetBit);

                return (block * BlockBits) + SelectInPattern(pattern, rank - ones);
            }

            ones += blockClass;
            offsetBit += OffsetWidth[blockClass];
        }

        return Length;
    }

    /// <summary>The position of the <paramref name="rank"/>-th unset bit (0-based) — the zero-side mirror of <see cref="Select1"/> over the complemented block patterns.</summary>
    /// <param name="rank">The zero-based rank, in <c>[0, ZeroCount)</c>.</param>
    /// <returns>The bit position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is out of range.</exception>
    public int Select0(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rank, ZeroCount);

        int blockCount = (Length + BlockBits - 1) / BlockBits;
        int superblock = SuperblockOf(rank, ones: false);
        int boundary = superblock * SuperblockBlocks;
        int ceiling = Math.Min(boundary + SuperblockBlocks, blockCount);
        int zeros = (boundary * BlockBits) - superblockOnes[superblock];
        long offsetBit = superblockOffsetBit[superblock];

        for(int block = boundary; block < ceiling; block++)
        {
            int blockClass = (int)ReadField(classWords, (long)block * ClassBits, ClassBits);

            //The block's valid width is short only for the final partial block;
            //its trailing positions are unset and do count as zeros.
            int validBits = Math.Min(BlockBits, Length - (block * BlockBits));
            int blockZeros = validBits - blockClass;
            if(zeros + blockZeros > rank)
            {
                int pattern = BlockPattern(block, offsetBit);
                int complement = (~pattern) & ((1 << validBits) - 1);

                return (block * BlockBits) + SelectInPattern(complement, rank - zeros);
            }

            zeros += blockZeros;
            offsetBit += OffsetWidth[blockClass];
        }

        return Length;
    }

    /// <summary>The superblock whose cumulative set-bit (or unset-bit) count is the last not exceeding <paramref name="rank"/> — a binary search over the directory.</summary>
    /// <param name="rank">The zero-based rank sought.</param>
    /// <param name="ones"><see langword="true"/> to search set-bit cumulatives, <see langword="false"/> for unset.</param>
    /// <returns>The superblock index whose boundary count is at or below <paramref name="rank"/>.</returns>
    private int SuperblockOf(int rank, bool ones)
    {
        int low = 0;
        int high = superblockOnes.Length - 1;
        while(low < high)
        {
            int mid = (low + high + 1) >> 1;
            int cumulative = ones
                ? superblockOnes[mid]
                : (mid * SuperblockBlocks * BlockBits) - superblockOnes[mid];
            if(cumulative <= rank)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    /// <summary>The bit position within a block pattern of its <paramref name="rank"/>-th set bit (0-based), low bit first.</summary>
    /// <param name="pattern">The block pattern.</param>
    /// <param name="rank">The zero-based in-block rank.</param>
    /// <returns>The in-block bit position.</returns>
    private static int SelectInPattern(int pattern, int rank)
    {
        uint bits = (uint)pattern;
        for(int cleared = 0; cleared < rank; cleared++)
        {
            bits &= bits - 1;
        }

        return BitOperations.TrailingZeroCount(bits);
    }

    /// <summary>Reads a <paramref name="width"/>-bit field at a bit offset of a packed word array, little-endian within each word.</summary>
    /// <param name="words">The packed words.</param>
    /// <param name="bitOffset">The field's bit offset.</param>
    /// <param name="width">The field width, in <c>[0, 32]</c>.</param>
    /// <returns>The field value.</returns>
    private static uint ReadField(ulong[] words, long bitOffset, int width)
    {
        if(width == 0)
        {
            return 0;
        }

        int word = (int)(bitOffset >> 6);
        int shift = (int)(bitOffset & 63);
        ulong mask = (width == 64) ? ulong.MaxValue : ((1UL << width) - 1);

        ulong bits = words[word] >> shift;
        if(shift + width > 64)
        {
            bits |= words[word + 1] << (64 - shift);
        }

        return (uint)(bits & mask);
    }

    /// <summary>Writes a <paramref name="width"/>-bit field at a bit offset of a packed word array, little-endian within each word; the target field is assumed zero.</summary>
    /// <param name="words">The packed words.</param>
    /// <param name="bitOffset">The field's bit offset.</param>
    /// <param name="value">The field value.</param>
    /// <param name="width">The field width, in <c>[0, 32]</c>.</param>
    private static void WriteField(ulong[] words, long bitOffset, uint value, int width)
    {
        if(width == 0)
        {
            return;
        }

        int word = (int)(bitOffset >> 6);
        int shift = (int)(bitOffset & 63);

        words[word] |= (ulong)value << shift;
        if(shift + width > 64)
        {
            words[word + 1] |= (ulong)value >> (64 - shift);
        }
    }

    /// <summary>Builds the binomial table C(n, k) for n, k in <c>[0, BlockBits]</c>, flattened row-major, by Pascal's recurrence.</summary>
    /// <returns>The table, with out-of-triangle entries zero.</returns>
    private static uint[] BuildBinomial()
    {
        uint[] table = new uint[BinomialStride * BinomialStride];
        for(int n = 0; n <= BlockBits; n++)
        {
            table[n * BinomialStride] = 1;
            for(int k = 1; k <= n; k++)
            {
                table[(n * BinomialStride) + k] = table[((n - 1) * BinomialStride) + (k - 1)] + table[((n - 1) * BinomialStride) + k];
            }
        }

        return table;
    }

    /// <summary>Builds the per-class offset widths — <c>ceil(log2(C(BlockBits, class)))</c>, zero where the class holds a single pattern.</summary>
    /// <returns>The widths indexed by class in <c>[0, BlockBits]</c>.</returns>
    private static int[] BuildOffsetWidth()
    {
        int[] widths = new int[BlockBits + 1];
        for(int blockClass = 0; blockClass <= BlockBits; blockClass++)
        {
            uint patterns = Binomial[(BlockBits * BinomialStride) + blockClass];
            widths[blockClass] = (patterns <= 1) ? 0 : (32 - BitOperations.LeadingZeroCount(patterns - 1));
        }

        return widths;
    }
}
