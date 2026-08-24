using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// Applies a converged reconcile's recovered delta back into a mutable dataset as an ordinary journalled mutation:
/// it opens a dataset edit session, applies the recovered triples as default-graph additions, and commits, so the
/// reconciled delta flows through the SAME journal, query store, and replication feed as a local update — repair as
/// ingest. The apply is value-based: an empty delta is a no-op, and a concurrent committer that keeps advancing the
/// journal head past the retry budget yields a conflict-exhausted outcome (a later reconcile round re-detects and
/// retries) rather than a throw.
/// </summary>
public static class ReconcileWriteBack
{
    /// <summary>The default number of times the write-back is re-attempted against an advancing journal head before reporting a conflict-exhausted outcome.</summary>
    private const int DefaultMaxAttempts = 16;

    /// <summary>Applies a recovered reconcile delta to a mutable dataset through a journalled edit-session commit.</summary>
    /// <param name="dataset">The mutable dataset to apply the delta into.</param>
    /// <param name="recoveredAdditions">The triples a converged reconcile recovered (an <c>AntiEntropySessionResult.RecoveredAdditions</c>); empty applies nothing.</param>
    /// <param name="maxAttempts">The number of commit attempts against a concurrently-advancing head before reporting conflict-exhausted; positive.</param>
    /// <param name="cancellationToken">A token that aborts the write-back.</param>
    /// <returns>The value-based outcome: committed, a no-op (empty delta), or conflict-exhausted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dataset"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is not positive.</exception>
    public static async ValueTask<WriteBackOutcome> ApplyAsync(MutableSparqlDataset dataset, ReadOnlyMemory<EncodedTriple> recoveredAdditions, int maxAttempts = DefaultMaxAttempts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        if(recoveredAdditions.IsEmpty)
        {
            return WriteBackOutcome.NoOp;
        }

        //Adapt the recovered delta to the edit session's collection contract WITHOUT copying it: the recovered
        //additions are array-backed, so the backing segment is passed straight through; only the unusual
        //non-array-backed memory falls back to a copy.
        IReadOnlyCollection<EncodedTriple> additions = MemoryMarshal.TryGetArray(recoveredAdditions, out ArraySegment<EncodedTriple> segment)
            ? segment
            : recoveredAdditions.ToArray();

        for(int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            //Open inside the try: opening a session itself appends a Started entry under the journal head-CAS, so a
            //concurrent committer can lose the race at OPEN time as well as at commit time. Both are the same
            //in-flight conflict and are retried (and ultimately exhausted), never thrown — honoring the
            //conflict-exhausted contract.
            DatasetEditSession? session = null;
            try
            {
                session = await dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
                await session.ApplyDeltaAsync(TermId.None, additions, [], cancellationToken).ConfigureAwait(false);
                await session.CommitAsync(cancellationToken).ConfigureAwait(false);

                return WriteBackOutcome.Committed;
            }
            catch(EditSessionConcurrencyException)
            {
                //A concurrent committer advanced the journal head; dispose this session and retry against the new
                //head. Apply is idempotent on the store (already-present triples are filtered), so re-applying the
                //same additions converges; on a remove-aware store the losing attempt's minted dots die with its
                //failed append and the retry mints fresh ones against the new head.
            }
            finally
            {
                if(session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        return WriteBackOutcome.ConflictExhausted;
    }

    /// <summary>
    /// Applies a remove-aware reconcile's peer knowledge — classified additions, peer-commanded drops, and the
    /// peer causal context — through one journalled, causality-annotated edit-session commit. EVERY attempt
    /// re-plans against the live ledger (<see cref="DottedCommitLedger.PrepareAdopt"/>, the commit-time
    /// adopt-guard): a peer dot the live context covers by then became a local tombstone mid-flight and is
    /// skipped, drops remove only dots still present, and the context fold is a monotone join — every branch
    /// idempotent, so losing the head race and retrying is safe by construction rather than by any pure-set
    /// idempotence argument. A plan with no work — peer knowledge already covered — is a no-op that commits
    /// nothing.
    /// </summary>
    /// <param name="dataset">The mutable dataset to apply the adopted delta into.</param>
    /// <param name="ledger">The dataset's dotted commit ledger the plan is built against.</param>
    /// <param name="peerAdditions">The peer entries classified as genuine adds, each with its peer dots.</param>
    /// <param name="peerDrops">The peer-commanded removals, each with the dots it cancels.</param>
    /// <param name="peerContext">The peer causal context the session exchanged; folded whole.</param>
    /// <param name="maxAttempts">The number of commit attempts against a concurrently-advancing head before reporting conflict-exhausted; positive.</param>
    /// <param name="cancellationToken">A token that aborts the write-back.</param>
    /// <returns>The receipt: how the write-back landed, and the addition and drop assignments the committed plan actually adopted.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is not positive.</exception>
    public static async ValueTask<DottedAdoptReceipt> ApplyAdoptAsync(
        MutableSparqlDataset dataset,
        DottedCommitLedger ledger,
        IReadOnlyList<DottedTripleAssignment> peerAdditions,
        IReadOnlyList<DottedTripleAssignment> peerDrops,
        CausalContext peerContext,
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(peerAdditions);
        ArgumentNullException.ThrowIfNull(peerDrops);
        ArgumentNullException.ThrowIfNull(peerContext);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        for(int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            //The whole attempt — open, plan, apply, commit, publish — runs inside one causality commit scope:
            //the gate is what keeps the plan's ledger basis live through the publish, because a competing
            //causality-only commit leaves the journal head VALUE unchanged and the head compare-and-swap alone
            //cannot fail this attempt's append against it. The session commit inside the scope carries the
            //adopted annotation and does not re-enter the gate.
            using CausalityCommitScope scope = await dataset.EnterCausalityCommitScopeAsync(cancellationToken).ConfigureAwait(false);
            DatasetEditSession? session = null;
            try
            {
                session = await dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
                LedgerAdoptPlan plan = ledger.PrepareAdopt(peerAdditions, peerDrops, peerContext);
                if(!plan.HasWork)
                {
                    return new DottedAdoptReceipt(WriteBackOutcome.NoOp, 0, 0);
                }

                await session.ApplyDeltaAsync(TermId.None, plan.EffectiveAdditions, plan.EffectiveRemovals, cancellationToken).ConfigureAwait(false);
                await session.CommitAsync(plan.Causality, cancellationToken).ConfigureAwait(false);

                return new DottedAdoptReceipt(WriteBackOutcome.Committed, plan.Causality!.Additions.Length, plan.Causality.Drops.Length);
            }
            catch(EditSessionConcurrencyException)
            {
                //A concurrent committer advanced the journal head; dispose this session, release the scope, and
                //re-plan against the new head — the guarded plan, not the delta, is what makes the retry safe.
            }
            finally
            {
                if(session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        return new DottedAdoptReceipt(WriteBackOutcome.ConflictExhausted, 0, 0);
    }
}
