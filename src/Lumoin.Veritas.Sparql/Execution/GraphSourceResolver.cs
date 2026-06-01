using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// <see langword="null"/> when the query carries no access context. A resolver that cannot produce the document
/// throws; <c>LOAD</c> swallows the failure only when the operation is <c>LOAD SILENT</c>.
/// </summary>
/// <param name="source">The source document IRI (a <c>LOAD</c> source or a dataset-clause graph).</param>
/// <param name="accessContext">The opaque access context to authorize the fetch with, or <see langword="null"/>.</param>
/// <param name="cancellationToken">A token that aborts resolution.</param>
/// <returns>The document's triples (its default graph).</returns>
public delegate ValueTask<IReadOnlyList<DataTriple>> GraphSourceResolver(IriRef source, AccessContext? accessContext, CancellationToken cancellationToken);
