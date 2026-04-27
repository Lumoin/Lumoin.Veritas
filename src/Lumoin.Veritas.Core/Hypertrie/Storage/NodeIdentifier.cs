using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A 64-bit content-addressed identifier for a
/// <see cref="HypertrieNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// A node's identifier is the XOR of a per-entry hash over every
/// entry the node carries. XOR is associative and self-inverse, so
/// adding an entry and removing the same entry are the same
/// operation:
/// <see cref="Add(ulong)"/> and <see cref="Remove(ulong)"/> are
/// aliases. Two nodes that hold the same multiset of entries get
/// the same identifier regardless of insertion order. This is the
/// fingerprint dedup uses to find candidates; it is not a
/// uniqueness guarantee on its own — two nodes with different
/// content can collide on identifier with probability roughly
/// <c>2^-64</c>, and the node store handles that with explicit
/// content-equality verification.
/// </para>
/// <para>
/// <b>Bit 63 — reserved tag.</b> The high bit is reserved as a
/// Single-Entry-Node / Full-Node discriminator (1 = SEN, 0 = FN).
/// SEN compression — storing depth-1 boolean leaves' single
/// key-part inside the identifier itself, with no separate node
/// object — is a later batch and is not yet implemented. Until
/// then the bit is always zero and <see cref="IsSingleEntryNode"/>
/// is always <c>false</c>. The XOR combiner respects the tag bit:
/// per-entry hashes are masked to 63 bits before combining so the
/// tag is not stomped by content.
/// </para>
/// <para>
/// <b>Empty node convention.</b>
/// <see cref="Empty"/> has the raw value <c>0</c>. A freshly-built
/// node with no entries is empty; XOR-combining an entry's hash in
/// and then back out returns the identifier to <see cref="Empty"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("NodeIdentifier({Value:X16})")]
public readonly record struct NodeIdentifier(ulong Value)
{
    /// <summary>The bit mask covering the content portion of the identifier (bits 0..62).</summary>
    public const ulong ContentMask = 0x7FFFFFFFFFFFFFFFUL;

    /// <summary>The bit mask covering the SEN/FN tag (bit 63).</summary>
    public const ulong TagMask = 0x8000000000000000UL;

    /// <summary>The identifier of an empty node — no entries, FN tag.</summary>
    public static NodeIdentifier Empty { get; } = new(0UL);

    /// <summary>Returns <c>true</c> when the identifier is <see cref="Empty"/>.</summary>
    public bool IsEmpty => Value == 0UL;

    /// <summary>
    /// Returns <c>true</c> when bit 63 is set (Single-Entry Node).
    /// The SEN representation is reserved for a later batch; until
    /// then this is always <c>false</c>.
    /// </summary>
    public bool IsSingleEntryNode => (Value & TagMask) != 0UL;

    /// <summary>Returns <c>true</c> when bit 63 is clear (Full Node, the current default).</summary>
    public bool IsFullNode => (Value & TagMask) == 0UL;

    /// <summary>The 63-bit content portion of the identifier, with the tag stripped.</summary>
    public ulong Content => Value & ContentMask;

    /// <summary>
    /// Folds <paramref name="entryHash"/> into the identifier. Adding
    /// an entry hash and later removing the same entry hash returns
    /// the identifier to its prior state. The hash is masked to 63
    /// bits so the SEN/FN tag is never disturbed by content.
    /// </summary>
    public NodeIdentifier Add(ulong entryHash) => new(Value ^ (entryHash & ContentMask));

    /// <summary>
    /// Removes an entry hash that was previously added. XOR is
    /// self-inverse, so this is identical to <see cref="Add(ulong)"/>.
    /// </summary>
    public NodeIdentifier Remove(ulong entryHash) => Add(entryHash);

    /// <summary>
    /// Returns a copy of this identifier with the SEN/FN tag bit
    /// set to the value of <paramref name="isSingleEntryNode"/>.
    /// </summary>
    public NodeIdentifier WithTag(bool isSingleEntryNode)
    {
        ulong content = Value & ContentMask;
        ulong tag = isSingleEntryNode ? TagMask : 0UL;
        return new NodeIdentifier(content | tag);
    }
}
