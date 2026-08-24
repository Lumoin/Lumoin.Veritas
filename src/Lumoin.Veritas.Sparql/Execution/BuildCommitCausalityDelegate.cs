using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Builds a locally-authored commit's causality annotation from its net default-graph delta, against the live
/// dotted commit ledger — the seam a remove-aware store wires so every local commit mints its dots and names
/// its drops without the dataset taking a dependency on the replication layer. Called by
/// <see cref="DatasetEditSession"/> between computing the commit's transitions and the linearising journal
/// append; the append's head compare-and-swap certifies the annotation's basis, and a commit that loses the
/// race rebuilds it against the new head. Returns <see langword="null"/> when the commit moves no default-graph
/// content. The signature matches the ledger's builder so it binds as a method group, without a closure.
/// </summary>
/// <param name="additions">The commit's net default-graph additions.</param>
/// <param name="removals">The commit's net default-graph removals.</param>
/// <returns>The annotation, or <see langword="null"/> when the commit moves no default-graph content.</returns>
public delegate CommitCausality? BuildCommitCausalityDelegate(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals);
