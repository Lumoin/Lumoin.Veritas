using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// A contiguous run of system-of-record items a feed walk excluded because the block carrying them failed
/// its checksum — the item-granular face of <c>DetectionPrecedesXor</c>: a corrupt block's items are named
/// here rather than folded into a sketch or any other derived structure, so a later repair can act on the
/// exact range that was dropped.
/// </summary>
/// <param name="BlockIndex">The excluded block's index in the segment.</param>
/// <param name="StartItem">The first excluded item's index.</param>
/// <param name="ItemCount">The number of items the excluded block covered.</param>
public readonly record struct SkippedItemRange(int BlockIndex, int StartItem, int ItemCount);

/// <summary>
/// The result of a checksum-gated walk over a system-of-record item segment
/// (<see cref="ItemSegment.ReadVerifiedItems"/>): the triples whose blocks passed their checksum, packed
/// contiguously, alongside the item ranges that were excluded because their block failed. The verified
/// triples are backed by a pooled buffer this owns; they stay valid until <see cref="Dispose"/> returns
/// the buffer to its pool.
/// </summary>
public sealed class ItemSegmentFeed : IDisposable
{
    /// <summary>The pooled buffer backing the verified triples.</summary>
    private readonly IMemoryOwner<EncodedTriple> owner;

    /// <summary>The number of verified triples packed at the front of the buffer.</summary>
    private readonly int verifiedCount;

    /// <summary>The item ranges excluded because their block failed its checksum.</summary>
    private readonly IReadOnlyList<SkippedItemRange> skippedRanges;

    /// <summary>Whether the segment carried per-block checksums, so the walk could actually verify each block.</summary>
    private readonly bool wasChecksumGated;

    /// <summary>One once the pooled buffer has been returned; guards a second return.</summary>
    private int disposed;

    /// <summary>Creates a feed result over a pooled triple buffer and the excluded ranges.</summary>
    /// <param name="owner">The pooled buffer backing the verified triples; this takes ownership and returns it on <see cref="Dispose"/>.</param>
    /// <param name="verifiedCount">The number of verified triples packed at the front of <paramref name="owner"/>.</param>
    /// <param name="skippedRanges">The item ranges excluded because their block failed its checksum.</param>
    /// <param name="wasChecksumGated">Whether the segment carried per-block checksums, so each block was actually verified rather than admitted unverified.</param>
    internal ItemSegmentFeed(IMemoryOwner<EncodedTriple> owner, int verifiedCount, IReadOnlyList<SkippedItemRange> skippedRanges, bool wasChecksumGated)
    {
        this.owner = owner;
        this.verifiedCount = verifiedCount;
        this.skippedRanges = skippedRanges;
        this.wasChecksumGated = wasChecksumGated;
    }

    /// <summary>The triples whose blocks passed their checksum, in stored order, packed contiguously; valid until <see cref="Dispose"/>.</summary>
    /// <exception cref="ObjectDisposedException">The feed has been disposed.</exception>
    public ReadOnlyMemory<EncodedTriple> VerifiedItems
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            return owner.Memory[..verifiedCount];
        }
    }

    /// <summary>The number of verified triples.</summary>
    public int VerifiedCount => verifiedCount;

    /// <summary>The item ranges excluded because their block failed its checksum; empty when every block passed.</summary>
    public IReadOnlyList<SkippedItemRange> SkippedRanges => skippedRanges;

    /// <summary>Whether the segment carried per-block checksums, so the walk verified each block; when <see langword="false"/> no block could be gated and a clean <see cref="IsClean"/> means only that nothing was excluded, not that anything was verified.</summary>
    public bool WasChecksumGated => wasChecksumGated;

    /// <summary>Whether no items were excluded. Read together with <see cref="WasChecksumGated"/>: clean and gated means every block was verified, clean and ungated means there were no digests to verify against.</summary>
    public bool IsClean => skippedRanges.Count == 0;

    /// <summary>Returns the pooled triple buffer; idempotent.</summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) == 0)
        {
            owner.Dispose();
        }
    }
}
