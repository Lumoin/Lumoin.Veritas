using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The capacity-1 parity code: the portable word-parallel path and the Vector128, Vector256, and Vector512 paths
/// agree with a naive byte-wise reference on every shape (equal-length blocks and shorter zero-extended blocks
/// across the word and vector boundaries), encoding then restoring recovers each lost block (a shorter block's
/// padding comes back as zero), and the fold obeys its XOR algebra. Shapes and patterns are fixed (no entropy
/// source) so the agreement is deterministic, and every block is a <see cref="ParityBlock"/> rented from one pool
/// per test.
/// </summary>
[TestClass]
internal sealed class ParityCodecTests
{
    /// <summary>A parity fold path under differential test: it XORs a block payload into an accumulator in place.</summary>
    /// <param name="accumulator">The accumulator to XOR into.</param>
    /// <param name="blockPayload">The block payload to fold in.</param>
    private delegate void ParityFoldPath(Span<byte> accumulator, ReadOnlySpan<byte> blockPayload);

    /// <summary>The largest accumulator any shape uses, so one rented block serves every shape.</summary>
    private const int MaximumShapeLength = 200;

    /// <summary>A spread of (accumulator length, block length) shapes: equal lengths straddling the word and 128/256/512-bit vector boundaries, and shorter blocks that exercise the implicit zero extension.</summary>
    /// <returns>The shapes to fold across.</returns>
    private static IEnumerable<(int AccumulatorLength, int BlockLength)> Shapes()
    {
        //Equal-length blocks straddling the 64-bit word and the 16/32/64-byte vector boundaries.
        int[] equalLengths = [0, 1, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, MaximumShapeLength];
        foreach(int length in equalLengths)
        {
            yield return (length, length);
        }

        //Shorter blocks, where the accumulator's trailing bytes are the block's implicit zero padding.
        yield return (16, 5);
        yield return (32, 17);
        yield return (64, 33);
        yield return (100, 64);
        yield return (128, 65);
        yield return (MaximumShapeLength, 120);
    }

    [TestMethod]
    public void TheVectorAndPortablePathsAgreeOnEveryShape()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ParityBlock originalBlock = ParityBlock.Rent(pool, MaximumShapeLength);
        using ParityBlock blockBuffer = ParityBlock.Rent(pool, MaximumShapeLength);
        using ParityBlock expectedBlock = ParityBlock.Rent(pool, MaximumShapeLength);
        using ParityBlock workBlock = ParityBlock.Rent(pool, MaximumShapeLength);

