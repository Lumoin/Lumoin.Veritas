using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The leapfrog intersection algorithm over
/// <see cref="ColumnarTriejoinIterator"/> cursors — the track-max
/// variant: maintain a running target equal to the maximum current
/// key across the participants, advance any participant lagging it
/// via <see cref="ColumnarTriejoinIterator.Seek"/>, and restart the
/// pass whenever a seek overshoots. A pass with no advancement means
/// every participant agrees on the next common key.
/// </summary>
/// <remarks>
/// Pre-conditions and effects mirror the hypertrie's leapfrog: every
/// participant must be positioned at the same variable level; on a
/// <c>true</c> return every participant's key equals the common key;
/// on <c>false</c> at least one participant is exhausted and the
/// others may have advanced.
/// </remarks>
public static class ColumnarLeapfrogIntersection
{
    /// <summary>
    /// Advances <paramref name="participants"/> until they all agree
    /// on a common key, or until one reaches end.
    /// </summary>
    /// <param name="participants">Iterators participating at the current variable level; non-empty.</param>
    /// <param name="commonKey">The agreed-upon key on success; <see cref="TermId.None"/> on failure.</param>
    /// <param name="cancellationToken">Cancellation token, honoured once per pass.</param>
    /// <returns><c>true</c> when a common key was found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="participants"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="participants"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static bool TryFindNextCommonKey(
        IReadOnlyList<ColumnarTriejoinIterator> participants,
        out TermId commonKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);

        if(participants.Count == 0)
        {
            throw new ArgumentException("Leapfrog intersection requires at least one participant.", nameof(participants));
        }

        cancellationToken.ThrowIfCancellationRequested();

        TermId target = TermId.None;

        for(int i = 0; i < participants.Count; i++)
        {
            ColumnarTriejoinIterator iterator = participants[i];

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

        bool advanced;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            advanced = false;

            for(int i = 0; i < participants.Count; i++)
            {
                ColumnarTriejoinIterator iterator = participants[i];

                if(iterator.Key == target)
                {
                    continue;
                }

                iterator.Seek(target);

                if(iterator.AtEnd)
                {
                    commonKey = TermId.None;

                    return false;
                }

                TermId newKey = iterator.Key;

                if(newKey > target)
                {
                    target = newKey;
                    advanced = true;

                    break;
                }
            }
        }
        while(advanced);

        commonKey = target;

        return true;
    }
}
