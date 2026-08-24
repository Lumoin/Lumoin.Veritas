using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// The internal representation chosen for an <see cref="EdgeMap"/>.
/// </summary>
/// <remarks>
/// <para>
/// Edge maps adapt their representation to entry count and
/// content: a freshly created edge map is <see cref="Empty"/>;
/// inserted entries store inline in struct fields
/// (<see cref="Inline"/>) up to the inline capacity; the next
/// entry promotes the map to sorted arrays with binary search —
/// <see cref="SortedKeysOnly"/> when every child slot is the
/// absence sentinel (the depth-1 leaf shape, where keys alone
/// carry the data), otherwise <see cref="SortedArray"/> with a
/// parallel child array. Either sorted form scales to whatever
/// cardinality the data requires.
/// </para>
/// <para>
/// <b>Sorted arrays throughout.</b> Sorted arrays are the
/// canonical high-density representation for hypertrie edge maps.
/// They give cache-friendly contiguous storage, naturally support
/// the sorted iteration that worst-case-optimal joins (leapfrog
/// triejoin) require at every variable elimination step, and stay
/// compact at high cardinality. Hash tables would force a sort
/// step on every WCOJ traversal and are not used.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "EdgeMapKind is a discriminator stored alongside other fields inside the EdgeMap value type. Every HypertrieNode carries up to three EdgeMap instances and a populated graph contains millions of nodes, so the per-node footprint is on the hot path. The byte underlying type keeps the discriminator at one byte and lets the struct pack tighter; no scenario benefits from a 32-bit enum here.")]
public enum EdgeMapKind: byte
{
    /// <summary>The edge map has no entries.</summary>
    Empty = 0,

    /// <summary>The edge map's entries are stored inline in the struct's value fields, up to the inline capacity.</summary>
    Inline = 1,

    /// <summary>The edge map's entries are stored in parallel sorted-key and child arrays. Lookup uses binary search. This representation scales to whatever cardinality the data requires.</summary>
    SortedArray = 2,

    /// <summary>The edge map's entries are stored in a sorted-key array alone — every child slot is the absence sentinel, so no child array is allocated. The depth-1 leaf shape, where the keys themselves are the data.</summary>
    SortedKeysOnly = 3,
}