        foreach((int accumulatorLength, int blockLength) in Shapes())
        {
            Span<byte> original = originalBlock.WritableSpan[..accumulatorLength];
            Span<byte> block = blockBuffer.WritableSpan[..blockLength];
            FillPattern(original, accumulatorLength + 1);
            FillPattern(block, blockLength + 7);

            //The naive byte-wise XOR is the reference every production path must reproduce.
            Span<byte> expected = expectedBlock.WritableSpan[..accumulatorLength];
            original.CopyTo(expected);
            NaiveAccumulate(expected, block);

            AssertFoldMatches("portable", ParityCodec.AccumulateXorPortable, original, block, expected, workBlock);
            AssertFoldMatches("vector128", ParityCodec.AccumulateXorVector128, original, block, expected, workBlock);
            AssertFoldMatches("vector256", ParityCodec.AccumulateXorVector256, original, block, expected, workBlock);
            AssertFoldMatches("vector512", ParityCodec.AccumulateXorVector512, original, block, expected, workBlock);
            AssertFoldMatches("dispatch", ParityCodec.AccumulateXor, original, block, expected, workBlock);
        }
    }

    [TestMethod]
    public void EncodingThenRestoringRecoversEachLostBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int stride = 100;
        const int fullBlockCount = 4;
        const int lastBlockLength = 37;
        int blockCount = fullBlockCount + 1;

        ParityBlock[] blockBuffers = new ParityBlock[blockCount];
        ReadOnlyMemory<byte>[] blocks = new ReadOnlyMemory<byte>[blockCount];
        try
        {
            for(int block = 0; block < blockCount; block++)
            {
                int length = block < fullBlockCount ? stride : lastBlockLength;
                ParityBlock buffer = ParityBlock.Rent(pool, length);
                FillPattern(buffer.WritableSpan, (block * 13) + 1);
                blockBuffers[block] = buffer;
                blocks[block] = buffer.Memory;
            }

            using ParityBlock parityBlock = ParityBlock.Rent(pool, stride);
            ParityCodec.Encode(blocks, parityBlock.WritableSpan);

            using ParityBlock restoredBlock = ParityBlock.Rent(pool, stride);
            for(int lost = 0; lost < blockCount; lost++)
            {
                ReadOnlyMemory<byte>[] survivors = new ReadOnlyMemory<byte>[blockCount - 1];
                int s = 0;
                for(int block = 0; block < blockCount; block++)
                {
                    if(block != lost)
                    {
                        survivors[s++] = blocks[block];
                    }
                }

                Span<byte> restored = restoredBlock.WritableSpan;
                ParityCodec.Restore(parityBlock.Span, survivors, restored);

                ReadOnlySpan<byte> lostPayload = blockBuffers[lost].Span;
                Assert.IsTrue(restored[..lostPayload.Length].SequenceEqual(lostPayload), $"lost block {lost} payload not recovered.");

                //A shorter block's recovered bytes beyond its true length are the cancelled zero padding.
                Assert.AreEqual(-1, restored[lostPayload.Length..].IndexOfAnyExcept((byte)0), $"lost block {lost} padding not zero.");
            }
        }
        finally
        {
            foreach(ParityBlock buffer in blockBuffers)
            {
                buffer?.Dispose();
            }
        }
    }

    [TestMethod]
    public void ASingleBlockIsRecoveredFromParityAlone()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int stride = 48;
        using ParityBlock blockBuffer = ParityBlock.Rent(pool, stride);
        FillPattern(blockBuffer.WritableSpan, 5);
        ReadOnlyMemory<byte>[] blocks = [blockBuffer.Memory];

        using ParityBlock parityBlock = ParityBlock.Rent(pool, stride);
        ParityCodec.Encode(blocks, parityBlock.WritableSpan);

        //One block's parity is that block, so it round-trips through the parity alone (no survivors).
        Assert.IsTrue(parityBlock.Span.SequenceEqual(blockBuffer.Span));

        using ParityBlock restoredBlock = ParityBlock.Rent(pool, stride);
        ParityCodec.Restore(parityBlock.Span, default, restoredBlock.WritableSpan);
        Assert.IsTrue(restoredBlock.Span.SequenceEqual(blockBuffer.Span));
    }

    [TestMethod]
    public void ParityOfNoBlocksIsAllZero()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int stride = 64;
        using ParityBlock parityBlock = ParityBlock.Rent(pool, stride);
        parityBlock.WritableSpan.Fill(0xAB);

        ParityCodec.Encode(default, parityBlock.WritableSpan);
        Assert.AreEqual(-1, parityBlock.Span.IndexOfAnyExcept((byte)0));
    }

    [TestMethod]
    public void EncodeIsDeterministic()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int stride = 80;
        const int blockCount = 3;

        ParityBlock[] blockBuffers = new ParityBlock[blockCount];
        ReadOnlyMemory<byte>[] blocks = new ReadOnlyMemory<byte>[blockCount];
        try
        {
            for(int block = 0; block < blockCount; block++)
            {
                ParityBlock buffer = ParityBlock.Rent(pool, stride);
                FillPattern(buffer.WritableSpan, (block * 29) + 4);
                blockBuffers[block] = buffer;
                blocks[block] = buffer.Memory;
            }

            using ParityBlock firstBlock = ParityBlock.Rent(pool, stride);
            using ParityBlock secondBlock = ParityBlock.Rent(pool, stride);
            ParityCodec.Encode(blocks, firstBlock.WritableSpan);
            ParityCodec.Encode(blocks, secondBlock.WritableSpan);

            Assert.IsTrue(firstBlock.Span.SequenceEqual(secondBlock.Span));
        }
        finally
        {
            foreach(ParityBlock buffer in blockBuffers)
            {
                buffer?.Dispose();
            }
        }
    }

    [TestMethod]
    public void FoldingIsSelfInverseAndAnAllZeroBlockIsIdentity()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int stride = 64;
        using ParityBlock accumulatorBlock = ParityBlock.Rent(pool, stride);
        using ParityBlock blockBuffer = ParityBlock.Rent(pool, stride);
        using ParityBlock snapshotBlock = ParityBlock.Rent(pool, stride);
        Span<byte> accumulator = accumulatorBlock.WritableSpan;
        Span<byte> block = blockBuffer.WritableSpan;
        FillPattern(accumulator, 3);
        FillPattern(block, 9);
        accumulator.CopyTo(snapshotBlock.WritableSpan);
        ReadOnlySpan<byte> snapshot = snapshotBlock.Span;

        //Self-inverse: folding the same block twice cancels back to the original accumulator.
        ParityCodec.AccumulateXor(accumulator, block);
        ParityCodec.AccumulateXor(accumulator, block);
        Assert.IsTrue(accumulator.SequenceEqual(snapshot));

        //Identity: folding an all-zero block is a no-op.
        block.Clear();
        ParityCodec.AccumulateXor(accumulator, block);
        Assert.IsTrue(accumulator.SequenceEqual(snapshot));
    }

    [TestMethod]
    public void AShorterBlockTouchesOnlyTheLeadingAccumulatorBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int stride = 16;
        const int blockLength = 5;
        using ParityBlock accumulatorBlock = ParityBlock.Rent(pool, stride);
        using ParityBlock blockBuffer = ParityBlock.Rent(pool, blockLength);
        using ParityBlock tailBlock = ParityBlock.Rent(pool, stride - blockLength);
        Span<byte> accumulator = accumulatorBlock.WritableSpan;
        Span<byte> block = blockBuffer.WritableSpan;
        FillPattern(accumulator, 2);
        FillPattern(block, 6);

        //The accumulator's bytes beyond the block are the block's implicit zero padding and stay untouched.
        accumulator[blockLength..].CopyTo(tailBlock.WritableSpan);
        ReadOnlySpan<byte> tail = tailBlock.Span;

        ParityCodec.AccumulateXor(accumulator, block);
        Assert.IsTrue(accumulator[blockLength..].SequenceEqual(tail));
    }

    [TestMethod]
    public void AccumulateXorRejectsABlockLongerThanTheAccumulator()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => ParityCodec.AccumulateXor(new byte[4], new byte[8]));
    }

    [TestMethod]
    public void RestoreRejectsARestoredLengthThatIsNotTheParityStride()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => ParityCodec.Restore(new byte[8], default, new byte[16]));
    }

    [TestMethod]
    public void EncodeRejectsABlockLongerThanTheParity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => ParityCodec.Encode(new ReadOnlyMemory<byte>[] { new byte[16] }, new byte[8]));
    }

    [TestMethod]
    public void RentRejectsANonPositiveLength()
    {
        using VeritasMemoryPool<byte> pool = new();

        //A zero-width block has no parity-code meaning, so it is refused at the block boundary, not by the pool.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ParityBlock.Rent(pool, 0).Dispose());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ParityBlock.Rent(pool, -1).Dispose());
    }

    /// <summary>Runs one fold path on a fresh copy of the accumulator and asserts it reproduces the reference.</summary>
    /// <param name="pathName">The path's name, for the failure message.</param>
    /// <param name="path">The fold path under test.</param>
    /// <param name="original">The accumulator's starting bytes.</param>
    /// <param name="block">The block payload to fold in.</param>
    /// <param name="expected">The reference result the path must reproduce.</param>
    /// <param name="workBlock">The rented block the path folds into (sliced to <paramref name="original"/>'s length).</param>
    private static void AssertFoldMatches(string pathName, ParityFoldPath path, ReadOnlySpan<byte> original, ReadOnlySpan<byte> block, ReadOnlySpan<byte> expected, ParityBlock workBlock)
    {
        Span<byte> work = workBlock.WritableSpan[..original.Length];
        original.CopyTo(work);
        path(work, block);
        Assert.IsTrue(work.SequenceEqual(expected), $"the {pathName} path disagreed at length {original.Length}/{block.Length}.");
    }

    /// <summary>Writes a deterministic, length-varied byte pattern into <paramref name="buffer"/> (no entropy source).</summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="seed">A per-buffer seed that varies the pattern.</param>
    private static void FillPattern(Span<byte> buffer, int seed)
    {
        for(int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = unchecked((byte)((seed * 31) + (i * 131) + 17));
        }
    }

    /// <summary>The naive byte-wise XOR reference: folds <paramref name="block"/> into <paramref name="accumulator"/>'s leading bytes one byte at a time.</summary>
    /// <param name="accumulator">The accumulator to XOR into.</param>
    /// <param name="block">The block payload to fold in.</param>
    private static void NaiveAccumulate(Span<byte> accumulator, ReadOnlySpan<byte> block)
    {
        for(int i = 0; i < block.Length; i++)
        {
            accumulator[i] ^= block[i];
        }
    }
}
