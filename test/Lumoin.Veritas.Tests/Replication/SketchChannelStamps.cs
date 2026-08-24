using System;
using System.Buffers;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// Wraps raw sketch-image bytes in a stamped sketch-channel frame and reads them back as an owning
/// <see cref="SketchFetchResult"/>, so a test's in-memory fetch hands the session the same stamped, pool-owned value
/// the real transport would — exercising the production frame writer and reader rather than constructing the result
/// directly.
/// </summary>
internal static class SketchChannelStamps
{
    /// <summary>Builds an owning <see cref="SketchFetchResult"/> stamped with the given domain and epoch over a copy of the image bytes, through the production frame writer and reader.</summary>
    /// <param name="domain">The reconciliation domain to stamp.</param>
    /// <param name="dictionaryEpoch">The dictionary epoch to stamp.</param>
    /// <param name="image">The raw sketch-image bytes; empty for a stamped decline.</param>
    /// <param name="pool">The pool the owned image is rented from.</param>
    /// <returns>The stamped, pool-owned fetch result.</returns>
    internal static SketchFetchResult OwnedImage(SketchChannelDomain domain, ulong dictionaryEpoch, ReadOnlyMemory<byte> image, MemoryPool<byte> pool)
    {
        ArrayBufferWriter<byte> frame = new();
        SketchChannelFraming.WriteStampedImage(new SketchChannelResponse(domain, dictionaryEpoch, image), frame);

        return SketchChannelFraming.ReadOwnedImage(new ReadOnlySequence<byte>(frame.WrittenMemory), pool);
    }
}
