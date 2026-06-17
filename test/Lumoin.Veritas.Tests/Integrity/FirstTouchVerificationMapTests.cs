using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The first-touch verify gate (<see cref="PersistenceInvariant.DetectionPrecedesUse"/> under lazy
/// sourcing): a block is verified exactly once on its first touch and skipped thereafter; a corrupt
/// block is refused and its bit stays clear so a later touch re-detects; the eager path can pre-mark a
/// block to make the gate a no-op; the bits are independent across the 64-bit word boundary; and
/// concurrent first-touches of every block verify each block at least once, set every bit, and never
/// tear — the lock-free, benign-double-verify contract.
/// </summary>
[TestClass]
internal sealed class FirstTouchVerificationMapTests
{
    /// <summary>A per-block detection routine with a settable corrupt set and an atomic per-block call counter — supplied to the gate as a bound instance method, never a captured local.</summary>
    private sealed class CountingBlockVerifier
    {
        /// <summary>The atomic call count per block.</summary>
        private readonly int[] callCounts;

        /// <summary>Whether each block is currently corrupt (reported unclean by <see cref="Verify"/>).</summary>
        private readonly bool[] corrupt;

        /// <summary>Creates a verifier over <paramref name="blockCount"/> clean blocks.</summary>
        /// <param name="blockCount">The number of blocks.</param>
        public CountingBlockVerifier(int blockCount)
        {
            callCounts = new int[blockCount];
            corrupt = new bool[blockCount];
        }

        /// <summary>Marks a block corrupt, so the next verification of it reports unclean.</summary>
        /// <param name="blockIndex">The block index.</param>
        public void MarkCorrupt(int blockIndex)
        {
            corrupt[blockIndex] = true;
        }

        /// <summary>Repairs a block, so subsequent verification of it reports clean.</summary>
        /// <param name="blockIndex">The block index.</param>
        public void Repair(int blockIndex)
        {
            corrupt[blockIndex] = false;
        }

        /// <summary>The number of times <paramref name="blockIndex"/> has been verified.</summary>
        /// <param name="blockIndex">The block index.</param>
        /// <returns>The call count.</returns>
        public int CallCount(int blockIndex)
        {
            return Volatile.Read(ref callCounts[blockIndex]);
        }

        /// <summary>The total number of verifications across all blocks.</summary>
        /// <returns>The summed call count.</returns>
        public int TotalCalls()
        {
            int total = 0;
            for(int b = 0; b < callCounts.Length; b++)
            {
                total += Volatile.Read(ref callCounts[b]);
            }

            return total;
        }

        /// <summary>The detection routine: counts the touch and reports clean unless the block is marked corrupt.</summary>
        /// <param name="blockIndex">The block index to verify.</param>
        /// <returns><see langword="true"/> when the block is clean.</returns>
        public bool Verify(int blockIndex)
        {
            Interlocked.Increment(ref callCounts[blockIndex]);

            return !corrupt[blockIndex];
        }
    }

    /// <summary>Drives every block through the gate from a worker thread, holding the gate and block count as explicit state so the parallel body is a bound method, not a closure.</summary>
    private sealed class TouchWorker
    {
        /// <summary>The gate under test.</summary>
        private readonly FirstTouchVerificationMap map;

        /// <summary>The number of blocks to touch.</summary>
        private readonly int blockCount;

        /// <summary>Creates a worker over <paramref name="map"/>.</summary>
        /// <param name="map">The gate.</param>
        /// <param name="blockCount">The number of blocks.</param>
        public TouchWorker(FirstTouchVerificationMap map, int blockCount)
        {
            this.map = map;
            this.blockCount = blockCount;
        }

        /// <summary>Touches every block once through the gate; the parallel-loop index is unused.</summary>
        /// <param name="iteration">The parallel-loop index.</param>
        public void TouchAll(int iteration)
        {
            for(int b = 0; b < blockCount; b++)
            {
                map.EnsureVerified(b);
            }
        }
    }

    /// <summary>Each block is verified exactly once across repeated touches, and every block ends verified.</summary>
    [TestMethod]
    public void FirstTouchVerifiesEachBlockExactlyOnce()
    {
        const int BlockCount = 40;
        CountingBlockVerifier verifier = new(BlockCount);
        using FirstTouchVerificationMap map = new(BlockCount, verifier.Verify, MemoryPool<ulong>.Shared);

        for(int touch = 0; touch < 3; touch++)
        {
            for(int b = 0; b < BlockCount; b++)
            {
                map.EnsureVerified(b);
            }
        }

        for(int b = 0; b < BlockCount; b++)
        {
            Assert.AreEqual(1, verifier.CallCount(b), $"Block {b} was verified more than once.");
            Assert.IsTrue(map.IsVerified(b), $"Block {b} is not recorded verified.");
        }
    }

    /// <summary>An already-verified block short-circuits without re-running the detection routine.</summary>
    [TestMethod]
    public void AlreadyVerifiedBlockSkipsTheDetectionRoutine()
    {
        CountingBlockVerifier verifier = new(4);
        using FirstTouchVerificationMap map = new(4, verifier.Verify, MemoryPool<ulong>.Shared);

        map.EnsureVerified(2);
        map.EnsureVerified(2);

        Assert.AreEqual(1, verifier.CallCount(2));
    }

    /// <summary>A corrupt block is refused on first touch, its bit stays clear so a later touch re-detects, and once repaired it verifies and is recorded.</summary>
    [TestMethod]
    public void CorruptBlockIsRefusedAndStaysUnverifiedUntilRepaired()
    {
        CountingBlockVerifier verifier = new(8);
        using FirstTouchVerificationMap map = new(8, verifier.Verify, MemoryPool<ulong>.Shared);
        verifier.MarkCorrupt(5);

        Assert.ThrowsExactly<InvalidDataException>(() => { map.EnsureVerified(5); });
        Assert.IsFalse(map.IsVerified(5), "A corrupt block must not be recorded verified.");

        //The detection routine re-runs on a second touch because the bit was never set.
        Assert.ThrowsExactly<InvalidDataException>(() => { map.EnsureVerified(5); });
        Assert.AreEqual(2, verifier.CallCount(5), "A corrupt block must re-detect on every touch.");

        verifier.Repair(5);
        map.EnsureVerified(5);
        Assert.IsTrue(map.IsVerified(5), "A repaired block must verify and be recorded.");
    }

    /// <summary>The eager path can mark a block verified without running detection, after which the gate is a no-op for that block.</summary>
    [TestMethod]
    public void MarkVerifiedRecordsWithoutRunningDetection()
    {
        CountingBlockVerifier verifier = new(4);
        using FirstTouchVerificationMap map = new(4, verifier.Verify, MemoryPool<ulong>.Shared);

        map.MarkVerified(3);
        Assert.IsTrue(map.IsVerified(3));

        map.EnsureVerified(3);
        Assert.AreEqual(0, verifier.CallCount(3), "A pre-marked block must not run the detection routine.");
    }

    /// <summary>The bits are independent across the 64-bit word boundary: marking blocks straddling words sets exactly those and no neighbours.</summary>
    [TestMethod]
    public void BitsAreIndependentAcrossWordBoundaries()
    {
        const int BlockCount = 130;
        CountingBlockVerifier verifier = new(BlockCount);
        using FirstTouchVerificationMap map = new(BlockCount, verifier.Verify, MemoryPool<ulong>.Shared);

        int[] marked = [0, 63, 64, 65, 127, 128, 129];
        foreach(int b in marked)
        {
            map.MarkVerified(b);
        }

        for(int b = 0; b < BlockCount; b++)
        {
            bool expected = Array.IndexOf(marked, b) >= 0;
            Assert.AreEqual(expected, map.IsVerified(b), $"Block {b} verified-state did not match the marked set (word-boundary arithmetic).");
        }
    }

    /// <summary>A block index outside the range is rejected on every entry point.</summary>
    [TestMethod]
    public void OutOfRangeBlockIndexIsRejected()
    {
        CountingBlockVerifier verifier = new(16);
        using FirstTouchVerificationMap map = new(16, verifier.Verify, MemoryPool<ulong>.Shared);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = map.IsVerified(-1); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = map.IsVerified(16); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { map.MarkVerified(-1); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { map.MarkVerified(16); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { map.EnsureVerified(-1); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { map.EnsureVerified(16); });
    }

    /// <summary>A zero-block map constructs against any injected pool — including one that rejects a zero-length rent — rejects every index, and disposes cleanly: the empty-image degenerate.</summary>
    [TestMethod]
    public void ZeroBlockMapConstructsAgainstAnyPoolAndRejectsEveryIndex()
    {
        CountingBlockVerifier verifier = new(0);
        using VeritasMemoryPool<ulong> pool = new();
        using FirstTouchVerificationMap map = new(0, verifier.Verify, pool);

        int blockCount = map.BlockCount;
        Assert.AreEqual(0, blockCount);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = map.IsVerified(0); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { map.EnsureVerified(0); });
    }

    /// <summary>Concurrent first-touches of every block verify each block at least once, set every bit, and stay within the benign double-verify bound; a second concurrent pass over the now-verified blocks adds zero verifications — the deterministic proof the gate short-circuits under contention rather than re-verifying.</summary>
    [TestMethod]
    public void ConcurrentFirstTouchVerifiesAndMarksSafely()
    {
        const int BlockCount = 256;
        const int Workers = 16;
        CountingBlockVerifier verifier = new(BlockCount);
        using FirstTouchVerificationMap map = new(BlockCount, verifier.Verify, MemoryPool<ulong>.Shared);
        TouchWorker worker = new(map, BlockCount);

        Parallel.For(0, Workers, worker.TouchAll);

        for(int b = 0; b < BlockCount; b++)
        {
            Assert.IsTrue(map.IsVerified(b), $"Block {b} was not verified under concurrency.");
            Assert.IsGreaterThanOrEqualTo(1, verifier.CallCount(b), $"Block {b} was never verified (first-touch-precedes-use broke under concurrency).");
        }

        int afterFirstPass = verifier.TotalCalls();
        Assert.IsGreaterThanOrEqualTo(BlockCount, afterFirstPass, $"Fewer verifications ({afterFirstPass}) than blocks ({BlockCount}).");
        Assert.IsLessThanOrEqualTo(BlockCount * Workers, afterFirstPass, $"More verifications ({afterFirstPass}) than the benign double-verify bound ({BlockCount * Workers}).");

        //Every block is verified now, so a second concurrent pass must short-circuit every touch and add
        //ZERO verifications — the deterministic distinguisher between a real gate and one that never skips
        //(a non-short-circuiting gate would re-verify all BlockCount*Workers here).
        Parallel.For(0, Workers, worker.TouchAll);
        Assert.AreEqual(afterFirstPass, verifier.TotalCalls(), "A second concurrent pass re-verified already-verified blocks; the gate does not short-circuit under concurrency.");
    }

    /// <summary>Dispose returns the backing and blocks further use; a second dispose is a no-op.</summary>
    [TestMethod]
    public void DisposeReturnsTheBackingAndBlocksFurtherUse()
    {
        CountingBlockVerifier verifier = new(4);
        FirstTouchVerificationMap map = new(4, verifier.Verify, MemoryPool<ulong>.Shared);

        map.Dispose();
        map.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = map.IsVerified(0); });
    }
}
