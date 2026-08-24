using System.Threading;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Draws one scrub-cadence jitter sample in the range [0, 1), positioning a round's delay
/// within its jitter band.
/// </summary>
/// <returns>A sample in [0, 1).</returns>
public delegate double JitterSampleDelegate();

/// <summary>
/// The policy a <see cref="StorageSelfHealService"/> runs its background scrub-and-heal loop under: how often a
/// round is initiated (the reliability-driven cadence and its de-correlating jitter), an optional per-round
/// observation sink, and the in-process commit-serialization mutex the owner shares with a foreground persist.
/// </summary>
/// <remarks>
/// <para>
/// The cadence comes from the injected Poisson reliability model (<see cref="CadenceEstimator"/>) evaluated over
/// the deployment facts in <see cref="CadenceContext"/>; the service overrides the context's protected-item count
/// per round from the live generation's own block count, so the interval scales with the data actually protected.
/// The jitter (<see cref="JitterFraction"/>, <see cref="JitterSample"/>) spreads a fleet's rounds out so replicas
/// do not scrub in lock-step.
/// </para>
/// <para>
/// The commit-serialization mutex (<see cref="CommitMutex"/>) is a single-process seam: it serializes the
/// service's heal publish against a foreground persist into the same store, so the two never interleave their
/// staging and rename windows in one process. Cross-process concurrency is out of scope for this seam — a shared
/// store written by two processes coordinates through its own atomic-publish contract, not this in-memory lock.
/// </para>
/// </remarks>
public sealed record SelfHealOptions
{
    /// <summary>The reliability model the target initiation interval is estimated from; defaults to <see cref="ScrubCadenceEstimators.Default"/> (the Poisson model). The service overrides the context's protected-item count per round from the live generation.</summary>
    public ScrubCadenceEstimatorDelegate CadenceEstimator { get; init; } = ScrubCadenceEstimators.Default;

    /// <summary>The deployment facts the cadence model weighs — memory protection, the replica constellation, and the target undetected-corruption probability. The protected-item count is overridden per round from the live generation, so a resolve-time count here is only the seed for the first interval.</summary>
    public ScrubCadenceContext CadenceContext { get; init; } = new ScrubCadenceContext(MemoryIsProtected: false);

    /// <summary>The fraction of the estimated interval the per-round delay is randomly spread over, so a fleet's rounds de-correlate; in the range [0, 1). The delay for a round is <c>interval * (1 - JitterFraction/2 + sample * JitterFraction)</c>.</summary>
    public double JitterFraction { get; init; } = 0.1;

    /// <summary>The jitter sample source, each call returning a value in [0, 1); <see langword="null"/> draws from the shared non-cryptographic RNG. A test injects a constant so the delay is exact. The jitter only de-correlates timing, so it is not an identity or a security token.</summary>
    public JitterSampleDelegate? JitterSample { get; init; }

    /// <summary>The in-process mutex the owner shares between this service's heal-publish (commit) step and a foreground persist into the same store, so the two never interleave; <see langword="null"/> runs the commit unsynchronized (the sole writer). Single-process only — cross-process writers coordinate through the store's own single-writer atomic-publish contract. The serialization keys on the store INSTANCE: every foreground persist into the healed store must go through the same store object this service was constructed over (a second store instance over the same directory is a second writer, outside the single-writer contract, and this mutex cannot see it).</summary>
    public Lock? CommitMutex { get; init; }

    /// <summary>An optional sink invoked once per round with the verify and repair verdicts, before the heal is published, so a host or test can observe each round; <see langword="null"/> observes nothing. Mirrors the scrub turn's round-result sink shape.</summary>
    public ScrubRoundResultDelegate? OnRoundComplete { get; init; }

    /// <summary>The seam that supplies the single-block peer-reconciliation restoring source per round, invoked inside the repair pass with the damaged generation's recovered facts and only when the system-of-record is damaged; <see langword="null"/> runs every round local-only. A provider fault leaves the rung unsourced, named on the trace — it never aborts the round.</summary>
    public ProvidePeerReconciliationSourceDelegate? ProvidePeerSource { get; init; }

    /// <summary>The seam that supplies the sharded multi-block peer-reconciliation restoring source per round, under the same invocation, null-answer, and fault contract as <see cref="ProvidePeerSource"/>. The composition root that binds this seam is inside the repair trust base: a transport that echoes the local shard-policy fingerprint as the peer's declaration defeats the policy-mismatch refusal — the same trust class as one that corrupts the difference stream itself.</summary>
    public ProvideShardedPeerReconciliationSourceDelegate? ProvideShardedPeerSource { get; init; }
}
