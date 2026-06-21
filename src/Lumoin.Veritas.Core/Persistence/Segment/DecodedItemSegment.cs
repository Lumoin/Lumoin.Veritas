using System;
using System.Buffers;
using System.Threading;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The triples decoded from a system-of-record item segment by the all-or-nothing read
/// (<see cref="ItemSegment.ReadFrom"/>), held in a buffer rented from a caller-threaded
/// <see cref="MemoryPool{T}"/> rather than a loose array — a semantic value with pooled, owned storage. The
/// triples stay valid until <see cref="Dispose"/> returns the buffer to that pool. The skip-and-continue
/// counterpart, which excludes a corrupt block rather than refusing the whole image, is
/// <see cref="ItemSegmentFeed"/>.
/// </summary>
public sealed class DecodedItemSegment: IDisposable
{
    /// <summary>The rented buffer backing the decoded triples; returned to the pool on dispose.</summary>
    private readonly IMemoryOwner<EncodedTriple> owner;

    /// <summary>The number of decoded triples packed at the front of the buffer.</summary>
    private readonly int count;

    /// <summary>One once the buffer has been returned; guards a second return.</summary>
    private int disposed;

    /// <summary>Wraps a rented buffer whose first <paramref name="count"/> triples are the decoded segment.</summary>
    /// <param name="owner">The rented buffer; this takes ownership and returns it on <see cref="Dispose"/>.</param>
    /// <param name="count">The number of decoded triples packed at the front of <paramref name="owner"/>.</param>
    internal DecodedItemSegment(IMemoryOwner<EncodedTriple> owner, int count)
    {
        this.owner = owner;
        this.count = count;
    }

    /// <summary>The decoded triples, in stored order; valid until <see cref="Dispose"/>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public ReadOnlyMemory<EncodedTriple> Items
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            return owner.Memory[..count];
        }
    }

    /// <summary>The number of decoded triples.</summary>
    public int Count => count;

    /// <summary>Returns the rented buffer to the pool; idempotent.</summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) == 0)
        {
            owner.Dispose();
        }
    }
}
