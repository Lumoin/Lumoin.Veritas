using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Fetches the triples a peer holds for a set of content-hash reconciliation items — the recovery step the
/// non-invertible content-hash domain needs. After the rateless peel names the peer-only items (the ones the local
/// replica lacks), the reconcile asks the peer to resolve each to its triple — the peer holds them, resolving
/// through its own side-map and decoding through its own dictionary — and drives each returned triple through
/// <paramref name="onTriple"/>, which the local replica re-encodes into its dictionary. Each triple is BORROWED for
/// its handler call: its terms view pooled memory the reader releases as the handler returns, so the reconcile
/// copies any term it keeps. Malformed or hostile response data surfaces as <see cref="System.IO.InvalidDataException"/>
/// from the fetch, which the session declines on. The transport (or an in-process peer) supplies this; the engine
/// never performs IO itself, mirroring <see cref="AsyncSketchFetchDelegate"/>.
/// </summary>
/// <param name="items">The peer-only content-hash items to fetch the triples for.</param>
/// <param name="onTriple">The synchronous handler each returned triple is driven through; the triple is valid only for the duration of the call.</param>
/// <param name="cancellationToken">A token that aborts the fetch.</param>
/// <returns>A task that completes when every returned triple has been handled and the channel has ended.</returns>
public delegate ValueTask AsyncContentTripleFetchDelegate(IReadOnlyList<ContentKey128> items, ContentTripleHandlerDelegate onTriple, CancellationToken cancellationToken);
