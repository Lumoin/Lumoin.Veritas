using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
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
                //same additions converges.
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
}
