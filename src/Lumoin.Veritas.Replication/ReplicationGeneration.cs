using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A point-in-time snapshot of a replica's reconciliation view: the columnar index of committed default-graph
/// triples and the dataset StateId it reflects. The pair is the generation a sketch is served over and tagged with,
/// so a peer can label which generation it reconciled against. The index is immutable, so a generation taken at one
/// connection is unaffected by later commits.
/// </summary>
/// <param name="Index">The reconciliation index at this generation.</param>
/// <param name="StateId">The dataset StateId the index reflects — the generation tag a durable sketch is keyed by and a peer labels its convergence against. Carried by the feed now; consumed once sketch-generation persistence and generation-pinned fetch land.</param>
public readonly record struct ReplicationGeneration(ColumnarTripleIndex Index, NodeIdentifier StateId);
