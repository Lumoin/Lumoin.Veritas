using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// An incremental XOR set hash over a dense integer domain — a Zobrist hash: the
/// O(1)-updatable state key the reasoner's double-blocking comparison buckets nodes by.
/// Each element <c>i</c> of the domain is assigned a pinned 64-bit value <c>R[i]</c>; the
/// hash of a set is the exclusive-or of <c>R[i]</c> over its members. Because exclusive-or
/// is associative, commutative, and self-inverse, toggling one element's membership updates
/// the hash in constant time (<see cref="Toggle"/>) — a node label hash stays current as the
/// label grows during tableau expansion, with no rescan of the set.
/// </summary>
/// <remarks>
/// <para>
/// <b>Collisions are silent.</b> Equal sets always hash equal, but distinct sets may share a
/// hash. Given table entries that are mutually independent and uniform — as the production
/// entropy source supplies — the exclusive-or over any non-empty symmetric difference is itself
/// uniform, so two distinct sets collide with probability exactly 2⁻⁶⁴ (mutual independence is
/// required, not merely pairwise: the symmetric difference can hold three or more elements, whose
/// exclusive-or is uniform only under full independence). Even so, a hash match is never a proof
/// of set equality: a consumer bucketing nodes by hash must confirm a match with an exact
/// <see cref="BitsetOps.SetEquals"/> before treating two nodes as label-equal. The hash
/// narrows the candidate set; the exact check decides.
/// </para>
/// <para>
/// <b>Reproducible seeding.</b> The pinned table is drawn once from the injected
/// <see cref="RandomnessDelegate"/> as a byte stream read little-endian, so a seeded
/// (deterministic) source yields a byte-identical table on every platform — x64 and
/// WebAssembly agree. The production source supplies real entropy; tests pin a seed.
/// </para>
/// <para>
/// <b>Domain contract.</b> A hashed bitset must be a set over this domain: its set bits all
/// lie in <c>[0, DomainSize)</c> (the <see cref="BitsetOps"/> tail invariant guarantees this
/// for a correctly built bitset), and a toggled <paramref name="id"/> lies in the same range.
/// An element outside the domain indexes past the table.
/// </para>
/// <para>
/// <b>Layout.</b> The pinned values are a single flat <see cref="ulong"/> array indexed directly
/// by element id. There is no product structure to fold into a multi-dimensional table — unlike a
/// board-game Zobrist table keyed by (square, piece) — because the domain is already the dense
/// interned id space, so id maps straight to a value. <see cref="Toggle"/> is then one array load
/// and one exclusive-or, and <see cref="Hash"/> is a gather over the present elements' ids, which a
/// flat dense array serves as well as any layout can: the access pattern is intrinsically scattered
/// by which elements are in the set, so no reordering improves it.
/// </para>
/// <para>
/// <b>Scale and when a different strategy is needed.</b> The table occupies
/// <c>DomainSize · 8</c> bytes, resident for the instance's lifetime. The intended domain — the
/// reasoner's interned concept universe — is at most a few thousand elements (tens of kilobytes),
/// so the table stays resident in a low-level cache and the direct load is the fastest option, with
/// the <see cref="Hash"/> gather touching only the label's few present elements. The explicit table
/// stops being the right choice when the domain grows orders of magnitude larger and is touched
/// sparsely — roughly past the point it no longer fits a low-level cache, on the order of 10⁵–10⁶
/// elements (about a megabyte and up), which would arise from interning a far larger universe than
/// the concept set (every individual or term of a city-scale graph) rather than from the reasoning
/// domain itself. At that scale a table-free scheme — deriving each element's value on demand from a
/// seeded hash of its id instead of storing it — trades the per-element memory for a little
/// recomputation; a tabulation hash with k-partition concentration (mixed tabulation, Dahlgaard et
/// al. 2015) is the principled on-demand choice when the same values must also feed a distinct-count
/// estimator. This type deliberately stores the table because its consumer's domain sits well inside
/// the cache-resident regime; a large-sparse domain is a new consumer that should select the
/// on-demand variant behind the same hashing contract.
/// </para>
/// </remarks>
public sealed class ZobristSetHash
{
    /// <summary>The shift mapping a bit index to its word index.</summary>
    private const int WordShift = 6;

