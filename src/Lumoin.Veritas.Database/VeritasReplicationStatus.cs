namespace Lumoin.Veritas.Database;

/// <summary>
/// A mutable database's replication-facing state as one status value: the committed default-graph triple count,
/// the dictionary epoch replicas must share for structural reconciliation, the maintained sketch generation, the
/// dictionary term count — the runtime check of the dictionary-stable active-active posture, since a
/// dictionary-stable write leaves the term count unchanged — and the remove-aware standing: the causality state
/// (which also answers whether a host replica identity was supplied) and the dotted commit ledger's fold
/// generation.
/// </summary>
/// <param name="CommittedTripleCount">The committed default-graph triple count.</param>
/// <param name="DictionaryEpoch">The dictionary epoch this database's structural identifiers are numbered under.</param>
/// <param name="SketchGeneration">The maintained sketch encoder's generation: the count of committed delta batches folded in.</param>
/// <param name="TermCount">The dictionary's term count.</param>
/// <param name="CausalityState">Where the database stands on the remove-aware ladder; <see cref="ReplicationCausalityState.AwaitingBaseline"/> means a host replica identity was supplied but no causality pair exists and the explicit baseline step has not run.</param>
/// <param name="LedgerGeneration">The dotted commit ledger's fold generation: the count of committed default-graph publishes the ledger has folded since open; zero when the database is not remove-aware.</param>
public readonly record struct VeritasReplicationStatus(int CommittedTripleCount, ulong DictionaryEpoch, long SketchGeneration, int TermCount, ReplicationCausalityState CausalityState, long LedgerGeneration);
