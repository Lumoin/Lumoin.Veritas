using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The composed result of a sharded multi-block peer reconciliation.
/// </summary>
/// <param name="Outcome">How the attempt concluded.</param>
/// <param name="RecoveredItems">The peer-only items to re-ingest under the lost blocks' geometry, empty unless the outcome is <see cref="ShardedRepairOutcome.Recovered"/>.</param>
/// <param name="ShardsProcessed">How many shards were driven before the attempt concluded.</param>
/// <param name="FailedShardIndex">The shard that failed, or minus one on success.</param>
public sealed record ShardedPeerRepairResult(ShardedRepairOutcome Outcome, IReadOnlyList<ReadOnlyMemory<byte>> RecoveredItems, int ShardsProcessed, int FailedShardIndex);
