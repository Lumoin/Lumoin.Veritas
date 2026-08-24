using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Computes the content-addressed identifiers the dataset journal
/// operates on: the dataset STATE identifier (what the head points
/// at — a fingerprint of every graph's root) and the dataset
/// transition COMMITMENT (what
/// <see cref="DatasetJournalEntry.EditCommitment"/> carries).
/// </summary>
/// <remarks>
/// <para>
/// <b>State identifier.</b> The dataset state is the default
/// graph's root plus the set of (graph, root) pairs of the named
/// graphs. Each graph contributes one hashed record — layout
/// version, graph term id, root identifier — and the
/// contributions XOR-fold into <see cref="NodeIdentifier.Empty"/>
/// via <see cref="NodeIdentifier.Add(ulong)"/>. XOR makes the
/// fold order-independent, so callers never canonicalise graph
/// order; mixing the graph id into each contribution keeps two
/// graphs with identical content from cancelling. The default
/// graph always contributes (under <see cref="TermId.None"/>),
/// so even the empty dataset's identifier differs from
/// <see cref="NodeIdentifier.Empty"/> — the journal's
/// empty-head sentinel never collides with a real state.
/// An EXISTING empty named graph contributes (its root is
/// <see cref="NodeIdentifier.Empty"/>); an absent graph
/// contributes nothing — creating an empty graph changes the
/// state identifier.
/// </para>
/// <para>
/// <b>Transition commitment.</b> Each
/// <see cref="DatasetGraphTransition"/> contributes one hashed
/// record over: layout version, presence flags (created /
/// dropped), graph term id, parent and child roots
/// (<see cref="NodeIdentifier.Empty"/> standing in for an absent
/// side, disambiguated by the flags), and the per-graph edit
/// fingerprint computed by
/// <see cref="EditCommitmentHashing.Compute"/> over the
/// transition's deltas. The contributions XOR-fold into the
/// parent STATE identifier, mirroring the per-store commitment
/// algebra: two entries with equal parent and equal commitment
/// describe the same logical dataset transition, the property
/// idempotent retry detection relies on.
/// </para>
/// <para>
/// <b>Zero-sentinel.</b> Contributions route through
/// <see cref="NodeIdentifier.SanitizeContribution"/>, the one
/// owner of the invariant across the commitment algebra — a
/// contribution with all-zero content bits would XOR-fold as a
/// no-op and silently vanish.
/// </para>
/// </remarks>
public static class DatasetStateHashing
{
    /// <summary>
    /// The byte-layout version written as the first byte of every
    /// hashed record. Bump when the layout downstream of the
    /// version byte changes.
    /// </summary>
    public const byte CurrentLayoutVersion = 0x01;

    //Per-graph state record: 1 version byte + 4 graph-id bytes +
    //8 root-identifier bytes.
    private const int PerGraphStateByteCount = 13;

    //Per-transition record: 1 version byte + 1 flags byte +
    //4 graph-id bytes + 8 parent-root + 8 child-root +
    //8 edit-fingerprint bytes.
    private const int PerTransitionByteCount = 30;

    //Flag bits for the per-transition record's second byte.
    private const byte FlagHasParent = 0b01;

    private const byte FlagHasChild = 0b10;

    /// <summary>
    /// Computes the dataset state identifier for the given graph
    /// roots.
    /// </summary>
    /// <param name="hash">The hash function the application chose at the composition root.</param>
    /// <param name="defaultGraphRoot">The default graph's root identifier; <see cref="NodeIdentifier.Empty"/> when the default graph is empty.</param>
    /// <param name="namedGraphRoots">The named graphs' (graph term id, root identifier) pairs, in any order.</param>
    /// <returns>The dataset state identifier.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public static NodeIdentifier ComputeStateId(
        VeritasHash hash,
        NodeIdentifier defaultGraphRoot,
        IEnumerable<KeyValuePair<TermId, NodeIdentifier>> namedGraphRoots)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(namedGraphRoots);

        NodeIdentifier accumulator = NodeIdentifier.Empty.Add(HashGraphState(hash, TermId.None, defaultGraphRoot));
        foreach((TermId graph, NodeIdentifier root) in namedGraphRoots)
        {
            accumulator = accumulator.Add(HashGraphState(hash, graph, root));
        }

        return accumulator;
    }

    /// <summary>
    /// Computes the commitment fingerprint for a dataset
    /// transition: the parent state identifier XOR-folded with one
    /// contribution per graph transition.
    /// </summary>
    /// <param name="hash">The hash function the application chose at the composition root.</param>
    /// <param name="parentStateId">The dataset state the transitions apply to. <see cref="NodeIdentifier.Empty"/> for an initial build.</param>
    /// <param name="transitions">The per-graph transitions, in any order.</param>
    /// <returns>The transition commitment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    public static NodeIdentifier ComputeCommitment(
        VeritasHash hash,
        NodeIdentifier parentStateId,
        ImmutableArray<DatasetGraphTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(hash);

        NodeIdentifier accumulator = parentStateId;
        foreach(DatasetGraphTransition transition in transitions)
        {
            accumulator = accumulator.Add(HashTransition(hash, transition));
        }

        return accumulator;
    }

    /// <summary>Hashes one graph's (graph, root) state record into a non-zero 64-bit contribution.</summary>
    /// <param name="hash">The hash function.</param>
    /// <param name="graph">The graph term id; <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="root">The graph's root identifier.</param>
    /// <returns>The contribution.</returns>
    private static ulong HashGraphState(VeritasHash hash, TermId graph, NodeIdentifier root)
    {
        Span<byte> buffer = stackalloc byte[PerGraphStateByteCount];
        buffer[0] = CurrentLayoutVersion;

        uint graphId = graph.Encoded;
        ulong rootValue = root.Value;
        MemoryMarshal.Write(buffer[1..], in graphId);
        MemoryMarshal.Write(buffer[5..], in rootValue);

        return NodeIdentifier.SanitizeContribution(hash(buffer));
    }

    /// <summary>Hashes one graph transition into a non-zero 64-bit contribution.</summary>
    /// <param name="hash">The hash function.</param>
    /// <param name="transition">The transition.</param>
    /// <returns>The contribution.</returns>
    private static ulong HashTransition(VeritasHash hash, DatasetGraphTransition transition)
    {
        NodeIdentifier parentRoot = transition.ParentRoot ?? NodeIdentifier.Empty;
        NodeIdentifier editFingerprint = EditCommitmentHashing.Compute(hash, parentRoot, transition.Additions, transition.Removals);

        byte flags = 0;
        if(transition.ParentRoot is not null)
        {
            flags |= FlagHasParent;
        }

        if(transition.ChildRoot is not null)
        {
            flags |= FlagHasChild;
        }

        Span<byte> buffer = stackalloc byte[PerTransitionByteCount];
        buffer[0] = CurrentLayoutVersion;
        buffer[1] = flags;

        uint graphId = transition.Graph.Encoded;
        ulong parentValue = parentRoot.Value;
        ulong childValue = (transition.ChildRoot ?? NodeIdentifier.Empty).Value;
        ulong fingerprintValue = editFingerprint.Value;
        MemoryMarshal.Write(buffer[2..], in graphId);
        MemoryMarshal.Write(buffer[6..], in parentValue);
        MemoryMarshal.Write(buffer[14..], in childValue);
        MemoryMarshal.Write(buffer[22..], in fingerprintValue);

        return NodeIdentifier.SanitizeContribution(hash(buffer));
    }
}
