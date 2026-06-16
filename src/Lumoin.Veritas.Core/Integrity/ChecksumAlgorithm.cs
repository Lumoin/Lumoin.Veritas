using System;
using System.IO.Hashing;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A pluggable, explicitly-selected checksum algorithm for the persistence container's integrity
/// layer: a stable on-disk id, a fixed byte width, and the compute function. Built-ins are exposed
/// as named static instances and resolved by <see cref="DefaultResolver"/>; a deployment adds its
/// own with <see cref="Create"/> and an injected <see cref="ResolveChecksumAlgorithmDelegate"/> that
/// maps the id on read. The id is a wire contract — once an image is written with an id, that id
/// must keep meaning the same algorithm.
/// </summary>
public sealed class ChecksumAlgorithm
{
    /// <summary>The largest checksum width this layer admits, bounding the on-load verify buffer.</summary>
    public const int MaximumByteWidth = 64;

    /// <summary>The stable on-disk algorithm id; 0 is reserved for "no checksum" and is not a valid algorithm id.</summary>
    public byte Id { get; }

    /// <summary>A short human-readable name.</summary>
    public string Name { get; }

    /// <summary>The fixed checksum width in bytes, in <c>[1, <see cref="MaximumByteWidth"/>]</c>.</summary>
    public int ByteWidth { get; }

    /// <summary>Computes the checksum of a span into a <see cref="ByteWidth"/>-byte destination.</summary>
    public ChecksumComputeDelegate Compute { get; }

    private ChecksumAlgorithm(byte id, string name, int byteWidth, ChecksumComputeDelegate compute)
    {
        if(id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Checksum-algorithm id 0 is reserved for no checksum.");
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(byteWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteWidth, MaximumByteWidth);
        ArgumentNullException.ThrowIfNull(compute);

        Id = id;
        Name = name;
        ByteWidth = byteWidth;
        Compute = compute;
    }

    /// <summary>Creates a custom checksum algorithm; the deployment is responsible for a globally-unique id and for resolving it on read.</summary>
    /// <param name="id">The stable on-disk id (non-zero).</param>
    /// <param name="name">A short human-readable name.</param>
    /// <param name="byteWidth">The fixed checksum width in bytes, in <c>[1, <see cref="MaximumByteWidth"/>]</c>.</param>
    /// <param name="compute">The compute function.</param>
    /// <returns>The algorithm.</returns>
    public static ChecksumAlgorithm Create(byte id, string name, int byteWidth, ChecksumComputeDelegate compute)
    {
        return new ChecksumAlgorithm(id, name, byteWidth, compute);
    }

    /// <summary>xxHash3 (64-bit) — the default: fast, well-distributed, non-cryptographic.</summary>
    public static ChecksumAlgorithm XxHash3 { get; } = new(1, "XxHash3-64", sizeof(ulong), ComputeXxHash3);

    /// <summary>CRC-32 — a selectable narrower alternative.</summary>
    public static ChecksumAlgorithm Crc32 { get; } = new(2, "CRC-32", sizeof(uint), ComputeCrc32);

    /// <summary>Resolves the built-in algorithms by id; returns <see langword="null"/> for an unknown or the reserved-none id.</summary>
    public static ResolveChecksumAlgorithmDelegate DefaultResolver { get; } = ResolveBuiltIn;

    /// <summary>Maps the built-in ids to their algorithms.</summary>
    /// <param name="id">The on-disk id.</param>
    /// <returns>The built-in algorithm, or <see langword="null"/>.</returns>
    private static ChecksumAlgorithm? ResolveBuiltIn(byte id)
    {
        return id switch
        {
            1 => XxHash3,
            2 => Crc32,
            _ => null,
        };
    }

    /// <summary>Computes xxHash3-64 into the 8-byte destination.</summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <param name="destination">The 8-byte destination.</param>
    private static void ComputeXxHash3(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        System.IO.Hashing.XxHash3.Hash(data, destination);
    }

    /// <summary>Computes CRC-32 into the 4-byte destination.</summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <param name="destination">The 4-byte destination.</param>
    private static void ComputeCrc32(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        System.IO.Hashing.Crc32.Hash(data, destination);
    }
}
