namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Discriminates additions from removals when computing per-edit
/// hashes for an <see cref="JournalEntry.EditCommitment"/>. The
/// underlying byte values are protocol-pinned: the framework's
/// XOR-fold combiner relies on adds and removes producing
/// distinct per-edit hashes, and the pinning ensures that two
/// implementations of <see cref="VeritasHash"/> agree on what
/// the framework passes for each edit kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate enum from <see cref="EditKind"/>.</b>
/// <see cref="EditKind"/> is the buffer's edit-intent enum;
/// reordering its members or inserting new ones would shift the
/// underlying integer values and silently change every
/// commitment hash a build produces. Carrying the protocol-
/// pinning on a dedicated type isolates the wire format from the
/// in-memory edit-buffer abstraction so neither can drift the
/// other.
/// </para>
/// <para>
/// <b>Why these specific byte values.</b>
/// <see cref="Addition"/> uses <c>0x00</c> and <see cref="Removal"/>
/// uses <c>0x01</c> — the bytes that go into the per-edit mixer's
/// input buffer in <see cref="EditCommitmentHashing.Default"/>.
/// Custom mixers may interpret the kind differently (for example,
/// pattern-matching on the enum members rather than reading the
/// byte) but the framework will always pass these specific values.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "The underlying type is the wire format of this enum, not an implementation detail. The values are written verbatim as the first byte of the per-edit hash input buffer in EditCommitmentHashing.Default. Widening to Int32 would either require a re-cast at every write site or change the protocol byte layout, both worse than the rule's recommendation. CA1028 explicitly notes the byte choice is appropriate when the storage size is a contractual concern, which it is here.")]
public enum EditCommitmentKind: byte
{
    /// <summary>An edit that adds the triple to the graph. Protocol byte <c>0x00</c>.</summary>
    Addition = 0x00,

    /// <summary>An edit that removes the triple from the graph. Protocol byte <c>0x01</c>.</summary>
    Removal = 0x01
}
