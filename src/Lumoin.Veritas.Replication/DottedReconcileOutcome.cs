namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of one dotted (remove-aware) reconcile exchange against a peer: how it ended, the peer's named
/// decline reason when it declined, and what the exchange moved — the operator-facing carrier of the dotted
/// lane, the remove-aware sibling of the add-only reconcile outcome. The counts describe COMMITTED effect on
/// the local store (adopted additions and drops) and transfer effect toward the peer (pushed entries, and the
/// tombstone dots answered as push-drops); on an interrupted exchange they describe the consistent committed
/// prefix, which re-running the session extends idempotently.
/// </summary>
/// <param name="Kind">How the exchange ended.</param>
/// <param name="PeerDeclineReason">The peer's named decline reason; <see cref="DottedDifferenceDeclineReason.None"/> unless <paramref name="Kind"/> is <see cref="DottedReconcileOutcomeKind.PeerDeclined"/>.</param>
/// <param name="AdoptedAdditions">The peer entries adopted into the local store as genuine additions.</param>
/// <param name="AdoptedDrops">The peer-observed removals applied to the local store as drops.</param>
/// <param name="PushedEntries">The local entries pushed to the peer as its genuine additions.</param>
/// <param name="PushedDropDots">The local tombstone dots answered back to the peer as push-drops, so the peer drops rather than re-adds them.</param>
public readonly record struct DottedReconcileOutcome(
    DottedReconcileOutcomeKind Kind,
    DottedDifferenceDeclineReason PeerDeclineReason,
    int AdoptedAdditions,
    int AdoptedDrops,
    int PushedEntries,
    int PushedDropDots)
{
    /// <summary>Creates an outcome carrying only its kind — the refusal shapes that move nothing.</summary>
    /// <param name="kind">How the exchange ended.</param>
    /// <returns>The outcome with zero counts and the absent decline reason.</returns>
    public static DottedReconcileOutcome ForKind(DottedReconcileOutcomeKind kind)
    {
        return new DottedReconcileOutcome(kind, DottedDifferenceDeclineReason.None, 0, 0, 0, 0);
    }
}
