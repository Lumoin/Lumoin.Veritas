using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The self-describing, versioned byte image of a standalone <see cref="ColumnarTripleIndex"/> —
/// the persistence container that frames the per-column codec into a whole-index sidecar so a
/// built index reloads WITHOUT re-sorting or re-packing (warm start). The layout is a fixed
/// header (magic, format version, required-feature flags, a reserved checksum-algorithm byte),
/// then the index scalars and accumulated delta, then a per-column directory, then the column
/// blobs laid back-to-back, each starting on a 64-byte boundary so a later memory-mapped reader
/// can address them in place. Each blob is one <see cref="BlockPackedColumn"/> image and is fully
/// self-describing via its leading mode tag; the directory mirrors the encoding id so a reader can
/// route or skip a blob without opening it.
/// </summary>
/// <remarks>
/// <para>
/// Out of scope for this container, by design: per-block and whole-file checksums (a later
/// increment; the algorithm-id byte is reserved here), atomic commit discipline (the
/// manifest/CURRENT-pointer story belongs to the durable journal tier), the string-to-identifier
/// term dictionary and the optional self-index (both external to this type — rebuildable or
/// separately persisted), and graph-view indexes whose columns are shared with a graph set (a
/// view's <c>level0Bounds</c> is rejected; persist the whole set instead). The decode kernels are
/// process-local and re-supplied at read time, never serialized.
/// </para>
/// </remarks>
public sealed partial class ColumnarTripleIndex
{
    /// <summary>The container's magic bytes — identifies a Veritas columnar index image.</summary>
    private static ReadOnlySpan<byte> ContainerMagic => "VTSCIDX1"u8;

    /// <summary>The format major version; a mismatch is an incompatible image this build refuses.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The required-feature flags this build understands; an image setting any bit outside this mask is refused.</summary>
    private const ulong KnownRequiredFeatures = 0UL;

    /// <summary>The reserved checksum-algorithm id meaning "no checksum" — the only value this increment writes.</summary>
    private const byte ChecksumAlgorithmNone = 0;

    /// <summary>The fixed header size: magic (8) + major (1) + minor (1) + feature flags (8) + checksum-algorithm id (1).</summary>
    private const int HeaderSize = 8 + 1 + 1 + 8 + 1;

    /// <summary>The fixed per-column directory entry size: order (1) + level (1) + role (1) + encoding (1) + byte offset (8) + byte length (8).</summary>
    private const int DirectoryEntrySize = 1 + 1 + 1 + 1 + 8 + 8;

    /// <summary>The blob start alignment in bytes — the widest column-kernel SIMD lane, so a mapped reader gets aligned views.</summary>
    private const int BlobAlignment = 64;

    /// <summary>The directory role tag for a value column.</summary>
    private const byte RoleValues = 0;

    /// <summary>The directory role tag for an offset column.</summary>
    private const byte RoleOffsets = 1;

    /// <summary>The fixed size of one delta triple: the subject, predicate, and object identifiers.</summary>
    private const int TripleByteSize = 3 * sizeof(uint);

    /// <summary>The smallest valid image: the header plus the fixed index scalars that precede the variable-length delta and directory.</summary>
    private const int MinimumImageSize = HeaderSize + 1 + 1 + sizeof(int) + 1 + 1;

    /// <summary>One directory row: which order and CSR position a column occupies, and the column itself.</summary>
    /// <param name="orderIndex">The permutation index in <c>[0, 6)</c>.</param>
    /// <param name="level">The CSR descent level: 0, 1, or 2.</param>
    /// <param name="role">The column role: <see cref="RoleValues"/> or <see cref="RoleOffsets"/>.</param>
    /// <param name="column">The column at this slot.</param>
    private readonly struct ColumnSlot(byte orderIndex, byte level, byte role, BlockPackedColumn column)
    {
        /// <summary>The permutation index this column belongs to.</summary>
        public byte OrderIndex { get; } = orderIndex;

        /// <summary>The CSR descent level.</summary>
        public byte Level { get; } = level;

        /// <summary>The column role — value or offset.</summary>
        public byte Role { get; } = role;

        /// <summary>The column.</summary>
        public BlockPackedColumn Column { get; } = column;
    }

    /// <summary>The number of bytes <see cref="WriteTo"/> writes for this index.</summary>
    /// <exception cref="NotSupportedException">This index is a graph view (its columns are shared and not persisted standalone).</exception>
    internal int SerializedSize
    {
        get
        {
            List<ColumnSlot> slots = CollectSlots();
            LayOutBlobs(slots, out int total);

            return total;
        }
    }

