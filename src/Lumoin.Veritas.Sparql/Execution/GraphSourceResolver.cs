using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Resolves a graph source IRI to the triples of the document it denotes — the injectable seam behind SPARQL Update
/// <c>LOAD</c> and an in-query <c>FROM</c> / <c>FROM NAMED</c> dataset graph. The engine never fetches anything
/// itself, so the host chooses the policy — a conformance harness maps the IRI to a manifest-local file, a
/// production deployment plugs an HTTP(S) fetcher (or a content-addressed / access-controlled store) into the same
/// delegate. The caller-supplied opaque <paramref name="accessContext"/> (the "who is asking" of the PIC framing)
/// is forwarded so the resolver can authorize/attach a credential when fetching across a trust boundary; it is
/// <see langword="null"/> when the query carries no access context.
/// </summary>
/// <remarks>
/// The document is delivered as an asynchronous stream: the resolver yields the source's triples as it parses them,
/// so a consumer encodes each triple as it arrives and the term-bearing document is never materialised in full. A
/// resolution or parse failure surfaces as an exception thrown from enumeration — possibly MID-stream, after some
/// triples have already been yielded. <c>LOAD</c> encodes during enumeration and applies nothing to the dataset
/// until the stream completes, so a mid-stream failure leaves the target unchanged (LOAD stays atomic): a
/// <c>LOAD SILENT</c> swallows the failure having applied nothing, while a non-silent <c>LOAD</c> propagates it.
/// Encoding mints terms into the shared dictionary as each triple arrives, so a failed load leaves the terms
/// yielded before the failure minted though no triple was applied — the same eager-minting behavior every update
/// operation exhibits (a multi-operation update that fails late likewise keeps the earlier operations' terms):
/// term ids name interned strings, never assertions, so an unreferenced id is inert in every query and closure.
/// </remarks>
/// <param name="source">The source document IRI (a <c>LOAD</c> source or a dataset-clause graph).</param>
/// <param name="accessContext">The opaque access context to authorize the fetch with, or <see langword="null"/>.</param>
/// <param name="cancellationToken">A token that aborts resolution.</param>
/// <returns>The document's triples (its default graph), streamed as they are parsed.</returns>
public delegate IAsyncEnumerable<DataTriple> GraphSourceResolver(IriRef source, AccessContext? accessContext, CancellationToken cancellationToken);
