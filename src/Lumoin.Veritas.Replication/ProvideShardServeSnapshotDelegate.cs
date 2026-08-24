using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Supplies the serving endpoint's CURRENT committed item set as projected reconciliation keys — the operand
/// the shard-difference server filters per requested shard. The host binds it over its live replica (the
/// engine feed's committed index projected under the structural reconciliation projection); each serve reads
/// one fresh snapshot, so a long-lived endpoint always serves its latest committed set. A generation-pinned
/// serve is a recorded follow-up; a peer diverged from the requesting side's damaged generation yields a named
/// decline at the requesting side's gates, never corruption.
/// </summary>
/// <returns>The projected keys of the current committed set; each exactly the reconciliation item width.</returns>
public delegate IReadOnlyList<ReadOnlyMemory<byte>> ProvideShardServeSnapshotDelegate();
