using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Fetches the triples a peer holds for a set of content-hash reconciliation items — the recovery step the
/// non-invertible content-hash domain needs. After the rateless peel names the peer-only items (the ones the local
/// replica lacks), the reconcile asks the peer to resolve each to its triple — the peer holds them, resolving
/// through its own side-map and decoding through its own dictionary — and return them as terms, which the local
/// replica re-encodes into its dictionary. The transport (or an in-process peer) supplies this; the engine never
/// performs IO itself, mirroring <see cref="AsyncSketchFetchDelegate"/>.
/// </summary>
/// <param name="items">The peer-only content-hash items to fetch the triples for.</param>
/// <param name="cancellationToken">A token that aborts the fetch.</param>
/// <returns>The triples the peer holds for the items, as dictionary-independent terms.</returns>
public delegate ValueTask<IReadOnlyList<ContentTriple>> AsyncContentTripleFetchDelegate(IReadOnlyList<ContentKey128> items, CancellationToken cancellationToken);
