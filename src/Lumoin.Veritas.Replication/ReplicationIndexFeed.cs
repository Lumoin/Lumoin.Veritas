using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A replica's reconciliation view: the <see cref="ColumnarTripleIndex"/> of the committed default-graph triples,
/// paired with the dataset StateId it reflects, kept in step with the query store by the SAME committed delta. It
/// lives BESIDE the query store rather than reusing the lazy, policy-gated query rendezvous view, so the sketch a
/// peer fetches always reflects the exact committed triple set. Seeded once at construction, then advanced by each
/// commit's effective (additions, removals) — the same delta the query rendezvous receives — so the reconciliation
/// index and the query store never diverge. <see cref="Current"/> reads the (index, StateId) generation atomically;
/// because the columnar index is immutable, a generation handed to one peer connection is unaffected by later
/// commits.
/// </summary>
public sealed class ReplicationIndexFeed
{
    //The Lock is a synchronization primitive, not mutable data state; a readonly field is the idiomatic form for
    //the C# lock statement over System.Threading.Lock.
    private readonly Lock gate = new();

    /// <summary>The reconciliation index of the committed default-graph triples, replaced on each <see cref="Advance"/>.</summary>
    private ColumnarTripleIndex Index { get; set; }

    /// <summary>The dataset StateId the current <see cref="Index"/> reflects.</summary>
    private NodeIdentifier StateId { get; set; }

    /// <summary>Creates a feed seeded with a replica's committed triples at a starting generation.</summary>
    /// <param name="seed">The committed default-graph triples to seed the reconciliation index from.</param>
    /// <param name="stateId">The dataset StateId the seed reflects.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seed"/> is <see langword="null"/>.</exception>
    public ReplicationIndexFeed(IEnumerable<EncodedTriple> seed, NodeIdentifier stateId)
    {
        ArgumentNullException.ThrowIfNull(seed);

        Index = ColumnarTripleIndex.Build(seed);
        StateId = stateId;
    }

    /// <summary>Advances the reconciliation index by a commit's effective delta and records the new generation — the second observer of the same delta the query store's rendezvous receives, so the two stay in step.</summary>
    /// <param name="additions">The triples the commit added.</param>
    /// <param name="removals">The triples the commit removed.</param>
    /// <param name="newStateId">The dataset StateId after the commit.</param>
    /// <exception cref="ArgumentNullException"><paramref name="additions"/> or <paramref name="removals"/> is <see langword="null"/>.</exception>
    public void Advance(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier newStateId)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        lock(gate)
        {
            Index = Index.Apply(additions, removals);
            StateId = newStateId;
        }
    }

    /// <summary>Reads the current reconciliation index and the generation it reflects atomically.</summary>
    /// <returns>The current index and StateId — the snapshot a sketch is served over and tagged with.</returns>
    public ReplicationGeneration Current()
    {
        lock(gate)
        {
            return new ReplicationGeneration(Index, StateId);
        }
    }
}
