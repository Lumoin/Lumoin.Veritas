using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The triples decoded from a system-of-record item segment by the all-or-nothing read
/// (<see cref="ItemSegment.ReadFrom"/>), as a pooled, owned buffer (<see cref="PooledBuffer{T}"/>): the triples
/// (<see cref="PooledBuffer{T}.Span"/> / <see cref="PooledBuffer{T}.Memory"/>, count
/// <see cref="PooledBuffer{T}.Length"/>) stay valid until disposed. The skip-and-continue counterpart, which
/// excludes a corrupt block rather than refusing the whole image, is <see cref="ItemSegmentFeed"/>. Being its own
/// type keeps a decoded item segment from being interchanged with another triple buffer of a different purpose.
/// </summary>
public sealed class DecodedItemSegment: PooledBuffer<EncodedTriple>
{
    /// <summary>Wraps a rented buffer whose first <paramref name="count"/> triples are the decoded segment.</summary>
    /// <param name="owner">The rented buffer owner; this takes ownership and returns it on dispose.</param>
    /// <param name="count">The number of decoded triples packed at the front of <paramref name="owner"/>.</param>
    internal DecodedItemSegment(IMemoryOwner<EncodedTriple> owner, int count): base(owner, count)
    {
    }
}
