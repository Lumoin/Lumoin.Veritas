using System;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Composes the per-entry byte layout for hypertrie node entries
/// and feeds it through a <see cref="VeritasHash"/>. The output
/// is what gets XOR-folded into a node's
/// <see cref="NodeIdentifier"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte layout (protocol-pinned).</b> Sixteen bytes — the
/// 8-byte little-endian <c>key</c> followed by the 8-byte
/// little-endian <c>childIdentifier</c>. The layout is fixed
/// across builds and across implementations: a future
/// PostgreSQL projection of the hypertrie computes the same
/// per-entry hash from the same content, given the same
/// <see cref="VeritasHash"/>. Only the hash function is
/// configurable.
/// </para>
/// <para>
/// <b>Zero-sentinel guarantee.</b> If the underlying hash
/// returns zero,
/// <see cref="Default(VeritasHash, long, ulong)"/> upgrades the
/// result to <see cref="ZeroSentinel"/>. Zero is reserved
/// because it would XOR-combine into a
/// <see cref="NodeIdentifier"/> as a no-op, making the entry
/// invisible to deduplication.
/// </para>
/// </remarks>
public static class NodeEntryHashing
{
    //Per-entry byte buffer size: two 8-byte little-endian fields.
    private const int BufferSize = 16;

    /// <summary>
    /// The non-zero replacement returned when the underlying
    /// hash happens to be zero. Selected as the 64-bit golden
    /// ratio constant, which has high bit dispersion and no
    /// special meaning elsewhere in the type system.
    /// </summary>
    public const ulong ZeroSentinel = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// Composes the 16-byte (<paramref name="key"/>,
    /// <paramref name="childIdentifier"/>) buffer, hashes it
    /// through <paramref name="hash"/>, and upgrades a zero
    /// result to <see cref="ZeroSentinel"/>.
    /// </summary>
    /// <param name="hash">The hash function the application chose at the composition root.</param>
    /// <param name="key">The encoded term identifier of the entry.</param>
    /// <param name="childIdentifier">
    /// The child node's identifier value, or a non-zero
    /// presence marker for depth-1 leaves where the child
    /// reference is null.
    /// </param>
    /// <returns>A non-zero 64-bit per-entry hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    public static ulong Default(VeritasHash hash, long key, ulong childIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hash);

        Span<byte> buffer = stackalloc byte[BufferSize];
        MemoryMarshal.Write(buffer, in key);
        MemoryMarshal.Write(buffer[8..], in childIdentifier);

        ulong raw = hash(buffer);
        return raw == 0UL ? ZeroSentinel : raw;
    }
}