    /// <summary>Collects the five columns of every materialised order in canonical slot order.</summary>
    /// <returns>The ordered column slots.</returns>
    private List<ColumnSlot> CollectSlots()
    {
        List<ColumnSlot> slots = [];
        for(byte i = 0; i < orders.Length; i++)
        {
            ColumnarOrder? order = orders[i];
            if(order is null)
            {
                continue;
            }

            slots.Add(new ColumnSlot(i, 0, RoleValues, order.ValuesColumnAt(0)));
            slots.Add(new ColumnSlot(i, 0, RoleOffsets, order.OffsetsColumnAt(0)));
            slots.Add(new ColumnSlot(i, 1, RoleValues, order.ValuesColumnAt(1)));
            slots.Add(new ColumnSlot(i, 1, RoleOffsets, order.OffsetsColumnAt(1)));
            slots.Add(new ColumnSlot(i, 2, RoleValues, order.ValuesColumnAt(2)));
        }

        return slots;
    }

    /// <summary>The byte size of the header, scalars, delta, and directory — everything before the first blob.</summary>
    /// <param name="slotCount">The number of column slots in the directory.</param>
    /// <returns>The front-matter byte size.</returns>
    private int FrontSize(int slotCount)
    {
        int scalars = 1                                  // order-set mode
            + 1                                          // backing
            + sizeof(int)                                // base triple count
            + 1                                          // orders-present bitmask
            + 1                                          // level0-bounds-present flag
            + sizeof(int) + (addedSet.Count * TripleByteSize)        // added delta: count + 3 ids per triple
            + sizeof(int) + (removedSet.Count * TripleByteSize);     // removed delta
        int directory = sizeof(int) + (slotCount * DirectoryEntrySize);

        return HeaderSize + scalars + directory;
    }

    /// <summary>Computes each blob's 64-byte-aligned byte offset and the total image size.</summary>
    /// <param name="slots">The column slots.</param>
    /// <param name="total">Receives the total image size in bytes.</param>
    /// <returns>The per-slot blob byte offsets.</returns>
    private int[] LayOutBlobs(List<ColumnSlot> slots, out int total)
    {
        if(level0Bounds is not null)
        {
            throw new NotSupportedException("A graph-view index shares its columns with a graph set and is not persisted standalone; persist the whole graph set.");
        }

        int[] offsets = new int[slots.Count];
        int cursor = FrontSize(slots.Count);
        for(int i = 0; i < slots.Count; i++)
        {
            cursor = Align(cursor);
            offsets[i] = cursor;
            cursor += slots[i].Column.SerializedSize;
        }

        total = cursor;

        return offsets;
    }

    /// <summary>Writes this index's self-describing byte image into <paramref name="destination"/> (exactly <see cref="SerializedSize"/> bytes); blob-alignment padding is zero-filled.</summary>
    /// <param name="destination">The buffer to write into; at least <see cref="SerializedSize"/> long.</param>
    /// <exception cref="NotSupportedException">This index is a graph view, or the host is big-endian.</exception>
    internal void WriteTo(Span<byte> destination)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        List<ColumnSlot> slots = CollectSlots();
        int[] offsets = LayOutBlobs(slots, out _);

