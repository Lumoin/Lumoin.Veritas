using System;
using System.Buffers;
using System.IO;
using System.Threading;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The first-touch verify gate that upholds <see cref="PersistenceInvariant.DetectionPrecedesUse"/>
/// under lazy block sourcing: a flat bit per addressable block records whether that block has been
/// verified, and <see cref="EnsureVerified"/> runs the per-block detection routine the first time a
/// block is touched and refuses its bytes if they are corrupt — so no decode kernel ever reads a
/// block whose checksum has not been confirmed.
/// </summary>
/// <remarks>
/// <para>
/// The map is the lazy-mode counterpart to eager decode-into-backing: when a whole image is verified
/// once at load (the default path), its blocks are already confirmed and need no per-touch gate; when
/// a block's bytes are only paged in on demand (the zero-copy aliasing source), the gate verifies each
/// block exactly once on the first touch and remembers it here.
/// </para>
/// <para>
/// The bit array is a flat <c>ulong[]</c> rented from an injected <see cref="MemoryPool{T}"/> rather
/// than a compressed bitset, because the bits are <em>set</em> from concurrent query threads and a
/// compressed set is not safe under concurrent mutation. A set is one lock-free
/// <see cref="Interlocked.Or(ref ulong, ulong)"/>; a double-touch racing two threads onto the same
/// clear bit is benign — each recomputes the same checksum and sets the same bit — so the gate needs
/// no lock. The map is runtime-only state (never serialized) and is disposed when its owning index
/// is torn down, at which point no reader remains.
/// </para>
/// </remarks>
public sealed class FirstTouchVerificationMap : IDisposable
{
    /// <summary>The rented backing for the bit array; one bit per block, returned to the pool on dispose.</summary>
    private readonly IMemoryOwner<ulong> owner;

    /// <summary>The per-block detection routine run on a block's first touch.</summary>
    private readonly VerifyBlockDelegate verify;

    /// <summary>Non-zero once <see cref="Dispose"/> has returned the rented backing; written and read with memory ordering so the disposal is visible to concurrent readers, matching the every-shared-field-is-synchronized invariant the bit array holds to.</summary>
    private int disposed;

    /// <summary>Creates a map over <paramref name="blockCount"/> blocks, all initially unverified.</summary>
    /// <param name="blockCount">The number of addressable blocks the gate covers.</param>
    /// <param name="verify">The per-block detection routine run on a block's first touch.</param>
    /// <param name="pool">The pool the flat bit array is rented from.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockCount"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="verify"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public FirstTouchVerificationMap(int blockCount, VerifyBlockDelegate verify, MemoryPool<ulong> pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockCount);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(pool);

        BlockCount = blockCount;
        this.verify = verify;

        //One bit per block; rent at least one word so an empty map constructs against any injected pool
        //(some pools reject a zero-length rent). Pooled memory is not zeroed on rent, so clear the
        //addressable words: every block must start unverified.
        int wordCount = (blockCount + 63) >> 6;
        owner = pool.Rent(Math.Max(1, wordCount));
        owner.Memory.Span[..wordCount].Clear();
    }

    /// <summary>The number of addressable blocks the gate covers.</summary>
    public int BlockCount { get; }

    /// <summary>Reports whether <paramref name="blockIndex"/> has already been verified.</summary>
    /// <param name="blockIndex">The zero-based block index.</param>
    /// <returns><see langword="true"/> when the block's bit is set; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockIndex"/> is outside <c>[0, BlockCount)</c>.</exception>
    /// <exception cref="ObjectDisposedException">The map has been disposed.</exception>
    public bool IsVerified(int blockIndex)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(blockIndex, BlockCount);

        Span<ulong> words = owner.Memory.Span;

        return (Volatile.Read(ref words[blockIndex >> 6]) & (1UL << (blockIndex & 63))) != 0;
    }

    /// <summary>Marks <paramref name="blockIndex"/> verified without running the detection routine — the entry point the eager whole-image path uses to record blocks it has already confirmed at load.</summary>
    /// <param name="blockIndex">The zero-based block index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockIndex"/> is outside <c>[0, BlockCount)</c>.</exception>
    /// <exception cref="ObjectDisposedException">The map has been disposed.</exception>
    public void MarkVerified(int blockIndex)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(blockIndex, BlockCount);

        Span<ulong> words = owner.Memory.Span;
        Interlocked.Or(ref words[blockIndex >> 6], 1UL << (blockIndex & 63));
    }

    /// <summary>Ensures <paramref name="blockIndex"/> is verified before its bytes are used: returns immediately if the block is already verified, otherwise runs the detection routine and either records the block verified or refuses its corrupt bytes.</summary>
    /// <param name="blockIndex">The zero-based block index to gate.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockIndex"/> is outside <c>[0, BlockCount)</c>.</exception>
    /// <exception cref="ObjectDisposedException">The map has been disposed.</exception>
    /// <exception cref="InvalidDataException">The detection routine reports the block corrupt; its bit stays clear so a later touch re-detects.</exception>
    public void EnsureVerified(int blockIndex)
    {
        if(IsVerified(blockIndex))
        {
            return;
        }

        if(!verify(blockIndex))
        {
            throw new InvalidDataException($"Block {blockIndex} failed verification on first touch (at-rest corruption); its bytes will not be decoded.");
        }

        MarkVerified(blockIndex);
    }

    /// <summary>Returns the flat bit array to the pool.</summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        owner.Dispose();
    }
}
