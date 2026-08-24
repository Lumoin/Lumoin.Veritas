using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One pinned ledger snapshot bound to the library's dotted reconciliation projection: it converts the
/// snapshot's entry table into the dotted-version-vector-set state (one dotted entry per present dot), builds
/// the projection whose items the session's coded streams subtract, and carries the lookups and conversions the
/// session seams need — item back to entry through the projection, dot back to triple for drops, and the causal
/// context in both the house and the clock representation. The canonical value binding is SAME-EPOCH: an
/// entry's value bytes are the encoded triple's fixed term-identifier layout, so both ends must share one
/// dictionary epoch, which the channel header refuses before any exchange.
/// </summary>
/// <remarks>
/// The house-to-clock context conversion is exact only for per-axis CONTIGUOUS coverage — the standing shape of
/// every context this protocol produces, since local mints extend the own axis contiguously and every
/// reconcile fold joins whole contexts. A context with cloud coverage cannot be represented as a clock without
/// losing or overclaiming knowledge, so the conversion refuses it loudly; reaching that refusal means the
/// store's causal state did not come from this protocol. House counters are unsigned 64-bit and the library's
/// are 32-bit signed, so a counter beyond the library's range refuses loudly at the boundary too.
/// </remarks>
public sealed class DottedLedgerProjection
{
    /// <summary>The serialized byte width of an entry's canonical value: the three encoded term identifiers, little-endian.</summary>
    private const int ValueByteWidth = 3 * sizeof(uint);

    /// <summary>The library projection over the snapshot's dotted entries: the items, the reverse lookup, and the exchanged clock state.</summary>
    public DottedReconciliationProjection<EncodedTriple> Projection { get; }

    /// <summary>The pinned house-typed causal context of the snapshot — the context wire classification reads (never the live ledger).</summary>
    public CausalContext SnapshotContext { get; }

    /// <summary>The present dots of the pinned snapshot resolved back to their triples — the lookup a drop's dots resolve through.</summary>
    private Dictionary<CausalDot, EncodedTriple> TriplesByDot { get; }

    /// <summary>Builds the binding over a pinned ledger snapshot.</summary>
    /// <param name="snapshot">The pinned snapshot one session reconciles.</param>
    /// <param name="pool">The pool the projection's framing scratch is rented from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The snapshot's context carries cloud coverage or a counter beyond the library's range — causal state this protocol does not produce.</exception>
    public DottedLedgerProjection(DottedLedgerSnapshot snapshot, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pool);

        SnapshotContext = snapshot.Context;

        int dotCount = 0;
        foreach(DottedTripleAssignment assignment in snapshot.Entries)
        {
            dotCount += assignment.Dots.Length;
        }

        ImmutableArray<DottedEntry<EncodedTriple>>.Builder entries = ImmutableArray.CreateBuilder<DottedEntry<EncodedTriple>>(dotCount);
        TriplesByDot = new Dictionary<CausalDot, EncodedTriple>(dotCount);
        foreach(DottedTripleAssignment assignment in snapshot.Entries)
        {
            foreach(CausalDot dot in assignment.Dots)
            {
                entries.Add(new DottedEntry<EncodedTriple>(ImmutableArray.Create(dot.Axis.Bytes.Span), ToLibraryCounter(dot.Counter), assignment.Triple));
                TriplesByDot[dot] = assignment.Triple;
            }
        }

