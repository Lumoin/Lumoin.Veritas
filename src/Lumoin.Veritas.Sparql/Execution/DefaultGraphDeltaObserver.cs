using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Observes a committed default-graph delta on a <see cref="MutableSparqlDataset"/>: the effective additions and
/// removals of one commit, the dataset StateId the commit produced, and the commit's causality annotation. A
/// replication feed subscribes its advance to this so its reconciliation index tracks the committed default graph
/// by the same delta the query store receives, without the dataset taking a dependency on the replication layer;
/// a dotted commit ledger folds the annotation through the same seam. The signature matches a subscriber's
/// advance so it binds as a method group, without a closure.
/// </summary>
/// <param name="additions">The triples the commit added to the default graph.</param>
/// <param name="removals">The triples the commit removed from the default graph.</param>
/// <param name="stateId">The dataset StateId the commit produced.</param>
/// <param name="causality">The commit's causality annotation under the dotted observed-remove regime, or <see langword="null"/> on a store that is not remove-aware or a commit that moves no default-graph content. A causality-only commit delivers a non-<see langword="null"/> annotation with empty deltas; a subscriber that tracks only the triple set ignores the parameter.</param>
public delegate void DefaultGraphDeltaObserver(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId, CommitCausality? causality);
