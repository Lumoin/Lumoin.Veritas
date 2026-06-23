using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Governs an outbound SPARQL <c>SERVICE</c> query: a closure-free decorator that runs the network-governance gate
/// before an inner <see cref="SparqlServiceTransport"/> and, on a permit, queries; on a deny, throws
/// <see cref="NetworkGovernanceDeniedException"/>, which the engine's <c>SERVICE SILENT</c> handling swallows to the
/// join identity while a non-silent <c>SERVICE</c> propagates it — a denial behaves exactly like an unreachable
/// endpoint. Its <see cref="Transport"/> is itself a <see cref="SparqlServiceTransport"/>, so it composes in front
/// of any transport (the in-process dispatch or an HTTP client) and is handed straight to a <see cref="SparqlClient"/>.
/// </summary>
/// <remarks>
/// The endpoint is a per-call argument, so the peer key is rented per call from the endpoint IRI's bytes and
/// disposed when the call returns; the <c>using</c> scope spans the whole governed call, so the owned bytes stay
/// valid across the asynchronous decision. As an explicit binding frame it captures nothing, so it holds no lexical
/// closure.
/// </remarks>
public sealed class GovernedSparqlServiceTransport
{
    private readonly SparqlServiceTransport inner;
    private readonly NetworkGovernanceDelegate governance;
    private readonly MemoryPool<byte> pool;
    private readonly TimeProvider timeProvider;
    private readonly TraceHandler<NetworkGovernanceTraceEvent>? trace;
    private readonly Guid correlationId;

    //A naked field: the trace sequence is advanced with Interlocked, which needs a by-ref target.
    private long sequence;

    /// <summary>Creates a governed transport over an inner transport.</summary>
    /// <param name="inner">The transport this governs — invoked on a permit.</param>
    /// <param name="governance">The policy consulted before each query.</param>
    /// <param name="pool">The pool the per-call endpoint key is rented from.</param>
    /// <param name="timeProvider">The clock a delayed query backs off against and the event is timestamped with.</param>
    /// <param name="trace">The diagnostics sink each governance verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted events carry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/>, <paramref name="governance"/>, <paramref name="pool"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public GovernedSparqlServiceTransport(
        SparqlServiceTransport inner,
        NetworkGovernanceDelegate governance,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace = null,
        Guid correlationId = default)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.inner = inner;
        this.governance = governance;
        this.pool = pool;
        this.timeProvider = timeProvider;
        this.trace = trace;
        this.correlationId = correlationId;
        Transport = Govern;
    }

    /// <summary>The governed transport — a <see cref="SparqlServiceTransport"/> that governs then queries. Hand it to a <see cref="SparqlClient"/>.</summary>
    public SparqlServiceTransport Transport { get; }

    /// <summary>Governs then queries: consults the policy for an outbound SERVICE query and, on a permit, invokes the inner transport; on a deny, throws.</summary>
    /// <param name="endpoint">The service endpoint IRI.</param>
    /// <param name="query">The self-contained SPARQL query.</param>
    /// <param name="accessContext">The opaque access context to authorize the call with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token that cancels the governance decision or the query.</param>
    /// <returns>The endpoint's result set on a permit.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the query.</exception>
    private async ValueTask<SparqlResultSet> Govern(IriRef endpoint, string query, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        using NetworkPeerKey peer = NetworkPeerKey.RentEndpointIri(pool, endpoint.Value.Span);
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundServiceQuery, accessContext, peer, query.Length, PartitionCoordinate: -1);
        await NetworkGovernanceGate.EnterOrThrowAsync(governance, request, timeProvider, trace, correlationId, Interlocked.Increment(ref sequence), cancellationToken).ConfigureAwait(false);

        return await inner(endpoint, query, accessContext, cancellationToken).ConfigureAwait(false);
    }
}
