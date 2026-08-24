using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The result of materializing an RL closure into a store: the post-commit
/// store, the number of derived triples, and the consistency verdict.
/// </summary>
/// <param name="Store">The store over the post-commit snapshot — the input store when nothing was derived.</param>
/// <param name="DerivedCount">The number of triples the rules derived and committed.</param>
/// <param name="IsConsistent">Whether the closure completed without deriving a contradiction.</param>
/// <param name="InconsistencyRule">The falsity rule that fired, or <c>null</c> when consistent.</param>
/// <param name="InconsistencyPremises">The triples the falsity rule matched; empty when consistent.</param>
/// <param name="MalformedShapes">The ill-formed encodings the closure declined to read; empty on well-formed input.</param>
public readonly record struct OwlRlMaterializationResult(
    HypertrieGraphStore Store,
    int DerivedCount,
    bool IsConsistent,
    string? InconsistencyRule,
    ImmutableArray<EncodedTriple> InconsistencyPremises,
    ImmutableArray<MalformedShape> MalformedShapes);

/// <summary>
/// Materializes the OWL 2 RL closure of a store's triples and commits the
/// derived triples through an <see cref="EditSession"/>, so the journal
/// entry's additions are exactly the inferred knowledge of the run — the
/// same provenance contract as the RDFS
/// <see cref="RdfsMaterialization.MaterializeAndCommitAsync"/>.
/// </summary>
public static class OwlRlMaterialization
{
    /// <summary>
    /// Computes the RL closure of <paramref name="store"/>'s triples and
    /// commits the derivations. A run that derives nothing — or that
    /// derives a contradiction, leaving nothing trustworthy to commit —
    /// returns the store unchanged with no commit.
    /// </summary>
    /// <param name="store">The store to materialize over; its snapshot is the base the session branches from.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the <c>dt-*</c> falsities; <see cref="OwlRlDatatypeOracle.None"/> disables them.</param>
    /// <param name="traceHandler">Optional handler receiving one <see cref="InferenceTraceEvent"/> per derivation, premises included.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Required when <paramref name="traceHandler"/> is non-<c>null</c>; ignored otherwise.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts derivation and the commit.</param>
    /// <returns>The post-commit store, the derivation count, and the consistency verdict with the falsity's premises.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="EditSessionConcurrencyException">Another session committed against the store's journal first.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<OwlRlMaterializationResult> MaterializeAndCommitAsync(
        HypertrieGraphStore store,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle = default,
        TraceHandler<InferenceTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(terms);

        OwlRlResult closure = OwlRlClosure.Compute(
            store.Match(TermId.None, TermId.None, TermId.None),
            terms,
            datatypeOracle,
            traceHandler,
            timeProvider,
            correlationId,
            cancellationToken: cancellationToken);

        if(!closure.IsConsistent || closure.Derived.Count == 0)
        {
            return new OwlRlMaterializationResult(store, DerivedCount: 0, closure.IsConsistent, closure.InconsistencyRule, closure.InconsistencyPremises, closure.MalformedShapes);
        }

        EditSession session = await store.Snapshot.Store.OpenEditSessionAsync(store.Snapshot, cancellationToken).ConfigureAwait(false);

        await using(session.ConfigureAwait(false))
        {
            session.AddRange(closure.Derived);
            HypertrieSnapshot committed = await session.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new OwlRlMaterializationResult(
                HypertrieGraphStore.FromSnapshot(committed),
                closure.Derived.Count,
                IsConsistent: true,
                InconsistencyRule: null,
                InconsistencyPremises: [],
                closure.MalformedShapes);
        }
    }
}
