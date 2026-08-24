using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// The little-endian span codec for dotted-assignment sections, shared by the durable dataset journal record
/// format (a <see cref="CommitCausality"/> annotation inside a journal record) and the at-rest replication
/// causality artifact (the ledger snapshot's entry table). One section is an <c>i32</c> count, then per
/// assignment the encoded triple (three <c>u32</c> term identifiers), an <c>i32</c> dot count, and the dots
/// (axis bytes then a <c>u64</c> counter each).
/// </summary>
public static class CausalitySerialization
{
    /// <summary>The serialized byte size of one assignment section.</summary>
    /// <param name="assignments">The assignments to size.</param>
    /// <returns>The byte size.</returns>
    public static int AssignmentsSize(ImmutableArray<DottedTripleAssignment> assignments)
    {
        int size = sizeof(int);
        foreach(DottedTripleAssignment assignment in assignments)
        {
            size += (3 * sizeof(uint)) + sizeof(int) + (assignment.Dots.Length * CausalDot.ByteWidth);
        }

        return size;
    }

    /// <summary>Writes one assignment section into <paramref name="destination"/>.</summary>
    /// <param name="destination">The destination slice; at least <see cref="AssignmentsSize"/> bytes.</param>
    /// <param name="assignments">The assignments to write.</param>
    /// <returns>The number of bytes written.</returns>
    public static int WriteAssignments(Span<byte> destination, ImmutableArray<DottedTripleAssignment> assignments)
    {
        int p = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination, assignments.Length);
        p += sizeof(int);
        foreach(DottedTripleAssignment assignment in assignments)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], assignment.Triple.Subject.Encoded);
            p += sizeof(uint);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], assignment.Triple.Predicate.Encoded);
            p += sizeof(uint);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], assignment.Triple.Object.Encoded);
            p += sizeof(uint);
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], assignment.Dots.Length);
            p += sizeof(int);
            foreach(CausalDot dot in assignment.Dots)
            {
                dot.Axis.Bytes.Span.CopyTo(destination[p..]);
                p += ReplicaAxis.ByteWidth;
                BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], dot.Counter);
                p += sizeof(ulong);
            }
        }

        return p;
    }

    /// <summary>Reads one assignment section written by <see cref="WriteAssignments"/>, advancing <paramref name="position"/> past it.</summary>
    /// <param name="source">The serialized bytes.</param>
    /// <param name="position">The read cursor; advanced past the section.</param>
    /// <returns>The assignments.</returns>
    /// <exception cref="InvalidDataException">The section is truncated, declares a negative or out-of-bounds count, or an assignment carries no dots.</exception>
    public static ImmutableArray<DottedTripleAssignment> ReadAssignments(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureRemaining(source, position, sizeof(int));
        int count = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
        position += sizeof(int);
        if(count < 0 || count > (source.Length - position) / ((3 * sizeof(uint)) + sizeof(int)))
        {
            throw new InvalidDataException("A dotted assignment section declares a count beyond its payload bounds.");
        }

        if(count == 0)
        {
            return [];
        }

        ImmutableArray<DottedTripleAssignment>.Builder builder = ImmutableArray.CreateBuilder<DottedTripleAssignment>(count);
        for(int i = 0; i < count; i++)
        {
            EnsureRemaining(source, position, (3 * sizeof(uint)) + sizeof(int));
            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            position += sizeof(uint);
            uint predicate = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            position += sizeof(uint);
            uint @object = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            position += sizeof(uint);
            int dotCount = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
            position += sizeof(int);
            if(dotCount <= 0 || dotCount > (source.Length - position) / CausalDot.ByteWidth)
            {
                throw new InvalidDataException("A dotted assignment declares a dot count beyond its payload bounds, or carries no dots.");
            }

            ImmutableArray<CausalDot>.Builder dots = ImmutableArray.CreateBuilder<CausalDot>(dotCount);
            for(int j = 0; j < dotCount; j++)
            {
                ReplicaAxis axis = new(source.Slice(position, ReplicaAxis.ByteWidth).ToArray());
                position += ReplicaAxis.ByteWidth;
                ulong counter = BinaryPrimitives.ReadUInt64LittleEndian(source[position..]);
                position += sizeof(ulong);
                dots.Add(new CausalDot(axis, counter));
            }

            builder.Add(new DottedTripleAssignment(EncodedTriple.FromEncoded(subject, predicate, @object), dots.MoveToImmutable()));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>Throws when fewer than <paramref name="needed"/> bytes remain in <paramref name="source"/> from <paramref name="position"/>.</summary>
    /// <param name="source">The serialized bytes.</param>
    /// <param name="position">The current read position.</param>
    /// <param name="needed">The bytes the next read needs.</param>
    /// <exception cref="InvalidDataException">The section is truncated.</exception>
    private static void EnsureRemaining(ReadOnlySpan<byte> source, int position, int needed)
    {
        if(source.Length - position < needed)
        {
            throw new InvalidDataException("A dotted assignment section is truncated.");
        }
    }
}
