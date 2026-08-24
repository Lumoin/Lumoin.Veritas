using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The serving endpoint's answer to a <see cref="ShardDifferenceRequestHeader"/>: whether the exchange may
/// proceed, and the server's OWN declared shard-policy fingerprint and dictionary epoch — read off its own
/// configured policy, never echoed from the request — so the requesting side always learns the peer's true
/// declaration even on a decline.
/// </summary>
/// <param name="Accepted">Whether the serving endpoint accepts the exchange; a decline carries the declaration and nothing follows it.</param>
/// <param name="Fingerprint">The serving endpoint's own declared fingerprint.</param>
/// <param name="DictionaryEpoch">The serving endpoint's dictionary epoch.</param>
internal readonly record struct ShardDifferenceReplyHeader(bool Accepted, ShardPolicyFingerprint Fingerprint, ulong DictionaryEpoch);
