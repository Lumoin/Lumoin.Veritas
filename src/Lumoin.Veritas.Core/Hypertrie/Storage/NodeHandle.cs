using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A 32-bit child-slot reference inside an
/// <see cref="EdgeMap"/>. Carries either a Full-Node arena index
/// addressing a real <see cref="HypertrieNode"/> in
/// <see cref="NodeStore"/>, or a Single-Entry-Node encoding where
/// the slot itself carries the lone leaf key with no separate node
/// allocation, or the absence sentinel
/// <see cref="None"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Encoding.</b> Bit 31 is the SEN / FN discriminator:
/// <c>1</c> means Single-Entry-Node (the slot's low 31 bits encode
/// the single leaf key directly), <c>0</c> means the slot is not a
/// leaf key — bit 30 then splits that space: <c>1</c> means
/// Single-Entry-Pair (the low 30 bits index a whole single-entry
/// depth-2 subtree in <see cref="NodeStore"/>'s pair arena),
/// <c>0</c> means Full-Node (the low 30 bits are an arena index
/// resolved through
/// <see cref="NodeStore.GetByHandle(NodeHandle)"/>). The
/// all-zeros encoding represents <see cref="None"/>; FN with
/// arena index <c>0</c> and SEN with key <c>0</c> are reserved and
/// must never be constructed, while pair index <c>0</c> is a valid
/// pair-arena slot (the SEN2 tag bit keeps its encoding non-zero).
/// </para>
/// <para>
/// <b>Range.</b> Both arena indices and SEN keys are constrained to
/// the low 31 bits — up to <c>0x7FFFFFFF</c> ≈ 2.1 billion. Arena
/// slot counts in real workloads never approach this limit. SEN
/// keys are encoded <c>TermId</c> values; the
/// <c>Lumoin.Veritas.Core.Encoding.TermDictionary</c> assigns
/// sequential ids starting at <c>1</c>, so the 31-bit limit is
/// equivalent to "no graph carries more than 2.1 billion distinct
/// terms" — comfortably above realistic city-scale corpus sizes.
/// </para>
/// <para>
/// <b>Sentinel.</b> <see cref="None"/> uses the encoded value <c>0</c>.
/// <c>default(NodeHandle)</c> equals <see cref="None"/>; uninitialised
/// handle slots in freshly-allocated arrays are safely absent by
/// construction. <see cref="NodeStore"/> assigns external arena
/// handles starting at <c>1</c>; <c>0</c> is reserved for this
/// sentinel.
/// </para>
/// <para>
/// <b>Conversion semantics.</b> Conversions in both directions are
/// deliberate; there are no implicit operators. A raw
/// <see cref="uint"/> becomes a <see cref="NodeHandle"/> only via
/// <see cref="FromEncoded(uint)"/> or the explicit constructor;
/// the encoded value is read via <see cref="Encoded"/>. SEN-tagged
/// handles are built via <see cref="ForSingleEntry(uint)"/>.
/// </para>
/// <para>
/// <b>Depth-1 EdgeMap child slots.</b> At depth-1 leaves stored as
/// real FN nodes, the EdgeMap's child slot is structurally present
/// but semantically unused — the parent's depth alone determines
/// that triples are emitted directly from the edge map's keys,
/// without descending. Depth-1 slots therefore hold
/// <see cref="None"/>. At depth-2 and depth-3, slots hold either an
/// FN handle pointing to the arena or an SEN handle carrying the
/// leaf's single key inline.
/// </para>
/// </remarks>
/// <param name="Encoded">The raw encoded handle.</param>
[DebuggerDisplay("NodeHandle({Encoded,h})")]
public readonly record struct NodeHandle(uint Encoded): IComparable<NodeHandle>, IComparable
{
    /// <summary>The bit mask covering the SEN/FN tag (bit 31).</summary>
    public const uint SenTag = 0x80000000U;

    /// <summary>The bit mask covering the payload (bits 0..30) — either arena index or SEN-encoded key.</summary>
    public const uint ContentMask = 0x7FFFFFFFU;

    /// <summary>The bit mask covering the single-entry-pair tag (bit 30), meaningful only when bit 31 is clear.</summary>
    public const uint Sen2Tag = 0x40000000U;

    /// <summary>The bit mask covering the pair-arena index payload (bits 0..29) of a single-entry-pair handle.</summary>
    public const uint Sen2ContentMask = 0x3FFFFFFFU;

    /// <summary>
    /// A sentinel representing the absence of a node, encoded as <c>0</c>.
    /// Equal to <c>default(NodeHandle)</c>.
    /// </summary>
    public static NodeHandle None { get; } = new(0);

    /// <summary>
    /// Creates a <see cref="NodeHandle"/> from a raw encoded value.
    /// </summary>
    /// <param name="encoded">The raw encoded handle.</param>
    /// <returns>The wrapped handle.</returns>
    public static NodeHandle FromEncoded(uint encoded) => new(encoded);

    /// <summary>
    /// Creates a Single-Entry-Node handle carrying
    /// <paramref name="key"/> directly in the slot. No arena
    /// allocation occurs; the parent's edge map stores this handle,
    /// and descent code reads the key from
    /// <see cref="SingleEntryKey"/> without consulting
    /// <see cref="NodeStore"/>.
    /// </summary>
    /// <param name="key">The single leaf key to encode. Must fit in 31 bits.</param>
    /// <returns>An SEN-tagged handle whose <see cref="SingleEntryKey"/> equals <paramref name="key"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> exceeds 31 bits.</exception>
    public static NodeHandle ForSingleEntry(uint key)
    {
        if((key & SenTag) != 0U)
        {
            throw new ArgumentOutOfRangeException(nameof(key), key,
                $"SEN key must fit in 31 bits (max 0x{ContentMask:X}); got 0x{key:X}.");
        }

        return new NodeHandle(key | SenTag);
    }

    /// <summary>
    /// Creates a single-entry-pair handle addressing the pair at
    /// <paramref name="pairIndex"/> in the owning store's pair
    /// arena. The pair carries a whole single-entry depth-2 subtree
    /// — its two remaining-position keys in ascending original-
    /// position order — without allocating a node or an intern
    /// entry.
    /// </summary>
    /// <param name="pairIndex">The pair-arena index. Must fit in 30 bits.</param>
    /// <returns>An SEN2-tagged handle whose <see cref="PairIndex"/> equals <paramref name="pairIndex"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pairIndex"/> exceeds 30 bits.</exception>
    public static NodeHandle ForSingleEntryPair(uint pairIndex)
    {
        if((pairIndex & ~Sen2ContentMask) != 0U)
        {
            throw new ArgumentOutOfRangeException(nameof(pairIndex), pairIndex,
                $"Pair-arena index must fit in 30 bits (max 0x{Sen2ContentMask:X}); got 0x{pairIndex:X}.");
        }

        return new NodeHandle(pairIndex | Sen2Tag);
    }

    /// <summary>
    /// Returns <c>true</c> when this handle is the <see cref="None"/> sentinel.
    /// </summary>
    public bool IsNone => Encoded == 0U;

    /// <summary>
    /// Returns <c>true</c> when bit 31 is set, indicating this slot
    /// carries a Single-Entry-Node encoded key rather than an arena
    /// index.
    /// </summary>
    public bool IsSingleEntry => (Encoded & SenTag) != 0U;

    /// <summary>
    /// Returns <c>true</c> when bit 31 is clear and bit 30 is set,
    /// indicating this slot addresses a single-entry depth-2 pair
    /// in the owning store's pair arena.
    /// </summary>
    public bool IsSingleEntryPair => (Encoded & SenTag) == 0U && (Encoded & Sen2Tag) != 0U;

    /// <summary>
    /// Returns <c>true</c> when this handle addresses a real
    /// <see cref="HypertrieNode"/> in the arena — neither
    /// <see cref="None"/>, <see cref="IsSingleEntry"/>, nor
    /// <see cref="IsSingleEntryPair"/>.
    /// </summary>
    public bool IsArenaHandle => Encoded != 0U && (Encoded & (SenTag | Sen2Tag)) == 0U;

    /// <summary>
    /// The arena index portion of this handle. Meaningful only when
    /// <see cref="IsArenaHandle"/> is <c>true</c>.
    /// </summary>
    public uint ArenaIndex => Encoded & ContentMask;

    /// <summary>
    /// The encoded SEN key carried by this handle. Meaningful only
    /// when <see cref="IsSingleEntry"/> is <c>true</c>.
    /// </summary>
    public uint SingleEntryKey => Encoded & ContentMask;

    /// <summary>
    /// The pair-arena index carried by this handle. Meaningful only
    /// when <see cref="IsSingleEntryPair"/> is <c>true</c>.
    /// </summary>
    public uint PairIndex => Encoded & Sen2ContentMask;

    /// <summary>Orders handles by their encoded value.</summary>
    public int CompareTo(NodeHandle other) => Encoded.CompareTo(other.Encoded);

    /// <inheritdoc/>
    public int CompareTo(object? obj)
    {
        if(obj is null)
        {
            return 1;
        }

        if(obj is NodeHandle other)
        {
            return CompareTo(other);
        }

        throw new ArgumentException($"Object must be of type {nameof(NodeHandle)}.", nameof(obj));
    }

    /// <summary>Returns <c>true</c> if <paramref name="left"/> precedes <paramref name="right"/>.</summary>
    public static bool operator <(NodeHandle left, NodeHandle right) => left.Encoded < right.Encoded;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> precedes or equals <paramref name="right"/>.</summary>
    public static bool operator <=(NodeHandle left, NodeHandle right) => left.Encoded <= right.Encoded;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> follows <paramref name="right"/>.</summary>
    public static bool operator >(NodeHandle left, NodeHandle right) => left.Encoded > right.Encoded;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> follows or equals <paramref name="right"/>.</summary>
    public static bool operator >=(NodeHandle left, NodeHandle right) => left.Encoded >= right.Encoded;

    /// <inheritdoc/>
    public override string ToString() => Encoded.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
