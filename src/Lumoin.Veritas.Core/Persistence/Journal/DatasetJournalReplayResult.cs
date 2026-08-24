using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// The outcome of folding a durable dataset journal onto a recovered generation
/// (<see cref="DatasetJournalRecovery.ReplayAsync"/>): the per-graph content the caller resumes the dataset
/// over, the graphs the log dropped, and the anchor and head the fold ran between. The final per-graph content
/// is the recovery's proposal; the content-addressed head check the caller performs on the rebuilt state is the
/// integrity oracle that confirms it.
/// </summary>
/// <param name="Outcome">Whether the log was replayed, was already up to date at the anchor, or diverged from it.</param>
/// <param name="TouchedGraphs">The final triple content of every graph the replayed suffix materialised and left surviving, keyed by graph-name term id; the default graph appears under <see cref="TermId.None"/> when it was touched. Empty on <see cref="DatasetJournalReplayOutcome.Diverged"/> and <see cref="DatasetJournalReplayOutcome.UpToDate"/>.</param>
/// <param name="DroppedGraphs">The graph-name term ids the replayed suffix dropped and did not re-create — graphs the caller removes from the loaded base.</param>
/// <param name="EntriesReplayed">The number of content-bearing (<see cref="Lumoin.Veritas.Core.Hypertrie.Editing.EditSessionEntryKind.Initial"/> or <see cref="Lumoin.Veritas.Core.Hypertrie.Editing.EditSessionEntryKind.Committed"/>) entries folded after the anchor; <c>0</c> exactly when the outcome is <see cref="DatasetJournalReplayOutcome.UpToDate"/> or <see cref="DatasetJournalReplayOutcome.Diverged"/>.</param>
/// <param name="Anchor">The generation anchor the replay resumed after, or <see cref="NodeIdentifier.Empty"/> when the whole self-contained log was replayed from empty.</param>
/// <param name="Head">The journal head the replayed content is expected to reconstruct.</param>
public sealed record DatasetJournalReplayResult(
    DatasetJournalReplayOutcome Outcome,
    IReadOnlyDictionary<TermId, HashSet<EncodedTriple>> TouchedGraphs,
    IReadOnlyCollection<TermId> DroppedGraphs,
    long EntriesReplayed,
    NodeIdentifier Anchor,
    NodeIdentifier Head);
