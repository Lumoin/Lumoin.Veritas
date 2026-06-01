using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The client a <see cref="SparqlQueryEngine"/> uses to evaluate a <c>SERVICE</c> sub-query at a remote endpoint.
/// It is a thin abstraction over a <see cref="SparqlServiceTransport"/> delegate: the engine depends on this type,
/// while the actual transport (in-process dispatch, an <c>HttpClient</c> POST, …) is injected, keeping the library
/// free of any HTTP dependency.
/// </summary>
public sealed class SparqlClient
{
    private readonly SparqlServiceTransport transport;

    /// <summary>Initialises a client over the given transport.</summary>
    /// <param name="transport">The transport that executes a query at an endpoint.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    public SparqlClient(SparqlServiceTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        this.transport = transport;
    }

    /// <summary>Evaluates a self-contained query at an endpoint and returns its result set.</summary>
    /// <param name="endpoint">The endpoint IRI.</param>
    /// <param name="query">The self-contained SPARQL query.</param>
    /// <param name="accessContext">The opaque access context to authorize the call with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts the remote call.</param>
    /// <returns>The endpoint's result set.</returns>
    public ValueTask<SparqlResultSet> QueryAsync(IriRef endpoint, string query, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        return transport(endpoint, query, accessContext, cancellationToken);
    }
}
