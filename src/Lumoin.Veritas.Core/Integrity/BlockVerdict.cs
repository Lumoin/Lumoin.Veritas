namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// One block's at-rest verify verdict in a block-framed persistence artifact, expressed in the format-neutral
/// terms a scrub needs: where the block lives in the image and whether its checksum matched. It is uniform
/// across every artifact a scrub walks — the columnar sidecar's column blobs, the system-of-record segment's
/// item blocks, and the integrity sketch's symbol blocks — so a scrub maps a failure to a repair without
/// knowing the artifact's format. A format's own richer verdict (a column blob's order/level/role, an item
/// block's item range) stays in that format; only these neutral coordinates cross the seam.
/// </summary>
/// <param name="BlockIndex">The block's index within its artifact.</param>
/// <param name="ByteOffset">The block's payload byte offset in the image.</param>
/// <param name="ByteLength">The block's payload byte length.</param>
/// <param name="IsValid">Whether the recomputed checksum matched the stored digest; always <see langword="true"/> when the image carried no checksums.</param>
public readonly record struct BlockVerdict(int BlockIndex, long ByteOffset, long ByteLength, bool IsValid);
