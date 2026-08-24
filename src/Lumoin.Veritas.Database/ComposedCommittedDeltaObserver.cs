using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The one committed-delta observer of a REMOVE-AWARE mutable database: fans each committed default-graph
/// delta to the incremental sketch maintainer and then to the dotted commit ledger, in that order, inside the
/// dataset's publish critical section — so the reconciliation feed, the maintained sketch encoder, and the
/// ledger all advance by the same delta under the same atomicity argument as a single observer. An add-only
/// database wires the sketch maintainer directly and this type is not constructed.
/// </summary>
internal sealed class ComposedCommittedDeltaObserver
{
    /// <summary>The sketch maintainer the delta reaches first — it advances the reconciliation feed the sketch and shard lanes serve from.</summary>
    private IncrementalSketchMaintainer SketchMaintainer { get; }

    /// <summary>The dotted commit ledger the delta's causality annotation folds into.</summary>
    private DottedCommitLedger Ledger { get; }

    /// <summary>Creates the composed observer over the two subscribers.</summary>
    /// <param name="sketchMaintainer">The sketch maintainer.</param>
    /// <param name="ledger">The dotted commit ledger.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ComposedCommittedDeltaObserver(IncrementalSketchMaintainer sketchMaintainer, DottedCommitLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(sketchMaintainer);
        ArgumentNullException.ThrowIfNull(ledger);

        SketchMaintainer = sketchMaintainer;
        Ledger = ledger;
    }

    /// <summary>The observer the dataset fires per committed default-graph delta; binds as a method group, without a closure.</summary>
    /// <param name="additions">The triples the commit added to the default graph.</param>
    /// <param name="removals">The triples the commit removed from the default graph.</param>
    /// <param name="stateId">The dataset StateId the commit produced.</param>
    /// <param name="causality">The commit's causality annotation; <see langword="null"/> only when the commit moved no default-graph content.</param>
    public void OnDefaultGraphDelta(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId, CommitCausality? causality)
    {
        SketchMaintainer.OnDefaultGraphDelta(additions, removals, stateId, causality);
        Ledger.OnDefaultGraphDelta(additions, removals, stateId, causality);
    }
}
