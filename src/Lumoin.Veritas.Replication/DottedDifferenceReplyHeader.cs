using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The serving endpoint's answer to a <see cref="DottedDifferenceRequestHeader"/>: whether the exchange may
/// proceed, the server's OWN dictionary epoch and offer-shaped contract declaration — read off its own
/// configuration, never echoed from the request — and, on a decline, the named reason. An accepted reply
/// carries <see cref="DottedDifferenceDeclineReason.None"/>; a decline always carries a real (non-default)
/// reason, so absence and refusal cannot be confused, and an unrecognized future reason code arrives as the
/// typed unknown carrier rather than a parse fault.
/// </summary>
/// <param name="Accepted">Whether the serving endpoint accepts the exchange; a decline carries the declaration and the reason, and nothing follows it.</param>
/// <param name="DictionaryEpoch">The serving endpoint's dictionary epoch.</param>
/// <param name="Declaration">The offer-shaped declaration of the serving endpoint's own dotted contract.</param>
/// <param name="DeclineReason">The named decline reason; <see cref="DottedDifferenceDeclineReason.None"/> exactly when <paramref name="Accepted"/>.</param>
internal sealed record DottedDifferenceReplyHeader(bool Accepted, ulong DictionaryEpoch, ReconciliationOffer Declaration, DottedDifferenceDeclineReason DeclineReason);
