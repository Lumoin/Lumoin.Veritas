using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Resolves a SPARQL Update <c>LOAD</c> source IRI to the triples of the document it denotes. The injectable seam
/// behind <c>LOAD</c>: the executor never fetches anything itself, so the host chooses the policy — a conformance
/// harness maps the IRI to a manifest-local file, a production deployment plugs an HTTP(S) fetcher (or a
/// content-addressed / access-controlled store) into the same delegate. A resolver that cannot produce the document
/// throws; the executor swallows the failure only when the operation is <c>LOAD SILENT</c>.
/// </summary>
/// <param name="source">The source document IRI from the <c>LOAD</c> operation.</param>
/// <param name="cancellationToken">A token that aborts resolution.</param>
/// <returns>The document's triples (its default graph).</returns>
public delegate ValueTask<IReadOnlyList<DataTriple>> GraphSourceResolver(IriRef source, CancellationToken cancellationToken);
