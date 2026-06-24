using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Observes a committed default-graph delta on a <see cref="MutableSparqlDataset"/>: the effective additions and
/// removals of one commit and the dataset StateId the commit produced. A replication feed subscribes its advance to
/// this so its reconciliation index tracks the committed default graph by the same delta the query store receives,
/// without the dataset taking a dependency on the replication layer. The signature matches a feed's advance so it
/// binds as a method group, without a closure.
/// </summary>
/// <param name="additions">The triples the commit added to the default graph.</param>
/// <param name="removals">The triples the commit removed from the default graph.</param>
/// <param name="stateId">The dataset StateId the commit produced.</param>
public delegate void DefaultGraphDeltaObserver(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId);
