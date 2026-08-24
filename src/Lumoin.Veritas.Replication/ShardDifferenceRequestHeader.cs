using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The header frame a shard-difference fetch opens its connection with: which shard is being reconciled, the
/// driving policy's declared fingerprint, the requesting endpoint's dictionary epoch, and the symbol cap the
/// exchange is bounded by. The serving endpoint answers with a <see cref="ShardDifferenceReplyHeader"/> before
/// any reconciliation envelope crosses, so a policy or epoch mismatch is refused at the wire.
/// </summary>
/// <param name="ShardIndex">The shard being reconciled.</param>
/// <param name="Fingerprint">The driving policy's declared fingerprint.</param>
/// <param name="DictionaryEpoch">The requesting endpoint's dictionary epoch; Core's <see langword="long"/> epoch crosses the wire as this <see langword="ulong"/> by raw bit reinterpretation, the manifest's own convention.</param>
/// <param name="SymbolCap">The symbol ceiling the requesting side bounds the exchange by.</param>
internal readonly record struct ShardDifferenceRequestHeader(int ShardIndex, ShardPolicyFingerprint Fingerprint, ulong DictionaryEpoch, int SymbolCap);
