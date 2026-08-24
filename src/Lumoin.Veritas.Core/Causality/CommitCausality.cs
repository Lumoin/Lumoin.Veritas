using System;
using System.Collections.Immutable;
using System.IO;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// The causality annotation of one committed dataset transition under the dotted observed-remove regime: the
/// dots assigned to the commit's default-graph additions, the dots its removals drop, and — for a reconcile
/// write-back — the peer context the commit folds. The annotation is explicit and uniform on every annotated
/// entry, so journal replay READS dots rather than re-deriving them; changing how a live commit derives its
/// dots can never corrupt recorded history.
/// </summary>
/// <remarks>
/// <para>
/// An annotation is built against the ledger state its commit's base head names, before the linearising journal
/// append; the append's head compare-and-swap certifies it. A competing commit that publishes first fails the
/// append, and the stale annotation dies with its failed entry — so an annotation that reaches the journal was
/// built against exactly the ledger state its commit extends, structurally.
/// </para>
/// <para>
/// Folding an annotation is idempotent branch by branch: addition dots union into the entry table, drops remove
/// only the named dots, and the context fold is a monotone join. Dots are unique events — a dropped dot is
/// never re-minted — so refolding an already-incorporated annotation, or an ordered prefix of them over a later
/// ledger snapshot, converges to the same state. Recovery leans on exactly this: it folds every annotated entry
/// in journal sequence order over the loaded causality artifact, needing no position bookkeeping.
/// </para>
/// </remarks>
/// <param name="Additions">The commit's default-graph additions, each paired with its assigned dots; empty when the commit adds nothing. An addition already present locally under other dots appears here with the newly adopted dots even though the committed triple set does not change — the dot union is durable knowledge.</param>
/// <param name="Drops">The commit's default-graph removals, each paired with the dots it cancels; empty when the commit drops nothing. A drop that cancels only part of an entry's dots leaves the triple present under the survivors — add-wins over assertion events.</param>
/// <param name="FoldedContext">The peer causal context a reconcile write-back folds, or <see langword="null"/> for a locally-authored commit. The fold is a monotone join over the ledger's own context.</param>
/// <param name="IsBaseline">Whether this annotation IS a baseline: it dots the whole present committed set on a fresh axis and the folded coverage starts from exactly those dots. A store created with a host identity carries a baseline annotation on its Initial entry — the Initial entry is its baseline.</param>
public sealed record CommitCausality(
    ImmutableArray<DottedTripleAssignment> Additions,
    ImmutableArray<DottedTripleAssignment> Drops,
    CausalContext? FoldedContext,
    bool IsBaseline)
{
    /// <summary>The serialized byte size of the annotation under <see cref="WriteTo"/>.</summary>
    /// <returns>The byte size.</returns>
    public int ComputeSerializedSize()
    {
        int size = sizeof(byte) + sizeof(byte) + CausalitySerialization.AssignmentsSize(Additions) + CausalitySerialization.AssignmentsSize(Drops);
        size += FoldedContext?.ComputeSerializedSize() ?? 0;

        return size;
    }

    /// <summary>Writes the annotation into <paramref name="destination"/>: a baseline flag, a context presence flag, the two assignment sections, and the folded context when present, all little-endian.</summary>
    /// <param name="destination">The destination; at least <see cref="ComputeSerializedSize"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public int WriteTo(Span<byte> destination)
    {
        int p = 0;
        destination[p++] = IsBaseline ? (byte)1 : (byte)0;
        destination[p++] = FoldedContext is null ? (byte)0 : (byte)1;
        p += CausalitySerialization.WriteAssignments(destination[p..], Additions);
        p += CausalitySerialization.WriteAssignments(destination[p..], Drops);
        if(FoldedContext is { } context)
        {
            p += context.WriteTo(destination[p..]);
        }

        return p;
    }

    /// <summary>Reads an annotation written by <see cref="WriteTo"/>, advancing <paramref name="position"/> past it.</summary>
    /// <param name="source">The serialized bytes.</param>
    /// <param name="position">The read cursor; advanced past the annotation.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="InvalidDataException">The annotation is truncated or carries an invalid flag, count, or dot layout.</exception>
    public static CommitCausality ReadFrom(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureRemaining(source, position, sizeof(byte) + sizeof(byte));
        byte baselineFlag = source[position++];
        if(baselineFlag > 1)
        {
            throw new InvalidDataException("A commit causality annotation has an invalid baseline flag.");
        }

        byte contextPresence = source[position++];
        if(contextPresence > 1)
        {
            throw new InvalidDataException("A commit causality annotation has an invalid folded-context presence flag.");
        }

        ImmutableArray<DottedTripleAssignment> additions = CausalitySerialization.ReadAssignments(source, ref position);
        ImmutableArray<DottedTripleAssignment> drops = CausalitySerialization.ReadAssignments(source, ref position);
        CausalContext? context = contextPresence == 1 ? CausalContext.ReadFrom(source, ref position) : null;

        return new CommitCausality(additions, drops, context, baselineFlag == 1);
    }

    /// <summary>Throws when fewer than <paramref name="needed"/> bytes remain in <paramref name="source"/> from <paramref name="position"/>.</summary>
    /// <param name="source">The serialized bytes.</param>
    /// <param name="position">The current read position.</param>
    /// <param name="needed">The bytes the next read needs.</param>
    /// <exception cref="InvalidDataException">The annotation is truncated.</exception>
    private static void EnsureRemaining(ReadOnlySpan<byte> source, int position, int needed)
    {
        if(source.Length - position < needed)
        {
            throw new InvalidDataException("A commit causality annotation is truncated.");
        }
    }
}
