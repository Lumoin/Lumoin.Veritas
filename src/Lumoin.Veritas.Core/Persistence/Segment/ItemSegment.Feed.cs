using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The checksum-gated feed read over a system-of-record item segment: the report-not-throw counterpart of
/// <see cref="ItemSegment.ReadFrom"/>. Framing and front-matter damage still refuse the image (the block
/// geometry could not be trusted), but a single block whose payload fails its checksum is excluded and named
/// rather than aborting the read, so a derived structure is never fed a corrupt block's items.
/// </summary>
public sealed partial class ItemSegment
{
    /// <summary>Walks the segment block by block, returning the triples whose blocks pass their checksum and naming the item ranges of any blocks that fail; the front-matter trailer and framing are verified first and refused on failure, since a corrupt block geometry cannot be safely walked.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="pool">The pool the returned triple buffer is rented from.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The verified triples and the excluded item ranges; the caller disposes it to return the buffer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static ItemSegmentFeed ReadVerifiedItems(ReadOnlySpan<byte> source, MemoryPool<EncodedTriple> pool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        SegmentLayout layout = ParseAndVerifyFrontMatter(source, resolveChecksum);
        IMemoryOwner<EncodedTriple> owner = pool.Rent(Math.Max(1, layout.ItemCount));
        try
        {
            Span<EncodedTriple> verified = owner.Memory.Span;
            Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            List<SkippedItemRange> skipped = [];
            int written = 0;
            for(int block = 0; block < layout.BlockCount; block++)
            {
                if(!VerifyBlock(source, layout, block, scratch, out int start, out int itemsInBlock, out long blockOffset))
                {
                    skipped.Add(new SkippedItemRange(block, start, itemsInBlock));
                    continue;
                }

                DecodeBlock(source, blockOffset, itemsInBlock, verified.Slice(written, itemsInBlock));
                written += itemsInBlock;
            }

            return new ItemSegmentFeed(owner, written, skipped, layout.Checksum is not null);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }
}
