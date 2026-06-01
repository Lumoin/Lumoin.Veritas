using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The transport seam a <c>SERVICE</c> federation step uses: given a remote endpoint IRI and a self-contained
/// SPARQL query string, return the endpoint's result set. The engine never performs IO itself — it invokes this
/// delegate — so the library carries no HTTP dependency; the caller supplies the transport (an in-process
/// dispatch, an <c>HttpClient</c> POST, a cache, …), mirroring <see cref="GraphSourceResolver"/> for <c>LOAD</c>.
/// The caller-supplied opaque <paramref name="accessContext"/> (the "who is asking" of the PIC framing) is
/// forwarded so the transport can attach the outbound credential (OAuth/DPoP) the endpoint requires and gate what
/// is revealed to it; it is <see langword="null"/> when the query carries no access context.
/// </summary>
/// <param name="endpoint">The service endpoint IRI (already absolute).</param>
/// <param name="query">The self-contained SPARQL query to evaluate at the endpoint (prologue-independent; all IRIs absolute).</param>
/// <param name="accessContext">The opaque access context to authorize the call with, or <see langword="null"/>.</param>
/// <param name="cancellationToken">A token that aborts the remote call.</param>
/// <returns>The endpoint's result set.</returns>
public delegate ValueTask<SparqlResultSet> SparqlServiceTransport(IriRef endpoint, string query, AccessContext? accessContext, CancellationToken cancellationToken);
