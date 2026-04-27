using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Computes the content-addressed fingerprint that
/// <see cref="JournalEntry.EditCommitment"/> carries on
/// <see cref="EditSessionEntryKind.Initial"/> and
/// <see cref="EditSessionEntryKind.Committed"/> entries.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it identifies.</b> A commitment is the content-
/// addressed answer to "what edits were applied to which base
/// to produce this snapshot." Two journal entries with equal
/// <see cref="JournalEntry.ParentId"/> and equal commitment
/// describe the same logical state transition, regardless of
/// which session produced them or in what order the caller
/// recorded the edits. This is the property idempotent retry
/// relies on: a session that crashes mid-commit and is
/// replayed against the same base with the same edits produces
/// the same commitment, and the journal can recognise the
/// duplicate.
/// </para>
/// <para>
/// <b>Algebra (fixed).</b> The commitment lives in the
/// <see cref="NodeIdentifier"/> algebra: XOR-fold over per-edit
/// hashes, content-mask applied so the SEN/FN tag bit is never
/// disturbed (handled by <see cref="NodeIdentifier.Add(ulong)"/>).
/// The base seed is the parent snapshot's
/// <see cref="NodeIdentifier.Value"/>; every effective edit
/// folds in via XOR. This algebra is not configurable — it is
/// the structural shape <see cref="JournalEntry.EditCommitment"/>'s
/// consumers depend on.
/// </para>
/// <para>
/// <b>Per-edit byte layout (protocol-pinned).</b> Twenty-five
/// bytes:
/// <list type="bullet">
///   <item><description>One byte for <c>(byte)kind</c> — see <see cref="EditCommitmentKind"/> for the byte values.</description></item>
///   <item><description>Eight bytes for <c>Subject.Encoded</c>, little-endian.</description></item>
///   <item><description>Eight bytes for <c>Predicate.Encoded</c>, little-endian.</description></item>
///   <item><description>Eight bytes for <c>Object.Encoded</c>, little-endian.</description></item>
/// </list>
/// The kind byte is included so
/// <see cref="EditCommitmentKind.Addition"/> and
/// <see cref="EditCommitmentKind.Removal"/> over the same
/// triple produce different per-edit hashes; without it, an
/// add followed by a remove of the same triple would XOR to
/// zero and silently drop from the commitment.
/// </para>
/// <para>
/// <b>Hash function (configurable).</b> The single point of
/// variability — the
/// <see cref="VeritasHash"/> — is taken as a parameter to
/// <see cref="Compute"/>. This is the same delegate type the
/// node-entry mixer uses, so an application picks one hash
/// function at the composition root and threads it through
/// every content-addressing site.
/// </para>
/// <para>
/// <b>Order-independence.</b> XOR is commutative, so callers
/// do not have to canonicalise the order of additions and
/// removals before computing the commitment. Two sessions
/// that produce the same multiset of effective edits get the
/// same commitment regardless of the order in which they
/// recorded them.
/// </para>
/// <para>
/// <b>Empty commit.</b> A commitment computed over no edits
/// returns the base snapshot id unchanged. An <c>Initial</c>
/// entry with an empty input set commits the empty graph;
/// its <see cref="NodeIdentifier.Empty"/> base XOR-folds with
/// no edits to <see cref="NodeIdentifier.Empty"/>, the same
/// value the corresponding empty snapshot's id takes. The
/// degenerate case is consistent with the non-degenerate one.
/// </para>
/// <para>
/// <b>Zero-sentinel.</b> Per-edit hashes are upgraded to
/// <see cref="NodeEntryHashing.ZeroSentinel"/> when the
/// underlying <see cref="VeritasHash"/> returns zero, parallel
/// to <see cref="NodeEntryHashing.Default"/>. Without the
/// upgrade, a zero-hashed edit would be a no-op in the XOR
/// fold.
/// </para>
/// </remarks>
public static class EditCommitmentHashing
{
    //Per-edit byte buffer size: 1 kind byte + three 8-byte
    //little-endian encoded ids. Twenty-five bytes total.
    private const int PerEditByteCount = 25;

    /// <summary>
    /// Computes the commitment for the given base and edits.
    /// </summary>
    /// <param name="hash">The hash function the application chose at the composition root.</param>
    /// <param name="baseSnapshotId">The identifier of the snapshot the edits apply to. <see cref="NodeIdentifier.Empty"/> for an initial build.</param>
    /// <param name="additions">The effective additions (already filtered: triples not present in base).</param>
    /// <param name="removals">The effective removals (already filtered: triples present in base).</param>
    /// <returns>A <see cref="NodeIdentifier"/> fingerprinting the transition <paramref name="baseSnapshotId"/> → resulting snapshot.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public static NodeIdentifier Compute(
        VeritasHash hash,
        NodeIdentifier baseSnapshotId,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        NodeIdentifier accumulator = baseSnapshotId;

        foreach(EncodedTriple triple in additions)
        {
            accumulator = accumulator.Add(HashEdit(hash, EditCommitmentKind.Addition, triple));
        }

        foreach(EncodedTriple triple in removals)
        {
            accumulator = accumulator.Add(HashEdit(hash, EditCommitmentKind.Removal, triple));
        }

        return accumulator;
    }

    //Hashes one (kind, triple) pair into a non-zero 64-bit value.
    //Uses a stack-allocated 25-byte buffer to avoid heap traffic;
    //the buffer's layout is fixed by the per-edit byte
    //representation in the type-level remarks.
    private static ulong HashEdit(VeritasHash hash, EditCommitmentKind kind, EncodedTriple triple)
    {
        Span<byte> buffer = stackalloc byte[PerEditByteCount];
        buffer[0] = (byte)kind;

        long subject = triple.Subject.Encoded;
        long predicate = triple.Predicate.Encoded;
        long @object = triple.Object.Encoded;

        MemoryMarshal.Write(buffer[1..], in subject);
        MemoryMarshal.Write(buffer[9..], in predicate);
        MemoryMarshal.Write(buffer[17..], in @object);

        ulong raw = hash(buffer);

        //A zero hash would XOR-combine into the accumulator as
        //a no-op, making the edit invisible to the commitment.
        //Upgrade to the same non-zero sentinel the node-entry
        //mixer uses so the failure mode is consistent.
        return raw == 0UL ? NodeEntryHashing.ZeroSentinel : raw;
    }
}
