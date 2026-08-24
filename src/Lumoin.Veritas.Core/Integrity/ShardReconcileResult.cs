using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The decoded symmetric difference a single shard's add-only reconciliation session recovered, before the
/// coordinator resolves direction. The difference set is what the peel yields; which side holds each item is
/// resolved against the local shard membership, because the coded symbols deliberately omit a count field.
/// </summary>
/// <param name="ShardIndex">The shard this result belongs to.</param>
/// <param name="PeerFingerprint">The peer's OWN declared shard-policy fingerprint, carried back by the host transport — never an echo of the local value — or <see langword="null"/> when the peer NEVER DECLARED (the transport faulted before or during the header exchange, so no declaration exists to compare). The rung refuses a null declaration as <see cref="ShardedRepairOutcome.PeerUndeclared"/> ahead of the fingerprint comparison, and compares a present declaration against the driving policy before consuming anything else in this result.</param>
/// <param name="DifferenceItems">The symmetric-difference item keys the shard's decoder recovered.</param>
/// <param name="Completed">Whether the shard's decoder reached completion within its symbol cap.</param>
/// <param name="AbsorbedSymbolCount">How many difference symbols the shard absorbed, for the measurement ledger.</param>
public sealed record ShardReconcileResult(int ShardIndex, ShardPolicyFingerprint? PeerFingerprint, IReadOnlyList<ReadOnlyMemory<byte>> DifferenceItems, bool Completed, int AbsorbedSymbolCount);
