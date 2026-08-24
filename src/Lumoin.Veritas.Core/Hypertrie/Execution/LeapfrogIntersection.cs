using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The leapfrog intersection algorithm — the inner loop of
/// worst-case-optimal joins. Given a set of
/// <see cref="TriejoinIterator"/> instances all positioned at
/// the same variable level, advances them in lock-step until
/// every iterator's <see cref="TriejoinIterator.Key"/> agrees
/// on a common value, or until one iterator reaches its end
/// (in which case no further common key exists at this level).
/// </summary>
/// <remarks>
/// <para>
/// <b>The track-max variant.</b> This implementation maintains
/// a running <c>target</c> equal to the maximum
/// <see cref="TriejoinIterator.Key"/> seen across the
/// participants. On each pass it walks the participants once;
/// any participant lagging the target is advanced via
/// <see cref="TriejoinIterator.Seek"/>. If the seek's new key
/// exceeds the target, the target updates and the pass
/// restarts. A pass with no advancement means every participant
/// agrees — that key is the next common key.
/// </para>
/// <para>
/// The variant is preferred over a sort-based leapfrog because
/// it allocates nothing, handles any number of participants
/// uniformly, and the inner work is exactly the
/// <see cref="TriejoinIterator.Seek"/> calls plus pointer
/// comparisons — leveraging the binary-search-into-sorted-array
/// shape of the underlying
/// <see cref="EdgeMapKeyCursor"/>.
/// </para>
/// <para>
/// <b>Pre-conditions.</b> Every participant must be at the
/// same variable level. The driver is responsible for ensuring
/// this — typically by opening every iterator that participates
/// in the current planner-chosen variable, leaving non-participants
/// untouched. The algorithm does not validate the pre-condition;
/// violating it produces meaningless results.
/// </para>
/// <para>
/// <b>Effects on iterators.</b> The algorithm mutates the
/// participants' cursors via
/// <see cref="TriejoinIterator.Seek"/>. On a successful return
/// every participant's <see cref="TriejoinIterator.Key"/> equals
/// <c>commonKey</c>. On a false return at least one participant
/// is at end; the others may have advanced past their starting
/// positions. The driver normally reacts to a false return by
/// rewinding (<see cref="TriejoinIterator.Up"/>) and abandoning
/// the current branch.
/// </para>
/// <para>
/// <b>Single participant.</b> With one participant the algorithm
/// trivially returns the iterator's current key (or <c>false</c>
/// if it is already at end).
/// </para>
/// </remarks>
public static class LeapfrogIntersection
{
    /// <summary>
    /// Advances <paramref name="participants"/> until they all
    /// agree on a common key, or until one reaches end. Returns
    /// <c>true</c> with <paramref name="commonKey"/> set to the
    /// agreed-upon value on success; returns <c>false</c> with
    /// <paramref name="commonKey"/> set to <c>0</c> when no
    /// further common key exists.
    /// </summary>
    /// <param name="participants">Iterators participating in the current variable level. Must not be <c>null</c>; must be non-empty.</param>
    /// <param name="commonKey">The agreed-upon key on success; <c>0</c> on failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a common key was found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="participants"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="participants"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static bool TryFindNextCommonKey(
        IReadOnlyList<TriejoinIterator> participants,
        out TermId commonKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);

        if(participants.Count == 0)
        {
            throw new ArgumentException("Leapfrog intersection requires at least one participant.", nameof(participants));
        }

        cancellationToken.ThrowIfCancellationRequested();

        //Initial scan: if any participant is at end, no common key
        //exists; otherwise compute the starting target as the max
        //of the participants' current keys.
        TermId target = TermId.None;

        for(int i = 0; i < participants.Count; i++)
        {
            TriejoinIterator iterator = participants[i];

            if(iterator.AtEnd)
            {
                commonKey = TermId.None;

                return false;
            }

            TermId key = iterator.Key;

            if(key > target)
            {
                target = key;
            }
        }

        //Track-max loop: walk participants until a full pass
        //completes with no advancement. Any advancement past the
        //target updates the target and restarts the pass; for
        //correctness the restart starts from index 0 because
        //earlier participants now lag the new target.
        bool advanced;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            advanced = false;

            for(int i = 0; i < participants.Count; i++)
            {
                TriejoinIterator iterator = participants[i];

                if(iterator.Key == target)
                {
                    continue;
                }

                //iterator.Key < target by construction (target is
                //always the running max). Advance via Seek.
                iterator.Seek(target, cancellationToken);

                if(iterator.AtEnd)
                {
                    commonKey = TermId.None;

                    return false;
                }

                TermId newKey = iterator.Key;

                if(newKey > target)
                {
                    //Overshoot: this iterator's first key at or
                    //above the previous target is strictly greater.
                    //Promote it to the new target and restart the
                    //pass — earlier participants must catch up.
                    target = newKey;
                    advanced = true;

                    break;
                }

                //newKey == target — this participant has caught up;
                //continue scanning the rest of the pass.
            }
        }
        while(advanced);

        commonKey = target;

        return true;
    }
}
