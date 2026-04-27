using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// The internal representation chosen for an <see cref="EdgeMap"/>.
/// </summary>
/// <remarks>
/// <para>
/// Edge maps adapt their representation to entry count: a freshly
/// created edge map is <see cref="Empty"/>; the first inserted entry
/// promotes it to <see cref="Inline"/> (one pair stored in struct
/// fields, no heap allocation); a second distinct entry promotes it
/// to <see cref="SortedArray"/> (parallel key/child arrays with
/// binary search), which then scales to whatever cardinality the
/// data requires.
/// </para>
/// <para>
/// <b>SortedArray throughout.</b> Sorted parallel arrays are the
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

    /// <summary>The edge map has exactly one entry, stored inline in the struct's value fields.</summary>
    Inline = 1,

    /// <summary>The edge map has two or more entries, stored in parallel sorted-key and child arrays. Lookup uses binary search. This representation scales to whatever cardinality the data requires.</summary>
    SortedArray = 2,
}