    /// <summary>
    /// The pinned per-element values: element <c>i</c> contributes <c>Table[i]</c> to the hash
    /// of any set containing it. Durable index state, sized to the domain and never mutated
    /// after construction.
    /// </summary>
    private ulong[] Table { get; }

    /// <summary>The number of elements the domain spans; a valid element id lies in <c>[0, DomainSize)</c>.</summary>
    public int DomainSize { get; }

    /// <summary>
    /// The call-site salt identifying this component to a seeded randomness source, so a
    /// seed shared with other consumers still produces an independent table here.
    /// </summary>
    private static ReadOnlyMemory<byte> Salt { get; } = "Lumoin.Veritas.Core.Collections.ZobristSetHash"u8.ToArray();

    /// <summary>
    /// Builds the hash table for a domain of <paramref name="domainSize"/> elements, drawing
    /// each element's pinned value from <paramref name="randomness"/>.
    /// </summary>
    /// <param name="domainSize">The number of elements the domain spans; must be non-negative.</param>
    /// <param name="randomness">The injected randomness source the pinned table is drawn from; a seeded source makes the table reproducible.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="domainSize"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="randomness"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The randomness source returned fewer bytes than the table needs.</exception>
    public ZobristSetHash(int domainSize, RandomnessDelegate randomness)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(domainSize);
        ArgumentNullException.ThrowIfNull(randomness);

        DomainSize = domainSize;
        Table = new ulong[domainSize];

        if(domainSize == 0)
        {
            return;
        }

        int byteCount = domainSize * sizeof(ulong);
        RandomnessRequest request = new(RandomnessKind.Bytes, Guid.Empty, byteCount, Salt);
        RandomnessValue value = randomness(in request);
        ReadOnlySpan<byte> bytes = value.Bytes.Span;
        if(bytes.Length < byteCount)
        {
            throw new InvalidOperationException($"The randomness source returned {bytes.Length} bytes for a {domainSize}-element Zobrist table needing {byteCount}.");
        }

        for(int i = 0; i < domainSize; i++)
        {
            Table[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i * sizeof(ulong), sizeof(ulong)));
        }
    }

    /// <summary>
    /// The hash of the set the <paramref name="words"/> encode, computed from scratch: the
    /// exclusive-or of every member's pinned value. Equal sets hash equal; a hash match
    /// between distinct sets is possible and must be confirmed exactly by the consumer.
    /// </summary>
    /// <param name="words">The bitset words, a set over this domain (tail-clean, set bits below <see cref="DomainSize"/>).</param>
    /// <returns>The set's hash.</returns>
    public ulong Hash(ReadOnlySpan<ulong> words)
    {
        ulong hash = 0UL;
        for(int wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            ulong word = words[wordIndex];
            int baseId = wordIndex << WordShift;
            while(word != 0UL)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                hash ^= Table[baseId + bit];
                word &= word - 1UL;
            }
        }

        return hash;
    }

    /// <summary>
    /// The hash of the set obtained by toggling element <paramref name="id"/> in a set whose
    /// current hash is <paramref name="hash"/>: it adds the element when absent and removes it
    /// when present (exclusive-or is its own inverse), in constant time.
    /// </summary>
    /// <param name="hash">The current set's hash.</param>
    /// <param name="id">The element to toggle; must lie in <c>[0, DomainSize)</c>.</param>
    /// <returns>The hash of the set with <paramref name="id"/>'s membership flipped.</returns>
    public ulong Toggle(ulong hash, int id)
    {
        return hash ^ Table[id];
    }

    /// <summary>The pinned value of element <paramref name="id"/> — the contribution it makes to any set containing it.</summary>
    /// <param name="id">The element; must lie in <c>[0, DomainSize)</c>.</param>
    /// <returns>The element's pinned 64-bit value.</returns>
    public ulong ValueOf(int id)
    {
        return Table[id];
    }

    /// <summary>The number of words a bitset over this domain occupies.</summary>
    /// <returns>The word count for <see cref="DomainSize"/> bits.</returns>
    public int WordCount()
    {
        return BitsetOps.WordCount(DomainSize);
    }
}
