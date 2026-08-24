using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// A causal context: the set of every <see cref="CausalDot"/> a replica has ever observed — its own minted dots
/// and every peer dot folded in through reconciliation. Coverage is kept compact per axis as a contiguous prefix
/// maximum plus a sorted cloud of counters beyond it; folding a counter adjacent to the prefix advances the
/// prefix through the cloud. The standing invariant of the dotted observed-remove regime is that the context
/// dominates every dot of every present entry; a dot the context covers whose entry is absent IS the tombstone —
/// no tombstone objects exist. All folds are monotone joins, so re-folding observed knowledge is a no-op.
/// </summary>
/// <remarks>
/// The context is mutable and unsynchronized; its owner serializes access (the ledger folds under its own lock).
/// </remarks>
public sealed class CausalContext
{
    /// <summary>The compact coverage of one axis: the contiguous prefix maximum and the sorted counters observed beyond it.</summary>
    private sealed class AxisCoverage
    {
        /// <summary>The largest counter N such that every counter in [1, N] on the axis is covered; 0 when none are.</summary>
        public ulong PrefixMax { get; set; }

        /// <summary>The counters observed beyond the contiguous prefix, sorted ascending; <see langword="null"/> until one exists.</summary>
        public List<ulong>? Cloud { get; set; }
    }

    /// <summary>The per-axis coverage table.</summary>
    private readonly Dictionary<ReplicaAxis, AxisCoverage> coverage = [];

    /// <summary>The number of axes the context has observed at least one dot on.</summary>
    public int AxisCount
    {
        get
        {
            return coverage.Count;
        }
    }

    /// <summary>Whether the context covers a dot — the dot has been observed, by mint or by fold.</summary>
    /// <param name="dot">The dot to test.</param>
    /// <returns><see langword="true"/> when the dot is covered.</returns>
    public bool Covers(in CausalDot dot)
    {
        if(!coverage.TryGetValue(dot.Axis, out AxisCoverage? axis))
        {
            return false;
        }

        if(dot.Counter <= axis.PrefixMax)
        {
            return true;
        }

        return axis.Cloud is not null && axis.Cloud.BinarySearch(dot.Counter) >= 0;
    }

    /// <summary>The largest counter the context covers contiguously from 1 on an axis; 0 when none. The next local mint on the owning replica's own axis is this plus one — counter continuity derives from the context, never from separate counter state.</summary>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The contiguous prefix maximum.</returns>
    public ulong PrefixMaxOn(ReplicaAxis axis)
    {
        return coverage.TryGetValue(axis, out AxisCoverage? found) ? found.PrefixMax : 0;
    }

    /// <summary>The largest counter the context covers ANYWHERE on an axis — the prefix maximum or the top of the cloud beyond it; 0 when none. A peer presenting coverage beyond the LOCAL axis's own maximum proves a second minter under the same identity (the identity-collision tripwire reads this).</summary>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The overall maximum covered counter.</returns>
    public ulong MaxOn(ReplicaAxis axis)
    {
        if(!coverage.TryGetValue(axis, out AxisCoverage? found))
        {
            return 0;
        }

        return found.Cloud is { Count: > 0 } cloud ? cloud[^1] : found.PrefixMax;
    }

    /// <summary>Folds one observed dot into the context — a monotone join, idempotent when the dot is already covered. A counter adjacent to the contiguous prefix advances the prefix and compacts the cloud through any run it completes.</summary>
    /// <param name="dot">The observed dot.</param>
    public void Fold(in CausalDot dot)
    {
        if(!coverage.TryGetValue(dot.Axis, out AxisCoverage? axis))
        {
            axis = new AxisCoverage();
            coverage[dot.Axis] = axis;
        }

        FoldCounter(axis, dot.Counter);
    }

    /// <summary>Folds the contiguous coverage [1, <paramref name="prefixMax"/>] on an axis in one step — the monotone join a wire binding uses to fold a peer clock entry without per-counter iteration; idempotent, and a no-op when the axis already covers the prefix.</summary>
    /// <param name="axis">The axis whose contiguous coverage is folded.</param>
    /// <param name="prefixMax">The contiguous coverage to fold; 0 folds nothing.</param>
    public void FoldContiguous(ReplicaAxis axis, ulong prefixMax)
    {
        if(prefixMax == 0)
        {
            return;
        }

        if(!coverage.TryGetValue(axis, out AxisCoverage? found))
        {
            found = new AxisCoverage();
            coverage[axis] = found;
        }

        if(prefixMax > found.PrefixMax)
        {
            FoldPrefix(found, prefixMax);
        }
    }

