using System.Collections.Immutable;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Journal;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The provenance of a mutable database reopened over a durable dataset journal
/// (<see cref="VeritasEngine.OpenMutableAsync(Lumoin.Veritas.Core.Persistence.PersistenceStore, VeritasEngineOptions?, System.Threading.CancellationToken)"/>):
/// how much acked history the reopen replayed and any damage the journal recovery named. A host reads this to
/// tell an exact recovery apart from one that discarded a torn tail or read back inconsistent commitment
/// fingerprints. It is <see langword="null"/> on <see cref="VeritasEngine.DatasetJournalRecovery"/> when no
/// durable dataset journal was wired.
/// </summary>
/// <param name="EntriesReplayed">The number of committed dataset transitions the reopen folded forward from the recovered generation (or from empty when none was persisted); <c>0</c> when the journal head already named the recovered generation's state.</param>
/// <param name="TornTailLoss">The torn-or-corrupt tail the durable journal truncated on replay, an <see cref="UnrecoverableItemReportKind.OperationRange"/> report naming the discarded suffix, or <see langword="null"/> when the log replayed intact.</param>
/// <param name="CommitmentFindings">The per-entry disagreements between a journal entry's stored edit-commitment fingerprint and a recomputation over its own contents; empty when every entry verified.</param>
public sealed record DatasetJournalRecoveryProvenance(
    long EntriesReplayed,
    UnrecoverableItemReport? TornTailLoss,
    ImmutableArray<JournalCommitmentFinding> CommitmentFindings);
