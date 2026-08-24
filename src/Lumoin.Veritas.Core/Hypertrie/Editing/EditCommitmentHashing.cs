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
/// <b>Per-edit byte layout (protocol-pinned).</b> Fourteen bytes,
/// prefixed by a layout-version byte so a future migration can
/// distinguish commitments across format versions:
/// <list type="bullet">
///   <item><description>One byte for <see cref="CurrentLayoutVersion"/> (<c>0x01</c> for this layout).</description></item>
///   <item><description>One byte for <c>(byte)kind</c> — see <see cref="EditCommitmentKind"/> for the byte values.</description></item>
///   <item><description>Four bytes for <c>Subject.Encoded</c>, little-endian.</description></item>
///   <item><description>Four bytes for <c>Predicate.Encoded</c>, little-endian.</description></item>
///   <item><description>Four bytes for <c>Object.Encoded</c>, little-endian.</description></item>
/// </list>
/// The version byte being part of the hashed buffer guarantees
/// that commitments produced under two different layouts are
/// cryptographically distinguishable even if the rest of the
/// bytes were identical between the two. The kind byte is included so
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
/// <b>Zero-sentinel.</b> Per-edit hashes route through
/// <see cref="NodeIdentifier.SanitizeContribution"/>, the one
/// owner of the no-invisible-contribution invariant across
/// every fold site. Without the upgrade, an edit whose hash
/// has all-zero content bits would be a no-op in the XOR fold.
/// </para>
/// </remarks>
public static class EditCommitmentHashing
{
    /// <summary>
    /// The current per-edit byte-layout version written as the
    /// first byte of every hashed edit buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version byte is a hedge against future layout changes:
    /// pre-pending it to the hash input guarantees that
    /// commitments produced under different layouts cannot
    /// collide, and gives a migration tool a discriminator to
    /// dispatch on when reading historical journals. Bump this
    /// constant whenever the per-edit byte layout downstream of
    /// the version byte changes.
    /// </para>
    /// </remarks>
    public const byte CurrentLayoutVersion = 0x01;

    //Per-edit byte buffer size: 1 layout-version byte + 1 kind
    //byte + three 4-byte little-endian encoded ids. Fourteen
    //bytes total.
    private const int PerEditByteCount = 14;

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
    //Uses a stack-allocated 14-byte buffer to avoid heap traffic;
    //the buffer's layout is fixed by the per-edit byte
    //representation in the type-level remarks.
    private static ulong HashEdit(VeritasHash hash, EditCommitmentKind kind, EncodedTriple triple)
    {
        Span<byte> buffer = stackalloc byte[PerEditByteCount];
        buffer[0] = CurrentLayoutVersion;
        buffer[1] = (byte)kind;

        uint subject = triple.Subject.Encoded;
        uint predicate = triple.Predicate.Encoded;
        uint @object = triple.Object.Encoded;

        MemoryMarshal.Write(buffer[2..], in subject);
        MemoryMarshal.Write(buffer[6..], in predicate);
        MemoryMarshal.Write(buffer[10..], in @object);

        //A hash with all-zero content bits would XOR-combine into
        //the accumulator as a no-op, making the edit invisible to
        //the commitment; the shared sanitizer owns that invariant.
        return NodeIdentifier.SanitizeContribution(hash(buffer));
    }
}