        DottedVersionVectorSetState<EncodedTriple> state = new(ToClockState(snapshot.Context), entries.MoveToImmutable());
        DottedItemDigest digest = new(VeritasHashing.Default);
        Projection = new DottedReconciliationProjection<EncodedTriple>(state, DottedReconciliationContract.Value, digest.Compute, CanonicalizeTriple, pool);
    }

    /// <summary>Resolves a present dot of the pinned snapshot back to its triple — the drop seam's lookup. A dot outside the snapshot resolves to nothing: its entry was never pinned here, and the peer context fold still carries the observation.</summary>
    /// <param name="dot">The dot to resolve.</param>
    /// <param name="triple">The triple the dot tags, when present.</param>
    /// <returns>Whether the dot names a pinned present entry.</returns>
    public bool TryResolveDot(in CausalDot dot, out EncodedTriple triple)
    {
        return TriplesByDot.TryGetValue(dot, out triple);
    }

    /// <summary>Converts a house causal context to the library's clock state — exact for per-axis contiguous coverage, refused otherwise.</summary>
    /// <param name="context">The context to convert.</param>
    /// <returns>The clock state.</returns>
    /// <exception cref="InvalidOperationException">The context carries cloud coverage or a counter beyond the library's range.</exception>
    public static VectorClockState ToClockState(CausalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ImmutableArray<CausalAxisCoverage> coverage = context.SnapshotCoverage();
        ImmutableArray<ReplicaCounterEntry>.Builder entries = ImmutableArray.CreateBuilder<ReplicaCounterEntry>(coverage.Length);
        foreach(CausalAxisCoverage axis in coverage)
        {
            if(!axis.Cloud.IsEmpty)
            {
                throw new InvalidOperationException("A causal context with cloud coverage cannot cross the dotted wire: this protocol keeps every context per-axis contiguous, so cloud coverage proves causal state that did not come from it.");
            }

            entries.Add(new ReplicaCounterEntry(ImmutableArray.Create(axis.Axis.Bytes.Span), ToLibraryCounter(axis.PrefixMax)));
        }

        return new VectorClockState(entries.MoveToImmutable());
    }

    /// <summary>Converts a peer's exchanged clock state to a house causal context, validating every entry's replica width and counter range.</summary>
    /// <param name="clock">The peer's exchanged clock state.</param>
    /// <returns>The house context; per-axis contiguous by construction.</returns>
    /// <exception cref="InvalidDataException">An entry's replica is not an identity axis's width, or its counter is negative.</exception>
    public static CausalContext ToCausalContext(VectorClockState clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        CausalContext context = new();
        foreach(ReplicaCounterEntry entry in clock.Entries)
        {
            if(entry.Replica.Length != ReplicaAxis.ByteWidth)
            {
                throw new InvalidDataException($"A peer clock entry's replica must be exactly {ReplicaAxis.ByteWidth} bytes; got {entry.Replica.Length}.");
            }

            if(entry.Count < 0)
            {
                throw new InvalidDataException("A peer clock entry's counter cannot be negative.");
            }

            context.FoldContiguous(new ReplicaAxis(ImmutableCollectionsUnderlying(entry.Replica)), (ulong)entry.Count);
        }

        return context;
    }

    /// <summary>Converts a library dot state to a house causal dot, validating the replica width and counter floor.</summary>
    /// <param name="dot">The dot state.</param>
    /// <returns>The causal dot.</returns>
    /// <exception cref="InvalidDataException">The replica is not an identity axis's width, or the counter is below one.</exception>
    public static CausalDot ToCausalDot(DotState dot)
    {
        ArgumentNullException.ThrowIfNull(dot);

        if(dot.Replica.Length != ReplicaAxis.ByteWidth)
        {
            throw new InvalidDataException($"A dotted dot's replica must be exactly {ReplicaAxis.ByteWidth} bytes; got {dot.Replica.Length}.");
        }

        if(dot.Counter < 1)
        {
            throw new InvalidDataException("A dotted dot's counter must be at least one.");
        }

        return new CausalDot(new ReplicaAxis(ImmutableCollectionsUnderlying(dot.Replica)), (ulong)dot.Counter);
    }

    /// <summary>Converts a library dotted entry to its house causal dot, validating the replica width and counter floor.</summary>
    /// <param name="entry">The dotted entry.</param>
    /// <returns>The causal dot.</returns>
    /// <exception cref="InvalidDataException">The replica is not an identity axis's width, or the counter is below one.</exception>
    public static CausalDot ToCausalDot(DottedEntry<EncodedTriple> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ToCausalDot(new DotState(entry.Replica, entry.Counter));
    }

    /// <summary>Serializes one dotted element for the elements leg: the length-prefixed replica bytes, the counter, and the three encoded term identifiers — the injected write half of the channel's element codec.</summary>
    /// <param name="element">The element to serialize.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public static void WriteElement(DottedEntry<EncodedTriple> element, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(output);

        ReconciliationWireCodec.WritePrefixedBytes(output, element.Replica);
        ReconciliationWireCodec.WriteInt(output, element.Counter);
        ReconciliationWireCodec.WriteInt(output, unchecked((int)element.Value.Subject.Encoded));
        ReconciliationWireCodec.WriteInt(output, unchecked((int)element.Value.Predicate.Encoded));
        ReconciliationWireCodec.WriteInt(output, unchecked((int)element.Value.Object.Encoded));
    }

    /// <summary>Deserializes one dotted element from the elements leg, validating the replica width and counter floor — the injected read half of the channel's element codec.</summary>
    /// <param name="reader">The frame cursor, advanced past the element.</param>
    /// <returns>The element, owning its content.</returns>
    /// <exception cref="InvalidDataException">The element is truncated, its replica is not an identity axis's width, or its counter is below one.</exception>
    public static DottedEntry<EncodedTriple> ReadElement(ref SequenceReader<byte> reader)
    {
        ImmutableArray<byte> replica = ReconciliationWireCodec.ReadPrefixedBytes(ref reader);
        if(replica.Length != ReplicaAxis.ByteWidth)
        {
            throw new InvalidDataException($"A dotted element's replica must be exactly {ReplicaAxis.ByteWidth} bytes; got {replica.Length}.");
        }

        int counter = ReconciliationWireCodec.ReadInt(ref reader);
        if(counter < 1)
        {
            throw new InvalidDataException("A dotted element's counter must be at least one.");
        }

        uint subject = unchecked((uint)ReconciliationWireCodec.ReadInt(ref reader));
        uint predicate = unchecked((uint)ReconciliationWireCodec.ReadInt(ref reader));
        uint @object = unchecked((uint)ReconciliationWireCodec.ReadInt(ref reader));

        return new DottedEntry<EncodedTriple>(replica, counter, EncodedTriple.FromEncoded(subject, predicate, @object));
    }

    /// <summary>The same-epoch canonical value binding: an entry's value bytes are the encoded triple's three term identifiers, little-endian — pure, deterministic, and replica-independent within one dictionary epoch, which the channel header pins.</summary>
    /// <param name="triple">The triple to canonicalize.</param>
    /// <returns>The canonical value bytes.</returns>
    private static ReadOnlyMemory<byte> CanonicalizeTriple(EncodedTriple triple)
    {
        byte[] value = new byte[ValueByteWidth];
        BinaryPrimitives.WriteUInt32LittleEndian(value, triple.Subject.Encoded);
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(sizeof(uint)), triple.Predicate.Encoded);
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(2 * sizeof(uint)), triple.Object.Encoded);

        return value;
    }

    /// <summary>Narrows a house counter to the library's 32-bit signed range, refusing overflow loudly rather than wrapping into a colliding dot.</summary>
    /// <param name="counter">The house counter.</param>
    /// <returns>The library counter.</returns>
    /// <exception cref="InvalidOperationException">The counter exceeds the library's range.</exception>
    private static int ToLibraryCounter(ulong counter)
    {
        if(counter > int.MaxValue)
        {
            throw new InvalidOperationException($"A causal counter of {counter} exceeds the dotted wire's 32-bit range; the exchange refuses rather than wrapping into a colliding dot.");
        }

        return (int)counter;
    }

    /// <summary>Wraps an immutable byte array's underlying memory without copying — the identity bytes are immutable by type, so the axis may hold them as given.</summary>
    /// <param name="bytes">The immutable bytes.</param>
    /// <returns>The bytes as read-only memory.</returns>
    private static ReadOnlyMemory<byte> ImmutableCollectionsUnderlying(ImmutableArray<byte> bytes)
    {
        return bytes.AsMemory();
    }

    /// <summary>The dotted item digest: two domain-separated passes of the house hash over the pinned frame, giving the two 64-bit halves of the 16-byte item from one canonical encoding — the content-hash projection's idiom over the dotted frame. The returned memory is this instance's reusable backing; the library projection copies it before the next call, per the digest seam's contract.</summary>
    /// <param name="hash">The deterministic house hash both ends compute items with.</param>
    private sealed class DottedItemDigest(VeritasHash hash)
    {
        /// <summary>The deterministic house hash.</summary>
        private VeritasHash Hash { get; } = hash;

        /// <summary>The reusable 16-byte digest backing; the caller copies it before the next call.</summary>
        private byte[] Digest { get; } = new byte[Core.ContentAddressing.ContentKey128.ByteWidth];

        /// <summary>The reusable domain-prefixed frame scratch, grown on demand.</summary>
        private byte[] scratch = [];

        /// <summary>Computes the 16-byte dotted item over a pinned frame.</summary>
        /// <param name="canonicalBytes">The pinned frame: the replica bytes, the counter, and the canonical value bytes.</param>
        /// <returns>The digest, backed by this instance's reusable buffer.</returns>
        public ReadOnlyMemory<byte> Compute(ReadOnlyMemory<byte> canonicalBytes)
        {
            int frameLength = canonicalBytes.Length + 1;
            if(scratch.Length < frameLength)
            {
                scratch = new byte[frameLength];
            }

            Span<byte> frame = scratch.AsSpan(0, frameLength);
            canonicalBytes.Span.CopyTo(frame[1..]);

            //The two passes differ only in the leading domain byte, so the hash's avalanche gives two
            //well-separated 64-bit halves of one 128-bit item from a single canonical encoding.
            frame[0] = 0;
            ulong low = Hash(frame);
            frame[0] = 1;
            ulong high = Hash(frame);

            BinaryPrimitives.WriteUInt64LittleEndian(Digest, low);
            BinaryPrimitives.WriteUInt64LittleEndian(Digest.AsSpan(sizeof(ulong)), high);

            return Digest;
        }
    }
}
