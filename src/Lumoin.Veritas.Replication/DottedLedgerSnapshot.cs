using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// An atomic read of a <see cref="DottedCommitLedger"/> — the identities that have minted on it, the dotted
/// entry table, the causal context, and the dataset StateId the table reflects — unaffected by later commits.
/// This is the content of the at-rest replication causality artifact: a persist serializes the snapshot
/// captured from the same committed state as the system of record, paired by StateId, and recovery restores a
/// ledger from it before folding the annotated journal tail.
/// </summary>
/// <param name="Identities">The host identity axes that have ever minted on the ledger.</param>
/// <param name="Entries">The entry table: every present asserted default-graph triple with its causal dots.</param>
/// <param name="Context">The causal context; dominates every dot of every entry.</param>
/// <param name="StateId">The dataset StateId the entry table reflects.</param>
public sealed record DottedLedgerSnapshot(
    ImmutableArray<ReplicaAxis> Identities,
    ImmutableArray<DottedTripleAssignment> Entries,
    CausalContext Context,
    NodeIdentifier StateId)
{
    /// <summary>The artifact layout version written as the image's first byte; bumped when the byte layout changes.</summary>
    private const byte ImageFormatVersion = 1;

    /// <summary>The serialized byte size of the snapshot under <see cref="WriteTo"/>.</summary>
    /// <returns>The byte size.</returns>
    public int ComputeSerializedSize()
    {
        return sizeof(byte)
            + sizeof(ulong)
            + sizeof(int) + (Identities.Length * ReplicaAxis.ByteWidth)
            + Context.ComputeSerializedSize()
            + CausalitySerialization.AssignmentsSize(Entries);
    }

    /// <summary>Writes the snapshot image: a version byte, the StateId, the length-prefixed identities, the context, and the entry table, all little-endian.</summary>
    /// <param name="destination">The destination; at least <see cref="ComputeSerializedSize"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public int WriteTo(Span<byte> destination)
    {
        int p = 0;
        destination[p++] = ImageFormatVersion;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], StateId.Value);
        p += sizeof(ulong);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], Identities.Length);
        p += sizeof(int);
        foreach(ReplicaAxis identity in Identities)
        {
            identity.Bytes.Span.CopyTo(destination[p..]);
            p += ReplicaAxis.ByteWidth;
        }

        p += Context.WriteTo(destination[p..]);
        p += CausalitySerialization.WriteAssignments(destination[p..], Entries);

        return p;
    }

    /// <summary>Reads a snapshot image written by <see cref="WriteTo"/>.</summary>
    /// <param name="source">The whole artifact image.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="InvalidDataException">The image is truncated, carries trailing bytes, or declares an invalid count.</exception>
    /// <exception cref="NotSupportedException">The image's layout version is unsupported.</exception>
    public static DottedLedgerSnapshot ReadFrom(ReadOnlySpan<byte> source)
    {
        int p = 0;
        if(source.Length < sizeof(byte) + sizeof(ulong) + sizeof(int))
        {
            throw new InvalidDataException("A replication causality artifact image is truncated.");
        }

        byte version = source[p++];
        if(version != ImageFormatVersion)
        {
            throw new NotSupportedException($"Replication causality artifact layout version {version} is not supported; this build reads version {ImageFormatVersion}.");
        }

        NodeIdentifier stateId = new(BinaryPrimitives.ReadUInt64LittleEndian(source[p..]));
        p += sizeof(ulong);
        int identityCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(identityCount < 0 || identityCount > (source.Length - p) / ReplicaAxis.ByteWidth)
        {
            throw new InvalidDataException("A replication causality artifact declares an identity count beyond its image bounds.");
        }

        ImmutableArray<ReplicaAxis>.Builder identities = ImmutableArray.CreateBuilder<ReplicaAxis>(identityCount);
        for(int i = 0; i < identityCount; i++)
        {
            identities.Add(new ReplicaAxis(source.Slice(p, ReplicaAxis.ByteWidth).ToArray()));
            p += ReplicaAxis.ByteWidth;
        }

        CausalContext context = CausalContext.ReadFrom(source, ref p);
        ImmutableArray<DottedTripleAssignment> entries = CausalitySerialization.ReadAssignments(source, ref p);

        if(p != source.Length)
        {
            throw new InvalidDataException("A replication causality artifact image has unexpected trailing bytes.");
        }

        return new DottedLedgerSnapshot(identities.MoveToImmutable(), entries, context, stateId);
    }
}