    /// <summary>Folds another context into this one — the pointwise monotone join over every axis, idempotent by nature. The other context is read only.</summary>
    /// <param name="other">The context to fold in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void Merge(CausalContext other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach((ReplicaAxis otherAxis, AxisCoverage otherCoverage) in other.coverage)
        {
            if(!coverage.TryGetValue(otherAxis, out AxisCoverage? axis))
            {
                axis = new AxisCoverage();
                coverage[otherAxis] = axis;
            }

            if(otherCoverage.PrefixMax > axis.PrefixMax)
            {
                FoldPrefix(axis, otherCoverage.PrefixMax);
            }

            if(otherCoverage.Cloud is { } cloud)
            {
                foreach(ulong counter in cloud)
                {
                    FoldCounter(axis, counter);
                }
            }
        }
    }

    /// <summary>Whether every dot this context covers is also covered by <paramref name="other"/> — folding this context into <paramref name="other"/> would add no knowledge.</summary>
    /// <param name="other">The context to compare against.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> dominates this context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool CoveredBy(CausalContext other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach((ReplicaAxis axis, AxisCoverage axisCoverage) in coverage)
        {
            if(!other.coverage.TryGetValue(axis, out AxisCoverage? otherCoverage))
            {
                return false;
            }

            if(axisCoverage.PrefixMax > otherCoverage.PrefixMax)
            {
                return false;
            }

            if(axisCoverage.Cloud is { } cloud)
            {
                foreach(ulong counter in cloud)
                {
                    if(!other.Covers(new CausalDot(axis, counter)))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Reads the per-axis coverage as one immutable snapshot — the enumeration a wire binding converts to its clock representation, unaffected by later folds.</summary>
    /// <returns>The per-axis coverage, one entry per observed axis.</returns>
    public ImmutableArray<CausalAxisCoverage> SnapshotCoverage()
    {
        ImmutableArray<CausalAxisCoverage>.Builder axes = ImmutableArray.CreateBuilder<CausalAxisCoverage>(coverage.Count);
        foreach((ReplicaAxis axis, AxisCoverage axisCoverage) in coverage)
        {
            ImmutableArray<ulong> cloud = axisCoverage.Cloud is { } counters ? [.. counters] : [];
            axes.Add(new CausalAxisCoverage(axis, axisCoverage.PrefixMax, cloud));
        }

        return axes.MoveToImmutable();
    }

    /// <summary>Deep-copies the context — the snapshot form a session pins or a persist serializes, unaffected by later folds.</summary>
    /// <returns>The copy.</returns>
    public CausalContext Clone()
    {
        CausalContext copy = new();
        foreach((ReplicaAxis axis, AxisCoverage axisCoverage) in coverage)
        {
            copy.coverage[axis] = new AxisCoverage
            {
                PrefixMax = axisCoverage.PrefixMax,
                Cloud = axisCoverage.Cloud is { } cloud ? [.. cloud] : null,
            };
        }

        return copy;
    }

    /// <summary>Folds the contiguous coverage [1, <paramref name="prefixMax"/>] into an axis. Postcondition: the representation is canonical — the cloud holds only counters beyond the prefix, never adjacent to it.</summary>
    /// <param name="axis">The axis coverage to fold into.</param>
    /// <param name="prefixMax">The contiguous coverage to fold.</param>
    private static void FoldPrefix(AxisCoverage axis, ulong prefixMax)
    {
        axis.PrefixMax = prefixMax;
        if(axis.Cloud is not { } cloud)
        {
            return;
        }

        int drained = 0;
        while(drained < cloud.Count && (cloud[drained] <= axis.PrefixMax || cloud[drained] == axis.PrefixMax + 1))
        {
            if(cloud[drained] > axis.PrefixMax)
            {
                axis.PrefixMax = cloud[drained];
            }

            drained++;
        }

        if(drained > 0)
        {
            cloud.RemoveRange(0, drained);
        }

        if(cloud.Count == 0)
        {
            axis.Cloud = null;
        }
    }

    /// <summary>Folds one observed counter into an axis. Postcondition: the counter is covered and the representation is canonical — the cloud holds only counters beyond the prefix, never adjacent to it.</summary>
    /// <param name="axis">The axis coverage to fold into.</param>
    /// <param name="counter">The observed counter.</param>
    private static void FoldCounter(AxisCoverage axis, ulong counter)
    {
        if(counter <= axis.PrefixMax)
        {
            return;
        }

        if(counter == axis.PrefixMax + 1)
        {
            axis.PrefixMax = counter;

            if(axis.Cloud is { } cloud)
            {
                int drained = 0;
                while(drained < cloud.Count && cloud[drained] == axis.PrefixMax + 1)
                {
                    axis.PrefixMax = cloud[drained];
                    drained++;
                }

                if(drained > 0)
                {
                    cloud.RemoveRange(0, drained);
                }

                if(cloud.Count == 0)
                {
                    axis.Cloud = null;
                }
            }

            return;
        }

        axis.Cloud ??= [];
        int position = axis.Cloud.BinarySearch(counter);
        if(position < 0)
        {
            axis.Cloud.Insert(~position, counter);
        }
    }

    /// <summary>The serialized byte size of the context under <see cref="WriteTo"/>.</summary>
    /// <returns>The byte size.</returns>
    public int ComputeSerializedSize()
    {
        int size = sizeof(int);
        foreach(AxisCoverage axisCoverage in coverage.Values)
        {
            size += ReplicaAxis.ByteWidth + sizeof(ulong) + sizeof(int) + ((axisCoverage.Cloud?.Count ?? 0) * sizeof(ulong));
        }

        return size;
    }

    /// <summary>Writes the context into <paramref name="destination"/>: an axis count, then per axis the identity bytes, the prefix maximum, and the length-prefixed cloud, all little-endian.</summary>
    /// <param name="destination">The destination; at least <see cref="ComputeSerializedSize"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public int WriteTo(Span<byte> destination)
    {
        int p = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination, coverage.Count);
        p += sizeof(int);

        foreach((ReplicaAxis axis, AxisCoverage axisCoverage) in coverage)
        {
            axis.Bytes.Span.CopyTo(destination[p..]);
            p += ReplicaAxis.ByteWidth;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], axisCoverage.PrefixMax);
            p += sizeof(ulong);

            int cloudCount = axisCoverage.Cloud?.Count ?? 0;
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], cloudCount);
            p += sizeof(int);
            if(axisCoverage.Cloud is { } cloud)
            {
                foreach(ulong counter in cloud)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], counter);
                    p += sizeof(ulong);
                }
            }
        }

        return p;
    }

    /// <summary>Reads a context written by <see cref="WriteTo"/>, advancing <paramref name="position"/> past it.</summary>
    /// <param name="source">The serialized bytes.</param>
    /// <param name="position">The read cursor; advanced past the context.</param>
    /// <returns>The context.</returns>
    /// <exception cref="InvalidDataException">The context section is truncated, declares a negative or out-of-bounds count, declares the same replica axis twice, or carries non-ascending cloud counters.</exception>
    public static CausalContext ReadFrom(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureRemaining(source, position, sizeof(int));
        int axisCount = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
        position += sizeof(int);
        if(axisCount < 0 || axisCount > (source.Length - position) / (ReplicaAxis.ByteWidth + sizeof(ulong) + sizeof(int)))
        {
            throw new InvalidDataException("A causal context declares an axis count beyond its payload bounds.");
        }

        CausalContext context = new();
        for(int i = 0; i < axisCount; i++)
        {
            EnsureRemaining(source, position, ReplicaAxis.ByteWidth + sizeof(ulong) + sizeof(int));
            ReplicaAxis axis = new(source.Slice(position, ReplicaAxis.ByteWidth).ToArray());
            if(context.coverage.ContainsKey(axis))
            {
                throw new InvalidDataException("A causal context declares the same replica axis twice; a duplicate would silently discard coverage.");
            }

            position += ReplicaAxis.ByteWidth;
            ulong prefixMax = BinaryPrimitives.ReadUInt64LittleEndian(source[position..]);
            position += sizeof(ulong);
            int cloudCount = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
            position += sizeof(int);
            if(cloudCount < 0 || cloudCount > (source.Length - position) / sizeof(ulong))
            {
                throw new InvalidDataException("A causal context declares a cloud count beyond its payload bounds.");
            }

            List<ulong>? cloud = cloudCount > 0 ? new List<ulong>(cloudCount) : null;
            ulong previous = 0;
            for(int j = 0; j < cloudCount; j++)
            {
                ulong counter = BinaryPrimitives.ReadUInt64LittleEndian(source[position..]);
                position += sizeof(ulong);
                if(counter <= prefixMax || counter <= previous)
                {
                    throw new InvalidDataException("A causal context's cloud counters must be strictly ascending beyond the prefix maximum.");
                }

                previous = counter;
                cloud!.Add(counter);
            }

            //A cloud counter adjacent to the prefix would be non-canonical; the strictly-ascending check above
            //admits it, so canonicalize by re-folding rather than trusting the writer.
            AxisCoverage axisCoverage = new() { PrefixMax = prefixMax, Cloud = cloud };
            if(cloud is { Count: > 0 } && cloud[0] == prefixMax + 1)
            {
                AxisCoverage canonical = new() { PrefixMax = prefixMax };
                foreach(ulong counter in cloud)
                {
                    FoldCounter(canonical, counter);
                }

                axisCoverage = canonical;
            }

            context.coverage[axis] = axisCoverage;
        }

        return context;
    }

    /// <summary>Throws when fewer than <paramref name="needed"/> bytes remain in <paramref name="source"/> from <paramref name="position"/>.</summary>
    /// <param name="source">The serialized bytes.</param>
    /// <param name="position">The current read position.</param>
    /// <param name="needed">The bytes the next read needs.</param>
    /// <exception cref="InvalidDataException">The context section is truncated.</exception>
    private static void EnsureRemaining(ReadOnlySpan<byte> source, int position, int needed)
    {
        if(source.Length - position < needed)
        {
            throw new InvalidDataException("A causal context section is truncated.");
        }
    }
}
