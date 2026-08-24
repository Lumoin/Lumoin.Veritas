using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// One graph's part of a dataset transition: which graph changed,
/// the root it moved from and to, and the literal triple delta
/// that effected the move. A <see cref="DatasetJournalEntry"/>
/// carries one transition per touched graph, all committed
/// atomically.
/// </summary>
/// <param name="Graph">The graph the transition applies to: a named graph's term id, or <see cref="TermId.None"/> for the default graph.</param>
/// <param name="ParentRoot">The graph's root before the transition, or <c>null</c> when the transition creates the graph. <see cref="NodeIdentifier.Empty"/> means the graph existed and was empty — existence and emptiness are distinct.</param>
/// <param name="ChildRoot">The graph's root after the transition, or <c>null</c> when the transition drops the graph.</param>
/// <param name="Additions">The triples added to <paramref name="ParentRoot"/> by this transition. Effective — already filtered against the parent state.</param>
/// <param name="Removals">The triples removed from <paramref name="ParentRoot"/> by this transition. Effective — already filtered against the parent state. Empty on a drop: the drop discards the graph wholesale and <paramref name="ParentRoot"/> identifies what was discarded.</param>
/// <remarks>
/// <para>
/// <b>Replay.</b> A transition replays as: create the graph when
/// <see cref="ParentRoot"/> is <c>null</c>; discard it when
/// <see cref="ChildRoot"/> is <c>null</c>; otherwise apply
/// <see cref="Additions"/> and <see cref="Removals"/> to the
/// graph at <see cref="ParentRoot"/> and verify the result's
/// identifier equals <see cref="ChildRoot"/>. The deltas are the
/// NET change across the whole dataset transition — a triple
/// added and then removed inside one session does not appear.
/// </para>
/// <para>
/// <b>Default graph.</b> The default graph is addressed by
/// <see cref="TermId.None"/> and always exists: its transitions
/// never carry a <c>null</c> <see cref="ParentRoot"/> or
/// <see cref="ChildRoot"/>; emptying it transitions to
/// <see cref="NodeIdentifier.Empty"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("DatasetGraphTransition Graph={Graph.Encoded} +{Additions.Length} -{Removals.Length}")]
public readonly record struct DatasetGraphTransition(
    TermId Graph,
    NodeIdentifier? ParentRoot,
    NodeIdentifier? ChildRoot,
    ImmutableArray<EncodedTriple> Additions,
    ImmutableArray<EncodedTriple> Removals);
