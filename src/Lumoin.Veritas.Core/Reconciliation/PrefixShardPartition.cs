using System;
using System.Buffers;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// One pooled snapshot of a partitioned key set, exposing an independent item enumeration per shard. The bytes
/// live in a single rented buffer; each shard's enumeration is a run of stable slices into it. The partition
/// owns the rental and releases it on <see cref="Dispose"/>, after which the shard views must not be read.
/// </summary>
/// <remarks>
/// The intended lifetime is: partition, construct every per-shard add-only session (each copies the keys it is
/// handed at construction), then dispose the partition. Disposing earlier would recycle the buffer under the
/// slice handles a not-yet-constructed session still points at.
/// </remarks>
public sealed class PrefixShardPartition: IDisposable
{
    /// <summary>The rented snapshot buffer the shard slices view; nulled on dispose, which is also the disposed sentinel.</summary>
    private IMemoryOwner<byte>? Owner { get; set; }

    /// <summary>The per-shard slice runs into the snapshot buffer, indexed by shard; dropped on dispose.</summary>
    private ReadOnlyMemory<byte>[][] Shards { get; set; }

    /// <summary>
    /// Initializes a partition over an owned snapshot buffer and its per-shard slice runs.
    /// </summary>
    /// <param name="owner">The rented snapshot buffer the shard slices view; disposed by this partition.</param>
    /// <param name="shards">The per-shard slice runs, indexed by shard.</param>
    /// <param name="itemCount">The total item count across all shards.</param>
    internal PrefixShardPartition(IMemoryOwner<byte> owner, ReadOnlyMemory<byte>[][] shards, int itemCount)
    {
        Owner = owner;
        Shards = shards;
        ItemCount = itemCount;
    }

    /// <summary>The number of shards this partition holds.</summary>
    public int ShardCount => Shards.Length;

    /// <summary>The total item count across all shards.</summary>
    public int ItemCount { get; }

    /// <summary>
    /// Returns shard <paramref name="shardIndex"/>'s item enumeration, ready to hand to an add-only session as
    /// its local operand.
    /// </summary>
    /// <param name="shardIndex">The shard index, in the range zero through <see cref="ShardCount"/> exclusive.</param>
    /// <returns>The shard's item keys as a read-only collection of stable slices into the snapshot buffer.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the partition has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="shardIndex"/> is out of range.</exception>
    public IReadOnlyList<ReadOnlyMemory<byte>> Shard(int shardIndex)
    {
        ObjectDisposedException.ThrowIf(Owner is null, this);

        if(shardIndex < 0 || shardIndex >= Shards.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(shardIndex), shardIndex, "The shard index must be within the partition's shard range.");
        }

        return Shards[shardIndex];
    }

    /// <summary>
    /// Returns the item count of shard <paramref name="shardIndex"/> without reading its bytes, for pre-sizing a
    /// session's symbol budget against the shard's occupancy.
    /// </summary>
    /// <param name="shardIndex">The shard index, in the range zero through <see cref="ShardCount"/> exclusive.</param>
    /// <returns>The number of items in the shard.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="shardIndex"/> is out of range.</exception>
    public int Occupancy(int shardIndex)
    {
        if(shardIndex < 0 || shardIndex >= Shards.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(shardIndex), shardIndex, "The shard index must be within the partition's shard range.");
        }

        return Shards[shardIndex].Length;
    }

    /// <summary>
    /// Releases the snapshot rental and drops the shard views; idempotent. After it, <see cref="Shard"/> throws
    /// and the previously returned slice handles must not be read.
    /// </summary>
    public void Dispose()
    {
        IMemoryOwner<byte>? held = Owner;
        if(held is null)
        {
            return;
        }

        Owner = null;
        Shards = [];
        held.Dispose();
    }
}
