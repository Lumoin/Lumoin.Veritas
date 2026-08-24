using System;
using System.Buffers;
using System.IO.Hashing;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The one digest every lineage-baseline consultation uses: a fixed-seed 64-bit hash over the minted
/// <see cref="CommitCausality"/>'s own canonical binary encoding. The engine digests here for the intent and
/// again for the confirm, and a crashed host's retry digests here once more — three computations that must
/// agree byte-for-byte across processes and machines, which is why the seed is a compile-time constant and the
/// input is the causality's single canonical encoding rather than any per-process view of it.
/// </summary>
public static class LineageDigests
{
    /// <summary>The fixed domain seed the digest hashes under, separating it from every other 64-bit hash in the store.</summary>
    private const long DigestSeed = 0x4C6D564D65746121;

    /// <summary>Digests one minted commit causality into the lineage identity its baseline is coordinated by.</summary>
    /// <param name="causality">The minted baseline causality.</param>
    /// <param name="pool">The pool the canonical encoding is staged in.</param>
    /// <returns>The digest, deterministic for one causality across processes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="causality"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public static NodeIdentifier DigestOf(CommitCausality causality, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(causality);
        ArgumentNullException.ThrowIfNull(pool);

        int size = causality.ComputeSerializedSize();
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> encoded = owner.Memory.Span[..size];
        int written = causality.WriteTo(encoded);

        return new NodeIdentifier(XxHash3.HashToUInt64(encoded[..written], DigestSeed));
    }
}
