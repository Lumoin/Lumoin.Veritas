using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Governs an outbound graph resolve for a SPARQL <c>FROM</c> / <c>FROM NAMED</c> dataset clause or an
/// <c>UPDATE LOAD</c>: a closure-free decorator that runs the network-governance gate before an inner
/// <see cref="GraphSourceResolver"/> and, on a permit, resolves; on a deny, throws
/// <see cref="NetworkGovernanceDeniedException"/>, which <c>LOAD SILENT</c> swallows while a non-silent resolve
/// propagates it — a denial behaves exactly like an unreachable source. Its <see cref="Resolver"/> is itself a
/// <see cref="GraphSourceResolver"/>, so it composes in front of any resolver.
/// </summary>
/// <remarks>
/// The source is a per-call argument, so the peer key is rented per call from the source IRI's bytes and disposed
/// when the call returns; the <c>using</c> scope spans the whole governed call, so the owned bytes stay valid
/// across the asynchronous decision. As an explicit binding frame it captures nothing, so it holds no lexical
/// closure.
/// </remarks>
public sealed class GovernedGraphSourceResolver
{
    private readonly GraphSourceResolver inner;
    private readonly NetworkGovernanceDelegate governance;
    private readonly MemoryPool<byte> pool;
    private readonly TimeProvider timeProvider;
    private readonly TraceHandler<NetworkGovernanceTraceEvent>? trace;
    private readonly Guid correlationId;

    //A naked field: the trace sequence is advanced with Interlocked, which needs a by-ref target.
    private long sequence;

    /// <summary>Creates a governed resolver over an inner resolver.</summary>
    /// <param name="inner">The resolver this governs — invoked on a permit.</param>
    /// <param name="governance">The policy consulted before each resolve.</param>
    /// <param name="pool">The pool the per-call source key is rented from.</param>
    /// <param name="timeProvider">The clock a delayed resolve backs off against and the event is timestamped with.</param>
    /// <param name="trace">The diagnostics sink each governance verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted events carry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/>, <paramref name="governance"/>, <paramref name="pool"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public GovernedGraphSourceResolver(
        GraphSourceResolver inner,
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
        Resolver = Resolve;
    }

    /// <summary>The governed resolver — a <see cref="GraphSourceResolver"/> that governs then resolves.</summary>
    public GraphSourceResolver Resolver { get; }

    /// <summary>Governs then resolves: consults the policy for an outbound graph resolve and, on a permit, invokes the inner resolver; on a deny, throws.</summary>
    /// <param name="source">The source document IRI.</param>
    /// <param name="accessContext">The opaque access context to authorize the fetch with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token that cancels the governance decision or the resolve.</param>
    /// <returns>The document's triples on a permit.</returns>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the resolve.</exception>
    private async ValueTask<IReadOnlyList<DataTriple>> Resolve(IriRef source, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        using NetworkPeerKey peer = NetworkPeerKey.RentEndpointIri(pool, source.Value.Span);
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundGraphResolve, accessContext, peer, OperationSizeHint: 0, PartitionCoordinate: -1);
        await NetworkGovernanceGate.EnterOrThrowAsync(governance, request, timeProvider, trace, correlationId, Interlocked.Increment(ref sequence), cancellationToken).ConfigureAwait(false);

        return await inner(source, accessContext, cancellationToken).ConfigureAwait(false);
    }
}