        int p = 0;
        ContainerMagic.CopyTo(destination[p..]);
        p += ContainerMagic.Length;
        destination[p++] = FormatVersionMajor;
        destination[p++] = FormatVersionMinor;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], KnownRequiredFeatures);
        p += sizeof(ulong);
        destination[p++] = ChecksumAlgorithmNone;

        destination[p++] = (byte)OrderSetMode;
        destination[p++] = (byte)Backing;
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], baseTripleCount);
        p += sizeof(int);

        byte present = 0;
        for(int i = 0; i < orders.Length; i++)
        {
            if(orders[i] is not null)
            {
                present |= (byte)(1 << i);
            }
        }

        destination[p++] = present;
        destination[p++] = 0;

        p += WriteTripleSet(destination[p..], addedSet);
        p += WriteTripleSet(destination[p..], removedSet);

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], slots.Count);
        p += sizeof(int);
        for(int i = 0; i < slots.Count; i++)
        {
            ColumnSlot slot = slots[i];
            destination[p++] = slot.OrderIndex;
            destination[p++] = slot.Level;
            destination[p++] = slot.Role;
            destination[p++] = (byte)slot.Column.Mode;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], (ulong)offsets[i]);
            p += sizeof(ulong);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], (ulong)slot.Column.SerializedSize);
            p += sizeof(ulong);
        }

        //Zero-fill the alignment gaps and write each blob into its slot, so the image carries no
        //stale buffer bytes between the directory and the blobs or between blobs.
        int gapStart = p;
        for(int i = 0; i < slots.Count; i++)
        {
            if(offsets[i] > gapStart)
            {
                destination[gapStart..offsets[i]].Clear();
            }

            int length = slots[i].Column.SerializedSize;
            slots[i].Column.WriteTo(destination.Slice(offsets[i], length));
            gapStart = offsets[i] + length;
        }
    }

    /// <summary>Reconstructs a standalone index from an image written by <see cref="WriteTo"/>, warm — the base columns reload through the per-column codec with no re-pack, and any accumulated delta is re-attached.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="deltaPool">The pool the transient delta triples are rented from while they are re-attached; nothing from it outlives the call.</param>
    /// <param name="backingOverride">Where reloaded payloads live; <see langword="null"/> honors the persisted backing so a native index reloads native.</param>
    /// <param name="backendOption">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="deltaPool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The image is not a Veritas columnar index, or its directory is malformed.</exception>
    /// <exception cref="NotSupportedException">The image's major version or required features are unsupported, the image is a graph view, or the host is big-endian.</exception>
    internal static ColumnarTripleIndex ReadFrom(ReadOnlySpan<byte> source, MemoryPool<EncodedTriple> deltaPool, ColumnPayloadBacking? backingOverride = null, ColumnarKernelBackend? backendOption = null)
    {
        ArgumentNullException.ThrowIfNull(deltaPool);
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < MinimumImageSize)
        {
            throw new InvalidDataException("The bytes are too short to be a Veritas columnar index image.");
        }

        int p = 0;
        if(!source[..ContainerMagic.Length].SequenceEqual(ContainerMagic))
        {
            throw new InvalidDataException("The bytes are not a Veritas columnar index image (magic mismatch).");
        }

        p += ContainerMagic.Length;
        byte versionMajor = source[p++];
        byte versionMinor = source[p++];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Columnar index format version {versionMajor}.{versionMinor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        ulong featureFlags = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);
        if((featureFlags & ~KnownRequiredFeatures) != 0)
        {
            throw new NotSupportedException("The columnar index image requires features this build does not implement.");
        }

        //The checksum-algorithm id is reserved; this increment writes and expects "none".
        p++;

        ColumnarOrderSetMode orderSetMode = (ColumnarOrderSetMode)source[p++];
        ColumnPayloadBacking persistedBacking = (ColumnPayloadBacking)source[p++];
        ColumnPayloadBacking backing = backingOverride ?? persistedBacking;
        int baseTripleCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        byte present = source[p++];
        byte level0BoundsPresent = source[p++];
        if(level0BoundsPresent != 0)
        {
            throw new NotSupportedException("Graph-view index images are not supported by this reader.");
        }

        using IMemoryOwner<EncodedTriple>? addedOwner = ReadTripleSet(source[p..], deltaPool, out int addedCount, out int addedRead);
        p += addedRead;
        using IMemoryOwner<EncodedTriple>? removedOwner = ReadTripleSet(source[p..], deltaPool, out int removedCount, out int removedRead);
        p += removedRead;

        if(source.Length - p < sizeof(int))
        {
            throw new InvalidDataException("The columnar index image is truncated before its directory.");
        }

        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(entryCount < 0 || ((long)entryCount * DirectoryEntrySize) > source.Length - p)
        {
            throw new InvalidDataException("The columnar index directory entry count is beyond the image bounds.");
        }

        ColumnarKernelBackend backend = backendOption ?? ColumnarKernelBackend.Default;
        BlockPackedColumn?[]?[] perOrder = new BlockPackedColumn?[Permutations.Length][];
        for(int e = 0; e < entryCount; e++)
        {
            byte orderIndex = source[p++];
            byte level = source[p++];
            byte role = source[p++];

            //The encoding id mirrors the blob's own leading mode tag; the blob is self-describing,
            //so the directory copy is for routing and is not consulted here.
            p++;
            ulong byteOffset = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
            p += sizeof(ulong);
            ulong byteLength = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
            p += sizeof(ulong);

            if(orderIndex >= Permutations.Length)
            {
                throw new InvalidDataException("The columnar index directory names an out-of-range order.");
            }

            if(role > RoleOffsets || level > 2 || SlotIndex(level, role) >= 5)
            {
                throw new InvalidDataException("The columnar index directory names an invalid column slot.");
            }

            if(byteOffset > (ulong)source.Length || byteLength > (ulong)source.Length - byteOffset)
            {
                throw new InvalidDataException("The columnar index directory names a blob beyond the image bounds.");
            }

            BlockPackedColumn column = BlockPackedColumn.ReadFrom(source.Slice((int)byteOffset, (int)byteLength), backing, backend);
            (perOrder[orderIndex] ??= new BlockPackedColumn?[5])[SlotIndex(level, role)] = column;
        }

        ColumnarOrder?[] orders = new ColumnarOrder?[Permutations.Length];
        for(int i = 0; i < orders.Length; i++)
        {
            if((present & (1 << i)) == 0)
            {
                continue;
            }

            BlockPackedColumn?[] columns = perOrder[i] ?? throw new InvalidDataException("The columnar index is missing the columns of a present order.");
            foreach(BlockPackedColumn? column in columns)
            {
                if(column is null)
                {
                    throw new InvalidDataException("The columnar index is missing a column of a present order.");
                }
            }

            orders[i] = ColumnarOrder.FromColumns(columns[0]!, columns[1]!, columns[2]!, columns[3]!, columns[4]!);
        }

        EncodedTriple[][] emptyRuns = [EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples, EmptyTriples];
        ColumnarTripleIndex baseIndex = new(orders, orderSetMode, baseTripleCount, [], [], emptyRuns, emptyRuns, null, backing);
        if(addedCount == 0 && removedCount == 0)
        {
            return baseIndex;
        }

        IEnumerable<EncodedTriple> additions = addedOwner is null ? [] : MemoryMarshal.ToEnumerable<EncodedTriple>(addedOwner.Memory[..addedCount]);
        IEnumerable<EncodedTriple> removals = removedOwner is null ? [] : MemoryMarshal.ToEnumerable<EncodedTriple>(removedOwner.Memory[..removedCount]);

        return baseIndex.Apply(additions, removals);
    }

    /// <summary>The 64-byte-aligned value at or above <paramref name="value"/>.</summary>
    /// <param name="value">The value to align.</param>
    /// <returns>The aligned value.</returns>
    private static int Align(int value)
    {
        return (value + (BlobAlignment - 1)) & ~(BlobAlignment - 1);
    }

    /// <summary>Maps a (level, role) pair to its fixed slot in a five-column order: value columns at even slots, offset columns at odd.</summary>
    /// <param name="level">The CSR level: 0, 1, or 2.</param>
    /// <param name="role">The column role.</param>
    /// <returns>The slot index in <c>[0, 5)</c>.</returns>
    private static int SlotIndex(byte level, byte role)
    {
        return role == RoleValues ? level * 2 : (level * 2) + 1;
    }

    /// <summary>Writes a delta triple set as a count followed by three little-endian identifiers per triple; returns the bytes written.</summary>
    /// <param name="destination">The buffer to write into.</param>
    /// <param name="set">The triple set.</param>
    /// <returns>The bytes written.</returns>
    private static int WriteTripleSet(Span<byte> destination, HashSet<EncodedTriple> set)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, set.Count);
        int p = sizeof(int);
        foreach(EncodedTriple triple in set)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], triple.Subject.Encoded);
            p += sizeof(uint);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], triple.Predicate.Encoded);
            p += sizeof(uint);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], triple.Object.Encoded);
            p += sizeof(uint);
        }

        return p;
    }

    /// <summary>Reads a delta triple set written by <see cref="WriteTripleSet"/> into a pool-rented buffer; sets <paramref name="count"/> and <paramref name="consumed"/>.</summary>
    /// <param name="source">The byte image positioned at the set.</param>
    /// <param name="pool">The pool the triples are rented from; the caller owns and disposes the returned rental.</param>
    /// <param name="count">Receives the triple count.</param>
    /// <param name="consumed">Receives the bytes consumed.</param>
    /// <returns>The rented buffer holding <paramref name="count"/> triples, or <see langword="null"/> when the set is empty (nothing is rented).</returns>
    /// <exception cref="InvalidDataException">The set is truncated, or declares a count beyond the image bounds.</exception>
    private static IMemoryOwner<EncodedTriple>? ReadTripleSet(ReadOnlySpan<byte> source, MemoryPool<EncodedTriple> pool, out int count, out int consumed)
    {
        if(source.Length < sizeof(int))
        {
            throw new InvalidDataException("The columnar index image is truncated within a delta set.");
        }

        count = BinaryPrimitives.ReadInt32LittleEndian(source);
        if(count < 0 || ((long)count * TripleByteSize) > source.Length - sizeof(int))
        {
            throw new InvalidDataException("The columnar index image declares a delta triple count beyond its bounds.");
        }

        consumed = sizeof(int);
        if(count == 0)
        {
            return null;
        }

        IMemoryOwner<EncodedTriple> owner = pool.Rent(count);
        Span<EncodedTriple> destination = owner.Memory.Span[..count];
        for(int i = 0; i < count; i++)
        {
            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(source[consumed..]);
            consumed += sizeof(uint);
            uint predicate = BinaryPrimitives.ReadUInt32LittleEndian(source[consumed..]);
            consumed += sizeof(uint);
            uint @object = BinaryPrimitives.ReadUInt32LittleEndian(source[consumed..]);
            consumed += sizeof(uint);
            destination[i] = EncodedTriple.FromEncoded(subject, predicate, @object);
        }

        return owner;
    }
}
