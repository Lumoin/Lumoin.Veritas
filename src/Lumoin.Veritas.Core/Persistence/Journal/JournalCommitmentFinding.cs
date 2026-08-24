using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// One replay-time disagreement between a journal entry's stored edit-commitment fingerprint and the
/// fingerprint recomputed from the entry's own contents. The record's bytes are checksum-valid — the entry
/// was read back exactly as written — so the disagreement is not at-rest corruption of the record but
/// evidence that the stored fingerprint and the stored edits are inconsistent. It is surfaced to the caller
/// as a finding rather than refused: replay continues, and the caller decides how to treat the entry. Both
/// the per-store and the durable dataset journals collect these.
/// </summary>
/// <param name="SequenceNumber">The sequence number of the entry whose commitment did not verify.</param>
/// <param name="Stored">The edit-commitment fingerprint the entry carried on disk.</param>
/// <param name="Recomputed">The fingerprint recomputed from the entry's parent and edits at replay.</param>
public readonly record struct JournalCommitmentFinding(long SequenceNumber, NodeIdentifier Stored, NodeIdentifier Recomputed);
