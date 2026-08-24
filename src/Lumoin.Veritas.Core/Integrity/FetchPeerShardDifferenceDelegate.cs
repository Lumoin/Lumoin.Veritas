using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Runs one shard's add-only reconciliation against the peer and returns its decoded difference. The transport
/// is the host's concern: an in-process fetch, or a duplex-pipe channel per shard. The delegate constructs a
/// fresh session over <paramref name="localShardItems"/> pinned as its snapshot, drives it to completion or the
/// cap, and reads the decoded items before disposing the session. The reconciliation contract every shard
/// shares is the host's to carry — it is bound into the delegate implementation alongside the transport, the
/// same way the sketch codec's contract stays host-side, so the coordinator layer never names it.
/// </summary>
/// <remarks>
/// The shard-policy handshake crosses this seam typed, both directions: the host transmits
/// <paramref name="localFingerprint"/> to the peer end, which refuses a mismatch at its own wire before
/// peeling, and the returned result carries the PEER'S OWN declared fingerprint — never an echo of the local
/// value — for the coordinator's backstop comparison. An honest transport answers every requested shard index
/// with its declaration, serving an empty difference when it holds no such shard; a transport that instead
/// throws surfaces its own exception, which the coordinator cannot convert into a named outcome.
/// </remarks>
/// <param name="shardIndex">The shard being reconciled, so the host can route to a distinct channel per concurrent shard.</param>
/// <param name="localFingerprint">The driving policy's fingerprint, which the host transmits to the peer so the mismatch refusal can happen at the peer's wire.</param>
/// <param name="localShardItems">The local operand for this shard, a stable snapshot from the partition.</param>
/// <param name="symbolCap">The symbol ceiling that bounds a non-terminating decode into an abort.</param>
/// <param name="pool">The pool the shard session rents from; never <c>.Shared</c>, always the engine's governed pool.</param>
/// <param name="cancellationToken">Cancels the shard exchange.</param>
/// <returns>The shard's decoded difference, completion status, and the peer's declared policy fingerprint.</returns>
public delegate ValueTask<ShardReconcileResult> FetchPeerShardDifferenceDelegate(
    int shardIndex,
    ShardPolicyFingerprint localFingerprint,
    IReadOnlyList<ReadOnlyMemory<byte>> localShardItems,
    int symbolCap,
    MemoryPool<byte> pool,
    CancellationToken cancellationToken);
